using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using COTL_API.CustomSkins;
using COTL_API.Helpers;
using Newtonsoft.Json;
using Spine;
using Spine.Unity;
using Spine.Unity.AttachmentTools;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

public class FollowerSpineLoader
{
    public static Dictionary<string, List<Tuple<int, string, Texture2D, FollowerSkinPartConfig>>> FollowerSkinOverrides = []; //name of skin, list of (slot name, part name, texture)
    public static Dictionary<string, List<WorshipperData.SlotsAndColours>> FollowerSlotColors = [];
    public static Dictionary<string, Skin> CustomFollowerSkins = [];

    public static void LoadAllFollowerSpines(Material material = null)
    {
        //get the plugin path, then find the foler FollowerSkins in it
        var followerFolder = Path.Combine(Plugin.PluginPath, "FollowerSpines");
        //check if the folder exists
        if (!Directory.Exists(followerFolder))
            Directory.CreateDirectory(followerFolder);

        //get each folder inside the directory
        var folders = Directory.GetDirectories(followerFolder);

        foreach (var folder in folders)
        {
            var followerSpineName = Path.GetFileName(folder);

            var spineSkeleton = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Where(x => !x.Contains("config")).ToArray();
            var spineTextures = Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly);
            var spineAtlas = Directory.GetFiles(folder, "*.atlas", SearchOption.TopDirectoryOnly);
            var config = Directory.GetFiles(folder, "config.json", SearchOption.TopDirectoryOnly);

            List<string> defaultSkinName = ["Cat"];
            var skinList = new string[0];

            if (config.Length > 0)
            {
                var configJson = new TextAsset(File.ReadAllText(config[0]));
                var configObj = JsonConvert.DeserializeObject<FollowerSpineConfig>(configJson.text);
                if (configObj != null)
                {
                    defaultSkinName = configObj.DefaultSkin;
                    skinList = configObj.Skins;
                    Plugin.Log.LogInfo($"Using default skin: {defaultSkinName}");
                    Plugin.Log.LogInfo($"Using skin list: {string.Join(", ", skinList)}");
                }
            }

            if (spineSkeleton.Length > 0 && spineTextures.Length > 0 && spineAtlas.Length > 0)
            {
                Plugin.Log.LogInfo("Reading atlas from " + spineAtlas[0]);
                var atlasTxt = new TextAsset(File.ReadAllText(spineAtlas[0]));

                Plugin.Log.LogInfo("Reading skeleton from " + spineSkeleton[0]);
                var skele = new TextAsset(File.ReadAllText(spineSkeleton[0]));
                var textures = new Texture2D[spineTextures.Length];

                foreach (var textureFile in spineTextures)
                {
                    Plugin.Log.LogInfo("Reading texture from " + textureFile);
                    Texture2D tex = TextureHelper.CreateTextureFromPath(textureFile);
                    tex.name = Path.GetFileNameWithoutExtension(textureFile);
                    textures[Array.IndexOf(spineTextures, textureFile)] = tex;
                }

                var mat = material ?? new Material(Shader.Find("Spine/Skeleton")); //TODO: find out what shader cotl uses
                var runtimeAtlasAsset = Spine.Unity.SpineAtlasAsset.CreateRuntimeInstance(atlasTxt, textures, mat, true);
                var runtimeSkeletonAsset = Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance(skele, runtimeAtlasAsset, true, 0.005f);
                Plugin.Log.LogInfo("Creating skeleton for " + followerSpineName);
                Plugin.Log.LogInfo("Using material name " + mat.name);
                CustomSkinManager.AddFollowerSpine(followerSpineName, runtimeSkeletonAsset);

                //apply the default skin, init each layer of the skin in order of defaultSkin string list
                //new skin("custom skin")
                for (int i = 0; i < defaultSkinName.Count; i++)
                {
                    var skinToApply = defaultSkinName[i];

                }

            }
            else
            {
                Plugin.Log.LogInfo($"Failed to load follower skin {followerSpineName}, ensure that the folder contains at least one of each .json, .png and .atlas file.");
            }

        }
    }

    public static void LoadAllNonSpineSkins()
    {
        //get the plugin path, then find the foler FollowerSkins in it
        var followerFolder = Path.Combine(Plugin.PluginPath, "FollowerSkins");
        //check if the folder exists
        if (!Directory.Exists(followerFolder))
            Directory.CreateDirectory(followerFolder);

        //get each folder inside the directory
        var folders = Directory.GetDirectories(followerFolder);

        //each png file represents a single part to override...?


        foreach (var folder in folders)
        {
            var followerSkinName = Path.GetFileName(folder);
            Plugin.Log.LogInfo("Loading Follower Override Skin: " + followerSkinName);
            List<string> completedSkins = [];

            //name of the variant file does not matter. each variant has their own config
            foreach (var variant in Directory.GetDirectories(folder))
            {
                Plugin.Log.LogInfo("Creating Variant: " + Path.GetFileName(variant));
                var overrides = Directory.GetFiles(variant, "*.png", SearchOption.TopDirectoryOnly);
                var config = Directory.GetFiles(variant, "config.json", SearchOption.TopDirectoryOnly);
                Plugin.Log.LogInfo("Variant has a total of " + overrides.Length + " overrides.");
                if (config.Length > 0)
                {
                    var configJson = new TextAsset(File.ReadAllText(config[0]));
                    var configObj = JsonConvert.DeserializeObject<FollowerSkinConfig>(configJson.text);
                    if (configObj == null)
                    {
                        Plugin.Log.LogWarning("Failed to deserialize config.json for follower skin " + followerSkinName + " variant: " + variant + ", please check syntax.");
                        continue;
                    }
                    Plugin.Log.LogInfo("Variant will be created using base skin: " + configObj.OverrideBaseSkin);

                    List<Tuple<int, string, Texture2D, FollowerSkinPartConfig>> skinOverrideList = []; //slot name, part name, texture
                    foreach (var textureFile in overrides)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(textureFile);
                        if (!configObj.PartConfigs.ContainsKey(fileName))
                        {
                            Plugin.Log.LogWarning($"{followerSkinName} variant: {variant} - {fileName} is not registered as a part in the config.json! Ignoring...");
                            continue;
                        }

                        var partConfig = configObj.PartConfigs[fileName];

                        Plugin.Log.LogInfo("Reading texture from " + Path.GetFileName(textureFile));
                        Texture2D tex = TextureHelper.CreateTextureFromPath(textureFile);
                        tex.name = Path.GetFileNameWithoutExtension(textureFile);
                        skinOverrideList.Add(new(partConfig.SlotIndex, partConfig.PartName, tex, partConfig));
                    }
                    Plugin.Log.LogInfo(followerSkinName + " variant " + variant + " has a total of " + overrides.Length + " and " + skinOverrideList.Count + " were registered successfully.");
                    FollowerSkinOverrides.Add(followerSkinName + "_" + Path.GetFileName(variant), skinOverrideList);
                    FollowerSlotColors.Add(followerSkinName + "_" + Path.GetFileName(variant), BuildColorsByIndex(configObj));
                    var skinname = BuildCustomOverrideSkin(followerSkinName + "_" + Path.GetFileName(variant), configObj.OverrideBaseSkin);

                    if (skinname != null)
                        completedSkins.Add(skinname);
                }
                else
                {
                    Plugin.Log.LogWarning("No config.json found for follower skin " + followerSkinName + " variant: " + variant + ", please create one.");
                    continue;
                }


                //create texture file from each overrides png


                //each PNG is a separate part to override, and the scales and rotations can be set in the config file
            }
            if (completedSkins.Count > 0)
            {
                Plugin.Log.LogInfo("Creating Follower Skin " + followerSkinName + " with " + completedSkins.Count + " variant(s).");
                CreateNewFollowerType(completedSkins[0], completedSkins, FollowerSlotColors[completedSkins[0]]); //TODO: all variants to add one function before
            }

            // if (spineSkeleton.Length > 0 && spineTextures.Length > 0 && spineAtlas.Length > 0)
            // {
            //     Plugin.Log.LogInfo("Reading atlas from " + spineAtlas[0]);
            //     var atlasTxt = new TextAsset(File.ReadAllText(spineAtlas[0]));

            //     Plugin.Log.LogInfo("Reading skeleton from " + spineSkeleton[0]);
            //     var skele = new TextAsset(File.ReadAllText(spineSkeleton[0]));
            //     var textures = new Texture2D[spineTextures.Length];

            //     foreach (var textureFile in spineTextures)
            //     {
            //         Plugin.Log.LogInfo("Reading texture from " + textureFile);
            //         Texture2D tex = TextureHelper.CreateTextureFromPath(textureFile);
            //         tex.name = Path.GetFileNameWithoutExtension(textureFile);
            //         textures[Array.IndexOf(spineTextures, textureFile)] = tex;
            //     }

            //     var mat = material ?? new Material(Shader.Find("Spine/Skeleton")); //TODO: find out what shader cotl uses
            //     var runtimeAtlasAsset = Spine.Unity.SpineAtlasAsset.CreateRuntimeInstance(atlasTxt, textures, mat, true);
            //     var runtimeSkeletonAsset = Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance(skele, runtimeAtlasAsset, true, 0.005f);
            //     Plugin.Log.LogInfo("Creating skeleton for " + followerSpineName);
            //     Plugin.Log.LogInfo("Using material name " + mat.name);
            //     CustomSkinManager.AddFollowerSpine(followerSpineName, runtimeSkeletonAsset);

            //     //apply the default skin, init each layer of the skin in order of defaultSkin string list
            //     //new skin("custom skin")
            //     for (int i = 0; i < defaultSkinName.Count; i++)
            //     {
            //         var skinToApply = defaultSkinName[i];

            //     }

            // }

        }
    }

    //CustomSpineLoader.SpineLoaderHelper.FollowerSpineLoader.ApplyOverridesToFollower(
    // FollowerManager.FindFollowerByID(266),
    // "CTTemplateSkin_base")

    public static string BuildCustomOverrideSkin(string skinVariantName, string baseSkinName)
    {
        if (CustomFollowerSkins.ContainsKey(skinVariantName))
        {
            Plugin.Log.LogWarning($"{skinVariantName} has already been built!");
            return null;
        }

        var skinData = FollowerSkinOverrides[skinVariantName];
        var baseSkin = WorshipperData.Instance.SkeletonData.Skeleton.Data.FindSkin(baseSkinName);

        if (baseSkin == null)
        {
            Plugin.Log.LogWarning($"Could not find base skin {baseSkinName} for variant {skinVariantName}! Defaulting to Cat.");
            baseSkin = WorshipperData.Instance.SkeletonData.Skeleton.Data.FindSkin("Cat");
        }
        var finalSkin = new Skin(skinVariantName);

        Plugin.Log.LogInfo($"Attempting to build skin with {skinData.Count} override parts for variant {skinVariantName}");

        //copy base to final skin
        baseSkin.Attachments.ToList().ForEach(attachment => { finalSkin.SetAttachment(attachment.SlotIndex, attachment.Name, attachment.attachment.Copy()); });
        baseSkin.Bones.ToList().ForEach(finalSkin.Bones.Add);
        baseSkin.Constraints.ToList().ForEach(finalSkin.Constraints.Add);

        foreach (var skinOverride in skinData)
        {
            try
            {
                //build atlas per image provided
                skinOverride.Item3.name = skinVariantName + "_" + skinOverride.Item2;
                Material mat = new(Shader.Find("Spine/Skeleton"))
                {
                    mainTexture = skinOverride.Item3
                };

                Material[] mats = [mat];
                var atlasAsset = SpineAtlasAsset.CreateRuntimeInstance( //TODO: build this first, then cache
                    GenerateAtlasText(
                        skinVariantName + "_" + skinOverride.Item2,
                        skinOverride.Item2,
                        skinOverride.Item3.width.ToString(),
                        skinOverride.Item3.height.ToString()),
                        mats,
                        true);

                var baseAttachment = baseSkin.GetAttachment(skinOverride.Item1, skinOverride.Item2);

                if (baseAttachment == null) continue;
                baseAttachment = baseAttachment.Copy();

                var translationX = skinOverride.Item4.OffsetX;
                var translationY = skinOverride.Item4.OffsetY;
                var rotation = skinOverride.Item4.Rotation;
                var scaleX = skinOverride.Item4.ScaleX;
                var scaleY = skinOverride.Item4.ScaleY;

                switch (baseAttachment)
                {
                    case MeshAttachment meshAttachment:
                        {
                            float minX = int.MaxValue;
                            float maxX = int.MinValue;
                            float minY = int.MaxValue;
                            float maxY = int.MinValue;

                            for (var j = 0; j < meshAttachment.Vertices.Length; j++)
                                switch (j % 3)
                                {
                                    case 0:
                                        minY = Math.Min(minY, meshAttachment.Vertices[j]);
                                        maxY = Math.Max(maxY, meshAttachment.Vertices[j]);
                                        break;
                                    case 1:
                                        minX = Math.Min(minX, meshAttachment.Vertices[j]);
                                        maxX = Math.Max(maxX, meshAttachment.Vertices[j]);
                                        break;
                                }

                            var diffX = maxX - minX;
                            var diffY = maxY - minY;

                            var centerX = minX + diffX / 2.0f;
                            var centerY = minY + diffY / 2.0f;

                            var regionAttachment = new RegionAttachment(skinOverride.Item2);
                            regionAttachment.SetRegion(atlasAsset.GetAtlas().regions[0]);

                            regionAttachment.X = centerY - translationY;
                            regionAttachment.Y = centerX - translationX;
                            regionAttachment.rotation = rotation;
                            regionAttachment.ScaleX = scaleX;
                            regionAttachment.ScaleY = scaleY;
                            regionAttachment.Width = diffX;
                            regionAttachment.Height = diffY;
                            finalSkin.SetAttachment(skinOverride.Item1, skinOverride.Item2, regionAttachment);
                            break;

                        }
                    case RegionAttachment regionAttachment:
                        regionAttachment.Name = skinVariantName + "_" + skinOverride.Item2;
                        atlasAsset.GetAtlas().regions[0].name = "Custom" + atlasAsset.GetAtlas().regions[0].name;

                        regionAttachment.SetRegion(atlasAsset.GetAtlas().regions[0]);

                        regionAttachment.X += translationX;
                        regionAttachment.Y += translationY;
                        regionAttachment.ScaleX = scaleX;
                        regionAttachment.ScaleY = scaleY;
                        regionAttachment.rotation = rotation;

                        finalSkin.SetAttachment(skinOverride.Item1, skinOverride.Item2, regionAttachment);
                    break;
                default:
                    Plugin.Log.LogWarning(
                        $"Attachment {baseAttachment.Name} is not a MeshAttachment or RegionAttachment, skipping...");
                    break;
                }
                Plugin.Log.LogInfo("Attached override part " + skinOverride.Item2 + " to slot " + skinOverride.Item1 + " for skin variant " + skinVariantName);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Failed to apply override part " + skinOverride.Item2 + " to skin variant " + skinVariantName + ": " + ex.Message);
                return null;
            }
        }
        Plugin.Log.LogInfo("Successfully created skin variant " + skinVariantName);
        
        var repackedSkin = finalSkin.GetRepackedSkin(skinVariantName, WorshipperData.Instance.SkeletonData.SkeletonDataAsset.atlasAssets[0].PrimaryMaterial, out var one, out var two);
        CustomFollowerSkins.Add(skinVariantName, repackedSkin);
        DataManager.SetFollowerSkinUnlocked(skinVariantName);

        return skinVariantName;
    }
    
    public static void CreateNewFollowerType(string name, List<string> variantNames,
        List<WorshipperData.SlotsAndColours> colors,
        bool hidden = false, bool twitchPremium = false, bool invariant = false)
    {
        var skins = variantNames.Select(v => new WorshipperData.CharacterSkin
        {
            Skin = v
        }).ToList();


        WorshipperData.Instance.Characters.Add(new WorshipperData.SkinAndData
        {
            Title = name,
            Skin = skins,
            SlotAndColours = colors,
            TwitchPremium = twitchPremium,
            _hidden = hidden,
            _dropLocation = WorshipperData.DropLocation.Other,
            _invariant = invariant
        });
    }

    public static TextAsset GenerateAtlasText(string filename, string name, string width, string height)
    {
        StringBuilder sb = new();
        sb.AppendLine();
        sb.AppendLine($"{filename}");
        sb.AppendLine($"size: {width}, {height}");
        sb.AppendLine("format: RGBA8888");
        sb.AppendLine("filter: Linear,Linear");
        sb.AppendLine("repeat: none");

        sb.AppendLine($"{name}");
        sb.AppendLine("  rotate: false");
        sb.AppendLine($"  xy: 0,0");
        sb.AppendLine($"  size: {width},{height}");
        sb.AppendLine($"  orig: {width},{height}");
        sb.AppendLine("  offset: 0,0");
        sb.AppendLine("  index: -1");
        return new TextAsset(sb.ToString());
    }

    public static List<WorshipperData.SlotsAndColours> BuildColorsByIndex(FollowerSkinConfig config)
    {
        var parts = config.PartConfigs.Values
            .Where(p => p.ColorChoices?.Any() == true)
            .ToList();

        int count = parts.Min(p => p.ColorChoices.Count);

        if (count == 0)
        {
            return [
                new()
                {
                    SlotAndColours =
                    [
                        new WorshipperData.SlotAndColor("ARM_LEFT_SKIN", new Color(1, 1, 1)),
                        new WorshipperData.SlotAndColor("ARM_RIGHT_SKIN", new Color(1, 1, 1)),
                        new WorshipperData.SlotAndColor("LEG_LEFT_SKIN", new Color(1, 1, 1)),
                        new WorshipperData.SlotAndColor("LEG_RIGHT_SKIN", new Color(1, 1, 1)),
                        new WorshipperData.SlotAndColor("Body_Naked", new Color(1, 1, 1)),
                        new WorshipperData.SlotAndColor("Body_Naked_Up", new Color(1, 1, 1)),
                        new WorshipperData.SlotAndColor("BODY_BTM", new Color(1, 1, 1)),
                        new WorshipperData.SlotAndColor("BODY_BTM_UP", new Color(1, 1, 1)),
                        new WorshipperData.SlotAndColor("BODY_TOP", new Color(1, 1, 1)),
                    ]
                }
            ];
        }

        return [.. Enumerable.Range(0, count)
            .Select(i => new WorshipperData.SlotsAndColours
            {
                SlotAndColours = [.. parts.Select(p =>
                    new WorshipperData.SlotAndColor(p.PartName, HexToColor(p.ColorChoices[i]))
                )]
            })];
    }

    public static Color HexToColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var color);
        return color;
    }

 
}

public class FollowerSpineConfig
{
    //default skins to initialize layered on load
    public List<string> DefaultSkin { get; set; }
    public string[] Skins { get; set; }

    //if it should be initialized with the original base skin, like clothes and necklaces
    public bool InitializeWithoutBase { get; set; } = true;
}

//the json looks like this:
    /* {
        partConfigs: {
            nameOfImage: {
                    "slotName": "HEAD",
                    "partName": "HEAD",
                    "scaleX": 1.0,
                    "scaleY": 1.0,
                    "rotation": 0.0,
                    "offsetX": 0.0,
                    "offsetY": 0.0,
                    "colorChoices": ["#FF0000", "#00FF00", "#0000FF"]
                },
            nameOfImage2: {
                "slotName": "Clothes",
                "partName": "Clothes",
                "scaleX": 1.0,
                "scaleY": 1.0,
                "rotation": 0.0,
                "offsetX": 0.0,
                "offsetY": 0.0
            } ...
        }
    }
    */

[Serializable]
public class FollowerSkinConfig
{
    public string OverrideBaseSkin { get; set; } = "Cat";
    public Dictionary<string, FollowerSkinPartConfig> PartConfigs { get; set; } //imagename without the png to config
}

[Serializable]
public class FollowerSkinPartConfig
{
    public int SlotIndex { get; set; }
    public string PartName { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float Rotation { get; set; } = -90f;
    public float OffsetX { get; set; } = 0f;
    public float OffsetY { get; set; } = 0f;

    public List<string> ColorChoices { get; set; } = ["#FFF"];
}

[Serializable]
public class DebugOutputSkin
{
    public int SlotIndex { get; set; }
    public string PartName { get; set; }
}