using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Net;

/// <summary>
/// The one identity a room object has. A tag, never a parent, in the same spirit as CTEditorGroup:
/// the hierarchy is left alone and the id travels in the saved records so a reload keeps it.
/// </summary>
public class CTEditorId : MonoBehaviour
{
    public string Id = "";
}

internal static class EditorIds
{
    private static readonly Dictionary<string, GameObject> _byId = new();
    private static bool _dirty = true;

    /// <summary>
    /// The object's id, minted if it has none. Objects the editor creates get a random id that
    /// travels with the creation record; objects both peers already have (vanilla scenery, the
    /// base's own pieces) get one derived from <paramref name="seed"/> so both machines agree
    /// without talking.
    /// </summary>
    public static string Of(GameObject go, string seed = null)
    {
        if (go == null) return null;

        var tag = go.GetComponent<CTEditorId>();
        if (tag != null && !string.IsNullOrEmpty(tag.Id))
        {
            Register(tag.Id, go);
            return tag.Id;
        }

        tag ??= go.AddComponent<CTEditorId>();

        var id = seed != null ? Deterministic(seed) : NewId();
        var suffix = 2;
        while (Taken(id, go)) id = (seed != null ? Deterministic(seed) : NewId()) + "~" + suffix++;

        tag.Id = id;
        Register(id, go);
        return id;
    }

    /// The id if the object has one, otherwise null. Never mints.
    public static string Peek(GameObject go)
    {
        if (go == null) return null;
        var tag = go.GetComponent<CTEditorId>();
        return tag != null && !string.IsNullOrEmpty(tag.Id) ? tag.Id : null;
    }

    /// Gives a freshly spawned object the id its record carries.
    public static void Adopt(GameObject go, string id)
    {
        if (go == null || string.IsNullOrEmpty(id)) return;

        var tag = go.GetComponent<CTEditorId>() ?? go.AddComponent<CTEditorId>();
        if (tag.Id == id)
        {
            Register(id, go);
            return;
        }

        if (!string.IsNullOrEmpty(tag.Id) && _byId.TryGetValue(tag.Id, out var registered) &&
            ReferenceEquals(registered, go))
            _byId.Remove(tag.Id);

        if (_byId.TryGetValue(id, out var other) && other != null && !ReferenceEquals(other, go))
        {
            var otherTag = other.GetComponent<CTEditorId>();
            if (otherTag != null) otherTag.Id = "";
            // Routine on a blueprint load: a parked vanilla piece hands its id to the copy the file describes.
            if (Plugin.EditorNetVerbose.Value)
                Plugin.Log.LogInfo($"EditorNet: id {id} moved from '{other.name}' to '{go.name}'.");
        }

        tag.Id = id;
        Register(id, go);
    }

    public static GameObject Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_dirty) Rebuild();

        if (_byId.TryGetValue(id, out var go))
        {
            if (go != null && !IsPooled(go)) return go;
            _byId.Remove(id);
        }

        return null;
    }

    /// Objects the pool has taken back sit under the pool's own transform; they are not in the room.
    private static bool IsPooled(GameObject go)
    {
        var pool = ObjectPool.instance;
        return pool != null && go.transform.IsChildOf(pool.transform);
    }

    /// The registry is rebuilt lazily from the scene the next time an id is looked up.
    public static void Reset()
    {
        _byId.Clear();
        _dirty = true;
    }

    private static void Rebuild()
    {
        _dirty = false;
        _byId.Clear();

        foreach (var tag in UnityEngine.Object.FindObjectsOfType<CTEditorId>(true))
        {
            if (tag == null || string.IsNullOrEmpty(tag.Id)) continue;
            if (!tag.gameObject.scene.IsValid() || IsPooled(tag.gameObject)) continue;
            _byId[tag.Id] = tag.gameObject;
        }
    }

    private static void Register(string id, GameObject go)
    {
        if (_dirty) Rebuild();
        _byId[id] = go;
    }

    private static bool Taken(string id, GameObject self)
    {
        if (_dirty) Rebuild();
        return _byId.TryGetValue(id, out var other) && other != null && !ReferenceEquals(other, self);
    }

    private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    /// FNV-1a over the seed, eight hex digits; the same seed gives the same id on both machines.
    public static string Deterministic(string seed)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in seed ?? "")
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    /// A seed for something both peers already have: what it is plus where it stands.
    public static string Seed(string kind, string key, Vector3 position) =>
        string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2:0.00}|{3:0.00}|{4:0.00}",
            kind, key ?? "", position.x, position.y, position.z);

    /// An object the pool took back is gone as far as the room is concerned; its id must not ride
    /// along when the pool hands the same object out somewhere else.
    internal static void Forget(GameObject go)
    {
        if (go == null) return;

        // A recycled room takes its decorations with it; every tag underneath goes too.
        foreach (var tag in go.GetComponentsInChildren<CTEditorId>(true))
        {
            if (tag == null) continue;

            if (!string.IsNullOrEmpty(tag.Id) && _byId.TryGetValue(tag.Id, out var registered) &&
                ReferenceEquals(registered, tag.gameObject))
                _byId.Remove(tag.Id);

            UnityEngine.Object.Destroy(tag);
        }
    }
}

/// The pool recycles scenery instead of destroying it; a recycled object sheds its editor id.
[HarmonyLib.HarmonyPatch(typeof(ObjectPool), "Recycle", typeof(GameObject), typeof(GameObject))]
internal static class ObjectPool_Recycle_EditorId_Patch
{
    private static void Postfix(GameObject obj) => EditorIds.Forget(obj);
}
