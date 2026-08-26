using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COTL_API.CustomEnemy;
using HarmonyLib;
using Spine.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CustomSpineLoader.MapEditor.Tools;

public class EnemyTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts
{
    public string Name => "Enemies";

    // Three address spaces: the last two are pre-Addressables leftovers holding 103 prefabs.
    private static readonly string[] VanillaPrefixes =
        ["Assets/Prefabs/Enemies/", "Assets/Resources_moved/Enemies/", "Enemies/"];

    private static readonly string[] ExcludedFolders = ["Dead Bodies", "Weapons"];

    // Boss prefabs are named for what the boss is, not who; only the bishops need aliasing.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["Enemy Forest Worm Boss"] = "Leshy (Worm Boss)",
        ["Enemy Forest Frog Boss"] = "Heket (Frog Boss)",
        ["Enemy Jellyfish Boss"] = "Kallamar (Jelly Boss)"
    };

    private readonly RuntimeMapEditor _editor;
    private readonly List<PlacedEnemy> _placed = [];

    // Group name -> list of (label, addressable key). Custom enemies use a synthetic group.
    private static SortedDictionary<string, List<(string label, string key)>> _catalog;

    private string _pendingKey;
    private bool _pendingIsCustom;
    private string _pendingLabel;

    private GameObject _preview;
    private string _previewKey;

    private class PlacedEnemy
    {
        public string Key;
        public bool IsCustom;
        public GameObject Instance;
    }

    public EnemyTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _search = new MapEditorSearchRow(_editor, ui, panel, ShowSearchResults, ShowCurrentGroup);

        _groupKeys.Clear();
        var options = new List<string>();
        foreach (var group in Catalog().Keys)
        {
            _groupKeys.Add(group);
            options.Add($"{group} ({Catalog()[group].Count})");
        }

        // Opened rather than scanned, so it goes last and says so: picking it reads room prefabs
        // off disk, which the other groups never do.
        _groupKeys.Add(BossGroupKey);
        options.Add("Bosses (inside rooms)");

        // Not cached: mods register enemies at their own pace, so this group is read live.
        _groupKeys.Add(null);
        options.Add("Custom (mods)");

        _groupDropdown = ui.CreateDropdown(panel, "Choose a group", options, (index, _) => ShowGroupAt(index));

        _grid = ui.CreateIconGrid(panel, "EnemyGrid", scrollHeight: GridHeight);

        ui.CreateButton(panel, "Clear Selection", () =>
        {
            _pendingKey = null;
            DestroyPreview();
            _grid?.SetSelected(null);
            _editor.SetStatus("Selection cleared.");
        });
    }

    private MapEditorDropdown _groupDropdown;
    private MapEditorSearchRow _search;

    // ---- search -------------------------------------------------------------------------------

    // Back to whichever group the dropdown is on, for a cleared or cancelled search.
    private void ShowCurrentGroup()
    {
        var index = _groupDropdown != null ? _groupDropdown.SelectedIndex : -1;
        ShowGroupAt(index < 0 ? 0 : index);
    }

    // Every group at once, plus whatever mods have registered: the catalog is filed by biome, and
    // the enemy you want is rarely in the biome you happen to be standing in.
    private void ShowSearchResults(string needle)
    {
        if (_grid == null) return;

        var list = new List<MapEditorGrid.Entry>();
        var seen = new HashSet<string>();
        var total = 0;

        foreach (var group in Catalog())
        foreach (var entry in group.Value)
        {
            if (entry.label.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!seen.Add(entry.key)) continue;

            total++;
            if (list.Count < MapEditorSearchRow.MaxResults)
                list.Add(CellFor(entry.label, entry.key, isCustom: false));
        }

        // Boss-room characters, but only once the group has been opened - searching cannot open two
        // dozen room prefabs on a keystroke, so until then they are simply not there to find.
        if (_bossEntries != null)
            foreach (var entry in _bossEntries)
            {
                if (entry.label.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!seen.Add(entry.key)) continue;

                total++;
                if (list.Count < MapEditorSearchRow.MaxResults)
                    list.Add(CellFor(entry.label, entry.key, isCustom: false));
            }

        foreach (var pair in CustomEnemies())
        {
            if (pair.Value == null) continue;

            var name = pair.Value.InternalName;
            if (name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!seen.Add(name)) continue;

            total++;
            if (list.Count < MapEditorSearchRow.MaxResults) list.Add(CellFor(name, name, isCustom: true));
        }

        Populate(list);
        _search.ReportCount(list.Count, total, needle);
    }

    private void ShowGroupAt(int index)
    {
        if (index < 0 || index >= _groupKeys.Count) return;

        var group = _groupKeys[index];
        if (group == null) ShowCustomGroup();
        else if (ReferenceEquals(group, BossGroupKey)) ShowBossGroup();
        else ShowGroup(group);
    }

    // ---- bosses that live inside rooms ---------------------------------------------------------

    // A sentinel, compared by reference, so it can never collide with a real biome folder name.
    private static readonly string BossGroupKey = "\0bosses";

    // The rooms worth opening. Every boss and miniboss arena, plus Narinder's two - which is where
    // the Guardians and the Death Cat itself are, none of them addressable in their own right.
    // Restricted to the top level of _Rooms: the BossRoomAssets folder beside it is scenery.
    private static readonly string[] BossRoomNames =
        ["Boss Room Dungeon", "MiniBoss Room Dungeon", "Death Cat Room", "Special Baal & Aym"];

    private const string RoomPrefix = "Assets/_Rooms/";

    private static List<string> BossRoomKeys()
    {
        var keys = new List<string>();

        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator?.Keys == null) continue;

            foreach (var keyObj in locator.Keys)
            {
                if (keyObj is not string key || !key.EndsWith(".prefab")) continue;
                if (!key.StartsWith(RoomPrefix)) continue;

                // One segment only - "Assets/_Rooms/X.prefab", never "Assets/_Rooms/Sub/X.prefab".
                var relative = key.Substring(RoomPrefix.Length);
                if (relative.IndexOf('/') >= 0) continue;

                var name = Path.GetFileNameWithoutExtension(key);
                if (!BossRoomNames.Any(n => name.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                if (!keys.Contains(key)) keys.Add(key);
            }
        }

        keys.Sort(System.StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    // Opened once per session. A room prefab is a heavy thing to load and there are a couple of
    // dozen of them, so the answer is kept rather than the rooms re-read every time the group is
    // picked.
    private static List<(string label, string key)> _bossEntries;
    private static int _bossScanToken;

    private void ShowBossGroup()
    {
        if (_grid == null) return;

        if (_bossEntries != null)
        {
            var list = new List<MapEditorGrid.Entry>(_bossEntries.Count);
            foreach (var entry in _bossEntries) list.Add(CellFor(entry.label, entry.key, isCustom: false));
            Populate(list);
            _editor.SetStatus($"{_bossEntries.Count} boss-room character(s).");
            return;
        }

        _editor.StartCoroutine(ScanBossRooms(++_bossScanToken));
    }

    // The NPC tool's room scan, pointed at arenas and looking for enemies instead of NPCs. Rooms are
    // opened one at a time with the grid filling in behind, because loading two dozen room prefabs
    // in a frame is a freeze and there is nothing to look at until the first one lands anyway.
    private IEnumerator ScanBossRooms(int token)
    {
        _grid.Clear();
        EnemyThumbnails.CancelPending();

        var rooms = BossRoomKeys();
        var found = new List<(string label, string key)>();

        for (var i = 0; i < rooms.Count; i++)
        {
            if (token != _bossScanToken) yield break;

            _editor.SetStatus($"Opening boss rooms - {i + 1} of {rooms.Count}...");

            GameObject room = null;
            yield return RoomSnapshot.LoadPrefabByKeyRoutine(rooms[i], p => room = p);
            if (token != _bossScanToken) yield break;
            if (room == null) continue;

            foreach (var entry in ExtractEnemies(room, rooms[i]))
            {
                if (_grid.Has(entry.key)) continue;

                found.Add(entry);
                _grid.AddCell(entry.key, entry.label, null, CellFor(entry.label, entry.key, isCustom: false).OnClick);
            }

            _editor.RequestOptionsResize();
        }

        _bossEntries = found;

        // Icons last: the thumbnail rig stages each subject and photographs it, and doing that while
        // rooms are still being opened is two heavy loads competing.
        Populate([.. found.Select(e => CellFor(e.label, e.key, isCustom: false))]);

        Plugin.Log.LogInfo($"MapEditor: boss rooms hold {found.Count} placeable character(s) " +
                           $"across {rooms.Count} room(s).");

        _editor.SetStatus(found.Count > 0
            ? $"{found.Count} boss-room character(s)."
            : "No placeable characters found in the boss rooms.", found.Count > 0
            ? StatusSeverity.Info
            : StatusSeverity.Warning);
    }

    // What counts as an enemy inside a room.
    //
    // Deliberately NOT the NPC tool's test, which asks for conversation and shop components: a boss
    // has none of those. It has a UnitObject and a Health, which is what makes it a thing that
    // fights - and the same pair is what the enemy prefabs under Assets/Prefabs/Enemies carry.
    private static List<(string label, string key)> ExtractEnemies(GameObject room, string roomKey)
    {
        var results = new List<(string label, string key)>();
        var seen = new HashSet<Transform>();

        foreach (var unit in room.GetComponentsInChildren<UnitObject>(true))
        {
            if (unit == null || unit.transform == room.transform) continue;

            // The UnitObject's own node, not a parent: a boss controller holds several of these
            // (Narinder, two Guardians, the eyes) and climbing above one would take them all as a
            // single lump.
            var root = unit.transform;
            if (!seen.Add(root)) continue;

            // Something has to draw, or the entry is an invisible marker.
            if (root.GetComponentInChildren<SkeletonAnimation>(true) == null) continue;

            results.Add((root.name, RoomChildPrefabs.KeyFor(roomKey, RoomChildPrefabs.PathOf(root, room.transform))));
        }

        return results;
    }

    private readonly List<string> _groupKeys = [];
    // Tall enough to browse in, short enough that the search field and the group picker
    // above it never leave the screen.
    private const float GridHeight = 620f;

    private MapEditorGrid _grid;

    public void OnEnter()
    {
        if (_grid != null && _groupDropdown != null && _groupDropdown.SelectedIndex < 0)
        {
            _groupDropdown.SetSelected(0);
            ShowGroupAt(0);
        }

        _editor.SetStatus("Pick a group, then an enemy.");
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Place selected enemy")
    ];

    public void OnExit() => DestroyPreview();

    public void OnUpdate()
    {
        if (string.IsNullOrEmpty(_pendingKey)) return;

        UpdatePreviewPosition();

        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;

        var world = _editor.MouseWorld();
        _editor.StartCoroutine(SpawnEnemyRoutine(_pendingKey, _pendingIsCustom, world, withVfx: false));
    }

    // The room snapshot skips objects this tool already serializes.
    public bool IsTracked(GameObject go)
    {
        foreach (var placed in _placed)
            if (placed.Instance == go) return true;
        return false;
    }

    public void ResetTracking() => _placed.Clear();

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

        _placed.Clear();
        return removed;
    }

    public IEnumerator SpawnEnemyRoutine(string key, bool isCustom, Vector3 position, bool withVfx)
    {
        if (isCustom)
        {
            SpawnCustom(key, position);
            yield break;
        }

        if (RoomChildPrefabs.IsRoomKey(key))
        {
            yield return SpawnRoomChild(key, position);
            yield break;
        }

        var parent = SceneRefs.ContentRoot;
        AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.InstantiateAsync(key, parent, false);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not instantiate enemy '{key}': {e.Message}");
            yield break;
        }

        while (!handle.IsDone) yield return null;

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Plugin.Log.LogWarning($"MapEditor: enemy load failed for '{key}'.");
            yield break;
        }

        var go = handle.Result;
        go.transform.position = position;

        if (withVfx)
        {
            try
            {
                EnemySpawner.CreateWithAndInitInstantiatedEnemy(position, parent, go);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: enemy spawn VFX failed, enemy placed directly: " + e.Message);
            }
        }

        go.AddComponent<EnemyContainment>();

        var placed = new PlacedEnemy { Key = key, IsCustom = false, Instance = go };
        _placed.Add(placed);
        PushUndo(placed, Path.GetFileNameWithoutExtension(key));
        _editor.SetStatus($"Placed {Path.GetFileNameWithoutExtension(key)}.");
    }

    // A boss lifted out of its arena.
    //
    // Instantiate on the prefab CHILD, not Addressables on the room: Unity hands back that subtree
    // alone, so the boss arrives without the room it was standing in. It also arrives without the
    // ritual that would normally start it - a placed boss stands where it is put rather than
    // playing its intro, which is what an editor wants and worth knowing before wondering why it
    // is not attacking.
    private IEnumerator SpawnRoomChild(string key, Vector3 position)
    {
        GameObject source = null;
        yield return RoomChildPrefabs.ResolveRoutine(key, p => source = p);

        var label = RoomChildPrefabs.Label(key) ?? key;

        if (source == null)
        {
            Plugin.Log.LogWarning($"MapEditor: '{key}' could not be found in its room prefab.");
            _editor.SetStatus($"'{label}' could not be read out of its room.", StatusSeverity.Error);
            yield break;
        }

        GameObject go;
        try
        {
            go = Object.Instantiate(source, SceneRefs.ContentRoot);
            go.SetActive(true);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: '{key}' failed to instantiate: {e.Message}");
            _editor.SetStatus($"'{label}' could not be placed.", StatusSeverity.Error);
            yield break;
        }

        go.transform.position = position;
        go.AddComponent<EnemyContainment>();

        var placed = new PlacedEnemy { Key = key, IsCustom = false, Instance = go };
        _placed.Add(placed);
        PushUndo(placed, label);
        _editor.SetStatus($"Placed {label}.");
    }

    private void SpawnCustom(string internalName, Vector3 position)
    {
        foreach (var pair in CustomEnemies())
        {
            if (pair.Value == null || pair.Value.InternalName != internalName) continue;

            var unit = CustomEnemyManager.Spawn(pair.Key, position);
            if (unit == null)
            {
                Plugin.Log.LogWarning($"MapEditor: CustomEnemyManager.Spawn returned null for '{internalName}'.");
                return;
            }

            unit.gameObject.AddComponent<EnemyContainment>();

            var placed = new PlacedEnemy { Key = internalName, IsCustom = true, Instance = unit.gameObject };
            _placed.Add(placed);
            PushUndo(placed, internalName);
            _editor.SetStatus($"Placed {internalName}.");
            return;
        }

        Plugin.Log.LogWarning($"MapEditor: custom enemy '{internalName}' is not registered (mod missing?), skipped.");
    }

    private void PushUndo(PlacedEnemy placed, string label)
    {
        _editor.History.Push($"place {label}", () =>
        {
            if (!_placed.Remove(placed) || placed.Instance == null) return false;
            Object.Destroy(placed.Instance);
            return true;
        });
    }

    // ---- picker -----------------------------------------------------------------------------

    private void ShowGroup(string group)
    {
        if (_grid == null || !Catalog().TryGetValue(group, out var entries)) return;

        var list = new List<MapEditorGrid.Entry>(entries.Count);
        foreach (var entry in entries) list.Add(CellFor(entry.label, entry.key, isCustom: false));
        Populate(list);
    }

    private void ShowCustomGroup()
    {
        if (_grid == null) return;

        var list = new List<MapEditorGrid.Entry>();
        foreach (var pair in CustomEnemies())
        {
            if (pair.Value == null) continue;
            list.Add(CellFor(pair.Value.InternalName, pair.Value.InternalName, isCustom: true));
        }

        Populate(list);
        if (list.Count == 0) _editor.SetStatus("No custom enemies registered.", StatusSeverity.Warning);
    }

    private void Populate(IList<MapEditorGrid.Entry> entries)
    {
        EnemyThumbnails.CancelPending();
        _grid.Populate(_editor, entries, id =>
        {
            var isCustom = _customIds.Contains(id);
            EnemyThumbnails.Request(_editor, id, isCustom, sprite => _grid?.SetCellIcon(id, sprite));
        });
    }

    private readonly HashSet<string> _customIds = [];

    private MapEditorGrid.Entry CellFor(string label, string key, bool isCustom)
    {
        if (isCustom) _customIds.Add(key);

        return new MapEditorGrid.Entry
        {
            Id = key,
            Display = label,
            OnClick = () =>
            {
                // Picking one is the end of the search; the status set below is what should be
                // left on screen, so this goes first.
                _search?.Confirm();

                _pendingKey = key;
                _pendingIsCustom = isCustom;
                _pendingLabel = label;
                DestroyPreview();
                _editor.SetStatus($"Selected {label}.");
            }
        };
    }

    private static SortedDictionary<string, List<(string label, string key)>> Catalog()
    {
        if (_catalog != null) return _catalog;

        _catalog = [];
        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator?.Keys == null) continue;
            foreach (var keyObj in locator.Keys)
            {
                if (keyObj is not string key || !key.EndsWith(".prefab")) continue;

                var prefix = VanillaPrefixes.FirstOrDefault(key.StartsWith);
                if (prefix == null) continue;

                var relative = key.Substring(prefix.Length);
                var slash = relative.IndexOf('/');
                var group = slash > 0 ? relative.Substring(0, slash) : "Misc";
                if (ExcludedFolders.Contains(group)) continue;

                if (!_catalog.TryGetValue(group, out var list))
                    _catalog[group] = list = [];

                var label = Path.GetFileNameWithoutExtension(key);
                if (Aliases.TryGetValue(label, out var alias)) label = alias;
                if (!list.Any(e => e.Item2 == key)) list.Add((label, key));
            }
        }

        foreach (var list in _catalog.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));

        Plugin.Log.LogInfo($"MapEditor: enemy catalog holds {_catalog.Sum(g => g.Value.Count)} entries " +
                           $"in {_catalog.Count} group(s).");
        return _catalog;
    }

    // Internal to COTL_API, read via Traverse; COTL_API mutates the dictionary in place, so one
    // read serves the session.
    private static Dictionary<Enemy, CustomEnemy> _customEnemies;

    private static Dictionary<Enemy, CustomEnemy> CustomEnemies()
    {
        if (_customEnemies != null) return _customEnemies;

        try
        {
            var dict = Traverse.Create(typeof(CustomEnemyManager))
                .Property("CustomEnemyList")
                .GetValue<Dictionary<Enemy, CustomEnemy>>();
            return _customEnemies = dict ?? [];
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not read COTL_API custom enemy list: " + e.Message);
            return [];
        }
    }

    // ---- preview ----------------------------------------------------------------------------

    private void UpdatePreviewPosition()
    {
        if (_preview != null && _previewKey == _pendingKey)
        {
            _preview.transform.position = _editor.MouseWorld();
            return;
        }

        DestroyPreview();
        _previewKey = _pendingKey;
        _editor.StartCoroutine(BuildPreview(_pendingKey, _pendingIsCustom));
    }

    // Key to prefab; shared with the thumbnail renderer.
    internal static IEnumerator ResolvePrefabRoutine(string key, bool isCustom, System.Action<GameObject> done)
    {
        // A boss that lives inside its arena: the room prefab is the address, the child is the
        // subject. Same grammar the NPC tool uses, shared through RoomChildPrefabs.
        if (RoomChildPrefabs.IsRoomKey(key))
        {
            yield return RoomChildPrefabs.ResolveRoutine(key, done);
            yield break;
        }

        if (isCustom)
        {
            GameObject found = null;
            foreach (var pair in CustomEnemies())
                if (pair.Value != null && pair.Value.InternalName == key &&
                    CustomEnemyManager.CustomEnemyPrefabList.TryGetValue(pair.Key, out var p))
                    found = p;
            done(found);
            yield break;
        }

        AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.LoadAssetAsync<GameObject>(key);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: enemy prefab load failed for '{key}': {e.Message}");
            done(null);
            yield break;
        }

        while (!handle.IsDone) yield return null;
        done(handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null);
    }

    internal static bool TryGetCustomSkin(string key, out SkeletonDataAsset asset, out string skin)
    {
        asset = null;
        skin = null;

        foreach (var pair in CustomEnemies())
        {
            if (pair.Value == null || pair.Value.InternalName != key) continue;
            if (pair.Value.SpineOverride == null) return false;

            asset = pair.Value.SpineOverride;
            skin = pair.Value.SpineSkinName;
            return true;
        }

        if (CustomSpineLoader.APIHelper.CustomNpcManager.CustomNpcList.TryGetValue(key, out var npc) &&
            npc?.SpineOverride != null)
        {
            asset = npc.SpineOverride;
            skin = npc.SpineSkinName;
            return true;
        }

        return false;
    }

    internal static void ApplyCustomSkin(SkeletonAnimation spine, string key)
    {
        if (spine == null) return;

        try
        {
            if (!TryGetCustomSkin(key, out var asset, out var skin) || asset == null) return;

            spine.skeletonDataAsset = asset;
            spine.initialSkinName = skin;
            spine.Initialize(true);
            spine.Skeleton.SetToSetupPose();
            spine.Update(0f);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not apply custom skin to '{key}': {e.Message}");
        }
    }

    private IEnumerator BuildPreview(string key, bool isCustom)
    {
        GameObject prefab = null;
        yield return ResolvePrefabRoutine(key, isCustom, p => prefab = p);

        // Selection changed while loading.
        if (_previewKey != key || prefab == null) yield break;

        var ghost = MapEditorGhost.Create(prefab, _editor.transform, "CultTweaker_EnemyPreview",
            disableBehaviours: true);
        if (ghost == null) yield break;
        ghost.transform.position = _editor.MouseWorld();

        var spine = MainSkeleton(ghost);
        if (spine != null)
        {
            foreach (var other in ghost.GetComponentsInChildren<Spine.Unity.SkeletonRenderer>(true))
            {
                if (other == null || ReferenceEquals(other, spine)) continue;
                var mesh = other.GetComponent<MeshRenderer>();
                if (mesh != null) mesh.enabled = false;
            }

            if (isCustom) ApplyCustomSkin(spine, key);

            // Before the alpha, so what is dragged around looks like what will be placed.

            if (spine.Skeleton != null) spine.Skeleton.A = 0.6f;
        }

        if (_previewKey != key)
        {
            Object.Destroy(ghost);
            yield break;
        }

        if (_preview != null) Object.Destroy(_preview);
        _preview = ghost;
    }

    // The controller's serialized Spine field (concrete type varies per enemy); first skeleton
    // in the hierarchy as fallback.
    internal static SkeletonAnimation MainSkeleton(GameObject ghost)
    {
        var unit = ghost.GetComponentInChildren<UnitObject>(true);
        if (unit != null)
        {
            var fromField = APIHelper.CustomEnemyDressing.SkeletonField(unit);
            if (fromField != null) return fromField;
        }

        return ghost.GetComponentInChildren<SkeletonAnimation>(true);
    }

    private void DestroyPreview()
    {
        if (_preview != null) Object.Destroy(_preview);
        _preview = null;
        _previewKey = null;
    }

    // The instance the last spawn produced, so a loader can finish setting it up without
    // every spawn routine having to hand one back.
    public GameObject LastPlacedInstance =>
        _placed.Count > 0 ? _placed[_placed.Count - 1].Instance : null;

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.Enemies.Clear();
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            map.Enemies.Add(new MapEnemyData
            {
                Key = placed.Key,
                IsCustom = placed.IsCustom,
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                Scale = MapEditorSerialization.V3(placed.Instance.transform.lossyScale)
            });
        }
    }
}

public class EnemyContainment : MonoBehaviour
{
    private const float CheckInterval = 0.5f;
    private const float MaxOffGraphSqr = 2.25f; // 1.5 units

    private float _next;

    private void Update()
    {
        // Scaled time: frozen while the editor is open, which is correct - nothing moves then.
        if (Time.time < _next) return;
        _next = Time.time + CheckInterval;

        if (AstarPath.active == null) return;

        var nearest = AstarPath.active.GetNearest(transform.position);
        if (nearest.node == null || !nearest.node.Walkable) return;

        var walkable = (Vector3)nearest.position;
        var offset = walkable - transform.position;
        offset.z = 0f;

        if (offset.sqrMagnitude > MaxOffGraphSqr)
            transform.position = new Vector3(walkable.x, walkable.y, transform.position.z);
    }
}
