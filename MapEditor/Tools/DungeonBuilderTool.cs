using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// Authors a dungeon as the game's adventure-map node graph, laid out by hand on a map screen.
//
// The gestures are the world editor's, because it is the same job: ctrl+click places a node,
// left-click selects and drags it, right-click links the selection to a node or cuts a line. The
// game addresses nodes by an integer grid, but that grid is derived from the layout on the way in
// (DungeonMapBuilder.Layout) rather than being the thing that is authored - nodes at the same
// height are a row, and their left-to-right order is the row's order.
public class DungeonBuilderTool : IMapEditorTool, IMapEditorShortcuts, IMapEditorScreenTool,
    IMapEditorEscapeHandler
{
    public string Name => "Dungeon Builder";

    private readonly RuntimeMapEditor _editor;
    private readonly List<GameObject> _dynamic = [];

    private RectTransform _panel;
    private MapEditorUI _ui;

    private CTDungeonMap _map;
    private CTDungeonMapNode _selected;
    // Resolved against the scene's config when the screen opens; this is only what to ask for.
    private string _pendingType = "DungeonFloor";

    private DungeonMapCanvas _canvas;

    public DungeonBuilderTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    // IMapEditorScreenTool: while the map is up it is the screen, so the room editor's own
    // furniture stands down and its camera keys stop meaning anything.
    public bool OwnsScreen => _canvas != null && _canvas.IsOpen;

    public void ScreenQuickSave() => QuickSave();

    public bool ScreenStepBack() => RequestClose();

    // A widget under the cursor; it borrows the bar and gives it back on the way out.
    public void ScreenHoverStatus(string message)
    {
        if (string.IsNullOrEmpty(message)) _canvas?.SetHint(_message, _severity);
        else _canvas?.SetHint(message);
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

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("Left click", "Select node / drag"),
        ("Ctrl+Left", "Add node here"),
        ("Right click", "Link selection to node"),
        ("Right click", "On a link: remove it"),
        ("Del", "Delete selected node")
    ];

    // ---- side panel ---------------------------------------------------------------------------

    private void Rebuild()
    {
        foreach (var go in _dynamic)
            if (go != null) UnityEngine.Object.Destroy(go);
        _dynamic.Clear();

        if (_panel == null || _ui == null) return;

        BuildChooser();
    }

    // The dock panel is only the question of which dungeon; everything that edits one lives on the
    // map screen, which is where picking a dungeon goes straight to.
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

        var dropdown = _ui.CreateDropdown(_panel, "Open Existing Dungeon", labels, (index, _) =>
        {
            if (index < 0 || index >= maps.Count) return;

            _map = maps[index];
            _selected = null;
            OpenOverlay();
            Hint($"Editing '{_map.MapName}'. " + DefaultHint);
        });
        _dynamic.Add(dropdown.Root);
    }

    private string Summary()
    {
        var bound = 0;
        foreach (var node in _map.Nodes)
            if (node != null && !string.IsNullOrEmpty(node.Level)) bound++;

        var rows = DungeonMapBuilder.Rows(_map).Count;
        var start = _map.StartNode();

        return $"{_map.Nodes.Count} node(s) on {rows} layer(s)\n" +
               $"{bound} playing a level\n" +
               "Starts on: " + (start == null
                   ? "nothing"
                   : string.IsNullOrEmpty(start.Level) ? $"a vanilla {start.NodeType} floor" : $"'{start.Level}'");
    }

    private void CreateNew()
    {
        _map = new CTDungeonMap { MapName = FreeName() };
        _selected = null;
        OpenOverlay();
        Hint($"Created '{_map.MapName}'. Ctrl+click the map to place the first node.");
    }

    private static string FreeName()
    {
        for (var i = 1; i < 1000; i++)
        {
            var candidate = "untitleddungeon" + i;
            if (!CTDungeonMapSerialization.Available(candidate)) return candidate;
        }
        return "untitleddungeon";
    }

    // ---- saving and entering ----------------------------------------------------------------

    private void SaveMap()
    {
        if (!Playable()) return;

        MapNamePrompt.Show(_editor, _map.MapName, "NAME THIS DUNGEON", name =>
        {
            _map.MapName = MapEditorSerialization.Sanitize(
                string.IsNullOrWhiteSpace(name) ? _map.MapName : name);
            Write();
        }, existsCheck: CTDungeonMapSerialization.Exists, existsNoun: "dungeon");
    }

    // Ctrl+S: the name is already chosen, so this is the same save without the dialog.
    private void QuickSave()
    {
        if (_map == null || !Playable()) return;
        Write();
    }

    // Saving a map that cannot play is worse than not saving it: every saved file registers a
    // dungeon at startup, so a broken one becomes a dungeon that crashes the map screen when it is
    // picked. The badge has been saying what is wrong the whole time.
    private bool Playable()
    {
        var problem = DungeonMapBuilder.Validate(_map);
        if (problem == null) return true;

        Report("Not saved - " + problem, StatusSeverity.Error);
        return false;
    }

    private bool Write()
    {
        var path = CTDungeonMapSerialization.Save(_map);

        if (path == null)
        {
            Report("Dungeon save failed, see log.", StatusSeverity.Error);
            return false;
        }

        // Registration is what makes the dungeon enterable; already-registered maps just take the
        // new graph.
        CTMapDungeon.RegisterAll();
        _savedJson = CTDungeonMapSerialization.ToJson(_map);
        Rebuild();
        RefreshChrome();

        var advisory = DungeonMapBuilder.Advisory(_map);

        Report(advisory != null ? $"Saved '{_map.MapName}'. " + advisory : $"Saved '{_map.MapName}'.",
            advisory == null ? StatusSeverity.Success : StatusSeverity.Warning);
        return true;
    }

    private void EnterDungeon()
    {
        var problem = DungeonMapBuilder.Validate(_map);
        if (problem != null)
        {
            Report(problem, StatusSeverity.Error);
            return;
        }

        var name = _map.MapName;
        var registered = CTMapDungeon.Find(name);
        if (registered == null)
        {
            Report("Save the dungeon first - entering runs the registered copy.", StatusSeverity.Warning);
            return;
        }

        CloseOverlay();

        // Scene change destroys the editor host; close first (same hand-off as Play Level).
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

        _editor.SetStatus($"Entering '{name}'.");
    }

    // ---- the map screen ---------------------------------------------------------------------

    private readonly List<string> _levelNames = [];
    private readonly List<string> _typeNames = [];

    private void OpenOverlay()
    {
        TeardownCanvas();
        if (_map == null || _ui == null) return;

        _savedJson = CTDungeonMapSerialization.Exists(_map.MapName)
            ? CTDungeonMapSerialization.ToJson(_map)
            : null;

        // "MinorEnemy" is only a default, and a config that has no blueprint for it would let a
        // whole map be built out of a type this dungeon cannot draw - which shows up as a Preview
        // that refuses while entering the dungeon works, because the two read different configs.
        var types = TypeNames();
        if (types.Count > 0 && !types.Contains(_pendingType)) _pendingType = types[0];

        _canvas = new DungeonMapCanvas(_editor, _ui) { CloseRequested = () => RequestClose() };

        _canvas.Open(_map,
        [
            new DungeonMapCanvas.DockItem("Preview", "Preview", "Preview - the map as the game draws it",
                PreviewMap),
            new DungeonMapCanvas.DockItem("Save", "Save", "Save - write this dungeon and register it",
                SaveMap),
            new DungeonMapCanvas.DockItem("Enter", "Play Level", "Enter - play the dungeon from its first floor",
                EnterDungeon)
        ], Shortcuts);

        BuildOptionsColumn();
        RefreshChrome();
        Hint(DefaultHint);
    }

    private const string DefaultHint =
        "Ctrl+click to place a node, left-click to select or drag it, right-click a node to link it " +
        "to the selection.";

    // Closing the screen closes the dungeon: the dock panel is the list of dungeons and nothing
    // else, so there is no half-open state for it to show.
    // One way out, whether it was pressed, clicked or F4'd: dismiss the prompt, else ask about
    // unsaved work, else close. Esc and the X share it so the corner button is never a second,
    // different door - the world map's arrangement.
    public bool RequestClose()
    {
        if (_canvas == null || !_canvas.IsOpen) return false;

        if (_canvas.ConfirmOpen)
        {
            _canvas.HideConfirm();
            return true;
        }

        if (!HasUnsavedEdits)
        {
            CloseOverlay();
            return true;
        }

        _canvas.ShowConfirm($"Save changes to '{_map.MapName}' before closing?",
            () =>
            {
                // Save refuses an unplayable map, and closing anyway would be the discard the
                // author did not ask for; the strip is gone, so the bar says why nothing happened.
                if (!Playable() || !Write()) return;
                CloseOverlay();
            },
            confirmLabel: "Save & close", altLabel: "Discard", onAlt: CloseOverlay);

        return true;
    }

    // Compared against what was last written rather than tracked with a dirty flag: a widget that
    // forgot to raise the flag would lose the edit silently. An empty map counts as nothing to
    // save - a new dungeon closed straight away has nothing to lose, and it could not be saved
    // anyway.
    private bool HasUnsavedEdits =>
        _map != null && _map.Nodes.Count > 0 &&
        !string.Equals(CTDungeonMapSerialization.ToJson(_map), _savedJson, StringComparison.Ordinal);

    private void TeardownCanvas()
    {
        ClosePreview();
        _canvas?.Close();
        _canvas = null;
        _dragging = false;
    }

    public void CloseOverlay()
    {
        var open = _map;
        var unsaved = HasUnsavedEdits;

        TeardownCanvas();
        _map = null;
        _selected = null;
        _savedJson = null;
        Rebuild();

        if (open == null) return;

        _editor.SetStatus(unsaved
                ? $"Closed '{open.MapName}' with changes that were never saved."
                : $"Closed '{open.MapName}'.",
            unsaved ? StatusSeverity.Warning : StatusSeverity.Info);
    }

    // The map as it last stood on disk; empty for one that has never been written.
    private string _savedJson;

    // Rebuilt on selection change; widgets hold their initial values otherwise.
    private void BuildOptionsColumn()
    {
        var column = _canvas?.OptionsContent;
        if (column == null) return;

        _ui.CloseTransientUi();
        for (var i = column.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(column.GetChild(i).gameObject);

        _ui.CreateHeader(column, "- Nodes -", 22);
        Note(column, Summary());

        BuildNodePicker(column);

        if (_selected == null)
        {
            Note(column, _map.Nodes.Count == 0
                ? "No nodes yet - ctrl+click the map to add one."
                : "Nothing selected. Left-click a node, or ctrl+click the map to add one.");
            BuildTypeChooser(column, forPending: true);
            return;
        }

        _ui.CreateHeader(column, "- Selected -", 20);

        BuildTypeChooser(column, forPending: false);

        Label(column, "Plays level");

        _levelNames.Clear();
        _levelNames.Add("");
        var levelLabels = new List<string> { "Vanilla floor" };
        foreach (var level in CTLevelSerialization.LoadAll())
        {
            // A hub shares the level file format but is not a floor: it is one town room entered
            // from the world map, with no doors and nowhere for a run to continue to.
            if (level.IsHub) continue;

            _levelNames.Add(level.LevelName);
            levelLabels.Add($"{level.LevelName} ({level.Rooms.Count} rooms)");
        }

        var levelPicker = _ui.CreateDropdown(column, "Plays level", levelLabels, (index, _) =>
        {
            if (index < 0 || index >= _levelNames.Count || _selected == null) return;
            RebindLevel(_selected, _levelNames[index]);
        });
        levelPicker.SetSelected(_levelNames.IndexOf(_selected.Level ?? ""));
        Note(column, string.IsNullOrEmpty(_selected.Level)
            ? "The game generates this floor."
            : $"Plays '{_selected.Level}'.");

        var incoming = IncomingCount(_selected);
        Note(column, $"{incoming} link(s) in, {_selected.Children.Count} out.");

        _ui.CreateButton(column, "Clear Links", ClearLinks);
    }

    private void BuildTypeChooser(RectTransform column, bool forPending)
    {
        Label(column, forPending ? "Type for new nodes" : "Node type");

        _typeNames.Clear();
        _typeNames.AddRange(TypeNames());

        var picker = _ui.CreateDropdown(column, "Node type", _typeNames, (index, value) =>
        {
            if (index < 0 || index >= _typeNames.Count) return;

            _pendingType = _typeNames[index];

            // With a selection: retype it; otherwise set the type the next placed node gets.
            if (_selected != null) RetypeNode(_selected, _pendingType, value);
            else Hint($"New nodes will be {value}.");
        });
        picker.SetSelected(_typeNames.IndexOf(_selected != null ? _selected.NodeType : _pendingType));
    }

    // Selecting from a list as well as from the map: nodes overlap, and one under another is
    // otherwise unreachable. Listed by where they stand and what they are, since a dungeon node
    // has no name - the game's own map does not give it one.
    private void BuildNodePicker(RectTransform column)
    {
        if (_map.Nodes.Count == 0) return;

        var nodes = new List<CTDungeonMapNode>();
        var labels = new List<string>();

        var rows = DungeonMapBuilder.Rows(_map);
        for (var y = rows.Count - 1; y >= 0; y--)
            foreach (var node in rows[y])
            {
                nodes.Add(node);
                labels.Add($"L{y + 1}  {node.NodeType}" +
                           (string.IsNullOrEmpty(node.Level) ? "" : $" ({node.Level})"));
            }

        var picker = _ui.CreateDropdown(column, "Select node", labels, (index, _) =>
        {
            if (index < 0 || index >= nodes.Count) return;

            // The same frame's click would otherwise reach the map underneath the list.
            _editor.BlockWorldClicks();
            Select(nodes[index]);
            BuildOptionsColumn();
        });

        var current = _selected != null ? nodes.IndexOf(_selected) : -1;
        if (current >= 0) picker.SetSelected(current);
    }

    // Everything a mutation has to put right: the drawing, both panels, and the verdict.
    private void RefreshAfterMutation()
    {
        _canvas?.HighlightNode(_selected);
        _canvas?.RebuildVisuals();
        BuildOptionsColumn();
        Rebuild();
        RefreshChrome();
    }

    private void RefreshChrome()
    {
        if (_canvas == null || _map == null) return;

        _canvas.SetTitle(_map.MapName);

        var issues = DungeonMapBuilder.Issues(_map);
        _canvas.SetIssues(issues);

        foreach (var issue in issues)
        {
            if (issue.IsAdvisory) continue;
            _canvas.SetBadge(issue.Message, new Color(1f, 0.45f, 0.45f));
            return;
        }

        foreach (var issue in issues)
        {
            if (!issue.IsAdvisory) continue;
            _canvas.SetBadge(issue.Message, new Color(1f, 0.75f, 0.3f));
            return;
        }

        _canvas.SetBadge("Playable", new Color(0.35f, 0.95f, 0.55f));
    }

    // ---- input ------------------------------------------------------------------------------

    private bool _dragging;
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPosition;

    private CTDungeonMapNode _hovered;
    private bool _hoverKnown;

    // Escape steps back through this tool's own layers before the host treats it as "close". The
    // host now reads the key before any tool update runs, so the answers live here instead of being
    // polled below - only the preview needs saying, since the dropdown is closed by the host and
    // the prompt and the screen are both handled by RequestClose through ScreenStepBack.
    public bool HandleEscape()
    {
        if (_preview == null) return false;

        ClosePreview();
        return true;
    }

    public void OnUpdate()
    {
        // The preview owns the screen while it is up; HandleEscape is the way back out of it.
        if (_preview != null) return;

        if (_canvas == null || !_canvas.IsOpen || !_canvas.Visible || _map == null) return;

        // A drag whose release was never seen - the name dialog stops this update for a few frames
        // - would otherwise resume against a stale anchor and fling the node at the next press.
        // The node has already been moved by then, so the move is finished here rather than
        // dropped: abandoning it left the node where the cursor put it with its links still drawn
        // to where it used to be, and nothing on the undo stack to take it back.
        if (_dragging && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            if (_selected != null) CommitMove(_selected);
        }

        // An open dropdown list owns every click and key while it is up, and it closes itself: its
        // own full-screen catcher does that on the way out. Closing it from here instead broke the
        // widget, because a list item's button fires on mouse *up* and this fires on mouse down -
        // so the list was already destroyed by the time the click had anywhere to land.
        if (_ui.TransientUiOpen) return;

        // The prompt owns the screen while it is up; its own buttons, or Escape through
        // RequestClose, are the way out of it.
        if (_canvas.ConfirmOpen) return;

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            DeleteSelected();
            return;
        }

        // Right click links the selection to whatever was clicked, or cuts a link under the cursor
        // when the click lands on empty map. Polled, not EventSystem: Rewired drops right clicks.
        if (Input.GetMouseButtonDown(1) && !_canvas.PointerOverChrome())
        {
            var target = _canvas.PointerContent();
            var clicked = _canvas.HitNode(target);

            if (clicked != null) ToggleLink(_selected, clicked);
            else TryRemoveLinkAt(target);
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (!_canvas.PointerOverChrome())
            {
                var point = _canvas.PointerContent();

                if (RuntimeMapEditor.CtrlHeld)
                {
                    PlaceNode(point);
                    return;
                }

                var hit = _canvas.HitNode(point);
                if (hit != null)
                {
                    if (!ReferenceEquals(hit, _selected))
                    {
                        Select(hit);
                        BuildOptionsColumn();
                    }

                    _dragging = true;
                    _dragStartPointer = point;
                    _dragStartPosition = DungeonMapCanvas.Position(hit);
                }
                else if (_selected != null)
                {
                    Select(null);
                    BuildOptionsColumn();
                }
            }
        }

        var dragged = _selected;
        if (_dragging && dragged != null && Input.GetMouseButton(0))
        {
            var target = _canvas.ClampToMap(
                _dragStartPosition + (_canvas.PointerContent() - _dragStartPointer));

            dragged.PosX = target.x;
            dragged.PosY = target.y;

            // Moved by reference, not by redrawing: a redraw per frame would destroy the very rect
            // being dragged.
            if (_canvas.NodeRects.TryGetValue(dragged, out var rect) && rect != null)
                rect.anchoredPosition = target;

            // The lines are re-aimed rather than rebuilt, so they stay attached to the node while
            // it is being dragged instead of snapping to it on release.
            _canvas.RefreshLinks();
        }

        if (_dragging && Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            if (dragged != null) CommitMove(dragged);
        }

        HandleHover();
    }

    private void CommitMove(CTDungeonMapNode node)
    {
        var moved = DungeonMapCanvas.Position(node);
        if (Vector2.Distance(moved, _dragStartPosition) > 0.5f)
        {
            var map = _map;
            var restore = _dragStartPosition;

            PushUndo("move a node", () =>
            {
                if (_map != map) return false;
                node.PosX = restore.x;
                node.PosY = restore.y;
                return true;
            });
        }

        // The lines only follow their nodes on a redraw, and the rows the game will see are
        // re-derived from where everything now stands.
        RefreshAfterMutation();
    }

    private void HandleHover()
    {
        if (_dragging) return;

        var node = HoverTarget();

        // Only on a change: this runs every frame and the line is a text mesh rebuild.
        if (_hoverKnown && ReferenceEquals(node, _hovered)) return;

        _hovered = node;
        _hoverKnown = true;

        // Off a node the bar goes back to whatever was last said, not to a blank.
        _canvas?.SetHint(node == null ? _message : Describe(node),
            node == null ? _severity : StatusSeverity.Info);
    }

    private CTDungeonMapNode HoverTarget() =>
        _canvas == null || _canvas.PointerOverChrome()
            ? null
            : _canvas.HitNode(_canvas.PointerContent());

    private string Describe(CTDungeonMapNode node) =>
        $"{node.NodeType} (L{LayerOf(node)}) - " +
        (string.IsNullOrEmpty(node.Level) ? "vanilla floor" : $"plays '{node.Level}'") +
        $" - {IncomingCount(node)} in / {node.Children.Count} out";

    private int IncomingCount(CTDungeonMapNode node)
    {
        var count = 0;
        foreach (var other in _map.Nodes)
            if (other != null && other != node && other.LinksTo(node.Id)) count++;

        return count;
    }

    // ---- editing ------------------------------------------------------------------------------

    private void Select(CTDungeonMapNode node)
    {
        _selected = node;
        _canvas?.HighlightNode(node);

        // The link lines are drawn in the selection's colour, so the redraw follows the mark.
        _canvas?.RebuildVisuals();
    }

    private void PlaceNode(Vector2 point)
    {
        var map = _map;
        var placed = new CTDungeonMapNode
        {
            Id = MintId(map),
            NodeType = _pendingType
        };

        var at = _canvas.ClampToMap(point);
        placed.PosX = at.x;
        placed.PosY = at.y;

        map.Nodes.Add(placed);

        // Placing with something selected links the two immediately - a path in one gesture per
        // step, which is how a run is drawn.
        var linkedFrom = _selected;
        var linked = linkedFrom != null;
        if (linked) linkedFrom.Children.Add(placed.Id);

        _selected = placed;

        PushUndo("place a " + placed.NodeType, () =>
        {
            if (_map != map) return false;

            map.Nodes.Remove(placed);
            if (linked && linkedFrom != null) linkedFrom.Children.RemoveAll(id => id == placed.Id);
            if (_selected == placed) _selected = null;
            return true;
        });

        Hint(linked
            ? $"Placed a {placed.NodeType} and linked the selection to it."
            : $"Placed a {placed.NodeType}. Select a node, then right-click another to link them.");

        RefreshAfterMutation();
    }

    private string MintId(CTDungeonMap map)
    {
        for (var i = 1; i < 10000; i++)
        {
            var candidate = "node" + i;
            if (map.FindNode(candidate) == null) return candidate;
        }
        return "node" + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    // Where a node stands, for the status line. Ids exist only so links have something to name;
    // the game's own map has no node names, so nothing on screen shows one.
    private string Where(CTDungeonMapNode node)
    {
        if (node == null || _map == null) return "that node";

        var layer = LayerOf(node);
        return layer > 0 ? $"the {node.NodeType} on layer {layer}" : "the " + node.NodeType;
    }

    // Which layer a node resolved onto, 1-based; 0 when it is not on the map. This is derived from
    // where the node was dropped, so it is worth saying out loud - it is the one thing about a node
    // that is not visible in the node itself.
    private int LayerOf(CTDungeonMapNode node)
    {
        if (node == null || _map == null) return 0;

        var rows = DungeonMapBuilder.Rows(_map);
        for (var y = 0; y < rows.Count; y++)
            if (rows[y].Contains(node)) return y + 1;

        return 0;
    }

    // Right click is the link gesture, and one gesture both makes and breaks a link.
    private void ToggleLink(CTDungeonMapNode source, CTDungeonMapNode target)
    {
        if (target == null) return;

        if (source == null)
        {
            Hint("Left-click a node to select it, then right-click the one it leads to.");
            return;
        }

        if (source == target)
        {
            Hint("A node cannot link to itself.");
            return;
        }

        var map = _map;
        var index = source.Children.FindIndex(id =>
            string.Equals(id, target.Id, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            var removed = source.Children[index];
            source.Children.RemoveAt(index);

            PushUndo("unlink", () =>
            {
                if (_map != map) return false;
                source.Children.Insert(Mathf.Clamp(index, 0, source.Children.Count), removed);
                return true;
            });

            Hint($"Removed the link to {Where(target)}.");
        }
        else
        {
            source.Children.Add(target.Id);

            PushUndo("link", () =>
            {
                if (_map != map) return false;
                source.Children.RemoveAll(id =>
                    string.Equals(id, target.Id, StringComparison.OrdinalIgnoreCase));
                return true;
            });

            Hint($"Linked the selection to {Where(target)}.");
        }

        RefreshAfterMutation();
    }

    private void TryRemoveLinkAt(Vector2 point)
    {
        if (!_canvas.HitLink(point, out var owner, out var childId)) return;

        var map = _map;
        var index = owner.Children.IndexOf(childId);
        if (index < 0) return;

        owner.Children.RemoveAt(index);

        PushUndo("cut a link", () =>
        {
            if (_map != map) return false;
            owner.Children.Insert(Mathf.Clamp(index, 0, owner.Children.Count), childId);
            return true;
        });

        Hint($"Cut the link from {Where(owner)}.");
        RefreshAfterMutation();
    }

    private void ClearLinks()
    {
        if (_selected == null)
        {
            Hint("Select a node first.");
            return;
        }

        var map = _map;
        var node = _selected;
        var own = new List<string>(node.Children);
        var incoming = IncomingRefs(node);

        node.Children.Clear();
        foreach (var entry in incoming) entry.Owner.Children.Remove(node.Id);

        PushUndo("clear links", () =>
        {
            if (_map != map) return false;

            node.Children.Clear();
            node.Children.AddRange(own);
            foreach (var entry in incoming)
                entry.Owner.Children.Insert(Mathf.Clamp(entry.Index, 0, entry.Owner.Children.Count),
                    node.Id);
            return true;
        });

        Hint($"Cleared the links on {Where(node)}.");
        RefreshAfterMutation();
    }

    private void DeleteSelected()
    {
        if (_selected == null)
        {
            Hint("Select a node first.");
            return;
        }

        var map = _map;
        var node = _selected;
        var index = map.Nodes.IndexOf(node);
        var incoming = IncomingRefs(node);

        map.Nodes.Remove(node);
        foreach (var entry in incoming) entry.Owner.Children.Remove(node.Id);

        _selected = null;

        PushUndo("delete a " + node.NodeType, () =>
        {
            if (_map != map) return false;

            map.Nodes.Insert(Mathf.Clamp(index, 0, map.Nodes.Count), node);
            foreach (var entry in incoming)
                entry.Owner.Children.Insert(Mathf.Clamp(entry.Index, 0, entry.Owner.Children.Count),
                    node.Id);
            return true;
        });

        Hint($"Deleted the {node.NodeType}. Ctrl+Z puts it back.");
        RefreshAfterMutation();
    }

    // Every link that names this node, with the index it sits at - what an undo needs to put a
    // deleted node back exactly as it was linked.
    private List<(CTDungeonMapNode Owner, int Index)> IncomingRefs(CTDungeonMapNode node)
    {
        var refs = new List<(CTDungeonMapNode, int)>();

        foreach (var other in _map.Nodes)
        {
            if (other == null || other == node) continue;

            for (var i = 0; i < other.Children.Count; i++)
                if (string.Equals(other.Children[i], node.Id, StringComparison.OrdinalIgnoreCase))
                    refs.Add((other, i));
        }

        return refs;
    }

    private void RetypeNode(CTDungeonMapNode node, string type, string label)
    {
        var map = _map;
        var old = node.NodeType;
        if (old == type) return;

        node.NodeType = type;

        PushUndo("retype a node", () =>
        {
            if (_map != map) return false;
            node.NodeType = old;
            return true;
        });

        Hint($"That node is now {label}.");
        RefreshAfterMutation();
    }

    private void RebindLevel(CTDungeonMapNode node, string level)
    {
        var map = _map;
        var old = node.Level ?? "";
        if (old == level) return;

        node.Level = level;

        PushUndo("bind a level", () =>
        {
            if (_map != map) return false;
            node.Level = old;
            return true;
        });

        Hint(string.IsNullOrEmpty(level)
            ? $"{Where(node)} is back to a vanilla floor."
            : $"{Where(node)} plays '{level}'.");
        RefreshAfterMutation();
    }

    // Undo runs from the room editor's own Ctrl+Z, which fires whether or not the map is up - so
    // every entry redraws whatever is showing when it lands.
    private void PushUndo(string description, Func<bool> undo)
    {
        _editor.History.Push(description, () =>
        {
            if (!undo()) return false;

            if (_selected != null && (_map == null || !_map.Nodes.Contains(_selected))) _selected = null;
            RefreshAfterMutation();
            return true;
        });
    }

    // ---- the real thing -------------------------------------------------------------------------

    private Lamb.UI.UIAdventureMapOverlayController _preview;

    // The screen the player is actually shown at an exit door, with this dungeon in it. It cannot
    // draw a map that is still being built - its own OnShowStarted indexes the first node, takes
    // GetFirstNode() as a .First(), and skips anything unlinked - so it is offered only once the
    // map would play. It draws on the derived grid, so it shows the rows the layout resolved to
    // rather than the exact positions things were dropped at.
    private void PreviewMap()
    {
        if (_preview != null) return;

        var built = DungeonMapBuilder.Build(_map, out var problem);
        if (built == null)
        {
            Report("Cannot preview yet: " + problem, StatusSeverity.Warning);
            return;
        }

        var ui = MonoSingleton<Lamb.UI.UIManager>.Instance;
        if (ui == null || ui.AdventureMapOverlayTemplate == null)
        {
            Report("The game's map screen is not loaded in this scene.", StatusSeverity.Warning);
            return;
        }

        try
        {
            // Shuffling regenerates the run's map over ours, and the prompt for it is one keypress
            // away on this screen.
            if (global::Map.MapManager.Instance != null)
                global::Map.MapManager.Instance.CanShuffle = false;

            // What the game's own Instantiate extension does; called directly so this does not
            // depend on that extension staying where it is.
            _preview = UnityEngine.Object.Instantiate(ui.AdventureMapOverlayTemplate.gameObject)
                .GetComponent<Lamb.UI.UIAdventureMapOverlayController>();
            _preview.OnHide += EndPreview;

            // disableInput: nothing on this screen should be enterable - it is a look, not a run.
            _preview.Show(built, disableInput: true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: dungeon map preview failed: " + e);
            Report("The map screen would not open, see log.", StatusSeverity.Error);
            EndPreview();
            return;
        }

        _canvas?.SetVisible(false);
        _editor.SetStatus("Previewing the map as the game draws it - Esc returns to editing.");
    }

    private void ClosePreview()
    {
        if (_preview == null) return;

        try
        {
            // Immediate: the screen is about to be destroyed, and its fade-out animation would be
            // running on an object that is going away underneath it.
            _preview.Hide(immediate: true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: closing the map preview failed: " + e.Message);
        }

        EndPreview();
    }

    // Both ways out come through here: the button, and the screen closing on its own. Hide only
    // switches a menu off - this one was instantiated for a look and has to be taken away again,
    // or every preview leaves another dead map screen in the scene.
    private void EndPreview()
    {
        var preview = _preview;
        _preview = null;

        if (preview != null)
        {
            preview.OnHide -= EndPreview;
            UnityEngine.Object.Destroy(preview.gameObject);
        }

        _canvas?.SetVisible(true);
        Hint(DefaultHint);
    }

    // ---- small helpers --------------------------------------------------------------------------

    // What the editor last said, which the bar falls back to whenever the cursor is not on a node.
    private string _message = DefaultHint;
    private StatusSeverity _severity = StatusSeverity.Info;

    private void Hint(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        _message = message;
        _severity = severity;
        _canvas?.SetHint(message, severity);

        // Placing or deleting changes what is under the cursor without the cursor moving. Taking
        // that reading now means the hover only speaks up when the pointer actually moves on to
        // something else, instead of talking over the sentence just written.
        _hovered = HoverTarget();
        _hoverKnown = true;
    }

    // The map covers the editor's own status bar, so anything worth saying goes to both.
    private void Report(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        _editor.SetStatus(message, severity);
        Hint(message, severity);
    }

    private void Note(Transform parent, string text)
    {
        var label = _ui.CreateLabel(parent, text, 15);
        var tmp = label.GetComponent<TMP_Text>();
        tmp.color = new Color(1f, 1f, 1f, 0.7f);
        FitHeight(label, tmp);
    }

    // Column widgets are laid out at one line each, and a label that runs to three - the summary,
    // a long validation message - drew straight over whatever came next. One implementation of the
    // fit, shared with the room editor; this panel is only narrower.
    private const float ColumnTextWidth = 316f;

    private static void FitHeight(GameObject label, TMP_Text text) =>
        MapEditorUI.FitLabelHeight(label, ColumnTextWidth);

    // A caption above a dropdown: the widget shows its current value once one is picked, and then
    // nothing on it says what it sets.
    private void Label(Transform parent, string text)
    {
        var label = _ui.CreateLabel(parent, text, 14);
        var tmp = label.GetComponent<TMP_Text>();
        tmp.color = new Color(1f, 1f, 1f, 0.55f);
        FitHeight(label, tmp);
    }

    // Types the loaded config has blueprints for; with no MapManager, fall back to a fixed list.
    private static List<string> TypeNames()
    {
        var names = new List<string>();

        foreach (var type in DungeonMapBuilder.AvailableTypes()) names.Add(type.ToString());
        if (names.Count > 0) return names;

        foreach (var type in FallbackTypes) names.Add(type);
        return names;
    }

    // Only reached with no MapManager in the scene. MinorEnemy is deliberately not here: the
    // dungeon configs this editor runs against have no blueprint for it, so it drew nothing.
    private static readonly string[] FallbackTypes =
    [
        "DungeonFloor", "FirstFloor", "Treasure", "Store", "RestSite", "Follower", "Tarot",
        "MiniBossFloor", "Boss"
    ];
}
