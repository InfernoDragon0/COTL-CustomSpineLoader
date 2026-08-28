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

    // Armed for the next placement, the way the scatter setting is: what is placed while these are
    // ticked comes out that way. Anything already down is changed from the Select tool instead.
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

        // The crystal tree's two looks, on anything, and separately: one lets the player show
        // through, the other lets the weather through. Anything already down is changed with the
        // Select tool.
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

    // Gather several things, then let each click choose between them. Filling a treeline or a field
    // of rubble one structure at a time produces rows that read as rows; the point of the mode is
    // that the variety costs nothing to place.
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

        // The single pending selection and the gathered set are two answers to the same question,
        // so only one of them is ever live. Switching modes drops the other rather than leaving it
        // to fire on the next click.
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

    // Returns true when the click was consumed by the gathering, so the caller does not also make
    // it the single selection.
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

    // No cursor ghost here, and that is deliberate: a ghost would have to show one of the picks,
    // and whichever it showed would be wrong most of the time. The status line carries the count
    // instead, so what the click will do is stated rather than mimed.
    private void UpdateScatterPlacement()
    {
        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;

        var pick = _picks[Random.Range(0, _picks.Count)];
        var world = _editor.MouseWorld();

        if (pick.IsProp) SpawnProp(pick.Path, world, isPreview: false);
        else Place(pick.Type, world);
    }

    // Tall enough to browse in, short enough that the search field and the group picker
    // above it never leave the screen.
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

    // The build-menu structures as grid entries. Shared with the search, which has to match on the
    // same names the group view shows.
    private List<MapEditorGrid.Entry> StructureEntries()
    {
        var entries = new List<MapEditorGrid.Entry>();
        var seen = new HashSet<StructureBrain.TYPES>();

        // First in the list, and only in a hub: it is the one entry here that is not a building but
        // the thing that lets the player put buildings up for themselves.
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

        // Modded structures may not be in the scene's placement list; fold them in explicitly.
        foreach (var pair in CustomStructureManager.CustomStructureList)
        {
            if (pair.Value == null || !seen.Add(pair.Key)) continue;
            entries.Add(StructureEntry(pair.Key, pair.Value.InternalName));
        }

        return entries;
    }

    // ---- the hub's build totem ------------------------------------------------------------------

    // Placed by hand like anything else in this tool: pick it, a ghost follows the cursor, click to
    // stand it up. What it does at play time is the game's own build menu - the player walks up to
    // it, opens it, and puts real buildings on the hub's ground for real resources.
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

    // For the loader: a totem rebuilt from the blueprint is the one this tool now owns.
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
                // Picking one is the end of the search; the status set below is what should be
                // left on screen, so this goes first.
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

    // Back to whichever group the dropdown is on, for a cleared or cancelled search.
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

    // Every group at once: the catalog is filed by Addressables folder, so the same word turns up
    // in several and searching one at a time would be searching the wrong one.
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

    // Group name -> prop prefab paths.
    private static SortedDictionary<string, List<string>> _propGroups;

    private const string PropPrefix = "Assets/Prefabs/";

    // Enemies have their own tool.
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

        // Icons still loading for the previous group would fill cells that no longer exist.
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
        // Picking one is the end of the search; the status set below is what should be left on
        // screen, so this goes first.
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
            // Pooled instances recycle; the ghost's fade must come off before this one goes back.
            Fade(_propPreview, 1f);
            Object.Destroy(_propPreview);
        }
        _propPreview = null;
        _propPreviewPath = null;
    }

    public void OnEnter()
    {
        // TypeAndPlacementObjects does not exist yet when the panels are built in Awake.
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

    // The room snapshot skips objects this tool already serializes.
    public bool IsTracked(GameObject go)
    {
        // Cursor ghosts are live pooled objects in the room; the snapshot must skip them too.
        if (go != null && (go == _preview || go == _propPreview || go == _totemGhost)) return true;

        // The totem round-trips as a position on the blueprint, and it is rebuilt from the base's
        // own on every load. Unclaimed, the snapshot took it for authored scenery - its name does
        // not end in "(Clone)" - and wrote it into KeptAuthored as well, which is a second way to
        // bring back an object that already has one.
        if (go != null && _placedTotem != null && go == _placedTotem) return true;

        foreach (var placed in _placed)
            if (placed.Instance == go) return true;
        return false;
    }

    // What this tool would call the object if it saved it now - the name that goes in the blueprint
    // and that other mods' folders are keyed by. The Select tool shows it; a clone dragged off a
    // placed structure is tracked too, so it answers for those as well.
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

    // The Select tool's two boxes. Only a structure this tool placed can answer, because only those
    // round-trip through the blueprint - the flags have to be written down as well as applied, or
    // they would be gone the next time the map is loaded.
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

        // Read back rather than assumed: a sprite an effect could not attach to leaves the boxes
        // saying what actually happened.
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

    // Keeps the serialised mirror flag in step with a transform flip (select tool's flip button).
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
                FlipX = placed.FlipX,

                // A ctrl-drag copy is a copy of the whole thing, the looks included - Unity copied
                // the components and materials with it.
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

        // Not ResolvePrefabPath for custom structures: their path is not a real addressable key.
        // COTL_API swaps it inside InstantiateAsync only, so LoadAssetAsync throws
        // InvalidKeyException; load the base prefab and dress it here instead.
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

        // The selection moved on while the asset was loading.
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
            // Nothing loadable; the flat icon at least shows what is armed.
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

    // What COTL_API instantiates for every custom structure before swapping its sprite.
    private const string CustomStructureBasePrefab =
        "Assets/Prefabs/Structures/Buildings/Decoration Wreath Stick.prefab";

    // Replicates COTL_API's InstantiateAsync dressing (sprite, plus our skeleton) for the
    // preview, which bypasses that patch.
    private static void DressCustomStructure(GameObject go, StructureBrain.TYPES type, float ghostAlpha)
    {
        if (!CustomStructureManager.CustomStructureList.TryGetValue(type, out var custom)) return;

        try
        {
            var renderer = go.GetComponentInChildren<SpriteRenderer>(true);
            var sprite = custom.Sprite;

            // Bottom-centre re-pivot, same as COTL_API: a structure stands on its tile.
            if (renderer != null && sprite != null)
                renderer.sprite = Sprite.Create(sprite.texture, sprite.rect, new Vector2(0.5f, 0f));

            StructureSpineHelper.TryAttach(go, type);

            // Sprite-tint fading misses the skeleton mesh; Spine carries its own colour.
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

    // A vanilla building re-skinned by a BuildingOverrides folder gets its new look from a postfix
    // on Structure.Start. The ghost never wakes that behaviour, so without this it shows the stock
    // art while you aim and the override only after the click - the one moment you are choosing by
    // eye is the one moment it lies.
    private static void DressOverriddenBuilding(GameObject ghost, StructureBrain.TYPES type)
    {
        var overrides = StructureBuildingOverrideHelper.GetOverridesForBuilding(type.ToString());
        if (overrides == null || overrides.Count == 0) return;

        try
        {
            CustomStructureManager.OverrideStructureBuilding(ghost, overrides);

            // The ghost was faded on the way out of MapEditorGhost.Create; these renderers are
            // newer than that pass.
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

        // The spine patch hangs off LocationManager.PlaceStructure; the editor instantiates its
        // own, so it must attach the skeleton itself.
        if (isCustom) StructureSpineHelper.TryAttach(go, type);

        if (Mathf.Abs(rotation) > 0.001f)
            go.transform.eulerAngles = new Vector3(0f, rotation, 0f);
        if (flipX)
        {
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(-s.x, s.y, s.z);
        }

        // After the flip and the scale, and after a custom structure has had its own art attached:
        // both looks read the sprite that is there when they are applied.
        if (seeThrough || fogThrough) SeeThrough.Set(go, seeThrough, fogThrough);

        // In the base, a structure is meant to be a building rather than a picture of one: the bed
        // gets slept in, the plot gets farmed. That takes a brain, which the game will make without
        // filing it in the player's save.
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

    // For the loader: the spawn routines are coroutines and hand nothing back.
    public GameObject LastPlacedInstance =>
        _placed.Count > 0 ? _placed[_placed.Count - 1].Instance : null;

    private static Vector3 Abs(Vector3 v) =>
        new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    public void ContributeTo(CTNodeBlueprint map)
    {
        // Destroyed with the Select tool rather than un-placed, so the check is for the object
        // still being there, not for a flag somebody remembered to clear.
        map.BuildTotem = _placedTotem == null
            ? null
            : new MapTotemData { Position = MapEditorSerialization.V3(_placedTotem.transform.position) };

        map.Structures.Clear();
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            map.Structures.Add(new MapStructureData
            {
                // InternalName, not ToString(): a GuidManager-minted enum prints a bare integer
                // that resolves differently next launch.
                TypeName = placed.IsCustom ? CustomInternalName(placed.Type) : placed.Type.ToString(),
                IsCustom = placed.IsCustom,
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                Rotation = placed.Rotation,
                FlipX = placed.FlipX,
                SeeThrough = placed.SeeThrough,
                FogThrough = placed.FogThrough,

                // Absolute: FlipX is stored separately and re-applied on load; a negative X
                // here would cancel it out.
                Scale = MapEditorSerialization.V3(Abs(placed.Instance.transform.lossyScale))
            });
        }
    }

    private static string CustomInternalName(StructureBrain.TYPES type) => InternalNameOf(type);

    // The name a structure is written down by, whoever is doing the writing - a blueprint, a hub's
    // building file, the Select tool's readout. A GuidManager-minted enum prints a bare integer
    // that resolves differently next launch, so a custom structure answers with the name its own
    // mod gave it.
    public static string InternalNameOf(StructureBrain.TYPES type) =>
        CustomStructureManager.CustomStructureList.TryGetValue(type, out var custom) && custom != null
            ? custom.InternalName
            : type.ToString();

    // For callers that saved a name without recording whether it was custom.
    public static bool TryResolveAnyType(string typeName, out StructureBrain.TYPES type) =>
        TryResolveType(typeName, isCustom: true, out type) ||
        TryResolveType(typeName, isCustom: false, out type);

    // Resolves a saved TypeName back to a live enum value.
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
