using System.Collections.Generic;
using System.IO;

namespace CustomSpineLoader.APIHelper;

public static class ModContentPaths
{
    public const string BridgeFolder = "CultTweaker";

    private const int MaxDepth = 3;

    private static List<string> _bridges;

    public static string OwnRoot(string folderName) => Path.Combine(Plugin.PluginPath, folderName);

    public static List<string> RootsFor(string folderName)
    {
        var roots = new List<string>();

        var own = OwnRoot(folderName);
        if (Directory.Exists(own)) roots.Add(own);

        foreach (var bridge in Bridges())
        {
            var candidate = Path.Combine(bridge, folderName);
            if (Directory.Exists(candidate)) roots.Add(candidate);
        }

        return roots;
    }

    public static bool HasForeignContent(string folderName) => RootsFor(folderName).Count > 1;

    public static List<string> FilesIn(string folderName, string pattern)
    {
        var byName = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var root in RootsFor(folderName))
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Could not read '{root}': {e.Message}");
                continue;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (byName.TryGetValue(name, out var winner))
                {
                    Plugin.Log.LogWarning($"'{name}' in {folderName} is provided twice; keeping " +
                                          $"{winner} and ignoring {file}.");
                    continue;
                }

                byName[name] = file;
                ordered.Add(file);
            }
        }

        return ordered;
    }

    public static List<string> DirectoriesIn(string folderName)
    {
        var byName = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var root in RootsFor(folderName))
        {
            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Could not read '{root}': {e.Message}");
                continue;
            }

            foreach (var folder in folders)
            {
                var name = Path.GetFileName(folder);
                if (byName.TryGetValue(name, out var winner))
                {
                    Plugin.Log.LogWarning($"'{name}' in {folderName} is provided twice; keeping " +
                                          $"{winner} and ignoring {folder}.");
                    continue;
                }

                byName[name] = folder;
                ordered.Add(folder);
            }
        }

        return ordered;
    }

    public static string FindFile(string folderName, string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;

        foreach (var root in RootsFor(folderName))
        {
            var path = Path.Combine(root, fileName);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public static string FindDirectory(string folderName, string subFolder, string mustContain = null)
    {
        if (string.IsNullOrEmpty(subFolder)) return null;

        foreach (var root in RootsFor(folderName))
        {
            var path = Path.Combine(root, subFolder);
            if (!Directory.Exists(path)) continue;
            if (mustContain != null && !File.Exists(Path.Combine(path, mustContain))) continue;
            return path;
        }

        return null;
    }

    // ---- finding the bridges -------------------------------------------------------------------

    private static List<string> Bridges()
    {
        if (_bridges != null) return _bridges;

        _bridges = [];

        try
        {
            var plugins = Path.GetDirectoryName(Plugin.PluginPath);
            if (string.IsNullOrEmpty(plugins) || !Directory.Exists(plugins)) return _bridges;

            var own = Path.GetFullPath(Plugin.PluginPath);

            foreach (var mod in Directory.GetDirectories(plugins))
            {
                if (string.Equals(Path.GetFullPath(mod), own, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Collect(mod, 1);
            }

            if (_bridges.Count > 0)
                Plugin.Log.LogInfo($"CultTweaker content folders found in {_bridges.Count} other " +
                                   $"mod(s): {string.Join(", ", _bridges.ToArray())}");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Could not scan the other mods for CultTweaker content: " + e.Message);
        }

        return _bridges;
    }

    private static void Collect(string folder, int depth)
    {
        if (depth > MaxDepth) return;

        try
        {
            var bridge = Path.Combine(folder, BridgeFolder);
            if (Directory.Exists(bridge))
            {
                _bridges.Add(bridge);
                return;
            }

            foreach (var child in Directory.GetDirectories(folder))
                Collect(child, depth + 1);
        }
        catch (System.Exception)
        {
        }
    }

    public static void LogWhatIsThere(params string[] folderNames)
    {
        var bridges = Bridges();
        if (bridges.Count == 0) return;

        foreach (var folderName in folderNames)
        {
            var roots = RootsFor(folderName);
            if (roots.Count <= 1) continue;

            var foreign = roots.GetRange(1, roots.Count - 1);
            Plugin.Log.LogInfo($"{folderName}: also loading from {string.Join(", ", foreign.ToArray())}");
        }
    }
}
