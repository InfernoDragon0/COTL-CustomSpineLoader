using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.MapEditor.Tools;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// One building the player put down in a hub.
public class HubStructureRecord
{
    public string TypeName;
    public int GridX;
    public int GridY;
    public int BoundsX = 1;
    public int BoundsY = 1;
    public int Direction = 1;
    public int Rotation;

    // Where it stands, as well as which cell it claims.
    //
    // The cell is not always a cell: a building the game does not put on the grid carries the
    // sentinel (-2147483647, -2147483647) instead, and a record of that cannot be looked up in any
    // grid. The world position always resolves - the region can find the tile under a point - so it
    // is what a restore prefers, with the cell kept as the fallback for records written before this.
    public float WorldX;
    public float WorldY;
    public bool HasWorld;

    // Whether this was a finished building or still a site waiting for a follower. Restoring is done
    // by building it again, which produces a site either way; one that was already finished when the
    // player left is finished again straight away rather than asking them to pay for the labour a
    // second time.
    public bool Finished = true;
}

// One floor decoration - a path tile, a plank, a tiled floor.
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

// What the player built in a hub, per save slot, in our own file.
//
// The game would happily store these itself: a hub runs in the Woolhaven room, and Woolhaven has a
// real save list behind it. It must not be used. That list is shared by every hub and by the actual
// Woolhaven, and the game prunes it on load - entries whose grid cell is already taken are deleted
// outright, which is what two hubs sharing one list would do to each other. Worse, it is the
// player's save: a bug here would cost them their town, not their hub.
//
// So the vanilla flow runs in full - the grid, the cost, the build - and the moment the game hands
// the new building to its save list, we take it straight back out and write it down here instead.
// The building itself is untouched and works exactly as the game intends; only where it is
// remembered has changed.
public static class HubStructureStore
{
    // The location a hub's structures belong to. The hub is the Woolhaven room dressed as something
    // else, so the game's own machinery - which location manager owns the build, which layer the
    // structure is parented to - keys off this.
    public const FollowerLocation HubLocation = FollowerLocation.DLC_ShrineRoom;

    private const string RootFolder = "CustomHubStructures";

    private static readonly List<HubStructureRecord> _records = [];
    private static readonly List<HubPathRecord> _paths = [];

    // Live structures we own, so a demolition or a move can find its record again. Keyed by the
    // data object the game hands around, which is the only thing both ends agree on.
    private static readonly Dictionary<StructuresData, HubStructureRecord> _live = new();

    private static readonly List<StructureBrain> _brains = [];

    private static string _hub;
    private static int _slot = -1;
    private static bool _tearingDown;

    public static bool Active => _hub != null;

    // The user-visible slot. Saving a DLC game shifts this by ten for the length of one write, and
    // a hub file named from that would be a file the next session cannot find.
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

    // Leaving the hub. The brains are ours and the game never clears them on a scene change, so
    // they would otherwise still be listed against Woolhaven the next time the player goes there -
    // pointing at buildings that were destroyed with the hub.
    public static void TearDown()
    {
        if (_hub == null && _brains.Count == 0) return;

        StructureManager.OnStructureRemoved -= Removed;
        StructureManager.OnStructureMoved -= Moved;

        // Leaving mid-placement would otherwise leave the town's own build region unfindable.
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

    // Called from the patch that lifts a hub build back out of the game's save list.
    public static void Adopt(StructureBrain brain)
    {
        if (brain?.Data == null) return;

        if (!_brains.Contains(brain)) _brains.Add(brain);

        // A build site is a placeholder for the real thing; only what it becomes is worth
        // remembering, and that arrives here in its own right a moment later.
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

        // A build site retires itself the moment it becomes the building it was standing in for,
        // so this fires for things that were never recorded as well as for demolitions.
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

    // Put back what the player built here last time. Free of charge - it was paid for when it was
    // first placed - and through the region's own placement call, so the grid is stamped exactly as
    // a fresh build would stamp it.
    public static int Restore()
    {
        var region = HubBuildTotem.Region;
        if (region == null || _records.Count == 0) return 0;

        // The list is rebuilt from what actually lands: a cell that no longer exists (the author
        // moved the ground out from under it) drops its building rather than keeping a record that
        // can never be placed.
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

    // Mirrored wholesale rather than one at a time: the game keeps them on the region's own data,
    // and that list is already exactly what needs writing down. It is short, and it only changes
    // when the player lays or lifts a tile.
    public static void SavePaths()
    {
        var region = HubBuildTotem.Region;
        var data = region != null ? region.StructureInfo : null;
        if (data?.pathData == null || !Active) return;

        _paths.Clear();
        foreach (var path in data.pathData)
        {
            // A struct, so there is no null to check for.
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

    // Put the floor back. The drawing half is a private method on the tile manager - the same one
    // the game calls when it restores the town's own paths on load - so it is called by name.
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

    // Written on every change: a hub is left by reloading the scene, and there is no moment before
    // that which is reliably "the end".
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
