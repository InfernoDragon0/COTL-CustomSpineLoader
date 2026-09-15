using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Chrome;

/// <summary>
/// One action in the top bar's right-hand cluster, drawn as a square icon button like a dock tool.
/// <c>Label</c> is what the status line says on hover and what the button falls back to drawing when
/// there is no icon for it; a null label marks a divider between groups.
/// </summary>
public readonly struct EditorBarAction
{
    public readonly string Label;
    public readonly Sprite Icon;
    public readonly Action Do;
    public readonly string Hover;

    /// For a button that answers with a menu: it is handed its own rect so the list drops from it.
    public readonly Action<RectTransform> DoAt;

    public EditorBarAction(string label, Sprite icon, Action act, string hover = null)
    {
        Label = label;
        Icon = icon;
        Do = act;
        DoAt = null;
        Hover = hover;
    }

    public EditorBarAction(string label, Sprite icon, Action<RectTransform> at, string hover = null)
    {
        Label = label;
        Icon = icon;
        Do = null;
        DoAt = at;
        Hover = hover;
    }

    public static EditorBarAction Divider => new(null, null, (Action)null);

    public bool IsDivider => string.IsNullOrEmpty(Label);
}

/// <summary>
/// The strip across the top of an editor: what is being edited on the left, what can be done to the
/// file on the right. It carries the title label itself, so the inline rename prompt keeps writing
/// into the same text object it always has. The title is built once and never rebuilt - the heading
/// font arrives asynchronously and a rebuild would drop the pending swap - while the action cluster
/// is rebuilt whenever the context changes and a different set of buttons applies.
/// </summary>
public class EditorTopBar
{
    public const float Height = 52f;

    private const float ActionSize = 42f;

    private const float MaxTitleWidth = 620f;

    private readonly MapEditorUI _ui;
    private readonly RectTransform _bar;
    private readonly RectTransform _left;
    private readonly RectTransform _right;

    private readonly TMP_Text _title;
    private readonly LayoutElement _titleElement;
    private readonly GameObject _badge;
    private readonly TMP_Text _badgeText;
    private readonly LayoutElement _badgeElement;
    private readonly Image _badgePlate;
    private readonly Image _badgeOutline;
    private readonly GameObject _dot;
    private readonly TMP_Text _note;

    private static readonly Color BadgeQuiet = new(0.78f, 0.75f, 0.70f, 1f);

    private readonly List<GameObject> _actions = [];

    public TMP_Text TitleText => _title;
    public RectTransform Root => _bar;

    public EditorTopBar(MapEditorUI ui, Transform canvas, Action<RectTransform> registerBlocker)
    {
        _ui = ui;
        _bar = EditorChrome.CreateBar(canvas, "TopBar", top: true, Height, registerBlocker);

        _left = EditorChrome.CreateRegion(_bar, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f));
        _left.anchoredPosition = new Vector2(18f, 0f);
        Row(_left, spacing: 12f);

        _right = EditorChrome.CreateRegion(_bar, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(1f, 0.5f));
        _right.anchoredPosition = new Vector2(-16f, 0f);
        Row(_right, spacing: 10f);

        var titleGo = _ui.CreateHeadingLabel(_left, "", 26);
        _title = titleGo.GetComponent<TMP_Text>();
        _title.alignment = TextAlignmentOptions.Left;
        _title.enableWordWrapping = false;
        _title.overflowMode = TextOverflowModes.Overflow;
        _title.raycastTarget = false;
        _titleElement = titleGo.GetComponent<LayoutElement>();
        _titleElement.flexibleWidth = 0f;

        _badge = BuildBadge(_left, out _badgeText, out _badgeElement, out _badgePlate, out _badgeOutline);
        _badge.SetActive(false);

        _dot = BuildDot(_left);
        _dot.SetActive(false);

        var noteGo = _ui.CreateLabel(_left, "", 18);
        _note = noteGo.GetComponent<TMP_Text>();
        _note.color = MapEditorUI.MutedText;
        _note.enableWordWrapping = false;
        _note.overflowMode = TextOverflowModes.Overflow;
        _note.raycastTarget = false;
        noteGo.GetComponent<LayoutElement>().flexibleWidth = 0f;
        noteGo.SetActive(false);

        SetUnsaved(false);
    }

    private static void Row(RectTransform rect, float spacing)
    {
        var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    // ---- left side ---------------------------------------------------------------------------

    public void SetTitle(string text)
    {
        if (_title == null) return;

        _title.text = text ?? "";
        Remeasure();
    }

    /// <summary>
    /// Keeps the title and the badge as wide as their text. Two earlier attempts at this were wrong
    /// in the same way: the width was taken once, and the label kept changing under it - the heading
    /// font arrives asynchronously, and the game's text scaler resizes the label again after that.
    /// Worse, the title was set to ellipse, and an ellipsing label reports its *truncated* width as
    /// its preferred one, so once it had been cut short it could never ask for the room to grow
    /// back. It does not truncate at all now, and the width is re-read from the label every frame,
    /// which costs a cached property on two labels and is right whatever moves underneath.
    /// </summary>
    public void Tick()
    {
        Remeasure();
        MeasureBadge();
    }

    private void Remeasure()
    {
        if (_title == null || _titleElement == null) return;

        var wanted = _title.preferredWidth + 8f;
        if (wanted <= 8f) return;

        var capped = Mathf.Min(wanted, MaxTitleWidth);
        if (Mathf.Abs(_titleElement.preferredWidth - capped) < 0.5f) return;

        _titleElement.preferredWidth = capped;
        _titleElement.minWidth = capped;
    }

    public void SetBadge(string text)
    {
        var show = !string.IsNullOrEmpty(text);
        if (_badge.activeSelf != show) _badge.SetActive(show);
        if (!show) return;

        _badgeText.text = text.ToUpperInvariant();
        MeasureBadge();
    }

    private void MeasureBadge()
    {
        if (_badgeText == null || _badgeElement == null || !_badge.activeSelf) return;

        var wanted = _badgeText.preferredWidth + 22f;
        if (wanted <= 22f) return;

        if (Mathf.Abs(_badgeElement.preferredWidth - wanted) < 0.5f) return;

        _badgeElement.preferredWidth = wanted;
        _badgeElement.minWidth = wanted;
    }

    /// <summary>
    /// Amber is the editor's colour for "there is something here for you", so the room-type badge
    /// wears it only while the room holds work no file has. With everything saved it is a plain
    /// grey label that says where you are and asks for nothing.
    /// </summary>
    /// A line of standing detail after the badge, for a screen that has a summary to keep on show -
    /// the level layout's room count and its warnings. Empty text hides it.
    public void SetNote(string text, Color? colour = null)
    {
        if (_note == null) return;

        var show = !string.IsNullOrEmpty(text);
        if (_note.gameObject.activeSelf != show) _note.gameObject.SetActive(show);
        if (!show) return;

        _note.text = text;
        _note.color = colour ?? MapEditorUI.MutedText;
    }

    public void SetUnsaved(bool unsaved)
    {
        if (_dot != null && _dot.activeSelf != unsaved) _dot.SetActive(unsaved);

        var tint = unsaved ? MapEditorUI.AmberAccent : BadgeQuiet;

        if (_badgeText != null) _badgeText.color = tint;
        if (_badgePlate != null) _badgePlate.color = new Color(tint.r, tint.g, tint.b, unsaved ? 0.18f : 0.07f);
        if (_badgeOutline != null) _badgeOutline.color = new Color(tint.r, tint.g, tint.b, unsaved ? 0.5f : 0.28f);
    }

    private GameObject BuildBadge(Transform parent, out TMP_Text text, out LayoutElement element,
        out Image plateImage, out Image outlineImage)
    {
        var go = new GameObject("Badge");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(120f, 22f);

        element = go.AddComponent<LayoutElement>();
        element.preferredHeight = 22f;
        element.minHeight = 22f;
        element.flexibleWidth = 0f;

        var plate = go.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 3.2f;
        plate.raycastTarget = false;
        plateImage = plate;

        outlineImage = MapEditorUI.AddOutline(rt, BadgeQuiet);

        var label = _ui.CreateLabel(go.transform, "", 17, TextAlignmentOptions.Center);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(6f, 0f);
        labelRt.offsetMax = new Vector2(-6f, 0f);

        text = label.GetComponent<TMP_Text>();
        text.color = BadgeQuiet;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        return go;
    }

    private static GameObject BuildDot(Transform parent)
    {
        var go = new GameObject("Unsaved");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(10f, 10f);

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 10f;
        element.minWidth = 10f;
        element.preferredHeight = 10f;
        element.flexibleWidth = 0f;

        var image = go.AddComponent<Image>();
        image.sprite = MapEditorUI.RoundedPlate;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 8f;
        image.color = MapEditorUI.AmberAccent;
        image.raycastTarget = false;
        return go;
    }

    // ---- right side --------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the file actions and the editor keys. Context decides which of these exist - a base
    /// has no Load, a hub has no Level screen - so the cluster is thrown away and rebuilt rather
    /// than hidden piecemeal.
    /// </summary>
    public void RebuildActions(IEnumerable<EditorBarAction> actions,
        IEnumerable<(string Key, string Action, Action Do)> keys)
    {
        foreach (var go in _actions)
            if (go != null)
            {
                go.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(go);
            }

        _actions.Clear();

        if (actions != null)
            foreach (var action in actions)
                _actions.Add(action.IsDivider
                    ? EditorChrome.CreateSeparator(_right, ActionSize)
                    : BuildAction(action));

        if (keys != null)
            foreach (var key in keys)
                _actions.Add(_ui.CreateKeyChip(_right, key.Key, key.Action, key.Do));

        LayoutRebuilder.ForceRebuildLayoutImmediate(_right);
    }

    /// <summary>
    /// A square icon button, the same control the dock uses. These are the four or five things a
    /// mapper reaches for constantly, and their icons already exist and already say what they are;
    /// the words beside them only made the buttons wider and the icons smaller. The name still
    /// reaches the reader through the status line on hover.
    /// </summary>
    private GameObject BuildAction(EditorBarAction action)
    {
        var hover = string.IsNullOrEmpty(action.Hover) ? action.Label : action.Hover;

        GameObject go = null;
        var click = action.Do ?? (() => action.DoAt?.Invoke(go != null ? (RectTransform)go.transform : null));

        go = _ui.CreateIconButton(_right, action.Icon, action.Label, click, out _, ActionSize, hover);
        return go;
    }

    public void SetVisible(bool visible)
    {
        if (_bar != null && _bar.gameObject.activeSelf != visible) _bar.gameObject.SetActive(visible);
    }

    public void Destroy() => EditorChrome.Destroy(_bar);
}
