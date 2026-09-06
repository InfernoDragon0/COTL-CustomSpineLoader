using System.Collections;
using MMTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomSpineLoader.MapEditor;

public static class HubSession
{
    public const string HubScene = "Base Biome 1";

    public enum Mode
    {
        None,
        Authoring,
        Playing
    }

    public static Mode Current { get; private set; }

    public static bool Active => Current != Mode.None;
    public static bool IsAuthoring => Current == Mode.Authoring;

    public static bool Busy => Active || _running || _pendingMode != Mode.None;

    public static string HubName { get; private set; }

    private static Mode _pendingMode;
    private static string _pendingHub;
    private static float _pendingSince;
    private static bool _running;

    // ---- entry points -------------------------------------------------------------------------

    public static string Author(string hubName)
    {
        if (string.IsNullOrWhiteSpace(hubName)) return "A hub needs a name.";
        return Begin(hubName, Mode.Authoring);
    }

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
        // In a multiplayer session only the host takes the party somewhere else.
        var held = Net.EditorNet.WhyNotWorldChange();
        if (held != null) return held;

        if (_running) return "A hub is already being prepared.";

        if (PlayerFarming.Instance == null) return "Hubs open in game, not on the menu.";

        BaseSession.End();
        SceneRefs.RoomOverride = null;
        SceneRefs.ContentRootOverride = null;

        _pendingHub = hubName;
        _pendingMode = mode;
        _pendingSince = Time.realtimeSinceStartup;

        HubCurtain.Raise($"Entering {hubName}...");

        if (SceneManager.GetActiveScene().name == HubScene)
        {
            StartPreparing();
            return null;
        }

        try
        {
            GameManager.ToShip(HubScene);
        }
        catch (System.Exception e)
        {
            _pendingHub = null;
            _pendingMode = Mode.None;
            HubCurtain.Lower();
            Plugin.Log.LogError("Hub: could not travel to the base: " + e);
            return "Could not travel to the base, see the log.";
        }

        return null;
    }

    public static void OnSceneLoaded(Scene scene)
    {
        EndActive();

        if (_pendingMode == Mode.None) return;

        if (Time.realtimeSinceStartup - _pendingSince > PendingTripTimeout)
        {
            _pendingHub = null;
            _pendingMode = Mode.None;
            HubCurtain.Lower();
            return;
        }

        if (scene.name != HubScene) return;

        StartPreparing();
    }

    private const float PendingTripTimeout = 90f;

    public static void End()
    {
        EndActive();
        _pendingHub = null;
        _pendingMode = Mode.None;
        HubCurtain.Lower();
    }

    private static void EndActive()
    {
        HubStructureStore.TearDown();

        Current = Mode.None;
        HubName = null;
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

        var deadline = Time.realtimeSinceStartup + 30f;
        var settledSince = 0f;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (!ArrivalFinished())
            {
                settledSince = 0f;
            }
            else
            {
                if (settledSince <= 0f) settledSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - settledSince >= ArrivalSettleSeconds) break;
            }

            yield return null;
        }

        if (BiomeBaseManager.Instance == null || PlayerFarming.Instance == null)
        {
            Plugin.Log.LogWarning("Hub: the base never finished loading; hub not opened.");
            _running = false;
            HubCurtain.Lower();
            yield break;
        }

        yield return null;
        yield return null;

        Current = mode;
        HubName = hubName;

        BiomeBaseManager.Instance.ActivateDLCShrineRoom();
        PlayerFarming.Location = FollowerLocation.DLC_ShrineRoom;
        PlayerFarming.LastLocation = FollowerLocation.Base;

        yield return null;
        yield return null;

        if (!ReferenceEquals(SceneRefs.Room, BiomeBaseManager.Instance.DLC_ShrineRoom))
        {
            Plugin.Log.LogError("Hub: the town room did not become the active room; refusing to " +
                                "clear the base. No hub was opened.");
            End();
            yield break;
        }

        var editor = EnsureEditor();
        if (editor == null)
        {
            Plugin.Log.LogWarning("Hub: no map editor could be created in the base.");
            _running = false;
            HubCurtain.Lower();
            yield break;
        }

        editor.GetTool<Tools.ShapeTool>()?.PrepareForLoad();
        editor.GetTool<Tools.PodiumTool>()?.AcquireTemplate();

        CaptureArrivalStart();

        ParkPlayersOutsideRoom();
        SuppressTown();
        yield return ClearRoom(editor);

        EnsureContentRoot();
        ResetTownCollision();
        AdoptPlayers();

        if (mode == Mode.Authoring)
        {
            yield return ArriveAtSpawn();
            editor.BeginHubAuthoring(hubName);
            ReportPlayer("handed to the editor");
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
            }

            yield return ArriveAtSpawn();
            ReportPlayer("in the hub");
        }

        _running = false;

        Plugin.Instance.StartCoroutine(HoldControl());
    }

    private static bool Stuck(StateMachine.State state) =>
        state is StateMachine.State.InActive or StateMachine.State.SpawnIn
            or StateMachine.State.Respawning or StateMachine.State.CustomAnimation
            or StateMachine.State.TimedAction or StateMachine.State.Grabbed
            or StateMachine.State.Building;

    private const float StuckGraceSeconds = 1.5f;

    private static bool _reportedStuck;

    private static IEnumerator HoldControl()
    {
        var stuckSince = 0f;
        _reportedStuck = false;

        while (Active)
        {
            var editing = RuntimeMapEditor.Active != null && RuntimeMapEditor.Active.IsEditing;

            var player = PlayerFarming.Instance;
            var stuck = !editing && player != null && player.state != null &&
                        Stuck(player.state.CURRENT_STATE) && !MMTransition.IsPlaying;

            if (!stuck)
            {
                if (!editing && player != null && player.state != null && !MMTransition.IsPlaying)
                {
                    if (_reportedStuck) Plugin.Log.LogInfo("Hub: the player is free; the watchdog " +
                                                           "stands down.");
                    yield break;
                }

                stuckSince = 0f;
                _reportedStuck = false;
            }
            else
            {
                if (stuckSince <= 0f) stuckSince = Time.realtimeSinceStartup;

                if (Time.realtimeSinceStartup - stuckSince >= StuckGraceSeconds)
                {
                    if (!_reportedStuck)
                    {
                        _reportedStuck = true;
                        ReportPlayer("stuck, freeing");
                    }

                    foreach (var each in PlayerFarming.players)
                        if (each != null && each.GoToAndStopping) each.AbortGoTo(InvokeAbortCallback: false);

                    PlayerFarming.SetStateForAllPlayers(StateMachine.State.Idle);
                    foreach (var each in PlayerFarming.players)
                        each?.simpleSpineAnimator?.UpdateIdleAndMoving();

                    FinishArrival();
                }
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    private static void ReportPlayer(string when)
    {
        var player = PlayerFarming.Instance;
        if (player == null)
        {
            Plugin.Log.LogWarning($"Hub ({when}): there is no PlayerFarming.Instance at all.");
            return;
        }

        var parent = player.transform.parent;
        var parentName = parent != null ? parent.name : "(scene root)";

        var manager = GameManager.GetInstance();
        var camera = manager != null ? manager.CamFollowTarget : null;
        var cameraAt = camera != null ? camera.transform.position.ToString() : "(no camera)";
        var blockedBy = "none";

        for (var t = player.transform; t != null; t = t.parent)
            if (!t.gameObject.activeSelf)
            {
                blockedBy = t.name;
                break;
            }

        var spine = player.Spine;
        var spineShown = spine != null && spine.gameObject.activeInHierarchy;
        var track = spine != null && spine.AnimationState != null
            ? spine.AnimationState.GetCurrent(0)
            : null;
        var animation = track?.Animation != null ? track.Animation.Name : "(none)";

        Plugin.Log.LogInfo($"Hub ({when}): state={player.state?.CURRENT_STATE}, " +
                           $"activeInHierarchy={player.gameObject.activeInHierarchy}, " +
                           $"spineShown={spineShown}, animation='{animation}', " +
                           $"parent='{parentName}', inactiveAncestor='{blockedBy}', " +
                           $"pos={player.transform.position}, camera={cameraAt}, " +
                           $"room={(SceneRefs.Room != null ? SceneRefs.Room.transform.position.ToString() : "(none)")}, " +
                           $"timeScale={Time.timeScale}, transition={MMTransition.IsPlaying}");
    }

    private static void EnsureContentRoot()
    {
        var room = SceneRefs.Room;
        if (room == null || room.CustomTransform != null) return;

        var go = new GameObject("CultTweaker_HubContent");
        go.transform.SetParent(room.transform, false);
        go.transform.localPosition = Vector3.zero;
        room.CustomTransform = go;

        Plugin.Log.LogInfo("Hub: gave the town room a content root for the editor's tools.");
    }

    private static void FinishArrival()
    {
        try
        {
            PlayerFarming.SetCollidersActive(collidersActive: true);

            var manager = GameManager.GetInstance();
            if (manager != null)
            {
                manager.OnConversationEnd(SetPlayerToIdle: true);
                manager.CameraSetOffset(Vector3.zero);

                var camera = manager.CamFollowTarget;
                if (camera != null)
                {
                    camera.ClearAllTargets();
                    camera.enabled = true;
                }

                manager.AddPlayerToCamera();

                var player = PlayerFarming.Instance;
                if (camera != null && player != null) camera.ForceSnapTo(player.transform.position);
            }

            CoopManager.RefreshCoopPlayerRewired();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Hub: handing control back to the player failed: " + e.Message);
        }

        try
        {
            if (MMTransition.IsPlaying)
            {
                MMTransition.CanResume = true;
                MMTransition.ResumePlay();
            }

            if (Time.timeScale <= 0f) Time.timeScale = 1f;
            SimulationManager.UnPause();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Hub: could not clear the arrival transition: " + e.Message);
        }
    }

    private static void ParkPlayersOutsideRoom()
    {
        foreach (var player in PlayerFarming.players)
            if (player != null && player.transform.parent != null)
                player.transform.SetParent(null, worldPositionStays: true);
    }

    private const float ArrivalSettleSeconds = 1.25f;

    private static bool ArrivalFinished()
    {
        if (BiomeBaseManager.Instance == null) return false;

        var player = PlayerFarming.Instance;
        if (player == null || !player.gameObject.activeInHierarchy) return false;

        if (MMTransition.IsPlaying) return false;

        return player.state == null || !Stuck(player.state.CURRENT_STATE);
    }

    private static Vector3? _arrivalStart;

    private static void CaptureArrivalStart()
    {
        _arrivalStart = null;

        var manager = DLCShrineRoomLocationManager.Instance;
        if (manager == null) return;

        if (manager.EntranceFromBase != null) _arrivalStart = manager.EntranceFromBase.position;
        else if (manager.DoorStartPosition != null) _arrivalStart = manager.DoorStartPosition.position;
    }

    public static Vector3? SpawnPoint()
    {
        foreach (var trigger in Tools.CTMapTrigger.All)
        {
            if (trigger == null) continue;

            foreach (var action in trigger.Actions)
                if (action != null && action.Type == Tools.TriggerActionType.HubSpawnPoint)
                    return trigger.transform.position;
        }

        return null;
    }

    private const float WalkInDistance = 3.5f;
    private const float WalkInSeconds = 3f;

    private static IEnumerator ArriveAtSpawn()
    {
        var player = PlayerFarming.Instance;
        if (player == null)
        {
            HubCurtain.Lower();
            yield break;
        }

        foreach (var each in PlayerFarming.players)
            if (each != null && each.GoToAndStopping) each.AbortGoTo(InvokeAbortCallback: false);

        var authored = SpawnPoint();
        var mark = authored ?? ArrivalStart();
        var from = mark - new Vector3(0f, WalkInDistance, 0f);

        var walk = GroundUnder(from) && GroundUnder(mark);

        var start = walk ? from : mark;
        PlayerFarming.PositionAllPlayers(start);
        foreach (var each in PlayerFarming.players)
            if (each != null) each.transform.position = start;

        Plugin.Log.LogInfo($"Hub: arriving at {mark} " +
                           (authored.HasValue
                               ? "(the hub's own spawn point)"
                               : "(no spawn point authored; the town's door)") +
                           (walk ? "." : "; no walkable ground for the walk-in, placing directly."));

        EndBaseArrival(player);
        FinishArrival();

        Tools.CTMapTrigger.MuteFiring(WalkInSeconds + 2f);

        yield return HubCurtain.LowerAndWait();

        if (!walk) yield break;

        player.GoToAndStop(mark, null, IdleOnEnd: true, DisableCollider: false, GoToCallback: null,
            maxDuration: WalkInSeconds, forcePositionOnTimeout: true);

        var deadline = Time.realtimeSinceStartup + WalkInSeconds + 1.5f;
        while (player.GoToAndStopping && Time.realtimeSinceStartup < deadline) yield return null;

        if (player.GoToAndStopping)
        {
            Plugin.Log.LogWarning("Hub: the arrival walk never finished; putting the player on the mark.");
            player.AbortGoTo(InvokeAbortCallback: false);
        }

        if (Vector2.Distance(player.transform.position, mark) > 2f)
        {
            Plugin.Log.LogWarning($"Hub: the arrival walk ended at {player.transform.position} " +
                                  $"instead of {mark}; snapping to the mark.");
            player.transform.position = mark;
        }
    }

    private static bool GroundUnder(Vector3 point)
    {
        if (AstarPath.active == null) return false;

        try
        {
            var nearest = AstarPath.active.GetNearest(point);
            return nearest.node != null && nearest.node.Walkable &&
                   Vector2.Distance((Vector3)nearest.position, point) <= 2.5f;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static Vector3 ArrivalStart()
    {
        var manager = DLCShrineRoomLocationManager.Instance;
        var start = _arrivalStart;

        if (manager != null && manager.EntranceFromBase != null)
            start = manager.EntranceFromBase.position;

        var centre = RoomCentre();
        if (!start.HasValue) return centre;

        return Vector2.Distance(start.Value, centre) > 80f ? centre : start.Value;
    }

    private static void EndBaseArrival(PlayerFarming player)
    {
        try
        {
            foreach (var each in PlayerFarming.players)
            {
                if (each == null) continue;

                var spine = each.Spine;
                if (spine != null && !spine.gameObject.activeSelf) spine.gameObject.SetActive(true);
            }

            PlayerFarming.SetStateForAllPlayers(StateMachine.State.Idle);

            foreach (var each in PlayerFarming.players)
                each?.simpleSpineAnimator?.UpdateIdleAndMoving();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Hub: could not end the base's arrival: " + e.Message);
        }
    }

    private static Vector3 RoomCentre()
    {
        var room = SceneRefs.Room;
        var centre = room != null ? room.transform.position : Vector3.zero;

        if (AstarPath.active != null)
        {
            var nearest = AstarPath.active.GetNearest(centre);

            if (nearest.node != null && nearest.node.Walkable &&
                Vector2.Distance((Vector3)nearest.position, centre) <= 12f)
                centre = (Vector3)nearest.position;
        }

        centre.z = 0f;
        return centre;
    }

    private static void AdoptPlayers()
    {
        var room = SceneRefs.Room;
        if (room == null || PlayerFarming.Instance == null) return;

        var host = DLCShrineRoomLocationManager.Instance != null &&
                   DLCShrineRoomLocationManager.Instance.UnitLayer != null
            ? DLCShrineRoomLocationManager.Instance.UnitLayer
            : room.transform;

        foreach (var player in PlayerFarming.players)
        {
            if (player == null) continue;
            if (!player.gameObject.activeSelf) player.gameObject.SetActive(true);
            if (player.transform.parent != host) player.transform.SetParent(host, worldPositionStays: true);
        }
    }

    private static void ResetTownCollision()
    {
        var manager = DLCShrineRoomLocationManager.Instance;
        if (manager != null && manager.sceneryCollider != null)
        {
            try
            {
                manager.sceneryCollider.GenerateGeometry();
                manager.sceneryCollider.enabled = false;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Hub: could not clear the town's scenery collider: " + e.Message);
            }
        }

        var room = SceneRefs.Room;
        if (room != null)
        {
            try
            {
                var composite = SceneRefs.EnsureRoomComposite();

                composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
                composite.GenerateGeometry();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Hub: could not rebuild the room collider: " + e.Message);
            }
        }

        SceneRefs.RescanNavigation();
    }

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

        yield return null;
    }

    private static RuntimeMapEditor EnsureEditor() => RuntimeMapEditor.Ensure("RuntimeMapEditorHost_Hub");

    // ---- the hub record -----------------------------------------------------------------------

    public static string BlueprintFor(CTLevelBlueprint hub)
    {
        if (hub is not { IsHub: true } || hub.Rooms.Count == 0) return null;

        var pool = hub.Rooms[0].NodePool;
        if (pool == null || pool.Count == 0) return null;

        return string.IsNullOrWhiteSpace(pool[0]) ? null : pool[0];
    }

    public static System.Collections.Generic.HashSet<string> BlueprintNames()
    {
        var names = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var level in CTLevelSerialization.LoadAll())
        {
            var blueprint = BlueprintFor(level);
            if (blueprint != null) names.Add(MapEditorSerialization.Sanitize(blueprint));
        }

        return names;
    }

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

    public static string FreeName(string stem = "untitledhub")
    {
        for (var i = 1; i < 1000; i++)
        {
            var candidate = stem + i;
            if (!CTLevelSerialization.Available(candidate)) return candidate;
        }
        return stem;
    }
}
