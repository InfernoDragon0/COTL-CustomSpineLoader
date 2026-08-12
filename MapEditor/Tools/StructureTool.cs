using System.Collections;
using System.Collections.Generic;
using COTL_API.CustomStructures;
using Lamb.UI;
using Lamb.UI.BuildMenu;
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
        ui.CreateLabel(panel, "Structure Tool", 20, TMPro.TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Pick a structure, then\nleft-click to place it.", 14, TMPro.TextAlignmentOptions.Center);
        ui.CreateButton(panel, "Open Picker", OpenPicker);
        ui.CreateButton(panel, "Clear Selection", () =>
        {
            _pending = StructureBrain.TYPES.NONE;
            DestroyPreview();
            _editor.SetStatus("Selection cleared.");
        });
        ui.CreateButton(panel, "Undo Last Placement", UndoLast);
    }

    public void OnEnter()
    {
        _editor.SetStatus(_pending == StructureBrain.TYPES.NONE
            ? "Structure tool: open the picker to choose something."
            : $"Placing {_pending}. Left-click in the world.");
    }

    public void OnExit() => DestroyPreview();

    // The loader wipes and rebuilds the room; anything this tool was tracking is gone.
    public void ResetTracking()
    {
        _placed.Clear();
        _pending = StructureBrain.TYPES.NONE;
        DestroyPreview();
    }

    // The room snapshot skips objects this tool already serializes.
    public bool IsTracked(GameObject go)
    {
        foreach (var placed in _placed)
            if (placed.Instance == go) return true;
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

        if (_pending == StructureBrain.TYPES.NONE) return;

        // Do not place while the picker is still on screen.
        if (_menu != null && _menu.isActiveAndEnabled) return;

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
