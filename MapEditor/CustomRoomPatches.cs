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

    [HarmonyPatch(typeof(GenerateRoom), "DisableDecorationsNearDoor")]
    private static class GenerateRoom_DisableDecorationsNearDoor_Patch
    {
        private static bool Prefix(GenerateRoom __instance) => !IsCustom(__instance);
    }

    [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.CreateBackgroundSpriteShape))]
    private static class GenerateRoom_CreateBackgroundSpriteShape_Patch
    {
        private static bool Prefix(GenerateRoom __instance)
        {
            if (!IsCustom(__instance)) return true;
            return !HasBackSprite(__instance);
        }
    }

    [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.Generate),
        typeof(int), typeof(GenerateRoom.ConnectionTypes), typeof(GenerateRoom.ConnectionTypes),
        typeof(GenerateRoom.ConnectionTypes), typeof(GenerateRoom.ConnectionTypes))]
    private static class GenerateRoom_Generate_Patch
    {
        private static void Prefix(GenerateRoom __instance)
        {
            var marker = __instance.GetComponent<CustomRoomMarker>();
            if (marker != null) Object.Destroy(marker);

            SetCustomDecorations(__instance, false);
        }
    }
}
