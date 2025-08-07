using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using Lamb.UI;
using Spine;
using Spine.Unity;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class SkinSelectorPatch
    {
        [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.Awake))]
        [HarmonyPrefix]
        private static bool PlayerFarming_Awake(PlayerFarming __instance)
        {
            Plugin.Log.LogInfo("PlayerFarming Awake called, checking for custom spines...");
            var test = __instance.Spine.skeletonDataAsset.atlasAssets[0].PrimaryMaterial;
            Plugin.Log.LogInfo("Test result is " + test.name);
            Plugin.Log.LogInfo("Test shader is " + test.shader.name);
            PlayerSpineLoader.LoadAllPlayerSpines(test);

            return true;
        }


        [HarmonyPatch(typeof(FollowerBrain), nameof(FollowerBrain.SetFollowerCostume),
            [typeof(Skeleton), typeof(int), typeof(string), typeof(int), typeof(FollowerOutfitType),
                typeof(FollowerHatType), typeof(FollowerClothingType), typeof(FollowerCustomisationType),
                typeof(FollowerSpecialType), typeof(InventoryItem.ITEM_TYPE), typeof(string), typeof(FollowerInfo)])]
        [HarmonyPostfix]
        private static void FollowerBrain_SetFollowerCostume(FollowerBrain __instance, Skeleton skeleton, FollowerInfo info)
        {
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

        [HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.Save))]
        [HarmonyPostfix]
        private static void SaveAndLoad_Save()
        {
            CustomColorHelper.SaveCustomColors();
        }
    }
    
}
