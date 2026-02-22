using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using COTL_API.CustomFollowerCommand;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.Commands;
using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using Spine;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CustomSpineLoader
{
    [BepInPlugin(PluginGuid, PluginName, PluginVer)]
    [BepInDependency("io.github.xhayper.COTL_API")]
    [HarmonyPatch]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "InfernoDragon0.cotl.CustomSpineLoader";
        public const string PluginName = "CultTweaker";
        public const string PluginVer = "1.0.8";

        internal static ManualLogSource Log;
        internal readonly static Harmony Harmony = new(PluginGuid);

        internal static string PluginPath;

        public static ConfigEntry<int> CurrentFleeceIndexP1 { get; set; }
        public static ConfigEntry<int> CurrentFleeceIndexP2 { get; set; }

        public static ConfigEntry<bool> DebugDumpFollowerSpineAtlas { get; set; }

        public static ConfigEntry<bool> FleeceCyclingEnabled { get; set; }

        private void Awake()
        {
            Log = base.Logger;
            PluginPath = Path.GetDirectoryName(Info.Location);
            PlayerSpineLoader.LoadAllPlayerSpines();
            Log.LogInfo("Cult Tweaker is loading! For more information or templates on how to use this mod, go to the NexusMods page!");
            CustomFollowerCommandManager.Add(new CustomColorCommand());
            StructureBuildingOverrideHelper.LoadBuildingOverrides();
            Log.LogInfo("Loading Custom Items...");
            CustomItemLoader.LoadAllCustomItems();
            Log.LogInfo("Loading Custom Meals...");
            CustomMealLoader.LoadAllCustomMeals();
            Log.LogInfo("Loading Custom Tarots...");
            CustomTarotLoader.LoadAllCustomTarots();
            Log.LogInfo("Loading Custom Structures...");
            CustomStructureLoader.LoadAllCustomStructures();
            Log.LogInfo("Loading Custom Follower Overrides...");
            FollowerSpineLoader.LoadAllNonSpineSkins();

            CurrentFleeceIndexP1 = Config.Bind("Fleece", "CurrentFleeceIndexP1", -1, "Current Fleece Index for Player 1");
            CurrentFleeceIndexP2 = Config.Bind("Fleece", "CurrentFleeceIndexP2", -1, "Current Fleece Index for Player 2");
            DebugDumpFollowerSpineAtlas = Config.Bind(
                "Debug", "DumpFollowerSpineAtlas", false,
                "If true, will dump the follower spine slots to a json file. May impact performance when enabled. Ensure followerSlots.json is not present before dumping.");
            FleeceCyclingEnabled = Config.Bind("Fleece", "FleeceCyclingEnabled", true, "Enable Fleece Cycling for all players.");


            PlayerSpineLoader.currentFleeceIndexP1 = CurrentFleeceIndexP1.Value;
            PlayerSpineLoader.currentFleeceIndexP2 = CurrentFleeceIndexP2.Value;
        }
    
        public void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                Log.LogInfo("Toggling Fleece Cycling to " + !FleeceCyclingEnabled.Value);
                FleeceCyclingEnabled.Value = !FleeceCyclingEnabled.Value;

                if (!FleeceCyclingEnabled.Value && PlayerFarming.Instance != null)
                {
                    if (CoopManager.CoopActive)
                    {
                        PlayerFarming.players[1].SetSkin();
                    }
                    PlayerFarming.Instance.SetSkin();

                }
                else
                {
                    TestApplySpineOverride(cycle: false);
                }
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                Log.LogInfo("F7 Pressed - Fleece Cycle Player 1");
                TestApplySpineOverride();
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                Log.LogInfo("F8 Pressed - Fleece Cycle Player 2");
                TestApplySpineOverride(1);
            }
        }
        private void TestApplySpineOverride(int playerID = 0, bool cycle = true)
        {
            if (!FleeceCyclingEnabled.Value)
            {
                Log.LogWarning("Fleece Cycling is disabled, Press F9 to enable first!");
                return;
            }

            var fleeceIndex = -1;
            if (cycle)
            {
                fleeceIndex = PlayerSpineLoader.CycleNextFleece(playerID);
            }
            else
            {
                fleeceIndex = playerID switch
                {
                    0 => CurrentFleeceIndexP1.Value,
                    1 => CurrentFleeceIndexP2.Value,
                    _ => -1
                };
            }
            var fleeceSkinName = PlayerSpineLoader.FleeceRotation[fleeceIndex];
            Log.LogInfo("Applying fleece skin: " + fleeceSkinName);
            //first, we load the default lamb spine, then we can extract the fleece attachments from it.
            var lambSpine = PlayerFarming.Instance.Spine;

            if (playerID >= 1)
            {
                if (CoopManager.CoopActive)
                {
                    lambSpine = PlayerFarming.players[1].Spine;
                    CurrentFleeceIndexP2.Value = fleeceIndex;
                }

                else
                {
                    Log.LogInfo("Coop not active, no fleece cycling");
                    return;
                }
            }
            else
            {
                CurrentFleeceIndexP1.Value = fleeceIndex;
            }

            Skin lambSkin;

            if (fleeceSkinName.Contains("CultTweaker_"))
            {
                //split the name for the custom fleece, CultTweaker_SpineName_FleeceName, max split of 2
                var split = fleeceSkinName.Split(['_'], count: 3);
                if (split.Length < 3)
                {
                    Log.LogWarning("Invalid custom fleece skin name: " + fleeceSkinName);
                    return;
                }

                var SpineName = split[1];
                if (!PlayerSpineLoader.FleeceCyclingSpines.ContainsKey(SpineName))
                {
                    Log.LogWarning("Invalid spine skin name: " + fleeceSkinName + " for spine: " + SpineName);
                    return;
                }

                lambSkin = PlayerSpineLoader.FleeceCyclingSpines[SpineName].Item1.skeletonData.FindSkin(split[2]);

                if (lambSkin == null)
                {
                    Log.LogWarning("Defaulting to default as Custom Fleece skin not found: " + fleeceSkinName);
                    lambSkin = lambSpine.Skeleton.Data.FindSkin("Lamb");
                }
            }
            else
            {
                lambSkin = lambSpine.Skeleton.Data.FindSkin(fleeceSkinName);
            }

            if (lambSpine == null || lambSkin == null)
            {
                Log.LogInfo("Lamb skin was null after cycling, an error occurred! at skin name " + fleeceSkinName);
                return;
            }

            var currentSkin = lambSpine.Skeleton.Skin;
            foreach (var slot in PlayerSpineLoader.FleeceOverrideSlots)
            {
                var slotIndex = lambSpine.Skeleton.FindSlotIndex(slot.Item1);
                var attachment = lambSkin.GetAttachment(slotIndex, slot.Item2);
                Log.LogInfo($"Slot {slot.Item1} index is {slotIndex}, attachment {(attachment != null ? "found" : "not found")}");

                if (attachment == null)
                    currentSkin.RemoveAttachment(slotIndex, slot.Item2);
                else
                    currentSkin.SetAttachment(slotIndex, slot.Item2, attachment);
                Log.LogInfo($"Applied {slot.Item2} attachment to current skin.");
            }

            // //Right Poncho
            // var ponchoRightSlot = lambSpine.Skeleton.FindSlotIndex("images/PonchoRight");
            // var ponchoRightAttachment = lambSkin.GetAttachment(ponchoRightSlot, "PonchoRight");

            // //Left Poncho
            // var ponchoLeftSlot = lambSpine.Skeleton.FindSlotIndex("images/PonchoLeft");
            // var ponchoLeftAttachment = lambSkin.GetAttachment(ponchoLeftSlot, "PonchoLeft");

            

            // Log.LogInfo("Current skin is " + (currentSkin != null ? "found" : "not found"));
            // currentSkin.SetAttachment(ponchoRightSlot, "PonchoRight", ponchoRightAttachment);
            // currentSkin.SetAttachment(ponchoLeftSlot, "PonchoLeft", ponchoLeftAttachment);
            Log.LogInfo("Applied" + fleeceSkinName + " attachment to current skin.");
            lambSpine.Skeleton.SetSlotsToSetupPose();
            lambSpine.Update(0);
        }

        private void OnEnable()
        {
            Harmony.PatchAll();
            Logger.LogInfo($"Loaded {PluginName}!");
        }

        private void OnDisable()
        {
            Harmony.UnpatchSelf();
            Logger.LogInfo($"Unloaded {PluginName}!");
        }
    }
}