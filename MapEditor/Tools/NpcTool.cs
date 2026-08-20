using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spine.Unity;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CustomSpineLoader.MapEditor.Tools;

public class NpcTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts
{
    public string Name => "NPCs";

    // Both spellings are accepted: the shipped catalog uses the singular folder, and nothing
    // guarantees a future update keeps it that way.
    private static readonly string[] PrefabPrefixes = ["Assets/Prefabs/NPC/", "Assets/Prefabs/NPCs/"];

    private static readonly string[] RoomPrefixes = ["Assets/_Rooms/", "Assets/Prefabs/Rescue Rooms/"];

    private static readonly (string Label, string[] Patterns)[] RoomBuckets =
    [
        ("Story Characters", [
            "Story Room", "Lore ", "Witness", "Ratau", "Death Cat", "Haro", "Sozo", "Midas",
            "Plimbo", "Fisherman", "Baal", "Klunko", "Sherpa", "Stelle", "Monch", "Jalala",
            "Lighthouse", "Dungeon Rancher"
        ]),
        ("Vendors & Games", ["Marketplace", "Follower Shop", "Knucklebones", "Shop"]),
        ("Special Rooms", ["Special ", "Rescue Room", "Fishing", "NPC ", "Healing Room"])
    ];

    // How a character entry names its source; also the marker that tells the spawner to dig into
    // a room prefab rather than instantiate an addressable directly.
    private const string RoomKeyPrefix = "room:";

    private readonly RuntimeMapEditor _editor;
    private readonly List<PlacedNpc> _placed = [];

    private static List<NpcGroup> _groups;

    private MapEditorDropdown _groupDropdown;
    private MapEditorGrid _grid;

    private string _pendingKey;
    private GameObject _preview;
    private string _previewKey;

    private class NpcGroup
    {
        public string Label;

        // Set for a bucket of source rooms; its characters are not known until those prefabs
        // have been opened, which is why Entries fills in progressively rather than up front.
        public List<string> RoomKeys;

        public List<(string label, string key)> Entries;
        public bool Scanned;

        // Mod-registered NPCs; entries are read live from CustomNpcManager on every open, since
        // mods register at their own pace.
        public bool IsCustom;
    }

    private class PlacedNpc
    {
        public string Key;
        public bool IsCustom;
        public GameObject Instance;
    }

    public NpcTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        var options = Groups()
            .Select(g => g.Entries != null ? $"{g.Label} ({g.Entries.Count})" : g.Label)
            .ToList();

        _groupDropdown = ui.CreateDropdown(panel, "Choose a group", options, (index, _) => ShowGroupAt(index));
        _grid = ui.CreateIconGrid(panel, "NpcGrid");

        ui.CreateButton(panel, "Clear Selection", () =>
        {
            _pendingKey = null;
            DestroyPreview();
            _grid?.SetSelected(null);
            _editor.SetStatus("Selection cleared.");
        });
    }

    private void ShowGroupAt(int index)
    {
        var groups = Groups();
        if (_grid == null || index < 0 || index >= groups.Count) return;

        // Whatever a previous scan was still filling in belongs to a grid that is about to go.
        _scanToken++;

        var group = groups[index];
        if (group.IsCustom)
        {
            ShowCustomGroup();
            return;
        }

        if (group.RoomKeys == null || group.Scanned)
        {
            Populate(group);
            return;
        }

        _editor.StartCoroutine(ScanRooms(group, _scanToken));
    }

    private void ShowCustomGroup()
    {
        EnemyThumbnails.CancelPending();
        _grid.Clear();

        var count = 0;
        foreach (var pair in APIHelper.CustomNpcManager.CustomNpcList)
        {
            if (pair.Value == null) continue;
            AddCell(pair.Key, pair.Value.DisplayName, isCustom: true);
            count++;
        }

        _editor.RequestOptionsResize();
        if (count == 0) _editor.SetStatus("No custom NPCs registered.", StatusSeverity.Warning);
        else _editor.SetStatus($"Custom NPCs: {count}.");
    }

    private int _scanToken;

    private IEnumerator ScanRooms(NpcGroup group, int token)
    {
        group.Entries ??= [];
        _grid.Clear();
        EnemyThumbnails.CancelPending();

        var index = Index();
        var opened = 0;

        for (var i = 0; i < group.RoomKeys.Count; i++)
        {
            if (token != _scanToken) yield break;

            var roomKey = group.RoomKeys[i];

            if (!index.TryGetValue(roomKey, out var entries))
            {
                _editor.SetStatus($"Scanning {group.Label} - room {i + 1} of {group.RoomKeys.Count}...");

                GameObject prefab = null;
                yield return RoomSnapshot.LoadPrefabByKeyRoutine(roomKey, p => prefab = p);
                if (token != _scanToken) yield break;

                entries = prefab != null ? ExtractCharacters(prefab, roomKey) : [];
                index[roomKey] = entries;
                opened++;
            }

            foreach (var entry in entries)
            {
                if (_grid.Has(entry.key)) continue;
                AddCell(entry.key, entry.label);
                group.Entries.Add(entry);
            }

            _editor.RequestOptionsResize();
        }

        group.Scanned = true;
        if (opened > 0) SaveIndex();

        Plugin.Log.LogInfo($"MapEditor: {group.Label} holds {group.Entries.Count} character(s) " +
                           $"across {group.RoomKeys.Count} room(s); {opened} room(s) opened this time.");

        _editor.SetStatus(group.Entries.Count > 0
            ? $"{group.Label}: {group.Entries.Count} character(s)."
            : $"No characters found in {group.Label}.");
    }

    private void Populate(NpcGroup group)
    {
        EnemyThumbnails.CancelPending();
        _grid.Clear();

        if (group.Entries == null) return;
        foreach (var entry in group.Entries) AddCell(entry.key, entry.label);
        _editor.RequestOptionsResize();
    }

    private void AddCell(string key, string label, bool isCustom = false)
    {
        _grid.AddCell(key, label, null, () =>
        {
            _pendingKey = key;
            _pendingIsCustom = isCustom;
            DestroyPreview();
            _editor.SetStatus($"Selected {label}.");
        });

        // isCustom routes the thumbnail renderer through the custom-skin lookup (generalized in
        // EnemyTool.TryGetCustomSkin to cover NPCs too), so the tile wears the override.
        EnemyThumbnails.Request(_editor, key, isCustom,
            sprite => _grid?.SetCellIcon(key, sprite), ResolveRoutine);
    }

    private bool _pendingIsCustom;

    public void OnEnter()
    {
        if (_grid != null && _groupDropdown != null && _groupDropdown.SelectedIndex < 0)
        {
            _groupDropdown.SetSelected(0);
            ShowGroupAt(0);
        }

        if (Groups().Count == 0)
        {
            _editor.SetStatus("No NPC prefabs found in the catalog.", StatusSeverity.Warning);
            return;
        }

        _editor.SetStatus("Pick a group, then an NPC. Named characters live under their room.");
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Place selected NPC")
    ];

    public void OnExit() => DestroyPreview();

    public void OnUpdate()
    {
        if (string.IsNullOrEmpty(_pendingKey)) return;

        UpdatePreviewPosition();

        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;
        _editor.StartCoroutine(SpawnNpcRoutine(_pendingKey, _editor.MouseWorld(), _pendingIsCustom));
    }

    // ---- placement ----------------------------------------------------------------------------

    // Also the loader's entry point: self-registers so load then save round-trips.
    public IEnumerator SpawnNpcRoutine(string key, Vector3 position, bool isCustom = false)
    {
        GameObject go = null;

        if (isCustom)
        {
            // The prefab list fills from a coroutine at plugin load; a blueprint loading very
            // early can outrun it.
            var deadline = Time.unscaledTime + 10f;
            while (!APIHelper.CustomNpcManager.CustomNpcPrefabList.ContainsKey(key) &&
                   APIHelper.CustomNpcManager.CustomNpcList.ContainsKey(key) &&
                   Time.unscaledTime < deadline)
                yield return null;

            go = APIHelper.CustomNpcManager.Spawn(key, position);
            if (go == null)
            {
                Plugin.Log.LogWarning($"MapEditor: custom NPC '{key}' could not be spawned " +
                                      "(not registered, or its prefab never loaded).");
                yield break;
            }
        }
        else if (IsRoomKey(key))
        {
            GameObject source = null;
            yield return ResolveRoutine(key, p => source = p);

            if (source == null)
            {
                Plugin.Log.LogWarning($"MapEditor: NPC '{key}' could not be resolved from its room prefab.");
                yield break;
            }

            if (!IsSafeToSpawn(source, out var reason))
            {
                _editor.SetStatus($"'{LabelFor(key)}' cannot be placed: {reason}.", StatusSeverity.Error);
                Plugin.Log.LogWarning($"MapEditor: refused to spawn NPC '{key}' - {reason}.");
                yield break;
            }

            try
            {
                // A child of a prefab asset instantiates on its own, which is the whole point:
                // the character comes across without the room around it.
                go = UnityEngine.Object.Instantiate(source, SceneRefs.ContentRoot);
                go.SetActive(true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: NPC '{key}' failed to instantiate: {e.Message}");
                yield break;
            }
        }
        else
        {
            AsyncOperationHandle<GameObject> handle;
            try
            {
                handle = Addressables.InstantiateAsync(key, SceneRefs.ContentRoot, false);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: could not instantiate NPC '{key}': {e.Message}");
                yield break;
            }

            while (!handle.IsDone) yield return null;

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Plugin.Log.LogWarning($"MapEditor: NPC load failed for '{key}'.");
                yield break;
            }

            go = handle.Result;
        }

        go.transform.position = position;

        var placed = new PlacedNpc { Key = key, IsCustom = isCustom, Instance = go };
        _placed.Add(placed);

        var label = isCustom ? key : LabelFor(key);
        _editor.History.Push($"place {label}", () =>
        {
            if (!_placed.Remove(placed) || placed.Instance == null) return false;
            UnityEngine.Object.Destroy(placed.Instance);
            return true;
        });

        _editor.SetStatus($"Placed {label}.");
    }

    // ---- keys ---------------------------------------------------------------------------------

    private static bool IsRoomKey(string key) => key != null && key.StartsWith(RoomKeyPrefix);

    private static bool TryParseRoomKey(string key, out string roomKey, out string childPath)
    {
        roomKey = null;
        childPath = null;
        if (!IsRoomKey(key)) return false;

        var body = key.Substring(RoomKeyPrefix.Length);
        var split = body.IndexOf('|');
        if (split <= 0) return false;

        roomKey = body.Substring(0, split);
        childPath = body.Substring(split + 1);
        return true;
    }

    private static string LabelFor(string key)
    {
        if (!TryParseRoomKey(key, out _, out var path)) return Path.GetFileNameWithoutExtension(key);

        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    // Turns either kind of key into a prefab. Shared by the grid thumbnails, the cursor preview
    // and placement, so all three agree on what a key means.
    internal static IEnumerator ResolveRoutine(string key, Action<GameObject> done)
    {
        if (APIHelper.CustomNpcManager.CustomNpcList.ContainsKey(key))
        {
            var deadline = Time.unscaledTime + 10f;
            while (!APIHelper.CustomNpcManager.CustomNpcPrefabList.TryGetValue(key, out _) &&
                   Time.unscaledTime < deadline)
                yield return null;

            APIHelper.CustomNpcManager.CustomNpcPrefabList.TryGetValue(key, out var mimic);
            done(mimic);
            yield break;
        }

        if (!TryParseRoomKey(key, out var roomKey, out var childPath))
        {
            yield return EnemyTool.ResolvePrefabRoutine(key, isCustom: false, done);
            yield break;
        }

        GameObject room = null;
        yield return RoomSnapshot.LoadPrefabByKeyRoutine(roomKey, p => room = p);

        if (room == null)
        {
            done(null);
            yield break;
        }

        var child = FindByPath(room.transform, childPath);
        done(child != null ? child.gameObject : null);
    }

    private static Transform FindByPath(Transform root, string path)
    {
        var current = root;
        foreach (var step in path.Split('/'))
        {
            if (current == null) return null;

            Transform next = null;
            for (var i = 0; i < current.childCount; i++)
            {
                if (current.GetChild(i).name != step) continue;
                next = current.GetChild(i);
                break;
            }
            current = next;
        }
        return current;
    }

    // ---- catalog ------------------------------------------------------------------------------

    private static List<NpcGroup> Groups()
    {
        if (_groups != null) return _groups;

        _groups = [];

        // 1. The handful of standalone NPC prefabs, grouped by their folder.
        var byFolder = new SortedDictionary<string, List<(string label, string key)>>();
        var rooms = new Dictionary<string, List<string>>();

        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator?.Keys == null) continue;
            foreach (var keyObj in locator.Keys)
            {
                if (keyObj is not string key || !key.EndsWith(".prefab")) continue;

                var prefabPrefix = PrefabPrefixes.FirstOrDefault(key.StartsWith);
                if (prefabPrefix != null)
                {
                    var relative = key.Substring(prefabPrefix.Length);
                    var slash = relative.IndexOf('/');
                    var folder = slash > 0 ? relative.Substring(0, slash) : "General";

                    if (!byFolder.TryGetValue(folder, out var list)) byFolder[folder] = list = [];
                    if (!list.Any(e => e.key == key)) list.Add((Path.GetFileNameWithoutExtension(key), key));
                    continue;
                }

                // 2. Rooms that hold authored characters, pooled into their bucket.
                if (!RoomPrefixes.Any(key.StartsWith)) continue;

                var name = Path.GetFileNameWithoutExtension(key);
                var bucket = BucketFor(name);
                if (bucket == null) continue;

                if (!rooms.TryGetValue(bucket, out var bucketRooms)) rooms[bucket] = bucketRooms = [];
                if (!bucketRooms.Contains(key)) bucketRooms.Add(key);
            }
        }

        foreach (var pair in byFolder)
        {
            pair.Value.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            _groups.Add(new NpcGroup { Label = pair.Key, Entries = pair.Value, Scanned = true });
        }

        // Bucket order follows the declaration, not the alphabet: the named characters are what
        // anyone opening this tool is looking for.
        foreach (var bucket in RoomBuckets)
        {
            if (!rooms.TryGetValue(bucket.Label, out var keys)) continue;
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            _groups.Add(new NpcGroup { Label = bucket.Label, RoomKeys = keys });
        }

        // Always present, contents read live when opened - mods register at their own pace.
        _groups.Add(new NpcGroup { Label = "Custom (mods)", IsCustom = true });

        Plugin.Log.LogInfo($"MapEditor: NPC catalog holds {byFolder.Sum(g => g.Value.Count)} standalone prefab(s) " +
                           $"and {rooms.Sum(r => r.Value.Count)} source room(s) in {rooms.Count} bucket(s).");
        return _groups;
    }

    private static string BucketFor(string roomName)
    {
        foreach (var bucket in RoomBuckets)
            if (bucket.Patterns.Any(p => roomName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                return bucket.Label;

        return null;
    }

    // ---- saved index ---------------------------------------------------------------------------

    private static Dictionary<string, List<(string label, string key)>> _index;

    private static string IndexPath => Path.Combine(Plugin.PluginPath, "EditorCache", "npc-index.json");

    private class IndexEntry
    {
        public string Label;
        public string Key;
    }

    private static Dictionary<string, List<(string label, string key)>> Index()
    {
        if (_index != null) return _index;

        _index = [];
        try
        {
            if (File.Exists(IndexPath))
            {
                var raw = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<Dictionary<string, List<IndexEntry>>>(File.ReadAllText(IndexPath));

                if (raw != null)
                    foreach (var pair in raw)
                        _index[pair.Key] = pair.Value
                            .Where(e => e != null && !string.IsNullOrEmpty(e.Key))
                            .Select(e => (e.Label, e.Key))
                            .ToList();

                Plugin.Log.LogInfo($"MapEditor: NPC index loaded for {_index.Count} room(s).");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: NPC index could not be read, rescanning: " + e.Message);
            _index = [];
        }

        return _index;
    }

    private static void SaveIndex()
    {
        try
        {
            var folder = Path.GetDirectoryName(IndexPath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var raw = _index.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(e => new IndexEntry { Label = e.label, Key = e.key }).ToList());

            File.WriteAllText(IndexPath,
                Newtonsoft.Json.JsonConvert.SerializeObject(raw, Newtonsoft.Json.Formatting.Indented));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: NPC index could not be written: " + e.Message);
        }
    }

    private const int MaxCharactersPerRoom = 40;

    private static List<(string label, string key)> ExtractCharacters(GameObject room, string roomKey)
    {
        var results = new List<(string label, string key)>();
        var seen = new HashSet<Transform>();

        foreach (var skeleton in room.GetComponentsInChildren<SkeletonAnimation>(true))
        {
            if (skeleton == null || skeleton.transform == room.transform) continue;

            var root = CharacterRoot(skeleton.transform, room.transform);
            if (root == null || !seen.Add(root)) continue;
            if (!LooksLikeCharacter(root.gameObject)) continue;

            results.Add((root.name, RoomKeyPrefix + roomKey + "|" + PathOf(root, room.transform)));
            if (results.Count >= MaxCharactersPerRoom)
            {
                Plugin.Log.LogInfo($"MapEditor: '{roomKey}' has more than {MaxCharactersPerRoom} " +
                                   "character-like objects; the rest are not listed.");
                break;
            }
        }

        return results;
    }

    private static Transform CharacterRoot(Transform skeleton, Transform roomRoot)
    {
        var best = skeleton;
        var current = skeleton.parent;

        while (current != null && current != roomRoot)
        {
            if (IsRoomStructure(current.gameObject) || ContainsRoomStructure(current.gameObject)) break;
            if (current.GetComponentsInChildren<Transform>(true).Length > MaxCharacterTransforms) break;

            // A few rigs is a character with an effect or a companion; a dozen is a crowd, and
            // taking the node above a crowd is how this starts swallowing the room again.
            if (current.GetComponentsInChildren<SkeletonAnimation>(true).Length > MaxSkeletonsPerCharacter) break;

            best = current;
            current = current.parent;
        }

        return best;
    }

    private const int MaxSkeletonsPerCharacter = 4;

    private static bool IsRoomStructure(GameObject go)
    {
        return go.GetComponent<MMRoomGeneration.GenerateRoom>() != null ||
               go.GetComponent<MMRoomGeneration.IslandPiece>() != null ||
               go.GetComponent<Door>() != null ||
               go.GetComponent<UnityEngine.U2D.SpriteShapeController>() != null;
    }

    private static bool LooksLikeCharacter(GameObject go)
    {
        if (ContainsRoomStructure(go)) return false;

        foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;

            var type = behaviour.GetType().Name;
            if (type.IndexOf("NPC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.StartsWith("Interaction") ||
                type.Contains("Conversation") ||
                type.Contains("Bark") ||
                type.Contains("Shop") ||
                type.Contains("Knucklebones") ||
                type == "Follower")
                return true;
        }

        return false;
    }

    private static bool ContainsRoomStructure(GameObject go)
    {
        return go.GetComponentInChildren<MMRoomGeneration.GenerateRoom>(true) != null ||
               go.GetComponentInChildren<MMRoomGeneration.IslandPiece>(true) != null ||
               go.GetComponentInChildren<Door>(true) != null ||
               go.GetComponentInChildren<UnityEngine.U2D.SpriteShapeController>(true) != null;
    }

    private static string PathOf(Transform node, Transform root)
    {
        var path = node.name;
        for (var current = node.parent; current != null && current != root; current = current.parent)
            path = current.name + "/" + path;
        return path;
    }

    private const int MaxCharacterTransforms = 250;

    private static bool IsSafeToSpawn(GameObject source, out string reason)
    {
        if (ContainsRoomStructure(source))
        {
            reason = "it is part of a room (doors, island pieces or terrain), not a character";
            return false;
        }

        var count = source.GetComponentsInChildren<Transform>(true).Length;
        if (count > MaxCharacterTransforms)
        {
            reason = $"it holds {count} objects, which is a room rather than a character";
            return false;
        }

        reason = null;
        return true;
    }

    // ---- preview ------------------------------------------------------------------------------

    private void UpdatePreviewPosition()
    {
        if (_preview != null && _previewKey == _pendingKey)
        {
            _preview.transform.position = _editor.MouseWorld();
            return;
        }

        DestroyPreview();
        _previewKey = _pendingKey;
        _editor.StartCoroutine(BuildPreview(_pendingKey));
    }

    private IEnumerator BuildPreview(string key)
    {
        var isCustom = _pendingIsCustom;

        GameObject prefab = null;
        yield return ResolveRoutine(key, p => prefab = p);

        // Selection changed while loading.
        if (_previewKey != key || prefab == null) yield break;

        // A cursor preview of a whole room is as bad as placing one; the same guard decides.
        if (!IsSafeToSpawn(prefab, out _)) yield break;

        var ghost = MapEditorGhost.Create(prefab, _editor.transform, "CultTweaker_NpcPreview",
            disableBehaviours: true);
        if (ghost == null) yield break;

        ghost.transform.position = _editor.MouseWorld();

        var spine = EnemyTool.MainSkeleton(ghost);
        // The custom NPC's ghost is its mimic; the skin override is what makes it look like
        // itself. The generalized lookup covers NPC keys.
        if (isCustom) EnemyTool.ApplyCustomSkin(spine, key);
        if (spine?.Skeleton != null) spine.Skeleton.A = 0.6f;

        // Two of these can overlap when the selection changes mid-load; only the one still
        // matching the current key may install its ghost.
        if (_previewKey != key)
        {
            UnityEngine.Object.Destroy(ghost);
            yield break;
        }

        if (_preview != null) UnityEngine.Object.Destroy(_preview);
        _preview = ghost;
    }

    private void DestroyPreview()
    {
        if (_preview != null) UnityEngine.Object.Destroy(_preview);
        _preview = null;
        _previewKey = null;
    }

    // ---- bookkeeping ---------------------------------------------------------------------------

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
            UnityEngine.Object.Destroy(placed.Instance);
            removed++;
        }

        _placed.Clear();
        return removed;
    }

    // The instance the last spawn produced, so a loader can finish setting it up without
    // every spawn routine having to hand one back.
    public GameObject LastPlacedInstance =>
        _placed.Count > 0 ? _placed[_placed.Count - 1].Instance : null;

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.Npcs.Clear();
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            map.Npcs.Add(new MapNpcData
            {
                Key = placed.Key,
                IsCustom = placed.IsCustom,
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                Scale = MapEditorSerialization.V3(placed.Instance.transform.lossyScale)
            });
        }
    }
}
