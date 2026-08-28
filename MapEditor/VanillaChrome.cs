using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

// The editor's panels wearing the game's own panel art.
//
// Photo mode's overlay is built from a nine-sliced plate the rest of the game reuses, and it is a
// far better backing than the rounded rectangle the mod draws for itself. It is not in the scene -
// photo mode loads its UI on demand and unloads it again - so it is fetched from the same
// addressable the game fetches it from, and the handle is kept for the session.
//
// Nothing here is required: until the load lands (and if it never does) every panel keeps the
// generated plate, and a panel already on screen when it lands is re-dressed in place.
public static class VanillaChrome
{
    // The address photo mode's own loader asks for; see UIManager.LoadPhotomodeAssets. Its
    // neighbours - "Edit Photo Overlay.prefab", "Photo Gallery Menu.prefab" - are worth a look if
    // the plate wanted is not in this one.
    private const string PhotoOverlayKey = "Assets/UI/Menus/Photo Mode/Take Photo Overlay.prefab";

    // The plate itself, by path from that prefab's root. Named rather than deduced: "Background"
    // on its own is also what both sliders call theirs, and the shape of the art is not enough to
    // tell a control's backing from the panel's.
    private const string PlatePath = "Controls/Background";

    private static string SourceKey
    {
        get
        {
            var configured = Plugin.MapEditorPanelSource?.Value;
            return string.IsNullOrWhiteSpace(configured) ? PhotoOverlayKey : configured.Trim();
        }
    }

    // What a panel wears while the game's art is not available.
    private static readonly Color FallbackTint = new(0f, 0f, 0f, 0.62f);
    private const float FallbackPixelsPerUnit = 1.6f;

    public static Sprite Plate { get; private set; }
    public static Color PlateTint { get; private set; } = Color.white;
    public static float PlatePixelsPerUnit { get; private set; } = 1f;
    public static Image.Type PlateDraw { get; private set; } = Image.Type.Sliced;

    public static bool Ready => Plate != null;

    // The colour a panel should be, whichever plate is in use. The status bar reads this rather
    // than blackening itself: on the game's art the accent outline already carries urgency, and
    // washing the plate out would throw its design away.
    public static Color Tint => Ready ? PlateTint : FallbackTint;

    private static bool _requested;
    private static bool _reported;

    // Panels dressed so far, so a late arrival can reach the ones already built. Editor sessions
    // come and go and take their canvas with them, so destroyed entries are dropped on each pass.
    private static readonly List<Image> _dressed = [];

    // Dress a panel background and keep it on the list. Safe to call before the art exists.
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
            // Slicing a border-less sprite only stretches it, which is what the game does with
            // this one anyway; the source's own draw mode is carried over rather than assumed.
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

        // Never released: releasing it unloads the sprite the panels are drawing.
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

        // As authored, except for art shipped invisible and faded in by a tween at runtime: a
        // plate at alpha 0 is not a plate. A translucent one is a design and is left alone.
        var tint = chosen.color;
        var alpha = tint.a < 0.05f ? 1f : tint.a;
        PlateTint = new Color(tint.r, tint.g, tint.b, alpha * Opacity);

        // The atlas the sprite lives in is held by our handle; the sprite itself must not be swept
        // up by a resource unload between editor sessions.
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

    // "Rough_WhiteSquare_bottomaligned" says what it is: the shape sits at the bottom of its rect
    // with empty space above, so a plate stretched to a panel is drawn short of the panel's edges.
    //
    // The atlas packer records that padding - textureRect is where the art actually is, rect is the
    // authored size around it - so the fix is to re-cut the sprite to the art and let it stretch to
    // the whole panel. On art the packer left untrimmed the two agree and this changes nothing,
    // which is what the config crop is for.
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

        // Nothing to re-cut, or a crop that would eat the whole plate.
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

    // left, bottom, right, top, in the art's own pixels.
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

    // The plate is asked for by path - PlatePath, or whatever the config names instead - and only
    // deduced from the art's shape when nothing answers to that, which is what a game update moving
    // it would look like. Everything the prefab draws is written to the log the first time round, so
    // a pick that goes wrong can be named rather than guessed at again.
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

            // Path first, so one plate among several sharing a name can still be asked for; then
            // the looser two, which are what a sprite is easiest to recognise by in the dump.
            if (Same(PathOf(image.transform, prefab.transform), wanted) ||
                Same(sprite.name, wanted) || Same(image.name, wanted))
            {
                // A named pick wins outright, whatever shape it is; the largest match settles a
                // name used more than once.
                if (best != null && bestNamed && area <= bestArea) continue;
                best = image;
                bestNamed = true;
                bestArea = area;
                continue;
            }

            if (bestNamed) continue;

            // Nothing named matched yet, so the shape has to say it: a panel background is
            // nine-sliced - the borders are what let one piece of art stretch to any size - and
            // decoration is not. This is the fallback for a game update moving the plate.
            var border = sprite.border;
            if (border.x <= 0f && border.y <= 0f && border.z <= 0f && border.w <= 0f) continue;

            // A frame drawn round the whole screen is not a plate, and neither is a rect the
            // prefab leaves for a layout to fill in.
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

    // Everything the source prefab draws, once, so a plate can be named in the config without
    // having to guess at it. Names are paths from the prefab root, and that is what to name.
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
