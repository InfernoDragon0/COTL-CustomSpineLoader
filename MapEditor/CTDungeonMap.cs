using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CustomSpineLoader.MapEditor;

// One authored adventure map, stored as a grid: the renderer lays nodes out at point * 300 plus
// jitter, so anything finer than a cell would be discarded.
public class CTDungeonMap
{
    public string MapName = "untitledmap";

    // Layers are rows. y 0 is where the run starts; the highest y is the boss end.
    public int Layers = 5;
    public int Columns = 5;

    // The Unity scene the dungeon runs in; json-only, no control in the tool.
    public string SceneName = "Dungeon1";

    public List<CTDungeonMapNode> Nodes = [];

    // The floor the player arrives in; validation keeps layer 0 to a single node.
    public CTDungeonMapNode StartNode()
    {
        foreach (var node in Nodes)
            if (node != null && node.Y == 0) return node;

        return null;
    }

    public CTDungeonMapNode NodeAt(int x, int y)
    {
        foreach (var node in Nodes)
            if (node != null && node.X == x && node.Y == y) return node;

        return null;
    }
}

public class CTDungeonMapNode
{
    public int X;
    public int Y;

    // Map.NodeType by name: a stored int would silently shift when the game renumbers the enum.
    public string NodeType = "MinorEnemy";

    // A CTLevelBlueprint by name, played on entry. Empty = the node's vanilla generation.
    public string Level = "";

    // Nodes one layer up that this one leads to; incoming is rebuilt from these, never stored.
    public List<CTDungeonMapLink> Outgoing = [];

    public bool LinksTo(int x, int y)
    {
        foreach (var link in Outgoing)
            if (link != null && link.X == x && link.Y == y) return true;

        return false;
    }
}

public class CTDungeonMapLink
{
    public int X;
    public int Y;
}

public static class CTDungeonMapSerialization
{
    public const string FolderName = "CustomDungeonMaps";

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    public static string PathFor(string mapName) =>
        Path.Combine(RootPath, MapEditorSerialization.Sanitize(mapName) + ".json");

    public static bool Exists(string mapName) => File.Exists(PathFor(mapName));

    public static string Save(CTDungeonMap map)
    {
        if (map == null) return null;

        map.MapName = MapEditorSerialization.Sanitize(map.MapName);

        try
        {
            if (!Directory.Exists(RootPath)) Directory.CreateDirectory(RootPath);

            var path = PathFor(map.MapName);
            File.WriteAllText(path, JsonConvert.SerializeObject(map, Formatting.Indented));
            Plugin.Log.LogInfo($"MapEditor: saved dungeon map '{map.MapName}' with {map.Nodes.Count} node(s) to {path}");
            return path;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: failed to save dungeon map: " + e);
            return null;
        }
    }

    public static bool Delete(string mapName)
    {
        try
        {
            var path = PathFor(mapName);
            if (!File.Exists(path)) return false;

            File.Delete(path);
            Plugin.Log.LogInfo($"MapEditor: deleted dungeon map '{mapName}'.");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: failed to delete dungeon map: " + e);
            return false;
        }
    }

    public static List<CTDungeonMap> LoadAll()
    {
        var results = new List<CTDungeonMap>();

        try
        {
            if (!Directory.Exists(RootPath)) return results;

            foreach (var file in Directory.GetFiles(RootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var map = JsonConvert.DeserializeObject<CTDungeonMap>(File.ReadAllText(file));
                    if (map != null) results.Add(map);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"MapEditor: could not parse dungeon map '{file}': {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: dungeon map scan failed: " + e);
        }

        return results;
    }
}
