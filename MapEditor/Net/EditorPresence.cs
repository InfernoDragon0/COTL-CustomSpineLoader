using System.Collections.Generic;
using System.Linq;
using CustomSpineLoader.MapEditor.Tools;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// Who is doing what: the peer's cursor, tool and selection. The selection doubles as a soft lock -
/// what the peer holds, the local tools refuse to pick up - and it is drawn in a second colour so
/// the two selections can never be confused. Local stays the editor's cyan; the peer is amber.
/// </summary>
internal static class EditorPresence
{
    public static readonly Color LocalColour = MapEditorGizmos.BoxColour;
    public static readonly Color PeerColour = new(0.949f, 0.627f, 0.239f, 1f);   // #F2A03D
    public const string PeerColourTag = "#F2A03D";

    private const float Interval = 0.1f;
    private const float StaleSeconds = 4f;
    private const float TintBlend = 0.45f;

    /// The sender drops its cursor whenever the mouse crosses one of its own panels; the receiver keeps
    /// drawing it this long before believing the cursor is really gone, so it does not flicker.
    private const float CursorGraceSeconds = 0.3f;

    private static float _nextSend;
    private static string _lastSignature;
    private static ushort _seq;

    // ---- the peer -------------------------------------------------------------------------------

    private static bool _peerEditing;
    private static string _peerTool = "";
    private static bool _peerHasCursor;
    private static Vector3 _peerCursor;
    private static float _peerCursorLostAt = -100f;
    private static float _peerSeenAt = -100f;
    private static bool _haveSeq;
    private static ushort _lastSeq;
    private static readonly HashSet<string> _peerSelected = new();

    private static GameObject _cursor;
    private static TextMesh _cursorLabel;
    private static readonly List<GameObject> _boxes = [];
    private static readonly Dictionary<GameObject, List<(Renderer Renderer, Color Original)>> _tinted = new();
    private static bool _loggedFirst;

    public static bool PeerEditing => _peerEditing && Time.unscaledTime - _peerSeenAt < StaleSeconds;

    public static string PeerTool => _peerTool;

    public static void Reset()
    {
        _peerEditing = false;
        _peerHasCursor = false;
        _peerSelected.Clear();
        _lastSignature = null;
        _loggedFirst = false;
        _haveSeq = false;
        ClearTints();
        HideVisuals();
    }

    public static void RoomChanged()
    {
        _peerSelected.Clear();
        ClearTints();
        HideVisuals();
        RefreshLayerRows();
    }

    public static void PeerStoppedEditing()
    {
        _peerEditing = false;
        _peerSelected.Clear();
        ClearTints();
        HideVisuals();
        RefreshLayerRows();
    }

    // ---- locks -----------------------------------------------------------------------------------

    /// The id presence uses for an object: a trigger's own id, otherwise its editor id (never minted here).
    public static string IdOf(GameObject go)
    {
        if (go == null) return null;
        var trigger = go.GetComponentInChildren<CTMapTrigger>(true);
        if (trigger != null && !string.IsNullOrEmpty(trigger.Id)) return trigger.Id;
        return EditorIds.Peek(go);
    }

    public static bool IsPeerSelected(GameObject go)
    {
        if (!PeerEditing || _peerSelected.Count == 0) return false;
        var id = IdOf(go);
        return id != null && _peerSelected.Contains(id);
    }

    public static bool IsPeerSelectedId(string id) => PeerEditing && id != null && _peerSelected.Contains(id);

    /// True when the peer holds it: selected on their side, or mid-drag.
    public static bool LockedByPeer(GameObject go)
    {
        if (!EditorNet.Enabled || go == null) return false;
        if (IsPeerSelected(go)) return true;
        var id = IdOf(go);
        return id != null && EditorLive.PeerDragging(id);
    }

    public static string LockMessage => $"In use by {EditorNet.PeerName}.";

    // ---- outgoing --------------------------------------------------------------------------------

    public static void Tick(RuntimeMapEditor editor)
    {
        if (!EditorNet.Enabled) return;

        var now = Time.unscaledTime;
        var msg = Build(editor);
        var signature = Signature(msg);
        var changed = signature != _lastSignature;

        // Idle players say nothing until something changes; editing ones stream the cursor.
        if (changed || (msg.Editing && now >= _nextSend))
        {
            _nextSend = now + Interval;
            _lastSignature = signature;
            Send(msg, reliable: changed);
        }

        DrawPeer(editor);
    }

    public static void SendNow(RuntimeMapEditor editor)
    {
        if (!EditorNet.Enabled) return;
        var msg = Build(editor);
        _lastSignature = Signature(msg);
        _nextSend = Time.unscaledTime + Interval;
        Send(msg, reliable: true);
    }

    private static void Send(PresenceMsg msg, bool reliable)
    {
        msg.Seq = ++_seq;
        EditorNet.Send(Channel.Presence, EditorWire.Encode(msg), reliable);
    }

    /// Wraparound-safe "is this newer than the last one we took": true when seq is ahead of last.
    internal static bool Newer(ushort seq, ushort last) => (short)(seq - last) > 0;

    private static PresenceMsg Build(RuntimeMapEditor editor)
    {
        var msg = new PresenceMsg { Room = EditorDocument.RoomKey() };
        if (editor == null || !editor.IsEditing) return msg;

        msg.Editing = true;
        msg.Tool = editor.ActiveToolName;
        msg.Selected = editor.SelectedIds();

        if (!editor.ModalOpen && !editor.PointerOverUi())
        {
            var world = editor.MouseWorld();
            msg.HasCursor = true;
            msg.CX = world.x;
            msg.CY = world.y;
        }

        return msg;
    }

    private static string Signature(PresenceMsg msg) =>
        (msg.Editing ? "1" : "0") + "|" + msg.Tool + "|" + string.Join(",", msg.Selected);

    // ---- incoming --------------------------------------------------------------------------------

    public static void OnPresence(PresenceMsg msg)
    {
        if (msg == null) return;

        if (!EditorDocument.SameRoom(msg.Room))
        {
            if (_peerEditing) PeerStoppedEditing();
            return;
        }

        if (!_loggedFirst)
        {
            _loggedFirst = true;
            Plugin.Log.LogInfo($"EditorNet: presence from {EditorNet.PeerName} started.");
        }

        // Presence rides the unreliable channel; a late older message must not step the cursor back.
        if (_haveSeq && !Newer(msg.Seq, _lastSeq)) return;
        _haveSeq = true;
        _lastSeq = msg.Seq;

        _peerSeenAt = Time.unscaledTime;
        _peerEditing = msg.Editing;
        _peerTool = msg.Tool ?? "";

        if (msg.Editing && msg.HasCursor)
        {
            _peerHasCursor = true;
            _peerCursor = new Vector3(msg.CX, msg.CY, 0f);
        }
        else if (_peerHasCursor)
        {
            _peerHasCursor = false;
            _peerCursorLostAt = Time.unscaledTime;
        }

        var incoming = msg.Selected ?? [];
        var selectionChanged = incoming.Count != _peerSelected.Count || incoming.Any(id => !_peerSelected.Contains(id));
        if (!selectionChanged) return;

        _peerSelected.Clear();
        if (msg.Editing) foreach (var id in incoming) _peerSelected.Add(id);

        RetintPeerSelection();
        RefreshLayerRows();
    }

    // ---- drawing ---------------------------------------------------------------------------------

    private static void DrawPeer(RuntimeMapEditor editor)
    {
        var show = PeerEditing && editor != null && editor.IsEditing;
        if (!show)
        {
            HideVisuals();
            if (_tinted.Count > 0 && !PeerEditing) ClearTints();
            return;
        }

        DrawCursor();
        DrawBoxes();
    }

    private static void DrawCursor()
    {
        // Gone for good, or only crossing a panel? Keep it where it was for a moment before deciding.
        if (!_peerHasCursor && Time.unscaledTime - _peerCursorLostAt > CursorGraceSeconds)
        {
            if (_cursor != null) _cursor.SetActive(false);
            return;
        }

        if (_cursor == null) BuildCursor();
        if (_cursor == null) return;

        if (!_cursor.activeSelf)
        {
            _cursor.SetActive(true);
            _cursor.transform.position = _peerCursor;
        }
        else
        {
            var current = _cursor.transform.position;
            _cursor.transform.position = Vector3.Lerp(current, _peerCursor, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        }

        if (_cursorLabel != null)
        {
            var text = EditorNet.PeerName + (string.IsNullOrEmpty(_peerTool) ? "" : " - " + _peerTool);
            if (_cursorLabel.text != text) _cursorLabel.text = text;
        }
    }

    private static void BuildCursor()
    {
        _cursor = new GameObject("MapEditor_PeerCursor");

        var line = _cursor.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.startWidth = line.endWidth = 0.06f;
        line.sharedMaterial = MapEditorGizmos.LineMaterial();
        line.startColor = line.endColor = PeerColour;
        line.sortingOrder = 32000;

        const int segments = 24;
        const float radius = 0.35f;
        line.positionCount = segments;
        for (var i = 0; i < segments; i++)
        {
            var angle = i / (float)segments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -0.1f));
        }

        var labelGo = new GameObject("Name");
        labelGo.transform.SetParent(_cursor.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, radius + 0.25f, -0.1f);

        _cursorLabel = labelGo.AddComponent<TextMesh>();
        _cursorLabel.anchor = TextAnchor.LowerCenter;
        _cursorLabel.alignment = TextAlignment.Center;
        _cursorLabel.characterSize = 0.12f;
        _cursorLabel.fontSize = 48;
        _cursorLabel.color = PeerColour;
        try
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                _cursorLabel.font = font;
                var renderer = labelGo.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = font.material;
                renderer.sortingOrder = 32000;
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogInfo("EditorNet: no built-in font for the peer cursor label: " + e.Message);
        }
    }

    private static void DrawBoxes()
    {
        var targets = new List<GameObject>();
        foreach (var id in _peerSelected)
        {
            var go = EditorIds.Find(id);
            if (go == null)
            {
                var trigger = RuntimeMapEditor.Active?.GetTool<TriggerTool>()?.FindTrigger(id);
                if (trigger != null) go = trigger.gameObject;
            }
            if (go != null) targets.Add(go);
        }

        while (_boxes.Count < targets.Count)
            _boxes.Add(MapEditorGizmos.CreateBox("MapEditor_PeerSelection", PeerColour));

        for (var i = 0; i < _boxes.Count; i++)
        {
            var box = _boxes[i];
            if (box == null) continue;

            if (i >= targets.Count)
            {
                if (box.activeSelf) box.SetActive(false);
                continue;
            }

            var target = targets[i];
            if (!MapEditorGizmos.TryGetBounds(target, out var bounds))
            {
                var trigger = target.GetComponentInChildren<CTMapTrigger>(true);
                if (trigger == null)
                {
                    if (box.activeSelf) box.SetActive(false);
                    continue;
                }
                var rect = trigger.WorldRect;
                bounds = new Bounds(new Vector3(rect.center.x, rect.center.y, 0f), new Vector3(rect.width, rect.height, 0f));
            }

            if (!box.activeSelf) box.SetActive(true);
            MapEditorGizmos.SetBox(box, bounds, target.transform.position.z - 0.06f);
        }
    }

    private static void HideVisuals()
    {
        if (_cursor != null && _cursor.activeSelf) _cursor.SetActive(false);
        foreach (var box in _boxes)
            if (box != null && box.activeSelf) box.SetActive(false);
    }

    // ---- tint --------------------------------------------------------------------------------------

    private static void RetintPeerSelection()
    {
        ClearTints();
        if (!_peerEditing) return;

        var editor = RuntimeMapEditor.Active;
        foreach (var id in _peerSelected)
        {
            var go = EditorIds.Find(id);
            if (go == null || go.GetComponentInChildren<CTMapTrigger>(true) != null) continue;
            if (editor != null && editor.IsLocallySelected(go)) continue;   // the local cyan wins on a race
            Tint(go);
        }
    }

    private static void Tint(GameObject go)
    {
        var list = new List<(Renderer, Color)>();
        foreach (var renderer in go.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null) continue;

            if (renderer is SpriteRenderer sprite)
            {
                list.Add((renderer, sprite.color));
                sprite.color = Color.Lerp(sprite.color, PeerColour, TintBlend);
                continue;
            }

            var shared = renderer.sharedMaterial;
            if (shared == null || !shared.HasProperty("_Color")) continue;

            list.Add((renderer, shared.color));
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", Color.Lerp(shared.color, PeerColour, TintBlend));
            renderer.SetPropertyBlock(block);
        }

        if (list.Count > 0) _tinted[go] = list;
    }

    private static void ClearTints()
    {
        foreach (var pair in _tinted)
        {
            foreach (var (renderer, original) in pair.Value)
            {
                if (renderer == null) continue;
                if (renderer is SpriteRenderer sprite) sprite.color = original;
                else renderer.SetPropertyBlock(null);
            }
        }
        _tinted.Clear();
    }

    private static void RefreshLayerRows() => RuntimeMapEditor.Active?.RefreshLayers();
}
