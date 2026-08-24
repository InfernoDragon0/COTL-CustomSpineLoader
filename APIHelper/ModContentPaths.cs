using System.Collections.Generic;
using System.IO;

namespace CustomSpineLoader.APIHelper;

// Content shipped by other mods.
//
// Every kind of custom content this mod loads lives in a folder of a known name inside its own
// plugin folder - CustomNpcs, CustomEnemies, CustomWorldMaps and the rest. Another mod can hand its
// content to those same loaders by putting a "CultTweaker" folder beside its own files and using the
// same folder names inside it:
//
//     BepInEx/plugins/SomeOtherMod/CultTweaker/CustomNpcs/TheirNpc/config.json
//
// Nothing is registered and nothing is copied: the loaders simply read those folders too. Only
// reading is shared - everything this mod writes still goes to its own folder, so a foreign mod's
// files are never edited in place and an update to that mod cannot be clobbered by ours.
//
// Where two mods use the same name for the same kind of thing, OURS WINS and the other is skipped
// with a warning; between two foreign mods, the first found wins. The player's own creations are
// therefore never shadowed by an installed mod.
public static class ModContentPaths
{
    // What another mod calls the folder it hands us content through.
    public const string BridgeFolder = "CultTweaker";

    // How far under BepInEx/plugins a bridge folder is looked for. Thunderstore installs one
    // folder per mod, but some managers nest a level or two deeper (Author-Mod/plugins/...), and an
    // unbounded sweep of a large plugins folder is a cost paid on every scan.
    private const int MaxDepth = 3;

    private static List<string> _bridges;

    // Our own folder for this kind of content: the only one anything is ever written to.
    public static string OwnRoot(string folderName) => Path.Combine(Plugin.PluginPath, folderName);

    // Every folder of this name to read from, ours first.
    public static List<string> RootsFor(string folderName)
    {
        var roots = new List<string>();

        var own = OwnRoot(folderName);
        if (Directory.Exists(own)) roots.Add(own);

        // Existence is re-tested per call rather than cached with the bridge list: a mod may create
        // its folder after we first looked.
        foreach (var bridge in Bridges())
        {
            var candidate = Path.Combine(bridge, folderName);
            if (Directory.Exists(candidate)) roots.Add(candidate);
        }

        return roots;
    }

    public static bool HasForeignContent(string folderName) => RootsFor(folderName).Count > 1;

    // Files of a pattern across every root, keyed by file name so ours shadows theirs.
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

    // Subfolders across every root, keyed by folder name so ours shadows theirs.
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

    // One named file, ours first. Null when no root holds it.
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

    // One named subfolder, ours first; a folder only counts when it holds `mustContain` (a world
    // map is its config.json - an empty folder of the right name is not a map).
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
                // Our own tree is not a foreign mod, and descending into it would find our own
                // folders a second time.
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
                // Found this mod's bridge; its own subfolders are content, not more mods.
                _bridges.Add(bridge);
                return;
            }

            foreach (var child in Directory.GetDirectories(folder))
                Collect(child, depth + 1);
        }
        catch (System.Exception)
        {
            // An unreadable folder is one place with no content in it, not a failed scan.
        }
    }

    // For the log line at startup: what was found, or that nothing was.
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
