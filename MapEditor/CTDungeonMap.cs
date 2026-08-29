using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CustomSpineLoader.MapEditor;

public class CTDungeonMap
{
    public string MapName = "untitledmap";

    public string SceneName = "Dungeon1";

    public List<CTDungeonMapNode> Nodes = [];

    public int Layers;
    public int Columns;

    public bool ShouldSerializeLayers() => false;
    public bool ShouldSerializeColumns() => false;

    public CTDungeonMapNode FindNode(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        foreach (var node in Nodes)
            if (node != null && string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase))
                return node;

        return null;
    }

    public CTDungeonMapNode StartNode()
    {
        var rows = DungeonMapBuilder.Rows(this);
        return rows.Count > 0 && rows[0].Count > 0 ? rows[0][0] : null;
    }

    private const float LegacyPitch = 160f;

    private static readonly HashSet<string> Reported = new(StringComparer.OrdinalIgnoreCase);

    public void Migrate()
    {
        var legacy = false;
        foreach (var node in Nodes)
            if (node != null && string.IsNullOrEmpty(node.Id)) legacy = true;

        if (!legacy) return;

        var byCell = new Dictionary<int, CTDungeonMapNode>();
        var minted = 1;

        foreach (var node in Nodes)
        {
            if (node == null) continue;

            if (string.IsNullOrEmpty(node.Id)) node.Id = "node" + minted++;

            node.PosX = (node.X - Math.Max(0, Columns - 1) * 0.5f) * LegacyPitch;
            node.PosY = (node.Y - Math.Max(0, Layers - 1) * 0.5f) * LegacyPitch;

            byCell[node.X * 1000 + node.Y] = node;
        }

        foreach (var node in Nodes)
        {
            if (node == null) continue;

            foreach (var link in node.Outgoing)
            {
                if (link == null) continue;
                if (byCell.TryGetValue(link.X * 1000 + link.Y, out var target) && target != null)
                    node.Children.Add(target.Id);
            }

            node.Outgoing.Clear();
        }

        if (Reported.Add(MapName ?? ""))
            Plugin.Log.LogInfo($"MapEditor: dungeon map '{MapName}' was on the old grid; " +
                               $"{Nodes.Count} node(s) moved onto free positions.");
    }
}

public class CTDungeonMapNode
{
    public string Id = "";

    public float PosX;
    public float PosY;

    public string NodeType = "MinorEnemy";

    public string Level = "";

    public List<string> Children = [];

    public int X;
    public int Y;
    public List<CTDungeonMapLink> Outgoing = [];

    public bool ShouldSerializeX() => false;
    public bool ShouldSerializeY() => false;
    public bool ShouldSerializeOutgoing() => false;

    public bool LinksTo(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;

        foreach (var child in Children)
            if (string.Equals(child, id, StringComparison.OrdinalIgnoreCase)) return true;

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

    public static bool Available(string mapName) => ReadPathFor(mapName) != null;

    private static string ReadPathFor(string mapName) =>
        APIHelper.ModContentPaths.FindFile(FolderName,
            MapEditorSerialization.Sanitize(mapName) + ".json");

    public static string ToJson(CTDungeonMap map) =>
        map == null ? "" : JsonConvert.SerializeObject(map, Formatting.Indented);

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
            foreach (var file in APIHelper.ModContentPaths.FilesIn(FolderName, "*.json"))
            {
                try
                {
                    var map = JsonConvert.DeserializeObject<CTDungeonMap>(File.ReadAllText(file));
                    if (map == null) continue;

                    map.Migrate();
                    results.Add(map);
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
