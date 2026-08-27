using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor.Tools;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Ground the base did not come with.
//
// The base's shape is one polygon: BiomeBaseManager.Room.Pieces[0].Collider. Three separate things
// read it, and all three have to agree or new ground is ground in name only -
//
//   - the collision and the navigation graph, so the player and the followers can walk on it;
//   - BiomeBaseManager's GroundValidationCollider, which is what "is this point in the base" means
//     to followers (Follower.EnsureWithinBounds teleports anyone outside it back to the town centre)
//     and to the loader (LocationManager.PlaceStructures deletes save entries outside it);
//   - the build totem's placement region, whose buildable grid is a lattice cut from its own polygon.
//
// The game already does all three in one place when the player buys land, and this is that same
// sequence with our outlines in place of the DLC's: merge the paths, re-derive the validation
// collider, rebuild the collision and pathfinding, then re-cut the buildable grid.
//
// The one thing never done is touch path 0. That path is the base as the game shipped it, every
// bounds check falls back to it, and ours are only ever appended after it.
public static class BaseGround
{
    // The base's own outline, in world space, as it stood before anything was added to it. Read once
    // per visit, after the game's own DLC land merge has run - so bought land counts as vanilla
    // ground, which it is.
    private static readonly List<Vector2[]> _vanilla = [];
    private static bool _remembered;

    // What this mod added, in world space.
    private static readonly List<Vector2[]> _mod = [];

    public static bool HasModGround => _mod.Count > 0;

    // Pieces of the base's own terrain the author has reshaped. Their outlines are collected beside
    // the mod's own, because an enlarged piece of ground is new ground: the polygon the base was
    // built with only describes the shape it *had*, and nothing else will ever notice it grew.
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

    // Is this point on ground the base has at all - its own, or any this mod added?
    //
    // Asked before a building of the player's is allowed to have its saved position rewritten. The
    // game deletes a save entry whose position falls outside the base's ground polygon, so a
    // position that fails this is one their file must never be shown.
    public static bool IsOnBaseGround(Vector3 world)
    {
        foreach (var path in _vanilla)
            if (Utils.PointWithinPolygon(world, path)) return true;

        foreach (var path in _mod)
            if (Utils.PointWithinPolygon(world, path)) return true;

        // Nothing remembered yet - the caller cannot be told anything useful, and refusing on a
        // question we have not been able to ask would block every move.
        return _vanilla.Count == 0;
    }

    // Is this point standing on ground this mod added rather than on the base's own?
    //
    // Asked of every building the player puts up: one on their own ground is the game's business and
    // is left entirely alone, one on ours would be deleted by the game's next load and is ours to
    // remember instead.
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

    // Called after a shape is drawn, dragged or deleted. Debounced: a drag commits its shape on every
    // frame it moves, and each apply rebuilds the navigation graph for the whole base.
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
        // Unscaled: the editor holds the clock at zero.
        yield return new WaitForSecondsRealtime(0.6f);
        _pending = null;
        yield return ApplyRoutine();
    }

    // The whole sequence, in the order the game itself does it.
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

        // Rewritten from scratch every time rather than appended to: a shape that was moved or
        // deleted has to lose its old outline, and the vanilla paths are the fixed part.
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
            // What "inside the base" means to a follower and to the structure loader.
            RederiveValidationCollider(manager, collider);

            // Collision and the navigation graph, resized from the room's own bounds.
            manager?.Room?.SetColliderAndUpdatePathfinding();

            // The cached path every follower's bounds check reads; stale, it would keep teleporting
            // anyone standing on the new ground back to the town centre.
            Follower.Points = new Vector2[0];
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Base editor: the ground could not be rebuilt: " + e.Message);
        }

        // The graph rescan runs over frames.
        yield return null;

        RefreshPlacementGrid();

        Plugin.Log.LogInfo($"Base editor: ground is now {_vanilla.Count} vanilla outline(s) plus " +
                           $"{_mod.Count} added one(s).");

        ReportCollision();
    }

    // The collider that answers "is this point in the base" - for followers deciding whether they
    // have wandered off it, and for the structure loader deciding which of the save's buildings to
    // keep. It is a copy of the ground outline, and the game re-derives it in exactly these four
    // lines whenever the ground changes.
    //
    // Reached by name because it arrived after the reference assembly this mod builds against. The
    // work is copied rather than the method called for the same reason - one field lookup is a
    // smaller thing to depend on than a method signature.
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

    // What the room's collision actually looks like after the ground has been rebuilt.
    //
    // A shape only becomes floor by being merged into the room's composite outline. One that is not
    // stays a solid body the player is shoved out of, and the two look identical until something is
    // walked into. Everything that decides which of the two it is gets said out loud here: what
    // carries the composite, whether the base's own ground is part of it, and for each added shape
    // its collider, its layer and whether it joined.
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

    // Every closed shape this editor drew, as a world-space outline.
    //
    // Open-ended shapes are skipped on purpose: they carry an edge collider, which is a line rather
    // than an area, and a line has no inside for anything to stand on.
    private static void CollectModOutlines()
    {
        _mod.Clear();

        foreach (var shape in Object.FindObjectsOfType<CTEditorShape>())
            if (shape != null) Collect(shape.GetComponent<PolygonCollider2D>());

        // And the base's own ground where the author has changed its shape. The vanilla outline was
        // read once, before anything was touched, and it describes the ground as the base shipped it
        // - so a piece of it dragged out over the water is ground nothing knows about: not the
        // polygon followers are kept inside, and not the lattice the build totem cuts its grid from.
        // Its own outline goes in beside the mod's, and the overlap with the original costs nothing
        // because a point inside the vanilla ground is answered by the vanilla ground first.
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

    // The buildable grid, re-cut. Vanilla's own recipe when the player buys land: throw the lattice
    // away, fill it again, and re-stamp everything already standing so the next thing built does not
    // land on top of it.
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
    //
    // The game's fill walks outward from one seed and, with the major DLC installed, rebuilds the
    // region's polygon from the base's cached outline first - so ground we appended is gone by the
    // time it looks, and ground the seed cannot walk to would be missed even if it were not.
    //
    // Rather than replace the fill, this runs after it: the added outlines go back onto the polygon,
    // and every lattice point inside one of them that the fill did not already claim becomes a tile.
    // Vanilla's own ground keeps vanilla's own tiles, exactly as before.
    private const int AddedTileCeiling = 3000;

    private static PolygonCollider2D _lastExtended;
    private static int _lastVanillaPaths = -1;

    // Takes the added outlines back off the region's polygon before the game's own fill runs.
    //
    // That fill is recursive and walks outward from a single seed until it runs out of polygon or out
    // of its tile budget. Ground appended to the polygon is ground it can wander into - and if it
    // does, the budget it spends there is budget the base's own tiles do not get. The added ground is
    // put back, and filled separately, once vanilla has had its turn.
    //
    // With the major DLC installed the fill rebuilds the polygon from the base's cached outline
    // anyway, so this finds nothing to do and costs a reference compare.
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

        // Grid is StructureInfo.Grid: a region with no brain hands back a throwaway list, and filling
        // that would look like it worked.
        if (region.StructureInfo == null) return;

        // How many of the polygon's paths are the game's own. With the major DLC installed the fill
        // rebuilds this polygon from scratch every time it runs, so the count is fresh; without it
        // the polygon is the same object we appended to last time, and appending again on top would
        // grow it by one copy of the added ground per build menu the player opens.
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

        // The region rebuilds this itself when it notices the grid grew, but only on the next lookup -
        // and the placement loop asks before that happens.
        region.CreateDictionaryLookup();

        Plugin.Log.LogInfo($"Base editor: {added} extra build tile(s) over the added ground.");
    }

    // How far out to look, in lattice steps: the added outlines' world extent, converted into the
    // region's own space. A world rectangle is a diamond there, so all four corners are converted.
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
