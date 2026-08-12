using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// Picks the music for this node blueprint from every music event in the loaded FMOD banks.
// Selecting a track plays it immediately as the preview; the choice is saved on the blueprint
// and replayed when it loads. Empty selection keeps the vanilla biome music.
public class MusicTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Music";

    private const string MusicPrefix = "event:/music";

    private readonly RuntimeMapEditor _editor;
    private readonly List<GameObject> _entries = [];

    private static List<string> _musicEvents;

    private RectTransform _panel;
    private MapEditorUI _ui;
    private TMP_Text _currentLabel;

    public MusicTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _panel = panel;
        _ui = ui;

        ui.CreateLabel(panel, "Music", 20, TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Selecting a track previews it and\nsaves it into this blueprint.",
            14, TextAlignmentOptions.Center);

        _currentLabel = ui.CreateLabel(panel, "Current: vanilla", 14, TextAlignmentOptions.Center)
            .GetComponent<TMP_Text>();

        ui.CreateButton(panel, "Clear (vanilla music)", () =>
        {
            _editor.Map.MusicEvent = "";
            UpdateCurrentLabel();
            _editor.SetStatus("Blueprint music cleared; vanilla music resumes on the next room change.");
        });

        ui.CreateToggle(panel, "Loop music", _editor.Map.MusicLoop, v =>
        {
            _editor.Map.MusicLoop = v;
            _editor.SetStatus(v
                ? "Track restarts whenever it finishes."
                : "Track plays as authored (one-shots end and stay silent).");
        });
    }

    public void OnEnter()
    {
        BuildList();
        UpdateCurrentLabel();
        _editor.SetStatus("Music: pick a track to preview and assign it to this blueprint.");
    }

    public void OnExit() { }
    public void OnUpdate() { }

    private void UpdateCurrentLabel()
    {
        if (_currentLabel == null) return;
        var current = _editor.Map.MusicEvent;
        _currentLabel.text = "Current: " + (string.IsNullOrEmpty(current) ? "vanilla" : ShortName(current));
    }

    private void BuildList()
    {
        foreach (var go in _entries)
            if (go != null) Object.Destroy(go);
        _entries.Clear();

        if (_panel == null || _ui == null) return;

        var events = MusicEvents();
        if (events.Count == 0)
        {
            _entries.Add(_ui.CreateLabel(_panel, "No music events found in the\nloaded FMOD banks.",
                14, TextAlignmentOptions.Center));
            return;
        }

        foreach (var path in events)
        {
            var captured = path;
            _entries.Add(_ui.CreateButton(_panel, ShortName(captured), () => Select(captured)));
        }
    }

    private void Select(string eventPath)
    {
        _editor.Map.MusicEvent = eventPath;
        UpdateCurrentLabel();

        try
        {
            AudioManager.Instance?.PlayMusic(eventPath);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: music preview failed for '{eventPath}': {e.Message}");
        }

        _editor.SetStatus($"Blueprint music set to {ShortName(eventPath)}.");
    }

    private static string ShortName(string eventPath) =>
        eventPath.StartsWith(MusicPrefix + "/") ? eventPath.Substring(MusicPrefix.Length + 1) : eventPath;

    // Enumerated once from the loaded FMOD banks; the game loads its banks at startup, so the
    // set is stable for the session.
    private static List<string> MusicEvents()
    {
        if (_musicEvents != null) return _musicEvents;

        _musicEvents = [];
        try
        {
            FMODUnity.RuntimeManager.StudioSystem.getBankList(out var banks);
            if (banks != null)
            {
                foreach (var bank in banks)
                {
                    bank.getEventList(out var descriptions);
                    if (descriptions == null) continue;

                    foreach (var description in descriptions)
                    {
                        description.getPath(out var path);
                        if (!string.IsNullOrEmpty(path) && path.StartsWith(MusicPrefix) &&
                            !_musicEvents.Contains(path))
                            _musicEvents.Add(path);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: FMOD music enumeration failed: " + e.Message);
        }

        _musicEvents.Sort(string.CompareOrdinal);
        Plugin.Log.LogInfo($"MapEditor: {_musicEvents.Count} music event(s) available.");
        return _musicEvents;
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        // MusicEvent is edited directly on the live blueprint; nothing to copy. The hook exists
        // so a future refactor that moves the field cannot silently skip this tool.
        map.MusicEvent = _editor.Map.MusicEvent;
        map.MusicLoop = _editor.Map.MusicLoop;
    }
}
