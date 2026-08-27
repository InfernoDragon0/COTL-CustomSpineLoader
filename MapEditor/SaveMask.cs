using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// The safeguard, enforced.
//
// A building the base editor moved has to be moved in the game's own terms - the grid it is stamped
// on, and the position every follower navigates by, both live on the save's own StructuresData. So
// for the game to behave, that data must say the new place; for the player's save to stay untouched,
// the file must say the old one. Both are true, at different moments.
//
// The moment that matters is the write. The save is serialized from the live object on a background
// thread, so nothing can be masked "around Save()" without racing the serializer. Instead this hooks
// the writer itself: the call that spawns that thread is on the main thread, and its prefix puts
// every moved building back where the player left it. When the write reports itself done - also on
// the main thread, marshalled there by the game - the moves go back on.
//
// The window is a few frames, only exists while a save is in flight, and the state it leaves behind
// if anything goes wrong is the vanilla layout, which is safe by construction: the moves re-apply on
// the next arrival from our own file.
public static class SaveMask
{
    private class Entry
    {
        public StructuresData Data;
        public Vector3 Original;
        public Vector2Int OriginalCell;

        // What the mask lifted, so it can be put back.
        public Vector3 Live;
        public Vector2Int LiveCell;
    }

    private static readonly List<Entry> _entries = [];
    private static bool _masked;
    private static bool _subscribed;
    private static Coroutine _watchdog;

    // How long a save may take before the mask lifts itself. The callback is reliable, but a mask
    // left engaged would mean a moved building silently standing back where it started.
    private const float WriteTimeoutSeconds = 20f;

    public static bool Anything => _entries.Count > 0;

    // ---- registration ---------------------------------------------------------------------------

    public static void Register(StructuresData data, Vector3 originalPosition, Vector2Int originalCell)
    {
        if (data == null) return;

        // A backstop, not a policy - BaseDelta already refuses to move these. A scene-anchored
        // building is found on load by its position matching a scene object exactly, so its position
        // is not a fact this mod may write at all, masked or otherwise.
        if (data.DontLoadMe)
        {
            Plugin.Log.LogWarning($"Base editor: refusing to mask {data.Type} - it is anchored to " +
                                  "the scene and its position is what the game finds it by.");
            return;
        }

        var existing = _entries.Find(e => ReferenceEquals(e.Data, data));
        if (existing != null)
        {
            // The original stays the original however many times it is moved after that.
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

    // Stop tracking these buildings - and leave the game holding exactly what it started with.
    //
    // This used to lift the mask first, which was precisely backwards and cost a player a temple.
    // Lifting it writes the moved positions back into the live save data, and the entries that would
    // have hidden them again are then thrown away - so the very next save wrote a moved position
    // into the player's file. For a scene-anchored building (a temple, a shrine) that is fatal: the
    // game binds those to their scene object by exact position equality on load, finds nothing where
    // the save says to look, and deletes the entry.
    //
    // Forgetting therefore means putting everything back, whether or not a write is in flight. The
    // original is what the file holds and what the scene object stands at, so it is the only state
    // that is safe to walk away from.
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

    // Called from the writer's prefix, on the main thread, before the serializing thread starts.
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

    // The completion signal is a field on the read-writer the game holds privately, and both the
    // success and the error path are marshalled onto the main thread before they fire. Subscribed
    // once, the first time anything is registered - a game with no base moves never touches it.
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
