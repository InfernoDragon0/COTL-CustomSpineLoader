using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CustomSpineLoader.MapEditor;

[Serializable]
public class CTLevelBlueprint
{
    public string LevelName = "UntitledLevel";
    public string SceneName = "Dungeon1";

    // 0 = roll a fresh seed per run; anything else makes room pool picks deterministic.
    public int Seed;

    // A hub is a level of one safe room: nothing spawns, the room never locks, and its exit takes
    // the player home rather than ending a run. See README "Hubs".
    public bool IsHub;

    // True when the rooms below carry an authored grid - a cell each and a door on each side -
    // which replaces vanilla's random walk with exactly that shape. False is the original
    // behaviour: the game lays out however many rooms it likes and the blueprints are dealt onto
    // them in the order the player finds them. Legacy blueprints deserialise with it false and
    // keep running the way they always did. See README "Authored layouts".
    public bool AuthoredLayout;

    public List<CTLevelRoom> Rooms = [];
}

[Serializable]
public class CTLevelRoom
{
    public const string VanillaNode = "<vanilla>";

    // The four things a side of a room can be. These are names of GenerateRoom.ConnectionTypes,
    // stored as text so a blueprint stays readable and survives the enum gaining members.
    public const string Wall = "False";
    public const string Door = "True";
    public const string WayIn = "Entrance";
    public const string WayOut = "NextLayer";

    // The rooms the game keeps as finished prefabs rather than generating: the one with the weapon
    // podiums that a run starts in, and the one at the end of a floor. Their paths are per-biome
    // fields (BiomeGenerator.EntranceRoomPath / EndOfFloorRoomPath), so what these resolve to
    // depends on which dungeon the level is played in - which is the point of naming them by role.
    public const string Generated = "";
    public const string PodiumRoom = "Podium";
    public const string EndOfFloorRoom = "EndOfFloor";

    public string Role = "Normal"; // Entrance | Normal | Exit
    public List<string> NodePool = []; // CTNodeBlueprint MapNames allowed here; empty = any saved node

    public string Modifier = "None";

    // Which vanilla prefab this room is, if any. Empty is the default and means an ordinary
    // generated room - the kind a blueprint gets pasted onto. A room that is a prefab keeps what
    // the prefab put in it and its NodePool is ignored.
    public string VanillaRoom = Generated;

    // ---- authored layout ---------------------------------------------------------------------
    // Only meaningful when the level's AuthoredLayout is set. The cell this room stands in; the
    // grid is the biome's own, where north is +Y and east is +X.
    public int X;
    public int Y;

    // What each side is. A side facing another authored room is Wall or Door; a side facing open
    // grid is Wall, WayIn (the door the player arrives through) or WayOut (the door that ends the
    // floor). LevelLayout.Normalize keeps that true after every edit.
    public string North = Wall;
    public string East = Wall;
    public string South = Wall;
    public string West = Wall;
}

public static class CTLevelSerialization
{
    public const string FolderName = "CustomLevelBlueprints";

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    public static string PathFor(string levelName) =>
        Path.Combine(RootPath, MapEditorSerialization.Sanitize(levelName) + ".json");

    // Exists = ours, the overwrite question. Available = ours or any other mod's, the load question.
    public static bool Exists(string levelName) => File.Exists(PathFor(levelName));

    public static bool Available(string levelName) => ReadPathFor(levelName) != null;

    private static string ReadPathFor(string levelName) =>
        APIHelper.ModContentPaths.FindFile(FolderName,
            MapEditorSerialization.Sanitize(levelName) + ".json");

    public static string Save(CTLevelBlueprint level)
    {
        if (level == null) return null;

        level.LevelName = MapEditorSerialization.Sanitize(level.LevelName);

        try
        {
            if (!Directory.Exists(RootPath)) Directory.CreateDirectory(RootPath);

            var path = PathFor(level.LevelName);
            File.WriteAllText(path, JsonConvert.SerializeObject(level, Formatting.Indented));
            Plugin.Log.LogInfo($"MapEditor: saved level '{level.LevelName}' with {level.Rooms.Count} room(s) to {path}");
            return path;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: failed to save level blueprint: " + e);
            return null;
        }
    }

    public static CTLevelBlueprint LoadByName(string levelName)
    {
        var path = ReadPathFor(levelName);
        if (path == null) return null;

        try
        {
            return JsonConvert.DeserializeObject<CTLevelBlueprint>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"MapEditor: could not parse level blueprint '{path}': {e.Message}");
            return null;
        }
    }

    public static List<CTLevelBlueprint> LoadAll()
    {
        var results = new List<CTLevelBlueprint>();

        try
        {
            foreach (var file in APIHelper.ModContentPaths.FilesIn(FolderName, "*.json"))
            {
                try
                {
                    var level = JsonConvert.DeserializeObject<CTLevelBlueprint>(File.ReadAllText(file));
                    if (level != null) results.Add(level);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"MapEditor: could not parse level blueprint '{file}': {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: level blueprint scan failed: " + e);
        }

        return results;
    }
}
