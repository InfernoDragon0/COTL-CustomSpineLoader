using System.Collections.Generic;
using CustomSpineLoader.MapEditor;
using UnityEngine;

namespace CustomSpineLoader.ModUI.MenuEditor.Tools;

public class MenuCentrepieceTool : IMapEditorTool
{
    public string Name => "Menu Centrepiece";

    private readonly MainMenuEditor _editor;
    private CTMenuCentrepiece Piece => _editor.Preset.Centrepiece;

    private MapEditorDropdown _assets;
    private MapEditorDropdown _skins;
    private MapEditorDropdown _animations;

    private static readonly string[] Sources = ["Menu", "Folder"];

    public MenuCentrepieceTool(MainMenuEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateHeader(panel, "Spine");

        ui.CreateToggle(panel, "Override the centrepiece", Piece.Enabled, on =>
        {
            Piece.Enabled = on;
            Touch(on ? "The title screen wears this preset's centrepiece." : "The lamb is back.");
        });

        var sourceNames = new List<string> { "Vanilla", "Custom spine" };

        var source = ui.CreateDropdown(panel, "Source", sourceNames, (index, _) =>
        {
            Piece.Source = Sources[Mathf.Clamp(index, 0, Sources.Length - 1)];
            Piece.Asset = "";
            Piece.Skin = "";
            Piece.Animation = "";
            Piece.Enabled = true;
            Touch("Source changed; pick what it should wear.");
            RefreshLists();
        });
        source.SetSelected(Mathf.Max(0, System.Array.IndexOf(Sources, Piece.Source)));

        _assets = ui.CreateDropdown(panel, "Spine", [], (_, name) =>
        {
            Piece.Asset = name;
            Piece.Enabled = true;
            Touch($"Centrepiece: {name}.");
            RefreshLists();
        });

        _skins = ui.CreateDropdown(panel, "Skin", [], (_, name) =>
        {
            Piece.Skin = name;
            Piece.Enabled = true;
            Touch($"Skin: {name}.");
        });

        _animations = ui.CreateDropdown(panel, "Animation", [], (_, name) =>
        {
            Piece.Animation = name;
            Piece.Enabled = true;
            Touch($"Animation: {name}.");
        });

        ui.CreateToggle(panel, "Loop the animation", Piece.Loop, v => { Piece.Loop = v; Touch(null); });
        ui.CreateSlider(panel, "Animation speed", 0f, 3f, Piece.TimeScale,
            v => { Piece.TimeScale = v; Touch(null); });

        ui.CreateHeader(panel, "Placement");

        ui.CreateSlider(panel, "Move X", -6f, 6f, Piece.Offset.X, v => { Piece.Offset.X = v; Touch(null); });
        ui.CreateSlider(panel, "Move Y", -6f, 6f, Piece.Offset.Y, v => { Piece.Offset.Y = v; Touch(null); });
        ui.CreateSlider(panel, "Move Z", -6f, 6f, Piece.Offset.Z, v => { Piece.Offset.Z = v; Touch(null); });

        ui.CreateSlider(panel, "Scale X", 0.1f, 4f, Piece.Scale.X, v => { Piece.Scale.X = v; Touch(null); });
        ui.CreateSlider(panel, "Scale Y", 0.1f, 4f, Piece.Scale.Y, v => { Piece.Scale.Y = v; Touch(null); });

        ui.CreateSlider(panel, "Turn", -180f, 180f, Piece.Rotation.Y,
            v => { Piece.Rotation.Y = v; Touch(null); });
        ui.CreateSlider(panel, "Lean", -90f, 90f, Piece.Rotation.X,
            v => { Piece.Rotation.X = v; Touch(null); });
        ui.CreateSlider(panel, "Tilt", -180f, 180f, Piece.Rotation.Z,
            v => { Piece.Rotation.Z = v; Touch(null); });

        ui.CreateButton(panel, "Reset centrepiece", () =>
        {
            _editor.Preset.Centrepiece = new CTMenuCentrepiece();
            _editor.Refresh();
            _editor.RebuildPanels();
            _editor.SetStatus("The centrepiece is back to the menu's own lamb.");
        }, 36f, MapEditorEmphasis.Quiet);

        RefreshLists();
    }

    private void RefreshLists()
    {
        if (_assets == null) return;

        var preset = _editor.Preset.PresetName;

        switch (Piece.Source)
        {
            case "Folder":
                var folders = MenuAssets.SpineFolderNames(preset);
                _assets.SetOptions(folders);
                Select(_assets, folders, Piece.Asset);
                if (folders.Count == 0)
                    _editor.SetStatus($"Drop a spine folder (.json + .atlas + png) into " +
                                      $"CustomMainMenus/{preset}.", StatusSeverity.Warning);
                break;

            default:
                _assets.SetOptions([]);
                break;
        }

        var skins = MenuSceneRefs.SkinNames();
        _skins.SetOptions(skins);
        Select(_skins, skins, Piece.Skin);

        var animations = MenuSceneRefs.AnimationNames();
        _animations.SetOptions(animations);
        Select(_animations, animations, Piece.Animation);
    }

    private static void Select(MapEditorDropdown dropdown, List<string> options, string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        var index = options.IndexOf(value);
        if (index >= 0) dropdown.SetSelected(index);
    }

    private void Touch(string message)
    {
        _editor.Refresh();
        if (message != null) _editor.SetStatus(message);
    }

    public void OnEnter()
    {
        RefreshLists();
        _editor.SetStatus("Move and scale are relative to where the game puts the lamb; 1 is untouched.");
    }

    public void OnExit() { }
    public void OnUpdate() { }
}
