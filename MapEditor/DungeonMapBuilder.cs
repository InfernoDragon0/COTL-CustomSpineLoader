using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Turns an authored CTDungeonMap into the game's own Map.Map and hands it to MapManager.
// Fully qualified throughout: the game's Map namespace also contains a class called Map.
public static class DungeonMapBuilder
{
    // Null when the map is playable; otherwise the first thing wrong. Each rule is the renderer's.
    public static string Validate(CTDungeonMap map)
    {
        if (map == null || map.Nodes.Count == 0) return "The map has no nodes.";

        var linked = 0;
        var starts = 0;
        var hasTop = false;

        foreach (var node in map.Nodes)
        {
            if (node == null) continue;

            if (node.Y == 0) starts++;
            if (node.Y == map.Layers - 1) hasTop = true;

            foreach (var link in node.Outgoing)
            {
                if (link == null) continue;

                // The renderer never null-checks a link's far end: a dangling link is a crash.
                if (map.NodeAt(link.X, link.Y) == null)
                    return $"Node ({node.X},{node.Y}) links to ({link.X},{link.Y}), where there is no node.";

                linked++;
            }
        }

        // GetFirstNode() is a .First(): no node on layer 0 throws before anything is drawn.
        if (starts == 0) return "No node on the bottom layer - that is where the run starts.";

        // The renderer marks GetFirstNode() visited; a second bottom node would never be reachable.
        if (starts > 1) return $"{starts} nodes on the bottom layer - the run can only start on one.";
        if (!hasTop) return "No node on the top layer - the run has nowhere to end.";
        if (linked == 0) return "Nothing is linked; the map would render empty.";

        // A node with no connections at all is silently skipped by the renderer.
        foreach (var node in map.Nodes)
        {
            if (node == null || node.Outgoing.Count > 0) continue;
            if (IsLinkedFrom(map, node.X, node.Y)) continue;

            return $"Node ({node.X},{node.Y}) has no links, so the game would not draw it.";
        }

        // Not fatal (falls back to a vanilla floor) but almost always a rename; catch it here.
        foreach (var node in map.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.Level)) continue;
            if (!CTLevelSerialization.Exists(node.Level))
                return $"Node ({node.X},{node.Y}) plays level '{node.Level}', which is not saved.";
        }

        return Reachable(map);
    }

    // Warn, don't refuse: a boss node with no level bound generates an ordinary floor (the
    // boss-fight flag comes from save data a minted location has none of), so the icon promises
    // more than the floor delivers.
    public static string Advisory(CTDungeonMap map)
    {
        if (map == null) return null;

        var plain = 0;
        foreach (var node in map.Nodes)
        {
            if (node == null || !string.IsNullOrEmpty(node.Level)) continue;
            if (node.NodeType is "MiniBossFloor" or "Boss" or "FinalBoss") plain++;
        }

        return plain == 0
            ? null
            : $"{plain} boss node(s) have no level bound, so they generate an ordinary floor - " +
              "vanilla picks its bosses from save data this dungeon has none of.";
    }

    // The player only moves along outgoing links; an unreachable branch is drawn but unenterable.
    private static string Reachable(CTDungeonMap map)
    {
        var open = new Queue<CTDungeonMapNode>();
        var seen = new HashSet<CTDungeonMapNode>();

        foreach (var node in map.Nodes)
        {
            if (node == null || node.Y != 0) continue;
            open.Enqueue(node);
            seen.Add(node);
        }

        while (open.Count > 0)
        {
            var node = open.Dequeue();
            foreach (var link in node.Outgoing)
            {
                var next = map.NodeAt(link.X, link.Y);
                if (next == null || !seen.Add(next)) continue;
                open.Enqueue(next);
            }
        }

        foreach (var node in map.Nodes)
            if (node != null && !seen.Contains(node))
                return $"Node ({node.X},{node.Y}) cannot be reached from the bottom layer.";

        return null;
    }

    public static bool IsLinkedFrom(CTDungeonMap map, int x, int y)
    {
        foreach (var node in map.Nodes)
            if (node != null && node.LinksTo(x, y)) return true;

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

        var built = new Dictionary<CTDungeonMapNode, global::Map.Node>();
        var nodes = new List<global::Map.Node>();

        foreach (var authored in map.Nodes)
        {
            if (authored == null) continue;

            if (!TryParseType(authored.NodeType, out var type))
            {
                error = $"Node ({authored.X},{authored.Y}) has unknown type '{authored.NodeType}'.";
                return null;
            }

            var blueprint = global::Map.MapManager.GetBlueprint(type, config);
            if (blueprint == null)
            {
                error = $"This dungeon's config has no blueprint for '{authored.NodeType}'.";
                return null;
            }

            var node = new global::Map.Node(type, blueprint, new global::Map.Point(authored.X, authored.Y))
            {
                // The Node constructor hides one node in ten at random; show what was authored.
                Hidden = false,
                CanBeHidden = false,
                position = new Vector2(authored.X, authored.Y)
            };

            built[authored] = node;
            nodes.Add(node);
        }

        // Both directions come from the authored outgoing list: the renderer walks outgoing,
        // traversal walks incoming, and they must agree.
        foreach (var pair in built)
        {
            foreach (var link in pair.Key.Outgoing)
            {
                var target = map.NodeAt(link.X, link.Y);
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
