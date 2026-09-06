using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// Inbound side: queues the peer's changes and writes them into the scene one batch at a time,
/// deletes first, then upserts in the loader's phase order, then one collision and navigation
/// rebuild. Runs with history suspended so a peer's work never lands in the local undo stack, and
/// with the same base-ownership flags the delta loader uses so placed structures become buildings.
/// </summary>
internal static class EditorApply
{
    private sealed class Batch
    {
        public List<EntryOp> Ops;
        public Dictionary<string, string> Snapshot;
    }

    private const float TombstoneSeconds = 5f;

    private static readonly Queue<Batch> _queue = new();
    private static readonly Dictionary<string, float> _tombstones = new();
    private static bool _running;
    private static int _generation;
    private static float _nextNoEditorReportAt;

    public static bool Busy => _running || _queue.Count > 0;

    // Session counters for the end-of-session summary line.
    public static int Applied { get; private set; }
    public static int Failures { get; private set; }
    public static int Snapshots { get; private set; }

    public static void Reset()
    {
        _queue.Clear();
        _tombstones.Clear();
        _generation++;
    }

    public static void ResetCounters()
    {
        Applied = 0;
        Failures = 0;
        Snapshots = 0;
    }

    /// A key that was just deleted, here or by the peer; a late upsert for it is dropped.
    public static void Tombstone(string key)
    {
        if (!string.IsNullOrEmpty(key)) _tombstones[key] = Time.unscaledTime + TombstoneSeconds;
    }

    private static bool Tombstoned(string key) =>
        _tombstones.TryGetValue(key, out var until) && Time.unscaledTime < until;

    public static void OnOps(OpsMsg msg)
    {
        if (msg?.Ops == null || msg.Ops.Count == 0) return;

        if (!EditorDocument.SameRoom(msg.Room))
        {
            Plugin.Log.LogInfo($"EditorNet: {msg.Ops.Count} change(s) for room '{msg.Room}' ignored; " +
                               $"we are in '{EditorDocument.RoomKey()}'.");
            return;
        }

        _queue.Enqueue(new Batch { Ops = msg.Ops });
    }

    public static void OnSnapshot(Dictionary<string, string> entries)
    {
        if (entries == null) return;
        Snapshots++;
        _queue.Enqueue(new Batch { Snapshot = entries });
    }

    public static void Tick()
    {
        if (_running || _queue.Count == 0 || Plugin.Instance == null) return;
        Plugin.Instance.StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        _running = true;
        var generation = _generation;

        try
        {
            while (_queue.Count > 0 && generation == _generation)
            {
                var batch = _queue.Dequeue();

                var editor = EnsureEditor();
                if (editor == null)
                {
                    if (Time.unscaledTime >= _nextNoEditorReportAt)
                    {
                        _nextNoEditorReportAt = Time.unscaledTime + 10f;
                        Plugin.Log.LogWarning("EditorNet: changes arrived but no room editor can exist in " +
                                              "this scene; dropped.");
                    }
                    continue;
                }

                var isSnapshot = batch.Snapshot != null;

                // A base that nobody here is editing still takes base changes: the tools work as in
                // the base editor and write into the delta's own blueprint, the same one the host edits.
                var inBase = EditorDocument.EffectiveContext == EditorContext.Base;
                var overrideContext = inBase && !BaseSession.Active;
                if (overrideContext) RuntimeMapEditor.ContextOverride = EditorContext.Base;
                if (inBase) editor.EnsureBaseMap();

                try
                {
                    var ops = isSnapshot ? Reconcile(editor, batch.Snapshot) : batch.Ops;

                    yield return ApplyBatch(editor, ops, isSnapshot, inBase);
                    if (generation != _generation) break;

                    if (isSnapshot) EditorOps.AbsorbAll(editor);
                    else EditorOps.AbsorbApplied(editor, ops);
                }
                finally
                {
                    if (overrideContext) RuntimeMapEditor.ContextOverride = null;
                }
            }
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>
    /// The editor host that can write into this room. In the base a hidden host is created the same
    /// way the delta loader creates one, so a player who is not editing still receives the peer's work.
    /// </summary>
    private static RuntimeMapEditor EnsureEditor()
    {
        var editor = RuntimeMapEditor.Active;
        if (editor != null) return editor;

        if (!BaseSession.SceneOwnsSessions || HubSession.Busy || BaseSession.BaseRoom() == null) return null;

        BaseSession.ClaimRoom();
        BaseDelta.EnsureContentRoot();
        BaseGround.RememberVanillaGround();
        return RuntimeMapEditor.Ensure("RuntimeMapEditorHost_Base");
    }

    private static IEnumerator ApplyBatch(RuntimeMapEditor editor, List<EntryOp> ops, bool isSnapshot, bool inBase)
    {
        if (ops == null || ops.Count == 0) yield break;

        var deletes = new List<(EntryKind Kind, string Key, string Id)>();
        var upserts = new List<(EntryKind Kind, string Key, string Id, string Json)>();

        foreach (var op in ops)
        {
            if (!EditorDocument.Split(op.K, out var kindName, out var id)) continue;
            var kind = EntryKinds.Get(kindName);
            if (kind == null)
            {
                Plugin.Log.LogWarning($"EditorNet: no applier for entry kind '{kindName}'; '{op.K}' skipped.");
                continue;
            }

            if (op.J == null) deletes.Add((kind, op.K, id));
            else upserts.Add((kind, op.K, id, op.J));
        }

        upserts.Sort((a, b) => EditorDocument.Order(a.Kind.Kind).CompareTo(EditorDocument.Order(b.Kind.Kind)));

        EditorNet.ApplyingRemote = true;
        editor.History.Suspended = true;
        if (inBase) BaseDelta.SetApplying(true);

        var rescanNavigation = false;
        var rebuildCollision = false;
        var applied = 0;

        // A kind decides between "adjust in place" and "destroy and respawn" by comparing the incoming
        // record with the one it last saw. With no baseline (a receiver that is not editing, a room
        // just reconciled) that comparison used to fail closed and respawn every existing object -
        // an enemy destroyed without dying, which the multiplayer mod cannot mirror. The scene's own
        // current record is the honest "previous" when the baseline has none.
        Dictionary<string, string> current = null;

        try
        {
            foreach (var (kind, key, id) in deletes)
            {
                try
                {
                    kind.Delete(editor, id, kind.Find(editor, id));
                    Tombstone(key);
                    applied++;
                    rescanNavigation |= kind.NeedsNavigationRescan;
                    rebuildCollision |= kind.NeedsCollisionRebuild;
                }
                catch (Exception e)
                {
                    Failures++;
                    Plugin.Log.LogWarning($"EditorNet: removing '{key}' failed: {e}");
                }
            }

            foreach (var (kind, key, id, json) in upserts)
            {
                if (!isSnapshot && Tombstoned(key))
                {
                    if (Plugin.EditorNetVerbose.Value)
                        Plugin.Log.LogInfo($"EditorNet: '{key}' was deleted moments ago; late change dropped.");
                    continue;
                }

                var existing = kind.Find(editor, id);
                var previous = EditorOps.BaselineJson(key);
                if (previous == null && existing != null)
                {
                    current ??= EditorDocument.Collect(editor);
                    current.TryGetValue(key, out previous);
                }
                var wasGliding = EditorLive.BeginAuthoritative(id, existing, out var glidePosition, out var glideScale);

                var routine = kind.Upsert(editor, id, json, previous, existing);
                var failed = false;
                while (true)
                {
                    try
                    {
                        if (!routine.MoveNext()) break;
                    }
                    catch (Exception e)
                    {
                        failed = true;
                        Failures++;
                        Plugin.Log.LogWarning($"EditorNet: applying '{key}' failed: {e}");
                        break;
                    }
                    yield return routine.Current;
                }

                if (failed) continue;
                applied++;

                // The peer's release landed; the lock lifts, and a dragged object finishes its glide.
                EditorLive.EndAuthoritative(id, kind.Find(editor, id) ?? existing, wasGliding, glidePosition, glideScale);
                rescanNavigation |= kind.NeedsNavigationRescan;
                rebuildCollision |= kind.NeedsCollisionRebuild;
            }

            if (rebuildCollision)
            {
                try
                {
                    editor.GetTool<Tools.DoorTool>()?.FinalizeAllPads();
                    SceneRefs.RegenerateRoomCollision();
                    BaseGround.RequestRefresh();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("EditorNet: collision rebuild after remote changes failed: " + e.Message);
                }
            }
            else if (rescanNavigation)
            {
                SceneRefs.RescanNavigation();
            }
        }
        finally
        {
            if (inBase) BaseDelta.SetApplying(false);
            editor.History.Suspended = false;
            EditorNet.ApplyingRemote = false;
        }

        Applied += applied;

        // Remote work counts as unsaved work, and it wakes the layer tree.
        if (applied > 0)
        {
            editor.MarkEdited();
            editor.RefreshLayers();
        }

        if (Plugin.EditorNetVerbose.Value)
            Plugin.Log.LogInfo($"EditorNet: applied {applied} of {ops.Count} change(s)" +
                               (isSnapshot ? " from a snapshot" : "") + ": " +
                               string.Join(", ", ops.Take(12).Select(o => o.K)) + (ops.Count > 12 ? ", ..." : ""));
        else if (isSnapshot || applied > 0)
            Plugin.Log.LogInfo($"EditorNet: applied {applied} change(s){(isSnapshot ? " from a snapshot" : "")}.");
    }

    // ---- self-test -----------------------------------------------------------------------------

    /// <summary>
    /// Debug (the NetVerbose button): runs the room's own document through the path a peer's snapshot
    /// takes, on one machine. Collects, encodes and decodes the snapshot, then re-applies every entry
    /// to itself with no previous record, so kinds that respawn on a field change do respawn under the
    /// same id, and finally collects again: the room must read back identical. Local undo entries for
    /// respawned objects go stale, as they would after a peer's change.
    /// </summary>
    public static void SelfTest(RuntimeMapEditor editor)
    {
        if (editor == null || Plugin.Instance == null) return;
        if (_running)
        {
            editor.SetStatus("Sync self-test: changes are still being applied; try again in a moment.", StatusSeverity.Warning);
            return;
        }
        Plugin.Instance.StartCoroutine(SelfTestRoutine(editor));
    }

    private static IEnumerator SelfTestRoutine(RuntimeMapEditor editor)
    {
        _running = true;
        var inBase = EditorDocument.EffectiveContext == EditorContext.Base;
        var overrideContext = inBase && !BaseSession.Active;
        if (overrideContext) RuntimeMapEditor.ContextOverride = EditorContext.Base;

        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var before = EditorDocument.Collect(editor);
            var collectMs = clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            var payload = EditorWire.EncodeCompressed(new SnapshotMsg { Room = EditorDocument.RoomKey(), Entries = before });
            var decoded = EditorWire.DecodeCompressed<SnapshotMsg>(payload);
            var wireMs = clock.Elapsed.TotalMilliseconds;
            var wireDiff = decoded?.Entries != null ? EditorOps.Diff(before, decoded.Entries) : null;

            // No baseline: every entry arrives as if never seen, the hardest path for each kind.
            var hadBaseline = EditorOps.HasBaseline;
            EditorOps.Reset();
            var ops = before.Select(p => new EntryOp { K = p.Key, J = p.Value }).ToList();
            var failuresBefore = Failures;

            clock.Restart();
            yield return ApplyBatch(editor, ops, isSnapshot: true, inBase);
            var applyMs = clock.Elapsed.TotalMilliseconds;

            var after = EditorDocument.Collect(editor);
            var diff = EditorOps.Diff(before, after);
            if (hadBaseline || EditorNet.Enabled) EditorOps.AbsorbAll(editor);

            var failures = Failures - failuresBefore;
            var wireText = wireDiff == null ? "decode failed" : wireDiff.Count + " wire difference(s)";
            var summary = $"EditorNet: self-test - {before.Count} entries; collect {collectMs:0.0} ms, " +
                          $"snapshot {payload.Length / 1024} KB in {wireMs:0.0} ms, {wireText}; " +
                          $"re-apply {applyMs:0} ms with {failures} failure(s); {diff.Count} entr(ies) differ after re-apply.";
            var clean = failures == 0 && diff.Count == 0 && wireDiff != null && wireDiff.Count == 0;

            if (clean) Plugin.Log.LogInfo(summary);
            else
            {
                Plugin.Log.LogWarning(summary);
                foreach (var op in diff.Take(20))
                    Plugin.Log.LogWarning($"EditorNet: self-test differs at {op.K}: " +
                                          (op.J == null ? "gone after re-apply" :
                                              before.TryGetValue(op.K, out var was) ? $"was {Trim(was)} now {Trim(op.J)}" : "appeared: " + Trim(op.J)));
                if (wireDiff != null)
                    foreach (var op in wireDiff.Take(10))
                        Plugin.Log.LogWarning($"EditorNet: self-test wire round-trip differs at {op.K}.");
            }

            editor.SetStatus(clean
                    ? $"Sync self-test passed: {before.Count} entries, collect {collectMs:0.0} ms, re-apply {applyMs:0} ms."
                    : $"Sync self-test: {failures} failure(s), {diff.Count} difference(s) - see the log.",
                clean ? StatusSeverity.Info : StatusSeverity.Warning);
        }
        finally
        {
            if (overrideContext) RuntimeMapEditor.ContextOverride = null;
            _running = false;
        }
    }

    private static string Trim(string json) => json == null ? "null" : json.Length <= 160 ? json : json.Substring(0, 160) + "...";

    // ---- reconcile -----------------------------------------------------------------------------

    /// <summary>
    /// Turns the peer's full document into the changes that make this room match it. Entries whose
    /// id is already here are diffed; entries not here are first matched against local objects that
    /// have no counterpart in the snapshot (same thing, same place) and adopt the peer's id, so a
    /// seeded room that both machines generated identically is not torn down and rebuilt; whatever
    /// is left over locally is removed, because the peer's room is the real one.
    /// </summary>
    private static List<EntryOp> Reconcile(RuntimeMapEditor editor, Dictionary<string, string> snapshot)
    {
        var local = EditorDocument.Collect(editor);
        var ops = new List<EntryOp>();

        var unmatched = new Dictionary<string, List<string>>();   // kind -> local keys not in the snapshot
        foreach (var key in local.Keys)
        {
            if (snapshot.ContainsKey(key)) continue;
            var kind = EditorDocument.KindOf(key);
            if (!unmatched.TryGetValue(kind, out var list)) unmatched[kind] = list = [];
            list.Add(key);
        }

        var adopted = 0;
        var spawned = 0;
        var changed = 0;

        foreach (var pair in snapshot)
        {
            if (!EditorDocument.Split(pair.Key, out var kindName, out var id)) continue;

            if (local.TryGetValue(pair.Key, out var localJson))
            {
                if (localJson != pair.Value)
                {
                    ops.Add(new EntryOp { K = pair.Key, J = pair.Value });
                    changed++;
                }
                continue;
            }

            var kind = EntryKinds.Get(kindName);
            var match = kind != null ? FindAdoptable(kind, pair.Value, local, unmatched, kindName) : null;
            if (match != null)
            {
                if (EditorDocument.Split(match, out _, out var localId))
                {
                    var go = kind.Find(editor, localId);
                    if (go != null) EditorIds.Adopt(go, id);
                }

                adopted++;
                if (local[match] != pair.Value)
                {
                    ops.Add(new EntryOp { K = pair.Key, J = pair.Value });
                    changed++;
                }
                continue;
            }

            ops.Add(new EntryOp { K = pair.Key, J = pair.Value });
            spawned++;
        }

        var removed = 0;
        foreach (var list in unmatched.Values)
        foreach (var key in list)
        {
            ops.Add(new EntryOp { K = key, J = null });
            removed++;
        }

        Plugin.Log.LogInfo($"EditorNet: reconciled with {EditorNet.PeerName}'s room - {snapshot.Count} entries, " +
                           $"{adopted} adopted, {changed} changed, {spawned} new, {removed} removed here.");
        if (Plugin.EditorNetVerbose.Value && removed > 0)
            Plugin.Log.LogInfo("EditorNet: removed by reconcile: " +
                               string.Join(", ", unmatched.Values.SelectMany(l => l).Take(20)));

        return ops;
    }

    private static string FindAdoptable(EntryKind kind, string peerJson, Dictionary<string, string> local,
        Dictionary<string, List<string>> unmatched, string kindName)
    {
        if (!unmatched.TryGetValue(kindName, out var candidates) || candidates.Count == 0) return null;

        JObject peer;
        try
        {
            peer = JObject.Parse(peerJson);
        }
        catch (Exception)
        {
            return null;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var key = candidates[i];
            JObject candidate;
            try
            {
                candidate = JObject.Parse(local[key]);
            }
            catch (Exception)
            {
                continue;
            }

            if (!kind.Matches(peer, candidate)) continue;

            candidates.RemoveAt(i);
            return key;
        }

        return null;
    }
}
