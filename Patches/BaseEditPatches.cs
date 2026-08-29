using CustomSpineLoader.MapEditor;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.Patches;

[HarmonyPatch]
public static class BaseEditPatches
{
    // ---- the buildable grid over added ground ----------------------------------------------------

    [HarmonyPatch(typeof(PlacementRegion), nameof(PlacementRegion.CreateFloodFill))]
    [HarmonyPrefix]
    private static void PlacementRegion_CreateFloodFillPrefix(PlacementRegion __instance)
    {
        if (__instance == null || __instance.GetComponent<HubBuildRegion>() != null) return;
        BaseGround.TrimAddedPaths(__instance);
    }

    [HarmonyPatch(typeof(PlacementRegion), nameof(PlacementRegion.CreateFloodFill))]
    [HarmonyPostfix]
    private static void PlacementRegion_CreateFloodFill(PlacementRegion __instance)
    {
        if (__instance == null) return;

        if (__instance.GetComponent<HubBuildRegion>() != null) return;
        if (!BaseGround.HasModGround) return;
        if (PlayerFarming.Location != FollowerLocation.Base) return;

        BaseGround.ExtendGrid(__instance);
    }

    // ---- buildings on added ground ---------------------------------------------------------------

    [HarmonyPatch(typeof(StructureManager), nameof(StructureManager.AddStructure))]
    [HarmonyPostfix]
    private static void StructureManager_AddStructure(FollowerLocation location, StructuresData data,
        bool save, StructureBrain __result)
    {
        if (data == null || !save) return;
        if (location != FollowerLocation.Base) return;
        if (HubSession.Busy) return;
        if (!BaseGround.HasModGround) return;
        if (!BaseGround.IsModGround(data.Position)) return;

        var list = StructureManager.StructuresDataAtLocation(location);
        if (list.Remove(data))
            Plugin.Log.LogInfo($"Base editor: {data.Type} stands on ground this mod added, so the " +
                               "game's save is not told about it - it is remembered here instead.");

        BaseDelta.AdoptModGroundStructure(__result);
    }

    [HarmonyPatch(typeof(StructureManager), nameof(StructureManager.BuildStructure))]
    [HarmonyPrefix]
    private static void StructureManager_BuildStructurePrefix(FollowerLocation location,
        StructuresData data, Vector3 position, out bool __state)
    {
        __state = false;

        if (location != FollowerLocation.Base || data == null) return;
        if (HubSession.Busy || !BaseGround.HasModGround) return;
        if (!BaseGround.IsModGround(position)) return;

        __state = DataManager.Instance != null &&
                  !DataManager.Instance.HistoryOfStructures.Contains(data.Type);
    }

    [HarmonyPatch(typeof(StructureManager), nameof(StructureManager.BuildStructure))]
    [HarmonyFinalizer]
    private static void StructureManager_BuildStructureFinalizer(StructuresData data, bool __state)
    {
        if (!__state || data == null || DataManager.Instance == null) return;
        DataManager.Instance.HistoryOfStructures.Remove(data.Type);
    }

    [HarmonyPatch(typeof(ObjectiveManager), nameof(ObjectiveManager.CheckObjectives),
        [typeof(Objectives.TYPES)])]
    [HarmonyPrefix]
    private static bool ObjectiveManager_CheckObjectives() => !BaseDelta.PlacingOurOwn;

    // ---- the save mask ---------------------------------------------------------------------------

    [HarmonyPatch(typeof(SaveAndLoad), "Saving")]
    [HarmonyPrefix]
    private static void SaveAndLoad_Saving()
    {
        if (SaveMask.Anything) SaveMask.Engage();
    }

    // ---- where the player lands ---------------------------------------------------------------------

    private static bool _arrivingInBase;
    private static GameObject _spawnAnchor;

    [HarmonyPatch(typeof(Interaction_BaseTeleporter), nameof(Interaction_BaseTeleporter.TeleportIn))]
    [HarmonyPrefix]
    private static void Interaction_BaseTeleporter_TeleportIn()
    {
        if (HubSession.Busy || Plugin.Instance == null) return;

        var spawn = BaseDelta.SpawnPoint();
        if (!spawn.HasValue) return;

        if (_spawnAnchor == null) _spawnAnchor = new GameObject("CultTweaker_BaseArrivalCamera");
        _spawnAnchor.transform.position = spawn.Value;

        _arrivingInBase = true;
        Plugin.Instance.StartCoroutine(LandOnSpawnPoint(spawn.Value));
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.OnConversationNext))]
    [HarmonyPrefix]
    private static void GameManager_OnConversationNext(ref GameObject Speaker)
    {
        if (!_arrivingInBase || _spawnAnchor == null || Speaker == null) return;

        var teleporter = Interaction_BaseTeleporter.Instance;
        if (teleporter == null || Speaker != teleporter.gameObject) return;

        Speaker = _spawnAnchor;
        Plugin.Log.LogInfo("Base editor: the arrival camera watches this base's spawn point rather " +
                           "than the portal.");
    }

    private static System.Collections.IEnumerator LandOnSpawnPoint(Vector3 spawn)
    {
        for (var frame = 0; frame < 6; frame++)
        {
            yield return null;

            if (PlayerFarming.Instance == null) continue;

            PlayerFarming.PositionAllPlayers(spawn);
            foreach (var player in PlayerFarming.players)
                if (player != null) player.transform.position = spawn;
        }

        _arrivingInBase = false;

        var trigger = BaseDelta.SpawnTrigger();
        var landed = PlayerFarming.Instance != null
            ? PlayerFarming.Instance.transform.position
            : spawn;

        Plugin.Log.LogInfo($"Base editor: spawn trigger centred on {spawn}, " +
                           $"{(trigger == null ? "size unknown" : $"{trigger.Width:0.##} x {trigger.Height:0.##}")}; " +
                           $"the player came to rest at {landed}, " +
                           $"{Vector3.Distance(landed, spawn):0.###} away.");
    }

    // ---- the loader, kept away from anything this mod wrote -----------------------------------------

    [HarmonyPatch(typeof(LocationManager), nameof(LocationManager.PlaceStructures))]
    [HarmonyPrefix]
    private static void LocationManager_PlaceStructures(LocationManager __instance)
    {
        if (__instance == null || __instance.Location != FollowerLocation.Base) return;
        if (HubSession.Busy) return;

        BaseDelta.RepairSavedPositions();
    }

    // ---- ordering insurance -----------------------------------------------------------------------

    [HarmonyPatch(typeof(LocationManager), nameof(LocationManager.EnsureWithinBounds))]
    [HarmonyPostfix]
    private static void LocationManager_EnsureWithinBounds(Vector3 pos, ref bool __result)
    {
        if (__result || !BaseGround.HasModGround) return;
        if (!BaseGround.IsModGround(pos)) return;

        __result = true;
        Plugin.Log.LogInfo($"Base editor: a saved structure at {pos} stands on ground this mod " +
                           "added; it was kept rather than culled.");
    }

    [HarmonyPatch(typeof(Follower), nameof(Follower.EnsureWithinBounds))]
    [HarmonyPrefix]
    private static bool Follower_EnsureWithinBounds(Follower __instance)
    {
        if (__instance == null || !BaseGround.HasModGround) return true;
        if (PlayerFarming.Location != FollowerLocation.Base) return true;

        return !BaseGround.IsModGround(__instance.transform.position);
    }
}
