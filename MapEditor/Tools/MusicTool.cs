using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public class MusicTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Music";

    private const string MusicPrefix = "event:/music";

    private readonly RuntimeMapEditor _editor;

    private static List<string> _musicEvents;

    public MusicTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _dropdown = ui.CreateDropdown(panel, VanillaOption, [], (index, _) =>
        {
            if (index <= 0)
            {
                _editor.Map.MusicEvent = "";
                _editor.SetStatus("Music set to vanilla.");
                return;
            }

            var events = MusicEvents();
            if (index - 1 < events.Count) Select(events[index - 1]);
        });

        ui.CreateToggle(panel, "Loop music", _editor.Map.MusicLoop, v =>
        {
            _editor.Map.MusicLoop = v;
            _editor.SetStatus(v
                ? "Music loop enabled."
                : "Music loop disabled.");
        });
    }

    private const string VanillaOption = "Vanilla (no override)";

    private MapEditorDropdown _dropdown;

    public void OnEnter()
    {
        RefreshOptions();
        _editor.SetStatus("Pick a track to preview and assign it.");
    }

    /// The blueprint's music changed under the panel (a peer edited it).
    internal void RefreshFromMap() => RefreshOptions();

    private void RefreshOptions()
    {
        if (_dropdown == null) return;

        var events = MusicEvents();
        if (events.Count == 0)
        {
            _editor.SetStatus("No music events found.", StatusSeverity.Warning);
            return;
        }

        var labels = new List<string>(events.Count + 1) { VanillaOption };
        foreach (var path in events) labels.Add(ShortName(path));

        _dropdown.SetOptions(labels);

        var current = _editor.Map.MusicEvent;
        var index = string.IsNullOrEmpty(current) ? 0 : events.IndexOf(current) + 1;
        _dropdown.SetSelected(Mathf.Max(index, 0));
    }

    public void OnExit() { }
    public void OnUpdate() { }

    private void Select(string eventPath)
    {
        _editor.Map.MusicEvent = eventPath;
        _editor.MarkEdited();

        try
        {
            AudioManager.Instance?.PlayMusic(eventPath);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: music preview failed for '{eventPath}': {e.Message}");
        }

        _editor.SetStatus($"Music: {ShortName(eventPath)}.");
    }

    public static string ShortName(string eventPath) =>
        eventPath.StartsWith(MusicPrefix + "/") ? eventPath.Substring(MusicPrefix.Length + 1) : eventPath;

    public static List<string> MusicEvents()
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
        map.MusicEvent = _editor.Map.MusicEvent;
        map.MusicLoop = _editor.Map.MusicLoop;
    }
}
