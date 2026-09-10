using System;
using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.APIHelper.NpcQuests;

/// <summary>
/// Runs custom NPC quests.
///
/// The game's quest panel is driven entirely by <c>ObjectiveManager</c>, so a custom quest is shown
/// by putting real vanilla objectives into it, one per goal, grouped under the quest's title. The
/// shell used is <c>Objectives_CollectItem</c> with no item and a custom term: it is the one vanilla
/// objective whose text is ours and whose completion is a plain count against a target, so we can
/// drive both. (<c>Objectives_Custom</c> looks like the natural choice and is not: with no target
/// follower its CheckComplete returns true, so the first refresh would finish the quest.)
///
/// Those shells are display copies. What the player has actually done lives in
/// <see cref="QuestProgress"/>, keyed by name, and the shells are taken back out of the game's
/// lists before it writes its save, so an uninstall leaves no unfinishable objective behind.
/// </summary>
public static class QuestRuntime
{
    private class Live
    {
        public QuestDefinition Def;
        public QuestRecord Record;
        public readonly List<Objectives_CollectItem> Goals = [];
        public Objectives_CollectItem Return;
    }

    private const float TickSeconds = 0.5f;

    /// How often the world is re-read even though nothing said it changed. This is a safety net,
    /// not the mechanism: everything below is event-driven, and this only catches a signal the
    /// game never sent.
    private const float SweepSeconds = 30f;

    /// The kinds of world state a goal can be read from. Reading any of these is only done when
    /// the game says it changed, so a settlement of buildings is walked when the player builds,
    /// not on a timer.
    [Flags]
    private enum Watch
    {
        None = 0,
        Items = 1,
        Kills = 2,
        Structures = 4,
        Followers = 8
    }

    private static readonly Dictionary<string, Live> Running =
        new(StringComparer.OrdinalIgnoreCase);

    private static float _nextTick;
    private static float _nextSweep;
    private static bool _hooked;

    /// What the quests currently under way actually care about. Nothing outside this is ever read,
    /// and the event handlers below cost nothing when no quest is watching.
    private static Watch _watching;

    /// What changed since the last tick. Several events in one frame coalesce into one read, which
    /// matters most when a settlement loads and every building announces itself at once.
    private static Watch _dirty;

    private static Watch WatchFor(QuestGoalKind kind) => kind switch
    {
        QuestGoalKind.CollectItem => Watch.Items,
        QuestGoalKind.KillEnemies => Watch.Kills,
        QuestGoalKind.BuildStructure => Watch.Structures,
        QuestGoalKind.Followers => Watch.Followers,
        _ => Watch.None
    };

    /// Recomputed whenever a quest starts or stops, so the handlers can drop out early.
    private static void Rewatch()
    {
        var watching = Watch.None;

        foreach (var live in Running.Values)
        {
            if (live?.Def?.Goals == null) continue;
            foreach (var goal in live.Def.Goals) watching |= WatchFor(goal.Kind);
        }

        _watching = watching;
    }

    private static void Mark(Watch what)
    {
        if ((_watching & what) != 0) _dirty |= what;
    }

    // ---- lifecycle -----------------------------------------------------------------------------

    private static void EnsureHooked()
    {
        if (_hooked) return;
        _hooked = true;

        SaveAndLoad.OnLoadComplete = (Action)Delegate.Combine(SaveAndLoad.OnLoadComplete,
            new Action(OnSaveLoaded));

        // The same signals the game's own ObjectiveManager listens to, for the same reasons.
        Inventory.OnInventoryUpdated = (Inventory.InventoryUpdated)Delegate.Combine(
            Inventory.OnInventoryUpdated, (Inventory.InventoryUpdated)(() => Mark(Watch.Items)));

        UnitObject.OnEnemyKilled += _ => Mark(Watch.Kills);

        StructureManager.OnStructureAdded = (StructureManager.StructureChanged)Delegate.Combine(
            StructureManager.OnStructureAdded,
            (StructureManager.StructureChanged)(_ => Mark(Watch.Structures)));

        StructureManager.OnStructureRemoved = (StructureManager.StructureChanged)Delegate.Combine(
            StructureManager.OnStructureRemoved,
            (StructureManager.StructureChanged)(_ => Mark(Watch.Structures)));

        FollowerManager.OnFollowerAdded = (FollowerManager.FollowerChanged)Delegate.Combine(
            FollowerManager.OnFollowerAdded,
            (FollowerManager.FollowerChanged)(_ => Mark(Watch.Followers)));

        FollowerManager.OnFollowerRemoved = (FollowerManager.FollowerChanged)Delegate.Combine(
            FollowerManager.OnFollowerRemoved,
            (FollowerManager.FollowerChanged)(_ => Mark(Watch.Followers)));
    }

    private static void OnSaveLoaded()
    {
        try
        {
            DropShells();
            QuestProgress.Reload();
            QuestGoals.ForgetResolutions();
            Restore();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Quests: could not restore quests for this save: " + e);
        }
    }

    /// Rebuilds the panel entries for every quest this save has under way.
    private static void Restore()
    {
        if (QuestRegistry.All.Count == 0) return;

        var restored = 0;
        foreach (var pair in QuestProgress.Data().Quests)
        {
            var record = pair.Value;
            if (record == null) continue;

            var state = record.State();
            if (state != QuestStatus.Active && state != QuestStatus.Ready) continue;

            var def = QuestRegistry.Find(pair.Key);
            if (def == null)
            {
                Plugin.Log.LogWarning($"Quests: this save has quest '{pair.Key}' under way but no " +
                                      "NPC declares it any more; it stays in the file, out of sight.");
                continue;
            }

            if (Build(def, record) != null) restored++;
        }

        if (restored > 0) Plugin.Log.LogInfo($"Quests: {restored} quest(s) restored for this save.");
    }

    // ---- the tick ------------------------------------------------------------------------------

    /// <summary>
    /// Called every frame from the plugin. Twice a second it checks the cheap things - a timer
    /// running out, a line the game failed - and it reads the world only for the kinds something
    /// said had changed. A tick with nothing dirty is a handful of float comparisons.
    /// </summary>
    public static void Tick()
    {
        EnsureHooked();

        if (Running.Count == 0 || Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + TickSeconds;

        if (DataManager.Instance == null) return;

        if (Time.unscaledTime >= _nextSweep)
        {
            _nextSweep = Time.unscaledTime + SweepSeconds;
            _dirty = _watching;
        }

        var read = _dirty;
        _dirty = Watch.None;

        foreach (var live in new List<Live>(Running.Values)) Advance(live, read);

        QuestProgress.Flush();
    }

    private static void Advance(Live live, Watch read)
    {
        if (live?.Def == null || live.Record == null) return;

        if (Expired(live) || AnyShellFailed(live))
        {
            Fail(live);
            return;
        }

        var goals = live.Def.Goals;
        var record = live.Record;
        Fit(record, goals.Count);

        // Once the quest is waiting to be handed in, its goals are settled. Re-reading them would
        // undo a goal the player has since spent, and the player has already done the work.
        if (record.State() == QuestStatus.Ready) return;

        var met = 0;
        var changed = new List<Objectives_CollectItem>();

        for (var i = 0; i < goals.Count && i < live.Goals.Count; i++)
        {
            var goal = goals[i];
            var value = record.Progress[i];

            // Read the world only for the kinds that said they moved. Everything else keeps the
            // number it already had, event goals included.
            if (goal.IsPolled && (read & WatchFor(goal.Kind)) != 0)
            {
                var raw = QuestGoals.Read(goal);
                value = goal.Total ? raw : raw - record.Baseline[i];
            }

            value = Mathf.Clamp(value, 0, goal.Count);
            if (value != record.Progress[i])
            {
                record.Progress[i] = value;
                QuestProgress.Touch();
            }

            if (value >= goal.Count) met++;

            var shell = live.Goals[i];
            if (shell == null || shell.Count == value) continue;

            shell.Count = value;
            changed.Add(shell);
        }

        var allMet = met >= goals.Count;

        // The panel drops a group the moment every line in it is complete, so the "come back to me"
        // line has to be in the group before the last goal is ticked off, not after.
        if (allMet && live.Def.TurnIn && live.Return == null && record.State() == QuestStatus.Active)
        {
            live.Return = AddShell(live.Def, live.Def.ReturnTerm, 1, live.Def.AutoTrack);
            record.SetState(QuestStatus.Ready);
            QuestProgress.Touch();
            Plugin.Log.LogInfo($"Quest '{live.Def.Key}': every goal met, waiting on " +
                               $"{live.Def.OwnerDisplay}.");
        }

        foreach (var shell in changed) SafeUpdate(shell);

        if (allMet && !live.Def.TurnIn && record.State() == QuestStatus.Active) Finish(live);
    }

    private static bool Expired(Live live)
    {
        if (live.Record.ExpiresAt <= 0f) return false;
        return TimeManager.TotalElapsedGameTime >= live.Record.ExpiresAt;
    }

    private static bool AnyShellFailed(Live live)
    {
        foreach (var shell in live.Goals)
            if (shell != null && shell.IsFailed) return true;

        return live.Return != null && live.Return.IsFailed;
    }

    // ---- what dialogue and the API call --------------------------------------------------------

    public static QuestStatus State(string key)
    {
        var record = QuestProgress.Find(key);
        return record?.State() ?? QuestStatus.NotStarted;
    }

    /// True when the quest can be taken right now.
    public static bool CanAccept(QuestDefinition def)
    {
        if (def == null) return false;

        return State(def.Key) switch
        {
            QuestStatus.Active => false,
            QuestStatus.Ready => false,
            QuestStatus.Done => def.Repeatable,
            _ => true
        };
    }

    public static bool Accept(QuestDefinition def)
    {
        if (def == null) return false;

        if (!CanAccept(def))
        {
            Plugin.Log.LogInfo($"Quest '{def.Key}' was offered again while it is " +
                               $"{State(def.Key)}; nothing to do.");
            return false;
        }

        var record = QuestProgress.Find(def.Key) ?? QuestProgress.Create(def.Key);
        record.SetState(QuestStatus.Active);
        record.Progress = [];
        record.Baseline = [];
        Fit(record, def.Goals.Count);

        // What the world already holds, so a goal counts what the player does from here on.
        for (var i = 0; i < def.Goals.Count; i++)
            record.Baseline[i] = def.Goals[i].IsPolled ? QuestGoals.Read(def.Goals[i]) : 0;

        record.ExpiresAt = def.ExpireSeconds > 0f
            ? TimeManager.TotalElapsedGameTime + def.ExpireSeconds
            : -1f;

        QuestProgress.Touch();
        QuestProgress.Flush();

        Build(def, record);
        Plugin.Log.LogInfo($"Quest '{def.Key}' accepted.");
        return true;
    }

    /// Hands the quest in. Works from the moment every goal is met.
    public static bool TurnIn(QuestDefinition def)
    {
        if (def == null) return false;

        if (!Running.TryGetValue(def.Key, out var live))
        {
            Plugin.Log.LogInfo($"Quest '{def.Key}' cannot be handed in: it is {State(def.Key)}.");
            return false;
        }

        if (live.Record.State() != QuestStatus.Ready)
        {
            Plugin.Log.LogInfo($"Quest '{def.Key}' is not finished yet.");
            return false;
        }

        Finish(live);
        return true;
    }

    /// Gives the quest up. The record goes away, so the NPC can offer it again.
    public static bool Abandon(QuestDefinition def)
    {
        if (def == null) return false;

        if (Running.TryGetValue(def.Key, out var live))
        {
            DropShells(live);
            Running.Remove(def.Key);
            Rewatch();
        }

        QuestProgress.Forget(def.Key);
        QuestProgress.Flush();
        Plugin.Log.LogInfo($"Quest '{def.Key}' abandoned.");
        return true;
    }

    /// Every quest this save has under way, by key.
    public static IReadOnlyList<string> RunningKeys()
    {
        var keys = new List<string>(Running.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    // ---- events the goals listen for -----------------------------------------------------------

    internal static void NoteDungeonCleared(FollowerLocation location) =>
        Count(QuestGoalKind.CompleteDungeon, goal => QuestGoals.IsDungeon(goal.Target, location));

    internal static void NoteRitual(UpgradeSystem.Type ritual) =>
        Count(QuestGoalKind.PerformRitual, goal => QuestGoals.RitualId(goal.Target) == (int)ritual);

    internal static void NoteGameEvent(Objectives.CustomQuestTypes type) =>
        Count(QuestGoalKind.GameEvent, goal => QuestGoals.GameEventId(goal.Target) == (int)type);

    internal static void NoteTalkedTo(string npcInternalName) =>
        Count(QuestGoalKind.TalkTo, goal =>
            string.Equals(goal.Target, npcInternalName, StringComparison.OrdinalIgnoreCase));

    /// Drives "flag" goals. Anything can raise one: another mod through the public API, or a
    /// dialogue branch.
    public static void NoteFlag(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return;
        Count(QuestGoalKind.Flag, goal =>
            string.Equals(goal.Target, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static void Count(QuestGoalKind kind, Func<QuestGoal, bool> matches)
    {
        if (Running.Count == 0) return;

        foreach (var live in Running.Values)
        {
            if (live.Record.State() != QuestStatus.Active) continue;

            var goals = live.Def.Goals;
            Fit(live.Record, goals.Count);

            for (var i = 0; i < goals.Count; i++)
            {
                if (goals[i].Kind != kind) continue;

                bool hit;
                try
                {
                    hit = matches(goals[i]);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Quests: matching a '{kind}' goal failed: {e.Message}");
                    continue;
                }

                if (!hit || live.Record.Progress[i] >= goals[i].Count) continue;

                live.Record.Progress[i]++;
                QuestProgress.Touch();
            }
        }

        QuestProgress.Flush();
    }

    // ---- finishing -----------------------------------------------------------------------------

    private static void Finish(Live live)
    {
        var def = live.Def;

        Consume(def);
        Reward(def);

        // Ticking the last line off lets the game close the group the way it closes its own.
        foreach (var shell in live.Goals)
        {
            if (shell == null) continue;
            shell.Count = shell.Target;
            SafeUpdate(shell);
        }

        if (live.Return != null)
        {
            live.Return.Count = live.Return.Target;
            SafeUpdate(live.Return);
        }

        live.Record.SetState(QuestStatus.Done);
        live.Record.TimesCompleted++;
        live.Record.ExpiresAt = -1f;
        QuestProgress.Touch();
        QuestProgress.Flush();

        Running.Remove(def.Key);
        Rewatch();
        Plugin.Log.LogInfo($"Quest '{def.Key}' completed.");
    }

    private static void Fail(Live live)
    {
        foreach (var shell in live.Goals) MarkFailed(shell);
        MarkFailed(live.Return);

        live.Record.SetState(QuestStatus.Failed);
        QuestProgress.Touch();
        QuestProgress.Flush();

        Running.Remove(live.Def.Key);
        Rewatch();
        Plugin.Log.LogInfo($"Quest '{live.Def.Key}' failed.");
    }

    private static void MarkFailed(Objectives_CollectItem shell)
    {
        if (shell == null || shell.IsComplete) return;

        try
        {
            shell.IsFailed = true;
            ObjectiveManager.UpdateObjective(shell);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Quests: failing an objective line went wrong: " + e.Message);
        }
    }

    private static void Consume(QuestDefinition def)
    {
        foreach (var goal in def.Goals)
        {
            if (!goal.Consume) continue;

            var id = QuestGoals.ItemId(goal.Target);
            if (id == int.MinValue) continue;

            try
            {
                Inventory.ChangeItemQuantity(id, -goal.Count);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Quest '{def.Key}': taking {goal.Count} " +
                                      $"{goal.Target} failed: {e.Message}");
            }
        }
    }

    private static void Reward(QuestDefinition def)
    {
        if (def.Reward?.Items == null) return;

        foreach (var reward in def.Reward.Items)
        {
            if (reward == null || string.IsNullOrWhiteSpace(reward.Item) || reward.Count <= 0) continue;

            var id = QuestGoals.ItemId(reward.Item);
            if (id == int.MinValue) continue;

            try
            {
                Inventory.AddItem(id, reward.Count, true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Quest '{def.Key}': handing over {reward.Count} " +
                                      $"{reward.Item} failed: {e.Message}");
            }
        }
    }

    // ---- the panel entries ---------------------------------------------------------------------

    private static Live Build(QuestDefinition def, QuestRecord record)
    {
        if (def == null || record == null) return null;

        if (Running.TryGetValue(def.Key, out var existing)) DropShells(existing);

        var live = new Live { Def = def, Record = record };
        Fit(record, def.Goals.Count);

        try
        {
            for (var i = 0; i < def.Goals.Count; i++)
            {
                var goal = def.Goals[i];
                var shell = AddShell(def, goal.Term, goal.Count, def.AutoTrack);

                // Kept in step with the goals even when one could not be built, so goal i is
                // always line i.
                live.Goals.Add(shell);
                if (shell != null) shell.Count = Mathf.Clamp(record.Progress[i], 0, goal.Count);
            }

            if (record.State() == QuestStatus.Ready && def.TurnIn)
                live.Return = AddShell(def, def.ReturnTerm, 1, def.AutoTrack);

            // Only now, with the whole group in place, may finished lines be ticked off.
            foreach (var shell in live.Goals)
                if (shell != null && shell.Count > 0) SafeUpdate(shell);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Quest '{def.Key}': its panel entries could not be built: {e}");
        }

        Running[def.Key] = live;
        Rewatch();

        // A quest that has just appeared has never read the world; let the next tick do it once.
        _dirty |= _watching;
        return live;
    }

    private static Objectives_CollectItem AddShell(QuestDefinition def, string term, int target,
        bool autoTrack)
    {
        if (string.IsNullOrEmpty(term)) return null;

        var shell = new Objectives_CollectItem(def.TitleTerm, InventoryItem.ITEM_TYPE.NONE,
            Math.Max(1, target), true, FollowerLocation.Base,
            def.ExpireSeconds > 0f ? def.ExpireSeconds : -1f)
        {
            CustomTerm = term,
            AutoRemoveQuestOnceComplete = true
        };

        ObjectiveManager.Add(shell, autoTrack);

        // Add ran Init, which reads the inventory and restarts the clock. Neither is ours to keep.
        shell.StartingAmount = -1;
        shell.Count = 0;
        shell.ExpireTimestamp = def.ExpireSeconds > 0f
            ? QuestProgress.Find(def.Key)?.ExpiresAt ?? shell.ExpireTimestamp
            : -1f;

        return shell;
    }

    private static void SafeUpdate(ObjectivesData shell)
    {
        try
        {
            ObjectiveManager.UpdateObjective(shell);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Quests: refreshing an objective line went wrong: " + e.Message);
        }
    }

    private static void DropShells()
    {
        foreach (var live in new List<Live>(Running.Values)) DropShells(live);
        Running.Clear();
        Rewatch();
    }

    private static void DropShells(Live live)
    {
        if (live == null) return;

        var group = "";
        foreach (var shell in live.Goals)
        {
            if (shell == null) continue;
            group = shell.UniqueGroupID;
            Remove(shell);
        }

        if (live.Return != null)
        {
            group = live.Return.UniqueGroupID;
            Remove(live.Return);
        }

        live.Goals.Clear();
        live.Return = null;

        if (!string.IsNullOrEmpty(group))
        {
            try
            {
                ObjectiveManager.UntrackGroup(group, true);
            }
            catch (Exception)
            {
            }
        }
    }

    private static void Remove(ObjectivesData shell)
    {
        Unlisten(shell);

        try
        {
            var data = DataManager.Instance;
            if (data != null)
            {
                data.Objectives.Remove(shell);
                data.CompletedObjectives.Remove(shell);
                data.FailedObjectives.Remove(shell);
            }

            ObjectiveManager.ObjectiveRemoved(shell);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Quests: removing an objective line went wrong: " + e.Message);
        }
    }

    /// <summary>
    /// Takes a dropped shell off the inventory event.
    ///
    /// Objectives_CollectItem subscribes in Init and only unsubscribes when it completes or fails.
    /// A shell we throw away on a save reload does neither, so without this every load would leave
    /// another dead listener on a static event. It is harmless in behaviour - our item type is NONE
    /// and the tick rewrites Count anyway - but it is still a leak, and it is cheap to close.
    /// </summary>
    private static void Unlisten(ObjectivesData shell)
    {
        if (shell == null) return;

        try
        {
            var field = HarmonyLib.AccessTools.Field(typeof(Inventory), "OnItemAddedToInventory");
            if (field?.GetValue(null) is not Delegate current) return;

            var remaining = current;
            foreach (var handler in current.GetInvocationList())
                if (ReferenceEquals(handler.Target, shell))
                    remaining = Delegate.Remove(remaining, handler);

            if (!ReferenceEquals(remaining, current)) field.SetValue(null, remaining);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Quests: could not unhook a dropped objective line: " + e.Message);
        }
    }

    private static void Fit(QuestRecord record, int count)
    {
        record.Progress ??= [];
        record.Baseline ??= [];

        while (record.Progress.Count < count) record.Progress.Add(0);
        while (record.Baseline.Count < count) record.Baseline.Add(0);
    }

    // ---- keeping the game's save clean ---------------------------------------------------------

    /// Every objective line we put in the game's lists, for the save mask to lift out.
    internal static IEnumerable<ObjectivesData> Shells()
    {
        foreach (var live in Running.Values)
        {
            foreach (var shell in live.Goals)
                if (shell != null) yield return shell;

            if (live.Return != null) yield return live.Return;
        }
    }
}
