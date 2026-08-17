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

    // Node blueprints carry their own enemies with exact positions; random per-room spawns
    // would fight them.
    public override void SpawnEnemies(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType) { }

    public override void OnRoomGenerated(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType)
        => LevelPlayback.OnRoomGenerated(room, connectionType);

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
