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

    public int Seed;

    public bool IsHub;

    public bool AuthoredLayout;

    public List<CTLevelRoom> Rooms = [];
}

[Serializable]
public class CTLevelRoom
{
    public const string VanillaNode = "<vanilla>";

    public const string Wall = "False";
    public const string Door = "True";
    public const string WayIn = "Entrance";
    public const string WayOut = "NextLayer";

    public const string Generated = "";
    public const string PodiumRoom = "Podium";
    public const string EndOfFloorRoom = "EndOfFloor";

    public string Role = "Normal"; // Entrance | Normal | Exit
    public List<string> NodePool = []; // CTNodeBlueprint MapNames allowed here; empty = any saved node

    public string Modifier = "None";

    public string VanillaRoom = Generated;

    // ---- authored layout ---------------------------------------------------------------------
    public int X;
    public int Y;

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
