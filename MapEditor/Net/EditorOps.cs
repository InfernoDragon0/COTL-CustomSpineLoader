using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// Outbound side: keeps the last document the peer knows about, re-collects the room after edits
/// and ships only the entries that changed. Nothing here is tool-specific; a tool that puts its
/// objects into the blueprint is synchronised by construction.
/// </summary>
internal static class EditorOps
{
    private const float SettleSeconds = 0.15f;
    private const float FallbackSeconds = 3f;
    private const int MaxBatchBytes = 200 * 1024;

    private static Dictionary<string, string> _baseline;
    private static int _seenEdits = -1;
    private static bool _pending;
    private static float _dueAt;
    private static float _nextFallback;

    public static bool HasBaseline => _baseline != null;

    public static int ChangesSent { get; private set; }

    public static void ResetCounters() => ChangesSent = 0;

    public static string BaselineJson(string key) =>
        _baseline != null && _baseline.TryGetValue(key, out var json) ? json : null;

    public static IReadOnlyDictionary<string, string> Baseline => _baseline;

    public static void Reset()
    {
        _baseline = null;
        _seenEdits = -1;
        _pending = false;
    }

    /// The document as it stands becomes the reference; nothing is sent.
    public static void TakeBaseline(RuntimeMapEditor editor)
    {
        if (editor == null) return;
        _baseline = EditorDocument.Collect(editor);
        _seenEdits = editor.EditCount;
        _pending = false;
        _nextFallback = Time.unscaledTime + FallbackSeconds;
    }

    /// The peer's document is now ours too (after a snapshot was applied).
    public static void SetBaseline(Dictionary<string, string> entries)
    {
        _baseline = entries != null ? new Dictionary<string, string>(entries) : null;
        var editor = RuntimeMapEditor.Active;
        _seenEdits = editor != null ? editor.EditCount : -1;
        _pending = false;
    }

    /// <summary>
    /// After remote entries were written into the scene, the baseline takes the re-collected form
    /// of exactly those entries, so they never echo back while edits to other entries made in the
    /// meantime still go out.
    /// </summary>
    public static void AbsorbApplied(RuntimeMapEditor editor, IEnumerable<EntryOp> ops)
    {
        if (editor == null || ops == null) return;

        var doc = EditorDocument.Collect(editor);
        _baseline ??= new Dictionary<string, string>();

        foreach (var op in ops)
        {
            if (op?.K == null) continue;

            // An object still easing toward the peer's release point is not where the document says
            // yet; the baseline takes the record itself so the last few frames of glide never echo.
            if (op.J != null && EditorDocument.Split(op.K, out _, out var id) && EditorLive.IsGliding(id))
            {
                _baseline[op.K] = op.J;
                continue;
            }

            if (doc.TryGetValue(op.K, out var json)) _baseline[op.K] = json;
            else _baseline.Remove(op.K);
        }

        _seenEdits = editor.EditCount;
    }

    /// Everything the scene holds now is the baseline (after a reconcile).
    public static void AbsorbAll(RuntimeMapEditor editor)
    {
        if (editor == null) return;
        _baseline = EditorDocument.Collect(editor);
        _seenEdits = editor.EditCount;
        _pending = false;
    }

    public static void Tick(RuntimeMapEditor editor)
    {
        if (editor == null || !editor.IsEditing || EditorNet.ApplyingRemote || EditorApply.Busy) return;

        var now = Time.unscaledTime;

        if (editor.EditCount != _seenEdits)
        {
            _seenEdits = editor.EditCount;
            _pending = true;
            _dueAt = now + SettleSeconds;
        }

        // A drag in progress is streamed live; the authoritative record goes when the mouse is up.
        if (EditorLive.LocalGestureActive(editor))
        {
            _pending = true;
            _dueAt = now + SettleSeconds;
            return;
        }

        if (_pending && now >= _dueAt)
        {
            _pending = false;
            Flush(editor);
            _nextFallback = now + FallbackSeconds;
            return;
        }

        if (now >= _nextFallback)
        {
            _nextFallback = now + FallbackSeconds;
            Flush(editor);
        }
    }

    /// A gesture just ended or the editor is closing: send whatever changed, now.
    public static void FlushNow(RuntimeMapEditor editor)
    {
        if (editor == null || !EditorNet.Enabled) return;
        _pending = false;
        Flush(editor);
    }

    private static void Flush(RuntimeMapEditor editor)
    {
        var doc = EditorDocument.Collect(editor);

        if (_baseline == null)
        {
            _baseline = doc;
            return;
        }

        var ops = Diff(_baseline, doc);
        _baseline = doc;
        if (ops.Count == 0) return;

        // What we removed must not come back through a peer's change that was already in flight.
        foreach (var op in ops)
            if (op.J == null) EditorApply.Tombstone(op.K);

        Send(ops);
    }

    public static List<EntryOp> Diff(Dictionary<string, string> previous, Dictionary<string, string> next)
    {
        var ops = new List<EntryOp>();

        foreach (var pair in previous)
            if (!next.ContainsKey(pair.Key)) ops.Add(new EntryOp { K = pair.Key, J = null });

        foreach (var pair in next)
            if (!previous.TryGetValue(pair.Key, out var old) || old != pair.Value)
                ops.Add(new EntryOp { K = pair.Key, J = pair.Value });

        return ops;
    }

    private static void Send(List<EntryOp> ops)
    {
        var room = EditorDocument.RoomKey();
        var batch = new OpsMsg { Room = room };
        var bytes = 0;

        foreach (var op in ops)
        {
            var size = (op.K?.Length ?? 0) + (op.J?.Length ?? 0) + 16;
            if (batch.Ops.Count > 0 && bytes + size > MaxBatchBytes)
            {
                EditorNet.Send(Channel.Ops, EditorWire.Encode(batch), reliable: true);
                batch = new OpsMsg { Room = room };
                bytes = 0;
            }

            batch.Ops.Add(op);
            bytes += size;
        }

        if (batch.Ops.Count > 0) EditorNet.Send(Channel.Ops, EditorWire.Encode(batch), reliable: true);

        ChangesSent += ops.Count;

        if (Plugin.EditorNetVerbose.Value)
            Plugin.Log.LogInfo($"EditorNet: sent {ops.Count} change(s): " +
                               string.Join(", ", ops.ConvertAll(o => o.J == null ? "-" + o.K : o.K).GetRange(0, System.Math.Min(ops.Count, 12))) +
                               (ops.Count > 12 ? ", ..." : ""));
        else
            Plugin.Log.LogInfo($"EditorNet: sent {ops.Count} change(s).");
    }
}
