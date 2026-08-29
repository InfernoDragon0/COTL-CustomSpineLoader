using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LambMainMenu = Lamb.UI.MainMenu.MainMenu;

namespace CustomSpineLoader.ModUI.MenuEditor;

public static class MenuButtonInjector
{
    public const string ButtonName = "CT_CustomizeMenuButton";
    private const string Label = "Customize Menu";

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

            var template = menu._achievementsButton ?? menu._creditsButton ?? menu._settingsButton;
            if (template == null)
            {
                Plugin.Log.LogWarning("MenuEditor: no sibling button to clone; no button was added.");
                return null;
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, container, false);
            clone.name = ButtonName;
            clone.SetActive(true);

            clone.transform.SetSiblingIndex(quit.transform.GetSiblingIndex());

            Delocalise(clone);
            Retitle(clone);
            Rewire(clone, onClick);
            Renavigate(clone, quit);

            if (container is RectTransform column)
                LayoutRebuilder.ForceRebuildLayoutImmediate(column);

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

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick?.Invoke());
        button.interactable = true;
    }

    private static void Renavigate(GameObject clone, Button quit)
    {
        var self = clone.GetComponent<Selectable>();
        if (self == null) return;

        var mine = self.navigation;
        mine.mode = Navigation.Mode.Automatic;
        self.navigation = mine;

        var theirs = quit.navigation;
        if (theirs.mode != Navigation.Mode.Explicit) return;

        theirs.selectOnUp = self;
        quit.navigation = theirs;
    }
}
