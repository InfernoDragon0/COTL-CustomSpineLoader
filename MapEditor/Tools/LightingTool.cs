using System;
using System.Collections;
using System.Collections.Generic;
using MMBiomeGeneration;
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

    // Slider + reader pairs, so a profile or loaded map can move the knobs.
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
            ForgetCurrentRoom();
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
        // Near/far: fade distances. Height/spread: vertical reach and edge softness.
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
        // HDR colours routinely exceed 1, hence the 0-3 range.
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

        // Clone: slider edits must not rewrite the profile.
        _editor.Map.Lighting = LightingProfiles.Clone(profile.Data);
        Apply();
        SyncSliders();
        UpdateStateLabel();
        _editor.SetStatus($"Applied lighting profile '{profile.Name}'.");
    }

    private void SaveProfile()
    {
        // Saving while following the biome saves what is on screen.
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
        // Uncaptured blueprint starts from the live look, not defaults.
        if (!Data.Enabled) CaptureCurrent();

        // Map loads and the trigger tool can change these while the tool is closed.
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

    // ---- which room owns which lighting -----------------------------------------------------

    // Per-room lighting, keyed by grid coords rather than the BiomeRoom object: BiomeRoom
    // identity can change on revisit, which left re-entered rooms plain.
    private static readonly Dictionary<(int x, int y), MapLightingData> _roomLighting = [];

    // Biome values captured once, before any override. Restoring these is not the same as
    // clearing inOverride: that transitions to the time-of-day target, not the biome's own look.
    private static MapLightingData _biomeSnapshot;

    private static BiomeRoom CurrentRoom =>
        BiomeGenerator.Instance != null ? BiomeGenerator.Instance.CurrentRoom : null;

    // Last room asserted; more than one hook announces the same arrival (see DungeonPatches).
    private static (int x, int y)? _assertedRoom;

    private static (int x, int y)? CurrentRoomKey()
    {
        var room = CurrentRoom;
        return room != null ? (room.x, room.y) : null;
    }

    // Subscribed to BiomeGenerator.OnBiomeChangeRoom, the one signal firing on EVERY door change
    // (revisits skip the generation hooks). Wrapped so a slip cannot break other subscribers.
    public static void OnBiomeRoomChanged()
    {
        try
        {
            OnRoomEntered();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: room-change lighting failed: " + e.Message);
        }
    }

    // On arrival: assert the arriving room's own lighting, or clear the override.
    public static void OnRoomEntered()
    {
        var key = CurrentRoomKey();

        // Nothing ever overridden: keep this per-room-change hook free in ordinary play.
        if (_roomLighting.Count == 0 && _biomeSnapshot == null) return;

        // The same arrival, announced a second time.
        if (key != null && key.Equals(_assertedRoom)) return;
        _assertedRoom = key;

        var where = key != null ? $"room {key}" : "an unknown room";

        if (key != null && _roomLighting.TryGetValue(key.Value, out var data) && data is { Enabled: true })
        {
            Plugin.Log.LogInfo($"MapEditor: re-applying the lighting {where} asked for.");
            Apply(data);
            return;
        }

        Plugin.Log.LogInfo($"MapEditor: {where} has no lighting of its own; back to the biome.");
        ClearOverride();
    }

    // Called on biome start and when a level run ends.
    public static void ForgetRoomLighting()
    {
        _roomLighting.Clear();
        _biomeSnapshot = null;
        _assertedRoom = null;
    }

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

    private static void Remember(MapLightingData data)
    {
        var key = CurrentRoomKey();
        if (key == null) return;

        // The live object, not a copy: sliders write straight into it while editing.
        if (data != null && data.Enabled) _roomLighting[key.Value] = data;
        else _roomLighting.Remove(key.Value);
    }

    // Public: the blueprint loader applies a loaded room's lighting the same way.
    public static void Apply(MapLightingData data) => Apply(data, 0f);

    // fadeSeconds > 0 cross-fades; sliders and the loader stay instant, trigger cues fade.
    public static void Apply(MapLightingData data, float fadeSeconds)
    {
        if (data == null || !data.Enabled) return;

        Remember(data);
        ApplyInternal(data, fadeSeconds);
    }

    // The push without Remember(): a biome restore must not be recorded as the room's own look.
    private static void ApplyInternal(MapLightingData data, float fadeSeconds)
    {
        var manager = LightingManager.Instance;
        if (manager == null) return;

        // Coroutine: the fade must wait out any transition already running.
        if (fadeSeconds > 0f && StartFade(manager, data, fadeSeconds)) return;

        ApplyTo(manager, data, fadeSeconds);
    }

    private static bool StartFade(LightingManager manager, MapLightingData data, float fadeSeconds)
    {
        try
        {
            // Hosted on the manager so a fade dies with the scene that wanted it.
            manager.StartCoroutine(FadeRoutine(manager, data, fadeSeconds));
            return true;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: lighting fade could not start, applying at once: " + e.Message);
            return false;
        }
    }

    private static IEnumerator FadeRoutine(LightingManager manager, MapLightingData data, float fadeSeconds)
    {
        // Starting mid-transition would lerp from stale currentSettings (a visible jump):
        // cancel the running one, let it land, then read the live values back.
        if (manager.lerpActive)
        {
            manager.lerpActive = false;

            // The cancelled coroutine needs two frames; the cap keeps the cue from stranding.
            for (var frames = 0; frames < 10 && manager != null && manager.IsTransitionActive; frames++)
                yield return null;

            if (manager == null) yield break;
            manager.currentSettings = manager.SetCurrentLightingSettings();
        }

        ApplyTo(manager, data, fadeSeconds);
    }

    // One settings object, reused and rewritten. A fresh ScriptableObject per apply gets swept by
    // UnloadUnusedAssets (room changes trigger it) while the manager still references it;
    // HideAndDontSave keeps this one out of that sweep.
    private static BiomeLightingSettings _overrideSettings;

    private static BiomeLightingSettings OverrideSettings()
    {
        if (_overrideSettings != null) return _overrideSettings;

        _overrideSettings = ScriptableObject.CreateInstance<BiomeLightingSettings>();
        _overrideSettings.hideFlags = HideFlags.HideAndDontSave;

        // Only these properties come from us; the rest follow the biome's time-of-day asset.
        _overrideSettings.overrideLightingProperties = new OverrideLightingProperties
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

        return _overrideSettings;
    }

    private static void ApplyTo(LightingManager manager, MapLightingData data, float fadeSeconds)
    {
        try
        {
            SnapshotBiome(manager);
            PrepareManager(manager);

            var settings = OverrideSettings();
            // Editor runs at timeScale 0: a scaled transition never finishes there.
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

    // The manager scales transitionDuration (5s default) and resets the multiplier to 1 after
    // every transition, so it is set on each apply. 0 lands the change this frame.
    private static float FadeMultiplier(LightingManager manager, float fadeSeconds) =>
        fadeSeconds > 0f && manager.transitionDuration > 0f ? fadeSeconds / manager.transitionDuration : 0f;

    private static void PrepareManager(LightingManager manager)
    {
        if (manager.currentSettings != null) manager.currentSettings.UnscaledTime = true;

        // UpdateLighting *reverses* a running lerp (flips deltaTimeMult) instead of starting
        // a new one; stand the old one down first.
        manager.lerpActive = false;
    }

    public static void ClearOverride() => ClearOverride(0f);

    // Explicit give-up only (Reset button, blueprint with no lighting). NOT part of
    // ClearOverride, which runs on every unlit-room arrival and would erase lit rooms' memory.
    public static void ForgetCurrentRoom()
    {
        var key = CurrentRoomKey();
        if (key != null) _roomLighting.Remove(key.Value);
    }

    public static void ClearOverride(float fadeSeconds)
    {
        var manager = LightingManager.Instance;
        if (manager == null) return;

        if (_biomeSnapshot == null && !manager.inOverride) return;

        if (_biomeSnapshot != null)
        {
            // ApplyInternal, not Apply: a restore must not be recorded as the room's own lighting.
            ApplyInternal(_biomeSnapshot, fadeSeconds);
            return;
        }

        try
        {
            PrepareManager(manager);
            manager.inOverride = false;
            // No snapshot: the manager's own path already defaults to a 5s fade; keep it.
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
        // Already edited live on the blueprint; hook guards against a refactor dropping it.
        map.Lighting = Data;
    }
}
