using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.CustomStructures;
using COTL_API.Helpers;
using CustomSpineLoader.SpineLoaderHelper;
using Spine.Unity;
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

            var spritePath = string.IsNullOrEmpty(cfg.SpritePath)
                ? "" : Path.Combine(entry.FolderPath, cfg.SpritePath);
            var hasSprite = spritePath.Length > 0 && File.Exists(spritePath);

            // A spine structure still wants a sprite - it is the build menu's icon - but it is no
            // longer what stands in the world, so a missing one is a warning and a placeholder
            // rather than a dropped structure.
            if (!hasSprite)
            {
                if (cfg.Spine == null)
                {
                    Plugin.Log.LogError("Sprite file not found for structure " + cfg.StructureName + " at path: " + spritePath);
                    continue;
                }

                Plugin.Log.LogWarning("No build-menu icon for structure " + cfg.StructureName +
                                      "; using the placeholder icon.");
                spritePath = "";
            }
            else
            {
                Plugin.Log.LogInfo("Loading structure sprite via " + spritePath);
            }

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
                    _structureDescriptionTemp = cfg.StructureDescription,
                    SpineConfig = cfg.Spine
                };

                if (cfg.Spine != null)
                {
                    // The icon lives in the same folder as the atlas pages, so it is kept out of
                    // the texture auto-discovery - otherwise it would be loaded as an atlas page
                    // and the skeleton would render blank.
                    custom.SpineData = SpineFolderLoader.Build(entry.FolderPath, internalName,
                        cfg.Spine.SkeletonPath, cfg.Spine.AtlasPath, cfg.Spine.TexturePaths,
                        cfg.Spine.SkeletonScale, cfg.Spine.ShaderName,
                        hasSprite ? [spritePath] : null);

                    if (custom.SpineData == null)
                        Plugin.Log.LogError("Structure " + cfg.StructureName + " asks for a spine, but its " +
                                            "folder has no skeleton .json, .atlas and .png set; it will use its sprite.");
                }


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

    // Set by the loader when the folder ships a spine: what actually stands in the world, in
    // place of the sprite. Null keeps the plain sprite behaviour.
    public SkeletonDataAsset SpineData;
    public StructureSpineConfig SpineConfig;

    private Sprite _sprite;

    //#########################################

    public override string InternalName => _internalName;

    // Cached: the getter is asked for the build-menu icon and again for every instantiation, and
    // each call used to decode the PNG from disk again. An empty path falls through to COTL_API's
    // placeholder, which is the spine case - the world object is the skeleton either way.
    public override Sprite Sprite =>
        _sprite ??= string.IsNullOrEmpty(_spritePath) ? base.Sprite : TextureHelper.CreateSpriteFromPath(_spritePath);
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

    // Null (or absent) = the plain sprite structure. Present = the sprite is only the build-menu
    // icon and a Spine skeleton from this folder is what gets built.
    public StructureSpineConfig Spine;

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

// The "Spine" block of a structure's config.json. Every path is relative to the structure's own
// folder, and every one of them is optional: the skeleton is the folder's one non-config .json,
// the atlas its one .atlas, and the pages its .png files apart from the build-menu icon.
public class StructureSpineConfig
{
    public string SkeletonPath = "";
    public string AtlasPath = "";
    public string[] TexturePaths = [];

    // Which skin of the skeleton to dress it in; empty uses the skeleton's default skin.
    public string SkinName = "";

    // Which animation to loop once it is placed; empty holds the setup pose, which is what a
    // static prop wants.
    public string Animation = "";
    public bool Loop = true;

    // Spine's import scale. 0.005 is what the game's own skeletons use, so it is the default
    // here too; Scale below is for nudging one structure, this is for art authored at a
    // different unit size.
    public float SkeletonScale = 0.005f;

    public string ShaderName = "Spine/Skeleton";

    public SerializableVector3 Offset;
    public SerializableVector3 Scale;

    // The sprite COTL_API paints onto the building prefab is switched off once the skeleton is
    // up. Set false to keep it, e.g. a painted base under an animated skeleton.
    public bool HideSprite = true;
}
