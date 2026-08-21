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
        public const string PluginVer = "1.1.1";

        internal static ManualLogSource Log;
        internal readonly static Harmony Harmony = new(PluginGuid);

        internal static string PluginPath;

        public static ConfigEntry<int> CurrentFleeceIndexP1 { get; set; }
        public static ConfigEntry<int> CurrentFleeceIndexP2 { get; set; }

        public static ConfigEntry<bool> DebugDumpFollowerSpineAtlas { get; set; }

        public static ConfigEntry<bool> FleeceCyclingEnabled { get; set; }

        private RuntimeMapEditor runtimeMapEditor;

        // The F7 panel. Unlike the map editor it is not scoped to a dungeon - fleeces and spines
        // are just as worth setting in the base - so it lives on its own persistent host.
        private ModUI.CultTweakerPanel cultTweakerPanel;

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

            // No loading to do - a cutscene is a file, read when it plays - but the folder is
            // created here so it is there to drop videos into, and listed so the log says what
            // the trigger tool will offer.
            CustomCutsceneLoader.LogWhatIsThere();

            // A video's soundtrack has to be its own file to be audible; this pulls it out with
            // ffmpeg when one is missing, in the background, so a new .mp4 only has to be
            // dropped in the folder.
            CustomCutsceneLoader.ConvertMissingAudio();

            // Enemies are registered here but their prefabs load asynchronously, which is why
            // this takes the plugin as a coroutine host the way the NPC loader does.
            CustomEnemyLoader.LoadAllCustomEnemies(this);
            Log.LogInfo("Loading Custom Follower Overrides...");
            FollowerSpineLoader.LoadAllNonSpineSkins();
            Log.LogInfo("Loading Custom NPCs...");
            CustomNpcLoader.LoadAllCustomNpcs(this);

            CurrentFleeceIndexP1 = Config.Bind("Fleece", "CurrentFleeceIndexP1", -1, "Current Fleece Index for Player 1");
            CurrentFleeceIndexP2 = Config.Bind("Fleece", "CurrentFleeceIndexP2", -1, "Current Fleece Index for Player 2");
            DebugDumpFollowerSpineAtlas = Config.Bind(
                "Debug", "DumpFollowerSpineAtlas", false,
                "If true, will dump the follower spine slots to a json file. May impact performance when enabled. Ensure followerSlots.json is not present before dumping.");
            FleeceCyclingEnabled = Config.Bind("Fleece", "FleeceCyclingEnabled", true, "Enable Fleece Cycling for all players.");


            PlayerSpineLoader.currentFleeceIndexP1 = CurrentFleeceIndexP1.Value;
            PlayerSpineLoader.currentFleeceIndexP2 = CurrentFleeceIndexP2.Value;

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Fired by ChangeRoomRoutine right after its fire-and-forget asset unload; the level
            // loader sequences its room rebuild behind it (see LevelPlayback.NoteBiomeRoomChanged).
            MMBiomeGeneration.BiomeGenerator.OnBiomeChangeRoom += MapEditor.LevelPlayback.NoteBiomeRoomChanged;

            // The one arrival signal a RE-ACTIVATED room also fires - the generation hooks only
            // see a room's first build, which is why a revisited room's lighting went missing.
            MMBiomeGeneration.BiomeGenerator.OnBiomeChangeRoom += MapEditor.Tools.LightingTool.OnBiomeRoomChanged;

            // Custom enemies (and so their corpses) belong to the room they spawned in; the
            // game's own teardown never sees them because COTL_API spawns at the scene root.
            MMBiomeGeneration.BiomeGenerator.OnBiomeLeftRoom += CustomEnemyDressing.OnBiomeLeftRoom;
            TryCreateRuntimeEditor(SceneManager.GetActiveScene());

            var panelHost = new GameObject("CultTweakerPanelHost");
            DontDestroyOnLoad(panelHost);
            cultTweakerPanel = panelHost.AddComponent<ModUI.CultTweakerPanel>();

            var customTestDungeon = new CustomDungeon();

            // Registered for the map editor's enemy picker. Deliberately NOT added to
            // NormalEnemyList: that would auto-spawn it in every dungeon room, which gets in the
            // way of map editing.
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

            // After the test dungeon: F5 enters CustomDungeonList[0], which must stay the
            // test dungeon. Level runs enter CTLevelDungeon through the Level tool instead.
            MapEditor.CTLevelDungeon.Register();

            // Every saved dungeon map is a dungeon. Registering mints a FollowerLocation per
            // map, so it happens once at startup rather than every time the tool lists them.
            MapEditor.CTMapDungeon.RegisterAll();
        }
    
        public void Update()
        {
            // Hands finished background skeleton parses back to their assets; does nothing once the
            // warm-up has drained.
            SpineLoaderHelper.PlayerSpineLoader.PumpWarmUp();

            // if (Input.GetKeyDown(KeyCode.F9))
            // {
            //     Log.LogInfo("Toggling Fleece Cycling to " + !FleeceCyclingEnabled.Value);
            //     FleeceCyclingEnabled.Value = !FleeceCyclingEnabled.Value;

            //     if (!FleeceCyclingEnabled.Value && PlayerFarming.Instance != null)
            //     {
            //         if (CoopManager.CoopActive)
            //         {
            //             PlayerFarming.players[1].SetSkin();
            //         }
            //         PlayerFarming.Instance.SetSkin();

            //     }
            //     else
            //     {
            //         TestApplySpineOverride(cycle: false);
            //     }
            // }

            // F7 used to cycle player 1's fleece one step per press. It opens the mod panel
            // instead, which does the same job as a list (for every player) alongside the spine
            // pickers and the mod's own information. F8 keeps the one-key cycle.
            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (cultTweakerPanel != null) cultTweakerPanel.Toggle();
            }
            // if (Input.GetKeyDown(KeyCode.F8))
            // {
            //     Log.LogInfo("F8 Pressed - Fleece Cycle Player 2");
            //     TestApplySpineOverride(1);
            // }
            if (Input.GetKeyDown(KeyCode.F5))
            {
                // Inside the map editor F5 resets the room; the test-dungeon shortcut would
                // otherwise throw away the room being edited without so much as a warning.
                if (runtimeMapEditor != null && runtimeMapEditor.IsEditing)
                {
                    runtimeMapEditor.RequestResetRoom();
                }
                else
                {
                    Log.LogInfo("F5 Pressed - Test Custom Dungeon");
                    var editorDungeon = CustomDungeonManager.CustomDungeonList.Values.FirstOrDefault();
                    if (editorDungeon != null) editorDungeon.EnterDungeon();
                    else Log.LogWarning("No custom dungeon is registered; nothing to enter.");
                }
            }

            // Both take the camera, the pause and the HUD; the panel wins while it is up.
            if (Input.GetKeyDown(KeyCode.F4) && runtimeMapEditor != null &&
                (cultTweakerPanel == null || !cultTweakerPanel.IsOpen))
            {
                runtimeMapEditor.ToggleEditor();
            }
        }
        private void TestApplySpineOverride(int playerID = 0, bool cycle = true)
        {
            if (!FleeceCyclingEnabled.Value)
            {
                Log.LogWarning("Fleece Cycling is disabled, Press F9 to enable first!");
                return;
            }

            var fleeceIndex = cycle
                ? PlayerSpineLoader.CycleNextFleece(playerID)
                : playerID switch
                {
                    0 => CurrentFleeceIndexP1.Value,
                    1 => CurrentFleeceIndexP2.Value,
                    _ => -1
                };

            if (playerID >= 1 && !CoopManager.CoopActive)
            {
                Log.LogInfo("Coop not active, no fleece cycling");
                return;
            }

            // The dressing itself lives in PlayerSpineLoader now, shared with the F7 panel and
            // the SetSkin patch, so the three cannot drift apart.
            PlayerSpineLoader.ApplyFleece(playerID, fleeceIndex);
        }

        private void OnEnable()
        {
            Harmony.PatchAll();
            Logger.LogInfo($"Loaded {PluginName}!");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Dungeon1")
            {
                TryCreateRuntimeEditor(scene);
            }
            else
            {
                DestroyRuntimeEditor();

                // Only the suppression flag is cleared here, never the run: entering a custom
                // level passes through intermediate scene loads (the transition, the dungeon
                // map's selector) with the run already bound, and stopping it here handed every
                // custom level to the vanilla generator. The flag alone is the thing that must
                // not outlive its room - and OnRoomGenerated re-arms it per room.
                MapEditor.LevelPlayback.ClearContentSuppression();
            }
        }

        private void TryCreateRuntimeEditor(Scene scene)
        {
            if (scene.name != "Dungeon1") return;
            if (runtimeMapEditor != null) return;

            // Not DontDestroyOnLoad: the editor is scoped to this scene and OnSceneLoaded
            // destroys it on any other, so persisting it would only leak.
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