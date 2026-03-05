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

        [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.OnEnable))]
        [HarmonyPrefix]
        private static void BiomeGenerator_OnEnable(BiomeGenerator __instance)
        {
            if (CustomDungeonManager.CustomDungeonList.ContainsKey(CustomDungeonManager.EnteringCustomDungeon))
            {
                Plugin.Log.LogInfo("Entering Custom Dungeon ONENABLE " + CustomDungeonManager.EnteringCustomDungeon);
                __instance.DungeonLocation = CustomDungeonManager.EnteringCustomDungeon;

                Plugin.Log.LogInfo("Custom Room Count for " + __instance.DungeonLocation + ": " + CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation].NumRooms);
                __instance.NumberOfRooms = CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation].NumRooms;
                // __instance.StartWithBossRoomDoor = true;

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

            Plugin.Log.LogInfo("GenerateRoom_Generate for custom dungeon " + BiomeGenerator.Instance.DungeonLocation);
            //Still need to find out which type of room it is, and if it is already completed or not. 
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
            // complete room manually with (RoomLockController.RoomCompleted(true,true))
        }
    }
}
