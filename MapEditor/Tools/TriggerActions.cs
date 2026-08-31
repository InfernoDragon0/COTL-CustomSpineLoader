using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.MapEditor.Npc;
using MMTools;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public enum TriggerActionType
{
    MovePlayersToTrigger,
    MovePlayersToObject,
    StartConversation,
    PlayPlayerAnimation,
    PlayObjectAnimation,

    ApplyLighting,

    ChangeMusic,

    Wait,

    CameraOffset,
    CameraOffsetReset,

    CameraZoom,
    CameraZoomReset,

    CameraLookAtObject,
    CameraLookAtTrigger,

    CameraEffect,

    CameraShake,

    PlayCutscene,

    ShowCaption,
    ShowFullscreenText,
    ShowTitleText,

    OpenWorldMap,

    ReturnToBase,

    HubSpawnPoint
}

public class TriggerAction
{
    public TriggerActionType Type;

    public string Target = "";

    public Vector3 Position;

    public float Spread = 1.3f;
    public bool Loop;
    public float Duration;

    public float Amount;

    public string Subtext = "";

    public bool FreezeAtEnd;

    public bool NeedsPlayerInput => Type == TriggerActionType.StartConversation;

    public const float DefaultLightingFade = 1.5f;

    public float LightingFade => Duration < 0f ? 0f : Duration > 0f ? Duration : DefaultLightingFade;

    public string Describe() => Type switch
    {
        TriggerActionType.MovePlayersToTrigger => $"Move to trigger {Target}",
        TriggerActionType.MovePlayersToObject => $"Move to {ShortName(Target)}",
        TriggerActionType.StartConversation => $"Talk to {ShortName(Target)}",
        TriggerActionType.PlayPlayerAnimation =>
            $"Play '{Target}'" + (Loop ? $" (loop {Duration:0.#}s)" : ""),
        TriggerActionType.PlayObjectAnimation =>
            $"{ShortName(Target)} plays '{Subtext}'" + (Loop ? $" (loop {Duration:0.#}s)" : "") +
            (FreezeAtEnd ? ", holds" : ""),
        TriggerActionType.ApplyLighting =>
            (string.IsNullOrEmpty(Target) ? "Lighting: vanilla" : $"Lighting: {Target}") +
            (LightingFade > 0f ? $" ({LightingFade:0.#}s)" : " (instant)"),
        TriggerActionType.ChangeMusic => $"Music: {MusicTool.ShortName(Target)}",
        TriggerActionType.Wait => $"Wait {Duration:0.#}s",
        TriggerActionType.CameraOffset =>
            $"Camera offset ({Position.x:0.#}, {Position.y:0.#})",
        TriggerActionType.CameraOffsetReset => "Camera offset: reset",
        TriggerActionType.CameraZoom => $"Camera zoom {Amount:0.#}",
        TriggerActionType.CameraZoomReset => "Camera zoom: reset",
        TriggerActionType.CameraLookAtObject =>
            $"Camera looks at {ShortName(Target)} ({Duration:0.#}s)",
        TriggerActionType.CameraLookAtTrigger =>
            $"Camera looks at trigger {Target} ({Duration:0.#}s)",
        TriggerActionType.CameraEffect => $"Effect: {Target} ({Duration:0.#}s)",
        TriggerActionType.CameraShake => $"Shake: {Target} ({Duration:0.#}s)",
        TriggerActionType.PlayCutscene =>
            $"Cutscene: {Target}" + (Loop ? " (skippable)" : ""),
        TriggerActionType.ShowCaption => $"Caption: {Quote(Target)}{SubtextNote()}",
        TriggerActionType.ShowFullscreenText => $"Fullscreen: {Quote(Target)}{SubtextNote()}",
        TriggerActionType.ShowTitleText => $"Title: {Quote(Target)}{SubtextNote()}",
        TriggerActionType.OpenWorldMap => $"World map: {Target}",
        TriggerActionType.ReturnToBase => "Return to base",
        TriggerActionType.HubSpawnPoint => "Hub spawn point",
        _ => Type.ToString()
    };

    private string SubtextNote() => string.IsNullOrEmpty(Subtext) ? "" : " + subtext";

    private static string Quote(string text)
    {
        if (string.IsNullOrEmpty(text)) return "\"\"";
        return text.Length <= 22 ? $"\"{text}\"" : $"\"{text.Substring(0, 21)}...\"";
    }

    private static string ShortName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        var slash = path.LastIndexOf('/');
        return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
    }
}

public static class TriggerActions
{
    public static IEnumerator Run(CTMapTrigger trigger)
    {
        if (trigger == null || trigger.Actions.Count == 0) yield break;

        var locked = false;

        var actions = new List<TriggerAction>(trigger.Actions);
        var lockControl = trigger.LockPlayerControl;

        foreach (var action in actions)
        {
            if (action == null) continue;

            if (lockControl)
            {
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

            case TriggerActionType.PlayObjectAnimation:
                yield return AnimateObject(action.Target, action.Subtext, action.Loop, action.Duration,
                    action.FreezeAtEnd);
                break;

            case TriggerActionType.ApplyLighting:
                ApplyLighting(action.Target, action.LightingFade);
                break;

            case TriggerActionType.ChangeMusic:
                ChangeMusic(action.Target);
                break;

            case TriggerActionType.Wait:
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, action.Duration));
                break;

            case TriggerActionType.CameraOffset:
                TriggerCameraActions.SetOffset(action.Position);
                break;

            case TriggerActionType.CameraOffsetReset:
                TriggerCameraActions.ResetOffset();
                break;

            case TriggerActionType.CameraZoom:
                TriggerCameraActions.SetZoom(action.Amount);
                break;

            case TriggerActionType.CameraZoomReset:
                TriggerCameraActions.ResetZoom();
                break;

            case TriggerActionType.CameraLookAtObject:
            {
                var go = ResolveObject(action.Target);
                yield return TriggerCameraActions.LookAt(go, action.Position, action.Duration);
                break;
            }

            case TriggerActionType.CameraLookAtTrigger:
            {
                var target = FindTrigger(action.Target);
                if (target == null)
                {
                    Plugin.Log.LogWarning($"MapEditor: camera action targets missing trigger '{action.Target}'.");
                    break;
                }

                yield return TriggerCameraActions.LookAt(null, target.transform.position, action.Duration);
                break;
            }

            case TriggerActionType.CameraEffect:
                yield return TriggerCameraActions.PlayEffect(action.Target, action.Duration);
                break;

            case TriggerActionType.CameraShake:
                yield return TriggerCameraActions.Shake(action.Amount, action.Duration);
                break;

            case TriggerActionType.PlayCutscene:
                yield return TriggerCameraActions.PlayCutscene(action.Target, action.Loop);
                break;

            case TriggerActionType.ShowCaption:
                TriggerScreenText.Show(TriggerScreenText.Mode.Caption, action.Target,
                    action.Subtext, action.Duration);
                break;

            case TriggerActionType.ShowFullscreenText:
                TriggerScreenText.Show(TriggerScreenText.Mode.Fullscreen, action.Target,
                    action.Subtext, action.Duration);
                break;

            case TriggerActionType.ShowTitleText:
                TriggerScreenText.Show(TriggerScreenText.Mode.Title, action.Target,
                    action.Subtext, action.Duration);
                break;

            case TriggerActionType.OpenWorldMap:
            {
                var screen = WorldMap.WorldMapScreen.Instance;
                if (screen == null || !CTWorldMapSerialization.Available(action.Target))
                {
                    Plugin.Log.LogWarning($"MapEditor: trigger action targets world map " +
                                          $"'{action.Target}', which is not saved here.");
                    break;
                }

                screen.Open(action.Target);

                while (WorldMap.WorldMapScreen.IsOpen)
                    yield return new WaitForSecondsRealtime(0.1f);
                break;
            }

            case TriggerActionType.HubSpawnPoint:
                break;

            case TriggerActionType.ReturnToBase:
                LevelPlayback.Stop();
                DungeonMapPlayback.Clear();
                GameManager.ToShip();

                yield break;
        }
    }

    // ---- music ------------------------------------------------------------------------------

    private static void ChangeMusic(string eventPath)
    {
        if (string.IsNullOrEmpty(eventPath)) return;

        try
        {
            AudioManager.Instance?.PlayMusic(eventPath);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: trigger music '{eventPath}' failed to play: {e.Message}");
            return;
        }

        var host = RuntimeMapEditor.Active;
        if (host != null) host.SetMusicLoop(eventPath);
        else Plugin.Log.LogWarning("MapEditor: no editor host to keep the trigger's music looping; " +
                                   "it will play once.");
    }

    // ---- lighting ---------------------------------------------------------------------------

    private static void ApplyLighting(string profileName, float fadeSeconds)
    {
        if (string.IsNullOrEmpty(profileName))
        {
            LightingTool.ClearOverride(fadeSeconds);
            return;
        }

        var profile = LightingProfiles.Find(profileName);
        if (profile == null)
        {
            Plugin.Log.LogWarning($"MapEditor: trigger action wants lighting profile " +
                                  $"'{profileName}', which is not saved on this machine.");
            return;
        }

        LightingTool.Apply(profile.Data, fadeSeconds);
    }

    // ---- players ---------------------------------------------------------------------------

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

    public static void SetControl(bool enabled)
    {
        foreach (var player in LivePlayers())
        {
            if (player.state == null) continue;

            if (enabled)
            {
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

    private static IEnumerator MovePlayers(Vector3 centre, float spread, bool keepLocked)
    {
        var players = LivePlayers();
        if (players.Count == 0) yield break;

        for (var i = 0; i < players.Count; i++)
        {
            var target = centre;
            if (players.Count > 1)
            {
                var angle = Mathf.PI * 0.5f + i * (Mathf.PI * 2f / players.Count);
                target += new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Mathf.Max(0.1f, spread);
            }

            var player = players[i];
            if (player.GoToAndStopping) player.AbortGoTo(InvokeAbortCallback: false);

            player.GoToAndStop(target, null, IdleOnEnd: !keepLocked, DisableCollider: false,
                GoToCallback: null, maxDuration: 8f, forcePositionOnTimeout: true,
                AbortGoToCallback: null, groupAction: false);
        }

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

    // ---- animating something in the room --------------------------------------------------------

    public static Spine.SkeletonData SkeletonDataOf(GameObject go)
    {
        if (go == null) return null;

        var animation = go.GetComponentInChildren<SkeletonAnimation>(true);
        if (animation != null)
        {
            var data = animation.Skeleton?.Data;
            if (data != null) return data;
            if (animation.skeletonDataAsset != null) return animation.skeletonDataAsset.GetSkeletonData(true);
        }

        var graphic = go.GetComponentInChildren<SkeletonGraphic>(true);
        if (graphic == null) return null;

        var graphicData = graphic.Skeleton?.Data;
        if (graphicData != null) return graphicData;

        return graphic.skeletonDataAsset != null ? graphic.skeletonDataAsset.GetSkeletonData(true) : null;
    }

    public static bool HasSpine(GameObject go) => SkeletonDataOf(go) != null;

    private static string Leaf(string path)
    {
        if (string.IsNullOrEmpty(path)) return "the object";
        var cut = path.LastIndexOf('/');
        return cut >= 0 && cut < path.Length - 1 ? path.Substring(cut + 1) : path;
    }

    public static List<string> ObjectAnimationNames(GameObject go)
    {
        var names = new List<string>();

        var data = SkeletonDataOf(go);
        if (data?.Animations == null) return names;

        foreach (var animation in data.Animations)
            if (animation != null && !string.IsNullOrEmpty(animation.Name)) names.Add(animation.Name);

        names.Sort(System.StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static Spine.AnimationState AnimationStateOf(GameObject go)
    {
        if (go == null) return null;

        var animation = go.GetComponentInChildren<SkeletonAnimation>(true);
        if (animation != null) return animation.AnimationState;

        var graphic = go.GetComponentInChildren<SkeletonGraphic>(true);
        return graphic != null ? graphic.AnimationState : null;
    }

    private static IEnumerator AnimateObject(string path, string animation, bool loop, float duration,
        bool freezeAtEnd)
    {
        if (string.IsNullOrEmpty(animation)) yield break;

        var go = ResolveObject(path);
        if (go == null)
        {
            Plugin.Log.LogWarning($"MapEditor: animation action targets '{path}', which is not in this room.");
            yield break;
        }

        var state = AnimationStateOf(go);
        var data = SkeletonDataOf(go);
        if (state == null || data == null)
        {
            Plugin.Log.LogWarning($"MapEditor: '{Leaf(path)}' has no spine to animate.");
            yield break;
        }

        var wanted = data.FindAnimation(animation);
        if (wanted == null)
        {
            Plugin.Log.LogWarning($"MapEditor: '{Leaf(path)}' has no animation '{animation}'.");
            yield break;
        }

        string was = null;
        var wasLooping = true;
        try
        {
            var current = state.GetCurrent(0);
            if (current?.Animation != null)
            {
                was = current.Animation.Name;
                wasLooping = current.Loop;
            }

            state.SetAnimation(0, animation, loop);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: '{animation}' would not play on {Leaf(path)}: {e.Message}");
            yield break;
        }

        var length = duration > 0f ? duration : wanted.Duration > 0f ? wanted.Duration : 1f;
        yield return new WaitForSeconds(length);

        if (freezeAtEnd || was == null || go == null) yield break;

        try
        {
            state.SetAnimation(0, was, wasLooping);
        }
        catch (System.Exception)
        {
        }
    }

    // ---- conversation -------------------------------------------------------------------------

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

        var guard = Time.unscaledTime + 2f;
        while (!NpcDialogueRunner.IsRunning && !MMConversation.isPlaying && Time.unscaledTime < guard)
            yield return null;

        var quietSince = -1f;
        while (NpcDialogueRunner.IsRunning || MMConversation.isPlaying)
        {
            if (MMConversation.isPlaying)
            {
                quietSince = -1f;
            }
            else if (quietSince < 0f)
            {
                quietSince = Time.unscaledTime;
            }
            else if (Time.unscaledTime - quietSince > 10f)
            {
                Plugin.Log.LogWarning("MapEditor: conversation appears to have been interrupted; " +
                                      "continuing the trigger sequence.");
                break;
            }

            yield return null;
        }

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
                Duration = entry.Duration,
                Amount = entry.Amount,
                Subtext = entry.Subtext ?? "",
                FreezeAtEnd = entry.FreezeAtEnd
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
                Duration = action.Duration,
                Amount = action.Amount,
                Subtext = action.Subtext ?? "",
                FreezeAtEnd = action.FreezeAtEnd
            });
        }

        return result;
    }
}
