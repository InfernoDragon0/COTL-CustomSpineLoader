using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class CTMenuPreset
{
    public string PresetName = "untitledmenu";
    public string DisplayName = "";

    public CTMenuLook Look = new();
    public CTMenuCentrepiece Centrepiece = new();
    public CTMenuTitle Title = new();

    public string ShownName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : PresetName;
}

public class CTMenuLook
{
    public bool Enabled;

    public string Palette = "";

    public float Blend;

    public float HueShift;
    public float Saturation = 1f;
    public float Brightness = 1f;

    public bool Background;
    public float BackgroundStrength = 1f;

    public bool OverrideClear;
    public SerializableColor ClearColor = new() { R = 0.992f, G = 0.941f, B = 0.827f, A = 1f };

    public float EffectIntensity = 1f;

    public bool Dither = true;
    public bool Grain = true;
    public bool Glitch;
}

public class CTMenuCentrepiece
{
    public bool Enabled;

    public string Source = "Menu";

    public string Asset = "";

    public string Skin = "";
    public string Animation = "";
    public bool Loop = true;
    public float TimeScale = 1f;

    public SerializableVector3 Offset = new();

    public SerializableVector3 Scale = new() { X = 1f, Y = 1f, Z = 1f };

    public SerializableVector3 Rotation = new();
}

public class CTMenuTitle
{
    public bool Enabled;

    public string Image = "";

    public SerializableVector3 Offset = new();
    public float Scale = 1f;

    public bool PreserveAspect = true;

    public bool Hidden;

    public string EditionText = "";
    public bool HideEdition;
    public bool TintEdition;
    public SerializableColor EditionColor = SerializableColor.From(Color.white);
    public SerializableVector3 EditionOffset = new();
}

public static class CTMenuPresetSerialization
{
    public const string FolderName = "CustomMainMenus";
    public const string ConfigFile = "config.json";

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    public static string FolderFor(string presetName) =>
        Path.Combine(RootPath, MapEditorSerialization.Sanitize(presetName));

    public static string FolderForRead(string presetName)
    {
        var own = FolderFor(presetName);
        if (File.Exists(Path.Combine(own, ConfigFile))) return own;

        return APIHelper.ModContentPaths.FindDirectory(FolderName,
            MapEditorSerialization.Sanitize(presetName), ConfigFile) ?? own;
    }

    public static string PathFor(string presetName) =>
        Path.Combine(FolderFor(presetName), ConfigFile);

    public static bool Exists(string presetName) => File.Exists(PathFor(presetName));

    public static bool Available(string presetName) =>
        File.Exists(Path.Combine(FolderForRead(presetName), ConfigFile));

    public static string FreeName(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) stem = "untitledmenu";
        stem = MapEditorSerialization.Sanitize(stem);

        if (!Available(stem)) return stem;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = stem + suffix;
            if (!Available(candidate)) return candidate;
        }

        return stem + Guid.NewGuid().ToString("N").Substring(0, 4);
    }

    public static void EnsureRootFolder()
    {
        try
        {
            if (!Directory.Exists(RootPath)) Directory.CreateDirectory(RootPath);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MenuEditor: could not create the main menu folder: " + e.Message);
        }
    }

    public static string Save(CTMenuPreset preset)
    {
        if (preset == null) return null;

        preset.PresetName = MapEditorSerialization.Sanitize(preset.PresetName);

        try
        {
            var folder = FolderFor(preset.PresetName);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var path = PathFor(preset.PresetName);
            File.WriteAllText(path, JsonConvert.SerializeObject(preset, Formatting.Indented));
            Plugin.Log.LogInfo($"MenuEditor: saved main menu preset '{preset.PresetName}' to {path}");
            return path;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MenuEditor: failed to save the main menu preset: " + e);
            return null;
        }
    }

    public static int CopyArt(string fromName, string toName)
    {
        if (string.IsNullOrWhiteSpace(fromName) || string.IsNullOrWhiteSpace(toName)) return 0;

        var from = FolderForRead(fromName);
        var to = FolderFor(toName);
        if (!Directory.Exists(from) ||
            string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
            return 0;

        try
        {
            if (!Directory.Exists(to)) Directory.CreateDirectory(to);
            return CopyTree(from, to, skipConfig: true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MenuEditor: could not copy the art of '{fromName}' to " +
                                  $"'{toName}': {e.Message}");
            return 0;
        }
    }

    private static int CopyTree(string from, string to, bool skipConfig)
    {
        var copied = 0;

        foreach (var file in Directory.GetFiles(from))
        {
            var name = Path.GetFileName(file);
            if (skipConfig && string.Equals(name, ConfigFile, StringComparison.OrdinalIgnoreCase)) continue;

            var destination = Path.Combine(to, name);
            if (File.Exists(destination)) continue;

            File.Copy(file, destination);
            copied++;
        }

        foreach (var folder in Directory.GetDirectories(from))
        {
            var destination = Path.Combine(to, Path.GetFileName(folder));
            if (!Directory.Exists(destination)) Directory.CreateDirectory(destination);
            copied += CopyTree(folder, destination, skipConfig: false);
        }

        return copied;
    }

    public static string ToJson(CTMenuPreset preset)
    {
        if (preset == null) return "";

        try
        {
            return JsonConvert.SerializeObject(preset, Formatting.Indented);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MenuEditor: could not serialise the preset: " + e.Message);
            return "";
        }
    }

    public static CTMenuPreset LoadByName(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName)) return null;

        var folder = FolderForRead(presetName);
        var path = Path.Combine(folder, ConfigFile);

        try
        {
            if (!File.Exists(path)) return null;

            var preset = JsonConvert.DeserializeObject<CTMenuPreset>(File.ReadAllText(path));
            if (preset == null) return null;

            preset.PresetName = Path.GetFileName(folder);
            preset.Look ??= new CTMenuLook();
            preset.Centrepiece ??= new CTMenuCentrepiece();
            preset.Title ??= new CTMenuTitle();
            return preset;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"MenuEditor: could not parse the preset at '{path}': {e.Message}");
            return null;
        }
    }

    public static List<string> ListNames()
    {
        var names = new List<string>();

        try
        {
            foreach (var folder in APIHelper.ModContentPaths.DirectoriesIn(FolderName))
            {
                if (!File.Exists(Path.Combine(folder, ConfigFile))) continue;
                names.Add(Path.GetFileName(folder));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MenuEditor: could not list the main menu presets: " + e.Message);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

}
