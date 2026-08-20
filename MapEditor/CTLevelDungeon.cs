using CustomSpineLoader.APIHelper;
using MMRoomGeneration;

namespace CustomSpineLoader.MapEditor;

public class CTLevelDungeon : CustomDungeon
{
    public static CTLevelDungeon Instance { get; private set; }

    // The level being played; null when no run is active (entering the dungeon without one
    // behaves like an empty 3-room custom dungeon, which only happens via debug paths).
    public CTLevelBlueprint Level;

    public override string SceneName => Level != null && !string.IsNullOrEmpty(Level.SceneName)
        ? Level.SceneName : "Dungeon1";

    public override string DungeonName => Level != null ? Level.LevelName : "Custom Level";

    public override int NumRooms => Level != null ? Level.Rooms.Count : 3;

    // Play Level binds its level before the scene loads; the entry guard must not undo it.
    public override bool DrivesLevelPlayback => true;

    // No caption: this is a saved level being played, and the level's own name is already on
    // screen when it loads. The editor's caption belongs to the dungeon things are built in.
    public override string CaptionTitle => "";
    public override string CaptionSubtext => "";

    // Node blueprints carry their own enemies with exact positions; random per-room spawns
    // would fight them.
    public override void SpawnEnemies(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType) { }

    public override void OnRoomGenerated(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType)
        => LevelPlayback.OnRoomGenerated(room, connectionType);

    public override void EnterDungeon()
    {
        // Play Level is one level and then the completion screen. A map left installed by a
        // Test Map press would otherwise turn its exit into a node picker.
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
