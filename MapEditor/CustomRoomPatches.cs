using HarmonyLib;
using MMRoomGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class CustomRoomMarker : MonoBehaviour { }

public static class CustomRoomPatches
{
    public static void Mark(GenerateRoom room)
    {
        if (room != null && room.GetComponent<CustomRoomMarker>() == null)
            room.gameObject.AddComponent<CustomRoomMarker>();
    }

    private static bool IsCustom(GenerateRoom room) =>
        room != null && room.GetComponent<CustomRoomMarker>() != null;

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
            Plugin.Log.LogInfo("MapEditor: suppressed vanilla decoration respawn in custom room.");
            return false;
        }
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
        }
    }
}
