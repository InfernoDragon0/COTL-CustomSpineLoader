using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Chrome;

/// <summary>
/// The strip across the bottom of an editor: the tools on the left, and to their right what the
/// editor is saying and what the mouse and keys do right now. It replaced three stacked plates in
/// the bottom centre - dock, status bar, shortcut column - which between them covered the middle of
/// the room being edited.
///
/// The bar carries no layout group of its own. The dock sizes itself to its icons through a content
/// fitter, and a fitter under a parent layout group is the one combination Unity will not resolve,
/// so the dock is measured after it is filled and the status region is moved to start after it.
/// </summary>
public class EditorBottomBar
{
    public const float Height = 96f;

    private const float DockLeft = 16f;
    private const float StatusGap = 22f;

    private readonly MapEditorUI _ui;
    private readonly RectTransform _bar;
    private readonly RectTransform _dock;
    private readonly RectTransform _status;
    private readonly Image _statusPlate;
    private readonly Image _statusBorder;
    private readonly TMP_Text _message;
    private readonly RectTransform _hintRow;

    private readonly List<MapEditorKeyChip> _chips = [];
    private GameObject _helpChip;

    private float _lastWidth = -1f;
    private int _remeasureFrames;

    public RectTransform Root => _bar;
    public RectTransform DockRoot => _dock;

    /// Raised by the "all shortcuts" chip at the end of the hint row.
    public Action OnHelp;

    public EditorBottomBar(MapEditorUI ui, Transform canvas, Action<RectTransform> registerBlocker)
    {
        _ui = ui;
        _bar = EditorChrome.CreateBar(canvas, "BottomBar", top: false, Height, registerBlocker);

        _dock = EditorChrome.CreateRegion(_bar, "Dock", new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f));
        _dock.anchoredPosition = new Vector2(DockLeft, 0f);

        var layout = _dock.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 8, 8);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = _dock.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        _status = EditorChrome.CreateRegion(_bar, "Status", Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f));
        _status.offsetMin = new Vector2(300f, 10f);
        _status.offsetMax = new Vector2(-16f, -10f);

        // The status plate is deliberately not dressed: it is tinted and untinted as messages come
        // and go, and the dressed art is re-applied asynchronously, which would fight that.
        _statusPlate = _status.gameObject.AddComponent<Image>();
        _statusPlate.sprite = MapEditorUI.RoundedPlate;
        _statusPlate.type = Image.Type.Sliced;
        _statusPlate.pixelsPerUnitMultiplier = 1.5f;
        _statusPlate.color = new Color(0f, 0f, 0f, 0f);
        _statusPlate.raycastTarget = false;

        _statusBorder = MapEditorUI.AddOutline(_status, MapEditorUI.Accent, inset: 2f);
        _statusBorder.gameObject.SetActive(false);

        var messageGo = _ui.CreateLabel(_status, "", 22);
        var messageRt = messageGo.GetComponent<RectTransform>();
        messageRt.anchorMin = new Vector2(0f, 1f);
        messageRt.anchorMax = new Vector2(1f, 1f);
        messageRt.pivot = new Vector2(0f, 1f);
        messageRt.offsetMin = new Vector2(14f, 0f);
        messageRt.offsetMax = new Vector2(-14f, 0f);
        messageRt.sizeDelta = new Vector2(messageRt.sizeDelta.x, 34f);
        messageRt.anchoredPosition = new Vector2(14f, -2f);

        _message = messageGo.GetComponent<TMP_Text>();
        _message.enableWordWrapping = false;
        _message.overflowMode = TextOverflowModes.Ellipsis;
        _message.raycastTarget = false;

        _hintRow = EditorChrome.CreateRegion(_status, "Hints", new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 0f));
        _hintRow.offsetMin = new Vector2(14f, 0f);
        _hintRow.offsetMax = new Vector2(-14f, 0f);
        _hintRow.sizeDelta = new Vector2(_hintRow.sizeDelta.x, 30f);
        _hintRow.anchoredPosition = new Vector2(14f, 4f);

        _hintRow.gameObject.AddComponent<RectMask2D>();

        var hints = _hintRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hints.spacing = 14f;
        hints.childAlignment = TextAnchor.MiddleLeft;
        hints.childControlWidth = true;
        hints.childControlHeight = false;
        hints.childForceExpandWidth = false;
        hints.childForceExpandHeight = false;
    }

    // ---- dock --------------------------------------------------------------------------------

    public GameObject AddTool(Sprite icon, string label, Action onClick, out Image ring, float size,
        string hoverText = null)
    {
        return _ui.CreateIconButton(_dock, icon, label, onClick, out ring, size, hoverText);
    }

    public void AddSeparator(float height) => EditorChrome.CreateSeparator(_dock, height);

    public void ClearDock()
    {
        var stale = new List<GameObject>();
        foreach (Transform child in _dock) stale.Add(child.gameObject);

        foreach (var child in stale)
        {
            child.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(child);
        }
    }

    /// Measures the filled dock and starts the status region after it.
    public void LayoutAfterDock()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(_dock);
        _status.offsetMin = new Vector2(DockLeft + _dock.rect.width + StatusGap, 10f);
        _remeasureFrames = 2;
    }

    // ---- status ------------------------------------------------------------------------------

    /// <summary>
    /// A hover message repaints the text and nothing else; a real status message may also darken the
    /// region and ring it, which is the editor's only way of saying "this one needs you".
    /// </summary>
    public void SetStatus(string message, StatusSeverity severity, bool pulse)
    {
        if (_message == null) return;

        _message.text = message ?? "";
        _message.color = MapEditorUI.StatusColour(severity);

        if (!pulse) return;

        var urgent = severity is StatusSeverity.Warning or StatusSeverity.Error;

        if (_statusBorder != null)
        {
            _statusBorder.gameObject.SetActive(urgent);
            if (urgent)
                _statusBorder.color = severity == StatusSeverity.Error
                    ? MapEditorUI.Accent
                    : new Color(1f, 0.76f, 0.3f);
        }

        if (_statusPlate != null)
            _statusPlate.color = new Color(0f, 0f, 0f, urgent ? 0.5f : 0f);
    }

    // ---- hints -------------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the chip row for the active tool. The globals are not here: pan, zoom, undo and
    /// quicksave are the same under every tool, so they live on the shortcut card and the row is
    /// only what this tool does. Chips that would not fit are hidden rather than clipped mid-word.
    /// </summary>
    public void SetHints(IEnumerable<(string Key, string Action)> hints)
    {
        var stale = new List<GameObject>();
        foreach (Transform child in _hintRow) stale.Add(child.gameObject);

        foreach (var child in stale)
        {
            child.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(child);
        }

        _chips.Clear();
        _helpChip = null;

        if (hints != null)
            foreach (var hint in hints)
            {
                var chip = _ui.CreateKeyChip(_hintRow, hint.Key, hint.Action);
                var component = chip.GetComponent<MapEditorKeyChip>();
                if (component != null) _chips.Add(component);
            }

        _helpChip = _ui.CreateKeyChip(_hintRow, "?", "All shortcuts", () => OnHelp?.Invoke(),
            MapEditorUI.AmberAccent);

        _remeasureFrames = 2;
    }

    public void SetHintsVisible(bool visible)
    {
        if (_hintRow != null && _hintRow.gameObject.activeSelf != visible)
            _hintRow.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Re-fits the chips. Called every frame the editor is open because two things move under it:
    /// the text scaler settles a frame or two after a chip is built, and the canvas is matched half
    /// on width and half on height, so the row is a different number of pixels wide on every aspect.
    /// </summary>
    public void Tick()
    {
        if (_hintRow == null) return;

        var width = _hintRow.rect.width;
        if (_remeasureFrames <= 0 && Mathf.Abs(width - _lastWidth) < 1f) return;

        if (_remeasureFrames > 0) _remeasureFrames--;
        _lastWidth = width;

        foreach (var chip in _chips)
            if (chip != null)
                chip.Remeasure();

        var help = _helpChip != null ? _helpChip.GetComponent<MapEditorKeyChip>() : null;
        if (help != null) help.Remeasure();

        var room = width - (help != null ? help.Width + 14f : 0f);
        var used = 0f;

        foreach (var chip in _chips)
        {
            if (chip == null) continue;

            var next = used + chip.Width + (used > 0f ? 14f : 0f);
            var fits = next <= room;
            if (chip.gameObject.activeSelf != fits) chip.gameObject.SetActive(fits);
            if (fits) used = next;
        }
    }

    public void SetVisible(bool visible)
    {
        if (_bar != null && _bar.gameObject.activeSelf != visible) _bar.gameObject.SetActive(visible);
    }

    public void Destroy() => EditorChrome.Destroy(_bar);
}
