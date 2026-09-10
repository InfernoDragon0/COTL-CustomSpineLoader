using System;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor.Npc;
using I2.Loc;

namespace CustomSpineLoader.APIHelper.NpcQuests;

/// <summary>
/// Every quest declared by a custom NPC, parsed once at boot. Quests are content, not save data:
/// what the player has done with them lives in <see cref="QuestProgress"/>.
/// </summary>
public static class QuestRegistry
{
    private static readonly Dictionary<string, QuestDefinition> ByKey =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<QuestDefinition> Ordered = [];

    public static IReadOnlyList<QuestDefinition> All => Ordered;

    public static QuestDefinition Find(string key) =>
        !string.IsNullOrEmpty(key) && ByKey.TryGetValue(key, out var quest) ? quest : null;

    /// Resolves a quest the way dialogue names one: a bare id belongs to the speaking NPC, a
    /// "npc/id" key can name any NPC's quest.
    public static QuestDefinition Resolve(string npcInternalName, string idOrKey)
    {
        if (string.IsNullOrWhiteSpace(idOrKey)) return null;

        var direct = Find(idOrKey);
        if (direct != null) return direct;

        return string.IsNullOrEmpty(npcInternalName) ? null : Find(npcInternalName + "/" + idOrKey);
    }

    public static IReadOnlyList<QuestDefinition> ForNpc(string npcInternalName)
    {
        var result = new List<QuestDefinition>();
        if (string.IsNullOrEmpty(npcInternalName)) return result;

        foreach (var quest in Ordered)
            if (string.Equals(quest.OwnerNpc, npcInternalName, StringComparison.OrdinalIgnoreCase))
                result.Add(quest);

        return result;
    }

    // ---- registration ----------------------------------------------------------------------------

    /// Parses an NPC's quests and registers their text. Called by the NPC loader; safe to call
    /// twice for the same NPC, the second call replaces the first.
    public static void Register(CustomNpc npc, List<NpcQuestConfig> configs)
    {
        if (npc == null || configs == null || configs.Count == 0) return;

        LanguageSourceData source;
        try
        {
            source = LocalizationManager.Sources[0];
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Custom NPC '{npc.InternalName}': no localization source, quest " +
                                "text would be blank, so its quests are skipped: " + e.Message);
            return;
        }

        foreach (var config in configs)
        {
            try
            {
                var quest = Build(npc, config, source);
                if (quest == null) continue;

                if (ByKey.TryGetValue(quest.Key, out var existing)) Ordered.Remove(existing);

                ByKey[quest.Key] = quest;
                Ordered.Add(quest);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Custom NPC '{npc.InternalName}': quest " +
                                    $"'{config?.Id}' failed to register: {e}");
            }
        }

        source.UpdateDictionary();
    }

    private static QuestDefinition Build(CustomNpc npc, NpcQuestConfig config,
        LanguageSourceData source)
    {
        if (config == null) return null;

        if (string.IsNullOrWhiteSpace(config.Id))
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}': a quest has no id, skipped.");
            return null;
        }

        var id = config.Id.Trim().Replace(" ", "_");
        var key = npc.InternalName + "/" + id;
        var prefix = "CultTweaker/Quest/" + key;

        var quest = new QuestDefinition
        {
            Key = key,
            Id = id,
            OwnerNpc = npc.InternalName,
            OwnerDisplay = string.IsNullOrEmpty(npc.DisplayName) ? npc.InternalName : npc.DisplayName,
            TurnIn = config.TurnIn,
            Repeatable = config.Repeatable,
            AutoTrack = config.AutoTrack,
            ExpireSeconds = Math.Max(0f, config.ExpireSeconds)
        };

        var title = string.IsNullOrWhiteSpace(config.Title) ? id : config.Title;
        quest.TitleTerm = NpcDialogue.Register(source, prefix + "/Title", title);

        if (quest.TurnIn)
        {
            var returnText = string.IsNullOrWhiteSpace(config.ReturnText)
                ? "Return to " + quest.OwnerDisplay
                : config.ReturnText;

            // One step, so the game's " 0 / 1" counter is noise; hide it rather than show it.
            quest.ReturnTerm = NpcDialogue.Register(source, prefix + "/Return", returnText + Hidden);
        }

        if (config.Goals == null || config.Goals.Count == 0)
        {
            Plugin.Log.LogWarning($"Quest '{key}' has no goals, so nothing could ever finish it; " +
                                  "skipped.");
            return null;
        }

        for (var i = 0; i < config.Goals.Count; i++)
        {
            var goal = BuildGoal(key, config.Goals[i]);
            if (goal == null) continue;

            var text = string.IsNullOrWhiteSpace(config.Goals[i].Text)
                ? DescribeGoal(goal)
                : config.Goals[i].Text;

            // The panel appends " 3 / 10" whenever a custom term is used. That reads well for a
            // goal worth counting and badly for a one-shot, so a one-shot hides it.
            if (goal.Count <= 1) text += Hidden;

            goal.Term = NpcDialogue.Register(source, $"{prefix}/Goal/{i}", text);
            quest.Goals.Add(goal);
        }

        if (quest.Goals.Count == 0)
        {
            Plugin.Log.LogWarning($"Quest '{key}': none of its goals could be read, skipped.");
            return null;
        }

        quest.Reward = config.Reward;
        Plugin.Log.LogInfo($"Quest '{key}' registered with {quest.Goals.Count} goal(s).");
        return quest;
    }

    /// Rich text that makes the counter the game appends invisible without removing it.
    private const string Hidden = "<alpha=#00>";

    private static QuestGoal BuildGoal(string questKey, QuestGoalConfig config)
    {
        if (config == null) return null;

        if (!TryParseKind(config.Type, out var kind))
        {
            Plugin.Log.LogWarning($"Quest '{questKey}': goal type '{config.Type}' is not one this " +
                                  "build knows, so the goal is dropped. Known types: collectItem, " +
                                  "killEnemies, completeDungeon, buildStructure, performRitual, " +
                                  "followers, gameEvent, talkTo, flag.");
            return null;
        }

        if (kind != QuestGoalKind.Followers && string.IsNullOrWhiteSpace(config.Target))
        {
            Plugin.Log.LogWarning($"Quest '{questKey}': a '{config.Type}' goal has no target, dropped.");
            return null;
        }

        return new QuestGoal
        {
            Kind = kind,
            Target = (config.Target ?? "").Trim(),
            Count = Math.Max(1, config.Count),
            Total = config.Total,
            Consume = config.Consume && kind == QuestGoalKind.CollectItem
        };
    }

    private static bool TryParseKind(string type, out QuestGoalKind kind)
    {
        switch ((type ?? "").Trim().ToLowerInvariant())
        {
            case "collectitem":
            case "item":
                kind = QuestGoalKind.CollectItem;
                return true;
            case "killenemies":
            case "kill":
                kind = QuestGoalKind.KillEnemies;
                return true;
            case "buildstructure":
            case "build":
                kind = QuestGoalKind.BuildStructure;
                return true;
            case "followers":
            case "recruitfollowers":
                kind = QuestGoalKind.Followers;
                return true;
            case "completedungeon":
            case "dungeon":
                kind = QuestGoalKind.CompleteDungeon;
                return true;
            case "performritual":
            case "ritual":
                kind = QuestGoalKind.PerformRitual;
                return true;
            case "gameevent":
            case "event":
                kind = QuestGoalKind.GameEvent;
                return true;
            case "talkto":
            case "talk":
                kind = QuestGoalKind.TalkTo;
                return true;
            case "flag":
                kind = QuestGoalKind.Flag;
                return true;
            default:
                kind = QuestGoalKind.Flag;
                return false;
        }
    }

    /// A plain fallback line, used when a goal has no text of its own.
    private static string DescribeGoal(QuestGoal goal)
    {
        switch (goal.Kind)
        {
            case QuestGoalKind.CollectItem: return "Gather " + goal.Target;
            case QuestGoalKind.KillEnemies: return "Defeat " + goal.Target;
            case QuestGoalKind.BuildStructure: return "Build " + goal.Target;
            case QuestGoalKind.Followers: return "Have followers";
            case QuestGoalKind.CompleteDungeon: return "Clear " + goal.Target;
            case QuestGoalKind.PerformRitual: return "Perform " + goal.Target;
            case QuestGoalKind.TalkTo: return "Speak to " + goal.Target;
            default: return goal.Target;
        }
    }
}
