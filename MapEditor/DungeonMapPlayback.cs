using System.Collections.Generic;
using MMBiomeGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class DungeonMapPlayback
{
    private static readonly Dictionary<int, string> LevelByPoint = [];

    private static string _mapName;
    private static CTDungeonMap _map;

    private static global::Map.Map _built;

    private static int _savedRoomCount = -1;

    public static bool Active => LevelByPoint.Count > 0;

    private static int Key(int x, int y) => x * 1000 + y;

    public static void Install(CTDungeonMap map)
    {
        Clear();
        if (map == null) return;

        _map = map;
        _mapName = map.MapName;

        var points = DungeonMapBuilder.Layout(map);
        _topLayer = 0;

        foreach (var pair in points)
        {
            if (pair.Value.y > _topLayer) _topLayer = pair.Value.y;
            if (string.IsNullOrEmpty(pair.Key.Level)) continue;

            LevelByPoint[Key(pair.Value.x, pair.Value.y)] = pair.Key.Level;
        }

        Plugin.Log.LogInfo($"MapEditor: dungeon map '{_mapName}' installed with " +
                           $"{LevelByPoint.Count} node(s) bound to a level.");
    }

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

        var current = manager.CurrentMap.GetCurrentNode();
        if ((current?.point.y ?? 0) >= TopLayer())
        {
            Plugin.Log.LogInfo($"MapEditor: map '{_mapName}' reached its top layer; the run is over.");
            return false;
        }

        manager.ShowMap();
        return true;
    }

    private static int _topLayer;

    private static int TopLayer() => _topLayer;

    public static void Clear()
    {
        LevelByPoint.Clear();
        _mapName = null;
        _map = null;
        _built = null;
        _topLayer = 0;

        _savedRoomCount = -1;
    }

    public static string LevelNameFor(global::Map.Node node)
    {
        if (node?.point == null) return null;
        return LevelByPoint.TryGetValue(Key(node.point.x, node.point.y), out var name) ? name : null;
    }

    public static void OnNodeEntered(global::Map.Node node)
    {
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

            biome.OverrideRandomWalk = false;
        }

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
