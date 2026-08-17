using System;
using System.Collections;
using Lamb.UI;
using src.Extensions;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class MapNamePrompt
{
    // Map names are paths, not cult names - the vanilla 16-character limit is too tight.
    private const int NameLimit = 40;

    public static void Show(RuntimeMapEditor editor, string prefill, string title,
        Action<string> onConfirmed, Action onClosed = null)
    {
        if (editor == null || onConfirmed == null) return;

        var uiManager = MonoSingleton<UIManager>.Instance;
        if (uiManager == null)
        {
            editor.SetStatus("UIManager unavailable; cannot open the name dialog.", StatusSeverity.Error);
            onClosed?.Invoke();
            return;
        }

        editor.ModalOpen = true;

        Time.timeScale = 1f;

        try
        {
            var task = uiManager.LoadCultNameAssets();
            editor.StartCoroutine(UIManager.LoadAssets(task,
                () => Build(editor, uiManager, prefill, title, onConfirmed, onClosed)));
        }
        catch (Exception e)
        {
            editor.ModalOpen = false;
            Plugin.Log.LogWarning("MapEditor: name dialog failed to load: " + e.Message);
            editor.SetStatus("Name dialog unavailable, see log.", StatusSeverity.Error);
            onClosed?.Invoke();
        }
    }

    private static void Build(RuntimeMapEditor editor, UIManager uiManager, string prefill, string title,
        Action<string> onConfirmed, Action onClosed)
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
            onClosed?.Invoke();
            return;
        }

        if (menu == null)
        {
            editor.ModalOpen = false;
            editor.SetStatus("Name dialog unavailable.", StatusSeverity.Error);
            onClosed?.Invoke();
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

        menu.OnNameConfirmed += result => onConfirmed(result);

        editor.StartCoroutine(FocusWhenShown(menu));
        editor.StartCoroutine(TrackLifetime(editor, menu, onClosed));
    }

    private static IEnumerator FocusWhenShown(UICultNameMenuController menu)
    {
        var deadline = Time.unscaledTime + 5f;
        while (menu != null && menu.IsShowing && Time.unscaledTime < deadline) yield return null;

        // One more frame so the navigator's own selection lands first and ours replaces it.
        yield return null;
        if (menu != null) FocusField(menu);
    }

    private static void FocusField(UICultNameMenuController menu)
    {
        try
        {
            var field = menu._nameInputField;
            if (field == null) return;

            var navigator = MonoSingleton<src.UINavigator.UINavigatorNew>.Instance;
            if (navigator != null) navigator.NavigateToNew(field);

            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events != null) events.SetSelectedGameObject(field.gameObject);

            if (!field.isFocused) field.TryPerformConfirmAction();
            field.ActivateInputField();

            field.caretPosition = field.text?.Length ?? 0;
            field.selectionAnchorPosition = field.caretPosition;
            field.selectionFocusPosition = field.caretPosition;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not focus the name field: " + e.Message);
        }
    }

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

    // The modal destroys itself on hide, and a cancel reports nothing - so the close is detected
    // by watching for the object to go rather than by a callback.
    private static IEnumerator TrackLifetime(RuntimeMapEditor editor, UICultNameMenuController menu,
        Action onClosed)
    {
        while (menu != null) yield return null;

        editor.ModalOpen = false;
        onClosed?.Invoke();
    }
}
