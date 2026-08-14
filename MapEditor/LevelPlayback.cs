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

// Drives a CTLevelBlueprint run through CTLevelDungeon, following the F5 convention end to
// end: EnterDungeon() reloads the scene, BiomeGenerator lays out NumRooms (from the level)
// with its normal walk, doors and room changes are fully vanilla, and every generated room is
// rebuilt from a node blueprint via the CustomDungeon.OnRoomGenerated hook.
//
// Presentation: the room hook fires inside the vanilla generation, while the door (or scene)
// transition still covers the screen. The apply routine holds that black cover
// (MMTransition.CanResume), waits for the room's own OnGenerated signal, swaps in the
// blueprint behind the cover, and only then resumes - so the player never sees the vanilla
// room and walks in exactly once.
//
// Slot mapping: every room slot resolves to one saved node blueprint up front (picked from
// its pool, or from all saved nodes when the pool is empty). The first generated room takes
// the Entrance slot, the room owning the exit (NextLayer) door takes the Exit slot, and other
// rooms consume the middle slots in the order the player discovers them. Revisited rooms
// regenerate vanilla content, so their remembered slot re-applies.
//
// All state is static: scene reloads destroy the editor host, and each fresh host re-binds
// via OnEditorReady.
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

    // True while a room is generating that we are about to rebuild from a blueprint. Vanilla's
    // content phases are skipped for it: every object they produce is destroyed by the rebuild
    // seconds later, and waiting for them is what made each room take the full apply timeout -
    // DisableIslands blocks until every island's async InitIsland reports back, and those never
    // completed behind the held fade. Rooms whose pool slot asks for vanilla never set this.
    public static bool SuppressVanillaContent { get; private set; }

    // Captures one room-apply request, created synchronously inside the generation hook.
    //
    // Completion is detected by polling GeneratedPathing rather than by subscribing to the
    // room's events: OnGenerated's delegate field is corrupted by other mods' reflection
    // (observed holding a float[], making add_OnGenerated throw) and OnGenerateComplete's
    // UnityEvent threw too. A flag we clear ourselves has no such dependency - and it has to
    // be an edge we create, because the whole biome shares ONE GenerateRoom object that is
    // re-generated per room, so its completion flags are already true from the room before.
    private class ApplyState
    {
        public GenerateRoom Room;
        public int Slot;
        public string EntryDirection;

        public void ArmCompletionFlag()
        {
            if (Room == null) return;
            Room.GeneratedPathing = false;
            // Vanilla raises GeneratedPathing from an async pathfinding callback and only then
            // finishes the coroutine (backdrop, sprite shape init). Waiting on that flag alone
            // let the rebuild start while generation was still adding objects, so vanilla
            // content landed on top of an already-cleared room. `generated` is the last thing
            // Generate() sets, so both together mean the room is genuinely finished.
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

    // CTLevelDungeon's per-room hook, via DungeonPatches' GenerateRoom.Generate postfix. Fires
    // once per room entry (GenCheck), including revisits, which regenerate vanilla content.
    // Runs synchronously inside the room's generation, behind the room-change transition.
    public static void OnRoomGenerated(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType)
    {
        // Harmony's enumerator patch hands us a null instance for the boot-time entrance
        // room, which silently skipped it. GenerateRoom.Instance is the same object (OnEnable
        // assigns it before Generate runs) and is always populated.
        if (room == null) room = SceneRefs.Room;

        if (!Active || room == null)
        {
            Plugin.Log.LogInfo($"MapEditor: level hook skipped (active={Active}, roomNull={room == null}, " +
                               $"type={connectionType}).");
            return;
        }

        // CurrentRoom is still null while the scene boots and the FIRST room generates - that
        // room is by definition the entrance. Its slot mapping is recorded later, once the
        // apply routine can see CurrentRoom.
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

            // ChangeRoomRoutine stops the clock mid-fade and leaves the fade-OUT to restart it,
            // and the transition pauses the simulation the same way. We are holding that
            // fade-out, so both stay stopped unless we restore them - and generation runs on
            // them. Costs nothing visually: the screen stays covered either way.
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
        // Wait for the flag armed at hook time to come back up: vanilla generation raises it
        // in its final pathfinding step, so this is the room's real end-of-generation edge.
        // With the content phases skipped this settles in a fraction of a second; the cap is a
        // backstop, not the expected path.
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

        // Nothing gates a room the blueprint left empty: vanilla releases the door locks when
        // the room's encounter is cleared, and with its content suppressed and no blueprint
        // enemies there is no encounter to clear - so the locks would stay shut for good.
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

    // Guarantees the next MMTransition.Play actually runs. Show() opens with
    // "if (IsPlaying) { CallBack?.Invoke(); return; }", so a transition still marked playing -
    // ours held mid-room-apply, or any transition orphaned by a coroutine dying with its
    // scene - turns every later Play into a silent no-op: the scene never loads, which is how
    // a level run left F5 (and itself) unable to enter a dungeon at all.
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

    // Every dungeon entry goes through here (F5's test dungeon, the exit door, Play Level), so
    // it is the one place that can guarantee a fresh scene load and stop a run that the entry
    // is leaving behind.
    [HarmonyPatch(typeof(CustomDungeon), nameof(CustomDungeon.EnterDungeon))]
    private static class CustomDungeon_EnterDungeon_Patch
    {
        private static void Prefix(CustomDungeon __instance)
        {
            if (Active && __instance is not CTLevelDungeon) Stop();
            ForceTransitionIdle();

            // The lighting override is global and outlives the room that set it. Entering a
            // dungeon by any route - F5, Reset Room, Play Level, the exit door - starts from
            // the biome's own lighting; a blueprint that wants its own re-applies it on load.
            Tools.LightingTool.ClearOverride();
        }
    }

    private static IEnumerator NoContent()
    {
        yield break;
    }

    // The island's own art and encounter spawn. DisableIslands starts one of these per island
    // and blocks until every one calls back, so this is the phase that stalled each room.
    // HideSprites is kept: it is what stops the island's flat placeholder fill from showing.
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
