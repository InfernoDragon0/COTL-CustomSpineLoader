using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

public class MapEditorConfirm
{
    private readonly GameObject _root;
    private readonly TMP_Text _text;
    private readonly TMP_Text _confirmLabel;
    private readonly TMP_Text _altLabel;
    private readonly GameObject _altButton;
    private readonly RectTransform _confirmRect;
    private readonly RectTransform _cancelRect;

    private Action _onConfirm;
    private Action _onAlt;

    public bool Open => _root != null && _root.activeSelf;

    public MapEditorConfirm(MapEditorUI ui, Transform parent, float bottom)
    {
        _root = new GameObject("Confirm");
        _root.transform.SetParent(parent, false);

        var rect = _root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(560f, 96f);
        rect.anchoredPosition = new Vector2(0f, bottom);

        var border = _root.AddComponent<Image>();
        border.sprite = MapEditorUI.RoundedPlate;
        border.type = Image.Type.Sliced;
        border.pixelsPerUnitMultiplier = 1.6f;
        border.color = new Color(0f, 0f, 0f, 0.55f);

        var fillRt = MapEditorUI.NewChild(_root.transform, "Fill", stretch: true);
        fillRt.offsetMin = new Vector2(3f, 3f);
        fillRt.offsetMax = new Vector2(-3f, -3f);

        var plate = fillRt.gameObject.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.6f;
        plate.color = new Color(0f, 0f, 0f, 0.82f);
        plate.raycastTarget = false;

        var label = ui.CreateLabel(rect, "", 20, TextAlignmentOptions.Center);
        _text = label.GetComponent<TMP_Text>();
        _text.raycastTarget = false;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = new Vector2(-12f, -6f);

        var confirm = ui.CreateButton(rect, "Enter", () =>
        {
            var action = _onConfirm;
            Hide();
            action?.Invoke();
        }, 34f);
        _confirmRect = confirm.GetComponent<RectTransform>();
        _confirmRect.anchorMin = _confirmRect.anchorMax = new Vector2(0.5f, 0f);
        _confirmRect.pivot = new Vector2(1f, 0f);
        _confirmRect.sizeDelta = new Vector2(150f, 34f);
        _confirmLabel = confirm.GetComponentInChildren<TMP_Text>();

        _altButton = ui.CreateButton(rect, "Discard", () =>
        {
            var action = _onAlt;
            Hide();
            action?.Invoke();
        }, 34f, MapEditorEmphasis.Quiet);
        var altRect = _altButton.GetComponent<RectTransform>();
        altRect.anchorMin = altRect.anchorMax = new Vector2(0.5f, 0f);
        altRect.pivot = new Vector2(0.5f, 0f);
        altRect.sizeDelta = new Vector2(150f, 34f);
        altRect.anchoredPosition = new Vector2(0f, 10f);
        _altLabel = _altButton.GetComponentInChildren<TMP_Text>();

        var cancel = ui.CreateButton(rect, "Cancel", Hide, 34f, MapEditorEmphasis.Quiet);
        _cancelRect = cancel.GetComponent<RectTransform>();
        _cancelRect.anchorMin = _cancelRect.anchorMax = new Vector2(0.5f, 0f);
        _cancelRect.pivot = new Vector2(0f, 0f);
        _cancelRect.sizeDelta = new Vector2(150f, 34f);

        ui.Editor?.RegisterUiBlocker(rect);
        _root.SetActive(false);
    }

    public void Show(string text, Action onConfirm, string confirmLabel = "Enter",
        string altLabel = null, Action onAlt = null)
    {
        if (_root == null) return;

        _text.text = text;
        _onConfirm = onConfirm;
        _onAlt = onAlt;

        if (_confirmLabel != null) _confirmLabel.text = confirmLabel;

        var threeWay = onAlt != null;
        if (_altButton != null) _altButton.SetActive(threeWay);
        if (_altLabel != null && altLabel != null) _altLabel.text = altLabel;

        if (_confirmRect != null) _confirmRect.anchoredPosition = new Vector2(threeWay ? -114f : -8f, 10f);
        if (_cancelRect != null) _cancelRect.anchoredPosition = new Vector2(threeWay ? 114f : 8f, 10f);

        _root.SetActive(true);
    }

    public void Hide()
    {
        _onConfirm = null;
        _onAlt = null;
        if (_root != null) _root.SetActive(false);
    }
}
