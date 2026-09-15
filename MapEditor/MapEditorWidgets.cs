using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

public class MapEditorDropdown
{
    private readonly MapEditorUI _ui;
    private readonly RectTransform _row;
    private readonly TMP_Text _caption;
    private readonly string _captionBase;
    private readonly Action<int, string> _onSelected;

    private List<string> _options = [];
    private GameObject _floating;

    public GameObject Root { get; }
    public int SelectedIndex { get; private set; } = -1;

    /// The rect the floating option list measures and lines itself up against. It is the field
    /// itself by default; a labelled dropdown points it at the whole row, so sharing the row with a
    /// label does not squeeze the list down to half width and clip the option text.
    internal RectTransform ListFrom;

    /// A menu built for one click owns nothing else; it takes its holder with it when it closes.
    internal bool DestroyRootOnClose;

    private RectTransform Anchor => ListFrom != null ? ListFrom : _row;

    private const float OptionHeight = 32f;
    private const float OptionSpacing = 2f;
    private const float MaxListHeight = 460f;

    private static readonly Color OptionIdle = new(1f, 1f, 1f, 0.04f);
    private static readonly Color OptionHover = new(1f, 1f, 1f, 0.12f);
    private static readonly Color OptionCurrent = new(0.83f, 0.24f, 0.20f, 0.22f);
    private static readonly Color OptionText = new(0.9f, 0.87f, 0.8f);

    internal MapEditorDropdown(MapEditorUI ui, GameObject root, RectTransform row, TMP_Text caption,
        string captionBase, Action<int, string> onSelected)
    {
        _ui = ui;
        Root = root;
        _row = row;
        _caption = caption;
        _captionBase = captionBase;
        _onSelected = onSelected;
    }

    public void SetOptions(IList<string> options)
    {
        _options = options != null ? new List<string>(options) : [];
        SelectedIndex = -1;
        UpdateCaption();
        Close();
    }

    public void SetSelected(int index)
    {
        SelectedIndex = index >= 0 && index < _options.Count ? index : -1;
        UpdateCaption();
    }

    private void UpdateCaption()
    {
        if (_caption == null) return;
        _caption.text = SelectedIndex >= 0 ? _options[SelectedIndex] : _captionBase;
    }

    public void Toggle()
    {
        if (_floating != null) Close();
        else Open();
    }

    public void Open()
    {
        Close();

        var canvas = _ui.CanvasRoot;
        if (canvas == null || _options.Count == 0)
        {
            _ui.Editor?.SetStatus("Nothing to choose from here.");
            return;
        }

        _floating = new GameObject("DropdownOverlay");
        _floating.transform.SetParent(canvas, false);
        var floatRt = _floating.AddComponent<RectTransform>();
        floatRt.anchorMin = Vector2.zero;
        floatRt.anchorMax = Vector2.one;
        floatRt.offsetMin = Vector2.zero;
        floatRt.offsetMax = Vector2.zero;

        var catcher = _floating.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0.25f);
        _ui.Editor?.RegisterUiBlocker(floatRt);

        var catcherButton = _floating.AddComponent<Button>();
        catcherButton.transition = Selectable.Transition.None;
        catcherButton.onClick.AddListener(Close);

        BuildList(canvas);
        _ui.NotifyDropdownOpened(this);
    }

    private void BuildList(RectTransform canvas)
    {
        var height = Mathf.Min(_options.Count * (OptionHeight + OptionSpacing) + 16f, MaxListHeight);
        var width = Mathf.Max(Anchor.rect.width, 220f);

        var panel = new GameObject("Options");
        panel.transform.SetParent(_floating.transform, false);
        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);

        var img = panel.AddComponent<Image>();
        VanillaChrome.Dress(img);
        MapEditorUI.AddOutline(rt, new Color(1f, 1f, 1f, 0.12f));

        PositionList(canvas, rt, height);

        var content = _ui.CreateScrollColumn(panel.transform, "OptionList", out var root, spacing: OptionSpacing);
        for (var i = 0; i < _options.Count; i++)
            BuildOption(content, i);

        // A long list opens on the current value rather than at the top.
        if (SelectedIndex > 0 && _options.Count > 1 && root != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var scroll = root.GetComponent<ScrollRect>();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 1f - SelectedIndex / (float)(_options.Count - 1);
        }
    }

    /// One flat row: a tint that lights on hover, a left bar and white text on the current value.
    private void BuildOption(RectTransform content, int index)
    {
        var option = _options[index];
        var current = index == SelectedIndex;

        var row = new GameObject("Option_" + option);
        row.transform.SetParent(content, false);
        var rt = row.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200f, OptionHeight);

        var element = row.AddComponent<LayoutElement>();
        element.minHeight = OptionHeight;
        element.preferredHeight = OptionHeight;
        element.flexibleWidth = 1f;

        var plate = row.AddComponent<Image>();
        var idle = current ? OptionCurrent : OptionIdle;
        plate.color = idle;

        if (current)
        {
            var bar = new GameObject("Current");
            bar.transform.SetParent(row.transform, false);
            var barRt = bar.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0f);
            barRt.anchorMax = new Vector2(0f, 1f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.sizeDelta = new Vector2(3f, 0f);
            barRt.anchoredPosition = Vector2.zero;
            var barImage = bar.AddComponent<Image>();
            barImage.color = MapEditorUI.Accent;
            barImage.raycastTarget = false;
        }

        var label = _ui.CreateLabel(row.transform, option, 17);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(14f, 0f);
        labelRt.offsetMax = new Vector2(-8f, 0f);
        var text = label.GetComponent<TMP_Text>();
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.color = current ? Color.white : OptionText;
        text.raycastTarget = false;

        MapEditorUI.AttachButton(row, plate, () => Choose(index));
        MapEditorUI.AddHover(row, plate, idle, current ? OptionCurrent : OptionHover, null);
    }

    private void PositionList(RectTransform canvas, RectTransform panel, float height)
    {
        var corners = new Vector3[4];
        Anchor.GetWorldCorners(corners);

        var below = RectTransformUtility.WorldToScreenPoint(null, corners[0]); // bottom-left
        var above = RectTransformUtility.WorldToScreenPoint(null, corners[1]); // top-left

        var fitsBelow = below.y - height * canvas.lossyScale.y > 0f;
        var anchor = fitsBelow ? below : above;
        panel.pivot = fitsBelow ? new Vector2(0f, 1f) : new Vector2(0f, 0f);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, anchor, null, out var local))
            panel.anchoredPosition = local;
    }

    private void Choose(int index)
    {
        SelectedIndex = index;
        var value = _options[index];
        UpdateCaption();
        Close();

        try
        {
            _onSelected?.Invoke(index, value);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: dropdown selection handler failed: " + e);
        }
    }

    public void Close()
    {
        if (_floating != null)
        {
            UnityEngine.Object.Destroy(_floating);
            _floating = null;
        }

        _ui.NotifyDropdownClosed(this);

        if (!DestroyRootOnClose || Root == null) return;

        DestroyRootOnClose = false;
        UnityEngine.Object.Destroy(Root);
    }
}

/// <summary>
/// A foldable group built by <see cref="MapEditorUI.CreateSection"/>. Owns the header; owns the
/// content column too when built with one, otherwise the caller listens to <see cref="OnToggled"/>
/// and shows or hides its own body.
/// </summary>
public class MapEditorSection
{
    private readonly MapEditorUI _ui;
    private readonly TMP_Text _title;
    private readonly TMP_Text _summary;
    private readonly Image _chevronImage;
    private readonly TMP_Text _chevronText;

    public RectTransform Header { get; }
    public RectTransform Content { get; }
    public bool Open { get; private set; }

    public Action<bool> OnToggled;

    internal MapEditorSection(MapEditorUI ui, RectTransform header, RectTransform content, TMP_Text title,
        TMP_Text summary, Image chevronImage, TMP_Text chevronText, bool open)
    {
        _ui = ui;
        Header = header;
        Content = content;
        _title = title;
        _summary = summary;
        _chevronImage = chevronImage;
        _chevronText = chevronText;
        Open = !open;
        SetOpen(open, notify: false);
    }

    public void Toggle() => SetOpen(!Open);

    public void SetOpen(bool open, bool notify = true)
    {
        if (Open == open) return;
        Open = open;

        if (Content != null) Content.gameObject.SetActive(open);

        if (_chevronImage != null)
            _chevronImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, open ? 0f : 90f);
        if (_chevronText != null) _chevronText.text = open ? "-" : "+";

        if (Content != null) _ui.Editor?.RequestOptionsResize();
        if (notify) OnToggled?.Invoke(open);
    }

    public void SetTitle(string text)
    {
        if (_title != null) _title.text = text ?? "";
    }

    public void SetSummary(string text)
    {
        if (_summary == null) return;
        var show = !string.IsNullOrEmpty(text);
        _summary.text = show ? text : "";
        if (_summary.gameObject.activeSelf != show) _summary.gameObject.SetActive(show);
    }

    public void SetVisible(bool visible)
    {
        if (Header != null && Header.gameObject.activeSelf != visible) Header.gameObject.SetActive(visible);
        if (Content != null) Content.gameObject.SetActive(visible && Open);
    }
}

public class MapEditorScrollBox
{
    private readonly MapEditorUI _ui;
    private readonly LayoutElement _box;
    private readonly RectTransform _boxRect;
    private readonly ScrollRect _scroll;
    private readonly float _max;
    private readonly float _rowHeight;
    private readonly float _spacing;

    private float _applied = -1f;

    private const float Padding = 16f;

    private const float EmptyHeight = 44f;

    public RectTransform Content { get; }

    internal MapEditorScrollBox(MapEditorUI ui, RectTransform content, GameObject root, float maxHeight,
        float rowHeight, float spacing)
    {
        _ui = ui;
        Content = content;
        _max = maxHeight;
        _rowHeight = rowHeight;
        _spacing = spacing;

        _box = root.AddComponent<LayoutElement>();
        _box.flexibleWidth = 1f;
        _boxRect = root.transform as RectTransform;
        _scroll = root.GetComponent<ScrollRect>();

        SetRows(0);
    }

    public void SetRows(int rows)
    {
        var needed = rows <= 0
            ? EmptyHeight
            : rows * _rowHeight + (rows - 1) * _spacing + Padding;

        var height = Mathf.Min(needed, _max);

        if (!Mathf.Approximately(height, _applied))
        {
            _applied = height;
            _box.minHeight = height;
            _box.preferredHeight = height;

            if (_boxRect != null)
            {
                _boxRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                if (_boxRect.parent is RectTransform parent) LayoutRebuilder.MarkLayoutForRebuild(parent);
            }
        }

        if (_scroll != null && _scroll.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);

        _ui.Editor?.RequestOptionsResize();
    }

    public void ScrollToTop()
    {
        if (_scroll == null) return;

        _scroll.StopMovement();
        if (_scroll.content != null) _scroll.content.anchoredPosition = Vector2.zero;
        _scroll.verticalNormalizedPosition = 1f;
    }
}

public class MapEditorGrid
{
    private class Cell
    {
        public GameObject Root;
        public Image Ring;
        public Image Icon;
        public GameObject Letter;
        public GameObject Star;
        public string Display;
    }

    private readonly MapEditorUI _ui;
    private readonly Transform _cells;
    private readonly TMP_Text _caption;
    private readonly Dictionary<string, Cell> _byId = [];

    private string _selectedId;
    private MonoBehaviour _host;
    private Coroutine _fill;

    public GameObject Root { get; }

    internal MapEditorGrid(MapEditorUI ui, GameObject root, Transform cells, TMP_Text caption)
    {
        _ui = ui;
        Root = root;
        _cells = cells;
        _caption = caption;
    }

    public class Entry
    {
        public string Id;
        public string Display;
        public Action OnClick;
    }

    // ---- scroll box ---------------------------------------------------------------------------

    private LayoutElement _box;
    private RectTransform _boxRect;
    private ScrollRect _boxScroll;
    private float _boxMax;
    private int _boxColumns = 4;
    private float _boxCell = 88f;
    private float _boxSpacing = 6f;
    private float _boxApplied = -1f;
    private Coroutine _boxSettle;

    internal void UseScrollBox(LayoutElement element, float maxHeight, int columns, float cellSize,
        float spacing)
    {
        _box = element;
        _boxRect = element != null ? element.transform as RectTransform : null;
        _boxScroll = element != null ? element.GetComponent<ScrollRect>() : null;
        _boxMax = maxHeight;
        _boxColumns = Mathf.Max(1, columns);
        _boxCell = cellSize;
        _boxSpacing = spacing;
        UpdateBoxHeight();
    }

    private void UpdateBoxHeight()
    {
        if (_box == null) return;

        var rows = Mathf.CeilToInt(_byId.Count / (float)_boxColumns);
        var needed = rows <= 0
            ? EmptyBoxHeight
            : rows * _boxCell + (rows - 1) * _boxSpacing + BoxPadding;

        var height = Mathf.Min(needed, _boxMax);
        if (Mathf.Abs(height - _boxApplied) < 0.5f) return;
        _boxApplied = height;

        _box.minHeight = height;
        _box.preferredHeight = height;

        if (_boxRect != null)
        {
            _boxRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            var parent = _boxRect.parent as RectTransform;
            if (parent != null) LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        SettleBox();
    }

    private void SettleBox()
    {
        if (_host == null) { ForceBox(); return; }

        if (_boxSettle != null) _host.StopCoroutine(_boxSettle);
        _boxSettle = _host.StartCoroutine(SettleRoutine());
    }

    private IEnumerator SettleRoutine()
    {
        yield return null;
        yield return null;

        _boxSettle = null;
        ForceBox();
    }

    private void ForceBox()
    {
        if (_boxScroll != null && _boxScroll.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_boxScroll.content);

        if (Root != null && Root.transform is RectTransform root)
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);

        ScrollBoxToTop();

        _ui.Editor?.RequestOptionsResize();
    }

    private void ScrollBoxToTop()
    {
        if (_boxScroll == null) return;

        _boxScroll.StopMovement();
        if (_boxScroll.content != null) _boxScroll.content.anchoredPosition = Vector2.zero;
        _boxScroll.verticalNormalizedPosition = 1f;
    }

    private const float BoxPadding = 16f;

    private const float EmptyBoxHeight = 48f;

    private Sprite IconOf(string id) =>
        _byId.TryGetValue(id, out var cell) && cell.Icon != null && cell.Icon.enabled
            ? cell.Icon.sprite
            : null;

    public void Clear()
    {
        StopFill();

        _ui.HideIconPreview();

        foreach (var cell in _byId.Values)
            if (cell.Root != null) UnityEngine.Object.Destroy(cell.Root);

        _byId.Clear();
        _selectedId = null;
        if (_caption != null) _caption.text = "";

        ScrollBoxToTop();
        UpdateBoxHeight();

        _ui.Editor?.RequestOptionsResize();
    }

    private void StopFill()
    {
        if (_fill != null && _host != null) _host.StopCoroutine(_fill);
        _fill = null;
    }

    public void Populate(MonoBehaviour host, IList<Entry> entries, Action<string> onCellAdded, int perFrame = 8)
    {
        Clear();
        if (entries == null || entries.Count == 0) return;

        _host = host;
        if (host == null)
        {
            foreach (var entry in entries) AddEntry(entry, onCellAdded);
            Reflow();
            return;
        }

        _fill = host.StartCoroutine(FillRoutine(entries, onCellAdded, perFrame));
    }

    private IEnumerator FillRoutine(IList<Entry> entries, Action<string> onCellAdded, int perFrame)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            AddEntry(entries[i], onCellAdded);
            if ((i + 1) % perFrame == 0)
            {
                _ui.Editor?.RequestOptionsResize();
                yield return null;
            }
        }

        _fill = null;
        _ui.Editor?.RequestOptionsResize();

        Reflow();
    }

    private void AddEntry(Entry entry, Action<string> onCellAdded)
    {
        if (entry == null) return;

        AddCell(entry.Id, entry.Display, null, entry.OnClick);
        onCellAdded?.Invoke(entry.Id);
    }

    public void Reflow()
    {
        if (Root == null) return;

        SettleBox();
    }

    public bool ShowNames { get; set; }

    /// Set by a tool that has a right-click meaning for its cells, and a badge to show for it.
    /// Both optional: left null, cells behave exactly as before.
    public Action<string, string> OnRightClick;
    public Func<string, bool> IsFavourite;

    public void AddCell(string id, string displayName, Sprite icon, Action onClick)
    {
        if (string.IsNullOrEmpty(id) || _byId.ContainsKey(id)) return;

        var go = _ui.CreateIconButton(_cells, icon, displayName, () =>
        {
            SetSelected(id);
            onClick?.Invoke();
        }, out var border, hoverText: displayName);

        if (ShowNames) AddCellName(go, displayName);

        var hover = go.GetComponent<MapEditorHover>();
        if (hover != null)
            hover.OnHover = hovered =>
            {
                if (hovered)
                {
                    ShowCaption(displayName);

                    _ui.ShowIconPreview(IconOf(id), displayName);
                }
                else
                {
                    ShowSelectedCaption();
                    _ui.HideIconPreview();
                }
            };

        var cell = new Cell
        {
            Root = go,
            Ring = border,
            Icon = go.transform.Find("Icon")?.GetComponent<Image>(),
            Letter = go.transform.Find("Label")?.gameObject,
            Display = displayName
        };
        _byId[id] = cell;

        if (OnRightClick != null)
            MapEditorUI.AttachRightClick(go, () => OnRightClick?.Invoke(id, displayName));

        SetStar(cell, IsFavourite != null && IsFavourite(id));

        if (border != null && _multi.Contains(id)) border.gameObject.SetActive(true);

        UpdateBoxHeight();
    }

    /// The badge is built the first time a cell needs one - the browser can hold several hundred
    /// cells and almost none of them are favourites.
    private static void SetStar(Cell cell, bool starred)
    {
        if (cell == null || cell.Root == null) return;

        if (!starred)
        {
            if (cell.Star != null) cell.Star.SetActive(false);
            return;
        }

        cell.Star ??= MapEditorUI.AddStarBadge(cell.Root.transform);
        cell.Star.SetActive(true);
        cell.Star.transform.SetAsLastSibling();
    }

    /// Re-asks IsFavourite for every cell on screen, for after a favourite is toggled elsewhere.
    public void RefreshFavourites()
    {
        if (IsFavourite == null) return;

        foreach (var pair in _byId) SetStar(pair.Value, IsFavourite(pair.Key));
    }

    private void AddCellName(GameObject cell, string displayName)
    {
        var stripRt = MapEditorUI.NewChild(cell.transform, "Name", stretch: false);
        stripRt.anchorMin = new Vector2(0f, 0f);
        stripRt.anchorMax = new Vector2(1f, 0f);
        stripRt.pivot = new Vector2(0.5f, 0f);
        stripRt.offsetMin = new Vector2(3f, 3f);
        stripRt.offsetMax = new Vector2(-3f, 25f);

        var plate = stripRt.gameObject.AddComponent<Image>();
        plate.color = new Color(0f, 0f, 0f, 0.66f);
        plate.raycastTarget = false;

        var label = _ui.CreateLabel(stripRt, displayName, 12, TextAlignmentOptions.Center);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(2f, 0f);
        labelRt.offsetMax = new Vector2(-2f, 0f);

        var text = label.GetComponent<TMP_Text>();
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
    }

    private void ShowCaption(string text)
    {
        if (_caption != null) _caption.text = text;
    }

    private void ShowSelectedCaption()
    {
        if (_caption == null) return;
        _caption.text = _selectedId != null && _byId.TryGetValue(_selectedId, out var cell) ? cell.Display : "";
    }

    public void SetCellIcon(string id, Sprite sprite)
    {
        if (sprite == null || !_byId.TryGetValue(id, out var cell)) return;
        if (cell.Icon == null) return;

        cell.Icon.sprite = sprite;
        cell.Icon.enabled = true;
        if (cell.Letter != null) cell.Letter.SetActive(false);
    }

    public void SetCellLetter(string id, string text)
    {
        if (!_byId.TryGetValue(id, out var cell) || cell.Letter == null) return;

        var label = cell.Letter.GetComponent<TMP_Text>();
        if (label == null) return;

        label.text = text ?? "";
        label.fontSize = 14f;
        label.enableWordWrapping = true;

        cell.Letter.SetActive(true);
        if (cell.Icon != null) cell.Icon.enabled = false;
    }

    public bool Has(string id) => _byId.ContainsKey(id);

    public IEnumerable<string> Ids => _byId.Keys;

    public void SetSelected(string id)
    {
        if (_multi.Count > 0) return;

        _selectedId = id;
        foreach (var pair in _byId)
        {
            if (pair.Value.Ring == null) continue;
            pair.Value.Ring.gameObject.SetActive(pair.Key == id);
        }
        ShowSelectedCaption();
    }

    private readonly HashSet<string> _multi = [];

    public void SetSelectedMany(IEnumerable<string> ids)
    {
        _multi.Clear();
        if (ids != null)
            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id)) _multi.Add(id);

        _selectedId = null;
        foreach (var pair in _byId)
        {
            if (pair.Value.Ring == null) continue;
            pair.Value.Ring.gameObject.SetActive(_multi.Contains(pair.Key));
        }

        ShowSelectedCaption();
    }
}
