using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

[Serializable]
public class CTNodeBlueprint
{
    public string MapName = "UntitledMap";
    public string SceneName = "Dungeon1";
    public string SourceRoom = "";
    public bool UseVanillaFloorCollision = true;
    public string MusicEvent = "";   // FMOD event path (event:/music/...); empty = vanilla music
    public bool MusicLoop;
    public MapLightingData Lighting = new();
    public MapWeatherData Weather = new();
    public List<MapShapeData> Shapes = [];
    public List<MapPropData> Props = [];
    public List<MapKeptData> KeptAuthored = [];
    public List<MapStructureData> Structures = [];
    public List<MapDoorData> Doors = [];
    public List<MapEnemyData> Enemies = [];
    public List<MapNpcData> Npcs = [];
    public List<MapTriggerData> Triggers = [];
    public List<MapPodiumData> Podiums = [];
    public List<MapGroupData> Groups = [];

    public MapTotemData BuildTotem;
}

[Serializable]
public class MapTotemData
{
    public SerializableVector3 Position;
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

    public int? SortingOrder;

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

[Serializable]
public class MapKeptData
{
    public string Parent = "Room";   // which sweep root it is a direct child of
    public string Name = "";
    public SerializableVector3 Position;
    public float RotationZ;

    public float RotationY;
    public SerializableVector3 Scale;
}

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

    public float RotationY;
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

    public SerializableVector3 Scale;

    public bool SeeThrough;
    public bool FogThrough;
    public bool Wind;
}

[Serializable]
public class MapDoorData
{
    public string Direction = "North";
    public SerializableVector3 Position;
    public float RotationZ;

    public float RotationY;
}

[Serializable]
public class MapEnemyData
{
    public string Key = "";          // vanilla: addressable prefab path; custom: CustomEnemy.InternalName
    public bool IsCustom;
    public SerializableVector3 Position;

    public SerializableVector3 Scale;
}

[Serializable]
public class MapNpcData
{
    public string Key = "";          // vanilla: addressable prefab path; custom: InternalName
    public bool IsCustom;
    public SerializableVector3 Position;

    public SerializableVector3 Scale;
}

[Serializable]
public class MapTriggerData
{
    public string Id = "";

    public string Action = "";

    public SerializableVector3 Position;   // centre
    public float Width = 4f;
    public float Height = 3f;
    public bool Once = true;

    public List<MapTriggerActionData> Actions = [];

    public bool LockPlayerControl = true;

    public bool Blocking;
}

[Serializable]
public class MapTriggerActionData
{
    public string Type = "";

    public string Target = "";

    public SerializableVector3 Position;

    public float Spread = 1.3f;

    public bool Loop;
    public float Duration;

    public float Amount;

    public string Subtext = "";

    public bool FreezeAtEnd;         // PlayObjectAnimation: hold the last frame instead of resuming
}

[Serializable]
public class MapLightingData
{
    public bool Enabled;

    public SerializableColor Ambient = new();
    public SerializableColor DirectionalLight = new();
    public float DirectionalIntensity = 1f;
    public float ShadowStrength = 0.5f;
    public float Exposure = 1.15f;

    public SerializableColor Fog = new();
    public float FogNear = 10f;
    public float FogFar = 15f;
    public float FogHeight = 0.5f;
    public float FogSpread = 1f;
}

[Serializable]
public class MapWeatherData
{
    public bool Enabled;
    public string Type = "";
    public string Strength = "";

    public float Transition;
}

[Serializable]
public class SerializableColor
{
    public float R;
    public float G;
    public float B;
    public float A = 1f;

    public static SerializableColor From(Color c) => new() { R = c.r, G = c.g, B = c.b, A = c.a };
    public Color ToColor() => new(R, G, B, A);
}

public class MapPodiumData
{
    public SerializableVector3 Position;
    public string Type = "Random";   // Interaction_WeaponSelectionPodium.Types name
    public bool ClearAllOnEquip = true;

    public SerializableVector3 Scale;
}

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

    public static bool Available(string mapName) => ReadPathFor(mapName) != null;

    private static string ReadPathFor(string mapName) =>
        APIHelper.ModContentPaths.FindFile(FolderName, Sanitize(mapName) + ".json");

    public static System.Threading.Tasks.Task<string> SaveAsync(CTNodeBlueprint map)
    {
        if (map == null) return System.Threading.Tasks.Task.FromResult<string>(null);

        map.MapName = Sanitize(map.MapName);

        string json;
        string path;
        try
        {
            json = JsonConvert.SerializeObject(map, Formatting.Indented);
            path = PathFor(map.MapName);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: failed to serialise blueprint: " + e);
            return System.Threading.Tasks.Task.FromResult<string>(null);
        }

        var shapes = map.Shapes.Count;
        var props = map.Props.Count;
        var structures = map.Structures.Count;
        var doors = map.Doors.Count;
        var enemies = map.Enemies.Count;
        var podiums = map.Podiums.Count;
        var name = map.MapName;

        return System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(RootPath)) Directory.CreateDirectory(RootPath);
                File.WriteAllText(path, json);
                Plugin.Log.LogInfo($"MapEditor: saved blueprint '{name}' with {shapes} shape(s), " +
                                   $"{props} prop(s), {structures} structure(s), {doors} door(s), " +
                                   $"{enemies} enemy(ies), {podiums} podium(s) to {path}");
                return path;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("MapEditor: failed to save blueprint: " + e);
                return null;
            }
        });
    }

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
            foreach (var file in APIHelper.ModContentPaths.FilesIn(FolderName, "*.json"))
                TryLoad(file, results);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: blueprint scan failed: " + e);
        }

        return results;
    }

    public static List<string> SavedNames()
    {
        var names = new List<string>();

        try
        {
            foreach (var file in APIHelper.ModContentPaths.FilesIn(FolderName, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: blueprint name scan failed: " + e);
        }

        return names;
    }

    public static CTNodeBlueprint LoadByName(string mapName)
    {
        var path = ReadPathFor(mapName);
        if (path == null) return null;

        var results = new List<CTNodeBlueprint>();
        TryLoad(path, results);
        return results.Count > 0 ? results[0] : null;
    }

    public static string SnapshotPathFor(string mapName) =>
        APIHelper.ModContentPaths.FindFile(FolderName, Sanitize(mapName) + ".png");

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
