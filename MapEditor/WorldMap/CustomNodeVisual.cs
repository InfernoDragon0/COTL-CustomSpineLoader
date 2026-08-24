using System;
using HarmonyLib;
using Lamb.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

// Drives a stripped clone of a vanilla DLC map node. The state visuals mirror the game's own
// DungeonMapIconContent - same images, materials and colours - because the art is built around
// them: the outline material carries the glow, and the icon tint is what separates a beaten node
// from a fresh one.
internal class CustomNodeVisual : MonoBehaviour
{
    private Image _icon;
    private Image _iconOutline;
    private Image _imageOutline;
    private Image _selectionIcon;
    private Image _selected;
    private Image _lockedFill;
    private GameObject _notification;
    private GameObject _portalEffect;

    private Material _normalOutline;
    private Material _unselectedOutline;
    private Material _selectedOutline;
    private Material _completedOutline;

    private Sprite _potentialSelection;
    private Sprite _selectedSelection;

    private CanvasGroup _group;

    private bool _keyOrLock;
    private bool _keepIconColour;
    private bool _pulse;
    private float _targetScale = 1f;

    // What the state pass last asked for, so dropping the selection can put it back.
    private WorldNodeState _lastState = WorldNodeState.Selectable;
    private bool _lastAffordable;
    private bool _editView;
    // The editor's mark on this node, if any: red for the selection, green for a gate member.
    private Color? _mark;
    private bool _markEmphasised;

    public float IconSize { get; private set; } = 96f;

    // Built while the clone is still inactive: the references come off the vanilla component, which
    // is then destroyed along with the rest of the logic.
    public static CustomNodeVisual Attach(GameObject clone, string nodeType)
    {
        if (clone == null) return null;

        var content = clone.GetComponentInChildren<DungeonMapIconContent>(true);
        if (content == null)
        {
            Plugin.Log.LogWarning("World map: a vanilla node clone had no icon content; using our own art.");
            return null;
        }

        var visual = clone.AddComponent<CustomNodeVisual>();

        try
        {
            var reader = Traverse.Create(content);
            visual._icon = reader.Field("_icon").GetValue<Image>();
            visual._iconOutline = reader.Field("_iconOutline").GetValue<Image>();
            visual._imageOutline = reader.Field("_imageOutline").GetValue<Image>();
            visual._selectionIcon = reader.Field("_selectionIcon").GetValue<Image>();
            visual._selected = reader.Field("_selected").GetValue<Image>();
            visual._lockedFill = reader.Field("_lockedFill").GetValue<Image>();
            visual._notification = reader.Field("_notification").GetValue<GameObject>();
            visual._portalEffect = reader.Field("_portalEffect").GetValue<GameObject>();

            visual._normalOutline = reader.Field("_normalOutline").GetValue<Material>();
            visual._unselectedOutline = reader.Field("_unselectedOutline").GetValue<Material>();
            visual._selectedOutline = reader.Field("_selectedOutline").GetValue<Material>();
            visual._completedOutline = reader.Field("_completedOutline").GetValue<Material>();

            visual._potentialSelection = reader.Field("_potentialSelectionSprite").GetValue<Sprite>();
            visual._selectedSelection = reader.Field("_selectedSelectionSprite").GetValue<Sprite>();

            visual._group = reader.Field("_canvasGroup").GetValue<CanvasGroup>();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("World map: reading the vanilla node art failed - " + e.Message);
        }

        if (visual._icon == null)
        {
            Plugin.Log.LogWarning("World map: the vanilla node clone had no icon image; using our own art.");
            return null;
        }

        HideMenuFurniture(clone);

        visual._keyOrLock = string.Equals(nodeType, "Key", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(nodeType, "Lock", StringComparison.OrdinalIgnoreCase);

        // Vanilla leaves these types their authored colour; tinting a lock or the home node green
        // reads as a bug rather than as progress.
        visual._keepIconColour = visual._keyOrLock ||
                                 string.Equals(nodeType, "Base", StringComparison.OrdinalIgnoreCase);

        var iconRect = visual._icon.rectTransform;
        if (iconRect.sizeDelta.x > 1f) visual.IconSize = iconRect.sizeDelta.x;

        CustomMapSkin.StripLogic(clone, visual);
        return visual;
    }

    // The "you are here" pin and the quest alert are hidden by the vanilla node's own Start, which
    // is one of the scripts being stripped - without this they show on every node.
    private static void HideMenuFurniture(GameObject clone)
    {
        var icon = clone.GetComponentInChildren<WorldMapIcon>(true);
        if (icon == null) return;

        Hide(AccessTools.Field(typeof(WorldMapIcon), "_youAreHere")?.GetValue(icon) as GameObject);
        Hide((AccessTools.Field(typeof(WorldMapIcon), "_alert")?.GetValue(icon) as Component)?.gameObject);
    }

    private static void Hide(GameObject go)
    {
        if (go != null) go.SetActive(false);
    }

    // An authored png replaces the vanilla icon; the outline, ring and glow stay.
    public void SetIcon(Sprite sprite)
    {
        if (sprite == null || _icon == null) return;
        _icon.sprite = sprite;
        _icon.preserveAspect = true;
    }

    public void Apply(WorldNodeState state, bool lockAffordable)
    {
        if (_icon == null) return;

        _lastState = state;
        _lastAffordable = lockAffordable;
        _editView = false;

        _pulse = false;
        Common();

        switch (state)
        {
            case WorldNodeState.Preview:
                if (_imageOutline != null) _imageOutline.enabled = false;
                if (_selectionIcon != null) _selectionIcon.enabled = false;
                _icon.color = Color.gray;
                break;

            case WorldNodeState.Selectable:
                SetOutline(_unselectedOutline, Color.white);
                if (_selectionIcon != null) _selectionIcon.enabled = true;
                if (_portalEffect != null) _portalEffect.SetActive(true);
                _pulse = true;
                break;

            case WorldNodeState.Completed:
                SetOutline(_completedOutline, Color.white);
                _icon.color = _keepIconColour
                    ? Color.white
                    : UIAdventureMapOverlayController.VisitedColour;
                break;

            case WorldNodeState.Locked:
                SetOutline(_normalOutline, new Color(1f, 1f, 1f, 0.75f));
                _icon.color = UIAdventureMapOverlayController.LockedColourLight;
                if (_lockedFill != null) _lockedFill.enabled = true;

                // An affordable lock is worth looking at; an unaffordable one is scenery.
                if (lockAffordable) _pulse = true;
                break;
        }

        if (_mark.HasValue) PaintMark();
    }

    // The editor's marks, worn on the node's own outline: red for the selection, green for the
    // nodes it is gated on. Null takes the mark off and puts the state's own look back.
    public void SetMark(Color? colour, bool emphasised = false)
    {
        if (Nullable.Equals(_mark, colour) && _markEmphasised == emphasised) return;
        _mark = colour;
        _markEmphasised = emphasised;

        if (colour.HasValue) PaintMark();
        else if (_editView) ApplyEditView();
        else Apply(_lastState, _lastAffordable);
    }

    private void PaintMark()
    {
        if (_imageOutline == null || !_mark.HasValue) return;

        // The vanilla "selected" outline material carries its own warm tint, which multiplies with
        // whatever colour is set on top - green through it came out a dark red. Only the selection
        // wears it; every other mark goes on the plain outline, where the tint reads true.
        var material = _markEmphasised ? _selectedOutline : _unselectedOutline ?? _normalOutline;
        if (material != null) _imageOutline.material = material;

        _imageOutline.gameObject.SetActive(true);
        _imageOutline.enabled = true;
        _imageOutline.color = _mark.Value;
    }

    // Editor view: every node drawn as though it were reachable, so placement is what is judged.
    public void ApplyEditView()
    {
        if (_icon == null) return;

        _editView = true;
        _pulse = false;
        Common();
        SetOutline(_unselectedOutline, Color.white);
        _icon.color = Color.white;

        if (_mark.HasValue) PaintMark();
    }

    private void Common()
    {
        _icon.enabled = true;
        _icon.color = Color.white;

        if (_imageOutline != null)
        {
            _imageOutline.enabled = true;
            _imageOutline.gameObject.SetActive(true);
        }

        if (_selectionIcon != null)
        {
            _selectionIcon.enabled = true;
            _selectionIcon.sprite = _potentialSelection;
            _selectionIcon.color = Color.white;
        }

        if (_selected != null) _selected.enabled = false;
        if (_lockedFill != null) _lockedFill.enabled = false;
        if (_notification != null) _notification.SetActive(false);
        if (_portalEffect != null) _portalEffect.SetActive(false);

        // The key and lock styles carry a second outline that is only theirs.
        if (_iconOutline != null) _iconOutline.gameObject.SetActive(_keyOrLock);

        if (_group != null) _group.alpha = 1f;

        _targetScale = 1f;
    }

    private void SetOutline(Material material, Color colour)
    {
        if (_imageOutline == null) return;
        if (material != null) _imageOutline.material = material;
        _imageOutline.color = colour;
    }

    public void SetHover(bool hovered, bool interactable)
    {
        if (_icon == null) return;

        _targetScale = hovered && interactable ? 1.1f : 1f;

        if (_selectionIcon != null && _selectionIcon.enabled)
            _selectionIcon.sprite = hovered && interactable ? _selectedSelection : _potentialSelection;

        if (_selected != null) _selected.enabled = hovered && interactable;

        if (hovered && interactable && _imageOutline != null && _selectedOutline != null)
            _imageOutline.material = _selectedOutline;
    }

    private void Update()
    {
        // Unscaled throughout: the map holds the world at timeScale 0.
        if (_pulse && _icon != null)
        {
            var wave = Mathf.PingPong(Time.unscaledTime, 0.5f) / 0.5f;
            _icon.color = Color.Lerp(UIAdventureMapOverlayController.LockedColourLight, Color.white, wave);
        }

        var scale = transform.localScale.x;
        if (Mathf.Abs(scale - _targetScale) > 0.001f)
        {
            scale = Mathf.Lerp(scale, _targetScale, Mathf.Clamp01(Time.unscaledDeltaTime * 12f));
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
