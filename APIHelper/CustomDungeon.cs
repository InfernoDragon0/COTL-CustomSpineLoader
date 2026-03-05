using System;
using Lamb.UI;
using Lamb.UI.DeathScreen;
using MMTools;

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
    public virtual FollowerLocation Location { get; set; } = FollowerLocation.Dungeon1_1;

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
        MMTransition.Play(MMTransition.TransitionType.ChangeRoomWaitToResume, MMTransition.Effect.BlackFade, this.SceneName, 1f, "", new System.Action(this.FadeSave));
        GameManager.GetInstance().OnConversationNew();
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
