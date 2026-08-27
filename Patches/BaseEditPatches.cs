using CustomSpineLoader.MapEditor;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.Patches;

// The base, extended without its save file ever hearing about it.
//
// Three jobs, and all three exist because the base is the player's own and this mod's changes to it
// must be invisible to their save:
//
//   - the buildable grid has to reach over ground the mod added, which the vanilla fill both cannot
//     see and would overwrite;
//   - a building the player puts up on that added ground has to be lifted out of their save, because
//     the game's own loader would delete it on the way back in and take the save entry with it;
//   - and every write of the save file has to see the base exactly as the game left it, moved
//     buildings included.
[HarmonyPatch]
public static class BaseEditPatches
{
    // ---- the buildable grid over added ground ----------------------------------------------------

    // A postfix, deliberately, where the hub's equivalent is a prefix that replaces the fill outright.
    // A hub's region is ours and its ground is all authored; the base's region is the player's, its
    // fill knows about bought DLC land and the bridge, and none of that is ours to reimplement. So
    // vanilla runs untouched and the added ground is a second pass over what it left.
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

        // A hub's region cuts its own grid and has nothing to do with the base's ground.
        if (__instance.GetComponent<HubBuildRegion>() != null) return;
        if (!BaseGround.HasModGround) return;
        if (PlayerFarming.Location != FollowerLocation.Base) return;

        BaseGround.ExtendGrid(__instance);
    }

    // ---- buildings on added ground ---------------------------------------------------------------

    // Same shape as the hub's interception, for the same reason: the brain is made either way, so
    // what the list does or does not hold decides only whether the game will try to save it.
    //
    // The scope is much narrower here. A hub intercepts every build while it is open, because the
    // whole room is ours. In the base almost every build is the player's own business and is left
    // completely alone - only one standing on ground this mod added is taken out, and only because
    // the game's own loader would delete it (and its save entry) the next time they came home.
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

    // The building history is a save field, and it is the one thing a build writes that has nothing
    // to do with the structure list. A type first put up on mod ground must not teach the player's
    // save that they have built one - the game hands out achievements and unlocks off that list.
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

    // A structure the editor stands up in the base is not a structure the player placed, and the
    // objective system cannot tell the difference. Left alone it would tick "place structures"
    // quests forward - once per building, and again on every arrival, since the saved base is
    // rebuilt each time. Scoped to exactly the calls this mod makes on its own behalf.
    [HarmonyPatch(typeof(ObjectiveManager), nameof(ObjectiveManager.CheckObjectives),
        [typeof(Objectives.TYPES)])]
    [HarmonyPrefix]
    private static bool ObjectiveManager_CheckObjectives() => !BaseDelta.PlacingOurOwn;

    // ---- the save mask ---------------------------------------------------------------------------

    // The one place a moved building can be hidden without racing the serializer.
    //
    // The writer hands the live DataManager to a background thread, so anything done "around" a save
    // is a coin toss. This is the single statement that spawns that thread, and it runs on the main
    // thread; SaveMask puts the moves back when the write reports itself finished, which the game
    // marshals back onto the main thread for us.
    //
    // Deliberately not the writer's own Write method, tempting as that is for being the last word:
    // it is generic over a reference type, so the runtime shares one compiled body between the save
    // file and the menu's metadata, and a patch there would fire for writes whose completion this
    // never hears about.
    [HarmonyPatch(typeof(SaveAndLoad), "Saving")]
    [HarmonyPrefix]
    private static void SaveAndLoad_Saving()
    {
        if (SaveMask.Anything) SaveMask.Engage();
    }

    // ---- where the player lands ---------------------------------------------------------------------

    // Arriving in the base means coming home, and nothing else.
    //
    // The obvious hook - LocationManager.PositionPlayer, which every arrival goes through - turned
    // out to be too many arrivals. Stepping out of the temple is one, and so is coming back from the
    // door room and the shrine room: those are doorways inside the base, and the game has a spot for
    // each of them. Landing on the spawn point out of the temple door is not what an author means by
    // "the player arrives here", it is being teleported away from where they just were.
    //
    // So the only hook is the warp-in below, which the game runs for a real arrival and for nothing
    // else. Interior doorways keep the positions the game chose for them.

    // The arrival, redirected to the spawn point without moving the portal.
    //
    // Interaction_BaseTeleporter's warp-in ties three things the player sees to the teleporter's own
    // transform: it places the player on it, it snaps the camera to it, and it frames it for the
    // length of the animation. That last one is what made re-placing the player look broken - the
    // lamb turned up at the spawn point while the camera watched an empty portal, because
    // OnConversationNext hands the camera a GameObject to follow and holds it there however often
    // anything else asks it to look elsewhere.
    //
    // But it is *handed* that object, so it can be handed a different one. A stand-in parked on the
    // spawn point is enough: the camera follows it, and the player is put on the mark over the frames
    // that follow. The portal keeps its place in the base.
    //
    // What cannot follow is the warp effect itself. It is an animation on the portal's own skeleton,
    // so it plays where the portal is - off-screen, since the camera is elsewhere. The player simply
    // arrives.
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

    // Only the teleporter's own framing call, and only while it is bringing the player in. Every
    // other conversation in the game - every follower, every shopkeeper - is left alone.
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

    // A handful of frames rather than one: the warp-in writes the player's position early in its own
    // coroutine, and exactly which frame that lands on is not worth depending on. It stops well
    // before the player has control, so it can never fight them for it.
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

        // Where the player actually came to rest against where the trigger's middle is, because the
        // two are supposed to be the same point and a report of one of them cannot show that they
        // are not. The trigger's own transform is its centre by construction - its collider sits on
        // it and its outline is drawn symmetrically around it - so any difference here is something
        // moving the player afterwards, and this is what would name it.
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

    // Runs before the loader walks the player's buildings, because what it does to one it dislikes
    // is delete it - see BaseDelta.RepairSavedPositions. A prefix on the iterator's stub, so it lands
    // before a single entry has been looked at.
    [HarmonyPatch(typeof(LocationManager), nameof(LocationManager.PlaceStructures))]
    [HarmonyPrefix]
    private static void LocationManager_PlaceStructures(LocationManager __instance)
    {
        if (__instance == null || __instance.Location != FollowerLocation.Base) return;
        if (HubSession.Busy) return;

        BaseDelta.RepairSavedPositions();
    }

    // ---- ordering insurance -----------------------------------------------------------------------

    // The loader culls save entries that fall outside the base's ground polygon. Ours is appended to
    // that polygon by the delta apply, which runs after the base has arrived - so on paper a build on
    // added ground could be read back before the ground it stands on exists. It cannot in practice
    // (those builds are not in the game's save at all, by the arm above), but a structure the *player*
    // moved onto added ground through the game's own edit mode would be, and losing one of those
    // would be losing something of theirs.
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

    // Followers are teleported to the town centre when they wander outside the base. Ground this mod
    // added is inside the base as far as anyone living there is concerned - without this they are
    // pulled off it every ten seconds.
    //
    // A postfix cannot see that decision, so this is the same test done first: a follower standing on
    // added ground is left where they are and the vanilla check is skipped.
    [HarmonyPatch(typeof(Follower), nameof(Follower.EnsureWithinBounds))]
    [HarmonyPrefix]
    private static bool Follower_EnsureWithinBounds(Follower __instance)
    {
        if (__instance == null || !BaseGround.HasModGround) return true;
        if (PlayerFarming.Location != FollowerLocation.Base) return true;

        return !BaseGround.IsModGround(__instance.transform.position);
    }
}
