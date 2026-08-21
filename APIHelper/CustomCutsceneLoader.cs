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

    // Audio formats FMOD decodes on Windows. Notably not the .mp4's own AAC track: FMOD only
    // decodes AAC on Apple platforms, which is why a companion file is the route rather than
    // pulling the audio out of the video.
    private static readonly string[] AudioExtensions = [".ogg", ".mp3", ".wav", ".flac", ".aiff"];

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

    // A sound file sitting next to the video with the same name - test2.mp4 and test2.ogg - is
    // played alongside it. This is how the game does its own cutscenes: the intro is a silent
    // video with "event:/music/intro/intro_video" fired next to it, because the video player in
    // this build has no audible route of its own.
    //
    // The name is matched against the vanilla cutscenes too, so Intro.ogg gives the game's own
    // intro a soundtrack.
    public static string AudioPathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        try
        {
            EnsureFolder();
            foreach (var extension in AudioExtensions)
            {
                var path = Path.Combine(RootPath, name + extension);
                if (File.Exists(path)) return path;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not look up cutscene audio for '{name}': {e.Message}");
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

    // ---- pulling the soundtrack out of the video -------------------------------------------

    // A video's audio has to be a separate file to be heard (see AudioPathFor), and asking
    // somebody to run a converter by hand for every cutscene they make is a poor trade. If
    // ffmpeg is around, the extraction is done here instead - once per video, in the background,
    // leaving the .ogg next to the .mp4 exactly where the player looks for it.
    private static readonly HashSet<string> Converting = new(StringComparer.OrdinalIgnoreCase);

    public static void ConvertMissingAudio()
    {
        var names = Names();
        if (names.Count == 0) return;

        var pending = new List<string>();
        foreach (var name in names)
            if (AudioPathFor(name) == null) pending.Add(name);

        if (pending.Count == 0) return;

        foreach (var name in pending) ExtractOne(name);
    }

    // Windows' own decoder first, and ffmpeg only if that cannot be used. The first needs nothing
    // installed and produces a .wav; the second is smaller output but somebody has to have
    // downloaded it. Either way the file lands next to the video and is found from then on.
    private static void ExtractOne(string name)
    {
        var video = PathFor(name);
        if (video == null) return;

        lock (Converting)
        {
            if (!Converting.Add(name)) return;
        }

        if (CutsceneAudioExtractor.Available)
        {
            var wav = Path.Combine(RootPath, name + ".wav");

            Plugin.Log.LogInfo($"Cutscene '{name}': decoding its soundtrack in the background.");

            CutsceneAudioExtractor.ExtractInBackground(video, wav, (ok, error) =>
            {
                lock (Converting) Converting.Remove(name);

                if (ok)
                {
                    Plugin.Log.LogInfo($"Cutscene '{name}': soundtrack cached as {Path.GetFileName(wav)}.");
                    return;
                }

                Plugin.Log.LogWarning($"Cutscene '{name}': could not decode its soundtrack ({error}).");

                // Only worth falling back for a decoder that is unusable at all; a video with no
                // audio track in it has nothing for ffmpeg to find either.
                if (CutsceneAudioExtractor.Available) return;

                var tool = FindFfmpeg();
                if (tool != null) ExtractWithFfmpeg(tool, name);
                else ReportNoConverter(name);
            });

            return;
        }

        lock (Converting) Converting.Remove(name);

        var ffmpeg = FindFfmpeg();
        if (ffmpeg != null) ExtractWithFfmpeg(ffmpeg, name);
        else ReportNoConverter(name);
    }

    private static void ReportNoConverter(string name)
    {
        Plugin.Log.LogInfo($"Cutscene '{name}' will play silently: its soundtrack could not be read " +
                           "here and no converter is available. Put an .ogg or .wav of the same name " +
                           $"in {RootPath}, or ffmpeg.exe in " +
                           $"{Path.Combine(Plugin.PluginPath, ToolsFolder)}.");
    }

    private const string ToolsFolder = "Tools";

    // The mod's own copy first, then whatever is on PATH. Running "ffmpeg" bare is left to the
    // OS to resolve rather than searching PATH by hand.
    private static string FindFfmpeg()
    {
        try
        {
            var local = Path.Combine(Path.Combine(Plugin.PluginPath, ToolsFolder), "ffmpeg.exe");
            if (File.Exists(local)) return local;

            var beside = Path.Combine(Plugin.PluginPath, "ffmpeg.exe");
            if (File.Exists(beside)) return beside;

            return CanRun("ffmpeg") ? "ffmpeg" : null;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Could not look for ffmpeg: " + e.Message);
            return null;
        }
    }

    private static bool CanRun(string executable)
    {
        try
        {
            using var probe = new System.Diagnostics.Process();
            probe.StartInfo.FileName = executable;
            probe.StartInfo.Arguments = "-version";
            probe.StartInfo.UseShellExecute = false;
            probe.StartInfo.CreateNoWindow = true;
            probe.StartInfo.RedirectStandardOutput = true;
            probe.StartInfo.RedirectStandardError = true;
            probe.Start();

            // -version returns immediately; anything slower is not ffmpeg answering.
            if (!probe.WaitForExit(4000))
            {
                try { probe.Kill(); } catch (Exception) { }
                return false;
            }

            return probe.ExitCode == 0;
        }
        catch (Exception)
        {
            // Not installed, or not allowed to start processes. Either way, not available.
            return false;
        }
    }

    // Fire and forget: a long cutscene takes a while to transcode and nothing should wait for it.
    // The file appears next to the video when it is done and is picked up the next time that
    // cutscene plays - including later in the same session.
    private static void ExtractWithFfmpeg(string ffmpeg, string name)
    {
        var video = PathFor(name);
        if (video == null) return;

        lock (Converting)
        {
            if (!Converting.Add(name)) return;
        }

        var output = Path.Combine(RootPath, name + ".ogg");

        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = ffmpeg;

            // -vn drops the video, so this only ever touches the audio track. -q:a 4 is Vorbis's
            // "good enough for anything with dialogue in it" without a large file.
            process.StartInfo.Arguments =
                $"-y -hide_banner -loglevel error -i \"{video}\" -vn -c:a libvorbis -q:a 4 \"{output}\"";

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;

            process.Exited += (_, _) =>
            {
                lock (Converting) Converting.Remove(name);

                try
                {
                    if (process.ExitCode == 0 && File.Exists(output))
                        Plugin.Log.LogInfo($"Cutscene '{name}': soundtrack extracted to {Path.GetFileName(output)}.");
                    else
                        Plugin.Log.LogWarning($"Cutscene '{name}': ffmpeg could not extract a soundtrack " +
                                              $"(exit {process.ExitCode}). {process.StandardError.ReadToEnd().Trim()}");
                }
                catch (Exception)
                {
                    // The process object is disposed underneath us on some runtimes; the file
                    // being there or not is the answer that matters.
                }

                process.Dispose();
            };

            process.Start();
            Plugin.Log.LogInfo($"Cutscene '{name}': extracting its soundtrack with ffmpeg in the background.");
        }
        catch (Exception e)
        {
            lock (Converting) Converting.Remove(name);
            Plugin.Log.LogWarning($"Cutscene '{name}': soundtrack extraction failed to start: {e.Message}");
        }
    }
}
