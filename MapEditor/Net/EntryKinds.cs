using System;
using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor.Tools;
using CustomSpineLoader.SpineLoaderHelper;
using MMRoomGeneration;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// How one kind of document entry is written into the scene and taken out again. Each kind is a
/// thin wrapper over the spawn and remove code the tools and the blueprint loader already have.
/// A new tool registers one of these and is synchronised; nothing else needs to know about it.
/// </summary>
internal abstract class EntryKind
{
    public abstract string Kind { get; }

    /// The scene object an id names, or null for singletons.
    public virtual GameObject Find(RuntimeMapEditor editor, string id) => EditorIds.Find(id);

    /// Fields that are applied to an existing object in place; any other change is a respawn.
    protected virtual string[] InPlaceFields => Array.Empty<string>();

    /// Every change to this kind is applied in place (triggers, shapes, doors, singletons).
    protected virtual bool AlwaysInPlace => false;

    public virtual bool NeedsNavigationRescan => false;
    public virtual bool NeedsCollisionRebuild => false;

    public abstract IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing);

    public abstract void Delete(RuntimeMapEditor editor, string id, GameObject existing);

    /// <summary>
    /// For a reconcile: does the peer's record describe the same thing as this local record, ids
    /// aside? The default asks for identical content; positional kinds relax that to "same thing
    /// standing in the same place".
    /// </summary>
    public virtual bool Matches(JObject peer, JObject local) => JToken.DeepEquals(Strip(peer), Strip(local));

    protected bool CanApplyInPlace(string previousJson, string json)
    {
        if (AlwaysInPlace) return true;
        if (previousJson == null) return false;

        try
        {
            var before = JObject.Parse(previousJson);
            var after = JObject.Parse(json);
            foreach (var field in InPlaceFields)
            {
                before.Remove(field);
                after.Remove(field);
            }
            return JToken.DeepEquals(Strip(before), Strip(after));
        }
        catch (Exception)
        {
            return false;
        }
    }

    protected static JObject Strip(JObject record)
    {
        var copy = (JObject)record.DeepClone();
        copy.Remove("Id");
        copy.Remove("ParentIslandId");
        copy.Remove("ParentIslandIndex");
        return copy;
    }

    protected static bool Near(JObject a, JObject b, float tolerance = 0.05f)
    {
        var pa = a["Position"] as JObject;
        var pb = b["Position"] as JObject;
        if (pa == null || pb == null) return false;

        var va = new Vector3(pa.Value<float>("X"), pa.Value<float>("Y"), pa.Value<float>("Z"));
        var vb = new Vector3(pb.Value<float>("X"), pb.Value<float>("Y"), pb.Value<float>("Z"));
        return Vector3.Distance(va, vb) <= tolerance;
    }

    protected static bool SameString(JObject a, JObject b, string field) =>
        string.Equals(a.Value<string>(field) ?? "", b.Value<string>(field) ?? "", StringComparison.Ordinal);

    protected static Vector3 V(SerializableVector3 v) => MapEditorSerialization.ToVector3(v);

    protected static void ApplyScale(GameObject go, SerializableVector3 scale)
    {
        if (go == null || scale == null || scale.X == 0f) return;
        go.transform.localScale = V(scale);
    }
}

internal static class EntryKinds
{
    private static readonly Dictionary<string, EntryKind> _kinds = new();

    static EntryKinds()
    {
        Register(new PropKind());
        Register(new KeptKind());
        Register(new StructureKind());
        Register(new EnemyKind());
        Register(new NpcKind());
        Register(new PodiumKind());
        Register(new TriggerKind());
        Register(new ShapeKind());
        Register(new DoorKind());
        Register(new LightingKind());
        Register(new WeatherKind());
        Register(new MusicKind());
        Register(new FloorKind());
        Register(new TotemKind());
        Register(new GroupsKind());
        Register(new StrokeKind());
        Register(new VanillaRemovedKind());
        Register(new VanillaMovedKind());
        Register(new VanillaShapeKind());
        Register(new StructureMovedKind());
    }

    public static void Register(EntryKind kind)
    {
        if (kind == null || string.IsNullOrEmpty(kind.Kind)) return;
        _kinds[kind.Kind] = kind;
    }

    public static EntryKind Get(string kind) =>
        !string.IsNullOrEmpty(kind) && _kinds.TryGetValue(kind, out var found) ? found : null;
}

// ---- scenery ------------------------------------------------------------------------------------

internal sealed class PropKind : EntryKind
{
    public override string Kind => EditorDocument.Prop;

    protected override string[] InPlaceFields => ["Position", "RotationZ", "RotationY", "Scale"];

    public override bool NeedsNavigationRescan => true;

    public override bool Matches(JObject peer, JObject local) =>
        SameString(peer, local, "Key") && SameString(peer, local, "Parent") && Near(peer, local);

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapPropData>(json);
        var room = SceneRefs.Room;
        if (record == null || room == null) yield break;

        if (existing != null)
        {
            if (CanApplyInPlace(previousJson, json))
            {
                BlueprintLoader.ApplyPropTransform(existing, record, room);
                yield break;
            }

            Object.Destroy(existing);
        }

        var parent = !string.IsNullOrEmpty(record.ParentIslandId)
            ? EditorIds.Find(record.ParentIslandId)?.transform
            : null;
        parent ??= BlueprintLoader.ParentFor(record.Parent, room);
        if (parent == null)
        {
            Plugin.Log.LogWarning($"EditorNet: no '{record.Parent}' parent for prop '{record.Key}', skipped.");
            yield break;
        }

        if (record.IsIslandRef)
        {
            GameObject prefab = null;
            yield return RoomSnapshot.ResolveIslandRoutine(room, record.Key, go => prefab = go);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"EditorNet: island '{record.Key}' could not be resolved, skipped.");
                yield break;
            }

            var island = Object.Instantiate(prefab, parent);
            BlueprintLoader.ApplyPropTransform(island, record, room);
            island.GetComponent<IslandPiece>()?.HideSprites();
            EditorIds.Adopt(island, id);
            yield break;
        }

        var done = false;
        try
        {
            ObjectPool.Spawn(record.Key, V(record.Position), Quaternion.Euler(0f, 0f, record.RotationZ), parent,
                go =>
                {
                    done = true;
                    if (go == null)
                    {
                        Plugin.Log.LogWarning($"EditorNet: prop '{record.Key}' spawned nothing.");
                        return;
                    }
                    BlueprintLoader.ApplyPropTransform(go, record, room);
                    EditorIds.Adopt(go, id);
                }, record.IsAddressable);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"EditorNet: prop '{record.Key}' failed to spawn: {e.Message}");
            yield break;
        }

        var deadline = Time.unscaledTime + 10f;
        while (!done && Time.unscaledTime < deadline) yield return null;
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        if (existing == null) return;
        BaseDelta.NoteRemoved(existing);
        Object.Destroy(existing);
    }
}

internal sealed class KeptKind : EntryKind
{
    public override string Kind => EditorDocument.Kept;

    protected override bool AlwaysInPlace => true;

    public override bool Matches(JObject peer, JObject local) =>
        SameString(peer, local, "Name") && SameString(peer, local, "Parent") && Near(peer, local);

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapKeptData>(json);
        if (record == null) yield break;

        if (existing == null)
        {
            Plugin.Log.LogInfo($"EditorNet: authored object '{record.Name}' is not in this room; " +
                               "it cannot be created here.");
            yield break;
        }

        existing.transform.position = V(record.Position);
        existing.transform.eulerAngles = new Vector3(0f, record.RotationY, record.RotationZ);
        ApplyScale(existing, record.Scale);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        if (existing == null) return;
        BaseDelta.NoteRemoved(existing);
        Object.Destroy(existing);
    }
}

// ---- tool-placed objects ------------------------------------------------------------------------

internal sealed class StructureKind : EntryKind
{
    public override string Kind => EditorDocument.Structure;

    protected override string[] InPlaceFields =>
        ["Position", "Rotation", "FlipX", "Scale", "SeeThrough", "FogThrough", "Wind"];

    public override bool NeedsNavigationRescan => true;

    public override bool Matches(JObject peer, JObject local) =>
        SameString(peer, local, "TypeName") && Near(peer, local);

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapStructureData>(json);
        var tool = editor.GetTool<StructureTool>();
        if (record == null || tool == null) yield break;

        if (existing != null)
        {
            if (CanApplyInPlace(previousJson, json))
            {
                var from = existing.transform.position;
                var fromScale = existing.transform.localScale;

                existing.transform.position = V(record.Position);
                existing.transform.eulerAngles = new Vector3(0f, record.Rotation, 0f);
                if (record.Scale != null && record.Scale.X != 0f)
                {
                    var s = V(record.Scale);
                    existing.transform.localScale = new Vector3(record.FlipX ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
                }
                tool.UpdatePlaced(existing, record.Rotation, record.FlipX);

                if (tool.IsSeeThrough(existing) != record.SeeThrough || tool.IsFogThrough(existing) != record.FogThrough ||
                    tool.IsWind(existing) != record.Wind)
                    tool.TrySetSeeThrough(existing, record.SeeThrough, record.FogThrough, record.Wind);

                BaseDelta.NoteMoved(existing, from, fromScale);
                yield break;
            }

            if (!tool.RemoveTracked(existing)) Object.Destroy(existing);
        }

        if (!StructureTool.TryResolveType(record.TypeName, record.IsCustom, out var type))
        {
            Plugin.Log.LogWarning($"EditorNet: structure '{record.TypeName}' is not known here, skipped.");
            yield break;
        }

        var before = tool.LastPlacedInstance;
        yield return tool.PlaceAt(type, record.IsCustom, V(record.Position), record.Rotation, record.FlipX,
            deferNav: true, seeThrough: record.SeeThrough, fogThrough: record.FogThrough, wind: record.Wind);

        var instance = tool.LastPlacedInstance;
        if (instance == null || ReferenceEquals(instance, before)) yield break;

        ApplyScale(instance, record.Scale);
        if (record.FlipX && instance.transform.localScale.x > 0f)
        {
            var s = instance.transform.localScale;
            instance.transform.localScale = new Vector3(-s.x, s.y, s.z);
        }
        EditorIds.Adopt(instance, id);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        if (existing == null) return;
        var tool = editor.GetTool<StructureTool>();
        if (tool == null || !tool.RemoveTracked(existing))
        {
            BaseDelta.NoteRemoved(existing);
            Object.Destroy(existing);
        }
    }
}

internal sealed class EnemyKind : EntryKind
{
    public override string Kind => EditorDocument.Enemy;

    protected override string[] InPlaceFields => ["Position", "Scale"];

    public override bool Matches(JObject peer, JObject local) => SameString(peer, local, "Key") && Near(peer, local);

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapEnemyData>(json);
        var tool = editor.GetTool<EnemyTool>();
        if (record == null || tool == null) yield break;

        if (existing != null)
        {
            if (CanApplyInPlace(previousJson, json))
            {
                existing.transform.position = V(record.Position);
                ApplyScale(existing, record.Scale);
                yield break;
            }

            if (!tool.RemoveTracked(existing)) Object.Destroy(existing);
        }

        var before = tool.LastPlacedInstance;
        yield return tool.SpawnEnemyRoutine(record.Key, record.IsCustom, V(record.Position), withVfx: false);

        var instance = tool.LastPlacedInstance;
        if (instance == null || ReferenceEquals(instance, before)) yield break;

        ApplyScale(instance, record.Scale);
        EditorIds.Adopt(instance, id);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        if (existing == null) return;
        var tool = editor.GetTool<EnemyTool>();
        if (tool == null || !tool.RemoveTracked(existing)) Object.Destroy(existing);
    }
}

internal sealed class NpcKind : EntryKind
{
    public override string Kind => EditorDocument.Npc;

    protected override string[] InPlaceFields => ["Position", "Scale"];

    public override bool Matches(JObject peer, JObject local) => SameString(peer, local, "Key") && Near(peer, local);

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapNpcData>(json);
        var tool = editor.GetTool<NpcTool>();
        if (record == null || tool == null) yield break;

        if (existing != null)
        {
            if (CanApplyInPlace(previousJson, json))
            {
                existing.transform.position = V(record.Position);
                ApplyScale(existing, record.Scale);
                yield break;
            }

            if (!tool.RemoveTracked(existing)) Object.Destroy(existing);
        }

        var before = tool.LastPlacedInstance;
        yield return tool.SpawnNpcRoutine(record.Key, V(record.Position), record.IsCustom);

        var instance = tool.LastPlacedInstance;
        if (instance == null || ReferenceEquals(instance, before)) yield break;

        ApplyScale(instance, record.Scale);
        EditorIds.Adopt(instance, id);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        if (existing == null) return;
        var tool = editor.GetTool<NpcTool>();
        if (tool == null || !tool.RemoveTracked(existing)) Object.Destroy(existing);
    }
}

internal sealed class PodiumKind : EntryKind
{
    public override string Kind => EditorDocument.Podium;

    protected override string[] InPlaceFields => ["Position", "Scale"];

    public override bool Matches(JObject peer, JObject local) => SameString(peer, local, "Type") && Near(peer, local);

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapPodiumData>(json);
        var tool = editor.GetTool<PodiumTool>();
        if (record == null || tool == null) yield break;

        if (existing != null)
        {
            if (CanApplyInPlace(previousJson, json) || !tool.IsTracked(existing))
            {
                existing.transform.position = V(record.Position);
                ApplyScale(existing, record.Scale);

                var marker = existing.GetComponentInChildren<CTPodiumBehavior>(true);
                if (marker != null) marker.ClearAllOnEquip = record.ClearAllOnEquip;
                yield break;
            }

            if (!tool.RemoveTracked(existing)) Object.Destroy(existing);
        }

        var go = tool.SpawnPodium(V(record.Position), record.Type, record.ClearAllOnEquip);
        if (go == null) yield break;

        ApplyScale(go, record.Scale);
        EditorIds.Adopt(go, id);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        if (existing == null) return;
        var tool = editor.GetTool<PodiumTool>();
        if (tool == null || !tool.RemoveTracked(existing)) Object.Destroy(existing);
    }
}

internal sealed class TriggerKind : EntryKind
{
    public override string Kind => EditorDocument.Trigger;

    protected override bool AlwaysInPlace => true;

    public override bool NeedsNavigationRescan => true;

    public override GameObject Find(RuntimeMapEditor editor, string id)
    {
        var trigger = editor.GetTool<TriggerTool>()?.FindTrigger(id);
        return trigger != null ? trigger.gameObject : EditorIds.Find(id);
    }

    public override bool Matches(JObject peer, JObject local) => SameString(peer, local, "Id");

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapTriggerData>(json);
        var tool = editor.GetTool<TriggerTool>();
        if (record == null || tool == null) yield break;

        var trigger = existing != null ? existing.GetComponentInChildren<CTMapTrigger>(true) : null;
        if (trigger != null)
        {
            tool.ApplyRecord(trigger, record);
            yield break;
        }

        var created = tool.CreateTrigger(V(record.Position), record.Width, record.Height, record.Id,
            record.Action, record.Once, record.Actions, record.LockPlayerControl, record.Blocking);
        if (created != null) EditorIds.Adopt(created.gameObject, id);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        var tool = editor.GetTool<TriggerTool>();
        var trigger = existing != null ? existing.GetComponentInChildren<CTMapTrigger>(true) : tool?.FindTrigger(id);
        if (trigger == null) return;
        if (tool == null || !tool.RemoveTrigger(trigger)) Object.Destroy(trigger.gameObject);
    }
}

internal sealed class ShapeKind : EntryKind
{
    public override string Kind => EditorDocument.Shape;

    protected override bool AlwaysInPlace => true;

    public override bool NeedsCollisionRebuild => true;

    public override bool Matches(JObject peer, JObject local)
    {
        var pa = peer["Points"] as JArray;
        var pb = local["Points"] as JArray;
        return pa != null && pb != null && pa.Count == pb.Count && Near(peer, local);
    }

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapShapeData>(json);
        var tool = editor.GetTool<ShapeTool>();
        if (record == null || tool == null) yield break;

        var ctrl = existing != null ? existing.GetComponent<SpriteShapeController>() : null;
        if (ctrl != null)
        {
            tool.ApplyRemoteShape(ctrl, record, bake: true);
            yield break;
        }

        tool.PrepareForLoad();
        var built = tool.RebuildShape(record);
        if (built == null) yield break;

        yield return null;
        tool.FinalizeLoadedShape(built);
        EditorIds.Adopt(built.gameObject, id);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        if (existing == null) return;
        var ctrl = existing.GetComponent<SpriteShapeController>();
        var tool = editor.GetTool<ShapeTool>();
        if (ctrl == null || tool == null || !tool.DeleteShape(ctrl)) Object.Destroy(existing);
    }
}

internal sealed class DoorKind : EntryKind
{
    public override string Kind => EditorDocument.Door;

    protected override bool AlwaysInPlace => true;

    public override bool NeedsCollisionRebuild => true;

    public override GameObject Find(RuntimeMapEditor editor, string id)
    {
        var door = editor.GetTool<DoorTool>()?.FindByDirection(id);
        return door != null ? door.gameObject : null;
    }

    public override bool Matches(JObject peer, JObject local) => SameString(peer, local, "Direction");

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapDoorData>(json);
        var tool = editor.GetTool<DoorTool>();
        if (record == null || tool == null) yield break;

        var door = tool.EnsureDoor(record.Direction, deferCollision: true);
        if (door == null) yield break;

        var target = V(record.Position);
        var island = door.GetComponentInParent<IslandPiece>(true);
        if (island != null && island.transform != door.transform)
            island.transform.position += target - door.transform.position;
        else
            door.transform.position = target;

        door.transform.eulerAngles = new Vector3(0f, 0f, record.RotationZ);

        tool.RefreshPad(door, deferCollision: true);
        DoorTool.RefreshMovementAnchors(door);
        editor.KeepCullingSuspended = true;
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        var tool = editor.GetTool<DoorTool>();
        var door = tool?.FindByDirection(id);
        if (door == null) return;
        tool.RemoveDoor(door, deferCollision: true);
    }
}

// ---- room-wide settings -------------------------------------------------------------------------

// ---- whiteboard -------------------------------------------------------------------------------

internal sealed class StrokeKind : EntryKind
{
    public override string Kind => EditorDocument.Stroke;

    protected override bool AlwaysInPlace => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) =>
        editor.GetTool<WhiteboardTool>()?.FindStroke(id)?.gameObject ?? EditorIds.Find(id);

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapStrokeData>(json);
        if (record == null) yield break;
        record.Id = id;
        editor.GetTool<WhiteboardTool>()?.ApplyRecord(record);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        var tool = editor.GetTool<WhiteboardTool>();
        if (tool == null || !tool.RemoveStroke(id)) { if (existing != null) Object.Destroy(existing); }
    }
}

internal sealed class LightingKind : EntryKind
{
    public override string Kind => EditorDocument.Lighting;

    private const float RemoteFadeSeconds = 0.25f;

    protected override bool AlwaysInPlace => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapLightingData>(json);
        if (record == null) yield break;

        editor.Map.Lighting = record;
        // A short fade: the peer's slider arrives one settled record at a time, not per frame, and the
        // fade hides the steps. The local slider keeps its zero fade so it stays responsive.
        if (record.Enabled) LightingTool.Apply(record, RemoteFadeSeconds);
        else
        {
            LightingTool.ForgetCurrentRoom();
            LightingTool.ClearOverride();
        }
        editor.GetTool<LightingTool>()?.RefreshFromMap();
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        editor.Map.Lighting = new MapLightingData();
        LightingTool.ForgetCurrentRoom();
        LightingTool.ClearOverride();
        editor.GetTool<LightingTool>()?.RefreshFromMap();
    }
}

internal sealed class WeatherKind : EntryKind
{
    public override string Kind => EditorDocument.Weather;

    protected override bool AlwaysInPlace => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapWeatherData>(json);
        if (record == null) yield break;

        editor.Map.Weather = record;
        WeatherControl.ForRoom(record);
        editor.GetTool<LightingTool>()?.RefreshFromMap();
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        editor.Map.Weather = new MapWeatherData();
        WeatherControl.ForRoom(editor.Map.Weather);
        editor.GetTool<LightingTool>()?.RefreshFromMap();
    }
}

internal sealed class MusicKind : EntryKind
{
    public override string Kind => EditorDocument.Music;

    protected override bool AlwaysInPlace => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<EditorDocument.MusicRecord>(json);
        if (record == null) yield break;

        editor.Map.MusicEvent = record.Event ?? "";
        editor.Map.MusicLoop = record.Loop;

        if (!string.IsNullOrEmpty(record.Event))
        {
            try
            {
                AudioManager.Instance?.PlayMusic(record.Event);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"EditorNet: music '{record.Event}' failed to play: {e.Message}");
            }
        }

        editor.SetMusicLoop(record.Loop && !string.IsNullOrEmpty(record.Event) ? record.Event : null);
        editor.GetTool<MusicTool>()?.RefreshFromMap();
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        editor.Map.MusicEvent = "";
        editor.Map.MusicLoop = false;
        editor.SetMusicLoop(null);
        editor.GetTool<MusicTool>()?.RefreshFromMap();
    }
}

internal sealed class FloorKind : EntryKind
{
    public override string Kind => EditorDocument.Floor;

    protected override bool AlwaysInPlace => true;

    public override bool NeedsCollisionRebuild => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<EditorDocument.FloorRecord>(json);
        if (record == null) yield break;

        editor.Map.UseVanillaFloorCollision = record.UseVanillaFloorCollision;
        editor.GetTool<ShapeTool>()?.ApplyVanillaFloorFlag(record.UseVanillaFloorCollision);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        editor.Map.UseVanillaFloorCollision = true;
        editor.GetTool<ShapeTool>()?.ApplyVanillaFloorFlag(true);
    }
}

internal sealed class TotemKind : EntryKind
{
    public override string Kind => EditorDocument.Totem;

    protected override bool AlwaysInPlace => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => editor.GetTool<StructureTool>()?.PlacedTotem;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<MapTotemData>(json);
        var tool = editor.GetTool<StructureTool>();
        if (record == null || tool == null) yield break;

        if (existing != null)
        {
            existing.transform.position = V(record.Position);
            yield break;
        }

        var totem = HubBuildTotem.Spawn(V(record.Position), SceneRefs.ContentRoot);
        if (totem != null) tool.AdoptTotem(totem);
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        editor.GetTool<StructureTool>()?.RemoveTotem();
    }
}

internal sealed class GroupsKind : EntryKind
{
    public override string Kind => EditorDocument.Groups;

    protected override bool AlwaysInPlace => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<EditorDocument.GroupsRecord>(json);
        if (record == null) yield break;

        var wanted = new Dictionary<string, MapGroupData>();
        foreach (var group in record.Groups ?? [])
            if (group != null && !string.IsNullOrEmpty(group.Id)) wanted[group.Id] = group;

        foreach (var (liveId, _, members) in MapEditorGroups.All())
        {
            if (wanted.TryGetValue(liveId, out var group) && group.Members != null &&
                group.Members.Count == members.Count) continue;
            MapEditorGroups.Dissolve(liveId);
        }

        var restore = new CTNodeBlueprint { Groups = record.Groups ?? [] };
        MapEditorGroups.Restore(restore);
        editor.Map.Groups = record.Groups ?? [];
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing)
    {
        foreach (var (liveId, _, _) in MapEditorGroups.All()) MapEditorGroups.Dissolve(liveId);
        editor.Map.Groups = [];
    }
}

// ---- the base's own things ------------------------------------------------------------------------

internal sealed class VanillaRemovedKind : EntryKind
{
    public override string Kind => EditorDocument.VanillaRemoved;

    protected override bool AlwaysInPlace => true;

    public override bool NeedsNavigationRescan => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<BaseVanillaRef>(json);
        if (record != null) BaseDelta.ApplyRemovedEntry(record);
        yield break;
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing) =>
        BaseDelta.UnapplyRemovedEntry(id);
}

internal sealed class VanillaMovedKind : EntryKind
{
    public override string Kind => EditorDocument.VanillaMoved;

    protected override bool AlwaysInPlace => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<BaseMovedVanilla>(json);
        if (record != null) BaseDelta.ApplyMovedEntry(record);
        yield break;
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing) =>
        BaseDelta.UnapplyMovedEntry(id);
}

internal sealed class VanillaShapeKind : EntryKind
{
    public override string Kind => EditorDocument.VanillaShape;

    protected override bool AlwaysInPlace => true;

    public override bool NeedsCollisionRebuild => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<BaseEditedShape>(json);
        var tool = editor.GetTool<ShapeTool>();
        if (record != null && tool != null) BaseDelta.ApplyEditedShapeEntry(record, tool);
        yield break;
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing) =>
        BaseDelta.UnapplyEditedShapeEntry(id);
}

internal sealed class StructureMovedKind : EntryKind
{
    public override string Kind => EditorDocument.StructureMoved;

    protected override bool AlwaysInPlace => true;

    public override GameObject Find(RuntimeMapEditor editor, string id) => null;

    public override IEnumerator Upsert(RuntimeMapEditor editor, string id, string json, string previousJson,
        GameObject existing)
    {
        var record = EditorWire.Parse<BaseMovedStructure>(json);
        if (record != null) BaseDelta.ApplyStructureMoveEntry(record);
        yield break;
    }

    public override void Delete(RuntimeMapEditor editor, string id, GameObject existing) =>
        BaseDelta.UnapplyStructureMoveEntry(id);
}
