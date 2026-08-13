using System.Collections;
using System.Collections.Generic;
using MMRoomGeneration;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor.Tools;

// Spawns the game's own island pieces as a starting point for a room. An island is a floor slab
// with its own collision and connectors - the building block the generator lays paths out of -
// so starting from one gives an authored room the shape and look of a real dungeon room.
//
// An island prefab on its own renders nothing usable: it carries a flat placeholder fill, and
// the textured floor is a separate prefab spawned as a child at runtime, chosen from the
// island's own SpriteShapes list. Vanilla's InitIsland hides the placeholder BEFORE that art
// arrives, so anything that interrupts the spawn leaves an invisible slab - which is why this
// spawns the art itself and only hides the placeholder once the art is actually there.
public class IslandTool : IMapEditorTool
{
    public string Name => "Islands";

    private readonly RuntimeMapEditor _editor;
    private readonly List<GameObject> _entries = [];

    private RectTransform _panel;
    private MapEditorUI _ui;
    private TMP_Text _statusLabel;

    private bool _clearFirst = true;
    private bool _spawnEncounters;
    private bool _busy;

    // An island offered by the panel: either a prefab this room already holds, or a catalog
    // key to load on demand.
    private class IslandEntry
    {
        public string Label;
        public IslandPiece Piece;
        public string Key;
    }

    private static SortedDictionary<string, List<IslandEntry>> _catalog;

    public IslandTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _panel = panel;
        _ui = ui;

        ui.CreateLabel(panel, "Island Tool", 20, TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Spawn a vanilla island as the\nbase shape for this room.",
            14, TextAlignmentOptions.Center);

        _statusLabel = ui.CreateLabel(panel, "", 14, TextAlignmentOptions.Center).GetComponent<TMP_Text>();

        ui.CreateToggle(panel, "Clear room first", _clearFirst, v => _clearFirst = v);
        ui.CreateToggle(panel, "Spawn encounters", _spawnEncounters, v => _spawnEncounters = v);
        ui.CreateButton(panel, "Refresh List", RefreshList);
    }

    public void OnEnter()
    {
        RefreshList();
        _editor.SetStatus("Island tool: pick an island to build this room from.");
    }

    public void OnExit() { }
    public void OnUpdate() { }

    private void RefreshList()
    {
        foreach (var go in _entries)
            if (go != null) Object.Destroy(go);
        _entries.Clear();

        if (_panel == null || _ui == null) return;

        foreach (var group in Catalog())
        {
            var captured = group.Key;
            _entries.Add(_ui.CreateButton(_panel, $"{captured} ({group.Value.Count})", () => ShowGroup(captured)));
        }
    }

    private readonly List<GameObject> _listButtons = [];

    private void ShowGroup(string group)
    {
        foreach (var go in _listButtons)
            if (go != null) Object.Destroy(go);
        _listButtons.Clear();

        if (_panel == null || _ui == null || !Catalog().TryGetValue(group, out var entries)) return;

        _listButtons.Add(_ui.CreateLabel(_panel, $"— {group} —", 14, TextAlignmentOptions.Center));
        foreach (var entry in entries)
        {
            var captured = entry;
            _listButtons.Add(_ui.CreateButton(_panel, captured.Label, () => Spawn(captured)));
        }
    }

    // Every island in the game, not just the ones this room's prefab happens to list - the same
    // catalog sweep the enemy tool does. Islands are addressable prefabs whose names and folders
    // carry "Island", which is what makes them findable without loading all 3,000+ prefabs.
    private static SortedDictionary<string, List<IslandEntry>> Catalog()
    {
        if (_catalog != null) return _catalog;

        _catalog = [];
        AddRoomGroups();

        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator?.Keys == null) continue;
            foreach (var keyObj in locator.Keys)
            {
                if (keyObj is not string key || !key.EndsWith(".prefab")) continue;

                var name = System.IO.Path.GetFileNameWithoutExtension(key);
                var isIsland = name.IndexOf("island", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                               key.IndexOf("/island", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isIsland) continue;

                var group = GroupFor(key);
                if (!_catalog.TryGetValue(group, out var list)) _catalog[group] = list = [];
                if (!list.Exists(e => e.Key == key || e.Label == name))
                    list.Add(new IslandEntry { Label = name, Key = key });
            }
        }

        foreach (var list in _catalog.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));

        var total = 0;
        foreach (var list in _catalog.Values) total += list.Count;
        Plugin.Log.LogInfo($"MapEditor: island catalog holds {total} piece(s) in {_catalog.Count} group(s).");
        return _catalog;
    }

    private static string GroupFor(string key)
    {
        var folder = System.IO.Path.GetDirectoryName(key)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder)) return "Islands";

        var parts = folder.Split('/');
        return parts.Length >= 2 ? parts[parts.Length - 2] + " / " + parts[parts.Length - 1] : parts[^1];
    }

    // The current room's own lists first: these are the pieces whose art matches this biome.
    private static void AddRoomGroups()
    {
        var room = SceneRefs.Room;
        if (room == null) return;

        void Add(string title, List<IslandPiece> pieces)
        {
            if (pieces == null || pieces.Count == 0) return;

            var list = new List<IslandEntry>();
            foreach (var piece in pieces)
            {
                if (piece == null || list.Exists(e => e.Label == piece.name)) continue;
                list.Add(new IslandEntry { Label = piece.name, Piece = piece });
            }
            if (list.Count > 0) _catalog["This Room / " + title] = list;
        }

        Add("Start Pieces", room.StartPieces);
        Add("Island Pieces", room.IslandPieces);
        Add("Resource Pieces", room.ResourcePieces);
    }

    private void Spawn(IslandEntry entry)
    {
        if (_busy)
        {
            _editor.SetStatus("An island is still spawning.");
            return;
        }
        if (entry == null) return;

        _editor.StartCoroutine(SpawnRoutine(entry));
    }

    private IEnumerator SpawnRoutine(IslandEntry entry)
    {
        _busy = true;

        var room = SceneRefs.Room;
        if (room == null)
        {
            _busy = false;
            yield break;
        }

        GameObject prefab = null;
        if (entry.Piece != null) prefab = entry.Piece.gameObject;
        else yield return RoomSnapshot.LoadPrefabByKeyRoutine(entry.Key, go => prefab = go);

        if (prefab == null)
        {
            _busy = false;
            SetLabel($"'{entry.Label}' could not be loaded.");
            _editor.SetStatus($"Island '{entry.Label}' could not be loaded, see log.");
            yield break;
        }

        if (_clearFirst)
        {
            _editor.GetTool<ClearTool>()?.ClearTerrain();
            // Deferred destruction has to flush before the new island joins the composite.
            yield return null;
        }

        var parent = room.RoomTransform != null ? room.RoomTransform.transform : room.transform;
        var island = Object.Instantiate(prefab, parent);
        island.transform.position = Vector3.zero;

        var piece = island.GetComponent<IslandPiece>();
        if (piece == null)
        {
            Object.Destroy(island);
            _busy = false;
            _editor.SetStatus("That prefab has no IslandPiece component.");
            yield break;
        }

        if (room.Pieces != null && !room.Pieces.Contains(piece)) room.Pieces.Add(piece);

        var artSpawned = false;
        yield return SpawnIslandChild(piece, ArtPath(piece), room, isArt: true, go => artSpawned = go != null);

        if (_spawnEncounters)
            yield return SpawnIslandChild(piece, EncounterPath(piece), room, isArt: false, null);

        // Only now: hiding the placeholder before the art exists is what leaves an empty slab.
        if (artSpawned) piece.HideSprites();

        // The island brings floor collision, so the union and the nav grid both need rebuilding.
        SceneRefs.RegenerateRoomCollision();

        _busy = false;
        SetLabel(artSpawned ? $"Spawned '{entry.Label}'." : $"'{entry.Label}' (no art, placeholder shown)");
        _editor.SetStatus(artSpawned
            ? $"Spawned island '{entry.Label}'. Shape and doors can be edited as usual."
            : $"Spawned '{entry.Label}', but it has no floor art - its placeholder fill is left visible.");
        Plugin.Log.LogInfo($"MapEditor: island '{entry.Label}' spawned (art: {artSpawned}).");
    }

    private static string ArtPath(IslandPiece piece)
    {
        var list = GameManager.Layer2 ? piece.SpriteShapes2 : piece.SpriteShapes;
        if (list?.ObjectList == null || list.ObjectList.Count == 0)
        {
            // Layer 2 sets are not authored for every island; fall back to the primary set.
            list = piece.SpriteShapes;
            if (list?.ObjectList == null || list.ObjectList.Count == 0) return null;
        }

        var entry = list.ObjectList[Random.Range(0, list.ObjectList.Count)];
        return entry?.GameObjectPath;
    }

    private static string EncounterPath(IslandPiece piece)
    {
        var list = piece.Encounters;
        if (list?.ObjectList == null || list.ObjectList.Count == 0) return null;

        var entry = list.ObjectList[Random.Range(0, list.ObjectList.Count)];
        return entry?.GameObjectPath;
    }

    // Mirrors what InitIsland does to a spawned child: the art's sprite shapes take the biome's
    // secondary profile and material and sit on the secondary ground layer, or they render with
    // the wrong texture (or not at all).
    private IEnumerator SpawnIslandChild(IslandPiece piece, string path, GenerateRoom room, bool isArt,
        System.Action<GameObject> onDone)
    {
        if (string.IsNullOrEmpty(path))
        {
            onDone?.Invoke(null);
            yield break;
        }

        GameObject spawned = null;
        var finished = false;

        try
        {
            ObjectPool.Spawn(path, piece.transform.position + new Vector3(0f, 0f, -0.005f),
                Quaternion.identity, piece.transform, go =>
                {
                    spawned = go;
                    finished = true;
                });
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: island child '{path}' failed to spawn: {e.Message}");
            onDone?.Invoke(null);
            yield break;
        }

        var deadline = Time.unscaledTime + 10f;
        while (!finished && Time.unscaledTime < deadline) yield return null;

        if (spawned != null && isArt)
        {
            var secondary = room.DecorationList != null ? room.DecorationList.SpriteShapeSecondary : null;
            var material = room.DecorationList != null ? room.DecorationList.SpriteShapeMaterial : null;

            foreach (var shape in spawned.GetComponentsInChildren<SpriteShapeController>(true))
            {
                if (shape == null) continue;
                if (secondary != null) shape.spriteShape = secondary;

                var renderer = shape.spriteShapeRenderer;
                if (renderer == null) continue;

                if (material != null)
                {
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                }
                renderer.sortingLayerName = "Ground - Secondary Layer";
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        onDone?.Invoke(spawned);
    }

    private void SetLabel(string text)
    {
        if (_statusLabel != null) _statusLabel.text = text;
    }
}
