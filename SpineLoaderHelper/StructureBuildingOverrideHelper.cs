using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.CustomStructures;
using COTL_API.Helpers;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

public class StructureBuildingOverrideHelper
{
    public static Dictionary<string, List<StructureBuildingOverride>> StructureBuildingOverrides { get; private set; } = [];

    private static readonly Dictionary<string, string> _folders = [];

    public static void LoadBuildingOverrides()
    {
        if (!Directory.Exists(Path.Combine(Plugin.PluginPath, $"BuildingOverrides")))
        {
            Directory.CreateDirectory(Path.Combine(Plugin.PluginPath, $"BuildingOverrides"));
            Plugin.Log.LogInfo("Created BuildingOverrides directory.");
        }
        foreach (var dir in APIHelper.ModContentPaths.DirectoriesIn("BuildingOverrides"))
        {
            var buildingName = new DirectoryInfo(dir).Name;
            var overrides = new List<StructureBuildingOverride>();

            if (File.Exists(Path.Combine(dir, "config.json")))
            {
                var jsonLoaded = File.ReadAllText(Path.Combine(dir, "config.json"));
                try
                {
                    var overrideData = JsonConvert.DeserializeObject<StructureBuildingOverrideData>(jsonLoaded) ?? null;
                    if (overrideData != null && overrideData.Overrides != null)
                    {
                        overrides.AddRange(overrideData.Overrides);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Error loading building override from config.json in {dir}: {e}");
                }
            }

            if (overrides.Count > 0)
            {
                StructureBuildingOverrides[buildingName] = overrides;
                _folders[buildingName] = dir;
                Plugin.Log.LogInfo($"Loaded {overrides.Count} overrides for building {buildingName}.");
            }
        }
    }
    
    private static readonly Dictionary<string, List<CustomStructureBuildingData>> _converted = [];

    public static List<CustomStructureBuildingData> GetOverridesForBuilding(string buildingName)
    {
        if (_converted.TryGetValue(buildingName, out var cached)) return cached;

        var convertibleFormat = StructureBuildingOverrides.TryGetValue(buildingName, out var overrides) ? overrides : null;
        if (convertibleFormat == null)
        {
            _converted[buildingName] = null;
            return null;
        }

        var folder = _folders.TryGetValue(buildingName, out var known)
            ? known
            : Path.Combine(Plugin.PluginPath, "BuildingOverrides/" + buildingName);

        var result = new List<CustomStructureBuildingData>();
        foreach (var item in convertibleFormat)
        {
            var data = new CustomStructureBuildingData
            {
                Offset = item.Offset.ToVector3(),
                Scale = item.Scale.ToVector3(),
                Rotation = item.Rotation.ToVector3(),
                Sprite = TextureHelper.CreateSpriteFromPath(Path.Combine(folder, item.SpriteImageName))
            };
            data.Sprite.texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            data.Sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            result.Add(data);
            Plugin.Log.LogInfo($"Custom Spine Loader: Loaded override with sprite {item.SpriteImageName} for building {buildingName}: offset {data.Offset}, scale {data.Scale}, rotation {data.Rotation}.");
        }

        _converted[buildingName] = result;
        return result;
    }
}

[Serializable]
public class StructureBuildingOverrideData
{
    public List<StructureBuildingOverride> Overrides = [];
}

[Serializable]
public class StructureBuildingOverride
{
    public SerializableVector3 Offset;
    public SerializableVector3 Scale;
    public SerializableVector3 Rotation;
    public string SpriteImageName;
}

[Serializable]
public class SerializableVector3
{
    public float X;
    public float Y;
    public float Z;

    public Vector3 ToVector3() => new(X, Y, Z);
}

[Serializable]
public class SerializableVector2
{
    public float X;
    public float Y;

    public Vector2 ToVector2() => new(X, Y);
}