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
        _editor.SetStatus("Nothing to save here - this screen only opens saved maps.");

    public void ScreenHoverStatus(string message) { }

    public bool ScreenStepBack()
    {
        if (!OwnsScreen) return false;

        // One step at a time: the enlarged picture is a layer above the browser, so Esc takes that
        // down first and leaves you where you were rather than closing the tool out from under you.
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
            _editor.SetStatus("The saved-map browser could not be opened.", StatusSeverity.Error);
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
        // Whatever is still scanning or decoding belongs to a screen that is going away.
        _previewToken++;
        _previewQueue = null;
        _previewCursor = 0;
        _thumbs.Clear();

        // The pane described a card that is about to be destroyed, and its picture is one of the
        // textures below.
        _selected = null;
        _fullToken++;
        _screen?.SetEmpty();
        _screen?.ClearCells();

        // Its own texture, not one of _previews - loaded separately and owned separately.
        if (_fullTexture != null) Object.Destroy(_fullTexture);
        _fullTexture = null;

        // Destroyed only after the cards that drew them are gone.
        foreach (var tex in _previews)
            if (tex != null) Object.Destroy(tex);
        _previews.Clear();
    }

    // One row of the browser: everything a card needs, and nothing that costs a parse to know.
    private class SavedRoom
    {
        public string Name;
        public System.DateTime Saved;
    }

    // Listing the folder, off the main thread.
    //
    // Nothing here parses a room blueprint, and that is the point: a blueprint carries every shape,
    // prop, structure and enemy in a room, and the browser needs a name and a date. Both are on the
    // file itself - the save writes to PathFor(MapName), so the file name IS the map name - so the
    // list costs a directory read instead of a full deserialise of everything ever saved. The one
    // blueprint that does get parsed is the one that gets clicked.
    private static List<SavedRoom> Scan(bool wantHubs)
    {
        var hubBlueprints = HubSession.BlueprintNames();
        var rooms = new List<SavedRoom>();

        foreach (var file in APIHelper.ModContentPaths.FilesIn(MapEditorSerialization.FolderName, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(name)) continue;

            // The two kinds of room are not interchangeable, so the browser only ever offers its own
            // kind - both ways round. A dungeon room loaded into a town arrives with four doors and
            // a spawn of enemies the town has no use for; a town loaded into a dungeon arrives with
            // no doors at all and a run that cannot continue past it.
            if (hubBlueprints.Contains(name) != wantHubs) continue;

            rooms.Add(new SavedRoom { Name = name, Saved = SavedAt(file) });
        }

        // Newest first. Working on a room usually means working on the one last saved, and a list
        // that puts it first is one that needs no reading most of the time. Taken from the file's
        // own write time rather than anything in the blueprint: the blueprint records no date, and
        // the file system already knows. Read once per file and kept, rather than asked for inside
        // the comparison - a sort asks more often than there are files.
        rooms.Sort((a, b) => b.Saved.CompareTo(a.Saved));
        return rooms;
    }

    // How many cards to build before giving the frame back. Each one is a plate, two labels and a
    // picture frame, so a folder of forty in one frame is a visible lurch.
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

        // Warmed here rather than on the worker: the bridge scan fills a static list the first time
        // anything asks for it, and that is not a race worth having.
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

        // Snapshots come after every card is standing, so the pictures fill in against a list that
        // has stopped moving.
        _previewQueue = rooms;
        _previewCursor = 0;
        for (var i = 0; i < PreviewWorkers; i++)
            _editor.StartCoroutine(PreviewWorker(token));
    }

    // The room the detail pane is currently describing, and the only one the button can load.
    private string _selected;

    // Clicking a card shows it; the button loads it.
    //
    // This is the one blueprint of the folder that gets parsed, and it is parsed here rather than
    // when the list was built - see Scan. One click, one file: reading every saved room to fill a
    // list of names was the stutter, and reading one to answer "what is in this?" is not.
    private void Select(SavedRoom room)
    {
        if (_screen == null || room == null) return;

        _selected = room.Name;

        var path = MapEditorSerialization.PathFor(room.Name);
        var when = room.Saved == System.DateTime.MinValue
            ? "never saved"
            : room.Saved.ToString("dddd d MMMM yyyy, HH:mm");

        _screen.SetDetail(room.Name, when, Size(path), Facts(room.Name), _editor.HasUnsavedEdits);

        // The picture is already loaded for the card, so the pane borrows the same texture rather
        // than decoding a second copy of a full-screen png.
        if (_screen.DetailShot != null)
        {
            var thumb = _thumbs.TryGetValue(room.Name, out var cell) ? cell : null;
            _screen.DetailShot.texture = thumb != null ? thumb.texture : null;
            _screen.DetailShot.enabled = _screen.DetailShot.texture != null;

            // No picture, no invitation to enlarge one.
            _screen.ShowCaption(_screen.DetailShot.texture != null);
        }

        // The card's picture stands in while the full-size one is read, so the pane fills at once
        // rather than sitting empty for a frame or two.
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
            // A file size is a nicety; a browser that throws over one is not.
            return "";
        }
    }

    // What is in the room, one line per kind, each behind the icon of the tool that makes it.
    //
    // The icons are the editor's own, so the pane reads as an index into the toolbar rather than as
    // a wall of numbers: whatever put a thing in the room is the picture beside its count.
    private static List<(Sprite Icon, string Text)> Facts(string mapName)
    {
        var rows = new List<(Sprite, string)>();

        var blueprint = MapEditorSerialization.LoadByName(mapName);
        if (blueprint == null)
        {
            rows.Add((null, "This map's file could not be read."));
            return rows;
        }

        // Only what is actually in there: a room with no NPCs says nothing about NPCs rather than
        // listing a zero, so the list is as long as the room is interesting.
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

        // Parsed here rather than at listing time - see Scan. This is the one blueprint of the
        // folder that anybody actually asked for.
        var blueprint = MapEditorSerialization.LoadByName(mapName);
        if (blueprint == null)
        {
            _editor.SetStatus($"'{mapName}' could not be read.", StatusSeverity.Error);
            return;
        }

        // A stale level run advancing on the next door would teleport the player.
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

    // Snapshots are fetched a few at a time rather than all at once. The decode is off the main
    // thread, but the upload to the GPU is not, and forty of those landing in the same handful of
    // frames is its own stutter. Three keeps the pipe busy without piling up uploads; the list is
    // newest-first, so the pictures worth waiting for are the ones that arrive first anyway.
    private const int PreviewWorkers = 3;

    private List<SavedRoom> _previewQueue;
    private int _previewCursor;

    // The selected room's snapshot, loaded again at full size for the pane and the enlarged view.
    private Texture2D _fullTexture;
    private int _fullToken;

    // Loaded the way the game loads its own photographs, and for the same reason.
    //
    // The grid's pictures come through UnityWebRequestTexture, which decodes off the main thread -
    // that is what stopped the browser stuttering. What it also does is build the texture WITH
    // mipmaps, and QualitySettings' mipmap limit then throws away the largest levels of every
    // mipmapped texture as it uploads. This game ships that limit at 2, so a 1920-wide snapshot was
    // being drawn from a 480-wide mip: fine in a grid cell, obviously wrong blown up.
    //
    // Nothing about the file or the decode shows it. Texture2D.width still reports 1920, because
    // that is the CPU-side descriptor and the limit applies to the GPU copy - which is what made
    // this look, twice, like it was not happening.
    //
    // MMImageDataReadWriter.Read is the answer, and it is the game's own photo loader:
    // mipChain:false. A texture with no mipmaps has nothing for the limit to discard. The cost is
    // that LoadImage decodes on the main thread, so this is done for ONE picture - the one being
    // looked at - rather than for every card in the folder.
    private IEnumerator LoadFullRes(string mapName, int token)
    {
        var path = MapEditorSerialization.SnapshotPathFor(mapName);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) yield break;

        // The read is the slow half and it is pure IO, so it goes off the main thread; only the
        // decode has to happen here.
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

        // Selection moved on while this was decoding.
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

        // A card can be picked before its own snapshot has arrived - the pictures fill in over
        // several seconds and nothing stops you clicking during that. When the one being described
        // lands, the pane takes it too.
        if (_selected == mapName && _screen != null && _screen.DetailShot != null)
        {
            _screen.DetailShot.texture = texture;
            _screen.DetailShot.enabled = true;
            _screen.ShowCaption(true);
        }
    }
}
