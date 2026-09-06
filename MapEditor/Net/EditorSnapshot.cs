using UnityEngine;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// Full-document exchange. The host's room is the real one: a guest says hello when its room is
/// ready or its editor opens, and the host answers with everything if its room has drifted from
/// what the files say (an editor is open, or edits were made and not saved). The guest reconciles.
/// </summary>
internal static class EditorSnapshot
{
    private static float _helloDueAt = -1f;

    public static int Sent { get; private set; }

    public static void Reset()
    {
        _helloDueAt = -1f;
    }

    public static void ResetCounters() => Sent = 0;

    public static void ScheduleHello(float delaySeconds) => _helloDueAt = Time.unscaledTime + delaySeconds;

    public static void Tick()
    {
        if (_helloDueAt < 0f || Time.unscaledTime < _helloDueAt) return;
        _helloDueAt = -1f;
        SendHello();
    }

    /// Guests announce where they are; the host decides whether anything needs to travel.
    public static void SendHello()
    {
        if (!EditorNet.Enabled || EditorNet.IsHost) return;

        var msg = new HelloMsg { Room = EditorDocument.RoomKey(), Editing = EditorNet.LocalEditorOpen };
        EditorNet.Send(Channel.Hello, EditorWire.Encode(msg), reliable: true);
        if (Plugin.EditorNetVerbose.Value) Plugin.Log.LogInfo($"EditorNet: hello for room '{msg.Room}'.");
    }

    public static void OnHello(HelloMsg msg)
    {
        if (msg == null || !EditorNet.IsHost) return;

        if (msg.Editing) EditorNet.NoteRemoteEditing(true);

        // A guest that just arrived, or just changed rooms, needs to know about a level in progress
        // before it follows us into the next floor.
        if (LevelPlayback.Active) EditorNet.LevelChanged();

        if (!EditorDocument.SameRoom(msg.Room))
        {
            Plugin.Log.LogInfo($"EditorNet: {EditorNet.PeerName} is in room '{msg.Room}', we are in " +
                               $"'{EditorDocument.RoomKey()}'; no snapshot.");
            return;
        }

        var editor = RuntimeMapEditor.Active;
        var drifted = editor != null && (editor.IsEditing || editor.EditCount > 0 || EditorNet.RemoteEditorOpen);
        if (drifted) SendSnapshot("hello from " + EditorNet.PeerName);
        else if (Plugin.EditorNetVerbose.Value)
            Plugin.Log.LogInfo("EditorNet: rooms agree with the files; nothing to send.");
    }

    public static void SendSnapshot(string reason)
    {
        if (!EditorNet.Enabled || !EditorNet.IsHost) return;

        var editor = RuntimeMapEditor.Active;
        if (editor == null)
        {
            Plugin.Log.LogInfo($"EditorNet: no editor here to snapshot from ({reason}).");
            return;
        }

        var entries = EditorDocument.Collect(editor);
        EditorOps.SetBaseline(entries);

        var payload = EditorWire.EncodeCompressed(new SnapshotMsg { Room = EditorDocument.RoomKey(), Entries = entries });
        EditorNet.Send(Channel.Snapshot, payload, reliable: true);
        Sent++;

        Plugin.Log.LogInfo($"EditorNet: sent a snapshot of {entries.Count} entries ({payload.Length / 1024} KB) - {reason}.");
    }

    public static void OnSnapshot(byte[] payload)
    {
        if (EditorNet.IsHost)
        {
            Plugin.Log.LogInfo("EditorNet: a snapshot arrived at the host; ignored, the host's room is the real one.");
            return;
        }

        var msg = EditorWire.DecodeCompressed<SnapshotMsg>(payload);
        if (msg?.Entries == null) return;

        if (!EditorDocument.SameRoom(msg.Room))
        {
            Plugin.Log.LogInfo($"EditorNet: snapshot for room '{msg.Room}' ignored; we are in '{EditorDocument.RoomKey()}'.");
            return;
        }

        Plugin.Log.LogInfo($"EditorNet: received a snapshot of {msg.Entries.Count} entries ({payload.Length / 1024} KB).");
        EditorApply.OnSnapshot(msg.Entries);
    }

    /// The Resync button: the host re-sends, a guest asks the host to.
    public static void RequestResync()
    {
        if (!EditorNet.Enabled) return;
        if (EditorNet.IsHost) SendSnapshot("resync requested here");
        else EditorNet.Send(Channel.ResyncRequest, [0], reliable: true);
    }
}
