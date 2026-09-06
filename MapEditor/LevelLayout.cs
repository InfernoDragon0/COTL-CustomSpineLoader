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
                    if (value != CTLevelRoom.Door) value = CTLevelRoom.Wall;
                    if (Get(neighbour, Opposite(side)) == CTLevelRoom.Door) value = CTLevelRoom.Door;

                    Set(room, side, value);
                    Set(neighbour, Opposite(side), value);
                    continue;
                }

                if (value == CTLevelRoom.Door) Set(room, side, CTLevelRoom.Wall);
            }
        }

        AssignEnds(level);
    }

    public static void WireRoom(CTLevelBlueprint level, CTLevelRoom room)
    {
        if (level == null || room == null) return;

        foreach (var side in Sides)
            if (Neighbour(level, room, side) != null) Set(room, side, CTLevelRoom.Door);

        Normalize(level);
    }

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

        if (!level.AuthoredLayout)
        {
            if (level.Rooms.Count < 2)
                issues.Add(new LayoutIssue(
                    "A level needs at least two rooms - one to arrive in, one with the way out.",
                    null, false));

            AddPoolAdvisory(level, issues);
            return issues;
        }

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

        if (entrances.Count == 0)
            issues.Add(new LayoutIssue(
                "The first room is boxed in; it needs a free side for the way in.",
                level.Rooms[0], false));

        if (exits.Count == 0)
            issues.Add(new LayoutIssue(
                "The last room is boxed in; it needs a free side for the way out.",
                level.Rooms[^1], false));

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

        if (biome.Rooms != null)
            foreach (var stale in biome.Rooms)
                stale?.Clear();

        biome.Rooms = [];

        var random = new System.Random(biome.Seed);
        var built = new Dictionary<(int, int), BiomeRoom>();

        foreach (var authored in level.Rooms)
        {
            if (authored == null || built.ContainsKey((authored.X, authored.Y))) continue;

            var room = new BiomeRoom(authored.X, authored.Y,
                random.Next(-2147483647, int.MaxValue), biome.GeneratorRoomPrefab.gameObject);

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

        biome.RoomEntrance = entrance;
        biome.RoomExit = exit;

        var reader = Traverse.Create(biome);
        reader.Field("StartX").SetValue(entrance.x);
        reader.Field("StartY").SetValue(entrance.y);
        reader.Field("lastRoom").SetValue(exit);

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

    [HarmonyPatch(typeof(BiomeGenerator), "CreateRandomWalk")]
    private static class BiomeGenerator_CreateRandomWalk_Patch
    {
        private static bool Prefix(BiomeGenerator __instance)
        {
            var level = LevelPlayback.CurrentLevel;
            if (!LevelPlayback.Active || !IsAuthored(level))
            {
                // Build sets OverrideRandomWalk after filling Rooms itself, so a copy of this
                // generator's settings taken afterwards (a multiplayer seed message) says "fixed
                // layout" with no OverrideRooms behind it. Vanilla would then build zero rooms and
                // never lift the fade. A random walk is wrong for the level but it is a floor.
                if (__instance.OverrideRandomWalk &&
                    (__instance.OverrideRooms == null || __instance.OverrideRooms.Count == 0))
                {
                    Plugin.Log.LogWarning("MapEditor: the generator asks for a fixed layout but has no " +
                                          "rooms for it; falling back to a random walk so the floor can build.");
                    __instance.OverrideRandomWalk = false;
                }
                return true;
            }

            try
            {
                return !Build(__instance, level);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("MapEditor: authored layout failed to build; the floor falls " +
                                    "back to a random walk: " + e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(MiniMap), "OnBiomeGenerated")]
    private static class MiniMap_OnBiomeGenerated_Patch
    {
        private static bool _lowered;

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
