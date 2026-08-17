using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class LightingTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Lighting";

    private readonly RuntimeMapEditor _editor;

    private TMP_Text _stateLabel;
    private bool _built;

    // Every slider with how to read its current value back, so a profile (or a loaded map) can
    // move the knobs instead of leaving them showing the previous room's numbers.
    private readonly List<(Slider slider, Func<float> read)> _sliders = [];

    private MapEditorDropdown _profileDropdown;
    private string _lastProfile;

    public LightingTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    private MapLightingData Data => _editor.Map.Lighting ??= new MapLightingData();

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _stateLabel = ui.CreateLabel(panel, "Following the biome", 15, TextAlignmentOptions.Center)
            .GetComponent<TMP_Text>();

        ui.CreateButton(panel, "Capture Biome Lighting", () =>
        {
            CaptureCurrent();
            Data.Enabled = true;
            Apply();
            _editor.SetStatus("Biome lighting captured.");
        });

        ui.CreateButton(panel, "Reset To Biome", () =>
        {
            Data.Enabled = false;
            ClearOverride();
            UpdateStateLabel();
            _editor.SetStatus("Lighting reset to biome.");
        });

        ui.CreateHeader(panel, "Profiles");
        _profileDropdown = ui.CreateDropdown(panel, "Apply saved profile", LightingProfiles.Names(),
            (_, name) => ApplyProfile(name));
        ui.CreateButton(panel, "Save As Profile", SaveProfile);
        ui.CreateButton(panel, "Delete Selected Profile", DeleteProfile);

        ui.CreateHeader(panel, "Ambient");
        ColourSliders(ui, panel, "Ambient", () => Data.Ambient);

        ui.CreateHeader(panel, "Sun");
        ColourSliders(ui, panel, "Sun", () => Data.DirectionalLight);
        TrackedSlider(ui, panel, "Sun Intensity", 0f, 4f,
            () => Data.DirectionalIntensity, v => Data.DirectionalIntensity = v);
        TrackedSlider(ui, panel, "Shadow Strength", 0f, 1f,
            () => Data.ShadowStrength, v => Data.ShadowStrength = v);
        TrackedSlider(ui, panel, "Exposure", 0f, 3f,
            () => Data.Exposure, v => Data.Exposure = v);

        ui.CreateHeader(panel, "Fog");
        ColourSliders(ui, panel, "Fog", () => Data.Fog);
        // Near/far are the distances the fog fades between; height and spread shape how far up
        // it climbs and how soft its edge is.
        TrackedSlider(ui, panel, "Fog Near", 0f, 60f, () => Data.FogNear, v => Data.FogNear = v);
        TrackedSlider(ui, panel, "Fog Far", 0f, 120f, () => Data.FogFar, v => Data.FogFar = v);
        TrackedSlider(ui, panel, "Fog Height", 0f, 10f, () => Data.FogHeight, v => Data.FogHeight = v);
        TrackedSlider(ui, panel, "Fog Spread", 0f, 10f, () => Data.FogSpread, v => Data.FogSpread = v);

        _built = true;
    }

    private void TrackedSlider(MapEditorUI ui, RectTransform panel, string label, float min, float max,
        Func<float> read, Action<float> write)
    {
        var slider = ui.CreateSlider(panel, label, min, max, read(), v => { write(v); Touch(); })
            .GetComponentInChildren<Slider>();
        _sliders.Add((slider, read));
    }

    private void ColourSliders(MapEditorUI ui, RectTransform panel, string label,
        Func<SerializableColor> colour)
    {
        // HDR colours in this game routinely exceed 1, which is what gives the glow.
        TrackedSlider(ui, panel, label + " R", 0f, 3f, () => colour().R, v => colour().R = v);
        TrackedSlider(ui, panel, label + " G", 0f, 3f, () => colour().G, v => colour().G = v);
        TrackedSlider(ui, panel, label + " B", 0f, 3f, () => colour().B, v => colour().B = v);
    }

    private void SyncSliders()
    {
        foreach (var (slider, read) in _sliders)
            if (slider != null) slider.SetValueWithoutNotify(read());
    }

    // ---- profiles -----------------------------------------------------------------------------

    private void ApplyProfile(string name)
    {
        var profile = LightingProfiles.Find(name);
        if (profile == null)
        {
            _editor.SetStatus($"Lighting profile '{name}' was not found.", StatusSeverity.Warning);
            return;
        }

        _lastProfile = profile.Name;

        // A copy, so slider edits from here shape this map without rewriting the profile.
        _editor.Map.Lighting = LightingProfiles.Clone(profile.Data);
        Apply();
        SyncSliders();
        UpdateStateLabel();
        _editor.SetStatus($"Applied lighting profile '{profile.Name}'.");
    }

    private void SaveProfile()
    {
        // Saving while "following the biome" means "save what is on screen".
        if (!Data.Enabled) CaptureCurrent();

        MapNamePrompt.Show(_editor, _lastProfile ?? "", "NAME THIS LIGHTING PROFILE", name =>
        {
            LightingProfiles.Save(name, Data);
            _lastProfile = name.Trim();
            RefreshProfileOptions();
            _editor.SetStatus($"Saved lighting profile '{_lastProfile}'.");
        }, existsCheck: LightingProfiles.Exists, existsNoun: "lighting profile");
    }

    private void DeleteProfile()
    {
        if (string.IsNullOrEmpty(_lastProfile))
        {
            _editor.SetStatus("Apply or save a profile first; that is the one deleted.",
                StatusSeverity.Warning);
            return;
        }

        if (!LightingProfiles.Delete(_lastProfile))
        {
            _editor.SetStatus($"Lighting profile '{_lastProfile}' was already gone.");
            _lastProfile = null;
            RefreshProfileOptions();
            return;
        }

        _editor.SetStatus($"Deleted lighting profile '{_lastProfile}'. The map keeps its current look.");
        _lastProfile = null;
        RefreshProfileOptions();
    }

    private void RefreshProfileOptions()
    {
        if (_profileDropdown == null) return;
        _profileDropdown.SetOptions(LightingProfiles.Names());
        _profileDropdown.SetSelected(-1);
    }

    public void OnEnter()
    {
        // A blueprint that never captured anything starts from what the room actually looks
        // like, so the first slider drag is a nudge rather than a jump to black.
        if (!Data.Enabled) CaptureCurrent();

        // Both can have changed while the tool was closed: a map load brings its own lighting,
        // and the trigger tool can add profiles.
        SyncSliders();
        RefreshProfileOptions();

        UpdateStateLabel();
        _editor.SetStatus("Capture the biome, then edit.");
    }

    public void OnExit() { }
    public void OnUpdate() { }

    private void Touch()
    {
        if (!_built) return;
        Data.Enabled = true;
        Apply();
        UpdateStateLabel();
    }

    private void UpdateStateLabel()
    {
        if (_stateLabel != null)
            _stateLabel.text = Data.Enabled ? "Overriding the biome" : "Following the biome";
    }

    // Reads the room's live values so editing starts from what is on screen.
    private void CaptureCurrent()
    {
        var current = LightingManager.Instance != null ? LightingManager.Instance.currentSettings : null;
        if (current == null) return;

        var data = Data;
        data.Ambient = SerializableColor.From(current.AmbientColour);
        data.DirectionalLight = SerializableColor.From(current.DirectionalLightColour);
        data.DirectionalIntensity = current.DirectionalLightIntensity;
        data.ShadowStrength = current.ShadowStrength;
        data.Exposure = current.Exposure;
        data.Fog = SerializableColor.From(current.FogColor);
        data.FogNear = current.FogDist.x;
        data.FogFar = current.FogDist.y;
        data.FogHeight = current.FogHeight;
        data.FogSpread = current.FogSpread;
    }

    private static MapLightingData _biomeSnapshot;

    private static void SnapshotBiome(LightingManager manager)
    {
        if (_biomeSnapshot != null || manager.currentSettings == null) return;

        var current = manager.currentSettings;
        _biomeSnapshot = new MapLightingData
        {
            Enabled = true,
            Ambient = SerializableColor.From(current.AmbientColour),
            DirectionalLight = SerializableColor.From(current.DirectionalLightColour),
            DirectionalIntensity = current.DirectionalLightIntensity,
            ShadowStrength = current.ShadowStrength,
            Exposure = current.Exposure,
            Fog = SerializableColor.From(current.FogColor),
            FogNear = current.FogDist.x,
            FogFar = current.FogDist.y,
            FogHeight = current.FogHeight,
            FogSpread = current.FogSpread
        };
        Plugin.Log.LogInfo("MapEditor: captured the biome's own lighting before overriding it.");
    }

    // A scene or biome change makes the snapshot meaningless - the next override captures the
    // new biome's values instead.
    public static void ForgetBiomeSnapshot() => _biomeSnapshot = null;

    // Public: the blueprint loader applies a loaded room's lighting the same way.
    public static void Apply(MapLightingData data) => Apply(data, 0f);

    // fadeSeconds > 0 cross-fades to the new look instead of snapping to it. The sliders and the
    // blueprint loader stay instant - a fade there reads as lag - but a trigger's Apply-lighting
    // action is a cue, and a cue that cuts is a flicker.
    public static void Apply(MapLightingData data, float fadeSeconds)
    {
        if (data == null || !data.Enabled) return;

        var manager = LightingManager.Instance;
        if (manager == null) return;

        // The fade has to wait out any transition already running, so it goes through a coroutine.
        if (fadeSeconds > 0f && StartFade(manager, data, fadeSeconds)) return;

        ApplyTo(manager, data, fadeSeconds);
    }

    private static bool StartFade(LightingManager manager, MapLightingData data, float fadeSeconds)
    {
        try
        {
            // Hosted on the manager itself: it is alive whenever there is lighting to fade, and a
            // fade whose room is being torn down should die with it.
            manager.StartCoroutine(FadeRoutine(manager, data, fadeSeconds));
            return true;
        }
        catch (System.Exception e)
        {
            // A missed lighting cue is worse than an abrupt one.
            Plugin.Log.LogWarning("MapEditor: lighting fade could not start, applying at once: " + e.Message);
            return false;
        }
    }

    private static IEnumerator FadeRoutine(LightingManager manager, MapLightingData data, float fadeSeconds)
    {
        // A fade that starts while another transition is still unwinding would lerp from whatever
        // that one leaves in currentSettings, which is not what is on screen - a visible jump
        // before the fade even begins. Cancel it, let it land, then read the live values back.
        if (manager.lerpActive)
        {
            manager.lerpActive = false;

            // The cancelled coroutine needs two frames; the cap is so a queue that keeps feeding
            // itself cannot strand the cue.
            for (var frames = 0; frames < 10 && manager != null && manager.IsTransitionActive; frames++)
                yield return null;

            if (manager == null) yield break;
            manager.currentSettings = manager.SetCurrentLightingSettings();
        }

        ApplyTo(manager, data, fadeSeconds);
    }

    private static void ApplyTo(LightingManager manager, MapLightingData data, float fadeSeconds)
    {
        try
        {
            SnapshotBiome(manager);
            PrepareManager(manager);

            var settings = ScriptableObject.CreateInstance<BiomeLightingSettings>();
            // Without this the transition below never finishes while the editor is open (see
            // PrepareManager), so the first slider drag would be the last one that did anything.
            settings.UnscaledTime = true;
            settings.AmbientColour = data.Ambient.ToColor();
            settings.DirectionalLightColour = data.DirectionalLight.ToColor();
            settings.DirectionalLightIntensity = data.DirectionalIntensity;
            settings.ShadowStrength = data.ShadowStrength;
            settings.Exposure = data.Exposure;
            settings.FogColor = data.Fog.ToColor();
            settings.FogDist = new Vector2(data.FogNear, data.FogFar);
            settings.FogHeight = data.FogHeight;
            settings.FogSpread = data.FogSpread;

            // Only the properties below are taken from our settings; the rest keep coming from
            // the biome's own time-of-day asset.
            settings.overrideLightingProperties = new OverrideLightingProperties
            {
                Enabled = true,
                UnscaledTime = true,
                AmbientColor = true,
                DirectionalLightColor = true,
                DirectionalLightIntensity = true,
                ShadowStrength = true,
                Exposure = true,
                FogColor = true,
                FogDist = true,
                FogHeight = true,
                FogSpread = true
            };

            manager.overrideSettings = settings;
            manager.inOverride = true;
            manager.transitionDurationMultiplier = FadeMultiplier(manager, fadeSeconds);
            manager.UpdateLighting(allowInterupt: true, ignoreAccessibilitySetting: false, forceUpdate: true);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: lighting override failed: " + e.Message);
        }
    }

    private void Apply() => Apply(Data);

    // The manager scales its own transitionDuration (5s out of the box) rather than taking
    // seconds, and resets the multiplier to 1 at the end of every transition - so it is set on
    // each apply. 0 lands the change this frame, which is what a slider drag needs.
    private static float FadeMultiplier(LightingManager manager, float fadeSeconds) =>
        fadeSeconds > 0f && manager.transitionDuration > 0f ? fadeSeconds / manager.transitionDuration : 0f;

    private static void PrepareManager(LightingManager manager)
    {
        if (manager.currentSettings != null) manager.currentSettings.UnscaledTime = true;

        // UpdateLighting *reverses* a running lerp instead of starting a new one (it flips
        // deltaTimeMult while lerpActive), so the old one is stood down first.
        manager.lerpActive = false;
    }

    public static void ClearOverride() => ClearOverride(0f);

    public static void ClearOverride(float fadeSeconds)
    {
        var manager = LightingManager.Instance;
        if (manager == null) return;

        // Nothing was ever overridden, so there is nothing to undo - and no snapshot to
        // restore from either.
        if (_biomeSnapshot == null && !manager.inOverride) return;

        if (_biomeSnapshot != null)
        {
            var snapshot = _biomeSnapshot;
            // Apply() must not treat this restore as the first override and re-snapshot the
            // custom values as if they were the biome's.
            Apply(snapshot, fadeSeconds);
            Plugin.Log.LogInfo("MapEditor: lighting restored to the biome's own values.");
            return;
        }

        try
        {
            PrepareManager(manager);
            manager.inOverride = false;
            // Without a snapshot the biome comes back through the manager's own path, whose
            // default is already a 5-second fade - so an unasked-for duration keeps that.
            manager.transitionDurationMultiplier =
                fadeSeconds > 0f ? FadeMultiplier(manager, fadeSeconds) : 1f;
            manager.UpdateLighting(allowInterupt: true, ignoreAccessibilitySetting: false, forceUpdate: true);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: lighting reset failed: " + e.Message);
        }
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        // Edited directly on the live blueprint; the hook exists so a future refactor cannot
        // silently drop it.
        map.Lighting = Data;
    }
}
