using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.APIHelper.NpcQuests;

/// <summary>
/// Lifts our quest lines out of the game's objective lists for the moment it writes its save, and
/// puts them back when the write is done.
///
/// The reason is what the file would mean afterwards. Our lines are real vanilla objectives whose
/// text points at a term this mod registers; left in the save they would still be there with
/// CultTweaker gone, showing a raw term in the quest panel with nothing able to finish them. Our
/// own per-slot file is the real record, so the game's save loses nothing by not holding them.
///
/// This mirrors the base editor's save mask: hide on <c>SaveAndLoad.Saving</c>, restore from the
/// writer's completion callback, with a timeout in case that callback never comes.
/// </summary>
internal static class QuestSaveMask
{
    private enum Where
    {
        Active,
        Completed,
        Failed
    }

    private static readonly List<(ObjectivesData Shell, Where List)> Lifted = [];

    private static bool _masked;
    private static bool _subscribed;
    private static Coroutine _watchdog;

    private const float WriteTimeoutSeconds = 20f;

    internal static void Engage()
    {
        if (_masked) return;

        var data = DataManager.Instance;
        if (data == null) return;

        EnsureSubscribed();

        foreach (var shell in QuestRuntime.Shells())
        {
            if (shell == null) continue;

            if (data.Objectives.Remove(shell)) Lifted.Add((shell, Where.Active));
            else if (data.CompletedObjectives.Remove(shell)) Lifted.Add((shell, Where.Completed));
            else if (data.FailedObjectives.Remove(shell)) Lifted.Add((shell, Where.Failed));
        }

        if (Lifted.Count == 0) return;

        _masked = true;
        StartWatchdog();
    }

    internal static void Release()
    {
        if (!_masked) return;
        _masked = false;

        var data = DataManager.Instance;
        if (data != null)
        {
            foreach (var (shell, list) in Lifted)
            {
                switch (list)
                {
                    case Where.Active:
                        if (!data.Objectives.Contains(shell)) data.Objectives.Add(shell);
                        break;
                    case Where.Completed:
                        if (!data.CompletedObjectives.Contains(shell))
                            data.CompletedObjectives.Add(shell);
                        break;
                    case Where.Failed:
                        if (!data.FailedObjectives.Contains(shell)) data.FailedObjectives.Add(shell);
                        break;
                }
            }
        }

        Lifted.Clear();
        StopWatchdog();
    }

    private static void StartWatchdog()
    {
        StopWatchdog();
        if (Plugin.Instance == null) return;
        _watchdog = Plugin.Instance.StartCoroutine(WatchdogRoutine());
    }

    private static void StopWatchdog()
    {
        if (_watchdog == null || Plugin.Instance == null)
        {
            _watchdog = null;
            return;
        }

        Plugin.Instance.StopCoroutine(_watchdog);
        _watchdog = null;
    }

    private static IEnumerator WatchdogRoutine()
    {
        yield return new WaitForSecondsRealtime(WriteTimeoutSeconds);

        if (!_masked) yield break;

        Plugin.Log.LogWarning("Quests: the save write never reported back; putting the quest lines " +
                              "back in the panel.");
        Release();
    }

    private static void EnsureSubscribed()
    {
        if (_subscribed) return;

        try
        {
            var instance = Singleton<SaveAndLoad>.Instance;
            if (instance == null) return;

            var field = AccessTools.Field(typeof(SaveAndLoad), "_saveFileReadWriter");
            if (field?.GetValue(instance) is not MMDataReadWriterBase<DataManager> writer)
            {
                Plugin.Log.LogWarning("Quests: the game's save writer has changed shape; quest " +
                                      "lines will be restored by timeout rather than by callback.");
                _subscribed = true;
                return;
            }

            writer.OnWriteCompleted = (Action)Delegate.Combine(writer.OnWriteCompleted, (Action)Release);
            writer.OnWriteError = (Action<MMReadWriteError>)Delegate.Combine(writer.OnWriteError,
                (Action<MMReadWriteError>)(_ => Release()));

            _subscribed = true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Quests: could not listen for the save write finishing: " + e.Message);
            _subscribed = true;
        }
    }
}
