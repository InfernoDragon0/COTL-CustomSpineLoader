using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using Spine.Unity;

namespace CustomSpineLoader.Patches;

[HarmonyPatch]
public static class SkeletonAssetPatches
{
    [HarmonyPatch(typeof(SkeletonDataAsset), nameof(SkeletonDataAsset.Clear))]
    [HarmonyPrefix]
    private static bool SkeletonDataAsset_Clear(SkeletonDataAsset __instance)
    {
        return !SpineFolderLoader.IsJsonFreed(__instance);
    }
}

[HarmonyPatch]
public static class SkeletonLodPatches
{
    [HarmonyPatch(typeof(SkeletonAnimationLODGlobalManager), "Update")]
    [HarmonyPrefix]
    private static bool SkeletonAnimationLODGlobalManager_Update()
    {
        return MonoSingleton<Lamb.UI.UIManager>.Instance != null;
    }
}
