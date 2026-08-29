using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor.Tools;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class BaseGround
{
    private static readonly List<Vector2[]> _vanilla = [];
    private static bool _remembered;

    private static readonly List<Vector2[]> _mod = [];

    public static bool HasModGround => _mod.Count > 0;

    private static readonly List<UnityEngine.U2D.SpriteShapeController> _reshaped = [];

    public static void RegisterReshapedGround(UnityEngine.U2D.SpriteShapeController ctrl)
    {
        if (ctrl == null || _reshaped.Contains(ctrl)) return;
        _reshaped.Add(ctrl);
    }

    public static void Forget()
    {
        _vanilla.Clear();
        _mod.Clear();
        _reshaped.Clear();
        _remembered = false;
        _lastExtended = null;
        _lastVanillaPaths = -1;
    }

    public static void RememberVanillaGround()
    {
        if (_remembered) return;

        var collider = GroundCollider();
        if (collider == null) return;

        _vanilla.Clear();
        for (var i = 0; i < collider.pathCount; i++)
        {
            var path = collider.GetPath(i);
            var world = new Vector2[path.Length];
            for (var p = 0; p < path.Length; p++)
                world[p] = collider.transform.TransformPoint(path[p]);
            _vanilla.Add(world);
        }

        _remembered = true;
        Plugin.Log.LogInfo($"Base editor: the base's own ground is {_vanilla.Count} outline(s); " +
                           "anything added sits beside it.");
    }

    private static PolygonCollider2D GroundCollider()
    {
        var manager = BiomeBaseManager.Instance;
        var room = manager != null ? manager.Room : null;
        if (room == null || room.Pieces == null || room.Pieces.Count == 0) return null;

        var piece = room.Pieces[0];
        return piece != null ? piece.Collider : null;
    }

    public static bool IsOnBaseGround(Vector3 world)
    {
        foreach (var path in _vanilla)
            if (Utils.PointWithinPolygon(world, path)) return true;

        foreach (var path in _mod)
            if (Utils.PointWithinPolygon(world, path)) return true;

        return _vanilla.Count == 0;
    }

    public static bool IsModGround(Vector3 world)
    {
        if (_mod.Count == 0) return false;

        foreach (var path in _vanilla)
            if (Utils.PointWithinPolygon(world, path)) return false;

        foreach (var path in _mod)
            if (Utils.PointWithinPolygon(world, path)) return true;

        return false;
    }

    // ---- applying -------------------------------------------------------------------------------

    private static Coroutine _pending;

    public static void RequestRefresh()
    {
        if (RuntimeMapEditor.Context != EditorContext.Base) return;

        var host = RuntimeMapEditor.Active;
        if (host == null) return;

        if (_pending != null) host.StopCoroutine(_pending);
        _pending = host.StartCoroutine(DebouncedRefresh(host));
    }

    private static IEnumerator DebouncedRefresh(RuntimeMapEditor host)
    {
        yield return new WaitForSecondsRealtime(0.6f);
        _pending = null;
        yield return ApplyRoutine();
    }

    public static IEnumerator ApplyRoutine()
    {
        RememberVanillaGround();

        var collider = GroundCollider();
        if (collider == null)
        {
            Plugin.Log.LogWarning("Base editor: the base has no ground outline to extend.");
            yield break;
        }

        CollectModOutlines();

        var paths = new List<Vector2[]>(_vanilla);
        paths.AddRange(_mod);

        collider.pathCount = paths.Count;
        for (var i = 0; i < paths.Count; i++)
        {
            var world = paths[i];
            var local = new Vector2[world.Length];
            for (var p = 0; p < world.Length; p++)
                local[p] = collider.transform.InverseTransformPoint(world[p]);
            collider.SetPath(i, local);
        }

        var manager = BiomeBaseManager.Instance;

        try
        {
            RederiveValidationCollider(manager, collider);

            manager?.Room?.SetColliderAndUpdatePathfinding();

            Follower.Points = new Vector2[0];
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Base editor: the ground could not be rebuilt: " + e.Message);
        }

        yield return null;

        RefreshPlacementGrid();

        Plugin.Log.LogInfo($"Base editor: ground is now {_vanilla.Count} vanilla outline(s) plus " +
                           $"{_mod.Count} added one(s).");

        ReportCollision();
    }

    private static System.Reflection.FieldInfo _validationField;
    private static bool _lookedForValidation;

    private static void RederiveValidationCollider(BiomeBaseManager manager, PolygonCollider2D ground)
    {
        if (manager == null) return;

        if (!_lookedForValidation)
        {
            _lookedForValidation = true;
            _validationField = HarmonyLib.AccessTools.Field(typeof(BiomeBaseManager),
                "GroundValidationCollider");

            if (_validationField == null)
                Plugin.Log.LogWarning("Base editor: this game keeps no ground validation collider " +
                                      "where one was expected; followers may be pulled off added " +
                                      "ground.");
        }

        if (_validationField?.GetValue(manager) is not CompositeCollider2D validation) return;

        var poly = validation.GetComponent<PolygonCollider2D>();
        if (poly == null) return;

        poly.pathCount = ground.pathCount;
        for (var i = 0; i < ground.pathCount; i++) poly.SetPath(i, ground.GetPath(i));
        validation.GenerateGeometry();
    }

    private static void ReportCollision()
    {
        var room = SceneRefs.Room;
        var composite = room != null ? room.RoomTransform : null;

        if (composite == null)
        {
            Plugin.Log.LogWarning("Base editor: the base room has no collision composite, so nothing " +
                                  "drawn here can become floor - every shape will be solid.");
            return;
        }

        var compositeBody = composite.GetComponent<Rigidbody2D>();

        Plugin.Log.LogInfo($"Base editor: room collision is '{composite.name}' " +
                           $"({composite.geometryType}, {composite.pathCount} path(s), body " +
                           $"{(compositeBody == null ? "<none>" : compositeBody.bodyType.ToString())}). " +
                           "More than one path means floor regions with nothing joining them.");

        var ground = GroundCollider();
        if (ground != null)
        {
            var groundBody = ground.attachedRigidbody;
            Plugin.Log.LogInfo($"Base editor: the base's own ground '{ground.name}' - " +
                               $"usedByComposite={ground.usedByComposite}, enabled={ground.enabled}, " +
                               $"isTrigger={ground.isTrigger}, " +
                               $"body {(groundBody == null ? "<none>" : groundBody.name)}, " +
                               $"same body as the composite: " +
                               $"{ReferenceEquals(groundBody, compositeBody)}.");
        }

        foreach (var shape in Object.FindObjectsOfType<CTEditorShape>())
        {
            if (shape == null) continue;

            var collider = shape.GetComponent<Collider2D>();
            var joined = collider != null && collider.usedByComposite &&
                         ReferenceEquals(collider.attachedRigidbody, compositeBody);

            Plugin.Log.LogInfo($"Base editor: added shape '{shape.name}' at " +
                               $"{shape.transform.position} - " +
                               (collider == null
                                   ? "no collider (decoration only)"
                                   : $"{collider.GetType().Name}, usedByComposite=" +
                                     $"{collider.usedByComposite}, layer " +
                                     $"'{LayerMask.LayerToName(shape.gameObject.layer)}', parent " +
                                     $"'{(shape.transform.parent == null ? "<none>" : shape.transform.parent.name)}'") +
                               (joined ? " - merged into the room outline." : " - NOT merged; this one is solid."));
        }
    }

    private static void CollectModOutlines()
    {
        _mod.Clear();

        foreach (var shape in Object.FindObjectsOfType<CTEditorShape>())
            if (shape != null) Collect(shape.GetComponent<PolygonCollider2D>());

        foreach (var ctrl in _reshaped)
            if (ctrl != null) Collect(ctrl.GetComponent<PolygonCollider2D>());
    }

    private static void Collect(PolygonCollider2D poly)
    {
        if (poly == null) return;

        for (var i = 0; i < poly.pathCount; i++)
        {
            var path = poly.GetPath(i);
            if (path.Length < 3) continue;

            var world = new Vector2[path.Length];
            for (var p = 0; p < path.Length; p++)
                world[p] = poly.transform.TransformPoint(path[p]);
            _mod.Add(world);
        }
    }

    private static void RefreshPlacementGrid()
    {
        var region = PlacementRegion.Instance;
        if (region == null) return;

        try
        {
            region.Grid.Clear();
            region.GridTileLookup.Clear();
            region.CreateFloodFill();

            var brain = region.structureBrain;
            if (brain == null) return;

            foreach (var structure in Structure.Structures)
            {
                var data = structure?.Brain?.Data;
                if (data == null || data.IgnoreGrid || data.DoesNotOccupyGrid) continue;
                brain.AddStructureToGrid(data);
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Base editor: the buildable grid could not be re-cut: " + e.Message);
        }
    }

    // ---- the buildable grid over added ground -----------------------------------------------------
    private const int AddedTileCeiling = 3000;

    private static PolygonCollider2D _lastExtended;
    private static int _lastVanillaPaths = -1;

    public static void TrimAddedPaths(PlacementRegion region)
    {
        var poly = region != null ? region.polygonCollider2D : null;
        if (poly == null || !ReferenceEquals(poly, _lastExtended)) return;
        if (_lastVanillaPaths < 0 || poly.pathCount <= _lastVanillaPaths) return;

        poly.pathCount = _lastVanillaPaths;
    }

    public static void ExtendGrid(PlacementRegion region)
    {
        if (region == null || _mod.Count == 0) return;

        var poly = region.polygonCollider2D;
        if (poly == null) return;

        if (region.StructureInfo == null) return;

        var vanillaPaths = ReferenceEquals(poly, _lastExtended) && _lastVanillaPaths >= 0 &&
                           poly.pathCount >= _lastVanillaPaths
            ? _lastVanillaPaths
            : poly.pathCount;

        poly.pathCount = vanillaPaths + _mod.Count;
        for (var i = 0; i < _mod.Count; i++)
        {
            var world = _mod[i];
            var local = new Vector2[world.Length];
            for (var p = 0; p < world.Length; p++)
                local[p] = poly.transform.InverseTransformPoint(world[p]);
            poly.SetPath(vanillaPaths + i, local);
        }

        _lastExtended = poly;
        _lastVanillaPaths = vanillaPaths;

        var grid = region.Grid;
        var bounds = LatticeBounds(region);
        var added = 0;

        for (var x = bounds.xMin; x <= bounds.xMax; x++)
        {
            for (var y = bounds.yMin; y <= bounds.yMax; y++)
            {
                var cell = new Vector2Int(x, y);
                if (region.GetTileGridTile(cell) != null) continue;

                var world = (Vector2)region.transform.TransformPoint(new Vector2(x, y));
                if (poly.ClosestPoint(world) != world) continue;

                grid.Add(PlacementRegion.TileGridTile.Create(region, cell,
                    Occupied: false, Obstructed: false));

                if (++added < AddedTileCeiling) continue;

                Plugin.Log.LogWarning($"Base editor: the added ground is larger than {AddedTileCeiling} " +
                                      "build tiles; the rest is not buildable.");
                x = bounds.xMax + 1;
                break;
            }
        }

        region.CreateDictionaryLookup();

        Plugin.Log.LogInfo($"Base editor: {added} extra build tile(s) over the added ground.");
    }

    private static RectInt LatticeBounds(PlacementRegion region)
    {
        var min = Vector2.positiveInfinity;
        var max = Vector2.negativeInfinity;

        foreach (var path in _mod)
        foreach (var point in path)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        var corners = new[]
        {
            new Vector3(min.x, min.y), new Vector3(max.x, min.y),
            new Vector3(min.x, max.y), new Vector3(max.x, max.y)
        };

        var localMin = Vector2.positiveInfinity;
        var localMax = Vector2.negativeInfinity;

        foreach (var corner in corners)
        {
            var local = region.transform.InverseTransformPoint(corner);
            localMin = Vector2.Min(localMin, local);
            localMax = Vector2.Max(localMax, local);
        }

        var xMin = Mathf.FloorToInt(localMin.x) - 1;
        var yMin = Mathf.FloorToInt(localMin.y) - 1;
        return new RectInt(xMin, yMin,
            Mathf.CeilToInt(localMax.x) + 1 - xMin,
            Mathf.CeilToInt(localMax.y) + 1 - yMin);
    }
}
