using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.Patches;
using HarmonyLib;
using MMBiomeGeneration;
using MMRoomGeneration;
using MMTools;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class LevelPlayback
{
    public static bool Active { get; private set; }

    private static CTLevelBlueprint _level;
    private static List<string> _resolvedRooms;
    private static RuntimeMapEditor _editor;

    // BiomeRoom -> assigned slot; rooms keep their slot for the whole run.
    private static readonly Dictionary<BiomeRoom, int> _roomSlots = [];
    private static int _normalCursor;

    // Rapid door usage can outrun a slow room apply; the token makes stale routines drop out.
    private static int _applyToken;
    private static ApplyState _pendingApply;
    private static bool _holdingResume;

    public static bool SuppressVanillaContent { get; private set; }

    private class ApplyState
    {
        public GenerateRoom Room;
        public int Slot;
        public string EntryDirection;

        public void ArmCompletionFlag()
        {
            if (Room == null) return;
            Room.GeneratedPathing = false;
            Room.generated = false;
        }

        public bool GenerationDone => Room == null || (Room.GeneratedPathing && Room.generated);
    }

    public static string Describe()
    {
        if (!Active) return "";
        return $"'{_level.LevelName}' ({_resolvedRooms.Count} rooms)";
    }

    // Resolves every room up front so a run never dead-ends mid-way on an empty pool, then
    // enters CTLevelDungeon. Returns null on success, otherwise the reason it can't start.
    public static string Start(CTLevelBlueprint level, RuntimeMapEditor editor)
    {
        if (level == null || editor == null) return "No level to play.";
        if (CTLevelDungeon.Instance == null) return "CTLevelDungeon is not registered.";

        var saved = MapEditorSerialization.LoadAll();
        if (saved.Count == 0) return "No node blueprints saved yet.";

        // Starting on top of a previous run would carry its slot map and transition hold.
        if (Active) Stop();

        var byName = new Dictionary<string, CTNodeBlueprint>();
        foreach (var node in saved) byName[node.MapName] = node;

        var resolved = new List<string>();
        for (var i = 0; i < level.Rooms.Count; i++)
        {
            var pool = new List<string>();
            foreach (var name in level.Rooms[i].NodePool)
                if (name == CTLevelRoom.VanillaNode || byName.ContainsKey(name)) pool.Add(name);

            if (level.Rooms[i].NodePool.Count > 0 && pool.Count == 0)
                Plugin.Log.LogWarning($"MapEditor: level room {i + 1} pool has no existing blueprints; using any.");

            if (pool.Count == 0)
                foreach (var name in byName.Keys) pool.Add(name);

            resolved.Add(pool[Random.Range(0, pool.Count)]);
        }

        _level = level;
        _resolvedRooms = resolved;
        _roomSlots.Clear();
        _normalCursor = 0;
        _applyToken++;
        _pendingApply = null;
        Active = true;

        CTLevelDungeon.Instance.Level = level;

        Plugin.Log.LogInfo($"MapEditor: level '{level.LevelName}' started - rooms: {string.Join(", ", resolved)}. " +
                           "Entering CTLevelDungeon.");

        // The scene change destroys the editor host; close it first so timeScale/HUD/camera
        // are sane for the transition (mirrors what Reset does before EnterDungeon).
        editor.ExitForPlayback();
        try
        {
            CTLevelDungeon.Instance.EnterDungeon();
        }
        catch (System.Exception e)
        {
            Stop();
            Plugin.Log.LogError($"MapEditor: dungeon entry failed: {e}");
            return "Dungeon entry failed, see log.";
        }
        return null;
    }

    public static void Stop()
    {
        if (Active) Plugin.Log.LogInfo($"MapEditor: level playback of '{_level.LevelName}' ended.");
        Active = false;
        SuppressVanillaContent = false;
        _level = null;
        _resolvedRooms = null;
        _roomSlots.Clear();
        _pendingApply = null;
        _applyToken++;
        ReleaseHold();
        if (CTLevelDungeon.Instance != null) CTLevelDungeon.Instance.Level = null;
        if (_editor != null) _editor.SetMusicLoop(null);

        // The run's last room may have left a lighting override in place; it is global state,
        // so it would follow the player out of the level.
        Tools.LightingTool.ClearOverride();
    }

    // Called by every freshly created editor host (one per scene load). The entrance room's
    // generation hook can fire before the host exists, so a deferred apply is picked up here.
    public static void OnEditorReady(RuntimeMapEditor editor)
    {
        _editor = editor;

        if (!Active || _pendingApply == null)
        {
            // A hold whose apply routine died with the previous scene would otherwise keep
            // MMTransition marked playing forever.
            if (_holdingResume) ReleaseHold();
            return;
        }

        var pending = _pendingApply;
        _pendingApply = null;
        editor.StartCoroutine(ApplyRoomRoutine(pending, ++_applyToken));
    }

    public static void OnRoomGenerated(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType)
    {
        if (room == null) room = SceneRefs.Room;

        if (!Active || room == null)
        {
            Plugin.Log.LogInfo($"MapEditor: level hook skipped (active={Active}, roomNull={room == null}, " +
                               $"type={connectionType}).");
            return;
        }

        var biomeRoom = BiomeGenerator.Instance != null ? BiomeGenerator.Instance.CurrentRoom : null;

        int slot;
        if (biomeRoom == null || _roomSlots.Count == 0)
        {
            slot = 0;
        }
        else if (!_roomSlots.TryGetValue(biomeRoom, out slot))
        {
            slot = AssignSlot(biomeRoom);
        }
        if (biomeRoom != null) _roomSlots[biomeRoom] = slot;

        Plugin.Log.LogInfo($"MapEditor: level room generated - applying slot {slot + 1}/{_resolvedRooms.Count} " +
                           $"('{_resolvedRooms[slot]}', {connectionType}).");

        // Set before generation continues past this first step, so the content phases it is
        // about to run are skipped outright rather than waited on.
        SuppressVanillaContent = _resolvedRooms[slot] != CTLevelRoom.VanillaNode;

        var state = new ApplyState
        {
            Room = room,
            Slot = slot,
            // Enter through the side the player came out of: opposite the used door. The
            // first room has no prior door and uses the blueprint's own entrance.
            EntryDirection = Opposite(DungeonPatches.LastDoorDirection)
        };
        state.ArmCompletionFlag();

        // Keep the room-change fade black until the blueprint is in; without this the vanilla
        // room is revealed first and then visibly swapped.
        if (MMTransition.IsPlaying && !_holdingResume)
        {
            MMTransition.CanResume = false;
            _holdingResume = true;

            if (Time.timeScale <= 0f) Time.timeScale = 1f;
            try
            {
                SimulationManager.UnPause();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: could not resume the simulation behind the fade: " + e.Message);
            }
        }

        var token = ++_applyToken;
        if (_editor == null)
        {
            // Scene still booting; OnEditorReady picks this up. The subscription above is
            // already live, so a generation finishing in the meantime is not missed.
            _pendingApply = state;
            return;
        }
        _editor.StartCoroutine(ApplyRoomRoutine(state, token));
    }

    private static int AssignSlot(BiomeRoom biomeRoom)
    {
        var last = _resolvedRooms.Count - 1;

        // First discovered room is the entrance.
        if (_roomSlots.Count == 0) return 0;

        // The room owning the exit door is the Exit slot wherever the walk placed it.
        if (HasExitConnection(biomeRoom)) return last;

        // Everything else consumes the middle slots in discovery order, clamped so extra
        // rooms reuse the final Normal slot rather than stealing the Exit one.
        _normalCursor++;
        return Mathf.Clamp(_normalCursor, 1, Mathf.Max(1, last - 1));
    }

    private static bool HasExitConnection(BiomeRoom room)
    {
        return IsExit(room.N_Room) || IsExit(room.E_Room) || IsExit(room.S_Room) || IsExit(room.W_Room);
    }

    private static bool IsExit(RoomConnection connection)
    {
        if (connection == null) return false;
        return connection.ConnectionType == GenerateRoom.ConnectionTypes.NextLayer ||
               connection.ConnectionType == GenerateRoom.ConnectionTypes.Exit;
    }

    private static IEnumerator ApplyRoomRoutine(ApplyState state, int token)
    {
        var startedAt = Time.unscaledTime;
        var deadline = startedAt + 8f;
        while (!state.GenerationDone && Time.unscaledTime < deadline)
        {
            if (Abort(token)) { SuppressVanillaContent = false; yield break; }
            yield return null;
        }
        SuppressVanillaContent = false;

        if (!state.GenerationDone)
            Plugin.Log.LogWarning("MapEditor: room generation signal timed out; rebuilding anyway.");
        else
            Plugin.Log.LogInfo($"MapEditor: room shell ready in {Time.unscaledTime - startedAt:0.00}s; " +
                               "rebuilding behind the fade.");

        // A pool slot can ask for the room the game generated, so a level mixes authored and
        // vanilla rooms. Nothing to rebuild - just lift the fade on what is already there.
        if (_resolvedRooms[state.Slot] == CTLevelRoom.VanillaNode)
        {
            Plugin.Log.LogInfo($"MapEditor: slot {state.Slot + 1} is a vanilla room; left as generated.");
            // A previous room's lighting override is global and outlives the room that set it,
            // so a vanilla room has to drop it or it keeps that room's mood.
            Tools.LightingTool.ClearOverride();
            ReleaseHold();
            yield break;
        }

        while (_editor != null && _editor.Loader.IsLoading)
        {
            if (Abort(token)) yield break;
            yield return null;
        }
        if (Abort(token) || _editor == null)
        {
            if (_editor == null) ReleaseHold();
            yield break;
        }

        // The boot-time entrance had no CurrentRoom to key its slot under; record it now so a
        // revisit finds the same slot instead of consuming a fresh one.
        var current = BiomeGenerator.Instance != null ? BiomeGenerator.Instance.CurrentRoom : null;
        if (current != null && !_roomSlots.ContainsKey(current)) _roomSlots[current] = state.Slot;

        var name = _resolvedRooms[state.Slot];
        var bp = MapEditorSerialization.LoadByName(name);
        if (bp == null)
        {
            Plugin.Log.LogWarning($"MapEditor: level room blueprint '{name}' vanished mid-run; playback stopped.");
            Stop();
            yield break;
        }

        // The whole load runs behind the held black cover; the walk-in it queues at the end
        // starts moving when the resume below restores time.
        _editor.Loader.Load(bp, state.EntryDirection);
        while (_editor != null && _editor.Loader.IsLoading)
        {
            if (Abort(token)) yield break;
            yield return null;
        }

        if (bp.Enemies.Count == 0)
        {
            try
            {
                RoomLockController.RoomCompleted();
                // RoomCompleted opens every lock in the room, including the barriers standing in
                // for doors that lead nowhere - so those have to be sealed again after it.
                _editor.GetTool<Tools.DoorTool>()?.SealDoorsWithoutNeighbours();
                Plugin.Log.LogInfo("MapEditor: no enemies in this room; doors unlocked.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: could not unlock the room's doors: " + e.Message);
            }
        }

        ReleaseHold();
    }

    // A newer room apply or a Stop() invalidates this routine. Stop/newer-token paths release
    // the hold themselves (Stop directly, a newer apply by re-holding then releasing).
    private static bool Abort(int token) => !Active || token != _applyToken;

    // Lets the room-change transition finish: the fade lifts on the blueprint room. Safe to
    // call when nothing is held or playing.
    private static void ReleaseHold()
    {
        if (!_holdingResume) return;
        _holdingResume = false;
        MMTransition.CanResume = true;
        if (MMTransition.IsPlaying) MMTransition.ResumePlay();
    }

    private static void ForceTransitionIdle()
    {
        _holdingResume = false;
        try
        {
            MMTransition.CanResume = true;
            MMTransition.StopCurrentTransition();

            // StopCurrentTransition only clears the flag when a transition coroutine is still
            // referenced; an orphaned one leaves IsPlaying stuck true.
            if (MMTransition.IsPlaying)
            {
                Plugin.Log.LogInfo("MapEditor: clearing a stuck transition before dungeon entry.");
                MMTransition.IsPlaying = false;
                SimulationManager.UnPause();
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: transition reset failed: " + e.Message);
        }

        // The fade-out that normally restores time never ran for a forced-idle transition.
        if (Time.timeScale <= 0f) Time.timeScale = 1f;
    }

    [HarmonyPatch(typeof(CustomDungeon), nameof(CustomDungeon.EnterDungeon))]
    private static class CustomDungeon_EnterDungeon_Patch
    {
        private static void Prefix(CustomDungeon __instance)
        {
            if (Active && __instance is not CTLevelDungeon) Stop();
            ForceTransitionIdle();

            Tools.LightingTool.ClearOverride();
        }
    }

    private static IEnumerator NoContent()
    {
        yield break;
    }

    [HarmonyPatch(typeof(IslandPiece), nameof(IslandPiece.InitIsland))]
    private static class IslandPiece_InitIsland_Patch
    {
        private static bool Prefix(IslandPiece __instance, System.Action completeCallback, ref IEnumerator __result)
        {
            if (!SuppressVanillaContent) return true;

            __instance.HideSprites();
            completeCallback?.Invoke();
            __result = NoContent();
            return false;
        }
    }

    // Decorations and critters: hundreds of pooled spawns, every one of them destroyed by the
    // rebuild that follows.
    [HarmonyPatch(typeof(GenerateRoom), "SpawnDecorations")]
    private static class GenerateRoom_SpawnDecorations_Patch
    {
        private static bool Prefix(GenerateRoom __instance, ref IEnumerator __result)
        {
            if (!SuppressVanillaContent) return true;

            __instance.GeneratedDecorations = true;
            __result = NoContent();
            return false;
        }
    }

    [HarmonyPatch(typeof(GenerateRoom), "SpawnSpecialContent")]
    private static class GenerateRoom_SpawnSpecialContent_Patch
    {
        private static bool Prefix() => !SuppressVanillaContent;
    }

    private static string Opposite(string direction) => direction switch
    {
        "North" => "South",
        "South" => "North",
        "East" => "West",
        "West" => "East",
        _ => null
    };
}
