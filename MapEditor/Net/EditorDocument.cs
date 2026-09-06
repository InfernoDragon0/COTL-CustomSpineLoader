using System.Collections.Generic;
using System.Globalization;
using MMBiomeGeneration;
using UnityEngine.SceneManagement;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// The room as a flat set of entries, "kind:id" to canonical JSON. It is the blueprint every tool
/// already contributes to for a save (plus the base's journals in the base), taken apart so two
/// copies can be diffed entry by entry. Nothing here knows about tools beyond calling the same
/// two collection steps the save path calls.
/// </summary>
internal static class EditorDocument
{
    public const string Shape = "shape";
    public const string Prop = "prop";
    public const string Kept = "kept";
    public const string Structure = "structure";
    public const string Door = "door";
    public const string Enemy = "enemy";
    public const string Npc = "npc";
    public const string Podium = "podium";
    public const string Trigger = "trigger";
    public const string Lighting = "lighting";
    public const string Weather = "weather";
    public const string Music = "music";
    public const string Floor = "floor";
    public const string Totem = "totem";
    public const string Groups = "groups";
    public const string Stroke = "stroke";
    public const string VanillaRemoved = "vremoved";
    public const string VanillaMoved = "vmoved";
    public const string VanillaShape = "vshape";
    public const string StructureMoved = "smoved";

    public const string One = "*";   // the id of a singleton entry

    public static string Key(string kind, string id) => kind + ":" + id;

    public static bool Split(string key, out string kind, out string id)
    {
        kind = null;
        id = null;
        if (string.IsNullOrEmpty(key)) return false;

        var colon = key.IndexOf(':');
        if (colon <= 0 || colon >= key.Length - 1) return false;

        kind = key.Substring(0, colon);
        id = key.Substring(colon + 1);
        return true;
    }

    public static string KindOf(string key) => Split(key, out var kind, out _) ? kind : "";

    /// A music entry is two blueprint fields; they travel together.
    internal sealed class MusicRecord
    {
        public string Event = "";
        public bool Loop;
    }

    internal sealed class FloorRecord
    {
        public bool UseVanillaFloorCollision = true;
    }

    internal sealed class GroupsRecord
    {
        public List<MapGroupData> Groups = [];
    }

    // ---- the room -------------------------------------------------------------------------------

    /// <summary>
    /// Which room this document describes. Both peers stand in the same seeded room, so the scene
    /// name plus the generator's grid position is the same on both machines; the base adds its own
    /// marker because its document has a different shape.
    /// </summary>
    public static string RoomKey()
    {
        var scene = SceneManager.GetActiveScene().name;
        var generator = BiomeGenerator.Instance;
        var room = generator != null
            ? string.Format(CultureInfo.InvariantCulture, "{0},{1}", generator.CurrentX, generator.CurrentY)
            : SceneRefs.Room != null ? RoomSnapshot.StripClone(SceneRefs.Room.name) : "";

        var context = EffectiveContext switch
        {
            EditorContext.Base => "base",
            EditorContext.Hub => "hub:" + (HubSession.HubName ?? ""),
            _ => "room"
        };

        return scene + "|" + room + "|" + context;
    }

    public static bool SameRoom(string key) => !string.IsNullOrEmpty(key) && key == RoomKey();

    /// <summary>
    /// What kind of document this scene holds. The editor's own Context follows the open session,
    /// but a player who has not opened the base editor is still standing in the base: their copy
    /// of the room is the base's delta, not a dungeon room, and the peer's base messages are theirs.
    /// </summary>
    public static EditorContext EffectiveContext
    {
        get
        {
            if (RuntimeMapEditor.ContextOverride.HasValue) return RuntimeMapEditor.ContextOverride.Value;
            if (BaseSession.Active) return EditorContext.Base;
            if (HubSession.Active) return EditorContext.Hub;
            if (BaseSession.SceneOwnsSessions && !HubSession.Busy) return EditorContext.Base;
            return EditorContext.Dungeon;
        }
    }

    public static bool IsBase => EffectiveContext == EditorContext.Base;

    // ---- collection -----------------------------------------------------------------------------

    private static float _lastCollectMs;

    public static float LastCollectMilliseconds => _lastCollectMs;

    /// <summary>
    /// The current state of the room as entries. Runs the same two steps a save runs (every tool's
    /// ContributeTo, then the scenery sweep) into the editor's own blueprint, then flattens it.
    /// </summary>
    public static Dictionary<string, string> Collect(RuntimeMapEditor editor)
    {
        var entries = new Dictionary<string, string>();
        if (editor == null) return entries;

        var clock = System.Diagnostics.Stopwatch.StartNew();

        var map = editor.Map;
        editor.ContributeAll();

        var isBase = IsBase;
        if (!isBase) RoomSnapshot.Collect(map, editor, quiet: true);

        foreach (var s in map.Shapes) Add(entries, Shape, s?.Id, s);
        if (!isBase)
        {
            foreach (var p in map.Props) Add(entries, Prop, p?.Id, p);
            foreach (var k in map.KeptAuthored) Add(entries, Kept, k?.Id, k);
            foreach (var d in map.Doors) Add(entries, Door, d?.Direction, d);
            foreach (var e in map.Enemies) Add(entries, Enemy, e?.Id, e);
            foreach (var p in map.Podiums) Add(entries, Podium, p?.Id, p);
            Add(entries, Floor, One, new FloorRecord { UseVanillaFloorCollision = map.UseVanillaFloorCollision });
            if (map.BuildTotem != null) Add(entries, Totem, One, map.BuildTotem);
        }
        foreach (var s in map.Structures) Add(entries, Structure, s?.Id, s);
        foreach (var n in map.Npcs) Add(entries, Npc, n?.Id, n);
        foreach (var t in map.Triggers) Add(entries, Trigger, t?.Id, t);
        if (map.Whiteboard != null) foreach (var s in map.Whiteboard) Add(entries, Stroke, s?.Id, s);

        Add(entries, Lighting, One, map.Lighting ?? new MapLightingData());
        Add(entries, Weather, One, map.Weather ?? new MapWeatherData());
        Add(entries, Music, One, new MusicRecord { Event = map.MusicEvent ?? "", Loop = map.MusicLoop });
        Add(entries, Groups, One, new GroupsRecord { Groups = map.Groups ?? [] });

        if (isBase)
        {
            var file = BaseDelta.Snapshot();
            foreach (var r in file.Removed) Add(entries, VanillaRemoved, BaseDelta.RefId(r), r);
            foreach (var m in file.Moved) Add(entries, VanillaMoved, BaseDelta.RefId(m), m);
            foreach (var s in file.EditedShapes) Add(entries, VanillaShape, BaseDelta.RefId(s), s);
            foreach (var m in file.MovedStructures)
                Add(entries, StructureMoved, m.StructureId.ToString(CultureInfo.InvariantCulture), m);
        }

        clock.Stop();
        _lastCollectMs = (float)clock.Elapsed.TotalMilliseconds;
        if (_lastCollectMs > 8f && UnityEngine.Time.unscaledTime >= _nextSlowReportAt)
        {
            _nextSlowReportAt = UnityEngine.Time.unscaledTime + 10f;
            Plugin.Log.LogWarning($"EditorNet: collecting the room took {_lastCollectMs:0.0} ms " +
                                  $"({entries.Count} entries).");
        }

        return entries;
    }

    private static float _nextSlowReportAt;

    private static void Add(Dictionary<string, string> entries, string kind, string id, object record)
    {
        if (record == null || string.IsNullOrEmpty(id)) return;
        entries[Key(kind, id)] = EditorWire.Canon(record);
    }

    /// Apply order: what stands on the floor comes after the floor, and room-wide settings last.
    public static int Order(string kind) => kind switch
    {
        VanillaRemoved => 0,
        VanillaMoved => 1,
        VanillaShape => 2,
        Shape => 3,
        Floor => 4,
        Prop => 5,
        Kept => 6,
        Structure => 7,
        StructureMoved => 8,
        Door => 9,
        Enemy => 10,
        Npc => 11,
        Podium => 12,
        Trigger => 13,
        Totem => 14,
        Groups => 15,
        Lighting => 16,
        Weather => 17,
        Music => 18,
        Stroke => 19,
        _ => 50
    };
}
