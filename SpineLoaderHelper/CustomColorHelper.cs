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
    public static Dictionary<int, CustomFollowerSpineSkin> CustomFollowerSkinConfigs { get; private set; } = [];
    public static void LoadCustomColors(int saveSlot)
    {
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

    public static void SaveCustomColors()
    {
        var json = JsonConvert.SerializeObject(CustomColors, Formatting.Indented);
        File.WriteAllText(Path.Combine(Plugin.PluginPath, $"CustomColors{SaveAndLoad.SAVE_SLOT}.json"), json);
        Plugin.Log.LogInfo("Saved custom colors");
    }

    public static CustomFollowerColor GetCustomColor(int id)
    {
        return CustomColors.TryGetValue(id, out var color) ? color : null;
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

    public float scale = Mathf.Clamp(scale, 0.1f, 5f);
}

[Serializable]
public class CustomFollowerSpineSkin
{
    public string SpineName;
    public List<string> SkinsApplied;
}