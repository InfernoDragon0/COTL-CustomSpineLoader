using System.Collections.Generic;
using HarmonyLib;
using MMBiomeGeneration;
using MMRoomGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public enum LevelSide
{
    North,
    East,
    South,
    West
}

// Turns an authored level grid into the biome the game actually builds.
//
// Vanilla lays a floor out by random walk: drop a room at the origin, then NumberOfRooms times pick
// a random room already placed and a random one of four directions, adding a neighbour wherever the
// cell is free (BiomeGenerator.CreateRandomWalk). Entrance and exit are then the two furthest-apart
// dead ends of whatever that produced. An author gets to say how many rooms and what is inside
// them, and nothing at all about the shape.
//
// The game already has the switch for authoring a shape instead: `OverrideRandomWalk`. It is not a
// back door - MapManager.EnterNode sets it for every dungeon-map node that is not a random floor,
// and the DLC intro dungeon runs on it. Setting it makes every procedural stage stand down where it
// starts: PlaceEntranceAndExit, GetCriticalPath, PlaceLockAndKey, PlaceStoryRooms,
// PlaceDynamicCustomRooms and PlaceFixedCustomRooms all check it and return.
//
// So this builds the room graph itself and raises that flag, and the rest of generation carries on
// as normal on top of it. Room shape and doors follow from the connection types alone -
// GenerateRoom.Generate(seed, N, E, S, W) walks CreatePaths and PlaceDoors off them - so authoring
// the sides is authoring the room.
//
// What it does NOT do is use vanilla's own `OverrideRooms` list. That path marks every room custom
// and loads a prefab by Addressable path for it, which is right for the fixed prefab rooms it was
// built for and wrong here: an authored level wants ordinary generated rooms, with islands and
// doors, that a blueprint is then pasted onto. Those are the rooms InstantiatePrefabs makes for
// anything left `IsCustom == false`.
public static class LevelLayout
{
    // ---- reading a layout ------------------------------------------------------------------------

    public static bool IsAuthored(CTLevelBlueprint level) =>
        level is { AuthoredLayout: true, Rooms.Count: > 0 };

    public static CTLevelRoom At(CTLevelBlueprint level, int x, int y)
    {
        if (level == null) return null;

        foreach (var room in level.Rooms)
            if (room != null && room.X == x && room.Y == y) return room;

        return null;
    }

    // The room's index in the level, which is also its slot for playback. -1 for a cell the level
    // has nothing on.
    public static int SlotFor(CTLevelBlueprint level, int x, int y)
    {
        if (level == null) return -1;

        for (var i = 0; i < level.Rooms.Count; i++)
        {
            var room = level.Rooms[i];
            if (room != null && room.X == x && room.Y == y) return i;
        }

        return -1;
    }

    public static Vector2Int Delta(LevelSide side) => side switch
    {
        LevelSide.North => new Vector2Int(0, 1),
        LevelSide.East => new Vector2Int(1, 0),
        LevelSide.South => new Vector2Int(0, -1),
        _ => new Vector2Int(-1, 0)
    };

    public static LevelSide Opposite(LevelSide side) => side switch
    {
        LevelSide.North => LevelSide.South,
        LevelSide.East => LevelSide.West,
        LevelSide.South => LevelSide.North,
        _ => LevelSide.East
    };

    public static readonly LevelSide[] Sides =
        [LevelSide.North, LevelSide.East, LevelSide.South, LevelSide.West];

    // North is +Y and east is +X, which on a grid drawn face-on is up and right. The compass is the
    // biome's own vocabulary and stays in the data; on screen it only ever confused the issue.
    public static string DescribeSide(LevelSide side) => side switch
    {
        LevelSide.North => "Up",
        LevelSide.East => "Right",
        LevelSide.South => "Down",
        _ => "Left"
    };

    public static string Get(CTLevelRoom room, LevelSide side)
    {
        if (room == null) return CTLevelRoom.Wall;

        return side switch
        {
            LevelSide.North => room.North,
            LevelSide.East => room.East,
            LevelSide.South => room.South,
            _ => room.West
        } ?? CTLevelRoom.Wall;
    }

    public static void Set(CTLevelRoom room, LevelSide side, string value)
    {
        if (room == null) return;
        value ??= CTLevelRoom.Wall;

        switch (side)
        {
            case LevelSide.North: room.North = value; break;
            case LevelSide.East: room.East = value; break;
            case LevelSide.South: room.South = value; break;
            default: room.West = value; break;
        }
    }

    // Sets a side and the side facing it back, together.
    //
    // A door is a thing two rooms share, and Normalize settles disagreements with "if either says
    // door, both do" - which is what lets a room dragged next to another open the door from one
    // side only. The cost is that closing one from a single side can never take: the neighbour is
    // still saying door, so the very next pass puts it back. Every deliberate change to a door goes
    // through here so both rooms say the same thing before Normalize is asked anything.
    public static void SetShared(CTLevelBlueprint level, CTLevelRoom room, LevelSide side, string value)
    {
        Set(room, side, value);

        var neighbour = Neighbour(level, room, side);
        if (neighbour != null) Set(neighbour, Opposite(side), value);
    }

    public static CTLevelRoom Neighbour(CTLevelBlueprint level, CTLevelRoom room, LevelSide side)
    {
        if (room == null) return null;

        var step = Delta(side);
        return At(level, room.X + step.x, room.Y + step.y);
    }

    // ---- keeping a layout honest ------------------------------------------------------------------

    // The rules a grid has to obey for the generator to make sense of it, re-applied after every
    // edit rather than defended at each call site: a room can be moved, and a move changes which of
    // its sides face a neighbour and which face open grid.
    //
    //   - a side facing a neighbour is a Door or a Wall, never a way in or out (the doorway would
    //     open onto the next room's floor rather than off the map)
    //   - a side facing open grid is a Wall, the way in, or the way out (a plain Door there is a
    //     doorway onto open water, and GenerateRoom would build one)
    //   - a Door is agreed by both rooms: if either side says door, both do
    public static void Normalize(CTLevelBlueprint level)
    {
        if (level == null) return;

        foreach (var room in level.Rooms)
        {
            if (room == null) continue;

            foreach (var side in Sides)
            {
                var neighbour = Neighbour(level, room, side);
                var value = Get(room, side);

                if (neighbour != null)
                {
                    // Facing a room: only door or wall, and the neighbour agrees.
                    if (value != CTLevelRoom.Door) value = CTLevelRoom.Wall;
                    if (Get(neighbour, Opposite(side)) == CTLevelRoom.Door) value = CTLevelRoom.Door;

                    Set(room, side, value);
                    Set(neighbour, Opposite(side), value);
                    continue;
                }

                // Facing nothing: a door there leads off the map.
                if (value == CTLevelRoom.Door) Set(room, side, CTLevelRoom.Wall);
            }
        }

        AssignEnds(level);
    }

    // Opens a door from this room to everything it touches. Called when a room is placed or moved,
    // which is the moment "which rooms are next to each other" changes - working it out by hand
    // afterwards is bookkeeping the arrangement already answers.
    //
    // Only ever this room's sides, so a door deliberately closed between two rooms elsewhere is not
    // re-opened by dragging a third one about.
    public static void WireRoom(CTLevelBlueprint level, CTLevelRoom room)
    {
        if (level == null || room == null) return;

        foreach (var side in Sides)
            if (Neighbour(level, room, side) != null) Set(room, side, CTLevelRoom.Door);

        Normalize(level);
    }

    // The way in goes on the first room and the way out on the last, on a side facing open grid.
    // Derived rather than set: they are a property of where a room sits in the level, and leaving
    // them to be placed by hand meant a level could be dragged into a shape with two ways in, or
    // none, and only find out at the badge.
    //
    // Preferences match vanilla's own: it puts an entrance on the south side where it can, and the
    // end-of-floor door on the north.
    private static readonly LevelSide[] WayInOrder =
        [LevelSide.South, LevelSide.West, LevelSide.East, LevelSide.North];

    private static readonly LevelSide[] WayOutOrder =
        [LevelSide.North, LevelSide.East, LevelSide.West, LevelSide.South];

    private static void AssignEnds(CTLevelBlueprint level)
    {
        foreach (var room in level.Rooms)
        {
            if (room == null) continue;

            foreach (var side in Sides)
            {
                var value = Get(room, side);
                if (value == CTLevelRoom.WayIn || value == CTLevelRoom.WayOut)
                    Set(room, side, CTLevelRoom.Wall);
            }
        }

        if (level.Rooms.Count == 0) return;

        // A one-room level is both ends at once; PlaceEnd skips a side already carrying the other.
        PlaceEnd(level, level.Rooms[0], CTLevelRoom.WayIn, WayInOrder);
        PlaceEnd(level, level.Rooms[^1], CTLevelRoom.WayOut, WayOutOrder);
    }

    private static void PlaceEnd(CTLevelBlueprint level, CTLevelRoom room, string value,
        LevelSide[] order)
    {
        if (room == null) return;

        foreach (var side in order)
        {
            if (Neighbour(level, room, side) != null) continue;
            if (Get(room, side) != CTLevelRoom.Wall) continue;

            Set(room, side, value);
            return;
        }
    }

    // Which side carries the way in or the way out, for a room that has one.
    public static LevelSide? SideCarrying(CTLevelRoom room, string value)
    {
        if (room == null) return null;

        foreach (var side in Sides)
            if (Get(room, side) == value) return side;

        return null;
    }

    // ---- validation --------------------------------------------------------------------------------

    public readonly struct LayoutIssue
    {
        public readonly string Message;
        public readonly CTLevelRoom Room;
        public readonly bool IsAdvisory;

        public LayoutIssue(string message, CTLevelRoom room, bool advisory)
        {
            Message = message;
            Room = room;
            IsAdvisory = advisory;
        }
    }

    public static List<LayoutIssue> Issues(CTLevelBlueprint level)
    {
        var issues = new List<LayoutIssue>();
        if (level == null) return issues;

        if (level.Rooms.Count == 0)
        {
            issues.Add(new LayoutIssue("The level has no rooms.", null, false));
            return issues;
        }

        // Without a layout there is nothing to check about the shape, because there is no shape: the
        // game rolls one, places its own entrance and end-of-floor rooms, and the blueprints are
        // dealt onto whatever it produced. All that is required is somewhere to start and somewhere
        // to finish - which here is the first room and the last.
        if (!level.AuthoredLayout)
        {
            if (level.Rooms.Count < 2)
                issues.Add(new LayoutIssue(
                    "A level needs at least two rooms - one to arrive in, one with the way out.",
                    null, false));

            AddPoolAdvisory(level, issues);
            return issues;
        }

        // Two rooms on one cell is the one thing the generator cannot survive: BiomeRoom.GetRoom
        // returns the first, so the second is built, never reachable, and never freed.
        var seen = new Dictionary<(int, int), CTLevelRoom>();
        foreach (var room in level.Rooms)
        {
            if (room == null) continue;
            if (seen.ContainsKey((room.X, room.Y)))
                issues.Add(new LayoutIssue($"Two rooms share the cell ({room.X}, {room.Y}).", room, false));
            else seen[(room.X, room.Y)] = room;
        }

        var entrances = Rooms(level, CTLevelRoom.WayIn);
        var exits = Rooms(level, CTLevelRoom.WayOut);

        // Both doors are placed for the author, so the only way to be missing one is a room with no
        // side facing open grid - it is walled in by its own neighbours.
        if (entrances.Count == 0)
            issues.Add(new LayoutIssue(
                "The first room is boxed in; it needs a free side for the way in.",
                level.Rooms[0], false));

        if (exits.Count == 0)
            issues.Add(new LayoutIssue(
                "The last room is boxed in; it needs a free side for the way out.",
                level.Rooms[^1], false));

        // Unreachable rooms are the failure this whole grid exists to make visible: the level runs,
        // and part of it is simply never seen.
        if (entrances.Count > 0)
        {
            var reached = Reachable(level, entrances[0]);
            foreach (var room in level.Rooms)
            {
                if (room == null || reached.Contains(room)) continue;
                issues.Add(new LayoutIssue($"The room at ({room.X}, {room.Y}) cannot be walked to.",
                    room, false));
            }

            foreach (var exit in exits)
            {
                if (reached.Contains(exit)) continue;
                issues.Add(new LayoutIssue("The way out cannot be walked to.", exit, false));
            }
        }

        // The two prefab rooms have a place they belong. Putting them elsewhere works - the podium
        // room is a room with podiums in it wherever it stands - so this says so rather than
        // refusing it.
        foreach (var room in level.Rooms)
        {
            if (room == null || room.VanillaRoom == CTLevelRoom.Generated) continue;

            var wants = room.VanillaRoom == CTLevelRoom.PodiumRoom
                ? CTLevelRoom.WayIn
                : CTLevelRoom.WayOut;

            var placed = false;
            foreach (var side in Sides)
                if (Get(room, side) == wants) placed = true;

            if (placed) continue;

            issues.Add(new LayoutIssue(
                room.VanillaRoom == CTLevelRoom.PodiumRoom
                    ? "The podium room is not the room the player arrives in."
                    : "The end-of-floor room is not the room with the way out.",
                room, true));
        }

        AddPoolAdvisory(level, issues);
        return issues;
    }

    // A level where nothing is bound plays as a string of vanilla rooms. That is a legitimate thing
    // to author, so it is worth saying and not worth refusing.
    private static void AddPoolAdvisory(CTLevelBlueprint level, List<LayoutIssue> issues)
    {
        var bound = 0;
        foreach (var room in level.Rooms)
            if (room != null && (room.NodePool.Count > 0 ||
                                 (level.AuthoredLayout && room.VanillaRoom != CTLevelRoom.Generated)))
                bound++;

        if (bound == 0)
            issues.Add(new LayoutIssue("No room names a blueprint; every room plays whatever is saved.",
                null, true));
    }

    public static string Validate(CTLevelBlueprint level)
    {
        foreach (var issue in Issues(level))
            if (!issue.IsAdvisory) return issue.Message;

        return null;
    }

    public static List<CTLevelRoom> Rooms(CTLevelBlueprint level, string sideValue)
    {
        var found = new List<CTLevelRoom>();
        if (level == null) return found;

        foreach (var room in level.Rooms)
        {
            if (room == null) continue;

            foreach (var side in Sides)
            {
                if (Get(room, side) != sideValue) continue;
                found.Add(room);
                break;
            }
        }

        return found;
    }

    private static HashSet<CTLevelRoom> Reachable(CTLevelBlueprint level, CTLevelRoom from)
    {
        var reached = new HashSet<CTLevelRoom>();
        if (from == null) return reached;

        var queue = new Queue<CTLevelRoom>();
        queue.Enqueue(from);
        reached.Add(from);

        while (queue.Count > 0)
        {
            var room = queue.Dequeue();

            foreach (var side in Sides)
            {
                if (Get(room, side) != CTLevelRoom.Door) continue;

                var next = Neighbour(level, room, side);
                if (next == null || !reached.Add(next)) continue;
                queue.Enqueue(next);
            }
        }

        return reached;
    }

    // ---- building the biome -------------------------------------------------------------------------

    // Which prefab a room's role resolves to in this biome, or null for an ordinary generated room.
    // Read off the biome rather than hardcoded, so "the podium room" means the right room in every
    // dungeon a level can be played in.
    public static string PrefabPathFor(BiomeGenerator biome, string role)
    {
        if (biome == null || string.IsNullOrEmpty(role)) return null;

        var path = role switch
        {
            CTLevelRoom.PodiumRoom => biome.EntranceRoomPath,
            CTLevelRoom.EndOfFloorRoom => biome.EndOfFloorRoomPath,
            _ => null
        };

        if (string.IsNullOrEmpty(path)) return null;

        // Vanilla's own second-layer swap: the biome carries one path per room and the harder
        // variant of it is the same file with _P2 on the end.
        if (GameManager.Layer2 && !path.Contains("_P2.prefab"))
            path = path.Replace(".prefab", "_P2.prefab");

        return path;
    }

    public static string DescribeRole(string role) => role switch
    {
        CTLevelRoom.PodiumRoom => "weapon podiums",
        CTLevelRoom.EndOfFloorRoom => "end of floor",
        _ => "generated"
    };

    private static GenerateRoom.ConnectionTypes Parse(string value)
    {
        if (!string.IsNullOrEmpty(value) &&
            System.Enum.TryParse<GenerateRoom.ConnectionTypes>(value, out var parsed))
            return parsed;

        return GenerateRoom.ConnectionTypes.False;
    }

    // True when this took over; false leaves the vanilla walk to run.
    private static bool Build(BiomeGenerator biome, CTLevelBlueprint level)
    {
        if (biome == null || !IsAuthored(level)) return false;

        if (biome.GeneratorRoomPrefab == null)
        {
            Plugin.Log.LogWarning("MapEditor: the biome has no room prefab to build an authored " +
                                  "layout from; falling back to the random walk.");
            return false;
        }

        Normalize(level);

        var problem = Validate(level);
        if (problem != null)
        {
            Plugin.Log.LogWarning($"MapEditor: level '{level.LevelName}' has an unusable layout " +
                                  $"({problem}); falling back to the random walk.");
            return false;
        }

        // Vanilla's own first act, and not optional: BiomeRoom registers itself in a static list on
        // construction, so rooms left over from the previous floor would still answer GetRoom.
        if (biome.Rooms != null)
            foreach (var stale in biome.Rooms)
                stale?.Clear();

        biome.Rooms = [];

        // Per-room seeds off the biome's, so a given biome seed rebuilds the same rooms. The
        // decoration and encounter passes read these.
        var random = new System.Random(biome.Seed);
        var built = new Dictionary<(int, int), BiomeRoom>();

        foreach (var authored in level.Rooms)
        {
            if (authored == null || built.ContainsKey((authored.X, authored.Y))) continue;

            var room = new BiomeRoom(authored.X, authored.Y,
                random.Next(-2147483647, int.MaxValue), biome.GeneratorRoomPrefab.gameObject);

            // A room that is a vanilla prefab is loaded by path instead of stamped out of the
            // generator prefab. IsCustom is what InstantiatePrefabs reads to do that; Generated
            // stays false so the prefab's own GenerateRoom still builds the island and the doors
            // around what the prefab brought with it - which is how vanilla places both of these.
            var prefab = PrefabPathFor(biome, authored.VanillaRoom);
            if (!string.IsNullOrEmpty(prefab))
            {
                room.IsCustom = true;
                room.Generated = false;
                room.GameObjectPath = prefab;
            }

            biome.Rooms.Add(room);
            built[(authored.X, authored.Y)] = room;
        }

        foreach (var authored in level.Rooms)
        {
            if (authored == null || !built.TryGetValue((authored.X, authored.Y), out var room)) continue;

            foreach (var side in Sides)
            {
                var step = Delta(side);
                built.TryGetValue((authored.X + step.x, authored.Y + step.y), out var neighbour);

                var type = Parse(Get(authored, side));

                // A connection that names a room carries it; one that names nothing is a door off
                // the map, which is what the way in and the way out are.
                var connection = neighbour != null
                    ? new RoomConnection(neighbour)
                    : new RoomConnection(type);
                connection.SetConnection(type);

                Assign(room, side, connection);
            }
        }

        var entranceRoom = Rooms(level, CTLevelRoom.WayIn)[0];
        var exitRooms = Rooms(level, CTLevelRoom.WayOut);

        var entrance = built[(entranceRoom.X, entranceRoom.Y)];
        var exit = exitRooms.Count > 0 ? built[(exitRooms[0].X, exitRooms[0].Y)] : entrance;

        // What PlaceEntranceAndExit would have set. These are not bookkeeping: Door and
        // Interaction_BiomeDoor both dereference RoomEntrance without checking it, and the room the
        // floor starts in is read straight out of StartX/StartY.
        biome.RoomEntrance = entrance;
        biome.RoomExit = exit;

        var reader = Traverse.Create(biome);
        reader.Field("StartX").SetValue(entrance.x);
        reader.Field("StartY").SetValue(entrance.y);
        reader.Field("lastRoom").SetValue(exit);

        // The flag the rest of generation reads to leave the layout alone.
        biome.OverrideRandomWalk = true;
        biome.NumberOfRooms = biome.Rooms.Count;

        Plugin.Log.LogInfo($"MapEditor: built '{level.LevelName}' as an authored layout - " +
                           $"{biome.Rooms.Count} room(s), starting at ({entrance.x}, {entrance.y}).");
        return true;
    }

    private static void Assign(BiomeRoom room, LevelSide side, RoomConnection connection)
    {
        switch (side)
        {
            case LevelSide.North: room.N_Room = connection; break;
            case LevelSide.East: room.E_Room = connection; break;
            case LevelSide.South: room.S_Room = connection; break;
            default: room.W_Room = connection; break;
        }
    }

    // ---- patches --------------------------------------------------------------------------------------

    // The one hook, and deliberately this one rather than the dungeon's OnBiomeReady: a floor
    // entered from a dungeon map is regenerated in place (MapManager.EnterNode -> Regenerate), which
    // runs GenerateRoutine again without the biome ever being enabled a second time. CreateRandomWalk
    // is the first thing GenerateRoutine does, on both paths.
    [HarmonyPatch(typeof(BiomeGenerator), "CreateRandomWalk")]
    private static class BiomeGenerator_CreateRandomWalk_Patch
    {
        private static bool Prefix(BiomeGenerator __instance)
        {
            var level = LevelPlayback.CurrentLevel;
            if (!LevelPlayback.Active || !IsAuthored(level)) return true;

            try
            {
                // Skipping the original is the whole point: it is the random walk.
                return !Build(__instance, level);
            }
            catch (System.Exception e)
            {
                // A throw here would take the generation coroutine down and leave a black room.
                // The vanilla walk is a worse level than the one that was authored, and an
                // enormously better one than no level at all.
                Plugin.Log.LogError("MapEditor: authored layout failed to build; the floor falls " +
                                    "back to a random walk: " + e);
                return true;
            }
        }
    }

    // OverrideRandomWalk hides the minimap, which is right for what vanilla uses the flag for - a
    // node whose whole floor is one room has no map worth drawing. An authored level is a floor like
    // any other and wants its map back.
    [HarmonyPatch(typeof(MiniMap), "OnBiomeGenerated")]
    private static class MiniMap_OnBiomeGenerated_Patch
    {
        private static bool _lowered;

        // Lower the flag for the length of the original call so it draws the rooms rather than
        // taking its early exit, then put it straight back - everything else that reads it, and
        // there is a lot, still needs it raised.
        private static void Prefix()
        {
            _lowered = false;

            var biome = BiomeGenerator.Instance;
            if (biome == null || !biome.OverrideRandomWalk) return;
            if (!LevelPlayback.Active || !IsAuthored(LevelPlayback.CurrentLevel)) return;

            biome.OverrideRandomWalk = false;
            _lowered = true;
        }

        private static void Postfix()
        {
            if (!_lowered) return;
            _lowered = false;

            if (BiomeGenerator.Instance != null) BiomeGenerator.Instance.OverrideRandomWalk = true;
        }
    }
}
