using System.Collections;
using System.Collections.Generic;
using Lamb.UI;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// The camera and on-screen text a trigger sequence can drive, all of it the game's own rig:
//
//  - offset and zoom are CameraFollowTarget's TargetOffset and targetDistance, the same fields
//    the game's own cutscenes push around;
//  - looking at something swaps the rig's follow targets, so the move is the camera's ordinary
//    smoothed follow rather than a teleport, and the players keep playing underneath;
//  - the effects are BiomeConstants' post-processing tweens (the ones the bosses and the winter
//    events use) plus CameraManager's shake and the cinematic letterbox;
//  - screen text has moved out to TriggerScreenText, which draws its own canvas.
//
// Offset and zoom are global state on a rig that outlives the room, exactly like the lighting
// override, so ResetAll() is called from the same place lighting is cleared.
public static class TriggerCameraActions
{
    // ---- offset -----------------------------------------------------------------------------

    // Relative to whatever the camera is following - the player, normally. The rig lerps
    // CurrentOffset towards this, so the shift is a glide rather than a cut.
    public static void SetOffset(Vector3 offset)
    {
        var rig = global::CameraFollowTarget.Instance;
        if (rig == null)
        {
            Plugin.Log.LogWarning("MapEditor: no camera rig in this scene; the offset was ignored.");
            return;
        }

        rig.SetOffset(offset);
    }

    public static void ResetOffset() => SetOffset(Vector3.zero);

    // ---- zoom -------------------------------------------------------------------------------

    // What the rig was set to before a trigger first touched it. Captured lazily rather than at
    // scene load: the rig's own value is only settled once the room has generated.
    private static float _restingZoom = -1f;

    public static void SetZoom(float zoom)
    {
        var rig = global::CameraFollowTarget.Instance;
        if (rig == null)
        {
            Plugin.Log.LogWarning("MapEditor: no camera rig in this scene; the zoom was ignored.");
            return;
        }

        if (_restingZoom < 0f) _restingZoom = rig.targetDistance;

        // Smaller is closer. The rig smooth-damps `distance` towards this every frame, so the
        // sequence does not have to animate anything itself.
        rig.targetDistance = Mathf.Clamp(zoom, 1f, 30f);
    }

    public static void ResetZoom()
    {
        var rig = global::CameraFollowTarget.Instance;
        if (rig == null || _restingZoom < 0f) return;

        rig.targetDistance = _restingZoom;
        _restingZoom = -1f;
    }

    // Called when a biome comes up, next to the lighting override being dropped: a room that
    // zoomed or shifted the camera must not hand that on to the next one.
    public static void ResetAll()
    {
        var rig = global::CameraFollowTarget.Instance;
        if (rig == null)
        {
            _restingZoom = -1f;
            return;
        }

        rig.SetOffset(Vector3.zero);
        ResetZoom();
    }

    // ---- look at ----------------------------------------------------------------------------

    // Frames something else for a while and then hands the camera back. The anchor is parented to
    // the object when there is one, so a moving target stays framed; a target that has since been
    // destroyed falls back to the position it was authored at.
    public static IEnumerator LookAt(GameObject target, Vector3 fallback, float hold)
    {
        var rig = global::CameraFollowTarget.Instance;
        if (rig == null) yield break;

        var anchor = new GameObject("CultTweaker_CameraLookTarget");
        anchor.transform.position = target != null ? target.transform.position : fallback;
        if (target != null) anchor.transform.SetParent(target.transform, true);

        // Weights matter: in co-op the rig frames two players by weight, and putting them back
        // at 1 each would reframe the room after the shot.
        var previous = new List<global::CameraFollowTarget.Target>();
        foreach (var entry in rig.targets)
            if (entry?.gameObject != null) previous.Add(entry);

        rig.ClearAllTargets();
        rig.AddTarget(anchor, 1f);

        yield return new WaitForSeconds(Mathf.Max(0.1f, hold));

        rig.ClearAllTargets();
        foreach (var entry in previous)
            if (entry.gameObject != null) rig.AddTarget(entry.gameObject, entry.Weight);

        // Everything it was following is gone (a room reload during the shot): the rig only
        // updates while it has a target, so it would freeze looking at nothing.
        if (rig.targets.Count == 0 && PlayerFarming.Instance != null &&
            PlayerFarming.Instance.CameraBone != null)
            rig.AddTarget(PlayerFarming.Instance.CameraBone, 1f);

        Object.Destroy(anchor);
    }

    // ---- effects ----------------------------------------------------------------------------

    public const string EffectChromatic = "Chromatic aberration";
    public const string EffectVignette = "Vignette";
    public const string EffectDesaturate = "Desaturate";
    public const string EffectShake = "Camera shake";
    public const string EffectLetterboxOn = "Letterbox in";
    public const string EffectLetterboxOff = "Letterbox out";

    public static readonly string[] Effects =
    [
        EffectChromatic, EffectVignette, EffectDesaturate, EffectShake,
        EffectLetterboxOn, EffectLetterboxOff
    ];

    // The pulses run out and back over the action's duration, so a sequence never leaves the
    // screen stuck in an effect it forgot to undo. The letterbox is the exception - bars are a
    // state, which is why they are two separate actions.
    public static IEnumerator PlayEffect(string effect, float duration)
    {
        var seconds = duration > 0f ? duration : 1.5f;
        var biome = BiomeConstants.Instance;

        switch (effect)
        {
            case EffectLetterboxOn:
                try { LetterBox.Show(SnapLetterBox: false); }
                catch (System.Exception e) { Warn(effect, e); }
                yield break;

            case EffectLetterboxOff:
                try { LetterBox.Hide(); }
                catch (System.Exception e) { Warn(effect, e); }
                yield break;

            case EffectShake:
                try { CameraManager.instance?.ShakeCameraForDuration(0.2f, 0.45f, seconds); }
                catch (System.Exception e) { Warn(effect, e); }
                yield return new WaitForSeconds(seconds);
                yield break;
        }

        if (biome == null)
        {
            Plugin.Log.LogWarning($"MapEditor: no BiomeConstants in this scene; '{effect}' was skipped.");
            yield break;
        }

        var half = seconds * 0.5f;

        // Each tween is told where to start as well as where to end, so the return leg is exact
        // rather than a guess at what the value drifted to.
        switch (effect)
        {
            case EffectChromatic:
                var restChroma = biome.ChromaticAberrationDefaultValue;
                Try(() => biome.ChromaticAbberationTween(half, restChroma, 1f), effect);
                yield return new WaitForSeconds(half);
                Try(() => biome.ChromaticAbberationTween(half, 1f, restChroma), effect);
                yield return new WaitForSeconds(half);
                break;

            case EffectVignette:
                var restVignette = biome.VignetteDefaultValue;
                Try(() => biome.VignetteTween(half, restVignette, 0.75f), effect);
                yield return new WaitForSeconds(half);
                Try(() => biome.VignetteTween(half, 0.75f, restVignette), effect);
                yield return new WaitForSeconds(half);
                break;

            case EffectDesaturate:
                Try(() => biome.DesaturationStencilTween(half, 0f, 1f, 1f, 1f), effect);
                yield return new WaitForSeconds(half);
                Try(() => biome.DesaturationStencilTween(half, 1f, 0f, 1f, 1f), effect);
                yield return new WaitForSeconds(half);
                break;

            default:
                Plugin.Log.LogWarning($"MapEditor: unknown camera effect '{effect}'.");
                break;
        }
    }

    private static void Try(System.Action action, string effect)
    {
        try { action(); }
        catch (System.Exception e) { Warn(effect, e); }
    }

    private static void Warn(string effect, System.Exception e) =>
        Plugin.Log.LogWarning($"MapEditor: camera effect '{effect}' failed: {e.Message}");

    // ---- cutscenes ----------------------------------------------------------------------------

    // Plays through the game's own MMVideoPlayer - the prefab, the fullscreen surface, the skip
    // prompt and the menu blocking are all vanilla. Only the source differs: a vanilla cutscene
    // is a VideoClip in Resources, a custom one is a file on disk played by url, because a
    // VideoClip cannot be built at run time.
    //
    // The sequence waits for it: a cutscene covering the screen while the next action walks the
    // players around would be the wrong way round.
    public static IEnumerator PlayCutscene(string name, bool skippable)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;

        var path = APIHelper.CustomCutsceneLoader.PathFor(name);
        var finished = false;

        try
        {
            if (path != null) PlayFromFile(name, path, skippable, () => finished = true);
            else
                MMTools.MMVideoPlayer.Play(name, () => finished = true,
                    skippable ? MMTools.MMVideoPlayer.Options.ENABLE : MMTools.MMVideoPlayer.Options.DISABLE,
                    MMTools.MMVideoPlayer.Options.DISABLE);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: cutscene '{name}' failed to start: {e.Message}");
            yield break;
        }

        Plugin.Log.LogInfo($"MapEditor: playing {(path != null ? "custom" : "vanilla")} cutscene '{name}'.");

        // Realtime, and generous: a cutscene runs while the game is paused around it, and the
        // ceiling is only there so a video that never reports finishing cannot strand the run.
        var deadline = Time.unscaledTime + 900f;
        while (!finished && Time.unscaledTime < deadline) yield return null;

        if (!finished)
        {
            Plugin.Log.LogWarning($"MapEditor: cutscene '{name}' never reported finishing; moving on.");
            try { MMTools.MMVideoPlayer.ForceStopVideo(); }
            catch (System.Exception) { }
        }
    }

    // MMVideoPlayer.Play's own setup, with the source pointed at a file. Doing it here rather
    // than calling Play and correcting it afterwards: Play starts the video the moment it is
    // called, and a start on a source that does not exist raises an error that ends the cutscene
    // before the real one is loaded.
    private static void PlayFromFile(string name, string path, bool skippable, System.Action onDone)
    {
        var instance = MMTools.MMVideoPlayer.Instance;
        if (instance == null)
        {
            instance = Object.Instantiate(Resources.Load("MMVideoPlayer/Video Player")) as GameObject;
            if (instance == null)
            {
                Plugin.Log.LogWarning("MapEditor: the game's video player prefab could not be loaded.");
                onDone?.Invoke();
                return;
            }

            MMTools.MMVideoPlayer.Instance = instance;
            MMTools.MMVideoPlayer.mmVideoPlayer = instance.GetComponent<MMTools.MMVideoPlayer>();
        }
        else
        {
            instance.SetActive(true);
        }

        var host = MMTools.MMVideoPlayer.mmVideoPlayer;
        var player = instance.GetComponent<UnityEngine.Video.VideoPlayer>();
        if (host == null || player == null)
        {
            Plugin.Log.LogWarning("MapEditor: the video player prefab is not what this expects.");
            onDone?.Invoke();
            return;
        }

        host.Skippable = skippable
            ? MMTools.MMVideoPlayer.Options.ENABLE
            : MMTools.MMVideoPlayer.Options.DISABLE;
        host.FastForward = MMTools.MMVideoPlayer.Options.DISABLE;
        host.HideOnCompete = true;
        host.completed = false;
        if (host.skipPrompt != null) host.skipPrompt.SetActive(skippable);
        if (host.controlprompt != null) host.controlprompt.SetActive(false);

        if (MonoSingleton<UIManager>.Instance != null)
            MonoSingleton<UIManager>.Instance.ForceBlockMenus = true;

        // The statics are what the vanilla component's own Update reads: without them the skip
        // button does nothing and the end of the video is never noticed.
        MMTools.MMVideoPlayer.videoPlayer = player;
        MMTools.MMVideoPlayer.Callback = () => onDone?.Invoke();

        player.clip = null;
        player.source = UnityEngine.Video.VideoSource.Url;
        player.url = path;

        player.loopPointReached += MMTools.MMVideoPlayer.EndReached;
        player.errorReceived += MMTools.MMVideoPlayer.HandleVideoPlayerError;
        player.Play();

        Plugin.Log.LogInfo($"MapEditor: cutscene '{name}' streaming from {path}.");
    }

}
