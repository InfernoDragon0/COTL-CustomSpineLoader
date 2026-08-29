using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class SaveMask
{
    private class Entry
    {
        public StructuresData Data;
        public Vector3 Original;
        public Vector2Int OriginalCell;

        public Vector3 Live;
        public Vector2Int LiveCell;
    }

    private static readonly List<Entry> _entries = [];
    private static bool _masked;
    private static bool _subscribed;
    private static Coroutine _watchdog;

    private const float WriteTimeoutSeconds = 20f;

    public static bool Anything => _entries.Count > 0;

    // ---- registration ---------------------------------------------------------------------------

    public static void Register(StructuresData data, Vector3 originalPosition, Vector2Int originalCell)
    {
        if (data == null) return;

        if (data.DontLoadMe)
        {
            Plugin.Log.LogWarning($"Base editor: refusing to mask {data.Type} - it is anchored to " +
                                  "the scene and its position is what the game finds it by.");
            return;
        }

        var existing = _entries.Find(e => ReferenceEquals(e.Data, data));
        if (existing != null)
        {
            existing.Original = originalPosition;
            existing.OriginalCell = originalCell;
            return;
        }

        _entries.Add(new Entry
        {
            Data = data,
            Original = originalPosition,
            OriginalCell = originalCell
        });

        EnsureSubscribed();
    }

    public static void Forget()
    {
        foreach (var entry in _entries)
        {
            if (entry.Data == null) continue;
            entry.Data.Position = entry.Original;
            entry.Data.GridTilePosition = entry.OriginalCell;
        }

        _masked = false;
        StopWatchdog();
        _entries.Clear();
    }

    // ---- the mask -------------------------------------------------------------------------------

    public static void Engage()
    {
        if (_masked || _entries.Count == 0) return;

        _masked = true;
        var masked = 0;

        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.Data == null)
            {
                _entries.RemoveAt(i);
                continue;
            }

            entry.Live = entry.Data.Position;
            entry.LiveCell = entry.Data.GridTilePosition;
            entry.Data.Position = entry.Original;
            entry.Data.GridTilePosition = entry.OriginalCell;
            masked++;
        }

        Plugin.Log.LogInfo($"Base editor: {masked} moved building(s) hidden from this save write; " +
                           "the game's file keeps their original places.");

        StartWatchdog();
    }

    public static void Release()
    {
        if (!_masked) return;
        _masked = false;

        foreach (var entry in _entries)
        {
            if (entry.Data == null) continue;
            entry.Data.Position = entry.Live;
            entry.Data.GridTilePosition = entry.LiveCell;
        }

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

        Plugin.Log.LogWarning("Base editor: the save write never reported back; putting the moved " +
                              "buildings back where the editor had them.");
        Release();
    }

    // ---- the writer -----------------------------------------------------------------------------

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
                Plugin.Log.LogWarning("Base editor: the game's save writer has changed shape; moved " +
                                      "buildings will be masked by timeout rather than by callback.");
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
            Plugin.Log.LogWarning("Base editor: could not listen for the save write finishing: " + e.Message);
            _subscribed = true;
        }
    }
}
