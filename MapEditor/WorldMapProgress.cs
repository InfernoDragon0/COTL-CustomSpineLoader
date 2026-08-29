using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace CustomSpineLoader.MapEditor;

public class WorldMapProgressData
{
    public Dictionary<string, WorldMapRecord> Maps = new(StringComparer.OrdinalIgnoreCase);
}

public class WorldMapRecord
{
    public List<string> CompletedNodes = [];
    public List<string> OpenedLocks = [];

    public List<string> KeyNodesBanked = [];

    public int KeysHeld;

    public bool IsCompleted(string nodeId) =>
        nodeId != null && CompletedNodes.Contains(nodeId, StringComparer.OrdinalIgnoreCase);

    public bool IsLockOpened(string nodeId) =>
        nodeId != null && OpenedLocks.Contains(nodeId, StringComparer.OrdinalIgnoreCase);
}

public static class WorldMapProgress
{
    private static WorldMapProgressData _data;
    private static int _loadedSlot = -1;

    private static string _pendingMap;
    private static string _pendingNode;

    public static string PathForSlot() =>
        Path.Combine(CTWorldMapSerialization.RootPath, $"progress_slot{SaveAndLoad.SAVE_SLOT}.json");

    public static WorldMapRecord For(string mapName)
    {
        var data = Data();
        if (string.IsNullOrEmpty(mapName)) return new WorldMapRecord();

        if (!data.Maps.TryGetValue(mapName, out var record) || record == null)
        {
            record = new WorldMapRecord();
            data.Maps[mapName] = record;
        }
        return record;
    }

    public static void MarkCompleted(CTWorldMap map, string nodeId)
    {
        if (map == null || string.IsNullOrEmpty(nodeId)) return;

        var record = For(map.MapName);
        if (!record.IsCompleted(nodeId)) record.CompletedNodes.Add(nodeId);

        var node = map.FindNode(nodeId);
        if (node != null && node.IsKey && node.KeysGranted > 0 &&
            !record.KeyNodesBanked.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
        {
            record.KeyNodesBanked.Add(nodeId);
            record.KeysHeld += node.KeysGranted;
            Plugin.Log.LogInfo($"World map '{map.MapName}': node '{nodeId}' banked " +
                               $"{node.KeysGranted} key(s), {record.KeysHeld} held.");
        }

        Save();
    }

    public static void OpenLock(CTWorldMap map, CTWorldMapNode node)
    {
        if (map == null || node == null) return;

        var record = For(map.MapName);
        if (record.IsLockOpened(node.Id)) return;

        record.KeysHeld = Math.Max(0, record.KeysHeld - Math.Max(0, node.KeysCost));
        record.OpenedLocks.Add(node.Id);
        Plugin.Log.LogInfo($"World map '{map.MapName}': lock '{node.Id}' opened, " +
                           $"{record.KeysHeld} key(s) left.");
        Save();
    }

    public static void WipeMap(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return;
        if (Data().Maps.Remove(mapName)) Save();
    }

    // ---- pending-run tracking ---------------------------------------------------------------

    public static void BeginTracking(string mapName, string nodeId)
    {
        _pendingMap = mapName;
        _pendingNode = nodeId;
        Plugin.Log.LogInfo($"World map '{mapName}': entering node '{nodeId}'.");
    }

    public static void AbortTracking()
    {
        _pendingMap = null;
        _pendingNode = null;
    }

    public static void NotifyRunSucceeded()
    {
        if (_pendingMap == null || _pendingNode == null) return;

        var mapName = _pendingMap;
        var nodeId = _pendingNode;
        AbortTracking();

        try
        {
            var map = CTWorldMapSerialization.LoadByName(mapName);
            if (map == null)
            {
                Plugin.Log.LogWarning($"World map '{mapName}' is gone; the completed node " +
                                      $"'{nodeId}' has nowhere to be recorded.");
                return;
            }

            MarkCompleted(map, nodeId);
            Plugin.Log.LogInfo($"World map '{mapName}': node '{nodeId}' completed.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("World map: completion could not be recorded: " + e);
        }
    }

    // ---- storage ----------------------------------------------------------------------------

    private static WorldMapProgressData Data()
    {
        if (_data != null && _loadedSlot == SaveAndLoad.SAVE_SLOT) return _data;

        _loadedSlot = SaveAndLoad.SAVE_SLOT;
        _data = Load();
        return _data;
    }

    private static WorldMapProgressData Load()
    {
        try
        {
            var path = PathForSlot();
            if (!File.Exists(path)) return new WorldMapProgressData();

            return JsonConvert.DeserializeObject<WorldMapProgressData>(File.ReadAllText(path))
                   ?? new WorldMapProgressData();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("World map: progress file could not be read: " + e.Message);
            return new WorldMapProgressData();
        }
    }

    private static void Save()
    {
        try
        {
            CTWorldMapSerialization.EnsureRootFolder();
            File.WriteAllText(PathForSlot(), JsonConvert.SerializeObject(Data(), Formatting.Indented));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("World map: progress file could not be written: " + e.Message);
        }
    }
}
