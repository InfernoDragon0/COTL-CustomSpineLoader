using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace CustomSpineLoader.MapEditor.Net;

/// Channel bytes are CultTweaker's own; the link carries them without looking.
internal enum Channel : byte
{
    Hello = 1,
    Snapshot = 2,
    Ops = 3,
    Live = 4,
    Presence = 5,
    Editor = 6,
    SaveRequest = 7,
    Saved = 8,
    ResyncRequest = 9,
    Level = 10
}

/// One document entry: K is "kind:id", J the canonical record JSON, or null for a delete.
internal sealed class EntryOp
{
    public string K = "";
    public string J;
}

internal sealed class HelloMsg
{
    public string Room = "";
    public bool Editing;
}

internal sealed class OpsMsg
{
    public string Room = "";
    public List<EntryOp> Ops = [];
}

internal sealed class SnapshotMsg
{
    public string Room = "";
    public Dictionary<string, string> Entries = new();
}

internal sealed class LiveItem
{
    public string Id = "";
    public float X, Y, Z;
    public float SX, SY, SZ;
}

internal sealed class LiveMsg
{
    public string Room = "";
    /// Per-sender counter; unreliable messages arrive in any order and an older one must not win.
    public ushort Seq;
    public List<LiveItem> Items = [];
    public List<EntryOp> Shapes;
}

internal sealed class PresenceMsg
{
    public string Room = "";
    public ushort Seq;
    public bool Editing;
    public string Tool = "";
    public bool HasCursor;
    public float CX, CY;
    public List<string> Selected = [];
}

internal sealed class EditorMsg
{
    public string Room = "";
    public bool Open;
    public string Context = "";
    public string MapName = "";
}

internal sealed class SaveMsg
{
    public string Room = "";
    public string MapName = "";
}

/// The host's level playback: which level, and which blueprint each floor slot resolved to.
internal sealed class LevelMsg
{
    public bool Active;
    public string LevelName = "";
    public List<string> Rooms = [];
}

/// <summary>
/// JSON on the wire, UTF-8, with every float rounded to three decimals so a drag that lands where
/// it started is a no-op and both peers produce byte-identical records for identical state.
/// Snapshots additionally go through GZip; the first byte says whether they did.
/// </summary>
internal static class EditorWire
{
    private sealed class RoundedFloat : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(float) || type == typeof(float?);

        public override bool CanRead => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer) => throw new NotSupportedException();

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var rounded = (float)Math.Round((float)value, 3);
            if (rounded == 0f) rounded = 0f;   // no negative zero on the wire
            writer.WriteValue(rounded);
        }
    }

    public static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.None,
        Converters = [new RoundedFloat()]
    };

    public static string Canon(object record) => JsonConvert.SerializeObject(record, Settings);

    public static T Parse<T>(string json) =>
        string.IsNullOrEmpty(json) ? default : JsonConvert.DeserializeObject<T>(json, Settings);

    public static byte[] Encode<T>(T message) =>
        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, Settings));

    public static T Decode<T>(byte[] payload) =>
        payload == null || payload.Length == 0
            ? default
            : JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(payload), Settings);

    public static byte[] EncodeCompressed<T>(T message)
    {
        var raw = Encode(message);
        try
        {
            using var output = new MemoryStream();
            output.WriteByte(1);
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(raw, 0, raw.Length);
            return output.ToArray();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("EditorNet: compression unavailable, sending raw: " + e.Message);
            var plain = new byte[raw.Length + 1];
            plain[0] = 0;
            Buffer.BlockCopy(raw, 0, plain, 1, raw.Length);
            return plain;
        }
    }

    public static T DecodeCompressed<T>(byte[] payload)
    {
        if (payload == null || payload.Length < 2) return default;

        byte[] raw;
        if (payload[0] == 1)
        {
            using var input = new MemoryStream(payload, 1, payload.Length - 1);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            raw = output.ToArray();
        }
        else
        {
            raw = new byte[payload.Length - 1];
            Buffer.BlockCopy(payload, 1, raw, 0, raw.Length);
        }

        return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(raw), Settings);
    }
}
