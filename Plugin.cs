using BepInEx;
using BepInEx.Logging;
using COTL_API.CustomFollowerCommand;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.Commands;
using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using System.IO;

namespace CustomSpineLoader
{
    [BepInPlugin(PluginGuid, PluginName, PluginVer)]
    [BepInDependency("io.github.xhayper.COTL_API")]
    [HarmonyPatch]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "InfernoDragon0.cotl.CustomSpineLoader";
        public const string PluginName = "CultTweaker";
        public const string PluginVer = "1.0.0";

        internal static ManualLogSource Log;
        internal readonly static Harmony Harmony = new(PluginGuid);

        internal static string PluginPath;

        private void Awake()
        {
            Log = base.Logger;
            PluginPath = Path.GetDirectoryName(Info.Location);
            // PlayerSpineLoader.LoadAllPlayerSpines();
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