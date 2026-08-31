using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;

namespace CustomSpineLoader.ModUI.SkinEditor;

public class CTFollowerSkinDocument
{
    public string SkinName = "untitledskin";
    public string VariantName = "base";
    public FollowerSkinConfig Config = new() { PartConfigs = [] };

    public string ShownName => SkinName + "/" + VariantName;

    public int ColourSetCount
    {
        get
        {
            var parts = Config?.PartConfigs?.Values.Where(p => p?.ColorChoices != null).ToList();
            return parts == null || parts.Count == 0 ? 1 : Math.Max(1, parts.Min(p => p.ColorChoices.Count));
        }
    }

    public void NormaliseColourSets()
    {
        if (Config?.PartConfigs == null) return;
        var count = ColourSetCount;
        foreach (var part in Config.PartConfigs.Values)
        {
            if (part == null) continue;
            part.ColorChoices ??= [];
            while (part.ColorChoices.Count < count)
                part.ColorChoices.Add(part.ColorChoices.Count > 0 ? part.ColorChoices[^1] : "#FFFFFF");
            while (part.ColorChoices.Count > count)
                part.ColorChoices.RemoveAt(part.ColorChoices.Count - 1);
        }
    }
}

public static class CTFollowerSkinSerialization
{
    public const string FolderName = FollowerSpineLoader.FolderName;
    public const string ConfigFile = "config.json";
    public const string DefaultVariant = "base";

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    public static string SkinFolder(string skinName) =>
        Path.Combine(RootPath, MapEditorSerialization.Sanitize(skinName));

    public static string VariantFolder(string skinName, string variantName) =>
        Path.Combine(SkinFolder(skinName), MapEditorSerialization.Sanitize(variantName));

    public static string ConfigPath(string skinName, string variantName) =>
        Path.Combine(VariantFolder(skinName, variantName), ConfigFile);

    public static bool SkinExists(string skinName) =>
        !string.IsNullOrWhiteSpace(skinName) && Directory.Exists(SkinFolder(skinName));

    public static bool VariantExists(string skinName, string variantName) =>
        !string.IsNullOrWhiteSpace(variantName) && Directory.Exists(VariantFolder(skinName, variantName));

    public static void EnsureRootFolder()
    {
        try
        {
            Directory.CreateDirectory(RootPath);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Follower skin folder could not be created: {e.Message}");
        }
    }

    public static string FreeSkinName(string stem = "untitledskin") =>
        FreeName(stem, SkinExists);

    public static string FreeVariantName(string skinName, string stem = "variant") =>
        FreeName(stem, v => VariantExists(skinName, v));

    private static string FreeName(string stem, Func<string, bool> taken)
    {
        if (string.IsNullOrWhiteSpace(stem)) stem = "untitled";
        stem = MapEditorSerialization.Sanitize(stem);
        if (!taken(stem)) return stem;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = stem + suffix;
            if (!taken(candidate)) return candidate;
        }

        return stem + Guid.NewGuid().ToString("N").Substring(0, 4);
    }

    public static List<string> ListSkins()
    {
        var names = new List<string>();
        if (!Directory.Exists(RootPath)) return names;

        foreach (var folder in Directory.GetDirectories(RootPath))
            names.Add(Path.GetFileName(folder));

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static List<string> ListVariants(string skinName)
    {
        var names = new List<string>();
        var folder = SkinFolder(skinName);
        if (!Directory.Exists(folder)) return names;

        foreach (var variant in Directory.GetDirectories(folder))
            names.Add(Path.GetFileName(variant));

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public const char PairSeparator = '/';

    public static string Pair(string skinName, string variantName) =>
        skinName + PairSeparator + variantName;

    public static bool TrySplitPair(string text, out string skinName, out string variantName)
    {
        skinName = null;
        variantName = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split(PairSeparator);
        if (parts.Length != 2) return false;
        if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) return false;

        skinName = MapEditorSerialization.Sanitize(parts[0].Trim());
        variantName = MapEditorSerialization.Sanitize(parts[1].Trim());
        return true;
    }

    public static string PairProblem(string text) =>
        TrySplitPair(text, out _, out _)
            ? null
            : "Write the name as skin" + PairSeparator + "variant, for example mycat" + PairSeparator + "base.";

    public static bool PairExists(string text) =>
        TrySplitPair(text, out var skin, out var variant) && VariantExists(skin, variant);

    public static List<string> ListPairs()
    {
        var pairs = new List<string>();
        foreach (var skin in ListSkins())
            foreach (var variant in ListVariants(skin))
                pairs.Add(Pair(skin, variant));

        return pairs;
    }

    public static List<string> ImageNames(string skinName, string variantName)
    {
        var names = new List<string>();
        var folder = VariantFolder(skinName, variantName);
        if (!Directory.Exists(folder)) return names;

        foreach (var file in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
            names.Add(Path.GetFileNameWithoutExtension(file));

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static string ImagePath(string skinName, string variantName, string imageName) =>
        Path.Combine(VariantFolder(skinName, variantName), imageName + ".png");

    public static string ToJson(FollowerSkinConfig config) =>
        JsonConvert.SerializeObject(config, Formatting.Indented);

    public static CTFollowerSkinDocument Load(string skinName, string variantName)
    {
        var path = ConfigPath(skinName, variantName);
        if (!File.Exists(path)) return null;

        try
        {
            var config = JsonConvert.DeserializeObject<FollowerSkinConfig>(File.ReadAllText(path));
            if (config == null) return null;
            config.PartConfigs ??= [];
            config.OverrideBaseSkin ??= "Cat";
            foreach (var part in config.PartConfigs.Values)
                if (part != null) part.ColorChoices ??= ["#FFFFFF"];

            var document = new CTFollowerSkinDocument
            {
                SkinName = Path.GetFileName(SkinFolder(skinName)),
                VariantName = Path.GetFileName(VariantFolder(skinName, variantName)),
                Config = config
            };
            document.NormaliseColourSets();
            return document;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Follower skin '{skinName}/{variantName}' could not be read: {e.Message}");
            return null;
        }
    }

    public static string Save(CTFollowerSkinDocument document)
    {
        if (document?.Config == null) return null;

        document.SkinName = MapEditorSerialization.Sanitize(document.SkinName);
        document.VariantName = MapEditorSerialization.Sanitize(document.VariantName);
        document.NormaliseColourSets();

        var path = ConfigPath(document.SkinName, document.VariantName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ToJson(document.Config));
            Plugin.Log.LogInfo($"Follower skin saved to {path}");
            return path;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Follower skin '{document.ShownName}' could not be saved: {e.Message}");
            return null;
        }
    }

    public static int CopyImages(string fromSkin, string fromVariant, string toSkin, string toVariant)
    {
        var from = VariantFolder(fromSkin, fromVariant);
        var to = VariantFolder(toSkin, toVariant);
        if (!Directory.Exists(from) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return 0;

        var copied = 0;
        try
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, "*.png", SearchOption.TopDirectoryOnly))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
                copied++;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Follower skin images could not be copied: {e.Message}");
        }

        return copied;
    }

    public static int CopyOtherVariants(string fromSkin, string toSkin, string exceptVariant)
    {
        var copied = 0;
        foreach (var variant in ListVariants(fromSkin))
        {
            if (string.Equals(variant, exceptVariant, StringComparison.OrdinalIgnoreCase)) continue;
            var source = VariantFolder(fromSkin, variant);
            var target = VariantFolder(toSkin, variant);
            try
            {
                Directory.CreateDirectory(target);
                foreach (var file in Directory.GetFiles(source, "*.*", SearchOption.TopDirectoryOnly))
                {
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                    copied++;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Variant '{variant}' could not be copied: {e.Message}");
            }
        }

        return copied;
    }
}
