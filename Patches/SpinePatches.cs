using COTL_API.CustomStructures;
using CustomSpineLoader.Commands;
using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using Lamb.UI;
using Newtonsoft.Json;
using Sirenix.Serialization.Utilities;
using Spine;
using Spine.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class SkinSelectorPatch
    {
        [HarmonyPatch(typeof(FollowerInformationBox), nameof(FollowerInformationBox.ConfigureImpl))]
        [HarmonyPostfix]
        private static void FollowerInformationBox_ConfigureImpl(FollowerInformationBox __instance)
        {
            if (FollowerSpineLoader.CustomFollowerSkins.ContainsKey(__instance.FollowerInfo.SkinName))
                __instance.FollowerSpine.Skeleton.Skin = FollowerSpineLoader.CustomFollowerSkins[__instance.FollowerInfo.SkinName];
        }

        [HarmonyPatch(typeof(SkeletonData), nameof(SkeletonData.FindSkin), typeof(string))]
        [HarmonyPostfix]
        private static void SkeletonData_FindSkin(ref Skin? __result, SkeletonData __instance, string skinName)
        {
            if (__result != null) return;
            if (FollowerSpineLoader.CustomFollowerSkins.TryGetValue(skinName, out var skin))
            {
                __result = skin;
                DataManager.SetFollowerSkinUnlocked(skinName);
            }
        }

        [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.Awake))]
        [HarmonyPrefix]
        private static bool PlayerFarming_Awake(PlayerFarming __instance)
        {
            if (PlayerSpineLoader.LoadedCustomSpines)
            {
                Plugin.Log.LogWarning("PlayerFarming_Awake was called again after all custom spines were loaded! Skipping...");
                return true;
            }

            Plugin.Log.LogInfo("PlayerFarming Awake called, checking for custom spines...");
            var test = __instance.Spine.skeletonDataAsset.atlasAssets[0].PrimaryMaterial;
            Plugin.Log.LogInfo("Test result is " + test.name);
            Plugin.Log.LogInfo("Test shader is " + test.shader.name);
            
            //Temporarily remove red emissions from custom skins
            test.SetTextureScale("_EmissionMap", new Vector2(0f, 0f));
            PlayerSpineLoader.LoadAllPlayerSpines(test);

            Plugin.Log.LogInfo("Creating Fleece Rotation!");
            var playerSpine = __instance.Spine;
            foreach (var skinName in playerSpine.Skeleton.Data.Skins)
            {
                //skip lamb_intro and lamb_0
                if (skinName.Name.ToLower().Contains("lamb_intro") || skinName.Name.ToLower().Contains("lamb_0")) continue;
                if (skinName.Name.ToLower().Contains("lamb"))
                {
                    PlayerSpineLoader.FleeceRotation.Add(skinName.Name);
                    Plugin.Log.LogInfo("Added fleece skin: " + skinName.Name);
                }
            }
            //add Goat, Snake, Owl if not exist
            if (!PlayerSpineLoader.FleeceRotation.Contains("Goat"))
            {
                PlayerSpineLoader.FleeceRotation.Add("Goat");
                Plugin.Log.LogInfo("Added fleece skin: Goat");
            }
            if (!PlayerSpineLoader.FleeceRotation.Contains("Snake"))
            {
                PlayerSpineLoader.FleeceRotation.Add("Snake");
                Plugin.Log.LogInfo("Added fleece skin: Snake");
            }
            if (!PlayerSpineLoader.FleeceRotation.Contains("Owl"))
            {
                PlayerSpineLoader.FleeceRotation.Add("Owl");
                Plugin.Log.LogInfo("Added fleece skin: Owl");
            }

            //add custom fleece skins
            foreach (var kvp in PlayerSpineLoader.FleeceCyclingSpines)
            {
                var spineName = kvp.Key;

                foreach (var fleeceName in kvp.Value.Item2)
                {
                    var fleeceString = "CultTweaker_" + spineName + "_" + fleeceName;
                    if (!PlayerSpineLoader.FleeceRotation.Contains(fleeceString))
                    {
                        PlayerSpineLoader.FleeceRotation.Add(fleeceString);
                        Plugin.Log.LogInfo("Added custom fleece skin: " + fleeceString);
                    }
                }
            }

            return true;
        }

        [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.SetSkin), typeof(bool))]
        [HarmonyPostfix]
        private static void PlayerFarming_SetSkin(ref Skin __result, PlayerFarming __instance, bool BlackAndWhite)
        {
            if (!Plugin.FleeceCyclingEnabled.Value) return;

            var fleeceIndex = -1;
            var fleeceSkinName = "";
            //check if p1 or p2
            if (CoopManager.CoopActive && __instance.playerID == 1)
            {
                fleeceIndex = PlayerSpineLoader.currentFleeceIndexP2;
                Plugin.Log.LogInfo("Applying fleece skin for Player 2: " + fleeceSkinName);
            }
            else
            {
                fleeceIndex = PlayerSpineLoader.currentFleeceIndexP1;
            }

            if (fleeceIndex == -1)
            {
                Plugin.Log.LogInfo("No fleece skin to apply.");
                return;
            }

            if (fleeceIndex >= PlayerSpineLoader.FleeceRotation.Count)
            {
                Plugin.Log.LogInfo("Fleece skin index out of range. Cycle with F7 or F8 to fix.");
                return;
            }

            fleeceSkinName = PlayerSpineLoader.FleeceRotation[fleeceIndex];


            Plugin.Log.LogInfo("Applying fleece skin: " + fleeceSkinName);
            //first, we load the default lamb spine, then we can extract the fleece attachments from it.
            var lambSpine = __instance.Spine;
            // var lambSkin = lambSpine.Skeleton.Data.FindSkin(fleeceSkinName);

            Skin lambSkin;

            if (fleeceSkinName.Contains("CultTweaker_"))
            {
                //split the name for the custom fleece, CultTweaker_SpineName_FleeceName, max split of 2
                var split = fleeceSkinName.Split(['_'], count: 3);
                if (split.Length < 3)
                {
                    Plugin.Log.LogWarning("Invalid custom fleece skin name: " + fleeceSkinName);
                    return;
                }

                var SpineName = split[1];
                if (!PlayerSpineLoader.FleeceCyclingSpines.ContainsKey(SpineName))
                {
                    Plugin.Log.LogWarning("Invalid spine skin name: " + fleeceSkinName + " for spine: " + SpineName);
                    return;
                }

                lambSkin = PlayerSpineLoader.FleeceCyclingSpines[SpineName].Item1.skeletonData.FindSkin(split[2]);

                if (lambSkin == null)
                {
                    Plugin.Log.LogWarning("Defaulting to default as Custom Fleece skin not found: " + fleeceSkinName);
                    lambSkin = lambSpine.Skeleton.Data.FindSkin("Lamb");
                }
            }
            else
            {
                lambSkin = lambSpine.Skeleton.Data.FindSkin(fleeceSkinName);
            }

            if (lambSpine == null) 
            {
                Plugin.Log.LogInfo("Lamb skin was null after cycling, an error occurred! at skin name " + fleeceSkinName);
                return;
            }

            var currentSkin = lambSpine.Skeleton.Skin;
            foreach (var slot in PlayerSpineLoader.FleeceOverrideSlots)
            {
                var slotIndex = lambSpine.Skeleton.FindSlotIndex(slot.Item1);
                var attachment = lambSkin.GetAttachment(slotIndex, slot.Item2);
                Plugin.Log.LogInfo($"Slot {slot.Item1} index is {slotIndex}, attachment {(attachment != null ? "found" : "not found")}");

                if (attachment == null)
                    currentSkin.RemoveAttachment(slotIndex, slot.Item2);
                else
                    currentSkin.SetAttachment(slotIndex, slot.Item2, attachment);
                Plugin.Log.LogInfo($"Applied {slot.Item2} attachment to current skin.");
            }


            Plugin.Log.LogInfo("Applied" + fleeceSkinName + " attachment to current skin.");
            lambSpine.Skeleton.SetSlotsToSetupPose();
            lambSpine.Update(0);
        }
        
        [HarmonyPatch(typeof(FollowerBrain), nameof(FollowerBrain.SetFollowerCostume),
            [typeof(Skeleton), typeof(int), typeof(string), typeof(int), typeof(FollowerOutfitType),
                typeof(FollowerHatType), typeof(FollowerClothingType), typeof(FollowerCustomisationType),
                typeof(FollowerSpecialType), typeof(InventoryItem.ITEM_TYPE), typeof(string), typeof(FollowerInfo)])]
        [HarmonyPrefix]
        public static bool FollowerBrain_SetFollowerCostume_Prefix(
            FollowerBrain __instance,
            Skeleton skeleton,
            ref string skinName,
            ref FollowerOutfitType outfit,
            ref FollowerHatType hat,
            ref FollowerClothingType clothing,
            FollowerCustomisationType customisation,
            ref FollowerSpecialType special,
            ref InventoryItem.ITEM_TYPE necklace,
            FollowerInfo info)
        {
            if (info != null)
            {
                Plugin.Log.LogInfo($"Follower ID: {info.ID}, Name: {info.Name}");
                var colorData = CustomColorHelper.GetCustomColor(info.ID);
                if (colorData == null) return true;
                if (!colorData.CustomFollowerCostume) return true;

                Plugin.Log.LogInfo($"Custom costume enabled: {colorData.CustomFollowerCostume}, ClothingType: {colorData.FollowerClothingType}, SpecialType: {colorData.FollowerSpecialType}, HatType: {colorData.FollowerHatType}, OutfitType: {colorData.FollowerOutfitType}");
                hat = (FollowerHatType)colorData.FollowerHatType;
                necklace = CustomColorCommand.GetNecklaceToUse(colorData.FollowerNecklaceType, info);
                special = (FollowerSpecialType)colorData.FollowerSpecialType;
                clothing = (FollowerClothingType)colorData.FollowerClothingType;
                outfit = (FollowerOutfitType)colorData.FollowerOutfitType;

                //snowman skin
                if (special is FollowerSpecialType.Snowman_Bad or FollowerSpecialType.Snowman_Average or FollowerSpecialType.Snowman_Great)
                {
                    skinName = CustomColorCommand.GetSnowmanRandomSkin(special);
                    Plugin.Log.LogInfo("Applying snowman skin: " + skinName);
                }

                //blacklisted clothing types
                if (clothing is FollowerClothingType.Jumper or FollowerClothingType.Shirt or FollowerClothingType.Robe or FollowerClothingType.Count)
                {
                    clothing = FollowerClothingType.Normal_1;
                    Plugin.Log.LogInfo("Clothing type was blacklisted, defaulting to Normal.");
                }

                //blacklisted outfits
                if (outfit is FollowerOutfitType.Custom)
                {
                    outfit = FollowerOutfitType.None;
                    Plugin.Log.LogInfo("Outfit type was blacklisted, defaulting to None.");
                }
            }
            return true;
        }


        [HarmonyPatch(typeof(FollowerBrain), nameof(FollowerBrain.SetFollowerCostume),
            [typeof(Skeleton), typeof(int), typeof(string), typeof(int), typeof(FollowerOutfitType),
                typeof(FollowerHatType), typeof(FollowerClothingType), typeof(FollowerCustomisationType),
                typeof(FollowerSpecialType), typeof(InventoryItem.ITEM_TYPE), typeof(string), typeof(FollowerInfo)])]
        [HarmonyPostfix]
        private static void FollowerBrain_SetFollowerCostume(FollowerBrain __instance, Skeleton skeleton, FollowerInfo info)
        {
            //debug dump all slots to a text file
            if (Plugin.DebugDumpFollowerSpineAtlas.Value)
            {
                Plugin.Log.LogWarning("Debug Dump is enabled! Performance may be impacted");
                var fileName = Path.Combine(Plugin.PluginPath, "followerSlots.json");
                if (File.Exists(fileName))
                    Plugin.Log.LogInfo("followerSlots.json already exists, skipping dump. Delete the file to dump again.");
                else
                {
                    Plugin.Log.LogInfo("Creating dump for followerslots");
                    List<DebugOutputSkin> list = [];
                    foreach (var slot in skeleton.Skin.Attachments)
                    {
                        list.Add(new DebugOutputSkin { SlotIndex = slot.SlotIndex, PartName = slot.Name });
                    }
                    var jsonString = JsonConvert.SerializeObject(list, Formatting.Indented);
                    File.AppendAllText(fileName, jsonString);
                }
            }

            Plugin.Log.LogInfo("Setting follower costume for"); 
            if (info != null)
            {
                Plugin.Log.LogInfo($"Follower ID: {info.ID}, Name: {info.Name}");
                var colorData = CustomColorHelper.GetCustomColor(info.ID);
                if (colorData == null) return;

                Plugin.Log.LogInfo($"Custom color found for follower {info.ID}: R={colorData.R}, G={colorData.G}, B={colorData.B}, A={colorData.A}");

                skeleton.FindSlot("ARM_LEFT_SKIN").SetColor(new Color(colorData.R, colorData.G, colorData.B, 1));
                skeleton.FindSlot("LEG_LEFT_SKIN").SetColor(new Color(colorData.R, colorData.G, colorData.B, 1));
                skeleton.FindSlot("LEG_RIGHT_SKIN").SetColor(new Color(colorData.R, colorData.G, colorData.B, 1));
                skeleton.FindSlot("ARM_RIGHT_SKIN").SetColor(new Color(colorData.R, colorData.G, colorData.B, 1));
                skeleton.FindSlot("HEAD_SKIN_BTM").SetColor(new Color(colorData.R, colorData.G, colorData.B, 1));

                skeleton.A = colorData.A;
                var follower = FollowerManager.FindFollowerByID(info.ID);
                if (follower != null)
                {
                    follower.transform.localScale = new Vector3(colorData.scale, colorData.scale, 1f);
                    Plugin.Log.LogInfo("Set follower scale to " + colorData.scale);
                }
                

                if (colorData.CustomFollowerCostume)
                {
                    try
                    {
                        
                        Plugin.Log.LogInfo("Costume override applied successfully.");
                    }
                    catch (Exception)
                    {
                        Plugin.Log.LogWarning("The costume combinations were invalid, try another!");
                    }
                }

                
            }
            else
            {
                Plugin.Log.LogInfo("Follower info is null, skipping costume setting.");
                return;
            }

        }

        [HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.Load))]
        [HarmonyPostfix]
        private static void SaveAndLoad_Load(int saveSlot)
        {
            CustomColorHelper.LoadCustomColors(saveSlot);
        }

        [HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.Save), [])]
        [HarmonyPostfix]
        private static void SaveAndLoad_Save()
        {
            CustomColorHelper.SaveCustomColors();
        }

        //For building overrides
        [HarmonyPatch(typeof(Structure), nameof(Structure.Start))]
        [HarmonyPostfix]
        private static void Structure_Start(Structure __instance)
        {
            var buildingName = __instance.Type.ToString();
            var overrides = StructureBuildingOverrideHelper.GetOverridesForBuilding(buildingName);
            if (overrides == null || overrides.Count == 0) return;

            Plugin.Log.LogInfo($"Custom Spine Loader: {overrides.Count} overrides to building {buildingName}.");
            CustomStructureManager.OverrideStructureBuilding(__instance.gameObject, overrides);
        }
    }
    
}
