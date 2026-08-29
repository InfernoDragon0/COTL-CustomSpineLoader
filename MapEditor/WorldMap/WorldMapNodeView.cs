using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

public class WorldMapNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public CTWorldMapNode Data { get; private set; }
    public WorldNodeState State { get; private set; }

    private Image _icon;
    private TMP_Text _label;
    private TMP_Text _caption;
    private GameObject _badge;
    private Button _button;
    private RectTransform _iconRect;
    private float _iconSize;
    private bool _pulse;

    private CustomNodeVisual _custom;
    private bool _interactable;

    private const float BaseIconSize = 72f;

    public RectTransform Rect => (RectTransform)transform;
    public Vector2 AnchoredPosition => Rect.anchoredPosition;

    public static WorldMapNodeView Create(Transform parent, CTWorldMapNode data, Sprite icon,
        MapEditorUI ui, Action<WorldMapNodeView> onClicked)
    {
        var go = new GameObject("Node_" + data.Id);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(data.Position.X, data.Position.Y);

        var view = go.AddComponent<WorldMapNodeView>();
        view.Data = data;
        view.Build(icon, ui, onClicked);
        return view;
    }

    private void Build(Sprite icon, MapEditorUI ui, Action<WorldMapNodeView> onClicked)
    {
        var scale = Mathf.Max(0.2f, Data.Scale);

        if (BuildVanillaArt(icon, scale)) BuildHitTarget(onClicked);
        else BuildOwnArt(icon, scale, onClicked);

        var label = ui.CreateLabel(transform, Data.DisplayName, 18, TextAlignmentOptions.Center);
        _label = label.GetComponent<TMP_Text>();
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.sizeDelta = new Vector2(220f, 26f);
        labelRect.anchoredPosition = new Vector2(0f, -4f);
        _label.enableWordWrapping = false;
        _label.raycastTarget = false;

        var caption = ui.CreateLabel(transform, "", 15, TextAlignmentOptions.Center);
        _caption = caption.GetComponent<TMP_Text>();
        _caption.color = new Color(1f, 0.85f, 0.4f);
        _caption.raycastTarget = false;
        var captionRect = caption.GetComponent<RectTransform>();
        captionRect.anchorMin = captionRect.anchorMax = new Vector2(0.5f, 0f);
        captionRect.pivot = new Vector2(0.5f, 1f);
        captionRect.sizeDelta = new Vector2(220f, 22f);
        captionRect.anchoredPosition = new Vector2(0f, -30f);

        if (_custom == null) BuildBadge();
    }

    // ---- art ---------------------------------------------------------------------------------

    private bool BuildVanillaArt(Sprite icon, float scale)
    {
        var clone = CustomMapSkin.CloneNode(Data.NodeType);
        if (clone == null) return false;

        _custom = CustomNodeVisual.Attach(clone, Data.NodeType);
        if (_custom == null)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }

        _custom.SetIcon(icon);

        clone.transform.SetParent(transform, false);
        var cloneRect = clone.transform as RectTransform;
        if (cloneRect != null)
        {
            cloneRect.anchorMin = cloneRect.anchorMax = new Vector2(0.5f, 0.5f);
            cloneRect.anchoredPosition = Vector2.zero;
        }
        clone.transform.localScale = Vector3.one;
        clone.SetActive(true);

        _iconSize = _custom.IconSize;
        Rect.sizeDelta = new Vector2(_iconSize, _iconSize);
        Rect.localScale = new Vector3(scale, scale, 1f);
        return true;
    }

    private void BuildHitTarget(Action<WorldMapNodeView> onClicked)
    {
        var hitGO = new GameObject("Hit");
        hitGO.transform.SetParent(transform, false);
        var hitRect = hitGO.AddComponent<RectTransform>();
        hitRect.anchorMin = hitRect.anchorMax = new Vector2(0.5f, 0.5f);
        hitRect.sizeDelta = new Vector2(_iconSize, _iconSize);

        var hit = hitGO.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        _button = hitGO.AddComponent<Button>();
        _button.targetGraphic = hit;
        _button.transition = Selectable.Transition.None;
        _button.onClick.AddListener(() => onClicked?.Invoke(this));
    }

    private void BuildOwnArt(Sprite icon, float scale, Action<WorldMapNodeView> onClicked)
    {
        _iconSize = BaseIconSize * scale;
        Rect.sizeDelta = new Vector2(_iconSize, _iconSize);

        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(transform, false);
        _iconRect = iconGO.AddComponent<RectTransform>();
        _iconRect.sizeDelta = new Vector2(_iconSize, _iconSize);

        _icon = iconGO.AddComponent<Image>();
        if (icon != null)
        {
            _icon.sprite = icon;
            _icon.preserveAspect = true;
        }
        else
        {
            _icon.sprite = MapEditorUI.RoundedPlate;
            _icon.type = Image.Type.Sliced;
            _icon.pixelsPerUnitMultiplier = 0.8f;
        }

        _button = iconGO.AddComponent<Button>();
        _button.targetGraphic = _icon;
        _button.onClick.AddListener(() => onClicked?.Invoke(this));
    }

    private void BuildBadge()
    {
        _badge = new GameObject("DoneBadge");
        _badge.transform.SetParent(transform, false);
        var badgeRect = _badge.AddComponent<RectTransform>();
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.pivot = new Vector2(0.5f, 0.5f);
        badgeRect.sizeDelta = new Vector2(22f, 22f);
        badgeRect.anchoredPosition = new Vector2(-4f, -4f);
        var badgeImage = _badge.AddComponent<Image>();
        badgeImage.sprite = MapEditorUI.RoundedPlate;
        badgeImage.type = Image.Type.Sliced;
        badgeImage.pixelsPerUnitMultiplier = 0.5f;
        badgeImage.color = new Color(0.35f, 0.85f, 0.4f);
        badgeImage.raycastTarget = false;
        var outline = MapEditorUI.AddOutline(badgeRect, new Color(0f, 0f, 0f, 0.6f));
        if (outline != null) outline.raycastTarget = false;
        _badge.SetActive(false);
    }

    // ---- states ------------------------------------------------------------------------------

    public void ApplyScale(float scale)
    {
        scale = Mathf.Max(0.2f, scale);

        if (_custom != null)
        {
            Rect.localScale = new Vector3(scale, scale, 1f);
            return;
        }

        _iconSize = BaseIconSize * scale;
        Rect.sizeDelta = new Vector2(_iconSize, _iconSize);
        if (_iconRect != null) _iconRect.sizeDelta = new Vector2(_iconSize, _iconSize);
    }

    public void SetState(WorldNodeState state, bool lockAffordable)
    {
        State = state;
        _pulse = false;
        ApplyScale(Data.Scale);

        gameObject.SetActive(state != WorldNodeState.Hidden);
        if (state == WorldNodeState.Hidden) return;

        _caption.text = "";
        _label.text = state == WorldNodeState.Preview ? "???" : Data.DisplayName;

        _interactable = state switch
        {
            WorldNodeState.Selectable => true,
            WorldNodeState.Completed => true,
            WorldNodeState.Locked => lockAffordable,
            _ => false
        };
        if (_button != null) _button.interactable = _interactable;

        if (state == WorldNodeState.Locked)
            _caption.text = Data.KeysCost == 1 ? "needs 1 key" : $"needs {Data.KeysCost} keys";

        if (_custom != null)
        {
            _custom.Apply(state, lockAffordable);
            return;
        }

        _badge.SetActive(state == WorldNodeState.Completed);
        _iconRect.localScale = Vector3.one;

        switch (state)
        {
            case WorldNodeState.Preview:
                _icon.color = new Color(0.55f, 0.55f, 0.55f, 0.9f);
                break;

            case WorldNodeState.Selectable:
                _icon.color = Color.white;
                _pulse = true;
                break;

            case WorldNodeState.Completed:
                _icon.color = Color.white;
                break;

            case WorldNodeState.Locked:
                _icon.color = new Color(0.5f, 0.55f, 0.7f, 0.95f);
                break;
        }
    }

    public void SetEditView()
    {
        State = WorldNodeState.Selectable;
        _pulse = false;
        _interactable = false;
        ApplyScale(Data.Scale);

        gameObject.SetActive(true);
        if (_button != null) _button.interactable = false;

        _label.text = string.IsNullOrWhiteSpace(Data.DisplayName) ? Data.Id : Data.DisplayName;

        _caption.text = Data.IsKey ? "[key]"
            : Data.IsLock ? $"[lock x{Data.KeysCost}]"
            : Data.IsBase ? "[home]"
            : "";

        if (_custom != null)
        {
            _custom.ApplyEditView();
            return;
        }

        _badge.SetActive(false);
        _iconRect.localScale = Vector3.one;
        _icon.color = Color.white;
    }

    public static readonly Color SelectedMark = new(1f, 0.33f, 0.28f);
    public static readonly Color RequiredMark = new(0.35f, 0.95f, 0.45f);

    public void SetMark(Color? colour, bool emphasised = false)
    {
        if (_custom != null)
        {
            _custom.SetMark(colour, emphasised);
            return;
        }

        if (_icon != null) _icon.color = colour ?? Color.white;
    }

    // ---- hover -------------------------------------------------------------------------------

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_custom == null || !_interactable) return;
        _custom.SetHover(true, true);
        CustomMapSkin.Play("event:/dlc/ui/map/node_move");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _custom?.SetHover(false, _interactable);
    }

    private void Update()
    {
        if (!_pulse || _iconRect == null) return;

        var scale = 1f + Mathf.Sin(Time.unscaledTime * 2.4f) * 0.05f;
        _iconRect.localScale = new Vector3(scale, scale, 1f);
    }
}
