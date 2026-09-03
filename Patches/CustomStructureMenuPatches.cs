using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using HarmonyLib;
using Lamb.UI.BuildMenu;

namespace CustomSpineLoader.Patches;

// A structure with "hideFromBuildMenu": true in its config stays fully registered -- it keeps its type,
// its prefab, its cost and its saved-map placements -- it just does not appear as something the player
// can choose to build. That is what a piece of scenery belonging to a hand-built map wants: present in
// the world, absent from the cult's menu.
[HarmonyPatch]
public static class CustomStructureMenuPatches
{
    // FollowerCategory.GetStructuresForCategory is the single funnel for the follower build menu: it
    // fills the Misc, Food and Items tabs and the aggregate list they share. COTL_API adds every custom
    // structure here in its own postfix, so this one has to run after it to take any back out again --
    // hence Priority.Last and HarmonyAfter. Removing them here rather than refusing to register them
    // keeps everything else about the structure working.
    [HarmonyPatch(typeof(FollowerCategory), nameof(FollowerCategory.GetStructuresForCategory))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("io.github.xhayper.COTL_API")]
    private static void FollowerCategory_GetStructuresForCategory(ref List<StructureBrain.TYPES> __result)
    {
        if (__result == null || CustomStructureLoader.HiddenFromBuildMenu.Count == 0) return;

        __result.RemoveAll(CustomStructureLoader.HiddenFromBuildMenu.Contains);
    }
}
