using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using HarmonyLib;
using MMTools;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Runs a CTLevelBlueprint as a playable sequence. Starting a run re-enters the dungeon scene
// from scratch (the same flow as the editor's Reset/F5 - loading into a stale room does not
// produce a clean state), then loads the entrance room's node blueprint once the fresh room
// has generated. Each room slot resolves to one saved node blueprint (picked from its pool,
// or from all saved nodes when the pool is empty) up front, and walking through any door in
// the current room loads the next room in the chain instead of the vanilla ChangeRoom.
// Reaching a door in the Exit room ends the run and returns door behavior to vanilla; the
// map-selector phase will replace that later.
//
// All state is static: the scene reload destroys the editor host, and the freshly created
// editor re-binds via OnEditorReady.
public static class LevelPlayback
{
    public static bool Active { get; private set; }

    private static CTLevelBlueprint _level;
    private static List<string> _resolvedRooms;
    private static int _currentIndex;
    private static bool _awaitingSceneEntry;
    private static RuntimeMapEditor _editor;

    public static string Describe()
    {
        if (!Active) return "";
        return $"'{_level.LevelName}' room {_currentIndex + 1}/{_resolvedRooms.Count}";
    }

    // Resolves every room up front so a run never dead-ends mid-way on an empty pool, then
    // re-enters the dungeon. Returns null on success, otherwise the reason it can't start.
    public static string Start(CTLevelBlueprint level, RuntimeMapEditor editor)
    {
        if (level == null || editor == null) return "No level to play.";
        if (editor.Loader.IsLoading) return "A load is already in progress.";
        if (CustomDungeonManager.CustomDungeonList.Count == 0)
            return "No custom dungeon registered to enter.";

        var saved = MapEditorSerialization.LoadAll();
        if (saved.Count == 0) return "No node blueprints saved yet.";

        var byName = new Dictionary<string, CTNodeBlueprint>();
        foreach (var node in saved) byName[node.MapName] = node;

        var resolved = new List<string>();
        for (var i = 0; i < level.Rooms.Count; i++)
        {
            var pool = new List<string>();
            foreach (var name in level.Rooms[i].NodePool)
                if (byName.ContainsKey(name)) pool.Add(name);

            if (level.Rooms[i].NodePool.Count > 0 && pool.Count == 0)
                Plugin.Log.LogWarning($"MapEditor: level room {i + 1} pool has no existing blueprints; using any.");

            if (pool.Count == 0)
                foreach (var name in byName.Keys) pool.Add(name);

            resolved.Add(pool[Random.Range(0, pool.Count)]);
        }

        _level = level;
        _resolvedRooms = resolved;
        _currentIndex = 0;
        _awaitingSceneEntry = true;
        Active = true;

        Plugin.Log.LogInfo($"MapEditor: level '{level.LevelName}' started - rooms: {string.Join(", ", resolved)}. " +
                           "Re-entering dungeon.");

        // The scene change destroys the editor host; close it first so timeScale/HUD/camera are
        // sane for the transition (mirrors what Reset does before EnterDungeon).
        editor.ExitForPlayback();
        try
        {
            foreach (var dungeon in CustomDungeonManager.CustomDungeonList.Values)
            {
                dungeon.EnterDungeon();
                break;
            }
        }
        catch (System.Exception e)
        {
            Stop();
            Plugin.Log.LogError($"MapEditor: dungeon re-entry failed: {e}");
            return "Dungeon re-entry failed, see log.";
        }
        return null;
    }

    // Called by every freshly created editor host (one per Dungeon1 scene load). Re-binds the
    // run to the new scene and, if a run is waiting on the scene entry, loads its first room
    // once generation settles.
    public static void OnEditorReady(RuntimeMapEditor editor)
    {
        _editor = editor;
        if (!Active || !_awaitingSceneEntry) return;
        _awaitingSceneEntry = false;
        editor.StartCoroutine(BeginAfterGeneration(editor));
    }

    private static IEnumerator BeginAfterGeneration(RuntimeMapEditor editor)
    {
        // Follow the F5 convention to the end: generation done, the ChangeRoomWaitToResume
        // black fade released, and the vanilla first-arrival walk-in fully finished (player
        // back in control). Loading earlier put two entry routines in flight at once and
        // cleared the room underneath the arrival sequence.
        var deadline = Time.unscaledTime + 30f;
        while (Time.unscaledTime < deadline)
        {
            var room = SceneRefs.Room;
            var player = PlayerFarming.Instance;
            if (room != null && room.GeneratedPathing && player != null &&
                !MMTransition.IsPlaying && !player.GoToAndStopping &&
                player.state != null && player.state.CURRENT_STATE != StateMachine.State.InActive)
                break;
            yield return null;
        }

        // Let the arrival's last frame of bookkeeping (entrance door solidifying, camera
        // settle) land before tearing the room down.
        yield return new WaitForSeconds(0.5f);

        if (!Active) yield break;
        var name = _resolvedRooms[0];
        var bp = MapEditorSerialization.LoadByName(name);
        if (bp == null)
        {
            Plugin.Log.LogWarning($"MapEditor: level entrance blueprint '{name}' missing; playback stopped.");
            Stop();
            yield break;
        }

        Plugin.Log.LogInfo($"MapEditor: level entering room 1/{_resolvedRooms.Count} ('{name}').");
        editor.Loader.Load(bp);
    }

    public static void Stop()
    {
        if (Active) Plugin.Log.LogInfo($"MapEditor: level playback of '{_level.LevelName}' ended.");
        Active = false;
        _awaitingSceneEntry = false;
        _level = null;
        _resolvedRooms = null;
        if (_editor != null) _editor.SetMusicLoop(null);
    }

    // Called from the Door.ChangeRoom prefix mid black-fade. True = the door was consumed by
    // the level run; false = let vanilla ChangeRoom proceed (playback over or in a bad state).
    private static bool AdvanceThroughDoor(Door door)
    {
        if (!Active || _awaitingSceneEntry || _editor == null) return false;

        if (_currentIndex >= _resolvedRooms.Count - 1)
        {
            // Exit room reached its door: the run is complete. Exit behavior beyond this
            // (rewards, next-level selection) belongs to the map-selector phase.
            Plugin.Log.LogInfo($"MapEditor: level '{_level.LevelName}' complete.");
            Stop();
            return false;
        }

        _currentIndex++;
        var name = _resolvedRooms[_currentIndex];
        var bp = MapEditorSerialization.LoadByName(name);
        if (bp == null)
        {
            Plugin.Log.LogWarning($"MapEditor: level room blueprint '{name}' vanished mid-run; playback stopped.");
            Stop();
            return false;
        }

        // Enter the next room through the side the player came out of: opposite the used door.
        var entryDirection = door != null ? Opposite(door.direction.ToString()) : null;
        Plugin.Log.LogInfo($"MapEditor: level advancing to room {_currentIndex + 1}/{_resolvedRooms.Count} ('{name}').");
        _editor.Loader.Load(bp, entryDirection);
        return true;
    }

    private static string Opposite(string direction) => direction switch
    {
        "North" => "South",
        "South" => "North",
        "East" => "West",
        "West" => "East",
        _ => null
    };

    // Door.OnTriggerEnter2D has already faded the screen, locked input and flagged the door
    // Used by the time this runs; redirecting here keeps the whole vanilla trigger flow.
    [HarmonyPatch(typeof(Door), "ChangeRoom")]
    private static class Door_ChangeRoom_Patch
    {
        private static bool Prefix(Door __instance)
        {
            try
            {
                if (AdvanceThroughDoor(__instance)) return false;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"MapEditor: level door advance failed: {e}");
                Stop();
            }
            return true;
        }
    }
}
