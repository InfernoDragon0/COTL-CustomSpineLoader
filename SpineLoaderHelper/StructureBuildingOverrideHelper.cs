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
    public static void LoadBuildingOverrides()
    {
        if (!Directory.Exists(Path.Combine(Plugin.PluginPath, $"BuildingOverrides")))
        {
            Directory.CreateDirectory(Path.Combine(Plugin.PluginPath, $"BuildingOverrides"));
            Plugin.Log.LogInfo("Created BuildingOverrides directory.");
            return;
        }
        //loop through each folder in BuildingOverrides
        foreach (var dir in Directory.GetDirectories(Path.Combine(Plugin.PluginPath, $"BuildingOverrides")))
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
                Plugin.Log.LogInfo($"Loaded {overrides.Count} overrides for building {buildingName}.");
            }
        }
    }
    
    public static List<CustomStructureBuildingData> GetOverridesForBuilding(string buildingName)
    {
        var convertibleFormat = StructureBuildingOverrides.TryGetValue(buildingName, out var overrides) ? overrides : null;
        if (convertibleFormat == null) return null;

        //convert this list into a list of StructureBuildingOverrideData
        var result = new List<CustomStructureBuildingData>();
        foreach (var item in convertibleFormat)
        {
            var data = new CustomStructureBuildingData
            {
                Offset = item.Offset.ToVector3(),
                Scale = item.Scale.ToVector3(),
                Rotation = item.Rotation.ToVector3(),
                Sprite = TextureHelper.CreateSpriteFromPath(Path.Combine(Plugin.PluginPath, "BuildingOverrides/" + buildingName + "/" + item.SpriteImageName))
            };
            result.Add(data);
            Plugin.Log.LogInfo($"Custom Spine Loader: Loaded override with sprite {item.SpriteImageName} for building {buildingName}: offset {data.Offset}, scale {data.Scale}, rotation {data.Rotation}.");
        }

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