using BepInEx;
using BepInEx.Logging;
using COTL_API.CustomFollowerCommand;
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
        public const string PluginName = "CustomSpineLoader";
        public const string PluginVer = "0.0.5";

        internal static ManualLogSource Log;
        internal readonly static Harmony Harmony = new(PluginGuid);

        internal static string PluginPath;

        private void Awake()
        {
            Plugin.Log = base.Logger;
            PluginPath = Path.GetDirectoryName(Info.Location);
            // PlayerSpineLoader.LoadAllPlayerSpines();
            CustomFollowerCommandManager.Add(new CustomColorCommand());
            StructureBuildingOverrideHelper.LoadBuildingOverrides();
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