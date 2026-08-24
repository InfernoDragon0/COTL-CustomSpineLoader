using System.Collections;
using MMTools;
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
    private static float _pendingSince;
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
        _pendingSince = Time.realtimeSinceStartup;

        // From here until the player is standing in the hub there is nothing on the way that is
        // worth looking at - least of all the town being emptied - so the screen goes dark first,
        // wearing the game's own loading corner so a covered screen reads as loading, not a hang.
        HubCurtain.Raise($"Entering {hubName}...");

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
            HubCurtain.Lower();
            Plugin.Log.LogError("Hub: could not travel to the base: " + e);
            return "Could not travel to the base, see the log.";
        }

        return null;
    }

    // Called by the plugin for every scene load. A running session never survives one - including a
    // reload of the base itself, which is how a hub is left: the town comes back untouched and
    // nothing of the hub survives it.
    //
    // A PENDING one does survive, until the base itself arrives. The trip there is not always one
    // load - a transition or a loading scene can land first - and ending the request on the first
    // scene that was not the base is what made travelling to a hub from a dungeon do nothing until
    // the button was pressed a second time (from the base, where no trip is needed at all).
    public static void OnSceneLoaded(Scene scene)
    {
        EndActive();

        if (_pendingMode == Mode.None) return;

        // A request nobody ever landed: a death warp or a menu trip can leave one behind, and it
        // must not build a hub around some later arrival in the base.
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

    // How long a trip to the base stays valid, in seconds.
    private const float PendingTripTimeout = 90f;

    // The session is over: either the scene changed under it, or the player left the hub. Also
    // drops a trip that has not landed yet.
    public static void End()
    {
        EndActive();
        _pendingHub = null;
        _pendingMode = Mode.None;
        HubCurtain.Lower();
    }

    private static void EndActive()
    {
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

        // The base's own arrival is left alone and waited out. Holding its transition to hide it
        // was tried and reverted: the arrival sets the player up (state, camera, animation) as part
        // of running, and a hub built behind a held cover inherited a half-finished one - an
        // invisible, immovable player. Seeing the base for a moment is the price of a player that
        // works, and it is the right price.
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

        // A couple of frames more: the room manager parks the town room at its authored position in
        // the same breath, and the player must not be placed against the old one.
        yield return null;
        yield return null;

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

        var editor = EnsureEditor();
        if (editor == null)
        {
            Plugin.Log.LogWarning("Hub: no map editor could be created in the base.");
            _running = false;
            HubCurtain.Lower();
            yield break;
        }

        // Before the sweep, not after: the shape tool builds new terrain by cloning a live sprite
        // shape, and the only ones in this scene belong to the room about to be emptied - the base
        // room's own are switched off with it, and FindObjectOfType does not see those.
        editor.GetTool<Tools.ShapeTool>()?.PrepareForLoad();
        editor.GetTool<Tools.PodiumTool>()?.AcquireTemplate();

        // Where the town's own arrival puts the player, remembered before the sweep can take the
        // transform it is measured from.
        CaptureArrivalStart();

        // The town is somebody else's furniture; a hub is authored from nothing. The players step
        // out to the scene root first: anything holding them is protected from the sweep, and a
        // protected container is a piece of town that stays standing in the finished hub.
        ParkPlayersOutsideRoom();
        SuppressTown();
        yield return ClearRoom(editor);

        // After the sweep, all of them: the content root would be swept away with everything else,
        // and moving the player into the room BEFORE it is emptied hands the player to the sweep -
        // which is exactly what destroyed it, and with it the F7 panel, which refuses to open
        // without a player.
        EnsureContentRoot();
        ResetTownCollision();
        AdoptPlayers();

        if (mode == Mode.Authoring)
        {
            // No blueprint is in the room yet, so there is no spawn point to arrive at: the arrival
            // plays wherever the town's own door was, which is where the author is dropped.
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

            // Last, so the player lands on the ground the hub actually has - and on the spawn point
            // the blueprint just rebuilt, which only exists once the triggers are back.
            yield return ArriveAtSpawn();
            ReportPlayer("in the hub");
        }

        _running = false;

        // Started last: while the room is being prepared the player is legitimately mid-arrival,
        // and a watchdog running through that would count the preparation itself as being stuck.
        Plugin.Instance.StartCoroutine(HoldControl());
    }

    // Every state PlayerFarming.Update refuses to take input in and that nothing in a town is
    // supposed to leave the player sitting in.
    private static bool Stuck(StateMachine.State state) =>
        state is StateMachine.State.InActive or StateMachine.State.SpawnIn
            or StateMachine.State.Respawning or StateMachine.State.CustomAnimation
            or StateMachine.State.TimedAction or StateMachine.State.Grabbed
            or StateMachine.State.Building;

    // How long the player may sit in a state it cannot act from before this counts as stuck rather
    // than as something the game is still in the middle of.
    private const float StuckGraceSeconds = 1.5f;

    // Runs only until the player is confirmed free. The arrival can leave the player wedged in a
    // state it cannot act from - that is what this exists to undo, and it keeps acting until it
    // takes. But the first sight of a free, controllable player is the end of its job: from that
    // moment anything that parks the player - an NPC conversation, the F7 panel, a trigger's
    // cutscene - is doing it on purpose, and a watchdog still running was yanking the player out
    // of dialogue mid-sentence.

    private static bool _reportedStuck;

    private static IEnumerator HoldControl()
    {
        var stuckSince = 0f;
        _reportedStuck = false;

        while (Active)
        {
            // Not while the editor has the room: it holds the world at timeScale 0 on purpose, and
            // an animation that cannot advance is not an animation that is stuck.
            var editing = RuntimeMapEditor.Active != null && RuntimeMapEditor.Active.IsEditing;

            var player = PlayerFarming.Instance;
            var stuck = !editing && player != null && player.state != null &&
                        Stuck(player.state.CURRENT_STATE) && !MMTransition.IsPlaying;

            if (!stuck)
            {
                // A real observation of a free player, not just a frame nobody could judge.
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
                    // Said once per episode; acted on every tick until it takes.
                    if (!_reportedStuck)
                    {
                        _reportedStuck = true;
                        ReportPlayer("stuck, freeing");
                    }

                    // A walk-in that never reached its mark holds the state and swallows input; it
                    // has to be called off before a state means anything.
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

    // The state of the thing the player is meant to be driving, for when it is not driving.
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

        // Where the camera is actually looking, which is a different question from where the player
        // is standing and the one that decides whether anybody can see it.
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

        // The skeleton as well as the object: "the player is gone" has meant a live object with a
        // switched-off skeleton at least as often as a destroyed one.
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

    // Every tool builds into GenerateRoom.CustomTransform (SceneRefs.ContentRoot). A generated
    // dungeon room is handed one; the town room is hand-authored and has none, which is why the
    // shape tool - and everything else - had nothing to build into.
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

    // The tail of an arrival: colliders back on, the conversation lock the trip took released, the
    // camera on the player and the input maps rebound - without that last pair only movement
    // survives the trip. It deliberately does NOT set the player's state: the walk-in ends itself
    // (GoToAndStop with IdleOnEnd), and forcing a state on top of a running animation is a fight
    // the animation wins, one frame at a time.
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

                // The camera is still where the base's arrival left it, watching a room that has
                // been switched off - which looks exactly like a hub with no player in it, however
                // healthy the player is. Cleared, re-aimed and snapped over in one go.
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

        // Whatever the trip left holding the clock. The editor sets its own pause after this.
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

    // How long the player must stay controllable before the arrival counts as over.
    private const float ArrivalSettleSeconds = 1.25f;

    // Everything the game does on its own way in: the player exists, is awake, is not mid-anything,
    // and no transition is still covering the screen.
    private static bool ArrivalFinished()
    {
        if (BiomeBaseManager.Instance == null) return false;

        var player = PlayerFarming.Instance;
        if (player == null || !player.gameObject.activeInHierarchy) return false;

        if (MMTransition.IsPlaying) return false;

        return player.state == null || !Stuck(player.state.CURRENT_STATE);
    }

    // Where DLCShrineRoomLocationManager would put an arriving player, read while the transform it
    // comes from is still standing.
    private static Vector3? _arrivalStart;

    private static void CaptureArrivalStart()
    {
        _arrivalStart = null;

        var manager = DLCShrineRoomLocationManager.Instance;
        if (manager == null) return;

        // EntranceFromBase is where HubLocationManager.GetStartPosition sends anyone arriving from
        // the base - the town's door, and inside the room. DoorStartPosition is a near neighbour of
        // it and does just as well when the entrance is missing.
        if (manager.EntranceFromBase != null) _arrivalStart = manager.EntranceFromBase.position;
        else if (manager.DoorStartPosition != null) _arrivalStart = manager.DoorStartPosition.position;
    }

    // Where the hub says the player arrives: the position of a trigger carrying the Hub spawn point
    // action. A hub cannot be saved without one, so a played hub always has it; an authoring session
    // starts with an empty room and has none until the author places it.
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

    // How far back the walk-in starts, and how long it may take. Vanilla's town arrival walks eight
    // units from the door; a hub's floor is only as big as the author drew it, so this is shorter.
    // It always ends: GoToAndStop gives up on its own after maxDuration or a second of standing
    // still, and forcePositionOnTimeout leaves the player on the mark either way - which is what the
    // first attempt at a walk-in was missing, and why it left a player who could not move.
    private const float WalkInDistance = 3.5f;
    private const float WalkInSeconds = 3f;

    // The arrival, played on the hub's own spawn point.
    //
    // Everything before this ran under the curtain: the base's arrival, the sweep, the rebuild. The
    // player is put down a few units short of the mark while the screen is still black - so the cut
    // nobody should see happens where nobody can see it - the screen comes back, and then the lamb
    // walks in. It is the town's own arrival (GoToAndStop, ending idle), aimed at where the hub says
    // to aim it.
    private static IEnumerator ArriveAtSpawn()
    {
        var player = PlayerFarming.Instance;
        if (player == null)
        {
            HubCurtain.Lower();
            yield break;
        }

        // Any walk-in the base started is called off first; it would drag the player back out.
        foreach (var each in PlayerFarming.players)
            if (each != null && each.GoToAndStopping) each.AbortGoTo(InvokeAbortCallback: false);

        var authored = SpawnPoint();
        var mark = authored ?? ArrivalStart();
        var from = mark - new Vector3(0f, WalkInDistance, 0f);

        // The walk plays only when the navigation graph actually covers both ends of it. GoToAndStop
        // paths through A*, and a graph that has not caught up with the hub's floor gives it a path
        // starting somewhere else entirely - the walk heads the wrong way, vanilla's own bail-outs
        // (timeout, or one second of no progress) fire, and forcePositionOnTimeout snaps the player
        // across the room. That snap is the "teleported mid-walk" report. No ground, no walk.
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

        // The walk crosses - and ends on - authored trigger volumes. An entry mid-walk is the
        // session carrying the player, not the player walking in, and a control-locking sequence
        // fired then wedges itself into the walk's own end. Muted until the player has landed; a
        // sequence on the spawn trigger itself fires the moment the mute lifts.
        Tools.CTMapTrigger.MuteFiring(WalkInSeconds + 2f);

        yield return HubCurtain.LowerAndWait();

        if (!walk) yield break;

        player.GoToAndStop(mark, null, IdleOnEnd: true, DisableCollider: false, GoToCallback: null,
            maxDuration: WalkInSeconds, forcePositionOnTimeout: true);

        // Its own timeout is the one that matters; this only stops the wait from outliving it.
        var deadline = Time.realtimeSinceStartup + WalkInSeconds + 1.5f;
        while (player.GoToAndStopping && Time.realtimeSinceStartup < deadline) yield return null;

        if (player.GoToAndStopping)
        {
            Plugin.Log.LogWarning("Hub: the arrival walk never finished; putting the player on the mark.");
            player.AbortGoTo(InvokeAbortCallback: false);
        }

        // Wherever the walk actually ended, the arrival ends on the mark. Vanilla's bail-outs are
        // supposed to guarantee this; the belt goes with the braces.
        if (Vector2.Distance(player.transform.position, mark) > 2f)
        {
            Plugin.Log.LogWarning($"Hub: the arrival walk ended at {player.transform.position} " +
                                  $"instead of {mark}; snapping to the mark.");
            player.transform.position = mark;
        }
    }

    // Whether the navigation graph has walkable ground at (well, within a stride of) a point.
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
            // Mid-scan the graph answers with exceptions; that is "no" with extra steps.
            return false;
        }
    }

    // The town's door if it is still standing, else where it stood, else the middle of the room -
    // and never somewhere outside the room, which is what "no player" turned out to mean.
    private static Vector3 ArrivalStart()
    {
        var manager = DLCShrineRoomLocationManager.Instance;
        var start = _arrivalStart;

        if (manager != null && manager.EntranceFromBase != null)
            start = manager.EntranceFromBase.position;

        var centre = RoomCentre();
        if (!start.HasValue) return centre;

        // A door that is nowhere near the room is a door that did not survive the sweep intact.
        return Vector2.Distance(start.Value, centre) > 80f ? centre : start.Value;
    }

    // Puts the lamb back on screen and out of the base's arrival: the skeleton object switched on,
    // the state out of CustomAnimation, and the animator back on its idle so the pose it was left
    // in is not the one it keeps.
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

            // Only a nudge onto nearby ground. The graph still holds the base's own walkable area,
            // and the nearest node to the town room was one of those - so the snap was carrying the
            // player out of the room and across the map rather than settling it on the floor.
            if (nearest.node != null && nearest.node.Walkable &&
                Vector2.Distance((Vector3)nearest.position, centre) <= 12f)
                centre = (Vector3)nearest.position;
        }

        centre.z = 0f;
        return centre;
    }

    // The middle of the room, snapped to ground the player can actually stand on - the sweep and
    // the blueprint have just rebuilt the collision under it.
    private static void AdoptPlayers()
    {
        var room = SceneRefs.Room;
        if (room == null || PlayerFarming.Instance == null) return;

        // The base's units hang off the base room, and raising the town room switches that room -
        // and therefore the player - off: the character vanishes mid-animation and stops taking
        // input, because an object under an inactive parent is not active however often it is told
        // to be. Vanilla's own Woolhaven unit layer is inside the room that stays on.
        var host = DLCShrineRoomLocationManager.Instance != null &&
                   DLCShrineRoomLocationManager.Instance.UnitLayer != null
            ? DLCShrineRoomLocationManager.Instance.UnitLayer
            : room.transform;

        // Where they stand is the arrival's business, not this one's.
        foreach (var player in PlayerFarming.players)
        {
            if (player == null) continue;
            if (!player.gameObject.activeSelf) player.gameObject.SetActive(true);
            if (player.transform.parent != host) player.transform.SetParent(host, worldPositionStays: true);
        }
    }

    // The town bakes its buildings into one composite collider (DLCShrineRoomLocationManager's
    // sceneryCollider) and the room into another. The sweep destroys what those were baked from,
    // but a composite keeps the geometry it last generated - so the emptied town was still solid
    // where its houses used to be, which is what shoved the player out of a hub's own floor.
    private static void ResetTownCollision()
    {
        var manager = DLCShrineRoomLocationManager.Instance;
        if (manager != null && manager.sceneryCollider != null)
        {
            try
            {
                // Nothing is left to collide with; a hub's own shapes carry its collision.
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
                // The town has no room composite of its own - its buildings each carry a baked
                // collider - so one is built here. Everything the editor lays down joins it, which
                // is what makes a shape a floor with walls round it instead of a solid block that
                // shoves the player out of the hub.
                var composite = SceneRefs.EnsureRoomComposite();

                // Outlines, as a dungeon room uses.
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
            if (!CTLevelSerialization.Available(candidate)) return candidate;
        }
        return stem;
    }
}
