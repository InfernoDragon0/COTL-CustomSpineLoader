using System;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Chrome;

/// <summary>
/// The right-hand column: the active tool on top, the layer tree below, one foldable header each.
/// It replaced two panels that used to sit on opposite edges of the screen and force each other
/// shut, so the two things a mapper reads while placing - what the tool can do, and what is already
/// in the room - are now in one place.
///
/// The column is a fixed height, so the two bodies share it. Whoever the mapper opened last gets
/// what it asks for, down to a floor of 40 per cent for the other; a tool whose panel is taller than
/// the whole column (Structures, Lighting) folds the tree away on its own until the mapper says
/// otherwise.
/// </summary>
public class EditorSidebar
{
    public const float Width = 420f;

    private const float MinShare = 0.40f;

    private readonly RectTransform _panel;
    private readonly MapEditorSection _options;
    private readonly MapEditorSection _layers;
    private readonly RectTransform _optionsBody;
    private readonly RectTransform _layersBody;

    private bool _userOpenedLayers;
    private bool _layersLast;
    private int _autoFoldFrames;

    public RectTransform Root => _panel;

    /// The per-tool option columns are parented here; each stretches to fill it.
    public RectTransform OptionsContent => _optionsBody;

    /// The layer tree is parented here.
    public RectTransform LayersContent => _layersBody;

    public bool LayersOpen => _layers.Open;
    public bool OptionsOpen => _options.Open;

    /// Raised when the layer section is folded or unfolded, however that happened.
    public Action<bool> LayersToggled;

    private readonly bool _hasLayers;

    /// <paramref name="withLayers"/> is false for the full-screen editors, which have one panel and
    /// no room tree; the options section then takes the whole column.
    public EditorSidebar(MapEditorUI ui, Transform canvas, Action<RectTransform> registerBlocker,
        float top, float bottom, float side = 12f, string optionsTitle = "Select",
        bool withLayers = true)
    {
        _hasLayers = withLayers;
        _panel = EditorChrome.CreateColumn(canvas, "Sidebar", Width, top, bottom, side, registerBlocker);

        _options = ui.CreateSection(_panel, optionsTitle, open: true, withContent: false);
        _optionsBody = EditorChrome.CreateRegion(_panel, "OptionsBody", new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f));

        _layers = ui.CreateSection(_panel, "Layers", open: false, withContent: false);
        _layersBody = EditorChrome.CreateRegion(_panel, "LayersBody", new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f));

        Stretch(_options.Header);
        Stretch(_layers.Header);

        if (!withLayers)
        {
            _layers.SetVisible(false);
            _layersBody.gameObject.SetActive(false);
        }

        _options.OnToggled += _ => { _layersLast = false; };
        _layers.OnToggled += open =>
        {
            _layersLast = open;
            if (open) _userOpenedLayers = true;
            LayersToggled?.Invoke(open);
        };
    }

    private static void Stretch(RectTransform rect)
    {
        if (rect == null) return;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, MapEditorUI.SectionHeaderHeight);
    }

    public void SetOptionsTitle(string title) => _options.SetTitle(title);

    public void SetOptionsSummary(string summary) => _options.SetSummary(summary);

    public void SetLayersSummary(string summary) => _layers.SetSummary(summary);

    public void SetLayersOpen(bool open) => _layers.SetOpen(open);

    /// Called when the tool changes, so the next few frames may fold the tree away for a tall panel.
    /// The panel height is not final until the option column has been rebuilt, hence the countdown.
    public void NoteToolChanged() => _autoFoldFrames = 4;

    /// <summary>
    /// Shares the column between the two bodies. Both wanted heights are what the content would like;
    /// whatever it is not given, it scrolls.
    /// </summary>
    public void Layout(float wantedOptions, float wantedLayers, bool layersHidden)
    {
        if (_panel == null) return;

        var header = MapEditorUI.SectionHeaderHeight;
        var total = _panel.rect.height;

        layersHidden |= !_hasLayers;
        if (_hasLayers) _layers.SetVisible(!layersHidden);

        var avail = Mathf.Max(0f, total - header * (layersHidden ? 1f : 2f));

        if (_autoFoldFrames > 0)
        {
            _autoFoldFrames--;
            if (_autoFoldFrames == 0 && !layersHidden && _layers.Open && !_userOpenedLayers &&
                wantedOptions > avail)
                _layers.SetOpen(false);
        }

        float optionsHeight;
        float layersHeight;

        if (layersHidden || !_layers.Open)
        {
            optionsHeight = _options.Open ? avail : 0f;
            layersHeight = 0f;
        }
        else if (!_options.Open)
        {
            optionsHeight = 0f;
            layersHeight = avail;
        }
        else if (wantedOptions + wantedLayers <= avail)
        {
            optionsHeight = wantedOptions;
            layersHeight = avail - optionsHeight;
        }
        else
        {
            var floor = avail * MinShare;
            if (_layersLast)
            {
                layersHeight = Mathf.Min(wantedLayers, Mathf.Max(floor, avail - wantedOptions));
                optionsHeight = avail - layersHeight;
            }
            else
            {
                optionsHeight = Mathf.Min(wantedOptions, Mathf.Max(floor, avail - wantedLayers));
                layersHeight = avail - optionsHeight;
            }
        }

        var y = 0f;
        Place(_options.Header, y, header);
        y += header;

        Place(_optionsBody, y, optionsHeight);
        _optionsBody.gameObject.SetActive(optionsHeight > 1f);
        y += optionsHeight;

        if (!layersHidden)
        {
            Place(_layers.Header, y, header);
            y += header;
        }

        Place(_layersBody, y, layersHeight);
        _layersBody.gameObject.SetActive(!layersHidden && layersHeight > 1f);
    }

    private static void Place(RectTransform rect, float y, float height)
    {
        if (rect == null) return;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        rect.anchoredPosition = new Vector2(0f, -y);
    }

    public void SetVisible(bool visible)
    {
        if (_panel != null && _panel.gameObject.activeSelf != visible)
            _panel.gameObject.SetActive(visible);
    }

    public void Destroy() => EditorChrome.Destroy(_panel);
}
