using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Turns an authored CTDungeonMap into the game's own Map.Map and hands it to MapManager.
// Fully qualified throughout: the game's Map namespace also contains a class called Map.
public static class DungeonMapBuilder
{
    // ---- the derived grid --------------------------------------------------------------------

    // Two nodes whose centres are within this many units of each other vertically stand on the
    // same layer. Roughly half a node: side by side reads as a row, stacked does not.
    public const float LayerBand = 70f;

    // The authored layout resolved onto the integer grid the game addresses nodes by.
    //
    // Map.Point is a pair of ints and the renderer draws at point * 300, so free positions cannot
    // survive into the game as they are. What survives is their arrangement: nodes are gathered
    // into rows by height, and within a row they keep their left-to-right order. Every node gets a
    // point of its own (rows are numbered from the bottom, columns from the left), which is what
    // the game needs - a point is a node's identity in every link and lookup it appears in.
    public static Dictionary<CTDungeonMapNode, global::Map.Point> Layout(CTDungeonMap map)
    {
        var points = new Dictionary<CTDungeonMapNode, global::Map.Point>();
        var rows = Rows(map);

        for (var y = 0; y < rows.Count; y++)
            for (var x = 0; x < rows[y].Count; x++)
                points[rows[y][x]] = new global::Map.Point(x, y);

        return points;
    }

    // Rows bottom to top, each ordered left to right. Deterministic: the same map always resolves
    // to the same grid, which is what lets the playback side re-derive it instead of storing it.
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
            // The band is measured from the lowest node in it, so a row cannot creep upwards one
            // node at a time.
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

    // One thing wrong with a map, and - where the rule is about a particular node - which node.
    // Validate and Advisory are the first blocking and the first advisory entry of this list; the
    // editor uses the nodes to ring the ones at fault.
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

    // Every rule the renderer and the run impose, blocking ones first in the order they are hit.
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
                // The renderer never null-checks a link's far end: a dangling link is a crash.
                if (map.FindNode(childId) == null)
                {
                    issues.Add(new MapIssue(
                        $"{Label(rows, node)} links to a node that is not on the map.", node));
                    continue;
                }

                linked++;
            }
        }

        // GetFirstNode() is a .First(): the leftmost node on the bottom row is the start, and a
        // second node down there is never reachable.
        if (rows.Count > 0 && rows[0].Count > 1)
        {
            var message = $"{rows[0].Count} nodes on layer 1 - the run can only start on one. " +
                          "Drag the others up a little.";
            for (var i = 1; i < rows[0].Count; i++)
                issues.Add(new MapIssue(message, rows[0][i]));
        }

        // The run ends when the top row is reached, so a map that is all one row is over at once.
        if (rows.Count == 1)
            issues.Add(new MapIssue("Every node is on layer 1, so the run would end after the " +
                                    "first floor. Drag some nodes higher."));

        if (linked == 0) issues.Add(new MapIssue("Nothing is linked; the map would render empty."));

        // A node with no connections at all is silently skipped by the renderer.
        foreach (var node in map.Nodes)
        {
            if (node == null || node.Children.Count > 0) continue;
            if (IsLinkedFrom(map, node.Id)) continue;

            issues.Add(new MapIssue(
                $"{Label(rows, node)} has no links, so the game would not draw it.", node));
        }

        // Not fatal (falls back to a vanilla floor) but almost always a rename; catch it here.
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

    // A dungeon node has no name - the game's own map does not give it one - so it is named by
    // the layer it resolved onto, which is what the node's own caption reads as "(L1)" and what
    // the halo on the map is pointing at. Rows and layers are the same thing here: a row of the
    // resolved layout is one layer of the run.
    private static string Label(List<List<CTDungeonMapNode>> rows, CTDungeonMapNode node)
    {
        for (var y = 0; y < rows.Count; y++)
            if (rows[y].Contains(node)) return $"The {node.NodeType} on layer {y + 1}";

        return "The " + node.NodeType;
    }

    // A type this scene's dungeon config has no blueprint for. Not blocking: the dungeon the map
    // is actually entered from loads its own config, which may well have it - which is exactly the
    // shape of the surprise, since Preview builds against whatever config is loaded here and
    // refuses, while entering the dungeon works.
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

    // Null when the map is playable; otherwise the first thing wrong.
    public static string Validate(CTDungeonMap map)
    {
        foreach (var issue in Issues(map))
            if (!issue.IsAdvisory) return issue.Message;

        return null;
    }

    // Warn, don't refuse: a boss node with no level bound generates an ordinary floor (the
    // boss-fight flag comes from save data a minted location has none of), so the icon promises
    // more than the floor delivers.
    public static string Advisory(CTDungeonMap map)
    {
        if (map == null) return null;

        foreach (var issue in Issues(map))
            if (issue.IsAdvisory) return issue.Message;

        return null;
    }

    // The aggregate first, so Advisory reads as one sentence about the map; the per-node entries
    // after it are only there to carry cells for the editor's rings.
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

    // The player only moves along outgoing links; an unreachable branch is drawn but unenterable.
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

    // Only types the config has a blueprint for: without one there is no icon and no RoomPrefabs.
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

        // FollowerLocation.None means "no per-biome override", which is what an editor wants.
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

        // In layout order, bottom row first and left to right: GetFirstNode() is a .First() over
        // this list, so the leftmost node of the bottom row is the one the run starts on.
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
                // The Node constructor hides one node in ten at random; show what was authored.
                Hidden = false,
                CanBeHidden = false,
                position = new Vector2(point.x, point.y)
            };

            built[authored] = node;
            nodes.Add(node);
        }

        // Both directions come from the authored link list: the renderer walks outgoing,
        // traversal walks incoming, and they must agree.
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

    // CurrentMap has a private setter; MapGenerated stops ShowMap generating a fresh map over it.
    public static void InstallMap(global::Map.MapManager manager, global::Map.Map built)
    {
        HarmonyLib.Traverse.Create(manager).Property("CurrentMap").SetValue(built);
        manager.MapGenerated = true;
    }
}
