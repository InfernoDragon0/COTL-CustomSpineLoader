using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Turns an authored CTDungeonMap into the game's own Map.Map and hands it to MapManager, so the
// selector the player sees is the real one rather than a preview of it.
//
// Everything here is fully qualified: the game puts its map types in a namespace called Map that
// also contains a class called Map, and importing it into this namespace is unreadable.
public static class DungeonMapBuilder
{
    // Null when the map is playable; otherwise the first thing wrong with it. The rules are the
    // renderer's, not ours - see the comments on each check.
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

                // UIAdventureMapOverlayController draws a connection for every outgoing point and
                // does not null-check the far end, so a dangling link is a crash, not a gap.
                if (map.NodeAt(link.X, link.Y) == null)
                    return $"Node ({node.X},{node.Y}) links to ({link.X},{link.Y}), where there is no node.";

                linked++;
            }
        }

        // GetFirstNode() is a .First(), not a .FirstOrDefault(): no node on layer 0 throws before
        // anything is drawn.
        if (starts == 0) return "No node on the bottom layer - that is where the run starts.";

        // One only, because the game does not let the player choose the first one: the renderer
        // marks GetFirstNode() visited and offers its links, so a second bottom node would be
        // drawn and never reachable.
        if (starts > 1) return $"{starts} nodes on the bottom layer - the run can only start on one.";
        if (!hasTop) return "No node on the top layer - the run has nowhere to end.";
        if (linked == 0) return "Nothing is linked; the map would render empty.";

        // A node with no connections at all is skipped by the renderer, so it is authored but
        // invisible. Worth naming rather than letting it quietly vanish.
        foreach (var node in map.Nodes)
        {
            if (node == null || node.Outgoing.Count > 0) continue;
            if (IsLinkedFrom(map, node.X, node.Y)) continue;

            return $"Node ({node.X},{node.Y}) has no links, so the game would not draw it.";
        }

        // A level that is not on this machine is not fatal - the node falls back to a vanilla
        // floor and logs - but it is almost always a rename, so it is worth catching here.
        foreach (var node in map.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.Level)) continue;
            if (!CTLevelSerialization.Exists(node.Level))
                return $"Node ({node.X},{node.Y}) plays level '{node.Level}', which is not saved.";
        }

        return Reachable(map);
    }

    // Not a reason to refuse the map - it plays - but worth saying out loud, because the node
    // icon promises something the vanilla floor behind it does not deliver.
    //
    // A node with no level bound generates a vanilla floor, and vanilla decides what a floor
    // contains from save data keyed by the dungeon's location: the boss-fight flag comes from
    // DataManager.GetDungeonLayer, which returns 0 for a minted location, so a MiniBoss or Boss
    // node produces an ordinary floor with ordinary encounters. Bind a level to it to author
    // what actually happens there.
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

    // Every node has to be walkable from the bottom layer: the player only ever moves along
    // outgoing links, so an unreachable branch is drawn but can never be entered.
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

    // Only the types the loaded dungeon config actually has a blueprint for. A type without one
    // has no icon and no RoomPrefabs, so picking it would place a node that cannot be entered.
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
                // The Node constructor hides one node in ten at random. An authored map showing
                // something other than what was authored is a bug, not a surprise.
                Hidden = false,
                CanBeHidden = false,
                position = new Vector2(authored.X, authored.Y)
            };

            built[authored] = node;
            nodes.Add(node);
        }

        // Both directions are filled from the authored outgoing list: the renderer walks outgoing
        // and the traversal state walks incoming, and they have to agree.
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

    // Hands the built graph to the game as the run's own map. CurrentMap has a private setter, so
    // it goes through Traverse; MapGenerated is what stops ShowMap throwing the map away and
    // generating a fresh one over it.
    public static void InstallMap(global::Map.MapManager manager, global::Map.Map built)
    {
        HarmonyLib.Traverse.Create(manager).Property("CurrentMap").SetValue(built);
        manager.MapGenerated = true;
    }
}
