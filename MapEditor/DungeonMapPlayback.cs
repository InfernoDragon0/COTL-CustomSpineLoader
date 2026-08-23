using System.Collections.Generic;
using MMBiomeGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Holds the authored map while it is played. The game's Node objects carry nothing of ours, so
// level bindings are looked up by grid point.
public static class DungeonMapPlayback
{
    private static readonly Dictionary<int, string> LevelByPoint = [];

    private static string _mapName;
    private static CTDungeonMap _map;

    // The Map handed to MapManager; tells "still mine" from "a scene reload rebuilt MapManager".
    private static global::Map.Map _built;

    // The biome's own floor length, put back when a node without a level is entered - it is a
    // field on the scene's BiomeGenerator, so a level's length would otherwise stick.
    private static int _savedRoomCount = -1;

    public static bool Active => LevelByPoint.Count > 0;

    private static int Key(int x, int y) => x * 1000 + y;

    public static void Install(CTDungeonMap map)
    {
        Clear();
        if (map == null) return;

        _map = map;
        _mapName = map.MapName;

        foreach (var node in map.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.Level)) continue;
            LevelByPoint[Key(node.X, node.Y)] = node.Level;
        }

        Plugin.Log.LogInfo($"MapEditor: dungeon map '{_mapName}' installed with " +
                           $"{LevelByPoint.Count} node(s) bound to a level.");
    }

    // Remembers the map by name on entry so the exit door has something to offer.
    public static void UseMap(string mapName)
    {
        Clear();
        if (string.IsNullOrEmpty(mapName)) return;

        foreach (var map in CTDungeonMapSerialization.LoadAll())
        {
            if (map == null ||
                !string.Equals(map.MapName, mapName, System.StringComparison.OrdinalIgnoreCase)) continue;

            Install(map);
            return;
        }

        Plugin.Log.LogWarning($"MapEditor: dungeon wants map '{mapName}', which is not saved on " +
                              "this machine; its exit will finish the run instead.");
    }

    // True when the selector is up; false = run finished, caller shows the completion screen.
    public static bool TryShowSelector()
    {
        if (_map == null) return false;

        var manager = global::Map.MapManager.Instance;
        if (manager == null)
        {
            Plugin.Log.LogWarning("MapEditor: no MapManager in this scene, so the dungeon map " +
                                  "cannot be shown; finishing the run instead.");
            return false;
        }

        // A scene load builds a fresh MapManager; rebuild the graph on the first exit after entry.
        if (manager.CurrentMap == null || !ReferenceEquals(manager.CurrentMap, _built))
        {
            _built = DungeonMapBuilder.Build(_map, out var error);
            if (_built == null)
            {
                Plugin.Log.LogWarning($"MapEditor: map '{_mapName}' is not playable ({error}); " +
                                      "finishing the run instead.");
                return false;
            }

            DungeonMapBuilder.InstallMap(manager, _built);
        }

        // Top-layer node = run over. An empty path means the first exit (the bottom node just
        // cleared), so a one-layer map finishes here rather than opening a map with nowhere to go.
        var current = manager.CurrentMap.GetCurrentNode();
        if ((current?.point.y ?? 0) >= TopLayer())
        {
            Plugin.Log.LogInfo($"MapEditor: map '{_mapName}' reached its top layer; the run is over.");
            return false;
        }

        manager.ShowMap();
        return true;
    }

    private static int TopLayer()
    {
        var top = 0;
        foreach (var node in _map.Nodes)
            if (node != null && node.Y > top) top = node.Y;

        return top;
    }

    public static void Clear()
    {
        LevelByPoint.Clear();
        _mapName = null;
        _map = null;
        _built = null;

        // Forgotten, not restored: the BiomeGenerator it belonged to is being left behind.
        _savedRoomCount = -1;
    }

    public static string LevelNameFor(global::Map.Node node)
    {
        if (node?.point == null) return null;
        return LevelByPoint.TryGetValue(Key(node.point.x, node.point.y), out var name) ? name : null;
    }

    // Runs after vanilla's node-entry setup but before the generation it queued (Regenerate
    // defers everything into an MMTransition callback).
    public static void OnNodeEntered(global::Map.Node node)
    {
        // Ours only: the patch fires for whatever map the game is showing, and a foreign node
        // reads as "no level" - which would end the run this dungeon had just bound.
        var manager = global::Map.MapManager.Instance;
        if (_built == null || manager == null || !ReferenceEquals(manager.CurrentMap, _built))
        {
            Plugin.Log.LogInfo("MapEditor: a node was entered on a map that is not this " +
                               "dungeon's; leaving the run alone.");
            return;
        }

        var levelName = LevelNameFor(node);
        if (string.IsNullOrEmpty(levelName))
        {
            // Whatever the last node bound stops here: the node just entered is the game's own.
            RestoreRoomCount();
            LevelPlayback.Stop();
            return;
        }

        var level = FindLevel(levelName);
        if (level == null)
        {
            Plugin.Log.LogWarning($"MapEditor: dungeon map node wants level '{levelName}', " +
                                  "which is not saved on this machine; the node stays vanilla.");
            RestoreRoomCount();
            LevelPlayback.Stop();
            return;
        }

        var error = LevelPlayback.StartForMapNode(level);
        if (error != null)
        {
            Plugin.Log.LogWarning($"MapEditor: level '{levelName}' could not start for this node: {error}");
            RestoreRoomCount();
            return;
        }

        var biome = BiomeGenerator.Instance;
        if (biome != null)
        {
            if (_savedRoomCount < 0) _savedRoomCount = biome.NumberOfRooms;
            biome.NumberOfRooms = Mathf.Max(2, level.Rooms.Count);

            // A level is a floor: non-floor node types are a single fixed room, which would
            // show only the level's entrance.
            biome.OverrideRandomWalk = false;
        }

        // The map is not a door, so nothing has reset the door hand-off the room hook reads.
        Patches.DungeonPatches.ResetRoomHandoff();

        Plugin.Log.LogInfo($"MapEditor: node ({node.point.x},{node.point.y}) plays level " +
                           $"'{level.LevelName}' ({level.Rooms.Count} room(s)).");
    }

    private static void RestoreRoomCount()
    {
        if (_savedRoomCount < 0) return;

        var biome = BiomeGenerator.Instance;
        if (biome != null) biome.NumberOfRooms = _savedRoomCount;
        _savedRoomCount = -1;
    }

    private static CTLevelBlueprint FindLevel(string levelName)
    {
        foreach (var level in CTLevelSerialization.LoadAll())
            if (level != null &&
                string.Equals(level.LevelName, levelName, System.StringComparison.OrdinalIgnoreCase))
                return level;

        return null;
    }
}
