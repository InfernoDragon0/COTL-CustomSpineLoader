using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

/// The game dresses some prefabs by rolling dice on load - which children are on, which sprite a
/// piece wears. That is right for a generated dungeon and wrong for an authored room, so anything
/// the editor spawns is frozen, and a set whose whole purpose is the roll is broken into its pieces.
public static class PropRandomisers
{
    /// Components that decide WHICH objects exist. Their children are the pieces worth breaking out.
    private static readonly System.Type[] Structural =
    [
        typeof(randomChildPicker),
        typeof(RandomChildPickerWeighted),
        typeof(RandomInstantiator),
        typeof(RandomObjectPicker),
        typeof(RandomBushPicker),
        typeof(RandomGrassPicker)
    ];

    /// Components that vary one object's look. Frozen, never broken apart.
    private static readonly System.Type[] Cosmetic =
    [
        typeof(RandomEnable),
        typeof(RandomSpritePicker),
        typeof(SimpleSpineSkineRandomiser),
        typeof(SimpleRandomSpineSkin),
        typeof(SpineRandomAnimationPicker),
        typeof(AnimatorStartOnRandomFrame),
        typeof(RandomFrame)
    ];

    /// Strips every randomiser out of a subtree. Called on anything the editor spawns, before the
    /// first Update - a picker that never reaches its Start leaves the prefab exactly as authored.
    public static void Freeze(GameObject go)
    {
        if (go == null) return;

        Strip(go, Structural);
        Strip(go, Cosmetic);
    }

    private static void Strip(GameObject go, System.Type[] types)
    {
        foreach (var type in types)
            foreach (var component in go.GetComponentsInChildren(type, true))
                if (component != null) Object.DestroyImmediate(component);
    }

    public static bool IsRandomisedSet(GameObject go)
    {
        if (go == null) return false;

        foreach (var type in Structural)
            if (go.GetComponentInChildren(type, true) != null) return true;

        return false;
    }

    /// Lifts every piece a randomiser would have chosen between out of `spawned` and into `parent`,
    /// each carrying the key that finds it again, then destroys what is left. Null if there was
    /// nothing to break apart; the caller keeps the object whole then.
    public static List<GameObject> BreakApart(GameObject spawned, string sourceKey, Transform parent)
    {
        if (spawned == null || parent == null || string.IsNullOrEmpty(sourceKey)) return null;

        var holders = new List<Transform>();
        foreach (var type in Structural)
            foreach (var component in spawned.GetComponentsInChildren(type, true))
            {
                if (component is not Component behaviour || behaviour == null) continue;
                if (!holders.Contains(behaviour.transform)) holders.Add(behaviour.transform);
            }

        if (holders.Count == 0) return null;

        var root = spawned.transform;
        var pieces = new List<GameObject>();

        foreach (var holder in holders)
        {
            if (holder == null) continue;

            var children = new List<Transform>();
            for (var i = 0; i < holder.childCount; i++) children.Add(holder.GetChild(i));

            foreach (var child in children)
            {
                if (child == null) continue;

                // The path has to be read while the piece is still where the prefab put it.
                var key = RoomChildPrefabs.KeyFor(sourceKey, RoomChildPrefabs.PathOf(child, root));

                child.SetParent(parent, worldPositionStays: true);

                Freeze(child.gameObject);
                child.gameObject.SetActive(true);

                var tag = child.gameObject.GetComponent<CTPropSource>() ??
                          child.gameObject.AddComponent<CTPropSource>();
                tag.Key = key;

                pieces.Add(child.gameObject);
            }
        }

        Object.Destroy(spawned);
        return pieces;
    }
}
