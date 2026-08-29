using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public static class CustomCutsceneLoader
{
    public const string FolderName = "CustomCutscenes";

    private static readonly string[] Extensions = [".mp4", ".webm", ".mov", ".m4v"];

    public static readonly string[] VanillaCutscenes = ["Intro", "DLC_Intro", "Trailer", "Update_Video"];

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

    public static List<string> Names()
    {
        var names = new List<string>();

        try
        {
            EnsureFolder();

            foreach (var file in ModContentPaths.FilesIn(FolderName, "*"))
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

    public static string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        try
        {
            EnsureFolder();
            foreach (var extension in Extensions)
            {
                var path = ModContentPaths.FindFile(FolderName, name + extension);
                if (path != null) return path;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not look up cutscene '{name}': {e.Message}");
        }

        return null;
    }

    public static string AudioPathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        try
        {
            EnsureFolder();
            foreach (var extension in AudioExtensions)
            {
                var path = ModContentPaths.FindFile(FolderName, name + extension);
                if (path != null) return path;
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

            if (!probe.WaitForExit(4000))
            {
                try { probe.Kill(); } catch (Exception) { }
                return false;
            }

            return probe.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

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

            process.StartInfo.Arguments =
                $"-y -hide_banner -loglevel error -i \"{video}\" -vn -c:a libvorbis -q:a 4 \"{output}\"";

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;

            var stderr = new System.Text.StringBuilder();
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null) lock (stderr) stderr.AppendLine(args.Data);
            };

            process.Exited += (_, _) =>
            {
                lock (Converting) Converting.Remove(name);

                try
                {
                    string tail;
                    lock (stderr) tail = stderr.ToString().Trim();

                    if (process.ExitCode == 0 && File.Exists(output))
                        Plugin.Log.LogInfo($"Cutscene '{name}': soundtrack extracted to {Path.GetFileName(output)}.");
                    else
                        Plugin.Log.LogWarning($"Cutscene '{name}': ffmpeg could not extract a soundtrack " +
                                              $"(exit {process.ExitCode}). {tail}");
                }
                catch (Exception)
                {
                }

                process.Dispose();
            };

            process.Start();
            process.BeginErrorReadLine();
            Plugin.Log.LogInfo($"Cutscene '{name}': extracting its soundtrack with ffmpeg in the background.");
        }
        catch (Exception e)
        {
            lock (Converting) Converting.Remove(name);
            Plugin.Log.LogWarning($"Cutscene '{name}': soundtrack extraction failed to start: {e.Message}");
        }
    }
}
