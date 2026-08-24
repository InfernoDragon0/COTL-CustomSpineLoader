using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.WorldMap.Tools;

// Save, load, new, and the map-wide knobs.
public class WorldFileTool : IMapEditorTool
{
    private readonly WorldMapEditor _editor;

    public WorldFileTool(WorldMapEditor editor) => _editor = editor;

    public string Name => "World File";

    public void OnEnter() { }
    public void OnExit() { }
    public void OnUpdate() { }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        var map = _editor.Map;
        if (map == null) return;

        ui.CreateHeader(panel, "- Map -", 22);
        Note(ui, panel, $"Editing: {map.MapName}");

        // One button, the way the room editor's Save is also its rename: the dialog opens on the
        // current name, so confirming it saves and changing it saves a copy under the new one.
        // Ctrl+S is the same save with no dialog at all.
        ui.CreateButton(panel, "Save Map...", () =>
            MapNamePrompt.Show(_editor, map.MapName, "World map name", chosen =>
            {
                var previous = map.MapName;
                map.MapName = chosen;

                if (CTWorldMapSerialization.Save(map) == null)
                {
                    map.MapName = previous;
                    _editor.SetStatus("Save failed, see log.", StatusSeverity.Error);
                    return;
                }

                _editor.NoteSaved(map.MapName);

                if (string.Equals(previous, map.MapName, System.StringComparison.OrdinalIgnoreCase))
                {
                    _editor.Screen?.MarkSaved();
                    _editor.RebuildActivePanel();
                    _editor.SetStatus($"Saved '{map.MapName}'.", StatusSeverity.Success);
                    return;
                }

                // A map is its folder, so the new name is an empty folder until its art follows.
                var copied = CTWorldMapSerialization.CopyArt(previous, map.MapName);
                WorldMapAssets.ForgetFailures();

                _editor.Screen?.MarkSaved();
                _editor.RebuildAll();
                _editor.RebuildActivePanel();
                _editor.SetStatus(copied > 0
                    ? $"Saved as '{map.MapName}', with {copied} art file(s) copied over."
                    : $"Saved as '{map.MapName}'.", StatusSeverity.Success);
            }, existsCheck: CTWorldMapSerialization.Exists, existsNoun: "world map"));

        ui.CreateButton(panel, "New Map...", () =>
            MapNamePrompt.Show(_editor, "", "New world map", chosen =>
            {
                var fresh = new CTWorldMap { MapName = chosen };

                // Every map starts with its home node; nothing selectable means nothing testable.
                fresh.Nodes.Add(new CTWorldMapNode
                {
                    Id = "start",
                    DisplayName = "Start",
                    NodeType = "Base",
                    InitialState = "Selectable"
                });

                CTWorldMapSerialization.Save(fresh);
                _editor.SelectedLayerId = null;
                _editor.SelectedNodeId = null;
                _editor.History.Clear();
                _editor.Screen.SetMap(fresh);
                _editor.RebuildActivePanel();
                _editor.SetStatus($"Created '{fresh.MapName}'.");
            }, existsCheck: CTWorldMapSerialization.Exists, existsNoun: "world map"));

        var names = CTWorldMapSerialization.ListNames();
        if (names.Count > 0)
        {
            ui.CreateDropdown(panel, "Load Map", names, (index, _) =>
            {
                if (index < 0 || index >= names.Count) return;

                var loaded = CTWorldMapSerialization.LoadByName(names[index]);
                if (loaded == null)
                {
                    _editor.SetStatus($"'{names[index]}' failed to load.", StatusSeverity.Error);
                    return;
                }

                _editor.SelectedLayerId = null;
                _editor.SelectedNodeId = null;
                _editor.History.Clear();
                _editor.Screen.SetMap(loaded);
                _editor.RebuildActivePanel();
                _editor.SetStatus($"Loaded '{loaded.MapName}'. Unsaved changes to the previous map are gone.");
            });
        }

        ui.CreateHeader(panel, "- Looks -", 22);

        var colour = map.BackgroundColor ??= SerializableColor.From(new Color(0.05f, 0.06f, 0.1f));
        ui.CreateSlider(panel, "Background R", 0f, 1f, colour.R, value => { colour.R = value; ApplyBackground(); });
        ui.CreateSlider(panel, "Background G", 0f, 1f, colour.G, value => { colour.G = value; ApplyBackground(); });
        ui.CreateSlider(panel, "Background B", 0f, 1f, colour.B, value => { colour.B = value; ApplyBackground(); });

        ui.CreateSlider(panel, "Parallax strength", 0f, 2f, map.ParallaxStrength,
            value => map.ParallaxStrength = value);
        Note(ui, panel, "Parallax shows in play view, not while editing.");

        ui.CreateHeader(panel, "- Progress -", 22);
        Note(ui, panel, "Completion is per save slot, in CustomWorldMaps.");
        ui.CreateButton(panel, "Wipe this slot's progress", () =>
        {
            WorldMapProgress.WipeMap(map.MapName);
            _editor.SetStatus($"Progress for '{map.MapName}' wiped on this slot.");
        });
    }

    private void ApplyBackground() => _editor.Screen?.ApplyBackgroundColor();

    private static void Note(MapEditorUI ui, Transform parent, string text)
    {
        var label = ui.CreateLabel(parent, text, 15);
        label.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.7f);
    }
}
