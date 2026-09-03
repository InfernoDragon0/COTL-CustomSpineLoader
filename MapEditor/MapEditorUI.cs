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

    public static float DefaultIconPreviewRightOffset => PreviewRightOffset;

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
        rect.anchoredPosition = new Vector2(-IconPreviewRightOffset, -12f);

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
        rect.anchoredPosition = new Vector2(-PreviewRightOffset, -12f);

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

        var caret = VanillaWidgets.DropdownArrow;
        if (caret != null)
        {
            var glyph = new GameObject("Caret");
            glyph.transform.SetParent(arrowPanel.transform, false);

            var caretRt = glyph.AddComponent<RectTransform>();
            caretRt.anchorMin = new Vector2(0.5f, 0.5f);
            caretRt.anchorMax = new Vector2(0.5f, 0.5f);
            caretRt.pivot = new Vector2(0.5f, 0.5f);
            caretRt.sizeDelta = new Vector2(22f, 22f);
            caretRt.anchoredPosition = Vector2.zero;

            var caretImage = glyph.AddComponent<Image>();
            caretImage.sprite = caret;
            caretImage.preserveAspect = true;
            caretImage.color = new Color(0.98f, 0.94f, 0.85f);
            caretImage.raycastTarget = false;
        }
        else
        {
            AddPlate(arrowPanel, Accent).raycastTarget = false;
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
        IList<string> options, Action<int, string> onSelected, float split = 0.4f)
    {
        const float rowHeight = 44f;

        var row = new GameObject("LabelledDropdown_" + label);
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(360f, rowHeight);
        ApplyRowLayout(row, rowHeight);

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
