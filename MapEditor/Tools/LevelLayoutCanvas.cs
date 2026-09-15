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

    // ---- metrics -------------------------------------------------------------------------------

    private const float OptionsWidth = Chrome.EditorSidebar.Width;
    private const float BarHeight = Chrome.EditorBottomBar.Height;
    private const float DockIconSize = 60f;

    private const float MaxPitch = 168f;
    private const float MinPitch = 46f;

    // ---- objects -------------------------------------------------------------------------------

    private GameObject _root;
    private RectTransform _contentRoot;
    private RectTransform _ghostLayer;
    private RectTransform _roomLayer;
    private RectTransform _markLayer;

    private Chrome.EditorTopBar _topBar;
    private Chrome.EditorBottomBar _bottomBar;
    private Chrome.EditorSidebar _sidebar;
    private Chrome.ShortcutCard _card;
    private MapEditorConfirm _confirm;

    private readonly List<RectTransform> _blockers = [];
    private readonly List<(string Key, string Action)> _shortcuts = [];

    public readonly Dictionary<CTLevelRoom, RectTransform> RoomRects = [];

    private CTLevelRoom _selected;
    private readonly HashSet<CTLevelRoom> _flagged = [];

    private float _pitch = 120f;
    private int _minX, _maxX, _minY, _maxY;

    // ---- open / close ---------------------------------------------------------------------------

    public void Open(CTLevelBlueprint level, List<Chrome.EditorDockItem> dock,
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

        BuildChrome(dock);

        _ui.IconPreviewRightOffset = OptionsWidth + 28f;
        _ui.IconPreviewTopOffset = Chrome.EditorTopBar.Height + 12f;

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
        _ui.IconPreviewTopOffset = MapEditorUI.DefaultIconPreviewTopOffset;

        _topBar = null;
        _bottomBar = null;
        _sidebar = null;
        _card = null;
        _confirm = null;

        _root = null;
        _contentRoot = _ghostLayer = _roomLayer = _markLayer = null;
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

    /// <summary>
    /// The screen wears the same three plates as the room editor, built from the same classes. It
    /// used to carry its own copy of all of them - title, dock, status, shortcut column, confirm
    /// strip - which is how the two drifted twelve pixels apart and how this screen ended up with a
    /// confirm strip that had quietly stopped being built at all.
    /// </summary>
    private void BuildChrome(List<Chrome.EditorDockItem> dock)
    {
        var parent = _root.transform;

        _topBar = new Chrome.EditorTopBar(_ui, parent, _blockers.Add);
        _topBar.RebuildActions(null,
        [
            ("Esc", "Close", () => CloseRequested?.Invoke())
        ]);

        _bottomBar = new Chrome.EditorBottomBar(_ui, parent, _blockers.Add);
        _bottomBar.OnHelp = () => _card?.Toggle();
        _bottomBar.SetHints(_shortcuts);

        if (dock != null)
            foreach (var item in dock)
            {
                var act = item.Do;
                _bottomBar.AddTool(item.Icon, item.Label, () => act?.Invoke(), out _, DockIconSize,
                    item.Hint);
            }

        _bottomBar.LayoutAfterDock();

        _sidebar = new Chrome.EditorSidebar(_ui, parent, _blockers.Add,
            Chrome.EditorTopBar.Height + 12f, BarHeight + 12f, optionsTitle: "Rooms",
            withLayers: false);

        OptionsContent = _ui.CreateScrollColumn(_sidebar.OptionsContent, "OptionsColumn", out _);

        _card = new Chrome.ShortcutCard(_ui, parent, _blockers.Add, Globals);

        _card.SetTool("Level layout", _shortcuts);

        _confirm = new MapEditorConfirm(_ui, parent, BarHeight + 12f);
    }

    /// True under every tool on this screen, so they live on the card rather than in the chip row.
    private static readonly (string Key, string Action)[] Globals =
    [
        ("Ctrl+Z", "Undo last change"),
        ("Ctrl+S", "Quicksave this level"),
        ("Esc", "Close the layout")
    ];

    public void LateUpdate()
    {
        _bottomBar?.Tick();
        _topBar?.Tick();
        _sidebar?.Layout(OptionsContent != null ? OptionsContent.rect.height + 12f : 0f, 0f, true);
    }

    // ---- confirm strip ---------------------------------------------------------------------------

    public bool ConfirmOpen => _confirm is { Open: true };

    public void ShowConfirm(string text, Action onConfirm, string confirmLabel = "Enter",
        string altLabel = null, Action onAlt = null)
    {
        _confirm?.Show(text, onConfirm, confirmLabel, altLabel, onAlt);
    }

    public void HideConfirm() => _confirm?.Hide();

    private static void Plate(RectTransform rect)
    {
        VanillaChrome.Dress(rect.gameObject.AddComponent<Image>());
    }

    // ---- what the tool sets ------------------------------------------------------------------------

    public void SetTitle(string text) => _topBar?.SetTitle(text);

    public void SetBadge(string text, Color colour) => _topBar?.SetNote(text, colour);

    public void SetHint(string text, StatusSeverity severity = StatusSeverity.Info) =>
        _bottomBar?.SetStatus(text, severity, pulse: true);

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
            size.x - (_sidebar is { OptionsOpen: true } ? OptionsWidth + 60f : 60f) - 40f,
            size.y - (Chrome.EditorTopBar.Height + 40f) - (BarHeight + 60f));

        var columns = Mathf.Max(1, _maxX - _minX + 1);
        var rows = Mathf.Max(1, _maxY - _minY + 1);

        _pitch = Mathf.Clamp(Mathf.Min(available.x / columns, available.y / rows), MinPitch, MaxPitch);

        if (_contentRoot != null)
            _contentRoot.anchoredPosition =
                new Vector2(_sidebar is { OptionsOpen: true } ? -OptionsWidth * 0.5f : 0f, -20f);
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
