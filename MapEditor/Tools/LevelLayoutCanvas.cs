using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class LevelLayoutCanvas
{
    private readonly RuntimeMapEditor _editor;
    private readonly MapEditorUI _ui;

    private CTLevelBlueprint _level;

    public LevelLayoutCanvas(RuntimeMapEditor editor, MapEditorUI ui)
    {
        _editor = editor;
        _ui = ui;
    }

    public bool IsOpen => _root != null;
    public RectTransform OptionsContent { get; private set; }
    public Action CloseRequested;

    public readonly struct DockItem
    {
        public readonly string Label;
        public readonly string Icon;
        public readonly string Hint;
        public readonly Action Do;

        public DockItem(string label, string icon, string hint, Action act)
        {
            Label = label;
            Icon = icon;
            Hint = hint;
            Do = act;
        }
    }

    // ---- metrics -------------------------------------------------------------------------------

    private const float OptionsWidth = 470f;

    private float _optionsHeight = 940f;
    private const float OptionsHeaderHeight = 34f;
    private const float ToolIconSize = 72f;
    private const int DockPadding = 8;
    private const float DockHeight = ToolIconSize + DockPadding * 2;
    private const float StatusLine = 26f;
    private const float StatusHeight = StatusLine * 2f + 8f;

    private const float MaxPitch = 168f;
    private const float MinPitch = 46f;

    // ---- objects -------------------------------------------------------------------------------

    private GameObject _root;
    private RectTransform _contentRoot;
    private RectTransform _ghostLayer;
    private RectTransform _roomLayer;
    private RectTransform _markLayer;

    private RectTransform _optionsRect;
    private RectTransform _optionsContent;
    private RectTransform _statusRect;
    private RectTransform _dockRect;
    private RectTransform _shortcutPanel;
    private RectTransform _titleRect;
    private RectTransform _closeRect;

    private GameObject _collapseButton;
    private bool _optionsCollapsed;
    private bool _shortcutsCollapsed;

    private TMP_Text _titleText;
    private TMP_Text _badgeText;
    private TMP_Text _hintText;

    private readonly List<RectTransform> _blockers = [];
    private readonly List<(string Key, string Action)> _shortcuts = [];

    public readonly Dictionary<CTLevelRoom, RectTransform> RoomRects = [];

    private CTLevelRoom _selected;
    private readonly HashSet<CTLevelRoom> _flagged = [];

    private float _pitch = 120f;
    private int _minX, _maxX, _minY, _maxY;

    // ---- open / close ---------------------------------------------------------------------------

    public void Open(CTLevelBlueprint level, List<DockItem> dock,
        IEnumerable<(string Key, string Action)> shortcuts)
    {
        Close();

        _level = level;
        var canvas = _ui.CanvasRoot;
        if (canvas == null || _level == null) return;

        _shortcuts.Clear();
        if (shortcuts != null) _shortcuts.AddRange(shortcuts);

        _root = new GameObject("LevelLayoutScreen");
        _root.transform.SetParent(canvas, false);
        Stretch(_root.AddComponent<RectTransform>());

        var backdrop = _root.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.97f);

        _contentRoot = MakeRect(_root.transform, "Content");
        Stretch(_contentRoot);
        _ghostLayer = MakeLayer("Ghosts");
        _roomLayer = MakeLayer("Rooms");
        _markLayer = MakeLayer("Marks");

        BuildTitle();
        BuildOptions();
        BuildDock(dock);
        BuildShortcutPanel();
        BuildStatus();
        BuildConfirmStrip();

        _ui.IconPreviewRightOffset = OptionsWidth + 34f;

        _editor.SetOwnChromeVisible(false);
        RebuildVisuals();
    }

    public void Close()
    {
        _editor.CancelPrompt();

        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _editor.SetOwnChromeVisible(true);
        }

        _ui.HideIconPreview();
        _ui.IconPreviewRightOffset = MapEditorUI.DefaultIconPreviewRightOffset;

        _root = null;
        _contentRoot = _ghostLayer = _roomLayer = _markLayer = null;
        _optionsRect = _optionsContent = _statusRect = _dockRect = null;
        _shortcutPanel = _titleRect = _closeRect = null;
        _collapseButton = null;
        _optionsCollapsed = false;
        _shortcutsCollapsed = false;

        _confirmRoot = null;
        _confirmLabel = _confirmButtonLabel = _altButtonLabel = null;
        _altButton = null;
        _confirmButtonRect = _cancelRect = null;
        _confirmAction = _altAction = null;
        _titleText = _badgeText = _hintText = null;
        OptionsContent = null;

        _blockers.Clear();
        _shortcuts.Clear();
        RoomRects.Clear();
        _flagged.Clear();
        _selected = null;
    }

    public void SetVisible(bool visible)
    {
        if (_root != null) _root.SetActive(visible);
    }

    public bool Visible => _root != null && _root.activeSelf;

    // ---- chrome ---------------------------------------------------------------------------------

    private RectTransform MakeLayer(string name)
    {
        var rt = MakeRect(_contentRoot, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    private void BuildTitle()
    {
        var title = _ui.CreateLabel(_root.transform, "", 30);
        _titleText = title.GetComponent<TMP_Text>();
        _titleText.enableWordWrapping = false;
        _titleText.raycastTarget = false;

        _titleRect = title.GetComponent<RectTransform>();
        _titleRect.anchorMin = _titleRect.anchorMax = new Vector2(0f, 1f);
        _titleRect.pivot = new Vector2(0f, 1f);
        _titleRect.sizeDelta = new Vector2(700f, 44f);
        _titleRect.anchoredPosition = new Vector2(24f, -18f);

        var close = _ui.CreateButton(_root.transform, "X", () => CloseRequested?.Invoke(), 36f);
        _closeRect = close.GetComponent<RectTransform>();
        _closeRect.anchorMin = _closeRect.anchorMax = new Vector2(1f, 1f);
        _closeRect.pivot = new Vector2(1f, 1f);
        _closeRect.sizeDelta = new Vector2(36f, 36f);
        _closeRect.anchoredPosition = new Vector2(-18f, -18f);
        _blockers.Add(_closeRect);
    }

    private void BuildOptions()
    {
        var canvasHeight = _ui.CanvasRoot != null ? _ui.CanvasRoot.rect.height : 1080f;
        _optionsHeight = Mathf.Clamp(canvasHeight - 58f - (DockHeight + StatusHeight + 12f), 320f, 1600f);

        _optionsRect = MakeRect(_root.transform, "Options");
        _optionsRect.anchorMin = _optionsRect.anchorMax = new Vector2(1f, 1f);
        _optionsRect.pivot = new Vector2(1f, 1f);
        _optionsRect.sizeDelta = new Vector2(OptionsWidth, _optionsHeight);
        _optionsRect.anchoredPosition = new Vector2(-14f, -58f);

        Plate(_optionsRect);
        _blockers.Add(_optionsRect);

        var header = MakeRect(_optionsRect, "Header");
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(0f, OptionsHeaderHeight);
        header.anchoredPosition = Vector2.zero;
        header.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        var title = _ui.CreateLabel(header, "Rooms", 20, TextAlignmentOptions.Left);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(12f, 0f);
        titleRect.offsetMax = new Vector2(-36f, 0f);
        var titleText = title.GetComponent<TMP_Text>();
        titleText.enableWordWrapping = false;
        titleText.raycastTarget = false;

        _collapseButton = _ui.CreateButton(header, "-", ToggleCollapsed, 26f);
        var collapseRect = _collapseButton.GetComponent<RectTransform>();
        collapseRect.anchorMin = collapseRect.anchorMax = new Vector2(1f, 0.5f);
        collapseRect.pivot = new Vector2(1f, 0.5f);
        collapseRect.sizeDelta = new Vector2(26f, 26f);
        collapseRect.anchoredPosition = new Vector2(-4f, 0f);

        var content = MakeRect(_optionsRect, "Content");
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = Vector2.zero;
        content.offsetMax = new Vector2(0f, -OptionsHeaderHeight);
        _optionsContent = content;

        OptionsContent = _ui.CreateScrollColumn(content, "OptionsColumn", out _);
    }

    private void ToggleCollapsed()
    {
        _editor.BlockWorldClicks();
        _optionsCollapsed = !_optionsCollapsed;

        if (_optionsContent != null) _optionsContent.gameObject.SetActive(!_optionsCollapsed);
        if (_optionsRect != null)
            _optionsRect.sizeDelta = new Vector2(OptionsWidth,
                _optionsCollapsed ? OptionsHeaderHeight : _optionsHeight);

        var label = _collapseButton != null ? _collapseButton.GetComponentInChildren<TMP_Text>() : null;
        if (label != null) label.text = _optionsCollapsed ? "+" : "-";

        RebuildVisuals();
    }

    private void BuildDock(List<DockItem> items)
    {
        if (items == null || items.Count == 0) return;

        var dock = new GameObject("Dock");
        dock.transform.SetParent(_root.transform, false);

        _dockRect = dock.AddComponent<RectTransform>();
        _dockRect.anchorMin = _dockRect.anchorMax = new Vector2(0.5f, 0f);
        _dockRect.pivot = new Vector2(0.5f, 0f);
        _dockRect.sizeDelta = new Vector2(0f, DockHeight);
        _dockRect.anchoredPosition = new Vector2(0f, 12f);

        Plate(_dockRect);

        var layout = dock.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 6f;
        layout.padding = new RectOffset(DockPadding, DockPadding, DockPadding, DockPadding);
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = dock.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (var item in items)
        {
            var act = item.Do;
            _ui.CreateIconButton(dock.transform, MapEditorIcons.GetToolIconOrNull(item.Icon),
                item.Label, () => act?.Invoke(), out _, ToolIconSize, item.Hint);
        }

        _blockers.Add(_dockRect);
    }

    private void BuildShortcutPanel()
    {
        var go = new GameObject("Shortcuts");
        go.transform.SetParent(_root.transform, false);

        _shortcutPanel = go.AddComponent<RectTransform>();
        _shortcutPanel.anchorMin = Vector2.zero;
        _shortcutPanel.anchorMax = Vector2.zero;
        _shortcutPanel.pivot = Vector2.zero;
        _shortcutPanel.sizeDelta = new Vector2(252f, 0f);
        _shortcutPanel.anchoredPosition = new Vector2(16f, 16f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _blockers.Add(_shortcutPanel);
        RefreshShortcuts();
    }

    private void RefreshShortcuts()
    {
        if (_shortcutPanel == null) return;

        ClearLayer(_shortcutPanel);

        if (!_shortcutsCollapsed)
        {
            foreach (var (key, action) in _shortcuts)
                _ui.CreateKeyHint(_shortcutPanel, key, action);

            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+Z", "Undo last change");
            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+S", "Quicksave this level");
            _ui.CreateKeyHint(_shortcutPanel, "Esc", "Close the layout");
        }

        _ui.CreateButton(_shortcutPanel, _shortcutsCollapsed ? "Shortcuts   +" : "Shortcuts   -",
            () =>
            {
                _editor.BlockWorldClicks();
                _shortcutsCollapsed = !_shortcutsCollapsed;
                RefreshShortcuts();
            }, 30f);
    }

    private void BuildStatus()
    {
        _statusRect = MakeRect(_root.transform, "Status");
        _statusRect.anchorMin = new Vector2(0f, 0f);
        _statusRect.anchorMax = new Vector2(1f, 0f);
        _statusRect.pivot = new Vector2(0.5f, 0f);

        _statusRect.offsetMin = new Vector2(284f, 12f + DockHeight + 8f);
        _statusRect.offsetMax = new Vector2(-388f, 12f + DockHeight + 8f);
        _statusRect.sizeDelta = new Vector2(_statusRect.sizeDelta.x, StatusHeight);

        Plate(_statusRect);
        _blockers.Add(_statusRect);

        var badge = _ui.CreateLabel(_statusRect, "", 17, TextAlignmentOptions.Left);
        var badgeRect = badge.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0f, 1f);
        badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.pivot = new Vector2(0.5f, 1f);
        badgeRect.offsetMin = new Vector2(14f, 0f);
        badgeRect.offsetMax = new Vector2(-14f, 0f);
        badgeRect.sizeDelta = new Vector2(badgeRect.sizeDelta.x, StatusLine);
        badgeRect.anchoredPosition = new Vector2(0f, -4f);
        _badgeText = badge.GetComponent<TMP_Text>();
        _badgeText.raycastTarget = false;
        _badgeText.enableWordWrapping = false;
        _badgeText.overflowMode = TextOverflowModes.Ellipsis;

        var hint = _ui.CreateLabel(_statusRect, "", 17, TextAlignmentOptions.Left);
        var hintRect = hint.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.offsetMin = new Vector2(14f, 0f);
        hintRect.offsetMax = new Vector2(-14f, 0f);
        hintRect.sizeDelta = new Vector2(hintRect.sizeDelta.x, StatusLine);
        hintRect.anchoredPosition = new Vector2(0f, 4f);
        _hintText = hint.GetComponent<TMP_Text>();
        _hintText.raycastTarget = false;
        _hintText.enableWordWrapping = false;
        _hintText.overflowMode = TextOverflowModes.Ellipsis;
    }

    // ---- confirm strip ---------------------------------------------------------------------------

    private const float ConfirmBottom = 12f + DockHeight + 8f + StatusHeight + 8f;

    private GameObject _confirmRoot;
    private TMP_Text _confirmLabel;
    private TMP_Text _confirmButtonLabel;
    private TMP_Text _altButtonLabel;
    private GameObject _altButton;
    private RectTransform _confirmButtonRect;
    private RectTransform _cancelRect;
    private Action _confirmAction;
    private Action _altAction;

    public bool ConfirmOpen => _confirmRoot != null && _confirmRoot.activeSelf;

    private void BuildConfirmStrip()
    {
        _confirmRoot = new GameObject("Confirm");
        _confirmRoot.transform.SetParent(_root.transform, false);

        var rect = _confirmRoot.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(560f, 96f);
        rect.anchoredPosition = new Vector2(0f, ConfirmBottom);

        var border = _confirmRoot.AddComponent<Image>();
        border.sprite = MapEditorUI.RoundedPlate;
        border.type = Image.Type.Sliced;
        border.pixelsPerUnitMultiplier = 1.6f;
        border.color = new Color(0f, 0f, 0f, 0.55f);

        var fill = MakeRect(rect, "Fill");
        Stretch(fill);
        fill.offsetMin = new Vector2(3f, 3f);
        fill.offsetMax = new Vector2(-3f, -3f);

        var plate = fill.gameObject.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.6f;
        plate.color = new Color(0f, 0f, 0f, 0.82f);
        plate.raycastTarget = false;

        var label = _ui.CreateLabel(rect, "", 20, TextAlignmentOptions.Center);
        _confirmLabel = label.GetComponent<TMP_Text>();
        _confirmLabel.raycastTarget = false;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = new Vector2(-12f, -6f);

        var confirm = _ui.CreateButton(rect, "Enter", () =>
        {
            var action = _confirmAction;
            HideConfirm();
            action?.Invoke();
        }, 34f);
        _confirmButtonRect = confirm.GetComponent<RectTransform>();
        _confirmButtonRect.anchorMin = _confirmButtonRect.anchorMax = new Vector2(0.5f, 0f);
        _confirmButtonRect.pivot = new Vector2(1f, 0f);
        _confirmButtonRect.sizeDelta = new Vector2(150f, 34f);
        _confirmButtonLabel = confirm.GetComponentInChildren<TMP_Text>();

        _altButton = _ui.CreateButton(rect, "Discard", () =>
        {
            var action = _altAction;
            HideConfirm();
            action?.Invoke();
        }, 34f, MapEditorEmphasis.Quiet);
        var altRect = _altButton.GetComponent<RectTransform>();
        altRect.anchorMin = altRect.anchorMax = new Vector2(0.5f, 0f);
        altRect.pivot = new Vector2(0.5f, 0f);
        altRect.sizeDelta = new Vector2(150f, 34f);
        altRect.anchoredPosition = new Vector2(0f, 10f);
        _altButtonLabel = _altButton.GetComponentInChildren<TMP_Text>();

        var cancel = _ui.CreateButton(rect, "Cancel", HideConfirm, 34f, MapEditorEmphasis.Quiet);
        _cancelRect = cancel.GetComponent<RectTransform>();
        _cancelRect.anchorMin = _cancelRect.anchorMax = new Vector2(0.5f, 0f);
        _cancelRect.pivot = new Vector2(0f, 0f);
        _cancelRect.sizeDelta = new Vector2(150f, 34f);

        _blockers.Add(rect);
        _confirmRoot.SetActive(false);
    }

    public void ShowConfirm(string text, Action onConfirm, string confirmLabel = "Enter",
        string altLabel = null, Action onAlt = null)
    {
        if (_confirmRoot == null) return;

        _confirmLabel.text = text;
        _confirmAction = onConfirm;
        _altAction = onAlt;

        if (_confirmButtonLabel != null) _confirmButtonLabel.text = confirmLabel;

        var threeWay = onAlt != null;
        if (_altButton != null) _altButton.SetActive(threeWay);
        if (_altButtonLabel != null && altLabel != null) _altButtonLabel.text = altLabel;

        if (_confirmButtonRect != null)
            _confirmButtonRect.anchoredPosition = new Vector2(threeWay ? -114f : -8f, 10f);
        if (_cancelRect != null) _cancelRect.anchoredPosition = new Vector2(threeWay ? 114f : 8f, 10f);

        _confirmRoot.SetActive(true);
    }

    public void HideConfirm()
    {
        _confirmAction = null;
        _altAction = null;
        if (_confirmRoot != null) _confirmRoot.SetActive(false);
    }

    private static void Plate(RectTransform rect)
    {
        VanillaChrome.Dress(rect.gameObject.AddComponent<Image>());
    }

    // ---- what the tool sets ------------------------------------------------------------------------

    public void SetTitle(string text)
    {
        if (_titleText != null) _titleText.text = text;
    }

    public void SetBadge(string text, Color colour)
    {
        if (_badgeText == null) return;

        _badgeText.text = text;
        _badgeText.color = colour;
    }

    public void SetHint(string text, StatusSeverity severity = StatusSeverity.Info)
    {
        if (_hintText == null) return;

        _hintText.text = text ?? "";
        _hintText.color = severity switch
        {
            StatusSeverity.Success => new Color(0.55f, 0.9f, 0.55f),
            StatusSeverity.Warning => new Color(1f, 0.76f, 0.3f),
            StatusSeverity.Error => new Color(1f, 0.42f, 0.42f),
            _ => Color.white
        };
    }

    public void SetIssues(List<LevelLayout.LayoutIssue> issues)
    {
        _flagged.Clear();

        if (issues != null)
            foreach (var issue in issues)
                if (!issue.IsAdvisory && issue.Room != null) _flagged.Add(issue.Room);

        RepaintMarks();
    }

    public void HighlightRoom(CTLevelRoom room)
    {
        _selected = room;
        RepaintMarks();
    }

    // ---- pointer ------------------------------------------------------------------------------------

    public Vector2 PointerContent()
    {
        if (_contentRoot == null) return Vector2.zero;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(_contentRoot, Input.mousePosition,
            null, out var local);
        return local;
    }

    public bool PointerOverChrome()
    {
        if (_editor.WorldClicksBlocked) return true;

        var mouse = (Vector2)Input.mousePosition;
        foreach (var blocker in _blockers)
        {
            if (blocker == null || !blocker.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(blocker, mouse, null)) return true;
        }

        return false;
    }

    public Vector2Int CellAt(Vector2 point)
    {
        var originX = (_minX + _maxX) * 0.5f;
        var originY = (_minY + _maxY) * 0.5f;

        return new Vector2Int(
            Mathf.RoundToInt(point.x / _pitch + originX),
            Mathf.RoundToInt(point.y / _pitch + originY));
    }

    public Vector2 CellCentre(int x, int y)
    {
        var originX = (_minX + _maxX) * 0.5f;
        var originY = (_minY + _maxY) * 0.5f;

        return new Vector2((x - originX) * _pitch, (y - originY) * _pitch);
    }

    public CTLevelRoom HitRoom(Vector2 point)
    {
        var cell = CellAt(point);

        var centre = CellCentre(cell.x, cell.y);
        var half = _pitch * 0.43f;
        if (Mathf.Abs(point.x - centre.x) > half || Mathf.Abs(point.y - centre.y) > half) return null;

        return LevelLayout.At(_level, cell.x, cell.y);
    }

    public bool InWindow(Vector2Int cell) =>
        cell.x >= _minX && cell.x <= _maxX && cell.y >= _minY && cell.y <= _maxY;

    // ---- drawing --------------------------------------------------------------------------------------

    private static readonly Color RoomFill = new(0.16f, 0.17f, 0.21f, 0.95f);
    private static readonly Color PodiumFill = new(0.13f, 0.24f, 0.19f, 0.95f);
    private static readonly Color EndFill = new(0.26f, 0.21f, 0.11f, 0.95f);
    private static readonly Color RoomEdge = new(0.75f, 0.65f, 0.45f, 0.9f);
    private static readonly Color DoorColour = new(0.85f, 0.8f, 0.66f, 0.95f);
    private static readonly Color WayInColour = new(0.35f, 0.9f, 0.5f, 0.95f);
    private static readonly Color WayOutColour = new(1f, 0.78f, 0.28f, 0.95f);
    private static readonly Color GhostColour = new(1f, 1f, 1f, 0.09f);
    private static readonly Color Accent = new(1f, 0.85f, 0.35f, 0.95f);
    private static readonly Color Blocking = new(1f, 0.42f, 0.42f, 0.95f);

    private static readonly Color CombatEdge = new(0.95f, 0.42f, 0.38f, 0.95f);
    private static readonly Color RewardEdge = new(0.45f, 0.82f, 1f, 0.95f);

    public void RebuildVisuals()
    {
        if (_root == null || _level == null) return;

        Measure();

        ClearLayer(_ghostLayer);
        ClearLayer(_roomLayer);
        RoomRects.Clear();

        for (var y = _minY; y <= _maxY; y++)
        for (var x = _minX; x <= _maxX; x++)
        {
            if (LevelLayout.At(_level, x, y) != null) continue;
            BuildGhost(x, y);
        }

        for (var i = 0; i < _level.Rooms.Count; i++)
        {
            var room = _level.Rooms[i];
            if (room != null) BuildRoom(room, i);
        }

        RepaintMarks();
    }

    private void Measure()
    {
        var canvas = _ui.CanvasRoot;
        var size = canvas != null ? canvas.rect.size : new Vector2(1920f, 1080f);

        if (_level.Rooms.Count == 0)
        {
            _minX = _minY = -2;
            _maxX = _maxY = 2;
        }
        else
        {
            _minX = _minY = int.MaxValue;
            _maxX = _maxY = int.MinValue;

            foreach (var room in _level.Rooms)
            {
                if (room == null) continue;
                _minX = Mathf.Min(_minX, room.X);
                _maxX = Mathf.Max(_maxX, room.X);
                _minY = Mathf.Min(_minY, room.Y);
                _maxY = Mathf.Max(_maxY, room.Y);
            }

            _minX--; _maxX++; _minY--; _maxY++;
        }

        var available = new Vector2(
            size.x - (_optionsCollapsed ? 60f : OptionsWidth + 60f) - 40f,
            size.y - 90f - (DockHeight + StatusHeight + 60f));

        var columns = Mathf.Max(1, _maxX - _minX + 1);
        var rows = Mathf.Max(1, _maxY - _minY + 1);

        _pitch = Mathf.Clamp(Mathf.Min(available.x / columns, available.y / rows), MinPitch, MaxPitch);

        if (_contentRoot != null)
            _contentRoot.anchoredPosition =
                new Vector2(_optionsCollapsed ? 0f : -OptionsWidth * 0.5f, -20f);
    }

    private void BuildGhost(int x, int y)
    {
        var rect = MakeRect(_ghostLayer, $"Ghost {x},{y}");
        rect.sizeDelta = Vector2.one * (_pitch * 0.2f);
        rect.anchoredPosition = CellCentre(x, y);

        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = MapEditorUI.RoundedPlate;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1.6f;
        image.color = GhostColour;
        image.raycastTarget = false;
    }

    private void BuildRoom(CTLevelRoom room, int index)
    {
        var rect = MakeRect(_roomLayer, $"Room {index + 1}");
        rect.sizeDelta = Vector2.one * (_pitch * 0.86f);
        rect.anchoredPosition = CellCentre(room.X, room.Y);
        RoomRects[room] = rect;

        var edge = rect.gameObject.AddComponent<Image>();
        edge.sprite = MapEditorUI.RoundedPlate;
        edge.type = Image.Type.Sliced;
        edge.pixelsPerUnitMultiplier = 1.6f;
        edge.color = room.Modifier switch
        {
            "Combat" => CombatEdge,
            "Reward" => RewardEdge,
            _ => RoomEdge
        };
        edge.raycastTarget = false;

        var fill = MakeRect(rect, "Fill");
        Stretch(fill);
        fill.offsetMin = new Vector2(3f, 3f);
        fill.offsetMax = new Vector2(-3f, -3f);

        var plate = fill.gameObject.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.6f;

        plate.color = room.VanillaRoom switch
        {
            CTLevelRoom.PodiumRoom => PodiumFill,
            CTLevelRoom.EndOfFloorRoom => EndFill,
            _ => RoomFill
        };
        plate.raycastTarget = false;

        if (_level.AuthoredLayout)
            foreach (var side in LevelLayout.Sides)
                BuildDoor(rect, room, side);

        var caption = _ui.CreateLabel(rect, Caption(room, index),
            Mathf.RoundToInt(Mathf.Clamp(_pitch * 0.13f, 11f, 18f)), TextAlignmentOptions.Center);
        var captionRect = caption.GetComponent<RectTransform>();
        Stretch(captionRect);
        captionRect.offsetMin = new Vector2(8f, 8f);
        captionRect.offsetMax = new Vector2(-8f, -8f);

        var text = caption.GetComponent<TMP_Text>();
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Truncate;
    }

    private string Caption(CTLevelRoom room, int index)
    {
        if (room.VanillaRoom != CTLevelRoom.Generated)
            return $"<b>{index + 1}</b>\n{LevelLayout.DescribeRole(room.VanillaRoom)}";

        return $"<b>{index + 1}</b>\n{DescribePool(room.NodePool)}";
    }

    private static string DescribePool(List<string> pool)
    {
        if (pool == null || pool.Count == 0) return "any map";

        var vanilla = pool.Contains(CTLevelRoom.VanillaNode);
        var named = pool.Count - (vanilla ? 1 : 0);

        if (named == 0) return "vanilla";

        if (named == 1 && !vanilla)
            return pool.Find(entry => entry != CTLevelRoom.VanillaNode);

        var maps = named == 1 ? "1 map" : $"{named} maps";
        return vanilla ? "vanilla + " + maps : maps;
    }

    private void BuildDoor(RectTransform parent, CTLevelRoom room, LevelSide side)
    {
        var value = LevelLayout.Get(room, side);
        if (value == CTLevelRoom.Wall) return;

        var colour = value switch
        {
            CTLevelRoom.WayIn => WayInColour,
            CTLevelRoom.WayOut => WayOutColour,
            _ => DoorColour
        };

        var thickness = Mathf.Max(4f, _pitch * 0.055f);
        var length = _pitch * 0.3f;

        var rect = MakeRect(parent, side + " door");
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = side is LevelSide.North or LevelSide.South
            ? new Vector2(length, thickness)
            : new Vector2(thickness, length);

        var offset = (_pitch * 0.86f) * 0.5f - thickness * 0.5f;
        rect.anchoredPosition = side switch
        {
            LevelSide.North => new Vector2(0f, offset),
            LevelSide.South => new Vector2(0f, -offset),
            LevelSide.East => new Vector2(offset, 0f),
            _ => new Vector2(-offset, 0f)
        };

        var image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
    }

    private void RepaintMarks()
    {
        if (_markLayer == null) return;
        ClearLayer(_markLayer);

        foreach (var room in _flagged) BuildRing(room, Blocking, 1.12f);
        if (_selected != null) BuildRing(_selected, Selection, 1f);
    }

    private static readonly Color Selection = new(1f, 0.35f, 0.35f, 1f);

    private void BuildRing(CTLevelRoom room, Color colour, float scale)
    {
        if (room == null) return;

        var rect = MakeRect(_markLayer, "Ring");
        rect.sizeDelta = Vector2.one * (_pitch * 0.86f * scale + 10f);
        rect.anchoredPosition = CellCentre(room.X, room.Y);

        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = MapEditorUI.RoundedOutline;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1.6f;
        image.color = colour;
        image.raycastTarget = false;
    }

    // ---- scaffolding ------------------------------------------------------------------------------

    private static RectTransform MakeRect(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void ClearLayer(RectTransform layer)
    {
        if (layer == null) return;

        for (var i = layer.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(layer.GetChild(i).gameObject);
    }
}
