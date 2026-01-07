using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.CustomInventory;
using COTL_API.Helpers;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public class CustomItemLoader : Loader<CustomItemConfig>
{
    public static List<InventoryItem.ITEM_TYPE> loadedItems = []; 
    //might need to keep track of the name as well, dictionary, for custom items to be used as recipe items

    public CustomItemLoader() : base("CustomInventoryItems") { }

    public static void LoadAllCustomItems()
    {
        var loader = new CustomItemLoader();
        var entries = loader.LoadAll();

        foreach (var entry in entries)
        {
            var customItemJson = entry.Config;
            var customItemFolder = entry.FolderName;
            Plugin.Log.LogInfo("Found custom item folder: " + customItemFolder);

            var internalName = "CULT_TWEAKER_" + customItemJson.ItemName.ToUpper().Replace(" ", "_");

            Plugin.Log.LogInfo("Trying to create custom item : " + customItemJson.ItemName);
            var spritePath1 = Path.Combine(entry.FolderPath, customItemJson.SpritePath);
            if (!File.Exists(spritePath1))
            {
                Plugin.Log.LogError("Sprite file not found for item " + customItemJson.ItemName + " at path: " + spritePath1);
                continue;
            }

            Plugin.Log.LogInfo("Loading Sprite via " + spritePath1);

            try
            {
                CustomInventoryItem CustomItem = new CultTweakerCustomItem(
                    internalName,
                    customItemJson.ItemName,
                    customItemJson.ItemType,
                    customItemJson.CanBeRefined,
                    customItemJson.RefineryInputQty,
                    customItemJson.CustomRefineryDuration,
                    spritePath1,
                    customItemJson.FuelWeight,
                    customItemJson.FoodSatitation,
                    customItemJson.IsFish,
                    customItemJson.IsFood,
                    customItemJson.IsBigFish,
                    customItemJson.IsCurrency,
                    customItemJson.IsBurnableFuel,
                    customItemJson.CanBeGivenToFollower,
                    customItemJson.Lore,
                    customItemJson.Description,
                    customItemJson.AddItemToOfferingShrine,
                    customItemJson.AddItemToDungeonChests,
                    customItemJson.DungeonChestSpawnChance,
                    customItemJson.DungeonChestMinAmount,
                    customItemJson.DungeonChestMaxAmount
                );

                Plugin.Log.LogInfo("Successfully created custom item with internal name : " + CustomItem.InternalName);
                loadedItems.Add(CustomItemManager.Add(CustomItem));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to create custom item : " + customItemJson.ItemName);
                Plugin.Log.LogError(e);
            }
        }
    }
}

public class CultTweakerCustomItem(
    string internalName,
    string ItemName,
    int ItemType,
    bool CanBeRefined,
    int RefineryInputQty,
    float CustomRefineryDuration,
    string SpritePath,
    int FuelWeight,
    int FoodSatitation,
    bool IsFish,
    bool IsFood,
    bool IsBigFish,
    bool IsCurrency,
    bool IsBurnableFuel,
    bool CanBeGivenToFollower,
    string Lore,
    string Description,
    bool AddItemToOfferingShrine,
    bool AddItemToDungeonChests,
    int DungeonChestSpawnChance,
    int DungeonChestMinAmount,
    int DungeonChestMaxAmount) : CustomInventoryItem
{
    private readonly string _internalName = internalName;

    private readonly string _itemName = ItemName;
    private readonly int _itemType = ItemType;

    private readonly bool _canBeRefined = CanBeRefined;
    private readonly int _refineryInputQty = RefineryInputQty;
    private readonly float _customRefineryDuration = CustomRefineryDuration;

    private readonly string _spritePath = SpritePath;

    private readonly int _fuelWeight = FuelWeight;
    private readonly int _foodSatitation = FoodSatitation;

    private readonly bool _isFish = IsFish;
    private readonly bool _isFood = IsFood;
    private readonly bool _isBigFish = IsBigFish;
    private readonly bool _isCurrency = IsCurrency;
    private readonly bool _isBurnableFuel = IsBurnableFuel;
    private readonly bool _canBeGivenToFollower = CanBeGivenToFollower;

    private readonly string _lore = Lore;
    private readonly string _description = Description;
    private readonly bool _addItemToOfferingShrine = AddItemToOfferingShrine;
    private readonly bool _addItemToDungeonChests = AddItemToDungeonChests;
    private readonly int _dungeonChestSpawnChance = DungeonChestSpawnChance;
    private readonly int _dungeonChestMinAmount = DungeonChestMinAmount;
    private readonly int _dungeonChestMaxAmount = DungeonChestMaxAmount;

    public override string InternalName => _internalName;
    public override bool CanBeRefined => _canBeRefined;
    public override int RefineryInputQty => _refineryInputQty;
    public override float CustomRefineryDuration => _customRefineryDuration;

    public override Sprite InventoryIcon => TextureHelper.CreateSpriteFromPath(_spritePath);
    public override Sprite Sprite => TextureHelper.CreateSpriteFromPath(_spritePath);
    public override int FuelWeight => _fuelWeight;
    public override int FoodSatitation => _foodSatitation;

    public override bool IsFish => _isFish;
    public override bool IsFood => _isFood;
    public override bool IsBigFish => _isBigFish;
    public override bool IsCurrency => _isCurrency;
    public override bool IsBurnableFuel => _isBurnableFuel;
    public override bool CanBeGivenToFollower => _canBeGivenToFollower;

    public override string Name() => _itemName;
    public override string Lore() => _lore;
    public override string Description() => _description;
    public override bool AddItemToOfferingShrine => _addItemToOfferingShrine;
    public override bool AddItemToDungeonChests => _addItemToDungeonChests;
    public override int DungeonChestSpawnChance => _dungeonChestSpawnChance;
    public override int DungeonChestMinAmount => _dungeonChestMinAmount;
    public override int DungeonChestMaxAmount => _dungeonChestMaxAmount;
}

[Serializable]
public class CustomItemConfig
{
    public string ItemName; //spaces become underscores
    public int ItemType; //0 = ITEM, 1 = CURRENCY, 2 = FOOD

    public bool CanBeRefined = false;
    public int RefineryInputQty = 15;
    public float CustomRefineryDuration = 0f;
    
    public string SpritePath;

    public int FuelWeight = 1;
    public int FoodSatitation = 75;

    public bool IsFish = false;
    public bool IsFood = false;
    public bool IsBigFish = false;
    public bool IsCurrency = false;
    public bool IsBurnableFuel = false;
    public bool CanBeGivenToFollower = false;

    public string Lore = "Custom Item created with CultTweaker.";
    public string Description = "This is a custom item created with CultTweaker.";
    public bool AddItemToOfferingShrine = false;
    public bool AddItemToDungeonChests = false;
    public int DungeonChestSpawnChance = 100;
    public int DungeonChestMinAmount = 1;
    public int DungeonChestMaxAmount = 1;
    // public virtual InventoryItem.ITEM_CATEGORIES ItemCategory => InventoryItem.ITEM_CATEGORIES.NONE;
    // public virtual InventoryItem.ITEM_TYPE SeedType => InventoryItem.ITEM_TYPE.NONE;
    // public FollowerCommands GiftCommand => FollowerCommands.None;
    // public virtual InventoryItem.ITEM_TYPE RefineryInput { get; set; } = InventoryItem.ITEM_TYPE.LOG;
    //public virtual CustomItemManager.ItemRarity Rarity => CustomItemManager.ItemRarity.COMMON;

}

