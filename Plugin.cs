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

        // The fleece by NAME as well as index: the index only means something after the fleece
        // rotation is built in game, but the lazy loader must know at boot which spine the
        // remembered fleece lives on so it can load it eagerly.
        public static ConfigEntry<string> CurrentFleeceNameP1 { get; set; }
        public static ConfigEntry<string> CurrentFleeceNameP2 { get; set; }

        // The chosen player spine, remembered here as well. COTL_API holds the selection in a
        // static of its own (SelectedSpine / SelectedSpine2) and nothing in it looks like it writes
        // that anywhere, so a look picked in the F7 panel was gone by the next launch. Re-applied at
        // startup only when the API has no selection of its own - see
        // PlayerSpineLoader.RestoreSelectedSpine.
        public static ConfigEntry<string> SelectedSpineP1 { get; set; }
        public static ConfigEntry<string> SelectedSpineP2 { get; set; }

        public static ConfigEntry<bool> DebugDumpFollowerSpineAtlas { get; set; }

        // The map editor's panels wear the game's own photo mode plate; see MapEditor/VanillaChrome.
        public static ConfigEntry<bool> MapEditorVanillaPanelArt { get; set; }
        public static ConfigEntry<string> MapEditorPanelPlate { get; set; }
        public static ConfigEntry<string> MapEditorPanelSource { get; set; }
        public static ConfigEntry<float> MapEditorPanelOpacity { get; set; }
        public static ConfigEntry<string> MapEditorPanelCrop { get; set; }
        public static ConfigEntry<bool> MapEditorVanillaWidgets { get; set; }
        public static ConfigEntry<bool> MapEditorFullWeather { get; set; }

        // The authored main menu. These two are what a player who never opens the editor is using:
        // the preset is re-applied on every menu load whether or not anything is looking.
        public static ConfigEntry<bool> MainMenuEnabled { get; set; }
        public static ConfigEntry<string> MainMenuPreset { get; set; }

        public static ConfigEntry<bool> FleeceCyclingEnabled { get; set; }

        // Per seat rather than one switch for everybody: a spine that dresses its own body wants
        // transmog off while the lamb standing next to it wants it on, and there is no reason those
        // two players should have to agree. Seeded from the old global switch on first run, so an
        // existing setting carries over rather than silently flipping back on.
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

        // Whichever room editor is up: the dungeon one this class owns, or the one a hub session
        // stands up in the base. RuntimeMapEditor keeps the live one in a static of its own.
        private static RuntimeMapEditor RoomEditor => RuntimeMapEditor.Active;

        // The menu editor takes the whole title screen, and every one of the mod's hotkeys would
        // either do nothing there or do something unwanted.
        private static bool MenuEditorOpen => ModUI.MenuEditor.MainMenuEditor.IsOpen;

        // The F7 panel. Unlike the map editor it is not scoped to a dungeon - fleeces and spines
        // are just as worth setting in the base - so it lives on its own persistent host.
        private ModUI.CultTweakerPanel cultTweakerPanel;

        private void Awake()
        {
            Log = base.Logger;
            Instance = this;
            PluginPath = Path.GetDirectoryName(Info.Location);

            // Bound BEFORE the spine loader runs: the saved fleece name is one of the things
            // that decides which spines load eagerly.
            CurrentFleeceIndexP1 = Config.Bind("Fleece", "CurrentFleeceIndexP1", -1, "Current Fleece Index for Player 1");
            CurrentFleeceIndexP2 = Config.Bind("Fleece", "CurrentFleeceIndexP2", -1, "Current Fleece Index for Player 2");
            CurrentFleeceNameP1 = Config.Bind("Fleece", "CurrentFleeceNameP1", "", "Current fleece skin name for Player 1 (kept alongside the index so its spine can load at boot)");
            CurrentFleeceNameP2 = Config.Bind("Fleece", "CurrentFleeceNameP2", "", "Current fleece skin name for Player 2 (kept alongside the index so its spine can load at boot)");

            SelectedSpineP1 = Config.Bind("Spine", "SelectedSpineP1", "", "Chosen player spine for Player 1");
            SelectedSpineP2 = Config.Bind("Spine", "SelectedSpineP2", "", "Chosen player spine for Player 2");

            // Before any loader runs, so the log says which other mods are handing us content
            // through their own CultTweaker folder (see ModContentPaths) before it says what loaded.
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

            // No loading to do - a cutscene is a file, read when it plays - but the folder is
            // created here so it is there to drop videos into, and listed so the log says what
            // the trigger tool will offer.
            CustomCutsceneLoader.LogWhatIsThere();

            // A video's soundtrack has to be its own file to be audible; this pulls it out with
            // ffmpeg when one is missing, in the background, so a new .mp4 only has to be
            // dropped in the folder.
            SpineMemory.Phase("CutsceneAudio", CustomCutsceneLoader.ConvertMissingAudio);

            // Enemies are registered here but their prefabs load asynchronously, which is why
            // this takes the plugin as a coroutine host the way the NPC loader does.
            SpineMemory.Phase("Enemies", () => CustomEnemyLoader.LoadAllCustomEnemies(this));
            Log.LogInfo("Loading Custom Follower Overrides...");
            SpineMemory.Phase("FollowerOverrides", FollowerSpineLoader.LoadAllNonSpineSkins);

            // Building those skins repacked atlases; the repack scaffolding (Spine's cache and
            // the API's page duplicates) is pure memory now.
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

            // The array is what everything reads; these two are the halves of it that persist.
            PlayerSpineLoader.FleeceIndexes[0] = CurrentFleeceIndexP1.Value;
            PlayerSpineLoader.FleeceIndexes[1] = CurrentFleeceIndexP2.Value;

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Fired by ChangeRoomRoutine right after its fire-and-forget asset unload; the level
            // loader sequences its room rebuild behind it (see LevelPlayback.NoteBiomeRoomChanged).
            MMBiomeGeneration.BiomeGenerator.OnBiomeChangeRoom += MapEditor.LevelPlayback.NoteBiomeRoomChanged;

            // The one arrival signal a RE-ACTIVATED room also fires - the generation hooks only
            // see a room's first build, which is why a revisited room's lighting went missing.
            MMBiomeGeneration.BiomeGenerator.OnBiomeChangeRoom += MapEditor.Tools.LightingTool.OnBiomeRoomChanged;

            // And the weather the room asked for, for the same reason: a room walked back into is
            // re-activated rather than rebuilt, so its blueprint never loads a second time.
            MMBiomeGeneration.BiomeGenerator.OnBiomeChangeRoom += MapEditor.Tools.WeatherControl.OnBiomeRoomChanged;

            // Custom enemies (and so their corpses) belong to the room they spawned in; the
            // game's own teardown never sees them because COTL_API spawns at the scene root.
            MMBiomeGeneration.BiomeGenerator.OnBiomeLeftRoom += CustomEnemyDressing.OnBiomeLeftRoom;
            TryCreateRuntimeEditor(SceneManager.GetActiveScene());

            var panelHost = new GameObject("CultTweakerPanelHost");
            DontDestroyOnLoad(panelHost);
            cultTweakerPanel = panelHost.AddComponent<ModUI.CultTweakerPanel>();
            if (PreRelease) panelHost.AddComponent<ModUI.PreReleaseBanner>();

            // The world map is a menu over whatever scene is running, so its host persists the
            // way the panel's does. The folder is created now so there is somewhere to drop a
            // map's art before any map is saved.
            MapEditor.CTWorldMapSerialization.EnsureRootFolder();
            var worldMapHost = new GameObject("WorldMapHost");
            DontDestroyOnLoad(worldMapHost);
            worldMapHost.AddComponent<MapEditor.WorldMap.WorldMapScreen>();
            worldMapHost.AddComponent<MapEditor.WorldMap.WorldMapEditor>();

            // The menu editor's host outlives the menu scene for the same reason: it is reached
            // from a button inside that scene, but the scene is thrown away and rebuilt on every
            // quit-to-menu, and a host that went with it would have to be found again each time.
            MapEditor.CTMenuPresetSerialization.EnsureRootFolder();
            var mainMenuHost = new GameObject("MainMenuEditorHost");
            DontDestroyOnLoad(mainMenuHost);
            mainMenuHost.AddComponent<ModUI.MenuEditor.MainMenuEditor>();

            // sceneLoaded does not fire for the scene that is already running, so the menu the game
            // booted into is dressed here - the same reason TryCreateRuntimeEditor is called above.
            OnMenuScene(SceneManager.GetActiveScene());

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
            SpineMemory.Phase("MapDungeons", MapEditor.CTMapDungeon.RegisterAll);
        }
    
        public void Update()
        {
            // Hands finished background skeleton parses back to their assets; does nothing once the
            // warm-up has drained.
            SpineLoaderHelper.PlayerSpineLoader.PumpWarmUp();

            // Logs a warning whenever memory leaps by 256MB within a second, so the log lines
            // around it name the culprit. Four reads per second.
            SpineMemory.Watch();

            // F7 used to cycle player 1's fleece one step per press. It opens the mod panel
            // instead, which does the same job as a list (for every player) alongside the spine
            // pickers and the mod's own information. F8 keeps the one-key cycle.
            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (cultTweakerPanel != null && !MenuEditorOpen) cultTweakerPanel.Toggle();
            }
            // if (Input.GetKeyDown(KeyCode.F8))
            // {
            //     Log.LogInfo("F8 Pressed - Fleece Cycle Player 2");
            //     TestApplySpineOverride(1);
            // }
            // F5 on the title screen used to enter the test dungeon straight out of the menu, with
            // nothing loaded and no way back; the menu editor being open is one more reason not to.
            if (Input.GetKeyDown(KeyCode.F5) && !MapEditor.WorldMap.WorldMapScreen.IsOpen &&
                !MenuEditorOpen && !ModUI.MenuEditor.MenuSceneRefs.InMenuScene)
            {
                // Inside the map editor F5 resets the room; the test-dungeon shortcut would
                // otherwise throw away the room being edited without so much as a warning.
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

            // F6 hides the chrome of whichever editor is up, leaving the paused scene to be looked
            // at (or screenshotted): on the world map that is the play view, in the room editor the
            // panels simply go away and come back.
            if (Input.GetKeyDown(KeyCode.F6) && !MenuEditorOpen)
            {
                if (MapEditor.WorldMap.WorldMapScreen.IsOpen &&
                    MapEditor.WorldMap.WorldMapEditor.Instance != null)
                    MapEditor.WorldMap.WorldMapEditor.Instance.ToggleEditMode();
                else if (RoomEditor != null && RoomEditor.IsEditing)
                    RoomEditor.ToggleChromeHidden();
            }

            // All three take the pause and the screen; whichever is up wins.
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

            // No rotation to cycle through yet, or a seat that does not exist.
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
            // The game repacks skins as it dresses things; every scene change is a safe moment
            // to drop the copies that pipeline leaves behind.
            SpineMemory.TrimRepackCaches("scene change");

            // A hub is authored and played in the base's own town room, so the hub session picks
            // its scene up here - and ends on any other.
            MapEditor.HubSession.OnSceneLoaded(scene);

            // The base's own room, in the same scene. Arriving there re-applies whatever this save
            // slot's author has added to it, whether or not they open the editor.
            MapEditor.BaseSession.OnSceneLoaded(scene);

            // No map has asked this scene's weather controller for anything yet.
            MapEditor.Tools.WeatherControl.Forget();

            OnMenuScene(scene);

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

        // Pass A of dressing the title screen: bind to the scene, then put on everything the game
        // will not write over later - the title, the centrepiece and the scene effects. The palette
        // waits for pass B in MainMenuPatches, because MainMenuController.Start has not run yet and
        // would overwrite it.
        //
        // On any other scene this lets go and puts the menu's own look back. The Stylizer belongs to
        // the menu scene, so in practice there is nothing left to restore - but the palettes it
        // points at are shared assets that outlive the scene, and being careless with them is how a
        // gold title screen would follow somebody into a dungeon.
        private void OnMenuScene(Scene scene)
        {
            if (scene.name != ModUI.MenuEditor.MenuSceneRefs.SceneName)
            {
                ModUI.MenuEditor.MainMenuEditor.Instance?.ForceClose();
                ModUI.MenuEditor.MenuSceneRefs.Forget();
                return;
            }

            ModUI.MenuEditor.MenuSceneRefs.Bind();
            ModUI.MenuEditor.MenuPresetApplier.LoadConfigured();
            ModUI.MenuEditor.MenuPresetApplier.ApplyStructural();
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