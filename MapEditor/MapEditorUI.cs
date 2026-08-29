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
    // Shared row height across the whole editor.
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

    private IMapEditorHost _editor;
    private RectTransform _canvasRoot;

    public RectTransform CanvasRoot => _canvasRoot;
    public IMapEditorHost Editor => _editor;

    // Whichever host built UI last. Static because the widgets that need it - MapEditorHover,
    // AttachButton - are static helpers on components the host never sees.
    //
    // Every editor attaches itself as it opens, so this is current whenever one is up. It can be
    // left pointing at a closed editor, which is the same no-op the two-host version had: a
    // status line written to a bar that is not on screen. Destroyed is the case that matters, and
    // CurrentHost answers that in Unity's terms rather than C#'s.
    private static IMapEditorHost _host;

    internal static IMapEditorHost CurrentHost
    {
        get
        {
            if (_host is MonoBehaviour behaviour) return behaviour == null ? null : _host;
            return _host;
        }
    }

    // Must run before any dropdown or grid is built.
    public void Attach(IMapEditorHost editor, RectTransform canvasRoot)
    {
        _editor = editor;
        _canvasRoot = canvasRoot;
        if (editor != null) _host = editor;
        WarmFonts();
    }

    // ---- rounded plate ----------------------------------------------------------------------

    private static Sprite _rounded;

    // 9-sliced rounded rectangle, generated at runtime - the mod ships no art.
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
                    // Distance past the corner arc; one pixel of edge softening.
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

    // Softer than the ribbon the pause menu paints, which is the cult red at full strength: this
    // one sits under a dozen buttons in a panel rather than one highlighted line in a menu, and
    // that much red at that many places reads as a warning rather than a button.
    private static readonly Color RibbonIdle = new(0.20f, 0.17f, 0.16f, 0.85f);
    private static readonly Color RibbonHover = new(0.55f, 0.33f, 0.28f, 0.95f);

    // Grey art, so these are near enough its own shades: a row in a list should read as part of the
    // list, not as a dozen things asking to be pressed.
    private static readonly Color QuietIdle = new(0.42f, 0.41f, 0.39f, 0.85f);
    private static readonly Color QuietHover = new(0.82f, 0.80f, 0.76f, 0.95f);

    // The pause menu's button art where it can be had, the mod's rounded plate until then. Inside a
    // list it is the grey cut of the same ribbon.
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

        // Either the caller says this is not the thing to reach for, or it is a row in a list and
        // nothing in a list is.
        var quiet = emphasis == MapEditorEmphasis.Quiet ||
                    go.GetComponentInParent<MapEditorQuietArea>() != null
            ? VanillaWidgets.QuietRibbon
            : null;

        idle = quiet != null ? QuietIdle : RibbonIdle;
        hover = quiet != null ? QuietHover : RibbonHover;

        var image = go.AddComponent<Image>();
        image.sprite = quiet != null ? quiet : source.sprite;
        image.type = source.type;
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

    // Headers built before the async heading font arrives; re-fonted on arrival.
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
                    // No font coming; clear or the static list accumulates destroyed TMP_Texts.
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
                // The settings rows' own face first, so the editor's writing matches the widgets
                // borrowed from them rather than sitting beside them in a different hand.
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

    // What the game writes its own menu buttons in - the pause menu's face. Kept apart from the
    // panel's writing on purpose: the two are different faces in the game and reading them as one
    // would flatten the difference between a row and a thing you press.
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

    // The game's menus never go below this.
    private const int MinFontSize = 17;

    // MMTextScaler captures the font size in OnEnable; add only once the size is final.
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

    // Pins row height for the layout group; width is driven by the column.
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

    // Gives a label the height its wrapped text actually needs. Every row is pinned to one line by
    // ApplyRowLayout, so a label that runs to three draws straight over whatever comes next.
    // Measured rather than laid out: this is called while the column is still being built, before
    // it has had a layout pass.
    public static void FitLabelHeight(GameObject label, float width = 380f)
    {
        if (label == null) return;

        var text = label.GetComponent<TMP_Text>();
        var element = label.GetComponent<LayoutElement>();
        if (text == null || element == null) return;

        var content = text.text ?? "";

        // Measured, with a floor taken from the line count. TMP's measurement wants a font that has
        // finished loading and a mesh that has been generated at least once; asked too early it
        // answers short, and a label that answers short draws straight over the button under it.
        var lines = 1;
        foreach (var character in content)
            if (character == '\n') lines++;

        var measured = text.GetPreferredValues(content, width, 0f).y;
        var needed = Mathf.Max(measured, lines * text.fontSize * 1.35f) + 6f;

        if (Mathf.Abs(element.preferredHeight - needed) < 0.5f) return;

        element.minHeight = needed;
        element.preferredHeight = needed;

        // **And the rect itself**, which is the part that actually matters here and the reason a
        // grown label kept drawing over the widget below it. A tool's options column is a
        // VerticalLayoutGroup with `childControlHeight = false`, and in that mode the group asks
        // each child for `sizeDelta.y` and never looks at its LayoutElement at all. So a label that
        // had grown to five lines still counted as the one row `ApplyRowLayout` gave it, the next
        // widget was placed 25px down, and the extra lines drew straight through it. Marking the
        // parent dirty only re-ran a layout that was reading the wrong number.
        //
        // The LayoutElement is still set, for a parent that *does* control height.
        if (label.transform is RectTransform rect)
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, needed);

        if (label.transform.parent is RectTransform parent) LayoutRebuilder.MarkLayoutForRebuild(parent);
    }

    // Section heading in the game's heading font.
    public GameObject CreateHeader(Transform parent, string text, int size = 24)
    {
        var borrowed = VanillaWidgets.CreateHeader(parent, text, size, size + 14f);
        if (borrowed != null) return borrowed;

        return CreateHeadingLabel(parent, text, size);
    }

    // The editor's own heading, drawn rather than borrowed. The borrowed one keeps its writing on a
    // child, so anything that wants the text component itself asks for this.
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
            // The editors poll the mouse themselves, and a click on a widget must not also read as
            // a click on whatever is underneath it.
            CurrentHost?.BlockWorldClicks();

            onClick?.Invoke();
        }

        try
        {
            var button = go.AddComponent<MMButton>();
            button._targetGraphics = new MaskableGraphic[] { graphic };
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;

            // The editor's buttons are clicked, never navigated to. Without this, MMButton's
            // OnPointerEnter hands the hovered button to UINavigatorNew as its current selectable,
            // and that navigator polls Rewired's accept binding - E on a keyboard - straight from
            // its own Update, outside the EventSystem entirely. So E pressed anywhere fired
            // whichever editor button the cursor had last passed over, including while typing into
            // a prompt, and suspending the EventSystem did nothing about it because that path never
            // goes near the EventSystem. It is worse than "while hovered": OnPointerExit never
            // clears the navigator's selectable, so the last button stayed armed indefinitely.
            // Clicks are unaffected - OnPointerClick does not consult this - and the hover state it
            // skips is Unity's, which we do not use (transition is None, MapEditorHover draws ours).
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

    // Hover brightens the plate and feeds the status bar.
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

        // A button is not a settings row: it keeps the menu face the game writes its own buttons
        // in, which is a heavier one than the panel's writing.
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

        // The same handle tools reach for on the game's row, so neither of them has to know which
        // one it got.
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

    // A cell is 60-ish pixels of a prop that may be a hundred times that on the ground, so half the
    // catalog reads as the same brown smudge. Hovering one blows its icon up beside the options
    // panel - drawn from the sprite the cell already holds, so it costs a texture draw and nothing
    // else, and it needs no click the way the ghost preview does.
    private const float PreviewSize = 300f;

    // Clear of the room editor's options panel (420 wide at a 12px margin).
    private const float PreviewRightOffset = 448f;

    private GameObject _previewGO;
    private Image _previewImage;
    private TMP_Text _previewCaption;

    // How far in from the right edge the hover preview sits. The default clears the room editor's
    // own panel; a screen that puts a wider panel there raises this while it is up, or the preview
    // lands on top of the very grid it is previewing.
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

        // Placed on every show rather than once at build: the offset belongs to whichever screen is
        // open, and the preview outlives all of them.
        var rect = (RectTransform)_previewGO.transform;
        rect.anchoredPosition = new Vector2(-IconPreviewRightOffset, -12f);

        // And raised to the front for the same reason. Sibling order is draw order on a canvas, and
        // this is built the first time anything hovers a cell - so any full-screen tool opened after
        // that is a later sibling and draws straight over it. Raising it on show costs nothing and
        // does not care which screens have come and gone.
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

        // Never a click target: it floats over the map and the cursor is on the grid behind it.
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

        // Gone entirely when the content fits: a rail against a list that cannot scroll is a
        // control that does nothing. AutoHide rather than AutoHideAndExpandViewport, because the
        // viewport's inset is set by hand above and that mode would fight it for the two pixels.
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        return contentRt;
    }

    // A thin rail rather than a bar: the wide one ate into the last column of icons, and a list
    // of pictures should not be framed by a slab of grey. The handle takes the editor's accent, so
    // it reads as the game's own red.
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

        // A plain rectangle: rounding a six-pixel rail only makes it look chewed.
        var handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.9f);

        scrollbar.targetGraphic = handleImg;
        scrollbar.handleRect = handleRt;
        return scrollbar;
    }

    // Fixed-height texture row; the inner RawImage letterboxes to the texture's aspect.
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

        // The game's own caret where it can be had: a bare triangle, no plate behind it, which is
        // how the settings menu ends a dropdown.
        //
        // The fallback is the plain accent tile this used to be. It used to carry a typed arrow as
        // well, which never drew a thing - the game's font has no glyph at that code point - so
        // what stood here was always just the square.
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

    // One dropdown open at a time; a stranded overlay would keep swallowing world clicks.
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

    // An open dropdown list stands outside every registered blocker rect, so a surface that polls
    // its own clicks has to know to close it rather than act on the click underneath.
    public bool TransientUiOpen => _openDropdown != null;

    public void CloseTransientUi() => _openDropdown?.Close();

    // A boxed, scrolling list of rows. maxHeight is a ceiling, not a size: the box is as tall as
    // its rows need and no taller, so a short list does not sit in a pane of empty black.
    public MapEditorScrollBox CreateScrollBox(Transform parent, string name, float maxHeight,
        float rowHeight = 30f, float spacing = 3f)
    {
        var content = CreateScrollColumn(parent, name, out var root, spacing);
        content.gameObject.AddComponent<MapEditorQuietArea>();
        return new MapEditorScrollBox(this, content, root, maxHeight, rowHeight, spacing);
    }

    // ---- icon grid --------------------------------------------------------------------------

    // scrollHeight: box the cells in a scroll view of their own that tall, instead of letting the
    // grid grow the whole panel. A catalogue of hundreds pushed the search field and the group
    // picker off the top of the tool's own column, so reaching them meant scrolling back up past
    // everything you had just scrolled down through. With the cells boxed, the column above them
    // does not move and the hovered-name caption stays pinned under the box. Zero keeps the old
    // grow-to-fit behaviour, which suits the short lists.
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

        // Where the cells go: straight into the root, or into a scroll box hung from it.
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

        // Inside a scroll box the grid has to report its own height, or the column that scrolls it
        // has nothing to measure and the box never scrolls.
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

        // The box grows with the list up to its ceiling, rather than reserving the full height for
        // three icons: a short group should not sit in a pane of empty black.
        if (boxElement != null) built.UseScrollBox(boxElement, scrollHeight, columns, cellSize, 6f);

        return built;
    }

    // One square icon cell; the letter tile shows until an icon arrives.
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

        // Drawn first and slightly larger than the plate, so only its rim shows.
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

    // One control prompt in the game's key-cap style.
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

// Toggle state kept separate from the button, so tools can push a value without re-entering
// their own change handler.
public class MapEditorToggle : MonoBehaviour
{
    public Action<bool> OnValueChanged;

    // The editor's own check box, or the game's settings toggle. Exactly one of them.
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

        // Its setter animates on a change and stays quiet on a match, and it never calls back - so
        // a value the player set on the control itself lands here without bouncing.
        if (Vanilla != null) Vanilla.Value = value;

        if (notify) OnValueChanged?.Invoke(value);
    }
}

// How loudly a button asks to be pressed.
//
// Action is the thing the panel is for - place this, save that. Quiet is everything that undoes,
// clears or backs out: still one click away, but not what the eye should land on first. A panel
// where every button is accented has no accent at all.
public enum MapEditorEmphasis
{
    Action,
    Quiet
}

// Marks a container whose buttons are rows in a list rather than things to press: they wear the
// quiet cut of the button art, so a list of twenty does not read as twenty invitations.
public class MapEditorQuietArea : MonoBehaviour
{
}

// Brightens a plate under the cursor and feeds the status bar (the editor has no tooltips).
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

        // Whichever editor owns the screen right now; they are never open together.
        var host = MapEditorUI.CurrentHost;
        if (host == null) return;

        if (hovered) host.ShowHoverStatus(text);
        else host.ClearHoverStatus();
    }
}
