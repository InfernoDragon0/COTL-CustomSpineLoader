using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class LoadTool : IMapEditorTool, IMapEditorScreenTool, IMapEditorShortcuts
{
    public string Name => "Load Map";

    private readonly RuntimeMapEditor _editor;
    private readonly List<Texture2D> _previews = [];
    private readonly Dictionary<string, RawImage> _thumbs = [];

    private MapEditorUI _ui;
    private LoadMapScreen _screen;

    public LoadTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    // No panel at all. This tool *is* the browser: picking it opens the screen and closing the
    // screen leaves the tool, so a submenu would be a page nobody is ever on - and one that would
    // flash up for a frame on the way in and out.
    public void BuildPanel(RectTransform panel, MapEditorUI ui) => _ui = ui;

    // ---- screen ---------------------------------------------------------------------------------

    public bool OwnsScreen => _screen != null && _screen.IsOpen;

    // Nothing on this screen is editable, so there is nothing here to quicksave.
    public void ScreenQuickSave() =>
        _editor.SetStatus("Nothing to save here - this screen only opens saved rooms.");

    public void ScreenHoverStatus(string message) { }

    public bool ScreenStepBack()
    {
        if (!OwnsScreen) return false;

        CloseScreen();
        return true;
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Load that room"),
        ("Esc", "Close the browser")
    ];

    private bool OpenScreen()
    {
        if (_ui == null) return false;

        _screen ??= new LoadMapScreen(_editor, _ui) { CloseRequested = CloseScreen };
        _screen.Open(HubSession.Active ? "Load hub" : "Load room");
        RefreshList();
        return true;
    }

    private void CloseScreen()
    {
        ClearEntries();
        _screen?.Close();

        // Leaving the screen leaves the tool, since the tool is nothing but the screen. SelectTool
        // calls OnExit on the way out, which finds the screen already closed and does nothing.
        _editor.SelectFirstTool();
    }

    // Picking the tool opens the browser, with no panel in between.
    public void OnEnter()
    {
        // Somewhere to go if the screen cannot be built: the alternative is sitting on a tool with
        // an empty panel and no way to tell that anything went wrong.
        if (!OpenScreen())
        {
            _editor.SetStatus("The saved-room browser could not be opened.", StatusSeverity.Error);
            _editor.SelectFirstTool();
        }
    }

    // Snapshots are megabytes each; alive only while the browser is on screen.
    public void OnExit()
    {
        ClearEntries();
        _screen?.Close();
    }

    // Escape closes the browser through the host's own step-back: it asks a screen tool first, and
    // ScreenStepBack above is what answers.
    public void OnUpdate() { }

    private void ClearEntries()
    {
        // Whatever is still decoding belongs to a screen that is going away.
        _previewToken++;
        _thumbs.Clear();
        _screen?.ClearCells();

        // Destroyed only after the cards that drew them are gone.
        foreach (var tex in _previews)
            if (tex != null) Object.Destroy(tex);
        _previews.Clear();
    }

    // The room blueprint each saved hub is dressed with.
    private static HashSet<string> HubBlueprintNames()
    {
        var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var level in CTLevelSerialization.LoadAll())
        {
            var blueprint = HubSession.BlueprintFor(level);
            if (blueprint != null) names.Add(blueprint);
        }

        return names;
    }

    private void RefreshList()
    {
        ClearEntries();
        if (_screen == null || !_screen.IsOpen) return;

        var results = MapEditorSerialization.LoadAll();

        // The two kinds of room are not interchangeable, so the browser only ever offers its own
        // kind - both ways round. A dungeon room loaded into a town arrives with four doors and a
        // spawn of enemies the town has no use for; a town loaded into a dungeon arrives with no
        // doors at all and a run that cannot continue past it.
        var hubBlueprints = HubBlueprintNames();
        var wantHubs = HubSession.Active;

        results.RemoveAll(bp => bp == null || hubBlueprints.Contains(bp.MapName) != wantHubs);

        if (results.Count == 0)
        {
            _screen.AddNote(wantHubs ? "No other hubs saved yet." : "No saved dungeon rooms yet.");
            return;
        }

        // Newest first. Working on a room usually means working on the one last saved, and a list
        // that puts it first is one that needs no reading most of the time. Taken from the file's
        // own write time rather than anything in the blueprint: the blueprint records no date, and
        // the file system already knows.
        results.Sort((a, b) => SavedAt(b).CompareTo(SavedAt(a)));

        _previewToken++;
        foreach (var result in results)
        {
            var captured = result;
            var card = _screen.CreateCard(captured.MapName, Describe(SavedAt(captured)),
                () => Load(captured), out var thumb);

            if (card == null) continue;

            _thumbs[captured.MapName] = thumb;
            _editor.StartCoroutine(LoadPreview(captured.MapName, _previewToken));
        }

        _editor.SetStatus($"{results.Count} saved {(wantHubs ? "hub" : "dungeon room")}(s). Newest first.");
    }

    private void Load(CTNodeBlueprint blueprint)
    {
        if (_editor.Loader.IsLoading)
        {
            _editor.SetStatus("Load already in progress.", StatusSeverity.Warning);
            return;
        }

        // A stale level run advancing on the next door would teleport the player.
        LevelPlayback.Stop();

        CloseScreen();
        _editor.Loader.Load(blueprint);
    }

    private static System.DateTime SavedAt(CTNodeBlueprint blueprint)
    {
        if (blueprint == null) return System.DateTime.MinValue;

        try
        {
            var path = MapEditorSerialization.PathFor(blueprint.MapName);
            return File.Exists(path) ? File.GetLastWriteTime(path) : System.DateTime.MinValue;
        }
        catch (System.Exception)
        {
            // An unreadable path sorts last rather than taking the browser down with it.
            return System.DateTime.MinValue;
        }
    }

    // Recent saves are the ones being worked on, so those get a clock; older ones get a date.
    private static string Describe(System.DateTime saved)
    {
        if (saved == System.DateTime.MinValue) return "never saved";

        var age = System.DateTime.Now - saved;
        if (age.TotalHours < 24d) return saved.ToString("HH:mm") + " today";
        if (age.TotalDays < 2d) return saved.ToString("HH:mm") + " yesterday";

        return saved.ToString("yyyy-MM-dd HH:mm");
    }

    // Bumped whenever the list is rebuilt or the browser closes, so snapshots still decoding for
    // cards that no longer exist throw their texture away instead of filling a dead frame.
    private int _previewToken;

    // A room snapshot is a full-screen png, and decoding one is milliseconds of blocked main
    // thread - a handful of them in a row was the stutter. UnityWebRequest decodes on a worker
    // thread and hands back a finished texture, so the frame only pays for the upload.
    private IEnumerator LoadPreview(string mapName, int token)
    {
        var path = MapEditorSerialization.SnapshotPathFor(mapName);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) yield break;

        string uri;
        try
        {
            uri = new System.Uri(path).AbsoluteUri;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: snapshot path '{path}' is not readable: {e.Message}");
            yield break;
        }

        // nonReadable: nothing reads these pixels back, and a readable copy doubles the memory of
        // what is already the heaviest thing in the editor.
        using var request = UnityWebRequestTexture.GetTexture(uri, nonReadable: true);
        yield return request.SendWebRequest();

        if (!string.IsNullOrEmpty(request.error))
        {
            Plugin.Log.LogWarning($"MapEditor: snapshot '{path}' failed to load: {request.error}");
            yield break;
        }

        var texture = DownloadHandlerTexture.GetContent(request);
        if (texture == null) yield break;

        if (token != _previewToken || !_thumbs.TryGetValue(mapName, out var thumb) || thumb == null)
        {
            Object.Destroy(texture);
            yield break;
        }

        _previews.Add(texture);
        thumb.texture = texture;
        thumb.enabled = true;
    }
}
