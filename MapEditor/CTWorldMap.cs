using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class CTWorldMap
{
    public string MapName = "untitledworld";
    public string DisplayName = "";

    public string MusicEvent = "";
    public bool MusicLoop = true;

    public SerializableColor BackgroundColor = new() { R = 0.05f, G = 0.06f, B = 0.10f, A = 1f };

    public float ParallaxStrength = 1f;

    public List<CTWorldMapLayer> Layers = [];
    public List<CTWorldMapNode> Nodes = [];

    public CTWorldMapNode FindNode(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var node in Nodes)
            if (node != null && string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase))
                return node;
        return null;
    }

    public string ShownName => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : MapName;
}

public class CTWorldMapLayer
{
    public string Id = "";

    public string Kind = "Sprite";

    public string Asset = "";

    public SerializableVector3 Position = new();
    public SerializableVector3 Scale = new() { X = 1f, Y = 1f, Z = 1f };
    public float RotationZ;

    public bool FlipX;

    public int SortOrder;

    public float ParallaxDistance;

    public SerializableColor Tint = SerializableColor.From(Color.white);

    public string Animation = "";
    public string Skin = "";
    public bool Loop = true;
    public float TimeScale = 1f;

    public bool IsSpine => string.Equals(Kind, "Spine", StringComparison.OrdinalIgnoreCase);
    public bool IsSprite => string.Equals(Kind, "Sprite", StringComparison.OrdinalIgnoreCase);
}

public class CTWorldMapNode
{
    public string Id = "";
    public string DisplayName = "";

    public SerializableVector3 Position = new();
    public float Scale = 1f;

    public string Icon = "";

    public string NodeType = "Dungeon";

    public List<string> Children = [];

    public string InitialState = "Hidden";

    public int RequiredCompletedCount;
    public List<string> RequiredNodes = [];

    public int KeysGranted;

    public int KeysCost;

    public string TargetKind = "None";
    public string Target = "";

    public bool IsBase => string.Equals(NodeType, "Base", StringComparison.OrdinalIgnoreCase);
    public bool IsKey => string.Equals(NodeType, "Key", StringComparison.OrdinalIgnoreCase);
    public bool IsLock => string.Equals(NodeType, "Lock", StringComparison.OrdinalIgnoreCase);
}

public enum WorldNodeState
{
    Hidden,
    Preview,
    Selectable,
    Completed,
    Locked
}

public static class CTWorldMapSerialization
{
    public const string FolderName = "CustomWorldMaps";
    public const string ConfigFile = "config.json";

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    public static string FolderFor(string mapName) =>
        Path.Combine(RootPath, MapEditorSerialization.Sanitize(mapName));

    public static string FolderForRead(string mapName)
    {
        var own = FolderFor(mapName);
        if (File.Exists(Path.Combine(own, ConfigFile))) return own;

        return APIHelper.ModContentPaths.FindDirectory(FolderName,
            MapEditorSerialization.Sanitize(mapName), ConfigFile) ?? own;
    }

    public static string PathFor(string mapName) => Path.Combine(FolderFor(mapName), ConfigFile);

    public static bool Exists(string mapName) => File.Exists(PathFor(mapName));

    public static bool Available(string mapName) =>
        File.Exists(Path.Combine(FolderForRead(mapName), ConfigFile));

    public static string FreeName(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) stem = "untitledworld";
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
            Plugin.Log.LogWarning("MapEditor: could not create the world map folder: " + e.Message);
        }
    }

    public static string Save(CTWorldMap map)
    {
        if (map == null) return null;

        map.MapName = MapEditorSerialization.Sanitize(map.MapName);

        try
        {
            var folder = FolderFor(map.MapName);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var path = PathFor(map.MapName);
            File.WriteAllText(path, JsonConvert.SerializeObject(map, Formatting.Indented));
            Plugin.Log.LogInfo($"MapEditor: saved world map '{map.MapName}' with " +
                               $"{map.Layers.Count} layer(s) and {map.Nodes.Count} node(s) to {path}");
            return path;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: failed to save world map: " + e);
            return null;
        }
    }

    public static int CopyArt(string fromMapName, string toMapName)
    {
        if (string.IsNullOrWhiteSpace(fromMapName) || string.IsNullOrWhiteSpace(toMapName)) return 0;

        var from = FolderForRead(fromMapName);
        var to = FolderFor(toMapName);
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
            Plugin.Log.LogWarning($"MapEditor: could not copy the art of '{fromMapName}' to " +
                                  $"'{toMapName}': {e.Message}");
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

    public static string ToJson(CTWorldMap map)
    {
        if (map == null) return "";

        try
        {
            return JsonConvert.SerializeObject(map, Formatting.Indented);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not serialise the world map: " + e.Message);
            return "";
        }
    }

    public static CTWorldMap LoadByName(string mapName)
    {
        var folder = FolderForRead(mapName);
        var path = Path.Combine(folder, ConfigFile);

        try
        {
            if (!File.Exists(path)) return null;

            var map = JsonConvert.DeserializeObject<CTWorldMap>(File.ReadAllText(path));
            if (map == null) return null;

            map.MapName = Path.GetFileName(folder);
            DropUnknowns(map);
            return map;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"MapEditor: could not parse world map '{path}': {e.Message}");
            return null;
        }
    }

    public static List<string> ListNames()
    {
        var names = new List<string>();

        try
        {
            foreach (var folder in APIHelper.ModContentPaths.DirectoriesIn(FolderName))
                if (File.Exists(Path.Combine(folder, ConfigFile)))
                    names.Add(Path.GetFileName(folder));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: world map scan failed: " + e);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static List<CTWorldMap> LoadAll()
    {
        var results = new List<CTWorldMap>();
        foreach (var name in ListNames())
        {
            var map = LoadByName(name);
            if (map != null) results.Add(map);
        }
        return results;
    }

    private static void DropUnknowns(CTWorldMap map)
    {
        map.Layers.RemoveAll(layer =>
        {
            if (layer == null) return true;
            if (layer.IsSprite || layer.IsSpine) return false;
            Plugin.Log.LogWarning($"MapEditor: world map '{map.MapName}' layer '{layer.Id}' has " +
                                  $"unknown kind '{layer.Kind}' and was skipped.");
            return true;
        });

        map.Nodes.RemoveAll(node =>
        {
            if (node == null) return true;
            if (!string.IsNullOrWhiteSpace(node.Id)) return false;
            Plugin.Log.LogWarning($"MapEditor: world map '{map.MapName}' has a node without an " +
                                  "id; it was skipped.");
            return true;
        });
    }
}
