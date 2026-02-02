using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using COTL_API.CustomFollowerCommand;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.Commands;
using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using System.IO;
using UnityEngine;

namespace CustomSpineLoader
{
    [BepInPlugin(PluginGuid, PluginName, PluginVer)]
    [BepInDependency("io.github.xhayper.COTL_API")]
    [HarmonyPatch]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "InfernoDragon0.cotl.CustomSpineLoader";
        public const string PluginName = "CultTweaker";
        public const string PluginVer = "1.0.3";

        internal static ManualLogSource Log;
        internal readonly static Harmony Harmony = new(PluginGuid);

        internal static string PluginPath;

        private ConfigEntry<int> CurrentFleeceIndexP1 { get; set; }
        private ConfigEntry<int> CurrentFleeceIndexP2 { get; set; }

        private void Awake()
        {
            Log = base.Logger;
            PluginPath = Path.GetDirectoryName(Info.Location);
            // PlayerSpineLoader.LoadAllPlayerSpines();
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

            CurrentFleeceIndexP1 = Config.Bind("Fleece", "CurrentFleeceIndexP1", -1, "Current Fleece Index for Player 1");
            CurrentFleeceIndexP2 = Config.Bind("Fleece", "CurrentFleeceIndexP2", -1, "Current Fleece Index for Player 2");
            PlayerSpineLoader.currentFleeceIndexP1 = CurrentFleeceIndexP1.Value;
            PlayerSpineLoader.currentFleeceIndexP2 = CurrentFleeceIndexP2.Value;
        }

        public void Update()
        {

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
        private void TestApplySpineOverride(int playerID = 0)
        {
            var fleeceIndex = PlayerSpineLoader.CycleNextFleece(playerID);
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

            var lambSkin = lambSpine.Skeleton.Data.FindSkin(fleeceSkinName);

            if (lambSpine == null)
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