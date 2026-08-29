using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class HubBuildRegion : MonoBehaviour
{
    private const int TileCeiling = 4000;

    private PlacementRegion _region;

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

    public void RebuildGrid()
    {
        var region = Region;
        if (region == null) return;

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

        region.CreateDictionaryLookup();

        RestampStructures(region);

        Plugin.Log.LogInfo($"Hub totem: {placed} build tile(s) over {poly.pathCount} ground " +
                           "outline(s).");
    }

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

    private static bool Inside(PlacementRegion region, PolygonCollider2D poly, int x, int y)
    {
        var world = (Vector2)region.transform.TransformPoint(new Vector2(x, y));
        return poly.ClosestPoint(world) == world;
    }

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
