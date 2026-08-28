using System;
using System.Collections.Generic;
using System.Text;
using Lamb.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

// The game's own settings toggle and slider, cloned into the editor's option panels.
//
// The rows come from UIManager's settings menu prefab - a plain serialized reference, so it is
// always loaded and there is no addressable to wait on. COTL_API has helpers that clone the same
// rows, but they are internal and their templates are captured from a *live* settings menu, so they
// are null until the player has opened Settings once and dangling after they close it again. The
// prefab has neither problem, and this repo already reads it in CustomColorCommand.
//
// The rows are built for a full-screen settings list and are roughly twice the height of an editor
// row, so each clone is scaled down inside a container that keeps its place in the column; see
// MapEditorScaledRow for why that is not just a localScale.
public static class VanillaWidgets
{
    // Where each row sits in the settings prefab. Positional, so each one is checked for the
    // component it is supposed to carry rather than trusted - a game update reordering these would
    // otherwise hand back the wrong widget silently.
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

    // Retried while it comes up empty - the panels are built once per editor session, and a session
    // opened before UIManager was up should not settle for plain widgets forever. Only the logging
    // is once.
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
            _ribbon = RibbonIn(MonoSingleton<UIManager>.Instance.PauseMenuController);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Map editor: the game's settings widgets could not be read (" +
                                  e.Message + "); the editor draws its own.");
        }
    }

    // The heading the game puts at the top of a menu: a title flanked by two flourishes. There is
    // no component that says "heading", but the flourishes are the game's divider art, so a divider
    // is what this looks for - and the node above it that also holds writing is the heading.
    private static GameObject HeaderIn(Component menu, Component page)
    {
        var ornate = Ornate(menu);
        if (ornate != null) return ornate;

        // Otherwise a plain one: the first row of a settings page that is only writing.
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

    // The pause menu's buttons have no plate of their own: one 'ButtonBackground' sits behind the
    // whole column and moves to whichever button is up. Only its art is wanted here, since our
    // buttons stand in different places and each keeps a plate of its own.
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

    // The ribbon the pause menu draws behind a button, for the editor's own buttons to wear.
    public static Image Ribbon
    {
        get
        {
            if (!Enabled) return null;
            Look();
            return _ribbon;
        }
    }

    // The same ribbon in grey. The pause menu's is 'Button Red' - red paint, not white art with a
    // red tint - so a row inside a list cannot be quietened by tinting it; that only makes a darker
    // red. The game draws the same shape in other colours, and the grey one is what a list wants.
    public static Sprite QuietRibbon => Art("a quiet button plate", "Button Grey", "Button Black");

    // The caret the game puts at the end of a settings dropdown.
    public static Sprite DropdownArrow => Art("a dropdown arrow", "DropdownArrow", "WhiteArrow");

    private static readonly Dictionary<string, Sprite> _art = [];
    private static readonly HashSet<string> _artMissing = [];

    // A sprite by name, out of everything the game has loaded. These are not addressable and are on
    // no prefab we already hold - but they live in atlases the menus have already brought in, so
    // they are there to be found. The sweep is not cheap, hence once per name, either way.
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

                    // It must survive a resource sweep between editor sessions; the panel drawing
                    // it holds no other claim on it.
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

    // The face the game's own settings rows are written in, so the editor's own writing can match
    // it. Read from the template rather than a clone: labels are built before any row is.
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

        // Off by a row after a game update: the neighbours are cheap to check and a wrong widget
        // is not.
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

    // Returns null when the game's row cannot be had, and the caller draws its own.
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

            // The template is a settings row and arrives wired to a setting. Ours is not that.
            toggle.OnValueChanged = null;

            var state = row.AddComponent<MapEditorToggle>();
            state.Vanilla = toggle;
            state.OnValueChanged = onChanged;
            state.SetValue(initial, notify: false);

            WholeRowClicks(row, host, state);
            Lights(row, host);

            // The control only redraws itself when its value *changes*, so one that starts on the
            // value the prefab shipped would otherwise wear the wrong face until first clicked.
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

    // A settings row hands its label and its control an equal share of the width, which reads well
    // across a full settings screen and puts the control adrift in the middle of a narrow panel.
    // The label is given the slack instead, so the control lands against the right edge.
    //
    // Only the toggle. The slider's bar is meant to stretch, and the same treatment would leave it
    // at whatever width it happened to prefer.
    private static void PushToEdge(GameObject host, MMToggle toggle)
    {
        var label = host.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;

        // Not the row's own layout: the name and the control are not always its direct children -
        // in this row they share a wrapper below it, which is why aiming at the root did nothing.
        // Whichever node holds them both is the one that splits the width between them.
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
            // Nothing dividing the width, so the control is placed by its anchors and moving it is
            // a matter of anchoring it to the right instead of wherever it was.
            control.anchorMin = new Vector2(1f, control.anchorMin.y);
            control.anchorMax = new Vector2(1f, control.anchorMax.y);
            control.pivot = new Vector2(1f, control.pivot.y);
            control.anchoredPosition = new Vector2(0f, control.anchoredPosition.y);
            return;
        }

        // Measured, not read off the rect: these widths are computed by the row's own layout at
        // runtime, and the rects hold nothing meaningful this early. A row that cannot say how wide
        // its control wants to be keeps the game's split rather than collapsing it to nothing.
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        var wanted = LayoutUtility.GetPreferredWidth(control);

        // That branch is a bare wrapper - no graphic, no layout of its own - so it reports nothing,
        // and the size that means anything is the control's own, which is a fixed one it was drawn
        // at rather than something a layout decides.
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

    // Which of the row's own children this node sits under.
    private static RectTransform Branch(Transform node, RectTransform row)
    {
        for (var walk = node; walk != null; walk = walk.parent)
            if (walk.parent == row) return walk as RectTransform;

        return null;
    }

    // The lowest node both of these sit under - the one whose layout decides how the width is
    // divided between them.
    private static RectTransform Fork(Transform a, Transform b)
    {
        var above = new HashSet<Transform>();
        for (var walk = a; walk != null; walk = walk.parent) above.Add(walk);

        for (var walk = b; walk != null; walk = walk.parent)
            if (above.Contains(walk)) return walk as RectTransform;

        return null;
    }

    // The name is part of the control, the way it is on the editor's own rows: anywhere on the row
    // flips it, the pill included.
    //
    // Every graphic in the borrowed row stops taking clicks and one plate behind the lot of them
    // takes all of them. Turning off the row's own background was not enough - the name, the pill
    // and the wrapper around them all raycast in their own right, and a child graphic is picked
    // before its parent's, so the clicks never reached down here. Routing the pill through this
    // plate as well means there is one way in rather than two, and the toggle's own button is left
    // with nothing to receive - which is also what keeps it from taking the keyboard.
    private static void WholeRowClicks(GameObject row, GameObject host, MapEditorToggle state)
    {
        foreach (var graphic in host.GetComponentsInChildren<Graphic>(true))
            if (graphic != null) graphic.raycastTarget = false;

        var plate = row.AddComponent<Image>();
        plate.color = new Color(1f, 1f, 1f, 0.03f);

        MapEditorUI.AttachButton(row, plate, () => state.SetValue(!state.Value, notify: true));
    }

    // The borrowed controls are Selectables, and a Selectable that holds the focus answers the
    // keyboard: arrow keys walk a slider's value, submit flips a toggle. The editor reads those
    // keys itself, so nothing borrowed is allowed to keep the focus - it is handed straight back
    // the moment it is given. Dragging is unaffected; that runs off pointer events, not selection.
    private static void Unfocusable(GameObject host)
    {
        foreach (var selectable in host.GetComponentsInChildren<Selectable>(true))
        {
            if (selectable == null) continue;

            selectable.navigation = new Navigation { mode = Navigation.Mode.None };
            if (selectable.GetComponent<MapEditorNoFocus>() == null)
                selectable.gameObject.AddComponent<MapEditorNoFocus>();
        }

        // Unity's own focus is only half of it: the game drives its menus through a navigator of
        // its own, and the MMSelectable_* scripts are what a row uses to answer it - a slider walks
        // its value on left and right whether or not Unity thinks anything is selected. Switched
        // off rather than removed; only their input is in the way, and something else on the row
        // may still hold a reference to them.
        foreach (var behaviour in host.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue;
            if (!behaviour.GetType().Name.StartsWith("MMSelectable", StringComparison.Ordinal)) continue;
            behaviour.enabled = false;
        }
    }

    private static System.Reflection.MethodInfo _updateState;

    // Redraws a toggle without animating it. Private on the game's side, so it is called by name.
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

            // The dashes were the editor drawing its own rule around a heading. The borrowed one
            // brings the game's flourishes with it, so they would now be a second set of brackets.
            Caption(host, text.Trim().Trim('-').Trim());

            // The menu face the buttons use, not the settings rows': a heading names a section the
            // way a button names what it does, and both stand apart from the writing inside a row.
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

    // MMSlider rounds every value it is given to a whole number of increments, and the increment is
    // an int - so a 0-to-1 slider driven through it can only ever be 0 or 1. The row is therefore
    // given a whole-numbered range of its own and the real value is scaled in and out of it, which
    // is what MapEditorSlider carries. Its readout is told the same, so it still reads in the
    // caller's units.
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

            // Told in the caller's units, so a fog distance still reads as a fog distance.
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

    // The clone goes inside a container that is what the column actually lays out, at our row
    // height and full width; the clone itself is scaled to fit inside it.
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

        // What the row wants to be, before it is squeezed into ours.
        var natural = NaturalHeight(host);

        var fit = container.AddComponent<MapEditorScaledRow>();
        fit.Child = host.transform as RectTransform;
        fit.Scale = natural > 1f ? Mathf.Clamp(rowHeight / natural, 0.2f, 1f) : 1f;

        // The row's own layout is left exactly as the game drew it. It hands the label and the
        // control an equal share of the width, which puts the control nearer the middle than the
        // edge - but the widths inside it are computed at runtime, and overriding them from what
        // the rects read at clone time collapsed the slider to nothing.
        Readable(host, fit.Scale, textSize);
        Unfocusable(host);

        // A settings row sizes itself off its own layout; inside ours it is driven by the fitter.
        var own = host.GetComponent<LayoutElement>();
        if (own != null) own.ignoreLayout = true;

        return container;
    }

    // What the row's text should measure once the row has been scaled down, in screen pixels. A
    // point above the editor's own 17: these labels are the game's settings face, which is lighter
    // and more spaced out than the panel's, and it reads smaller at the same size.
    private const float LabelSize = 18f;

    // The row shrinks; its writing does not. Scaling a settings row to editor height takes its text
    // down with it, which is the one part of the design that cannot afford it - so each label is
    // given back the size it loses, in the row's own units, and lands on screen at LabelSize.
    private static void Readable(GameObject row, float scale, float onScreen)
    {
        if (scale <= 0f) return;

        var size = onScreen / scale;

        // The row's name, as everywhere else here: the first text in it.
        var label = row.GetComponentInChildren<TMP_Text>(true);

        foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text == null) continue;

            // Auto-sizing would fit the text back to the box and undo this immediately.
            text.enableAutoSizing = false;
            text.fontSize = size;

            // One line, always. A settings row is wide enough to wrap and grow; ours is a fixed
            // height, so a second line draws over the row below it.
            text.enableWordWrapping = false;

            // A clipped name still reads; a clipped number does not, and a slider's readout box was
            // drawn for text a good deal smaller than it now holds. It is let out of its box rather
            // than cut off - it sits at the row's right edge with the panel's margin beyond it.
            text.overflowMode = text == label ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;

            // Upright, so the borrowed rows read as the same writing as the panel around them. The
            // slant is a style on the text rather than a font of its own, and the row's TextStyler
            // would put it back on the next enable - so that goes. It exists only to strip styling
            // for accessibility, which is the very thing being done here.
            text.fontStyle = FontStyles.Normal;

            var styler = text.GetComponent<TextStyler>();
            if (styler != null) UnityEngine.Object.Destroy(styler);

            Rescaler(text, size);
        }
    }

    // The game's accessibility text scaling reads the size the text had when it first woke up and
    // re-applies it every time the object is enabled again - and a tool's option column is built,
    // hidden, and shown again when the tool is picked. Setting the size alone therefore held only
    // until the panel was first opened. The remembered size is moved to ours instead of the scaler
    // being torn out, so the player's text scale still multiplies it.
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
            // Whatever it remembers, it cannot re-apply it once it is gone.
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
        // The first text in a settings row is its name; a slider's readout comes after it.
        var text = row.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null) return;

        // The game localises these by key, and a key it does not know draws as the key.
        var localize = text.GetComponent<I2.Loc.Localize>();
        if (localize != null) UnityEngine.Object.Destroy(localize);

        text.text = label;
    }

    // Idle and hovered writing on a row. The editor's own rows have always lit up under the
    // cursor - it is how a panel with no tooltips says a row can be used at all - and a borrowed
    // row that stays one shade reads as disabled beside them.
    private static readonly Color RowIdle = new(0.70f, 0.68f, 0.63f, 1f);
    private static readonly Color RowLit = new(1f, 0.96f, 0.86f, 1f);

    // Put on the container rather than the borrowed row: a pointer entering a child is reported to
    // its ancestors too, so one handler up here covers the whole row however deep the cursor lands.
    private static void Lights(GameObject row, GameObject host)
    {
        var hover = row.AddComponent<MapEditorRowHover>();
        hover.Texts = host.GetComponentsInChildren<TMP_Text>(true);
        hover.Idle = RowIdle;
        hover.Lit = RowLit;
        hover.Paint(false);
    }

    // What a settings row is actually made of, once per kind. The shape matters as much as the
    // parts: which node holds the name and which holds the control decides where the width can be
    // divided, and that is not something to keep guessing at from a screenshot.
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

// Lights a row's writing while the cursor is on it, anywhere on it.
public class MapEditorRowHover : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public TMP_Text[] Texts;
    public Color Idle;
    public Color Lit;

    // The game's own accessibility pass can repaint these when a panel is shown again.
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

// Refuses the keyboard focus, so a borrowed control cannot answer keys the editor is reading.
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

// A row laid out at our size with the game's own row scaled to fit inside it.
//
// A localScale on its own is not enough: the column controls its children's width, so a scaled row
// would be laid out at full width and then drawn at half of it. The container takes the layout and
// the child is sized to the container divided by the scale, which lands it back at exactly the
// container's bounds once scaled.
public class MapEditorScaledRow : MonoBehaviour
{
    public RectTransform Child;
    public float Scale = 1f;

    private void OnEnable() => Fit();

    // Sent by Unity whenever this rect is resized, which is every time the column relays out.
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

// Slider state kept separate from the control, the way MapEditorToggle is, so tools can read and
// push a value without knowing whether the row is the game's or the editor's own - and without
// knowing that the game's runs on a scaled range of its own.
public class MapEditorSlider : MonoBehaviour
{
    public Action<float> OnValueChanged;

    public Slider Slider;

    // 1 on the editor's own slider, which runs in the caller's units already.
    public float Scale = 1f;

    // The editor's own row draws its value itself; the game's row has a readout of its own.
    public TMP_Text Readout;

    public float Value => Slider != null ? Slider.value / Scale : 0f;

    public void SetValue(float value, bool notify)
    {
        if (Slider == null) return;

        // Not the value property: on MMSlider that one rounds, and this is a value the caller
        // already holds rather than one the player just dragged to. It does not call back either,
        // so there is no bounce to guard against.
        Slider.SetValueWithoutNotify(value * Scale);
        Show(value);

        if (notify) OnValueChanged?.Invoke(value);
    }

    // What the control reports as the player drags it.
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
