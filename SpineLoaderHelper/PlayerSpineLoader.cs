using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using COTL_API;
using COTL_API.Helpers;
using COTL_API.CustomSkins;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Spine.Unity;

namespace CustomSpineLoader.SpineLoaderHelper;

public class PlayerSpineLoader
{
    public static List<string> FleeceRotation = []; //string of skin names that have fleeces
    public static Dictionary<string, Tuple<SkeletonDataAsset, List<string>>> FleeceCyclingSpines = []; //spineName: Skel and list of skin names
    public static int currentFleeceIndexP1 = -1;
    public static int currentFleeceIndexP2 = -1;

    public static string currentFleeceSpineNameP1 = "";
    public static string currentFleeceSpineNameP2 = "";

    public static bool LoadedCustomSpines = false;

    public static List<(string, string)> FleeceOverrideSlots = [ //(slot index, slot name)
        ("images/PonchoLeft", "PonchoLeft"),
        ("images/PonchoRight", "PonchoRight"),
        ("images/PonchoLeft", "PonchoLeft2"),
        ("images/PonchoRight", "PonchoRight2"),
        ("images/PonchoExtra", "PonchoExtra"),
        ("images/PonchoRightCorner2", "PonchoRightCorner"),
        ("images/PonchoRightCorner", "PonchoRightCorner"),
        ("images/PonchoShoulder", "PonchoShoulder"),
        ("images/PonchoShoulder2", "PonchoShoulder_Right"),
        ("RopeTopLeft", "images/RopeTopLeft"),
        ("RopeTopRight", "images/RopeTopRight"),
        ("images/Rope", "images/Rope"),
        ("images/Bell", "Bell"),
        ("images/Body", "Body")
    ]; //Tuple<string, string>

    public static int CycleNextFleece(int playerID)
    {
        var result = 0;
        switch (playerID)
        {
            case 0:
                //clamp index to 0 to FleeceRotation
                currentFleeceIndexP1++;
                if (currentFleeceIndexP1 >= FleeceRotation.Count) currentFleeceIndexP1 = 0;
                result = currentFleeceIndexP1;
                break;
            case 1:
                currentFleeceIndexP2++;
                if (currentFleeceIndexP2 >= FleeceRotation.Count) currentFleeceIndexP2 = 0;
                result = currentFleeceIndexP2;
                break;
        }

        Plugin.Log.LogInfo("Player " + (playerID + 1) + " cycled to fleece index " + result + " (" + FleeceRotation[result] + ")");

        return result;
    }
    public static void LoadAllPlayerSpines(Material material = null)
    {
        if (LoadedCustomSpines)
        {
            Plugin.Log.LogInfo("Load Player Spines was called again but already loaded!");
            return;
        }
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
            var isFleeceCycleSkin = false;

            if (config.Length > 0)
            {
                var configJson = new TextAsset(File.ReadAllText(config[0]));
                var configObj = JsonConvert.DeserializeObject<PlayerSpineConfig>(configJson.text);
                if (configObj != null)
                {
                    defaultSkinName = configObj.DefaultSkin;
                    skinList = configObj.Skins;
                    isFleeceCycleSkin = configObj.FleeceCyclingOnly;
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
                Plugin.Log.LogInfo("Creating skeleton for " + playerSpineName);
                Plugin.Log.LogInfo("Using material name " + mat.name);

                if (isFleeceCycleSkin)
                {
                    Plugin.Log.LogInfo("Skin: " + playerSpineName + " is added as a fleece cycle skin.");
                    FleeceCyclingSpines.Add(playerSpineName, new(runtimeSkeletonAsset, [.. skinList]));
                }
                else
                {
                    CustomSkinManager.AddPlayerSpine(playerSpineName, runtimeSkeletonAsset, [.. skinList]);
                    CustomSkinManager.ChangeSelectedPlayerSpine(playerSpineName + "/" + defaultSkinName);
                }


                // PlayerFarming.Instance.Spine.skeletonDataAsset = runtimeSkeletonAsset;
                // PlayerFarming.Instance.Spine.initialSkinName = Plugin.Instance?.SkinToLoad;
                // PlayerFarming.Instance.Spine.Initialize(true);
            }
            else
            {
                Plugin.Log.LogInfo($"Failed to load player skin {playerSpineName}, ensure that the folder contains at least one of each .json, .png and .atlas file.");
            }

        }
        
        LoadedCustomSpines = true;
    }
}

public class PlayerSpineConfig
{
    public string DefaultSkin { get; set; }
    public string[] Skins { get; set; }
    public bool FleeceCyclingOnly { get; set; } = false;
}
