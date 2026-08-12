using System;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Save format for an authored map. Written under <PluginPath>/CustomMaps/<Name>/map.json,
// matching the per-folder convention of APIHelper/Loader.cs so a future loader can use Loader<T>.
//
// Structures are keyed by NAME, never by the StructureBrain.TYPES integer: vanilla ids shift
// between game versions and COTL_API mints custom ones at runtime via GuidManager, so a saved
// integer would resolve to the wrong structure on the next launch.
[Serializable]
public class MapData
{
    public string MapName = "UntitledMap";
    public string SceneName = "Dungeon1";
    public string Cleared = "None";
    public List<string> Deleted = [];
    public List<MapShapeData> Shapes = [];
    public List<MapStructureData> Structures = [];
    public List<MapDoorData> Doors = [];
}

[Serializable]
public class MapShapeData
{
    public SerializableVector3 Position;
    public string Profile = "Primary";
    public bool IsOpenEnded;
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

[Serializable]
public class MapStructureData
{
    public string TypeName = "";
    public bool IsCustom;
    public SerializableVector3 Position;
    public float Rotation;
    public bool FlipX;
}

[Serializable]
public class MapDoorData
{
    public string Direction = "North";
    public SerializableVector3 Position;
    public float RotationZ;
}

public static class MapEditorSerialization
{
    public const string FolderName = "CustomMaps";

    public static SerializableVector3 V3(Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };

    public static string RootPath => Path.Combine(Plugin.PluginPath, FolderName);

    // Returns the path written, or null on failure.
    public static string Save(MapData map)
    {
        if (map == null) return null;

        var name = string.IsNullOrWhiteSpace(map.MapName) ? "UntitledMap" : map.MapName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        map.MapName = name;

        try
        {
            var folder = Path.Combine(RootPath, name);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, "config.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(map, Formatting.Indented));
            Plugin.Log.LogInfo($"MapEditor: saved map '{name}' with {map.Shapes.Count} shape(s), " +
                               $"{map.Structures.Count} structure(s), {map.Doors.Count} door(s) to {path}");
            return path;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("MapEditor: failed to save map: " + e);
            return null;
        }
    }
}
