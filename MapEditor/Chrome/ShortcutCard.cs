using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Chrome;

/// <summary>
/// The full shortcut list, over the room, on demand. The bottom bar shows only what the active tool
/// does; everything that is true under every tool - pan, zoom, undo, quicksave, the function keys -
/// moved here, which is what let the tool hints shrink to a single row of chips.
///
/// Built once and left inactive rather than created on first use: the full-screen editors hide the
/// editor chrome by walking the canvas children captured at startup, and anything made later would
/// still be drawn on top of them.
/// </summary>
public class ShortcutCard
{
    private const float CardWidth = 640f;
    private const float MaxCardHeight = 760f;

    private readonly MapEditorUI _ui;
    private readonly RectTransform _overlay;
    private readonly RectTransform _card;
    private readonly RectTransform _body;
    private readonly RectTransform _rows;
    private readonly RectTransform _extras;

    private readonly List<(string Key, string Action)> _globals = [];
    private List<(string Key, string Action)> _tool = [];
    private List<(string Label, Action Do)> _extraButtons = [];

    private string _toolName = "";
    private string _note;
    private bool _dirty = true;

    public bool Open => _overlay != null && _overlay.gameObject.activeSelf;

    public ShortcutCard(MapEditorUI ui, Transform canvas, Action<RectTransform> registerBlocker,
        IEnumerable<(string Key, string Action)> globals)
    {
        _ui = ui;

        if (globals != null) _globals.AddRange(globals);

        _overlay = EditorChrome.CreateRegion(canvas, "ShortcutCard", Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f));

        var catcher = _overlay.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0.45f);

        var button = _overlay.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Hide);

        registerBlocker?.Invoke(_overlay);

        _card = EditorChrome.CreateRegion(_overlay, "Card", new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        _card.sizeDelta = new Vector2(CardWidth, 400f);

        VanillaChrome.Dress(_card.gameObject.AddComponent<Image>());

        var heading = _ui.CreateHeadingLabel(_card, "Shortcuts", 26);
        var headingRt = heading.GetComponent<RectTransform>();
        headingRt.anchorMin = new Vector2(0f, 1f);
        headingRt.anchorMax = new Vector2(1f, 1f);
        headingRt.pivot = new Vector2(0.5f, 1f);
        headingRt.offsetMin = new Vector2(16f, 0f);
        headingRt.offsetMax = new Vector2(-16f, 0f);
        headingRt.sizeDelta = new Vector2(headingRt.sizeDelta.x, 44f);
        headingRt.anchoredPosition = new Vector2(0f, -8f);
        heading.GetComponent<TMP_Text>().raycastTarget = false;

        _body = EditorChrome.CreateRegion(_card, "Body", Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f));
        _body.offsetMin = new Vector2(14f, 52f);
        _body.offsetMax = new Vector2(-14f, -56f);

        _rows = _ui.CreateScrollColumn(_body, "ShortcutRows", out _);

        _extras = EditorChrome.CreateRegion(_card, "Extras", new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0.5f, 0f));
        _extras.offsetMin = new Vector2(14f, 0f);
        _extras.offsetMax = new Vector2(-14f, 0f);
        _extras.sizeDelta = new Vector2(_extras.sizeDelta.x, 44f);
        _extras.anchoredPosition = new Vector2(0f, 8f);

        var layout = _extras.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        _overlay.gameObject.SetActive(false);
    }

    /// The active tool's hints, plus an optional line of prose for anything a key badge cannot say.
    public void SetTool(string toolName, IEnumerable<(string Key, string Action)> hints, string note = null)
    {
        _toolName = toolName ?? "";
        _tool = hints != null ? new List<(string, string)>(hints) : [];
        _note = note;
        _dirty = true;

        if (Open) Rebuild();
    }

    /// Buttons that belong with the shortcut list rather than on screen: the multiplayer resync and
    /// the sync self-test, which were the last two things living in the old shortcut column.
    public void SetExtras(IEnumerable<(string Label, Action Do)> extras)
    {
        _extraButtons = extras != null ? new List<(string, Action)>(extras) : [];
        _dirty = true;

        if (Open) Rebuild();
    }

    public void Toggle()
    {
        if (Open) Hide();
        else Show();
    }

    public void Show()
    {
        if (_overlay == null) return;

        if (_dirty) Rebuild();
        _overlay.gameObject.SetActive(true);
        _overlay.SetAsLastSibling();
    }

    public void Hide()
    {
        if (_overlay != null && _overlay.gameObject.activeSelf) _overlay.gameObject.SetActive(false);
    }

    private void Rebuild()
    {
        _dirty = false;

        Clear(_rows);
        Clear(_extras);

        var lines = 0;

        if (_tool.Count > 0)
        {
            Heading(string.IsNullOrEmpty(_toolName) ? "Tool" : _toolName);
            lines++;

            foreach (var hint in _tool)
            {
                _ui.CreateKeyHint(_rows, hint.Key, hint.Action);
                lines++;
            }
        }

        if (!string.IsNullOrEmpty(_note))
        {
            var note = _ui.CreateLabel(_rows, _note, 17);
            var text = note.GetComponent<TMP_Text>();
            text.color = MapEditorUI.MutedText;
            text.raycastTarget = false;
            MapEditorUI.FitLabelHeight(note, CardWidth - 60f);
            lines++;
        }

        Heading("Editor");
        lines++;

        foreach (var hint in _globals)
        {
            _ui.CreateKeyHint(_rows, hint.Key, hint.Action);
            lines++;
        }

        foreach (var extra in _extraButtons)
            _ui.CreateButton(_extras, extra.Label, extra.Do, 32f, MapEditorEmphasis.Quiet);

        var extrasHeight = _extraButtons.Count > 0 ? 52f : 12f;
        _extras.gameObject.SetActive(_extraButtons.Count > 0);

        _body.offsetMin = new Vector2(14f, extrasHeight);

        var wanted = 56f + extrasHeight + lines * 39f + 16f;
        _card.sizeDelta = new Vector2(CardWidth, Mathf.Min(wanted, MaxCardHeight));
    }

    private void Heading(string text)
    {
        var go = _ui.CreateLabel(_rows, text.ToUpperInvariant(), 17);
        var label = go.GetComponent<TMP_Text>();
        label.color = MapEditorUI.MutedText;
        label.raycastTarget = false;
    }

    private static void Clear(RectTransform parent)
    {
        var stale = new List<GameObject>();
        foreach (Transform child in parent) stale.Add(child.gameObject);

        foreach (var child in stale)
        {
            child.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(child);
        }
    }

    public void Destroy() => EditorChrome.Destroy(_overlay);
}
