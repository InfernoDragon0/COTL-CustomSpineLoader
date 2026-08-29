using System;
using CustomSpineLoader.APIHelper;
using HarmonyLib;
using MMBiomeGeneration;
using MMRoomGeneration;
using MMTools;
using UnityEngine;
using static MMRoomGeneration.GenerateRoom;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class DungeonPatches
    {
        public static ConnectionTypes NextRoomConnectionType = ConnectionTypes.Entrance;
        public static bool GenCheck = false;
        public static string LastDoorDirection = null;

        public static void ResetRoomHandoff()
        {
            GenCheck = false;
            NextRoomConnectionType = ConnectionTypes.Entrance;
            LastDoorDirection = null;
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

        [HarmonyPatch(typeof(LocationManager), nameof(LocationManager.LocationIsDungeon))]
        [HarmonyPrefix]
        public static bool LocationManager_LocationIsDungeon(LocationManager __instance, FollowerLocation location, ref bool __result)
        {
            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(location)) return true;
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(global::Map.MapManager), nameof(global::Map.MapManager.EnterNode))]
        [HarmonyPostfix]
        private static void MapManager_EnterNode(global::Map.Node mapNode)
        {
            if (!MapEditor.DungeonMapPlayback.Active || mapNode == null) return;

            try
            {
                MapEditor.DungeonMapPlayback.OnNodeEntered(mapNode);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("MapEditor: dungeon map node entry failed: " + e);
            }
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

        [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.SetRoom))]
        [HarmonyPostfix]
        public static void BiomeGenerator_SetRoom() => AssertRoomLighting();

        [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.RoomBecameActive))]
        [HarmonyPostfix]
        public static void BiomeGenerator_RoomBecameActive() => AssertRoomLighting();

        private static void AssertRoomLighting()
        {
            try
            {
                MapEditor.Tools.LightingTool.OnRoomEntered();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: room-change lighting failed: " + e.Message);
            }
        }

        [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.OnEnable))]
        [HarmonyPrefix]
        private static void BiomeGenerator_OnEnable(BiomeGenerator __instance)
        {
            var entering = CustomDungeonManager.EnteringCustomDungeon;
            var bindsOwnLevel = CustomDungeonManager.CustomDungeonList.TryGetValue(entering, out var target) &&
                                target.DrivesLevelPlayback;

            if (entering != FollowerLocation.None && !bindsOwnLevel)
                MapEditor.LevelPlayback.Stop();

            MapEditor.Tools.LightingTool.ClearOverride();
            MapEditor.Tools.LightingTool.ForgetRoomLighting();

            MapEditor.Tools.TriggerCameraActions.ResetAll();

            MapEditor.Tools.CTMapTrigger.ResetSequenceState();

            GenCheck = false;
            NextRoomConnectionType = ConnectionTypes.Entrance;
            LastDoorDirection = null;

            RoomLockNet.Disarm();

            if (CustomDungeonManager.CustomDungeonList.ContainsKey(CustomDungeonManager.EnteringCustomDungeon))
            {
                Plugin.Log.LogInfo("Entering Custom Dungeon ONENABLE " + CustomDungeonManager.EnteringCustomDungeon);
                __instance.DungeonLocation = CustomDungeonManager.EnteringCustomDungeon;

                Plugin.Log.LogInfo("Custom Room Count for " + __instance.DungeonLocation + ": " + CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation].NumRooms);
                __instance.NumberOfRooms = CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation].NumRooms;

                if (__instance.StartWithBossRoomDoor)
                {
                    Plugin.Log.LogInfo("Custom dungeon: turning off StartWithBossRoomDoor - it would " +
                                       "add an unaccounted room and take over the start position.");
                    __instance.StartWithBossRoomDoor = false;
                }

                CustomDungeonManager.EnteringCustomDungeon = FollowerLocation.None;

                var entered = CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation];

                try
                {
                    entered.OnBiomeReady(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Custom dungeon OnBiomeReady failed: " + e);
                }

                try
                {
                    if (!string.IsNullOrEmpty(entered.CaptionTitle))
                        __instance.StartCoroutine(CustomDungeon.ShowCaptionWhenPlayable(entered));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Custom dungeon caption failed: " + e.Message);
                }
            }
            else
            {
                Plugin.Log.LogInfo("Not a custom dungeon, using default dungeon for " + __instance.DungeonLocation);
            }

        }

        [HarmonyPatch(typeof(Door), nameof(Door.OnTriggerEnter2D))] //*** THIS IS TEMPORARY, change to Health.DealDamage
        [HarmonyPrefix]
        public static bool Door_OnTriggerEnter2D(Door __instance, Collider2D collision)
        {
            if (BiomeGenerator.Instance == null) return true;

            var customDungeon =
                CustomDungeonManager.CustomDungeonList.ContainsKey(BiomeGenerator.Instance.DungeonLocation);

            if (!customDungeon && !MapEditor.LevelPlayback.Active) return true;

            if (!IsPlayerUsingDoor(__instance, collision)) return true;

            if (customDungeon && __instance.ConnectionType == MMRoomGeneration.GenerateRoom.ConnectionTypes.NextLayer)
            {
                Plugin.Log.LogInfo("Exit Door Triggered for custom dungeon " + BiomeGenerator.Instance.DungeonLocation);

                __instance.Used = true;

                CustomDungeonManager.CustomDungeonList[BiomeGenerator.Instance.DungeonLocation].ExitDoor();
                return false;
            }
            Plugin.Log.LogInfo("Entering room type " + __instance.ConnectionType);
            NextRoomConnectionType = __instance.ConnectionType;
            LastDoorDirection = __instance.direction.ToString();
            GenCheck = false;
            return true;
        }

        private static bool IsPlayerUsingDoor(Door door, Collider2D collision)
        {
            if (door == null || collision == null) return false;

            var player = collision.gameObject.GetComponent<PlayerFarming>();
            if (player == null) return false;

            if (MMTransition.IsPlaying || door.Used || player.GoToAndStopping) return false;

            return door.ConnectionType != MMRoomGeneration.GenerateRoom.ConnectionTypes.False &&
                   door.ConnectionType != MMRoomGeneration.GenerateRoom.ConnectionTypes.LeaderBoss;
        }

        [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.Generate), MethodType.Enumerator)]
        [HarmonyPatch([])]
        [HarmonyPostfix]
        public static void GenerateRoom_Generate(GenerateRoom __instance)
        {
            if (BiomeGenerator.Instance == null) return;
            if (GenCheck) return;

            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(BiomeGenerator.Instance.DungeonLocation))
            {
                if (!MapEditor.LevelPlayback.Active) return;

                GenCheck = true;
                MapEditor.Tools.LightingTool.OnRoomEntered();
                MapEditor.LevelPlayback.OnRoomGenerated(
                    __instance != null ? __instance : GenerateRoom.Instance, NextRoomConnectionType);
                return;
            }

            GenCheck = true;

            try
            {
                GenerateForCustomDungeon(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Custom dungeon room content failed; the room generates empty: " + e);
            }
        }

        private static void GenerateForCustomDungeon(GenerateRoom __instance)
        {
            MapEditor.Tools.LightingTool.OnRoomEntered();

            var room = __instance != null ? __instance : GenerateRoom.Instance;

            Plugin.Log.LogInfo("GenerateRoom_Generate for custom dungeon " + BiomeGenerator.Instance.DungeonLocation);

            var currentRoom = BiomeGenerator.Instance.CurrentRoom;
            var completed = currentRoom != null && currentRoom.Completed;
            Plugin.Log.LogInfo("Room complete status: " + completed);
            if (!completed)
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

            CustomDungeonManager.CustomDungeonList[BiomeGenerator.Instance.DungeonLocation]
                .OnRoomGenerated(room, NextRoomConnectionType);

            if (!MapEditor.LevelPlayback.Active) RoomLockNet.Arm(BiomeGenerator.Instance);
        }
    }
}
