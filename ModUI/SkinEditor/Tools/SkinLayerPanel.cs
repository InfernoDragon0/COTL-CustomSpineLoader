using System;
using System.Collections.Generic;
using System.Linq;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.SpineLoaderHelper;
using UnityEngine;

namespace CustomSpineLoader.ModUI.SkinEditor.Tools;

public class SkinLayerPanel : IMapEditorTool
{
    public string Name => "Layer";

    private const string NoImage = "No image (colour only)";

    private readonly FollowerSkinEditor _editor;

    private CTFollowerSkinDocument Document => _editor.Document;
    private FollowerSkinConfig Config => Document.Config;

    public SkinLayerPanel(FollowerSkinEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateButton(panel, "Refresh skin list", () =>
        {
            _editor.RebuildPanels();
            _editor.SetStatus("The skins, variants and images were read from the folders again.");
        }, 36f, MapEditorEmphasis.Quiet);

        var key = _editor.SelectedLayer;

        if (key == null || Config?.PartConfigs == null || !Config.PartConfigs.TryGetValue(key, out var part))
        {
            var idle = ui.CreateLabel(panel, "Select a layer for info.", 18);
            MapEditorUI.FitLabelHeight(idle);
            return;
        }

        var texture = TextureOf(key);
        var hasImage = texture != null;

        ui.CreateHeader(panel, part.PartName ?? key);

        var info = ui.CreateLabel(panel,
            $"Slot {part.SlotIndex} - {SkinParts.SlotName(part.SlotIndex)}", 16);
        MapEditorUI.FitLabelHeight(info);

        if (hasImage) ui.CreateImage(panel, texture, 150f);

        BuildImagePicker(panel, ui, key, part, hasImage);

        ui.CreateToggle(panel, "Hide " + (part.PartName ?? key), part.HideSlot, on =>
        {
            part.HideSlot = on;
            Touch(on ? $"'{part.PartName}' is hidden." : $"'{part.PartName}' is showing again.");
        });

        if (hasImage) BuildPlacement(panel, ui, part);

        BuildColour(panel, ui, part);

        ui.CreateButton(panel, "Remove layer", () =>
        {
            Config.PartConfigs.Remove(key);
            _editor.SelectedLayer = null;
            Touch($"Layer '{key}' removed.");
            _editor.RebuildPanels();
        }, 36f, MapEditorEmphasis.Quiet);
    }

    private void BuildImagePicker(RectTransform panel, MapEditorUI ui, string key,
        FollowerSkinPartConfig part, bool hasImage)
    {
        var used = new HashSet<string>(Config.PartConfigs.Keys, StringComparer.OrdinalIgnoreCase);
        var available = CTFollowerSkinSerialization.ImageNames(Document.SkinName, Document.VariantName)
            .Where(i => !used.Contains(i) || string.Equals(i, key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var options = new List<string> { NoImage };
        options.AddRange(available);

        var picker = ui.CreateDropdown(panel, hasImage ? key : NoImage, options, (index, _) =>
        {
            var wanted = index > 0 && index - 1 < available.Count ? available[index - 1] : null;

            if (wanted == null)
            {
                if (!hasImage) return;
                if (!SkinParts.Rekey(Config, key,
                        SkinParts.FreeKey(Config, "color_" + (part.PartName ?? "part").ToLowerInvariant())))
                    return;

                _editor.SelectedLayer = null;
                Touch($"'{part.PartName}' only recolours now; no image covers it.");
            }
            else
            {
                if (string.Equals(wanted, key, StringComparison.OrdinalIgnoreCase)) return;
                if (!SkinParts.Rekey(Config, key, wanted)) return;

                _editor.SelectedLayer = wanted;
                Touch($"'{wanted}.png' now covers '{part.PartName}'.");
            }

            _editor.RebuildPanels();
        });

        picker.SetSelected(hasImage ? Mathf.Max(0, available.IndexOf(key) + 1) : 0);

        if (available.Count > 0) return;

        var hint = ui.CreateLabel(panel,
            $"Drop PNG files into FollowerSkins/{Document.SkinName}/{Document.VariantName} to pick them here.", 16);
        MapEditorUI.FitLabelHeight(hint);
    }

    private void BuildPlacement(RectTransform panel, MapEditorUI ui, FollowerSkinPartConfig part)
    {
        ui.CreateSlider(panel, "Scale X", 0.1f, 3f, part.ScaleX, v => { part.ScaleX = v; Touch(null); });
        ui.CreateSlider(panel, "Scale Y", 0.1f, 3f, part.ScaleY, v => { part.ScaleY = v; Touch(null); });
        ui.CreateSlider(panel, "Rotation", -180f, 180f, part.Rotation, v => { part.Rotation = v; Touch(null); });
        ui.CreateSlider(panel, "Offset X", -1f, 1f, part.OffsetX, v => { part.OffsetX = v; Touch(null); });
        ui.CreateSlider(panel, "Offset Y", -1f, 1f, part.OffsetY, v => { part.OffsetY = v; Touch(null); });

        ui.CreateButton(panel, "Reset placement", () =>
        {
            part.ScaleX = 1f;
            part.ScaleY = 1f;
            part.Rotation = -90f;
            part.OffsetX = 0f;
            part.OffsetY = 0f;
            Touch("The image sits where the game's own part would.");
            _editor.RebuildPanels();
        }, 36f, MapEditorEmphasis.Quiet);
    }

    private void BuildColour(RectTransform panel, MapEditorUI ui, FollowerSkinPartConfig part)
    {
        var set = Mathf.Clamp(_editor.ColourSet, 0, Document.ColourSetCount - 1);
        part.ColorChoices ??= ["#FFFFFF"];
        while (part.ColorChoices.Count <= set) part.ColorChoices.Add("#FFFFFF");

        var colour = FollowerSpineLoader.HexToColor(part.ColorChoices[set]);

        ui.CreateHeader(panel, $"Colour (set {set + 1})", 20);

        ui.CreateSlider(panel, "Red", 0f, 1f, colour.r, v => { colour.r = v; Write(part, set, colour); });
        ui.CreateSlider(panel, "Green", 0f, 1f, colour.g, v => { colour.g = v; Write(part, set, colour); });
        ui.CreateSlider(panel, "Blue", 0f, 1f, colour.b, v => { colour.b = v; Write(part, set, colour); });

        ui.CreateButton(panel, "Reset colour", () =>
        {
            Write(part, set, Color.white);
            Touch("The part shows its own colours.");
            _editor.RebuildPanels();
        }, 36f, MapEditorEmphasis.Quiet);
    }

    private void Write(FollowerSkinPartConfig part, int set, Color colour)
    {
        while (part.ColorChoices.Count <= set) part.ColorChoices.Add("#FFFFFF");
        part.ColorChoices[set] = "#" + ColorUtility.ToHtmlStringRGB(colour);
        Touch(null);
    }

    private Texture2D TextureOf(string key) =>
        _editor.Preview.TextureFor(
            CTFollowerSkinSerialization.ImagePath(Document.SkinName, Document.VariantName, key));

    private void Touch(string message)
    {
        _editor.Refresh();
        if (message != null) _editor.SetStatus(message);
    }

    public void OnEnter() { }
    public void OnExit() { }
    public void OnUpdate() { }
}
