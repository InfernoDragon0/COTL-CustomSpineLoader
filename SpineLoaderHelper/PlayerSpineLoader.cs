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
using Spine;
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
    public static bool LoadedFleeceCycling = false;

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

    // ---- fleece application -------------------------------------------------------------------

    // The fleece lives on ANOTHER skin (a vanilla one on the lamb's own skeleton, or a skin from a
    // FleeceCyclingOnly spine we loaded ourselves); wearing it means copying that skin's
    // attachments into the slots the player is currently rendering. Shared by every caller -
    // the F-keys, the panel and the SetSkin patch - so one fix reaches all three.
    public static Skin ResolveFleeceSkin(string fleeceSkinName, SkeletonAnimation targetSpine)
    {
        if (string.IsNullOrEmpty(fleeceSkinName) || targetSpine == null) return null;

        if (!fleeceSkinName.Contains("CultTweaker_"))
            return targetSpine.Skeleton.Data.FindSkin(fleeceSkinName);

        // CultTweaker_<SpineName>_<FleeceName>; the fleece name may itself contain underscores,
        // which is why the split is capped at 3.
        var split = fleeceSkinName.Split(['_'], count: 3);
        if (split.Length < 3)
        {
            Plugin.Log.LogWarning("Invalid custom fleece skin name: " + fleeceSkinName);
            return null;
        }

        var spineName = split[1];
        if (!FleeceCyclingSpines.ContainsKey(spineName))
        {
            Plugin.Log.LogWarning("Invalid spine skin name: " + fleeceSkinName + " for spine: " + spineName);
            return null;
        }

        var skin = FleeceCyclingSpines[spineName].Item1.skeletonData.FindSkin(split[2]);
        if (skin != null) return skin;

        Plugin.Log.LogWarning("Defaulting to default as Custom Fleece skin not found: " + fleeceSkinName);
        return targetSpine.Skeleton.Data.FindSkin("Lamb");
    }

    // Copies the fleece's attachments into the live skin, slot by slot. A slot the fleece does not
    // fill is CLEARED rather than left alone - otherwise the previous fleece's poncho stays on
    // under the new one.
    public static void ApplyFleeceAttachments(SkeletonAnimation spine, Skin fleeceSkin)
    {
        if (spine == null || fleeceSkin == null) return;

        var currentSkin = spine.Skeleton.Skin;
        foreach (var slot in FleeceOverrideSlots)
        {
            var slotIndex = spine.Skeleton.FindSlotIndex(slot.Item1);
            var attachment = fleeceSkin.GetAttachment(slotIndex, slot.Item2);

            if (attachment == null)
                currentSkin.RemoveAttachment(slotIndex, slot.Item2);
            else
                currentSkin.SetAttachment(slotIndex, slot.Item2, attachment);
        }

        spine.Skeleton.SetSlotsToSetupPose();
        spine.Update(0);
    }

    // players is only populated when coop features are enabled, so solo play lives entirely in
    // Instance and player 0 has to fall back to it.
    public static PlayerFarming ResolvePlayer(int playerId)
    {
        var players = PlayerFarming.players;
        if (players != null && playerId >= 0 && playerId < players.Count && players[playerId] != null)
            return players[playerId];

        return playerId == 0 ? PlayerFarming.Instance : null;
    }

    public static int GetFleeceIndex(int playerId) => playerId switch
    {
        0 => currentFleeceIndexP1,
        1 => currentFleeceIndexP2,
        _ => -1
    };

    // Dresses one player in one fleece. Players beyond the second are dressed but NOT remembered:
    // both the config and the SetSkin patch that re-applies a fleece after a respawn only know
    // about two, so a third player's choice lasts until the game next rebuilds their skin.
    public static bool ApplyFleece(int playerId, int fleeceIndex, bool persist = true)
    {
        if (fleeceIndex < 0 || fleeceIndex >= FleeceRotation.Count)
        {
            Plugin.Log.LogWarning($"Fleece index {fleeceIndex} is out of range (0-{FleeceRotation.Count - 1}).");
            return false;
        }

        var player = ResolvePlayer(playerId);
        if (player == null || player.Spine == null)
        {
            Plugin.Log.LogInfo($"Player {playerId + 1} is not in the game; no fleece applied.");
            return false;
        }

        var fleeceSkinName = FleeceRotation[fleeceIndex];
        var fleeceSkin = ResolveFleeceSkin(fleeceSkinName, player.Spine);
        if (fleeceSkin == null)
        {
            Plugin.Log.LogWarning("Fleece skin could not be resolved: " + fleeceSkinName);
            return false;
        }

        ApplyFleeceAttachments(player.Spine, fleeceSkin);

        if (persist)
        {
            switch (playerId)
            {
                case 0:
                    currentFleeceIndexP1 = fleeceIndex;
                    Plugin.CurrentFleeceIndexP1.Value = fleeceIndex;
                    break;
                case 1:
                    currentFleeceIndexP2 = fleeceIndex;
                    Plugin.CurrentFleeceIndexP2.Value = fleeceIndex;
                    break;
            }
        }

        Plugin.Log.LogInfo($"Player {playerId + 1} is wearing {fleeceSkinName}.");
        return true;
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
