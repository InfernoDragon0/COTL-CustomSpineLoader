using CustomSpineLoader.MapEditor;
using UnityEngine;

namespace CustomSpineLoader.ModUI.MenuEditor.Tools;

public class MenuLookTool : IMapEditorTool
{
    public string Name => "Menu Look";

    private readonly MainMenuEditor _editor;
    private CTMenuLook Look => _editor.Preset.Look;

    public MenuLookTool(MainMenuEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateHeader(panel, "Palette");

        ui.CreateToggle(panel, "Override palette", Look.Enabled, on =>
        {
            Look.Enabled = on;
            Touch(on
                ? "The menu wears this preset's palette."
                : "The menu is back to the game's own palette.");
        });

        var palettes = MenuAssets.PaletteNames();
        var picker = ui.CreateDropdown(panel, "Palette", palettes, (index, name) =>
        {
            Look.Palette = name;
            Touch(Look.Enabled
                ? $"Palette: {name}. Raise Blend to see it."
                : $"Palette: {name}. Switch the override on to see it.");
        });

        var current = palettes.IndexOf(Look.Palette ?? "");
        if (current >= 0) picker.SetSelected(current);

        ui.CreateSlider(panel, "Blend", 0f, 1f, Look.Blend, v => { Look.Blend = v; Touch(null); });

        ui.CreateButton(panel, "Reset palette", () =>
        {
            var look = _editor.Preset.Look;
            var fresh = new CTMenuLook();

            look.Palette = fresh.Palette;
            look.Blend = fresh.Blend;
            look.HueShift = fresh.HueShift;
            look.Saturation = fresh.Saturation;
            look.Brightness = fresh.Brightness;

            MenuSceneRefs.RestoreLook();
            _editor.Refresh();
            _editor.RebuildPanels();
            _editor.SetStatus("The palette is back to the game's own.");
        }, 36f, MapEditorEmphasis.Quiet);

        ui.CreateHeader(panel, "Background");

        ui.CreateToggle(panel, "Override background color", Look.OverrideClear, on =>
        {
            Look.OverrideClear = on;
            Touch(on
                ? "The background is this preset's color."
                : "The background is the game's own color.");
        });

        ui.CreateSlider(panel, "Red", 0f, 1f, Look.ClearColor.R, v => { Look.ClearColor.R = v; Cleared(); });
        ui.CreateSlider(panel, "Green", 0f, 1f, Look.ClearColor.G, v => { Look.ClearColor.G = v; Cleared(); });
        ui.CreateSlider(panel, "Blue", 0f, 1f, Look.ClearColor.B, v => { Look.ClearColor.B = v; Cleared(); });

        ui.CreateToggle(panel, "Apply negative effect", Look.Background, on =>
        {
            Look.Background = on;
            Touch(on
                ? "The background is inverted; the lamb and the text are untouched."
                : "The background is the game's own.");
        });

        ui.CreateSlider(panel, "Negative strength", 0f, 1f, Look.BackgroundStrength, v =>
        {
            Look.BackgroundStrength = v;
            Touch(null);
        });

        ui.CreateHeader(panel, "Effects");

        ui.CreateSlider(panel, "Effect strength", 0f, 1f, Look.EffectIntensity,
            v => { Look.EffectIntensity = v; Touch(null); });

        ui.CreateSlider(panel, "Hue", -180f, 180f, Look.HueShift,
            v => { Look.HueShift = v; Touch(null); });
        ui.CreateSlider(panel, "Saturation", 0f, 2f, Look.Saturation,
            v => { Look.Saturation = v; Touch(null); });
        ui.CreateSlider(panel, "Brightness", 0f, 2f, Look.Brightness,
            v => { Look.Brightness = v; Touch(null); });

        ui.CreateToggle(panel, "Dither", Look.Dither, v => { Look.Dither = v; Touch(null); });
        ui.CreateToggle(panel, "Grain", Look.Grain, v => { Look.Grain = v; Touch(null); });
        ui.CreateToggle(panel, "Glitch", Look.Glitch, v => { Look.Glitch = v; Touch(null); });
    }

    private void Cleared()
    {
        Look.OverrideClear = true;
        Touch(null);
    }

    private void Touch(string message)
    {
        _editor.Refresh();
        if (message != null) _editor.SetStatus(message);
    }

    public void OnEnter() =>
        _editor.SetStatus("Blend is how much of the chosen palette shows; 0 is the game's own menu.");

    public void OnExit() { }
    public void OnUpdate() { }
}
