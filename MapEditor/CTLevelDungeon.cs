using CustomSpineLoader.APIHelper;
using MMRoomGeneration;

namespace CustomSpineLoader.MapEditor;

public class CTLevelDungeon : CustomDungeon
{
    public static CTLevelDungeon Instance { get; private set; }

    // The level being played; null when no run is active.
    public CTLevelBlueprint Level;

    public override string SceneName => Level != null && !string.IsNullOrEmpty(Level.SceneName)
        ? Level.SceneName : "Dungeon1";

    public override string DungeonName => Level != null ? Level.LevelName : "Custom Level";

    public override int NumRooms => Level != null ? Level.Rooms.Count : 3;

    // Play Level binds its level before the scene loads; the entry guard must not undo it.
    public override bool DrivesLevelPlayback => true;

    // No caption: the level's own name is already on screen when it loads.
    public override string CaptionTitle => "";
    public override string CaptionSubtext => "";

    // Node blueprints carry their own enemies; random per-room spawns would fight them.
    public override void SpawnEnemies(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType) { }

    public override void OnRoomGenerated(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType)
        => LevelPlayback.OnRoomGenerated(room, connectionType);

    public override void EnterDungeon()
    {
        // A map left installed by Test Map would turn this level's exit into a node picker.
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
