using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.Helpers;
using CustomSpineLoader.SpineLoaderHelper;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.WorldMap;

public static class WorldMapAssets
{
    private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SkeletonDataAsset> Skeletons = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);

    private static Material _graphicMaterial;

    public static Sprite GetSprite(string mapName, string fileName)
    {
        if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(fileName)) return null;

        var key = mapName + "/" + fileName;
        if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
        if (Failed.Contains(key)) return null;

        var path = Path.Combine(CTWorldMapSerialization.FolderForRead(mapName), fileName);
        if (!File.Exists(path))
        {
            Plugin.Log.LogWarning($"World map '{mapName}': image '{fileName}' is not in the map's folder.");
            Failed.Add(key);
            return null;
        }

        try
        {
            var texture = TextureHelper.CreateTextureFromPath(path);
            texture.name = Path.GetFileNameWithoutExtension(path);
            SpineFolderLoader.Keep(texture);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            SpineFolderLoader.Keep(sprite);

            Sprites[key] = sprite;
            return sprite;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"World map '{mapName}': image '{fileName}' failed to load: {e.Message}");
            Failed.Add(key);
            return null;
        }
    }

    public static SkeletonDataAsset GetSkeleton(string mapName, string folderName)
    {
        if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(folderName)) return null;

        var key = mapName + "/" + folderName;
        if (Skeletons.TryGetValue(key, out var cached) && cached != null) return cached;
        if (Failed.Contains(key)) return null;

        var folder = Path.Combine(CTWorldMapSerialization.FolderForRead(mapName), folderName);

        var data = SpineFolderLoader.Build(folder, "worldmap:" + mapName, scale: 1f);
        if (data == null)
        {
            Plugin.Log.LogWarning($"World map '{mapName}': spine folder '{folderName}' has no " +
                                  "usable skeleton (.json + .atlas + pages).");
            Failed.Add(key);
            return null;
        }

        Skeletons[key] = data;
        return data;
    }

    public static Material GraphicMaterial()
    {
        if (_graphicMaterial != null) return _graphicMaterial;

        var shader = Shader.Find("Spine/SkeletonGraphic");
        if (shader == null)
        {
            Plugin.Log.LogWarning("Shader 'Spine/SkeletonGraphic' was not found; world map spines " +
                                  "fall back to UI/Default and may show fringed edges.");
            shader = Shader.Find("UI/Default");
        }

        _graphicMaterial = new Material(shader);
        SpineFolderLoader.Keep(_graphicMaterial);
        return _graphicMaterial;
    }

    public const float SpineBaseScale = 0.005f;

    public static Vector3 LayerLocalScale(CTWorldMapLayer layer)
    {
        if (layer == null) return Vector3.one;

        var basis = layer.IsSpine ? SpineBaseScale : 1f;
        var x = (layer.Scale?.X ?? 1f) * basis * (layer.FlipX ? -1f : 1f);
        var y = (layer.Scale?.Y ?? 1f) * basis;
        return new Vector3(x, y, 1f);
    }

    public static SkeletonGraphic CreateSpineGraphic(Transform parent, SkeletonDataAsset data,
        string name, string skin, string animation, bool loop, float timeScale)
    {
        if (data == null) return null;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        try
        {
            var graphic = go.AddComponent<SkeletonGraphic>();
            graphic.material = GraphicMaterial();
            graphic.skeletonDataAsset = data;

            graphic.unscaledTime = true;
            graphic.raycastTarget = false;

            graphic.Initialize(false);
            if (graphic.Skeleton == null)
            {
                Plugin.Log.LogWarning($"World map: skeleton '{name}' failed to initialise.");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            if (!string.IsNullOrEmpty(skin))
            {
                try
                {
                    graphic.Skeleton.SetSkin(skin);
                    graphic.Skeleton.SetSlotsToSetupPose();
                }
                catch (Exception)
                {
                    Plugin.Log.LogWarning($"World map: skeleton '{name}' has no skin '{skin}'.");
                }
            }

            if (!string.IsNullOrEmpty(animation))
            {
                var found = data.GetSkeletonData(true)?.FindAnimation(animation);
                if (found != null) graphic.AnimationState.SetAnimation(0, animation, loop);
                else Plugin.Log.LogWarning($"World map: skeleton '{name}' has no animation '{animation}'.");
            }

            graphic.timeScale = timeScale > 0f ? timeScale : 1f;
            return graphic;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"World map: skeleton '{name}' could not be created: {e.Message}");
            UnityEngine.Object.Destroy(go);
            return null;
        }
    }

    public static List<string> AnimationNames(string mapName, string folderName)
    {
        var names = new List<string>();
        var data = GetSkeleton(mapName, folderName);
        var skeletonData = data != null ? data.GetSkeletonData(true) : null;
        if (skeletonData == null) return names;

        foreach (var animation in skeletonData.Animations)
            if (animation != null) names.Add(animation.Name);
        return names;
    }

    public static List<string> SkinNames(string mapName, string folderName)
    {
        var names = new List<string>();
        var data = GetSkeleton(mapName, folderName);
        var skeletonData = data != null ? data.GetSkeletonData(true) : null;
        if (skeletonData == null) return names;

        foreach (var skin in skeletonData.Skins)
            if (skin != null) names.Add(skin.Name);
        return names;
    }

    public static void ForgetFailures() => Failed.Clear();
}
