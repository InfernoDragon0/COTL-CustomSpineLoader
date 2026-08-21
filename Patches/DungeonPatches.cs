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
        // Direction of the last door the player walked through; the next room's blueprint
        // entry prefers the opposite side.
        public static string LastDoorDirection = null;

        // Puts the door hand-off back to "arriving fresh". Doors normally do this; a floor
        // entered from the adventure map has no door to do it, and a latched GenCheck would make
        // the room hook skip the first room of the level.
        public static void ResetRoomHandoff()
        {
            GenCheck = false;
            NextRoomConnectionType = ConnectionTypes.Entrance;
            LastDoorDirection = null;
        }

        // Every route into a floor from the adventure map goes through here, so it is where an
        // authored node hands its level over. A postfix rather than a prefix because the vanilla
        // body is what sets up the floor this then adjusts - and it is still early enough:
        // Regenerate defers the whole generation into an MMTransition callback.
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

        [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.OnEnable))]
        [HarmonyPrefix]
        private static void BiomeGenerator_OnEnable(BiomeGenerator __instance)
        {
            // A dungeon entry that does not bring its own level ends the run in progress, so its
            // statics never leak into an unrelated scene. FollowerLocation.None means "no entry
            // pending" (a later re-enable of the same biome), which must leave a run alone.
            //
            // Asking the dungeon rather than naming one: CTLevelDungeon used to be the only thing
            // that bound a level, and hardcoding it here meant a map dungeon - which binds its
            // start node's level before the scene loads - had that binding torn down on arrival.
            var entering = CustomDungeonManager.EnteringCustomDungeon;
            var bindsOwnLevel = CustomDungeonManager.CustomDungeonList.TryGetValue(entering, out var target) &&
                                target.DrivesLevelPlayback;

            if (entering != FollowerLocation.None && !bindsOwnLevel)
                MapEditor.LevelPlayback.Stop();

            // Any biome coming up starts on its own lighting: the override is global state and
            // would otherwise follow the player into the next room or scene. What each room of
            // the old biome asked for goes with it - these are new rooms.
            MapEditor.Tools.LightingTool.ClearOverride();
            MapEditor.Tools.LightingTool.ForgetRoomLighting();

            // Same reasoning for the camera: a trigger's offset or zoom lives on a rig that
            // outlives the room, so it would follow the player into the next one.
            MapEditor.Tools.TriggerCameraActions.ResetAll();

            // A trigger sequence whose coroutine died with the previous scene would leave the
            // global owner set - and every trigger in every later room silently blocked.
            MapEditor.Tools.CTMapTrigger.ResetSequenceState();

            // The room hand-off statics reset for every biome, not only ours: entering a vanilla
            // dungeon after a custom run otherwise starts with the last custom door still latched
            // (a stale direction, and GenCheck in whatever state the last room left it).
            GenCheck = false;
            NextRoomConnectionType = ConnectionTypes.Entrance;
            LastDoorDirection = null;

            if (CustomDungeonManager.CustomDungeonList.ContainsKey(CustomDungeonManager.EnteringCustomDungeon))
            {
                Plugin.Log.LogInfo("Entering Custom Dungeon ONENABLE " + CustomDungeonManager.EnteringCustomDungeon);
                __instance.DungeonLocation = CustomDungeonManager.EnteringCustomDungeon;

                Plugin.Log.LogInfo("Custom Room Count for " + __instance.DungeonLocation + ": " + CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation].NumRooms);
                __instance.NumberOfRooms = CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation].NumRooms;
                // __instance.StartWithBossRoomDoor = true;

                CustomDungeonManager.EnteringCustomDungeon = FollowerLocation.None;

                // Last, and deliberately here rather than in EnterDungeon: this is the first
                // moment in the new scene, so whatever the dungeon sets up now cannot be undone
                // by the teardown of the scene it came from.
                var entered = CustomDungeonManager.CustomDungeonList[__instance.DungeonLocation];

                try
                {
                    entered.OnBiomeReady(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Custom dungeon OnBiomeReady failed: " + e);
                }

                // Started here rather than from OnBiomeReady so an override that forgets to call
                // base cannot lose it. The biome is the host because it lives for the scene the
                // caption belongs to, and dies with it.
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
        public static bool Door_OnTriggerEnter2D(Door __instance, Collider2D collision)
        {
            if (BiomeGenerator.Instance == null) return true;

            var customDungeon =
                CustomDungeonManager.CustomDungeonList.ContainsKey(BiomeGenerator.Instance.DungeonLocation);

            // A dungeon-map node bound to a level plays inside the vanilla dungeon it was entered
            // from, so the hand-off below has to be recorded there too - without it GenCheck stays
            // latched from the last door and the next room's blueprint is never applied.
            if (!customDungeon && !MapEditor.LevelPlayback.Active) return true;

            // Vanilla's own filter, repeated here because this prefix used to skip it entirely and
            // acted on whatever touched the trigger. A door is walked through by a player, once,
            // while nothing else is going on; everything else - a follower, a thrown item, a
            // knocked-back enemy, the player's own scripted walk-in on arrival - is not a door
            // being used. Letting those through set the room hand-off at arbitrary moments, which
            // re-applied blueprints to rooms already built (their doors visibly moving to another
            // room's authored positions), and fired the exit door on arrival, which reopened the
            // dungeon map the instant a node was entered.
            if (!IsPlayerUsingDoor(__instance, collision)) return true;

            //check the roomtype
            if (customDungeon && __instance.ConnectionType == MMRoomGeneration.GenerateRoom.ConnectionTypes.NextLayer)
            {
                Plugin.Log.LogInfo("Exit Door Triggered for custom dungeon " + BiomeGenerator.Instance.DungeonLocation);

                // Vanilla marks the door spent as it takes it; this branch never reaches vanilla,
                // so it marks it here. Otherwise every further overlap re-runs the exit - the map
                // reopening on top of itself.
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

        // Door.OnTriggerEnter2D's own opening conditions, checked before this patch acts on the
        // trigger. Kept in the same order as the original so the two cannot drift.
        private static bool IsPlayerUsingDoor(Door door, Collider2D collision)
        {
            if (door == null || collision == null) return false;

            var player = collision.gameObject.GetComponent<PlayerFarming>();
            if (player == null) return false;

            // GoToAndStopping is the scripted walk: vanilla's arrival animation, and the map
            // editor's own entry routine, both drive the player through a doorway that way.
            if (MMTransition.IsPlaying || door.Used || player.GoToAndStopping) return false;

            return door.ConnectionType != MMRoomGeneration.GenerateRoom.ConnectionTypes.False &&
                   door.ConnectionType != MMRoomGeneration.GenerateRoom.ConnectionTypes.LeaderBoss;
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

        // Room lighting is global state on LightingManager, so every arrival has to re-assert what
        // the arriving room asked for. Generation is the wrong signal on its own: the game builds a
        // room once and only re-activates it afterwards, so walking back into one announced nothing
        // and its lighting never changed - the symptom being a custom mood that follows the player
        // out of the room that set it and a revisited room that comes back plain.
        //
        // Both arrival points are hooked because they answer different halves of it: SetRoom is the
        // moment the current room changes, which is early enough that the swap happens behind the
        // transition's fade, while RoomBecameActive covers arrivals that do not go through it.
        // LightingTool ignores the second announcement of the same arrival, so the pair is safe.
        [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.SetRoom))]
        [HarmonyPostfix]
        public static void BiomeGenerator_SetRoom() => AssertRoomLighting();

        [HarmonyPatch(typeof(BiomeGenerator), nameof(BiomeGenerator.RoomBecameActive))]
        [HarmonyPostfix]
        public static void BiomeGenerator_RoomBecameActive() => AssertRoomLighting();

        // A lighting slip is not worth taking a room change down with it.
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

        [HarmonyPatch(typeof(GenerateRoom), nameof(GenerateRoom.Generate), MethodType.Enumerator)]
        [HarmonyPatch([])]
        [HarmonyPostfix]
        public static void GenerateRoom_Generate(GenerateRoom __instance)
        {
            if (BiomeGenerator.Instance == null) return;
            if (GenCheck) return;

            if (!CustomDungeonManager.CustomDungeonList.ContainsKey(BiomeGenerator.Instance.DungeonLocation))
            {
                // Not one of our dungeons - but a dungeon-map node can bind a level inside a
                // vanilla one, and that level still needs its rooms built.
                if (!MapEditor.LevelPlayback.Active) return;

                GenCheck = true;
                MapEditor.Tools.LightingTool.OnRoomEntered();
                MapEditor.LevelPlayback.OnRoomGenerated(
                    __instance != null ? __instance : GenerateRoom.Instance, NextRoomConnectionType);
                return;
            }

            GenCheck = true;

            // Everything below runs inside GenerateRoom.Generate's own MoveNext: an uncaught
            // throw kills the generation coroutine and leaves a black, soft-locked room. Bad
            // content (a blueprint that fails to apply, an enemy that fails to spawn) is worth a
            // broken room's worth of logging, never a broken run.
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
            // The earliest a freshly generated room can be put on its own lighting. Revisits come
            // through the arrival hooks above instead; this one only ever sees a first arrival.
            MapEditor.Tools.LightingTool.OnRoomEntered();

            // Harmony's enumerator patch supplies a null __instance in some invocations (seen
            // on the boot-time entrance room). GenerateRoom.Instance is the same object -
            // OnEnable assigns it before Generate is ever called - so it is the safer handle.
            var room = __instance != null ? __instance : GenerateRoom.Instance;

            Plugin.Log.LogInfo("GenerateRoom_Generate for custom dungeon " + BiomeGenerator.Instance.DungeonLocation);

            // CurrentRoom can lag the generation hook by a step on the boot-time entrance.
            var currentRoom = BiomeGenerator.Instance.CurrentRoom;
            var completed = currentRoom != null && currentRoom.Completed;
            Plugin.Log.LogInfo("Room complete status: " + completed);
            // if not completed, then spawn monsters
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

            // Data-driven dungeons (CTLevelDungeon) build the room's content here, for every
            // connection type. Deliberately outside the Completed guard: revisiting a room
            // regenerates its vanilla content, so blueprint-driven rooms must re-apply too.
            CustomDungeonManager.CustomDungeonList[BiomeGenerator.Instance.DungeonLocation]
                .OnRoomGenerated(room, NextRoomConnectionType);
            // complete room manually with (RoomLockController.RoomCompleted(true,true))
        }
    }
}
