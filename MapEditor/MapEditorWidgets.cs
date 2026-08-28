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

    private const float OptionHeight = 34f;
    private const float MaxListHeight = 460f;

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

    // Reflects a selection made elsewhere; does not fire the callback.
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

        // Catcher: dims the editor, absorbs the closing click, and blocks it from reaching a tool.
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
        var height = Mathf.Min(_options.Count * (OptionHeight + 4f) + 16f, MaxListHeight);
        var width = Mathf.Max(_row.rect.width, 220f);

        var panel = new GameObject("Options");
        panel.transform.SetParent(_floating.transform, false);
        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);

        var img = panel.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.8f);

        PositionList(canvas, rt, height);

        var content = _ui.CreateScrollColumn(panel.transform, "OptionList", out _, spacing: 4f);
        content.gameObject.AddComponent<MapEditorQuietArea>();
        for (var i = 0; i < _options.Count; i++)
        {
            var index = i;
            var option = _options[i];
            _ui.CreateButton(content, option, () => Choose(index), OptionHeight);
        }
    }

    // Opens downward from the row, upward when there is not enough room below.
    private void PositionList(RectTransform canvas, RectTransform panel, float height)
    {
        var corners = new Vector3[4];
        _row.GetWorldCorners(corners);

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
            // The blocker list purges destroyed rects on its own next sweep.
            UnityEngine.Object.Destroy(_floating);
            _floating = null;
        }
        _ui.NotifyDropdownClosed(this);
    }
}

// A list of rows boxed in a scroll view: as tall as its rows need, up to a ceiling, and scrolling
// beyond that rather than growing the panel forever.
//
// The height is worked out from the row *count* rather than measured, and that is not laziness. A
// rebuild destroys its old rows with Object.Destroy, which defers to end of frame, so anything
// measured in the same breath is measuring the rows that are on their way out as well as the ones
// replacing them. Counting is exact and needs no second frame.
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

    // The scroll column's own vertical padding, which the rows sit inside.
    private const float Padding = 16f;

    // Tall enough that an empty list reads as deliberate rather than broken.
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

        // The outer force-rebuild stops at the scroll viewport, which carries no layout controller
        // of its own, so the column of rows underneath has to be rebuilt by name.
        if (_scroll != null && _scroll.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);

        _ui.Editor?.RequestOptionsResize();
    }

    // A rebuilt list must not inherit the last one's scroll position, or a shorter list puts its
    // rows above the top of the viewport.
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

    // What a grid needs to know about one entry before its icon exists.
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

    // The box is as tall as its rows need, up to the ceiling it was given. Worked out from the
    // count rather than measured: the cells fill in over several frames, and a rect that has not
    // been through a layout pass yet measures zero.
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

    // Why the LayoutElement is not enough on its own, which is what the last two attempts at this
    // assumed.
    //
    // The editor answers RequestOptionsResize with ForceRebuildLayoutImmediate on the tool's option
    // column, and that walk does not reach in here. LayoutRebuilder descends into a child only if
    // the rect it is standing on carries a layout controller of its own - and a ScrollRect's
    // Viewport carries nothing but a RectMask2D. So the walk stops dead at the viewport, and the
    // grid of cells underneath it, with its own ContentSizeFitter, is never re-measured from above.
    // It gets there eventually through its own dirty flags, but the box was already sized against
    // the stale measurement by then, and nothing came back to correct it: a box that had been tall
    // for a big group stayed tall for the small group after it.
    //
    // So the inner column is rebuilt here by name, a couple of frames later - late enough that the
    // destroyed cells of the previous group are actually gone (Destroy defers to end of frame) and
    // the staggered fill has stopped adding to this one.
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

        // And to the top AFTER the rebuild, which is the half Clear() cannot do.
        //
        // Clear() resets the scroll while the box is empty, and it has to - a shorter list must not
        // inherit the last one's position. But the box then grows through the staggered fill, and a
        // scroll view whose content gets taller under it does not keep its top where it was: by the
        // end of the fill the first row had drifted above the viewport. That is the clipped row, and
        // it came good on the next selection only because a second settle happened to land after the
        // growing had stopped. Here it always does.
        ScrollBoxToTop();

        _ui.Editor?.RequestOptionsResize();
    }

    // A shorter list must not inherit the last one's scroll position, or the handful of icons it
    // does have sit above the top of the viewport and have to be scrolled back up to.
    private void ScrollBoxToTop()
    {
        if (_boxScroll == null) return;

        _boxScroll.StopMovement();
        if (_boxScroll.content != null) _boxScroll.content.anchoredPosition = Vector2.zero;
        _boxScroll.verticalNormalizedPosition = 1f;
    }

    // The scroll column's own vertical padding, which the rows sit inside.
    private const float BoxPadding = 16f;

    // Tall enough to read "nothing here" as deliberate rather than broken.
    private const float EmptyBoxHeight = 48f;

    // The sprite a cell is currently wearing, if its icon has arrived.
    private Sprite IconOf(string id) =>
        _byId.TryGetValue(id, out var cell) && cell.Icon != null && cell.Icon.enabled
            ? cell.Icon.sprite
            : null;

    public void Clear()
    {
        StopFill();

        // The cells the preview could be showing are about to go.
        _ui.HideIconPreview();

        foreach (var cell in _byId.Values)
            if (cell.Root != null) UnityEngine.Object.Destroy(cell.Root);

        _byId.Clear();
        _selectedId = null;
        if (_caption != null) _caption.text = "";

        ScrollBoxToTop();
        UpdateBoxHeight();

        // Nested size fitters will not notice the shrink on their own; tell the panel.
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

        // The box grew all the way through the fill, and a scroll view whose content gets taller
        // under it does not keep its top where it was - which is why the first row came up clipped
        // on the first draw and came good on the next one, when something else forced a re-measure.
        // Measured once here, at the only moment the final height is known.
        Reflow();
    }

    private void AddEntry(Entry entry, Action<string> onCellAdded)
    {
        if (entry == null) return;

        AddCell(entry.Id, entry.Display, null, entry.OnClick);
        onCellAdded?.Invoke(entry.Id);
    }

    // Re-measures the grid where Unity would not have thought to.
    //
    // A layout rebuilds when its own contents change, not when it is moved: a grid that is
    // reparented - the level tool lifts this one out of its column so a rebuild of the column
    // cannot destroy it - keeps whatever sizes it was measured with in its old home, and a scroll
    // box measured against a stale content height clips the first row. Nothing about that is
    // visible until it happens, which is why it appeared only sometimes.
    public void Reflow()
    {
        if (Root == null) return;

        // Through the same settle every other re-measure goes through, rather than a rebuild of its
        // own: the box needs a couple of frames for destroyed cells to actually be gone, and two
        // rebuilds racing each other is how the last attempt at this ended up depending on timing.
        SettleBox();
    }

    // Whether each cell carries its own name along the bottom. Off by default: a catalogue of
    // hundreds is browsed by picture and the name lives in the caption under the grid, where one
    // line serves every cell. A short list of things the author named themselves is the other case
    // - there the name IS the identifying thing, and reading it should not need a hover.
    public bool ShowNames { get; set; }

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

                    // The cell's own sprite, blown up beside the panel. Read at hover time rather
                    // than captured: icons arrive asynchronously, so the cell may have been empty
                    // when it was built.
                    _ui.ShowIconPreview(IconOf(id), displayName);
                }
                else
                {
                    ShowSelectedCaption();
                    _ui.HideIconPreview();
                }
            };

        _byId[id] = new Cell
        {
            Root = go,
            Ring = border,
            Icon = go.transform.Find("Icon")?.GetComponent<Image>(),
            Letter = go.transform.Find("Label")?.gameObject,
            Display = displayName
        };

        // Born lit if it is already in the gathered set - see SetSelectedMany.
        if (border != null && _multi.Contains(id)) border.gameObject.SetActive(true);

        UpdateBoxHeight();
    }

    // A strip along the bottom of the cell, over the picture rather than beside it - the cell is
    // square and the name is one line, so taking a slice of the image costs less than shrinking
    // every image to make room.
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

    // Several cells lit at once, for a tool that gathers a set rather than picking one.
    //
    // Held by the grid rather than re-applied by the caller because the cells arrive over several
    // frames: a set applied once would only reach whichever cells happened to exist at the time,
    // and switching group or searching rebuilds all of them. AddCell consults it instead, so a cell
    // is born lit if it belongs to the set. Clear() deliberately leaves it alone - it is the tool's
    // intent, not the view's state.
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
