using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.MapEditor.Tools;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class HubStructureRecord
{
    public string TypeName;
    public int GridX;
    public int GridY;
    public int BoundsX = 1;
    public int BoundsY = 1;
    public int Direction = 1;
    public int Rotation;

    public float WorldX;
    public float WorldY;
    public bool HasWorld;

    public bool Finished = true;
}

public class HubPathRecord
{
    public int TileX;
    public int TileY;
    public float WorldX;
    public float WorldY;
    public int PathID;
}

public class HubStructureFile
{
    public List<HubStructureRecord> Structures = [];
    public List<HubPathRecord> Paths = [];
}

public static class HubStructureStore
{
    public const FollowerLocation HubLocation = FollowerLocation.DLC_ShrineRoom;

    private const string RootFolder = "CustomHubStructures";

    private static readonly List<HubStructureRecord> _records = [];
    private static readonly List<HubPathRecord> _paths = [];

    private static readonly Dictionary<StructuresData, HubStructureRecord> _live = new();

    private static readonly List<StructureBrain> _brains = [];

    private static string _hub;
    private static int _slot = -1;
    private static bool _tearingDown;

    public static bool Active => _hub != null;

    private static int Slot => SaveAndLoad.SAVE_SLOT % 10;

    public static string FolderForSlot() =>
        Path.Combine(Plugin.PluginPath, RootFolder, $"slot{Slot}");

    public static string PathFor(string hubName) =>
        Path.Combine(FolderForSlot(), MapEditorSerialization.Sanitize(hubName) + ".json");

    // ---- session ------------------------------------------------------------------------------

    public static void Begin(string hubName)
    {
        TearDown();

        _hub = hubName;
        _slot = Slot;
        var file = Read(hubName);
        _records.Clear();
        _records.AddRange(file.Structures);
        _paths.Clear();
        _paths.AddRange(file.Paths);

        StructureManager.OnStructureRemoved -= Removed;
        StructureManager.OnStructureRemoved += Removed;
        StructureManager.OnStructureMoved -= Moved;
        StructureManager.OnStructureMoved += Moved;
    }

    public static void TearDown()
    {
        if (_hub == null && _brains.Count == 0) return;

        StructureManager.OnStructureRemoved -= Removed;
        StructureManager.OnStructureMoved -= Moved;

        Patches.HubBuildPatches.RestoreTownRegion();

        _tearingDown = true;
        try
        {
            foreach (var brain in _brains)
            {
                if (brain?.Data == null) continue;
                try
                {
                    StructureManager.RemoveStructure(brain);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Hub structures: one could not be unregistered: " + e.Message);
                }
            }
        }
        finally
        {
            _tearingDown = false;
        }

        _brains.Clear();
        _live.Clear();
        _records.Clear();
        _paths.Clear();
        _hub = null;
        HubBuildTotem.Forget();
    }

    // ---- bookkeeping --------------------------------------------------------------------------

    public static void Adopt(StructureBrain brain)
    {
        if (brain?.Data == null) return;

        if (!_brains.Contains(brain)) _brains.Add(brain);

        if (brain is Structures_BuildSite || brain is Structures_BuildSiteProject) return;
        if (brain.Data.Type == StructureBrain.TYPES.PLACEMENT_REGION) return;

        if (!Active)
        {
            Plugin.Log.LogWarning($"Hub structures: {brain.Data.Type} was built with no hub open; " +
                                  "it will not be remembered.");
            return;
        }

        var record = new HubStructureRecord
        {
            TypeName = StructureTool.InternalNameOf(brain.Data.Type),
            GridX = brain.Data.GridTilePosition.x,
            GridY = brain.Data.GridTilePosition.y,
            BoundsX = Mathf.Max(1, brain.Data.Bounds.x),
            BoundsY = Mathf.Max(1, brain.Data.Bounds.y),
            Direction = brain.Data.Direction,
            Rotation = brain.Data.Rotation
        };

        _records.Add(record);
        _live[brain.Data] = record;
        Save();
    }

    private static void Removed(StructuresData data)
    {
        if (_tearingDown || data == null) return;

        _brains.RemoveAll(brain => brain?.Data == data);

        if (!_live.TryGetValue(data, out var record)) return;

        _live.Remove(data);
        _records.Remove(record);
        Save();
    }

    private static void Moved(StructuresData data)
    {
        if (_tearingDown || data == null) return;
        if (!_live.TryGetValue(data, out var record)) return;

        record.GridX = data.GridTilePosition.x;
        record.GridY = data.GridTilePosition.y;
        record.Direction = data.Direction;
        record.Rotation = data.Rotation;
        Save();
    }

    // ---- restore ------------------------------------------------------------------------------

    public static int Restore()
    {
        var region = HubBuildTotem.Region;
        if (region == null || _records.Count == 0) return 0;

        var wanted = new List<HubStructureRecord>(_records);
        _records.Clear();

        var restored = 0;
        foreach (var record in wanted)
        {
            if (!StructureTool.TryResolveAnyType(record.TypeName, out var type))
            {
                Plugin.Log.LogWarning($"Hub structures: '{record.TypeName}' is not a structure this " +
                                      "game knows; it was dropped.");
                continue;
            }

            var cell = new Vector2Int(record.GridX, record.GridY);
            var tile = region.GetTileGridTile(cell);
            if (tile == null || !tile.CanPlaceStructure)
            {
                Plugin.Log.LogWarning($"Hub structures: {type} stood on a tile the hub no longer " +
                                      "has; it was dropped.");
                continue;
            }

            try
            {
                region.PlaceStructureAtGridPosition(type, cell,
                    new Vector2Int(Mathf.Max(1, record.BoundsX), Mathf.Max(1, record.BoundsY)));
                restored++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Hub structures: {type} could not be rebuilt: {e.Message}");
            }
        }

        Save();
        if (restored > 0) Plugin.Log.LogInfo($"Hub structures: {restored} rebuilt.");
        return restored;
    }

    // ---- floor decorations ----------------------------------------------------------------------

    public static void SavePaths()
    {
        var region = HubBuildTotem.Region;
        var data = region != null ? region.StructureInfo : null;
        if (data?.pathData == null || !Active) return;

        _paths.Clear();
        foreach (var path in data.pathData)
        {
            if (path.PathID == -1) continue;
            _paths.Add(new HubPathRecord
            {
                TileX = path.TilePosition.x,
                TileY = path.TilePosition.y,
                WorldX = path.WorldPosition.x,
                WorldY = path.WorldPosition.y,
                PathID = path.PathID
            });
        }

        Save();
    }

    public static int RestorePaths()
    {
        var region = HubBuildTotem.Region;
        var data = region != null ? region.StructureInfo : null;
        var manager = PathTileManager.Instance;

        if (data == null || manager == null || _paths.Count == 0) return 0;

        var draw = HarmonyLib.AccessTools.Method(typeof(PathTileManager), "SetTile",
            [typeof(Vector3), typeof(int)]);
        if (draw == null)
        {
            Plugin.Log.LogWarning("Hub paths: the game's tile drawing has changed shape; floor " +
                                  "decorations were not restored.");
            return 0;
        }

        var restored = 0;
        foreach (var path in _paths)
        {
            var world = new Vector3(path.WorldX, path.WorldY, 0f);
            try
            {
                data.SetPathData(new Vector2Int(path.TileX, path.TileY), world, path.PathID);
                draw.Invoke(manager, [world, path.PathID]);
                restored++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Hub paths: one tile could not be laid: {e.Message}");
            }
        }

        if (restored > 0) Plugin.Log.LogInfo($"Hub paths: {restored} floor tile(s) restored.");
        return restored;
    }

    // ---- storage ------------------------------------------------------------------------------

    private static HubStructureFile Read(string hubName)
    {
        try
        {
            var path = PathFor(hubName);
            if (!File.Exists(path)) return new HubStructureFile();

            return JsonConvert.DeserializeObject<HubStructureFile>(File.ReadAllText(path))
                   ?? new HubStructureFile();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Hub structures: the file could not be read: " + e.Message);
            return new HubStructureFile();
        }
    }

    private static void Save()
    {
        if (_hub == null) return;

        try
        {
            Directory.CreateDirectory(FolderForSlot());
            File.WriteAllText(PathFor(_hub),
                JsonConvert.SerializeObject(
                    new HubStructureFile { Structures = _records, Paths = _paths },
                    Formatting.Indented));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Hub structures: the file could not be written: " + e.Message);
        }
    }
}
