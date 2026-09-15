using CustomSpineLoader.ModUI;
using HarmonyLib;
using Lamb.UI.PauseMenu;

namespace CustomSpineLoader.Patches;

[HarmonyPatch]
public static class PauseMenuPatches
{
    /// The pause menu is instantiated fresh each time the player pauses and destroyed on hide, so
    /// the button is added in Start, after the game has wired its own buttons up.
    [HarmonyPatch(typeof(UIPauseMenuController), "Start")]
    [HarmonyPostfix]
    private static void UIPauseMenuController_Start(UIPauseMenuController __instance)
    {
        PauseMenuButton.Inject(__instance);
    }
}
