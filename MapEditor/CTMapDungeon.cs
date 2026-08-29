using System;
using System.Collections.Generic;
using CustomSpineLoader.APIHelper;
using MMRoomGeneration;

namespace CustomSpineLoader.MapEditor;

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

    public override bool DrivesLevelPlayback => true;

    public override string CaptionTitle => "";
    public override string CaptionSubtext => "";

    public override void SpawnEnemies(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType) { }

    public override void OnRoomGenerated(GenerateRoom room, GenerateRoom.ConnectionTypes connectionType)
        => LevelPlayback.OnRoomGenerated(room, connectionType);

    public override void EnterDungeon()
    {
        DungeonMapPlayback.UseMap(Map?.MapName);

        base.EnterDungeon();
    }

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
