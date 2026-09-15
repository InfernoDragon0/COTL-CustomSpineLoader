using System;
using Lamb.UI.PauseMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI;

/// <summary>
/// Adds a "CultTweaker" button to the escape menu that opens the F7 panel. The panel refuses to
/// run under a menu (the pause menu pins <c>Time.timeScale</c> to 0 every frame and the panel wants
/// 0.1), so the button hides the menu first and opens on <c>OnHidden</c>, by which point the game's
/// own handler has already put the time scale back to 1 and the panel saves a sane value to restore.
/// </summary>
public static class PauseMenuButton
{
    public const string ButtonName = "CT_CultTweakerButton";
    private const string Label = "CultTweaker";

    public static GameObject Inject(UIPauseMenuController menu)
    {
        if (menu == null) return null;

        try
        {
            var template = menu._settingsButton ?? menu._helpButton ?? menu._photoModeButton;
            if (template == null)
            {
                Plugin.Log.LogWarning("CultTweaker: the pause menu has no button to clone; no " +
                                      "button was added.");
                return null;
            }

            var container = template.transform.parent;
            if (container == null) return null;

            var existing = container.Find(ButtonName);
            if (existing != null) return existing.gameObject;

            var clone = UnityEngine.Object.Instantiate(template.gameObject, container, false);
            clone.name = ButtonName;
            clone.SetActive(true);

            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            Delocalise(clone);
            Retitle(clone);
            Rewire(clone, menu);
            Renavigate(clone, template);

            if (container is RectTransform column)
                LayoutRebuilder.ForceRebuildLayoutImmediate(column);

            Plugin.Log.LogInfo($"CultTweaker: added '{Label}' to the pause menu.");
            return clone;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("CultTweaker: the pause menu button could not be added: " + e);
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

    private static void Rewire(GameObject clone, UIPauseMenuController menu)
    {
        var button = clone.GetComponent<Button>();
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OpenPanel(menu));
        button.interactable = true;
    }

    private static void OpenPanel(UIPauseMenuController menu)
    {
        if (menu == null || menu.IsHiding) return;

        menu.OnHidden = (Action)Delegate.Combine(menu.OnHidden, (Action)OpenPanelNow);
        menu.Hide();
    }

    private static void OpenPanelNow()
    {
        var panel = CultTweakerPanel.Active;
        if (panel == null)
        {
            Plugin.Log.LogWarning("CultTweaker: the mod panel is not loaded; nothing to open.");
            return;
        }

        if (MenuEditor.MainMenuEditor.IsOpen || SkinEditor.FollowerSkinEditor.IsOpen) return;

        if (!panel.IsOpen) panel.Open();
    }

    /// <summary>
    /// The pause menu wires its buttons up and down explicitly, so the clone is spliced into that
    /// chain rather than left to the automatic solver, which would otherwise route around it.
    /// </summary>
    private static void Renavigate(GameObject clone, Selectable above)
    {
        var self = clone.GetComponent<Selectable>();
        if (self == null || above == null) return;

        var theirs = above.navigation;
        var mine = self.navigation;

        if (theirs.mode != Navigation.Mode.Explicit)
        {
            mine.mode = Navigation.Mode.Automatic;
            self.navigation = mine;
            return;
        }

        var below = theirs.selectOnDown;

        mine.mode = Navigation.Mode.Explicit;
        mine.selectOnUp = above;
        mine.selectOnDown = below;
        mine.selectOnLeft = theirs.selectOnLeft;
        mine.selectOnRight = theirs.selectOnRight;
        self.navigation = mine;

        theirs.selectOnDown = self;
        above.navigation = theirs;

        if (below == null) return;

        var next = below.navigation;
        if (next.mode != Navigation.Mode.Explicit || next.selectOnUp != above) return;

        next.selectOnUp = self;
        below.navigation = next;
    }
}
