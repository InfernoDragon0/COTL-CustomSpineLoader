using System;
using HarmonyLib;
using Lamb.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

internal class DungeonNodeVisual : MonoBehaviour
{
    private Image _icon;
    private Image _imageOutline;
    private Image _selectionIcon;
    private Image _selected;
    private Image _whiteFill;
    private Image _lockedFill;
    private Image _flairImage;
    private Image _modifierIcon;
    private GameObject _notification;

    private Material _normalOutline;
    private Material _unselectedOutline;
    private Material _selectedOutline;
    private Material _completedOutline;

    private Sprite _potentialSelection;
    private Sprite _selectedSelection;

    private CanvasGroup _group;
    private CanvasGroup _startingIcon;
    private CanvasGroup _modifierGroup;

    private Color? _mark;
    private bool _markEmphasised;

    public static DungeonNodeVisual Attach(GameObject clone)
    {
        if (clone == null) return null;

        var node = clone.GetComponent<AdventureMapNode>();
        if (node == null)
        {
            Plugin.Log.LogWarning("Dungeon map: a vanilla node clone had no node component; using our own art.");
            return null;
        }

        var visual = clone.AddComponent<DungeonNodeVisual>();

        try
        {
            var reader = Traverse.Create(node);
            visual._icon = reader.Field("_icon").GetValue<Image>();
            visual._imageOutline = reader.Field("_imageOutline").GetValue<Image>();
            visual._selectionIcon = reader.Field("_selectionIcon").GetValue<Image>();
            visual._selected = reader.Field("_selected").GetValue<Image>();
            visual._whiteFill = reader.Field("_whiteFill").GetValue<Image>();
            visual._lockedFill = reader.Field("_lockedFill").GetValue<Image>();
            visual._flairImage = reader.Field("_flairImage").GetValue<Image>();
            visual._modifierIcon = reader.Field("_modifierIcon").GetValue<Image>();
            visual._notification = reader.Field("_notification").GetValue<GameObject>();

            visual._normalOutline = reader.Field("_normalOutline").GetValue<Material>();
            visual._unselectedOutline = reader.Field("_unselectedOutline").GetValue<Material>();
            visual._selectedOutline = reader.Field("_selectedOutline").GetValue<Material>();
            visual._completedOutline = reader.Field("_completedOutline").GetValue<Material>();

            visual._potentialSelection = reader.Field("_potentialSelectionSprite").GetValue<Sprite>();
            visual._selectedSelection = reader.Field("_selectedSelectionSprite").GetValue<Sprite>();

            visual._group = reader.Field("_canvasGroup").GetValue<CanvasGroup>();
            visual._startingIcon = reader.Field("_startingIconCanvasGroup").GetValue<CanvasGroup>();
            visual._modifierGroup = reader.Field("_modifierCanvasGroup").GetValue<CanvasGroup>();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Dungeon map: reading the vanilla node art failed - " + e.Message);
        }

        if (visual._icon == null)
        {
            Plugin.Log.LogWarning("Dungeon map: the vanilla node clone had no icon image; using our own art.");
            return null;
        }

        DungeonMapSkin.StripLogic(clone, visual);
        return visual;
    }

    public void SetIcon(Sprite sprite)
    {
        if (_icon == null) return;

        if (sprite != null) _icon.sprite = sprite;
        _icon.preserveAspect = true;
    }

    public void ApplyEditView()
    {
        if (_icon == null) return;

        _icon.enabled = true;
        _icon.color = Color.white;

        if (_imageOutline != null)
        {
            _imageOutline.gameObject.SetActive(true);
            _imageOutline.enabled = true;
            SetOutline(_unselectedOutline, Color.white);
        }

        if (_selectionIcon != null)
        {
            _selectionIcon.enabled = true;
            _selectionIcon.sprite = _potentialSelection;
            _selectionIcon.color = Color.white;
        }

        if (_selected != null) _selected.enabled = false;
        if (_lockedFill != null) _lockedFill.enabled = false;
        if (_whiteFill != null) _whiteFill.enabled = false;
        if (_flairImage != null) _flairImage.enabled = false;
        if (_modifierIcon != null) _modifierIcon.enabled = false;
        if (_notification != null) _notification.SetActive(false);
        if (_startingIcon != null) _startingIcon.alpha = 0f;
        if (_modifierGroup != null) _modifierGroup.alpha = 0f;
        if (_group != null) _group.alpha = 1f;

        if (_mark.HasValue) PaintMark();
    }

    public void ShowStartingPin()
    {
        if (_startingIcon != null) _startingIcon.alpha = 1f;
    }

    public void SetMark(Color? colour, bool emphasised = false)
    {
        if (Nullable.Equals(_mark, colour) && _markEmphasised == emphasised) return;

        _mark = colour;
        _markEmphasised = emphasised;

        if (colour.HasValue) PaintMark();
        else ApplyEditView();
    }

    private void PaintMark()
    {
        if (_imageOutline == null || !_mark.HasValue) return;

        var material = _markEmphasised ? _selectedOutline : _unselectedOutline ?? _normalOutline;
        if (material != null) _imageOutline.material = material;

        _imageOutline.gameObject.SetActive(true);
        _imageOutline.enabled = true;
        _imageOutline.color = _mark.Value;
    }

    private void SetOutline(Material material, Color colour)
    {
        if (_imageOutline == null) return;

        if (material != null) _imageOutline.material = material;
        _imageOutline.color = colour;
    }
}
