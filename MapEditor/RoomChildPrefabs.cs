using System;
using System.Collections;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Addressing something that lives INSIDE a room prefab.
//
// Not everything the game ships has an address of its own. NPCs have none at all, and neither do
// the bosses that are built into their arenas - Narinder and the two Guardians stand under a
// "Death Cat Controller" inside Boss Room Dungeon 1_6, and nothing under Assets/Prefabs/Enemies
// will ever name them. What they do have is a stable position in a prefab that IS addressable, so
// that is what gets written down: the room's key and the path to the child within it.
//
// The important property is that a child of a prefab ASSET instantiates on its own. Unity hands
// back that subtree and nothing else, so the character comes across without the room around it.
//
// The NPC tool has worked this way since it was written; this is the same grammar, lifted out so
// the enemy tool can use it too and the two cannot drift apart on what a key means.
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

    // The child's own name: the path is how to find it, not what to call it.
    public static string Label(string key)
    {
        if (!TryParse(key, out _, out var path)) return null;

        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    // Key -> the prefab child, for thumbnails, the cursor preview and placement alike, so all three
    // agree on what a key points at.
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
}
