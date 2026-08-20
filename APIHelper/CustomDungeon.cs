using System;
using System.Collections.Generic;
using COTL_API.CustomEnemy;
using Lamb.UI;
using Lamb.UI.DeathScreen;
using MMRoomGeneration;
using MMTools;
using UnityEngine;
using static MMRoomGeneration.GenerateRoom;
using Pathfinding;
using MMBiomeGeneration;
using System.Collections;

namespace CustomSpineLoader.APIHelper;

public class CustomDungeon
{
    internal string ModPrefix = "";
    public virtual string InternalName { get; } = "";
    public virtual string SceneName => "Dungeon1";
    public virtual string DungeonName => "Meow Dungeon";
    public virtual int Difficulty => 1;
    public HUD_DisplayName.textBlendMode TitleTextBlendMode => HUD_DisplayName.textBlendMode.FrogBoss;
    public HUD_DisplayName.Positions TitleTextPosition => HUD_DisplayName.Positions.Centre;
    public virtual int NumRooms => 3;

    // True for dungeons that bind a level blueprint before the scene loads. Entering any other
    // dungeon ends a level run in progress, so its state cannot leak into an unrelated scene -
    // see BiomeGenerator_OnEnable.
    public virtual bool DrivesLevelPlayback => false;

    // Which of the vanilla difficulty layers this dungeon's rooms draw their encounters from,
    // 1 (easiest) to 4. It matters more than it looks: see EnterDungeon.
    public virtual int DungeonLayer => 1;

    // The caption a dungeon announces itself with, in the trigger tool's screen-text style: a
    // title and a smaller line under it, bottom left.
    //
    // The default is the editor's own name, because a plain CustomDungeon is the room things are
    // built in. A dungeon that is somewhere the player was *sent* rather than somewhere the
    // author is standing overrides these back to empty - the game already puts a dungeon's name
    // on screen when you arrive, and a second announcement over the top of it is noise.
    public virtual string CaptionTitle => "The Worldshaper";

    public virtual string CaptionSubtext =>
        "Unleash your creativity. You have entered CultTweaker's custom dungeon builder.";

    public virtual float CaptionSeconds => 5f;

    // Deliberately not shown at biome-ready: the room is not built then, the fade is still up,
    // and the player is parked. A caption that lands while the screen is black has been shown to
    // nobody. This waits for the transition to end and the player to have control back, then
    // holds a beat so the caption arrives after the room, not with it.
    public static IEnumerator ShowCaptionWhenPlayable(CustomDungeon dungeon)
    {
        if (dungeon == null || string.IsNullOrEmpty(dungeon.CaptionTitle)) yield break;

        // Realtime: the transition itself runs at timeScale 0.
        var deadline = Time.unscaledTime + 30f;

        while (MMTransition.IsPlaying && Time.unscaledTime < deadline) yield return null;

        while (Time.unscaledTime < deadline)
        {
            var player = PlayerFarming.Instance;
            if (player != null && player.state != null && !player.GoToAndStopping &&
                player.state.CURRENT_STATE != StateMachine.State.InActive)
                break;

            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.35f);

        MapEditor.Tools.TriggerScreenText.Show(MapEditor.Tools.TriggerScreenText.Mode.Caption,
            dungeon.CaptionTitle, dungeon.CaptionSubtext, dungeon.CaptionSeconds);
    }
    public virtual FollowerLocation Location { get; set; } = FollowerLocation.Dungeon1_1;

    public List<Enemy> NormalEnemyList = []; //TODO: expand on this, become a blueprint class instead
    public int mobsPerRoom = 3;

    private Vector3 GetRandomWalkablePosition()
    {
        var gg = AstarPath.active.graphs[0] as GridGraph;
        if (gg == null) 
        {
            Plugin.Log.LogWarning("AStar GridGraph not found, using fallback position for enemy spawn.");
            return PlayerFarming.Instance.transform.position + UnityEngine.Random.insideUnitSphere * 2f; // fallback
        }

        List<GridNode> walkableNodes = new List<GridNode>();
        foreach (var node in gg.nodes)
        {
            if (node.Walkable) walkableNodes.Add(node as GridNode);
        }

        if (walkableNodes.Count == 0) 
        {
            Plugin.Log.LogWarning("No walkable nodes found in AStar graph, using fallback position for enemy spawn.");
            return PlayerFarming.Instance.transform.position + UnityEngine.Random.insideUnitSphere * 2f;
        }

        var randomNode = walkableNodes[UnityEngine.Random.Range(0, walkableNodes.Count)];
        return (Vector3)randomNode.position;
    }

    public virtual void EnterDungeon()
    {
        CustomDungeonManager.EnteringCustomDungeon = this.Location;
        Plugin.Log.LogInfo($"Entering Custom Dungeon: {this.Location} with scene {this.SceneName} and {this.NumRooms} rooms.");
        AudioManager.Instance.StopCurrentMusic();
        AudioManager.Instance.StopCurrentAtmos();
        AudioManager.Instance.PlayOneShot("event:/Stings/boss_door_complete");
        AudioManager.Instance.PlayOneShot("event:/ui/map_location_appear");
        PlayerFarming.ReloadAllFaith();

        MMTransition.StopCurrentTransition();
        if (this.Location == FollowerLocation.Dungeon1_5 || this.Location == FollowerLocation.Dungeon1_6)
            DataManager.Instance.CurrentDLCNodeType = DungeonWorldMapIcon.NodeType.Dungeon5_MiniBoss;
        Interaction_BaseDungeonDoor.GetFloor(this.Location);

        // GetFloor reads the layer out of save data keyed by location, and a minted location has
        // none - DataManager.GetDungeonLayer returns 0 for anything it does not recognise, and
        // NextDungeonLayer stores that as the current layer. Every island encounter then reports
        // "not available on this layer" (IslandPiece.AvailableOnLayer has no case for 0), so a
        // vanilla floor in a custom dungeon generates rooms with nothing in them at all - no
        // enemies, no resources, and the generator logging that it has run out of encounters.
        if (!GameManager.DungeonUseAllLayers)
        {
            GameManager.CurrentDungeonLayer = UnityEngine.Mathf.Clamp(this.DungeonLayer, 1, 4);
            Plugin.Log.LogInfo($"Custom dungeon {this.Location} runs on encounter layer " +
                               $"{GameManager.CurrentDungeonLayer}.");
        }
        MMTransition.Play(MMTransition.TransitionType.ChangeRoomWaitToResume, MMTransition.Effect.BlackFade, this.SceneName, 1f, "", new System.Action(this.FadeSave));
        GameManager.GetInstance().OnConversationNew();
    }

    // Called from BiomeGenerator.OnEnable in the dungeon's own scene, once per entry, before
    // any room is generated. That is the last point at which state can be set up for the run and
    // the first at which nothing left over from the scene being left behind can tear it down -
    // anything bound before the transition has to survive a scene load and every teardown along
    // the way, and a dungeon that binds a level did not.
    public virtual void OnBiomeReady(BiomeGenerator biome) { }

    // Called once per newly generated room, for every connection type (SpawnEnemies is only
    // invoked for True rooms). Subclasses that build room content from data - CTLevelDungeon
    // applying node blueprints - hook here. Default: vanilla-generated rooms stay as they are.
    public virtual void OnRoomGenerated(GenerateRoom room, ConnectionTypes connectionType) { }

    public virtual void SpawnEnemies(GenerateRoom room, ConnectionTypes connectionType)
    {
        Plugin.Log.LogInfo($"Spawning enemies for connection type {connectionType} in custom dungeon {this.Location}");
        switch (connectionType)
        {
            case ConnectionTypes.True:
                Plugin.Log.LogInfo("Spawning true test");
                if (NormalEnemyList.Count == 0)
                {
                    Plugin.Log.LogWarning("No enemies to spawn for this dungeon.");
                    if (RoomLockController.RoomLockControllers.Count > 0)
                        RoomLockController.RoomCompleted();
                    return;
                }

                for (int i = 0; i < mobsPerRoom; i++)
                {
                    var enemy = NormalEnemyList[UnityEngine.Random.Range(0, NormalEnemyList.Count)];
                    var pos = GetRandomWalkablePosition();
                    var spawned = CustomEnemyManager.Spawn(enemy, pos);

                    //TODO: destroy the script controller, and spine components
                    // then, apply a new spine component and the script controller from the enemy

                    spawned.health.OnDie += (Attacker,
                        AttackLocation,
                        Victim,
                        AttackType,
                        AttackFlags) =>
                    {
                        Plugin.Log.LogInfo("Custom Enemy died, checking if room is clear...");
                        if (Health.team2.Count - 1 == 0)
                        {
                            Plugin.Log.LogInfo("Room is clear!");
                            RoomLockController.RoomCompleted();
                        }
                        else
                        {
                            Plugin.Log.LogInfo($"Enemies remaining: {Health.team2.Count}");
                        }
                    };
                    
                    RoomLockController roomLockController1 = null;
                    float num1 = float.PositiveInfinity;
                    foreach (RoomLockController roomLockController2 in RoomLockController.RoomLockControllers)
                    {
                        if (roomLockController2.CanApplyUnitCorrection)
                        {
                            float num2 = Vector3.Distance(spawned.transform.position, roomLockController2.transform.position);
                            if (num2 < num1)
                            {
                                roomLockController1 = roomLockController2;
                                num1 = num2;
                            }
                        }
                    }

                    if (roomLockController1 != null)
                    {
                        Plugin.Log.LogInfo($"Found RoomLockController for enemy position correction: {roomLockController1.name}");
                        //spawn in bounds
                        spawned.transform.position = roomLockController1.BlockingCollider.transform.position - roomLockController1.BlockingCollider.transform.up * 0.5f;
                        //update the position to the playerposition
                    }
                    else
                    {
                        Plugin.Log.LogWarning("No RoomLockController found for enemy position correction.");
                    }

                    var moveToPosition = spawned.transform.position;
                    Plugin.Log.LogInfo("Starting coroutine to reposition enemy after spawn...");
                    spawned.StartCoroutine(RepositionEnemies(spawned));
                }

                break;
            case ConnectionTypes.Boss:
                Plugin.Log.LogInfo("Spawning boss test");
                break;
        }
    }

    public IEnumerator RepositionEnemies(UnitObject spawned)
    {
        Plugin.Log.LogInfo($"Repositioning enemies {spawned})");
        yield return new WaitForSeconds(.5f);
        var tries = 10;
        while (tries > 0)
        {
            if (Door.GetFirstNonEntranceDoor() == null && tries > 1)
            {
                Plugin.Log.LogWarning("Door not found for enemy repositioning, retrying...");
                yield return new WaitForSeconds(.5f);
                tries--;
                continue;
            }

            if (tries <= 1)
            {
                Plugin.Log.LogWarning("Failed to find door for enemy repositioning after multiple attempts, placing enemy in fallback position.");
                //check if 0th door exists in door.doors
                if (Door.Doors.Count > 0)
                {
                    var fallbackDoor = Door.Doors[0];
                    var moveToPosition2 = fallbackDoor.transform.position + fallbackDoor.GetDoorDirection() * 7.3f;
                    spawned.transform.position = moveToPosition2;
                    Plugin.Log.LogInfo("Enemy placed on fallback door position.");
                }
                else
                {
                    Plugin.Log.LogWarning("No doors found in the scene for enemy repositioning, using current position.");
                }
                yield break;
            }

            var moveToPosition = Door.GetFirstNonEntranceDoor().transform.position;
            moveToPosition += Door.GetFirstNonEntranceDoor().GetDoorDirection() * 7.3f; //TODO: door has not generated yet.
            spawned.transform.position = moveToPosition;
            Plugin.Log.LogInfo("Enemy placed on entrance position.");
            yield break;

        }
        Plugin.Log.LogInfo("Failed to reposition enemy after multiple attempts, leaving in original position.");
        
    }
    
    public virtual void ExitDoor()
    {
        //Default behavior for exiting the final room is to exit to base.
        //you can override this behavior to exit into a different scene for a cutscene, etc.
        MonoSingleton<UIManager>.Instance.ShowDeathScreenOverlay(UIDeathScreenOverlayController.Results.Completed);
        AudioManager.Instance.PlayOneShot("event:/pentagram_platform/pentagram_platform_curse") ;
        AudioManager.Instance.PlayOneShot("event:/ui/heretics_defeated");
        AudioManager.Instance.PlayMusic("event:/music/game_over/game_over");
    }
    
    private void FadeSave() => SaveAndLoad.Save();
}
