using System.Collections.Generic;
using MMBiomeGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Holds the authored map while it is being played, so that entering one of its nodes can start
// the level bound to that node. The game's Node objects carry no room of ours, and the map it
// renders is a rebuilt copy, so the binding is looked up by grid point rather than stored on the
// node itself.
public static class DungeonMapPlayback
{
    private static readonly Dictionary<int, string> LevelByPoint = [];

    private static string _mapName;
    private static CTDungeonMap _map;

    // The Map handed to MapManager. Kept so a later selector can tell "still mine" from "a scene
    // reload built a fresh MapManager", which is when the graph has to be rebuilt.
    private static global::Map.Map _built;

    // The biome's own floor length, put back when a node without a level is entered - it is a
    // field on the scene's BiomeGenerator, so a level's length would otherwise stick to the
    // rest of the run.
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

    // What a dungeon does on entry: remember the map it was authored with, by name, so its exit
    // door has something to offer. An empty name is a dungeon that simply ends.
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

    // True when the selector is up, false when this dungeon is finished and the caller should
    // show the completion screen.
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

        // A scene load builds a fresh MapManager with no map of ours in it, so the graph is
        // rebuilt on the first exit after entering the dungeon.
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

        // The floor just cleared is the run's last when its node sits on the top layer. An empty
        // path is the first exit after entering the dungeon, where the floor just cleared is the
        // bottom node - so a one-layer map finishes here rather than opening a map with nowhere
        // to go.
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

        // Not restored, just forgotten: Clear happens on entering a dungeon or finishing a run,
        // and either way the BiomeGenerator the saved value belonged to is being left behind.
        _savedRoomCount = -1;
    }

    public static string LevelNameFor(global::Map.Node node)
    {
        if (node?.point == null) return null;
        return LevelByPoint.TryGetValue(Key(node.point.x, node.point.y), out var name) ? name : null;
    }

    // Called after the game has set up its own node entry, and before the generation it queued
    // actually runs - Regenerate defers everything into an MMTransition callback.
    public static void OnNodeEntered(global::Map.Node node)
    {
        // Ours only. A map being installed does not mean every node entry belongs to it: the
        // patch fires for whatever MapManager is showing, and a node from the game's own
        // adventure map reads here as "a node with no level" - which would end the level run
        // this dungeon had just bound.
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

            // A level is a floor, so it needs the ordinary random walk. Every node type except
            // the floor ones is a single fixed room, which would show only the level's entrance.
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
