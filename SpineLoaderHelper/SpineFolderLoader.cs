using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COTL_API.Helpers;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

public static class SpineFolderLoader
{
    public const float DefaultScale = 0.005f;
    public const string DefaultShader = "Spine/Skeleton";

    private static Shader _spineShader;

    public static Shader SpineShader()
    {
        if (_spineShader != null) return _spineShader;

        _spineShader = Shader.Find(DefaultShader);
        if (_spineShader == null)
        {
            Plugin.Log.LogWarning($"Shader '{DefaultShader}' was not found; spine materials fall " +
                                  "back to Sprites/Default and will render flat.");
            _spineShader = Shader.Find("Sprites/Default");
        }
        return _spineShader;
    }

    public static SkeletonDataAsset Build(string folder, string owner, string skeletonPath = null,
        string atlasPath = null, IList<string> texturePaths = null, float scale = DefaultScale,
        string shaderName = DefaultShader, IEnumerable<string> excludedTextures = null)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (excludedTextures != null)
            foreach (var file in excludedTextures)
                if (!string.IsNullOrEmpty(file)) skipped.Add(Path.GetFullPath(file));

        var skeletonFile = !string.IsNullOrEmpty(skeletonPath)
            ? Path.Combine(folder, skeletonPath)
            : Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => !Path.GetFileName(f).Equals("config.json", StringComparison.OrdinalIgnoreCase));

        var atlasFile = !string.IsNullOrEmpty(atlasPath)
            ? Path.Combine(folder, atlasPath)
            : Directory.GetFiles(folder, "*.atlas", SearchOption.TopDirectoryOnly).FirstOrDefault();

        var textureFiles = texturePaths is { Count: > 0 }
            ? texturePaths.Select(p => Path.Combine(folder, p)).ToArray()
            : Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly)
                .Where(f => !skipped.Contains(Path.GetFullPath(f))).ToArray();

        if (skeletonFile == null || atlasFile == null || textureFiles.Length == 0) return null;

        if (!File.Exists(skeletonFile) || !File.Exists(atlasFile))
        {
            Plugin.Log.LogError($"Spine for '{owner}': skeleton or atlas file is missing " +
                                $"({skeletonFile} / {atlasFile}).");
            return null;
        }

        var atlasText = new TextAsset(File.ReadAllText(atlasFile));
        var skeletonText = new TextAsset(File.ReadAllText(skeletonFile));

        var textures = new Texture2D[textureFiles.Length];
        for (var i = 0; i < textureFiles.Length; i++)
        {
            var texture = TextureHelper.CreateTextureFromPath(textureFiles[i]);
            texture.name = Path.GetFileNameWithoutExtension(textureFiles[i]);

            Keep(texture);

            Seal(texture);
            textures[i] = texture;
        }

        Keep(atlasText);
        Keep(skeletonText);

        var shader = Shader.Find(string.IsNullOrEmpty(shaderName) ? DefaultShader : shaderName);
        if (shader == null)
        {
            Plugin.Log.LogWarning($"Spine for '{owner}': shader '{shaderName}' was not found, " +
                                  $"falling back to {DefaultShader}.");
            shader = Shader.Find(DefaultShader);
        }

        var material = new Material(shader);
        Keep(material);

        var atlas = SpineAtlasAsset.CreateRuntimeInstance(atlasText, textures, material, true);
        Keep(atlas);

        var data = SkeletonDataAsset.CreateRuntimeInstance(skeletonText, atlas,
            true, scale > 0f ? scale : DefaultScale);
        Keep(data);

        if (data.skeletonData != null)
        {
            data.skeletonJSON = PlaceholderJson();
            UnityEngine.Object.Destroy(skeletonText);
            MarkJsonFreed(data);
        }
        return data;
    }

    public static void Keep(UnityEngine.Object asset)
    {
        if (asset != null) asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
    }

    private static readonly HashSet<SkeletonDataAsset> JsonFreed = [];

    public static void MarkJsonFreed(SkeletonDataAsset asset)
    {
        if (asset != null) JsonFreed.Add(asset);
    }

    public static bool IsJsonFreed(SkeletonDataAsset asset) =>
        asset != null && JsonFreed.Contains(asset);

    private static TextAsset _placeholderJson;

    public static TextAsset PlaceholderJson()
    {
        if (_placeholderJson == null)
        {
            _placeholderJson = new TextAsset("{}");
            Keep(_placeholderJson);
        }
        return _placeholderJson;
    }

    public static void Seal(Texture2D texture)
    {
        if (texture == null) return;
        try
        {
            texture.Apply(false, true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Texture '{texture.name}' could not be sealed: {e.Message}");
        }
    }
}
