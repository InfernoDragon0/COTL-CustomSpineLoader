using System;
using System.Collections.Generic;
using COTL_API.UI.Helpers;
using Lamb.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

public class MapEditorUI
{
    public const float RowHeight = 34f;

    // ---- palette ----------------------------------------------------------------------------

    public static readonly Color PlateIdle = new(0.09f, 0.08f, 0.07f, 0.96f);
    public static readonly Color PlateHover = new(0.26f, 0.23f, 0.19f, 1f);
    public static readonly Color FieldIdle = new(0.05f, 0.05f, 0.04f, 0.96f);
    public static readonly Color FieldHover = new(0.16f, 0.15f, 0.13f, 1f);

    public static readonly Color Accent = new(0.83f, 0.24f, 0.20f, 1f);

    public static readonly Color SliderFill = new(0.55f, 0.78f, 0.25f, 1f);

    public static readonly Color TrackColour = new(0.05f, 0.05f, 0.04f, 0.95f);

    /// Header plate for foldable sections. Nearly opaque on purpose: a light tint over the dressed
    /// panel let the room show through and the header stopped reading as a separate bar.
    public static readonly Color SectionIdle = new(0.09f, 0.08f, 0.07f, 0.92f);
    public static readonly Color SectionHover = new(0.24f, 0.21f, 0.18f, 0.96f);

    public static readonly Color MutedText = new(1f, 1f, 1f, 0.5f);
    public static readonly Color AmberAccent = new(0.94f, 0.70f, 0.23f, 1f);

    public const float SectionHeaderHeight = 36f;

    public static Color StatusColour(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Success => new Color(0.55f, 0.9f, 0.55f),
        StatusSeverity.Warning => new Color(1f, 0.76f, 0.3f),
        StatusSeverity.Error => new Color(1f, 0.42f, 0.42f),
        _ => Color.white
    };

    private IMapEditorHost _editor;
    private RectTransform _canvasRoot;

    public RectTransform CanvasRoot => _canvasRoot;
    public IMapEditorHost Editor => _editor;

    private static IMapEditorHost _host;

    internal static void Rehost(IMapEditorHost host)
    {
        if (host != null) _host = host;
    }

    internal static IMapEditorHost CurrentHost
    {
        get
        {
            if (_host is MonoBehaviour behaviour) return behaviour == null ? null : _host;
            return _host;
        }
    }

    public void Attach(IMapEditorHost editor, RectTransform canvasRoot)
    {
        _editor = editor;
        _canvasRoot = canvasRoot;
        if (editor != null) _host = editor;
        WarmFonts();
    }

    // ---- rounded plate ----------------------------------------------------------------------

    private static Sprite _rounded;

    public static Sprite RoundedPlate
    {
        get
        {
            if (_rounded != null) return _rounded;

            const int size = 48;
            const float radius = 12f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                    var dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            _rounded = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            _rounded.name = "CultTweaker_RoundedPlate";
            _rounded.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _rounded;
        }
    }

    // ---- handle ring -------------------------------------------------------------------------

    private static Sprite _handleRing;

    /// The editor's drag nodes: a hard ring with a faint disc inside it. White throughout, and the
    /// alpha carries the shape, so one `Image.color` tints ring and fill together and every node
    /// gets its own colour from one sprite. Never sliced - a circle cannot be stretched in nine parts.
    public static Sprite HandleRing
    {
        get
        {
            if (_handleRing != null) return _handleRing;

            const int size = 64;
            // Thick enough that the smallest node (a shape point, 27px on screen) still draws a
            // ring rather than a hairline; the sprite scales with the rect, so this is a ratio.
            const float ringWidth = 7f;
            const float fillAlpha = 0.28f;

            var centre = size * 0.5f;
            var outer = centre - 1.5f;
            var inner = outer - ringWidth;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x + 0.5f - centre;
                    var dy = y + 0.5f - centre;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Both edges of the ring feather over one pixel, and the disc is what is left
                    // inside it, so the two never double up into a bright seam.
                    var ring = Mathf.Clamp01(outer - distance + 0.5f) *
                               Mathf.Clamp01(distance - inner + 0.5f);
                    var fill = Mathf.Clamp01(inner - distance + 0.5f) * fillAlpha;

                    var alpha = Mathf.Clamp01(Mathf.Max(ring, fill));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            _handleRing = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            _handleRing.name = "CultTweaker_HandleRing";
            _handleRing.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _handleRing;
        }
    }

    /// Dresses an editor drag node. `size` is the size the node used to be; the ring is drawn a
    /// little larger because an outline is a smaller target than a filled square of the same width.
    public static Image DressHandle(GameObject go, Color colour, float size)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size) * HandleGrowth;

        var image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        image.sprite = HandleRing;
        image.type = Image.Type.Simple;
        image.color = colour;
        return image;
    }

    public const float HandleGrowth = 1.35f;

    // ---- favourite star ----------------------------------------------------------------------

    private static Sprite _star;

    /// The badge a pinned structure wears. White throughout with the shape in the alpha, like the
    /// drag rings, so one sprite serves the gold fill and the dark rim behind it.
    public static Sprite Star
    {
        get
        {
            if (_star != null) return _star;

            const int size = 32;
            const int samples = 3;

            var centre = size * 0.5f;
            var outer = centre - 1f;
            var inner = outer * 0.44f;

            // Ten points around the circle, alternating long and short, starting at the top so the
            // star stands upright.
            var corners = new Vector2[10];
            for (var i = 0; i < 10; i++)
            {
                var radius = (i & 1) == 0 ? outer : inner;
                var angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                corners[i] = new Vector2(centre + Mathf.Cos(angle) * radius,
                    centre + Mathf.Sin(angle) * radius);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // A star has five spikes a single sample per pixel turns into gravel, so each
                    // pixel is judged by a grid of sub-samples and the count becomes its alpha.
                    var hits = 0;
                    for (var sy = 0; sy < samples; sy++)
                    {
                        for (var sx = 0; sx < samples; sx++)
                        {
                            var px = x + (sx + 0.5f) / samples;
                            var py = y + (sy + 0.5f) / samples;
                            if (Inside(corners, px, py)) hits++;
                        }
                    }

                    var alpha = hits / (float)(samples * samples);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            _star = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            _star.name = "CultTweaker_Star";
            _star.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _star;
        }
    }

    /// Crossing count: a ray to the right of the point crosses an odd number of edges when the point
    /// is inside. A star is not convex, so nothing simpler will do.
    private static bool Inside(Vector2[] corners, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var a = corners[i];
            var b = corners[j];
            if (a.y > y == b.y > y) continue;
            if (x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    /// A small star pinned to the top-right corner of a cell, dark rim behind gold so it reads on a
    /// pale icon as well as a dark plate.
    public static GameObject AddStarBadge(Transform parent, float size = 15f)
    {
        var badge = new GameObject("Star");
        badge.transform.SetParent(parent, false);

        var rt = badge.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-2f, -2f);
        rt.sizeDelta = new Vector2(size, size);

        var rim = badge.AddComponent<Image>();
        rim.sprite = Star;
        rim.color = new Color(0f, 0f, 0f, 0.7f);
        rim.raycastTarget = false;

        var fill = new GameObject("Fill");
        fill.transform.SetParent(badge.transform, false);
        var fillRt = fill.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(1.5f, 1.5f);
        fillRt.offsetMax = new Vector2(-1.5f, -1.5f);

        var star = fill.AddComponent<Image>();
        star.sprite = Star;
        star.color = new Color(1f, 0.82f, 0.26f, 1f);
        star.raycastTarget = false;

        return badge;
    }

    /// Right mouse button on an element that already has a Button on it.
    internal static MapEditorRightClick AttachRightClick(GameObject go, Action onRightClick)
    {
        var handler = go.GetComponent<MapEditorRightClick>() ?? go.AddComponent<MapEditorRightClick>();
        handler.OnRightClick = onRightClick;
        return handler;
    }

    private static Sprite _outline;

    public static Sprite RoundedOutline
    {
        get
        {
            if (_outline != null) return _outline;

            const int size = 48;
            const float radius = 12f;
            const float thickness = 4f;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                    var dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                    var corner = Mathf.Sqrt(dx * dx + dy * dy) - radius;

                    var edge = Mathf.Min(
                        Mathf.Min(x + 0.5f, size - (x + 0.5f)),
                        Mathf.Min(y + 0.5f, size - (y + 0.5f)));
                    var distance = dx > 0f || dy > 0f ? corner : -edge;

                    var alpha = Mathf.Clamp01(-distance + 0.5f) * Mathf.Clamp01(distance + thickness + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            _outline = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            _outline.name = "CultTweaker_RoundedOutline";
            _outline.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _outline;
        }
    }

    public static Image AddOutline(RectTransform parent, Color colour, float inset = 0f)
    {
        var go = new GameObject("Outline");
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-inset, -inset);
        rt.offsetMax = new Vector2(inset, inset);

        var image = go.AddComponent<Image>();
        image.sprite = RoundedOutline;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1.5f;
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    private static readonly Color RibbonIdle = new(0.20f, 0.17f, 0.16f, 0.85f);
    private static readonly Color RibbonHover = new(0.55f, 0.33f, 0.28f, 0.95f);

    private static readonly Color QuietIdle = new(0.42f, 0.41f, 0.39f, 0.85f);
    private static readonly Color QuietHover = new(0.82f, 0.80f, 0.76f, 0.95f);

    private static Image AddRibbonPlate(GameObject go, MapEditorEmphasis emphasis,
        out Color idle, out Color hover)
    {
        var source = VanillaWidgets.Ribbon;
        if (source == null || source.sprite == null)
        {
            idle = PlateIdle;
            hover = PlateHover;
            return AddPlate(go, PlateIdle);
        }

        var quiet = emphasis == MapEditorEmphasis.Quiet ||
                    go.GetComponentInParent<MapEditorQuietArea>() != null
            ? VanillaWidgets.QuietRibbon
            : null;

        idle = quiet != null ? QuietIdle : RibbonIdle;
        hover = quiet != null ? QuietHover : RibbonHover;

        var image = go.AddComponent<Image>();
        image.sprite = quiet != null ? quiet : source.sprite;

        var sprite = image.sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : source.type;
        image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier > 0f
            ? source.pixelsPerUnitMultiplier
            : 1f;
        image.color = idle;
        return image;
    }

    private static Image AddPlate(GameObject go, Color colour)
    {
        var image = go.AddComponent<Image>();
        image.sprite = RoundedPlate;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1.5f;
        image.color = colour;
        return image;
    }

    // ---- fonts ------------------------------------------------------------------------------

    private static TMP_FontAsset _cachedFont;
    private static TMP_FontAsset _headingFont;
    private static bool _headingRequested;

    private static readonly List<TMP_Text> _pendingHeaders = [];

    public static void WarmFonts()
    {
        if (_headingRequested) return;
        _headingRequested = true;

        try
        {
            I2.Loc.LocalizationManager.GetLaptureRegularFont(font =>
            {
                if (font == null)
                {
                    _pendingHeaders.Clear();
                    return;
                }
                _headingFont = font;

                for (var i = _pendingHeaders.Count - 1; i >= 0; i--)
                {
                    var text = _pendingHeaders[i];
                    if (text != null) text.font = font;
                }
                _pendingHeaders.Clear();
            });
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: heading font unavailable, using the UI font: " + e.Message);
        }
    }

    private static TMP_FontAsset GameFont
    {
        get
        {
            if (_cachedFont != null) return _cachedFont;

            try
            {
                _cachedFont = VanillaWidgets.RowFont
                              ?? FontHelpers.UIFont ?? FontHelpers.PauseMenu ?? FontHelpers.StartMenu;
            }
            catch
            {
                _cachedFont = null;
            }

            if (_cachedFont == null)
            {
                foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
                {
                    if (text == null || text.font == null) continue;
                    _cachedFont = text.font;
                    break;
                }
            }

            return _cachedFont;
        }
    }

    private static TMP_FontAsset _cachedButtonFont;

    internal static TMP_FontAsset ButtonFont
    {
        get
        {
            if (_cachedButtonFont != null) return _cachedButtonFont;

            try
            {
                _cachedButtonFont = FontHelpers.PauseMenu ?? FontHelpers.StartMenu;
            }
            catch
            {
                _cachedButtonFont = null;
            }

            return _cachedButtonFont != null ? _cachedButtonFont : GameFont;
        }
    }

    private const int MinFontSize = 17;

    private static void AddTextScaler(GameObject go)
    {
        try
        {
            go.AddComponent<MMTextScaler>();
        }
        catch (Exception)
        {
        }
    }

    private static void ApplyRowLayout(GameObject go, float height)
    {
        var element = go.GetComponent<LayoutElement>();
        if (element == null) element = go.AddComponent<LayoutElement>();

        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleWidth = 1f;
    }

    // ---- text -------------------------------------------------------------------------------

    public GameObject CreateLabel(Transform parent, string text, int size = 20, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        size = Mathf.Max(size, MinFontSize);

        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(360, 28);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.fontStyle = FontStyles.Normal;

        var font = GameFont;
        if (font != null) tmp.font = font;

        ApplyRowLayout(go, size + 10f);
        AddTextScaler(go);
        return go;
    }

    public static void FitLabelHeight(GameObject label, float width = 380f)
    {
        if (label == null) return;

        var text = label.GetComponent<TMP_Text>();
        var element = label.GetComponent<LayoutElement>();
        if (text == null || element == null) return;

        var content = text.text ?? "";

        var lines = 1;
        foreach (var character in content)
            if (character == '\n') lines++;

        var measured = text.GetPreferredValues(content, width, 0f).y;
        var needed = Mathf.Max(measured, lines * text.fontSize * 1.35f) + 6f;

        if (Mathf.Abs(element.preferredHeight - needed) < 0.5f) return;

        element.minHeight = needed;
        element.preferredHeight = needed;

        if (label.transform is RectTransform rect)
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, needed);

        if (label.transform.parent is RectTransform parent) LayoutRebuilder.MarkLayoutForRebuild(parent);
    }

    public GameObject CreateHeader(Transform parent, string text, int size = 24)
    {
        var borrowed = VanillaWidgets.CreateHeader(parent, text, size, size + 14f);
        if (borrowed != null) return borrowed;

        return CreateHeadingLabel(parent, text, size);
    }

    public GameObject CreateHeadingLabel(Transform parent, string text, int size)
    {
        var go = CreateLabel(parent, text, size, TextAlignmentOptions.Center);
        var tmp = go.GetComponent<TMP_Text>();

        if (_headingFont != null) tmp.font = _headingFont;
        else _pendingHeaders.Add(tmp);

        tmp.color = new Color(0.95f, 0.88f, 0.72f);
        ApplyRowLayout(go, size + 14f);
        return go;
    }

    // ---- foldable section -------------------------------------------------------------------

    /// <summary>
    /// A foldable group under one header. The header is a flat tint the whole of which toggles the
    /// fold; the title sits in the heading font with an optional muted summary on the right. With
    /// <paramref name="withContent"/> the section also owns a content column right after the header
    /// (for rows inside a tool panel); without it the caller owns the body and only listens to
    /// <see cref="MapEditorSection.OnToggled"/> (the sidebar's slots).
    /// </summary>
    public MapEditorSection CreateSection(Transform parent, string title, bool open = true, bool withContent = true)
    {
        var header = new GameObject("Section_" + title);
        header.transform.SetParent(parent, false);
        var headerRt = header.AddComponent<RectTransform>();
        headerRt.sizeDelta = new Vector2(360f, SectionHeaderHeight);
        ApplyRowLayout(header, SectionHeaderHeight);

        var plate = header.AddComponent<Image>();
        plate.color = SectionIdle;

        var row = header.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(10, 12, 0, 0);
        row.spacing = 8;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;

        var chevron = CreateChevron(header.transform, out var chevronImage, out var chevronText);

        var titleGo = CreateHeadingLabel(header.transform, title, 20);
        var titleText = titleGo.GetComponent<TMP_Text>();
        titleText.alignment = TextAlignmentOptions.Left;
        titleText.enableWordWrapping = false;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        titleText.raycastTarget = false;
        var titleElement = titleGo.GetComponent<LayoutElement>();
        titleElement.flexibleWidth = 1f;
        titleElement.minWidth = 40f;

        var summaryGo = CreateLabel(header.transform, "", 17, TextAlignmentOptions.Right);
        var summaryText = summaryGo.GetComponent<TMP_Text>();
        summaryText.color = MutedText;
        summaryText.enableWordWrapping = false;
        summaryText.raycastTarget = false;
        var summaryElement = summaryGo.GetComponent<LayoutElement>();
        summaryElement.flexibleWidth = 0f;
        summaryGo.SetActive(false);

        RectTransform content = null;
        if (withContent)
        {
            var body = new GameObject("SectionContent_" + title);
            body.transform.SetParent(parent, false);
            content = body.AddComponent<RectTransform>();

            var column = body.AddComponent<VerticalLayoutGroup>();
            column.spacing = 5f;
            column.padding = new RectOffset(0, 0, 4, 4);
            column.childControlWidth = true;
            column.childForceExpandWidth = true;
            column.childControlHeight = false;
            column.childForceExpandHeight = false;

            var fitter = body.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var element = body.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
        }

        var section = new MapEditorSection(this, headerRt, content, titleText, summaryText, chevronImage,
            chevronText, open);

        AttachButton(header, plate, section.Toggle);
        AddHover(header, plate, SectionIdle, SectionHover, null);
        return section;
    }

    /// The fold marker: the game's own dropdown arrow when it can be borrowed, turned to point right
    /// while folded; a plain "+"/"-" label otherwise.
    private GameObject CreateChevron(Transform parent, out Image image, out TMP_Text text)
    {
        var go = new GameObject("Chevron");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(16f, 16f);

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 16f;
        element.minWidth = 16f;
        element.flexibleWidth = 0f;

        image = null;
        text = null;

        var sprite = VanillaWidgets.DropdownArrow;
        if (sprite != null)
        {
            var glyph = new GameObject("Glyph");
            glyph.transform.SetParent(go.transform, false);
            var glyphRt = glyph.AddComponent<RectTransform>();
            glyphRt.anchorMin = new Vector2(0.5f, 0.5f);
            glyphRt.anchorMax = new Vector2(0.5f, 0.5f);
            glyphRt.sizeDelta = new Vector2(14f, 14f);

            image = glyph.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = new Color(1f, 1f, 1f, 0.7f);
            image.raycastTarget = false;
            return go;
        }

        var label = CreateLabel(go.transform, "-", 17, TextAlignmentOptions.Center);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        text = label.GetComponent<TMP_Text>();
        text.color = new Color(1f, 1f, 1f, 0.7f);
        text.raycastTarget = false;
        return go;
    }

    // ---- buttons ----------------------------------------------------------------------------

    public GameObject CreateButton(Transform parent, string text, Action onClick, float height = 36f,
        MapEditorEmphasis emphasis = MapEditorEmphasis.Action)
    {
        var go = new GameObject("Btn_" + text);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(190, height);
        ApplyRowLayout(go, height);

        var plate = AddRibbonPlate(go, emphasis, out var idle, out var hover);

        AttachButton(go, plate, onClick);
        AddHover(go, plate, idle, hover, null);
        CreateButtonLabel(go.transform, text, height >= 32f ? 19 : 16);
        return go;
    }

    internal static void AttachButton(GameObject go, Image graphic, Action onClick)
    {
        void Handle()
        {
            CurrentHost?.BlockWorldClicks();

            onClick?.Invoke();
        }

        try
        {
            var button = go.AddComponent<MMButton>();
            button._targetGraphics = new MaskableGraphic[] { graphic };
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;

            button.PreventMouseSelection = true;

            button.onClick.AddListener(Handle);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: MMButton unavailable, using plain Button: " + e.Message);
            var plain = go.AddComponent<Button>();
            plain.targetGraphic = graphic;
            plain.transition = Selectable.Transition.None;
            plain.onClick.AddListener(Handle);
        }
    }

    internal static MapEditorHover AddHover(GameObject go, Image plate, Color idle, Color hover, string hoverText)
    {
        var component = go.AddComponent<MapEditorHover>();
        component.Plate = plate;
        component.Idle = idle;
        component.Hover = hover;
        component.HoverText = hoverText;
        component.Apply(false);
        return component;
    }

    private void CreateButtonLabel(Transform parent, string text, int size = 19)
    {
        var label = CreateLabel(parent, text, size, TextAlignmentOptions.Center);

        var face = ButtonFont;
        if (face != null) label.GetComponent<TMP_Text>().font = face;
        var rt = label.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(6f, 0f);
        rt.offsetMax = new Vector2(-6f, 0f);

        var tmp = label.GetComponent<TMP_Text>();
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }

    // ---- slider -----------------------------------------------------------------------------

    public GameObject CreateSlider(Transform parent, string label, float min, float max, float initial, Action<float> onChanged)
    {
        var borrowed = VanillaWidgets.CreateSlider(parent, label, min, max, initial, onChanged, RowHeight);
        if (borrowed != null) return borrowed;

        var row = new GameObject("Slider_" + label);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(360, RowHeight);
        ApplyRowLayout(row, RowHeight);

        var caption = CreateLabel(row.transform, label, 17);
        var captionRt = caption.GetComponent<RectTransform>();
        captionRt.anchorMin = new Vector2(0f, 0f);
        captionRt.anchorMax = new Vector2(0.44f, 1f);
        captionRt.offsetMin = Vector2.zero;
        captionRt.offsetMax = Vector2.zero;
        caption.GetComponent<TMP_Text>().enableWordWrapping = false;

        var readout = CreateLabel(row.transform, initial.ToString("0.##"), 17, TextAlignmentOptions.Right);
        var readoutRt = readout.GetComponent<RectTransform>();
        readoutRt.anchorMin = new Vector2(1f, 0f);
        readoutRt.anchorMax = new Vector2(1f, 1f);
        readoutRt.pivot = new Vector2(1f, 0.5f);
        readoutRt.sizeDelta = new Vector2(48f, 0f);
        readoutRt.anchoredPosition = Vector2.zero;
        var readoutText = readout.GetComponent<TMP_Text>();

        var sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(row.transform, false);
        var srt = sliderGO.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.44f, 0f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.offsetMin = new Vector2(6f, 10f);
        srt.offsetMax = new Vector2(-52f, -10f);

        AddPlate(sliderGO, TrackColour);

        var fillArea = NewChild(sliderGO.transform, "FillArea", stretch: true);
        var fill = NewChild(fillArea, "Fill", stretch: true);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = RoundedPlate;
        fillImg.type = Image.Type.Sliced;
        fillImg.pixelsPerUnitMultiplier = 3f;
        fillImg.color = SliderFill;

        var handleArea = NewChild(sliderGO.transform, "HandleArea", stretch: true);
        var handle = NewChild(handleArea, "Handle", stretch: false);
        handle.sizeDelta = new Vector2(12f, 0f);
        handle.anchorMin = new Vector2(0f, 0f);
        handle.anchorMax = new Vector2(0f, 1f);
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = RoundedPlate;
        handleImg.type = Image.Type.Sliced;
        handleImg.pixelsPerUnitMultiplier = 3f;
        handleImg.color = new Color(0.95f, 0.93f, 0.86f, 1f);

        var slider = sliderGO.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(initial);

        var state = row.AddComponent<MapEditorSlider>();
        state.Slider = slider;
        state.Readout = readoutText;
        state.OnValueChanged = onChanged;

        slider.onValueChanged.AddListener(state.Raw);

        return row;
    }

    internal static RectTransform NewChild(Transform parent, string name, bool stretch)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        if (!stretch) return rt;

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    // ---- hover preview ------------------------------------------------------------------------

    private const float PreviewSize = 300f;

    private const float PreviewRightOffset = 448f;

    private GameObject _previewGO;
    private Image _previewImage;
    private TMP_Text _previewCaption;

    public float IconPreviewRightOffset { get; set; } = PreviewRightOffset;

    /// Distance from the top of the canvas; a screen with a top bar pushes the preview under it.
    public float IconPreviewTopOffset { get; set; } = PreviewTopOffset;

    private const float PreviewTopOffset = 12f;

    public static float DefaultIconPreviewRightOffset => PreviewRightOffset;

    public static float DefaultIconPreviewTopOffset => PreviewTopOffset;

    public void ShowIconPreview(Sprite sprite, string caption)
    {
        if (sprite == null || _canvasRoot == null)
        {
            HideIconPreview();
            return;
        }

        EnsurePreview();
        if (_previewGO == null) return;

        var rect = (RectTransform)_previewGO.transform;
        rect.anchoredPosition = new Vector2(-IconPreviewRightOffset, -IconPreviewTopOffset);

        rect.SetAsLastSibling();

        _previewImage.sprite = sprite;
        _previewCaption.text = caption ?? "";
        _previewGO.SetActive(true);
    }

    public void HideIconPreview()
    {
        if (_previewGO != null) _previewGO.SetActive(false);
    }

    private void EnsurePreview()
    {
        if (_previewGO != null) return;

        _previewGO = new GameObject("IconPreview");
        _previewGO.transform.SetParent(_canvasRoot, false);

        var rect = _previewGO.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(PreviewSize, PreviewSize + 34f);
        rect.anchoredPosition = new Vector2(-IconPreviewRightOffset, -IconPreviewTopOffset);

        var plate = _previewGO.AddComponent<Image>();
        plate.sprite = RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.6f;
        plate.color = new Color(0f, 0f, 0f, 0.82f);

        plate.raycastTarget = false;

        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(_previewGO.transform, false);
        var iconRect = iconGO.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0f);
        iconRect.anchorMax = new Vector2(1f, 1f);
        iconRect.offsetMin = new Vector2(10f, 34f);
        iconRect.offsetMax = new Vector2(-10f, -10f);

        _previewImage = iconGO.AddComponent<Image>();
        _previewImage.preserveAspect = true;
        _previewImage.raycastTarget = false;

        var captionGO = CreateLabel(_previewGO.transform, "", 17, TextAlignmentOptions.Center);
        var captionRect = captionGO.GetComponent<RectTransform>();
        captionRect.anchorMin = new Vector2(0f, 0f);
        captionRect.anchorMax = new Vector2(1f, 0f);
        captionRect.pivot = new Vector2(0.5f, 0f);
        captionRect.offsetMin = new Vector2(8f, 6f);
        captionRect.offsetMax = new Vector2(-8f, 6f);
        captionRect.sizeDelta = new Vector2(captionRect.sizeDelta.x, 26f);

        _previewCaption = captionGO.GetComponent<TMP_Text>();
        _previewCaption.raycastTarget = false;
        _previewCaption.enableWordWrapping = false;
        _previewCaption.overflowMode = TextOverflowModes.Ellipsis;

        _previewGO.SetActive(false);
    }

    // ---- toggle -----------------------------------------------------------------------------

    public GameObject CreateToggle(Transform parent, string label, bool initial, Action<bool> onChanged)
    {
        var borrowed = VanillaWidgets.CreateToggle(parent, label, initial, onChanged, RowHeight);
        if (borrowed != null) return borrowed;

        var row = new GameObject("Toggle_" + label);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(360, RowHeight);
        ApplyRowLayout(row, RowHeight);

        var bg = row.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.03f);

        var caption = CreateLabel(row.transform, label, 17);
        var captionRt = caption.GetComponent<RectTransform>();
        captionRt.anchorMin = Vector2.zero;
        captionRt.anchorMax = new Vector2(1f, 1f);
        captionRt.offsetMin = new Vector2(4f, 0f);
        captionRt.offsetMax = new Vector2(-40f, 0f);
        caption.GetComponent<TMP_Text>().enableWordWrapping = false;

        var box = new GameObject("Box");
        box.transform.SetParent(row.transform, false);
        var boxRt = box.AddComponent<RectTransform>();
        boxRt.anchorMin = new Vector2(1f, 0.5f);
        boxRt.anchorMax = new Vector2(1f, 0.5f);
        boxRt.pivot = new Vector2(1f, 0.5f);
        boxRt.sizeDelta = new Vector2(26f, 26f);
        boxRt.anchoredPosition = new Vector2(-4f, 0f);

        var outline = box.AddComponent<Image>();
        outline.sprite = RoundedPlate;
        outline.type = Image.Type.Sliced;
        outline.pixelsPerUnitMultiplier = 2.2f;
        outline.color = new Color(0.75f, 0.71f, 0.62f, 1f);

        var innerRt = NewChild(box.transform, "Inner", stretch: true);
        innerRt.offsetMin = new Vector2(3f, 3f);
        innerRt.offsetMax = new Vector2(-3f, -3f);
        var inner = innerRt.gameObject.AddComponent<Image>();
        inner.sprite = RoundedPlate;
        inner.type = Image.Type.Sliced;
        inner.pixelsPerUnitMultiplier = 2.6f;
        inner.raycastTarget = false;

        var toggle = row.AddComponent<MapEditorToggle>();
        toggle.Fill = inner;
        toggle.OnValueChanged = onChanged;
        toggle.SetValue(initial, notify: false);

        AttachButton(row, bg, () => toggle.SetValue(!toggle.Value, notify: true));
        AddHover(row, bg, new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.12f), null);
        return row;
    }

    // ---- containers -------------------------------------------------------------------------

    public RectTransform CreateScrollColumn(Transform parent, string name, out GameObject root, float spacing = 5f)
    {
        var scrollGO = new GameObject(name);
        scrollGO.transform.SetParent(parent, false);
        root = scrollGO;

        var scrollRt = scrollGO.AddComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;

        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 30f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGO.transform, false);
        var viewportRt = viewport.AddComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.pivot = new Vector2(0f, 1f);
        viewportRt.offsetMin = new Vector2(0f, 0f);
        viewportRt.offsetMax = new Vector2(-(ScrollbarWidth + 2f), 0f);
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;

        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(10, 10, 8, 8);

        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        scroll.verticalScrollbar = CreateScrollbar(scrollGO.transform);

        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        return contentRt;
    }

    public const float ScrollbarWidth = 2f;

    private static Scrollbar CreateScrollbar(Transform parent)
    {
        var go = new GameObject("Scrollbar");
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(ScrollbarWidth, 0f);
        rt.anchoredPosition = Vector2.zero;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.07f);

        var scrollbar = go.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var area = new GameObject("SlidingArea");
        area.transform.SetParent(go.transform, false);
        var areaRt = area.AddComponent<RectTransform>();
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = Vector2.zero;
        areaRt.offsetMax = Vector2.zero;

        var handle = new GameObject("Handle");
        handle.transform.SetParent(area.transform, false);
        var handleRt = handle.AddComponent<RectTransform>();
        handleRt.offsetMin = Vector2.zero;
        handleRt.offsetMax = Vector2.zero;

        var handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.9f);

        scrollbar.targetGraphic = handleImg;
        scrollbar.handleRect = handleRt;
        return scrollbar;
    }

    public GameObject CreateImage(Transform parent, Texture2D texture, float height = 100f)
    {
        var row = new GameObject("Preview");
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>();
        ApplyRowLayout(row, height);

        var imageGO = new GameObject("Image");
        imageGO.transform.SetParent(row.transform, false);
        var rt = imageGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var raw = imageGO.AddComponent<RawImage>();
        raw.texture = texture;

        var fitter = imageGO.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = texture != null && texture.height > 0
            ? (float)texture.width / texture.height : 16f / 9f;

        return row;
    }

    // ---- dropdown ---------------------------------------------------------------------------

    public const float DropdownHeight = 38f;

    private static readonly Color DropdownOutline = new(1f, 1f, 1f, 0.14f);

    public MapEditorDropdown CreateDropdown(Transform parent, string caption, IList<string> options,
        Action<int, string> onSelected)
    {
        var row = new GameObject("Dropdown_" + caption);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(360, DropdownHeight);
        ApplyRowLayout(row, DropdownHeight);

        var field = AddPlate(row, FieldIdle);

        AddOutline(rowRt, DropdownOutline);

        var label = CreateLabel(row.transform, caption, 18);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(12f, 0f);
        labelRt.offsetMax = new Vector2(-40f, 0f);
        var labelText = label.GetComponent<TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        labelText.color = Color.white;

        var arrowPanel = new GameObject("Arrow");
        arrowPanel.transform.SetParent(row.transform, false);
        var arrowRt = arrowPanel.AddComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(1f, 0f);
        arrowRt.anchorMax = new Vector2(1f, 1f);
        arrowRt.pivot = new Vector2(1f, 0.5f);
        arrowRt.sizeDelta = new Vector2(34f, -8f);
        arrowRt.anchoredPosition = new Vector2(-4f, 0f);

        var caret = VanillaWidgets.DropdownArrow;
        if (caret != null)
        {
            var glyph = new GameObject("Caret");
            glyph.transform.SetParent(arrowPanel.transform, false);

            var caretRt = glyph.AddComponent<RectTransform>();
            caretRt.anchorMin = new Vector2(0.5f, 0.5f);
            caretRt.anchorMax = new Vector2(0.5f, 0.5f);
            caretRt.pivot = new Vector2(0.5f, 0.5f);
            caretRt.sizeDelta = new Vector2(18f, 18f);
            caretRt.anchoredPosition = Vector2.zero;

            var caretImage = glyph.AddComponent<Image>();
            caretImage.sprite = caret;
            caretImage.preserveAspect = true;
            caretImage.color = new Color(1f, 1f, 1f, 0.75f);
            caretImage.raycastTarget = false;
        }
        else
        {
            var fallback = CreateLabel(arrowPanel.transform, "v", 17, TextAlignmentOptions.Center);
            var fallbackRt = fallback.GetComponent<RectTransform>();
            fallbackRt.anchorMin = Vector2.zero;
            fallbackRt.anchorMax = Vector2.one;
            fallbackRt.offsetMin = Vector2.zero;
            fallbackRt.offsetMax = Vector2.zero;
            var fallbackText = fallback.GetComponent<TMP_Text>();
            fallbackText.color = MutedText;
            fallbackText.raycastTarget = false;
        }

        var dropdown = new MapEditorDropdown(this, row, rowRt, labelText, caption, onSelected);
        dropdown.SetOptions(options);

        AttachButton(row, field, dropdown.Toggle);
        AddHover(row, field, FieldIdle, FieldHover, null);
        return dropdown;
    }

    /// <summary>
    /// A dropdown that sits beside its label the way a toggle does, for a setting whose name cannot be
    /// read off the value alone. The field takes the right of the row; the floating option list still
    /// spans the whole row, so long option text stays readable.
    /// </summary>
    public MapEditorDropdown CreateLabelledDropdown(Transform parent, string label, string caption,
        IList<string> options, Action<int, string> onSelected, float split = 0.35f)
    {
        var row = new GameObject("LabelledDropdown_" + label);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(360f, DropdownHeight);
        ApplyRowLayout(row, DropdownHeight);

        var title = CreateLabel(row.transform, label, 17);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = Vector2.zero;
        titleRt.anchorMax = new Vector2(split, 1f);
        titleRt.offsetMin = new Vector2(4f, 0f);
        titleRt.offsetMax = new Vector2(-8f, 0f);
        var titleText = title.GetComponent<TMP_Text>();
        titleText.enableWordWrapping = false;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        titleText.raycastTarget = false;
        titleText.color = MutedText;

        var dropdown = CreateDropdown(row.transform, caption, options, onSelected);

        // The field lays itself out for a column; inside a row it is anchored to the right instead,
        // and the layout element CreateDropdown left on it goes inert with no layout group above it.
        var fieldRt = dropdown.Root.GetComponent<RectTransform>();
        fieldRt.anchorMin = new Vector2(split, 0f);
        fieldRt.anchorMax = Vector2.one;
        fieldRt.pivot = new Vector2(0.5f, 0.5f);
        fieldRt.offsetMin = Vector2.zero;
        fieldRt.offsetMax = Vector2.zero;

        dropdown.ListFrom = rowRt;
        return dropdown;
    }

    /// <summary>
    /// A dropdown with no field of its own: the list drops from whatever rect is handed in. It lets
    /// a button answer a click with its own menu, instead of sending the reader to a panel to find
    /// the same list. Returns null when there is nothing to choose from.
    /// </summary>
    public MapEditorDropdown ShowMenu(RectTransform anchor, IList<string> options,
        Action<int, string> onSelected)
    {
        if (anchor == null || _canvasRoot == null || options == null || options.Count == 0) return null;

        var holder = new GameObject("Menu");
        holder.transform.SetParent(_canvasRoot, false);
        var rt = holder.AddComponent<RectTransform>();
        rt.sizeDelta = Vector2.zero;

        var menu = new MapEditorDropdown(this, holder, rt, null, "", onSelected) { ListFrom = anchor };
        menu.SetOptions(options);

        // Set after the options, because setting them closes the menu first.
        menu.DestroyRootOnClose = true;
        menu.Open();
        return menu;
    }

    private MapEditorDropdown _openDropdown;

    internal void NotifyDropdownOpened(MapEditorDropdown dropdown)
    {
        if (_openDropdown != null && _openDropdown != dropdown) _openDropdown.Close();
        _openDropdown = dropdown;
    }

    internal void NotifyDropdownClosed(MapEditorDropdown dropdown)
    {
        if (_openDropdown == dropdown) _openDropdown = null;
    }

    public bool TransientUiOpen => _openDropdown != null;

    public void CloseTransientUi() => _openDropdown?.Close();

    public MapEditorScrollBox CreateScrollBox(Transform parent, string name, float maxHeight,
        float rowHeight = 30f, float spacing = 3f)
    {
        var content = CreateScrollColumn(parent, name, out var root, spacing);
        content.gameObject.AddComponent<MapEditorQuietArea>();
        return new MapEditorScrollBox(this, content, root, maxHeight, rowHeight, spacing);
    }

    // ---- icon grid --------------------------------------------------------------------------

    public MapEditorGrid CreateIconGrid(Transform parent, string name, int columns = 4,
        float cellSize = 88f, float scrollHeight = 0f)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.AddComponent<RectTransform>();

        var rootLayout = root.AddComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 4f;
        rootLayout.childControlWidth = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childAlignment = TextAnchor.UpperCenter;

        var rootFitter = root.AddComponent<ContentSizeFitter>();
        rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var cellParent = root.transform;
        LayoutElement boxElement = null;

        if (scrollHeight > 0f)
        {
            var boxed = CreateScrollColumn(root.transform, "Cells_Scroll", out var boxRoot, spacing: 0f);

            boxElement = boxRoot.AddComponent<LayoutElement>();
            boxElement.flexibleWidth = 1f;

            cellParent = boxed;
        }

        var cells = new GameObject("Cells");
        cells.transform.SetParent(cellParent, false);
        cells.AddComponent<RectTransform>();

        if (scrollHeight > 0f)
        {
            var cellFitter = cells.AddComponent<ContentSizeFitter>();
            cellFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        var grid = cells.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(cellSize, cellSize);
        grid.spacing = new Vector2(6f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperCenter;

        var caption = CreateLabel(root.transform, "", 17, TextAlignmentOptions.Center);
        var captionText = caption.GetComponent<TMP_Text>();
        captionText.enableWordWrapping = false;
        captionText.overflowMode = TextOverflowModes.Ellipsis;
        ApplyRowLayout(caption, 26f);

        var built = new MapEditorGrid(this, root, cells.transform, captionText);

        if (boxElement != null) built.UseScrollBox(boxElement, scrollHeight, columns, cellSize, 6f);

        return built;
    }

    public GameObject CreateIconButton(Transform parent, Sprite icon, string label, Action onClick,
        out Image selectionBorder, float size = 60f, string hoverText = null)
    {
        var cell = new GameObject("Icon_" + label);
        cell.transform.SetParent(parent, false);
        var cellRt = cell.AddComponent<RectTransform>();
        cellRt.sizeDelta = new Vector2(size, size);

        var element = cell.AddComponent<LayoutElement>();
        element.preferredWidth = size;
        element.preferredHeight = size;
        element.minWidth = size;
        element.minHeight = size;

        var border = new GameObject("Border");
        border.transform.SetParent(cell.transform, false);
        var borderRt = border.AddComponent<RectTransform>();
        borderRt.anchorMin = Vector2.zero;
        borderRt.anchorMax = Vector2.one;
        borderRt.offsetMin = new Vector2(-3f, -3f);
        borderRt.offsetMax = new Vector2(3f, 3f);
        selectionBorder = border.AddComponent<Image>();
        selectionBorder.sprite = RoundedPlate;
        selectionBorder.type = Image.Type.Sliced;
        selectionBorder.pixelsPerUnitMultiplier = 1.4f;
        selectionBorder.color = Accent;
        selectionBorder.raycastTarget = false;
        border.SetActive(false);

        var plateGO = new GameObject("Plate");
        plateGO.transform.SetParent(cell.transform, false);
        var plateRt = plateGO.AddComponent<RectTransform>();
        plateRt.anchorMin = Vector2.zero;
        plateRt.anchorMax = Vector2.one;
        plateRt.offsetMin = Vector2.zero;
        plateRt.offsetMax = Vector2.zero;
        var plate = AddPlate(plateGO, PlateIdle);

        var letter = CreateLabel(cell.transform, Initials(label), 20, TextAlignmentOptions.Center);
        var letterRt = letter.GetComponent<RectTransform>();
        letterRt.anchorMin = Vector2.zero;
        letterRt.anchorMax = Vector2.one;
        letterRt.offsetMin = Vector2.zero;
        letterRt.offsetMax = Vector2.zero;
        letter.GetComponent<TMP_Text>().raycastTarget = false;

        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(cell.transform, false);
        var iconRt = iconGO.AddComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(5f, 5f);
        iconRt.offsetMax = new Vector2(-5f, -5f);

        var iconImg = iconGO.AddComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        iconImg.sprite = icon;
        iconImg.enabled = icon != null;
        letter.SetActive(icon == null);

        AttachButton(cell, plate, onClick);
        AddHover(cell, plate, PlateIdle, PlateHover, hoverText);
        return cell;
    }

    public GameObject CreateKeyHint(Transform parent, string key, string action)
    {
        var row = new GameObject("Hint_" + action);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(240f, 34f);
        ApplyRowLayout(row, 34f);

        var plate = row.AddComponent<Image>();
        plate.sprite = RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.5f;
        plate.color = new Color(0f, 0f, 0f, 0.62f);
        plate.raycastTarget = false;

        var capWidth = Mathf.Clamp(22f + (key?.Length ?? 1) * 9f, 30f, 76f);

        var cap = new GameObject("Cap");
        cap.transform.SetParent(row.transform, false);
        var capRt = cap.AddComponent<RectTransform>();
        capRt.anchorMin = new Vector2(0f, 0.5f);
        capRt.anchorMax = new Vector2(0f, 0.5f);
        capRt.pivot = new Vector2(0f, 0.5f);
        capRt.sizeDelta = new Vector2(capWidth, 26f);
        capRt.anchoredPosition = new Vector2(5f, 0f);

        var capImage = cap.AddComponent<Image>();
        capImage.sprite = RoundedPlate;
        capImage.type = Image.Type.Sliced;
        capImage.pixelsPerUnitMultiplier = 3.5f;
        capImage.color = new Color(0.95f, 0.93f, 0.86f, 1f);
        capImage.raycastTarget = false;

        var capLabel = CreateLabel(cap.transform, key, 17, TextAlignmentOptions.Center);
        var capLabelRt = capLabel.GetComponent<RectTransform>();
        capLabelRt.anchorMin = Vector2.zero;
        capLabelRt.anchorMax = Vector2.one;
        capLabelRt.offsetMin = Vector2.zero;
        capLabelRt.offsetMax = Vector2.zero;

        var capText = capLabel.GetComponent<TMP_Text>();
        capText.color = new Color(0.11f, 0.10f, 0.09f);
        capText.enableWordWrapping = false;
        capText.raycastTarget = false;

        var label = CreateLabel(row.transform, action, 17);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(capWidth + 13f, 0f);
        labelRt.offsetMax = new Vector2(-8f, 0f);

        var labelText = label.GetComponent<TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        labelText.raycastTarget = false;

        return row;
    }

    /// <summary>
    /// The key hint's sideways twin for a horizontal row: the same cream cap, the action beside it, no
    /// plate of its own, and a width measured from the text so a layout group can pack chips left to
    /// right. <see cref="MapEditorKeyChip.Remeasure"/> re-reads the width once the text scaler has had
    /// its say.
    /// </summary>
    public GameObject CreateKeyChip(Transform parent, string key, string action, Action onClick = null,
        Color? textColour = null)
    {
        const float height = 28f;

        var row = new GameObject("Chip_" + action);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(120f, height);

        var element = row.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleWidth = 0f;

        // A chip is normally just painted text, but the ones that stand for a button (the editor keys
        // in the top bar, the "all shortcuts" chip) need something for the raycast to land on.
        Image hit = null;
        if (onClick != null)
        {
            hit = row.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
        }

        var capWidth = Mathf.Clamp(22f + (key?.Length ?? 1) * 9f, 30f, 96f);

        var cap = new GameObject("Cap");
        cap.transform.SetParent(row.transform, false);
        var capRt = cap.AddComponent<RectTransform>();
        capRt.anchorMin = new Vector2(0f, 0.5f);
        capRt.anchorMax = new Vector2(0f, 0.5f);
        capRt.pivot = new Vector2(0f, 0.5f);
        capRt.sizeDelta = new Vector2(capWidth, 24f);
        capRt.anchoredPosition = Vector2.zero;

        var capImage = cap.AddComponent<Image>();
        capImage.sprite = RoundedPlate;
        capImage.type = Image.Type.Sliced;
        capImage.pixelsPerUnitMultiplier = 3.5f;
        capImage.color = new Color(0.95f, 0.93f, 0.86f, 1f);
        capImage.raycastTarget = false;

        var capLabel = CreateLabel(cap.transform, key, 17, TextAlignmentOptions.Center);
        var capLabelRt = capLabel.GetComponent<RectTransform>();
        capLabelRt.anchorMin = Vector2.zero;
        capLabelRt.anchorMax = Vector2.one;
        capLabelRt.offsetMin = Vector2.zero;
        capLabelRt.offsetMax = Vector2.zero;

        var capText = capLabel.GetComponent<TMP_Text>();
        capText.color = new Color(0.11f, 0.10f, 0.09f);
        capText.enableWordWrapping = false;
        capText.raycastTarget = false;

        var label = CreateLabel(row.transform, action, 17);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(0f, 1f);
        labelRt.pivot = new Vector2(0f, 0.5f);
        labelRt.offsetMin = new Vector2(capWidth + 8f, 0f);
        labelRt.offsetMax = new Vector2(capWidth + 8f, 0f);

        var labelText = label.GetComponent<TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Overflow;
        labelText.color = textColour ?? new Color(0.87f, 0.83f, 0.76f);
        labelText.raycastTarget = false;

        var chip = row.AddComponent<MapEditorKeyChip>();
        chip.Element = element;
        chip.Label = labelText;
        chip.LabelRect = labelRt;
        chip.CapWidth = capWidth;
        chip.Remeasure();

        if (onClick != null)
        {
            AttachButton(row, hit, onClick);
            AddHover(row, hit, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.10f), null);
        }

        return row;
    }

    public static string Initials(string label)
    {
        if (string.IsNullOrEmpty(label)) return "?";

        var parts = label.Split([' ', '_', '-', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return label.Substring(0, 1).ToUpperInvariant();
        if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
        return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
    }
}

public class MapEditorToggle : MonoBehaviour
{
    public Action<bool> OnValueChanged;

    public Image Fill;
    public Lamb.UI.MMToggle Vanilla;

    private bool _value;

    public bool Value
    {
        get => _value;
        set => SetValue(value, notify: true);
    }

    public void SetValue(bool value, bool notify)
    {
        _value = value;

        if (Fill != null)
            Fill.color = value ? MapEditorUI.SliderFill : new Color(0.05f, 0.05f, 0.04f, 1f);

        if (Vanilla != null) Vanilla.Value = value;

        if (notify) OnValueChanged?.Invoke(value);
    }
}

public enum MapEditorEmphasis
{
    Action,
    Quiet
}

public class MapEditorQuietArea : MonoBehaviour
{
}

public class MapEditorHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image Plate;
    public Color Idle;
    public Color Hover;

    public string HoverText;

    public Func<string> HoverTextProvider;

    public Action<bool> OnHover;

    private bool _hovered;

    public void OnPointerEnter(PointerEventData eventData) => Apply(true);
    public void OnPointerExit(PointerEventData eventData) => Apply(false);

    private void OnDisable()
    {
        if (_hovered) Apply(false);
    }

    public void Apply(bool hovered)
    {
        _hovered = hovered;
        if (Plate != null) Plate.color = hovered ? Hover : Idle;
        OnHover?.Invoke(hovered);

        var text = HoverTextProvider != null ? HoverTextProvider() : HoverText;
        if (string.IsNullOrEmpty(text)) return;

        var host = MapEditorUI.CurrentHost;
        if (host == null) return;

        if (hovered) host.ShowHoverStatus(text);
        else host.ClearHoverStatus();
    }
}

/// A key chip's width is the cap plus the action text; the text scaler can change the font size
/// after the chip is built, so the bar asks for a re-measure for a couple of frames after a rebuild.
public class MapEditorKeyChip : MonoBehaviour
{
    public LayoutElement Element;
    public TMP_Text Label;
    public RectTransform LabelRect;
    public float CapWidth;

    public float Width { get; private set; }

    public void Remeasure()
    {
        if (Label == null || Element == null) return;

        var text = Label.GetPreferredValues(Label.text ?? "").x + 2f;
        Width = CapWidth + 8f + text + 6f;

        Element.preferredWidth = Width;
        Element.minWidth = Width;

        if (LabelRect != null) LabelRect.sizeDelta = new Vector2(text, LabelRect.sizeDelta.y);
    }
}

/// Right mouse button on a UI element. Unity's Button only answers to the left one, so a cell that
/// wants both carries this alongside it - both components see the click and each takes its own.
public class MapEditorRightClick : MonoBehaviour, IPointerClickHandler
{
    public Action OnRightClick;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Right) return;
        OnRightClick?.Invoke();
    }
}
