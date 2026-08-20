using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

// Authors a dungeon as its adventure map - the node graph the game shows between rooms, where one
// node is one level blueprint. The side panel keeps the tool's usual shape (new / open / save /
// enter); the graph itself is edited on a grid overlay, because a graph is a spatial thing and a
// column of buttons is the wrong shape for it.
//
// The grid is not a simplification of a freeform canvas: the game's renderer lays every node out
// at point * 300 plus its own random jitter, so the integer cell IS the position. Authoring
// anything finer would be thrown away the first time the map opened.
public class DungeonBuilderTool : IMapEditorTool, IMapEditorShortcuts
{
    public string Name => "Dungeon Builder";

    private readonly RuntimeMapEditor _editor;
    private readonly List<GameObject> _dynamic = [];

    private RectTransform _panel;
    private MapEditorUI _ui;

    private CTDungeonMap _map;
    private string _pendingType = "MinorEnemy";

    public DungeonBuilderTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _panel = panel;
        _ui = ui;
    }

    public void OnEnter()
    {
        Rebuild();
        _editor.SetStatus(_map == null
            ? "Dungeon: create one, or open a saved dungeon."
            : $"Editing dungeon '{_map.MapName}'.");
    }

    public void OnExit() => CloseOverlay();

    public void OnUpdate() => HandleRightClick();

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Place or select a node"),
        ("RMB", "Link / unlink to the selected node"),
        ("LMB again", "Deselect")
    ];

    // Right-click is polled rather than taken from the EventSystem: this game installs Rewired's
    // pointer module, which the editor already works around for left clicks, and a link gesture
    // that silently never fires would be worse than a hit test of our own. The canvas is
    // ScreenSpaceOverlay, so a null camera is the correct argument here.
    private void HandleRightClick()
    {
        if (_overlay == null || !Input.GetMouseButtonDown(1)) return;

        var mouse = (Vector2)Input.mousePosition;
        foreach (var cell in _cellRects)
        {
            if (cell.Rect == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(cell.Rect, mouse, null)) continue;

            OnCellRightClicked(cell.X, cell.Y);
            return;
        }
    }

    // ---- side panel ---------------------------------------------------------------------------

    private void Rebuild()
    {
        foreach (var go in _dynamic)
            if (go != null) UnityEngine.Object.Destroy(go);
        _dynamic.Clear();

        if (_panel == null || _ui == null) return;

        if (_map == null) BuildChooser();
        else BuildMapEditor();
    }

    private void AddDropdown(string caption, IList<string> options, Action<int> onPicked, int selected = -1)
    {
        var dropdown = _ui.CreateDropdown(_panel, caption, options, (index, _) => onPicked(index));
        dropdown.SetSelected(selected);
        _dynamic.Add(dropdown.Root);
    }

    private void BuildChooser()
    {
        _dynamic.Add(_ui.CreateButton(_panel, "New Dungeon", CreateNew));

        var maps = CTDungeonMapSerialization.LoadAll();
        if (maps.Count == 0)
        {
            _dynamic.Add(_ui.CreateLabel(_panel, "No dungeons yet.", 14, TextAlignmentOptions.Center));
            return;
        }

        var labels = new List<string>(maps.Count);
        foreach (var map in maps) labels.Add($"{map.MapName} ({map.Nodes.Count} nodes)");

        AddDropdown("Open Existing Dungeon", labels, index =>
        {
            if (index < 0 || index >= maps.Count) return;

            _map = maps[index];
            Rebuild();
            _editor.SetStatus($"Opened dungeon '{_map.MapName}'.");
        });
    }

    private void BuildMapEditor()
    {
        _dynamic.Add(_ui.CreateLabel(_panel, "Dungeon: " + _map.MapName, 16, TextAlignmentOptions.Center));

        var bound = 0;
        foreach (var node in _map.Nodes)
            if (node != null && !string.IsNullOrEmpty(node.Level)) bound++;

        var start = _map.StartNode();
        _dynamic.Add(_ui.CreateLabel(_panel,
            $"{_map.Nodes.Count} node(s), {_map.Columns} x {_map.Layers}\n" +
            $"{bound} playing a level\n" +
            "Starts on: " + (start == null
                ? "nothing on the bottom layer"
                : string.IsNullOrEmpty(start.Level) ? "a vanilla floor" : $"'{start.Level}'"),
            14, TextAlignmentOptions.Center));

        _dynamic.Add(_ui.CreateButton(_panel, "Edit Nodes", OpenOverlay));

        // Save is also rename: the dialog opens on the current name, so saving under a different
        // one writes a second dungeon rather than needing a button of its own.
        _dynamic.Add(_ui.CreateButton(_panel, "Save Dungeon", SaveMap));
        _dynamic.Add(_ui.CreateButton(_panel, "Enter Dungeon", EnterDungeon));
        _dynamic.Add(_ui.CreateButton(_panel, "Close Dungeon", () =>
        {
            CloseOverlay();
            _map = null;
            Rebuild();
            _editor.SetStatus("Dungeon closed.");
        }));

        _dynamic.Add(_ui.CreateHeader(_panel, "Grid"));

        var sizes = new List<string>();
        for (var i = MinGrid; i <= MaxLayers; i++) sizes.Add(i + " layers");
        AddDropdown("Layers", sizes, index => Resize(_map.Columns, index + MinGrid), _map.Layers - MinGrid);

        var widths = new List<string>();
        for (var i = MinGrid; i <= MaxColumns; i++) widths.Add(i + " columns");
        AddDropdown("Columns", widths, index => Resize(index + MinGrid, _map.Layers), _map.Columns - MinGrid);
    }

    private const int MinGrid = 2;
    private const int MaxLayers = 10;
    private const int MaxColumns = 7;

    private void Resize(int columns, int layers)
    {
        _map.Columns = Mathf.Clamp(columns, MinGrid, MaxColumns);
        _map.Layers = Mathf.Clamp(layers, MinGrid, MaxLayers);

        // Nodes outside the new grid go, and so does every link that pointed at them - a link to
        // a node that is not there any more is what the renderer crashes on.
        var dropped = _map.Nodes.RemoveAll(n => n == null || n.X >= _map.Columns || n.Y >= _map.Layers);
        foreach (var node in _map.Nodes)
            node.Outgoing.RemoveAll(l => l == null || _map.NodeAt(l.X, l.Y) == null);

        if (_selected != null && !_map.Nodes.Contains(_selected)) _selected = null;

        Rebuild();
        RefreshOverlay();
        _editor.SetStatus(dropped > 0
            ? $"Grid is now {_map.Columns} x {_map.Layers}; {dropped} node(s) fell outside it."
            : $"Grid is now {_map.Columns} x {_map.Layers}.");
    }

    private void CreateNew()
    {
        _map = new CTDungeonMap { MapName = FreeName() };
        _selected = null;
        Rebuild();
        _editor.SetStatus($"Created '{_map.MapName}'. Edit Nodes to lay it out.");
    }

    private static string FreeName()
    {
        for (var i = 1; i < 1000; i++)
        {
            var candidate = "untitleddungeon" + i;
            if (!CTDungeonMapSerialization.Exists(candidate)) return candidate;
        }
        return "untitleddungeon";
    }

    // The same name dialog the map save uses, rather than the inline prompt: it is a save screen,
    // it warns about overwriting on its own - which is what the old press-twice-to-confirm was
    // for - and confirming under a different name is how a dungeon gets renamed.
    private void SaveMap()
    {
        MapNamePrompt.Show(_editor, _map.MapName, "NAME THIS DUNGEON", name =>
        {
            _map.MapName = MapEditorSerialization.Sanitize(
                string.IsNullOrWhiteSpace(name) ? _map.MapName : name);

            // Saved even when it is not playable: a half-built dungeon is worth keeping on disk,
            // and the status says what is still missing.
            var problem = DungeonMapBuilder.Validate(_map);
            var path = CTDungeonMapSerialization.Save(_map);

            if (path == null)
            {
                _editor.SetStatus("Dungeon save failed, see log.", StatusSeverity.Error);
                return;
            }

            // Saving is what makes the dungeon enterable: registration reads the folder back, and
            // one already registered keeps its minted location and just takes the new graph.
            CTMapDungeon.RegisterAll();
            Rebuild();

            // Playable but worth a word: a boss node with no level behind it is an icon making a
            // promise the vanilla floor will not keep.
            var advisory = problem == null ? DungeonMapBuilder.Advisory(_map) : null;

            _editor.SetStatus(
                problem != null ? "Saved, but not playable yet: " + problem
                : advisory != null ? "Dungeon saved. " + advisory
                : "Dungeon saved to " + path,
                problem == null && advisory == null ? StatusSeverity.Success : StatusSeverity.Warning);
        }, existsCheck: CTDungeonMapSerialization.Exists, existsNoun: "dungeon");
    }

    private void EnterDungeon()
    {
        var problem = DungeonMapBuilder.Validate(_map);
        if (problem != null)
        {
            _editor.SetStatus(problem, StatusSeverity.Error);
            return;
        }

        var registered = CTMapDungeon.Find(_map.MapName);
        if (registered == null)
        {
            _editor.SetStatus("Save the dungeon first - entering runs the registered copy.",
                StatusSeverity.Warning);
            return;
        }

        CloseOverlay();

        // The scene change destroys the editor host, so it closes first - the same hand-off Play
        // Level makes.
        _editor.ExitForPlayback();

        try
        {
            registered.EnterDungeon();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: dungeon entry failed: " + e);
            _editor.SetStatus("Dungeon entry failed, see log.", StatusSeverity.Error);
            return;
        }

        _editor.SetStatus($"Entering '{_map.MapName}'.");
    }

    // ---- grid overlay -------------------------------------------------------------------------

    private GameObject _overlay;
    private RectTransform _cellRoot;
    private RectTransform _linkRoot;
    private TMP_Text _overlayHint;
    private CTDungeonMapNode _selected;

    private MapEditorDropdown _levelDropdown;
    private readonly List<string> _levelNames = [];

    // Every cell's rect, for the right-click hit test.
    private readonly List<(RectTransform Rect, int X, int Y)> _cellRects = [];

    private const float PanelWidth = 980f;
    private const float PanelHeight = 660f;
    private const float GridInsetTop = 96f;
    private const float GridInsetBottom = 130f;

    private void OpenOverlay()
    {
        CloseOverlay();

        var canvas = _ui.CanvasRoot;
        if (canvas == null) return;

        _overlay = new GameObject("DungeonMapOverlay");
        _overlay.transform.SetParent(canvas, false);

        var overlayRt = _overlay.AddComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;

        // Absorbs clicks so laying out nodes never drops a prop into the room behind, and is
        // registered as a blocker for the same reason the dropdown overlay is.
        var catcher = _overlay.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0.65f);
        _editor.RegisterUiBlocker(overlayRt);

        var panel = new GameObject("Panel");
        panel.transform.SetParent(_overlay.transform, false);

        var panelRt = panel.AddComponent<RectTransform>();
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        var plate = panel.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.5f;
        plate.color = new Color(0.05f, 0.06f, 0.08f, 0.97f);
        _editor.RegisterUiBlocker(panelRt);

        BuildOverlayChrome(panelRt);

        // Links first so their lines sit behind the cells they join.
        _linkRoot = MakeLayer(panelRt, "Links");
        _cellRoot = MakeLayer(panelRt, "Cells");

        RefreshOverlay();
    }

    private RectTransform MakeLayer(RectTransform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;

        // Centred on the grid area rather than the panel: the header and the button row below are
        // not part of the graph, and a grid centred on the whole panel drifts under them.
        rt.anchoredPosition = new Vector2(0f, (GridInsetBottom - GridInsetTop) * 0.5f);
        return rt;
    }

    private void BuildOverlayChrome(RectTransform panel)
    {
        var title = _ui.CreateLabel(panel, "Dungeon Map - " + _map.MapName, 20, TextAlignmentOptions.Center);
        Place(title, new Vector2(0f, PanelHeight * 0.5f - 26f), new Vector2(PanelWidth - 40f, 30f));

        var hint = _ui.CreateLabel(panel,
            "Left-click a cell to place or select a node. Right-click another node to link or unlink it.",
            14, TextAlignmentOptions.Center);
        Place(hint, new Vector2(0f, PanelHeight * 0.5f - 56f), new Vector2(PanelWidth - 40f, 24f));
        _overlayHint = hint.GetComponent<TMP_Text>();

        var dropRowY = -PanelHeight * 0.5f + 84f;
        var buttonRowY = -PanelHeight * 0.5f + 32f;

        var typeNames = TypeNames();
        var typeDropdown = _ui.CreateDropdown(panel, "Node type", typeNames, (index, value) =>
        {
            if (index < 0 || index >= typeNames.Count) return;

            _pendingType = typeNames[index];

            // Picking a type with a node selected retypes it; with nothing selected it is the
            // type the next placed node gets.
            if (_selected != null)
            {
                _selected.NodeType = _pendingType;
                RefreshOverlay();
                _editor.SetStatus($"Node ({_selected.X},{_selected.Y}) is now {value}.");
            }
            else
            {
                SetHint($"New nodes will be {value}.");
            }
        });
        typeDropdown.SetSelected(typeNames.IndexOf(_pendingType));
        Place(typeDropdown.Root, new Vector2(-180f, dropRowY), new Vector2(320f, 44f));

        // Slot 0 is "no level", so a node can be handed back to the game without deleting it.
        _levelNames.Clear();
        _levelNames.Add("");
        var levelLabels = new List<string> { "Vanilla floor" };
        foreach (var level in CTLevelSerialization.LoadAll())
        {
            _levelNames.Add(level.LevelName);
            levelLabels.Add($"{level.LevelName} ({level.Rooms.Count} rooms)");
        }

        _levelDropdown = _ui.CreateDropdown(panel, "Plays level", levelLabels, (index, _) =>
        {
            if (_selected == null)
            {
                SetHint("Select a node first, then choose what it plays.");
                return;
            }

            if (index < 0 || index >= _levelNames.Count) return;

            _selected.Level = _levelNames[index];
            RefreshOverlay();
            Rebuild();
            SetHint(string.IsNullOrEmpty(_selected.Level)
                ? $"({_selected.X},{_selected.Y}) is back to a vanilla floor."
                : $"({_selected.X},{_selected.Y}) plays '{_selected.Level}'.");
        });
        Place(_levelDropdown.Root, new Vector2(180f, dropRowY), new Vector2(320f, 44f));
        SyncLevelDropdown();

        var delete = _ui.CreateButton(panel, "Delete Node", DeleteSelected);
        Place(delete, new Vector2(-200f, buttonRowY), new Vector2(200f, 40f));

        var clear = _ui.CreateButton(panel, "Clear Links", () =>
        {
            if (_selected == null)
            {
                SetHint("Select a node first.");
                return;
            }

            _selected.Outgoing.Clear();
            foreach (var node in _map.Nodes) node.Outgoing.RemoveAll(l => l.X == _selected.X && l.Y == _selected.Y);
            RefreshOverlay();
            SetHint($"Cleared the links on ({_selected.X},{_selected.Y}).");
        });
        Place(clear, new Vector2(0f, buttonRowY), new Vector2(200f, 40f));

        var done = _ui.CreateButton(panel, "Done", CloseOverlay);
        Place(done, new Vector2(200f, buttonRowY), new Vector2(200f, 40f));
    }

    // The level dropdown belongs to whichever node is selected, so its caption follows the
    // selection rather than the last thing picked.
    private void SyncLevelDropdown()
    {
        if (_levelDropdown == null) return;

        var index = _selected == null ? -1 : _levelNames.IndexOf(_selected.Level ?? "");
        _levelDropdown.SetSelected(index);
    }

    // The shared widgets build themselves for a vertical layout column; the overlay places them
    // by hand, so their rects are pinned here rather than left to a layout group that is not there.
    private static void Place(GameObject go, Vector2 position, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;

        var element = go.GetComponent<LayoutElement>();
        if (element != null) element.ignoreLayout = true;
    }

    private void SetHint(string message)
    {
        if (_overlayHint != null) _overlayHint.text = message;
    }

    public void CloseOverlay()
    {
        if (_overlay != null) UnityEngine.Object.Destroy(_overlay);
        _overlay = null;
        _cellRoot = null;
        _linkRoot = null;
        _overlayHint = null;
        _levelDropdown = null;
    }

    private float CellSize()
    {
        var width = (PanelWidth - 80f) / _map.Columns;
        var height = (PanelHeight - GridInsetTop - GridInsetBottom) / _map.Layers;
        return Mathf.Min(width, height, 110f);
    }

    // y counts upward on screen exactly as it does in the game: layer 0 is the bottom, where the
    // run starts, and the top row is the end of the run.
    private Vector2 CellPosition(int x, int y)
    {
        var cell = CellSize();
        return new Vector2((x - (_map.Columns - 1) * 0.5f) * cell, (y - (_map.Layers - 1) * 0.5f) * cell);
    }

    private void RefreshOverlay()
    {
        if (_overlay == null || _cellRoot == null || _linkRoot == null) return;

        foreach (Transform child in _cellRoot) UnityEngine.Object.Destroy(child.gameObject);
        foreach (Transform child in _linkRoot) UnityEngine.Object.Destroy(child.gameObject);
        _cellRects.Clear();

        for (var y = 0; y < _map.Layers; y++)
            for (var x = 0; x < _map.Columns; x++)
                BuildCell(x, y);

        foreach (var node in _map.Nodes)
        {
            if (node == null) continue;
            foreach (var link in node.Outgoing)
                if (link != null && _map.NodeAt(link.X, link.Y) != null) BuildLink(node, link);
        }

        SyncLevelDropdown();
    }

    private void BuildCell(int x, int y)
    {
        var node = _map.NodeAt(x, y);
        var cell = CellSize();

        var go = new GameObject($"Cell_{x}_{y}");
        go.transform.SetParent(_cellRoot, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(cell - 10f, cell - 10f);
        rt.anchoredPosition = CellPosition(x, y);

        var plate = go.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 2.5f;

        if (node == null) plate.color = new Color(1f, 1f, 1f, 0.06f);
        else if (node == _selected)
            plate.color = new Color(MapEditorUI.Accent.r, MapEditorUI.Accent.g, MapEditorUI.Accent.b, 0.85f);
        else plate.color = new Color(0.16f, 0.2f, 0.26f, 0.95f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = plate;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => OnCellClicked(x, y));

        _cellRects.Add((rt, x, y));

        if (node == null) return;

        // A bound node is marked rather than relabelled: the icon is what says which node this
        // is, and the level name never fits in a cell.
        if (!string.IsNullOrEmpty(node.Level))
        {
            var badge = new GameObject("Bound");
            badge.transform.SetParent(go.transform, false);

            var badgeRt = badge.AddComponent<RectTransform>();
            badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(1f, 0f);
            badgeRt.pivot = new Vector2(1f, 0f);
            badgeRt.sizeDelta = new Vector2(14f, 14f);
            badgeRt.anchoredPosition = new Vector2(-4f, 4f);

            var badgeImage = badge.AddComponent<Image>();
            badgeImage.sprite = MapEditorUI.RoundedPlate;
            badgeImage.type = Image.Type.Sliced;
            badgeImage.pixelsPerUnitMultiplier = 4f;
            badgeImage.color = new Color(0.35f, 1f, 0.45f, 0.95f);
            badgeImage.raycastTarget = false;
        }

        var icon = DungeonMapBuilder.IconFor(node.NodeType);
        if (icon != null)
        {
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);

            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(cell - 26f, cell - 26f);

            var image = iconGo.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        else
        {
            // No blueprint sprite (or no MapManager to read one from): the name still says what
            // the node is, which beats an empty square.
            var label = _ui.CreateLabel(go.transform, Shorten(node.NodeType), 12, TextAlignmentOptions.Center);
            Stretch(label);
            label.GetComponent<TMP_Text>().raycastTarget = false;
        }
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var element = go.GetComponent<LayoutElement>();
        if (element != null) element.ignoreLayout = true;
    }

    private static string Shorten(string typeName) =>
        string.IsNullOrEmpty(typeName) || typeName.Length <= 9 ? typeName : typeName.Substring(0, 9);

    private void BuildLink(CTDungeonMapNode from, CTDungeonMapLink link)
    {
        var a = CellPosition(from.X, from.Y);
        var b = CellPosition(link.X, link.Y);
        var delta = b - a;

        var go = new GameObject($"Link_{from.X}_{from.Y}_{link.X}_{link.Y}");
        go.transform.SetParent(_linkRoot, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(delta.magnitude, 5f);
        rt.anchoredPosition = a;
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.35f, 0.85f, 1f, 0.75f);
        image.raycastTarget = false;
    }

    // ---- editing ------------------------------------------------------------------------------

    // Left click never links. It places on an empty cell and selects on a node, so clicking a
    // second node always means "I want that one now" rather than sometimes meaning "join these".
    private void OnCellClicked(int x, int y)
    {
        var node = _map.NodeAt(x, y);

        if (node == null)
        {
            var placed = new CTDungeonMapNode { X = x, Y = y, NodeType = _pendingType };
            _map.Nodes.Add(placed);

            // Placing one layer from the selected node links the two straight away. That is
            // unambiguous now that left click cannot mean anything else, and it is what makes
            // laying out a path one click per step.
            if (_selected != null && TryLink(_selected, placed, out var message)) SetHint(message);
            else SetHint($"Placed {_pendingType} at ({x},{y}).");

            _selected = placed;
            RefreshOverlay();
            Rebuild();
            return;
        }

        if (node == _selected)
        {
            _selected = null;
            SetHint("Nothing selected.");
            RefreshOverlay();
            return;
        }

        _selected = node;
        SetHint($"Selected ({x},{y}) - {node.NodeType}" +
                (string.IsNullOrEmpty(node.Level) ? ", vanilla floor." : $", plays '{node.Level}'."));
        RefreshOverlay();
    }

    // Right click is the link gesture, and only that.
    private void OnCellRightClicked(int x, int y)
    {
        var node = _map.NodeAt(x, y);
        if (node == null)
        {
            SetHint("Right-click a node to link it; left-click an empty cell to place one.");
            return;
        }

        if (_selected == null)
        {
            SetHint("Left-click a node to select it, then right-click the one to link it to.");
            return;
        }

        if (node == _selected)
        {
            SetHint("A node cannot link to itself.");
            return;
        }

        if (!TryLink(_selected, node, out var message))
        {
            SetHint(message);
            return;
        }

        SetHint(message);
        RefreshOverlay();
    }

    // Toggles the link between two nodes. Returns false when they are not a layer apart, which
    // reads as "select this one instead" rather than an error.
    private bool TryLink(CTDungeonMapNode a, CTDungeonMapNode b, out string message)
    {
        message = null;
        if (a == null || b == null || a == b) return false;

        if (Mathf.Abs(a.Y - b.Y) != 1)
        {
            // Vanilla only ever joins neighbouring layers, and the player walks one layer per
            // move, so a longer link draws a line across the map that nothing can use.
            message = "Links only join neighbouring layers.";
            return false;
        }

        // The lower node is always the source: the run climbs, and outgoing is what the game
        // follows forward.
        var lower = a.Y < b.Y ? a : b;
        var upper = a.Y < b.Y ? b : a;

        if (lower.LinksTo(upper.X, upper.Y))
        {
            lower.Outgoing.RemoveAll(l => l.X == upper.X && l.Y == upper.Y);
            message = $"Unlinked ({lower.X},{lower.Y}) from ({upper.X},{upper.Y}).";
        }
        else
        {
            lower.Outgoing.Add(new CTDungeonMapLink { X = upper.X, Y = upper.Y });
            message = $"Linked ({lower.X},{lower.Y}) to ({upper.X},{upper.Y}).";
        }

        return true;
    }

    private void DeleteSelected()
    {
        if (_selected == null)
        {
            SetHint("Select a node first.");
            return;
        }

        var x = _selected.X;
        var y = _selected.Y;

        _map.Nodes.Remove(_selected);
        foreach (var node in _map.Nodes) node.Outgoing.RemoveAll(l => l.X == x && l.Y == y);

        _selected = null;
        RefreshOverlay();
        Rebuild();
        SetHint($"Deleted the node at ({x},{y}).");
    }

    // The types the loaded dungeon config has blueprints for. Without a MapManager (the editor
    // opened somewhere with no adventure map) the list falls back to the handful of types worth
    // authoring, so the tool still lays out a map that a dungeon scene can open later.
    private static List<string> TypeNames()
    {
        var names = new List<string>();

        foreach (var type in DungeonMapBuilder.AvailableTypes()) names.Add(type.ToString());
        if (names.Count > 0) return names;

        foreach (var type in FallbackTypes) names.Add(type);
        return names;
    }

    private static readonly string[] FallbackTypes =
    [
        "MinorEnemy", "EliteEnemy", "Treasure", "Store", "RestSite", "Follower", "Tarot",
        "FirstFloor", "DungeonFloor", "MiniBossFloor", "Boss"
    ];
}
