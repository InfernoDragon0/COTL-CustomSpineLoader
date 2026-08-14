using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// Lists the saved blueprints under CustomNodeBlueprints/ and loads the chosen one. Each entry
// shows the save-time snapshot (<mapname>.png) when one exists. Loading clears the whole room,
// rebuilds it from the blueprint, closes the editor and walks the player in through the
// entrance door; press F4 afterwards to continue editing the loaded room.
public class LoadTool : IMapEditorTool
{
    public string Name => "Load Map";

    private readonly RuntimeMapEditor _editor;
    private readonly List<GameObject> _entries = [];
    private readonly List<Texture2D> _previews = [];

    private RectTransform _panel;
    private MapEditorUI _ui;

    public LoadTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _panel = panel;
        _ui = ui;

        ui.CreateButton(panel, "Refresh List", RefreshList);

        // Same browser as the structure tool, with much larger cells: the icon here is the
        // save-time screenshot of the whole room, which is unreadable at prop-icon size.
        _grid = ui.CreateIconGrid(panel, "MapGrid", columns: 2, cellSize: 168f);
    }

    private MapEditorGrid _grid;

    // Rebuilt on every entry so newly saved blueprints show up without a refresh press.
    public void OnEnter()
    {
        RefreshList();
        _editor.SetStatus("Pick a blueprint - this discards the current room.");
    }

    // Full-screen snapshots are megabytes of texture each; keep them alive only while the
    // panel is on screen.
    public void OnExit() => ClearEntries();

    public void OnUpdate() { }

    private void ClearEntries()
    {
        _grid?.Clear();

        foreach (var go in _entries)
            if (go != null) Object.Destroy(go);
        _entries.Clear();

        // Destroyed only after the cells that drew them are gone.
        foreach (var tex in _previews)
            if (tex != null) Object.Destroy(tex);
        _previews.Clear();
    }

    private void RefreshList()
    {
        ClearEntries();

        if (_panel == null || _ui == null || _grid == null) return;

        var results = MapEditorSerialization.LoadAll();

        if (results.Count == 0)
        {
            _entries.Add(_ui.CreateLabel(_panel, "No saved blueprints yet.", 16, TextAlignmentOptions.Center));
            return;
        }

        var entries = new List<MapEditorGrid.Entry>(results.Count);
        foreach (var result in results)
        {
            var captured = result;
            entries.Add(new MapEditorGrid.Entry
            {
                Id = captured.MapName,
                Display = captured.MapName,
                OnClick = () =>
                {
                    if (_editor.Loader.IsLoading)
                    {
                        _editor.SetStatus("Load already in progress.", StatusSeverity.Warning);
                        return;
                    }
                    // A manual load is not part of any running level; a stale run advancing on
                    // the next door would teleport the player into an unrelated room chain.
                    LevelPlayback.Stop();
                    _editor.Loader.Load(captured);
                }
            });
        }

        // Full-screen screenshots are megabytes each, so they are read as the cells appear
        // rather than all up front, and dropped again when the panel closes.
        _grid.Populate(_editor, entries, name =>
        {
            var preview = LoadPreview(name);
            if (preview == null) return;

            _previews.Add(preview);
            var sprite = Sprite.Create(preview, new Rect(0f, 0f, preview.width, preview.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MapPreview_" + name;
            _grid.SetCellIcon(name, sprite);
        }, perFrame: 2);
    }

    private static Texture2D LoadPreview(string mapName)
    {
        var path = MapEditorSerialization.SnapshotPathFor(mapName);
        if (path == null) return null;

        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(System.IO.File.ReadAllBytes(path)))
            {
                Object.Destroy(texture);
                return null;
            }
            return texture;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: snapshot '{path}' failed to load: {e.Message}");
            return null;
        }
    }
}
