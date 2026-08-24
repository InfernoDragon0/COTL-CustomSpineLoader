using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.WorldMap.Tools;

// Travel nodes: place, drag, link, and configure type, unlocking and destination.
public class WorldNodeTool : IMapEditorTool, IMapEditorShortcuts
{
    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("Left click", "Select node / drag"),
        ("Ctrl+Left", "Add node here"),
        ("Right click", "Link selection to node"),
        ("Right click", "On a link: remove it"),
        ("Drag corner", "Resize the selection"),
        ("Del", "Delete selected node")
    ];

    private readonly WorldMapEditor _editor;

    private static readonly string[] NodeTypes = ["Base", "Dungeon", "MiniBoss", "Boss", "Key", "Lock", "Reward"];
    private static readonly string[] InitialStates = ["Hidden", "Preview", "Selectable"];
    // Stored values, and what the picker calls them - "None" reads as an unset field rather than
    // the deliberate choice it is.
    private static readonly string[] TargetKinds = ["None", "DungeonMap", "Level", "Hub"];
    private static readonly string[] TargetKindLabels =
        ["No destination", "Dungeon map", "Level", "Hub"];

    // Entries in the icon list that are not node types.
    private const string DefaultIconOption = "(default art for the type)";
    private const string IconPrefix = "icon: ";

    private bool _pickingRequired;
    private bool _dragging;
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPosition;

    // Corner-node resize, as in the layer tool and the room editor's select tool.
    private const float MinScale = 0.3f;
    private const float MaxScale = 3f;

    private bool _scaling;
    private Vector2 _scaleCentre;
    private float _scaleStartDistance;
    private float _scaleStartValue;

    public WorldNodeTool(WorldMapEditor editor) => _editor = editor;

    public string Name => "World Nodes";

    public void OnEnter() { }

    public void OnExit()
    {
        _pickingRequired = false;
        _dragging = false;
        _scaling = false;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        var map = _editor.Map;
        if (map == null) return;

        ui.CreateHeader(panel, "- Nodes -", 22);
        BuildNodePicker(panel, ui, map);

        var selected = SelectedNode();
        if (selected == null) return;

        ui.CreateHeader(panel, "- Selected: " + selected.Id + " -", 20);

        ui.CreateButton(panel, "Name...", () =>
            MapNamePrompt.Show(_editor,
                string.IsNullOrWhiteSpace(selected.DisplayName) ? selected.Id : selected.DisplayName,
                "Node name", chosen => RenameNode(selected, chosen), existsCheck: _ => false));

        // One list for everything that decides how the node is drawn: the seven frame types, then
        // the map folder's own pngs. Picking a png keeps the type - Key, Lock and Base still mean
        // what they mean, they just wear a different face.
        Label(ui, panel, "Icon type");

        var iconOptions = new List<string>(NodeTypes) { DefaultIconOption };
        var pngs = ListPngs(map.MapName);
        foreach (var png in pngs) iconOptions.Add(IconPrefix + png);

        var typePicker = ui.CreateDropdown(panel, "Icon type", iconOptions, (index, value) =>
        {
            if (index < NodeTypes.Length) selected.NodeType = value;
            else if (index == NodeTypes.Length) selected.Icon = "";
            else selected.Icon = pngs[index - NodeTypes.Length - 1];

            _editor.RebuildAll();
            _editor.RebuildActivePanel();
        });

        typePicker.SetSelected(string.IsNullOrEmpty(selected.Icon)
            ? Array.FindIndex(NodeTypes,
                t => string.Equals(t, selected.NodeType, StringComparison.OrdinalIgnoreCase))
            : iconOptions.IndexOf(IconPrefix + selected.Icon));

        WorldMapEditor.Hint(typePicker,
            $"Icon type - the node's frame, or one of the map folder's {pngs.Count} png(s)");
        Note(ui, panel, $"Type: {selected.NodeType}  |  icon: " +
                        (string.IsNullOrEmpty(selected.Icon) ? "default" : selected.Icon));

        Label(ui, panel, "Visibility");
        var statePicker = ui.CreateDropdown(panel, "Visibility", InitialStates, (_, value) =>
        {
            selected.InitialState = value;
        });
        statePicker.SetSelected(Array.FindIndex(InitialStates,
            s => string.Equals(s, selected.InitialState, StringComparison.OrdinalIgnoreCase)));
        WorldMapEditor.Hint(statePicker,
            "Visibility - how the node starts before anything unlocks it");
        Note(ui, panel, "The start node should be Selectable.");

        // Where the node goes belongs with what it is, not in a category of its own two headers
        // further down.
        Label(ui, panel, "Destination");
        var kindPicker = ui.CreateDropdown(panel, "Destination", TargetKindLabels, (index, _) =>
        {
            if (index < 0 || index >= TargetKinds.Length) return;

            selected.TargetKind = TargetKinds[index];
            selected.Target = "";
            _editor.RebuildActivePanel();
        });
        kindPicker.SetSelected(Array.FindIndex(TargetKinds,
            k => string.Equals(k, selected.TargetKind, StringComparison.OrdinalIgnoreCase)));
        WorldMapEditor.Hint(kindPicker,
            "Destination - what selecting this node enters; without one it completes on the spot");

        var targets = TargetsFor(selected.TargetKind);
        if (targets.Count > 0)
        {
            Label(ui, panel, "Target");
            var targetPicker = ui.CreateDropdown(panel, "Target", targets, (_, value) =>
            {
                selected.Target = value;
            });
            var current = targets.FindIndex(t =>
                string.Equals(t, selected.Target, StringComparison.OrdinalIgnoreCase));
            if (current >= 0) targetPicker.SetSelected(current);
        }
        else if (!string.Equals(selected.TargetKind, "None", StringComparison.OrdinalIgnoreCase))
        {
            // The one note worth keeping here: an empty picker otherwise reads as a broken one.
            Note(ui, panel, "Nothing saved to target yet - save a dungeon map, level or hub first.");
        }

        Note(ui, panel, $"Scale {selected.Scale:0.##} - drag the corner node to resize.");

        if (selected.IsKey)
            ui.CreateSlider(panel, "Keys granted", 0f, 5f, selected.KeysGranted,
                value => selected.KeysGranted = Mathf.RoundToInt(value));
        if (selected.IsLock)
            ui.CreateSlider(panel, "Keys to open", 0f, 5f, selected.KeysCost,
                value => selected.KeysCost = Mathf.Max(1, Mathf.RoundToInt(value)));

        ui.CreateHeader(panel, "- Unlock gate -", 20);
        ui.CreateSlider(panel, "Requires N completed", 0f, 10f, selected.RequiredCompletedCount,
            value => selected.RequiredCompletedCount = Mathf.RoundToInt(value));
        ui.CreateButton(panel, _pickingRequired ? "Done picking" : "Pick required nodes (click)", () =>
        {
            _pickingRequired = !_pickingRequired;
            _editor.SetStatus(_pickingRequired
                ? "Click nodes to add or remove them from the requirement."
                : $"Requirement list has {selected.RequiredNodes.Count} node(s).");
            _editor.RebuildActivePanel();
        });
        Note(ui, panel, $"Required now: {string.Join(", ", selected.RequiredNodes)}");

        // Deleting is the Del key; a button for it sat one slip away from the sliders above it.
        Note(ui, panel, "Del deletes this node.");
    }

    public void OnUpdate()
    {
        var map = _editor.Map;
        if (map == null) return;

        // A drag whose release was never seen - a modal dialog stops this update for a few frames -
        // would otherwise resume against a stale anchor and fling the node at the next press.
        if (_dragging && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) _dragging = false;

        if (HandleScaleDrag()) return;

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            var doomed = SelectedNode();
            if (doomed != null)
            {
                DeleteNode(doomed);
                _editor.SetStatus($"Deleted node '{doomed.Id}'. Ctrl+Z puts it back.");
            }
            else
                _editor.SetStatus("No node selected.");
            return;
        }

        // Right click links the selection to whatever was clicked, or removes a link under the
        // cursor when the click lands on empty map.
        if (Input.GetMouseButtonDown(1) && !_editor.PointerOverEditorUi())
        {
            var target = _editor.PointerContent();
            var clicked = _editor.HitNode(target);

            if (clicked != null) ToggleLink(SelectedNode(), clicked);
            else TryRemoveLinkAt(target);
            return;
        }

        if (Input.GetMouseButtonDown(0) && !_editor.PointerOverEditorUi())
        {
            var point = _editor.PointerContent();

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                PlaceNode(point);
                return;
            }

            var hit = _editor.HitNode(point);

            if (_pickingRequired)
            {
                var selected = SelectedNode();
                if (hit != null && selected != null && hit != selected)
                {
                    var already = selected.RequiredNodes.FindIndex(id =>
                        string.Equals(id, hit.Id, StringComparison.OrdinalIgnoreCase));
                    if (already >= 0) selected.RequiredNodes.RemoveAt(already);
                    else selected.RequiredNodes.Add(hit.Id);

                    MarkRequired();
                    _editor.SetStatus($"Requirement list: {string.Join(", ", selected.RequiredNodes)}");
                }
                return;
            }

            if (hit != null)
            {
                if (!string.Equals(hit.Id, _editor.SelectedNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    Select(hit.Id);
                    _editor.RebuildActivePanel();
                }

                _dragging = true;
                _dragStartPointer = point;
                _dragStartPosition = new Vector2(hit.Position.X, hit.Position.Y);
            }
            else if (!string.IsNullOrEmpty(_editor.SelectedNodeId))
            {
                Select(null);
                _editor.RebuildActivePanel();
            }
        }

        var dragged = SelectedNode();
        if (_dragging && dragged != null && Input.GetMouseButton(0))
        {
            var target = _dragStartPosition + (_editor.PointerContent() - _dragStartPointer);
            dragged.Position.X = target.x;
            dragged.Position.Y = target.y;

            if (_editor.Screen.NodeRects.TryGetValue(dragged.Id, out var rect) && rect != null)
                rect.anchoredPosition = target;
        }

        if (_dragging && Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            if (dragged != null)
            {
                var moved = new Vector2(dragged.Position.X, dragged.Position.Y);
                if (Vector2.Distance(moved, _dragStartPosition) > 0.5f)
                {
                    var node = dragged;
                    var restore = _dragStartPosition;
                    _editor.PushUndo("move node " + node.Id, () =>
                    {
                        node.Position.X = restore.x;
                        node.Position.Y = restore.y;
                        _editor.RebuildAll();
                        return true;
                    });
                }

                // The lines only follow their nodes on a redraw.
                _editor.RebuildAll();
            }
        }
    }

    // True while the corner node owns the mouse, so the caller skips selecting, linking and moving.
    private bool HandleScaleDrag()
    {
        var handle = _editor.Screen != null ? _editor.Screen.NodeHandle : null;
        var selected = SelectedNode();

        if (!_scaling)
        {
            if (handle == null || selected == null) return false;
            if (!Input.GetMouseButtonDown(0)) return false;
            if (!handle.ContainsPointer(Input.mousePosition)) return false;

            _scaling = true;
            _scaleCentre = handle.ScreenCentre;
            _scaleStartDistance = Mathf.Max(8f, Vector2.Distance(Input.mousePosition, _scaleCentre));
            _scaleStartValue = selected.Scale;
            return true;
        }

        if (selected == null || (!Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)))
        {
            _scaling = false;
            return false;
        }

        var distance = Vector2.Distance(Input.mousePosition, _scaleCentre);
        var value = Mathf.Clamp(_scaleStartValue * (distance / _scaleStartDistance),
            MinScale, MaxScale);

        selected.Scale = value;
        ApplyScale(selected);
        _editor.ShowHoverStatus($"{selected.Id} scale {value:0.##}");

        if (Input.GetMouseButtonUp(0))
        {
            _scaling = false;

            var node = selected;
            var restore = _scaleStartValue;
            if (!Mathf.Approximately(restore, value))
                _editor.PushUndo("scale node " + node.Id, () =>
                {
                    node.Scale = restore;
                    ApplyScale(node);
                    _editor.RebuildActivePanel();
                    return true;
                });

            _editor.ClearHoverStatus();

            // The links meet the node's edge, so they follow its size only on a redraw.
            _editor.RebuildAll();
            _editor.RebuildActivePanel();
        }

        return true;
    }

    // Resized by reference during the drag; a rebuild every frame would drop the handle it is
    // being dragged by.
    private void ApplyScale(CTWorldMapNode node)
    {
        if (_editor.Screen.NodeRects.TryGetValue(node.Id, out var rect) && rect != null)
            rect.GetComponent<WorldMapNodeView>()?.ApplyScale(node.Scale);
    }

    // Selecting from a list as well as from the map: nodes overlap, and a small one under a big
    // one is otherwise unreachable. Adding is the ctrl+click gesture, so no button for it.
    private void BuildNodePicker(RectTransform panel, MapEditorUI ui, CTWorldMap map)
    {
        if (map.Nodes.Count == 0)
        {
            Note(ui, panel, "No nodes yet - ctrl+click the map to add one.");
            return;
        }

        var ids = new List<string>(map.Nodes.Count);
        var labels = new List<string>(map.Nodes.Count);

        foreach (var node in map.Nodes)
        {
            if (node == null) continue;
            ids.Add(node.Id);
            labels.Add(string.IsNullOrWhiteSpace(node.DisplayName) || node.DisplayName == node.Id
                ? node.Id
                : $"{node.DisplayName}  ({node.Id})");
        }

        var picker = ui.CreateDropdown(panel, "Select node", labels, (index, _) =>
        {
            if (index < 0 || index >= ids.Count) return;

            // The same frame's click would otherwise reach the map underneath the list.
            _editor.BlockWorldClicks();
            Select(ids[index]);
            _editor.RebuildActivePanel();
        });

        var current = ids.FindIndex(id =>
            string.Equals(id, _editor.SelectedNodeId, StringComparison.OrdinalIgnoreCase));
        if (current >= 0) picker.SetSelected(current);

        WorldMapEditor.Hint(picker,
            $"Select node - {ids.Count} on this map; ctrl+click the map to add another");
    }

    private void Select(string nodeId)
    {
        _editor.SelectedNodeId = nodeId;
        _editor.Screen?.HighlightNode(nodeId);
        MarkRequired();
    }

    // The gate list, drawn on the map in green.
    private void MarkRequired()
    {
        var node = SelectedNode();
        _editor.Screen?.SetRequiredMarks(node?.RequiredNodes);
    }

    // ---- links -------------------------------------------------------------------------------

    private void ToggleLink(CTWorldMapNode source, CTWorldMapNode target)
    {
        if (target == null) return;

        if (source == null)
        {
            _editor.SetStatus("Select a node with left click first, then right click the node it unlocks.");
            return;
        }

        if (source == target)
        {
            _editor.SetStatus("A node cannot link to itself.");
            return;
        }

        var existing = source.Children.FindIndex(id =>
            string.Equals(id, target.Id, StringComparison.OrdinalIgnoreCase));

        // Right clicking an existing link removes it, so one gesture both makes and breaks links.
        if (existing >= 0)
        {
            source.Children.RemoveAt(existing);
            _editor.PushUndo($"unlink {source.Id} -> {target.Id}", () =>
            {
                source.Children.Insert(Mathf.Clamp(existing, 0, source.Children.Count), target.Id);
                _editor.RebuildAll();
                return true;
            });

            _editor.RebuildAll();
            _editor.SetStatus($"Removed link '{source.Id}' -> '{target.Id}'.");
            return;
        }

        source.Children.Add(target.Id);
        _editor.PushUndo($"link {source.Id} -> {target.Id}", () =>
        {
            source.Children.RemoveAll(id =>
                string.Equals(id, target.Id, StringComparison.OrdinalIgnoreCase));
            _editor.RebuildAll();
            return true;
        });

        _editor.RebuildAll();
        _editor.SetStatus($"Linked '{source.Id}' -> '{target.Id}'.");
    }

    private void TryRemoveLinkAt(Vector2 point)
    {
        var map = _editor.Map;

        foreach (var node in map.Nodes)
        {
            foreach (var childId in node.Children)
            {
                var child = map.FindNode(childId);
                if (child == null) continue;

                var from = new Vector2(node.Position.X, node.Position.Y);
                var to = new Vector2(child.Position.X, child.Position.Y);
                if (DistanceToSegment(point, from, to) > 10f) continue;

                var owner = node;
                var removedId = childId;
                var index = owner.Children.IndexOf(childId);
                owner.Children.RemoveAt(index);

                _editor.PushUndo($"unlink {owner.Id} -> {removedId}", () =>
                {
                    owner.Children.Insert(Mathf.Clamp(index, 0, owner.Children.Count), removedId);
                    _editor.RebuildAll();
                    return true;
                });

                _editor.RebuildAll();
                _editor.SetStatus($"Removed link '{owner.Id}' -> '{removedId}'.");
                return;
            }
        }
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.sqrMagnitude;
        if (lengthSquared < 0.001f) return Vector2.Distance(point, a);

        var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        return Vector2.Distance(point, a + ab * t);
    }

    private void PlaceNode(Vector2 point)
    {
        var map = _editor.Map;
        var node = new CTWorldMapNode
        {
            Id = _editor.MintId("node", id => map.FindNode(id) != null),
            InitialState = "Preview",
            NodeType = "Dungeon"
        };
        node.DisplayName = node.Id;
        node.Position.X = point.x;
        node.Position.Y = point.y;

        map.Nodes.Add(node);
        Select(node.Id);
        _editor.PushUndo("add node " + node.Id, () =>
        {
            map.Nodes.Remove(node);
            if (_editor.SelectedNodeId == node.Id) Select(null);
            _editor.RebuildAll();
            _editor.RebuildActivePanel();
            return true;
        });

        _editor.RebuildAll();
        _editor.RebuildActivePanel();
        _editor.SetStatus($"Placed '{node.Id}'. Select its parent, then right click this one to " +
                          "link them; it starts as a preview.");
    }

    // One name for both jobs: it is what the map shows, and the id everything refers to is derived
    // from it. Ids stay unique on their own, so two nodes may carry the same display name.
    private void RenameNode(CTWorldMapNode node, string chosen)
    {
        var map = _editor.Map;
        if (string.IsNullOrWhiteSpace(chosen)) return;

        node.DisplayName = chosen;

        var newId = UniqueId(map, Slug(chosen), node);
        var oldId = node.Id;

        if (string.Equals(oldId, newId, StringComparison.Ordinal))
        {
            _editor.RebuildAll();
            _editor.RebuildActivePanel();
            _editor.SetStatus($"Renamed to '{chosen}'.");
            return;
        }

        node.Id = newId;

        // Every in-map reference follows the rename; progress keyed on the old id does not.
        foreach (var other in map.Nodes)
        {
            for (var i = 0; i < other.Children.Count; i++)
                if (string.Equals(other.Children[i], oldId, StringComparison.OrdinalIgnoreCase))
                    other.Children[i] = newId;
            for (var i = 0; i < other.RequiredNodes.Count; i++)
                if (string.Equals(other.RequiredNodes[i], oldId, StringComparison.OrdinalIgnoreCase))
                    other.RequiredNodes[i] = newId;
        }

        Select(newId);
        _editor.RebuildAll();
        _editor.RebuildActivePanel();
        _editor.SetStatus($"Renamed to '{chosen}' (id '{oldId}' -> '{newId}'). Progress recorded " +
                          "under the old id does not carry over.");
    }

    private static string Slug(string name)
    {
        var slug = new System.Text.StringBuilder(name.Length);
        foreach (var character in name.Trim())
        {
            if (char.IsLetterOrDigit(character)) slug.Append(char.ToLowerInvariant(character));
            else if (slug.Length > 0 && slug[slug.Length - 1] != '_') slug.Append('_');
        }

        var result = slug.ToString().Trim('_');
        return result.Length > 0 ? result : "node";
    }

    private static string UniqueId(CTWorldMap map, string wanted, CTWorldMapNode owner)
    {
        var taken = map.FindNode(wanted);
        if (taken == null || taken == owner) return wanted;

        for (var suffix = 2; suffix < 10000; suffix++)
        {
            var candidate = wanted + suffix;
            var other = map.FindNode(candidate);
            if (other == null || other == owner) return candidate;
        }
        return wanted + Guid.NewGuid().ToString("N").Substring(0, 4);
    }

    private void DeleteNode(CTWorldMapNode node)
    {
        var map = _editor.Map;
        var index = map.Nodes.IndexOf(node);

        // Everything that pointed at it, remembered for the undo.
        var childRefs = new List<(CTWorldMapNode owner, int index)>();
        var requiredRefs = new List<(CTWorldMapNode owner, int index)>();
        foreach (var other in map.Nodes)
        {
            if (other == node) continue;
            for (var i = other.Children.Count - 1; i >= 0; i--)
                if (string.Equals(other.Children[i], node.Id, StringComparison.OrdinalIgnoreCase))
                {
                    childRefs.Add((other, i));
                    other.Children.RemoveAt(i);
                }
            for (var i = other.RequiredNodes.Count - 1; i >= 0; i--)
                if (string.Equals(other.RequiredNodes[i], node.Id, StringComparison.OrdinalIgnoreCase))
                {
                    requiredRefs.Add((other, i));
                    other.RequiredNodes.RemoveAt(i);
                }
        }

        map.Nodes.Remove(node);
        if (string.Equals(_editor.SelectedNodeId, node.Id, StringComparison.OrdinalIgnoreCase))
            _editor.SelectedNodeId = null;

        _editor.PushUndo("delete node " + node.Id, () =>
        {
            map.Nodes.Insert(Mathf.Clamp(index, 0, map.Nodes.Count), node);
            foreach (var (owner, at) in childRefs)
                owner.Children.Insert(Mathf.Clamp(at, 0, owner.Children.Count), node.Id);
            foreach (var (owner, at) in requiredRefs)
                owner.RequiredNodes.Insert(Mathf.Clamp(at, 0, owner.RequiredNodes.Count), node.Id);
            _editor.RebuildAll();
            _editor.RebuildActivePanel();
            return true;
        });

        _editor.RebuildAll();
        _editor.RebuildActivePanel();
    }

    private CTWorldMapNode SelectedNode() =>
        _editor.Map != null ? _editor.Map.FindNode(_editor.SelectedNodeId) : null;

    private static List<string> TargetsFor(string kind)
    {
        var result = new List<string>();

        if (string.Equals(kind, "DungeonMap", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var map in CTDungeonMapSerialization.LoadAll())
                if (map != null && !string.IsNullOrWhiteSpace(map.MapName)) result.Add(map.MapName);
        }
        else if (string.Equals(kind, "Level", StringComparison.OrdinalIgnoreCase))
        {
            // Hubs are levels too, and are offered under their own kind instead.
            foreach (var level in CTLevelSerialization.LoadAll())
                if (level is { IsHub: false } && !string.IsNullOrWhiteSpace(level.LevelName))
                    result.Add(level.LevelName);
        }
        else if (string.Equals(kind, "Hub", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var level in CTLevelSerialization.LoadAll())
                if (level is { IsHub: true } && !string.IsNullOrWhiteSpace(level.LevelName))
                    result.Add(level.LevelName);
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static List<string> ListPngs(string mapName)
    {
        var result = new List<string>();
        try
        {
            var folder = CTWorldMapSerialization.FolderForRead(mapName);
            if (Directory.Exists(folder))
                foreach (var file in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
                    result.Add(Path.GetFileName(file));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("WorldEditor: could not list the map's images: " + e.Message);
        }
        return result;
    }

    private static void Note(MapEditorUI ui, Transform parent, string text)
    {
        var label = ui.CreateLabel(parent, text, 15);
        label.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.7f);
    }

    // A caption above a dropdown: the widget itself shows its current value once one is picked,
    // and then nothing on it says what it sets.
    private static void Label(MapEditorUI ui, Transform parent, string text)
    {
        var label = ui.CreateLabel(parent, text, 14);
        label.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.55f);
    }
}
