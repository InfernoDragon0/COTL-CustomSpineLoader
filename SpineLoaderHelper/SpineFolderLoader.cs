using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COTL_API.Helpers;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

// The one recipe for "a folder of Spine exports on disk" -> SkeletonDataAsset, shared by every
// loader that ships spine art next to a config.json (NPCs, structures). It is the FollowerSpines
// recipe the mod has always used: text assets read straight off disk, textures NAMED after their
// file (the atlas resolves its pages by name - without this the skeleton renders blank), the
// Spine/Skeleton shader, and the game's 0.005 import scale.
public static class SpineFolderLoader
{
    public const float DefaultScale = 0.005f;
    public const string DefaultShader = "Spine/Skeleton";

    // Null when the folder ships no spine - that is a legitimate answer (a config that only
    // renames things), so the caller decides whether to complain.
    public static SkeletonDataAsset Build(string folder, string owner, string skeletonPath = null,
        string atlasPath = null, IList<string> texturePaths = null, float scale = DefaultScale,
        string shaderName = DefaultShader, IEnumerable<string> excludedTextures = null)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        // Everything not explicitly pointed at is discovered: the skeleton is the one .json that
        // is not the config, the atlas is the one .atlas, the pages are the .png files that are
        // not claimed for something else (a structure's build-menu icon, say).
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
            textures[i] = texture;
        }

        var shader = Shader.Find(string.IsNullOrEmpty(shaderName) ? DefaultShader : shaderName);
        if (shader == null)
        {
            Plugin.Log.LogWarning($"Spine for '{owner}': shader '{shaderName}' was not found, " +
                                  $"falling back to {DefaultShader}.");
            shader = Shader.Find(DefaultShader);
        }

        var material = new Material(shader);
        var atlas = SpineAtlasAsset.CreateRuntimeInstance(atlasText, textures, material, true);
        return SkeletonDataAsset.CreateRuntimeInstance(skeletonText, atlas,
            true, scale > 0f ? scale : DefaultScale);
    }
}
