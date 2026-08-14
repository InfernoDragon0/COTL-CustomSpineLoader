using System;
using System.Collections;
using Lamb.UI;
using src.Extensions;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Asks for a map name using the game's own naming modal.
//
// This is the same dialog the cult is named through (UICultNameMenuController): a real text
// field with on-screen keyboard and controller support, a confirm button, and a disclaimer line
// under the field. The editor's own rename flow reads Input.inputString directly because a
// TMP_InputField needs the EventSystem this game hands to Rewired - but that only ever worked
// because it was the editor's own chrome. For saving, borrowing the vanilla dialog gets a real
// input field, the game's look, and somewhere to put the overwrite warning.
//
// The disclaimer is repurposed: instead of the cult-rename notice it carries a live warning that
// appears only while the typed name matches a map already on disk.
public static class MapNamePrompt
{
    // Map names are paths, not cult names - the vanilla 16-character limit is too tight.
    private const int NameLimit = 40;

    public static void Show(RuntimeMapEditor editor, string prefill, string title, Action<string> onConfirmed)
    {
        if (editor == null || onConfirmed == null) return;

        var uiManager = MonoSingleton<UIManager>.Instance;
        if (uiManager == null)
        {
            editor.SetStatus("UIManager unavailable; cannot open the name dialog.", StatusSeverity.Error);
            return;
        }

        editor.ModalOpen = true;

        try
        {
            var task = uiManager.LoadCultNameAssets();
            editor.StartCoroutine(UIManager.LoadAssets(task,
                () => Build(editor, uiManager, prefill, title, onConfirmed)));
        }
        catch (Exception e)
        {
            editor.ModalOpen = false;
            Plugin.Log.LogWarning("MapEditor: name dialog failed to load: " + e.Message);
            editor.SetStatus("Name dialog unavailable, see log.", StatusSeverity.Error);
        }
    }

    private static void Build(RuntimeMapEditor editor, UIManager uiManager, string prefill, string title,
        Action<string> onConfirmed)
    {
        UICultNameMenuController menu;
        try
        {
            menu = uiManager.CultNameMenuTemplate.Instantiate();
        }
        catch (Exception e)
        {
            editor.ModalOpen = false;
            Plugin.Log.LogWarning("MapEditor: name dialog failed to open: " + e.Message);
            editor.SetStatus("Name dialog unavailable, see log.", StatusSeverity.Error);
            return;
        }

        if (menu == null)
        {
            editor.ModalOpen = false;
            editor.SetStatus("Name dialog unavailable.", StatusSeverity.Error);
            return;
        }

        // showDisclaimer keeps the disclaimer object alive so its text can be swapped for the
        // overwrite warning; it is hidden again immediately unless the prefill already collides.
        menu.Show(prefill ?? "", cancellable: true, showDisclaimer: true);
        menu.SetTitle(title);
        menu.RequiresName = true;

        try
        {
            menu._nameInputField.characterLimit = NameLimit;
        }
        catch (Exception)
        {
            // Field layout differs in some build; the vanilla limit still produces a usable name.
        }

        SetUpWarning(menu);
        menu.OnNameConfirmed += result =>
        {
            editor.ModalOpen = false;
            onConfirmed(result);
        };

        editor.StartCoroutine(TrackLifetime(editor, menu));
    }

    // The disclaimer line under the field, rewired to say whether this name is about to replace
    // something. Driven by the field's own onValueChanged - MMInputField is a TMP_InputField, so
    // every keystroke (and every programmatic set, including the on-screen keyboard) raises it.
    private static void SetUpWarning(UICultNameMenuController menu)
    {
        GameObject holder;
        TMP_Text label;
        TMPro.TMP_InputField field;
        try
        {
            holder = menu._renameDisclaimer;
            label = holder != null ? holder.GetComponentInChildren<TMP_Text>(true) : null;
            field = menu._nameInputField;
        }
        catch (Exception)
        {
            return;
        }

        if (holder == null) return;

        void Refresh(string text)
        {
            var exists = !string.IsNullOrWhiteSpace(text) && MapEditorSerialization.Exists(text);
            holder.SetActive(exists);
            if (exists && label != null)
                label.text = $"A map named '{text.Trim()}' already exists and will be overwritten.";
        }

        if (field != null) field.onValueChanged.AddListener(Refresh);
        Refresh(field != null ? field.text : "");
    }

    // The modal destroys itself on hide, and a cancel reports nothing - so the editor's modal
    // flag is released by watching for the object to go rather than by a callback.
    private static IEnumerator TrackLifetime(RuntimeMapEditor editor, UICultNameMenuController menu)
    {
        while (menu != null) yield return null;

        editor.ModalOpen = false;
        // The modal restores timeScale to 1 as it hides; the editor is still open behind it.
        editor.ReassertPause();
    }
}
