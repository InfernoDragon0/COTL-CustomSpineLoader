using HarmonyLib;
using Lamb.UI.MainMenu;
using CustomSpineLoader.ModUI.MenuEditor;

namespace CustomSpineLoader.Patches;

// The second of the two passes that dress the main menu, and the one that adds the button.
//
// The first pass runs on the scene load (Plugin.OnSceneLoaded) and puts on everything the game will
// not touch again: the title, the centrepiece, the scene effects. It cannot do the palette, because
// MainMenuController.Start runs after it and writes "LerpPalette = 0" and EnableSecondPalette()
// over anything already there.
//
// StartInput is where the intro ends: it has no callers in the game's own code at all, and is fired
// by an animation event on the "Intro" clip, once per menu load. That makes it the exact moment
// after Start and before the player can press anything - which is what both the palette and the
// button want.
[HarmonyPatch]
public static class MainMenuPatches
{
    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.StartInput))]
    [HarmonyPostfix]
    private static void MainMenuController_StartInput()
    {
        // The scene load binds; this only re-checks, because a menu reached some other way (the
        // demo's own scene reload, say) may not have gone through OnSceneLoaded first.
        if (!MenuSceneRefs.Ready) MenuSceneRefs.Bind();

        MenuPresetApplier.ApplyLook();

        if (Plugin.MainMenuEnabled is { Value: false }) return;

        MenuButtonInjector.Inject(MenuSceneRefs.Menu, () => MainMenuEditor.Instance?.Open());
    }

    // ChangeLogoPerLanguage re-asserts its own sprite on every localisation event. The applier
    // disables the component, which stops that - but belt and braces, since a language change is
    // exactly when a player would notice their title reverting.
    [HarmonyPatch(typeof(ChangeLogoPerLanguage), nameof(ChangeLogoPerLanguage.UpdateLogo))]
    [HarmonyPostfix]
    private static void ChangeLogoPerLanguage_UpdateLogo()
    {
        if (!MenuSceneRefs.Ready) return;
        if (MenuPresetApplier.Active?.Title is not { Enabled: true }) return;

        MenuPresetApplier.ApplyStructural();
    }
}
