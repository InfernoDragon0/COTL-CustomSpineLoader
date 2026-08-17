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
            if (!PlayerSpineLoader.LoadedFleeceCycling)
            {
                Plugin.Log.LogInfo("Creating Fleece Rotation!");
                var playerSpine = __instance.Spine;
                foreach (var skinName in playerSpine.Skeleton.Data.Skins)
                {
                    if (PlayerSpineLoader.FleeceRotation.Contains(skinName.Name)) continue;
                    //skip and lamb_0
                    if (skinName.Name.ToLower().Contains("lamb_0")) continue;
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

                PlayerSpineLoader.LoadedFleeceCycling = true;
            }

            if (!PlayerSpineLoader.LoadedCustomSpines)
            {
                Plugin.Log.LogInfo("PlayerFarming Awake called, checking for custom spines...");
                var test = __instance.Spine.skeletonDataAsset.atlasAssets[0].PrimaryMaterial;
                Plugin.Log.LogInfo("Test result is " + test.name);
                Plugin.Log.LogInfo("Test shader is " + test.shader.name);
                
                //Temporarily remove red emissions from custom skins
                test.SetTextureScale("_EmissionMap", new Vector2(0f, 0f));
                PlayerSpineLoader.LoadAllPlayerSpines(test);
            }
            return true;
        }

        // A spine hotswap (and a respawn, which goes through OnEnable) calls PlayerFarming.Start
        // again. COTL_API's prefix does the swap and Initialize, but the original Start returns at
        // its own "if (StartComplete)" guard before reaching the SetSkin() it ends with - so on
        // every swap after the first, the skin is whatever Initialize left behind: the raw data
        // skin, with no weapon or chore overlay, and nothing to trigger the postfix below.
        //
        // StartComplete is read before the original runs, because the original is what sets it.
        [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.Start))]
        [HarmonyPrefix]
        private static void PlayerFarming_Start_Prefix(PlayerFarming __instance, ref bool __state)
        {
            __state = __instance.StartComplete;
        }

        [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.Start))]
        [HarmonyPostfix]
        private static void PlayerFarming_Start_Postfix(PlayerFarming __instance, bool __state)
        {
            // A first, genuine Start already called SetSkin itself.
            if (!__state) return;

            var playerId = CoopManager.CoopActive && __instance.playerID == 1 ? 1 : 0;

            // Limited to spines this mod loaded with a config.json, so a vanilla respawn keeps
            // behaving exactly as it did.
            if (PlayerSpineLoader.ConfigFor(playerId) == null) return;

            Plugin.Log.LogInfo($"Rebuilding player {playerId + 1}'s skin after a spine swap.");
            __instance.SetSkin();
        }

        [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.SetSkin), typeof(bool))]
        [HarmonyPostfix]
        private static void PlayerFarming_SetSkin(ref Skin __result, PlayerFarming __instance, bool BlackAndWhite)
        {
            //check if p1 or p2
            var playerId = CoopManager.CoopActive && __instance.playerID == 1 ? 1 : 0;
            var config = PlayerSpineLoader.ConfigFor(playerId);

            DressFleece(__instance, playerId, config);

            // Always, and always last: the fleece above writes to some of the same slots, and the
            // skin the game just rebuilt has to be stripped whether or not a fleece was applied.
            PlayerSpineLoader.HideSlots(__instance.Spine, config);
        }

        private static void DressFleece(PlayerFarming player, int playerId, PlayerSpineConfig config)
        {
            if (!Plugin.FleeceCyclingEnabled.Value) return;

            // This spine dresses its own body; the fleece would write lamb artwork over it.
            if (config != null && config.DisableFleeceCycling) return;

            var fleeceIndex = playerId == 1
                ? PlayerSpineLoader.currentFleeceIndexP2
                : PlayerSpineLoader.currentFleeceIndexP1;

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

            var fleeceSkinName = PlayerSpineLoader.FleeceRotation[fleeceIndex];

            Plugin.Log.LogInfo($"Applying fleece skin for player {playerId + 1}: {fleeceSkinName}");

            var lambSpine = player.Spine;
            if (lambSpine == null) return;

            // Shared with the F-keys and the F7 panel: the fleece lives on another skin, and
            // wearing it means copying that skin's attachments into the live one.
            var lambSkin = PlayerSpineLoader.ResolveFleeceSkin(fleeceSkinName, lambSpine);
            if (lambSkin == null)
            {
                Plugin.Log.LogInfo("Lamb skin was null after cycling, an error occurred! at skin name " + fleeceSkinName);
                return;
            }

            PlayerSpineLoader.ApplyFleeceAttachments(lambSpine, lambSkin, config);
        }

        [HarmonyPatch(typeof(Follower), nameof(Follower.Update))]
        [HarmonyPostfix]
        public static void Follower_FacePosition(Follower __instance)
        {
            var followerId = __instance.Brain.Info.ID;
            var scale = CustomColorHelper.GetCustomScale(followerId);

            if (scale <= 0) return;
            __instance.Spine.skeleton.ScaleX = __instance.transform.position.x < __instance._destPos.x ? -scale : scale;
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
                FollowerSlotDumper.Dump(skeleton, overwrite: false);
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
                    // follower.transform.localScale = new Vector3(colorData.scale, colorData.scale, 1f);
                    follower.Spine.skeleton.scaleY = colorData.scale;
                    follower.Spine.skeleton.scaleX = colorData.scale;
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
