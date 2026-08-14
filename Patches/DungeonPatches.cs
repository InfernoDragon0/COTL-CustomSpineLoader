using System;
using CustomSpineLoader.APIHelper;
using HarmonyLib;
using MMBiomeGeneration;
using MMRoomGeneration;
using static MMRoomGeneration.GenerateRoom;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class DungeonPatches
    {
        public static ConnectionTypes NextRoomConnectionType = ConnectionTypes.Entrance;
        public static bool GenCheck = false;
        // Direction of the last door the player walked through; the next room's blueprint
        // entry prefers the opposite side.
        public static string LastDoorDirection = null;

        [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.OnEnable))]
        [HarmonyPrefix]
        private static void BiomeGenerator_OnEnable(BiomeGenerator __instance)
        {
            // A dungeon entry that is not the level run's own ends that run, so its statics
            // never leak into the next scene. FollowerLocation.None means "no entry pending"
            // (a later re-enable of the same biome), which must leave a run alone.
            var entering = CustomDungeonManager.EnteringCustomDungeon;
            var levelLocation = MapEditor.CTLevelDungeon.Instance != null
                ? MapEditor.CTLevelDungeon.Instance.Location
                : FollowerLocation.None;
            if (entering != FollowerLocation.None && entering != levelLocation)
                MapEditor.LevelPlayback.Stop();

            // Any biome coming up starts on its own lighting: the map editor's override is
            // global state and would otherwise follow the player into the next room or scene.
            // A blueprint carrying lighting re-applies it when it loads. The snapshot of "what
            // the biome looked like" is dropped first - the new biome's values are its own.
            MapEditor.Tools.LightingTool.ClearOverride();
            MapEditor.Tools.LightingTool.ForgetBiomeSnapshot();

            if (CustomDungeonManager.CustomDungeonList.ContainsKey(CustomDungeonManager.EnteringCustomDungeon))
            {
                Plugin.Log.LogInfo("Entering Custom Dungeon ONENABLE " + CustomDungeonManager.EnteringCustomDungeon);
                __instance.DungeonLocation = CustomDungeonManager.EnteringCustomDungeon;

                Plugin.Log.LogInfo("Custom Room Count for " + __instance.DungeonLocation + ": " + CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation].NumRooms);
                __instance.NumberOfRooms = CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation].NumRooms;
                // __instance.StartWithBossRoomDoor = true;

                // Statics survive from the previous run; without this the entrance room's
                // Generate hook is skipped (GenCheck stuck true from the last door used).
                GenCheck = false;
                NextRoomConnectionType = ConnectionTypes.Entrance;
                LastDoorDirection = null;

                CustomDungeonManager.EnteringCustomDungeon = FollowerLocation.None;
                
            }
            else
            {
                Plugin.Log.LogInfo("Not a custom dungeon, using default dungeon for " + __instance.DungeonLocation);
            }

        }

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.IsDungeon))]
        [HarmonyPrefix]
        private static bool GameManager_IsDungeon(GameManager __instance, FollowerLocation location, ref bool __result)
        {
            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(location)) return true;
            // Plugin.Log.LogInfo("GameManager ISDungeon custom " + location);
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(Door), nameof(Door.OnTriggerEnter2D))] //*** THIS IS TEMPORARY, change to Health.DealDamage
        [HarmonyPrefix]
        public static bool Door_OnTriggerEnter2D(Door __instance)
        {
            if (BiomeGenerator.Instance == null) return true;
            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(BiomeGenerator.Instance.DungeonLocation)) return true;
            //check the roomtype
            if (__instance.ConnectionType == MMRoomGeneration.GenerateRoom.ConnectionTypes.NextLayer)
            {
                Plugin.Log.LogInfo("Exit Door Triggered for custom dungeon " + BiomeGenerator.Instance.DungeonLocation);
                CustomDungeonManager.CustomDungeonList[BiomeGenerator.Instance.DungeonLocation].ExitDoor();
                return false;
            }
            Plugin.Log.LogInfo("Entering room type " + __instance.ConnectionType);
            NextRoomConnectionType = __instance.ConnectionType;
            LastDoorDirection = __instance.direction.ToString();
            GenCheck = false;
            return true;
        }

        [HarmonyPatch(typeof(LocationManager), nameof(LocationManager.LocationIsDungeon))]
        [HarmonyPrefix]
        public static bool LocationManager_LocationIsDungeon(LocationManager __instance, FollowerLocation location, ref bool __result)
        {
            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(location)) return true;
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(HUD_DisplayName), nameof(HUD_DisplayName.Play))]
        [HarmonyPatch([typeof(string), typeof(int), typeof(HUD_DisplayName.Positions), typeof(HUD_DisplayName.textBlendMode), typeof(int)])]
        [HarmonyPrefix]
        public static bool HUD_DisplayName_Play(ref string Name,
        ref HUD_DisplayName.Positions Position,
        ref HUD_DisplayName.textBlendMode blend,
        ref int winterSeverity)
        {
            if (BiomeGenerator.Instance == null) return true;
            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(BiomeGenerator.Instance.DungeonLocation)) return true;
            Plugin.Log.LogInfo("Custom Dungeon HUD_DisplayName_Play for " + BiomeGenerator.Instance.DungeonLocation);

            var data = CustomDungeonManager.CustomDungeonList[BiomeGenerator.Instance.DungeonLocation];
            Position = data.TitleTextPosition;
            blend = data.TitleTextBlendMode;
            winterSeverity = data.Difficulty;

            return true;
        }

        [HarmonyPatch(typeof(HUD_DisplayName), nameof(HUD_DisplayName.Show))]
        [HarmonyPrefix]
        public static bool HUD_DisplayName_Show(ref string Name)
        {
            if (BiomeGenerator.Instance == null) return true;
            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(BiomeGenerator.Instance.DungeonLocation)) return true;
            Plugin.Log.LogInfo("Custom Dungeon HUD_DisplayName_Show for " + BiomeGenerator.Instance.DungeonLocation);

            var data = CustomDungeonManager.CustomDungeonList[BiomeGenerator.Instance.DungeonLocation];
            Name = data.DungeonName;
            return true;
        }

        [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.Generate), MethodType.Enumerator)]
        [HarmonyPatch([])]
        [HarmonyPostfix]
        public static void GenerateRoom_Generate(GenerateRoom __instance)
        {
            if (BiomeGenerator.Instance == null) return;
            if (GenCheck) return;
            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(BiomeGenerator.Instance.DungeonLocation)) return;

            GenCheck = true;

            // Each new room starts on the biome's lighting. Walking from a room whose blueprint
            // set its own mood into an ordinary generated room would otherwise keep that mood:
            // the override lives on LightingManager, not on the room. A blueprint that carries
            // lighting re-applies it further down, when it loads.
            MapEditor.Tools.LightingTool.ClearOverride();

            // Harmony's enumerator patch supplies a null __instance in some invocations (seen
            // on the boot-time entrance room). GenerateRoom.Instance is the same object -
            // OnEnable assigns it before Generate is ever called - so it is the safer handle.
            var room = __instance != null ? __instance : GenerateRoom.Instance;

            Plugin.Log.LogInfo("GenerateRoom_Generate for custom dungeon " + BiomeGenerator.Instance.DungeonLocation);
            //TODO: this seems to run once for every room instance which makes it run multiple times. 
            Plugin.Log.LogInfo("Room complete status: " + BiomeGenerator.Instance.CurrentRoom.Completed);
            // if not completed, then spawn monsters
            if (!BiomeGenerator.Instance.CurrentRoom.Completed)
            {
                switch (NextRoomConnectionType)
                {
                    case ConnectionTypes.False:
                        Plugin.Log.LogInfo("False Room Generated"); 
                        break;
                    case ConnectionTypes.True:
                        Plugin.Log.LogInfo("True Room Generated");//mob room
                        CustomDungeonManager.CustomDungeonList[BiomeGenerator.Instance.DungeonLocation].SpawnEnemies(room, NextRoomConnectionType);
                        break;
                    case ConnectionTypes.Entrance:
                        Plugin.Log.LogInfo("Entrance Room Generated");
                        break;
                    case ConnectionTypes.Exit:
                        Plugin.Log.LogInfo("Exit Room Generated");
                        break;
                    case ConnectionTypes.Boss:
                        Plugin.Log.LogInfo("Boss Room Generated");
                        break;
                    case ConnectionTypes.DoorRoom:
                        Plugin.Log.LogInfo("Door Room Generated");
                        break;
                    case ConnectionTypes.NextLayer:
                        Plugin.Log.LogInfo("NextLayer Room Generated");
                        break;
                    case ConnectionTypes.DungeonFirstRoom:
                        Plugin.Log.LogInfo("DungeonFirstRoom Generated");
                        break;
                    case ConnectionTypes.LeaderBoss:
                        Plugin.Log.LogInfo("LeaderBoss Room Generated");
                        break;
                    case ConnectionTypes.Tarot:
                        Plugin.Log.LogInfo("Tarot Room Generated");
                        break;
                    case ConnectionTypes.WeaponShop:
                        Plugin.Log.LogInfo("WeaponShop Room Generated");
                        break;
                    case ConnectionTypes.RelicShop:
                        Plugin.Log.LogInfo("RelicShop Room Generated");
                        break;
                    case ConnectionTypes.LoreStoneRoom:
                        Plugin.Log.LogInfo("LoreStoneRoom Generated");
                        break;
                    default:
                        Plugin.Log.LogInfo("Default Room Generated");
                        break;

                }
            }

            // Data-driven dungeons (CTLevelDungeon) build the room's content here, for every
            // connection type. Deliberately outside the Completed guard: revisiting a room
            // regenerates its vanilla content, so blueprint-driven rooms must re-apply too.
            CustomDungeonManager.CustomDungeonList[BiomeGenerator.Instance.DungeonLocation]
                .OnRoomGenerated(room, NextRoomConnectionType);
            // complete room manually with (RoomLockController.RoomCompleted(true,true))
        }
    }
}
