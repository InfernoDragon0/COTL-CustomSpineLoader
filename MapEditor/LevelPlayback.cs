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

    public static CTLevelBlueprint CurrentLevel => _level;

    private static CTLevelBlueprint _level;
    private static List<string> _resolvedRooms;
    private static RuntimeMapEditor _editor;

    private static readonly Dictionary<BiomeRoom, int> _roomSlots = [];
    private static int _normalCursor;

    private static int _applyToken;
    private static ApplyState _pendingApply;
    private static bool _holdingResume;

    public static bool SuppressVanillaContent { get; private set; }

    public static void ClearContentSuppression() => SuppressVanillaContent = false;

    private static int _roomChangeTick;

    public static void NoteBiomeRoomChanged() => _roomChangeTick++;

    private class ApplyState
    {
        public GenerateRoom Room;
        public int Slot;
        public string EntryDirection;

        public bool AwaitRoomChange;

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

    public static string Start(CTLevelBlueprint level, RuntimeMapEditor editor)
    {
        if (level == null || editor == null) return "No level to play.";
        if (CTLevelDungeon.Instance == null) return "CTLevelDungeon is not registered.";

        var resolved = Resolve(level, out var error);
        if (resolved == null) return error;

        if (Active) Stop();

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

    private static List<string> Resolve(CTLevelBlueprint level, out string error)
    {
        error = null;

        var problem = LevelLayout.Validate(level);
        if (problem != null)
        {
            error = "Layout: " + problem;
            return null;
        }

        var saved = MapEditorSerialization.LoadAll();
        if (saved.Count == 0)
        {
            error = "No node blueprints saved yet.";
            return null;
        }

        var byName = new Dictionary<string, CTNodeBlueprint>();
        foreach (var node in saved) byName[node.MapName] = node;

        var resolved = new List<string>();
        for (var i = 0; i < level.Rooms.Count; i++)
        {
            if (level.Rooms[i].VanillaRoom != CTLevelRoom.Generated)
            {
                resolved.Add(CTLevelRoom.VanillaNode);
                continue;
            }

            var pool = new List<string>();
            foreach (var name in level.Rooms[i].NodePool)
                if (name == CTLevelRoom.VanillaNode || byName.ContainsKey(name)) pool.Add(name);

            if (level.Rooms[i].NodePool.Count > 0 && pool.Count == 0)
                Plugin.Log.LogWarning($"MapEditor: level room {i + 1} pool has no existing blueprints; using any.");

            if (pool.Count == 0)
                foreach (var name in byName.Keys) pool.Add(name);

            resolved.Add(pool[Random.Range(0, pool.Count)]);
        }

        return resolved;
    }

    public static string StartForMapNode(CTLevelBlueprint level)
    {
        if (level == null) return "No level bound to this node.";

        var resolved = Resolve(level, out var error);
        if (resolved == null) return error;

        if (Active) Stop();

        _level = level;
        _resolvedRooms = resolved;
        _roomSlots.Clear();
        _normalCursor = 0;
        _applyToken++;
        _pendingApply = null;
        Active = true;

        Plugin.Log.LogInfo($"MapEditor: level '{level.LevelName}' bound to a dungeon-map node - " +
                           $"rooms: {string.Join(", ", resolved)}.");
        return null;
    }

    public static void Stop()
    {
        if (Active)
            Plugin.Log.LogInfo($"MapEditor: level playback of '{_level.LevelName}' ended, " +
                               $"stopped by {Caller()}.");
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

        Tools.LightingTool.ClearOverride();
        Tools.LightingTool.ForgetRoomLighting();
    }

    private static string Caller()
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(2, false);
            var names = new List<string>();

            for (var i = 0; i < 2 && i < trace.FrameCount; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                if (method == null) continue;
                names.Add((method.DeclaringType != null ? method.DeclaringType.Name + "." : "") + method.Name);
            }

            return names.Count > 0 ? string.Join(" <- ", names.ToArray()) : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    public static void OnEditorReady(RuntimeMapEditor editor)
    {
        _editor = editor;

        if (!Active || _pendingApply == null)
        {
            SuppressVanillaContent = false;
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

        if (_roomSlots.Count == 0 && BiomeGenerator.Instance?.Rooms != null)
        {
            var built = BiomeGenerator.Instance.Rooms.Count;
            var wanted = _resolvedRooms.Count;

            if (built == wanted)
                Plugin.Log.LogInfo($"MapEditor: floor built with {built} room(s), as the level asks.");
            else
                Plugin.Log.LogWarning($"MapEditor: floor built with {built} room(s) for a level of " +
                                      $"{wanted} - blueprints will not line up with rooms.");
        }

        int slot;
        if (biomeRoom != null && LevelLayout.IsAuthored(_level))
        {
            slot = LevelLayout.SlotFor(_level, biomeRoom.x, biomeRoom.y);
            if (slot < 0)
            {
                Plugin.Log.LogWarning($"MapEditor: no authored room at cell ({biomeRoom.x}, " +
                                      $"{biomeRoom.y}); using the first room's blueprint.");
                slot = 0;
            }
        }
        else if (biomeRoom == null || _roomSlots.Count == 0)
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

        SuppressVanillaContent = _resolvedRooms[slot] != CTLevelRoom.VanillaNode;

        var state = new ApplyState
        {
            Room = room,
            Slot = slot,
            EntryDirection = Opposite(DungeonPatches.LastDoorDirection),
            AwaitRoomChange = DungeonPatches.LastDoorDirection != null
        };
        state.ArmCompletionFlag();

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
            _pendingApply = state;
            return;
        }
        _editor.StartCoroutine(ApplyRoomRoutine(state, token));
    }

    private static int AssignSlot(BiomeRoom biomeRoom)
    {
        var last = _resolvedRooms.Count - 1;

        if (_roomSlots.Count == 0) return 0;

        if (HasExitConnection(biomeRoom)) return last;

        _normalCursor++;

        var middles = last - 1;
        if (middles < 1) return Mathf.Max(0, last);

        return 1 + (_normalCursor - 1) % middles;
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
        var tickAtStart = _roomChangeTick;
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

        ResetRoomLocks();

        if (_resolvedRooms[state.Slot] == CTLevelRoom.VanillaNode)
        {
            Plugin.Log.LogInfo($"MapEditor: slot {state.Slot + 1} is a vanilla room; left as generated.");
            Tools.LightingTool.OnRoomEntered();
            ReleaseHold();
            _editor?.StartCoroutine(LockIfContested(token));
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

        if (state.AwaitRoomChange)
        {
            var signalDeadline = Time.unscaledTime + 4f;
            while (_roomChangeTick == tickAtStart && Time.unscaledTime < signalDeadline)
            {
                if (Abort(token)) yield break;
                yield return null;
            }
        }

        var sweep = Resources.UnloadUnusedAssets();
        while (sweep != null && !sweep.isDone)
        {
            if (Abort(token)) yield break;
            yield return null;
        }

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
                _editor.GetTool<Tools.DoorTool>()?.SealDoorsWithoutNeighbours();
                Plugin.Log.LogInfo("MapEditor: no enemies in this room; doors unlocked.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: could not unlock the room's doors: " + e.Message);
            }
        }

        ReleaseHold();
        _editor?.StartCoroutine(LockIfContested(token));
    }

    // ---- room locking ---------------------------------------------------------------------------

    private static void ResetRoomLocks()
    {
        try
        {
            foreach (var controller in RoomLockController.RoomLockControllers)
            {
                if (controller == null || controller.Standalone) continue;
                controller.Completed = false;
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not reset the room locks: " + e.Message);
        }
    }

    private static IEnumerator LockIfContested(int token)
    {
        var until = Time.unscaledTime + 1.5f;
        while (Time.unscaledTime < until)
        {
            if (Abort(token)) yield break;
            yield return null;
        }

        if (Abort(token)) yield break;
        LockIfEnemiesRemain();
    }

    private static void LockIfEnemiesRemain()
    {
        try
        {
            if (Health.team2.Count == 0) return;

            var room = BiomeGenerator.Instance != null ? BiomeGenerator.Instance.CurrentRoom : null;
            if (room != null && room.Completed) return;

            var open = false;
            foreach (var controller in RoomLockController.RoomLockControllers)
            {
                if (controller == null || controller.Standalone || !controller.Open) continue;
                open = true;
                break;
            }

            if (!open) return;

            Plugin.Log.LogInfo($"MapEditor: room has {Health.team2.Count} enemy(s) with its doors " +
                               "open; locking it.");
            RoomLockController.CloseAll();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not lock the room: " + e.Message);
        }
    }

    public static void OnContentReady()
    {
        if (!_holdingResume) return;

        Plugin.Log.LogInfo("MapEditor: room is built; lifting the fade for the walk-in.");
        ReleaseHold();
    }

    private static bool Abort(int token) => !Active || token != _applyToken;

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
