using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using COTL_API.CustomEnemy;
using COTL_API.CustomFollowerCommand;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.Commands;
using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using Spine;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using CustomSpineLoader.MapEditor;
using UnityEngine.UIElements.Collections;

namespace CustomSpineLoader
{
    [BepInPlugin(PluginGuid, PluginName, PluginVer)]
    [BepInDependency("io.github.xhayper.COTL_API")]
    [HarmonyPatch]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "InfernoDragon0.cotl.CustomSpineLoader";
        public const string PluginName = "CultTweaker";
        public const string PluginVer = "2.0.0";
        public const bool PreRelease = true;

        internal static ManualLogSource Log;

        internal static Plugin Instance;
        internal readonly static Harmony Harmony = new(PluginGuid);

        internal static string PluginPath;

        public static ConfigEntry<int> CurrentFleeceIndexP1 { get; set; }
        public static ConfigEntry<int> CurrentFleeceIndexP2 { get; set; }

        public static ConfigEntry<string> CurrentFleeceNameP1 { get; set; }
        public static ConfigEntry<string> CurrentFleeceNameP2 { get; set; }

        public static ConfigEntry<string> SelectedSpineP1 { get; set; }
        public static ConfigEntry<string> SelectedSpineP2 { get; set; }

        public static ConfigEntry<bool> DebugDumpFollowerSpineAtlas { get; set; }

        public static ConfigEntry<bool> MapEditorVanillaPanelArt { get; set; }
        public static ConfigEntry<string> MapEditorPanelPlate { get; set; }
        public static ConfigEntry<string> MapEditorPanelSource { get; set; }
        public static ConfigEntry<float> MapEditorPanelOpacity { get; set; }
        public static ConfigEntry<string> MapEditorPanelCrop { get; set; }
        public static ConfigEntry<bool> MapEditorVanillaWidgets { get; set; }
        public static ConfigEntry<bool> MapEditorFullWeather { get; set; }

        public static ConfigEntry<bool> MainMenuEnabled { get; set; }
        public static ConfigEntry<string> MainMenuPreset { get; set; }

        public static ConfigEntry<bool> FleeceCyclingEnabled { get; set; }

        public static readonly ConfigEntry<bool>[] FleeceTransmog = new ConfigEntry<bool>[4];

        public static bool TransmogOn(int playerId)
        {
            if (playerId < 0 || playerId >= FleeceTransmog.Length) return false;

            var entry = FleeceTransmog[playerId];
            return entry != null ? entry.Value : FleeceCyclingEnabled == null || FleeceCyclingEnabled.Value;
        }

        public static void SetTransmog(int playerId, bool on)
        {
            if (playerId < 0 || playerId >= FleeceTransmog.Length) return;
            if (FleeceTransmog[playerId] != null) FleeceTransmog[playerId].Value = on;
        }

        private RuntimeMapEditor runtimeMapEditor;

        private static RuntimeMapEditor RoomEditor => RuntimeMapEditor.Active;

        private static bool MenuEditorOpen => ModUI.MenuEditor.MainMenuEditor.IsOpen;

        private ModUI.CultTweakerPanel cultTweakerPanel;

        private void Awake()
        {
            Log = base.Logger;
            Instance = this;
            PluginPath = Path.GetDirectoryName(Info.Location);

            CurrentFleeceIndexP1 = Config.Bind("Fleece", "CurrentFleeceIndexP1", -1, "Current Fleece Index for Player 1");
            CurrentFleeceIndexP2 = Config.Bind("Fleece", "CurrentFleeceIndexP2", -1, "Current Fleece Index for Player 2");
            CurrentFleeceNameP1 = Config.Bind("Fleece", "CurrentFleeceNameP1", "", "Current fleece skin name for Player 1 (kept alongside the index so its spine can load at boot)");
            CurrentFleeceNameP2 = Config.Bind("Fleece", "CurrentFleeceNameP2", "", "Current fleece skin name for Player 2 (kept alongside the index so its spine can load at boot)");

            SelectedSpineP1 = Config.Bind("Spine", "SelectedSpineP1", "", "Chosen player spine for Player 1");
            SelectedSpineP2 = Config.Bind("Spine", "SelectedSpineP2", "", "Chosen player spine for Player 2");

            ModContentPaths.LogWhatIsThere(
                "PlayerSkins", "FollowerSpines", "FollowerSkins", "BuildingOverrides",
                "CustomInventoryItems", "CustomMeals", "CustomTarotCards", "CustomStructures",
                "CustomNpcs", "CustomEnemies", "CustomCutscenes", "CustomShapeProfiles",
                MapEditor.MapEditorSerialization.FolderName, MapEditor.CTLevelSerialization.FolderName,
                MapEditor.CTDungeonMapSerialization.FolderName, MapEditor.CTWorldMapSerialization.FolderName,
                MapEditor.CTMenuPresetSerialization.FolderName);

            SpineMemory.Phase("PlayerSpines", () => PlayerSpineLoader.LoadAllPlayerSpines());
            Log.LogInfo("Cult Tweaker is loading! For more information or templates on how to use this mod, go to the NexusMods page!");
            CustomFollowerCommandManager.Add(new CustomColorCommand());
            SpineMemory.Phase("BuildingOverrides", StructureBuildingOverrideHelper.LoadBuildingOverrides);
            Log.LogInfo("Loading Custom Items...");
            SpineMemory.Phase("Items", CustomItemLoader.LoadAllCustomItems);
            Log.LogInfo("Loading Custom Meals...");
            SpineMemory.Phase("Meals", CustomMealLoader.LoadAllCustomMeals);
            Log.LogInfo("Loading Custom Tarots...");
            SpineMemory.Phase("Tarots", CustomTarotLoader.LoadAllCustomTarots);
            Log.LogInfo("Loading Custom Structures...");
            SpineMemory.Phase("Structures", CustomStructureLoader.LoadAllCustomStructures);

            CustomCutsceneLoader.LogWhatIsThere();

            SpineMemory.Phase("CutsceneAudio", CustomCutsceneLoader.ConvertMissingAudio);

            SpineMemory.Phase("Enemies", () => CustomEnemyLoader.LoadAllCustomEnemies(this));
            Log.LogInfo("Loading Custom Follower Overrides...");
            SpineMemory.Phase("FollowerOverrides", FollowerSpineLoader.LoadAllNonSpineSkins);

            SpineMemory.TrimRepackCaches("startup skin builds");
            Log.LogInfo("Loading Custom NPCs...");
            SpineMemory.Phase("NPCs", () => CustomNpcLoader.LoadAllCustomNpcs(this));

            DebugDumpFollowerSpineAtlas = Config.Bind(
                "Debug", "DumpFollowerSpineAtlas", false,
                "If true, will dump the follower spine slots to a json file. May impact performance when enabled. Ensure followerSlots.json is not present before dumping.");
            FleeceCyclingEnabled = Config.Bind("Fleece", "FleeceCyclingEnabled", true, "Enable Fleece Cycling for all players.");

            MapEditorVanillaPanelArt = Config.Bind(
                "MapEditor", "VanillaPanelArt", true,
                "Back the map editor's panels with the game's own photo mode plate instead of the mod's plain rounded one.");
            MapEditorPanelPlate = Config.Bind(
                "MapEditor", "PanelPlate", "",
                "Which piece of that art to use, by sprite or object name. Empty picks the largest nine-sliced panel; everything the source prefab draws is listed in the log the first time the editor opens.");
            MapEditorPanelSource = Config.Bind(
                "MapEditor", "PanelSource", "",
                "Which prefab that art comes from, by addressable key. Empty uses photo mode's take-photo overlay; its neighbours are 'Assets/UI/Menus/Photo Mode/Edit Photo Overlay.prefab' and 'Assets/UI/Menus/Photo Mode/Photo Gallery Menu.prefab'.");
            MapEditorPanelOpacity = Config.Bind(
                "MapEditor", "PanelOpacity", 0.82f,
                "How solid the map editor's panels are. 1 is the art as the game draws it; lower lets the room show through.");
            MapEditorPanelCrop = Config.Bind(
                "MapEditor", "PanelCrop", "",
                "Extra pixels to trim off that art, as left,bottom,right,top. Empty trims only the transparent padding the atlas records, which is usually all of it.");
            MapEditorVanillaWidgets = Config.Bind(
                "MapEditor", "VanillaWidgets", true,
                "Use the game's own settings toggle and slider in the map editor's tool panels, scaled down to the editor's row height, instead of the mod's plain ones.");
            MapEditorFullWeather = Config.Bind(
                "MapEditor", "FullWeather", true,
                "Offer every strength of every weather the game has art for, building the ones it does not ship (extreme wind, say) from the nearest one it does. The weather a player gets on a normal day is unaffected either way.");

            MainMenuEnabled = Config.Bind(
                "MainMenu", "Enabled", true,
                "Let a saved main menu preset dress the title screen. Off leaves the menu exactly as the game draws it, and hides the Customize Menu button.");
            MainMenuPreset = Config.Bind(
                "MainMenu", "Preset", "",
                "Which saved preset the title screen wears, by folder name. Empty is the game's own menu; the editor writes this when a preset is made active.");

            for (var i = 0; i < FleeceTransmog.Length; i++)
                FleeceTransmog[i] = Config.Bind("Fleece", $"FleeceTransmogP{i + 1}",
                    FleeceCyclingEnabled.Value,
                    $"Dress player {i + 1} in the fleece chosen for them (F7 panel).");

            PlayerSpineLoader.currentFleeceIndexP1 = CurrentFleeceIndexP1.Value;
            PlayerSpineLoader.currentFleeceIndexP2 = CurrentFleeceIndexP2.Value;

            PlayerSpineLoader.FleeceIndexes[0] = CurrentFleeceIndexP1.Value;
            PlayerSpineLoader.FleeceIndexes[1] = CurrentFleeceIndexP2.Value;

            SceneManager.sceneLoaded += OnSceneLoaded;

            MMBiomeGeneration.BiomeGenerator.OnBiomeChangeRoom += MapEditor.LevelPlayback.NoteBiomeRoomChanged;

            MMBiomeGeneration.BiomeGenerator.OnBiomeChangeRoom += MapEditor.Tools.LightingTool.OnBiomeRoomChanged;

            MMBiomeGeneration.BiomeGenerator.OnBiomeChangeRoom += MapEditor.Tools.WeatherControl.OnBiomeRoomChanged;

            MMBiomeGeneration.BiomeGenerator.OnBiomeLeftRoom += CustomEnemyDressing.OnBiomeLeftRoom;
            TryCreateRuntimeEditor(SceneManager.GetActiveScene());

            var panelHost = new GameObject("CultTweakerPanelHost");
            DontDestroyOnLoad(panelHost);
            cultTweakerPanel = panelHost.AddComponent<ModUI.CultTweakerPanel>();
            if (PreRelease) panelHost.AddComponent<ModUI.PreReleaseBanner>();

            MapEditor.CTWorldMapSerialization.EnsureRootFolder();
            var worldMapHost = new GameObject("WorldMapHost");
            DontDestroyOnLoad(worldMapHost);
            worldMapHost.AddComponent<MapEditor.WorldMap.WorldMapScreen>();
            worldMapHost.AddComponent<MapEditor.WorldMap.WorldMapEditor>();

            MapEditor.CTMenuPresetSerialization.EnsureRootFolder();
            var mainMenuHost = new GameObject("MainMenuEditorHost");
            DontDestroyOnLoad(mainMenuHost);
            mainMenuHost.AddComponent<ModUI.MenuEditor.MainMenuEditor>();

            OnMenuScene(SceneManager.GetActiveScene());

            var customTestDungeon = new CustomDungeon();

            try
            {
                var newEnemy = new BaseCustomEnemy();
                CustomEnemyManager.Add(newEnemy);
                StartCoroutine(CustomEnemyManager.BuildEnemyPrefab(newEnemy));
                Log.LogInfo("Custom test enemy registered.");
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Custom test enemy could not be registered (missing Spine assets?): " + e.Message);
            }

            CustomDungeonManager.Add(customTestDungeon);

            MapEditor.CTLevelDungeon.Register();

            SpineMemory.Phase("MapDungeons", MapEditor.CTMapDungeon.RegisterAll);
        }
    
        public void Update()
        {
            SpineLoaderHelper.PlayerSpineLoader.PumpWarmUp();

            SpineMemory.Watch();

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (cultTweakerPanel != null && !MenuEditorOpen) cultTweakerPanel.Toggle();
            }
            // if (Input.GetKeyDown(KeyCode.F8))
            // {
            //     Log.LogInfo("F8 Pressed - Fleece Cycle Player 2");
            //     TestApplySpineOverride(1);
            // }
            if (Input.GetKeyDown(KeyCode.F5) && !MapEditor.WorldMap.WorldMapScreen.IsOpen &&
                !MenuEditorOpen && !ModUI.MenuEditor.MenuSceneRefs.InMenuScene)
            {
                if (RoomEditor != null && RoomEditor.IsEditing)
                {
                    RoomEditor.RequestResetRoom();
                }
                else
                {
                    Log.LogInfo("F5 Pressed - Test Custom Dungeon");
                    var editorDungeon = CustomDungeonManager.CustomDungeonList.Values.FirstOrDefault();
                    if (editorDungeon != null) editorDungeon.EnterDungeon();
                    else Log.LogWarning("No custom dungeon is registered; nothing to enter.");
                }
            }

            if (Input.GetKeyDown(KeyCode.F6) && !MenuEditorOpen)
            {
                if (MapEditor.WorldMap.WorldMapScreen.IsOpen &&
                    MapEditor.WorldMap.WorldMapEditor.Instance != null)
                    MapEditor.WorldMap.WorldMapEditor.Instance.ToggleEditMode();
                else if (RoomEditor != null && RoomEditor.IsEditing)
                    RoomEditor.ToggleChromeHidden();
            }

            if (Input.GetKeyDown(KeyCode.F4) && RoomEditor != null &&
                (cultTweakerPanel == null || !cultTweakerPanel.IsOpen) &&
                !MapEditor.WorldMap.WorldMapScreen.IsOpen)
            {
                RoomEditor.ToggleEditor();
            }
        }
        private void TestApplySpineOverride(int playerID = 0, bool cycle = true)
        {
            if (!TransmogOn(playerID))
            {
                Log.LogWarning($"Fleece transmog is off for player {playerID + 1}; turn it on in the " +
                               "F7 panel first.");
                return;
            }

            var fleeceIndex = cycle
                ? PlayerSpineLoader.CycleNextFleece(playerID)
                : PlayerSpineLoader.GetFleeceIndex(playerID);

            if (fleeceIndex < 0)
            {
                Log.LogInfo("No fleece to apply yet; enter a level once so the rotation is built.");
                return;
            }

            if (playerID >= 1 && !CoopManager.CoopActive)
            {
                Log.LogInfo("Coop not active, no fleece cycling");
                return;
            }

            PlayerSpineLoader.ApplyFleece(playerID, fleeceIndex);
        }

        private void OnEnable()
        {
            Harmony.PatchAll();
            Logger.LogInfo($"Loaded {PluginName}!");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SpineMemory.TrimRepackCaches("scene change");

            MapEditor.HubSession.OnSceneLoaded(scene);

            MapEditor.BaseSession.OnSceneLoaded(scene);

            MapEditor.Tools.WeatherControl.Forget();

            OnMenuScene(scene);

            if (scene.name == "Dungeon1")
            {
                TryCreateRuntimeEditor(scene);
            }
            else
            {
                DestroyRuntimeEditor();

                MapEditor.LevelPlayback.ClearContentSuppression();
            }
        }

        private void OnMenuScene(Scene scene)
        {
            if (scene.name != ModUI.MenuEditor.MenuSceneRefs.SceneName)
            {
                ModUI.MenuEditor.MainMenuEditor.Instance?.ForceClose();
                ModUI.MenuEditor.MenuSceneRefs.Forget();
                return;
            }

            ModUI.MenuEditor.MenuSceneRefs.Bind();

            ModUI.MenuEditor.MenuAssets.Forget();
            ModUI.MenuEditor.MenuPresetApplier.LoadConfigured();
            ModUI.MenuEditor.MenuPresetApplier.ApplyStructural();
        }

        private void TryCreateRuntimeEditor(Scene scene)
        {
            if (scene.name != "Dungeon1") return;
            if (runtimeMapEditor != null) return;

            var editorHost = new GameObject("RuntimeMapEditorHost");
            runtimeMapEditor = editorHost.AddComponent<RuntimeMapEditor>();
        }

        private void DestroyRuntimeEditor()
        {
            if (runtimeMapEditor == null) return;
            Destroy(runtimeMapEditor.gameObject);
            runtimeMapEditor = null;
        }

        private void OnDisable()
        {
            Harmony.UnpatchSelf();
            Logger.LogInfo($"Unloaded {PluginName}!");
        }
    }
}