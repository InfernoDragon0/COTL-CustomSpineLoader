using CustomSpineLoader.MapEditor;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.ModUI.MenuEditor.Tools;

public class MenuPresetTool : IMapEditorTool
{
    public string Name => "Menu Presets";

    private const string Vanilla = "Vanilla (the game's own menu)";

    private readonly MainMenuEditor _editor;
    private MapEditorDropdown _presets;
    private TMP_Text _editing;
    private System.Collections.Generic.List<string> _names = [];

    public MenuPresetTool(MainMenuEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateHeader(panel, "Presets");

        _presets = ui.CreateDropdown(panel, "Menu", [], (index, _) =>
        {
            if (index <= 0)
            {
                UseVanilla();
                return;
            }

            if (index - 1 < _names.Count) Load(_names[index - 1]);
        });

        ui.CreateButton(panel, "New preset", () =>
        {
            _editor.ReloadPreset(new CTMenuPreset
            {
                PresetName = CTMenuPresetSerialization.FreeName("untitledmenu")
            });
            _editor.SetStatus("A new preset. Nothing is written until it is saved.");
        }, 36f);

        ui.CreateHeader(panel, "This preset");

        _editing = ui.CreateLabel(panel, "", 18).GetComponent<TMP_Text>();

        ui.CreateButton(panel, "Save as...", () =>
            MapNamePrompt.Show(_editor, _editor.Preset.PresetName, "Save the menu preset as",
                name =>
                {
                    var from = _editor.Preset.PresetName;
                    _editor.Preset.PresetName = name;

                    _editor.Save();

                    var copied = CTMenuPresetSerialization.CopyArt(from, name);
                    if (copied > 0)
                        _editor.SetStatus($"Saved as '{name}' with {copied} file(s) of art.",
                            StatusSeverity.Success);

                    _refreshQueued = true;
                },
                existsCheck: CTMenuPresetSerialization.Exists, existsNoun: "menu preset"),
            36f);

        Refresh();
    }

    private void Refresh()
    {
        if (_editing != null) _editing.text = "Editing: " + _editor.Preset.ShownName;
        if (_presets == null) return;

        _names = CTMenuPresetSerialization.ListNames();

        var options = new System.Collections.Generic.List<string>(_names.Count + 1) { Vanilla };
        options.AddRange(_names);
        _presets.SetOptions(options);

        var chosen = Plugin.MainMenuPreset?.Value ?? "";
        var index = string.IsNullOrWhiteSpace(chosen)
            ? 0
            : _names.FindIndex(n => string.Equals(n, chosen, System.StringComparison.OrdinalIgnoreCase)) + 1;

        _presets.SetSelected(Mathf.Max(index, 0));
    }

    private void UseVanilla()
    {
        if (Plugin.MainMenuPreset != null) Plugin.MainMenuPreset.Value = "";

        MenuPresetApplier.Use(null);
        MenuPresetApplier.RevertAll();

        _editor.SetStatus("The title screen is the game's own. Touching a control previews " +
                          $"'{_editor.Preset.ShownName}' again.");
    }

    private void Load(string name)
    {
        if (_editor.HasUnsavedEdits && _pendingLoad != name)
        {
            _pendingLoad = name;
            _refreshQueued = true;
            _editor.SetStatus($"'{_editor.Preset.ShownName}' has unsaved changes. Save it, or " +
                              "pick this again to discard them.", StatusSeverity.Warning);
            return;
        }

        _pendingLoad = null;

        var preset = CTMenuPresetSerialization.LoadByName(name);
        if (preset == null)
        {
            _editor.SetStatus($"'{name}' could not be read; the log says why.", StatusSeverity.Error);
            return;
        }

        if (Plugin.MainMenuPreset != null) Plugin.MainMenuPreset.Value = preset.PresetName;

        _editor.ReloadPreset(preset);
        _editor.SetStatus($"Editing '{preset.ShownName}', and the menu is wearing it.");
    }

    private string _pendingLoad;
    private bool _refreshQueued;

    public void OnEnter()
    {
        Refresh();
        _editor.SetStatus("Pick a preset to wear and edit it, or Vanilla for the game's own menu.");
    }

    public void OnExit() => _pendingLoad = null;

    public void OnUpdate()
    {
        if (!_refreshQueued) return;

        _refreshQueued = false;
        Refresh();
    }
}
