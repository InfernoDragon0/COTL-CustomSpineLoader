using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class DungeonMapCanvas
{
    private readonly RuntimeMapEditor _editor;
    private readonly MapEditorUI _ui;

    private CTDungeonMap _map;

    public DungeonMapCanvas(RuntimeMapEditor editor, MapEditorUI ui)
    {
        _editor = editor;
        _ui = ui;
    }

    // ---- what the screen offers ------------------------------------------------------------

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

    // ---- metrics -----------------------------------------------------------------------------

    private const float OptionsWidth = 360f;
    private const float OptionsHeight = 940f;
    private const float OptionsHeaderHeight = 34f;
    private const float ToolIconSize = 72f;
    private const int DockPadding = 8;
    private const float DockHeight = ToolIconSize + DockPadding * 2;
    private const float StatusLine = 26f;
    private const float StatusHeight = StatusLine * 2f + 8f;

    private const float NodeScale = 0.5f;
    private const float BigNodeScale = 1.5f;

    private const float NodeRadius = 52f;

    private const float LinkReach = 14f;

    // ---- objects -----------------------------------------------------------------------------

    private GameObject _root;
    private RectTransform _contentRoot;
    private RectTransform _linkLayer;
    private RectTransform _nodeLayer;
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

    public readonly Dictionary<CTDungeonMapNode, RectTransform> NodeRects = [];
    private readonly Dictionary<CTDungeonMapNode, DungeonNodeVisual> _visuals = [];

    private sealed class LinkView
    {
        public RectTransform Rect;
        public Lamb.UI.MMUILineRenderer Line;
        public CTDungeonMapNode From;
        public CTDungeonMapNode To;
    }

    private readonly List<LinkView> _links = [];

    private CTDungeonMapNode _marked;
    private CTDungeonMapNode _startNode;

    private readonly Dictionary<CTDungeonMapNode, int> _layerOf = [];
    private List<DungeonMapBuilder.MapIssue> _issues = [];

    // ---- open / close ------------------------------------------------------------------------

    public void Open(CTDungeonMap map, List<DockItem> dock,
        IEnumerable<(string Key, string Action)> shortcuts)
    {
        Close();

        _map = map;
        var canvas = _ui.CanvasRoot;
        if (canvas == null || _map == null) return;

        _shortcuts.Clear();
        if (shortcuts != null) _shortcuts.AddRange(shortcuts);

        if (DungeonMapSkin.NeedsLoad) _editor.StartCoroutine(LoadSkin());

        _root = new GameObject("DungeonMapScreen");
        _root.transform.SetParent(canvas, false);
        Stretch(_root.AddComponent<RectTransform>());

        var backdrop = _root.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.97f);

        BuildChrome();

        _contentRoot = MakeRect(_root.transform, "Content");
        Stretch(_contentRoot);
        _linkLayer = MakeLayer("Connections");
        _nodeLayer = MakeLayer("Nodes");
        _markLayer = MakeLayer("Marks");

        BuildTitle();
        BuildOptions();
        BuildDock(dock);
        BuildShortcutPanel();
        BuildStatus();
        BuildConfirmStrip();

        _editor.SetOwnChromeVisible(false);

        RebuildVisuals();
    }

    private System.Collections.IEnumerator LoadSkin()
    {
        var map = _map;
        yield return DungeonMapSkin.EnsureLoaded();

        if (_root != null && ReferenceEquals(_map, map)) RebuildVisuals();
    }

    public void Close()
    {
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _editor.SetOwnChromeVisible(true);
        }

        _root = null;
        _contentRoot = _linkLayer = _nodeLayer = _markLayer = null;
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
        NodeRects.Clear();
        _visuals.Clear();
        _links.Clear();
        _issues = [];
        _marked = null;
    }

    public void SetVisible(bool visible)
    {
        if (_root != null) _root.SetActive(visible);
    }

    public bool Visible => _root != null && _root.activeSelf;

    // ---- chrome ------------------------------------------------------------------------------

    private void BuildChrome()
    {
        var chrome = DungeonMapSkin.CloneChrome();
        if (chrome == null) return;

        chrome.transform.SetParent(_root.transform, false);
        chrome.SetActive(true);

        var rt = chrome.GetComponent<RectTransform>();
        if (rt != null) Stretch(rt);
    }

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
        _optionsRect = MakeRect(_root.transform, "Options");
        _optionsRect.anchorMin = _optionsRect.anchorMax = new Vector2(1f, 1f);
        _optionsRect.pivot = new Vector2(1f, 1f);
        _optionsRect.sizeDelta = new Vector2(OptionsWidth, OptionsHeight);
        _optionsRect.anchoredPosition = new Vector2(-14f, -70f);

        Plate(_optionsRect);
        _blockers.Add(_optionsRect);

        var header = MakeRect(_optionsRect, "Header");
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(0f, OptionsHeaderHeight);
        header.anchoredPosition = Vector2.zero;
        header.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        var title = _ui.CreateLabel(header, "Dungeon Nodes", 20, TextAlignmentOptions.Left);
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
                _optionsCollapsed ? OptionsHeaderHeight : OptionsHeight);

        var label = _collapseButton != null ? _collapseButton.GetComponentInChildren<TMP_Text>() : null;
        if (label != null) label.text = _optionsCollapsed ? "+" : "-";
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
            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+S", "Quicksave this dungeon");
            _ui.CreateKeyHint(_shortcutPanel, "Esc", "Close the map");
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

    // ---- confirm strip -------------------------------------------------------------------------

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

    // ---- what the tool sets --------------------------------------------------------------------

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

    public void SetIssues(List<DungeonMapBuilder.MapIssue> issues)
    {
        _issues = issues ?? [];
        RepaintMarks();
    }

    // ---- pointer ------------------------------------------------------------------------------

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

    public CTDungeonMapNode HitNode(Vector2 point)
    {
        if (_map == null) return null;

        CTDungeonMapNode best = null;
        var bestDistance = float.MaxValue;

        foreach (var node in _map.Nodes)
        {
            if (node == null) continue;

            var radius = NodeRadius * (IsBig(node) ? BigNodeScale : 1f);
            var distance = Vector2.Distance(point, Position(node));
            if (distance <= radius && distance < bestDistance)
            {
                best = node;
                bestDistance = distance;
            }
        }

        return best;
    }

    public bool HitLink(Vector2 point, out CTDungeonMapNode owner, out string childId)
    {
        owner = null;
        childId = null;
        if (_map == null) return false;

        foreach (var node in _map.Nodes)
        {
            if (node == null) continue;

            foreach (var id in node.Children)
            {
                var child = _map.FindNode(id);
                if (child == null) continue;
                if (DistanceToSegment(point, Position(node), Position(child)) > LinkReach) continue;

                owner = node;
                childId = id;
                return true;
            }
        }

        return false;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.sqrMagnitude;
        if (lengthSquared < 0.001f) return Vector2.Distance(point, a);

        var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        return Vector2.Distance(point, a + ab * t);
    }

    public static Vector2 Position(CTDungeonMapNode node) => new(node.PosX, node.PosY);

    private static bool IsBig(CTDungeonMapNode node) =>
        node.NodeType is "Boss" or "MiniBossFloor" or "FinalBoss" or "LeaderFloor";

    // ---- drawing --------------------------------------------------------------------------------

    public void RebuildVisuals()
    {
        if (_root == null || _map == null) return;

        ClearLayer(_linkLayer);
        ClearLayer(_nodeLayer);
        NodeRects.Clear();
        _visuals.Clear();
        _links.Clear();

        var rows = DungeonMapBuilder.Rows(_map);
        _layerOf.Clear();
        for (var y = 0; y < rows.Count; y++)
            foreach (var node in rows[y])
                _layerOf[node] = y + 1;

        _startNode = rows.Count > 0 && rows[0].Count > 0 ? rows[0][0] : null;

        foreach (var node in _map.Nodes)
        {
            if (node == null) continue;

            foreach (var childId in node.Children)
            {
                var child = _map.FindNode(childId);
                if (child == null) continue;

                BuildLink(node, child);
            }
        }

        foreach (var node in _map.Nodes)
            if (node != null) BuildNode(node);

        RepaintMarks();
    }

    public void HighlightNode(CTDungeonMapNode node)
    {
        _marked = node;
        RepaintMarks();
    }

    private static readonly Color SelectedMark =
        new(MapEditorUI.Accent.r, MapEditorUI.Accent.g, MapEditorUI.Accent.b, 0.9f);

    private static readonly Color BlockingMark = new(1f, 0.42f, 0.42f, 0.95f);
    private static readonly Color AdvisoryMark = new(1f, 0.72f, 0.25f, 0.9f);

    private void RepaintMarks()
    {
        if (_map == null) return;

        ClearLayer(_markLayer);

        foreach (var issue in _issues)
        {
            if (!issue.HasNode || !_map.Nodes.Contains(issue.Node)) continue;
            BuildHalo(issue.Node, issue.IsAdvisory ? AdvisoryMark : BlockingMark);
        }

        foreach (var pair in _visuals)
        {
            if (pair.Value == null) continue;

            var isSelected = ReferenceEquals(pair.Key, _marked);
            pair.Value.SetMark(isSelected ? SelectedMark : null, isSelected);
        }
    }

    private void BuildHalo(CTDungeonMapNode node, Color colour)
    {
        var size = NodeRadius * 2f * (IsBig(node) ? BigNodeScale : 1f) * 1.1f;

        var rt = MakeRect(_markLayer, "Halo_" + node.Id);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Position(node);

        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = MapEditorUI.RoundedPlate;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f;
        image.color = new Color(colour.r, colour.g, colour.b, 0.5f);
        image.raycastTarget = false;
    }

    private void BuildNode(CTDungeonMapNode node)
    {
        var rt = MakeRect(_nodeLayer, "Node_" + node.Id);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(NodeRadius * 2f, NodeRadius * 2f);
        rt.anchoredPosition = Position(node);

        NodeRects[node] = rt;

        var big = IsBig(node);
        var icon = DungeonMapBuilder.IconFor(node.NodeType);

        if (!BuildVanillaArt(node, rt, big, icon)) BuildOwnArt(node, rt, big, icon);

        BuildCaption(node, rt, big);
    }

    private void BuildCaption(CTDungeonMapNode node, RectTransform parent, bool big)
    {
        var bound = !string.IsNullOrEmpty(node.Level);
        var layer = _layerOf.TryGetValue(node, out var index) ? $" (L{index})" : "";

        var caption = _ui.CreateLabel(parent,
            $"{node.NodeType}{layer} - " + (bound ? node.Level : "vanilla floor"),
            15, TextAlignmentOptions.Center);

        var captionRect = caption.GetComponent<RectTransform>();
        captionRect.anchorMin = captionRect.anchorMax = new Vector2(0.5f, 0.5f);
        captionRect.pivot = new Vector2(0.5f, 1f);
        captionRect.sizeDelta = new Vector2(230f, 20f);
        captionRect.anchoredPosition = new Vector2(0f, -(NodeRadius * (big ? BigNodeScale : 1f) + 4f));

        var captionText = caption.GetComponent<TMP_Text>();
        captionText.enableWordWrapping = false;
        captionText.raycastTarget = false;

        captionText.color = bound
            ? new Color(0.004f, 0.835f, 0.635f)
            : new Color(1f, 1f, 1f, 0.62f);
    }

    private bool BuildVanillaArt(CTDungeonMapNode node, RectTransform parent, bool big, Sprite icon)
    {
        var clone = DungeonMapSkin.CloneNode();
        if (clone == null) return false;

        var visual = DungeonNodeVisual.Attach(clone);
        if (visual == null)
        {
            UnityEngine.Object.DestroyImmediate(clone);
            return false;
        }

        clone.transform.SetParent(parent, false);
        clone.SetActive(true);

        var cloneRt = clone.GetComponent<RectTransform>();
        if (cloneRt != null)
        {
            cloneRt.anchorMin = cloneRt.anchorMax = new Vector2(0.5f, 0.5f);
            cloneRt.anchoredPosition = Vector2.zero;
        }

        var scale = NodeScale * (big ? BigNodeScale : 1f);
        clone.transform.localScale = new Vector3(scale, scale, 1f);

        visual.SetIcon(icon);
        visual.ApplyEditView();

        if (ReferenceEquals(_startNode, node)) visual.ShowStartingPin();

        _visuals[node] = visual;
        return true;
    }

    private void BuildOwnArt(CTDungeonMapNode node, RectTransform parent, bool big, Sprite icon)
    {
        var size = NodeRadius * 1.6f * (big ? BigNodeScale : 1f);

        if (icon != null)
        {
            var iconRt = MakeRect(parent, "Icon");
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(size, size);

            var image = iconRt.gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return;
        }

        var plateRt = MakeRect(parent, "Plate");
        plateRt.anchorMin = plateRt.anchorMax = new Vector2(0.5f, 0.5f);
        plateRt.sizeDelta = new Vector2(size, size);

        var plate = plateRt.gameObject.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 2.5f;
        plate.color = new Color(0.16f, 0.2f, 0.26f, 0.95f);
        plate.raycastTarget = false;

        var label = _ui.CreateLabel(plateRt, Shorten(node.NodeType), 12, TextAlignmentOptions.Center);
        Stretch(label.GetComponent<RectTransform>());
        label.GetComponent<TMP_Text>().raycastTarget = false;
    }

    private static string Shorten(string typeName) =>
        string.IsNullOrEmpty(typeName) || typeName.Length <= 9 ? typeName : typeName.Substring(0, 9);

    // ---- links ------------------------------------------------------------------------------

    private static readonly Color LinkColour = new(0.8f, 0.8f, 0.8f, 0.85f);

    private void BuildLink(CTDungeonMapNode from, CTDungeonMapNode to)
    {
        var view = BuildVanillaLink(from, to) ?? BuildPlainLink(from, to);
        if (view == null) return;

        _links.Add(view);
        AimLink(view);
    }

    private LinkView BuildVanillaLink(CTDungeonMapNode from, CTDungeonMapNode to)
    {
        if (!DungeonMapSkin.Usable || DungeonMapSkin.LineMaterial == null) return null;

        try
        {
            var rt = MakeRect(_linkLayer, $"Link_{from.Id}_{to.Id}");
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var line = rt.gameObject.AddComponent<Lamb.UI.MMUILineRenderer>();
            line.Texture = DungeonMapSkin.LineTexture;
            line.material = DungeonMapSkin.LineMaterial;
            line.Width = DungeonMapSkin.VanillaLineWidth * NodeScale;
            line.raycastTarget = false;

            line.Points =
            [
                new Lamb.UI.MMUILineRenderer.BranchPoint(Position(from)),
                new Lamb.UI.MMUILineRenderer.BranchPoint(Position(to))
            ];

            return new LinkView { Rect = rt, Line = line, From = from, To = to };
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Dungeon map: the game's line renderer would not draw a link (" +
                                  e.Message + "); using plain ones.");
            return null;
        }
    }

    private LinkView BuildPlainLink(CTDungeonMapNode from, CTDungeonMapNode to)
    {
        var rt = MakeRect(_linkLayer, $"Link_{from.Id}_{to.Id}");
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);

        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = Dash;
        image.type = Image.Type.Tiled;
        image.raycastTarget = false;

        return new LinkView { Rect = rt, Line = null, From = from, To = to };
    }

    public void RefreshLinks()
    {
        for (var i = _links.Count - 1; i >= 0; i--)
        {
            var view = _links[i];
            if (view.Rect == null)
            {
                _links.RemoveAt(i);
                continue;
            }

            AimLink(view);
        }
    }

    private void AimLink(LinkView view)
    {
        var a = Position(view.From);
        var b = Position(view.To);

        var touchesSelection = _marked != null &&
                               (ReferenceEquals(view.From, _marked) || ReferenceEquals(view.To, _marked));
        var colour = touchesSelection
            ? new Color(MapEditorUI.Accent.r, MapEditorUI.Accent.g, MapEditorUI.Accent.b, 0.95f)
            : LinkColour;

        if (view.Line != null)
        {
            try
            {
                view.Line.Points =
                [
                    new Lamb.UI.MMUILineRenderer.BranchPoint(a),
                    new Lamb.UI.MMUILineRenderer.BranchPoint(b)
                ];
                view.Line.Color = colour;
            }
            catch (Exception)
            {
                view.Line = null;
            }

            return;
        }

        var delta = b - a;
        view.Rect.sizeDelta = new Vector2(delta.magnitude, 5f);
        view.Rect.anchoredPosition = a;
        view.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

        var image = view.Rect.GetComponent<Image>();
        if (image != null) image.color = colour;
    }

    private static Sprite _dash;

    private static Sprite Dash
    {
        get
        {
            if (_dash != null) return _dash;

            var texture = new Texture2D(12, 4, TextureFormat.RGBA32, false)
            {
                name = "CultTweaker_DungeonDash",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };

            var pixels = new Color32[12 * 4];
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 12; x++)
                    pixels[y * 12 + x] = x < 8 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);

            texture.SetPixels32(pixels);
            texture.Apply();

            _dash = Sprite.Create(texture, new Rect(0f, 0f, 12f, 4f), new Vector2(0.5f, 0.5f), 1f, 0,
                SpriteMeshType.FullRect);
            _dash.name = "CultTweaker_DungeonDash";
            _dash.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _dash;
        }
    }

    // ---- scaffolding ------------------------------------------------------------------------------

    public Vector2 ClampToMap(Vector2 point)
    {
        var canvas = _ui.CanvasRoot;
        var size = canvas != null ? canvas.rect.size : new Vector2(1920f, 1080f);

        var halfWidth = size.x * 0.5f - 60f;
        var halfHeight = size.y * 0.5f - 60f;

        return new Vector2(Mathf.Clamp(point.x, -halfWidth, halfWidth),
            Mathf.Clamp(point.y, -halfHeight, halfHeight));
    }

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
