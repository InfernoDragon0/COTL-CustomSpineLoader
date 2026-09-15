using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

public class CustomColorHelper
{
    //TODO: maybe, each part of the body can be a different color in the future
    public static Dictionary<int, CustomFollowerColor> CustomColors { get; private set; } = [];

    /// A multiplayer guest mirroring the host's followers: this machine's own records are set aside
    /// and nothing is written to disk until mirroring ends.
    public static bool Mirroring { get; private set; }
    private static Dictionary<int, CustomFollowerColor> _own;

    public static void LoadCustomColors(int saveSlot)
    {
        if (Mirroring) return;
        if (!File.Exists(Path.Combine(Plugin.PluginPath, $"CustomColors{saveSlot}.json")))
        {
            Plugin.Log.LogInfo("Creating new CustomColors.json file for save slot " + saveSlot + ".");
            var json = JsonConvert.SerializeObject(CustomColors, Formatting.Indented);
            File.WriteAllText(Path.Combine(Plugin.PluginPath, $"CustomColors{saveSlot}.json"), json);
            return;
        }
        var jsonLoaded = File.ReadAllText(Path.Combine(Plugin.PluginPath, $"CustomColors{saveSlot}.json"));
        CustomColors = JsonConvert.DeserializeObject<Dictionary<int, CustomFollowerColor>>(jsonLoaded) ?? [];

    }

    public static void SetMirroring(bool on)
    {
        if (on == Mirroring) return;
        Mirroring = on;
        if (on)
        {
            _own = CustomColors;
            CustomColors = [];
        }
        else
        {
            CustomColors = _own ?? [];
            _own = null;
        }
    }

    public static string Serialize(int id) =>
        CustomColors.TryGetValue(id, out var record) ? JsonConvert.SerializeObject(record) : null;

    /// Replaces a follower's record with one another machine serialised; null or empty removes it.
    public static bool Apply(int id, string json)
    {
        if (string.IsNullOrEmpty(json)) return CustomColors.Remove(id);

        var record = JsonConvert.DeserializeObject<CustomFollowerColor>(json);
        if (record == null) return false;
        record.FollowerId = id;
        CustomColors[id] = record;
        return true;
    }

    public static void SaveCustomColors()
    {
        if (Mirroring) return;
        var json = JsonConvert.SerializeObject(CustomColors, Formatting.Indented);
        File.WriteAllText(Path.Combine(Plugin.PluginPath, $"CustomColors{SaveAndLoad.SAVE_SLOT}.json"), json);
        Plugin.Log.LogInfo("Saved custom colors");
    }

    public static CustomFollowerColor GetCustomColor(int id)
    {
        return CustomColors.TryGetValue(id, out var color) ? color : null;
    }

    public static float GetCustomScale(int id)
    {
        return CustomColors.TryGetValue(id, out var color) ? color.scale : -1f;
    }

    public static void SetCustomColor(int id, float r, float g, float b, float a, float scale = 1f)
    {
        var color = new CustomFollowerColor(id, r, g, b, a, scale);
        CustomColors[id] = color;
        Plugin.Log.LogInfo($"Set custom color for follower {id} to ({r}, {g}, {b}, {a}) with scale {scale}");
    }

    public static void SetCustomCostume(int id, bool enabled, int FollowerClothingType, int FollowerSpecialType, int FollowerHatType, int FollowerOutfitType, int FollowerNecklaceType)
    {
        if (CustomColors.ContainsKey(id))
        {
            CustomColors[id].CustomFollowerCostume = enabled;
            CustomColors[id].FollowerClothingType = FollowerClothingType;
            CustomColors[id].FollowerSpecialType = FollowerSpecialType;
            CustomColors[id].FollowerHatType = FollowerHatType;
            CustomColors[id].FollowerOutfitType = FollowerOutfitType;
            CustomColors[id].FollowerNecklaceType = FollowerNecklaceType;
            Plugin.Log.LogInfo($"Set custom costume for follower {id} to ClothingType: {FollowerClothingType}, SpecialType: {FollowerSpecialType}, HatType: {FollowerHatType}, OutfitType: {FollowerOutfitType}");
        }
        else
        {
            Plugin.Log.LogWarning("Tried to set custom costume for follower " + id + " but no custom color exists. Enable Customization first.");
        }
    }

    /// Which wardrobe hat and clothes the follower wears, as FollowerWardrobe keys; null for none.
    public static void SetWardrobe(int id, string hat, string clothes)
    {
        if (!CustomColors.TryGetValue(id, out var record))
        {
            Plugin.Log.LogWarning("Tried to set wardrobe for follower " + id + " but no custom color exists. Enable Customization first.");
            return;
        }
        record.CustomHat = string.IsNullOrEmpty(hat) ? null : hat;
        record.CustomClothes = string.IsNullOrEmpty(clothes) ? null : clothes;
    }

    public static void RemoveCustomColor(int id)
    {
        if (CustomColors.ContainsKey(id))
        {
            CustomColors.Remove(id);
            Plugin.Log.LogInfo($"Removed custom color for follower {id}");
        }
    }
}

[Serializable]
public class CustomFollowerColor(int id, float r, float g, float b, float a, float scale = 1f)
{
    public int FollowerId { get; set; } = id;
    public float R { get; set; } = Mathf.Clamp(r, 0f, 1f);
    public float G { get; set; } = Mathf.Clamp(g, 0f, 1f);
    public float B { get; set; } = Mathf.Clamp(b, 0f, 1f);
    public float A { get; set; } = Mathf.Clamp(a, 0f, 1f);
    
    public bool CustomFollowerCostume = false;
    public int FollowerClothingType { get; set; } = 0;
    public int FollowerSpecialType { get; set; } = 0;
    public int FollowerHatType { get; set; } = 0;
    public int FollowerOutfitType { get; set; } = 0;

    public int FollowerNecklaceType { get; set; } = 0;

    public string CustomHat { get; set; }
    public string CustomClothes { get; set; }

    public float scale = Mathf.Clamp(scale, 0.1f, 5f);
}