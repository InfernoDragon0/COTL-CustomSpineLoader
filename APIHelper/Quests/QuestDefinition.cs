using System;
using System.Collections.Generic;

namespace CustomSpineLoader.APIHelper.NpcQuests;

/// <summary>
/// One quest exactly as it is written in a custom NPC's config.json. The loader turns this into a
/// <see cref="QuestDefinition"/> once, at boot; nothing here is read again at runtime.
/// </summary>
[Serializable]
public class NpcQuestConfig
{
    /// Unique within this NPC. Dialogue refers to the quest by this.
    public string Id = "";

    /// The header the quest gets in the objectives panel, on the right of the screen.
    public string Title = "";

    /// The line shown once every goal is met and the player has to come back. Ignored when
    /// TurnIn is false.
    public string ReturnText = "";

    /// True (the default) means the quest finishes when the player talks to this NPC again.
    /// False means it finishes the moment the last goal is met.
    public bool TurnIn = true;

    /// True lets the player take the quest again after finishing it.
    public bool Repeatable;

    /// True (the default) pins the quest to the panel when it is accepted.
    public bool AutoTrack = true;

    /// Seconds of game time before the quest fails on its own. 0 means never.
    public float ExpireSeconds;

    public List<QuestGoalConfig> Goals = [];

    public QuestRewardConfig Reward;
}

/// One line of a quest: what the player has to do, and the text that says so.
[Serializable]
public class QuestGoalConfig
{
    /// collectItem, killEnemies, completeDungeon, buildStructure, performRitual, followers,
    /// gameEvent, talkTo or flag.
    public string Type = "";

    /// The line shown in the panel. When Count is 2 or more the game appends " 3 / 10" to it.
    public string Text = "";

    /// What the goal is about: an item, enemy, dungeon, structure, ritual, event, NPC or flag
    /// name. Vanilla names and custom content names both work.
    public string Target = "";

    public int Count = 1;

    /// collectItem, buildStructure and followers only: count what the player already has instead
    /// of only what they gather after taking the quest.
    public bool Total;

    /// collectItem only: take the items when the quest is handed in.
    public bool Consume;
}

[Serializable]
public class QuestRewardConfig
{
    public List<QuestRewardItem> Items = [];
}

[Serializable]
public class QuestRewardItem
{
    public string Item = "";
    public int Count = 1;
}

/// What a goal watches. Poll kinds are read from the game every tick; event kinds are counted as
/// the game reports them.
public enum QuestGoalKind
{
    CollectItem,
    KillEnemies,
    BuildStructure,
    Followers,
    CompleteDungeon,
    PerformRitual,
    GameEvent,
    TalkTo,
    Flag
}

/// A goal after parsing, with its localisation term.
public class QuestGoal
{
    public QuestGoalKind Kind;
    public string Target = "";
    public int Count = 1;
    public bool Total;
    public bool Consume;

    /// The registered term the objectives panel reads this line from.
    public string Term;

    /// Poll kinds read the world each tick; event kinds only move when something reports in.
    public bool IsPolled =>
        Kind is QuestGoalKind.CollectItem or QuestGoalKind.KillEnemies
            or QuestGoalKind.BuildStructure or QuestGoalKind.Followers;
}

/// A quest after parsing. One of these exists per quest for the whole session.
public class QuestDefinition
{
    /// "npcInternalName/questId". Unique across the install and stable between runs.
    public string Key = "";

    public string Id = "";

    /// The NPC that hands it out, by internal name.
    public string OwnerNpc = "";

    /// The NPC's display name, for the "return to" line.
    public string OwnerDisplay = "";

    public string TitleTerm;
    public string ReturnTerm;

    public bool TurnIn = true;
    public bool Repeatable;
    public bool AutoTrack = true;
    public float ExpireSeconds;

    public List<QuestGoal> Goals = [];
    public QuestRewardConfig Reward;
}

/// Where a quest stands for the current save.
public enum QuestStatus
{
    NotStarted,
    Active,

    /// Every goal is met and the player has to talk to the NPC to finish it.
    Ready,
    Done,
    Failed
}
