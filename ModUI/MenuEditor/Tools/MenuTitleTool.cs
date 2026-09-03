using CustomSpineLoader.MapEditor;
using UnityEngine;

namespace CustomSpineLoader.ModUI.MenuEditor.Tools;

public class MenuTitleTool : IMapEditorTool
{
    public string Name => "Menu Title";

    private const string VanillaLogo = "Vanilla logo";

    private readonly MainMenuEditor _editor;
    private CTMenuTitle Title => _editor.Preset.Title;

    private MapEditorDropdown _images;
    private System.Collections.Generic.List<string> _imageNames = [];

    private readonly System.Collections.Generic.List<GameObject> _colourRows = [];

    public MenuTitleTool(MainMenuEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _colourRows.Clear();

        ui.CreateHeader(panel, "Title");

        ui.CreateToggle(panel, "Override title", Title.Enabled, on =>
        {
            Title.Enabled = on;
            Touch(on ? "The title screen wears this preset's logo." : "The game's logo is back.");
        });

        _images = ui.CreateDropdown(panel, "Image", [], (index, name) =>
        {
            Title.Image = index <= 0 ? "" : name;
            Title.Enabled = true;
            Touch(index <= 0 ? "Using the game's own logo art." : $"Title: {name}.");
        });

        ui.CreateToggle(panel, "Hide the title", Title.Hidden, v =>
        {
            Title.Hidden = v;
            Title.Enabled = true;
            Touch(v ? "The title is hidden." : "The title is shown.");
        });

        ui.CreateHeader(panel, "Placement");

        ui.CreateToggle(panel, "Keep the image's shape", Title.PreserveAspect, v =>
        {
            Title.PreserveAspect = v;
            Touch(v
                ? "The image keeps its own proportions."
                : "The image is stretched to the game's logo box.");
        });

        ui.CreateSlider(panel, "Move X", -900f, 900f, Title.Offset.X,
            v => { Title.Offset.X = v; Title.Enabled = true; Touch(null); });
        ui.CreateSlider(panel, "Move Y", -600f, 600f, Title.Offset.Y,
            v => { Title.Offset.Y = v; Title.Enabled = true; Touch(null); });
        ui.CreateSlider(panel, "Size", 0.1f, 3f, Title.Scale,
            v => { Title.Scale = v; Title.Enabled = true; Touch(null); });

        ui.CreateHeader(panel, "Edition");

        ui.CreateButton(panel, "Set text...", () =>
            MapNamePrompt.Show(_editor,
                string.IsNullOrEmpty(Title.EditionText)
                    ? MenuSceneRefs.Title.EditionText ?? ""
                    : Title.EditionText,
                "The line under the title",
                text =>
                {
                    Title.EditionText = text;
                    Touch($"Edition line: {text}");
                },
                characterLimit: 60),
            36f);

        ui.CreateButton(panel, "Reset text", () =>
        {
            Title.EditionText = "";
            Touch("The edition line says what the game says.");
        }, 36f, MapEditorEmphasis.Quiet);

        ui.CreateToggle(panel, "Hide edition", Title.HideEdition, v =>
        {
            Title.HideEdition = v;
            Touch(v ? "The edition line is hidden." : "The edition line is shown.");
        });

        ui.CreateToggle(panel, "Override color", Title.TintEdition, v =>
        {
            Title.TintEdition = v;
            ShowColourRows(v);
            Touch(v ? null : "The edition line keeps the game's color.");
        });

        _colourRows.Add(ui.CreateSlider(panel, "Red", 0f, 1f, Title.EditionColor.R,
            v => { Title.EditionColor.R = v; Touch(null); }));
        _colourRows.Add(ui.CreateSlider(panel, "Green", 0f, 1f, Title.EditionColor.G,
            v => { Title.EditionColor.G = v; Touch(null); }));
        _colourRows.Add(ui.CreateSlider(panel, "Blue", 0f, 1f, Title.EditionColor.B,
            v => { Title.EditionColor.B = v; Touch(null); }));

        ui.CreateSlider(panel, "Move X", -900f, 900f, Title.EditionOffset.X,
            v => { Title.EditionOffset.X = v; Touch(null); });
        ui.CreateSlider(panel, "Move Y", -600f, 600f, Title.EditionOffset.Y,
            v => { Title.EditionOffset.Y = v; Touch(null); });

        ui.CreateButton(panel, "Reset title", () =>
        {
            _editor.Preset.Title = new CTMenuTitle();
            _editor.Refresh();
            _editor.RebuildPanels();
            _editor.SetStatus("The title and the edition line are back to the game's own.");
        }, 36f, MapEditorEmphasis.Quiet);

        ShowColourRows(Title.TintEdition);
        RefreshImages();
    }

    private void ShowColourRows(bool shown)
    {
        foreach (var row in _colourRows)
            if (row != null)
                row.SetActive(shown);

        _editor.RequestOptionsResize();
    }

    private void RefreshImages()
    {
        if (_images == null) return;

        var preset = _editor.Preset.PresetName;
        _imageNames = MenuAssets.ImageNames(preset);

        var options = new System.Collections.Generic.List<string>(_imageNames.Count + 1) { VanillaLogo };
        options.AddRange(_imageNames);
        _images.SetOptions(options);

        var index = string.IsNullOrEmpty(Title.Image) ? 0 : _imageNames.IndexOf(Title.Image) + 1;
        _images.SetSelected(Mathf.Max(index, 0));

        if (_imageNames.Count == 0)
            _editor.SetStatus($"Drop a png into CustomMainMenus/{preset} to use it as the title.");
    }

    private void Touch(string message)
    {
        _editor.Refresh();
        if (message != null) _editor.SetStatus(message);
    }

    public void OnEnter()
    {
        RefreshImages();
        _editor.SetStatus("Any png in the preset's folder can be the title.");
    }

    public void OnExit() { }
    public void OnUpdate() { }
}
