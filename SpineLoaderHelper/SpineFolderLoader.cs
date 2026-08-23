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

    // Shader.Find can return null (a build that stripped the shader), and new Material(null)
    // throws - inside Plugin.Awake that aborts the whole mod load. Found once, cached, and
    // fallen back to a shader every build ships rather than left to crash.
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

            // These pages are built from disk and have no asset backing to be reloaded from, so
            // an UnloadUnusedAssets sweep that decides nothing references them cannot be undone -
            // the skeleton wearing them is left pointing at freed memory. Room changes run that
            // sweep, and it is what the crash-time asset collector thread walks.
            Keep(texture);

            // These skeletons (structures, NPCs, enemies, world maps) render directly and are
            // never repacked, so the decoded CPU copy is dead weight.
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

        // initialize:true above parsed the skeleton, so the JSON TextAsset is a dead copy of
        // the file from here on - GetSkeletonData answers from the cached data.
        if (data.skeletonData != null)
        {
            data.skeletonJSON = PlaceholderJson();
            UnityEngine.Object.Destroy(skeletonText);
            MarkJsonFreed(data);
        }
        return data;
    }

    // Everything above is a runtime asset with no file behind it: once unloaded it is gone for
    // the session, so it is marked as not-unloadable rather than left to the asset sweep's
    // judgement about whether anything still points at it.
    public static void Keep(UnityEngine.Object asset)
    {
        if (asset != null) asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
    }

    // Assets whose JSON TextAsset has been freed after parsing. The game occasionally calls
    // Clear() + GetSkeletonData() on an asset to force a re-parse (the entrance shrine's statue
    // does it to refresh the player dummy's look); on these assets there is no JSON left to
    // re-parse from, so Clear() is skipped for them instead - the cached data IS the file.
    private static readonly HashSet<SkeletonDataAsset> JsonFreed = [];

    public static void MarkJsonFreed(SkeletonDataAsset asset)
    {
        if (asset != null) JsonFreed.Add(asset);
    }

    public static bool IsJsonFreed(SkeletonDataAsset asset) =>
        asset != null && JsonFreed.Contains(asset);

    // GetSkeletonData refuses to answer at all - cached data or not - when skeletonJSON is
    // null, so a freed asset keeps this stand-in instead. It is never parsed: the cached data
    // short-circuits the read, and Clear() (the only thing that could wipe the cache) is
    // skipped for freed assets.
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

    // Frees the CPU half of a texture that only the GPU will ever sample again - LoadImage
    // keeps every decoded PNG readable, which doubles its memory for nothing once it is on an
    // atlas. NOT for anything the skin repacker may still read: player and follower spines go
    // through the game's dressing pipeline, which pulls pixels back off their pages.
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
