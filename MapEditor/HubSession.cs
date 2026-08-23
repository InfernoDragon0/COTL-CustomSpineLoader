using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomSpineLoader.MapEditor;

// Hubs, authored and played in the game's own DLC town room.
//
// Woolhaven is not a scene: it is a GenerateRoom ("DLC_ShrineRoom") sitting inside Base Biome 1,
// switched on by BiomeBaseManager.ActivateDLCShrineRoom(), which also switches the base room and
// the church off. Enabling it makes it GenerateRoom.Instance, and GenerateRoom.Instance is what
// the whole map editor works on - so once the room is up, every tool, the blueprint loader and
// the clear sweeps apply to it unchanged.
//
// Both jobs are the same three steps: get to the room, strip it, then either hand it to the
// editor (authoring) or rebuild the saved blueprint in it (playing). Nothing is written back to
// the scene, so leaving is a reload of Base Biome 1 and the town returns untouched.
public static class HubSession
{
    public const string HubScene = "Base Biome 1";

    // What the session is for. Authoring opens the editor on an empty room; Playing rebuilds a
    // saved hub and leaves the player in it.
    public enum Mode
    {
        None,
        Authoring,
        Playing
    }

    public static Mode Current { get; private set; }

    public static bool Active => Current != Mode.None;
    public static bool IsAuthoring => Current == Mode.Authoring;

    // The hub being authored or played, by name.
    public static string HubName { get; private set; }

    private static Mode _pendingMode;
    private static string _pendingHub;
    private static bool _running;

    // ---- entry points -------------------------------------------------------------------------

    // Author a hub: the room is emptied and the editor opens on it. Returns an error to show, or
    // null when the trip has started.
    public static string Author(string hubName)
    {
        if (string.IsNullOrWhiteSpace(hubName)) return "A hub needs a name.";
        return Begin(hubName, Mode.Authoring);
    }

    // Play a saved hub - what a world map node does when it points at one.
    public static string Play(string hubName)
    {
        if (string.IsNullOrWhiteSpace(hubName)) return "That node has no hub to enter.";

        var hub = CTLevelSerialization.LoadByName(hubName);
        if (hub == null) return $"Hub '{hubName}' is not saved on this machine.";
        if (BlueprintFor(hub) == null)
            return $"Hub '{hubName}' has no room blueprint saved for it yet.";

        return Begin(hubName, Mode.Playing);
    }

    private static string Begin(string hubName, Mode mode)
    {
        if (_running) return "A hub is already being prepared.";

        if (PlayerFarming.Instance == null) return "Hubs open in game, not on the menu.";

        _pendingHub = hubName;
        _pendingMode = mode;

        // Already in the scene the room lives in: no trip, just the room.
        if (SceneManager.GetActiveScene().name == HubScene)
        {
            StartPreparing();
            return null;
        }

        // The trip home is the game's own, so the transition and the load are the ones the player
        // knows; the arrival is picked up in OnSceneLoaded.
        try
        {
            GameManager.ToShip(HubScene);
        }
        catch (System.Exception e)
        {
            _pendingHub = null;
            _pendingMode = Mode.None;
            Plugin.Log.LogError("Hub: could not travel to the base: " + e);
            return "Could not travel to the base, see the log.";
        }

        return null;
    }

    // Called by the plugin for every scene load. Every load ends the session that was running -
    // including a reload of the base itself, which is how a hub is left: the town comes back
    // untouched and nothing of the hub survives it.
    public static void OnSceneLoaded(Scene scene)
    {
        var mode = _pendingMode;
        var hub = _pendingHub;
        End();

        if (scene.name != HubScene || mode == Mode.None) return;

        _pendingMode = mode;
        _pendingHub = hub;
        StartPreparing();
    }

    // The session is over: either the scene changed under it, or the player left the hub.
    public static void End()
    {
        Current = Mode.None;
        HubName = null;
        _pendingHub = null;
        _pendingMode = Mode.None;
        _running = false;
    }

    private static void StartPreparing()
    {
        if (Plugin.Instance == null) return;
        Plugin.Instance.StartCoroutine(PrepareRoutine());
    }

    // ---- the room -----------------------------------------------------------------------------

    private static IEnumerator PrepareRoutine()
    {
        _running = true;

        var hubName = _pendingHub;
        var mode = _pendingMode;
        _pendingHub = null;
        _pendingMode = Mode.None;

        // The base builds itself over several frames; the room manager is the last thing up.
        var deadline = Time.realtimeSinceStartup + 20f;
        while ((BiomeBaseManager.Instance == null || PlayerFarming.Instance == null) &&
               Time.realtimeSinceStartup < deadline)
            yield return null;

        if (BiomeBaseManager.Instance == null)
        {
            Plugin.Log.LogWarning("Hub: the base never finished loading; hub not opened.");
            _running = false;
            yield break;
        }

        Current = mode;
        HubName = hubName;

        // Switches the town room on and the base room off, and makes it GenerateRoom.Instance.
        BiomeBaseManager.Instance.ActivateDLCShrineRoom();
        PlayerFarming.Location = FollowerLocation.DLC_ShrineRoom;
        PlayerFarming.LastLocation = FollowerLocation.Base;

        // OnEnable runs its own coroutines (nav rescan, decoration pooling); clearing on top of
        // them destroys objects mid-rebuild.
        yield return null;
        yield return null;

        // The one check that must never be skipped. Everything below clears GenerateRoom.Instance,
        // and in this scene that is the player's own base until the town room takes it over. If
        // the handover did not happen, stop: an emptied base is not something an undo can fix.
        if (!ReferenceEquals(SceneRefs.Room, BiomeBaseManager.Instance.DLC_ShrineRoom))
        {
            Plugin.Log.LogError("Hub: the town room did not become the active room; refusing to " +
                                "clear the base. No hub was opened.");
            End();
            yield break;
        }

        MovePlayerIntoRoom();

        var editor = EnsureEditor();
        if (editor == null)
        {
            Plugin.Log.LogWarning("Hub: no map editor could be created in the base.");
            _running = false;
            yield break;
        }

        // The town is somebody else's furniture; a hub is authored from nothing.
        SuppressTown();
        yield return ClearRoom(editor);

        if (mode == Mode.Authoring)
        {
            editor.BeginHubAuthoring(hubName);
        }
        else
        {
            var hub = CTLevelSerialization.LoadByName(hubName);
            var blueprintName = BlueprintFor(hub);
            var bp = blueprintName != null ? MapEditorSerialization.LoadByName(blueprintName) : null;

            if (bp == null)
            {
                Plugin.Log.LogWarning($"Hub '{hubName}': its room blueprint is missing; the room " +
                                      "is empty.");
            }
            else
            {
                editor.Loader.Load(bp);
                while (editor.Loader.IsLoading) yield return null;
                MovePlayerIntoRoom();
            }
        }

        _running = false;
    }

    // The room's own entrance, so the player is never dropped outside the collision that was
    // just rebuilt.
    private static void MovePlayerIntoRoom()
    {
        var room = SceneRefs.Room;
        if (room == null || PlayerFarming.Instance == null) return;

        var target = room.transform.position;
        foreach (var player in PlayerFarming.players)
            if (player != null) player.transform.position = target;
    }

    // Woolhaven's controller keeps driving its shops, boards and animals at objects that are
    // about to stop existing.
    private static void SuppressTown()
    {
        try
        {
            var town = Object.FindObjectOfType<LambTownController>();
            if (town != null) town.enabled = false;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Hub: could not quiet the town controller: " + e.Message);
        }
    }

    private static IEnumerator ClearRoom(RuntimeMapEditor editor)
    {
        var clear = editor.GetTool<Tools.ClearTool>();
        if (clear == null) yield break;

        clear.ClearPlaced();
        clear.ClearScenery();
        clear.ClearTerrain();

        // Destroy defers to the end of the frame; anything built on top of doomed objects in the
        // same frame bakes against them.
        yield return null;
    }

    // The editor is normally scoped to dungeon scenes. A hub session is the one time it belongs
    // in the base, and it goes away with the session.
    private static RuntimeMapEditor EnsureEditor()
    {
        if (RuntimeMapEditor.Active != null) return RuntimeMapEditor.Active;

        var host = new GameObject("RuntimeMapEditorHost_Hub");
        return host.AddComponent<RuntimeMapEditor>();
    }

    // ---- the hub record -----------------------------------------------------------------------

    // A hub is a CTLevelBlueprint with IsHub set: a name, and one room naming the blueprint that
    // dresses it. The level tier already carries hubs everywhere - saving, listing, and the world
    // map's own target picker - so a hub stays one of those rather than a fourth kind of file.
    public static string BlueprintFor(CTLevelBlueprint hub)
    {
        if (hub is not { IsHub: true } || hub.Rooms.Count == 0) return null;

        var pool = hub.Rooms[0].NodePool;
        if (pool == null || pool.Count == 0) return null;

        return string.IsNullOrWhiteSpace(pool[0]) ? null : pool[0];
    }

    // Written when a hub room is saved from the editor, so the world map's Hub picker can find it
    // and knows which blueprint to rebuild.
    public static void WriteRecord(string hubName, string blueprintName)
    {
        if (string.IsNullOrWhiteSpace(hubName) || string.IsNullOrWhiteSpace(blueprintName)) return;

        var hub = CTLevelSerialization.LoadByName(hubName) ?? new CTLevelBlueprint();
        hub.LevelName = hubName;
        hub.IsHub = true;

        if (hub.Rooms.Count == 0) hub.Rooms.Add(new CTLevelRoom());
        while (hub.Rooms.Count > 1) hub.Rooms.RemoveAt(hub.Rooms.Count - 1);

        hub.Rooms[0].Role = "Entrance";
        hub.Rooms[0].NodePool.Clear();
        hub.Rooms[0].NodePool.Add(blueprintName);

        CTLevelSerialization.Save(hub);
    }

    // The next free hub name, for the panel's New Hub button.
    public static string FreeName(string stem = "untitledhub")
    {
        for (var i = 1; i < 1000; i++)
        {
            var candidate = stem + i;
            if (!CTLevelSerialization.Exists(candidate)) return candidate;
        }
        return stem;
    }
}
