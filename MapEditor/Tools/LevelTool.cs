using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// Authors CTLevelBlueprints: the room chain a custom level generates from. Create or open a
// level, set how many rooms it has, and pick which CTNodeBlueprints each room may generate
// from. The first room is always the Entrance and the last always the Exit - added rooms go
// between them, and neither end can be removed.
//
// Play Level runs the open level: every room slot resolves to one node blueprint from its
// pool, the entrance room loads, and each door walks the player into the next room in the
// chain (LevelPlayback owns that door redirection).
public class LevelTool : IMapEditorTool
{
    public string Name => "Level";

    private readonly RuntimeMapEditor _editor;
    private readonly List<GameObject> _dynamic = [];

    private RectTransform _panel;
    private MapEditorUI _ui;

    private CTLevelBlueprint _level;
    private int _selectedRoom = -1;
    private bool _saveArmed;
    private float _saveArmedAt;
    private const float SaveArmWindow = 5f;

    public LevelTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _panel = panel;
        _ui = ui;

        ui.CreateLabel(panel, "Level Blueprint", 20, TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "A level is a chain of rooms, each\ngenerated from a node blueprint pool.",
            14, TextAlignmentOptions.Center);
    }

    public void OnEnter()
    {
        Rebuild();
        _editor.SetStatus(_level == null
            ? "Level: create a new blueprint or open an existing one."
            : $"Editing level '{_level.LevelName}'.");
    }

    public void OnExit() { }

    public void OnUpdate()
    {
        if (_saveArmed && Time.unscaledTime - _saveArmedAt > SaveArmWindow) _saveArmed = false;
    }

    // The whole lower panel is dynamic: it shows either the open/create chooser or the loaded
    // level's editing controls, and is rebuilt after every structural change.
    private void Rebuild()
    {
        foreach (var go in _dynamic)
            if (go != null) Object.Destroy(go);
        _dynamic.Clear();

        if (_panel == null || _ui == null) return;

        if (_level == null) BuildChooser();
        else BuildLevelEditor();
    }

    private void BuildChooser()
    {
        _dynamic.Add(_ui.CreateButton(_panel, "New Level Blueprint", CreateNew));
        _dynamic.Add(_ui.CreateLabel(_panel, "— Open Existing —", 14, TextAlignmentOptions.Center));

        var levels = CTLevelSerialization.LoadAll();
        if (levels.Count == 0)
        {
            _dynamic.Add(_ui.CreateLabel(_panel, "No level blueprints yet.", 14, TextAlignmentOptions.Center));
            return;
        }

        foreach (var level in levels)
        {
            var captured = level;
            _dynamic.Add(_ui.CreateButton(_panel, $"{captured.LevelName} ({captured.Rooms.Count} rooms)", () =>
            {
                _level = captured;
                EnsureEndRooms(_level);
                _selectedRoom = -1;
                Rebuild();
                _editor.SetStatus($"Opened level '{_level.LevelName}'.");
            }));
        }
    }

    private void BuildLevelEditor()
    {
        _dynamic.Add(_ui.CreateLabel(_panel, "Level: " + _level.LevelName, 16, TextAlignmentOptions.Center));

        _dynamic.Add(_ui.CreateButton(_panel, "Rename Level", () =>
            _editor.PromptText("level name", _level.LevelName, value =>
            {
                _level.LevelName = MapEditorSerialization.Sanitize(
                    string.IsNullOrWhiteSpace(value) ? _level.LevelName : value);
                Rebuild();
                _editor.SetStatus($"Level renamed to '{_level.LevelName}'.");
            })));

        _dynamic.Add(_ui.CreateButton(_panel, "Save Level", SaveLevel));
        _dynamic.Add(_ui.CreateButton(_panel, "Play Level", PlayLevel));
        _dynamic.Add(_ui.CreateButton(_panel, "Close Level", () =>
        {
            _level = null;
            _selectedRoom = -1;
            Rebuild();
            _editor.SetStatus("Level closed.");
        }));

        _dynamic.Add(_ui.CreateLabel(_panel,
            $"Rooms: {_level.Rooms.Count} (entrance and exit are fixed)", 14, TextAlignmentOptions.Center));
        _dynamic.Add(_ui.CreateButton(_panel, "Add Room", AddRoom));
        _dynamic.Add(_ui.CreateButton(_panel, "Remove Room", RemoveRoom));

        _dynamic.Add(_ui.CreateLabel(_panel, "— Rooms —", 14, TextAlignmentOptions.Center));
        for (var i = 0; i < _level.Rooms.Count; i++)
        {
            var index = i;
            var room = _level.Rooms[i];
            var marker = index == _selectedRoom ? " <" : "";
            var pool = room.NodePool.Count == 0 ? "any" : room.NodePool.Count.ToString();
            _dynamic.Add(_ui.CreateButton(_panel, $"{index + 1}: {room.Role} (pool: {pool}){marker}", () =>
            {
                _selectedRoom = index;
                Rebuild();
            }));
        }

        if (_selectedRoom < 0 || _selectedRoom >= _level.Rooms.Count) return;

        var selected = _level.Rooms[_selectedRoom];

        _dynamic.Add(_ui.CreateButton(_panel, $"Modifier: {selected.Modifier}", () =>
        {
            selected.Modifier = selected.Modifier switch
            {
                "None" => "Combat",
                "Combat" => "Reward",
                _ => "None"
            };
            Rebuild();
            _editor.SetStatus($"Room {_selectedRoom + 1} modifier: {selected.Modifier}.");
        }));

        _dynamic.Add(_ui.CreateLabel(_panel,
            $"Room {_selectedRoom + 1} pool\n(empty pool = any saved node)", 14, TextAlignmentOptions.Center));

        // A pool can also offer the room the game would have generated, so a level mixes
        // authored rooms with vanilla ones.
        var vanillaInPool = selected.NodePool.Contains(CTLevelRoom.VanillaNode);
        _dynamic.Add(_ui.CreateButton(_panel, (vanillaInPool ? "[x] " : "[  ] ") + "Vanilla generated room", () =>
        {
            if (vanillaInPool) selected.NodePool.Remove(CTLevelRoom.VanillaNode);
            else selected.NodePool.Add(CTLevelRoom.VanillaNode);
            Rebuild();
        }));

        var nodes = MapEditorSerialization.LoadAll();
        if (nodes.Count == 0)
        {
            _dynamic.Add(_ui.CreateLabel(_panel, "No node blueprints saved yet.", 14, TextAlignmentOptions.Center));
            return;
        }

        foreach (var node in nodes)
        {
            var name = node.MapName;
            var inPool = selected.NodePool.Contains(name);
            _dynamic.Add(_ui.CreateButton(_panel, (inPool ? "[x] " : "[  ] ") + name, () =>
            {
                if (inPool) selected.NodePool.Remove(name);
                else selected.NodePool.Add(name);
                Rebuild();
            }));
        }
    }

    // Resolves the whole chain, re-enters the dungeon scene fresh (same flow as Reset/F5) and
    // loads the entrance room once the new scene has generated. Walking through doors then
    // advances the chain until the exit room's door ends it.
    private void PlayLevel()
    {
        EnsureEndRooms(_level);
        var error = LevelPlayback.Start(_level, _editor);
        if (error != null)
        {
            _editor.SetStatus(error);
            return;
        }
        _editor.SetStatus($"Playing level {LevelPlayback.Describe()} - re-entering dungeon.");
    }

    private void CreateNew()
    {
        _level = new CTLevelBlueprint
        {
            LevelName = FreeName(),
            Rooms =
            [
                new CTLevelRoom { Role = "Entrance" },
                new CTLevelRoom { Role = "Exit" }
            ]
        };
        _selectedRoom = -1;
        Rebuild();
        _editor.SetStatus($"Created level '{_level.LevelName}'. Add rooms and assign node pools.");
    }

    private static string FreeName()
    {
        for (var i = 1; i < 1000; i++)
        {
            var candidate = "untitledlevel" + i;
            if (!CTLevelSerialization.Exists(candidate)) return candidate;
        }
        return "untitledlevel";
    }

    private void AddRoom()
    {
        // Between the fixed ends, never after the exit.
        _level.Rooms.Insert(_level.Rooms.Count - 1, new CTLevelRoom { Role = "Normal" });
        Rebuild();
    }

    private void RemoveRoom()
    {
        if (_level.Rooms.Count <= 2)
        {
            _editor.SetStatus("A level always keeps its entrance and exit rooms.");
            return;
        }

        _level.Rooms.RemoveAt(_level.Rooms.Count - 2);
        if (_selectedRoom >= _level.Rooms.Count) _selectedRoom = -1;
        Rebuild();
    }

    // Older or hand-edited files could miss the invariant; repair rather than trusting it.
    private static void EnsureEndRooms(CTLevelBlueprint level)
    {
        if (level.Rooms.Count == 0)
        {
            level.Rooms.Add(new CTLevelRoom { Role = "Entrance" });
            level.Rooms.Add(new CTLevelRoom { Role = "Exit" });
        }
        else if (level.Rooms.Count == 1)
        {
            level.Rooms[0].Role = "Entrance";
            level.Rooms.Add(new CTLevelRoom { Role = "Exit" });
        }
        else
        {
            level.Rooms[0].Role = "Entrance";
            level.Rooms[^1].Role = "Exit";
            for (var i = 1; i < level.Rooms.Count - 1; i++)
                level.Rooms[i].Role = "Normal";
        }
    }

    private void SaveLevel()
    {
        if (!_saveArmed && CTLevelSerialization.Exists(_level.LevelName))
        {
            _saveArmed = true;
            _saveArmedAt = Time.unscaledTime;
            _editor.SetStatus($"'{_level.LevelName}.json' already exists - press Save Level again to overwrite.");
            return;
        }
        _saveArmed = false;

        EnsureEndRooms(_level);
        var path = CTLevelSerialization.Save(_level);
        _editor.SetStatus(path != null ? "Level saved to " + path : "Level save failed, see log.");
    }
}
