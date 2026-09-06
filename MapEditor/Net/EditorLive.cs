using System.Collections.Generic;
using CustomSpineLoader.MapEditor.Tools;
using UnityEngine;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// A tool that drags things reports what it is dragging while the mouse is down, so the peer sees
/// the motion instead of a jump on release. The authoritative record still goes through the
/// document diff when the gesture ends.
/// </summary>
public interface IMapEditorLivePreview
{
    bool LiveActive { get; }

    IEnumerable<GameObject> LiveObjects { get; }
}

internal static class EditorLive
{
    private const float Interval = 0.05f;
    private const float ShapeInterval = 0.1f;
    private const float StaleSeconds = 3f;

    /// How fast a peer-dragged object closes on its latest reported transform (per second, exponential).
    private const float GlideRate = 14f;

    private static float _nextSend;
    private static float _nextShapeSend;
    private static readonly Dictionary<string, (Vector3 Position, Vector3 Scale)> _lastSent = new();
    private static bool _loggedFirst;
    private static ushort _seq;
    private static bool _haveSeq;
    private static ushort _lastSeq;

    /// Objects the peer is dragging right now, by id, and when we last heard about them.
    private static readonly Dictionary<string, float> _peerDragging = new();

    /// Where each peer-dragged object is heading. Transforms arrive at 20 Hz; the object is moved
    /// toward the latest one every frame instead of jumping to it, the same easing the cursor uses.
    private sealed class Glide
    {
        public Vector3 Position;
        public Vector3 Scale;
        public bool HasScale;
        public float UpdatedAt;
    }

    private static readonly Dictionary<string, Glide> _glides = new();

    public static void Reset()
    {
        _lastSent.Clear();
        _peerDragging.Clear();
        _glides.Clear();
        _loggedFirst = false;
        _haveSeq = false;
    }

    public static bool LocalGestureActive(RuntimeMapEditor editor)
    {
        if (editor == null || !editor.IsEditing) return false;
        foreach (var preview in editor.LivePreviews())
            if (preview.LiveActive) return true;
        return false;
    }

    /// True while the peer is dragging this object; the local side leaves it alone.
    public static bool PeerDragging(string id) =>
        !string.IsNullOrEmpty(id) && _peerDragging.TryGetValue(id, out var at) &&
        Time.unscaledTime - at < StaleSeconds;

    public static void Tick(RuntimeMapEditor editor)
    {
        if (!EditorNet.Enabled) return;

        Smooth();

        if (editor == null || !editor.IsEditing) return;

        var now = Time.unscaledTime;
        if (now < _nextSend) return;
        _nextSend = now + Interval;

        LiveMsg transforms = null;
        LiveMsg records = null;
        var shapesDue = now >= _nextShapeSend;

        foreach (var preview in editor.LivePreviews())
        {
            if (!preview.LiveActive) continue;

            foreach (var go in preview.LiveObjects)
            {
                if (go == null) continue;

                var trigger = go.GetComponentInChildren<CTMapTrigger>(true);
                if (trigger != null)
                {
                    if (!shapesDue) continue;
                    records ??= new LiveMsg { Room = EditorDocument.RoomKey(), Shapes = [] };
                    records.Shapes.Add(new EntryOp
                    {
                        K = EditorDocument.Key(EditorDocument.Trigger, trigger.Id),
                        J = EditorWire.Canon(TriggerTool.Describe(trigger))
                    });
                    continue;
                }

                var stroke = go.GetComponent<CTWhiteboardStroke>();
                if (stroke != null)
                {
                    if (!shapesDue) continue;
                    records ??= new LiveMsg { Room = EditorDocument.RoomKey(), Shapes = [] };
                    records.Shapes.Add(new EntryOp
                    {
                        K = EditorDocument.Key(EditorDocument.Stroke, stroke.Id),
                        J = EditorWire.Canon(WhiteboardTool.Describe(stroke))
                    });
                    continue;
                }

                var ctrl = go.GetComponent<SpriteShapeController>();
                if (ctrl != null)
                {
                    if (!shapesDue) continue;
                    var shapeId = EditorIds.Of(go);
                    records ??= new LiveMsg { Room = EditorDocument.RoomKey(), Shapes = [] };
                    records.Shapes.Add(new EntryOp
                    {
                        K = EditorDocument.Key(EditorDocument.Shape, shapeId),
                        J = EditorWire.Canon(ShapeTool.Describe(ctrl))
                    });
                    continue;
                }

                var id = EditorIds.Of(go);
                var position = go.transform.position;
                var scale = go.transform.localScale;
                if (_lastSent.TryGetValue(id, out var last) && last.Position == position && last.Scale == scale)
                    continue;

                _lastSent[id] = (position, scale);
                transforms ??= new LiveMsg { Room = EditorDocument.RoomKey() };
                transforms.Items.Add(new LiveItem
                {
                    Id = id,
                    X = position.x, Y = position.y, Z = position.z,
                    SX = scale.x, SY = scale.y, SZ = scale.z
                });
            }
        }

        if (shapesDue && records != null) _nextShapeSend = now + ShapeInterval;

        // Transforms are tiny and superseded 50 ms later, so they may be lost; a shape or trigger
        // record is a few kilobytes and is sent reliably at a slower rate.
        if (transforms != null)
        {
            transforms.Seq = ++_seq;
            EditorNet.Send(Channel.Live, EditorWire.Encode(transforms), reliable: false);
        }
        if (records != null)
        {
            records.Seq = ++_seq;
            EditorNet.Send(Channel.Live, EditorWire.Encode(records), reliable: true);
        }
    }

    public static void OnLive(LiveMsg msg)
    {
        if (msg == null || !EditorDocument.SameRoom(msg.Room)) return;

        if (!_loggedFirst)
        {
            _loggedFirst = true;
            Plugin.Log.LogInfo($"EditorNet: live drag stream from {EditorNet.PeerName} started.");
        }

        var editor = RuntimeMapEditor.Active;
        var now = Time.unscaledTime;

        // Unreliable transforms may arrive out of order; one older than what is already applied would
        // pull the object backwards for a frame. Records are reliable and ordered, they always apply.
        var newer = !_haveSeq || EditorPresence.Newer(msg.Seq, _lastSeq);
        if (newer)
        {
            _haveSeq = true;
            _lastSeq = msg.Seq;
        }

        if (newer && msg.Items != null)
        {
            foreach (var item in msg.Items)
            {
                var go = EditorIds.Find(item.Id);
                if (go == null) continue;

                _peerDragging[item.Id] = now;

                var position = new Vector3(item.X, item.Y, item.Z);
                var scale = new Vector3(item.SX, item.SY, item.SZ);
                var hasScale = item.SX != 0f;

                if (!_glides.TryGetValue(item.Id, out var glide))
                {
                    // First word of a new drag: start from where the peer says, not from wherever
                    // the object happened to stand, so nothing glides in from across the room.
                    go.transform.position = position;
                    if (hasScale) go.transform.localScale = scale;
                    _glides[item.Id] = new Glide { Position = position, Scale = scale, HasScale = hasScale, UpdatedAt = now };
                    continue;
                }

                glide.Position = position;
                glide.Scale = scale;
                glide.HasScale = hasScale;
                glide.UpdatedAt = now;
            }
        }

        if (msg.Shapes == null || editor == null) return;

        foreach (var op in msg.Shapes)
        {
            if (op?.J == null || !EditorDocument.Split(op.K, out var kind, out var id)) continue;
            _peerDragging[id] = now;

            if (kind == EditorDocument.Trigger)
            {
                var tool = editor.GetTool<TriggerTool>();
                var trigger = tool?.FindTrigger(id);
                var record = EditorWire.Parse<MapTriggerData>(op.J);
                if (trigger != null && record != null) tool.ApplyRecord(trigger, record);
            }
            else if (kind == EditorDocument.Shape)
            {
                var go = EditorIds.Find(id);
                var ctrl = go != null ? go.GetComponent<SpriteShapeController>() : null;
                var record = EditorWire.Parse<MapShapeData>(op.J);
                if (ctrl != null && record != null) editor.GetTool<ShapeTool>()?.ApplyRemoteShape(ctrl, record, bake: false);
            }
            else if (kind == EditorDocument.Stroke)
            {
                var record = EditorWire.Parse<MapStrokeData>(op.J);
                if (record != null)
                {
                    record.Id = id;
                    editor.GetTool<WhiteboardTool>()?.ApplyRecord(record);
                }
            }
        }
    }

    // ---- smoothing -------------------------------------------------------------------------------

    private static readonly List<string> _finished = [];

    /// Every frame: move each peer-dragged object a step toward its latest reported transform.
    private static void Smooth()
    {
        if (_glides.Count == 0) return;

        var now = Time.unscaledTime;
        var k = 1f - Mathf.Exp(-GlideRate * Time.unscaledDeltaTime);
        _finished.Clear();

        foreach (var pair in _glides)
        {
            var go = EditorIds.Find(pair.Key);
            if (go == null)
            {
                _finished.Add(pair.Key);
                continue;
            }

            var glide = pair.Value;
            var t = go.transform;
            t.position = Vector3.Lerp(t.position, glide.Position, k);
            if (glide.HasScale) t.localScale = Vector3.Lerp(t.localScale, glide.Scale, k);

            var arrived = (t.position - glide.Position).sqrMagnitude < 1e-6f &&
                          (!glide.HasScale || (t.localScale - glide.Scale).sqrMagnitude < 1e-6f);
            if (arrived)
            {
                t.position = glide.Position;
                if (glide.HasScale) t.localScale = glide.Scale;
                if (now - glide.UpdatedAt > StaleSeconds) _finished.Add(pair.Key);
            }
        }

        foreach (var id in _finished) _glides.Remove(id);
    }

    /// True while this object is still easing toward a peer-reported transform.
    public static bool IsGliding(string id) => !string.IsNullOrEmpty(id) && _glides.ContainsKey(id);

    /// <summary>
    /// The authoritative record for an object the peer was dragging is about to be written. The
    /// record's transform should become the glide's destination rather than a snap, so the release
    /// does not visibly jump: remember where the eased copy stands now.
    /// </summary>
    public static bool BeginAuthoritative(string id, GameObject go, out Vector3 position, out Vector3 scale)
    {
        position = default;
        scale = default;
        if (go == null || !IsGliding(id)) return false;

        position = go.transform.position;
        scale = go.transform.localScale;
        return true;
    }

    /// <summary>
    /// The record landed: the object now stands where the document says. Put it back where the
    /// glide had it and let the glide finish there; the peer's drag is over, so the drag lock lifts.
    /// </summary>
    public static void EndAuthoritative(string id, GameObject go, bool wasGliding, Vector3 position, Vector3 scale)
    {
        if (string.IsNullOrEmpty(id)) return;

        _peerDragging.Remove(id);

        if (!wasGliding || go == null || !_glides.TryGetValue(id, out var glide)) return;

        glide.Position = go.transform.position;
        glide.Scale = go.transform.localScale;
        glide.HasScale = true;
        glide.UpdatedAt = Time.unscaledTime;

        go.transform.position = position;
        go.transform.localScale = scale;
    }
}
