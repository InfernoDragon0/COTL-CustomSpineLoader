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

    public void BuildPanel(RectTransform panel, MapEditorUI ui) => _ui = ui;

    // ---- screen ---------------------------------------------------------------------------------

    public bool OwnsScreen => _screen != null && _screen.IsOpen;

    public void ScreenQuickSave() =>
        _editor.SetStatus("Nothing to save here - this screen only opens saved maps.");

    public void ScreenHoverStatus(string message) { }

    public bool ScreenStepBack()
    {
        if (!OwnsScreen) return false;

        if (_screen.LightboxOpen)
        {
            _screen.HideLightbox();
            return true;
        }

        CloseScreen();
        return true;
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Show that map"),
        ("Esc", "Close the browser")
    ];

    private bool OpenScreen()
    {
        if (_ui == null) return false;

        _screen ??= new LoadMapScreen(_editor, _ui)
        {
            CloseRequested = CloseScreen,
            LoadRequested = () => Load(_selected)
        };
        _screen.Open(RuntimeMapEditor.Context == EditorContext.Hub ? "Load hub" : "Load map");
        RefreshList();
        return true;
    }

    private void CloseScreen()
    {
        ClearEntries();
        _screen?.Close();

        _editor.SelectFirstTool();
    }

    public void OnEnter()
    {
        if (!OpenScreen())
        {
            _editor.SetStatus("The saved-map browser could not be opened.", StatusSeverity.Error);
            _editor.SelectFirstTool();
        }
    }

    public void OnExit()
    {
        ClearEntries();
        _screen?.Close();
    }

    public void OnUpdate() { }

    private void ClearEntries()
    {
        _previewToken++;
        _previewQueue = null;
        _previewCursor = 0;
        _thumbs.Clear();

        _selected = null;
        _fullToken++;
        _screen?.SetEmpty();
        _screen?.ClearCells();

        if (_fullTexture != null) Object.Destroy(_fullTexture);
        _fullTexture = null;

        foreach (var tex in _previews)
            if (tex != null) Object.Destroy(tex);
        _previews.Clear();
    }

    private class SavedRoom
    {
        public string Name;
        public System.DateTime Saved;
    }

    private static List<SavedRoom> Scan(bool wantHubs)
    {
        var hubBlueprints = HubSession.BlueprintNames();
        var rooms = new List<SavedRoom>();

        foreach (var file in APIHelper.ModContentPaths.FilesIn(MapEditorSerialization.FolderName, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(name)) continue;

            if (hubBlueprints.Contains(name) != wantHubs) continue;

            rooms.Add(new SavedRoom { Name = name, Saved = SavedAt(file) });
        }

        rooms.Sort((a, b) => b.Saved.CompareTo(a.Saved));
        return rooms;
    }

    private const int CardsPerFrame = 6;

    private void RefreshList()
    {
        ClearEntries();
        if (_screen == null || !_screen.IsOpen) return;

        _editor.StartCoroutine(RefreshRoutine(_previewToken));
    }

    private IEnumerator RefreshRoutine(int token)
    {
        var wantHubs = RuntimeMapEditor.Context == EditorContext.Hub;
        var waiting = _screen.AddNote("Reading saved maps...");

        APIHelper.ModContentPaths.RootsFor(MapEditorSerialization.FolderName);

        var scan = System.Threading.Tasks.Task.Run(() => Scan(wantHubs));
        while (!scan.IsCompleted) yield return null;

        if (token != _previewToken) yield break;

        if (waiting != null) Object.Destroy(waiting);

        if (scan.Exception != null)
        {
            Plugin.Log.LogError("MapEditor: saved-map scan failed: " + scan.Exception);
            _screen.AddNote("The saved maps could not be read.");
            _editor.SetStatus("The saved maps could not be read.", StatusSeverity.Error);
            yield break;
        }

        var rooms = scan.Result;
        if (rooms.Count == 0)
        {
            _screen.AddNote(wantHubs ? "No other hubs saved yet." : "No saved dungeon maps yet.");
            _editor.SetStatus(wantHubs ? "No other hubs saved yet." : "No saved dungeon maps yet.");
            yield break;
        }

        var kind = wantHubs ? "hub" : "dungeon map";

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            var card = _screen.CreateCard(room.Name, Describe(room.Saved), () => Select(room), out var thumb);
            if (card != null) _thumbs[room.Name] = thumb;

            if ((i + 1) % CardsPerFrame != 0 || i + 1 >= rooms.Count) continue;

            _editor.SetStatus($"Reading saved {kind}s... {i + 1} of {rooms.Count}.");
            yield return null;
            if (token != _previewToken) yield break;
        }

        _editor.SetStatus($"{rooms.Count} saved {kind}(s). Newest first.");

        _previewQueue = rooms;
        _previewCursor = 0;
        for (var i = 0; i < PreviewWorkers; i++)
            _editor.StartCoroutine(PreviewWorker(token));
    }

    private string _selected;

    private void Select(SavedRoom room)
    {
        if (_screen == null || room == null) return;

        _selected = room.Name;

        var path = MapEditorSerialization.PathFor(room.Name);
        var when = room.Saved == System.DateTime.MinValue
            ? "never saved"
            : room.Saved.ToString("dddd d MMMM yyyy, HH:mm");

        _screen.SetDetail(room.Name, when, Size(path), Facts(room.Name), _editor.HasUnsavedEdits);

        if (_screen.DetailShot != null)
        {
            var thumb = _thumbs.TryGetValue(room.Name, out var cell) ? cell : null;
            _screen.DetailShot.texture = thumb != null ? thumb.texture : null;
            _screen.DetailShot.enabled = _screen.DetailShot.texture != null;

            _screen.ShowCaption(_screen.DetailShot.texture != null);
        }

        _editor.StartCoroutine(LoadFullRes(room.Name, ++_fullToken));

        _editor.SetStatus($"{room.Name} - press Load Map to open it.");
    }

    private static string Size(string path)
    {
        try
        {
            if (!File.Exists(path)) return "";

            var bytes = new FileInfo(path).Length;
            return bytes >= 1024L * 1024L
                ? $"{bytes / (1024f * 1024f):0.0} MB on disk"
                : $"{bytes / 1024f:0} KB on disk";
        }
        catch (System.Exception)
        {
            return "";
        }
    }

    private static List<(Sprite Icon, string Text)> Facts(string mapName)
    {
        var rows = new List<(Sprite, string)>();

        var blueprint = MapEditorSerialization.LoadByName(mapName);
        if (blueprint == null)
        {
            rows.Add((null, "This map's file could not be read."));
            return rows;
        }

        Count(rows, blueprint.Shapes.Count, "Shape", "terrain shape");
        Count(rows, blueprint.Props.Count, "Structures", "prop");
        Count(rows, blueprint.Structures.Count, "Structures", "structure");
        Count(rows, blueprint.Enemies.Count, "Enemies", "enemy", "enemies");
        Count(rows, blueprint.Npcs.Count, "NPCs", "NPC");
        Count(rows, blueprint.Podiums.Count, "Podiums", "podium");
        Count(rows, blueprint.Triggers.Count, "Triggers", "trigger");
        Count(rows, blueprint.Doors.Count, "Doors", "door");
        Count(rows, blueprint.KeptAuthored.Count, "Select", "kept vanilla object");

        if (rows.Count == 0) rows.Add((null, "Empty map."));
        return rows;
    }

    private static void Count(List<(Sprite, string)> into, int count, string tool,
        string singular, string plural = null)
    {
        if (count <= 0) return;

        into.Add((MapEditorIcons.GetToolIconOrNull(tool),
            $"{count} {(count == 1 ? singular : plural ?? singular + "s")}"));
    }

    private void Load(string mapName)
    {
        if (string.IsNullOrEmpty(mapName))
        {
            _editor.SetStatus("Pick a map first.", StatusSeverity.Warning);
            return;
        }

        if (_editor.Loader.IsLoading)
        {
            _editor.SetStatus("Load already in progress.", StatusSeverity.Warning);
            return;
        }

        var blueprint = MapEditorSerialization.LoadByName(mapName);
        if (blueprint == null)
        {
            _editor.SetStatus($"'{mapName}' could not be read.", StatusSeverity.Error);
            return;
        }

        LevelPlayback.Stop();

        CloseScreen();
        _editor.Loader.Load(blueprint);
    }

    private static System.DateTime SavedAt(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTime(path) : System.DateTime.MinValue;
        }
        catch (System.Exception)
        {
            return System.DateTime.MinValue;
        }
    }

    private static string Describe(System.DateTime saved)
    {
        if (saved == System.DateTime.MinValue) return "never saved";

        var age = System.DateTime.Now - saved;
        if (age.TotalHours < 24d) return saved.ToString("HH:mm") + " today";
        if (age.TotalDays < 2d) return saved.ToString("HH:mm") + " yesterday";

        return saved.ToString("yyyy-MM-dd HH:mm");
    }

    private int _previewToken;

    private const int PreviewWorkers = 3;

    private List<SavedRoom> _previewQueue;
    private int _previewCursor;

    private Texture2D _fullTexture;
    private int _fullToken;

    private IEnumerator LoadFullRes(string mapName, int token)
    {
        var path = MapEditorSerialization.SnapshotPathFor(mapName);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) yield break;

        var read = System.Threading.Tasks.Task.Run(() => File.ReadAllBytes(path));
        while (!read.IsCompleted) yield return null;

        if (token != _fullToken) yield break;

        if (read.Exception != null || read.Result == null)
        {
            Plugin.Log.LogWarning($"MapEditor: snapshot '{path}' could not be read: {read.Exception}");
            yield break;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: false);
        if (!texture.LoadImage(read.Result))
        {
            Object.Destroy(texture);
            yield break;
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        if (token != _fullToken)
        {
            Object.Destroy(texture);
            yield break;
        }

        if (_fullTexture != null) Object.Destroy(_fullTexture);
        _fullTexture = texture;

        if (_screen == null || _screen.DetailShot == null) yield break;

        _screen.DetailShot.texture = texture;
        _screen.DetailShot.enabled = true;
        _screen.ShowCaption(true);
    }

    private IEnumerator PreviewWorker(int token)
    {
        while (token == _previewToken && _previewQueue != null && _previewCursor < _previewQueue.Count)
        {
            var room = _previewQueue[_previewCursor++];
            yield return LoadPreview(room.Name, token);
        }
    }

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

        if (_selected == mapName && _screen != null && _screen.DetailShot != null)
        {
            _screen.DetailShot.texture = texture;
            _screen.DetailShot.enabled = true;
            _screen.ShowCaption(true);
        }
    }
}
