using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LambMainMenu = Lamb.UI.MainMenu.MainMenu;

namespace CustomSpineLoader.ModUI.MenuEditor;

// Puts one more button in the vanilla menu's column, cloned from a sibling so the hover animation,
// the font, the plate and the MMButton type all come along without being rebuilt.
//
// MMButton is not optional: UINavigatorNew resolves the next selectable with
// "FindSelectableOnUp() as IMMSelectable", so a plain UnityEngine.UI.Button is invisible to the
// controller no matter how it looks.
public static class MenuButtonInjector
{
    public const string ButtonName = "CT_CustomizeMenuButton";
    private const string Label = "Customize Menu";

    // Idempotent: the menu is rebuilt on every quit-to-menu, but the hook that calls this can fire
    // more than once within one menu, and a second button would be worse than none.
    public static GameObject Inject(LambMainMenu menu, Action onClick)
    {
        if (menu == null) return null;

        try
        {
            var quit = menu._quitButton;
            if (quit == null)
            {
                Plugin.Log.LogWarning("MenuEditor: the menu has no Quit button to sit above; " +
                                      "no button was added.");
                return null;
            }

            var container = quit.transform.parent;
            if (container == null) return null;

            var existing = container.Find(ButtonName);
            if (existing != null) return existing.gameObject;

            // Achievements, not Quit. Quit is the one button in this column with Explicit
            // navigation, so a clone of it would arrive holding a hard-wired pair of neighbours
            // that are not ours; every other sibling navigates Automatically and finds them.
            var template = menu._achievementsButton ?? menu._creditsButton ?? menu._settingsButton;
            if (template == null)
            {
                Plugin.Log.LogWarning("MenuEditor: no sibling button to clone; no button was added.");
                return null;
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, container, false);
            clone.name = ButtonName;
            clone.SetActive(true);

            // Directly above Quit, which is where a mod's own entry belongs: after everything the
            // game offers, before the way out.
            clone.transform.SetSiblingIndex(quit.transform.GetSiblingIndex());

            Delocalise(clone);
            Retitle(clone);
            Rewire(clone, onClick);
            Renavigate(clone, quit);

            // Settled here rather than at the end of the frame. One more row in a vertical layout
            // moves every button in the column, and the menu's highlight places itself by reading
            // a button's world position one frame after the selection changes - so a column that
            // is still shifting underneath it leaves the highlight sitting over a neighbour.
            if (container is RectTransform column)
                LayoutRebuilder.ForceRebuildLayoutImmediate(column);

            // The buttons MainMenu.EnableDemo takes away; this belongs with them.
            if (CheatConsole.IN_DEMO) clone.SetActive(false);

            Plugin.Log.LogInfo($"MenuEditor: added '{Label}' to the main menu.");
            return clone;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MenuEditor: the menu button could not be added: " + e);
            return null;
        }
    }

    // I2 would put the template's own term back over our label - on enable, and again on every
    // language change. Disabling first is the part that matters: OnDisable is what unhooks the
    // localisation event, and Destroy does not run it until the end of the frame.
    private static void Delocalise(GameObject clone)
    {
        foreach (var localize in clone.GetComponentsInChildren<I2.Loc.Localize>(true))
        {
            if (localize == null) continue;
            localize.enabled = false;
            UnityEngine.Object.Destroy(localize);
        }
    }

    private static void Retitle(GameObject clone)
    {
        foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true))
            if (text != null)
                text.text = Label;
    }

    private static void Rewire(GameObject clone, Action onClick)
    {
        var button = clone.GetComponent<Button>();
        if (button == null) return;

        // Runtime listeners are not copied by Instantiate, but a serialized one would be.
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick?.Invoke());
        button.interactable = true;
    }

    private static void Renavigate(GameObject clone, Button quit)
    {
        var self = clone.GetComponent<Selectable>();
        if (self == null) return;

        // Automatic, like every sibling but Quit: Unity finds the neighbours geometrically, so
        // nothing has to be told about the button above us.
        var mine = self.navigation;
        mine.mode = Navigation.Mode.Automatic;
        self.navigation = mine;

        // Quit's own navigation is Explicit and still points up at whatever used to be above it,
        // which would step straight over this button. Navigation is a struct, so it is read,
        // changed and written back whole.
        var theirs = quit.navigation;
        if (theirs.mode != Navigation.Mode.Explicit) return;

        theirs.selectOnUp = self;
        quit.navigation = theirs;
    }
}
