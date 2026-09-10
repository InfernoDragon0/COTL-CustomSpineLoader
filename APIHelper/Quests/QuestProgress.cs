using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CustomSpineLoader.APIHelper.NpcQuests;

public class QuestProgressData
{
    public Dictionary<string, QuestRecord> Quests = new(StringComparer.OrdinalIgnoreCase);
}

/// What one quest has done in this save. Keyed by the quest's stable "npc/id" key, never by a
/// game id, so it survives a change of mod load order.
public class QuestRecord
{
    /// active, ready, done or failed.
    public string Status = "active";

    /// How far each goal has come, in the order the goals are declared.
    public List<int> Progress = [];

    /// What the world already held when the quest was taken, for goals that count only what the
    /// player does afterwards.
    public List<int> Baseline = [];

    /// Game time the quest fails at, or -1 when it never does.
    public float ExpiresAt = -1f;

    public int TimesCompleted;

    public QuestStatus State() =>
        (Status ?? "").ToLowerInvariant() switch
        {
            "active" => QuestStatus.Active,
            "ready" => QuestStatus.Ready,
            "done" => QuestStatus.Done,
            "failed" => QuestStatus.Failed,
            _ => QuestStatus.NotStarted
        };

    public void SetState(QuestStatus status) => Status = status switch
    {
        QuestStatus.Active => "active",
        QuestStatus.Ready => "ready",
        QuestStatus.Done => "done",
        QuestStatus.Failed => "failed",
        _ => "notStarted"
    };
}

/// <summary>
/// Quest state for the current save slot, in our own file. The game's own objective list only
/// ever holds display copies (see <see cref="QuestRuntime"/>), which are taken back out before
/// the game writes its save, so uninstalling CultTweaker leaves no unfinishable objectives behind.
/// </summary>
public static class QuestProgress
{
    private const string FolderName = "QuestProgress";

    private static QuestProgressData _data;
    private static int _loadedSlot = -1;
    private static bool _dirty;

    public static string PathForSlot() =>
        Path.Combine(ModContentPaths.OwnRoot(FolderName), $"quests_slot{SaveAndLoad.SAVE_SLOT}.json");

    public static QuestProgressData Data()
    {
        if (_data != null && _loadedSlot == SaveAndLoad.SAVE_SLOT) return _data;

        _loadedSlot = SaveAndLoad.SAVE_SLOT;
        _data = Load();
        _dirty = false;
        return _data;
    }

    public static QuestRecord Find(string key) =>
        !string.IsNullOrEmpty(key) && Data().Quests.TryGetValue(key, out var record) ? record : null;

    public static QuestRecord Create(string key)
    {
        var record = new QuestRecord();
        Data().Quests[key] = record;
        Touch();
        return record;
    }

    public static void Forget(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (Data().Quests.Remove(key)) Touch();
    }

    /// Marks the file as needing a write. The write itself happens on the next flush so a busy
    /// tick does not hit the disk once per goal.
    public static void Touch() => _dirty = true;

    public static void Flush()
    {
        if (!_dirty) return;
        _dirty = false;

        try
        {
            var folder = ModContentPaths.OwnRoot(FolderName);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            File.WriteAllText(PathForSlot(), JsonConvert.SerializeObject(Data(), Formatting.Indented));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Quests: progress file could not be written: " + e.Message);
        }
    }

    /// Drops the cache so the next read comes from the slot the player just loaded.
    public static void Reload()
    {
        Flush();
        _data = null;
        _loadedSlot = -1;
    }

    private static QuestProgressData Load()
    {
        try
        {
            var path = PathForSlot();
            if (!File.Exists(path)) return new QuestProgressData();

            return JsonConvert.DeserializeObject<QuestProgressData>(File.ReadAllText(path))
                   ?? new QuestProgressData();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Quests: progress file could not be read: " + e.Message);
            return new QuestProgressData();
        }
    }
}
