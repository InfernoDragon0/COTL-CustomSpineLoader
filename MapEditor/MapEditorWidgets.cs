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

        // Catcher: dims the rest of the editor, absorbs the click that closes the list, and is
        // registered as a blocker so that click never reaches a placement tool.
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
        for (var i = 0; i < _options.Count; i++)
        {
            var index = i;
            var option = _options[i];
            _ui.CreateButton(content, option, () => Choose(index), OptionHeight);
        }
    }

    // Opens downward from the row, and upward instead when there is not enough room below - the
    // enemy and prop dropdowns sit low in a tall options panel.
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

    public void Clear()
    {
        StopFill();

        foreach (var cell in _byId.Values)
            if (cell.Root != null) UnityEngine.Object.Destroy(cell.Root);

        _byId.Clear();
        _selectedId = null;
        if (_caption != null) _caption.text = "";

        // Going from a long group to a short one shrinks this grid, and the panel around it has
        // to be told - nested size fitters will not notice on their own.
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
    }

    private void AddEntry(Entry entry, Action<string> onCellAdded)
    {
        if (entry == null) return;

        AddCell(entry.Id, entry.Display, null, entry.OnClick);
        onCellAdded?.Invoke(entry.Id);
    }

    public void AddCell(string id, string displayName, Sprite icon, Action onClick)
    {
        if (string.IsNullOrEmpty(id) || _byId.ContainsKey(id)) return;

        // The hover text is the item's name in the status bar; the tile itself is far too small
        // to carry it, and the caption under the grid echoes it locally.
        var go = _ui.CreateIconButton(_cells, icon, displayName, () =>
        {
            SetSelected(id);
            onClick?.Invoke();
        }, out var border, hoverText: displayName);

        var hover = go.GetComponent<MapEditorHover>();
        if (hover != null)
            hover.OnHover = hovered =>
            {
                if (hovered) ShowCaption(displayName);
                else ShowSelectedCaption();
            };

        _byId[id] = new Cell
        {
            Root = go,
            Ring = border,
            Icon = go.transform.Find("Icon")?.GetComponent<Image>(),
            Letter = go.transform.Find("Label")?.gameObject,
            Display = displayName
        };
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
        _selectedId = id;
        foreach (var pair in _byId)
        {
            if (pair.Value.Ring == null) continue;
            pair.Value.Ring.gameObject.SetActive(pair.Key == id);
        }
        ShowSelectedCaption();
    }
}
