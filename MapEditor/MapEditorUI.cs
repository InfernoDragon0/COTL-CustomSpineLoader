using System;
using COTL_API.UI.Helpers;
using Lamb.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

// Builds the editor chrome out of the game's own widgets.
//
// Sliders and toggles are cloned from the live settings menu (the same harvesting trick
// Commands/CustomColorCommand.cs already relies on). Buttons get a real MMButton component
// added to our own GameObject, which gives us the game's selection states and confirm SFX
// without needing a prefab to clone.
//
// Every helper degrades to plain uGUI if the settings menu has never been opened, because
// SettingsMenuControllerTemplate is null until then.
public class MapEditorUI
{
    private GameObject _sliderTemplate;
    private GameObject _toggleTemplate;
    private bool _templatesResolved;

    // Settings-menu rows are authored around this height.
    private const float SettingsRowHeight = 56f;

    public bool UsingGameWidgets => _sliderTemplate != null && _toggleTemplate != null;

    private void ResolveTemplates()
    {
        if (_templatesResolved) return;
        _templatesResolved = true;

        try
        {
            var settings = MonoSingleton<UIManager>.Instance?.SettingsMenuControllerTemplate;
            if (settings == null)
            {
                Plugin.Log.LogWarning("MapEditor: settings menu template unavailable, falling back to plain UI widgets.");
                return;
            }

            // Same child indices CustomColorCommand uses: audio row 0 is a slider, graphics row 4 a toggle.
            _sliderTemplate = settings._audioSettings.GetComponentInChildren<ScrollRect>()
                .content.GetChild(0).gameObject;
            _toggleTemplate = settings._graphicsSettings.GetComponentInChildren<ScrollRect>()
                .content.GetChild(4).gameObject;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not harvest game UI templates, falling back to plain widgets: " + e.Message);
            _sliderTemplate = null;
            _toggleTemplate = null;
        }
    }

    private static TMP_FontAsset _cachedFont;

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

    // Rows cloned out of the settings menu keep anchors that stretched to their original parent.
    // Dropped into a layout group those collapse to almost no width, which is why the labels
    // looked squashed. Pinning an explicit height and letting the column drive width fixes it.
    private static void ApplyRowLayout(GameObject go, float height)
    {
        var element = go.GetComponent<LayoutElement>();
        if (element == null) element = go.AddComponent<LayoutElement>();

        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleWidth = 1f;
    }

    public GameObject CreateLabel(Transform parent, string text, int size = 20, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
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

        ApplyRowLayout(go, size + 12f);
        return go;
    }

    public GameObject CreateButton(Transform parent, string text, Action onClick)
    {
        var go = new GameObject("Btn_" + text);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(190, 34);
        ApplyRowLayout(go, 34f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.16f, 0.14f, 0.12f, 0.95f);

        // MMButton is the game's own button class, so we inherit its confirm SFX and selection
        // states. It drives _targetGraphics during transitions, so that must be populated.
        MMButton button;
        try
        {
            button = go.AddComponent<MMButton>();
            button._targetGraphics = new MaskableGraphic[] { img };
            button.targetGraphic = img;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: MMButton unavailable, using plain Button: " + e.Message);
            var plain = go.AddComponent<Button>();
            plain.targetGraphic = img;
            plain.onClick.AddListener(() => onClick?.Invoke());
            CreateButtonLabel(go.transform, text);
            return go;
        }

        button.onClick.AddListener(() => onClick?.Invoke());
        CreateButtonLabel(go.transform, text);
        return go;
    }

    private void CreateButtonLabel(Transform parent, string text)
    {
        var label = CreateLabel(parent, text, 18, TextAlignmentOptions.Center);
        var rt = label.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // Returns the row GameObject. min/max are the real value range; the underlying MMSlider works
    // in whole increments, so we map through it rather than exposing raw slider units to callers.
    public GameObject CreateSlider(Transform parent, string label, float min, float max, float initial, Action<float> onChanged)
    {
        ResolveTemplates();

        if (_sliderTemplate == null)
            return CreateFallbackSlider(parent, label, min, max, initial, onChanged);

        var row = UnityEngine.Object.Instantiate(_sliderTemplate, parent);
        row.name = "Slider_" + label;
        row.SetActive(true);
        ApplyRowLayout(row, SettingsRowHeight);

        var text = row.GetComponentInChildren<TMP_Text>();
        if (text != null) text.text = label;

        var slider = row.GetComponentInChildren<MMSlider>();
        if (slider == null) return row;

        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider._increment = 1;
        slider.GetCustomDisplayFormat = v => (min + (max - min) * (v / 100f)).ToString("0.##");
        slider.value = Mathf.InverseLerp(min, max, initial) * 100f;
        slider.onValueChanged.AddListener(v => onChanged?.Invoke(Mathf.Lerp(min, max, v / 100f)));
        return row;
    }

    private GameObject CreateFallbackSlider(Transform parent, string label, float min, float max, float initial, Action<float> onChanged)
    {
        var row = new GameObject("Slider_" + label);
        row.transform.SetParent(parent, false);
        var rt = row.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(360, 30);
        ApplyRowLayout(row, 34f);

        CreateLabel(row.transform, label, 16);

        var sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(row.transform, false);
        var srt = sliderGO.AddComponent<RectTransform>();
        srt.sizeDelta = new Vector2(160, 18);
        srt.anchoredPosition = new Vector2(110, 0);

        var bg = sliderGO.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

        var slider = sliderGO.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = initial;
        slider.onValueChanged.AddListener(v => onChanged?.Invoke(v));
        return row;
    }

    public GameObject CreateToggle(Transform parent, string label, bool initial, Action<bool> onChanged)
    {
        ResolveTemplates();

        if (_toggleTemplate == null)
            return CreateFallbackToggle(parent, label, initial, onChanged);

        var row = UnityEngine.Object.Instantiate(_toggleTemplate, parent);
        row.name = "Toggle_" + label;
        row.SetActive(true);
        ApplyRowLayout(row, SettingsRowHeight);

        var text = row.GetComponentInChildren<TMP_Text>();
        if (text != null) text.text = label;

        var toggle = row.GetComponentInChildren<MMToggle>();
        if (toggle == null) return row;

        toggle.Value = initial;
        toggle.OnValueChanged += v => onChanged?.Invoke(v);
        return row;
    }

    private GameObject CreateFallbackToggle(Transform parent, string label, bool initial, Action<bool> onChanged)
    {
        var state = initial;
        GameObject row = null;
        row = CreateButton(parent, label + ": " + (initial ? "ON" : "OFF"), () =>
        {
            state = !state;
            var t = row.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = label + ": " + (state ? "ON" : "OFF");
            onChanged?.Invoke(state);
        });
        return row;
    }

    // Scrollable vertical list. Tool panels outgrew their fixed height, so the options column
    // lives inside a ScrollRect. Returns the content transform to build into; `root` is the outer
    // object to show and hide.
    public RectTransform CreateScrollColumn(Transform parent, string name, out GameObject root, float spacing = 6f)
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
        layout.padding = new RectOffset(12, 12, 12, 12);

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
        handleImg.color = new Color(1f, 1f, 1f, 0.45f);

        scrollbar.targetGraphic = handleImg;
        scrollbar.handleRect = handleRt;
        return scrollbar;
    }

    // Vertical list container with padding, used for the tool option panels.
    public RectTransform CreateColumn(Transform parent, string name, float spacing = 6f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;

        // Width is driven by the column so cloned settings rows fill it instead of collapsing;
        // height stays with each row's own LayoutElement.
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(12, 12, 12, 12);
        return rt;
    }
}
