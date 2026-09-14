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

    /// Takes a node OUT of the completed set. Downstream nodes need no bookkeeping - state is
    /// derived by the resolver each refresh, so re-locking follows on its own. A key node also
    /// un-banks what it granted, clamped at zero: keys already spent on a lock cannot be clawed
    /// back, and that lock stays open, because slamming shut a door the player paid for reaches
    /// well beyond the node that was named.
    public static bool Uncomplete(CTWorldMap map, string nodeId)
    {
        if (map == null || string.IsNullOrEmpty(nodeId)) return false;

        var record = For(map.MapName);

        var index = record.CompletedNodes.FindIndex(
            id => string.Equals(id, nodeId, StringComparison.OrdinalIgnoreCase));
        var banked = record.KeyNodesBanked.FindIndex(
            id => string.Equals(id, nodeId, StringComparison.OrdinalIgnoreCase));

        if (index < 0 && banked < 0) return false;

        if (index >= 0) record.CompletedNodes.RemoveAt(index);

        if (banked >= 0)
        {
            record.KeyNodesBanked.RemoveAt(banked);

            var node = map.FindNode(nodeId);
            var granted = node != null ? Math.Max(0, node.KeysGranted) : 0;
            record.KeysHeld = Math.Max(0, record.KeysHeld - granted);
        }

        Plugin.Log.LogInfo($"World map '{map.MapName}': node '{nodeId}' un-completed, " +
                           $"{record.KeysHeld} key(s) held.");
        Save();
        return true;
    }

    /// False when the lock is already open or the player cannot pay. The guard lives here rather
    /// than in the caller so a second caller cannot forget it; the map screen refuses earlier and
    /// so never reaches this.
    public static bool OpenLock(CTWorldMap map, CTWorldMapNode node)
    {
        if (map == null || node == null) return false;

        var record = For(map.MapName);
        if (record.IsLockOpened(node.Id)) return false;

        var cost = Math.Max(0, node.KeysCost);
        if (record.KeysHeld < cost)
        {
            Plugin.Log.LogInfo($"World map '{map.MapName}': lock '{node.Id}' costs {cost} key(s), " +
                               $"{record.KeysHeld} held.");
            return false;
        }

        record.KeysHeld -= cost;
        record.OpenedLocks.Add(node.Id);
        Plugin.Log.LogInfo($"World map '{map.MapName}': lock '{node.Id}' opened, " +
                           $"{record.KeysHeld} key(s) left.");
        Save();
        return true;
    }

    /// Shuts an opened lock and refunds what it cost - the exact inverse of OpenLock, and unlike
    /// un-completing a key node there is nothing ambiguous to decide.
    public static bool CloseLock(CTWorldMap map, CTWorldMapNode node)
    {
        if (map == null || node == null) return false;

        var record = For(map.MapName);

        var index = record.OpenedLocks.FindIndex(
            id => string.Equals(id, node.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        record.OpenedLocks.RemoveAt(index);
        record.KeysHeld += Math.Max(0, node.KeysCost);

        Plugin.Log.LogInfo($"World map '{map.MapName}': lock '{node.Id}' closed, " +
                           $"{record.KeysHeld} key(s) held.");
        Save();
        return true;
    }

    /// The wallet, set directly. Needed because For() hands back the live record while Save() is
    /// private - writing KeysHeld on it looks like it works and is lost at the next slot change.
    public static void SetKeys(string mapName, int keys)
    {
        if (string.IsNullOrEmpty(mapName)) return;

        For(mapName).KeysHeld = Math.Max(0, keys);
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
