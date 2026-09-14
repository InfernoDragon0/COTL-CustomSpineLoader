using System.Collections.Generic;
using CustomSpineLoader.MapEditor.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

/// One thing the Structure tool placed, kept so it can be placed again without going back to the
/// browser. Structures are named by type, props by their prefab path.
public class QuickPickEntry
{
    public string Key = "";
    public string Label = "";

    public bool IsProp;
    public StructureBrain.TYPES Type;
    public bool IsCustom;
    public string Path = "";
}

/// A nine-slot bar above the status bar holding the last things placed this session.
public class MapEditorQuickPick
{
    public const int Slots = 9;

    private const float SlotSize = 56f;
    private const float Spacing = 5f;

    // Wider at the ends than top and bottom: the bar reads as a held object rather than a strip cut
    // off at the first and last slot.
    private const float PaddingX = 22f;
    private const float PaddingY = 9f;

    private readonly RuntimeMapEditor _editor;
    private readonly MapEditorUI _ui;

    private readonly GameObject _chrome;
    private readonly RectTransform _bar;
    private readonly List<SlotView> _views = [];
    private readonly List<QuickPickEntry> _recent = [];
    private readonly List<QuickPickEntry> _shown = [];

    /// Set by the Structure tool: what to do when a slot is chosen, and where its icon comes from.
    public System.Action<QuickPickEntry> OnPick;
    public System.Func<QuickPickEntry, Sprite> Icon;
    public System.Func<QuickPickEntry, bool> IsArmed;

    /// The pinned half of the bar, and the right-click that pins and unpins.
    public System.Func<List<QuickPickEntry>> Favourites;
    public System.Func<QuickPickEntry, bool> IsFavourite;
    public System.Action<QuickPickEntry> OnToggleFavourite;

    private bool _wanted;

    private class SlotView
    {
        public GameObject Go;
        public Image Plate;
        public Image Icon;
        public Image Ring;
        public GameObject Star;
        public Image Hover;
        public TMP_Text Name;
        public TMP_Text Label;
    }

    public MapEditorQuickPick(RuntimeMapEditor editor, MapEditorUI ui, Transform canvas, float bottom)
    {
        _editor = editor;
        _ui = ui;

        // Two objects: the outer one is what F6 hides with the rest of the chrome, the inner one is
        // what the tool's toggle shows and hides. One object could not answer to both.
        _chrome = new GameObject("QuickPick");
        _chrome.transform.SetParent(canvas, false);
        var chromeRt = _chrome.AddComponent<RectTransform>();
        chromeRt.anchorMin = chromeRt.anchorMax = chromeRt.pivot = new Vector2(0.5f, 0f);
        chromeRt.sizeDelta = new Vector2(Width, Height);
        chromeRt.anchoredPosition = new Vector2(0f, bottom);

        var bar = new GameObject("Bar");
        bar.transform.SetParent(_chrome.transform, false);
        _bar = bar.AddComponent<RectTransform>();
        _bar.anchorMin = Vector2.zero;
        _bar.anchorMax = Vector2.one;
        _bar.offsetMin = Vector2.zero;
        _bar.offsetMax = Vector2.zero;

        VanillaChrome.Dress(bar.AddComponent<Image>());
        _editor.RegisterUiBlocker(_bar);

        var layout = bar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset((int)PaddingX, (int)PaddingX, (int)PaddingY, (int)PaddingY);
        layout.spacing = Spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        for (var i = 0; i < Slots; i++) _views.Add(BuildSlot(i));

        bar.SetActive(false);
    }

    private static float Width => Slots * SlotSize + (Slots - 1) * Spacing + PaddingX * 2f;
    private static float Height => SlotSize + PaddingY * 2f;

    public bool Visible
    {
        get => _wanted;
        set
        {
            _wanted = value;
            if (_bar != null) _bar.gameObject.SetActive(value);
            if (value) Refresh();
        }
    }

    private SlotView BuildSlot(int index)
    {
        var go = new GameObject("Slot" + (index + 1));
        go.transform.SetParent(_bar, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(SlotSize, SlotSize);

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = SlotSize;
        element.preferredHeight = element.minHeight = SlotSize;

        var plate = go.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 2.5f;
        plate.color = EmptyPlate;

        MapEditorUI.AttachButton(go, plate, () => Pick(index));
        MapEditorUI.AttachRightClick(go, () => RightClick(index));

        var icon = new GameObject("Icon");
        icon.transform.SetParent(go.transform, false);
        var iconRt = icon.AddComponent<RectTransform>();
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(5f, 5f);
        iconRt.offsetMax = new Vector2(-5f, -5f);

        var iconImage = icon.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.enabled = false;

        // The label a slot falls back to when its icon has not loaded or the thing has none, the way
        // a browser cell does. Under the icon in the hierarchy so an icon that arrives covers it.
        var name = _ui.CreateLabel(go.transform, "", 11, TextAlignmentOptions.Center);
        var nameRt = name.GetComponent<RectTransform>();
        nameRt.anchorMin = Vector2.zero;
        nameRt.anchorMax = Vector2.one;
        nameRt.offsetMin = new Vector2(3f, 3f);
        nameRt.offsetMax = new Vector2(-3f, -3f);

        var nameText = name.GetComponent<TMP_Text>();
        nameText.raycastTarget = false;
        nameText.enableWordWrapping = true;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.color = new Color(1f, 0.95f, 0.85f, 0.9f);
        name.SetActive(false);

        var ring = MapEditorUI.AddOutline(rt, MapEditorUI.Accent, inset: 2f);
        ring.gameObject.SetActive(false);

        var star = MapEditorUI.AddStarBadge(go.transform);
        star.SetActive(false);

        // Hover lights an overlay rather than tinting the plate, because Refresh owns the plate
        // colour (empty slots are darker) and the two would overwrite each other.
        var glow = new GameObject("Hover");
        glow.transform.SetParent(go.transform, false);
        var glowRt = glow.AddComponent<RectTransform>();
        glowRt.anchorMin = Vector2.zero;
        glowRt.anchorMax = Vector2.one;
        glowRt.offsetMin = Vector2.zero;
        glowRt.offsetMax = Vector2.zero;

        var glowImage = glow.AddComponent<Image>();
        glowImage.sprite = MapEditorUI.RoundedPlate;
        glowImage.type = Image.Type.Sliced;
        glowImage.pixelsPerUnitMultiplier = 2.5f;
        glowImage.raycastTarget = false;

        var hover = MapEditorUI.AddHover(go, glowImage, HoverOff, HoverOn, null);
        hover.HoverTextProvider = () => HoverTextFor(index);

        var label = _ui.CreateLabel(go.transform, (index + 1).ToString(), 13,
            TextAlignmentOptions.BottomRight);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(0f, 2f);
        labelRt.offsetMax = new Vector2(-4f, 0f);

        var labelText = label.GetComponent<TMP_Text>();
        labelText.raycastTarget = false;
        labelText.color = new Color(1f, 1f, 1f, 0.45f);

        return new SlotView
        {
            Go = go, Plate = plate, Icon = iconImage, Ring = ring, Star = star,
            Hover = glowImage, Name = nameText, Label = labelText
        };
    }

    private static readonly Color EmptyPlate = new(0f, 0f, 0f, 0.45f);
    private static readonly Color FilledPlate = new(0f, 0f, 0f, 0.72f);

    private static readonly Color HoverOff = new(1f, 1f, 1f, 0f);
    private static readonly Color HoverOn = new(1f, 0.93f, 0.76f, 0.2f);

    /// What the status bar says while a slot is under the pointer. Naming the gesture on a pinned
    /// slot is the only hint that right-click is what unpins it.
    private string HoverTextFor(int index)
    {
        if (index < 0 || index >= _shown.Count) return "";

        var entry = _shown[index];
        var name = string.IsNullOrEmpty(entry.Label) ? entry.Key : entry.Label;

        return IsFavourite != null && IsFavourite(entry)
            ? $"{name}  -  pinned, right-click to unpin"
            : $"{name}  -  right-click to pin";
    }

    /// Puts an entry at the front, or moves it there if the bar already holds it. Placing the same
    /// thing ten times in a row should not push everything else off the bar.
    public void Note(QuickPickEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Key)) return;

        var at = _recent.FindIndex(e => e.Key == entry.Key);
        if (at >= 0) _recent.RemoveAt(at);

        _recent.Insert(0, entry);
        // More recents than slots are kept on purpose: unpinning a favourite should reveal the next
        // thing placed, not an empty slot.
        while (_recent.Count > Slots) _recent.RemoveAt(_recent.Count - 1);

        Refresh();
    }

    public void Clear()
    {
        _recent.Clear();
        Refresh();
    }

    /// Pinned first, in the order they were pinned, then the recents that are not already up. A
    /// favourite therefore keeps its place no matter what has been placed since.
    private void Compose()
    {
        _shown.Clear();

        var pinned = Favourites?.Invoke();
        if (pinned != null)
            foreach (var entry in pinned)
            {
                if (_shown.Count >= Slots) break;
                if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;
                if (_shown.Exists(e => e.Key == entry.Key)) continue;
                _shown.Add(entry);
            }

        foreach (var entry in _recent)
        {
            if (_shown.Count >= Slots) break;
            if (_shown.Exists(e => e.Key == entry.Key)) continue;
            _shown.Add(entry);
        }
    }

    private void Pick(int index, bool blockWorldClicks = true)
    {
        if (index < 0 || index >= _shown.Count) return;

        // A slot chosen with the mouse must not let the same press through to the world; one
        // chosen with the wheel or a number key must not eat the click that follows it.
        if (blockWorldClicks) _editor.BlockWorldClicks();

        OnPick?.Invoke(_shown[index]);
        Refresh();
    }

    /// The mouse wheel walks the bar while it is up, wrapping at both ends. False when the bar is
    /// hidden or empty, so the wheel goes back to cycling tools rather than dead-ending.
    public bool Step(int direction)
    {
        if (!_wanted || _bar == null || !_bar.gameObject.activeInHierarchy) return false;

        Compose();
        if (_shown.Count == 0) return false;

        var armed = ArmedIndex();
        var next = armed < 0
            ? (direction > 0 ? 0 : _shown.Count - 1)
            : ((armed + direction) % _shown.Count + _shown.Count) % _shown.Count;

        Pick(next, blockWorldClicks: false);
        return true;
    }

    private int ArmedIndex()
    {
        if (IsArmed == null) return -1;

        for (var i = 0; i < _shown.Count; i++)
            if (IsArmed(_shown[i])) return i;

        return -1;
    }

    private void RightClick(int index)
    {
        if (index < 0 || index >= _shown.Count) return;

        _editor.BlockWorldClicks();
        OnToggleFavourite?.Invoke(_shown[index]);
        Refresh();
    }

    public void Refresh()
    {
        Compose();

        for (var i = 0; i < _views.Count; i++)
        {
            var view = _views[i];
            var entry = i < _shown.Count ? _shown[i] : null;

            view.Plate.color = entry == null ? EmptyPlate : FilledPlate;

            var sprite = entry != null && Icon != null ? Icon(entry) : null;
            view.Icon.sprite = sprite;
            view.Icon.enabled = sprite != null;

            var named = entry != null && sprite == null;
            if (named) view.Name.text = string.IsNullOrEmpty(entry.Label) ? entry.Key : entry.Label;
            if (view.Name.gameObject.activeSelf != named) view.Name.gameObject.SetActive(named);

            var armed = entry != null && IsArmed != null && IsArmed(entry);
            if (view.Ring.gameObject.activeSelf != armed) view.Ring.gameObject.SetActive(armed);

            var starred = entry != null && IsFavourite != null && IsFavourite(entry);
            if (view.Star.activeSelf != starred) view.Star.SetActive(starred);
        }
    }

    private float _nextRefresh;

    public void Tick()
    {
        if (!_wanted || _bar == null || !_bar.gameObject.activeInHierarchy) return;

        for (var i = 0; i < Slots && i < _shown.Count; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { Pick(i, blockWorldClicks: false); return; }

        // Prop icons arrive from a background load, so a filled slot can stay blank for a moment.
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.4f;
        Refresh();
    }
}
