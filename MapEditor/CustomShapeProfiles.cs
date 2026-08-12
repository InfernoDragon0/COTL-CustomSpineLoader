using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.Helpers;
using CustomSpineLoader.APIHelper;
using UnityEngine;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor;

// Builds SpriteShape profiles from disk: CustomShapeProfiles/<Folder>/config.json plus the PNGs
// it references. A profile named "Dirt" becomes the runtime asset "CultTweaker_Dirt", so custom
// names can never collide with vanilla profiles, and blueprints reference it by exactly that
// name (MapShapeData.Profile).
//
// Minimal config is fill-only: {"Name":"Dirt","FillTexture":"dirt.png"} renders the interior
// texture with no edge art. AngleRanges add edge sprites per outline angle, Corners add corner
// sprites - both optional.
[Serializable]
public class ShapeProfileConfig
{
    public string Name = "Unnamed";
    public string FillTexture = "";
    public bool UseSpriteBorders = true;
    public List<ShapeProfileAngleRange> AngleRanges = [];
    public List<ShapeProfileCorner> Corners = [];
}

[Serializable]
public class ShapeProfileAngleRange
{
    public float Start = -180f;
    public float End = 180f;
    public int Order;
    public List<ShapeProfileSprite> Sprites = [];
}

[Serializable]
public class ShapeProfileSprite
{
    public string Texture = "";
    public float PixelsPerUnit = 100f;
    // 9-slice borders in pixels, so edge strips stretch their middle rather than their caps.
    public float BorderLeft;
    public float BorderBottom;
    public float BorderRight;
    public float BorderTop;
}

[Serializable]
public class ShapeProfileCorner
{
    // A UnityEngine.U2D.CornerType name: OuterTopLeft, OuterTopRight, OuterBottomLeft,
    // OuterBottomRight, InnerTopLeft, InnerTopRight, InnerBottomLeft, InnerBottomRight.
    public string Corner = "OuterTopLeft";
    public string Texture = "";
    public float PixelsPerUnit = 100f;
}

public static class CustomShapeProfiles
{
    public const string FolderName = "CustomShapeProfiles";
    public const string NamePrefix = "CultTweaker_";

    private static List<SpriteShape> _profiles;

    public static IReadOnlyList<SpriteShape> All => LoadAll();

    private static List<SpriteShape> LoadAll()
    {
        if (_profiles != null) return _profiles;
        _profiles = [];

        foreach (var result in new Loader<ShapeProfileConfig>(FolderName).LoadAll())
        {
            try
            {
                var shape = Build(result.Config, result.FolderPath);
                if (shape != null) _profiles.Add(shape);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MapEditor: shape profile '{result.FolderName}' failed to build: {e}");
            }
        }

        if (_profiles.Count > 0)
            Plugin.Log.LogInfo($"MapEditor: built {_profiles.Count} custom shape profile(s).");
        return _profiles;
    }

    private static SpriteShape Build(ShapeProfileConfig config, string folder)
    {
        var shape = ScriptableObject.CreateInstance<SpriteShape>();
        shape.name = NamePrefix + MapEditorSerialization.Sanitize(config.Name);

        // The game runs Resources.UnloadUnusedAssets on every room change; without this flag the
        // runtime-built asset would be collected mid-session and shapes using it would go blank.
        shape.hideFlags = HideFlags.DontUnloadUnusedAsset;
        shape.useSpriteBorders = config.UseSpriteBorders;

        if (!string.IsNullOrEmpty(config.FillTexture))
        {
            var fill = LoadTexture(folder, config.FillTexture);
            if (fill != null)
            {
                fill.wrapMode = TextureWrapMode.Repeat;
                shape.fillTexture = fill;
            }
        }

        foreach (var rangeConfig in config.AngleRanges)
        {
            var range = new AngleRange
            {
                start = rangeConfig.Start,
                end = rangeConfig.End,
                order = rangeConfig.Order
            };

            foreach (var spriteConfig in rangeConfig.Sprites)
            {
                var sprite = MakeSprite(folder, spriteConfig.Texture, spriteConfig.PixelsPerUnit,
                    new Vector4(spriteConfig.BorderLeft, spriteConfig.BorderBottom,
                        spriteConfig.BorderRight, spriteConfig.BorderTop));
                if (sprite != null) range.sprites.Add(sprite);
            }

            shape.angleRanges.Add(range);
        }

        foreach (var cornerConfig in config.Corners)
        {
            if (!Enum.TryParse<CornerType>(cornerConfig.Corner, out var cornerType))
            {
                Plugin.Log.LogWarning($"MapEditor: unknown corner type '{cornerConfig.Corner}' in profile '{config.Name}'.");
                continue;
            }

            var sprite = MakeSprite(folder, cornerConfig.Texture, cornerConfig.PixelsPerUnit, Vector4.zero);
            if (sprite == null) continue;

            var corner = new CornerSprite { cornerType = cornerType };
            corner.sprites.Add(sprite);
            shape.cornerSprites.Add(corner);
        }

        Plugin.Log.LogInfo($"MapEditor: shape profile '{shape.name}' built " +
                           $"(fill: {(shape.fillTexture != null ? "yes" : "no")}, " +
                           $"{shape.angleRanges.Count} angle range(s), {shape.cornerSprites.Count} corner(s)).");
        return shape;
    }

    private static Texture2D LoadTexture(string folder, string file)
    {
        var path = Path.Combine(folder, file);
        if (!File.Exists(path))
        {
            Plugin.Log.LogWarning($"MapEditor: shape profile texture missing: {path}");
            return null;
        }

        var texture = TextureHelper.CreateTextureFromPath(path);
        if (texture != null) texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return texture;
    }

    private static Sprite MakeSprite(string folder, string file, float pixelsPerUnit, Vector4 border)
    {
        if (string.IsNullOrEmpty(file)) return null;

        var texture = LoadTexture(folder, file);
        if (texture == null) return null;

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), pixelsPerUnit <= 0f ? 100f : pixelsPerUnit, 0,
            SpriteMeshType.FullRect, border);
        sprite.name = Path.GetFileNameWithoutExtension(file);
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }
}
