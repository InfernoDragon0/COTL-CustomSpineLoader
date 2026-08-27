using System.Collections;
using CustomSpineLoader.MapEditor;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.Patches;

// Building in a hub, without the game's save ever hearing about it.
//
// The vanilla flow is left running end to end - the player opens the real build menu, sees the real
// grid, pays the real cost, and gets the real building. Only the last step is intercepted: the
// moment the game files the new structure in the save list it belongs to, it is taken back out and
// written to the hub's own file instead.
//
// The interception is scoped as tightly as it can be. It is not "anything that happens while a hub
// is open" - the game restores the player's actual Woolhaven buildings through the same call, and
// deleting those would cost them their town. It is only what passes through a build driven by
// *our* placement region, which is a window a few statements wide.
[HarmonyPatch]
public static class HubBuildPatches
{
    // Depth rather than a flag: a build site's completion nests one BuildStructure inside another.
    private static int _ourBuild;

    private static bool Ours => _ourBuild > 0;

    // ---- the grid -----------------------------------------------------------------------------

    // A hub's grid is built from the hub's ground, not from the base's cached outline - see
    // HubBuildRegion for why the vanilla fill cannot be used as it stands.
    [HarmonyPatch(typeof(PlacementRegion), nameof(PlacementRegion.CreateFloodFill))]
    [HarmonyPrefix]
    private static bool PlacementRegion_CreateFloodFill(PlacementRegion __instance)
    {
        var hub = __instance.GetComponent<HubBuildRegion>();
        if (hub == null) return true;

        hub.RebuildGrid();
        return false;
    }

    // The placement loop clamps the cursor to the base's own extents. A hub is somewhere else in
    // the world, so those numbers would pin the cursor to a corner of it - or off it entirely.
    // Widened only while a hub totem is standing; the base, the ranches and the sleeping followers
    // that read the same two properties are left alone.
    [HarmonyPatch(typeof(PlacementRegion), "get_X_Constraints")]
    [HarmonyPostfix]
    private static void PlacementRegion_XConstraints(ref Vector2 __result)
    {
        if (Bounds(out var bounds)) __result = new Vector2(bounds.min.x - 4f, bounds.max.x + 4f);
    }

    [HarmonyPatch(typeof(PlacementRegion), "get_Y_Constraints")]
    [HarmonyPostfix]
    private static void PlacementRegion_YConstraints(ref Vector2 __result)
    {
        if (Bounds(out var bounds)) __result = new Vector2(bounds.min.y - 4f, bounds.max.y + 4f);
    }

    private static bool Bounds(out UnityEngine.Bounds bounds)
    {
        bounds = default;
        if (!HubSession.Active || !HubBuildTotem.Exists) return false;

        var composite = SceneRefs.RoomComposite;
        if (composite == null) return false;

        bounds = composite.bounds;
        return true;
    }

    // A region clears PlacementRegion.Instance when it is destroyed, whether or not it was ever the
    // one holding it. That is harmless in a game with a single region and a scene reload behind
    // every teardown; it is not harmless when a hub's copy is torn down while the town's own is
    // sitting switched off, because the town would come back with no region at all. Only ours is
    // put right - vanilla's own behaviour is left exactly as it is.
    [HarmonyPatch(typeof(PlacementRegion), "OnDestroy")]
    [HarmonyPrefix]
    private static void PlacementRegion_OnDestroyPrefix(PlacementRegion __instance,
        out PlacementRegion __state)
    {
        __state = null;
        if (__instance == null || __instance.GetComponent<HubBuildRegion>() == null) return;
        if (ReferenceEquals(PlacementRegion.Instance, __instance)) return;

        __state = PlacementRegion.Instance;
    }

    [HarmonyPatch(typeof(PlacementRegion), "OnDestroy")]
    [HarmonyPostfix]
    private static void PlacementRegion_OnDestroyPostfix(PlacementRegion __state)
    {
        if (__state != null) PlacementRegion.Instance = __state;
    }

    // ---- the singleton, for the length of a placement -------------------------------------------

    // The prompt over the player's head while they are placing a building reads
    // PlacementRegion.Instance.StructureType - the singleton, not the region they are actually
    // building through. In a hub that singleton is still the town's region, which is not building
    // anything, so the prompt asked for the name of structure type NONE and got the raw
    // untranslated term back: "Place Structures/NONE".
    //
    // The singleton is not simply handed to the hub for the whole session, tempting as that is:
    // some twenty other things read it, and most of them are follower tasks that reason about the
    // *town's* grid - where its beds are, where a body can be composted. Pointing those at a hub's
    // grid while the player is away would be a poor trade for a label.
    //
    // Instead it is swapped for exactly as long as a placement lasts. The edit-mode HUD bar is the
    // bookend for that: the placement routine raises it on the way in and drops it on the way out,
    // and nothing else in the game calls it. The clock is stopped for almost all of that window, so
    // no follower is thinking anyway.
    private static PlacementRegion _townRegion;
    private static bool _swapped;

    [HarmonyPatch(typeof(HUD_Manager), nameof(HUD_Manager.ShowEditMode))]
    [HarmonyPostfix]
    private static void HUD_Manager_ShowEditMode(bool show)
    {
        if (!show)
        {
            RestoreTownRegion();
            return;
        }

        if (_swapped || !HubSession.Active) return;

        var region = HubBuildTotem.Region;
        if (region == null) return;

        _townRegion = PlacementRegion.Instance;
        PlacementRegion.Instance = region;
        _swapped = true;

        if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(CheckTiles(region));
    }

    // The white squares that show which cells can be built on.
    //
    // They are pooled from a template the region points at, and the pool hands a *fresh* one back
    // without switching it on - only a recycled one gets that. In the town the template is switched
    // on, so nobody has ever noticed; a copy made while the town room is switched off can inherit an
    // off template, and then every tile is spawned invisible. Cheap to put right, and it says what
    // it found either way.
    private static System.Collections.IEnumerator CheckTiles(PlacementRegion region)
    {
        // Unscaled: the placement loop stops the clock the moment it starts.
        yield return new WaitForSecondsRealtime(0.5f);

        if (region == null) yield break;

        var tiles = region.GetComponentsInChildren<PlacementTile>(true);
        var woken = 0;

        foreach (var tile in tiles)
        {
            if (tile == null || tile.gameObject.activeSelf) continue;
            tile.gameObject.SetActive(true);
            woken++;
        }

        Plugin.Log.LogInfo($"Hub totem: {tiles.Length} grid square(s) under the region" +
                           (woken > 0 ? $", {woken} of them switched on here" : "") + ".");
    }

    // Also called when a hub is torn down: a scene change in the middle of a placement would
    // otherwise leave the town's own region unfindable.
    public static void RestoreTownRegion()
    {
        if (!_swapped) return;

        PlacementRegion.Instance = _townRegion;
        _townRegion = null;
        _swapped = false;
    }

    // ---- scoping ------------------------------------------------------------------------------

    // Everything the region's own build does is ours, whichever branch it takes - including the one
    // that hard-codes the base as the location, which is how a fence or a patch of rubble placed in
    // a hub would otherwise end up in the player's town.
    [HarmonyPatch(typeof(PlacementRegion), "Build")]
    [HarmonyPrefix]
    private static void PlacementRegion_BuildPrefix(PlacementRegion __instance)
    {
        if (__instance.GetComponent<HubBuildRegion>() != null) _ourBuild++;
    }

    [HarmonyPatch(typeof(PlacementRegion), "Build")]
    [HarmonyFinalizer]
    private static void PlacementRegion_BuildFinalizer(PlacementRegion __instance)
    {
        if (__instance.GetComponent<HubBuildRegion>() != null) _ourBuild--;
    }

    // The other end of a build site: a follower finishes it long after the placement loop is gone,
    // and the finished structure is filed then. While a hub is open, nothing else can be building
    // in the Woolhaven room - the room is the hub.
    [HarmonyPatch(typeof(StructureManager), nameof(StructureManager.BuildStructure))]
    [HarmonyPrefix]
    private static void StructureManager_BuildStructurePrefix(FollowerLocation location,
        StructuresData data, out bool __state)
    {
        __state = false;
        if (!HubSession.Active || location != HubStructureStore.HubLocation) return;

        _ourBuild++;

        // The building history is a save field, and it is the one thing this call writes that has
        // nothing to do with the structure list. A type first seen in a hub must not teach the
        // player's save that they have built one.
        __state = data != null && DataManager.Instance != null &&
                  !DataManager.Instance.HistoryOfStructures.Contains(data.Type);
    }

    [HarmonyPatch(typeof(StructureManager), nameof(StructureManager.BuildStructure))]
    [HarmonyFinalizer]
    private static void StructureManager_BuildStructureFinalizer(FollowerLocation location,
        StructuresData data, bool __state)
    {
        if (!HubSession.Active || location != HubStructureStore.HubLocation) return;

        _ourBuild--;

        if (__state && data != null && DataManager.Instance != null)
            DataManager.Instance.HistoryOfStructures.Remove(data.Type);
    }

    // ---- the interception ---------------------------------------------------------------------

    // The brain is made either way, and the brain is the building: what the list does or does not
    // hold decides only whether the game will save it.
    [HarmonyPatch(typeof(StructureManager), nameof(StructureManager.AddStructure))]
    [HarmonyPostfix]
    private static void StructureManager_AddStructure(FollowerLocation location, StructuresData data,
        bool save, StructureBrain __result)
    {
        if (!Ours || data == null) return;

        if (save)
        {
            var list = StructureManager.StructuresDataAtLocation(location);
            if (list.Remove(data))
                Plugin.Log.LogInfo($"Hub build: {data.Type} kept out of the game's save; it is the " +
                                   "hub's, not the town's.");
        }

        HubStructureStore.Adopt(__result);
        CompleteBuildSite(__result);
    }

    // Nobody lives in a hub. A build site there would stand unbuilt for ever, because what finishes
    // one is a follower walking over with an armful of wood - so the moment it exists, it is done.
    // One frame later than this: the region stamps the site's grid cell and bounds after the call
    // that lands here, and finishing it before that would build it in the wrong place.
    private static void CompleteBuildSite(StructureBrain brain)
    {
        if (brain is not Structures_BuildSite site) return;
        if (Plugin.Instance == null) return;

        Plugin.Instance.StartCoroutine(FinishNextFrame(site));
    }

    private static IEnumerator FinishNextFrame(Structures_BuildSite site)
    {
        yield return null;

        if (site?.Data == null || site.Data.Destroyed) yield break;

        try
        {
            site.Build();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Hub build: a building could not be finished: " + e.Message);
        }
    }

    // Where a hub's buildings actually end up, said once each.
    //
    // The game parents a finished structure to its location's "structure layer", which for the town
    // room is the room object itself - not the content root everything the editor places lives
    // under. Whether that is what has them sorting against each other is exactly what this reports:
    // the depth they were given, the parent they were given it under, and the sorting the renderer
    // ended up with.
    // A building put up in a hub belongs to the hub's own content, not to the scene root.
    //
    // The game parents a finished structure to its location's "structure layer", which for the town
    // room is a reference to the room object - and the hub session cleared that object out from
    // under it, so the reference is dead and the parenting quietly becomes "no parent at all".
    // Everything downstream follows from that: the buildings sit outside the room's sorting
    // hierarchy that every other object in the hub is inside, so they sort against the world by
    // depth alone and, being flat on the same plane, fight each other for it. They would also have
    // outlived the hub, since nothing clears the scene root.
    [HarmonyPatch(typeof(LocationManager), nameof(LocationManager.PlaceStructure))]
    [HarmonyPostfix]
    private static void LocationManager_PlaceStructure(StructuresData structure, GameObject g)
    {
        if (!HubSession.Active || structure == null || g == null) return;
        if (structure.Location != HubStructureStore.HubLocation) return;

        var root = SceneRefs.ContentRoot;
        if (root != null && g.transform.parent != root)
            g.transform.SetParent(root, worldPositionStays: true);

        var renderer = g.GetComponentInChildren<SpriteRenderer>(true);
        var sorting = renderer == null
            ? "no sprite renderer"
            : $"layer '{renderer.sortingLayerName}' order {renderer.sortingOrder}";

        Plugin.Log.LogInfo($"Hub build: {structure.Type} at {g.transform.position} under " +
                           $"'{(g.transform.parent == null ? "<none>" : g.transform.parent.name)}', " +
                           $"cell {structure.GridTilePosition.x},{structure.GridTilePosition.y}, {sorting}.");

        if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(ReportSettled(g, structure.Type));
    }

    // Where it ended up, once the drop-in animation has had time to finish.
    //
    // The game builds a structure one unit further back and slides it forward over half a second -
    // on *scaled* time, and the whole placement session runs with the clock stopped. Anything that
    // interrupts that tween leaves the building at a depth of its own, which is exactly what a row
    // of buildings fighting each other over which is in front would look like.
    private static System.Collections.IEnumerator ReportSettled(GameObject g, StructureBrain.TYPES type)
    {
        yield return new WaitForSecondsRealtime(3f);

        if (g == null) yield break;

        var z = g.transform.position.z;
        if (Mathf.Abs(z) > 0.001f)
            Plugin.Log.LogWarning($"Hub build: {type} settled at depth {z:0.###} rather than 0 - its " +
                                  "drop-in never finished.");
        else
            Plugin.Log.LogInfo($"Hub build: {type} settled at depth 0.");
    }

    // The buildable squares, made legible on ground the game never anticipated.
    //
    // Vanilla draws an available square as white at half alpha - and in winter as *dark grey* at
    // half alpha. That is tuned for one place, the town, whose ground it knows. A hub's ground is
    // whatever its author drew, so a half-transparent white diamond can land on white snow and a
    // dark one on dark stone, and in both cases the grid is a rumour.
    //
    // Only the plain "you may build here" square is touched. Red for blocked and green for the
    // building being moved carry meaning and are already opaque.
    private static readonly Color AvailableTile = new(0.35f, 0.9f, 1f, 0.95f);

    [HarmonyPatch(typeof(PlacementTile), nameof(PlacementTile.SetColor))]
    [HarmonyPostfix]
    private static void PlacementTile_SetColor(PlacementTile __instance, Color color)
    {
        if (__instance == null || color != Color.white) return;

        // A reference compare, not a component search: this runs for every square of the grid on
        // every frame of a placement.
        var region = HubBuildTotem.Region;
        if (region == null || __instance.transform.parent != region.transform) return;

        if (__instance.spriteRenderer != null) __instance.spriteRenderer.color = AvailableTile;
    }

    // Floor decorations are not structures and never reach AddStructure, so they are written down
    // on their own. Mirrored after the fact rather than intercepted: the game has already recorded
    // them on the region's data by this point, and that list is what gets saved.
    [HarmonyPatch(typeof(PathTileManager), nameof(PathTileManager.SetTile),
        [typeof(StructureBrain.TYPES), typeof(Vector3)])]
    [HarmonyPostfix]
    private static void PathTileManager_SetTile() => RememberPaths();

    [HarmonyPatch(typeof(PathTileManager), nameof(PathTileManager.DeleteTile))]
    [HarmonyPostfix]
    private static void PathTileManager_DeleteTile() => RememberPaths();

    private static void RememberPaths()
    {
        if (!HubSession.Active || !HubStructureStore.Active) return;

        // Only when the tile manager doing the work is the hub's own copy; the town's writes to the
        // town's data and has nothing to do with this file.
        var manager = PathTileManager.Instance;
        if (manager == null || manager.GetComponentInParent<HubBuildRegion>() == null) return;

        HubStructureStore.SavePaths();
    }

    // ---- the totem ----------------------------------------------------------------------------

    // The placement routine reaches for a handful of base-scene singletons without checking any of
    // them. They should all be there - a hub runs in the base's own scene - but a null inside a
    // coroutine dies halfway through and leaves the player unable to move, so it is worth one look
    // before the menu opens rather than a locked game afterwards.
    [HarmonyPatch(typeof(Interaction_PlacementRegion), nameof(Interaction_PlacementRegion.OnInteract))]
    [HarmonyPrefix]
    private static bool Interaction_PlacementRegion_OnInteract(Interaction_PlacementRegion __instance)
    {
        if (__instance == null || __instance.placementRegion == null) return true;

        var hub = __instance.placementRegion.GetComponent<HubBuildRegion>();
        if (hub == null) return true;

        var missing = HubBuildTotem.MissingDependency();
        if (missing != null)
        {
            Plugin.Log.LogWarning($"Hub totem: not opening the build menu - {missing} is not " +
                                  "present in this scene.");
            return false;
        }

        // Only which cells are taken, never which cells exist - see HubBuildRegion.RefreshOccupancy.
        // The lattice itself is cut when the totem goes up, and a hub is stood up fresh on every
        // visit, so ground redrawn between visits is picked up there.
        hub.RefreshOccupancy();
        return true;
    }
}
