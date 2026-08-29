using System;
using System.Collections.Generic;
using System.Text;
using Lamb.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

public static class VanillaWidgets
{
    private const int AudioSliderRow = 0;
    private const int GraphicsToggleRow = 4;
    private const int GraphicsHeaderRow = 0;

    private static GameObject _slider;
    private static GameObject _toggle;
    private static GameObject _header;
    private static Image _ribbon;
    private static bool _looked;

    private static readonly HashSet<string> _reported = [];

    public static bool Enabled => Plugin.MapEditorVanillaWidgets is not { Value: false };

    // ---- templates --------------------------------------------------------------------------

    private static void Look()
    {
        if (_slider != null && _toggle != null && _header != null && _ribbon != null) return;

        try
        {
            var manager = MonoSingleton<UIManager>.Instance;
            var settings = manager != null ? manager.SettingsMenuControllerTemplate : null;
            if (settings == null)
            {
                if (_looked) return;
                _looked = true;
                Plugin.Log.LogInfo("Map editor: the game's settings menu is not available; the " +
                                   "editor draws its own toggles and sliders.");
                return;
            }

            _looked = true;

            _slider = RowIn(settings._audioSettings, AudioSliderRow, typeof(MMSlider));
            _toggle = RowIn(settings._graphicsSettings, GraphicsToggleRow, typeof(MMToggle));
            _header = HeaderIn(settings, settings._graphicsSettings);
            _ribbon = RibbonIn(MonoSingleton<UIManager>.Instance.PauseMenuController)
                      ?? MenuHighlightRibbon();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Map editor: the game's settings widgets could not be read (" +
                                  e.Message + "); the editor draws its own.");
        }
    }

    private static GameObject HeaderIn(Component menu, Component page)
    {
        var ornate = Ornate(menu);
        if (ornate != null) return ornate;

        var content = page != null ? page.GetComponentInChildren<ScrollRect>(true)?.content : null;
        if (content == null) return null;

        for (var i = GraphicsHeaderRow; i < content.childCount; i++)
        {
            var row = content.GetChild(i).gameObject;
            if (row.GetComponentInChildren<TMP_Text>(true) == null) continue;
            if (row.GetComponentInChildren<Selectable>(true) != null) continue;
            return row;
        }

        Plugin.Log.LogWarning("Map editor: no heading in the game's settings menu; the editor " +
                              "draws its own.");
        return null;
    }

    private static Image RibbonIn(Component menu)
    {
        if (menu == null) return null;

        foreach (var image in menu.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null) continue;
            if (image.name != "ButtonBackground") continue;

            Plugin.Log.LogInfo($"Map editor: button plates take the pause menu's ribbon " +
                               $"('{image.sprite.name}', drawn {image.type}).");
            return image;
        }

        Plugin.Log.LogInfo("Map editor: the pause menu's button ribbon was not found; buttons keep " +
                           "the mod's own plate.");
        return null;
    }

    private static Image MenuHighlightRibbon()
    {
        var menu = ModUI.MenuEditor.MenuSceneRefs.Menu;
        var highlight = menu != null ? menu._buttonHighlight : null;
        var image = highlight != null ? highlight._image : null;
        if (image == null || image.sprite == null) return null;

        image.sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Plugin.Log.LogInfo("Map editor: button plates take the main menu's own ribbon " +
                           $"('{image.sprite.name}').");
        return image;
    }

    public static Image Ribbon
    {
        get
        {
            if (!Enabled) return null;
            Look();
            return _ribbon;
        }
    }

    public static Sprite QuietRibbon => Art("a quiet button plate", "Button Grey", "Button Black");

    public static Sprite DropdownArrow => Art("a dropdown arrow", "DropdownArrow", "WhiteArrow");

    private static readonly Dictionary<string, Sprite> _art = [];
    private static readonly HashSet<string> _artMissing = [];

    private static Sprite Art(string what, params string[] names)
    {
        if (!Enabled) return null;

        var key = names[0];
        if (_art.TryGetValue(key, out var found) && found != null) return found;
        if (_artMissing.Contains(key)) return null;

        try
        {
            var loaded = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var name in names)
            {
                foreach (var sprite in loaded)
                {
                    if (sprite == null || sprite.name != name) continue;

                    sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    _art[key] = sprite;
                    Plugin.Log.LogInfo($"Map editor: {what} taken from the game's '{name}'.");
                    return sprite;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Map editor: {what} could not be looked up: {e.Message}");
        }

        _artMissing.Add(key);
        Plugin.Log.LogInfo($"Map editor: no {what} in this game; the editor draws its own.");
        return null;
    }

    private static GameObject Ornate(Component menu)
    {
        if (menu == null) return null;

        foreach (var image in menu.GetComponentsInChildren<Image>(true))
        {
            var sprite = image != null ? image.sprite : null;
            if (sprite == null) continue;
            if (sprite.name.IndexOf("Divider", StringComparison.OrdinalIgnoreCase) < 0) continue;

            for (var walk = image.transform.parent; walk != null && walk != menu.transform; walk = walk.parent)
            {
                if (walk.GetComponentInChildren<TMP_Text>(true) == null) continue;

                Plugin.Log.LogInfo($"Map editor: headings taken from '{walk.name}', the game's own " +
                                   $"title bar (flourish '{sprite.name}').");
                return walk.gameObject;
            }
        }

        return null;
    }

    public static TMP_FontAsset RowFont
    {
        get
        {
            if (!Enabled) return null;
            Look();

            var text = _toggle != null ? _toggle.GetComponentInChildren<TMP_Text>(true) : null;
            return text != null ? text.font : null;
        }
    }

    private static GameObject RowIn(Component page, int index, Type carries)
    {
        var content = page != null ? page.GetComponentInChildren<ScrollRect>(true)?.content : null;
        if (content == null || index >= content.childCount) return null;

        var row = content.GetChild(index).gameObject;
        if (row.GetComponentInChildren(carries, true) != null) return row;

        for (var i = 0; i < content.childCount; i++)
        {
            var other = content.GetChild(i).gameObject;
            if (other.GetComponentInChildren(carries, true) == null) continue;

            Plugin.Log.LogInfo($"Map editor: the settings {carries.Name} moved from row {index} to " +
                               $"row {i}; taken from there.");
            return other;
        }

        Plugin.Log.LogWarning($"Map editor: no {carries.Name} in the game's settings menu; the " +
                              "editor draws its own.");
        return null;
    }

    // ---- toggle -------------------------------------------------------------------------------

    public static GameObject CreateToggle(Transform parent, string label, bool initial,
        Action<bool> onChanged, float rowHeight)
    {
        if (!Enabled) return null;
        Look();
        if (_toggle == null) return null;

        GameObject row;
        MMToggle toggle;
        try
        {
            row = Clone(_toggle, parent, "Toggle_" + label, rowHeight, out var host);
            toggle = host.GetComponentInChildren<MMToggle>(true);
            if (toggle == null) { UnityEngine.Object.Destroy(row); return null; }

            Caption(host, label);
            PushToEdge(host, toggle);

            toggle.OnValueChanged = null;

            var state = row.AddComponent<MapEditorToggle>();
            state.Vanilla = toggle;
            state.OnValueChanged = onChanged;
            state.SetValue(initial, notify: false);

            WholeRowClicks(row, host, state);
            Lights(row, host);

            Snap(toggle);

            toggle.OnValueChanged = value => state.SetValue(value, notify: true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Map editor: the game's toggle could not be dressed (" + e.Message +
                                  "); the editor draws its own.");
            return null;
        }

        return row;
    }

    private static void PushToEdge(GameObject host, MMToggle toggle)
    {
        var label = host.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;

        var rect = Fork(label.transform, toggle.transform);
        if (rect == null) return;

        var control = Branch(toggle.transform, rect);
        var caption = Branch(label.transform, rect);
        if (control == null || caption == null || control == caption)
        {
            Plugin.Log.LogInfo("Map editor: the settings toggle's name and control are not laid " +
                               "out side by side; it stays where the game puts it.");
            return;
        }

        var layout = rect.GetComponent<HorizontalLayoutGroup>();
        if (layout == null)
        {
            control.anchorMin = new Vector2(1f, control.anchorMin.y);
            control.anchorMax = new Vector2(1f, control.anchorMax.y);
            control.pivot = new Vector2(1f, control.pivot.y);
            control.anchoredPosition = new Vector2(0f, control.anchoredPosition.y);
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        var wanted = LayoutUtility.GetPreferredWidth(control);

        if (wanted <= 1f && toggle.transform is RectTransform pill) wanted = pill.rect.width;

        if (wanted <= 1f)
        {
            Plugin.Log.LogInfo("Map editor: the settings toggle does not say how wide it wants to " +
                               "be; it stays where the game puts it.");
            return;
        }

        layout.childForceExpandWidth = false;
        layout.childAlignment = TextAnchor.MiddleLeft;

        Slack(caption, 1f, -1f);
        Slack(control, 0f, wanted);
    }

    private static void Slack(RectTransform child, float flexible, float preferred)
    {
        var element = child.GetComponent<LayoutElement>();
        if (element == null) element = child.gameObject.AddComponent<LayoutElement>();

        element.flexibleWidth = flexible;
        if (preferred > 0f) element.preferredWidth = preferred;
        else element.minWidth = 0f;
    }

    private static RectTransform Branch(Transform node, RectTransform row)
    {
        for (var walk = node; walk != null; walk = walk.parent)
            if (walk.parent == row) return walk as RectTransform;

        return null;
    }

    private static RectTransform Fork(Transform a, Transform b)
    {
        var above = new HashSet<Transform>();
        for (var walk = a; walk != null; walk = walk.parent) above.Add(walk);

        for (var walk = b; walk != null; walk = walk.parent)
            if (above.Contains(walk)) return walk as RectTransform;

        return null;
    }

    private static void WholeRowClicks(GameObject row, GameObject host, MapEditorToggle state)
    {
        foreach (var graphic in host.GetComponentsInChildren<Graphic>(true))
            if (graphic != null) graphic.raycastTarget = false;

        var plate = row.AddComponent<Image>();
        plate.color = new Color(1f, 1f, 1f, 0.03f);

        MapEditorUI.AttachButton(row, plate, () => state.SetValue(!state.Value, notify: true));
    }

    private static void Unfocusable(GameObject host)
    {
        foreach (var selectable in host.GetComponentsInChildren<Selectable>(true))
        {
            if (selectable == null) continue;

            selectable.navigation = new Navigation { mode = Navigation.Mode.None };
            if (selectable.GetComponent<MapEditorNoFocus>() == null)
                selectable.gameObject.AddComponent<MapEditorNoFocus>();
        }

        foreach (var behaviour in host.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;
            if (!behaviour.GetType().Name.StartsWith("MMSelectable", StringComparison.Ordinal)) continue;
            behaviour.enabled = false;
        }
    }

    private static System.Reflection.MethodInfo _updateState;

    private static void Snap(MMToggle toggle)
    {
        _updateState ??= HarmonyLib.AccessTools.Method(typeof(MMToggle), "UpdateState");
        if (_updateState == null) return;

        try { _updateState.Invoke(toggle, [true]); }
        catch (Exception) { }
    }

    // ---- header -------------------------------------------------------------------------------

    public static GameObject CreateHeader(Transform parent, string text, float textSize, float rowHeight)
    {
        if (!Enabled) return null;
        Look();
        if (_header == null) return null;

        try
        {
            var row = Clone(_header, parent, "Header_" + text, rowHeight, out var host, textSize);

            Caption(host, text.Trim().Trim('-').Trim());

            var face = MapEditorUI.ButtonFont;
            if (face != null)
                foreach (var line in host.GetComponentsInChildren<TMP_Text>(true))
                    if (line != null) line.font = face;

            return row;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Map editor: the game's heading could not be dressed (" + e.Message +
                                  "); the editor draws its own.");
            return null;
        }
    }

    // ---- slider -------------------------------------------------------------------------------

    private const float SliderSteps = 200f;

    public static GameObject CreateSlider(Transform parent, string label, float min, float max,
        float initial, Action<float> onChanged, float rowHeight)
    {
        if (!Enabled) return null;
        Look();
        if (_slider == null) return null;
        if (max <= min) return null;

        try
        {
            var row = Clone(_slider, parent, "Slider_" + label, rowHeight, out var host);
            var slider = host.GetComponentInChildren<MMSlider>(true);
            if (slider == null) { UnityEngine.Object.Destroy(row); return null; }

            Caption(host, label);

            var scale = SliderSteps / (max - min);

            slider.onValueChanged.RemoveAllListeners();
            slider._increment = 1;
            slider.wholeNumbers = false;
            slider.minValue = min * scale;
            slider.maxValue = max * scale;

            slider._valueDisplayFormat = MMSlider.ValueDisplayFormat.Custom;
            slider.GetCustomDisplayFormat = raw => (raw / scale).ToString("0.##");

            var state = row.AddComponent<MapEditorSlider>();
            state.Slider = slider;
            state.Scale = scale;
            state.OnValueChanged = onChanged;
            state.SetValue(initial, notify: false);

            slider.onValueChanged.AddListener(raw => state.Raw(raw));

            Lights(row, host);
            return row;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Map editor: the game's slider could not be dressed (" + e.Message +
                                  "); the editor draws its own.");
            return null;
        }
    }

    // ---- cloning ------------------------------------------------------------------------------

    private static GameObject Clone(GameObject template, Transform parent, string name,
        float rowHeight, out GameObject host, float textSize = LabelSize)
    {
        var container = new GameObject(name);
        container.transform.SetParent(parent, false);
        var containerRt = container.AddComponent<RectTransform>();
        containerRt.sizeDelta = new Vector2(360f, rowHeight);

        var element = container.AddComponent<LayoutElement>();
        element.minHeight = rowHeight;
        element.preferredHeight = rowHeight;
        element.flexibleWidth = 1f;

        host = UnityEngine.Object.Instantiate(template, container.transform);
        host.name = "Vanilla";
        host.SetActive(true);

        Report(template == _toggle ? "toggle" : template == _slider ? "slider" : "heading", host);

        var natural = NaturalHeight(host);

        var fit = container.AddComponent<MapEditorScaledRow>();
        fit.Child = host.transform as RectTransform;
        fit.Scale = natural > 1f ? Mathf.Clamp(rowHeight / natural, 0.2f, 1f) : 1f;

        Readable(host, fit.Scale, textSize);
        Unfocusable(host);

        var own = host.GetComponent<LayoutElement>();
        if (own != null) own.ignoreLayout = true;

        return container;
    }

    private const float LabelSize = 18f;

    private static void Readable(GameObject row, float scale, float onScreen)
    {
        if (scale <= 0f) return;

        var size = onScreen / scale;

        var label = row.GetComponentInChildren<TMP_Text>(true);

        foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text == null) continue;

            text.enableAutoSizing = false;
            text.fontSize = size;

            text.enableWordWrapping = false;

            text.overflowMode = text == label ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;

            text.fontStyle = FontStyles.Normal;

            var styler = text.GetComponent<TextStyler>();
            if (styler != null) UnityEngine.Object.Destroy(styler);

            Rescaler(text, size);
        }
    }

    private static void Rescaler(TMP_Text text, float size)
    {
        var scaler = text.GetComponent<MMTextScaler>();
        if (scaler == null) return;

        try
        {
            scaler._originalFontSize = size;
            scaler._originalFontSizeMin = size;
            scaler._originalFontSizeMax = size;
        }
        catch (Exception)
        {
            UnityEngine.Object.Destroy(scaler);
        }
    }

    private static float NaturalHeight(GameObject row)
    {
        var element = row.GetComponent<LayoutElement>();
        var wanted = element != null ? Mathf.Max(element.preferredHeight, element.minHeight) : 0f;

        if (row.transform is RectTransform rect) wanted = Mathf.Max(wanted, rect.rect.height);
        return wanted > 1f ? wanted : 60f;
    }

    private static void Caption(GameObject row, string label)
    {
        var text = row.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null) return;

        var localize = text.GetComponent<I2.Loc.Localize>();
        if (localize != null) UnityEngine.Object.Destroy(localize);

        text.text = label;
    }

    private static readonly Color RowIdle = new(0.70f, 0.68f, 0.63f, 1f);
    private static readonly Color RowLit = new(1f, 0.96f, 0.86f, 1f);

    private static void Lights(GameObject row, GameObject host)
    {
        var hover = row.AddComponent<MapEditorRowHover>();
        hover.Texts = host.GetComponentsInChildren<TMP_Text>(true);
        hover.Idle = RowIdle;
        hover.Lit = RowLit;
        hover.Paint(false);
    }

    private static void Report(string kind, GameObject row)
    {
        if (!_reported.Add(kind)) return;

        var lines = new StringBuilder();
        Walk(row.transform, row.transform, lines);
        Plugin.Log.LogInfo($"Map editor: the game's {kind} row is made of -\n{lines}");
    }

    private static void Walk(Transform node, Transform root, StringBuilder into, int depth = 0)
    {
        var parts = new StringBuilder();
        foreach (var component in node.GetComponents<Component>())
        {
            if (component == null || component is Transform) continue;
            parts.Append(parts.Length == 0 ? "" : ", ").Append(component.GetType().Name);
        }

        var size = node is RectTransform rect ? $"{rect.rect.width:0}x{rect.rect.height:0}" : "-";
        into.Append(' ', 2 + depth * 2).Append(node == root ? "<row>" : node.name)
            .Append("  ").Append(size).Append("  ").Append(parts).Append('\n');

        for (var i = 0; i < node.childCount; i++) Walk(node.GetChild(i), root, into, depth + 1);
    }
}

public class MapEditorRowHover : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public TMP_Text[] Texts;
    public Color Idle;
    public Color Lit;

    private void OnEnable() => Paint(false);

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) => Paint(true);
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) => Paint(false);

    public void Paint(bool lit)
    {
        if (Texts == null) return;

        foreach (var text in Texts)
            if (text != null) text.color = lit ? Lit : Idle;
    }
}

public class MapEditorNoFocus : MonoBehaviour, UnityEngine.EventSystems.ISelectHandler
{
    public void OnSelect(UnityEngine.EventSystems.BaseEventData eventData)
    {
        var system = eventData != null && eventData.currentInputModule != null
            ? eventData.currentInputModule.eventSystem
            : UnityEngine.EventSystems.EventSystem.current;

        if (system != null && system.currentSelectedGameObject == gameObject)
            system.SetSelectedGameObject(null);
    }
}

public class MapEditorScaledRow : MonoBehaviour
{
    public RectTransform Child;
    public float Scale = 1f;

    private void OnEnable() => Fit();

    private void OnRectTransformDimensionsChange() => Fit();

    private void Fit()
    {
        if (Child == null || Scale <= 0f) return;
        if (transform is not RectTransform self) return;

        Child.localScale = new Vector3(Scale, Scale, 1f);
        Child.anchorMin = new Vector2(0.5f, 0.5f);
        Child.anchorMax = new Vector2(0.5f, 0.5f);
        Child.pivot = new Vector2(0.5f, 0.5f);
        Child.anchoredPosition = Vector2.zero;

        var size = self.rect.size;
        if (size.x <= 0f || size.y <= 0f) return;
        Child.sizeDelta = size / Scale;
    }
}

public class MapEditorSlider : MonoBehaviour
{
    public Action<float> OnValueChanged;

    public Slider Slider;

    public float Scale = 1f;

    public TMP_Text Readout;

    public float Value => Slider != null ? Slider.value / Scale : 0f;

    public void SetValue(float value, bool notify)
    {
        if (Slider == null) return;

        Slider.SetValueWithoutNotify(value * Scale);
        Show(value);

        if (notify) OnValueChanged?.Invoke(value);
    }

    internal void Raw(float raw)
    {
        var value = raw / Scale;
        Show(value);
        OnValueChanged?.Invoke(value);
    }

    private void Show(float value)
    {
        if (Readout != null) Readout.text = value.ToString("0.##");
    }
}
