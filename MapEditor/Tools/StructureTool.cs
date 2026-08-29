using System.Collections;
using System.Collections.Generic;
using COTL_API.CustomStructures;
using CustomSpineLoader.SpineLoaderHelper;
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
        public bool SeeThrough;
        public bool FogThrough;
    }

    private bool _placeSeeThrough;
    private bool _placeFogThrough;

    public StructureTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    private const string StructureGroup = "Build Menu Structures";

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        BuildSearchRow(panel, ui);

        _groupKeys.Clear();
        var options = new List<string>();

        _groupKeys.Add(StructureGroup);
        options.Add(StructureGroup);

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

        _grid = ui.CreateIconGrid(panel, "PlacementGrid", scrollHeight: GridHeight);

        ui.CreateToggle(panel, "Multi-select randomised placement", false, SetScatterMode);

        ui.CreateToggle(panel, "Place see-through", false, on =>
        {
            _placeSeeThrough = on;
            _editor.SetStatus(on
                ? "New objects will show the player through."
                : "New objects will hide the player.");
        });

        ui.CreateToggle(panel, "Place fog pass-through", false, on =>
        {
            _placeFogThrough = on;
            _editor.SetStatus(on
                ? "New objects will take the fog."
                : "New objects will ignore the fog.");
        });

        ui.CreateButton(panel, "Clear Selection", () =>
        {
            _pending = StructureBrain.TYPES.NONE;
            _propPath = null;
            _picks.Clear();
            DestroyPreview();
            DestroyPropPreview();
            _grid?.SetSelectedMany(null);
            _grid?.SetSelected(null);
            _editor.SetStatus("Selection cleared.");
        }, emphasis: MapEditorEmphasis.Quiet);
    }

    // ---- randomised placement -------------------------------------------------------------------

    private class Pick
    {
        public string Id;
        public string Label;
        public bool IsProp;
        public string Path;
        public StructureBrain.TYPES Type;
    }

    private readonly List<Pick> _picks = [];
    private bool _scatter;

    private void SetScatterMode(bool on)
    {
        _scatter = on;

        _picks.Clear();
        _grid?.SetSelectedMany(null);

        _pending = StructureBrain.TYPES.NONE;
        _propPath = null;
        DestroyPreview();
        DestroyPropPreview();

        _editor.SetStatus(on
            ? "Pick several, then click to place one of them at random."
            : "Back to placing one at a time.");
    }

    private bool TogglePick(string id, string label, bool isProp, string path, StructureBrain.TYPES type)
    {
        if (!_scatter) return false;

        var existing = _picks.FindIndex(p => p.Id == id);
        if (existing >= 0)
        {
            _picks.RemoveAt(existing);
            _editor.SetStatus($"Dropped {label} - {_picks.Count} in the mix.");
        }
        else
        {
            _picks.Add(new Pick { Id = id, Label = label, IsProp = isProp, Path = path, Type = type });
            _editor.SetStatus($"Added {label} - {_picks.Count} in the mix.");
        }

        _grid?.SetSelectedMany(_picks.ConvertAll(p => p.Id));
        return true;
    }

    private void UpdateScatterPlacement()
    {
        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;

        var pick = _picks[Random.Range(0, _picks.Count)];
        var world = _editor.MouseWorld();

        if (pick.IsProp) SpawnProp(pick.Path, world, isPreview: false);
        else Place(pick.Type, world);
    }

    private const float GridHeight = 620f;

    private MapEditorGrid _grid;
    private MapEditorDropdown _groupDropdown;
    private readonly List<string> _groupKeys = [];

    private void ShowStructureGroup()
    {
        if (_grid == null) return;

        MapEditorIcons.CancelPendingPropIcons();
        _grid.Populate(_editor, StructureEntries(), id =>
        {
            if (_typesById.TryGetValue(id, out var type))
                _grid.SetCellIcon(id, MapEditorIcons.GetStructureIcon(type));
        });
    }

    private List<MapEditorGrid.Entry> StructureEntries()
    {
        var entries = new List<MapEditorGrid.Entry>();
        var seen = new HashSet<StructureBrain.TYPES>();

        if (HubSession.Active) entries.Add(TotemEntry());

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

        foreach (var pair in CustomStructureManager.CustomStructureList)
        {
            if (pair.Value == null || !seen.Add(pair.Key)) continue;
            entries.Add(StructureEntry(pair.Key, pair.Value.InternalName));
        }

        return entries;
    }

    // ---- the hub's build totem ------------------------------------------------------------------

    public const string TotemId = "hub:buildtotem";
    private const string TotemLabel = "Build Totem";

    private bool _pendingTotem;
    private GameObject _totemGhost;
    private Vector3 _totemGhostOffset;
    private GameObject _placedTotem;

    private MapEditorGrid.Entry TotemEntry() => new()
    {
        Id = TotemId,
        Display = TotemLabel,
        OnClick = () =>
        {
            _search?.Confirm();

            if (_placedTotem != null)
            {
                _editor.SetStatus("This hub already has a build totem.", StatusSeverity.Warning);
                return;
            }

            _pending = StructureBrain.TYPES.NONE;
            _propPath = null;
            _picks.Clear();
            DestroyPreview();
            DestroyPropPreview();

            _pendingTotem = true;
            _editor.SetStatus("Click to place the build totem.");
        }
    };

    private void UpdateTotemPlacement()
    {
        if (_totemGhost == null)
        {
            var source = HubBuildTotem.FindSourceObject();
            if (source != null)
            {
                _totemGhost = MapEditorGhost.Create(source, _editor.transform,
                    "CultTweaker_TotemPreview", disableBehaviours: true,
                    beforeWake: HubBuildTotem.DisarmGhost);

                _totemGhostOffset = HubBuildTotem.InteractionOffset(_totemGhost);
            }
        }

        if (_totemGhost != null)
            _totemGhost.transform.position = _editor.MouseWorld() - _totemGhostOffset;

        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;

        PlaceTotem(_editor.MouseWorld());
    }

    private void PlaceTotem(Vector3 position)
    {
        var totem = HubBuildTotem.Spawn(position, SceneRefs.ContentRoot);
        if (totem == null)
        {
            _editor.SetStatus("The build totem could not be copied from the base.",
                StatusSeverity.Error);
            CancelTotem();
            return;
        }

        _placedTotem = totem;
        CancelTotem();

        _editor.History.Push("place build totem", () =>
        {
            if (_placedTotem == null) return false;
            Object.Destroy(_placedTotem);
            _placedTotem = null;
            HubBuildTotem.Forget();
            return true;
        });

        _editor.SetStatus("Build totem placed. Players can build on the hub's ground from here.");
    }

    private void CancelTotem()
    {
        _pendingTotem = false;
        if (_totemGhost != null) Object.Destroy(_totemGhost);
        _totemGhost = null;
    }

    public void AdoptTotem(GameObject totem) => _placedTotem = totem;

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
                _search?.Confirm();

                if (TogglePick(id, label, isProp: false, path: null, type: type)) return;

                _pending = type;
                _propPath = null;
                DestroyPropPreview();
                _editor.SetStatus($"Selected {label}.");
            }
        };
    }

    // ---- search -------------------------------------------------------------------------------

    private MapEditorSearchRow _search;

    private void BuildSearchRow(RectTransform panel, MapEditorUI ui)
    {
        _search = new MapEditorSearchRow(_editor, ui, panel, ShowSearchResults, ShowCurrentGroup);
    }

    private void ShowCurrentGroup()
    {
        var index = _groupDropdown != null ? _groupDropdown.SelectedIndex : -1;
        if (index < 0 || index >= _groupKeys.Count)
        {
            ShowStructureGroup();
            return;
        }

        if (_groupKeys[index] == StructureGroup) ShowStructureGroup();
        else ShowPropGroup(_groupKeys[index]);
    }

    private void ShowSearchResults(string needle)
    {
        if (_grid == null) return;

        MapEditorIcons.CancelPendingPropIcons();

        var entries = new List<MapEditorGrid.Entry>();
        var total = 0;

        foreach (var structure in StructureEntries())
        {
            if (structure.Display.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            total++;
            if (entries.Count < MapEditorSearchRow.MaxResults) entries.Add(structure);
        }

        foreach (var pair in PropGroups())
        foreach (var path in pair.Value)
        {
            var label = System.IO.Path.GetFileNameWithoutExtension(path);
            if (label.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            total++;
            if (entries.Count >= MapEditorSearchRow.MaxResults) continue;

            var captured = path;
            entries.Add(new MapEditorGrid.Entry
            {
                Id = captured,
                Display = label,
                OnClick = () => SelectProp(captured, label)
            });
        }

        _grid.Populate(_editor, entries, id =>
        {
            if (_typesById.TryGetValue(id, out var type)) _grid.SetCellIcon(id, MapEditorIcons.GetStructureIcon(type));
            else MapEditorIcons.GetPropIcon(_editor, id, sprite => _grid?.SetCellIcon(id, sprite));
        });

        _search.ReportCount(entries.Count, total, needle);
    }

    private readonly List<GameObject> _placedProps = [];

    private string _propPath;
    private GameObject _propPreview;
    private string _propPreviewPath;
    private bool _propPreviewPending;

    private static SortedDictionary<string, List<string>> _propGroups;

    private const string PropPrefix = "Assets/Prefabs/";

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
                OnClick = () => SelectProp(captured, label)
            });
        }

        _grid.Populate(_editor, entries, id =>
            MapEditorIcons.GetPropIcon(_editor, id, sprite => _grid?.SetCellIcon(id, sprite)));
    }

    private void SelectProp(string path, string label)
    {
        _search?.Confirm();

        if (TogglePick(path, label, isProp: true, path: path, type: StructureBrain.TYPES.NONE)) return;

        _propPath = path;
        _pending = StructureBrain.TYPES.NONE;
        DestroyPreview();
        DestroyPropPreview();
        _editor.SetStatus($"Selected {label}.");
    }

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
            Fade(_propPreview, 1f);
            Object.Destroy(_propPreview);
        }
        _propPreview = null;
        _propPreviewPath = null;
    }

    public void OnEnter()
    {
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
        CancelTotem();
    }

    public void ResetTracking()
    {
        _placed.Clear();
        _placedProps.Clear();
        _pending = StructureBrain.TYPES.NONE;
        _propPath = null;
        _placedTotem = null;
        DestroyPreview();
        DestroyPropPreview();
        CancelTotem();
    }

    public bool IsTracked(GameObject go)
    {
        if (go != null && (go == _preview || go == _propPreview || go == _totemGhost)) return true;

        if (go != null && _placedTotem != null && go == _placedTotem) return true;

        foreach (var placed in _placed)
            if (placed.Instance == go) return true;
        return false;
    }

    public bool TryGetPlacedName(GameObject go, out string internalName)
    {
        internalName = null;
        if (go == null) return false;

        foreach (var placed in _placed)
        {
            if (placed.Instance != go) continue;
            internalName = placed.IsCustom ? CustomInternalName(placed.Type) : placed.Type.ToString();
            return true;
        }
        return false;
    }

    public bool CanSetSeeThrough(GameObject go) => IndexOfPlaced(go) >= 0;

    public bool IsSeeThrough(GameObject go)
    {
        var index = IndexOfPlaced(go);
        return index >= 0 && _placed[index].SeeThrough;
    }

    public bool IsFogThrough(GameObject go)
    {
        var index = IndexOfPlaced(go);
        return index >= 0 && _placed[index].FogThrough;
    }

    public bool TrySetSeeThrough(GameObject go, bool player, bool fog)
    {
        var index = IndexOfPlaced(go);
        if (index < 0) return false;

        var placed = _placed[index];
        if (placed.Instance == null) return false;

        SeeThrough.Set(placed.Instance, player, fog);

        placed.SeeThrough = SeeThrough.IsPlayerThrough(placed.Instance);
        placed.FogThrough = SeeThrough.IsFogThrough(placed.Instance);
        return true;
    }

    private int IndexOfPlaced(GameObject go)
    {
        if (go == null) return -1;

        for (var i = 0; i < _placed.Count; i++)
            if (_placed[i].Instance == go) return i;
        return -1;
    }

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
                FlipX = placed.FlipX,

                SeeThrough = placed.SeeThrough,
                FogThrough = placed.FogThrough
            });
            return true;
        }
        return false;
    }

    public void OnUpdate()
    {
        if (_pendingTotem)
        {
            UpdateTotemPlacement();
            return;
        }

        if (_scatter)
        {
            if (_picks.Count > 0) UpdateScatterPlacement();
            return;
        }

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

        var prefabPath = isCustom ? CustomStructureBasePrefab : ResolvePrefabPath(type, false);

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

        if (_previewType != type) yield break;

        GameObject ghost = null;
        if (prefab != null)
        {
            ghost = MapEditorGhost.Create(prefab, _editor.transform, "CultTweaker_PlacementPreview",
                disableBehaviours: true);

            if (ghost != null && isCustom) DressCustomStructure(ghost, type, ghostAlpha: 0.6f);
            else if (ghost != null) DressOverriddenBuilding(ghost, type);
        }

        if (ghost == null)
        {
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

    private const string CustomStructureBasePrefab =
        "Assets/Prefabs/Structures/Buildings/Decoration Wreath Stick.prefab";

    private static void DressCustomStructure(GameObject go, StructureBrain.TYPES type, float ghostAlpha)
    {
        if (!CustomStructureManager.CustomStructureList.TryGetValue(type, out var custom)) return;

        try
        {
            var renderer = go.GetComponentInChildren<SpriteRenderer>(true);
            var sprite = custom.Sprite;

            if (renderer != null && sprite != null)
                renderer.sprite = Sprite.Create(sprite.texture, sprite.rect, new Vector2(0.5f, 0f));

            StructureSpineHelper.TryAttach(go, type);

            if (ghostAlpha < 1f)
            {
                var spine = go.GetComponentInChildren<Spine.Unity.SkeletonAnimation>(true);
                if (spine?.Skeleton != null) spine.Skeleton.A = ghostAlpha;
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: custom structure {type} preview could not be " +
                                  $"dressed: {e.Message}");
        }
    }

    private static void DressOverriddenBuilding(GameObject ghost, StructureBrain.TYPES type)
    {
        var overrides = StructureBuildingOverrideHelper.GetOverridesForBuilding(type.ToString());
        if (overrides == null || overrides.Count == 0) return;

        try
        {
            CustomStructureManager.OverrideStructureBuilding(ghost, overrides);

            foreach (var renderer in ghost.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.color = new Color(renderer.color.r, renderer.color.g, renderer.color.b, 0.6f);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: {type} preview could not take its building " +
                                  $"override: {e.Message}");
        }
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
        float rotation, bool flipX, bool deferNav) =>
        PlaceAt(type, isCustom, position, rotation, flipX, deferNav, _placeSeeThrough, _placeFogThrough);

    public IEnumerator PlaceAt(StructureBrain.TYPES type, bool isCustom, Vector3 position,
        float rotation, bool flipX, bool deferNav, bool seeThrough, bool fogThrough)
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

        if (isCustom) StructureSpineHelper.TryAttach(go, type);

        if (Mathf.Abs(rotation) > 0.001f)
            go.transform.eulerAngles = new Vector3(0f, rotation, 0f);
        if (flipX)
        {
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(-s.x, s.y, s.z);
        }

        if (seeThrough || fogThrough) SeeThrough.Set(go, seeThrough, fogThrough);

        BaseDelta.GiveBrain(go, type, position);

        var placed = new PlacedStructure
        {
            Type = type,
            IsCustom = isCustom,
            Instance = go,
            Rotation = rotation,
            FlipX = flipX,
            SeeThrough = seeThrough,
            FogThrough = fogThrough
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

    public GameObject LastPlacedInstance =>
        _placed.Count > 0 ? _placed[_placed.Count - 1].Instance : null;

    private static Vector3 Abs(Vector3 v) =>
        new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.BuildTotem = _placedTotem == null
            ? null
            : new MapTotemData { Position = MapEditorSerialization.V3(_placedTotem.transform.position) };

        map.Structures.Clear();
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            map.Structures.Add(new MapStructureData
            {
                TypeName = placed.IsCustom ? CustomInternalName(placed.Type) : placed.Type.ToString(),
                IsCustom = placed.IsCustom,
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                Rotation = placed.Rotation,
                FlipX = placed.FlipX,
                SeeThrough = placed.SeeThrough,
                FogThrough = placed.FogThrough,

                Scale = MapEditorSerialization.V3(Abs(placed.Instance.transform.lossyScale))
            });
        }
    }

    private static string CustomInternalName(StructureBrain.TYPES type) => InternalNameOf(type);

    public static string InternalNameOf(StructureBrain.TYPES type) =>
        CustomStructureManager.CustomStructureList.TryGetValue(type, out var custom) && custom != null
            ? custom.InternalName
            : type.ToString();

    public static bool TryResolveAnyType(string typeName, out StructureBrain.TYPES type) =>
        TryResolveType(typeName, isCustom: true, out type) ||
        TryResolveType(typeName, isCustom: false, out type);

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
