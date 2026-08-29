using System;
using System.Collections.Generic;
using HarmonyLib;
using MMBiomeGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public static class WeatherControl
{
    public static bool Available => WeatherSystemController.Instance != null;

    public static bool Holding { get; private set; }

    // ---- what this game actually has ----------------------------------------------------------

    private static List<(WeatherSystemController.WeatherType type,
        WeatherSystemController.WeatherStrength strength)> _combos;

    private static WeatherSystemController _read;

    private static void Read()
    {
        var controller = WeatherSystemController.Instance;
        if (controller == null) { _combos ??= []; return; }
        if (_combos != null && ReferenceEquals(_read, controller)) return;

        _read = controller;
        _combos = [];

        try
        {
            var field = AccessTools.Field(typeof(WeatherSystemController), "weatherData");
            if (field?.GetValue(controller) is not WeatherSystemController.WeatherData[] table)
            {
                Plugin.Log.LogWarning("MapEditor: the game's weather table could not be read; the " +
                                      "weather list will be empty.");
                return;
            }

            if (Plugin.MapEditorFullWeather is not { Value: false })
                table = Fill(controller, field, table);

            foreach (var entry in table)
            {
                if (entry == null) continue;
                if (entry.WeatherType == WeatherSystemController.WeatherType.None) continue;
                if (_combos.Contains((entry.WeatherType, entry.WeatherStrength))) continue;
                _combos.Add((entry.WeatherType, entry.WeatherStrength));
            }

            Plugin.Log.LogInfo($"MapEditor: {_combos.Count} weather setting(s) available.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: the game's weather table could not be read: " + e.Message);
        }
    }

    // ---- the strengths the game did not ship ---------------------------------------------------

    private static WeatherSystemController.WeatherData[] Fill(WeatherSystemController controller,
        System.Reflection.FieldInfo field, WeatherSystemController.WeatherData[] table)
    {
        var have = new List<WeatherSystemController.WeatherData>(table);
        var added = new List<string>();

        foreach (WeatherSystemController.WeatherType type in
                 Enum.GetValues(typeof(WeatherSystemController.WeatherType)))
        {
            if (type == WeatherSystemController.WeatherType.None) continue;

            foreach (WeatherSystemController.WeatherStrength strength in
                     Enum.GetValues(typeof(WeatherSystemController.WeatherStrength)))
            {
                if (have.Exists(e => e != null && e.WeatherType == type && e.WeatherStrength == strength))
                    continue;

                var source = Nearest(have, type, strength);
                if (source == null) continue;

                have.Add(Scaled(source, strength));
                added.Add($"{type} {strength}");
            }
        }

        if (added.Count == 0) return table;

        var filled = have.ToArray();
        field.SetValue(controller, filled);

        Plugin.Log.LogInfo("MapEditor: weather the game does not ship, built from what it does - " +
                           string.Join(", ", added) + ".");
        return filled;
    }

    private static WeatherSystemController.WeatherData Nearest(
        List<WeatherSystemController.WeatherData> table,
        WeatherSystemController.WeatherType type,
        WeatherSystemController.WeatherStrength strength)
    {
        WeatherSystemController.WeatherData best = null;
        var bestGap = float.MaxValue;

        foreach (var entry in table)
        {
            if (entry == null || entry.WeatherType != type) continue;

            if (entry.Chance <= 0f && best != null) continue;

            var gap = Mathf.Abs(Weight(entry.WeatherStrength) - Weight(strength));
            if (gap >= bestGap) continue;

            best = entry;
            bestGap = gap;
        }

        return best;
    }

    private static float Weight(WeatherSystemController.WeatherStrength strength) => strength switch
    {
        WeatherSystemController.WeatherStrength.Dusting => 0.35f,
        WeatherSystemController.WeatherStrength.Light => 0.6f,
        WeatherSystemController.WeatherStrength.Medium => 1f,
        WeatherSystemController.WeatherStrength.Heavy => 1.6f,
        WeatherSystemController.WeatherStrength.Extreme => 2.4f,
        _ => 1f
    };

    private static WeatherSystemController.WeatherData Scaled(
        WeatherSystemController.WeatherData source,
        WeatherSystemController.WeatherStrength strength)
    {
        var factor = Weight(strength) / Mathf.Max(0.01f, Weight(source.WeatherStrength));

        var tint = source.WeatherTint;
        var built = new WeatherSystemController.WeatherData
        {
            WeatherType = source.WeatherType,
            WeatherStrength = strength,

            Chance = 0f,

            ParticlesOverTime = source.ParticlesOverTime * factor,
            ParticleSystemReference = source.ParticleSystemReference,
            WeatherTint = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(tint.a * factor)),
            BlizzardOverlayOpacity = Mathf.Clamp01(source.BlizzardOverlayOpacity * factor),

            ShaderKeyword = source.ShaderKeyword,
            ShaderVariables = Scaled(source.ShaderVariables, factor),

            SoundLoop = source.SoundLoop,
            Volume = Mathf.Clamp01(source.Volume * factor),
            HasWind = source.HasWind,
            OverrideDuration = source.OverrideDuration
        };

        built.ParticleSystem = source.ParticleSystem;
        return built;
    }

    private static WeatherSystemController.ShaderVariable[] Scaled(
        WeatherSystemController.ShaderVariable[] source, float factor)
    {
        if (source == null) return null;

        var scaled = new WeatherSystemController.ShaderVariable[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var variable = source[i];

            if (!variable.SetOnStartOnly)
            {
                variable.TargetValue *= factor;
                variable.TargetVector *= factor;
            }

            scaled[i] = variable;
        }

        return scaled;
    }

    public static List<string> Types()
    {
        Read();

        var names = new List<string>();
        foreach (var (type, _) in _combos)
        {
            var name = type.ToString();
            if (!names.Contains(name)) names.Add(name);
        }

        return names;
    }

    public static List<string> StrengthsFor(string typeName)
    {
        Read();

        var names = new List<string>();
        foreach (var (type, strength) in _combos)
        {
            if (!string.Equals(type.ToString(), typeName, StringComparison.OrdinalIgnoreCase)) continue;

            var name = strength.ToString();
            if (!names.Contains(name)) names.Add(name);
        }

        return names;
    }

    // ---- applying -----------------------------------------------------------------------------

    public static void Apply(MapWeatherData data)
    {
        if (data == null || !data.Enabled) return;

        var controller = WeatherSystemController.Instance;
        if (controller == null) return;

        if (!Enum.TryParse<WeatherSystemController.WeatherType>(data.Type, true, out var type) ||
            type == WeatherSystemController.WeatherType.None)
            return;

        if (!Enum.TryParse<WeatherSystemController.WeatherStrength>(data.Strength, true, out var strength))
            return;

        Untouched(() => controller.SetWeather(type, strength, Mathf.Max(0f, data.Transition)));
        Holding = true;
    }

    public static void Clear()
    {
        Holding = false;

        var controller = WeatherSystemController.Instance;
        if (controller == null) return;

        Untouched(() => controller.StopCurrentWeather(0f));
    }

    public static void Release()
    {
        if (!Holding) return;
        Clear();
    }

    public static void Forget()
    {
        Holding = false;
        _combos = null;
        _read = null;
        _roomWeather.Clear();
        _asserted = null;
    }

    // ---- which room asked for what -------------------------------------------------------------

    private static readonly Dictionary<(int x, int y), MapWeatherData> _roomWeather = [];
    private static (int x, int y)? _asserted;

    private static (int x, int y)? RoomKey()
    {
        var room = BiomeGenerator.Instance != null ? BiomeGenerator.Instance.CurrentRoom : null;
        return room != null ? (room.x, room.y) : null;
    }

    public static void ForRoom(MapWeatherData data)
    {
        var key = RoomKey();
        if (key != null) _roomWeather[key.Value] = data;
        _asserted = key;

        if (data is { Enabled: true }) Apply(data);
        else Release();
    }

    public static void OnBiomeRoomChanged()
    {
        try
        {
            OnRoomEntered();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: room-change weather failed: " + e.Message);
        }
    }

    private static void OnRoomEntered()
    {
        if (_roomWeather.Count == 0 && !Holding) return;

        var key = RoomKey();

        if (key != null && key.Equals(_asserted)) return;
        _asserted = key;

        if (key != null && _roomWeather.TryGetValue(key.Value, out var data) && data is { Enabled: true })
        {
            Apply(data);
            return;
        }

        Release();
    }

    private static void Untouched(Action act)
    {
        var save = DataManager.Instance;
        if (save == null) { act(); return; }

        var type = save.WeatherType;
        var strength = save.WeatherStrength;
        var start = save.WeatherStartingTime;
        var duration = save.WeatherDuration;

        try
        {
            act();
        }
        finally
        {
            save.WeatherType = type;
            save.WeatherStrength = strength;
            save.WeatherStartingTime = start;
            save.WeatherDuration = duration;
        }
    }
}
