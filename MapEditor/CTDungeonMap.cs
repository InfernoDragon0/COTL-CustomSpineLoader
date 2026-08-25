using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CustomSpineLoader.MapEditor;

// One authored adventure map: nodes placed freely, joined by links.
//
// The game addresses a node by an integer Point and draws it at point * 300, so the grid still
// exists - but it is derived from the layout on the way in (DungeonMapBuilder.Layout) rather than
// being the thing that is authored. What is authored is a position and a graph, the way the world
// map is, so a dungeon can be laid out by eye.
public class CTDungeonMap
{
    public string MapName = "untitledmap";

    // The Unity scene the dungeon runs in; json-only, no control in the tool.
    public string SceneName = "Dungeon1";

    public List<CTDungeonMapNode> Nodes = [];

    // The grid the builder was first written around. Read so a map saved by that build still
    // opens; never written again - Migrate turns it into positions and ids.
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

    // The floor the player arrives in: the leftmost node on the lowest row. Asked of the layout
    // rather than worked out here, because the run starts on whichever node Map.GetFirstNode()
    // lands on and that is decided by the same resolution the builder uses.
    public CTDungeonMapNode StartNode()
    {
        var rows = DungeonMapBuilder.Rows(this);
        return rows.Count > 0 && rows[0].Count > 0 ? rows[0][0] : null;
    }

    // The spacing the old grid was drawn at, so a map authored on it keeps its shape.
    private const float LegacyPitch = 160f;

    private static readonly HashSet<string> Reported = new(StringComparer.OrdinalIgnoreCase);

    // A map written before nodes had ids addresses them by cell: give every node an id, turn its
    // cell into a position, and rewrite its (x,y) links as links to those ids. Idempotent - a map
    // that already has ids passes straight through.
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

        // Once per map per session: nothing rewrites the file until it is saved, so every scan of
        // the folder migrates it again and the log would fill with the same six lines.
        if (Reported.Add(MapName ?? ""))
            Plugin.Log.LogInfo($"MapEditor: dungeon map '{MapName}' was on the old grid; " +
                               $"{Nodes.Count} node(s) moved onto free positions.");
    }
}

public class CTDungeonMapNode
{
    // Stable identity: links name it, and a node that moves keeps it.
    public string Id = "";

    // Where the node sits on the map, in the editor's own units. The game's grid is derived from
    // this, so two nodes at the same height stand on the same layer.
    public float PosX;
    public float PosY;

    // Map.NodeType by name: a stored int would silently shift when the game renumbers the enum.
    public string NodeType = "MinorEnemy";

    // A CTLevelBlueprint by name, played on entry. Empty = the node's vanilla generation.
    public string Level = "";

    // Nodes this one leads to, by id; incoming is rebuilt from these, never stored.
    public List<string> Children = [];

    // The old grid's cell and links, read from a map saved by that build and dropped by Migrate.
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

// Only ever read: the cell pair a pre-id map stored its links as.
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

    // Exists = ours, the overwrite question. Available = ours or any other mod's, the load question.
    public static bool Exists(string mapName) => File.Exists(PathFor(mapName));

    public static bool Available(string mapName) => ReadPathFor(mapName) != null;

    private static string ReadPathFor(string mapName) =>
        APIHelper.ModContentPaths.FindFile(FolderName,
            MapEditorSerialization.Sanitize(mapName) + ".json");

    // The map as it would be written. Comparing this to the last write beats a dirty flag set from
    // every widget: one that forgot to raise the flag would lose the edit silently.
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
