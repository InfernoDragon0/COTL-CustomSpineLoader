using System;
using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// The transport a multiplayer mod plugs in. CultTweaker never references the network; it hands
/// opaque bytes to whatever implements this and receives bytes back through EditorNet.Receive.
/// Treat this as a versioned contract: add members on a new interface, never change these.
/// </summary>
public interface IEditorNetLink
{
    bool Connected { get; }
    bool IsHost { get; }
    string PeerName { get; }

    /// Opaque payload. Unreliable sends are for high-rate cosmetic streams and stay small;
    /// reliable payloads may be large, the link fragments them itself.
    void Send(byte channel, byte[] payload, bool reliable);

    /// A system line in the local chat or notice area.
    void Notice(string text);

    /// Host only: ship files under CultTweaker's own folder to the peer, by path relative to that
    /// folder ("CustomNodeBlueprints/MyRoom.json"). The link records them so the peer's next join
    /// does not download them again.
    void ShipFiles(IReadOnlyList<string> relativePaths);
}

/// <summary>
/// The room editor's multiplayer seam. Everything the editor does that a peer must see funnels
/// through here as a document diff (EditorOps), a full snapshot (EditorSnapshot), a live transform
/// stream (EditorLive) or presence (EditorPresence). With no link installed every call is a no-op.
/// </summary>
public static class EditorNet
{
    /// 1: the link, receive, pause and gate members. 2: adds ObjectId.
    public const int ContractVersion = 2;

    public static IEditorNetLink Link;

    /// <summary>
    /// The editor's own id for a room object, or null if it has none. The same object carries the
    /// same id on every machine in a session (ids travel with the records), so a multiplayer mod
    /// that has to match units across machines can key on this instead of on name and position.
    /// </summary>
    public static string ObjectId(GameObject go) => EditorIds.Peek(go);

    public static bool Enabled => Link != null && Link.Connected;

    public static bool LocalEditorOpen { get; private set; }

    public static bool RemoteEditorOpen { get; private set; }

    /// True while either side has the editor open: the world should stand still for both. The local
    /// half does not wait for a peer - a host with the editor open and nobody joined yet is still
    /// editing, and the link's time-scale loop runs as soon as a session exists.
    public static bool PausesTime => LocalEditorOpen || (Enabled && RemoteEditorOpen);

    /// The peer's chat box has the keyboard; editor hotkeys and camera keys stand down.
    public static bool ExternalTyping;

    /// Something is drawn over the bottom-left corner; the shortcut panel hides while it is.
    public static bool ExternalOverlayVisible;

    /// A reason the local player may not change scene or room right now, or null. The link sets it.
    public static Func<string> WhyNotChangeWorld;

    /// Called by the link on the main thread for every payload from the peer.
    public static void Receive(byte channel, byte[] payload)
    {
        if (payload == null) return;

        try
        {
            Dispatch((Channel)channel, payload);
        }
        catch (Exception e)
        {
            if (Time.unscaledTime >= _nextErrorAt)
            {
                _nextErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"EditorNet: message on channel {channel} failed: {e}");
            }
        }
    }

    /// Called by the link when the session ends, for whatever reason.
    public static void OnSessionEnded()
    {
        var hadSession = _wasEnabled;
        RemoteEditorOpen = false;
        _wasEnabled = false;
        EditorOps.Reset();
        EditorApply.Reset();
        EditorLive.Reset();
        EditorPresence.Reset();
        EditorSnapshot.Reset();
        LevelPlayback.ForgetPeer();

        if (hadSession)
            Plugin.Log.LogInfo($"EditorNet: session ended; {EditorOps.ChangesSent} change(s) sent, " +
                               $"{EditorApply.Applied} applied, {EditorSnapshot.Sent} snapshot(s) sent, " +
                               $"{EditorApply.Snapshots} received, {EditorApply.Failures} failure(s).");

        EditorOps.ResetCounters();
        EditorApply.ResetCounters();
        EditorSnapshot.ResetCounters();
    }

    internal static void NoteRemoteEditing(bool editing) => RemoteEditorOpen = editing;

    // ---- internals -----------------------------------------------------------------------------

    internal static bool IsHost => Link == null || Link.IsHost;

    internal static string PeerName => Link != null && !string.IsNullOrEmpty(Link.PeerName) ? Link.PeerName : "the other player";

    /// True while a remote change is being written into the scene: history is suspended and the
    /// outbound diff waits.
    internal static bool ApplyingRemote { get; set; }

    private static bool _wasEnabled;
    private static float _nextErrorAt;

    internal static string WhyNotWorldChange()
    {
        if (!Enabled) return null;
        try
        {
            return WhyNotChangeWorld?.Invoke();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("EditorNet: world-change gate threw: " + e.Message);
            return null;
        }
    }

    internal static void Send(Channel channel, byte[] payload, bool reliable)
    {
        if (!Enabled || payload == null) return;
        try
        {
            Link.Send((byte)channel, payload, reliable);
        }
        catch (Exception e)
        {
            if (Time.unscaledTime >= _nextErrorAt)
            {
                _nextErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogWarning($"EditorNet: send on {channel} failed: {e.Message}");
            }
        }
    }

    internal static void Notice(string text)
    {
        if (!Enabled) return;
        try
        {
            Link.Notice(text);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("EditorNet: notice failed: " + e.Message);
        }
    }

    /// Runs every frame from Plugin.Update, with or without an editor open.
    internal static void Tick()
    {
        var enabled = Enabled;
        if (enabled != _wasEnabled)
        {
            _wasEnabled = enabled;
            if (enabled) OnLinkUp();
            else OnSessionEnded();
        }
        if (!enabled) return;

        var editor = RuntimeMapEditor.Active;

        EditorSnapshot.Tick();
        EditorApply.Tick();
        EditorLive.Tick(editor);
        EditorPresence.Tick(editor);
        EditorOps.Tick(editor);
    }

    private static void OnLinkUp()
    {
        Plugin.Log.LogInfo($"EditorNet: link up as {(IsHost ? "host" : "guest")}; peer is {PeerName}.");
        EditorOps.Reset();
        EditorSnapshot.ScheduleHello(0.5f);
        if (LocalEditorOpen) SendEditorState(true);
        if (LevelPlayback.Active) LevelChanged();
    }

    // ---- level playback ------------------------------------------------------------------------

    /// <summary>
    /// The host's level playback started or stopped. The guest cannot start a level itself (the host
    /// decides where the party goes), but its generator must build the same floor: an authored layout
    /// is built from the level file, not from the seed, and the blueprint each slot plays was picked
    /// at random on the host. So the host ships the level's name and the resolved room list, and the
    /// guest arms them for the floor it is about to follow the host into.
    /// </summary>
    internal static void LevelChanged()
    {
        if (!Enabled || !IsHost) return;
        var msg = LevelPlayback.DescribeForPeer();
        Send(Channel.Level, EditorWire.Encode(msg), reliable: true);
        Plugin.Log.LogInfo(msg.Active
            ? $"EditorNet: told {PeerName} we are playing level '{msg.LevelName}' ({msg.Rooms.Count} rooms)."
            : $"EditorNet: told {PeerName} the level ended.");
    }

    // ---- lifecycle hooks from the editor -------------------------------------------------------

    internal static void LocalEditorChanged(bool open)
    {
        LocalEditorOpen = open;
        if (!Enabled) return;

        SendEditorState(open);

        if (open)
        {
            EditorOps.TakeBaseline(RuntimeMapEditor.Active);
            if (IsHost) EditorSnapshot.SendSnapshot("editor opened");
            else EditorSnapshot.SendHello();
        }
        else
        {
            EditorOps.FlushNow(RuntimeMapEditor.Active);
            EditorLive.Reset();
        }

        EditorPresence.SendNow(RuntimeMapEditor.Active);
    }

    private static void SendEditorState(bool open)
    {
        var editor = RuntimeMapEditor.Active;
        var msg = new EditorMsg
        {
            Room = EditorDocument.RoomKey(),
            Open = open,
            Context = RuntimeMapEditor.Context.ToString(),
            MapName = editor != null ? editor.Map.MapName : ""
        };
        Send(Channel.Editor, EditorWire.Encode(msg), reliable: true);
    }

    /// The scene or the dungeon room changed: nothing from the previous room applies any more.
    internal static void RoomChanged()
    {
        EditorIds.Reset();
        EditorOps.Reset();
        EditorApply.Reset();
        EditorLive.Reset();
        EditorPresence.RoomChanged();
        if (Enabled) EditorSnapshot.ScheduleHello(2f);
    }

    internal static void OnBiomeRoomChanged() => RoomChanged();

    /// A room's CultTweaker content is in place (blueprint loaded, base delta applied).
    internal static void RoomContentReady()
    {
        if (!Enabled) return;
        EditorSnapshot.SendHello();
    }

    // ---- saving --------------------------------------------------------------------------------

    /// A guest cannot write files; it asks the host to save instead.
    internal static bool ShouldRequestSave => Enabled && !IsHost;

    internal static void RequestSave(string mapName)
    {
        if (!Enabled) return;
        Send(Channel.SaveRequest, EditorWire.Encode(new SaveMsg { Room = EditorDocument.RoomKey(), MapName = mapName ?? "" }),
            reliable: true);
    }

    /// The host wrote these files; the peer gets copies and marks its own editor as saved.
    internal static void LocalSaved(string mapName, IReadOnlyList<string> relativePaths)
    {
        if (!Enabled || !IsHost) return;

        try
        {
            if (relativePaths != null && relativePaths.Count > 0) Link.ShipFiles(relativePaths);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("EditorNet: shipping saved files failed: " + e.Message);
        }

        Send(Channel.Saved, EditorWire.Encode(new SaveMsg { Room = EditorDocument.RoomKey(), MapName = mapName ?? "" }),
            reliable: true);
    }

    // ---- dispatch ------------------------------------------------------------------------------

    private static void Dispatch(Channel channel, byte[] payload)
    {
        switch (channel)
        {
            case Channel.Hello:
                EditorSnapshot.OnHello(EditorWire.Decode<HelloMsg>(payload));
                break;

            case Channel.Snapshot:
                EditorSnapshot.OnSnapshot(payload);
                break;

            case Channel.Ops:
                EditorApply.OnOps(EditorWire.Decode<OpsMsg>(payload));
                break;

            case Channel.Live:
                EditorLive.OnLive(EditorWire.Decode<LiveMsg>(payload));
                break;

            case Channel.Presence:
                EditorPresence.OnPresence(EditorWire.Decode<PresenceMsg>(payload));
                break;

            case Channel.Editor:
                OnEditorState(EditorWire.Decode<EditorMsg>(payload));
                break;

            case Channel.SaveRequest:
                OnSaveRequested(EditorWire.Decode<SaveMsg>(payload));
                break;

            case Channel.Saved:
                OnPeerSaved(EditorWire.Decode<SaveMsg>(payload));
                break;

            case Channel.ResyncRequest:
                if (IsHost) EditorSnapshot.SendSnapshot("resync requested by " + PeerName);
                break;

            case Channel.Level:
                if (!IsHost) LevelPlayback.OnPeerLevel(EditorWire.Decode<LevelMsg>(payload));
                break;

            default:
                Plugin.Log.LogWarning($"EditorNet: unknown channel {(byte)channel}.");
                break;
        }
    }

    private static void OnEditorState(EditorMsg msg)
    {
        if (msg == null) return;

        var was = RemoteEditorOpen;
        RemoteEditorOpen = msg.Open;

        if (msg.Open && !was)
        {
            var where = string.IsNullOrEmpty(msg.MapName) ? "the map editor" : $"the map editor on '{msg.MapName}'";
            Notice(LocalEditorOpen
                ? $"{PeerName} opened {where} too."
                : $"{PeerName} opened {where}; press F4 to edit together.");
            RuntimeMapEditor.Active?.SetStatus($"{PeerName} is editing this room with you.");
        }
        else if (!msg.Open && was)
        {
            Notice($"{PeerName} closed the map editor.");
            EditorPresence.PeerStoppedEditing();
        }

        if (msg.Open && IsHost && !EditorDocument.SameRoom(msg.Room))
            Plugin.Log.LogInfo($"EditorNet: {PeerName} opened the editor in '{msg.Room}', we are in " +
                               $"'{EditorDocument.RoomKey()}'; nothing is shared until the rooms match.");
    }

    private static void OnSaveRequested(SaveMsg msg)
    {
        if (msg == null || !IsHost) return;

        var editor = RuntimeMapEditor.Active;
        if (editor == null)
        {
            Notice($"{PeerName} asked to save, but there is no editor here to save from.");
            return;
        }

        if (!EditorDocument.SameRoom(msg.Room))
        {
            Notice($"{PeerName} asked to save a different room; ignored.");
            return;
        }

        editor.SaveForPeer(msg.MapName);
    }

    private static void OnPeerSaved(SaveMsg msg)
    {
        if (msg == null || IsHost) return;

        var editor = RuntimeMapEditor.Active;
        if (editor == null) return;

        editor.PeerSaved(msg.MapName);
        Notice(string.IsNullOrEmpty(msg.MapName)
            ? $"{PeerName} saved the room."
            : $"{PeerName} saved '{msg.MapName}'.");
    }
}
