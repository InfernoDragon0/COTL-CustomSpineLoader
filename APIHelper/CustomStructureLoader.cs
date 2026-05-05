using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.CustomStructures;
using COTL_API.Helpers;
using CustomSpineLoader.SpineLoaderHelper;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public class CustomStructureLoader : Loader<CustomStructureConfig>
{
    public static List<StructureBrain.TYPES> loadedStructures = [];

    public CustomStructureLoader() : base("CustomStructures") { }

    public static void LoadAllCustomStructures()
    {
        var loader = new CustomStructureLoader();
        var entries = loader.LoadAll();

        foreach (var entry in entries)
        {
            var cfg = entry.Config;
            var folderName = entry.FolderName;
            Plugin.Log.LogInfo("Found custom structure folder: " + folderName);

            var internalName = "CULT_TWEAKER_STRUCTURE_" + cfg.StructureName.ToUpper().Replace(" ", "_");

            var spritePath = Path.Combine(entry.FolderPath, cfg.SpritePath ?? "");
            if (!File.Exists(spritePath))
            {
                Plugin.Log.LogError("Sprite file not found for structure " + cfg.StructureName + " at path: " + spritePath);
                continue;
            }

            Plugin.Log.LogInfo("Loading structure sprite via " + spritePath);

            try
            {
                var bounds = new Vector2Int((int)cfg.Bounds.X, (int)cfg.Bounds.Y);
                var buildingParts = new List<CustomStructureBuildingData>(); //we will do this later

                var itemCostList = new List<StructuresData.ItemCost>();
                foreach (var kvp in cfg.ItemCost)
                {
                    if (Enum.TryParse<InventoryItem.ITEM_TYPE>(kvp.Key, out var itemType))
                    {
                        var itemCost = new StructuresData.ItemCost(itemType, kvp.Value);
                        itemCostList.Add(itemCost);
                    }
                    else
                    {
                        Plugin.Log.LogError("Invalid item type in ItemCost: " + kvp.Key);
                    }
                }

                CultTweakerCustomStructure custom = new()
                {
                    _internalName = internalName,
                    _spritePath = spritePath,
                    _buildDurationMinutes = cfg.BuildDurationMinutes,
                    _buildOnlyOne = cfg.BuildOnlyOne,
                    _requiresTempleToBuild = cfg.RequiresTempleToBuild,
                    _canBeFlipped = cfg.CanBeFlipped,
                    _bounds = bounds,
                    _itemCost = itemCostList,
                    _buildingParts = buildingParts,
                    _structureNameTemp = cfg.StructureName,
                    _structureDescriptionTemp = cfg.StructureDescription
                };


                Plugin.Log.LogInfo("Successfully created custom structure with internal name : " + custom.InternalName);
                loadedStructures.Add(CustomStructureManager.Add(custom));

            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to create custom structure : " + cfg.StructureName);
                Plugin.Log.LogError(e);
            }
        }
    }
}

// string internalName,
    // string spritePath,
    // int buildDurationMinutes,
    // bool buildOnlyOne,
    // bool requiresTempleToBuild,
    // bool canBeFlipped,
    // Vector2Int bounds,
    // List<StructuresData.ItemCost> itemCost,
    // List<CustomStructureBuildingData> buildingParts
public class CultTweakerCustomStructure : CustomStructure
{
    public string _internalName = "EMPTY_CULTTWEAKER_CUSTOM_STRUCTURE";
    public string _spritePath = "";
    public string _structureNameTemp = "Nameless CultTweaker Structure";
    public string _structureDescriptionTemp = "No description provided.";
    public int _buildDurationMinutes = 30;
    public bool _buildOnlyOne = false;
    public bool _requiresTempleToBuild = true;
    public bool _canBeFlipped = true;
    public Vector2Int _bounds = new(1, 1);
    public List<StructuresData.ItemCost> _itemCost = [];

    public List<CustomStructureBuildingData> _buildingParts = [];

    //#########################################

    public override string InternalName => _internalName;
    public override Sprite Sprite => TextureHelper.CreateSpriteFromPath(_spritePath);
    public override List<CustomStructureBuildingData> BuildingParts => _buildingParts;

    public override int BuildDurationMinutes => _buildDurationMinutes;
    public override bool GetBuildOnlyOne() => _buildOnlyOne;
    public override bool RequiresTempleToBuild() => _requiresTempleToBuild;
    public override bool CanBeFlipped() => _canBeFlipped;
    public override Vector2Int Bounds => _bounds;
    public override List<StructuresData.ItemCost> Cost => _itemCost;

    public override string GetLocalizedDescription()
    {
        return _structureDescriptionTemp;
    }

    public override string GetLocalizedName()
    {
        return _structureNameTemp;
    }
}

public class CustomStructureConfig
{
    //TODO: localization
    public string StructureName;
    public string StructureDescription; //localization not supported yet!
    public string SpritePath;
    public List<StructureBuildingOverride> Overrides = [];

    public int BuildDurationMinutes = 30;

    public bool BuildOnlyOne = false;

    public bool RequiresTempleToBuild = true;

    public bool CanBeFlipped = true;
    public SerializableVector2 Bounds = new() { X = 1, Y = 1 };

    public Dictionary<string, int> ItemCost = []; //StructuresData.ItemCost of ITEM_TYPE to int CostValue


    // public virtual Type? Interaction => null;
    // public virtual Categories StructureCategories => Categories.CULT;
    // public virtual TypeAndPlacementObjects.Tier Tier => TypeAndPlacementObjects.Tier.Zero;

    // public virtual string LocalizedPros()
    // {
    //     return LocalizationManager.GetTranslation($"Structures/{ModPrefix}.{InternalName}/Pros");
    // }

    // public virtual string LocalizedCons()
    // {
    //     return LocalizationManager.GetTranslation($"Structures/{ModPrefix}.{InternalName}/Cons");
    // }

    // public virtual string GetLocalizedName()
    // {
    //     return LocalizationManager.GetTranslation($"Structures/{ModPrefix}.{InternalName}");
    // }

    // public virtual string GetLocalizedName(bool plural, bool withArticle, bool definite)
    // {
    //     var article = definite ? "/Definite" : "/Indefinite";

    //     var text = $"Structures/{ModPrefix}.{InternalName}{(plural ? "/Plural" : "")}{(!withArticle ? "" : article)}";
    //     return LocalizationManager.GetTranslation(text);
    // }

    // public virtual string GetLocalizedDescription()
    // {
    //     return LocalizationManager.GetTranslation($"Structures/{ModPrefix}.{InternalName}/Description");
    // }

    // public virtual string GetLocalizedLore()
    // {
    //     return LocalizationManager.GetTranslation($"Structures/{ModPrefix}.{InternalName}/Lore");
    // }
}