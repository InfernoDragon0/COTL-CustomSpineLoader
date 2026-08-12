using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Save format for one authored room, written as <PluginPath>/CustomNodeBlueprints/<name>.json.
//
// Naming: the dungeon data hierarchy is CTMapSelector (run map, later) -> CTLevelBlueprint (one
// level, later) -> CTNodeBlueprint (one room, this class). The CT prefix keeps these clear of the
// game's own Map.NodeBlueprint ScriptableObject, which the CTMapSelector work will reference.
//
// A blueprint is a FULL snapshot of the room: loading always clears everything first, so a deleted
// object is simply absent from the snapshot and vanilla scenery is captured as Props entries.
//
// Structures and custom enemies are keyed by NAME, never by their enum integer: vanilla ids shift
// between game versions and COTL_API mints custom ones at runtime via GuidManager, so a saved
// integer would resolve to the wrong thing on the next launch.
[Serializable]
public class CTNodeBlueprint
{
    public string MapName = "UntitledMap";
    public string SceneName = "Dungeon1";
    public bool UseVanillaFloorCollision = true;
    public string MusicEvent = "";   // FMOD event path (event:/music/...); empty = vanilla music
    // Restart MusicEvent when it finishes. FMOD events loop only if authored to; this covers
    // one-shot tracks used as room music.
    public bool MusicLoop;
    public List<MapShapeData> Shapes = [];
    public List<MapPropData> Props = [];
    public List<MapKeptData> KeptAuthored = [];
    public List<MapStructureData> Structures = [];
    public List<MapDoorData> Doors = [];
    public List<MapEnemyData> Enemies = [];
    public List<MapPodiumData> Podiums = [];
}

[Serializable]
public class MapShapeData
{
    public SerializableVector3 Position;
    public string Profile = "Primary";
    public bool IsOpenEnded;
    public bool HasCollision = true;
    public int ColliderDetail = 16;
    public float ColliderOffset;
    public List<MapShapePointData> Points = [];
}

[Serializable]
public class MapShapePointData
{
    public SerializableVector3 Position;
    public SerializableVector3 LeftTangent;
    public SerializableVector3 RightTangent;
    public string TangentMode = "Linear";
    public float Height = 1f;
    public int SpriteIndex;
    public bool Corner;
}

// A room-prefab-authored object (no prefab key exists for it - e.g. the "Dungeon Specific"
// backdrop assets) that was present at save time. It cannot be re-instantiated, but the same
// room shell regenerates it, so the loader PRESERVES it through the clear instead: parked under
// the editor transform while everything else is wiped, then restored at the saved transform.
// Deleting one in the editor still round-trips - absent from this list means it gets cleared.
[Serializable]
public class MapKeptData
{
    public string Parent = "Room";   // which sweep root it is a direct child of
    public string Name = "";
    public SerializableVector3 Position;
    public float RotationZ;
    public SerializableVector3 Scale;
}

// One snapshotted scene object (vanilla scenery, decorations, encounter props, clones), resolved
// back to the prefab it was spawned from so the room can be recreated after a full clear.
[Serializable]
public class MapPropData
{
    public string Key = "";          // addressable key, Resources path, or island prefab name
    public bool IsAddressable = true;
    public bool IsIslandRef;         // Key names a prefab in GenerateRoom's island piece lists
    public int ParentIslandIndex = -1; // index into Props of the island this was a child of
    public string Parent = "Scenery"; // Scenery | Heavy | Room | Custom | Island
    public SerializableVector3 Position;
    public float RotationZ;
    public SerializableVector3 Scale;
}

[Serializable]
public class MapStructureData
{
    public string TypeName = "";     // vanilla: StructureBrain.TYPES name; custom: InternalName
    public bool IsCustom;
    public SerializableVector3 Position;
    public float Rotation;
    public bool FlipX;
}

// Door-to-next-node routing is NOT stored here: which blueprint a door leads to is level-scoped
// data and will live in the future CTLevelBlueprint.
[Serializable]
public class MapDoorData
{
    public string Direction = "North";
    public SerializableVector3 Position;
    public float RotationZ;
}

[Serializable]
public class MapEnemyData
{
    public string Key = "";          // vanilla: addressable prefab path; custom: CustomEnemy.InternalName
    public bool IsCustom;
    public SerializableVector3 Position;
}

[Serializable]
public class MapPodiumData
{
    public SerializableVector3 Position;
    public string Type = "Random";   // Interaction_WeaponSelectionPodium.Types name
    // true = vanilla choose-one-of-N (equipping disables the room's other podiums);
    // false = only the equipped podium is consumed, the rest stay usable.
    public bool ClearAllOnEquip = true;
}

// Node blueprints live as flat files: CustomNodeBlueprints/<mapname>.json.
public static class MapEditorSerialization
{
    public const string FolderName = "CustomNodeBlueprints";

    public static SerializableVector3 V3(Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };

    public static Vector3 ToVector3(SerializableVector3 v) =>
        v == null ? Vector3.zero : new Vector3(v.X, v.Y, v.Z);

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    public static string Sanitize(string name)
    {
        var result = string.IsNullOrWhiteSpace(name) ? "UntitledMap" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            result = result.Replace(c, '_');
        return result;
    }

    public static string PathFor(string mapName) => Path.Combine(RootPath, Sanitize(mapName) + ".json");

    public static bool Exists(string mapName) => File.Exists(PathFor(mapName));

    // Returns the path written, or null on failure.
    public static string Save(CTNodeBlueprint map)
    {
        if (map == null) return null;

        map.MapName = Sanitize(map.MapName);

        try
        {
            if (!Directory.Exists(RootPath)) Directory.CreateDirectory(RootPath);

            var path = PathFor(map.MapName);
            File.WriteAllText(path, JsonConvert.SerializeObject(map, Formatting.Indented));
            Plugin.Log.LogInfo($"MapEditor: saved blueprint '{map.MapName}' with {map.Shapes.Count} shape(s), " +
                               $"{map.Props.Count} prop(s), {map.Structures.Count} structure(s), " +
                               $"{map.Doors.Count} door(s), {map.Enemies.Count} enemy(ies), " +
                               $"{map.Podiums.Count} podium(s) to {path}");
            return path;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: failed to save blueprint: " + e);
            return null;
        }
    }

    public static List<CTNodeBlueprint> LoadAll()
    {
        var results = new List<CTNodeBlueprint>();

        try
        {
            if (Directory.Exists(RootPath))
                foreach (var file in Directory.GetFiles(RootPath, "*.json", SearchOption.TopDirectoryOnly))
                    TryLoad(file, results);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: blueprint scan failed: " + e);
        }

        return results;
    }

    public static CTNodeBlueprint LoadByName(string mapName)
    {
        var path = PathFor(mapName);
        if (!File.Exists(path)) return null;

        var results = new List<CTNodeBlueprint>();
        TryLoad(path, results);
        return results.Count > 0 ? results[0] : null;
    }

    // The save-time screenshot written next to the json; null when the map predates snapshots.
    public static string SnapshotPathFor(string mapName)
    {
        var path = Path.Combine(RootPath, Sanitize(mapName) + ".png");
        return File.Exists(path) ? path : null;
    }

    private static void TryLoad(string path, List<CTNodeBlueprint> results)
    {
        try
        {
            var bp = JsonConvert.DeserializeObject<CTNodeBlueprint>(File.ReadAllText(path));
            if (bp != null) results.Add(bp);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"MapEditor: could not parse blueprint '{path}': {e.Message}");
        }
    }
}
