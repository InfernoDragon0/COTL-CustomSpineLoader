using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.CustomInventory;
using COTL_API.Helpers;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public class CustomMealLoader : Loader<CustomMealConfig>
{
    public static List<InventoryItem.ITEM_TYPE> loadedMeals = [];

    public CustomMealLoader() : base("CustomMeals") { }

    public static void LoadAllCustomMeals()
    {
        var loader = new CustomMealLoader();
        var entries = loader.LoadAll();

        foreach (var entry in entries)
        {
            var cfg = entry.Config;
            var folder = entry.FolderName;
            Plugin.Log.LogInfo("Found custom meal folder: " + folder);

            var internalName = "CULT_TWEAKER_MEAL_" + (cfg.ItemName ?? "UNKNOWN").ToUpper().Replace(" ", "_");

            Plugin.Log.LogInfo("Trying to create custom meal : " + cfg.ItemName);

            var spritePath = string.IsNullOrEmpty(cfg.SpritePath) ? string.Empty : Path.Combine(entry.FolderPath, cfg.SpritePath);
            if (!string.IsNullOrEmpty(spritePath) && !File.Exists(spritePath))
            {
                Plugin.Log.LogWarning("Sprite file not found for meal " + cfg.ItemName + " at path: " + spritePath + ", skipping sprite (this may be optional)");
            }

            try
            {
                var meal = new CultTweakerCustomMeal(internalName, cfg, spritePath);
                Plugin.Log.LogInfo("Successfully created custom meal with internal name : " + meal.InternalName);

                loadedMeals.Add(CustomItemManager.Add(meal));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to create custom meal : " + cfg.ItemName);
                Plugin.Log.LogError(e.ToString());
            }
        }
    }
}

[Serializable]
public class CustomMealConfig : CustomItemConfig
{
    // 0..3
    public int SatiationLevel = 0;
    // 0.0..1.0
    public float TummyRating = 0f;
    // BAD, NORMAL, GOOD
    public string MealQuality = "NORMAL";
    public bool MealSafeToEat = true;

    // Recipe represented as dictionary: item internal name -> quantity
    public Dictionary<string, int> Recipe = [];

    // Meal effects mapping (effect name -> chance percent)
    public Dictionary<string, int> MealEffectsDictionary = [];
}

// Minimal custom meal implementation that reads values from config.
public class CultTweakerCustomMeal(string internalName, CustomMealConfig cfg, string spritePath) : CustomMeal
{
    private readonly string _internalName = internalName;
    private readonly string _mealName = cfg.ItemName ?? internalName;
    private readonly string _spritePath = string.IsNullOrEmpty(spritePath) ? cfg.SpritePath : spritePath;

    private readonly int _satiationLevel = cfg.SatiationLevel;
    private readonly float _tummyRating = cfg.TummyRating;
    private readonly bool _safeToEat = cfg.MealSafeToEat;
    private readonly string _mealQualityString = cfg.MealQuality ?? "NORMAL";

    private readonly Dictionary<string, int> _recipe = cfg.Recipe ?? [];
    private readonly Dictionary<string, int> _effects = cfg.MealEffectsDictionary ?? [];

    private readonly string _lore = cfg.Lore ?? "Custom Meal created with CultTweaker.";
    private readonly string _description = cfg.Description ?? "This is a custom meal created with CultTweaker.";

    public override string InternalName => _internalName;
    public override Sprite InventoryIcon => TextureHelper.CreateSpriteFromPath(_spritePath);
    public override Sprite Sprite => TextureHelper.CreateSpriteFromPath(_spritePath);

    public override string Name() => _mealName;
    public override string Lore() => _lore;
    public override string Description() => _description;

    public override int SatiationLevel => _satiationLevel;
    public override float TummyRating => _tummyRating;
    public override bool MealSafeToEat => _safeToEat;

    public override MealQuality Quality
    {
        get
        {
            try
            {
                return (MealQuality)Enum.Parse(typeof(MealQuality), _mealQualityString, true);
            }
            catch
            {
                return MealQuality.NORMAL;
            }
        }
    }

    public override List<List<InventoryItem>> Recipe
    {
        get
        {
            var list = new List<List<InventoryItem>>();
            var innerList = new List<InventoryItem>();
            foreach (var kv in _recipe)
            {
                try
                {
                    var itemType = (InventoryItem.ITEM_TYPE)Enum.Parse(typeof(InventoryItem.ITEM_TYPE), kv.Key, true);
                    innerList.Add(new InventoryItem(itemType, kv.Value));
                }
                catch
                {
                    Plugin.Log.LogWarning("Unknown recipe item type: " + kv.Key + " for meal " + InternalName);
                }
            }
            list.Add(innerList);
            return list;
        }
    }

    public override CookingData.MealEffect[] MealEffects
    {
        get
        {
            var outList = new List<CookingData.MealEffect>();
            foreach (var kv in _effects)
            {
                try
                {
                    var effectType = (CookingData.MealEffectType)Enum.Parse(typeof(CookingData.MealEffectType), kv.Key, true);
                    outList.Add(new CookingData.MealEffect { MealEffectType = effectType, Chance = kv.Value });
                }
                catch
                {
                    Plugin.Log.LogWarning("Unknown meal effect type: " + kv.Key + " for meal " + InternalName);
                }
            }

            return [.. outList];
        }
    }
}