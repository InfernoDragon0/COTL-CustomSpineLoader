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

    // Also called on every re-entry (and once by the loader, deliberately). Vanilla never
    // removes old copies, so without this a custom room gains one backdrop per visit.
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
