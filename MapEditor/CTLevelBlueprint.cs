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

    public List<CTLevelRoom> Rooms = [];
}

[Serializable]
public class CTLevelRoom
{
    public const string VanillaNode = "<vanilla>";

    public string Role = "Normal"; // Entrance | Normal | Exit
    public List<string> NodePool = []; // CTNodeBlueprint MapNames allowed here; empty = any saved node

    public string Modifier = "None";
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
