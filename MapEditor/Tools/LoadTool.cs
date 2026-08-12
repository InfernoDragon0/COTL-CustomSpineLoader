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

        ui.CreateLabel(panel, "Load Map", 20, TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Loading clears the room, rebuilds it,\ncloses the editor and walks the player in.",
            14, TextAlignmentOptions.Center);
        ui.CreateButton(panel, "Refresh List", RefreshList);
    }

    // Rebuilt on every entry so newly saved blueprints show up without a refresh press.
    public void OnEnter()
    {
        RefreshList();
        _editor.SetStatus("Load Map: pick a blueprint. This discards the current room state.");
    }

    // Full-screen snapshots are megabytes of texture each; keep them alive only while the
    // panel is on screen.
    public void OnExit() => ClearEntries();

    public void OnUpdate() { }

    private void ClearEntries()
    {
        foreach (var go in _entries)
            if (go != null) Object.Destroy(go);
        _entries.Clear();

        foreach (var tex in _previews)
            if (tex != null) Object.Destroy(tex);
        _previews.Clear();
    }

    private void RefreshList()
    {
        ClearEntries();

        if (_panel == null || _ui == null) return;

        var results = MapEditorSerialization.LoadAll();

        if (results.Count == 0)
        {
            _entries.Add(_ui.CreateLabel(_panel, "No saved blueprints yet.", 14, TextAlignmentOptions.Center));
            return;
        }

        foreach (var result in results)
        {
            var captured = result;

            var preview = LoadPreview(captured.MapName);
            if (preview != null)
            {
                _previews.Add(preview);
                _entries.Add(_ui.CreateImage(_panel, preview));
            }

            _entries.Add(_ui.CreateButton(_panel, captured.MapName, () =>
            {
                if (_editor.Loader.IsLoading)
                {
                    _editor.SetStatus("A load is already in progress.");
                    return;
                }
                // A manual load is not part of any running level; a stale run advancing on the
                // next door would teleport the player into an unrelated room chain.
                LevelPlayback.Stop();
                _editor.Loader.Load(captured);
            }));
        }
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
