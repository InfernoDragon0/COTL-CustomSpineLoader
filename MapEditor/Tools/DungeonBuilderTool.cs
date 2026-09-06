using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

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
    private string _pendingType = "DungeonFloor";

    private DungeonMapCanvas _canvas;

    public DungeonBuilderTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public bool OwnsScreen => _canvas != null && _canvas.IsOpen;

    public void ScreenQuickSave() => QuickSave();

    public bool ScreenStepBack() => RequestClose();

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

    private void QuickSave()
    {
        if (_map == null || !Playable()) return;
        Write();
    }

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
        var held = Net.EditorNet.WhyNotWorldChange();
        if (held != null)
        {
            Report(held, StatusSeverity.Error);
            return;
        }

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
                if (!Playable() || !Write()) return;
                CloseOverlay();
            },
            confirmLabel: "Save & close", altLabel: "Discard", onAlt: CloseOverlay);

        return true;
    }

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

    private string _savedJson;

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

        _ui.CreateButton(column, "Clear Links", ClearLinks, emphasis: MapEditorEmphasis.Quiet);
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

            if (_selected != null) RetypeNode(_selected, _pendingType, value);
            else Hint($"New nodes will be {value}.");
        });
        picker.SetSelected(_typeNames.IndexOf(_selected != null ? _selected.NodeType : _pendingType));
    }

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

            _editor.BlockWorldClicks();
            Select(nodes[index]);
            BuildOptionsColumn();
        });

        var current = _selected != null ? nodes.IndexOf(_selected) : -1;
        if (current >= 0) picker.SetSelected(current);
    }

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

    public bool HandleEscape()
    {
        if (_preview == null) return false;

        ClosePreview();
        return true;
    }

    public void OnUpdate()
    {
        if (_preview != null) return;

        if (_canvas == null || !_canvas.IsOpen || !_canvas.Visible || _map == null) return;

        if (_dragging && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            if (_selected != null) CommitMove(_selected);
        }

        if (_ui.TransientUiOpen) return;

        if (_canvas.ConfirmOpen) return;

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            DeleteSelected();
            return;
        }

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

            if (_canvas.NodeRects.TryGetValue(dragged, out var rect) && rect != null)
                rect.anchoredPosition = target;

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

        RefreshAfterMutation();
    }

    private void HandleHover()
    {
        if (_dragging) return;

        var node = HoverTarget();

        if (_hoverKnown && ReferenceEquals(node, _hovered)) return;

        _hovered = node;
        _hoverKnown = true;

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

    private string Where(CTDungeonMapNode node)
    {
        if (node == null || _map == null) return "that node";

        var layer = LayerOf(node);
        return layer > 0 ? $"the {node.NodeType} on layer {layer}" : "the " + node.NodeType;
    }

    private int LayerOf(CTDungeonMapNode node)
    {
        if (node == null || _map == null) return 0;

        var rows = DungeonMapBuilder.Rows(_map);
        for (var y = 0; y < rows.Count; y++)
            if (rows[y].Contains(node)) return y + 1;

        return 0;
    }

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
            if (global::Map.MapManager.Instance != null)
                global::Map.MapManager.Instance.CanShuffle = false;

            _preview = UnityEngine.Object.Instantiate(ui.AdventureMapOverlayTemplate.gameObject)
                .GetComponent<Lamb.UI.UIAdventureMapOverlayController>();
            _preview.OnHide += EndPreview;

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
            _preview.Hide(immediate: true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: closing the map preview failed: " + e.Message);
        }

        EndPreview();
    }

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

    private string _message = DefaultHint;
    private StatusSeverity _severity = StatusSeverity.Info;

    private void Hint(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        _message = message;
        _severity = severity;
        _canvas?.SetHint(message, severity);

        _hovered = HoverTarget();
        _hoverKnown = true;
    }

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

    private const float ColumnTextWidth = 316f;

    private static void FitHeight(GameObject label, TMP_Text text) =>
        MapEditorUI.FitLabelHeight(label, ColumnTextWidth);

    private void Label(Transform parent, string text)
    {
        var label = _ui.CreateLabel(parent, text, 14);
        var tmp = label.GetComponent<TMP_Text>();
        tmp.color = new Color(1f, 1f, 1f, 0.55f);
        FitHeight(label, tmp);
    }

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
        "DungeonFloor", "FirstFloor", "Treasure", "Store", "RestSite", "Follower", "Tarot",
        "MiniBossFloor", "Boss"
    ];
}
