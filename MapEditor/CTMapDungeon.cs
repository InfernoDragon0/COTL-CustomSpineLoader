using System;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using MMRoomGeneration;

namespace CustomSpineLoader.MapEditor;

// A dungeon map *is* a dungeon: one node is one level blueprint, and the graph of them is the
// whole run. So every saved map registers one of these, and there is no separate dungeon file to
// keep in step with it.
public class CTMapDungeon : CustomDungeon
{
    public CTDungeonMap Map;

    public override string InternalName => "CultTweaker_" + (Map?.MapName ?? "dungeon");

    public override string SceneName =>
        Map != null && !string.IsNullOrEmpty(Map.SceneName) ? Map.SceneName : "Dungeon1";

    public override string DungeonName => Map?.MapName ?? "Custom Dungeon";

    public override int NumRooms
    {
        get
        {
            var level = StartLevel();
            return level != null ? level.Rooms.Count : 3;
        }
    }

    // The start node's level is bound before the scene loads; the entry guard must not undo it.
    public override bool DrivesLevelPlayback => true;

    // An authored dungeon is not the editor's dungeon, so it does not inherit its caption. Its
    // own name is already announced on arrival; give it a title and subtext here if a map should
    // introduce itself with more than that.
    public override string CaptionTitle => "";
    public override string CaptionSubtext => "";

    // Node blueprints carry their own enemies at exact positions; random per-room spawns fight them.
    public override void SpawnEnemies(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType) { }

    public override void OnRoomGenerated(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType)
        => LevelPlayback.OnRoomGenerated(room, connectionType);

    public override void EnterDungeon()
    {
        // Remembered on entry, not looked up on exit: by the time the exit door asks which map
        // this dungeon uses, the thing that knew is out of reach.
        DungeonMapPlayback.UseMap(Map?.MapName);

        base.EnterDungeon();
    }

    // The bottom node is the floor the player arrives in, and its level is bound here rather than
    // in EnterDungeon. Binding before the transition looked right and was not: the level run is
    // static state, and everything between the button press and the new scene - the editor
    // closing, the old scene tearing down, the entry guard - can end it. By the time the biome
    // enables, all of that is behind us.
    //
    // The map is not shown yet: the game's renderer marks the first node visited the first time
    // the selector opens, so arriving in it and meeting the map afterwards lines up.
    public override void OnBiomeReady(MMBiomeGeneration.BiomeGenerator biome)
    {
        var level = StartLevel();
        if (level == null)
        {
            LevelPlayback.Stop();
            return;
        }

        var error = LevelPlayback.StartForMapNode(level);
        if (error != null)
            Plugin.Log.LogWarning($"MapEditor: dungeon '{DungeonName}' could not start " +
                                  $"'{level.LevelName}': {error}");
    }

    // With more map above, the exit door is where the next floor is chosen; the run finishes once
    // the floor just cleared was on the top layer.
    public override void ExitDoor()
    {
        if (DungeonMapPlayback.TryShowSelector()) return;

        LevelPlayback.Stop();
        DungeonMapPlayback.Clear();
        base.ExitDoor();
    }

    private CTLevelBlueprint StartLevel()
    {
        var start = Map?.StartNode();
        if (start == null || string.IsNullOrEmpty(start.Level)) return null;

        foreach (var level in CTLevelSerialization.LoadAll())
            if (level != null &&
                string.Equals(level.LevelName, start.Level, StringComparison.OrdinalIgnoreCase))
                return level;

        Plugin.Log.LogWarning($"MapEditor: dungeon '{DungeonName}' starts on level " +
                              $"'{start.Level}', which is not saved on this machine.");
        return null;
    }

    // ---- registry ---------------------------------------------------------------------------

    private static readonly Dictionary<string, CTMapDungeon> Registered = new(StringComparer.OrdinalIgnoreCase);

    public static CTMapDungeon Find(string mapName) =>
        mapName != null && Registered.TryGetValue(mapName, out var dungeon) ? dungeon : null;

    // Registering mints a FollowerLocation, which cannot be handed back, so a map already
    // registered keeps its slot and only its graph is refreshed. That is what lets Save make a
    // dungeon enterable straight away instead of after a restart.
    public static void RegisterAll()
    {
        foreach (var map in CTDungeonMapSerialization.LoadAll())
        {
            if (map == null || string.IsNullOrWhiteSpace(map.MapName)) continue;

            if (Registered.TryGetValue(map.MapName, out var existing))
            {
                existing.Map = map;
                continue;
            }

            try
            {
                var dungeon = new CTMapDungeon
                {
                    Map = map,
                    Location = FollowerLocation.Dungeon2_1
                };

                CustomDungeonManager.Add(dungeon);
                Registered[map.MapName] = dungeon;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MapEditor: could not register dungeon '{map.MapName}': {e.Message}");
            }
        }

        Plugin.Log.LogInfo($"MapEditor: {Registered.Count} map dungeon(s) registered.");
    }
}
