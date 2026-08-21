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

    // How many of these are open at once. It is only ever 0 or 1 for most of the editor, but the
    // trigger tool's screen text asks two questions in a row and opens the second from the
    // first's confirm callback - so the second is opened while the first is still alive, and the
    // first's own close then cleared the editor's modal flag out from under it a moment later.
    // The dialog stayed on screen with the editor reading WASD and tool shortcuts underneath,
    // which is what "controls go through while typing" was.
    private static int _openCount;

    private static void OpenModal(RuntimeMapEditor editor)
    {
        _openCount++;
        editor.ModalOpen = true;
    }

    private static void CloseModal(RuntimeMapEditor editor)
    {
        if (_openCount > 0) _openCount--;
        if (_openCount == 0) editor.ModalOpen = false;
    }

    // A scene change kills TrackLifetime before it can close its dialog, and the count is
    // static - the next scene's editor would start with ModalOpen latched and every input
    // ignored. The editor host calls this from OnDestroy.
    public static void ResetModalState() => _openCount = 0;

    // existsCheck/existsNoun drive the overwrite warning; the default is the map store, because
    // that is what the prompt was built for. The lighting tool passes its own profile store.
    public static void Show(RuntimeMapEditor editor, string prefill, string title,
        Action<string> onConfirmed, Action onClosed = null,
        Func<string, bool> existsCheck = null, string existsNoun = "map", int characterLimit = NameLimit)
    {
        if (editor == null || onConfirmed == null) return;

        var uiManager = MonoSingleton<UIManager>.Instance;
        if (uiManager == null)
        {
            editor.SetStatus("UIManager unavailable; cannot open the name dialog.", StatusSeverity.Error);
            onClosed?.Invoke();
            return;
        }

        OpenModal(editor);

        Time.timeScale = 1f;

        try
        {
            var task = uiManager.LoadCultNameAssets();
            editor.StartCoroutine(UIManager.LoadAssets(task,
                () => Build(editor, uiManager, prefill, title, onConfirmed, onClosed,
                    existsCheck ?? MapEditorSerialization.Exists, existsNoun, characterLimit)));
        }
        catch (Exception e)
        {
            CloseModal(editor);
            Plugin.Log.LogWarning("MapEditor: name dialog failed to load: " + e.Message);
            editor.SetStatus("Name dialog unavailable, see log.", StatusSeverity.Error);
            onClosed?.Invoke();
        }
    }

    private static void Build(RuntimeMapEditor editor, UIManager uiManager, string prefill, string title,
        Action<string> onConfirmed, Action onClosed, Func<string, bool> existsCheck, string existsNoun, int characterLimit)
    {
        UICultNameMenuController menu;
        try
        {
            menu = uiManager.CultNameMenuTemplate.Instantiate();
        }
        catch (Exception e)
        {
            CloseModal(editor);
            Plugin.Log.LogWarning("MapEditor: name dialog failed to open: " + e.Message);
            editor.SetStatus("Name dialog unavailable, see log.", StatusSeverity.Error);
            onClosed?.Invoke();
            return;
        }

        if (menu == null)
        {
            CloseModal(editor);
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
            menu._nameInputField.characterLimit = characterLimit > 0 ? characterLimit : NameLimit;
        }
        catch (Exception)
        {
            // Field layout differs in some build; the vanilla limit still produces a usable name.
        }

        SetUpWarning(menu, existsCheck, existsNoun);

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

    private static void SetUpWarning(UICultNameMenuController menu, Func<string, bool> existsCheck,
        string existsNoun)
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
            var exists = !string.IsNullOrWhiteSpace(text) && existsCheck(text);
            holder.SetActive(exists);
            if (exists && label != null)
                label.text = $"A {existsNoun} named '{text.Trim()}' already exists and will be overwritten.";
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

        CloseModal(editor);
        onClosed?.Invoke();
    }
}
