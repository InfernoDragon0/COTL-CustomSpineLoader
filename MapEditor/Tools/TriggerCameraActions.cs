using System.Collections;
using System.Collections.Generic;
using Lamb.UI;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public static class TriggerCameraActions
{
    // ---- offset -----------------------------------------------------------------------------

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

        rig.targetDistance = Mathf.Clamp(zoom, 1f, 30f);
    }

    public static void ResetZoom()
    {
        var rig = global::CameraFollowTarget.Instance;
        if (rig == null || _restingZoom < 0f) return;

        rig.targetDistance = _restingZoom;
        _restingZoom = -1f;
    }

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

    public static IEnumerator LookAt(GameObject target, Vector3 fallback, float hold)
    {
        var rig = global::CameraFollowTarget.Instance;
        if (rig == null) yield break;

        var anchor = new GameObject("CultTweaker_CameraLookTarget");
        anchor.transform.position = target != null ? target.transform.position : fallback;
        if (target != null) anchor.transform.SetParent(target.transform, true);

        var previous = new List<global::CameraFollowTarget.Target>();
        foreach (var entry in rig.targets)
            if (entry?.gameObject != null) previous.Add(entry);

        rig.ClearAllTargets();
        rig.AddTarget(anchor, 1f);

        yield return new WaitForSeconds(Mathf.Max(0.1f, hold));

        rig.ClearAllTargets();
        foreach (var entry in previous)
            if (entry.gameObject != null) rig.AddTarget(entry.gameObject, entry.Weight);

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
        EffectChromatic, EffectVignette, EffectDesaturate,
        EffectLetterboxOn, EffectLetterboxOff
    ];

    // ---- shake ------------------------------------------------------------------------------

    public static readonly string[] ShakeLabels =
    [
        "Rumble (faint)", "Light", "Medium", "Heavy", "Violent"
    ];

    private static readonly float[] ShakeStrengths = [0.12f, 0.25f, 0.45f, 0.75f, 1.2f];

    public static float ShakeStrength(int index) =>
        index >= 0 && index < ShakeStrengths.Length ? ShakeStrengths[index] : 0.45f;

    public static IEnumerator Shake(float strength, float duration)
    {
        var seconds = duration > 0f ? duration : 1f;
        var top = strength > 0f ? strength : 0.45f;

        try
        {
            CameraManager.instance?.ShakeCameraForDuration(top * 0.45f, top, seconds);
        }
        catch (System.Exception e)
        {
            Warn("Camera shake", e);
        }

        yield return new WaitForSeconds(seconds);
    }

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
                yield return Shake(0.45f, seconds);
                yield break;
        }

        if (biome == null)
        {
            Plugin.Log.LogWarning($"MapEditor: no BiomeConstants in this scene; '{effect}' was skipped.");
            yield break;
        }

        var half = seconds * 0.5f;

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

    public static IEnumerator PlayCutscene(string name, bool skippable)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;

        var path = APIHelper.CustomCutsceneLoader.PathFor(name);
        var finished = false;

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
            RestoreVideoCamera();
            yield break;
        }

        var audio = APIHelper.CustomCutsceneLoader.AudioPathFor(name);
        PlayCompanionAudio(audio);

        Plugin.Log.LogInfo($"MapEditor: playing {(path != null ? "custom" : "vanilla")} cutscene " +
                           $"'{name}'{(audio != null ? " with sound" : " (no soundtrack file)")}.");

        try
        {
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
        }
        finally
        {
            StopCompanionAudio();
            ResumeRoomAudio();
            RestoreVideoCamera();
            MMTools.MMVideoPlayer.Callback = null;

            try
            {
                if (hudHidden && HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: could not restore the HUD after the cutscene: " + e.Message);
            }
        }
    }

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

        player.loopPointReached -= MMTools.MMVideoPlayer.EndReached;
        player.loopPointReached += MMTools.MMVideoPlayer.EndReached;
        player.errorReceived -= MMTools.MMVideoPlayer.HandleVideoPlayerError;
        player.errorReceived += MMTools.MMVideoPlayer.HandleVideoPlayerError;

        void OnPrepared(UnityEngine.Video.VideoPlayer prepared)
        {
            prepared.prepareCompleted -= OnPrepared;
            prepared.Play();
        }

        player.prepareCompleted += OnPrepared;
        player.Prepare();
    }

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

    private static void PrepareOverlay(GameObject instance)
    {
        if (instance == null) return;

        try
        {
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

    private static FMOD.Sound _companionSound;
    private static FMOD.Channel _companionChannel;
    private static bool _companionPlaying;

    private static bool _companionOnMusicBus;
    private static FMOD.Studio.Bus _companionBus;

    private static void PlayCompanionAudio(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        StopCompanionAudio();

        try
        {
            var core = FMODUnity.RuntimeManager.CoreSystem;

            var result = core.createSound(path,
                FMOD.MODE.CREATESTREAM | FMOD.MODE._2D | FMOD.MODE.LOOP_OFF, out _companionSound);

            if (result != FMOD.RESULT.OK)
            {
                Plugin.Log.LogWarning($"MapEditor: cutscene audio '{path}' could not be opened: {result}.");
                return;
            }

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
        }
    }

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
        }
    }

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
        }

        _movedCamera = null;
    }

}
