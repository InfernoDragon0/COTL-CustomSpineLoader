using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using COTL_API;
using COTL_API.Helpers;
using COTL_API.CustomSkins;
using System.Linq;
using Newtonsoft.Json;

namespace CustomSpineLoader.SpineLoaderHelper;

public class PlayerSpineLoader
{
    public static void LoadAllPlayerSpines()
    {
        //get the plugin path, then find the foler PlayerSkins in it
        var playerFolder = Path.Combine(Plugin.PluginPath, "PlayerSkins");
        //check if the player folder exists
        if (!Directory.Exists(playerFolder))
            Directory.CreateDirectory(playerFolder);

        //get each folder inside the directory
        var folders = Directory.GetDirectories(playerFolder);

        foreach (var folder in folders)
        {
            var playerSpineName = Path.GetFileName(folder);

            var spineSkeleton = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Where(x => !x.Contains("config")).ToArray();
            var spineTextures = Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly);
            var spineAtlas = Directory.GetFiles(folder, "*.atlas", SearchOption.TopDirectoryOnly);
            var config = Directory.GetFiles(folder, "config.json", SearchOption.TopDirectoryOnly);

            var defaultSkinName = "Lamb";
            var skinList = new string[0];

            if (config.Length > 0)
            {
                var configJson = new TextAsset(File.ReadAllText(config[0]));
                var configObj = JsonConvert.DeserializeObject<PlayerSpineConfig>(configJson.text);
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

                var mat = new Material(Shader.Find("Spine/Skeleton"));
                var runtimeAtlasAsset = Spine.Unity.SpineAtlasAsset.CreateRuntimeInstance(atlasTxt, textures, mat, true);
                var runtimeSkeletonAsset = Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance(skele, runtimeAtlasAsset, true, 0.005f);
                Plugin.Log.LogInfo("Creating skeleton for " + playerSpineName);
                CustomSkinManager.AddPlayerSpine(playerSpineName, runtimeSkeletonAsset, skinList.ToList());
                CustomSkinManager.ChangeSelectedPlayerSpine(playerSpineName + "/" + defaultSkinName);

                // PlayerFarming.Instance.Spine.skeletonDataAsset = runtimeSkeletonAsset;
                // PlayerFarming.Instance.Spine.initialSkinName = Plugin.Instance?.SkinToLoad; //"Shepherds/Karakal"
                // PlayerFarming.Instance.Spine.Initialize(true);
            }
            else
            {
                Plugin.Log.LogInfo($"Failed to load player skin {playerSpineName}, ensure that the folder contains at least one of each .json, .png and .atlas file.");
            }

        }
    }
}

public class PlayerSpineConfig
{
    public string DefaultSkin { get; set; }
    public string[] Skins { get; set; }
}
