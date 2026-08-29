using HarmonyLib;
using Lamb.UI.MainMenu;
using CustomSpineLoader.ModUI.MenuEditor;

namespace CustomSpineLoader.Patches;

[HarmonyPatch]
public static class MainMenuPatches
{
    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.StartInput))]
    [HarmonyPostfix]
    private static void MainMenuController_StartInput()
    {
        if (!MenuSceneRefs.Ready) MenuSceneRefs.Bind();

        MenuPresetApplier.ApplyLook();

        MenuPresetApplier.ApplyStructural();

        if (Plugin.MainMenuEnabled is { Value: false }) return;

        MenuButtonInjector.Inject(MenuSceneRefs.Menu, () => MainMenuEditor.Instance?.Open());
    }
}
