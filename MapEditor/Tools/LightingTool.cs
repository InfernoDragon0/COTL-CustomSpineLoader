using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public class LightingTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Lighting";

    private readonly RuntimeMapEditor _editor;

    private TMP_Text _stateLabel;
    private bool _built;

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

        ui.CreateHeader(panel, "Ambient");
        ColourSliders(ui, panel, "Ambient", () => Data.Ambient);

        ui.CreateHeader(panel, "Sun");
        ColourSliders(ui, panel, "Sun", () => Data.DirectionalLight);
        ui.CreateSlider(panel, "Sun Intensity", 0f, 4f, Data.DirectionalIntensity,
            v => { Data.DirectionalIntensity = v; Touch(); });
        ui.CreateSlider(panel, "Shadow Strength", 0f, 1f, Data.ShadowStrength,
            v => { Data.ShadowStrength = v; Touch(); });
        ui.CreateSlider(panel, "Exposure", 0f, 3f, Data.Exposure,
            v => { Data.Exposure = v; Touch(); });

        ui.CreateHeader(panel, "Fog");
        ColourSliders(ui, panel, "Fog", () => Data.Fog);
        // Near/far are the distances the fog fades between; height and spread shape how far up
        // it climbs and how soft its edge is.
        ui.CreateSlider(panel, "Fog Near", 0f, 60f, Data.FogNear, v => { Data.FogNear = v; Touch(); });
        ui.CreateSlider(panel, "Fog Far", 0f, 120f, Data.FogFar, v => { Data.FogFar = v; Touch(); });
        ui.CreateSlider(panel, "Fog Height", 0f, 10f, Data.FogHeight, v => { Data.FogHeight = v; Touch(); });
        ui.CreateSlider(panel, "Fog Spread", 0f, 10f, Data.FogSpread, v => { Data.FogSpread = v; Touch(); });

        _built = true;
    }

    private void ColourSliders(MapEditorUI ui, RectTransform panel, string label,
        System.Func<SerializableColor> colour)
    {
        // HDR colours in this game routinely exceed 1, which is what gives the glow.
        ui.CreateSlider(panel, label + " R", 0f, 3f, colour().R, v => { colour().R = v; Touch(); });
        ui.CreateSlider(panel, label + " G", 0f, 3f, colour().G, v => { colour().G = v; Touch(); });
        ui.CreateSlider(panel, label + " B", 0f, 3f, colour().B, v => { colour().B = v; Touch(); });
    }

    public void OnEnter()
    {
        // A blueprint that never captured anything starts from what the room actually looks
        // like, so the first slider drag is a nudge rather than a jump to black.
        if (!Data.Enabled) CaptureCurrent();
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
    public static void Apply(MapLightingData data)
    {
        if (data == null || !data.Enabled) return;

        var manager = LightingManager.Instance;
        if (manager == null) return;

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
            // 0 = apply now rather than crossfading, so a slider reads as immediate feedback.
            manager.transitionDurationMultiplier = 0f;
            manager.UpdateLighting(allowInterupt: true, ignoreAccessibilitySetting: false, forceUpdate: true);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: lighting override failed: " + e.Message);
        }
    }

    private void Apply() => Apply(Data);

    private static void PrepareManager(LightingManager manager)
    {
        if (manager.currentSettings != null) manager.currentSettings.UnscaledTime = true;
        manager.lerpActive = false;
    }

    public static void ClearOverride()
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
            Apply(snapshot);
            Plugin.Log.LogInfo("MapEditor: lighting restored to the biome's own values.");
            return;
        }

        try
        {
            PrepareManager(manager);
            manager.inOverride = false;
            manager.transitionDurationMultiplier = 1f;
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
