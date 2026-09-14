using System;
using System.Collections;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class RoomChildPrefabs
{
    public const string Prefix = "room:";

    public static bool IsRoomKey(string key) => key != null && key.StartsWith(Prefix);

    public static string KeyFor(string roomKey, string childPath) => Prefix + roomKey + "|" + childPath;

    public static bool TryParse(string key, out string roomKey, out string childPath)
    {
        roomKey = null;
        childPath = null;
        if (!IsRoomKey(key)) return false;

        var body = key.Substring(Prefix.Length);
        var split = body.IndexOf('|');
        if (split <= 0) return false;

        roomKey = body.Substring(0, split);
        childPath = body.Substring(split + 1);
        return true;
    }

    public static string Label(string key)
    {
        if (!TryParse(key, out _, out var path)) return null;

        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    public static IEnumerator ResolveRoutine(string key, Action<GameObject> done)
    {
        if (!TryParse(key, out var roomKey, out var childPath))
        {
            done(null);
            yield break;
        }

        GameObject room = null;
        yield return RoomSnapshot.LoadPrefabByKeyRoutine(roomKey, p => room = p);

        if (room == null)
        {
            done(null);
            yield break;
        }

        var child = FindByPath(room.transform, childPath);
        done(child != null ? child.gameObject : null);
    }

    public static Transform FindByPath(Transform root, string path)
    {
        var current = root;

        foreach (var step in path.Split('/'))
        {
            if (current == null) return null;

            Transform next = null;
            for (var i = 0; i < current.childCount; i++)
            {
                if (current.GetChild(i).name != step) continue;
                next = current.GetChild(i);
                break;
            }

            current = next;
        }

        return current;
    }

    public static string PathOf(Transform node, Transform root)
    {
        var path = node.name;
        for (var current = node.parent; current != null && current != root; current = current.parent)
            path = current.name + "/" + path;
        return path;
    }

    /// Copies a piece out of a loaded prefab into the room, tagged with the key that finds it again.
    public static GameObject Lift(GameObject source, Transform parent, string key)
    {
        if (source == null) return null;

        var go = UnityEngine.Object.Instantiate(source, parent);

        // Frozen before it is switched on: RandomEnable rolls in OnEnable and would switch the
        // piece straight back off.
        Tools.PropRandomisers.Freeze(go);
        go.SetActive(true);

        go.AddComponent<CTPropSource>().Key = key;
        return go;
    }
}

/// The key of the prefab piece an object was copied from, for things the object pool cannot name:
/// a child lifted out of a room or a plot. Read by the snapshot, written by whoever lifted it.
public class CTPropSource : MonoBehaviour
{
    public string Key = "";
}
