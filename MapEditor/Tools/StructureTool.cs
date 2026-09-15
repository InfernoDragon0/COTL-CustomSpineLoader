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
        public bool Wind;
    }

    private bool _placeSeeThrough;
    private bool _placeFogThrough;
    private bool _placeWind;

    private MapEditorToggle _seeThroughToggle;
    private MapEditorToggle _windToggle;

    public StructureTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    private const string StructureGroup = "Build Menu Structures";

    // Custom structures get their own group rather than being tacked onto the game's list, and it shows
    // ALL of them -- including any with hideFromBuildMenu set, which is the point: the player cannot
    // build those, but a map maker still has to be able to place them.
    private const string CustomGroup = "Custom";

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        BuildSearchRow(panel, ui);

        _groupKeys.Clear();
        var options = new List<string>();

        _groupKeys.Add(StructureGroup);
        options.Add(StructureGroup);

        // No count in the label: the options are fixed when the panel is built and a number here
        // would go stale the first time anything was pinned.
        _groupKeys.Add(FavouritesGroup);
        options.Add("Favourites");

        var customCount = CustomStructureManager.CustomStructureList.Count;
        if (customCount > 0)
        {
            _groupKeys.Add(CustomGroup);
            options.Add($"{CustomGroup} ({customCount})");
        }

        _groupKeys.Add(BossSceneryGroup);
        options.Add("Boss & special room pieces");

        foreach (var group in PropGroups().Keys)
        {
            _groupKeys.Add(group);
            options.Add($"{group} ({PropGroups()[group].Count})");
        }

        _groupDropdown = ui.CreateDropdown(panel, "Choose a group", options, (index, _) =>
        {
            if (index < 0 || index >= _groupKeys.Count) return;
            ShowGroup(_groupKeys[index]);
        });

        _grid = ui.CreateIconGrid(panel, "PlacementGrid", scrollHeight: GridHeight);
        _grid.OnRightClick = RightClickCell;
        _grid.IsFavourite = id => MapEditorFavourites.IsFavourite(EntryForCell(id, null)?.Key);

        // The browser is what this panel is for; everything that decides how the next thing lands
        // goes under one header so the grid keeps the height.
        var placement = ui.CreateSection(panel, "Placement");
        var rows = placement.Content;

        ui.CreateToggle(rows, "Multi-select randomised placement", false, SetScatterMode);

        ui.CreateToggle(rows, "Break apart randomised sets", true, on =>
        {
            _breakApart = on;
            _editor.SetStatus(on
                ? "A randomised set drops as separate pieces you can move and delete."
                : "A randomised set drops whole, with its dice thrown away.");
        });

        WireQuickPick();

        ui.CreateToggle(rows, "Structure quick pick dock", true, on =>
        {
            var bar = _editor.QuickPick;
            if (bar != null) bar.Visible = on;

            _editor.SetStatus(on
                ? "Quick pick holds your pinned picks and the last things you placed; " +
                  "1-9 or the wheel arms one, right-click pins and unpins."
                : "Quick pick hidden; the wheel goes back to cycling tools.");
        });

        _seeThroughToggle = ui.CreateToggle(rows, "Place see-through", false, on =>
        {
            _placeSeeThrough = on;
            _editor.SetStatus(on
                ? "New objects will show the player through."
                : "New objects will hide the player.");
        }).GetComponent<MapEditorToggle>();

        ui.CreateToggle(rows, "Place fog pass-through", false, on =>
        {
            _placeFogThrough = on;
            _editor.SetStatus(on
                ? "New objects will take the fog."
                : "New objects will ignore the fog.");
        });

        _windToggle = ui.CreateToggle(rows, "Place affected by wind", false, on =>
        {
            _placeWind = on;
            _editor.SetStatus(on
                ? "New objects will sway with the biome's wind."
                : "New objects will stand still.");
        }).GetComponent<MapEditorToggle>();

        ui.CreateButton(rows, "Clear Selection", () =>
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

        if (pick.IsProp)
        {
            NotePlacedProp(pick.Path);
            SpawnProp(pick.Path, world, isPreview: false);
        }
        else
        {
            Place(pick.Type, world);
        }
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

        return entries;
    }

    private void ShowCustomGroup()
    {
        if (_grid == null) return;

        MapEditorIcons.CancelPendingPropIcons();
        _grid.Populate(_editor, CustomEntries(), id =>
        {
            if (_typesById.TryGetValue(id, out var type))
                _grid.SetCellIcon(id, MapEditorIcons.GetStructureIcon(type));
        });
    }

    private List<MapEditorGrid.Entry> CustomEntries()
    {
        var entries = new List<MapEditorGrid.Entry>();

        foreach (var pair in CustomStructureManager.CustomStructureList)
        {
            if (pair.Value == null) continue;
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

    internal GameObject PlacedTotem => _placedTotem;

    internal void RemoveTotem()
    {
        if (_placedTotem != null) Object.Destroy(_placedTotem);
        _placedTotem = null;
        HubBuildTotem.Forget();
    }

    /// Takes a placed structure or prop out of the room and out of the books; false if it was not ours.
    internal bool RemoveTracked(GameObject go)
    {
        if (go == null) return false;

        var index = IndexOfPlaced(go);
        if (index >= 0)
        {
            _placed.RemoveAt(index);
            BaseDelta.RetireBrain(go);
            Object.Destroy(go);
            return true;
        }

        if (!_placedProps.Remove(go)) return false;
        Object.Destroy(go);
        return true;
    }

    /// A peer moved or turned a placed structure; the books follow the object.
    internal void UpdatePlaced(GameObject go, float rotation, bool flipX)
    {
        var index = IndexOfPlaced(go);
        if (index < 0) return;
        _placed[index].Rotation = rotation;
        _placed[index].FlipX = flipX;
    }

    private readonly Dictionary<string, StructureBrain.TYPES> _typesById = [];

    private const string TypeCellPrefix = "type:";

    private MapEditorGrid.Entry StructureEntry(StructureBrain.TYPES type, string label)
    {
        var id = TypeCellPrefix + type;
        _typesById[id] = type;

        return new MapEditorGrid.Entry
        {
            Id = id,
            Display = label,
            OnClick = () => SelectStructure(type, label)
        };
    }

    private void SelectStructure(StructureBrain.TYPES type, string label)
    {
        _search?.Confirm();

        if (TogglePick("type:" + type, label, isProp: false, path: null, type: type)) return;

        _pending = type;
        _propPath = null;
        DestroyPropPreview();
        _editor.SetStatus($"Selected {label}.");
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

        ShowGroup(_groupKeys[index]);
    }

    private string _currentGroup = StructureGroup;

    private void ShowGroup(string group)
    {
        _currentGroup = group;

        if (group == StructureGroup) ShowStructureGroup();
        else if (group == CustomGroup) ShowCustomGroup();
        else if (ReferenceEquals(group, FavouritesGroup)) ShowFavouritesGroup();
        else if (ReferenceEquals(group, BossSceneryGroup)) ShowBossSceneryGroup();
        else ShowPropGroup(group);
    }

    // ---- the pinned group ----------------------------------------------------------------------

    private static readonly string FavouritesGroup = "\0favourites";

    private void ShowFavouritesGroup()
    {
        if (_grid == null) return;

        var pinned = MapEditorFavourites.Entries();
        if (pinned.Count == 0)
        {
            _grid.Clear();
            _editor.SetStatus("Nothing pinned yet - right-click anything in the browser to pin it.");
            return;
        }

        var entries = new List<MapEditorGrid.Entry>(pinned.Count);
        foreach (var entry in pinned)
        {
            if (!entry.IsProp)
            {
                entries.Add(StructureEntry(entry.Type, entry.Label));
                continue;
            }

            var path = entry.Path;
            var label = entry.Label;
            entries.Add(new MapEditorGrid.Entry
            {
                Id = path,
                Display = label,
                OnClick = () => SelectProp(path, label)
            });
        }

        // One group, both kinds: a structure's icon is a dictionary lookup and a prop's is a
        // background load, so each id is asked the way its own group would ask.
        MapEditorIcons.CancelPendingPropIcons();
        _grid.Populate(_editor, entries, id =>
        {
            if (_typesById.TryGetValue(id, out var type))
            {
                _grid.SetCellIcon(id, MapEditorIcons.GetStructureIcon(type));
                return;
            }

            MapEditorIcons.GetPropIcon(_editor, id, sprite => _grid?.SetCellIcon(id, sprite));
        });

        _editor.SetStatus($"{pinned.Count} pinned pick(s).");
    }

    // ---- pieces that only exist inside boss and special rooms ---------------------------------

    private static readonly string BossSceneryGroup = "\0bossscenery";

    private static List<(string label, string key)> _bossPieces;
    private static int _bossScanToken;

    private void ShowBossSceneryGroup()
    {
        if (_grid == null) return;

        if (_bossPieces != null)
        {
            Populate(_bossPieces);
            _editor.SetStatus($"{_bossPieces.Count} piece(s) from the boss and special rooms.");
            return;
        }

        _editor.StartCoroutine(ScanBossScenery(++_bossScanToken));
    }

    private void Populate(List<(string label, string key)> pieces)
    {
        var entries = new List<MapEditorGrid.Entry>(pieces.Count);
        foreach (var (label, key) in pieces)
        {
            var capturedKey = key;
            var capturedLabel = label;
            entries.Add(new MapEditorGrid.Entry
            {
                Id = capturedKey,
                Display = capturedLabel,
                OnClick = () => SelectProp(capturedKey, capturedLabel)
            });
        }

        MapEditorIcons.CancelPendingPropIcons();
        _grid.Populate(_editor, entries, id =>
            MapEditorIcons.GetPropIcon(_editor, id, sprite => _grid?.SetCellIcon(id, sprite)));
    }

    private IEnumerator ScanBossScenery(int token)
    {
        _grid.Clear();

        var rooms = EnemyTool.BossRoomKeys();
        rooms.AddRange(DeathRoomKeys());

        var found = new List<(string label, string key)>();

        for (var i = 0; i < rooms.Count; i++)
        {
            if (token != _bossScanToken) yield break;

            _editor.SetStatus($"Opening rooms - {i + 1} of {rooms.Count}...");

            GameObject room = null;
            yield return RoomSnapshot.LoadPrefabByKeyRoutine(rooms[i], p => room = p);
            if (token != _bossScanToken) yield break;
            if (room == null) continue;

            foreach (var piece in ExtractPieces(room, rooms[i]))
            {
                if (_grid.Has(piece.key)) continue;
                found.Add(piece);
                _grid.AddCell(piece.key, piece.label, null, () => SelectProp(piece.key, piece.label));
            }

            _editor.RequestOptionsResize();
        }

        _bossPieces = found;
        Populate(found);

        Plugin.Log.LogInfo($"MapEditor: boss and special rooms hold {found.Count} placeable piece(s) " +
                           $"across {rooms.Count} room(s).");

        _editor.SetStatus(found.Count > 0
            ? $"{found.Count} piece(s) from the boss and special rooms."
            : "No scenery pieces found in those rooms.",
            found.Count > 0 ? StatusSeverity.Info : StatusSeverity.Warning);
    }

    private const string DeathRoomPrefix = "Assets/Prefabs/Dungeon/Death Room/";

    private static List<string> DeathRoomKeys()
    {
        var keys = new List<string>();

        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator?.Keys == null) continue;
            foreach (var keyObj in locator.Keys)
            {
                if (keyObj is not string key || !key.EndsWith(".prefab")) continue;
                if (!key.StartsWith(DeathRoomPrefix)) continue;
                if (!keys.Contains(key)) keys.Add(key);
            }
        }

        keys.Sort(System.StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    /// Every drawn piece inside a room prefab, named by where it sits so it can be found again.
    private static List<(string label, string key)> ExtractPieces(GameObject room, string roomKey)
    {
        var results = new List<(string label, string key)>();
        var seen = new HashSet<string>();

        foreach (var renderer in room.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;

            var node = renderer.transform;
            if (node == room.transform) continue;
            if (node.GetComponentInParent<UnitObject>() != null) continue;
            if (node.name.StartsWith("Room Back Sprite")) continue;
            if (renderer is not SpriteRenderer && renderer is not MeshRenderer) continue;

            // A spine draws through a MeshRenderer; take the skeleton's own node, not the mesh's.
            var skeleton = node.GetComponentInParent<Spine.Unity.SkeletonRenderer>();
            if (skeleton != null) node = skeleton.transform;
            else if (renderer is MeshRenderer) continue;

            var path = RoomChildPrefabs.PathOf(node, room.transform);
            if (!seen.Add(path)) continue;

            results.Add((node.name, RoomChildPrefabs.KeyFor(roomKey, path)));
        }

        return results;
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

        foreach (var structure in CustomEntries())
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

        // Only once the group has been opened: scanning every boss room to answer a keystroke would
        // stall the search for as long as the rooms take to load.
        if (_bossPieces != null)
            foreach (var (label, key) in _bossPieces)
            {
                if (label.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                total++;
                if (entries.Count >= MapEditorSearchRow.MaxResults) continue;

                var capturedKey = key;
                var capturedLabel = label;
                entries.Add(new MapEditorGrid.Entry
                {
                    Id = capturedKey,
                    Display = capturedLabel,
                    OnClick = () => SelectProp(capturedKey, capturedLabel)
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

    private bool _breakApart = true;

    private static SortedDictionary<string, List<string>> _propGroups;

    private const string PropPrefix = "Assets/Prefabs/";

    private static readonly HashSet<string> ExcludedPropFolders =
        ["Enemies", "UI", "Fonts", "Audio", "Materials", "Shaders", "Player", "Followers"];

    // The DLC dungeons' dressing is not under Assets/Prefabs: Ewefall's pieces live in the
    // "Mountain" decoration plots and the Rot dungeon's in "Prison", both under Resources_moved. The
    // folder names are the game's; the labels are the dungeons a map maker knows them as.
    private const string DlcPlotPrefix = "Assets/Resources_moved/Dungeon/Decoration Plots/";

    private static readonly Dictionary<string, string> DlcPlotLabels = new()
    {
        ["Mountain"] = "Ewefall (Mountain)",
        ["Prison"] = "Rot (Prison)"
    };

    // A few more biome pieces (the cave biome, base weeds, boss and shop room dressing) sit under
    // Assets/Art. Its tooling, UI and shader folders are skipped.
    private const string ArtPrefix = "Assets/Art/";

    private static readonly HashSet<string> ExcludedArtFolders =
        ["UI", "Tools", "Materials", "Shaders", "AmplifyColorVolumes"];

    private const string TilePrefix = "Assets/Tile Decorations/";

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
                if (keyObj is not string key || !key.EndsWith(".prefab")) continue;

                var group = GroupOfKey(key);
                if (group == null) continue;

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

    /// <summary>
    /// The browser group an addressable prefab key belongs in, or null for keys the browser does
    /// not list (rooms, island pieces, UI, enemies and the like).
    /// </summary>
    private static string GroupOfKey(string key)
    {
        if (key.StartsWith(PropPrefix))
        {
            var relative = key.Substring(PropPrefix.Length);
            var slash = relative.IndexOf('/');
            if (slash <= 0) return null;

            var top = relative.Substring(0, slash);
            if (ExcludedPropFolders.Contains(top)) return null;

            var rest = relative.Substring(slash + 1);
            var nextSlash = rest.IndexOf('/');
            if (nextSlash <= 0) return top;

            // "Placement Objects / DLC" and "VFX / DLC" are grab-bags of the DLC's cult decorations
            // and effects; now that the DLC dungeons have groups of their own, the folder's name
            // would only suggest those. They are listed as Misc.
            var second = rest.Substring(0, nextSlash);
            if (second == "DLC") second = "Misc";
            return top + " / " + second;
        }

        if (key.StartsWith(DlcPlotPrefix))
        {
            var relative = key.Substring(DlcPlotPrefix.Length);
            var slash = relative.IndexOf('/');
            if (slash <= 0) return null;

            var biome = relative.Substring(0, slash);
            if (!DlcPlotLabels.TryGetValue(biome, out var label)) label = biome;

            // The files straight under the biome folder are whole plots (a 2x2 of cages, a 3x3 of
            // ruins); the individual pieces are one folder down.
            var rest = relative.Substring(slash + 1);
            return rest.IndexOf('/') > 0
                ? "DLC Dungeon / " + label
                : "DLC Dungeon / " + label + " Plots";
        }

        if (key.StartsWith(ArtPrefix))
        {
            var parts = key.Substring(ArtPrefix.Length).Split('/');
            if (parts.Length < 2 || ExcludedArtFolders.Contains(parts[0])) return null;

            // Up to two folders deep, so "Sprite / Dungeon" and "Sprite / Base" stay apart.
            return parts.Length >= 3
                ? "Art / " + parts[0] + " / " + parts[1]
                : "Art / " + parts[0];
        }

        if (key.StartsWith(TilePrefix)) return "Tile Decorations";

        return null;
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

        NotePlacedProp(_propPath);
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

    private static Transform PropParent() =>
        SceneRefs.Room != null && SceneRefs.Room.SceneryTransform != null
            ? SceneRefs.Room.SceneryTransform.transform
            : SceneRefs.ContentRoot;

    private void SpawnProp(string path, Vector3 position, bool isPreview)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (RoomChildPrefabs.IsRoomKey(path))
        {
            _editor.StartCoroutine(SpawnLiftedProp(path, position, isPreview));
            return;
        }

        var parent = PropParent();

        try
        {
            ObjectPool.Spawn(path, position, Quaternion.identity, parent, go =>
            {
                if (isPreview) _propPreviewPending = false;
                if (go == null) return;

                go.transform.position = position;

                if (!isPreview)
                {
                    var label = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (BreakApartPlaced(go, path, parent, label)) return;

                    // Whichever way it went, no picker gets to run: this happens in the spawn
                    // callback, before the first Update, so the prefab stays as it was authored
                    // rather than as this one spawn happened to roll it.
                    PropRandomisers.Freeze(go);
                    PropBrains.Adopt(go);

                    _placedProps.Add(go);
                    _editor.History.Push($"place {label}", () =>
                    {
                        if (!_placedProps.Remove(go) || go == null) return false;
                        Object.Destroy(go);
                        return true;
                    });
                    return;
                }

                PropRandomisers.Freeze(go);

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

    /// A piece that lives inside another prefab: the prefab is loaded, then the piece is copied out.
    private IEnumerator SpawnLiftedProp(string key, Vector3 position, bool isPreview)
    {
        GameObject source = null;
        yield return RoomChildPrefabs.ResolveRoutine(key, go => source = go);

        var label = RoomChildPrefabs.Label(key) ?? key;
        if (isPreview) _propPreviewPending = false;

        if (source == null)
        {
            Plugin.Log.LogWarning($"MapEditor: '{key}' could not be read out of its prefab.");
            if (!isPreview) _editor.SetStatus($"'{label}' could not be placed.", StatusSeverity.Error);
            yield break;
        }

        var go = RoomChildPrefabs.Lift(source, PropParent(), key);
        if (go == null) yield break;
        go.transform.position = position;

        if (isPreview)
        {
            if (_propPath != _propPreviewPath) { Object.Destroy(go); yield break; }
            Fade(go, 0.6f);
            _propPreview = go;
            yield break;
        }

        PropBrains.Adopt(go);

        _placedProps.Add(go);
        _editor.History.Push($"place {label}", () =>
        {
            if (!_placedProps.Remove(go) || go == null) return false;
            Object.Destroy(go);
            return true;
        });

        _editor.SetStatus($"Placed {label}.");
    }

    // ---- quick pick ---------------------------------------------------------------------------

    private void WireQuickPick()
    {
        var bar = _editor.QuickPick;
        if (bar == null) return;

        bar.OnPick = ArmQuickPick;
        bar.Icon = QuickPickIcon;
        bar.IsArmed = entry => entry.IsProp
            ? _propPath == entry.Path
            : _pending == entry.Type;

        bar.Favourites = MapEditorFavourites.Entries;
        bar.IsFavourite = entry => MapEditorFavourites.IsFavourite(entry?.Key);
        bar.OnToggleFavourite = ToggleFavourite;
    }

    // ---- favourites ---------------------------------------------------------------------------

    /// Right-click, from a browser cell or from a slot on the bar. Pinned things hold a slot for
    /// good; unpinning drops them back to being whatever the recent list says they are.
    private void ToggleFavourite(QuickPickEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Key)) return;

        var pinned = MapEditorFavourites.Toggle(entry);

        var bar = _editor.QuickPick;
        bar?.Refresh();

        // The pinned group IS the favourites, so a change there adds or removes a cell rather than
        // only its star.
        if (ReferenceEquals(_currentGroup, FavouritesGroup)) ShowFavouritesGroup();
        else _grid?.RefreshFavourites();

        if (!pinned)
        {
            _editor.SetStatus($"Unpinned {entry.Label}.");
            return;
        }

        _editor.SetStatus(bar != null && bar.Visible
            ? $"Pinned {entry.Label} to the quick pick."
            : $"Pinned {entry.Label} - turn on the structure quick pick dock to reach it.");
    }

    private void RightClickCell(string id, string display)
    {
        var entry = EntryForCell(id, display);
        if (entry == null) return;

        ToggleFavourite(entry);
    }

    /// A browser cell's id in the form the quick pick speaks. Structure cells are "type:<TYPES>" and
    /// carry their type in _typesById; everything else in this tool's grid is a prop path.
    private QuickPickEntry EntryForCell(string id, string display)
    {
        if (string.IsNullOrEmpty(id) || id == TotemId) return null;

        if (id.StartsWith(TypeCellPrefix, System.StringComparison.Ordinal))
        {
            if (!_typesById.TryGetValue(id, out var type)) return null;

            var internalName = InternalNameOf(type);
            return new QuickPickEntry
            {
                Key = "structure:" + internalName,
                Label = ShortName(internalName),
                Type = type,
                IsCustom = CustomStructureManager.CustomStructureList.ContainsKey(type)
            };
        }

        return new QuickPickEntry
        {
            Key = "prop:" + id,
            Label = display ?? System.IO.Path.GetFileNameWithoutExtension(id),
            IsProp = true,
            Path = id
        };
    }

    /// Choosing a slot arms it exactly as clicking its cell in the browser would - the bar is a
    /// shortcut into this tool, never a second way of placing things.
    private void ArmQuickPick(QuickPickEntry entry)
    {
        if (entry == null) return;

        if (!ReferenceEquals(_editor.ActiveTool, this)) _editor.ActivateTool(this);

        if (entry.IsProp) SelectProp(entry.Path, entry.Label);
        else SelectStructure(entry.Type, entry.Label);
    }

    private readonly Dictionary<string, Sprite> _quickPickIcons = [];
    private readonly HashSet<string> _quickPickAsked = [];

    /// <summary>
    /// A prop icon is loaded from its prefab through a throttled queue, so it is never ready on the
    /// frame it is asked for. The bar used to throw the callback away and show the entry's name
    /// instead, forever. The answer is cached here as it arrives, and the bar picks it up on its
    /// next refresh, the same way a browser cell fills in.
    /// </summary>
    private Sprite QuickPickIcon(QuickPickEntry entry)
    {
        if (entry == null) return null;

        if (!entry.IsProp) return MapEditorIcons.GetStructureIcon(entry.Type);

        var path = entry.Path;
        if (string.IsNullOrEmpty(path)) return null;

        if (_quickPickIcons.TryGetValue(path, out var cached)) return cached;

        if (_quickPickAsked.Add(path))
            MapEditorIcons.GetPropIcon(_editor, path, sprite =>
            {
                _quickPickIcons[path] = sprite;
                if (sprite != null) _editor.QuickPick?.Refresh();
            });

        return null;
    }

    private void NoteQuickPick(QuickPickEntry entry)
    {
        var bar = _editor.QuickPick;
        if (bar == null || entry == null) return;

        bar.Note(entry);
    }

    private void NotePlacedStructure(StructureBrain.TYPES type)
    {
        var label = InternalNameOf(type);
        NoteQuickPick(new QuickPickEntry
        {
            Key = "structure:" + label,
            Label = ShortName(label),
            Type = type,
            IsCustom = CustomStructureManager.CustomStructureList.ContainsKey(type)
        });
    }

    private void NotePlacedProp(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        var label = RoomChildPrefabs.IsRoomKey(path)
            ? RoomChildPrefabs.Label(path) ?? path
            : System.IO.Path.GetFileNameWithoutExtension(path);

        NoteQuickPick(new QuickPickEntry
        {
            Key = "prop:" + path,
            Label = label,
            IsProp = true,
            Path = path
        });
    }

    /// True when the prefab was a randomised set and has been replaced by its pieces.
    private bool BreakApartPlaced(GameObject go, string path, Transform parent, string label)
    {
        if (!_breakApart || !PropRandomisers.IsRandomisedSet(go)) return false;

        var pieces = PropRandomisers.BreakApart(go, path, parent);
        if (pieces == null || pieces.Count == 0) return false;

        foreach (var piece in pieces) PropBrains.Adopt(piece);

        _placedProps.AddRange(pieces);
        _editor.History.Push($"place {label} ({pieces.Count} pieces)", () =>
        {
            var removed = 0;
            foreach (var piece in pieces)
            {
                if (piece == null) continue;
                _placedProps.Remove(piece);
                Object.Destroy(piece);
                removed++;
            }
            return removed > 0;
        });

        _editor.SetStatus($"{label} placed as {pieces.Count} separate piece(s) - move or delete each one.");
        return true;
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
        ("LMB", "Place"),
        ("RMB", "Pin / unpin"),
        ("1-9", "Arm slot"),
        ("Wheel", "Step slot")
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

    public bool IsWind(GameObject go)
    {
        var index = IndexOfPlaced(go);
        return index >= 0 && _placed[index].Wind;
    }

    public bool TrySetSeeThrough(GameObject go, bool player, bool fog, bool wind = false)
    {
        var index = IndexOfPlaced(go);
        if (index < 0) return false;

        var placed = _placed[index];
        if (placed.Instance == null) return false;

        SeeThrough.Set(placed.Instance, player, fog, wind);

        placed.SeeThrough = SeeThrough.IsPlayerThrough(placed.Instance);
        placed.FogThrough = SeeThrough.IsFogThrough(placed.Instance);
        placed.Wind = SeeThrough.IsWind(placed.Instance);
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
                FogThrough = placed.FogThrough,
                Wind = placed.Wind
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
        NotePlacedStructure(type);
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
        PlaceAt(type, isCustom, position, rotation, flipX, deferNav, _placeSeeThrough,
            _placeFogThrough, _placeWind);

    public IEnumerator PlaceAt(StructureBrain.TYPES type, bool isCustom, Vector3 position,
        float rotation, bool flipX, bool deferNav, bool seeThrough, bool fogThrough,
        bool wind = false)
    {
        if (!Begin(type, isCustom, position, rotation, flipX, seeThrough, fogThrough, wind,
                out var placement)) yield break;

        while (!placement.Handle.IsDone) yield return null;

        Finish(ref placement, deferNav);
        if (!placement.Relook) yield break;

        yield return null;
        Relook(placement);
    }

    /// Loads a whole file's structures at once and settles them together. One at a time was the
    /// cost that showed on a map with many of them; see the notes in MapEditor/README.md.
    public IEnumerator PlaceMany(IList<MapStructureData> records,
        System.Action<MapStructureData, GameObject> onPlaced)
    {
        if (records == null || records.Count == 0) yield break;

        var owners = new List<MapStructureData>(records.Count);
        var started = new List<Placement>(records.Count);

        foreach (var record in records)
        {
            if (record == null) continue;

            if (!TryResolveType(record.TypeName, record.IsCustom, out var type))
            {
                Plugin.Log.LogWarning($"MapEditor: structure '{record.TypeName}' could not be resolved, skipped.");
                continue;
            }

            if (!Begin(type, record.IsCustom, MapEditorSerialization.ToVector3(record.Position),
                    record.Rotation, record.FlipX, record.SeeThrough, record.FogThrough, record.Wind,
                    out var placement)) continue;

            owners.Add(record);
            started.Add(placement);
        }

        if (started.Count == 0) yield break;

        while (true)
        {
            var waiting = false;
            foreach (var placement in started)
                if (!placement.Handle.IsDone) { waiting = true; break; }

            if (!waiting) break;
            yield return null;
        }

        var relooks = new List<Placement>();

        for (var i = 0; i < started.Count; i++)
        {
            var placement = started[i];
            var go = Finish(ref placement, deferNav: true, quiet: true);
            if (placement.Relook) relooks.Add(placement);
            onPlaced?.Invoke(owners[i], go);
        }

        if (relooks.Count == 0) yield break;

        yield return null;
        foreach (var placement in relooks) Relook(placement);
    }

    /// A structure whose prefab is on its way. Addressables never lands an instantiate in the frame
    /// it was asked for, so a batch starts every load before waiting on any of them.
    private struct Placement
    {
        public AsyncOperationHandle<GameObject> Handle;
        public string PrefabPath;
        public StructureBrain.TYPES Type;
        public bool IsCustom;
        public Vector3 Position;
        public float Rotation;
        public bool FlipX;
        public bool SeeThrough;
        public bool FogThrough;
        public bool Wind;

        public GameObject Instance;
        public bool Relook;
    }

    private bool Begin(StructureBrain.TYPES type, bool isCustom, Vector3 position, float rotation,
        bool flipX, bool seeThrough, bool fogThrough, bool wind, out Placement placement)
    {
        placement = default;

        var root = SceneRefs.ContentRoot;
        if (root == null)
        {
            _editor.SetStatus("No room content root.", StatusSeverity.Error);
            return false;
        }

        var prefabPath = ResolvePrefabPath(type, isCustom);
        if (string.IsNullOrEmpty(prefabPath))
        {
            _editor.SetStatus($"{type} has no prefab path.", StatusSeverity.Error);
            return false;
        }

        try
        {
            placement.Handle = Addressables.InstantiateAsync(prefabPath, root, false);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not instantiate {type} ({prefabPath}): {e.Message}");
            _editor.SetStatus($"Failed to place {type}.", StatusSeverity.Error);
            return false;
        }

        placement.PrefabPath = prefabPath;
        placement.Type = type;
        placement.IsCustom = isCustom;
        placement.Position = position;
        placement.Rotation = rotation;
        placement.FlipX = flipX;
        placement.SeeThrough = seeThrough;
        placement.FogThrough = fogThrough;
        placement.Wind = wind;
        return true;
    }

    private GameObject Finish(ref Placement placement, bool deferNav, bool quiet = false)
    {
        var type = placement.Type;

        if (placement.Handle.Status != AsyncOperationStatus.Succeeded || placement.Handle.Result == null)
        {
            Plugin.Log.LogWarning($"MapEditor: addressable load failed for {type} ({placement.PrefabPath}).");
            _editor.SetStatus($"Failed to load {type}.", StatusSeverity.Error);
            return null;
        }

        var go = placement.Handle.Result;
        go.transform.position = placement.Position;
        go.name = $"CultTweaker_Placed_{type}";

        if (placement.IsCustom)
        {
            StructureSpineHelper.TryAttach(go, type);
            APIHelper.StructureShadows.TryEnable(go, type);
        }

        if (Mathf.Abs(placement.Rotation) > 0.001f)
            go.transform.eulerAngles = new Vector3(0f, placement.Rotation, 0f);
        if (placement.FlipX)
        {
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(-s.x, s.y, s.z);
        }

        var dressed = placement.SeeThrough || placement.FogThrough || placement.Wind;
        if (dressed) SeeThrough.Set(go, placement.SeeThrough, placement.FogThrough, placement.Wind);

        BaseDelta.GiveBrain(go, type, placement.Position);

        // Sprites that arrive a frame late - a brain's own, and the art a Structure.Start postfix
        // swaps into an overridden building - have to be dressed again. A batch waits once for all
        // of them rather than once each.
        placement.Instance = go;
        placement.Relook = dressed;

        var placed = new PlacedStructure
        {
            Type = type,
            IsCustom = placement.IsCustom,
            Instance = go,
            Rotation = placement.Rotation,
            FlipX = placement.FlipX,
            SeeThrough = placement.SeeThrough,
            FogThrough = placement.FogThrough,
            Wind = SeeThrough.IsWind(go)
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
        if (!quiet) _editor.SetStatus($"Placed {type}.");
        return go;
    }

    private static void Relook(Placement placement)
    {
        if (placement.Instance == null) return;
        SeeThrough.Set(placement.Instance, placement.SeeThrough, placement.FogThrough, placement.Wind);
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
                Id = Net.EditorIds.Of(placed.Instance),
                TypeName = placed.IsCustom ? CustomInternalName(placed.Type) : placed.Type.ToString(),
                IsCustom = placed.IsCustom,
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                Rotation = placed.Rotation,
                FlipX = placed.FlipX,
                SeeThrough = placed.SeeThrough,
                FogThrough = placed.FogThrough,
                Wind = placed.Wind,

                Scale = MapEditorSerialization.V3(Abs(placed.Instance.transform.lossyScale))
            });
        }
    }

    private static string CustomInternalName(StructureBrain.TYPES type) => InternalNameOf(type);

    /// A custom structure's internal name with the registry prefix taken off, for lists narrow
    /// enough that the prefix would be all the reader ever sees. Never written to a file.
    public static string ShortName(string internalName)
    {
        if (string.IsNullOrEmpty(internalName)) return internalName;
        if (!internalName.StartsWith(CustomPrefix, System.StringComparison.Ordinal)) return internalName;

        var body = internalName.Substring(CustomPrefix.Length).Replace('_', ' ').Trim();
        if (body.Length == 0) return internalName;

        // The registry upper-cased whatever the author typed; sentence case is the closest thing
        // to it that reads at a glance.
        var words = body.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0) continue;
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1).ToLowerInvariant();
        }

        return string.Join(" ", words);
    }

    private const string CustomPrefix = "CULT_TWEAKER_STRUCTURE_";

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
