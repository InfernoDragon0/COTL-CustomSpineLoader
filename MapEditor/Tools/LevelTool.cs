using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

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
            if (go != null) UnityEngine.Object.Destroy(go);
        _dynamic.Clear();

        if (_panel == null || _ui == null) return;

        if (_level == null) BuildChooser();
        else BuildLevelEditor();
    }

    private void BuildChooser()
    {
        _dynamic.Add(_ui.CreateButton(_panel, "New Level Blueprint", CreateNew));

        var levels = CTLevelSerialization.LoadAll();
        if (levels.Count == 0)
        {
            _dynamic.Add(_ui.CreateLabel(_panel, "No level blueprints yet.", 14, TextAlignmentOptions.Center));
            return;
        }

        var labels = new List<string>(levels.Count);
        foreach (var level in levels) labels.Add($"{level.LevelName} ({level.Rooms.Count} rooms)");

        AddDropdown("Open Existing Level", labels, index =>
        {
            if (index < 0 || index >= levels.Count) return;

            _level = levels[index];
            EnsureEndRooms(_level);
            _selectedRoom = -1;
            Rebuild();
            _editor.SetStatus($"Opened level '{_level.LevelName}'.");
        });
    }

    // Every list here is rebuilt from scratch after each pick, so a dropdown's Root has to join
    // the dynamic set or the old widget is left sitting on top of the new one. Picking from a
    // dropdown that Rebuild then destroys is safe: it closes its own overlay and reads nothing
    // more once the handler returns.
    private void AddDropdown(string caption, IList<string> options, Action<int> onPicked,
        int selected = -1)
    {
        var dropdown = _ui.CreateDropdown(_panel, caption, options, (index, _) => onPicked(index));
        dropdown.SetSelected(selected);
        _dynamic.Add(dropdown.Root);
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

        _dynamic.Add(_ui.CreateHeader(_panel, "Rooms"));

        var roomLabels = new List<string>(_level.Rooms.Count);
        for (var i = 0; i < _level.Rooms.Count; i++)
        {
            var room = _level.Rooms[i];
            var pool = room.NodePool.Count == 0 ? "any" : room.NodePool.Count + " node(s)";
            roomLabels.Add($"{i + 1}: {room.Role} (pool: {pool})");
        }

        // Pre-selected, so after the rebuild the closed dropdown still names the room being
        // edited - that is what the old "<" marker was for.
        AddDropdown("Select a room", roomLabels, index =>
        {
            _selectedRoom = index;
            Rebuild();
        }, _selectedRoom);

        if (_selectedRoom < 0 || _selectedRoom >= _level.Rooms.Count) return;

        var selected = _level.Rooms[_selectedRoom];

        // Labelled rather than bare values: a closed dropdown reading just "Combat" says nothing
        // about what it sets.
        var modifierLabels = new List<string>(Modifiers.Length);
        foreach (var modifier in Modifiers) modifierLabels.Add("Modifier: " + modifier);

        AddDropdown("Modifier", modifierLabels, index =>
        {
            if (index < 0 || index >= Modifiers.Length) return;

            selected.Modifier = Modifiers[index];
            Rebuild();
            _editor.SetStatus($"Room {_selectedRoom + 1} modifier: {selected.Modifier}.");
        }, Array.IndexOf(Modifiers, selected.Modifier));

        _dynamic.Add(_ui.CreateLabel(_panel,
            $"Room {_selectedRoom + 1} pool\n(empty pool = any saved node)", 14, TextAlignmentOptions.Center));

        BuildPoolControls(selected);
    }

    private static readonly string[] Modifiers = ["None", "Combat", "Reward"];

    private const string VanillaLabel = "Vanilla generated room";

    // The pool is a set the author builds up, so it takes the trigger tool's shape rather than a
    // checkbox per saved blueprint: one dropdown offering what is not in the pool yet, and a row
    // per member that removes it. The old list grew by a button for every map ever saved.
    private void BuildPoolControls(CTLevelRoom room)
    {
        var candidateKeys = new List<string>();
        var candidateLabels = new List<string>();

        // A pool can also offer the room the game would have generated, so a level mixes
        // authored rooms with vanilla ones.
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
            AddDropdown("Add To Pool", candidateLabels, index =>
            {
                if (index < 0 || index >= candidateKeys.Count) return;

                room.NodePool.Add(candidateKeys[index]);
                Rebuild();
                _editor.SetStatus($"Room {_selectedRoom + 1} pool: {room.NodePool.Count} node(s).");
            });
        else
            _dynamic.Add(_ui.CreateLabel(_panel, "Everything saved is already in this pool.", 14,
                TextAlignmentOptions.Center));

        if (room.NodePool.Count == 0)
        {
            _dynamic.Add(_ui.CreateLabel(_panel, "Pool empty - any saved node can appear here.", 14,
                TextAlignmentOptions.Center));
            return;
        }

        // Copied: removing walks the list these rows were built from.
        foreach (var entry in new List<string>(room.NodePool))
        {
            var captured = entry;
            var label = captured == CTLevelRoom.VanillaNode ? VanillaLabel : captured;
            _dynamic.Add(_ui.CreateButton(_panel, "X  " + label, () =>
            {
                room.NodePool.Remove(captured);
                Rebuild();
                _editor.SetStatus($"Room {_selectedRoom + 1} pool: {room.NodePool.Count} node(s).");
            }));
        }
    }

    private void PlayLevel()
    {
        EnsureEndRooms(_level);
        var error = LevelPlayback.Start(_level, _editor);
        if (error != null)
        {
            _editor.SetStatus(error, StatusSeverity.Error);
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
            _editor.SetStatus("A level always keeps its entrance and exit rooms.", StatusSeverity.Warning);
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
            _editor.SetStatus($"'{_level.LevelName}.json' already exists - press Save Level again to overwrite.",
                StatusSeverity.Warning);
            return;
        }
        _saveArmed = false;

        EnsureEndRooms(_level);
        var path = CTLevelSerialization.Save(_level);
        _editor.SetStatus(path != null ? "Level saved to " + path : "Level save failed, see log.",
            path != null ? StatusSeverity.Success : StatusSeverity.Error);
    }
}
