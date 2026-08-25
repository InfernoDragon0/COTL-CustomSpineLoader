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

    // What to hand vanilla's walk so the floor comes out the length the level asks for.
    //
    // NumberOfRooms is not a room count. CreateRandomWalk seeds a room *before* its loop and then
    // adds NumberOfRooms more, and PlaceEntranceAndExit appends the end-of-floor room on top of
    // that - so a floor arrives at NumberOfRooms + 2. Passing the level's room count straight
    // through is what made a two-room level generate four.
    //
    // The two are not padding: they are the room the player arrives in and the room with the way
    // out, and a random-walk level carries them as its first and last entries (LevelTool's
    // EnsureEndRooms). So subtracting them here is subtracting exactly those, and the count in the
    // editor is the floor the player walks.
    //
    // Never below one: PlaceEntranceAndExit picks the entrance and the exit from rooms with exactly
    // one connection, and a lone room has none, so it dereferences null. That is also why a
    // random-walk level always keeps at least one room of its own between the ends.
    //
    // None of this applies to an authored layout - LevelLayout builds the graph itself and the walk
    // never runs - so that reports its real length, which is what the entry log says.
    public override int NumRooms
    {
        get
        {
            if (Level == null) return 3;
            if (LevelLayout.IsAuthored(Level)) return Level.Rooms.Count;

            return UnityEngine.Mathf.Max(1, Level.Rooms.Count - 2);
        }
    }

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
