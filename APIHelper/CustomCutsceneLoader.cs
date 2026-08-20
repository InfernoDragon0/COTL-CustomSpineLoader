using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

// Videos dropped in BepInEx/plugins/CultTweaker/CustomCutscenes. There is no config.json and no
// registration step: a file in the folder is a cutscene, named after itself.
//
// The game's own cutscenes are VideoClips compiled into Resources, which is why they cannot be
// added to - a VideoClip cannot be built at run time. A file on disk is played the other way
// Unity supports, VideoSource.Url pointed at the path, through the same MMVideoPlayer the game
// uses for its own. That is the only difference between a custom cutscene and a vanilla one.
public static class CustomCutsceneLoader
{
    public const string FolderName = "CustomCutscenes";

    // What Unity's VideoPlayer can open on Windows without extra codecs. Anything else in the
    // folder is left alone rather than offered and then failing at play time.
    private static readonly string[] Extensions = [".mp4", ".webm", ".mov", ".m4v"];

    // The game's own, by the name Resources knows them under. Offered alongside the custom ones
    // because "play the intro" is a reasonable thing for an authored room to want.
    public static readonly string[] VanillaCutscenes = ["Intro", "DLC_Intro", "Trailer", "Update_Video"];

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    public static void EnsureFolder()
    {
        try
        {
            if (!Directory.Exists(RootPath)) Directory.CreateDirectory(RootPath);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not create the {FolderName} folder: {e.Message}");
        }
    }

    // Read from disk each time rather than cached: a video dropped in while the game is running
    // should show up in the editor's picker without a restart.
    public static List<string> Names()
    {
        var names = new List<string>();

        try
        {
            EnsureFolder();
            foreach (var file in Directory.GetFiles(RootPath))
            {
                var extension = Path.GetExtension(file);
                if (Array.IndexOf(Extensions, extension.ToLowerInvariant()) < 0) continue;
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not list custom cutscenes: {e.Message}");
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // Null when no file of that name is in the folder, which is what tells the player to fall
    // back to a vanilla cutscene of the same name.
    public static string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        try
        {
            EnsureFolder();
            foreach (var extension in Extensions)
            {
                var path = Path.Combine(RootPath, name + extension);
                if (File.Exists(path)) return path;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not look up cutscene '{name}': {e.Message}");
        }

        return null;
    }

    public static void LogWhatIsThere()
    {
        var names = Names();
        Plugin.Log.LogInfo(names.Count == 0
            ? $"No custom cutscenes in {RootPath}."
            : $"{names.Count} custom cutscene(s) available: {string.Join(", ", names.ToArray())}.");
    }
}
