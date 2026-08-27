using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// The buildable grid of a hub's build totem.
//
// The game's own grid is a flood fill: it walks the integer lattice of the region's local space
// outwards from local (0,0), keeping every point that falls inside a PolygonCollider2D. That is
// exactly the right idea for arbitrary ground - a hub is whatever shape its author drew - but two
// details of the vanilla fill do not survive the trip:
//
//   - it is seeded once, so ground the seed cannot walk to is not buildable. A hub is often several
//     islands, and the totem stands on one of them.
//   - with the major DLC installed it rebuilds the polygon from the base's cached outline every
//     time it runs, which would throw away the hub's shape a frame after we set it.
//
// So the fill is ours: same lattice, same inside test, but seeded nowhere and bounded by the
// polygon instead - every lattice point inside the outline becomes a tile, disjoint islands
// included. A prefix on CreateFloodFill sends the region here whenever the game would have run its
// own. Everything else about the region - the cost check, the tile art, the placement loop, what a
// finished building does - stays vanilla.
public class HubBuildRegion : MonoBehaviour
{
    // Far above vanilla's 50: that number is a guard against the recursive fill running away, and
    // the fill here is a bounded scan that cannot. A hub the size of the base is a few hundred
    // tiles; this only has to be a ceiling nobody reaches by accident.
    private const int TileCeiling = 4000;

    private PlacementRegion _region;

    // The world-space extent of the ground outline as it was last laid down, kept because the
    // physics world cannot be asked for it on the frame it changes.
    private Vector2 _worldMin;
    private Vector2 _worldMax;

    public PlacementRegion Region
    {
        get
        {
            if (_region == null) _region = GetComponent<PlacementRegion>();
            return _region;
        }
    }

    // Which cells are taken, without disturbing which cells exist.
    //
    // This is what opening the build menu needs, and for a long time it called RebuildGrid instead -
    // which re-cut the lattice from the ground outline every time. That outline is not as fixed as
    // it looks: colliders join and leave the room's composite as things are built and the navigation
    // is rebuilt, so the set of cells could come back subtly different from the one the player had
    // just built on. Their buildings then stood on cells that no longer existed, which the demolish
    // tool reads as no building being there at all - it picks by grid stamp, not by what is under
    // the cursor.
    //
    // The lattice is cut once, when the totem goes up, and a hub is stood up fresh on every visit.
    // That is the right cadence for ground that only changes between visits.
    public void RefreshOccupancy()
    {
        var region = Region;
        if (region == null || region.StructureInfo == null) return;

        foreach (var tile in region.Grid)
        {
            if (tile == null) continue;
            tile.Occupied = false;
            tile.Obstructed = false;
            tile.ObjectOnTile = StructureBrain.TYPES.NONE;
            tile.ObjectID = -1;
            tile.BlockNeighbouringTiles = 0;
            tile.IsUpgrade = false;
            tile.Collapsed = false;
        }

        RestampStructures(region);
    }

    // Every lattice point inside the hub's ground outline, as tiles the vanilla placement loop can
    // read. Called when the region is built and again whenever the ground changes underneath it.
    public void RebuildGrid()
    {
        var region = Region;
        if (region == null) return;

        // Grid is StructureInfo.Grid - a region with no brain hands back a throwaway list, and
        // filling that would look like it worked and leave the totem with no tiles.
        if (region.StructureInfo == null)
        {
            Plugin.Log.LogWarning("Hub totem: the region has no structure data yet, so its grid " +
                                  "cannot be built.");
            return;
        }

        if (!ApplyGroundOutline(region)) return;

        var grid = region.Grid;
        grid.Clear();
        region.GridTileLookup.Clear();

        var poly = region.polygonCollider2D;
        var bounds = LatticeBounds(region);
        var placed = 0;

        for (var x = bounds.xMin; x <= bounds.xMax; x++)
        {
            for (var y = bounds.yMin; y <= bounds.yMax; y++)
            {
                if (!Inside(region, poly, x, y)) continue;

                grid.Add(PlacementRegion.TileGridTile.Create(region, new Vector2Int(x, y),
                    Occupied: false, Obstructed: false));

                if (++placed < TileCeiling) continue;

                Plugin.Log.LogWarning($"Hub totem: the ground is larger than {TileCeiling} build " +
                                      "tiles; the rest is not buildable.");
                x = bounds.xMax + 1;
                break;
            }
        }

        // The region rebuilds this itself when it notices the grid grew, but only on the next
        // lookup - and the placement loop asks before that happens.
        region.CreateDictionaryLookup();

        RestampStructures(region);

        Plugin.Log.LogInfo($"Hub totem: {placed} build tile(s) over {poly.pathCount} ground " +
                           "outline(s).");
    }

    // A fresh grid is an empty grid: every tile comes back unoccupied, including the ones with a
    // building already standing on them. Vanilla has the same problem when the player buys land and
    // solves it the same way - re-stamp what is standing before anyone is allowed to place
    // anything, or the next thing they build lands on top of the last.
    private static void RestampStructures(PlacementRegion region)
    {
        var brain = region.structureBrain;
        if (brain == null) return;

        var stamped = new List<string>();
        var offGrid = new List<string>();

        foreach (var structure in Structure.Structures)
        {
            var data = structure?.Brain?.Data;
            if (data == null || data.IgnoreGrid || data.DoesNotOccupyGrid) continue;

            try
            {
                brain.AddStructureToGrid(data);

                // Whether the stamp actually landed. A structure whose recorded cell is not a cell
                // this grid has - because it was built against a different region, or before the
                // ground moved - marks nothing, and the demolish tool finds nothing to pick up.
                var cell = region.GetTileGridTile(data.GridTilePosition);
                if (cell != null && cell.ObjectID == data.ID)
                    stamped.Add($"{data.Type}#{data.ID}@{data.GridTilePosition.x},{data.GridTilePosition.y}");
                else
                    offGrid.Add($"{data.Type}#{data.ID}@{data.GridTilePosition.x},{data.GridTilePosition.y}");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Hub totem: {data.Type} could not be marked on the grid: " +
                                      e.Message);
            }
        }

        // The lattice the cells above are being looked up in. A structure whose cell is inside this
        // range but still finds nothing is a lookup problem; one outside it was built against a
        // different lattice altogether.
        var min = new Vector2Int(int.MaxValue, int.MaxValue);
        var max = new Vector2Int(int.MinValue, int.MinValue);
        foreach (var tile in region.Grid)
        {
            min = Vector2Int.Min(min, tile.Position);
            max = Vector2Int.Max(max, tile.Position);
        }

        if (region.Grid.Count > 0)
            Plugin.Log.LogInfo($"Hub totem: lattice holds {region.Grid.Count} cell(s), " +
                               $"x {min.x}..{max.x}, y {min.y}..{max.y}; region at " +
                               $"{region.transform.position}.");

        Plugin.Log.LogInfo($"Hub totem: grid knows {stamped.Count} structure(s)" +
                           (stamped.Count > 0 ? " [" + string.Join(", ", stamped.GetRange(0, Mathf.Min(6, stamped.Count))) + "]" : "") +
                           (offGrid.Count > 0
                               ? $"; {offGrid.Count} stand on no cell of it [" +
                                 string.Join(", ", offGrid.GetRange(0, Mathf.Min(6, offGrid.Count))) + "]"
                               : "") + ".");
    }

    // The room's collision outline is the ground, so the buildable area is the ground by
    // construction: no second shape to author and nothing to keep in step with the terrain tools.
    private bool ApplyGroundOutline(PlacementRegion region)
    {
        var poly = region.polygonCollider2D;
        if (poly == null)
        {
            Plugin.Log.LogWarning("Hub totem: the region has no polygon to shape.");
            return false;
        }

        var composite = SceneRefs.RoomComposite;
        if (composite == null || composite.pathCount == 0)
        {
            Plugin.Log.LogWarning("Hub totem: the hub has no ground outline, so nothing is " +
                                  "buildable. Draw some ground with the Shape tool first.");
            poly.pathCount = 0;
            return false;
        }

        // ClosestPoint is asked about world points; a disabled collider answers with its own
        // transform position instead, which would make every tile look valid.
        poly.enabled = true;
        poly.isTrigger = true;
        poly.offset = Vector2.zero;

        var paths = new List<Vector2[]>();
        var buffer = new List<Vector2>();

        _worldMin = Vector2.positiveInfinity;
        _worldMax = Vector2.negativeInfinity;

        for (var i = 0; i < composite.pathCount; i++)
        {
            buffer.Clear();
            composite.GetPath(i, buffer);
            if (buffer.Count < 3) continue;

            var converted = new Vector2[buffer.Count];
            for (var p = 0; p < buffer.Count; p++)
            {
                // Composite-local to world to polygon-local: the two colliders hang off different
                // transforms, and the totem is wherever its author put it.
                var world = composite.transform.TransformPoint(buffer[p]);
                _worldMin = Vector2.Min(_worldMin, world);
                _worldMax = Vector2.Max(_worldMax, world);
                converted[p] = poly.transform.InverseTransformPoint(world);
            }
            paths.Add(converted);
        }

        if (paths.Count == 0)
        {
            poly.pathCount = 0;
            return false;
        }

        poly.pathCount = paths.Count;
        for (var i = 0; i < paths.Count; i++) poly.SetPath(i, paths[i]);
        return true;
    }

    // Vanilla's test, kept to the letter: a lattice point counts when the polygon's nearest point
    // to it is itself. Vector2's own equality does the approximate compare.
    private static bool Inside(PlacementRegion region, PolygonCollider2D poly, int x, int y)
    {
        var world = (Vector2)region.transform.TransformPoint(new Vector2(x, y));
        return poly.ClosestPoint(world) == world;
    }

    // How far out to look, in lattice steps.
    //
    // Measured from the outline this class just laid down rather than from Collider2D.bounds: that
    // property answers from the physics world, which has not been told about the new paths yet on
    // the frame they are set, and answers with an empty box for a collider whose object is still
    // switched off. The ground rectangle is a rectangle in world space and a diamond in the
    // region's, so all four corners are converted rather than two.
    private RectInt LatticeBounds(PlacementRegion region)
    {
        var corners = new[]
        {
            new Vector3(_worldMin.x, _worldMin.y),
            new Vector3(_worldMax.x, _worldMin.y),
            new Vector3(_worldMin.x, _worldMax.y),
            new Vector3(_worldMax.x, _worldMax.y)
        };

        var min = Vector2.positiveInfinity;
        var max = Vector2.negativeInfinity;

        foreach (var corner in corners)
        {
            var local = region.transform.InverseTransformPoint(corner);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        var xMin = Mathf.FloorToInt(min.x) - 1;
        var yMin = Mathf.FloorToInt(min.y) - 1;
        return new RectInt(xMin, yMin,
            Mathf.CeilToInt(max.x) + 1 - xMin,
            Mathf.CeilToInt(max.y) + 1 - yMin);
    }
}
