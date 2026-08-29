using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

public static class VanillaChrome
{
    private const string PhotoOverlayKey = "Assets/UI/Menus/Photo Mode/Take Photo Overlay.prefab";

    private const string PlatePath = "Controls/Background";

    private static string SourceKey
    {
        get
        {
            var configured = Plugin.MapEditorPanelSource?.Value;
            return string.IsNullOrWhiteSpace(configured) ? PhotoOverlayKey : configured.Trim();
        }
    }

    private static readonly Color FallbackTint = new(0f, 0f, 0f, 0.62f);
    private const float FallbackPixelsPerUnit = 1.6f;

    public static Sprite Plate { get; private set; }
    public static Color PlateTint { get; private set; } = Color.white;
    public static float PlatePixelsPerUnit { get; private set; } = 1f;
    public static Image.Type PlateDraw { get; private set; } = Image.Type.Sliced;

    public static bool Ready => Plate != null;

    public static Color Tint => Ready ? PlateTint : FallbackTint;

    private static bool _requested;
    private static bool _reported;

    private static readonly List<Image> _dressed = [];

    public static void Dress(Image image)
    {
        if (image == null) return;

        if (!_dressed.Contains(image)) _dressed.Add(image);
        Apply(image);
        Prime();
    }

    private static void Apply(Image image)
    {
        if (Ready)
        {
            image.type = PlateDraw;
            image.sprite = Plate;
            image.pixelsPerUnitMultiplier = PlatePixelsPerUnit;
            image.color = PlateTint;
            return;
        }

        image.type = Image.Type.Sliced;
        image.sprite = MapEditorUI.RoundedPlate;
        image.pixelsPerUnitMultiplier = FallbackPixelsPerUnit;
        image.color = FallbackTint;
    }

    // ---- loading --------------------------------------------------------------------------------

    public static void Prime()
    {
        if (_requested || Ready) return;
        if (Plugin.MapEditorVanillaPanelArt is { Value: false }) return;

        _requested = true;

        AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.LoadAssetAsync<GameObject>(SourceKey);
        }
        catch (Exception e)
        {
            Plugin.Log.LogInfo("Map editor: the game's panel art could not be requested (" +
                               e.Message + "); the editor keeps its own.");
            return;
        }

        handle.Completed += op =>
        {
            try
            {
                if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                {
                    Plugin.Log.LogInfo("Map editor: the game's panel art did not load; the editor " +
                                       "keeps its own.");
                    return;
                }

                Adopt(op.Result);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Map editor: the game's panel art could not be read: " + e.Message);
            }
        };
    }

    private static void Adopt(GameObject prefab)
    {
        var chosen = Choose(prefab);
        if (chosen == null) return;

        Plate = Trim(chosen.sprite);
        PlatePixelsPerUnit = chosen.pixelsPerUnitMultiplier > 0f ? chosen.pixelsPerUnitMultiplier : 1f;
        PlateDraw = chosen.type;

        var tint = chosen.color;
        var alpha = tint.a < 0.05f ? 1f : tint.a;
        PlateTint = new Color(tint.r, tint.g, tint.b, alpha * Opacity);

        Plate.hideFlags |= HideFlags.DontUnloadUnusedAsset;

        Plugin.Log.LogInfo($"Map editor: panel art '{Plate.name}' taken from '{SourceKey}' " +
                           $"(from '{chosen.name}'), drawn {PlateDraw} at alpha {PlateTint.a:0.00}.");

        Redress();
    }

    private static float Opacity =>
        Plugin.MapEditorPanelOpacity != null
            ? Mathf.Clamp01(Plugin.MapEditorPanelOpacity.Value)
            : 1f;

    // ---- the art's own padding ------------------------------------------------------------------

    private static Sprite Trim(Sprite sprite)
    {
        if (sprite == null) return null;

        var texture = sprite.texture;
        if (texture == null) return sprite;

        var authored = sprite.rect;
        var art = sprite.textureRect;
        var offset = sprite.textureRectOffset;

        Plugin.Log.LogInfo($"Map editor: '{sprite.name}' is {authored.width:0}x{authored.height:0} " +
                           $"as authored, {art.width:0}x{art.height:0} of art at " +
                           $"{offset.x:0}/{offset.y:0} (packed: {sprite.packed}).");

        var crop = Crop();
        var rect = new Rect(art.x + crop.x, art.y + crop.y,
            art.width - crop.x - crop.z, art.height - crop.y - crop.w);

        if (rect.width < 1f || rect.height < 1f) return sprite;
        if (rect == art && Mathf.Approximately(art.width, authored.width) &&
            Mathf.Approximately(art.height, authored.height)) return sprite;

        Sprite cut;
        try
        {
            cut = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit, 0,
                SpriteMeshType.FullRect, sprite.border);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Map editor: the panel art could not be re-cut to its own " +
                                  "padding (" + e.Message + "); it is used as it comes.");
            return sprite;
        }

        cut.name = sprite.name;
        return cut;
    }

    private static Vector4 Crop()
    {
        var configured = Plugin.MapEditorPanelCrop?.Value;
        if (string.IsNullOrWhiteSpace(configured)) return Vector4.zero;

        var parts = configured.Split(',');
        if (parts.Length != 4)
        {
            Plugin.Log.LogWarning($"Map editor: PanelCrop '{configured}' is not four numbers " +
                                  "(left,bottom,right,top); it was ignored.");
            return Vector4.zero;
        }

        var crop = Vector4.zero;
        for (var i = 0; i < 4; i++)
        {
            if (float.TryParse(parts[i].Trim(), out var value)) { crop[i] = value; continue; }
            Plugin.Log.LogWarning($"Map editor: PanelCrop '{configured}' is not four numbers " +
                                  "(left,bottom,right,top); it was ignored.");
            return Vector4.zero;
        }

        return crop;
    }

    private static void Redress()
    {
        for (var i = _dressed.Count - 1; i >= 0; i--)
        {
            var image = _dressed[i];
            if (image == null) { _dressed.RemoveAt(i); continue; }
            Apply(image);
        }
    }

    // ---- picking a plate ------------------------------------------------------------------------

    private static Image Choose(GameObject prefab)
    {
        var images = prefab.GetComponentsInChildren<Image>(true);
        Report(prefab, images);

        var configured = Plugin.MapEditorPanelPlate?.Value;
        var wanted = string.IsNullOrWhiteSpace(configured) ? PlatePath : configured.Trim();

        Image best = null;
        var bestArea = 0f;
        var bestNamed = false;

        foreach (var image in images)
        {
            var sprite = image.sprite;
            if (sprite == null) continue;

            var rect = image.rectTransform.rect;
            var area = Mathf.Abs(rect.width * rect.height);

            if (Same(PathOf(image.transform, prefab.transform), wanted) ||
                Same(sprite.name, wanted) || Same(image.name, wanted))
            {
                if (best != null && bestNamed && area <= bestArea) continue;
                best = image;
                bestNamed = true;
                bestArea = area;
                continue;
            }

            if (bestNamed) continue;

            var border = sprite.border;
            if (border.x <= 0f && border.y <= 0f && border.z <= 0f && border.w <= 0f) continue;

            if (rect.width < 1f || rect.height < 1f) continue;
            if (border.x + border.z >= rect.width || border.y + border.w >= rect.height) continue;

            if (area <= bestArea) continue;
            best = image;
            bestArea = area;
        }

        if (best == null)
            Plugin.Log.LogWarning($"Map editor: nothing named '{wanted}' in '{SourceKey}', and no " +
                                  "panel art to fall back on; the editor keeps its own.");
        else if (!bestNamed)
            Plugin.Log.LogWarning($"Map editor: nothing named '{wanted}' in '{SourceKey}'; the " +
                                  "largest nine-sliced plate was taken instead.");

        return best;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void Report(GameObject prefab, Image[] images)
    {
        if (_reported) return;
        _reported = true;

        var lines = new List<string>();
        foreach (var image in images)
        {
            var sprite = image.sprite;
            var rect = image.rectTransform.rect;
            var border = sprite != null ? sprite.border : Vector4.zero;
            var colour = image.color;

            lines.Add($"  {PathOf(image.transform, prefab.transform)}  sprite " +
                      $"'{(sprite != null ? sprite.name : "<none>")}'  {rect.width:0}x{rect.height:0}  " +
                      $"border {border.x:0}/{border.y:0}/{border.z:0}/{border.w:0}  " +
                      $"rgba {colour.r:0.00}/{colour.g:0.00}/{colour.b:0.00}/{colour.a:0.00}  " +
                      $"{image.type}{(image.gameObject.activeSelf ? "" : "  (off)")}");
        }

        Plugin.Log.LogInfo(lines.Count == 0
            ? $"Map editor: '{SourceKey}' draws nothing this could borrow."
            : $"Map editor: what '{SourceKey}' draws, for the MapEditor/PanelPlate setting -\n" +
              string.Join("\n", lines));
    }

    private static string PathOf(Transform node, Transform root)
    {
        var name = node.name;
        for (var parent = node.parent; parent != null && parent != root; parent = parent.parent)
            name = parent.name + "/" + name;
        return name;
    }
}
