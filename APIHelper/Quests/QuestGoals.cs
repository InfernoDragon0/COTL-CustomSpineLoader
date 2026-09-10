using System;
using System.Collections.Generic;

namespace CustomSpineLoader.APIHelper.NpcQuests;

/// <summary>
/// Turns a goal's target name into whatever the game wants, and reads the world for the goals that
/// can be polled. Names are resolved lazily and cached: a custom item's id is allocated per install
/// and cannot be known until the loaders have run, and it must never be written to our progress
/// file, so the name stays the key everywhere and the id is only ever a runtime lookup.
/// </summary>
internal static class QuestGoals
{
    private static readonly Dictionary<string, int> Resolved = new(StringComparer.OrdinalIgnoreCase);

    internal static void ForgetResolutions() => Resolved.Clear();

    // ---- name resolution ---------------------------------------------------------------------

    private static int Cached(string cacheKey, Func<int> resolve)
    {
        if (Resolved.TryGetValue(cacheKey, out var id)) return id;

        id = resolve();
        Resolved[cacheKey] = id;
        return id;
    }

    /// An inventory item by vanilla name, or by the internal name of a custom item or meal.
    internal static int ItemId(string name) => Cached("item:" + name, () =>
    {
        if (Enum.TryParse<InventoryItem.ITEM_TYPE>(name, true, out var vanilla)) return (int)vanilla;

        var custom = Api.CultTweakerApi.IdOf(Api.CultTweakerApi.Kind.Items, name);
        if (custom != int.MinValue) return custom;

        custom = Api.CultTweakerApi.IdOf(Api.CultTweakerApi.Kind.Meals, name);
        if (custom != int.MinValue) return custom;

        Plugin.Log.LogWarning($"Quests: no item called '{name}' in this install.");
        return int.MinValue;
    });

    internal static int EnemyId(string name) => Cached("enemy:" + name, () =>
    {
        if (Enum.TryParse<Enemy>(name, true, out var vanilla)) return (int)vanilla;

        var custom = Api.CultTweakerApi.IdOf(Api.CultTweakerApi.Kind.Enemies, name);
        if (custom != int.MinValue) return custom;

        Plugin.Log.LogWarning($"Quests: no enemy called '{name}' in this install.");
        return int.MinValue;
    });

    internal static int StructureId(string name) => Cached("structure:" + name, () =>
    {
        if (Enum.TryParse<StructureBrain.TYPES>(name, true, out var vanilla)) return (int)vanilla;

        var custom = Api.CultTweakerApi.IdOf(Api.CultTweakerApi.Kind.Structures, name);
        if (custom != int.MinValue) return custom;

        Plugin.Log.LogWarning($"Quests: no structure called '{name}' in this install.");
        return int.MinValue;
    });

    internal static int RitualId(string name) => Cached("ritual:" + name, () =>
    {
        if (Enum.TryParse<UpgradeSystem.Type>(name, true, out var ritual)) return (int)ritual;

        // A bare ritual name is the common way to write it; the enum spells them "Ritual_Feast".
        if (Enum.TryParse<UpgradeSystem.Type>("Ritual_" + name, true, out ritual)) return (int)ritual;

        Plugin.Log.LogWarning($"Quests: no ritual called '{name}'.");
        return int.MinValue;
    });

    internal static int GameEventId(string name) => Cached("event:" + name, () =>
    {
        if (Enum.TryParse<Objectives.CustomQuestTypes>(name, true, out var value)) return (int)value;

        Plugin.Log.LogWarning($"Quests: '{name}' is not one of the game's own quest events.");
        return int.MinValue;
    });

    /// True when the location the player just cleared is the one a goal names. Custom dungeons are
    /// matched by name so a different install's numbering cannot break a save.
    internal static bool IsDungeon(string name, FollowerLocation location)
    {
        if (string.IsNullOrEmpty(name)) return false;

        if (string.Equals(location.ToString(), name, StringComparison.OrdinalIgnoreCase)) return true;

        var current = Api.CultTweakerApi.CurrentDungeon();
        if (!string.IsNullOrEmpty(current) &&
            string.Equals(current, name, StringComparison.OrdinalIgnoreCase)) return true;

        return CustomDungeonManager.CustomDungeonList.TryGetValue(location, out var dungeon) &&
               dungeon != null &&
               (string.Equals(dungeon.InternalName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dungeon.DungeonName, name, StringComparison.OrdinalIgnoreCase));
    }

    // ---- polling -----------------------------------------------------------------------------

    /// What the world says about a polled goal right now, before the baseline is taken off.
    /// Event goals never come through here: their progress is counted as the events arrive.
    internal static int Read(QuestGoal goal)
    {
        try
        {
            switch (goal.Kind)
            {
                case QuestGoalKind.CollectItem:
                {
                    var id = ItemId(goal.Target);
                    return id == int.MinValue ? 0 : Inventory.GetItemQuantity(id);
                }

                case QuestGoalKind.KillEnemies:
                {
                    var id = EnemyId(goal.Target);
                    if (id == int.MinValue || DataManager.Instance == null) return 0;
                    return DataManager.Instance.GetEnemiesKilled((Enemy)id);
                }

                case QuestGoalKind.BuildStructure:
                {
                    var id = StructureId(goal.Target);
                    if (id == int.MinValue) return 0;
                    var built = StructureManager.GetAllStructuresOfType((StructureBrain.TYPES)id);
                    return built?.Count ?? 0;
                }

                case QuestGoalKind.Followers:
                    return DataManager.Instance?.Followers?.Count ?? 0;

                default:
                    return 0;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Quests: reading a '{goal.Kind}' goal failed: {e.Message}");
            return 0;
        }
    }
}
