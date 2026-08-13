using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.MapEditor.Tools;
using MMRoomGeneration;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor;

// Captures every prop currently in the room as prefab-path + transform entries, so a blueprint
// can recreate the whole room from nothing. Resolution is tiered:
//
//   1. ObjectPool bookkeeping: spawnedObjects maps a live instance back to its prefab, and the
//      pool's path dictionaries map that prefab back to the string it was spawned from. This
//      covers all pooled decorations and critters exactly.
//   2. Name matching against the Addressables catalog for anything instantiated directly
//      (island pieces, encounters, secondary sprite shapes, ctrl-drag clones). Only names
//      ending in "(Clone)" are considered - prefab-authored children never carry that suffix,
//      so this cleanly separates runtime additions from content a recorded prefab already
//      brings back by itself.
//   3. Anything else is logged and skipped.
public static class RoomSnapshot
{
    // filename-without-extension -> addressable key; null value marks an ambiguous name.
    private static Dictionary<string, string> _catalogByName;

    public static void Collect(CTNodeBlueprint map, RuntimeMapEditor editor)
    {
        map.Props.Clear();

        var room = SceneRefs.Room;
        if (room == null) return;

        map.KeptAuthored.Clear();
        map.SourceRoom = StripClone(room.gameObject.name);

        var prefabPaths = BuildPrefabPathLookup();
        var resolved = 0;
        var kept = 0;
        var skipped = 0;

        void Sweep(Transform parent, string parentTag, bool runtimeOnly, int parentIslandIndex = -1)
        {
            if (parent == null) return;

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || !child.gameObject.activeSelf) continue;
                if (ShouldSkip(child.gameObject, editor, runtimeOnly)) continue;

                if (TryResolveIsland(child.gameObject, room, out var islandKey))
                {
                    map.Props.Add(new MapPropData
                    {
                        Key = islandKey,
                        IsIslandRef = true,
                        Parent = "Island",
                        Position = MapEditorSerialization.V3(child.position),
                        RotationZ = child.eulerAngles.z,
                        Scale = MapEditorSerialization.V3(child.lossyScale)
                    });
                    resolved++;

                    // The island's runtime children (its textured floor art and encounter) are
                    // their own spawns - the bare prefab is only the flat green placeholder fill,
                    // so losing the art child is very visible. They record which island entry
                    // owns them so the loader can re-parent them correctly.
                    Sweep(child, parentTag, runtimeOnly: true, parentIslandIndex: map.Props.Count - 1);
                }
                else if (TryResolve(child.gameObject, prefabPaths, out var key, out var isAddressable))
                {
                    map.Props.Add(new MapPropData
                    {
                        Key = key,
                        IsAddressable = isAddressable,
                        Parent = parentTag,
                        ParentIslandIndex = parentIslandIndex,
                        Position = MapEditorSerialization.V3(child.position),
                        RotationZ = child.eulerAngles.z,
                        Scale = MapEditorSerialization.V3(child.lossyScale)
                    });
                    resolved++;

                    // Runtime additions nested one level down (an island piece's encounter and
                    // secondary sprite shape) are their own spawns and need their own entries -
                    // re-instantiating the island prefab alone would not bring them back.
                    Sweep(child, parentTag, runtimeOnly: true, parentIslandIndex);
                }
                else if (!runtimeOnly && !child.name.EndsWith("(Clone)"))
                {
                    // Authored into the room prefab, so no prefab key exists - but the room
                    // shell regenerates it. Recorded so the loader preserves it through the
                    // clear; a deleted one is simply absent and gets cleared like the rest.
                    map.KeptAuthored.Add(new MapKeptData
                    {
                        Parent = parentTag,
                        Name = child.name,
                        Position = MapEditorSerialization.V3(child.position),
                        RotationZ = child.eulerAngles.z,
                        Scale = MapEditorSerialization.V3(child.lossyScale)
                    });
                    kept++;
                }
                else
                {
                    skipped++;
                    Plugin.Log.LogInfo($"MapEditor snapshot: could not resolve '{HierarchyPath(child)}', not saved.");
                    Sweep(child, parentTag, runtimeOnly: true);
                }
            }
        }

        Sweep(room.SceneryTransform != null ? room.SceneryTransform.transform : null, "Scenery", false);
        Sweep(room.HeavyAssetsTransform, "Heavy", false);
        Sweep(room.CustomTransform != null ? room.CustomTransform.transform : null, "Custom", false);
        Sweep(room.RoomTransform != null ? room.RoomTransform.transform : null, "Island", false);
        SweepRoomRoot(room, editor, map, prefabPaths, ref resolved, ref kept, ref skipped);

        Plugin.Log.LogInfo($"MapEditor snapshot: {resolved} prop(s) resolved, " +
                           $"{kept} authored object(s) marked kept, {skipped} skipped.");
    }

    // The room root holds the structural containers (swept separately above) plus loose backdrop
    // objects; only the loose ones are wanted here.
    private static void SweepRoomRoot(GenerateRoom room, RuntimeMapEditor editor, CTNodeBlueprint map,
        Dictionary<GameObject, (string key, bool addressable)> prefabPaths, ref int resolved, ref int kept,
        ref int skipped)
    {
        var containers = new HashSet<Transform>();
        if (room.SceneryTransform != null) containers.Add(room.SceneryTransform.transform);
        if (room.HeavyAssetsTransform != null) containers.Add(room.HeavyAssetsTransform);
        if (room.CustomTransform != null) containers.Add(room.CustomTransform.transform);
        if (room.RoomTransform != null) containers.Add(room.RoomTransform.transform);

        for (var i = 0; i < room.transform.childCount; i++)
        {
            var child = room.transform.GetChild(i);
            if (child == null || containers.Contains(child) || !child.gameObject.activeSelf) continue;
            if (ShouldSkip(child.gameObject, editor, runtimeOnly: false)) continue;

            if (TryResolve(child.gameObject, prefabPaths, out var key, out var isAddressable))
            {
                map.Props.Add(new MapPropData
                {
                    Key = key,
                    IsAddressable = isAddressable,
                    Parent = "Room",
                    Position = MapEditorSerialization.V3(child.position),
                    RotationZ = child.eulerAngles.z,
                    Scale = MapEditorSerialization.V3(child.lossyScale)
                });
                resolved++;
            }
            else if (!child.name.EndsWith("(Clone)"))
            {
                map.KeptAuthored.Add(new MapKeptData
                {
                    Parent = "Room",
                    Name = child.name,
                    Position = MapEditorSerialization.V3(child.position),
                    RotationZ = child.eulerAngles.z,
                    Scale = MapEditorSerialization.V3(child.lossyScale)
                });
                kept++;
            }
            else
            {
                skipped++;
                Plugin.Log.LogInfo($"MapEditor snapshot: could not resolve '{HierarchyPath(child)}', not saved.");
            }
        }
    }

    private static bool ShouldSkip(GameObject go, RuntimeMapEditor editor, bool runtimeOnly)
    {
        // Doors, player, camera, room-lock logic and the editor's own objects.
        if (MapEditorProtection.IsProtected(go)) return true;

        // The room backdrop is derived state: the loader wipes every copy and recreates exactly
        // one, so saving it would only ever duplicate it (vanilla also stacks a new copy per
        // room re-entry, which is why it kept appearing as an unresolvable skip).
        if (go.name.StartsWith("Room Back Sprite")) return true;

        // Nested sweeps only pick up runtime spawns; prefab-authored children come back with
        // their parent prefab.
        if (runtimeOnly && !go.name.EndsWith("(Clone)")) return true;

        // Standalone sprite shapes round-trip as spline data via the shape tool. Shapes under
        // island pieces stay eligible: they are runtime spawns recorded as props.
        if (go.GetComponent<SpriteShapeController>() != null &&
            go.GetComponentInParent<IslandPiece>() == null) return true;

        // A lone enemy root is EnemyTool's or the encounter's business, never a prop. Containers
        // with enemy children (encounters) stay eligible.
        if (go.GetComponent<UnitObject>() != null) return true;

        // Objects the placement tools already serialize under their own sections.
        if (editor != null)
        {
            if (editor.GetTool<StructureTool>()?.IsTracked(go) == true) return true;
            if (editor.GetTool<EnemyTool>()?.IsTracked(go) == true) return true;
            if (editor.GetTool<PodiumTool>()?.IsTracked(go) == true) return true;
        }

        // Weapon podiums are serialized by the podium tool; vanilla-authored ones that survived
        // are skipped rather than duplicated as unresolvable props.
        if (go.GetComponentInChildren<Interaction_WeaponSelectionPodium>(true) != null) return true;

        return false;
    }

    // Island pieces are Object.Instantiate'd from GenerateRoom's own prefab lists, never pooled
    // and rarely unique in the addressables catalog - resolving them against those lists is what
    // guarantees the floor terrain round-trips. A missing island is not cosmetic: the composite
    // union loses the region connecting the doorway to the authored floor and the player gets
    // walled in at the entrance.
    private static bool TryResolveIsland(GameObject go, GenerateRoom room, out string key)
    {
        key = null;
        if (room == null || go.GetComponent<IslandPiece>() == null) return false;
        if (!go.name.EndsWith("(Clone)")) return false;

        var name = go.name;
        while (name.EndsWith("(Clone)"))
            name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();

        if (FindIslandPrefab(room, name) == null) return false;
        key = name;
        return true;
    }

    public static IslandPiece FindIslandPrefab(GenerateRoom room, string prefabName)
    {
        var go = FindIslandPrefabObject(room, prefabName);
        return go != null ? go.GetComponent<IslandPiece>() : null;
    }

    // name -> island prefab, across every source. Cached because tier 2 blocks on a load.
    private static readonly Dictionary<string, GameObject> _islandPrefabs = [];

    // Islands are the room's floor: a missing one leaves a hole in the collision union and the
    // player walled off from part of the map. Resolution cannot stop at the current room's
    // lists, because each room prefab carries its OWN addressable island set (Addr_StartPieces
    // and friends) - an entrance room's islands simply are not in a normal room's lists, so a
    // blueprint authored in one room type would lose its terrain in the other.
    public static GameObject FindIslandPrefabObject(GenerateRoom room, string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;
        if (_islandPrefabs.TryGetValue(prefabName, out var cached) && cached != null) return cached;

        // Tier 1: this room's own lists.
        if (room != null)
        {
            foreach (var list in new[] { room.StartPieces, room.IslandPieces, room.ResourcePieces })
            {
                if (list == null) continue;
                foreach (var piece in list)
                    if (piece != null && piece.name == prefabName) return CacheIsland(prefabName, piece.gameObject);
            }
        }

        // Tier 2: the addressables catalog - islands are addressable assets, so every room's
        // set is reachable regardless of which room is loaded.
        var catalog = CatalogByName();
        if (catalog.TryGetValue(prefabName, out var key) && key != null)
        {
            try
            {
                var result = Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion();
                if (result != null && result.GetComponent<IslandPiece>() != null)
                {
                    Plugin.Log.LogInfo($"MapEditor: island '{prefabName}' resolved from the addressables catalog.");
                    return CacheIsland(prefabName, result);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: island '{prefabName}' failed to load from '{key}': {e.Message}");
            }
        }

        // Tier 3: any island prefab still held in memory from an earlier room.
        foreach (var piece in Resources.FindObjectsOfTypeAll<IslandPiece>())
        {
            if (piece == null || piece.name != prefabName) continue;
            // Prefab assets only - a live scene instance would be a copy of the room we are
            // about to clear.
            if (piece.gameObject.scene.IsValid()) continue;
            Plugin.Log.LogInfo($"MapEditor: island '{prefabName}' resolved from loaded assets.");
            return CacheIsland(prefabName, piece.gameObject);
        }

        return null;
    }

    private static GameObject CacheIsland(string name, GameObject prefab)
    {
        _islandPrefabs[name] = prefab;
        return prefab;
    }

    public static string StripClone(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        while (name.EndsWith("(Clone)"))
            name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();
        return name;
    }

    // Any prefab in the addressables catalog, by filename. Used to reach a room prefab that is
    // not the one currently loaded.
    public static GameObject FindPrefabByName(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;

        var catalog = CatalogByName();
        if (!catalog.TryGetValue(prefabName, out var key) || key == null) return null;

        try
        {
            return Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: prefab '{prefabName}' failed to load from '{key}': {e.Message}");
            return null;
        }
    }

    // Depth-first search for a named descendant, used to locate an authored object inside a
    // room prefab whose container layout may differ from the live room's.
    public static Transform FindChildByName(Transform root, string name, int depth = 0)
    {
        if (root == null || depth > 6) return null;

        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child.name == name) return child;

            var nested = FindChildByName(child, name, depth + 1);
            if (nested != null) return nested;
        }
        return null;
    }

    private static bool TryResolve(GameObject go,
        Dictionary<GameObject, (string key, bool addressable)> prefabPaths,
        out string key, out bool isAddressable)
    {
        key = null;
        isAddressable = true;

        // Tier 1: the pool knows exactly which prefab this instance came from.
        var pool = ObjectPool.instance;
        if (pool != null && pool.spawnedObjects.TryGetValue(go, out var prefab) && prefab != null &&
            prefabPaths.TryGetValue(prefab, out var entry))
        {
            key = entry.key;
            isAddressable = entry.addressable;
            return true;
        }

        if (!go.name.EndsWith("(Clone)")) return false;

        var name = go.name;
        while (name.EndsWith("(Clone)"))
            name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();

        // Tier 2: the owning island's serialized art/encounter path lists. These hold the exact
        // addressable keys InitIsland spawned the child from, so unlike the global catalog there
        // is no name ambiguity - and the floor art child MUST resolve, or a loaded island shows
        // only its flat green placeholder fill.
        var piece = go.GetComponentInParent<IslandPiece>();
        if (piece != null)
        {
            var fromLists = MatchIslandChildPath(piece, name);
            if (fromLists != null)
            {
                key = fromLists;
                isAddressable = true;
                return true;
            }
        }

        // Tier 3: catalog lookup by name for direct Addressables instantiations and clones.
        var catalog = CatalogByName();
        if (catalog.TryGetValue(name, out var catalogKey) && catalogKey != null)
        {
            key = catalogKey;
            isAddressable = true;
            return true;
        }

        return false;
    }

    private static string MatchIslandChildPath(IslandPiece piece, string strippedName)
    {
        foreach (var list in new[] { piece.SpriteShapes, piece.SpriteShapes2, piece.Encounters })
        {
            if (list?.ObjectList == null) continue;
            foreach (var entry in list.ObjectList)
            {
                if (entry == null || string.IsNullOrEmpty(entry.GameObjectPath)) continue;
                if (Path.GetFileNameWithoutExtension(entry.GameObjectPath) == strippedName)
                    return entry.GameObjectPath;
            }
        }
        return null;
    }

    // prefab asset -> the path string ObjectPool.Spawn(path, ...) was called with.
    private static Dictionary<GameObject, (string, bool)> BuildPrefabPathLookup()
    {
        var result = new Dictionary<GameObject, (string, bool)>();
        var pool = ObjectPool.instance;
        if (pool == null) return result;

        foreach (var pair in pool.loadedAddressables)
        {
            try
            {
                if (pair.Value.IsValid() && pair.Value.Result != null)
                    result[pair.Value.Result] = (pair.Key, true);
            }
            catch (System.Exception)
            {
                // A handle mid-load or released; nothing spawned from it can be in the scene.
            }
        }

        foreach (var pair in pool.loadedFromResources)
            if (pair.Value != null) result[pair.Value] = (pair.Key, false);

        return result;
    }

    private static Dictionary<string, string> CatalogByName()
    {
        if (_catalogByName != null) return _catalogByName;

        _catalogByName = [];
        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator?.Keys == null) continue;
            foreach (var keyObj in locator.Keys)
            {
                if (keyObj is not string key || !key.EndsWith(".prefab")) continue;

                var name = Path.GetFileNameWithoutExtension(key);
                if (_catalogByName.TryGetValue(name, out var existing))
                {
                    // Same filename under two keys cannot be resolved by name alone.
                    if (existing != key) _catalogByName[name] = null;
                }
                else
                {
                    _catalogByName[name] = key;
                }
            }
        }

        Plugin.Log.LogInfo($"MapEditor snapshot: catalog name index holds {_catalogByName.Count} prefab name(s).");
        return _catalogByName;
    }

    private static string HierarchyPath(Transform t)
    {
        var path = t.name;
        var parent = t.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
