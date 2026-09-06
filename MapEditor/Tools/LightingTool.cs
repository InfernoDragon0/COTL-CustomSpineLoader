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

    private bool _built;

    private const int HeaderSize = 19;

    private readonly List<(MapEditorSlider slider, Func<float> read)> _sliders = [];

    private MapEditorDropdown _profileDropdown;
    private string _lastProfile;

    public LightingTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    private MapLightingData Data => _editor.Map.Lighting ??= new MapLightingData();

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {

        ui.CreateButton(panel, "Reset To Biome", () =>
        {
            ForgetCurrentRoom();
            ClearOverride();

            AdoptBiomeValues();

            _editor.MarkEdited();
            _editor.SetStatus("Lighting reset to biome.");
        });

        ui.CreateHeader(panel, "- Profiles -", HeaderSize);
        _profileDropdown = ui.CreateDropdown(panel, "Apply saved profile", LightingProfiles.Names(),
            (_, name) => ApplyProfile(name));
        ui.CreateButton(panel, "Save As Profile", SaveProfile);
        ui.CreateButton(panel, "Delete Selected Profile", DeleteProfile, emphasis: MapEditorEmphasis.Quiet);

        ui.CreateHeader(panel, "- Ambient -", HeaderSize);
        ColourSliders(ui, panel, "Ambient", () => Data.Ambient);

        ui.CreateHeader(panel, "- Sun -", HeaderSize);
        ColourSliders(ui, panel, "Sun", () => Data.DirectionalLight);
        TrackedSlider(ui, panel, "Sun Intensity", 0f, 4f,
            () => Data.DirectionalIntensity, v => Data.DirectionalIntensity = v);
        TrackedSlider(ui, panel, "Shadow Strength", 0f, 1f,
            () => Data.ShadowStrength, v => Data.ShadowStrength = v);
        TrackedSlider(ui, panel, "Exposure", 0f, 3f,
            () => Data.Exposure, v => Data.Exposure = v);

        ui.CreateHeader(panel, "- Fog -", HeaderSize);
        ColourSliders(ui, panel, "Fog", () => Data.Fog);
        TrackedSlider(ui, panel, "Fog Near", 0f, 60f, () => Data.FogNear, v => Data.FogNear = v);
        TrackedSlider(ui, panel, "Fog Far", 0f, 120f, () => Data.FogFar, v => Data.FogFar = v);
        TrackedSlider(ui, panel, "Fog Height", 0f, 10f, () => Data.FogHeight, v => Data.FogHeight = v);
        TrackedSlider(ui, panel, "Fog Spread", 0f, 10f, () => Data.FogSpread, v => Data.FogSpread = v);

        BuildWeather(ui, panel);

        _built = true;
    }

    // ---- weather ------------------------------------------------------------------------------

    private MapWeatherData Weather => _editor.Map.Weather ??= new MapWeatherData();

    private readonly List<GameObject> _weatherRows = [];
    private MapEditorDropdown _weatherType;
    private MapEditorDropdown _weatherStrength;

    private void BuildWeather(MapEditorUI ui, RectTransform panel)
    {
        if (!WeatherControl.Available) return;

        _weatherRows.Add(ui.CreateHeader(panel, "- Weather -", HeaderSize));

        _weatherRows.Add(ui.CreateToggle(panel, "Override weather", Weather.Enabled, on =>
        {
            Weather.Enabled = on;
            if (on) WeatherControl.Apply(Weather);
            else WeatherControl.Clear();

            _editor.MarkEdited();
            _editor.SetStatus(on
                ? "This map sets its own weather."
                : "Weather left to the biome.");
        }));

        _weatherType = ui.CreateDropdown(panel, "Weather", WeatherControl.Types(),
            (_, name) => ChooseWeatherType(name));
        _weatherRows.Add(_weatherType.Root);

        _weatherStrength = ui.CreateDropdown(panel, "Strength", WeatherControl.StrengthsFor(Weather.Type),
            (_, name) => ChooseWeatherStrength(name));
        _weatherRows.Add(_weatherStrength.Root);
    }

    private void ChooseWeatherType(string name)
    {
        Weather.Type = name;

        var strengths = WeatherControl.StrengthsFor(name);
        _weatherStrength?.SetOptions(strengths);

        Weather.Strength = strengths.Count > 0 ? strengths[0] : "";
        _weatherStrength?.SetSelected(strengths.Count > 0 ? 0 : -1);

        TouchWeather();
    }

    private void ChooseWeatherStrength(string name)
    {
        Weather.Strength = name;
        TouchWeather();
    }

    private void TouchWeather()
    {
        Weather.Enabled = true;
        WeatherControl.Apply(Weather);

        _editor.MarkEdited();
        _editor.SetStatus($"Weather set to {Weather.Type} ({Weather.Strength}).");
    }

    private void SyncWeather()
    {
        var offer = RuntimeMapEditor.Context != EditorContext.Base;
        foreach (var row in _weatherRows)
            if (row != null) row.SetActive(offer);

        if (!offer || _weatherType == null) return;

        var types = WeatherControl.Types();
        _weatherType.SetOptions(types);
        _weatherType.SetSelected(types.IndexOf(Weather.Type ?? ""));

        var strengths = WeatherControl.StrengthsFor(Weather.Type);
        _weatherStrength.SetOptions(strengths);
        _weatherStrength.SetSelected(strengths.IndexOf(Weather.Strength ?? ""));
    }

    private void TrackedSlider(MapEditorUI ui, RectTransform panel, string label, float min, float max,
        Func<float> read, Action<float> write)
    {
        var slider = ui.CreateSlider(panel, label, min, max, read(), v => { write(v); Touch(); })
            .GetComponent<MapEditorSlider>();
        _sliders.Add((slider, read));
    }

    private void ColourSliders(MapEditorUI ui, RectTransform panel, string label,
        Func<SerializableColor> colour)
    {
        TrackedSlider(ui, panel, label + " R", 0f, 3f, () => colour().R, v => colour().R = v);
        TrackedSlider(ui, panel, label + " G", 0f, 3f, () => colour().G, v => colour().G = v);
        TrackedSlider(ui, panel, label + " B", 0f, 3f, () => colour().B, v => colour().B = v);
    }

    private void SyncSliders()
    {
        foreach (var (slider, read) in _sliders)
            if (slider != null) slider.SetValue(read(), notify: false);
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

        _editor.Map.Lighting = LightingProfiles.Clone(profile.Data);
        Apply();
        SyncSliders();
        _editor.MarkEdited();
        _editor.SetStatus($"Applied lighting profile '{profile.Name}'.");
    }

    private void SaveProfile()
    {
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
        if (!Data.Enabled) CaptureCurrent();

        SyncSliders();
        RefreshProfileOptions();
        SyncWeather();

        _editor.SetStatus("Drag a slider to take the room's lighting off the biome.");
    }

    public void OnExit() { }
    public void OnUpdate() { }

    /// The blueprint's lighting or weather changed under the panel (a peer edited it).
    internal void RefreshFromMap()
    {
        if (!_built) return;
        SyncSliders();
        SyncWeather();
    }

    private void Touch()
    {
        if (!_built) return;
        Data.Enabled = true;
        Apply();
        _editor.MarkEdited();
    }

    private void AdoptBiomeValues()
    {
        if (_biomeSnapshot != null) _editor.Map.Lighting = LightingProfiles.Clone(_biomeSnapshot);
        else CaptureCurrent();      // nothing ever overrode it, so the manager is showing the biome

        Data.Enabled = false;
        SyncSliders();
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

    private static readonly Dictionary<(int x, int y), MapLightingData> _roomLighting = [];

    private static MapLightingData _biomeSnapshot;

    private static BiomeRoom CurrentRoom =>
        BiomeGenerator.Instance != null ? BiomeGenerator.Instance.CurrentRoom : null;

    private static (int x, int y)? _assertedRoom;

    private static (int x, int y)? CurrentRoomKey()
    {
        var room = CurrentRoom;
        return room != null ? (room.x, room.y) : null;
    }

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

    public static void OnRoomEntered()
    {
        var key = CurrentRoomKey();

        if (_roomLighting.Count == 0 && _biomeSnapshot == null) return;

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

        if (data != null && data.Enabled) _roomLighting[key.Value] = data;
        else _roomLighting.Remove(key.Value);
    }

    public static void Apply(MapLightingData data) => Apply(data, 0f);

    public static void Apply(MapLightingData data, float fadeSeconds)
    {
        if (data == null || !data.Enabled) return;

        Remember(data);
        ApplyInternal(data, fadeSeconds);
    }

    private static void ApplyInternal(MapLightingData data, float fadeSeconds)
    {
        var manager = LightingManager.Instance;
        if (manager == null) return;

        if (fadeSeconds > 0f && StartFade(manager, data, fadeSeconds)) return;

        ApplyTo(manager, data, fadeSeconds);
    }

    private static bool StartFade(LightingManager manager, MapLightingData data, float fadeSeconds)
    {
        try
        {
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
        if (manager.lerpActive)
        {
            manager.lerpActive = false;

            for (var frames = 0; frames < 10 && manager != null && manager.IsTransitionActive; frames++)
                yield return null;

            if (manager == null) yield break;
            manager.currentSettings = manager.SetCurrentLightingSettings();
        }

        ApplyTo(manager, data, fadeSeconds);
    }

    private static BiomeLightingSettings _overrideSettings;

    private static BiomeLightingSettings OverrideSettings()
    {
        if (_overrideSettings != null) return _overrideSettings;

        _overrideSettings = ScriptableObject.CreateInstance<BiomeLightingSettings>();
        _overrideSettings.hideFlags = HideFlags.HideAndDontSave;

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

    private static float FadeMultiplier(LightingManager manager, float fadeSeconds) =>
        fadeSeconds > 0f && manager.transitionDuration > 0f ? fadeSeconds / manager.transitionDuration : 0f;

    private static void PrepareManager(LightingManager manager)
    {
        if (manager.currentSettings != null) manager.currentSettings.UnscaledTime = true;

        manager.lerpActive = false;
    }

    public static void ClearOverride() => ClearOverride(0f);

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
            ApplyInternal(_biomeSnapshot, fadeSeconds);
            return;
        }

        try
        {
            PrepareManager(manager);
            manager.inOverride = false;
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
        map.Lighting = Data;
        map.Weather = Weather;
    }
}
