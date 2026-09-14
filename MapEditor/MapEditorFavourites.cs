using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.CustomStructures;
using Newtonsoft.Json;

namespace CustomSpineLoader.MapEditor;

/// One pinned structure or prop in the form that survives a restart. A custom structure's
/// StructureBrain.TYPES is minted while the mod loads and differs between sessions, so the internal
/// NAME is what goes on disk and the type is resolved when the favourite is read back.
[Serializable]
public class FavouriteRecord
{
    public string Key = "";
    public string Label = "";
    public bool IsProp;
    public string Path = "";
    public string Structure = "";
}

/// The quick pick's pinned half. Favourites outlive the session and always hold a slot; the
/// recently-placed list fills whatever is left over.
public static class MapEditorFavourites
{
    public const string FileName = "EditorFavourites.json";

    private static string PathOnDisk => Path.Combine(Plugin.PluginPath, FileName);

    private static List<FavouriteRecord> _records;

    public static bool IsFavourite(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        Load();
        return Find(key) != null;
    }

    public static int Count
    {
        get
        {
            Load();
            return _records.Count;
        }
    }

    /// Returns the state the entry is in afterwards, so the caller can say which way it went.
    public static bool Toggle(QuickPickEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Key)) return false;
        Load();

        var found = Find(entry.Key);
        if (found != null)
        {
            _records.Remove(found);
            Write();
            return false;
        }

        _records.Add(new FavouriteRecord
        {
            Key = entry.Key,
            Label = entry.Label ?? "",
            IsProp = entry.IsProp,
            Path = entry.Path ?? "",
            Structure = entry.IsProp ? "" : Tools.StructureTool.InternalNameOf(entry.Type)
        });

        Write();
        return true;
    }

    /// Pinned entries in the order they were pinned. A custom structure whose mod is no longer
    /// installed is skipped rather than deleted, so reinstalling it brings the favourite back.
    public static List<QuickPickEntry> Entries()
    {
        Load();

        var entries = new List<QuickPickEntry>(_records.Count);
        foreach (var record in _records)
        {
            var entry = Resolve(record);
            if (entry != null) entries.Add(entry);
        }

        return entries;
    }

    private static QuickPickEntry Resolve(FavouriteRecord record)
    {
        if (record == null || string.IsNullOrEmpty(record.Key)) return null;

        if (record.IsProp)
            return string.IsNullOrEmpty(record.Path)
                ? null
                : new QuickPickEntry
                {
                    Key = record.Key,
                    Label = record.Label,
                    IsProp = true,
                    Path = record.Path
                };

        if (!Tools.StructureTool.TryResolveAnyType(record.Structure, out var type))
        {
            WarnOnce(record.Key, $"MapEditor: favourite {record.Structure} is not installed any more; " +
                                 "its slot is left to the recent list.");
            return null;
        }

        return new QuickPickEntry
        {
            Key = record.Key,
            Label = record.Label,
            Type = type,
            IsCustom = CustomStructureManager.CustomStructureList.ContainsKey(type)
        };
    }

    private static readonly HashSet<string> _warned = [];

    private static void WarnOnce(string key, string message)
    {
        if (!_warned.Add(key)) return;
        Plugin.Log.LogWarning(message);
    }

    private static FavouriteRecord Find(string key) =>
        _records.Find(r => r != null && string.Equals(r.Key, key, StringComparison.Ordinal));

    private static void Load()
    {
        if (_records != null) return;

        _records = [];
        try
        {
            if (!File.Exists(PathOnDisk)) return;

            var loaded = JsonConvert.DeserializeObject<List<FavouriteRecord>>(File.ReadAllText(PathOnDisk));
            if (loaded == null) return;

            foreach (var record in loaded)
                if (record != null && !string.IsNullOrEmpty(record.Key) && Find(record.Key) == null)
                    _records.Add(record);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not read {FileName}: {e.Message}");
        }
    }

    private static void Write()
    {
        try
        {
            File.WriteAllText(PathOnDisk, JsonConvert.SerializeObject(_records, Formatting.Indented));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not write {FileName}: {e.Message}");
        }
    }
}
