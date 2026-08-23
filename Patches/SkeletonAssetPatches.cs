using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using Spine.Unity;

namespace CustomSpineLoader.Patches;

// The game refreshes some skeletons by round-tripping their asset: Clear() then
// GetSkeletonData() re-parses the JSON (Interaction_EntranceShrine.ReloadStatue does this to
// the player-dummy statues). Our runtime assets free their JSON once parsed - the file never
// changes, so the cached data is the file - which turns that round trip into "Skeleton JSON
// file not set" and a skeleton that never initialises again. For those assets Clear() is
// skipped: GetSkeletonData already answers with exactly what a re-parse would produce.
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

// The game's skeleton-LOD manager dereferences UIManager.Instance on its first line, and the
// game tears that singleton down during every scene switch (its own loader literally waits on
// "UIManager.Instance == null") - so each transition spams NullReferenceExceptions from
// SkeletonAnimationLODGlobalManager.Update. Vanilla noise, but it buries real errors; the
// update is skipped for the frames where its first dereference would throw.
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
