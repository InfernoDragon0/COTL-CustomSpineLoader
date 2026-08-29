using CustomSpineLoader.APIHelper;
using MMRoomGeneration;

namespace CustomSpineLoader.MapEditor;

public class CTLevelDungeon : CustomDungeon
{
    public static CTLevelDungeon Instance { get; private set; }

    public CTLevelBlueprint Level;

    public override string SceneName => Level != null && !string.IsNullOrEmpty(Level.SceneName)
        ? Level.SceneName : "Dungeon1";

    public override string DungeonName => Level != null ? Level.LevelName : "Custom Level";

    public override int NumRooms
    {
        get
        {
            if (Level == null) return 3;
            if (LevelLayout.IsAuthored(Level)) return Level.Rooms.Count;

            return UnityEngine.Mathf.Max(1, Level.Rooms.Count - 2);
        }
    }

    public override bool DrivesLevelPlayback => true;

    public override string CaptionTitle => "";
    public override string CaptionSubtext => "";

    public override void SpawnEnemies(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType) { }

    public override void OnRoomGenerated(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType)
        => LevelPlayback.OnRoomGenerated(room, connectionType);

    public override void EnterDungeon()
    {
        DungeonMapPlayback.Clear();
        base.EnterDungeon();
    }

    public override void ExitDoor()
    {
        LevelPlayback.Stop();
        base.ExitDoor();
    }

    public static void Register()
    {
        if (Instance != null) return;
        Instance = new CTLevelDungeon
        {
            Location = FollowerLocation.Dungeon2_1
        };
        CustomDungeonManager.Add(Instance);
    }
}
