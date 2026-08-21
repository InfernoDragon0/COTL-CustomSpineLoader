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

        // The HUD is not MMVideoPlayer's business - vanilla's callers hide it themselves before
        // they play anything, which is why a cutscene started from a trigger came up with the
        // health and XP bars still sitting on top of it.
        // The room's own music and ambience are paused rather than stopped: a cutscene should not
        // play over the top of them, and pausing puts the track back exactly where it was without
        // needing to know what it was. Vanilla stops its music outright before a video, but it is
        // changing scene afterwards and does not have to.
        PauseRoomAudio();

        var hudHidden = false;
        try
        {
            if (HUD_Manager.Instance != null)
            {
                HUD_Manager.Instance.Hide(Snap: true, 0);
                hudHidden = true;
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not hide the HUD for the cutscene: " + e.Message);
        }

        try
        {
            if (path != null)
            {
                PlayFromFile(name, path, skippable, () => finished = true);
            }
            else
            {
                // The player is brought up and set up first, because MMVideoPlayer.Play starts
                // the video in the same call - there is no moment afterwards in which to
                // configure the audio that is not already too late.
                var host = EnsureVideoPlayer();
                if (host != null)
                {
                    SilenceVideoTrack(host.GetComponent<UnityEngine.Video.VideoPlayer>());
                    PrepareOverlay(host);
                }

                MMTools.MMVideoPlayer.Play(name, () => finished = true,
                    skippable ? MMTools.MMVideoPlayer.Options.ENABLE : MMTools.MMVideoPlayer.Options.DISABLE,
                    MMTools.MMVideoPlayer.Options.DISABLE);
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: cutscene '{name}' failed to start: {e.Message}");
            if (hudHidden && HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0);
            ResumeRoomAudio();
            yield break;
        }

        // The cached soundtrack next to the video, played through the game's own audio engine.
        var audio = APIHelper.CustomCutsceneLoader.AudioPathFor(name);
        PlayCompanionAudio(audio);

        Plugin.Log.LogInfo($"MapEditor: playing {(path != null ? "custom" : "vanilla")} cutscene " +
                           $"'{name}'{(audio != null ? " with sound" : " (no soundtrack file)")}.");

        // Realtime, and generous: a cutscene runs while the game is paused around it, and the
        // ceiling is only there so a video that never reports finishing cannot strand the run.
        var deadline = Time.unscaledTime + 900f;
        while (!finished && Time.unscaledTime < deadline)
        {
            ApplySettingsVolume();
            yield return null;
        }

        if (!finished)
        {
            Plugin.Log.LogWarning($"MapEditor: cutscene '{name}' never reported finishing; moving on.");
            try { MMTools.MMVideoPlayer.ForceStopVideo(); }
            catch (System.Exception) { }
        }

        StopCompanionAudio();
        ResumeRoomAudio();
        RestoreVideoCamera();

        try
        {
            if (hudHidden && HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not restore the HUD after the cutscene: " + e.Message);
        }
    }

    // MMVideoPlayer.Play's own setup, with the source pointed at a file. Doing it here rather
    // than calling Play and correcting it afterwards: Play starts the video the moment it is
    // called, and a start on a source that does not exist raises an error that ends the cutscene
    // before the real one is loaded.
    private static void PlayFromFile(string name, string path, bool skippable, System.Action onDone)
    {
        var instance = EnsureVideoPlayer();
        if (instance == null)
        {
            onDone?.Invoke();
            return;
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
        player.aspectRatio = UnityEngine.Video.VideoAspectRatio.FitInside;
        player.waitForFirstFrame = true;

        SilenceVideoTrack(player);
        ClearSurface(player);
        PrepareOverlay(instance);

        player.loopPointReached += MMTools.MMVideoPlayer.EndReached;
        player.errorReceived += MMTools.MMVideoPlayer.HandleVideoPlayerError;

        // Prepared first, then played: a url source that starts before it is open shows a black
        // frame or two while it catches up.
        void OnPrepared(UnityEngine.Video.VideoPlayer prepared)
        {
            prepared.prepareCompleted -= OnPrepared;
            prepared.Play();
        }

        player.prepareCompleted += OnPrepared;
        player.Prepare();
    }

    // The game's video player, brought up the way MMVideoPlayer.Play brings it up. Shared so
    // both routes get the same setup applied before anything starts playing.
    private static GameObject EnsureVideoPlayer()
    {
        var instance = MMTools.MMVideoPlayer.Instance;
        if (instance != null)
        {
            instance.SetActive(true);
            return instance;
        }

        instance = Object.Instantiate(Resources.Load("MMVideoPlayer/Video Player")) as GameObject;
        if (instance == null)
        {
            Plugin.Log.LogWarning("MapEditor: the game's video player prefab could not be loaded.");
            return null;
        }

        MMTools.MMVideoPlayer.Instance = instance;
        MMTools.MMVideoPlayer.mmVideoPlayer = instance.GetComponent<MMTools.MMVideoPlayer>();
        return instance;
    }

    // The line down the middle of every cutscene. The video prefab carries its own Camera with a
    // Stylizer image effect on it, and that effect draws a seam at the centre of the frame here -
    // the scenes vanilla plays cutscenes in never show it, because they are not this scene. The
    // effect contributes nothing to a video that is already a finished image, so it is switched
    // off for the duration and put back afterwards.
    private static void PrepareOverlay(GameObject instance)
    {
        if (instance == null) return;

        try
        {
            // By name rather than by type: Stylizer lives in an assembly the mod does not
            // reference, and the only thing needed from it is the Behaviour switch every image
            // effect has.
            foreach (var component in instance.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "Stylizer") continue;
                if (component is not Behaviour effect || !effect.enabled) continue;

                effect.enabled = false;
            }

            IsolateVideoCamera(instance.GetComponentInChildren<Camera>(true));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not disable the video Stylizer: " + e.Message);
        }
    }

    // ---- companion audio ---------------------------------------------------------------------

    // Unity's video player produces no audible sound in this build - the engine's own audio is
    // not what this game runs on - so a cutscene's sound comes from a file played through FMOD,
    // which is what everything else here is played through. Started at the same moment as the
    // video, so the two run together; a long video and a short track simply end apart.
    private static FMOD.Sound _companionSound;
    private static FMOD.Channel _companionChannel;
    private static bool _companionPlaying;

    // True when the sound is inside the game's music bus and inherits its volume on its own.
    // False when it is on the master group and this has to ride the slider itself.
    private static bool _companionOnMusicBus;
    private static FMOD.Studio.Bus _companionBus;

    private static void PlayCompanionAudio(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        StopCompanionAudio();

        try
        {
            var core = FMODUnity.RuntimeManager.CoreSystem;

            // Streamed rather than loaded: a cutscene's soundtrack is minutes long and there is
            // no reason to hold it in memory. 2D, because it is not coming from anywhere.
            var result = core.createSound(path,
                FMOD.MODE.CREATESTREAM | FMOD.MODE._2D | FMOD.MODE.LOOP_OFF, out _companionSound);

            if (result != FMOD.RESULT.OK)
            {
                Plugin.Log.LogWarning($"MapEditor: cutscene audio '{path}' could not be opened: {result}.");
                return;
            }

            // Into the game's music bus if it will have us, so the music slider, the pause duck
            // and anything else Studio does to that bus apply to a cutscene's soundtrack the same
            // way they apply to the game's own. A raw core channel would sit outside all of it and
            // play at full volume into a muted game.
            var group = MusicChannelGroup();

            result = core.playSound(_companionSound, group, false, out _companionChannel);
            if (result != FMOD.RESULT.OK)
            {
                Plugin.Log.LogWarning($"MapEditor: cutscene audio '{path}' could not be played: {result}.");
                _companionSound.release();
                ReleaseMusicBus();
                return;
            }

            _companionPlaying = true;
            if (!_companionOnMusicBus) ApplySettingsVolume();

            Plugin.Log.LogInfo(_companionOnMusicBus
                ? "MapEditor: cutscene audio routed into the game's music bus."
                : "MapEditor: cutscene audio on the master group, following the volume sliders.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: cutscene audio failed: " + e.Message);
        }
    }

    private static void StopCompanionAudio()
    {
        if (!_companionPlaying) return;
        _companionPlaying = false;

        try
        {
            _companionChannel.stop();
            _companionSound.release();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: cutscene audio could not be stopped: " + e.Message);
        }

        ReleaseMusicBus();
    }

    // ---- the room underneath ------------------------------------------------------------------

    private static bool _musicPaused;
    private static bool _atmosPaused;

    private static void PauseRoomAudio()
    {
        var audio = AudioManager.Instance;
        if (audio == null) return;

        try
        {
            var music = audio.CurrentMusicInstance;
            if (music.isValid() && music.setPaused(true) == FMOD.RESULT.OK) _musicPaused = true;

            // A paused instance still reports PLAYING, which is what keeps the blueprint music
            // watchdog from deciding the track has ended and starting it again over the cutscene.
            var atmos = audio.AtmosInstance;
            if (atmos.isValid() && atmos.setPaused(true) == FMOD.RESULT.OK) _atmosPaused = true;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not pause the room's audio: " + e.Message);
        }
    }

    private static void ResumeRoomAudio()
    {
        if (!_musicPaused && !_atmosPaused) return;

        var audio = AudioManager.Instance;

        try
        {
            if (audio != null)
            {
                if (_musicPaused)
                {
                    var music = audio.CurrentMusicInstance;
                    if (music.isValid()) music.setPaused(false);
                }

                if (_atmosPaused)
                {
                    var atmos = audio.AtmosInstance;
                    if (atmos.isValid()) atmos.setPaused(false);
                }
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not resume the room's audio: " + e.Message);
        }

        _musicPaused = false;
        _atmosPaused = false;
    }

    // A Studio bus only has a core channel group once something asks it to keep one, which is
    // what locking it does; flushCommands waits for that to actually happen, because Studio
    // processes its command queue on its own schedule.
    private static FMOD.ChannelGroup MusicChannelGroup()
    {
        _companionOnMusicBus = false;

        try
        {
            _companionBus = FMODUnity.RuntimeManager.GetBus("bus:/MusicBus");

            if (_companionBus.lockChannelGroup() != FMOD.RESULT.OK) return new FMOD.ChannelGroup();

            FMODUnity.RuntimeManager.StudioSystem.flushCommands();

            if (_companionBus.getChannelGroup(out var group) != FMOD.RESULT.OK)
            {
                _companionBus.unlockChannelGroup();
                return new FMOD.ChannelGroup();
            }

            _companionOnMusicBus = true;
            return group;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: cutscene audio could not join the music bus " +
                                  $"({e.Message}); it will follow the volume sliders directly.");
            return new FMOD.ChannelGroup();
        }
    }

    private static void ReleaseMusicBus()
    {
        if (!_companionOnMusicBus) return;
        _companionOnMusicBus = false;

        try
        {
            _companionBus.unlockChannelGroup();
        }
        catch (System.Exception)
        {
            // The bus went away with the banks; there is nothing left to unlock.
        }
    }

    // The fallback when the bus is not available: master times music, the same two sliders the
    // game's own music answers to. Re-applied while the cutscene runs so moving a slider mid-play
    // does something, which is what the bus route gives for free.
    public static void ApplySettingsVolume()
    {
        if (!_companionPlaying || _companionOnMusicBus) return;

        try
        {
            var audio = SettingsManager.Settings?.Audio;
            if (audio == null) return;

            _companionChannel.setVolume(Mathf.Clamp01(audio.MasterVolume) * Mathf.Clamp01(audio.MusicVolume));
        }
        catch (System.Exception)
        {
            // A channel that has finished cannot take a volume; nothing to do about it.
        }
    }

    // The video's own audio track is switched off rather than configured. Unity's audio engine
    // is compiled out of this build - sample rate 0, no voices - so nothing routed through it can
    // be heard, and leaving the track enabled only invites the backend to warn about output modes
    // it does not support. The sound comes from the cached soundtrack instead, through FMOD.
    private static void SilenceVideoTrack(UnityEngine.Video.VideoPlayer player)
    {
        if (player == null) return;

        try
        {
            player.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            player.controlledAudioTrackCount = 0;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: cutscene audio track could not be silenced: " + e.Message);
        }
    }

    // The prefab's render texture still holds the last thing drawn into it, and a video that does
    // not cover it exactly leaves that showing round the edges.
    private static void ClearSurface(UnityEngine.Video.VideoPlayer player)
    {
        try
        {
            if (player.renderMode != UnityEngine.Video.VideoRenderMode.RenderTexture) return;
            if (player.targetTexture == null) return;

            var previous = RenderTexture.active;
            RenderTexture.active = player.targetTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: cutscene surface could not be cleared: " + e.Message);
        }
    }

    // The video is drawn on the camera's near plane (renderMode CameraNearPlane), and that camera
    // is a real camera - so as well as the video it renders whatever world geometry falls in its
    // frustum. From where it sits, a room outline or an editor gizmo projects to a hairline,
    // which is the line down the middle of every cutscene: not part of the video, and not a UI
    // element either, which is why the canvas scan found nothing.
    //
    // Emptying its culling mask was the obvious answer and turned out to take the video with it -
    // Unity draws the near-plane quad as part of the camera's ordinary rendering, so a camera
    // culled down to nothing draws nothing at all. Moving the camera instead leaves it rendering
    // exactly as before, with nothing but empty space in front of it. The video rides on the near
    // plane, so it travels with the camera.
    private static Transform _movedCamera;
    private static Vector3 _cameraHome;

    private static readonly Vector3 Nowhere = new(0f, 100000f, 0f);

    private static void IsolateVideoCamera(Camera camera)
    {
        if (camera == null || _movedCamera != null) return;

        try
        {
            _movedCamera = camera.transform;
            _cameraHome = _movedCamera.position;
            _movedCamera.position = Nowhere;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not isolate the video camera: " + e.Message);
            _movedCamera = null;
        }
    }

    private static void RestoreVideoCamera()
    {
        if (_movedCamera == null) return;

        try
        {
            _movedCamera.position = _cameraHome;
        }
        catch (System.Exception)
        {
            // Gone with the scene; nothing to put back.
        }

        _movedCamera = null;
    }

}
