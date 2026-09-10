using System.Collections.Generic;
using System.Reflection;
using CustomSpineLoader.APIHelper.NpcQuests;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.DeathScreen;

namespace CustomSpineLoader.Patches;

/// <summary>
/// Where custom NPC quests listen to the game.
///
/// Everything a goal can watch is either polled (see QuestGoals) or arrives here. The game already
/// funnels its own quest events through a handful of ObjectiveManager entry points, so hooking
/// those gives custom quests the same signals vanilla ones get, rather than a second, parallel
/// set of hooks into rituals, cooking and the rest.
/// </summary>
[HarmonyPatch]
public static class QuestPatches
{
    // ---- the save file stays free of our display lines --------------------------------------------

    [HarmonyPatch(typeof(SaveAndLoad), "Saving")]
    [HarmonyPrefix]
    private static void SaveAndLoad_Saving() => QuestSaveMask.Engage();

    // ---- a run was cleared ------------------------------------------------------------------------

    /// Both overloads, so a boss run and a plain exit both count. The result screen is the one
    /// place a cleared run is announced whatever the dungeon was, custom ones included.
    [HarmonyPatch]
    private static class DeathScreen_Patch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets()
        {
            foreach (var method in typeof(UIManager).GetMethods())
                if (method.Name == nameof(UIManager.ShowDeathScreenOverlay)) yield return method;
        }

        [HarmonyPostfix]
        private static void Postfix(UIDeathScreenOverlayController.Results result)
        {
            if (result != UIDeathScreenOverlayController.Results.Completed &&
                result != UIDeathScreenOverlayController.Results.BeatenBoss &&
                result != UIDeathScreenOverlayController.Results.BeatenMiniBoss &&
                result != UIDeathScreenOverlayController.Results.BeatenBossNoDamage) return;

            QuestRuntime.NoteDungeonCleared(PlayerFarming.Location);
        }
    }

    // ---- the game's own quest events ---------------------------------------------------------------

    [HarmonyPatch(typeof(ObjectiveManager), nameof(ObjectiveManager.CompleteRitualObjective))]
    [HarmonyPostfix]
    private static void ObjectiveManager_CompleteRitualObjective(UpgradeSystem.Type ritualType) =>
        QuestRuntime.NoteRitual(ritualType);

    [HarmonyPatch(typeof(ObjectiveManager), nameof(ObjectiveManager.CompleteCustomObjective))]
    [HarmonyPostfix]
    private static void ObjectiveManager_CompleteCustomObjective(
        Objectives.CustomQuestTypes customQuestType) =>
        QuestRuntime.NoteGameEvent(customQuestType);
}
