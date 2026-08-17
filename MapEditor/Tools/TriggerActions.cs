using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.MapEditor.Npc;
using MMTools;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public enum TriggerActionType
{
    MovePlayersToTrigger,
    MovePlayersToObject,
    StartConversation,
    PlayPlayerAnimation
}

// One step of a trigger's sequence. The runtime twin of MapTriggerActionData - the tool edits
// these, the blueprint stores the other.
public class TriggerAction
{
    public TriggerActionType Type;

    // Trigger id / object path / NPC internal name / animation name.
    public string Target = "";

    // Authored position of the target object, used when the object itself cannot be found.
    public Vector3 Position;

    public float Spread = 1.3f;
    public bool Loop;
    public float Duration;

    // A conversation hands input back to the player (the wheel needs a button press), so the
    // control lock is lifted around it. Everything else runs on rails.
    public bool NeedsPlayerInput => Type == TriggerActionType.StartConversation;

    public string Describe() => Type switch
    {
        TriggerActionType.MovePlayersToTrigger => $"Move to trigger {Target}",
        TriggerActionType.MovePlayersToObject => $"Move to {ShortName(Target)}",
        TriggerActionType.StartConversation => $"Talk to {ShortName(Target)}",
        TriggerActionType.PlayPlayerAnimation =>
            $"Play '{Target}'" + (Loop ? $" (loop {Duration:0.#}s)" : ""),
        _ => Type.ToString()
    };

    private static string ShortName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        var slash = path.LastIndexOf('/');
        return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
    }
}

// Runs a trigger's action list.
//
// Everything here drives the game's OWN player API rather than moving transforms directly:
// GoToAndStop pathfinds and animates the walk, CustomAnimation owns the spine track and the state
// it belongs to, and InActive is the state the game itself parks the player in during a cutscene
// (PlayerFarming.Update returns early on it, so no input is read). Teleporting a transform and
// poking the skeleton would have fought all three.
//
// Coop is a first-class case: every routine works over the whole player list, never
// PlayerFarming.Instance alone.
public static class TriggerActions
{
    public static IEnumerator Run(CTMapTrigger trigger)
    {
        if (trigger == null || trigger.Actions.Count == 0) yield break;

        var locked = false;

        // Copied, because an action can outlive the trigger (a conversation runs for as long as
        // the player reads it) and the list must not change underneath the loop.
        var actions = new List<TriggerAction>(trigger.Actions);
        var lockControl = trigger.LockPlayerControl;

        foreach (var action in actions)
        {
            if (action == null) continue;

            if (lockControl)
            {
                // Handed back for the action that needs it, taken again for the next one that
                // does not - so a sequence can be "walk over, talk, walk away" without the player
                // wandering off mid-scene or being stuck unable to answer.
                if (action.NeedsPlayerInput && locked)
                {
                    SetControl(true);
                    locked = false;
                }
                else if (!action.NeedsPlayerInput && !locked)
                {
                    SetControl(false);
                    locked = true;
                }
            }

            yield return Execute(action, lockControl && !action.NeedsPlayerInput);
        }

        if (locked) SetControl(true);
    }

    private static IEnumerator Execute(TriggerAction action, bool keepLocked)
    {
        switch (action.Type)
        {
            case TriggerActionType.MovePlayersToTrigger:
            {
                var target = FindTrigger(action.Target);
                if (target == null)
                {
                    Plugin.Log.LogWarning($"MapEditor: trigger action targets missing trigger '{action.Target}'.");
                    yield break;
                }

                yield return MovePlayers(target.transform.position, action.Spread, keepLocked);
                break;
            }

            case TriggerActionType.MovePlayersToObject:
            {
                var go = ResolveObject(action.Target);
                var position = go != null ? go.transform.position : action.Position;
                yield return MovePlayers(position, action.Spread, keepLocked);
                break;
            }

            case TriggerActionType.StartConversation:
                yield return Converse(action.Target);
                break;

            case TriggerActionType.PlayPlayerAnimation:
                yield return Animate(action.Target, action.Loop, action.Duration, keepLocked);
                break;
        }
    }

    // ---- players ---------------------------------------------------------------------------

    // PlayerFarming.players is only filled when coop features are enabled, so solo play has an
    // empty list and lives entirely in Instance. Both are read, and Instance is added only when
    // the list does not already hold it.
    public static List<PlayerFarming> LivePlayers()
    {
        var result = new List<PlayerFarming>(2);

        var players = PlayerFarming.players;
        if (players != null)
            foreach (var player in players)
                if (player != null && player.gameObject.activeInHierarchy) result.Add(player);

        var instance = PlayerFarming.Instance;
        if (instance != null && instance.gameObject.activeInHierarchy && !result.Contains(instance))
            result.Add(instance);

        return result;
    }

    // InActive is the game's own cutscene parking state: PlayerFarming.Update bails on it before
    // reading a single input. SetInactive handles the case where the player is mid-walk, which
    // assigning the state directly would strand.
    public static void SetControl(bool enabled)
    {
        foreach (var player in LivePlayers())
        {
            if (player.state == null) continue;

            if (enabled)
            {
                // Only states this system puts them in are cleared - a player who died or
                // started a conversation of their own mid-sequence keeps that state.
                if (player.state.CURRENT_STATE is StateMachine.State.InActive
                    or StateMachine.State.CustomAnimation)
                    player.state.CURRENT_STATE = StateMachine.State.Idle;
            }
            else if (player.GoToAndStopping)
            {
                player.SetInactive();
            }
            else
            {
                player.state.CURRENT_STATE = StateMachine.State.InActive;
            }
        }
    }

    // ---- move ------------------------------------------------------------------------------

    // One player lands on the spot; several settle evenly around it on a ring, so a coop pair
    // never ends up standing inside each other (and the game's own group offset - a flat 1 unit
    // below the leader - does not apply, which is why groupAction is left off).
    private static IEnumerator MovePlayers(Vector3 centre, float spread, bool keepLocked)
    {
        var players = LivePlayers();
        if (players.Count == 0) yield break;

        for (var i = 0; i < players.Count; i++)
        {
            var target = centre;
            if (players.Count > 1)
            {
                // Starting at the top of the circle and going round: with two players that is
                // above and below the point, which reads as "either side" in this camera.
                var angle = Mathf.PI * 0.5f + i * (Mathf.PI * 2f / players.Count);
                target += new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Mathf.Max(0.1f, spread);
            }

            var player = players[i];
            if (player.GoToAndStopping) player.AbortGoTo(InvokeAbortCallback: false);

            // IdleOnEnd decides the state the player is left in on arrival: Idle hands control
            // back, InActive keeps the lock for the rest of the sequence.
            // forcePositionOnTimeout, because a blocked path must not stall the whole sequence -
            // the game snaps the player to the spot after maxDuration and carries on.
            player.GoToAndStop(target, null, IdleOnEnd: !keepLocked, DisableCollider: false,
                GoToCallback: null, maxDuration: 8f, forcePositionOnTimeout: true,
                AbortGoToCallback: null, groupAction: false);
        }

        // The game's own timeout plus a margin; a player destroyed mid-walk (a room reload)
        // simply stops being counted.
        var deadline = Time.time + 10f;
        while (Time.time < deadline)
        {
            var walking = false;
            foreach (var player in players)
                if (player != null && player.GoToAndStopping) walking = true;

            if (!walking) break;
            yield return null;
        }
    }

    // ---- animation --------------------------------------------------------------------------

    // Both players perform it, offset by a small random delay: two lambs hitting the same frame
    // of the same animation looks like one puppet mirrored, and the game's own crowd scenes
    // stagger for the same reason.
    private static IEnumerator Animate(string animation, bool loop, float duration, bool keepLocked)
    {
        if (string.IsNullOrEmpty(animation)) yield break;

        var players = LivePlayers();
        if (players.Count == 0) yield break;

        var host = RuntimeMapEditor.Active;
        var longestDelay = 0f;

        for (var i = 0; i < players.Count; i++)
        {
            var delay = i == 0 ? 0f : Random.Range(0.08f, 0.28f);
            longestDelay = Mathf.Max(longestDelay, delay);

            if (host != null) host.StartCoroutine(AnimateOne(players[i], animation, loop, delay));
            else players[i].CustomAnimation(animation, loop);
        }

        var length = duration > 0f ? duration : AnimationLength(players[0], animation);
        yield return new WaitForSeconds(longestDelay + length);

        // A looping animation never ends on its own, and CustomAnimation leaves the player in the
        // CustomAnimation state either way - which is itself a control lock. Cleared here unless
        // the sequence is about to re-lock anyway.
        foreach (var player in players)
        {
            if (player == null || player.state == null) continue;
            if (player.state.CURRENT_STATE != StateMachine.State.CustomAnimation) continue;
            player.state.CURRENT_STATE = keepLocked ? StateMachine.State.InActive : StateMachine.State.Idle;
        }
    }

    private static IEnumerator AnimateOne(PlayerFarming player, string animation, bool loop, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (player == null || !player.gameObject.activeInHierarchy) yield break;

        player.CustomAnimation(animation, loop);
    }

    // The skeleton knows how long its own animation is; 1 second is the fallback for a name the
    // skeleton does not have (the game plays nothing in that case, so the wait is only a beat).
    private static float AnimationLength(PlayerFarming player, string animation)
    {
        var found = FindPlayerAnimation(player, animation);
        return found != null && found.Duration > 0f ? found.Duration : 1f;
    }

    private static Spine.Animation FindPlayerAnimation(PlayerFarming player, string animation)
    {
        var data = player != null && player.Spine != null ? player.Spine.skeleton?.Data : null;
        return data?.FindAnimation(animation);
    }

    // Every animation on the player's own skeleton, for the tool's picker - authoring by typing
    // names into a text box got them wrong, and a wrong name plays nothing at all.
    public static List<string> PlayerAnimationNames()
    {
        var names = new List<string>();

        var player = PlayerFarming.Instance;
        if (player == null)
        {
            var players = LivePlayers();
            if (players.Count > 0) player = players[0];
        }

        var data = player != null && player.Spine != null ? player.Spine.skeleton?.Data : null;
        if (data?.Animations == null) return names;

        foreach (var animation in data.Animations)
            if (animation != null && !string.IsNullOrEmpty(animation.Name)) names.Add(animation.Name);

        names.Sort(System.StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // ---- conversation -------------------------------------------------------------------------

    // Custom NPCs only: they are the ones that carry a dialogue tree this mod can start. A vanilla
    // NPC's conversation lives inside its own Interaction and expects the player to have pressed
    // the button themselves.
    private static IEnumerator Converse(string internalName)
    {
        if (string.IsNullOrEmpty(internalName)) yield break;

        GameObject speaker = null;
        CustomNpc definition = null;

        foreach (var behaviour in Object.FindObjectsOfType<CustomNpcBehaviour>())
        {
            if (behaviour == null || behaviour.Definition == null) continue;
            if (behaviour.Definition.InternalName != internalName) continue;

            speaker = behaviour.gameObject;
            definition = behaviour.Definition;
            break;
        }

        if (speaker == null)
        {
            Plugin.Log.LogWarning($"MapEditor: trigger action wants a conversation with " +
                                  $"'{internalName}', which is not in this room.");
            yield break;
        }

        NpcDialogueRunner.Play(definition, speaker);

        // Play is asynchronous by a frame or two (and refuses outright if another conversation
        // owns the screen), so the wait is guarded rather than assuming it started.
        var guard = Time.unscaledTime + 2f;
        while (!MMConversation.isPlaying && Time.unscaledTime < guard) yield return null;
        while (MMConversation.isPlaying) yield return null;

        // The teardown the runner does on the last node lands a frame later.
        yield return null;
    }

    // ---- lookup ---------------------------------------------------------------------------------

    public static CTMapTrigger FindTrigger(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var trigger in CTMapTrigger.All)
            if (trigger != null && trigger.Id == id) return trigger;
        return null;
    }

    // Objects are addressed by scene path, which survives a save/load as long as the room is the
    // same one. When it is not, the caller falls back to the position captured at authoring time.
    public static string PathOf(GameObject go)
    {
        if (go == null) return "";

        var path = go.name;
        var parent = go.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    public static GameObject ResolveObject(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var direct = GameObject.Find(path);
        if (direct != null) return direct;

        // The room rebuilds its hierarchy on load, so an exact path is not guaranteed; the leaf
        // name is the part an author would recognise, and matching on it is better than giving up
        // and using a stale position.
        var slash = path.LastIndexOf('/');
        var leaf = slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;

        foreach (var transform in Object.FindObjectsOfType<Transform>())
            if (transform.name == leaf) return transform.gameObject;

        return null;
    }

    // ---- serialization ---------------------------------------------------------------------------

    public static List<TriggerAction> FromData(List<MapTriggerActionData> data, string triggerId)
    {
        var result = new List<TriggerAction>();
        if (data == null) return result;

        foreach (var entry in data)
        {
            if (entry == null) continue;

            if (!System.Enum.TryParse<TriggerActionType>(entry.Type, out var type))
            {
                Plugin.Log.LogWarning($"MapEditor: trigger '{triggerId}' has an unknown action " +
                                      $"type '{entry.Type}'; it was dropped.");
                continue;
            }

            result.Add(new TriggerAction
            {
                Type = type,
                Target = entry.Target ?? "",
                Position = MapEditorSerialization.ToVector3(entry.Position),
                Spread = entry.Spread > 0f ? entry.Spread : 1.3f,
                Loop = entry.Loop,
                Duration = entry.Duration
            });
        }

        return result;
    }

    public static List<MapTriggerActionData> ToData(List<TriggerAction> actions)
    {
        var result = new List<MapTriggerActionData>();
        if (actions == null) return result;

        foreach (var action in actions)
        {
            if (action == null) continue;
            result.Add(new MapTriggerActionData
            {
                Type = action.Type.ToString(),
                Target = action.Target ?? "",
                Position = MapEditorSerialization.V3(action.Position),
                Spread = action.Spread,
                Loop = action.Loop,
                Duration = action.Duration
            });
        }

        return result;
    }
}
