using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// One authored main menu. See README "Main menu presets".
//
// Everything in here is an OFFSET or a MULTIPLIER over what the game already draws, never an
// absolute: a preset straight out of the constructor has to mean "exactly vanilla", or a player who
// changes one thing silently inherits whatever the numbers happened to be on the machine it was
// authored on - and the menu moves between game versions.
public class CTMenuPreset
{
    public string PresetName = "untitledmenu";
    public string DisplayName = "";

    public CTMenuLook Look = new();
    public CTMenuCentrepiece Centrepiece = new();
    public CTMenuTitle Title = new();

    public string ShownName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : PresetName;
}

// The menu's colour, which is the Stylizer post-effect on the menu camera.
//
// Only the parts of that effect the menu actually runs are here. The Stylizer carries two separate
// grain implementations and the menu uses the newer one, which is driven by a shared
// PostProcessing profile asset rather than by the loose fields beside it - so `colored`,
// `monochrome`, `intensity`, `size`, `luminanceContribution` and `softness` are wired to the OLD
// path and change nothing on this screen. They were offered once, did nothing, and are gone.
public class CTMenuLook
{
    public bool Enabled;

    // A Palette asset by name - the game's own, listed live. Empty leaves the game's second slot
    // alone, which means Blend still works as a dial towards the DLC palette the menu already holds.
    public string Palette = "";

    // 0 is exactly vanilla; 1 is the chosen palette outright. This is the game's own LerpPalette,
    // which the menu already runs at 0 with a second palette loaded, so it costs nothing.
    public float Blend;

    // A shift applied to every colour in the chosen palette. Neutral is 0 / 1 / 1.
    public float HueShift;
    public float Saturation = 1f;
    public float Brightness = 1f;

    // The menu's own dark-mode overlay: a full-screen blend that inverts the field behind the lamb
    // and leaves the lamb alone. The blend ignores the image's hue, so there is no colour here -
    // only how far it goes.
    public bool Background;
    public float BackgroundStrength = 1f;

    // The colour the menu camera clears to, which is what the palette maps into the gold field.
    // The default is the cream the game ships, so a fresh preset is vanilla until it is moved.
    public bool OverrideClear;
    public SerializableColor ClearColor = new() { R = 0.992f, G = 0.941f, B = 0.827f, A = 1f };

    // How strongly the palette mapping applies at all. 1 is the menu's own setting.
    public float EffectIntensity = 1f;

    public bool Dither = true;
    public bool Grain = true;
    public bool Glitch;
}

public class CTMenuCentrepiece
{
    public bool Enabled;

    // "Menu" keeps the game's own skeleton and only redresses it. "Folder" loads a spine from a
    // subfolder of this preset. "Player" borrows whichever spine the player wears.
    public string Source = "Menu";

    // Folder source: a subfolder name inside the preset folder. Player source: a registered
    // "<spine>/<skin>" key, or empty for whichever is currently selected.
    public string Asset = "";

    public string Skin = "";
    public string Animation = "";
    public bool Loop = true;
    public float TimeScale = 1f;

    // Offsets in world units, added to wherever the game puts the skeleton.
    public SerializableVector3 Offset = new();

    // Multipliers on the skeleton's own scale, which is not uniform on this menu.
    public SerializableVector3 Scale = new() { X = 1f, Y = 1f, Z = 1f };

    // Degrees added to the skeleton's own rotation. The menu authors it lying back, so this is
    // rarely wanted, but a replacement spine may not share the same up.
    public SerializableVector3 Rotation = new();
}

public class CTMenuTitle
{
    public bool Enabled;

    // A png file name in the preset's folder. Empty keeps the game's logo and only moves it.
    public string Image = "";

    public SerializableVector3 Offset = new();
    public float Scale = 1f;

    // Draw the replacement at the logo rect's height, keeping the png's own aspect. Off stretches
    // it to the rect the game uses, which is almost never what a differently-shaped png wants.
    public bool PreserveAspect = true;

    public bool Hidden;

    // The line under the logo - "Cultist Edition" on this build. Its own switch, because a preset
    // that replaces the logo art usually wants that line gone, and one that only nudges the logo
    // usually wants it left alone.
    public bool OverrideEdition;

    // Empty keeps whatever the game put there.
    public string EditionText = "";
    public bool HideEdition;
    public bool TintEdition;
    public SerializableColor EditionColor = SerializableColor.From(Color.white);
}

public static class CTMenuPresetSerialization
{
    public const string FolderName = "CustomMainMenus";
    public const string ConfigFile = "config.json";

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    // Where a preset of this name is written: always our own folder.
    public static string FolderFor(string presetName) =>
        Path.Combine(RootPath, MapEditorSerialization.Sanitize(presetName));

    // Where a preset of this name is read from: ours if we have it, else whichever other mod ships
    // it. A preset is its folder - the config, the title art and any spine - so everything that
    // reads a preset's files goes through this, not FolderFor.
    public static string FolderForRead(string presetName)
    {
        var own = FolderFor(presetName);
        if (File.Exists(Path.Combine(own, ConfigFile))) return own;

        return APIHelper.ModContentPaths.FindDirectory(FolderName,
            MapEditorSerialization.Sanitize(presetName), ConfigFile) ?? own;
    }

    public static string PathFor(string presetName) =>
        Path.Combine(FolderFor(presetName), ConfigFile);

    // Exists = ours, the overwrite question. Available = ours or any other mod's, the load question.
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

    // Called at startup so there is somewhere to drop a logo png before any preset is saved.
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

    // A preset is its folder: saving under a new name writes a config.json somewhere the art is
    // not, so the art comes along. Existing files at the destination are left alone.
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

    // What Save would write, without writing it - the editor compares this against the last saved
    // copy to know whether closing would lose anything.
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

            // The folder name is the identity, not the name stored in the file.
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

    public static bool Delete(string presetName)
    {
        // Ours only: another mod's folder is not this editor's to remove.
        var folder = FolderFor(presetName);

        try
        {
            if (!Directory.Exists(folder)) return false;
            Directory.Delete(folder, true);
            Plugin.Log.LogInfo($"MenuEditor: deleted the main menu preset '{presetName}'.");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MenuEditor: could not delete '{presetName}': {e.Message}");
            return false;
        }
    }
}
