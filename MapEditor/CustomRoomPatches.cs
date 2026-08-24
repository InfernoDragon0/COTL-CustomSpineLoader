using System.Collections;
using HarmonyLib;
using MMRoomGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class CustomRoomMarker : MonoBehaviour { }

public static class CustomRoomPatches
{
    public static void Mark(GenerateRoom room)
    {
        if (room == null) return;

        if (room.GetComponent<CustomRoomMarker>() == null)
            room.gameObject.AddComponent<CustomRoomMarker>();

        SetCustomDecorations(room, true);
    }

    private static bool IsCustom(GenerateRoom room) =>
        room != null && room.GetComponent<CustomRoomMarker>() != null;

    // Vanilla's own switch for "the scenery in this room is not mine": the only thing that reads it
    // is OnDisable, which otherwise recycles every SceneryTransform child on the way out - and
    // ObjectPool.Recycle DESTROYS anything the pool did not spawn. Authored props were being thrown
    // away when the room was left, and a revisit never brings them back because it does not re-run
    // Generate and so never re-applies the blueprint. Private, hence the reflection.
    private static readonly System.Reflection.FieldInfo CustomDecorationsField =
        AccessTools.Field(typeof(GenerateRoom), "customDecorations");

    private static void SetCustomDecorations(GenerateRoom room, bool value)
    {
        if (room == null || CustomDecorationsField == null) return;

        try
        {
            CustomDecorationsField.SetValue(room, value);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not set the room's customDecorations flag: " +
                                  e.Message);
        }
    }

    private static IEnumerator Nothing()
    {
        yield break;
    }

    public static bool HasBackSprite(GenerateRoom room)
    {
        var composite = room != null ? room.RoomTransform : null;
        if (composite == null) return false;

        foreach (Transform child in composite.transform)
            if (child != null && child.name.StartsWith("Room Back Sprite")) return true;
        return false;
    }

    // A later room swap can destroy islands the loader registered in room.Pieces; vanilla's
    // decoration coroutine dies on the first destroyed entry, so prune before it runs.
    [HarmonyPatch(typeof(GenerateRoom), "OnEnable")]
    private static class GenerateRoom_OnEnable_Patch
    {
        private static void Prefix(GenerateRoom __instance)
        {
            try
            {
                __instance.Pieces?.RemoveAll(p => p == null);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: could not prune the room's island list: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.SpawnHeavyAssets))]
    private static class GenerateRoom_SpawnHeavyAssets_Patch
    {
        private static bool Prefix(GenerateRoom __instance)
        {
            if (!IsCustom(__instance)) return true;
            Plugin.Log.LogInfo("MapEditor: suppressed vanilla heavy-asset respawn in custom room.");
            return false;
        }
    }

    // Re-entering a room is not a regeneration. The room object is only switched off and on again,
    // and OnEnable then runs RegenerateDecorationsWithPool over a room it still believes it
    // generated - which re-rolls the biome's trees, rocks and critters into it, and spawns them
    // INSIDE sprite shapes, i.e. all over the author's own floor. That is where the random trees in
    // a custom room came from, on every revisit. The generation-window suppression in LevelPlayback
    // cannot catch this one: by the time a room is revisited that window is long closed, so the
    // marker on the room is what answers instead.
    //
    // GeneratedDecorations is still set: BiomeGenerator blocks the arrival until it turns true.
    [HarmonyPatch(typeof(GenerateRoom), "SpawnDecorations")]
    private static class GenerateRoom_SpawnDecorations_Patch
    {
        private static bool Prefix(GenerateRoom __instance, ref IEnumerator __result)
        {
            if (!IsCustom(__instance)) return true;

            __instance.GeneratedDecorations = true;
            __result = Nothing();
            return false;
        }
    }

    // The tail of that same re-entry pass, and just as wrong here: it switches off ANY
    // SceneryTransform child within three units of a door, on the assumption that everything under
    // there is scattered biome dressing. In a custom room it is a prop the author put there.
    [HarmonyPatch(typeof(GenerateRoom), "DisableDecorationsNearDoor")]
    private static class GenerateRoom_DisableDecorationsNearDoor_Patch
    {
        private static bool Prefix(GenerateRoom __instance) => !IsCustom(__instance);
    }

    // Vanilla never removes old backdrops; without this a custom room gains one per visit.
    [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.CreateBackgroundSpriteShape))]
    private static class GenerateRoom_CreateBackgroundSpriteShape_Patch
    {
        private static bool Prefix(GenerateRoom __instance)
        {
            if (!IsCustom(__instance)) return true;
            return !HasBackSprite(__instance);
        }
    }

    // A real regeneration replaces everything - the room is vanilla again.
    [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.Generate),
        typeof(int), typeof(GenerateRoom.ConnectionTypes), typeof(GenerateRoom.ConnectionTypes),
        typeof(GenerateRoom.ConnectionTypes), typeof(GenerateRoom.ConnectionTypes))]
    private static class GenerateRoom_Generate_Patch
    {
        private static void Prefix(GenerateRoom __instance)
        {
            var marker = __instance.GetComponent<CustomRoomMarker>();
            if (marker != null) Object.Destroy(marker);

            // Its scenery is the biome's again, and the pool wants it back on the way out.
            SetCustomDecorations(__instance, false);
        }
    }
}
