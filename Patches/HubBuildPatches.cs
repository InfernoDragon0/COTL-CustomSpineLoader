using System.Collections;
using CustomSpineLoader.MapEditor;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.Patches;

[HarmonyPatch]
public static class HubBuildPatches
{
    private static int _ourBuild;

    private static bool Ours => _ourBuild > 0;

    // ---- the grid -----------------------------------------------------------------------------

    [HarmonyPatch(typeof(PlacementRegion), nameof(PlacementRegion.CreateFloodFill))]
    [HarmonyPrefix]
    private static bool PlacementRegion_CreateFloodFill(PlacementRegion __instance)
    {
        var hub = __instance.GetComponent<HubBuildRegion>();
        if (hub == null) return true;

        hub.RebuildGrid();
        return false;
    }

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

    private static System.Collections.IEnumerator CheckTiles(PlacementRegion region)
    {
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

    public static void RestoreTownRegion()
    {
        if (!_swapped) return;

        PlacementRegion.Instance = _townRegion;
        _townRegion = null;
        _swapped = false;
    }

    // ---- scoping ------------------------------------------------------------------------------

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

    [HarmonyPatch(typeof(StructureManager), nameof(StructureManager.BuildStructure))]
    [HarmonyPrefix]
    private static void StructureManager_BuildStructurePrefix(FollowerLocation location,
        StructuresData data, out bool __state)
    {
        __state = false;
        if (!HubSession.Active || location != HubStructureStore.HubLocation) return;

        _ourBuild++;

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

    private static readonly Color AvailableTile = new(0.35f, 0.9f, 1f, 0.95f);

    [HarmonyPatch(typeof(PlacementTile), nameof(PlacementTile.SetColor))]
    [HarmonyPostfix]
    private static void PlacementTile_SetColor(PlacementTile __instance, Color color)
    {
        if (__instance == null || color != Color.white) return;

        var region = HubBuildTotem.Region;
        if (region == null || __instance.transform.parent != region.transform) return;

        if (__instance.spriteRenderer != null) __instance.spriteRenderer.color = AvailableTile;
    }

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

        var manager = PathTileManager.Instance;
        if (manager == null || manager.GetComponentInParent<HubBuildRegion>() == null) return;

        HubStructureStore.SavePaths();
    }

    // ---- the totem ----------------------------------------------------------------------------

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

        hub.RefreshOccupancy();
        return true;
    }
}
