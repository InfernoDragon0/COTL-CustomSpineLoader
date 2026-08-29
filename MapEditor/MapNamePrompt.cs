using System;
using System.Collections;
using Lamb.UI;
using src.Extensions;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class MapNamePrompt
{
    private const int NameLimit = 40;

    private static int _openCount;

    private static void OpenModal(IMapEditorHost editor)
    {
        _openCount++;
        editor.ModalOpen = true;
    }

    private static void CloseModal(IMapEditorHost editor)
    {
        if (_openCount > 0) _openCount--;
        if (_openCount == 0) editor.ModalOpen = false;
    }

    public static void ResetModalState() => _openCount = 0;

    public static void Show(IMapEditorHost editor, string prefill, string title,
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

    private static void Build(IMapEditorHost editor, UIManager uiManager, string prefill, string title,
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

        menu.Show(prefill ?? "", cancellable: true, showDisclaimer: true);
        menu.SetTitle(title);
        menu.RequiresName = true;

        editor.StartCoroutine(MuteBackdrop(menu));

        try
        {
            menu._nameInputField.characterLimit = characterLimit > 0 ? characterLimit : NameLimit;
        }
        catch (Exception)
        {
        }

        SetUpWarning(menu, existsCheck, existsNoun);

        menu.OnNameConfirmed += result => onConfirmed(result);

        editor.StartCoroutine(FocusWhenShown(menu));
        editor.StartCoroutine(TrackLifetime(editor, menu, onClosed));
    }

    private static IEnumerator MuteBackdrop(UICultNameMenuController menu)
    {
        yield return null;
        if (menu == null) yield break;

        try
        {
            menu._buttonHighlight.SetAsBlack();
            var highlight = menu._buttonHighlight.Image;
            if (highlight != null) highlight.color = new Color(1f, 1f, 1f, 0.75f);
        }
        catch (Exception)
        {
        }

        try
        {
            foreach (var image in menu.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image == null) continue;
                var rect = image.rectTransform.rect;
                if (rect.width < 1000f || rect.height < 700f) continue;

                image.color = new Color(0f, 0f, 0f, Mathf.Clamp01(image.color.a) * 0.85f);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: name dialog restyle skipped: " + e.Message);
        }
    }

    private static IEnumerator FocusWhenShown(UICultNameMenuController menu)
    {
        var deadline = Time.unscaledTime + 5f;
        while (menu != null && menu.IsShowing && Time.unscaledTime < deadline) yield return null;

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

    private static IEnumerator TrackLifetime(IMapEditorHost editor, UICultNameMenuController menu,
        Action onClosed)
    {
        while (menu != null) yield return null;

        CloseModal(editor);
        onClosed?.Invoke();
    }
}
