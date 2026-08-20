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

public class StructureTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts
{
    public string Name => "Structures";

    private readonly RuntimeMapEditor _editor;
    private readonly List<PlacedStructure> _placed = [];

    private StructureBrain.TYPES _pending = StructureBrain.TYPES.NONE;

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

    private const string StructureGroup = "Build Menu Structures";

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _groupKeys.Clear();
        var options = new List<string>();

        _groupKeys.Add(StructureGroup);
        options.Add(StructureGroup);

        // Everything a room is actually dressed with - grass tufts, background pieces, rocks,
        // props from every dungeon and the DLC - is an ordinary prefab in the catalog.
        foreach (var group in PropGroups().Keys)
        {
            _groupKeys.Add(group);
            options.Add($"{group} ({PropGroups()[group].Count})");
        }

        _groupDropdown = ui.CreateDropdown(panel, "Choose a group", options, (index, _) =>
        {
            if (index < 0 || index >= _groupKeys.Count) return;
            if (_groupKeys[index] == StructureGroup) ShowStructureGroup();
            else ShowPropGroup(_groupKeys[index]);
        });

        _grid = ui.CreateIconGrid(panel, "PlacementGrid");

        ui.CreateButton(panel, "Clear Selection", () =>
        {
            _pending = StructureBrain.TYPES.NONE;
            _propPath = null;
            DestroyPreview();
            DestroyPropPreview();
                _grid?.SetSelected(null);
            _editor.SetStatus("Selection cleared.");
        });
    }

    private MapEditorGrid _grid;
    private MapEditorDropdown _groupDropdown;
    private readonly List<string> _groupKeys = [];

    // Cult structures, drawn from the same TypeAndPlacementObjects list (and the same icons) the
    // vanilla build menu uses, plus anything other mods registered through COTL_API.
    private void ShowStructureGroup()
    {
        if (_grid == null) return;

        var entries = new List<MapEditorGrid.Entry>();
        var seen = new HashSet<StructureBrain.TYPES>();

        var host = Object.FindObjectOfType<TypeAndPlacementObjects>();
        if (host?.TypeAndPlacementObject != null)
        {
            foreach (var entry in host.TypeAndPlacementObject)
            {
                if (entry == null || entry.Type == StructureBrain.TYPES.NONE) continue;
                if (entry.Type == StructureBrain.TYPES.EDIT_BUILDINGS) continue;
                if (!seen.Add(entry.Type)) continue;

                MapEditorIcons.GetStructureIcon(entry.Type, entry.IconImage);
                entries.Add(StructureEntry(entry.Type, entry.Type.ToString()));
            }
        }

        // Modded structures may not have made it into the scene's placement list; they are the
        // reason the build-menu button existed, so they are folded in here explicitly.
        foreach (var pair in CustomStructureManager.CustomStructureList)
        {
            if (pair.Value == null || !seen.Add(pair.Key)) continue;
            entries.Add(StructureEntry(pair.Key, pair.Value.InternalName));
        }

        MapEditorIcons.CancelPendingPropIcons();
        _grid.Populate(_editor, entries, id =>
        {
            if (_typesById.TryGetValue(id, out var type))
                _grid.SetCellIcon(id, MapEditorIcons.GetStructureIcon(type));
        });
    }

    private readonly Dictionary<string, StructureBrain.TYPES> _typesById = [];

    private MapEditorGrid.Entry StructureEntry(StructureBrain.TYPES type, string label)
    {
        var id = "type:" + type;
        _typesById[id] = type;

        return new MapEditorGrid.Entry
        {
            Id = id,
            Display = label,
            OnClick = () =>
            {
                _pending = type;
                _propPath = null;
                DestroyPropPreview();
                        _editor.SetStatus($"Selected {label}.");
            }
        };
    }

    private readonly List<GameObject> _placedProps = [];

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
        if (_grid == null || !PropGroups().TryGetValue(group, out var paths)) return;

        // The previous group's icons are still loading and would fill cells that no longer exist.
        MapEditorIcons.CancelPendingPropIcons();

        var entries = new List<MapEditorGrid.Entry>(paths.Count);
        foreach (var path in paths)
        {
            var captured = path;
            var label = System.IO.Path.GetFileNameWithoutExtension(path);

            entries.Add(new MapEditorGrid.Entry
            {
                Id = captured,
                Display = label,
                OnClick = () =>
                {
                    _propPath = captured;
                    _pending = StructureBrain.TYPES.NONE;
                    DestroyPreview();
                    DestroyPropPreview();
                                _editor.SetStatus($"Selected {label}.");
                }
            });
        }

        _grid.Populate(_editor, entries, id =>
            MapEditorIcons.GetPropIcon(_editor, id, sprite => _grid?.SetCellIcon(id, sprite)));
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
                    var label = System.IO.Path.GetFileNameWithoutExtension(path);
                    _editor.History.Push($"place {label}", () =>
                    {
                        if (!_placedProps.Remove(go) || go == null) return false;
                        Object.Destroy(go);
                        return true;
                    });
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
            _editor.SetStatus("Prop failed to spawn - see log.", StatusSeverity.Error);
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
        // The structure group is only browsable once TypeAndPlacementObjects exists in the
        // scene, which it does not when the panels are built in Awake.
        if (_grid != null && _groupDropdown != null && _groupDropdown.SelectedIndex < 0)
        {
            _groupDropdown.SetSelected(0);
            ShowStructureGroup();
        }

        _editor.SetStatus(_pending == StructureBrain.TYPES.NONE && string.IsNullOrEmpty(_propPath)
            ? "Pick a group, then an item."
            : "Ready to place.");
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Place selected item")
    ];

    public void OnExit()
    {
        DestroyPreview();
        DestroyPropPreview();
    }

    // The loader wipes and rebuilds the room; anything this tool was tracking is gone.
    public void ResetTracking()
    {
        _placed.Clear();
        _placedProps.Clear();
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

    // Keeps a tracked structure's serialised mirror flag in step with a flip applied to its
    // transform (the select tool's flip button), so it survives save and load.
    public bool TryFlip(GameObject go)
    {
        foreach (var placed in _placed)
        {
            if (placed.Instance != go) continue;
            placed.FlipX = !placed.FlipX;
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

    private void UpdatePreview()
    {
        if (_preview != null && _previewType == _pending)
        {
            _preview.transform.position = _editor.MouseWorld();
            return;
        }

        if (_previewPending) return;

        DestroyPreview();
        _previewType = _pending;
        _previewPending = true;
        _editor.StartCoroutine(BuildPreview(_pending));
    }

    private bool _previewPending;

    private IEnumerator BuildPreview(StructureBrain.TYPES type)
    {
        var isCustom = CustomStructureManager.CustomStructureList.ContainsKey(type);
        var prefabPath = ResolvePrefabPath(type, isCustom);

        GameObject prefab = null;
        if (!string.IsNullOrEmpty(prefabPath))
        {
            AsyncOperationHandle<GameObject> handle = default;
            var started = true;
            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(prefabPath);
            }
            catch (System.Exception e)
            {
                started = false;
                Plugin.Log.LogWarning($"MapEditor: preview load failed for {type} ({prefabPath}): {e.Message}");
            }

            if (started)
            {
                while (!handle.IsDone) yield return null;
                if (handle.Status == AsyncOperationStatus.Succeeded) prefab = handle.Result;
            }
        }

        _previewPending = false;

        // The selection moved on while the asset was loading.
        if (_previewType != type) yield break;

        GameObject ghost = null;
        if (prefab != null)
            ghost = MapEditorGhost.Create(prefab, _editor.transform, "CultTweaker_PlacementPreview",
                disableBehaviours: true);

        if (ghost == null)
        {
            // Nothing loadable (a custom structure with no prefab, usually); the flat icon at
            // least shows what is armed.
            ghost = new GameObject("CultTweaker_PlacementPreview");
            var renderer = ghost.AddComponent<SpriteRenderer>();
            renderer.sprite = MapEditorIcons.GetStructureIcon(type);
            renderer.sortingOrder = 9999;
            renderer.color = new Color(1f, 1f, 1f, 0.6f);
        }

        if (_previewType != type)
        {
            Object.Destroy(ghost);
            yield break;
        }

        if (_preview != null) Object.Destroy(_preview);
        _preview = ghost;
        _preview.transform.position = _editor.MouseWorld();
    }

    private void DestroyPreview()
    {
        if (_preview != null) Object.Destroy(_preview);
        _preview = null;
        _previewType = StructureBrain.TYPES.NONE;
    }

    private void Place(StructureBrain.TYPES type, Vector3 position)
    {
        var isCustom = CustomStructureManager.CustomStructureList.ContainsKey(type);
        _editor.StartCoroutine(PlaceAt(type, isCustom, position, 0f, false, deferNav: false));
    }

    private static string ResolvePrefabPath(StructureBrain.TYPES type, bool isCustom)
    {
        if (isCustom && CustomStructureManager.CustomStructureList.TryGetValue(type, out var custom))
            return custom.PrefabPath;

        var data = StructuresData.GetInfoByType(type, 0);
        if (data == null || string.IsNullOrEmpty(data.PrefabPath)) return null;

        return data.PrefabPath.Contains("Assets")
            ? data.PrefabPath
            : "Assets/" + data.PrefabPath + ".prefab";
    }

    public IEnumerator PlaceAt(StructureBrain.TYPES type, bool isCustom, Vector3 position,
        float rotation, bool flipX, bool deferNav)
    {
        var root = SceneRefs.ContentRoot;
        if (root == null)
        {
            _editor.SetStatus("No room content root.", StatusSeverity.Error);
            yield break;
        }

        var prefabPath = ResolvePrefabPath(type, isCustom);
        if (string.IsNullOrEmpty(prefabPath))
        {
            _editor.SetStatus($"{type} has no prefab path.", StatusSeverity.Error);
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
            _editor.SetStatus($"Failed to place {type}.", StatusSeverity.Error);
            yield break;
        }

        // timeScale is 0 while editing, so this must not depend on scaled time.
        while (!handle.IsDone) yield return null;

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Plugin.Log.LogWarning($"MapEditor: addressable load failed for {type} ({prefabPath}).");
            _editor.SetStatus($"Failed to load {type}.", StatusSeverity.Error);
            yield break;
        }

        var go = handle.Result;
        go.transform.position = position;
        go.name = $"CultTweaker_Placed_{type}";

        if (Mathf.Abs(rotation) > 0.001f)
            go.transform.eulerAngles = new Vector3(0f, rotation, 0f);
        if (flipX)
        {
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(-s.x, s.y, s.z);
        }

        var placed = new PlacedStructure
        {
            Type = type,
            IsCustom = isCustom,
            Instance = go,
            Rotation = rotation,
            FlipX = flipX
        };
        _placed.Add(placed);
        _editor.History.Push($"place {type}", () =>
        {
            if (!_placed.Remove(placed) || placed.Instance == null) return false;
            Object.Destroy(placed.Instance);
            SceneRefs.RescanNavigation();
            return true;
        });

        if (!deferNav) SceneRefs.RescanNavigation();
        _editor.SetStatus($"Placed {type}.");
    }

    // Everything this tool put in the room, for the clear tool.
    public int ClearPlaced()
    {
        var removed = 0;

        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            Object.Destroy(placed.Instance);
            removed++;
        }

        foreach (var prop in _placedProps)
        {
            if (prop == null) continue;
            Object.Destroy(prop);
            removed++;
        }

        _placed.Clear();
        _placedProps.Clear();
        return removed;
    }

    // The instance the last placement produced, so a loader can finish setting it up without
    // every spawn routine having to hand one back.
    public GameObject LastPlacedInstance =>
        _placed.Count > 0 ? _placed[_placed.Count - 1].Instance : null;

    private static Vector3 Abs(Vector3 v) =>
        new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

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
                FlipX = placed.FlipX,

                // Absolute: the mirror is stored separately as FlipX and re-applied by the
                // structure's own flip, so a negative X here would cancel it out on load.
                Scale = MapEditorSerialization.V3(Abs(placed.Instance.transform.lossyScale))
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
