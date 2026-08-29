using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public class LevelTool : IMapEditorTool, IMapEditorShortcuts, IMapEditorScreenTool,
    IMapEditorEscapeHandler
{
    public string Name => "Level";

    private readonly RuntimeMapEditor _editor;
    private readonly List<GameObject> _dynamic = [];

    private RectTransform _panel;
    private MapEditorUI _ui;

    private CTLevelBlueprint _level;
    private CTLevelRoom _selected;
    private LevelLayoutCanvas _canvas;
    private string _savedJson;

    public LevelTool(RuntimeMapEditor editor)
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
        _editor.SetStatus("Level: create a new blueprint or open an existing one.");
    }

    public void OnExit() => CloseOverlay();

    // ---- screen tool -----------------------------------------------------------------------------

    public bool OwnsScreen => _canvas != null && _canvas.IsOpen;

    public void ScreenQuickSave()
    {
        if (_level != null) Write();
    }

    public bool ScreenStepBack() => RequestClose();

    public void ScreenHoverStatus(string message)
    {
        if (string.IsNullOrEmpty(message)) _canvas?.SetHint(_message, _severity);
        else _canvas?.SetHint(message);
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Select room / drag"),
        ("Ctrl+LMB", "Add a room here"),
        ("RMB", "Toggle node doors"),
        ("Del", "Delete selected room")
    ];

    public bool HandleEscape() => false;

    // ---- dock panel: the list of levels ------------------------------------------------------------

    private void Rebuild()
    {
        foreach (var go in _dynamic)
            if (go != null) UnityEngine.Object.Destroy(go);
        _dynamic.Clear();

        if (_panel == null || _ui == null) return;

        _dynamic.Add(_ui.CreateButton(_panel, "New Level Blueprint", CreateNew));

        var levels = CTLevelSerialization.LoadAll();
        levels.RemoveAll(level => level is { IsHub: true });

        if (levels.Count == 0)
        {
            _dynamic.Add(_ui.CreateLabel(_panel, "No level blueprints yet.", 14,
                TextAlignmentOptions.Center));
            return;
        }

        var labels = new List<string>(levels.Count);
        foreach (var level in levels)
            labels.Add(level.AuthoredLayout
                ? $"{level.LevelName} ({level.Rooms.Count} rooms, custom walk)"
                : $"{level.LevelName} ({level.Rooms.Count} rooms, random walk)");

        var dropdown = _ui.CreateDropdown(_panel, "Open Existing Level", labels, (index, _) =>
        {
            if (index < 0 || index >= levels.Count) return;

            _level = levels[index];
            _selected = null;
            OpenOverlay();
        });
        _dynamic.Add(dropdown.Root);
    }

    private void CreateNew()
    {
        _level = new CTLevelBlueprint
        {
            LevelName = FreeName(),
            AuthoredLayout = true,
            Rooms =
            [
                new CTLevelRoom
                {
                    Role = "Entrance", X = 0, Y = 0,
                    South = CTLevelRoom.WayIn, North = CTLevelRoom.Door
                },
                new CTLevelRoom
                {
                    Role = "Exit", X = 0, Y = 1,
                    South = CTLevelRoom.Door, North = CTLevelRoom.WayOut
                }
            ]
        };

        _selected = _level.Rooms[0];
        OpenOverlay();
        Hint("Ctrl+click a cell to add a room. Right-click another room to door them together.");
    }

    private static string FreeName(string stem = "untitledlevel")
    {
        for (var i = 1; i < 1000; i++)
        {
            var candidate = stem + i;
            if (!CTLevelSerialization.Available(candidate)) return candidate;
        }
        return stem;
    }

    // ---- the screen --------------------------------------------------------------------------------

    private void OpenOverlay()
    {
        TeardownCanvas();
        if (_level == null || _ui == null) return;

        if (_level.AuthoredLayout)
        {
            EnsureCells(_level);
            LevelLayout.Normalize(_level);
        }
        else
        {
            EnsureEndRooms(_level);
            EnsureCells(_level);
        }

        _savedJson = CTLevelSerialization.Exists(_level.LevelName) ? ToJson(_level) : null;

        _canvas = new LevelLayoutCanvas(_editor, _ui) { CloseRequested = () => RequestClose() };
        _canvas.Open(_level,
        [
            new LevelLayoutCanvas.DockItem("Save", "Save", "Save - write this level's blueprint",
                SaveFromScreen),
            new LevelLayoutCanvas.DockItem("Play", "Play Level",
                "Play - enter the level from its way in", PlayLevel)
        ], Shortcuts);

        BuildOptionsColumn();
        RefreshChrome();
        Hint(DefaultHint);
    }

    private const string DefaultHint =
        "Ctrl+click a cell to add a room, left-click to select or drag one, right-click another " +
        "room to open or close the door between them.";

    private static string ToJson(CTLevelBlueprint level) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(level, Newtonsoft.Json.Formatting.Indented);

    private bool HasUnsavedEdits =>
        _level != null && !string.Equals(ToJson(_level), _savedJson, StringComparison.Ordinal);

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

        _canvas.ShowConfirm($"Save changes to '{_level.LevelName}' before closing?",
            () =>
            {
                if (!Write()) return;
                CloseOverlay();
            },
            confirmLabel: "Save & close", altLabel: "Discard", onAlt: CloseOverlay);

        return true;
    }

    private void TeardownCanvas()
    {
        DestroyPoolGrid();

        _canvas?.Close();
        _canvas = null;
        _dragging = false;
    }

    public void CloseOverlay()
    {
        var open = _level;
        var wasOpen = _canvas != null && _canvas.IsOpen;
        var unsaved = wasOpen && HasUnsavedEdits;

        TeardownCanvas();
        _level = null;
        _selected = null;
        _savedJson = null;
        Rebuild();

        if (open == null || !wasOpen) return;

        _editor.SetStatus(unsaved
                ? $"Closed '{open.LevelName}' with changes that were never saved."
                : $"Closed '{open.LevelName}'.",
            unsaved ? StatusSeverity.Warning : StatusSeverity.Info);
    }

    private void SaveFromScreen()
    {
        MapNamePrompt.Show(_editor, _level.LevelName, "NAME THIS LEVEL", name =>
        {
            _level.LevelName = MapEditorSerialization.Sanitize(
                string.IsNullOrWhiteSpace(name) ? _level.LevelName : name);
            Write();
        }, existsCheck: CTLevelSerialization.Exists, existsNoun: "level");
    }

    private bool Write()
    {
        if (_level.AuthoredLayout) LevelLayout.Normalize(_level);
        else EnsureEndRooms(_level);

        var path = CTLevelSerialization.Save(_level);
        if (path == null)
        {
            Report("Level save failed, see log.", StatusSeverity.Error);
            return false;
        }

        _savedJson = ToJson(_level);
        RefreshChrome();

        var problem = LevelLayout.Validate(_level);
        Report(problem == null
                ? $"Saved '{_level.LevelName}'."
                : $"Saved '{_level.LevelName}'. It will not play yet - {problem}",
            problem == null ? StatusSeverity.Success : StatusSeverity.Warning);
        return true;
    }

    private void PlayLevel()
    {
        if (_level.AuthoredLayout) LevelLayout.Normalize(_level);
        else EnsureEndRooms(_level);

        var error = LevelPlayback.Start(_level, _editor);
        if (error != null)
        {
            Report(error, StatusSeverity.Error);
            return;
        }
        _editor.SetStatus($"Playing level {LevelPlayback.Describe()} - re-entering dungeon.");
    }

    // ---- options column ------------------------------------------------------------------------------

    private static readonly string[] Modifiers = ["None", "Combat", "Reward"];
    private const string VanillaLabel = "Vanilla generated";

    private static readonly string[] RoomKinds =
        [CTLevelRoom.Generated, CTLevelRoom.PodiumRoom, CTLevelRoom.EndOfFloorRoom];

    private void BuildOptionsColumn()
    {
        var column = _canvas?.OptionsContent;
        if (column == null) return;

        _ui.CloseTransientUi();

        DetachPoolGrid();

        for (var i = column.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(column.GetChild(i).gameObject);

        _poolNote = null;

        _ui.CreateToggle(column, "Random walk", !_level.AuthoredLayout, on => SetAuthored(!on));

        if (_selected == null)
        {
            if (_level.Rooms.Count == 0) Note(column, "No rooms yet - ctrl+click a cell to add one.");
            return;
        }

        var index = _level.Rooms.IndexOf(_selected);
        _ui.CreateHeader(column, $"- Room {index + 1} -", 20);

        BuildEndControls(column, index);

        if (_level.AuthoredLayout) BuildKindControl(column);

        if (_selected.VanillaRoom == CTLevelRoom.Generated)
        {
            _ui.CreateHeader(column, "- Maps -", 20);
            BuildPoolControls(column, _selected);
        }
        else if (_selected.VanillaRoom == CTLevelRoom.PodiumRoom)
        {
            Note(column, "The podiums only appear on the first floor of a run.");
        }

        BuildModifierControl(column);
    }

    private void BuildModifierControl(RectTransform column)
    {
        Label(column, "Modifier");

        var labels = new List<string>(Modifiers.Length);
        foreach (var modifier in Modifiers) labels.Add("Modifier: " + modifier);

        var picker = _ui.CreateDropdown(column, "Modifier", labels, (i, _) =>
        {
            if (i < 0 || i >= Modifiers.Length || _selected == null) return;

            _selected.Modifier = Modifiers[i];

            _canvas?.RebuildVisuals();
            _canvas?.HighlightRoom(_selected);
        });

        picker.SetSelected(Array.IndexOf(Modifiers, _selected.Modifier));
    }

    private void BuildEndControls(RectTransform column, int index)
    {
        if (!_level.AuthoredLayout) return;

        var first = index == 0;
        var last = index == _level.Rooms.Count - 1;

        _ui.CreateToggle(column, "Entrance", first, on =>
        {
            if (on && _selected != null && _level.Rooms.IndexOf(_selected) != 0) MoveTo(0, "starts here");
            else BuildOptionsColumn();
        });

        _ui.CreateToggle(column, "Exit", last, on =>
        {
            if (on && _selected != null && _level.Rooms.IndexOf(_selected) != _level.Rooms.Count - 1)
                MoveTo(-1, "has the way out");
            else BuildOptionsColumn();
        });
    }

    private void MoveTo(int position, string became)
    {
        if (_selected == null) return;

        var before = Snapshot();
        var room = _selected;

        _level.Rooms.Remove(room);
        if (position < 0) _level.Rooms.Add(room);
        else _level.Rooms.Insert(Mathf.Clamp(position, 0, _level.Rooms.Count), room);

        if (_level.AuthoredLayout) LevelLayout.Normalize(_level);
        else EnsureEndRooms(_level);

        PushUndo("change which room is an end", before);
        AfterMutation($"Room {_level.Rooms.IndexOf(room) + 1} {became}.");
    }

    private void BuildKindControl(RectTransform column)
    {
        Label(column, "Room kind");

        var labels = new List<string>
        {
            "Generated room",
            "Vanilla entrance (weapon podiums)",
            "Vanilla exit (end of floor)"
        };

        var picker = _ui.CreateDropdown(column, "Room kind", labels, (i, _) =>
        {
            if (i < 0 || i >= RoomKinds.Length || _selected == null) return;
            SetKind(_selected, RoomKinds[i]);
        });

        var at = Array.IndexOf(RoomKinds, _selected.VanillaRoom ?? CTLevelRoom.Generated);
        picker.SetSelected(at < 0 ? 0 : at);
    }

    private MapEditorGrid _poolGrid;
    private GameObject _poolNote;

    private const float PoolGridHeight = 320f;

    private void BuildPoolControls(RectTransform column, CTLevelRoom room)
    {
        _poolSearch = new MapEditorSearchRow(_editor, _ui, column, ShowPoolSearch, ShowPoolAll);

        if (_poolGrid == null)
        {
            _poolGrid = _ui.CreateIconGrid(column, "PoolGrid", columns: 3, cellSize: 132f,
                scrollHeight: PoolGridHeight);
            _poolGrid.ShowNames = true;

            var entries = new List<MapEditorGrid.Entry>
            {
                new()
                {
                    Id = CTLevelRoom.VanillaNode,
                    Display = VanillaLabel,
                    OnClick = () => TogglePool(CTLevelRoom.VanillaNode)
                }
            };

            var hubBlueprints = HubSession.BlueprintNames();

            foreach (var name in MapEditorSerialization.SavedNames())
            {
                if (hubBlueprints.Contains(name)) continue;

                var captured = name;
                entries.Add(new MapEditorGrid.Entry
                {
                    Id = captured,
                    Display = captured,
                    OnClick = () =>
                    {
                        _poolSearch?.Confirm();
                        TogglePool(captured);
                    }
                });
            }

            _poolEntries = entries;
            PopulatePool(null);
        }
        else
        {
            _poolGrid.Root.transform.SetParent(column, false);
            _poolGrid.Root.SetActive(true);

            _poolGrid.Reflow();
        }

        _poolGrid.SetSelectedMany(room.NodePool);

        _ui.CreateButton(column, "Clear Map Selection", ClearPool, emphasis: MapEditorEmphasis.Quiet);

        _poolNote = _ui.CreateLabel(column, PoolNoteFor(room), 14, TextAlignmentOptions.Center);
        MapEditorUI.FitLabelHeight(_poolNote);
    }

    private List<MapEditorGrid.Entry> _poolEntries;
    private MapEditorSearchRow _poolSearch;

    private void PopulatePool(string needle)
    {
        if (_poolGrid == null || _poolEntries == null) return;

        var shown = _poolEntries;

        if (!string.IsNullOrEmpty(needle))
            shown = _poolEntries.FindAll(entry =>
                entry.Display != null &&
                entry.Display.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);

        _poolGrid.Populate(_editor, shown, RequestRoomIcon);

        if (_selected != null) _poolGrid.SetSelectedMany(_selected.NodePool);
    }

    private void ShowPoolSearch(string needle)
    {
        PopulatePool(needle);

        var matches = _poolGrid != null && _poolEntries != null
            ? _poolEntries.FindAll(entry =>
                entry.Display != null &&
                entry.Display.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).Count
            : 0;

        _poolSearch?.ReportCount(matches, matches, needle);
    }

    private void ShowPoolAll() => PopulatePool(null);

    private static string PoolNoteFor(CTLevelRoom room) =>
        room == null || room.NodePool.Count == 0
            ? "Empty - any saved map can appear here."
            : $"{room.NodePool.Count} map(s) can appear here.";

    private void DetachPoolGrid()
    {
        if (_poolGrid?.Root == null) return;

        _poolGrid.Root.transform.SetParent(null, false);
        _poolGrid.Root.SetActive(false);
    }

    private void DestroyPoolGrid()
    {
        if (_poolGrid?.Root != null) UnityEngine.Object.Destroy(_poolGrid.Root);
        _poolGrid = null;
        _poolNote = null;
    }

    private void TogglePool(string key)
    {
        var room = _selected;
        if (room == null) return;

        if (!room.NodePool.Remove(key)) room.NodePool.Add(key);

        AfterPoolEdit(room);
    }

    private void ClearPool()
    {
        var room = _selected;
        if (room == null || room.NodePool.Count == 0) return;

        room.NodePool.Clear();
        AfterPoolEdit(room);
    }

    private void AfterPoolEdit(CTLevelRoom room)
    {
        _poolGrid?.SetSelectedMany(room.NodePool);

        if (_poolNote != null)
        {
            var text = _poolNote.GetComponent<TMP_Text>();
            if (text != null) text.text = PoolNoteFor(room);
        }

        _canvas?.RebuildVisuals();
        _canvas?.HighlightRoom(_selected);
        RefreshChrome();
    }

    private static readonly Dictionary<string, Sprite> _roomIcons = [];

    private void RequestRoomIcon(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        if (id == CTLevelRoom.VanillaNode)
        {
            _poolGrid?.SetCellIcon(id, MapEditorIcons.GetToolIconOrNull("World File"));
            return;
        }

        if (_roomIcons.TryGetValue(id, out var cached))
        {
            if (cached != null) _poolGrid?.SetCellIcon(id, cached);
            return;
        }

        _editor.StartCoroutine(LoadRoomIcon(id));
    }

    private System.Collections.IEnumerator LoadRoomIcon(string mapName)
    {
        _roomIcons[mapName] = null;

        var path = MapEditorSerialization.SnapshotPathFor(mapName);
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) yield break;

        string uri;
        try
        {
            uri = new Uri(path).AbsoluteUri;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: snapshot path '{path}' is not readable: {e.Message}");
            yield break;
        }

        using var request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(uri, nonReadable: true);
        yield return request.SendWebRequest();

        if (!string.IsNullOrEmpty(request.error)) yield break;

        var texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
        if (texture == null) yield break;

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));

        _roomIcons[mapName] = sprite;
        _poolGrid?.SetCellIcon(mapName, sprite);
    }

    // ---- mutation --------------------------------------------------------------------------------------

    private void SetAuthored(bool authored)
    {
        if (_level.AuthoredLayout == authored) return;

        var before = Snapshot();
        _level.AuthoredLayout = authored;

        if (authored)
        {
            if (LevelLayout.Rooms(_level, CTLevelRoom.WayIn).Count == 0) LayOutAsLine();
            LevelLayout.Normalize(_level);
        }
        else
        {
            EnsureEndRooms(_level);
            EnsureCells(_level);
        }

        PushUndo(authored ? "author the layout" : "hand the layout back to the game", before);
        AfterMutation(authored ? "The floor is this grid." : "Random walk.");
    }

    private void LayOutAsLine()
    {
        for (var i = 0; i < _level.Rooms.Count; i++)
        {
            var room = _level.Rooms[i];
            room.X = 0;
            room.Y = i;
            room.North = i == _level.Rooms.Count - 1 ? CTLevelRoom.WayOut : CTLevelRoom.Door;
            room.South = i == 0 ? CTLevelRoom.WayIn : CTLevelRoom.Door;
            room.East = CTLevelRoom.Wall;
            room.West = CTLevelRoom.Wall;
        }
    }

    private void SetKind(CTLevelRoom room, string kind)
    {
        if (room.VanillaRoom == kind) return;

        var before = Snapshot();
        room.VanillaRoom = kind;

        PushUndo("change a room's kind", before);
        AfterMutation(kind == CTLevelRoom.Generated
            ? "Generated room."
            : $"The game's own {LevelLayout.DescribeRole(kind)} room.");
    }

    private void SetSide(CTLevelRoom room, LevelSide side, string value)
    {
        if (LevelLayout.Get(room, side) == value) return;

        var before = Snapshot();

        LevelLayout.SetShared(_level, room, side, value);
        LevelLayout.Normalize(_level);

        PushUndo("open or close a door", before);
        AfterMutation($"{LevelLayout.DescribeSide(side)}: " +
                      (value == CTLevelRoom.Door ? "door." : "wall."));
    }

    private void PlaceRoom(Vector2Int cell)
    {
        if (LevelLayout.At(_level, cell.x, cell.y) != null)
        {
            Hint("There is already a room there.", StatusSeverity.Warning);
            return;
        }

        var before = Snapshot();
        var room = new CTLevelRoom { Role = "Normal", X = cell.x, Y = cell.y };

        if (_level.AuthoredLayout)
        {
            _level.Rooms.Add(room);
            LevelLayout.WireRoom(_level, room);
        }
        else
        {
            _level.Rooms.Insert(Mathf.Max(0, _level.Rooms.Count - 1), room);
            EnsureEndRooms(_level);
        }

        _selected = room;

        PushUndo("place a room", before);
        AfterMutation($"Room {_level.Rooms.IndexOf(room) + 1} at ({cell.x}, {cell.y}).");
    }

    private void DeleteSelected()
    {
        if (_selected == null) return;

        if (IsFixedEnd(_selected))
        {
            Hint("The game builds the entrance and end-of-floor rooms; they cannot be removed.",
                StatusSeverity.Warning);
            return;
        }

        if (!_level.AuthoredLayout && _level.Rooms.Count <= 3)
        {
            Hint("A random walk needs at least one room between the two the game builds.",
                StatusSeverity.Warning);
            return;
        }

        var before = Snapshot();
        var where = $"({_selected.X}, {_selected.Y})";

        _level.Rooms.Remove(_selected);
        _selected = null;

        if (_level.AuthoredLayout) LevelLayout.Normalize(_level);
        else EnsureEndRooms(_level);

        PushUndo("delete a room", before);
        AfterMutation($"Deleted the room at {where}.");
    }

    private void ToggleDoor(CTLevelRoom from, CTLevelRoom to)
    {
        if (!_level.AuthoredLayout)
        {
            Hint("Doors are ignored while the game lays the floor out.", StatusSeverity.Warning);
            return;
        }

        if (from == null)
        {
            Hint("Select a room first, then right-click the one to connect it to.",
                StatusSeverity.Warning);
            return;
        }

        if (ReferenceEquals(from, to)) return;

        LevelSide? shared = null;
        foreach (var side in LevelLayout.Sides)
            if (ReferenceEquals(LevelLayout.Neighbour(_level, from, side), to)) shared = side;

        if (shared == null)
        {
            Hint("Doors only join rooms that touch.", StatusSeverity.Warning);
            return;
        }

        var before = Snapshot();
        var opening = LevelLayout.Get(from, shared.Value) != CTLevelRoom.Door;

        LevelLayout.SetShared(_level, from, shared.Value,
            opening ? CTLevelRoom.Door : CTLevelRoom.Wall);
        LevelLayout.Normalize(_level);

        PushUndo(opening ? "open a door" : "close a door", before);
        AfterMutation(opening ? "Door opened." : "Door closed.");
    }

    private void CommitMove(Vector2Int from, Vector2Int to)
    {
        if (from == to)
        {
            _movePreState = null;
            _canvas?.RebuildVisuals();
            _canvas?.HighlightRoom(_selected);
            return;
        }

        var before = _movePreState;
        _movePreState = null;

        if (_level.AuthoredLayout) LevelLayout.WireRoom(_level, _selected);

        PushUndo("move a room", before);
        AfterMutation($"Moved to ({to.x}, {to.y}).");
    }

    private static void EnsureEndRooms(CTLevelBlueprint level)
    {
        if (level.AuthoredLayout) return;

        if (level.IsHub)
        {
            if (level.Rooms.Count == 0) level.Rooms.Add(new CTLevelRoom());
            while (level.Rooms.Count > 1) level.Rooms.RemoveAt(level.Rooms.Count - 1);
            level.Rooms[0].Role = "Entrance";
            return;
        }

        var inserted = false;

        if (!HasFixedEnds(level))
        {
            level.Rooms.Insert(0, Fresh(level, CTLevelRoom.PodiumRoom));
            level.Rooms.Add(Fresh(level, CTLevelRoom.EndOfFloorRoom));
            inserted = true;
        }

        if (level.Rooms.Count < 3)
        {
            level.Rooms.Insert(1, Fresh(level, CTLevelRoom.Generated));
            inserted = true;
        }

        if (inserted) LineUpInOrder(level);

        level.Rooms[0].Role = "Entrance";
        level.Rooms[0].VanillaRoom = CTLevelRoom.PodiumRoom;
        level.Rooms[0].NodePool.Clear();

        level.Rooms[^1].Role = "Exit";
        level.Rooms[^1].VanillaRoom = CTLevelRoom.EndOfFloorRoom;
        level.Rooms[^1].NodePool.Clear();

        for (var i = 1; i < level.Rooms.Count - 1; i++)
        {
            level.Rooms[i].Role = "Normal";
            level.Rooms[i].VanillaRoom = CTLevelRoom.Generated;
        }
    }

    private static void LineUpInOrder(CTLevelBlueprint level)
    {
        for (var i = 0; i < level.Rooms.Count; i++)
        {
            if (level.Rooms[i] == null) continue;

            level.Rooms[i].X = 0;
            level.Rooms[i].Y = i;
        }
    }

    private static CTLevelRoom Fresh(CTLevelBlueprint level, string kind)
    {
        var room = new CTLevelRoom { VanillaRoom = kind };

        for (var y = 0; y < 1000; y++)
        {
            if (LevelLayout.At(level, 0, y) != null) continue;

            room.X = 0;
            room.Y = y;
            break;
        }

        return room;
    }

    private static bool HasFixedEnds(CTLevelBlueprint level) =>
        level.Rooms.Count >= 2 &&
        level.Rooms[0].VanillaRoom == CTLevelRoom.PodiumRoom &&
        level.Rooms[^1].VanillaRoom == CTLevelRoom.EndOfFloorRoom;

    private bool IsFixedEnd(CTLevelRoom room) =>
        !_level.AuthoredLayout && _level.Rooms.Count > 0 &&
        (ReferenceEquals(room, _level.Rooms[0]) || ReferenceEquals(room, _level.Rooms[^1]));

    private static void EnsureCells(CTLevelBlueprint level)
    {
        var seen = new HashSet<(int, int)>();
        var clash = false;

        foreach (var room in level.Rooms)
            if (room != null && !seen.Add((room.X, room.Y))) clash = true;

        if (!clash) return;

        for (var i = 0; i < level.Rooms.Count; i++)
        {
            if (level.Rooms[i] == null) continue;
            level.Rooms[i].X = 0;
            level.Rooms[i].Y = i;
        }
    }

    // ---- undo ------------------------------------------------------------------------------------------

    private string Snapshot() => ToJson(_level);

    private void PushUndo(string description, string before)
    {
        if (before == null) return;

        var level = _level;
        _editor.History.Push(description, () =>
        {
            if (_level != level) return false;

            var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<CTLevelBlueprint>(before);
            if (restored == null) return false;

            level.Rooms = restored.Rooms;
            level.AuthoredLayout = restored.AuthoredLayout;
            _selected = null;

            AfterMutation("Undone.");
            return true;
        });
    }

    private void AfterMutation(string message)
    {
        _canvas?.RebuildVisuals();
        _canvas?.HighlightRoom(_selected);
        BuildOptionsColumn();
        RefreshChrome();
        Hint(message);
    }

    private void RefreshChrome()
    {
        if (_canvas == null || _level == null) return;

        _canvas.SetTitle(_level.LevelName);

        var issues = LevelLayout.Issues(_level);
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

    // ---- input -----------------------------------------------------------------------------------------

    private bool _dragging;
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPosition;
    private Vector2Int _dragStartCell;
    private string _movePreState;

    public void OnUpdate()
    {
        if (_canvas == null || !_canvas.IsOpen || !_canvas.Visible || _level == null) return;

        if (_dragging && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) DropDrag();

        if (_ui.TransientUiOpen) return;
        if (_canvas.ConfirmOpen) return;

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            DeleteSelected();
            return;
        }

        if (Input.GetMouseButtonDown(1) && !_canvas.PointerOverChrome())
        {
            var clicked = _canvas.HitRoom(_canvas.PointerContent());
            if (clicked != null) ToggleDoor(_selected, clicked);
            return;
        }

        if (Input.GetMouseButtonDown(0) && !_canvas.PointerOverChrome())
        {
            var point = _canvas.PointerContent();

            if (RuntimeMapEditor.CtrlHeld)
            {
                var cell = _canvas.CellAt(point);
                if (_canvas.InWindow(cell)) PlaceRoom(cell);
                return;
            }

            var hit = _canvas.HitRoom(point);
            if (hit != null)
            {
                if (!ReferenceEquals(hit, _selected))
                {
                    _selected = hit;
                    _canvas.HighlightRoom(_selected);
                    BuildOptionsColumn();
                }

                _dragging = true;
                _dragStartPointer = point;
                _dragStartCell = new Vector2Int(hit.X, hit.Y);
                _dragStartPosition = _canvas.CellCentre(hit.X, hit.Y);
                _movePreState = Snapshot();
            }
            else if (_selected != null)
            {
                _selected = null;
                _canvas.HighlightRoom(null);
                BuildOptionsColumn();
            }
        }

        var dragged = _selected;
        if (_dragging && dragged != null && Input.GetMouseButton(0))
        {
            if (_canvas.RoomRects.TryGetValue(dragged, out var rect) && rect != null)
                rect.anchoredPosition =
                    _dragStartPosition + (_canvas.PointerContent() - _dragStartPointer);
        }

        if (_dragging && Input.GetMouseButtonUp(0)) DropDrag();
    }

    private void DropDrag()
    {
        _dragging = false;

        var room = _selected;
        if (room == null)
        {
            _movePreState = null;
            return;
        }

        var cell = _canvas.CellAt(_canvas.PointerContent());
        var occupant = LevelLayout.At(_level, cell.x, cell.y);

        if (!_canvas.InWindow(cell) || (occupant != null && !ReferenceEquals(occupant, room)))
        {
            _movePreState = null;
            _canvas.RebuildVisuals();
            _canvas.HighlightRoom(_selected);

            if (occupant != null && !ReferenceEquals(occupant, room))
                Hint("There is already a room there.", StatusSeverity.Warning);
            return;
        }

        room.X = cell.x;
        room.Y = cell.y;
        CommitMove(_dragStartCell, cell);
    }

    // ---- talking ---------------------------------------------------------------------------------------

    private string _message = "";
    private StatusSeverity _severity = StatusSeverity.Info;

    private void Hint(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        _message = message;
        _severity = severity;
        _canvas?.SetHint(message, severity);
    }

    private void Report(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        if (_canvas != null && _canvas.IsOpen) Hint(message, severity);
        else _editor.SetStatus(message, severity);
    }

    private void Note(Transform parent, string text)
    {
        var label = _ui.CreateLabel(parent, text, 14, TextAlignmentOptions.Center);
        MapEditorUI.FitLabelHeight(label);
    }

    private void Label(Transform parent, string text) =>
        _ui.CreateLabel(parent, text, 16, TextAlignmentOptions.Left);
}
