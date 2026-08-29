using System;
using System.Collections.Generic;
using System.IO;
using Beffio.Dithering;
using COTL_API.Helpers;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.SpineLoaderHelper;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.ModUI.MenuEditor;

public static class MenuAssets
{
    private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SkeletonDataAsset> Skeletons = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);

    // ---- palettes -------------------------------------------------------------------------------

    private static readonly List<Palette> Palettes = [];

    public static List<Palette> AllPalettes()
    {
        Palettes.RemoveAll(p => p == null);
        if (Palettes.Count > 0) return Palettes;

        try
        {
            foreach (var palette in Resources.FindObjectsOfTypeAll<Palette>())
            {
                if (palette == null) continue;

                if (!palette.HasTexture || palette.Texture == null) continue;

                palette.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                Palettes.Add(palette);
            }

            Palettes.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Plugin.Log.LogInfo($"MenuEditor: {Palettes.Count} palette(s) available.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MenuEditor: could not list the game's palettes: " + e.Message);
        }

        return Palettes;
    }

    public static List<string> PaletteNames()
    {
        var names = new List<string>();
        foreach (var palette in AllPalettes()) names.Add(palette.name);
        return names;
    }

    public static Palette PaletteByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        foreach (var palette in AllPalettes())
            if (string.Equals(palette.name, name, StringComparison.OrdinalIgnoreCase))
                return palette;

        return null;
    }

    // ---- recolouring ----------------------------------------------------------------------------

    private static Palette _tinted;
    private static Texture2D _tintedTexture;
    private static Palette _tintedFrom;
    private static Color[] _sourcePixels;

    private static float _tintedHue = float.NaN;
    private static float _tintedSaturation;
    private static float _tintedBrightness;

    private static bool IsNeutral(float hueShift, float saturation, float brightness) =>
        Mathf.Approximately(hueShift, 0f) &&
        Mathf.Approximately(saturation, 1f) &&
        Mathf.Approximately(brightness, 1f);

    public static Palette Recoloured(Palette source, float hueShift, float saturation, float brightness)
    {
        if (source == null || source.Texture == null) return source;
        if (IsNeutral(hueShift, saturation, brightness)) return source;

        if (_tinted != null && _tintedTexture != null && _tintedFrom == source &&
            _tintedHue == hueShift && _tintedSaturation == saturation && _tintedBrightness == brightness)
            return _tinted;

        try
        {
            if (_tintedFrom != source || _sourcePixels == null)
            {
                _sourcePixels = ReadablePixels(source.Texture);
                _tintedFrom = source;

                if (_tintedTexture != null) UnityEngine.Object.Destroy(_tintedTexture);
                _tintedTexture = null;
            }

            if (_sourcePixels == null) return source;

            if (_tintedTexture == null)
            {
                _tintedTexture = new Texture2D(source.Texture.width, source.Texture.height,
                    TextureFormat.RGBA32, false)
                {
                    name = source.name + " (recoloured)",
                    filterMode = source.Texture.filterMode,
                    wrapMode = TextureWrapMode.Clamp
                };
                SpineFolderLoader.Keep(_tintedTexture);
            }

            var pixels = new Color[_sourcePixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var source_ = _sourcePixels[i];
                Color.RGBToHSV(source_, out var h, out var s, out var v);

                h = Mathf.Repeat(h + hueShift / 360f, 1f);
                s = Mathf.Clamp01(s * saturation);
                v = Mathf.Clamp01(v * brightness);

                var shifted = Color.HSVToRGB(h, s, v);
                pixels[i] = new Color(shifted.r, shifted.g, shifted.b, source_.a);
            }

            _tintedTexture.SetPixels(pixels);
            _tintedTexture.Apply(false);

            if (_tinted == null)
            {
                _tinted = ScriptableObject.CreateInstance<Palette>();
                _tinted.name = "CultTweaker Menu Palette";
                SpineFolderLoader.Keep(_tinted);
            }

            _tinted.Texture = _tintedTexture;
            _tinted.MixedColorCount = source.MixedColorCount;
            _tinted.Colors = source.Colors;
            _tinted.HasTexture = true;

            _tintedHue = hueShift;
            _tintedSaturation = saturation;
            _tintedBrightness = brightness;
            return _tinted;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MenuEditor: the palette could not be recoloured: " + e.Message);
            return source;
        }
    }

    private static Color[] ReadablePixels(Texture2D texture)
    {
        var previous = RenderTexture.active;
        var buffer = RenderTexture.GetTemporary(texture.width, texture.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

        try
        {
            Graphics.Blit(texture, buffer);
            RenderTexture.active = buffer;

            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
            copy.Apply(false);

            var pixels = copy.GetPixels();
            UnityEngine.Object.Destroy(copy);
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(buffer);
        }
    }

    // ---- preset art -----------------------------------------------------------------------------

    public static Sprite GetSprite(string presetName, string fileName)
    {
        if (string.IsNullOrWhiteSpace(presetName) || string.IsNullOrWhiteSpace(fileName)) return null;

        var key = presetName + "/" + fileName;
        if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
        if (Failed.Contains(key)) return null;

        var path = Path.Combine(CTMenuPresetSerialization.FolderForRead(presetName), fileName);
        if (!File.Exists(path))
        {
            Plugin.Log.LogWarning($"MenuEditor: preset '{presetName}' has no image '{fileName}'.");
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
            Plugin.Log.LogError($"MenuEditor: preset '{presetName}' image '{fileName}' failed to " +
                                $"load: {e.Message}");
            Failed.Add(key);
            return null;
        }
    }

    public static SkeletonDataAsset GetSkeleton(string presetName, string folderName)
    {
        if (string.IsNullOrWhiteSpace(presetName) || string.IsNullOrWhiteSpace(folderName)) return null;

        var key = presetName + "/" + folderName;
        if (Skeletons.TryGetValue(key, out var cached) && cached != null) return cached;
        if (Failed.Contains(key)) return null;

        var folder = Path.Combine(CTMenuPresetSerialization.FolderForRead(presetName), folderName);
        var data = SpineFolderLoader.Build(folder, "mainmenu:" + presetName);
        if (data == null)
        {
            Plugin.Log.LogWarning($"MenuEditor: preset '{presetName}' spine folder '{folderName}' " +
                                  "has no usable skeleton (.json + .atlas + pages).");
            Failed.Add(key);
            return null;
        }

        Skeletons[key] = data;
        return data;
    }

    public static List<string> ImageNames(string presetName)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(presetName)) return names;

        try
        {
            var folder = CTMenuPresetSerialization.FolderForRead(presetName);
            if (!Directory.Exists(folder)) return names;

            foreach (var file in Directory.GetFiles(folder, "*.png"))
                names.Add(Path.GetFileName(file));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MenuEditor: could not list the images in '{presetName}': {e.Message}");
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static List<string> SpineFolderNames(string presetName)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(presetName)) return names;

        try
        {
            var folder = CTMenuPresetSerialization.FolderForRead(presetName);
            if (!Directory.Exists(folder)) return names;

            foreach (var sub in Directory.GetDirectories(folder))
                if (Directory.GetFiles(sub, "*.atlas").Length > 0)
                    names.Add(Path.GetFileName(sub));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MenuEditor: could not list the spines in '{presetName}': {e.Message}");
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static void Forget()
    {
        Sprites.Clear();
        Skeletons.Clear();
        Failed.Clear();
    }
}
