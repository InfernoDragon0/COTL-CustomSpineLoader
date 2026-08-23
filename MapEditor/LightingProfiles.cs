using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CustomSpineLoader.MapEditor;

[Serializable]
public class LightingProfile
{
    public string Name = "";
    public MapLightingData Data = new();
}

// Named lighting looks in one flat LightingProfiles.json, shared across maps.
public static class LightingProfiles
{
    public const string FileName = "LightingProfiles.json";

    private static string PathOnDisk => Path.Combine(Plugin.PluginPath, FileName);

    private static List<LightingProfile> _profiles;

    public static List<string> Names()
    {
        Load();
        var names = new List<string>(_profiles.Count);
        foreach (var profile in _profiles) names.Add(profile.Name);
        return names;
    }

    public static bool Exists(string name) => Find(name) != null;

    public static LightingProfile Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        Load();
        return _profiles.Find(p =>
            string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // Upsert. Stores a clone (map edits must not rewrite the profile) with Enabled forced on
    // (else a profile saved while following the biome applies as a no-op).
    public static void Save(string name, MapLightingData data)
    {
        if (string.IsNullOrWhiteSpace(name) || data == null) return;
        Load();

        var clone = Clone(data);
        clone.Enabled = true;

        var existing = Find(name);
        if (existing != null) existing.Data = clone;
        else _profiles.Add(new LightingProfile { Name = name.Trim(), Data = clone });

        Write();
    }

    public static bool Delete(string name)
    {
        var found = Find(name);
        if (found == null) return false;

        _profiles.Remove(found);
        Write();
        return true;
    }

    public static MapLightingData Clone(MapLightingData data) =>
        JsonConvert.DeserializeObject<MapLightingData>(JsonConvert.SerializeObject(data));

    private static void Load()
    {
        if (_profiles != null) return;

        _profiles = [];
        try
        {
            if (!File.Exists(PathOnDisk)) return;

            var loaded = JsonConvert.DeserializeObject<List<LightingProfile>>(File.ReadAllText(PathOnDisk));
            if (loaded == null) return;

            foreach (var profile in loaded)
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Name) && profile.Data != null)
                    _profiles.Add(profile);
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
            File.WriteAllText(PathOnDisk, JsonConvert.SerializeObject(_profiles, Formatting.Indented));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not write {FileName}: {e.Message}");
        }
    }
}
