using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class DungeonMapBuilder
{
    // ---- the derived grid --------------------------------------------------------------------

    public const float LayerBand = 70f;

    public static Dictionary<CTDungeonMapNode, global::Map.Point> Layout(CTDungeonMap map)
    {
        var points = new Dictionary<CTDungeonMapNode, global::Map.Point>();
        var rows = Rows(map);

        for (var y = 0; y < rows.Count; y++)
            for (var x = 0; x < rows[y].Count; x++)
                points[rows[y][x]] = new global::Map.Point(x, y);

        return points;
    }

    public static List<List<CTDungeonMapNode>> Rows(CTDungeonMap map)
    {
        var rows = new List<List<CTDungeonMapNode>>();
        if (map == null) return rows;

        var ordered = new List<CTDungeonMapNode>();
        foreach (var node in map.Nodes)
            if (node != null) ordered.Add(node);

        ordered.Sort(Compare);

        foreach (var node in ordered)
        {
            if (rows.Count == 0 || node.PosY - rows[rows.Count - 1][0].PosY > LayerBand)
                rows.Add([]);

            rows[rows.Count - 1].Add(node);
        }

        foreach (var row in rows) row.Sort(CompareAcross);
        return rows;
    }

    private static int Compare(CTDungeonMapNode a, CTDungeonMapNode b)
    {
        var y = a.PosY.CompareTo(b.PosY);
        return y != 0 ? y : CompareAcross(a, b);
    }

    private static int CompareAcross(CTDungeonMapNode a, CTDungeonMapNode b)
    {
        var x = a.PosX.CompareTo(b.PosX);
        return x != 0 ? x : string.CompareOrdinal(a.Id ?? "", b.Id ?? "");
    }

    // ---- validation --------------------------------------------------------------------------

    public readonly struct MapIssue
    {
        public readonly string Message;
        public readonly CTDungeonMapNode Node;
        public readonly bool IsAdvisory;

        public bool HasNode => Node != null;

        public MapIssue(string message, bool advisory = false)
        {
            Message = message;
            Node = null;
            IsAdvisory = advisory;
        }

        public MapIssue(string message, CTDungeonMapNode node, bool advisory = false)
        {
            Message = message;
            Node = node;
            IsAdvisory = advisory;
        }
    }

    public static List<MapIssue> Issues(CTDungeonMap map)
    {
        var issues = new List<MapIssue>();

        if (map == null || map.Nodes.Count == 0)
        {
            issues.Add(new MapIssue("The map has no nodes."));
            return issues;
        }

        var linked = 0;
        var rows = Rows(map);

        foreach (var node in map.Nodes)
        {
            if (node == null) continue;

            foreach (var childId in node.Children)
            {
                if (map.FindNode(childId) == null)
                {
                    issues.Add(new MapIssue(
                        $"{Label(rows, node)} links to a node that is not on the map.", node));
                    continue;
                }

                linked++;
            }
        }

        if (rows.Count > 0 && rows[0].Count > 1)
        {
            var message = $"{rows[0].Count} nodes on layer 1 - the run can only start on one. " +
                          "Drag the others up a little.";
            for (var i = 1; i < rows[0].Count; i++)
                issues.Add(new MapIssue(message, rows[0][i]));
        }

        if (rows.Count == 1)
            issues.Add(new MapIssue("Every node is on layer 1, so the run would end after the " +
                                    "first floor. Drag some nodes higher."));

        if (linked == 0) issues.Add(new MapIssue("Nothing is linked; the map would render empty."));

        foreach (var node in map.Nodes)
        {
            if (node == null || node.Children.Count > 0) continue;
            if (IsLinkedFrom(map, node.Id)) continue;

            issues.Add(new MapIssue(
                $"{Label(rows, node)} has no links, so the game would not draw it.", node));
        }

        foreach (var node in map.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.Level)) continue;
            if (!CTLevelSerialization.Available(node.Level))
                issues.Add(new MapIssue(
                    $"{Label(rows, node)} plays level '{node.Level}', which is not saved.", node));
        }

        foreach (var node in Unreachable(map))
            issues.Add(new MapIssue(
                $"{Label(rows, node)} cannot be reached from layer 1.", node));

        AddTypeAdvisory(map, rows, issues);
        AddBossAdvisory(map, rows, issues);
        return issues;
    }

    private static string Label(List<List<CTDungeonMapNode>> rows, CTDungeonMapNode node)
    {
        for (var y = 0; y < rows.Count; y++)
            if (rows[y].Contains(node)) return $"The {node.NodeType} on layer {y + 1}";

        return "The " + node.NodeType;
    }

    private static void AddTypeAdvisory(CTDungeonMap map, List<List<CTDungeonMapNode>> rows,
        List<MapIssue> issues)
    {
        var config = Config();
        if (config == null) return;

        foreach (var node in map.Nodes)
        {
            if (node == null) continue;
            if (TryParseType(node.NodeType, out var type) &&
                global::Map.MapManager.GetBlueprint(type, config) != null) continue;

            issues.Add(new MapIssue(
                $"This scene's dungeon config has no blueprint for '{node.NodeType}', so Preview " +
                "cannot draw it - the dungeon it is entered from may still have one.",
                node, advisory: true));
        }
    }

    public static string Validate(CTDungeonMap map)
    {
        foreach (var issue in Issues(map))
            if (!issue.IsAdvisory) return issue.Message;

        return null;
    }

    public static string Advisory(CTDungeonMap map)
    {
        if (map == null) return null;

        foreach (var issue in Issues(map))
            if (issue.IsAdvisory) return issue.Message;

        return null;
    }

    private static void AddBossAdvisory(CTDungeonMap map, List<List<CTDungeonMapNode>> rows,
        List<MapIssue> issues)
    {
        var plain = new List<CTDungeonMapNode>();
        foreach (var node in map.Nodes)
        {
            if (node == null || !string.IsNullOrEmpty(node.Level)) continue;
            if (node.NodeType is "MiniBossFloor" or "Boss" or "FinalBoss") plain.Add(node);
        }

        if (plain.Count == 0) return;

        issues.Add(new MapIssue(
            $"{plain.Count} boss node(s) have no level bound, so they generate an ordinary floor - " +
            "vanilla picks its bosses from save data this dungeon has none of.", advisory: true));

        foreach (var node in plain)
            issues.Add(new MapIssue("This boss node has no level bound.", node, advisory: true));
    }

    private static List<CTDungeonMapNode> Unreachable(CTDungeonMap map)
    {
        var open = new Queue<CTDungeonMapNode>();
        var seen = new HashSet<CTDungeonMapNode>();
        var stranded = new List<CTDungeonMapNode>();

        var rows = Rows(map);
        if (rows.Count > 0)
            foreach (var node in rows[0])
            {
                open.Enqueue(node);
                seen.Add(node);
            }

        while (open.Count > 0)
        {
            var node = open.Dequeue();
            foreach (var childId in node.Children)
            {
                var next = map.FindNode(childId);
                if (next == null || !seen.Add(next)) continue;
                open.Enqueue(next);
            }
        }

        foreach (var node in map.Nodes)
            if (node != null && !seen.Contains(node))
                stranded.Add(node);

        return stranded;
    }

    public static bool IsLinkedFrom(CTDungeonMap map, string id)
    {
        foreach (var node in map.Nodes)
            if (node != null && node.LinksTo(id)) return true;

        return false;
    }

    // ---- the game's node types ------------------------------------------------------------

    public static List<global::Map.NodeType> AvailableTypes()
    {
        var results = new List<global::Map.NodeType>();

        var config = Config();
        if (config == null) return results;

        void Add(global::Map.NodeBlueprint blueprint)
        {
            if (blueprint == null || results.Contains(blueprint.nodeType)) return;
            results.Add(blueprint.nodeType);
        }

        if (config.nodeBlueprints != null)
            foreach (var blueprint in config.nodeBlueprints) Add(blueprint);

        Add(config.FirstFloorBluePrint);
        Add(config.SecondFloorBluePrint);
        Add(config.MiniBossFloorBluePrint);
        Add(config.TreasureBluePrint);
        Add(config.LeaderFloorBluePrint);

        return results;
    }

    public static global::Map.MapConfig Config() =>
        global::Map.MapManager.Instance != null ? global::Map.MapManager.Instance.DungeonConfig : null;

    public static global::Map.NodeBlueprint BlueprintFor(string typeName)
    {
        var config = Config();
        if (config == null || !TryParseType(typeName, out var type)) return null;

        return global::Map.MapManager.GetBlueprint(type, config);
    }

    public static bool TryParseType(string typeName, out global::Map.NodeType type)
    {
        type = global::Map.NodeType.MinorEnemy;
        return !string.IsNullOrEmpty(typeName) &&
               System.Enum.TryParse(typeName, out type);
    }

    public static Sprite IconFor(string typeName)
    {
        var blueprint = BlueprintFor(typeName);
        if (blueprint == null) return null;

        var sprite = blueprint.GetSprite(FollowerLocation.None);
        return sprite != null ? sprite : blueprint.sprite;
    }

    // ---- building ---------------------------------------------------------------------------

    public static global::Map.Map Build(CTDungeonMap map, out string error)
    {
        error = Validate(map);
        if (error != null) return null;

        var config = Config();
        if (config == null)
        {
            error = "No MapManager in this scene, so the game's node blueprints are unavailable.";
            return null;
        }

        var points = Layout(map);
        var built = new Dictionary<CTDungeonMapNode, global::Map.Node>();
        var nodes = new List<global::Map.Node>();

        foreach (var row in Rows(map))
        foreach (var authored in row)
        {
            if (!TryParseType(authored.NodeType, out var type))
            {
                error = $"A node has unknown type '{authored.NodeType}'.";
                return null;
            }

            var blueprint = global::Map.MapManager.GetBlueprint(type, config);
            if (blueprint == null)
            {
                error = $"This dungeon's config has no blueprint for '{authored.NodeType}'.";
                return null;
            }

            var point = points[authored];
            var node = new global::Map.Node(type, blueprint, point)
            {
                Hidden = false,
                CanBeHidden = false,
                position = new Vector2(point.x, point.y)
            };

            built[authored] = node;
            nodes.Add(node);
        }

        foreach (var pair in built)
        {
            foreach (var childId in pair.Key.Children)
            {
                var target = map.FindNode(childId);
                if (target == null || !built.TryGetValue(target, out var targetNode)) continue;

                pair.Value.AddOutgoing(targetNode.point);
                targetNode.AddIncoming(pair.Value.point);
            }
        }

        return new global::Map.Map(config.name, nodes, []);
    }

    public static void InstallMap(global::Map.MapManager manager, global::Map.Map built)
    {
        HarmonyLib.Traverse.Create(manager).Property("CurrentMap").SetValue(built);
        manager.MapGenerated = true;
    }
}
