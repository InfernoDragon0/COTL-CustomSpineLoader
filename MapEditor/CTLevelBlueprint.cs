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

    public static bool Exists(string levelName) => File.Exists(PathFor(levelName));

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

    public static List<CTLevelBlueprint> LoadAll()
    {
        var results = new List<CTLevelBlueprint>();

        try
        {
            if (!Directory.Exists(RootPath)) return results;

            foreach (var file in Directory.GetFiles(RootPath, "*.json", SearchOption.TopDirectoryOnly))
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
