using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// Authors a level: the rooms it is made of, and the shape they stand in.
//
// The dock panel is the list of levels and nothing else - picking one goes straight to the screen,
// the dungeon builder's arrangement, because a half-open state with a second set of controls in it
// is a page nobody is ever on.
//
// A level either lays its own floor out - a cell per room and a door per side, which the generator
// then builds exactly (see LevelLayout) - or leaves the shape to the game, which is the original
// behaviour and what every blueprint written before this did. The toggle for that is on the screen.
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
        ("Left click", "Select room / drag"),
        ("Ctrl+Left", "Add a room here"),
        ("Right click", "Open/close door to selection"),
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

        // Hubs are not dungeon content: they are authored in the game's town room from the F7
        // panel, and would only be half-editable here.
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
                // The smallest thing that is already a whole level: arrive in the south of one room,
                // through a door into the next, and out of the top of that one.
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

        // Ends first: it can insert rooms, and every room it inserts needs a cell of its own. A
        // blueprint written before layouts existed has no cells at all - every room sits on (0, 0),
        // which would draw as one plate with the whole level hidden under it.
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

    // One way out, whether it was pressed, clicked or F4'd: dismiss the prompt, else ask about
    // unsaved work, else close.
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
        _canvas?.Close();
        _canvas = null;
        _dragging = false;
    }

    // Closing the screen closes the level: the dock panel is the list of levels and nothing else, so
    // there is no half-open state for it to show.
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
    private const string VanillaLabel = "Vanilla generated room";

    private static readonly string[] RoomKinds =
        [CTLevelRoom.Generated, CTLevelRoom.PodiumRoom, CTLevelRoom.EndOfFloorRoom];

    private void BuildOptionsColumn()
    {
        var column = _canvas?.OptionsContent;
        if (column == null) return;

        _ui.CloseTransientUi();
        for (var i = column.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(column.GetChild(i).gameObject);

        _ui.CreateHeader(column, "- Level -", 22);
        Note(column, Summary());

        _ui.CreateToggle(column, "Random walk", !_level.AuthoredLayout, on => SetAuthored(!on));

        if (_selected == null)
        {
            Note(column, _level.Rooms.Count == 0
                ? "No rooms yet - ctrl+click a cell to add one."
                : "Nothing selected. Left-click a room, or ctrl+click a cell to add one.");
            return;
        }

        var index = _level.Rooms.IndexOf(_selected);
        _ui.CreateHeader(column, $"- Room {index + 1} -", 20);

        BuildEndControls(column, index);

        if (_level.AuthoredLayout)
        {
            BuildKindControl(column);
            BuildSideControls(column);
        }

        Label(column, "Modifier");
        var modifierLabels = new List<string>(Modifiers.Length);
        foreach (var modifier in Modifiers) modifierLabels.Add("Modifier: " + modifier);

        var modifierPicker = _ui.CreateDropdown(column, "Modifier", modifierLabels, (i, _) =>
        {
            if (i < 0 || i >= Modifiers.Length || _selected == null) return;
            _selected.Modifier = Modifiers[i];
            BuildOptionsColumn();
        });
        modifierPicker.SetSelected(Array.IndexOf(Modifiers, _selected.Modifier));

        // A prefab room has no pool to pick from: it is that room, and the podiums or the exit
        // platform standing in it are the reason it is there.
        if (_selected.VanillaRoom != CTLevelRoom.Generated)
        {
            // The podiums take themselves away past the first floor of a run - the game's rule, not
            // ours - and a room that arrives empty reads as a broken feature unless it is said here.
            if (_selected.VanillaRoom == CTLevelRoom.PodiumRoom)
                Note(column, "The podiums only appear on the first floor of a run.");

            return;
        }

        _ui.CreateHeader(column, "- Plays -", 20);
        BuildPoolControls(column, _selected);
    }

    // Which room the run starts in and which one has the way out are the first and last in the
    // level, so choosing them is choosing where this room sits in it. The doors themselves are
    // placed by LevelLayout - there is nothing here to wire.
    private void BuildEndControls(RectTransform column, int index)
    {
        var first = index == 0;
        var last = index == _level.Rooms.Count - 1;

        // Under a random walk the ends are the game's own rooms and their position is not a choice,
        // so there is nothing to move and nothing to move it to.
        if (!_level.AuthoredLayout)
        {
            if (first) Note(column, "Built by the game. The run starts here.");
            else if (last) Note(column, "Built by the game. The way out is here.");
            return;
        }

        if (first && last) Note(column, "The only room: the run starts and ends here.");
        else if (first) Note(column, "The run starts here.");
        else if (last) Note(column, "The way out is here.");

        if (!first) _ui.CreateButton(column, "Start Here", () => MoveTo(0, "starts here"));
        if (!last) _ui.CreateButton(column, "Way Out Here", () => MoveTo(-1, "has the way out"));
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

    private string Summary()
    {
        if (_level == null) return "";

        var bound = 0;
        var prefabs = 0;
        foreach (var room in _level.Rooms)
        {
            if (room == null) continue;
            if (room.VanillaRoom != CTLevelRoom.Generated) prefabs++;
            else if (room.NodePool.Count > 0) bound++;
        }

        // The two the game builds are rooms in the level now, so the count on screen is the floor
        // the player will walk - there is no arithmetic left to explain.
        return $"{_level.Rooms.Count} room(s), {bound} naming a blueprint" +
               (prefabs > 0 ? $", {prefabs} built by the game" : "");
    }

    // Whether this room is generated - the kind a blueprint is pasted onto - or one of the game's
    // own finished rooms. Only meaningful with a layout: without one the game places its own
    // entrance and end-of-floor rooms and never consults ours.
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

    // Only the sides that face another room. Doors are opened for every room this one touches the
    // moment it is placed or dragged, so these are here to close one again - a spiral, a room you
    // walk past rather than through. A side facing open grid has nothing to decide: it is a wall,
    // or it is one of the two end doors, and both of those are placed by LevelLayout.
    private void BuildSideControls(RectTransform column)
    {
        var joins = new List<LevelSide>();
        foreach (var side in LevelLayout.Sides)
            if (LevelLayout.Neighbour(_level, _selected, side) != null) joins.Add(side);

        var wayIn = LevelLayout.SideCarrying(_selected, CTLevelRoom.WayIn);
        var wayOut = LevelLayout.SideCarrying(_selected, CTLevelRoom.WayOut);

        if (joins.Count == 0 && wayIn == null && wayOut == null) return;

        _ui.CreateHeader(column, "- Doors -", 20);

        if (wayIn != null) Note(column, $"Way in: {LevelLayout.DescribeSide(wayIn.Value)}");
        if (wayOut != null) Note(column, $"Way out: {LevelLayout.DescribeSide(wayOut.Value)}");

        var keys = new List<string> { CTLevelRoom.Door, CTLevelRoom.Wall };

        foreach (var side in joins)
        {
            var captured = side;
            var name = LevelLayout.DescribeSide(side);
            var labels = new List<string> { $"{name}: door", $"{name}: wall" };

            var picker = _ui.CreateDropdown(column, name, labels, (i, _) =>
            {
                if (i < 0 || i >= keys.Count || _selected == null) return;
                SetSide(_selected, captured, keys[i]);
            });

            picker.SetSelected(LevelLayout.Get(_selected, side) == CTLevelRoom.Door ? 0 : 1);
        }
    }

    // One dropdown offering what is not in the pool yet, and an X row per member, so the panel
    // scales with the pool rather than with the save folder.
    private void BuildPoolControls(RectTransform column, CTLevelRoom room)
    {
        var candidateKeys = new List<string>();
        var candidateLabels = new List<string>();

        // <vanilla> = the room the game would have generated.
        if (!room.NodePool.Contains(CTLevelRoom.VanillaNode))
        {
            candidateKeys.Add(CTLevelRoom.VanillaNode);
            candidateLabels.Add(VanillaLabel);
        }

        foreach (var node in MapEditorSerialization.LoadAll())
        {
            if (room.NodePool.Contains(node.MapName)) continue;
            candidateKeys.Add(node.MapName);
            candidateLabels.Add(node.MapName);
        }

        if (candidateLabels.Count > 0)
        {
            var dropdown = _ui.CreateDropdown(column, "Add To Pool", candidateLabels, (index, _) =>
            {
                if (index < 0 || index >= candidateKeys.Count) return;

                room.NodePool.Add(candidateKeys[index]);
                AfterPoolChange();
            });
            dropdown.SetSelected(-1);
        }
        else
        {
            Note(column, "Everything saved is already in this pool.");
        }

        if (room.NodePool.Count == 0)
        {
            Note(column, "Empty - any saved room can appear here.");
            return;
        }

        // Copied: removing walks the list these rows were built from.
        foreach (var entry in new List<string>(room.NodePool))
        {
            var captured = entry;
            var label = captured == CTLevelRoom.VanillaNode ? VanillaLabel : captured;
            _ui.CreateButton(column, "X  " + label, () =>
            {
                room.NodePool.Remove(captured);
                AfterPoolChange();
            });
        }
    }

    private void AfterPoolChange()
    {
        _canvas?.RebuildVisuals();
        _canvas?.HighlightRoom(_selected);
        BuildOptionsColumn();
        RefreshChrome();
    }

    // ---- mutation --------------------------------------------------------------------------------------

    private void SetAuthored(bool authored)
    {
        if (_level.AuthoredLayout == authored) return;

        var before = Snapshot();
        _level.AuthoredLayout = authored;

        if (authored)
        {
            // Coming back to a grid the level may never have had. A level that has no way in has
            // never been laid out, so it gets the straight line its room order always described.
            if (LevelLayout.Rooms(_level, CTLevelRoom.WayIn).Count == 0) LayOutAsLine();
            LevelLayout.Normalize(_level);
        }
        else
        {
            // Without a layout the level is a sequence again, and the game builds its two ends -
            // which it may have to insert, and anything inserted needs a cell of its own.
            EnsureEndRooms(_level);
            EnsureCells(_level);
        }

        PushUndo(authored ? "author the layout" : "hand the layout back to the game", before);
        AfterMutation(authored ? "The floor is this grid." : "Random walk.");
    }

    // Entrance at the origin, each room north of the last, the way out off the top: the shape a flat
    // room list always described.
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

        // Both rooms, or Normalize hands the door straight back - see LevelLayout.SetShared.
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
            // Placed before wiring: WireRoom reads the level to find what this room touches, and a
            // room not in it yet touches nothing.
            _level.Rooms.Add(room);
            LevelLayout.WireRoom(_level, room);
        }
        else
        {
            // Without a layout the order is the level, and the last room is the one with the way
            // out - so a new room goes before it rather than after, the way Add Room always did.
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

        // Refused rather than honoured and undone: the walk cannot lay out a floor with nothing
        // between its ends, so EnsureEndRooms would put a room straight back and the delete would
        // look like it had done nothing but move a room onto the podiums.
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

    // Moving a room changes what it touches, so its doors are worked out again from where it now
    // is: doors to everything it has arrived next to, walls where it has parted company, and the
    // two end doors re-placed on whatever sides are free.
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

    // A random walk always builds two rooms of its own, whatever the level says: PlaceEntranceAndExit
    // makes the room the player arrives in the EntranceRoomPath prefab - the weapon podiums - and
    // appends the EndOfFloorRoomPath room with the way out. They were always on the floor; the level
    // just did not know about them, which is why its first blueprint was being loaded over the
    // podiums and its last over the exit platform.
    //
    // So the level owns them. They are the first and last entries, they carry no pool, and they
    // cannot be deleted - the floor has them whether or not the author wants them.
    private static void EnsureEndRooms(CTLevelBlueprint level)
    {
        if (level.AuthoredLayout) return;

        // A hub is one room that is both the way in and the way out; the entrance/exit pair does
        // not apply to it.
        if (level.IsHub)
        {
            if (level.Rooms.Count == 0) level.Rooms.Add(new CTLevelRoom());
            while (level.Rooms.Count > 1) level.Rooms.RemoveAt(level.Rooms.Count - 1);
            level.Rooms[0].Role = "Entrance";
            return;
        }

        var inserted = false;

        // Inserted, never repurposed: a blueprint written before this has real rooms at both ends
        // with pools the author chose, and taking those over would throw two of them away.
        if (!HasFixedEnds(level))
        {
            level.Rooms.Insert(0, Fresh(level, CTLevelRoom.PodiumRoom));
            level.Rooms.Add(Fresh(level, CTLevelRoom.EndOfFloorRoom));
            inserted = true;
        }

        // The walk cannot be asked for fewer than two rooms of its own (PlaceEntranceAndExit reads
        // rooms with exactly one connection, and a lone room has none), so there is always at least
        // one room of the author's between the ends.
        if (level.Rooms.Count < 3)
        {
            level.Rooms.Insert(1, Fresh(level, CTLevelRoom.Generated));
            inserted = true;
        }

        // The grid says nothing about the shape under a random walk, but it does say the order - so
        // when the list has been restructured the column is drawn to match it, rather than leaving
        // rooms the author never placed sitting wherever there happened to be a gap.
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

    // A new room standing somewhere of its own. A CTLevelRoom defaults to cell (0, 0), which is
    // almost always where another room already is - and a room drawn on top of another one is one
    // the author can neither see nor click. Called before the room joins the list, so the search
    // cannot find the room it is placing.
    private static CTLevelRoom Fresh(CTLevelBlueprint level, string kind)
    {
        var room = new CTLevelRoom { VanillaRoom = kind };

        // Straight up the column from the origin: under a random walk the arrangement means
        // nothing, so the only requirement is that the cell is empty.
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

    // The two the game builds; the author's rooms are the ones between them.
    private bool IsFixedEnd(CTLevelRoom room) =>
        !_level.AuthoredLayout && _level.Rooms.Count > 0 &&
        (ReferenceEquals(room, _level.Rooms[0]) || ReferenceEquals(room, _level.Rooms[^1]));

    // A blueprint written before layouts existed has every room on cell (0, 0). The grid is how
    // rooms are seen and picked in both modes, so it has to mean something even when the shape does
    // not: rooms with no cells of their own are strung out in a line, in their own order.
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

    // Undo works on a snapshot of the whole grid rather than an inverse of each edit. The grid is a
    // handful of small structs and every edit renormalises the rooms around it, so an inverse would
    // have to carry the neighbours' sides too - at which point it is a snapshot with extra steps.
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

    // Deliberately no _editor.MarkEdited(): that is the *room* editor's dirty flag, and a level
    // edit is not an unsaved room. This screen keeps its own, against what was last written.
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

        // A drag whose release was never seen - the name dialog stops this update for a few frames
        // - would otherwise resume against a stale anchor and fling the room at the next press.
        if (_dragging && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) DropDrag();

        if (_ui.TransientUiOpen) return;
        if (_canvas.ConfirmOpen) return;

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            DeleteSelected();
            return;
        }

        // Polled, not EventSystem: Rewired drops right clicks.
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
            // Moved by reference: a redraw per frame would destroy the very rect being dragged.
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

        // Off the window or onto another room: the room goes back where it was. The rect is put
        // straight rather than left where the cursor dropped it.
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

    // Says it wherever it can be seen: the screen's own bar while it is up, the editor's otherwise.
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
