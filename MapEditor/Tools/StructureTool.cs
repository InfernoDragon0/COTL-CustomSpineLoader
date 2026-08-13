using System.Collections;
using System.Collections.Generic;
using COTL_API.CustomStructures;
using Lamb.UI;
using Lamb.UI.BuildMenu;
using MMRoomGeneration;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CustomSpineLoader.MapEditor.Tools;

// Places structures into the room.
//
// The picker is the game's real build menu, extended with a "Map Assets" tab (MapAssetsTab) so
// non-buildable background props are reachable too.
//
// Placement instantiates the structure prefab directly under the room's content root rather than
// going through StructureManager.BuildStructure: dungeon locations have no cult placement grid,
// and a map builder wants a positioned prop, not a functioning cult building with a StructureBrain
// and a save entry.
public class StructureTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Structures";

    private readonly RuntimeMapEditor _editor;
    private readonly List<PlacedStructure> _placed = [];

    private StructureBrain.TYPES _pending = StructureBrain.TYPES.NONE;
    private UIBuildMenuController _menu;
    private UIBuildMenuController _hookedMenu;

    private GameObject _preview;
    private StructureBrain.TYPES _previewType = StructureBrain.TYPES.NONE;

    private class PlacedStructure
    {
        public StructureBrain.TYPES Type;
        public bool IsCustom;
        public GameObject Instance;
        public float Rotation;
        public bool FlipX;
    }

    public StructureTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _panel = panel;
        _ui = ui;

        ui.CreateLabel(panel, "Structure Tool", 20, TMPro.TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Pick a structure, then\nleft-click to place it.", 14, TMPro.TextAlignmentOptions.Center);
        ui.CreateButton(panel, "Open Picker", OpenPicker);
        ui.CreateButton(panel, "Clear Selection", () =>
        {
            _pending = StructureBrain.TYPES.NONE;
            _propPath = null;
            DestroyPreview();
            DestroyPropPreview();
            UpdatePropLabel();
            _editor.SetStatus("Selection cleared.");
        });
        ui.CreateButton(panel, "Undo Last Placement", UndoLast);

        // The build menu only knows about cult structures. Everything a room is actually
        // dressed with - grass tufts, background pieces, rocks, props from every dungeon and
        // the DLC - is an ordinary prefab in the catalog, browsable here the way enemies are.
        ui.CreateLabel(panel, "— Vanilla Props —", 14, TMPro.TextAlignmentOptions.Center);

        _propLabel = ui.CreateLabel(panel, "No prop selected", 15, TMPro.TextAlignmentOptions.Center)
            .GetComponent<TMPro.TMP_Text>();

        ui.CreateButton(panel, "Undo Last Prop", UndoLastProp);

        ui.CreateLabel(panel, "— Groups —", 14, TMPro.TextAlignmentOptions.Center);
        foreach (var group in PropGroups().Keys)
        {
            var captured = group;
            ui.CreateButton(panel, $"{captured} ({PropGroups()[captured].Count})", () => ShowPropGroup(captured));
        }
    }

    private TMPro.TMP_Text _propLabel;
    private readonly List<GameObject> _placedProps = [];

    private void UpdatePropLabel()
    {
        if (_propLabel == null) return;
        _propLabel.text = string.IsNullOrEmpty(_propPath)
            ? "No prop selected"
            : "Placing: " + System.IO.Path.GetFileNameWithoutExtension(_propPath);
    }

    private void UndoLastProp()
    {
        // Trailing nulls: a prop can be destroyed by a clear or a load between placements.
        while (_placedProps.Count > 0 && _placedProps[_placedProps.Count - 1] == null)
            _placedProps.RemoveAt(_placedProps.Count - 1);

        if (_placedProps.Count == 0)
        {
            _editor.SetStatus("No props placed yet.");
            return;
        }

        var last = _placedProps[_placedProps.Count - 1];
        _placedProps.RemoveAt(_placedProps.Count - 1);
        Object.Destroy(last);
        _editor.SetStatus("Removed the last prop.");
    }

    private RectTransform _panel;
    private MapEditorUI _ui;
    private readonly List<GameObject> _propButtons = [];

    private string _propPath;
    private GameObject _propPreview;
    private string _propPreviewPath;
    private bool _propPreviewPending;

    // Group name -> prop prefab paths.
    private static SortedDictionary<string, List<string>> _propGroups;

    private const string PropPrefix = "Assets/Prefabs/";

    // Enemies have their own tool; the rest is chrome that would only pad the list.
    private static readonly HashSet<string> ExcludedPropFolders =
        ["Enemies", "UI", "Fonts", "Audio", "Materials", "Shaders", "Player", "Followers"];

    // Every prop prefab in the catalog, grouped by folder - the same sweep the enemy tool does,
    // so DLC and other dungeons' decorations are reachable and not just the biome the editor
    // happens to be standing in. The current biome's own decoration set is listed first, since
    // those are the pieces that match this room's art.
    private static SortedDictionary<string, List<string>> PropGroups()
    {
        if (_propGroups != null) return _propGroups;

        _propGroups = [];
        AddBiomeGroups();

        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator?.Keys == null) continue;
            foreach (var keyObj in locator.Keys)
            {
                if (keyObj is not string key) continue;
                if (!key.StartsWith(PropPrefix) || !key.EndsWith(".prefab")) continue;

                var relative = key.Substring(PropPrefix.Length);
                var slash = relative.IndexOf('/');
                if (slash <= 0) continue;

                var top = relative.Substring(0, slash);
                if (ExcludedPropFolders.Contains(top)) continue;

                // Two levels deep keeps groups browsable: "Decorations / Dungeon 4" rather than
                // one bucket of several hundred.
                var rest = relative.Substring(slash + 1);
                var nextSlash = rest.IndexOf('/');
                var group = nextSlash > 0 ? top + " / " + rest.Substring(0, nextSlash) : top;

                if (!_propGroups.TryGetValue(group, out var list)) _propGroups[group] = list = [];
                if (!list.Contains(key)) list.Add(key);
            }
        }

        foreach (var list in _propGroups.Values) list.Sort(string.CompareOrdinal);

        var total = 0;
        foreach (var group in _propGroups.Values) total += group.Count;
        Plugin.Log.LogInfo($"MapEditor: prop catalog holds {total} prefab(s) in {_propGroups.Count} group(s).");
        return _propGroups;
    }

    private static void AddBiomeGroups()
    {
        var decorations = SceneRefs.Decorations;
        if (decorations == null) return;

        void Add(string group, GeneraterDecorations.ListOfDecorations list)
        {
            if (list?.DecorationAndProabilies == null) return;

            var paths = new List<string>();
            foreach (var entry in list.DecorationAndProabilies)
            {
                if (entry == null || string.IsNullOrEmpty(entry.ObjectPath)) continue;
                if (!paths.Contains(entry.ObjectPath)) paths.Add(entry.ObjectPath);
            }
            if (paths.Count > 0) _propGroups["This Biome / " + group] = paths;
        }

        void AddShapes(string group, GeneraterDecorations.ListOfPerlinSpriteShape list)
        {
            if (list?.DecorationAndProabilies == null) return;

            var paths = new List<string>();
            foreach (var entry in list.DecorationAndProabilies)
            {
                if (entry == null || string.IsNullOrEmpty(entry.ObjectPath)) continue;
                if (!paths.Contains(entry.ObjectPath)) paths.Add(entry.ObjectPath);
            }
            if (paths.Count > 0) _propGroups["This Biome / " + group] = paths;
        }

        Add("1x1 Pieces", decorations.DecorationPiece);
        Add("2x2 Pieces", decorations.DecorationPiece2x2);
        Add("3x3 Pieces", decorations.DecorationPiece3x3);
        Add("3x3 Tall", decorations.DecorationPiece3x3Tall);
        Add("Ground Cover (on path)", decorations.DecorationPerlinNoiseOnPath);
        Add("Ground Cover (off path)", decorations.DecorationPerlinNoiseOffPath);
        Add("Critters", decorations.Critters);
        AddShapes("Shape Overlay (primary)", decorations.DecorationPerlinSpriteShapePrimary);
        AddShapes("Shape Overlay (secondary)", decorations.DecorationPerlinSpriteShapeSecondary);
    }

    private void ShowPropGroup(string group)
    {
        ClearPropButtons();
        if (_panel == null || _ui == null) return;
        if (!PropGroups().TryGetValue(group, out var paths)) return;

        _propButtons.Add(_ui.CreateLabel(_panel, $"— {group} —", 14, TMPro.TextAlignmentOptions.Center));

        foreach (var path in paths)
        {
            var captured = path;
            var label = System.IO.Path.GetFileNameWithoutExtension(path);
            _propButtons.Add(_ui.CreateButton(_panel, label, () =>
            {
                _propPath = captured;
                _pending = StructureBrain.TYPES.NONE;
                DestroyPreview();
                DestroyPropPreview();
                UpdatePropLabel();
                _editor.SetStatus($"Selected {label}. Left-click in the world to place it.");
            }));
        }
    }

    private void ClearPropButtons()
    {
        foreach (var go in _propButtons)
            if (go != null) Object.Destroy(go);
        _propButtons.Clear();
    }

    // Props are pooled spawns, so the room snapshot resolves them back to their path on save
    // without this tool tracking them at all.
    private void UpdatePropPlacement()
    {
        UpdatePropPreview();

        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;

        SpawnProp(_propPath, _editor.MouseWorld(), isPreview: false);
    }

    private void UpdatePropPreview()
    {
        if (_propPreview != null && _propPreviewPath == _propPath)
        {
            _propPreview.transform.position = _editor.MouseWorld();
            return;
        }

        if (_propPreviewPending) return;

        DestroyPropPreview();
        _propPreviewPath = _propPath;
        _propPreviewPending = true;
        SpawnProp(_propPath, _editor.MouseWorld(), isPreview: true);
    }

    private void SpawnProp(string path, Vector3 position, bool isPreview)
    {
        if (string.IsNullOrEmpty(path)) return;

        var parent = SceneRefs.Room != null && SceneRefs.Room.SceneryTransform != null
            ? SceneRefs.Room.SceneryTransform.transform
            : SceneRefs.ContentRoot;

        try
        {
            ObjectPool.Spawn(path, position, Quaternion.identity, parent, go =>
            {
                if (isPreview) _propPreviewPending = false;
                if (go == null) return;

                go.transform.position = position;
                if (!isPreview)
                {
                    _placedProps.Add(go);
                    return;
                }

                // A selection made while this was in flight wins.
                if (_propPath != _propPreviewPath) { Object.Destroy(go); return; }

                Fade(go, 0.6f);
                _propPreview = go;
            });
        }
        catch (System.Exception e)
        {
            if (isPreview) _propPreviewPending = false;
            Plugin.Log.LogWarning($"MapEditor: prop '{path}' failed to spawn: {e.Message}");
            _editor.SetStatus("That prop could not be spawned, see log.");
        }
    }

    private static void Fade(GameObject go, float alpha)
    {
        foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            var color = renderer.color;
            renderer.color = new Color(color.r, color.g, color.b, alpha);
        }
    }

    private void DestroyPropPreview()
    {
        if (_propPreview != null)
        {
            // Pooled instances get recycled, so the ghost's transparency has to come off
            // before this one goes back.
            Fade(_propPreview, 1f);
            Object.Destroy(_propPreview);
        }
        _propPreview = null;
        _propPreviewPath = null;
    }

    public void OnEnter()
    {
        _editor.SetStatus(_pending == StructureBrain.TYPES.NONE
            ? "Structure tool: open the picker to choose something."
            : $"Placing {_pending}. Left-click in the world.");
    }

    public void OnExit()
    {
        DestroyPreview();
        DestroyPropPreview();
    }

    // The loader wipes and rebuilds the room; anything this tool was tracking is gone.
    public void ResetTracking()
    {
        _placed.Clear();
        _pending = StructureBrain.TYPES.NONE;
        _propPath = null;
        DestroyPreview();
        DestroyPropPreview();
    }

    // The room snapshot skips objects this tool already serializes.
    public bool IsTracked(GameObject go)
    {
        // The cursor ghosts are live pooled objects sitting in the room, so the snapshot has to
        // be told they are not content.
        if (go != null && (go == _preview || go == _propPreview)) return true;

        foreach (var placed in _placed)
            if (placed.Instance == go) return true;
        return false;
    }

    // Keeps a tracked structure's serialised rotation in step with a turn applied to its
    // transform (the select tool's rotate button), so it survives save and load.
    public bool TryRotate(GameObject go, float degrees)
    {
        foreach (var placed in _placed)
        {
            if (placed.Instance != go) continue;
            placed.Rotation = Mathf.Repeat(placed.Rotation + degrees, 360f);
            return true;
        }
        return false;
    }

    // Adopts a ctrl-drag clone of one of our placed structures so it saves with its type.
    public bool TryAdoptClone(GameObject source, GameObject clone)
    {
        foreach (var placed in _placed)
        {
            if (placed.Instance != source) continue;
            _placed.Add(new PlacedStructure
            {
                Type = placed.Type,
                IsCustom = placed.IsCustom,
                Instance = clone,
                Rotation = placed.Rotation,
                FlipX = placed.FlipX
            });
            return true;
        }
        return false;
    }

    public void OnUpdate()
    {
        // The unlock/affordability window is held open for the whole picker session and released
        // the moment the menu is gone, so nothing leaks into normal gameplay.
        if (MapAssetsTab.ForceUnlockAll && (_menu == null || !_menu.isActiveAndEnabled))
            MapAssetsTab.ForceUnlockAll = false;

        // Do not place while the picker is still on screen.
        if (_menu != null && _menu.isActiveAndEnabled) return;

        if (!string.IsNullOrEmpty(_propPath))
        {
            UpdatePropPlacement();
            return;
        }

        if (_pending == StructureBrain.TYPES.NONE) return;

        UpdatePreview();

        if (!Input.GetMouseButtonDown(0)) return;

        if (_editor.PointerOverUi())
        {
            // _editor.SetStatus("Click was over the editor UI, ignored.");
            return;
        }

        var world = _editor.MouseWorld();
        Plugin.Log.LogInfo($"MapEditor structure: placing {_pending} at {world}");
        Place(_pending, world);
    }

    // Ghost of the chosen structure following the cursor.
    //
    // Built from TypeAndPlacementObject.PlacementObject, which is the prefab the game itself
    // uses for build previews. The menu's IconImage is a flat UI icon and looks nothing like the
    // placed object, which is why the first attempt previewed the wrong thing.
    private void UpdatePreview()
    {
        if (_preview != null && _previewType == _pending)
        {
            _preview.transform.position = _editor.MouseWorld();
            return;
        }

        DestroyPreview();
        _previewType = _pending;

        var entry = TypeAndPlacementObjects.GetByType(_pending);
        GameObject prefab = null;
        try
        {
            prefab = entry?.PlacementObject;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: no placement prefab for {_pending}: {e.Message}");
        }

        if (prefab != null)
        {
            // Via the ghost helper, never a plain Instantiate: the placement prefabs carry
            // Interaction components that would register with Interactor half-initialized and
            // make Interactor.Update throw every frame. Scripts stay on - they build the visuals.
            _preview = MapEditorGhost.Create(prefab, _editor.transform, "CultTweaker_PlacementPreview",
                disableBehaviours: false);
        }
        else
        {
            // Custom structures have no registered placement object; fall back to their icon.
            _preview = new GameObject("CultTweaker_PlacementPreview");
            var renderer = _preview.AddComponent<SpriteRenderer>();
            renderer.sprite = entry?.IconImage;
            renderer.sortingOrder = 9999;
            renderer.color = new Color(1f, 1f, 1f, 0.6f);
        }

        if (_preview != null) _preview.transform.position = _editor.MouseWorld();
    }

    private void DestroyPreview()
    {
        if (_preview != null) Object.Destroy(_preview);
        _preview = null;
        _previewType = StructureBrain.TYPES.NONE;
    }

    private void OpenPicker()
    {
        var uiManager = MonoSingleton<UIManager>.Instance;
        if (uiManager == null)
        {
            _editor.SetStatus("UIManager unavailable; cannot open the picker.");
            return;
        }

        // Held for the whole picker session (released in OnUpdate when the menu closes): the
        // vanilla tabs populate outside MapAssetsTab's transient window, so without this their
        // items stay greyed out by unlock state and material costs.
        MapAssetsTab.ForceUnlockAll = true;

        try
        {
            _menu = uiManager.ShowBuildMenu(StructureBrain.TYPES.NONE);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: build menu failed to open: " + e.Message);
            _editor.SetStatus("Build menu would not open in this scene.");
            return;
        }

        if (_menu == null)
        {
            _editor.SetStatus("Build menu unavailable in this scene.");
            return;
        }

        MapAssetsTab.Inject(_menu);

        // ShowBuildMenu can hand back a fresh controller each time. Tracking the instance we
        // hooked (rather than a bool) means reopening the picker re-subscribes to the new one;
        // otherwise only the very first structure chosen ever reached us.
        if (!ReferenceEquals(_hookedMenu, _menu))
        {
            _menu.OnBuildingChosen += OnBuildingChosen;
            _hookedMenu = _menu;
        }
    }

    private void OnBuildingChosen(StructureBrain.TYPES type)
    {
        // The menu's edit-buildings shortcut fires this with a sentinel type; base-only feature.
        if (type == StructureBrain.TYPES.EDIT_BUILDINGS || type == StructureBrain.TYPES.NONE)
        {
            _editor.SetStatus("Edit buildings is not available in the map editor.");
            return;
        }

        _pending = type;
        _editor.SetStatus($"Selected {type}. Left-click in the world to place it.");
    }

    private void Place(StructureBrain.TYPES type, Vector3 position)
    {
        var isCustom = CustomStructureManager.CustomStructureList.ContainsKey(type);
        _editor.StartCoroutine(PlaceAt(type, isCustom, position, 0f, false, deferNav: false));
    }

    private static string ResolvePrefabPath(StructureBrain.TYPES type, bool isCustom)
    {
        // Custom structures expose their synthetic addressable key, which COTL_API's
        // AddressablesImpl.InstantiateAsync patch intercepts and re-skins. It is already a
        // complete key and must not be rewritten.
        if (isCustom && CustomStructureManager.CustomStructureList.TryGetValue(type, out var custom))
            return custom.PrefabPath;

        var data = StructuresData.GetInfoByType(type, 0);
        if (data == null || string.IsNullOrEmpty(data.PrefabPath)) return null;

        // Vanilla PrefabPath is stored as a bare relative name, not an addressable key. The game
        // expands it the same way in LocationManager.InstantiateStructureAsync; skipping this is
        // why placing vanilla structures failed with an invalid-key error.
        return data.PrefabPath.Contains("Assets")
            ? data.PrefabPath
            : "Assets/" + data.PrefabPath + ".prefab";
    }

    // Shared by direct placement and the blueprint loader. Self-registers into _placed so the
    // next save round-trips. deferNav skips the per-placement A* rescan; the loader does one
    // batch rebuild at the end instead.
    public IEnumerator PlaceAt(StructureBrain.TYPES type, bool isCustom, Vector3 position,
        float rotation, bool flipX, bool deferNav)
    {
        var root = SceneRefs.ContentRoot;
        if (root == null)
        {
            _editor.SetStatus("No room content root; cannot place here.");
            yield break;
        }

        var prefabPath = ResolvePrefabPath(type, isCustom);
        if (string.IsNullOrEmpty(prefabPath))
        {
            _editor.SetStatus($"{type} has no prefab path; cannot place it.");
            yield break;
        }

        AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.InstantiateAsync(prefabPath, root, false);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not instantiate {type} ({prefabPath}): {e.Message}");
            _editor.SetStatus($"Failed to place {type}.");
            yield break;
        }

        // timeScale is 0 while editing, so this must not depend on scaled time.
        while (!handle.IsDone) yield return null;

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Plugin.Log.LogWarning($"MapEditor: addressable load failed for {type} ({prefabPath}).");
            _editor.SetStatus($"Failed to load {type}.");
            yield break;
        }

        var go = handle.Result;
        go.transform.position = position;
        go.name = $"CultTweaker_Placed_{type}";

        if (Mathf.Abs(rotation) > 0.001f)
            go.transform.eulerAngles = new Vector3(0f, 0f, rotation);
        if (flipX)
        {
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(-s.x, s.y, s.z);
        }

        _placed.Add(new PlacedStructure
        {
            Type = type,
            IsCustom = isCustom,
            Instance = go,
            Rotation = rotation,
            FlipX = flipX
        });

        if (!deferNav) SceneRefs.RescanNavigation();
        _editor.SetStatus($"Placed {type} at {position}.");
    }

    private void UndoLast()
    {
        if (_placed.Count == 0)
        {
            _editor.SetStatus("Nothing placed yet.");
            return;
        }

        var last = _placed[_placed.Count - 1];
        _placed.RemoveAt(_placed.Count - 1);
        if (last.Instance != null) Object.Destroy(last.Instance);

        SceneRefs.RescanNavigation();
        _editor.SetStatus($"Removed the last {last.Type}.");
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.Structures.Clear();
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            map.Structures.Add(new MapStructureData
            {
                // Custom types are saved by InternalName: ToString() on a GuidManager-minted
                // enum prints a bare integer that resolves differently on the next launch.
                TypeName = placed.IsCustom ? CustomInternalName(placed.Type) : placed.Type.ToString(),
                IsCustom = placed.IsCustom,
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                Rotation = placed.Rotation,
                FlipX = placed.FlipX
            });
        }
    }

    private static string CustomInternalName(StructureBrain.TYPES type)
    {
        return CustomStructureManager.CustomStructureList.TryGetValue(type, out var custom)
            ? custom.InternalName
            : type.ToString();
    }

    // Resolves a saved TypeName back to a live enum value. Custom names scan the registered
    // custom-structure list; vanilla names parse the enum, with a numeric legacy fallback.
    public static bool TryResolveType(string typeName, bool isCustom, out StructureBrain.TYPES type)
    {
        type = StructureBrain.TYPES.NONE;
        if (string.IsNullOrEmpty(typeName)) return false;

        if (isCustom)
        {
            foreach (var pair in CustomStructureManager.CustomStructureList)
            {
                if (pair.Value == null || pair.Value.InternalName != typeName) continue;
                type = pair.Key;
                return true;
            }

            // Old saves wrote the raw enum integer; honour it if the value still exists.
            if (int.TryParse(typeName, out var raw) &&
                CustomStructureManager.CustomStructureList.ContainsKey((StructureBrain.TYPES)raw))
            {
                type = (StructureBrain.TYPES)raw;
                return true;
            }
            return false;
        }

        return System.Enum.TryParse(typeName, out type) && type != StructureBrain.TYPES.NONE;
    }
}
