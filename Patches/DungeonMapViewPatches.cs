using System;
using System.Collections.Generic;
using HarmonyLib;
using Lamb.UI;
using UnityEngine;

namespace CustomSpineLoader.Patches
{
    /// <summary>
    /// The game's map screen throws away the layout it was given. UIAdventureMapOverlayController
    /// places each node at <c>point * 300</c> plus <c>Random.insideUnitCircle * 50</c>, and it
    /// rebuilds every node each time the screen opens, so the same map is drawn differently every
    /// time. On top of that, <c>point</c> is the derived grid - a node's column index within its
    /// layer - so a map authored in the dungeon editor loses its spacing before the jitter is even
    /// applied: rows are evenly spaced whatever the author did, a short row is pushed to the left
    /// rather than sitting under its parent, and layers are all one step apart.
    ///
    /// These two patches put an authored map back where it was drawn. Nothing here touches a map
    /// the game generated - <see cref="MapEditor.DungeonMapBuilder.IsAuthored"/> is the gate, and a
    /// vanilla run never passes it.
    /// </summary>
    [HarmonyPatch]
    public static class DungeonMapViewPatches
    {
        /// Vanilla's own placement, replaced. Node.position carries the editor's coordinates
        /// (see DungeonMapBuilder.Build); everything the screen works out afterwards - the bounds
        /// it centres on, the map height, the scroll offset, the connection lines - then falls out
        /// of these positions rather than the grid, so only this one assignment is needed.
        [HarmonyPatch(typeof(UIAdventureMapOverlayController), "MakeMapNode")]
        [HarmonyPostfix]
        private static void MakeMapNode(global::Map.Node mapNode, AdventureMapNode __result,
            global::Map.Map ____map)
        {
            if (__result == null || mapNode == null || !MapEditor.DungeonMapBuilder.IsAuthored(____map))
                return;

            try
            {
                var rect = __result.RectTransform;
                if (rect == null) return;

                rect.localPosition = new Vector3(mapNode.position.x, mapNode.position.y,
                    rect.localPosition.z);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("MapEditor: placing a custom map node failed: " + e);
            }
        }

        /// The screen centres the whole map on its bounds and then, after the fact, drags every
        /// Boss node to x = 0. That last step would undo the postfix above for exactly those nodes,
        /// and it runs too late to be headed off from MakeMapNode, so the layout is re-applied here
        /// once the screen has finished building.
        ///
        /// Re-applying is a no-op for every node the boss rule did not touch: the centre is worked
        /// out the same way the screen worked it out, over the same positions. The connection lines
        /// are rebuilt regardless because they captured their endpoints when they were made.
        [HarmonyPatch(typeof(UIAdventureMapOverlayController), "OnShowStarted")]
        [HarmonyPostfix]
        private static void OnShowStarted(global::Map.Map ____map,
            List<AdventureMapNode> ____adventureMapNodes,
            List<NodeConnection> ____nodeConnections,
            RectTransform ____crownSpineRectTransform,
            AdventureMapNode ____currentNode)
        {
            if (!MapEditor.DungeonMapBuilder.IsAuthored(____map)) return;
            if (____adventureMapNodes == null || ____adventureMapNodes.Count == 0) return;

            try
            {
                var centre = Centre(____adventureMapNodes);

                foreach (var node in ____adventureMapNodes)
                {
                    if (node == null || node.RectTransform == null || node.MapNode == null) continue;

                    node.RectTransform.localPosition = new Vector3(
                        node.MapNode.position.x - centre.x,
                        node.MapNode.position.y - centre.y,
                        node.RectTransform.localPosition.z);
                }

                RedrawConnections(____nodeConnections);

                if (____crownSpineRectTransform != null && ____currentNode != null)
                    ____crownSpineRectTransform.position = ____currentNode.transform.position;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("MapEditor: laying out the custom map failed: " + e);
            }
        }

        /// The midpoint of the authored positions, which is what the screen's Bounds.center comes to
        /// for the same set of points.
        private static Vector2 Centre(List<AdventureMapNode> nodes)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var any = false;

            foreach (var node in nodes)
            {
                if (node?.MapNode == null) continue;

                var position = node.MapNode.position;
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
                any = true;
            }

            return any ? (min + max) * 0.5f : Vector2.zero;
        }

        private static void RedrawConnections(List<NodeConnection> connections)
        {
            if (connections == null) return;

            foreach (var connection in connections)
            {
                if (connection == null || connection.LineRenderer == null ||
                    connection.From == null || connection.To == null) continue;

                connection.LineRenderer.Points =
                [
                    new MMUILineRenderer.BranchPoint(connection.From.RectTransform.localPosition),
                    new MMUILineRenderer.BranchPoint(connection.To.RectTransform.localPosition)
                ];
            }
        }
    }
}
