using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COTL_API.CustomSkins;
using COTL_API.Helpers;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

public class FollowerSpineLoader
{

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
        var followerFolder = Path.Combine(Plugin.PluginPath, "FollowerSpines");
        //check if the folder exists
        if (!Directory.Exists(followerFolder))
            Directory.CreateDirectory(followerFolder);

        //get each folder inside the directory
        var folders = Directory.GetDirectories(followerFolder);

        //each png file represents a single part to override...?
        

        foreach (var folder in folders)
        {
            var followerSkinName = Path.GetFileName(folder);
            var skinList = new string[0];

            //name of the variant file does not matter. each variant has their own config
            foreach (var variant in Directory.GetDirectories(folder))
            {
                var overrides = Directory.GetFiles(variant, "*.png", SearchOption.TopDirectoryOnly);
                var config = Directory.GetFiles(folder, "config.json", SearchOption.TopDirectoryOnly);

                if (config.Length > 0)
                {
                    var configJson = new TextAsset(File.ReadAllText(config[0]));
                    var configObj = JsonConvert.DeserializeObject<FollowerSkinConfig>(configJson.text);
                    if (configObj == null)
                    {
                        Plugin.Log.LogInfo("No config.json found for follower skin " + followerSkinName + " variant: " + variant + ", using default scales and rotations.");
                    }
                }
                    

                //each PNG is a separate part to override, and the scales and rotations can be set in the config file
            }
            //     if (configObj != null)
            //     {
            //         defaultSkinName = configObj.DefaultSkin;
            //         skinList = configObj.Skins;
            //         Plugin.Log.LogInfo($"Using default skin: {defaultSkinName}");
            //         Plugin.Log.LogInfo($"Using skin list: {string.Join(", ", skinList)}");
            //     }
            // }

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
            // else
            // {
            //     Plugin.Log.LogInfo($"Failed to load follower skin {followerSpineName}, ensure that the folder contains at least one of each .json, .png and .atlas file.");
            // }
        }
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
        partConfigs: [
            {
                "partName": "HEAD",
                "scaleX": 1.0,
                "scaleY": 1.0,
                "rotation": 0.0,
                "offsetX": 0.0,
                "offsetY": 0.0
            },
            {
                "partName": "Clothes",
                "scale": 1.0,
                "rotation": 0.0,
                "positionX": 0.0,
                "positionY": 0.0
            } ...
        ]
    }
    */

[Serializable]
public class FollowerSkinConfig
{
    public List<FollowerSkinPartConfig> PartConfigs { get; set; }
}

[Serializable]
public class FollowerSkinPartConfig
{
    public string PartName { get; set; }
    public float ScaleX { get; set; }
    public float ScaleY { get; set; }
    public float Rotation { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
}
