using System;
using System.Collections.Generic;
using COTL_API.UI.Helpers;
using Lamb.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

// Builds the editor chrome.
//
// Rows are built from scratch rather than cloned out of the settings menu: those rows are
// authored 56px tall for a full-width panel and squashed their labels in the editor's narrower
// column, and they only existed at all once the player had opened the settings menu. Everything
// here is self-contained and available on a cold start.
//
// The look follows the game's own menus: rounded dark plates that brighten under the cursor, the
// menu font at menu sizes, the settings sliders' green for anything that fills, and the cult red
// for whatever is currently selected. Buttons still get a real MMButton for its confirm SFX,
// with its colour transition switched off so it does not fight the hover tint.
public class MapEditorUI
{
    // Rows across the whole editor. One number, so panels stay predictable in height.
    public const float RowHeight = 34f;

    // ---- palette ----------------------------------------------------------------------------

    public static readonly Color PlateIdle = new(0.09f, 0.08f, 0.07f, 0.96f);
    public static readonly Color PlateHover = new(0.26f, 0.23f, 0.19f, 1f);
    public static readonly Color FieldIdle = new(0.05f, 0.05f, 0.04f, 0.96f);
    public static readonly Color FieldHover = new(0.16f, 0.15f, 0.13f, 1f);

    // The cult red, used for the selected tool and the selected grid cell.
    public static readonly Color Accent = new(0.83f, 0.24f, 0.20f, 1f);

    // The green the game's own settings sliders fill with; shared by the check boxes.
    public static readonly Color SliderFill = new(0.55f, 0.78f, 0.25f, 1f);

    public static readonly Color TrackColour = new(0.05f, 0.05f, 0.04f, 0.95f);

    private RuntimeMapEditor _editor;
    private RectTransform _canvasRoot;

    public RectTransform CanvasRoot => _canvasRoot;
    public RuntimeMapEditor Editor => _editor;

    // Needed before any dropdown or grid is built: floating overlays parent to the canvas root
    // and register themselves as click blockers, and async icon fills need a coroutine host.
    public void Attach(RuntimeMapEditor editor, RectTransform canvasRoot)
    {
        _editor = editor;
        _canvasRoot = canvasRoot;
        WarmFonts();
    }

    // ---- rounded plate ----------------------------------------------------------------------

    private static Sprite _rounded;

    // A 9-sliced rounded rectangle, generated rather than shipped: the mod has no art pipeline,
    // and every plate in the editor wants the same shape at a different size.
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
                    // Distance past the corner arc, so the edge gets one pixel of softening
                    // instead of a staircase.
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

    private static Sprite _outline;

    // The same rounded rectangle with a transparent middle, so it can be laid over a panel as a
    // frame rather than covering it. A child Graphic always draws on top of its parent's, which
    // is why the status bar's alert border cannot simply be a tinted plate behind it.
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
                    // Signed distance to the rounded edge: 0 on the outline, negative inside.
                    var dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                    var dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                    var corner = Mathf.Sqrt(dx * dx + dy * dy) - radius;

                    var edge = Mathf.Min(
                        Mathf.Min(x + 0.5f, size - (x + 0.5f)),
                        Mathf.Min(y + 0.5f, size - (y + 0.5f)));
                    var distance = dx > 0f || dy > 0f ? corner : -edge;

                    // Opaque in the band [-thickness, 0], faded at both ends.
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

    // A frame laid over an existing panel, used to mark it as needing attention.
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

    // Headers built before the async heading font arrives; re-fonted on arrival rather than
    // left on the fallback for the rest of the session.
    private static readonly List<TMP_Text> _pendingHeaders = [];

    public static void WarmFonts()
    {
        if (_headingRequested) return;
        _headingRequested = true;

        try
        {
            I2.Loc.LocalizationManager.GetLaptureRegularFont(font =>
            {
                if (font == null) return;
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

    // COTL_API's font helpers scrape the live menus, so they return null until those menus have
    // been built. Fall back through the alternatives and finally scavenge the font off any text
    // already in the scene, so editor chrome matches the game rather than dropping to the
    // TextMeshPro default.
    private static TMP_FontAsset GameFont
    {
        get
        {
            if (_cachedFont != null) return _cachedFont;

            try
            {
                _cachedFont = FontHelpers.UIFont ?? FontHelpers.PauseMenu ?? FontHelpers.StartMenu;
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

    // The game's menus never go below this, and the editor sitting at 14-15 was the main reason
    // its panels read as a debug overlay rather than part of the game.
    private const int MinFontSize = 17;

    // The game's accessibility Text Scale slider drives this; it captures the font size in
    // OnEnable, so it can only be added once the size is final.
    private static void AddTextScaler(GameObject go)
    {
        try
        {
            go.AddComponent<MMTextScaler>();
        }
        catch (Exception)
        {
            // No AccessibilityManager in this scene; the label simply does not scale.
        }
    }

    // Pins a row's height for the vertical layout group it lands in; width is driven by the
    // column so labels fill it instead of collapsing to their anchored size.
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

        var font = GameFont;
        if (font != null) tmp.font = font;

        ApplyRowLayout(go, size + 10f);
        AddTextScaler(go);
        return go;
    }

    // Section heading in the game's heading font. Used for the '— Groups —' style dividers the
    // tools used to draw as ordinary labels.
    public GameObject CreateHeader(Transform parent, string text, int size = 24)
    {
        var go = CreateLabel(parent, text, size, TextAlignmentOptions.Center);
        var tmp = go.GetComponent<TMP_Text>();

        if (_headingFont != null) tmp.font = _headingFont;
        else _pendingHeaders.Add(tmp);

        tmp.color = new Color(0.95f, 0.88f, 0.72f);
        ApplyRowLayout(go, size + 14f);
        return go;
    }

    // ---- buttons ----------------------------------------------------------------------------

    public GameObject CreateButton(Transform parent, string text, Action onClick, float height = 36f)
    {
        var go = new GameObject("Btn_" + text);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(190, height);
        ApplyRowLayout(go, height);

        var plate = AddPlate(go, PlateIdle);

        AttachButton(go, plate, onClick);
        AddHover(go, plate, PlateIdle, PlateHover, null);
        CreateButtonLabel(go.transform, text, height >= 32f ? 19 : 16);
        return go;
    }

    // MMButton is the game's own button class, so we inherit its confirm SFX. Its colour
    // transition is switched off: it drives the same graphic the hover tint owns, and the two
    // fought over it.
    private static void AttachButton(GameObject go, Image graphic, Action onClick)
    {
        // Every editor button goes through here, which is the one place that can guarantee a
        // click on chrome is never ALSO treated as a world click. Without it, pressing a tool
        // icon while a structure was armed dropped that structure into the world behind the
        // dock - the button fires on pointer-up, and the tool that took over saw the same
        // press as its own.
        void Handle()
        {
            RuntimeMapEditor.Active?.BlockWorldClicks();
            onClick?.Invoke();
        }

        try
        {
            var button = go.AddComponent<MMButton>();
            button._targetGraphics = new MaskableGraphic[] { graphic };
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;
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

    // Hover does double duty: it brightens the plate, and it is how the status bar learns what
    // the cursor is over (tool names on the dock, item names in the grids).
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

    // A real-range uGUI slider in one row: label, track, numeric readout. The old implementation
    // cloned the settings menu's row, which only worked in whole percent and needed the settings
    // menu to have been opened at least once. The fill is the game's own slider green.
    public GameObject CreateSlider(Transform parent, string label, float min, float max, float initial, Action<float> onChanged)
    {
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
        slider.onValueChanged.AddListener(v =>
        {
            if (readoutText != null) readoutText.text = v.ToString("0.##");
            onChanged?.Invoke(v);
        });

        return row;
    }

    private static RectTransform NewChild(Transform parent, string name, bool stretch)
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

    // ---- toggle -----------------------------------------------------------------------------

    // Deliberately not plate-shaped: a toggle that looked exactly like a button gave no hint
    // that it had a state. The row is bare, and the state lives in an outlined box on the right
    // that fills with the game's green when it is on.
    public GameObject CreateToggle(Transform parent, string label, bool initial, Action<bool> onChanged)
    {
        var row = new GameObject("Toggle_" + label);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(360, RowHeight);
        ApplyRowLayout(row, RowHeight);

        // Nearly invisible, but a Graphic is required for the row to receive the click.
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

        // Outline plate, hollow when off - the "blank check box" the game uses.
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

    // Scrollable vertical list. Tool panels outgrew their fixed height, so the options column
    // lives inside a ScrollRect. Returns the content transform to build into; `root` is the outer
    // object to show and hide.
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
        viewportRt.offsetMax = new Vector2(-16f, 0f);
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

        return contentRt;
    }

    private static Scrollbar CreateScrollbar(Transform parent)
    {
        var go = new GameObject("Scrollbar");
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(14f, 0f);
        rt.anchoredPosition = Vector2.zero;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.10f);

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
        handleImg.sprite = RoundedPlate;
        handleImg.type = Image.Type.Sliced;
        handleImg.pixelsPerUnitMultiplier = 3f;
        handleImg.color = new Color(1f, 1f, 1f, 0.45f);

        scrollbar.targetGraphic = handleImg;
        scrollbar.handleRect = handleRt;
        return scrollbar;
    }

    // A texture row for the option panels (blueprint snapshot previews). The outer row fixes
    // the height for the layout group; the inner RawImage letterboxes to the texture's aspect.
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

    // The game's own MMDropdown cannot be used here: its Open() walks UIMenuBase.ActiveMenus,
    // a stack the editor is deliberately not part of. This is the same interaction built on the
    // widgets we already have.
    //
    // Styled as a sunken field with its own arrow panel rather than as a plate, so it does not
    // read as one more button in the column.
    public MapEditorDropdown CreateDropdown(Transform parent, string caption, IList<string> options,
        Action<int, string> onSelected)
    {
        const float dropdownHeight = 44f;

        var row = new GameObject("Dropdown_" + caption);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(360, dropdownHeight);
        ApplyRowLayout(row, dropdownHeight);

        var field = AddPlate(row, FieldIdle);

        // A rounded plate on its own read as one more button in the column. The gold frame and
        // the red arrow block are what make this look like the control that opens the catalogue.
        AddOutline(rowRt, new Color(0.75f, 0.65f, 0.45f, 0.9f));

        var label = CreateLabel(row.transform, caption, 19);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(12f, 0f);
        labelRt.offsetMax = new Vector2(-54f, 0f);
        var labelText = label.GetComponent<TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        labelText.color = new Color(0.98f, 0.94f, 0.85f);

        var arrowPanel = new GameObject("Arrow");
        arrowPanel.transform.SetParent(row.transform, false);
        var arrowRt = arrowPanel.AddComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(1f, 0f);
        arrowRt.anchorMax = new Vector2(1f, 1f);
        arrowRt.pivot = new Vector2(1f, 0.5f);
        arrowRt.sizeDelta = new Vector2(44f, -8f);
        arrowRt.anchoredPosition = new Vector2(-4f, 0f);

        var arrowPlate = AddPlate(arrowPanel, Accent);
        arrowPlate.raycastTarget = false;

        var arrow = CreateLabel(arrowPanel.transform, "▼", 20, TextAlignmentOptions.Center);
        var glyphRt = arrow.GetComponent<RectTransform>();
        glyphRt.anchorMin = Vector2.zero;
        glyphRt.anchorMax = Vector2.one;
        glyphRt.offsetMin = Vector2.zero;
        glyphRt.offsetMax = Vector2.zero;
        arrow.GetComponent<TMP_Text>().raycastTarget = false;

        var dropdown = new MapEditorDropdown(this, row, rowRt, labelText, caption, onSelected);
        dropdown.SetOptions(options);

        AttachButton(row, field, dropdown.Toggle);
        AddHover(row, field, FieldIdle, FieldHover, null);
        return dropdown;
    }

    // Only one dropdown list may be open, and it must not survive a tool switch or the editor
    // closing - a floating overlay left behind would keep swallowing world clicks.
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

    public void CloseTransientUi() => _openDropdown?.Close();

    // ---- icon grid --------------------------------------------------------------------------

    // The vanilla build menu's grid of square icons, rebuilt for the editor: browsing 200 props
    // as a text list is what made the old tools unusable.
    public MapEditorGrid CreateIconGrid(Transform parent, string name, int columns = 4, float cellSize = 88f)
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

        var cells = new GameObject("Cells");
        cells.transform.SetParent(root.transform, false);
        cells.AddComponent<RectTransform>();

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

        return new MapEditorGrid(this, root, cells.transform, captionText);
    }

    // One square icon cell: a rounded plate that brightens under the cursor, a red selection
    // border behind it, the icon, and a letter tile that shows through until one arrives.
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

        // Drawn first and slightly larger than the plate, so only its rim shows: a border, not
        // a highlight that swallows the icon.
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

    // One control prompt, shaped like the game's own: a pale key cap with dark text, the action
    // spelled out beside it, on a dark brush-stroke plate.
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

        // Wide enough for "Ctrl" and "Del" without the cap turning into a bar.
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

    // Short enough to fit a tile: first letters of the first two words.
    public static string Initials(string label)
    {
        if (string.IsNullOrEmpty(label)) return "?";

        var parts = label.Split([' ', '_', '-', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return label.Substring(0, 1).ToUpperInvariant();
        if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
        return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
    }
}

// Row-level toggle state. Separate from the row's button so tools can push a value in without
// re-entering their own change handler.
public class MapEditorToggle : MonoBehaviour
{
    public Action<bool> OnValueChanged;
    public Image Fill;

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

        if (notify) OnValueChanged?.Invoke(value);
    }
}

// Brightens a plate under the cursor, and tells the status bar what the cursor is over. The
// editor has no tooltips, so the status bar is where a wordless icon gets its name.
public class MapEditorHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image Plate;
    public Color Idle;
    public Color Hover;

    // Shown in the status bar while hovered; null means this widget says nothing.
    public string HoverText;

    // Set by grids, whose cells are reused across groups and change what they represent.
    public Func<string> HoverTextProvider;

    // Lets a container react too - the icon grids echo the hovered name in their own caption.
    public Action<bool> OnHover;

    private bool _hovered;

    public void OnPointerEnter(PointerEventData eventData) => Apply(true);
    public void OnPointerExit(PointerEventData eventData) => Apply(false);

    private void OnDisable()
    {
        // A panel hidden under the cursor never gets its exit event.
        if (_hovered) Apply(false);
    }

    public void Apply(bool hovered)
    {
        _hovered = hovered;
        if (Plate != null) Plate.color = hovered ? Hover : Idle;
        OnHover?.Invoke(hovered);

        var text = HoverTextProvider != null ? HoverTextProvider() : HoverText;
        if (string.IsNullOrEmpty(text)) return;

        var editor = RuntimeMapEditor.Active;
        if (editor == null) return;

        if (hovered) editor.ShowHoverStatus(text);
        else editor.ClearHoverStatus();
    }
}
