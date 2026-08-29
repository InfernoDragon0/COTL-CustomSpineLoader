using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class LoadMapScreen
{
    private readonly RuntimeMapEditor _editor;
    private readonly MapEditorUI _ui;

    private GameObject _root;
    private TMP_Text _title;

    private const int Columns = 3;
    private const float Spacing = 18f;
    private const float SideInset = 48f;

    private const float ScrollInset = 28f;

    private const float CaptionHeight = 64f;

    private const float DetailShare = 0.34f;
    private const float DetailMinWidth = 460f;
    private const float DetailGap = 28f;
    private const float DetailPad = 28f;

    private float _detailWidth = DetailMinWidth;

    private GridLayoutGroup _grid;
    private float _thumbHeight = 180f;

    public RectTransform Cells { get; private set; }

    public bool IsOpen => _root != null;

    public Action CloseRequested;

    public LoadMapScreen(RuntimeMapEditor editor, MapEditorUI ui)
    {
        _editor = editor;
        _ui = ui;
    }

    public void Open(string title)
    {
        if (_root != null)
        {
            SetTitle(title);
            return;
        }

        var canvas = _ui.CanvasRoot;
        if (canvas == null) return;

        _root = new GameObject("LoadMapScreen");
        _root.transform.SetParent(canvas, false);

        var rootRt = _root.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var backdrop = _root.AddComponent<Image>();
        backdrop.color = new Color(0.03f, 0.03f, 0.04f, 0.96f);
        _editor.RegisterUiBlocker(rootRt);

        _detailWidth = Mathf.Max(DetailMinWidth, canvas.rect.width * DetailShare);

        BuildDetail(rootRt);
        BuildHeader(rootRt, title);
        BuildGrid(rootRt);
        BuildLightbox(rootRt);

        _editor.SetOwnChromeVisible(false);
    }

    private void BuildHeader(RectTransform parent, string title)
    {
        var header = MapEditorUI.NewChild(parent, "Header", stretch: false);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.offsetMin = new Vector2(_detailWidth + DetailGap, -72f);
        header.offsetMax = new Vector2(-48f, 0f);

        var label = _ui.CreateLabel(header, title, 30, TextAlignmentOptions.Left);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = new Vector2(0.8f, 1f);
        labelRt.offsetMin = new Vector2(0f, 8f);
        labelRt.offsetMax = new Vector2(0f, -8f);

        _title = label.GetComponent<TMP_Text>();
        _title.raycastTarget = false;

        var close = _ui.CreateButton(header, "Close  (Esc)", () => CloseRequested?.Invoke(), 40f, MapEditorEmphasis.Quiet);
        var closeRt = close.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.sizeDelta = new Vector2(180f, 40f);
        closeRt.anchoredPosition = new Vector2(0f, 0f);

        var element = close.GetComponent<LayoutElement>();
        if (element != null) element.flexibleWidth = 0f;
    }

    private void BuildDetail(RectTransform parent)
    {
        var pane = MapEditorUI.NewChild(parent, "Detail", stretch: false);
        pane.anchorMin = new Vector2(0f, 0f);
        pane.anchorMax = new Vector2(0f, 1f);
        pane.pivot = new Vector2(0f, 0.5f);
        pane.offsetMin = new Vector2(0f, 0f);
        pane.offsetMax = new Vector2(_detailWidth, 0f);

        var plate = pane.gameObject.AddComponent<Image>();
        plate.color = new Color(1f, 1f, 1f, 0.045f);
        plate.raycastTarget = false;

        var shotHeight = _detailWidth * 9f / 16f;

        var shot = MapEditorUI.NewChild(pane, "Shot", stretch: false);
        shot.anchorMin = new Vector2(0f, 1f);
        shot.anchorMax = new Vector2(1f, 1f);
        shot.pivot = new Vector2(0.5f, 1f);
        shot.offsetMin = new Vector2(0f, -shotHeight);
        shot.offsetMax = new Vector2(0f, 0f);

        var backing = shot.gameObject.AddComponent<Image>();
        backing.color = new Color(0f, 0f, 0f, 0.55f);

        var imageRt = MapEditorUI.NewChild(shot, "Image", stretch: true);
        DetailShot = imageRt.gameObject.AddComponent<RawImage>();
        DetailShot.raycastTarget = false;
        DetailShot.enabled = false;

        MapEditorUI.AttachButton(shot.gameObject, backing, () => ShowLightbox());
        MapEditorUI.AddHover(shot.gameObject, backing,
            new Color(0f, 0f, 0f, 0.55f), new Color(0.35f, 0.35f, 0.4f, 0.55f), null);

        var capRt = MapEditorUI.NewChild(shot, "Caption", stretch: false);
        capRt.anchorMin = new Vector2(0f, 0f);
        capRt.anchorMax = new Vector2(1f, 0f);
        capRt.pivot = new Vector2(0.5f, 0f);
        capRt.offsetMin = new Vector2(0f, 0f);
        capRt.offsetMax = new Vector2(0f, 30f);

        var capPlate = capRt.gameObject.AddComponent<Image>();
        capPlate.color = new Color(0f, 0f, 0f, 0.55f);
        capPlate.raycastTarget = false;

        var caption = _ui.CreateLabel(capRt, "Enlarge image", 14, TextAlignmentOptions.Center);
        var captionRt = caption.GetComponent<RectTransform>();
        captionRt.anchorMin = Vector2.zero;
        captionRt.anchorMax = Vector2.one;
        captionRt.offsetMin = Vector2.zero;
        captionRt.offsetMax = Vector2.zero;

        DetailCaption = caption;
        caption.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.75f);
        caption.GetComponent<TMP_Text>().raycastTarget = false;

        var top = -(shotHeight + DetailPad);

        DetailName = Line(pane, "Name", top, 46f, 32, new Color(1f, 1f, 1f, 0.95f));
        DetailWhen = Line(pane, "When", top - 48f, 30f, 19, new Color(1f, 1f, 1f, 0.62f));
        DetailSize = Line(pane, "Size", top - 78f, 28f, 17, new Color(1f, 1f, 1f, 0.45f));

        _rows = MapEditorUI.NewChild(pane, "Facts", stretch: false);
        _rows.anchorMin = new Vector2(0f, 1f);
        _rows.anchorMax = new Vector2(1f, 1f);
        _rows.pivot = new Vector2(0.5f, 1f);
        _rows.offsetMin = new Vector2(DetailPad, -1000f);
        _rows.offsetMax = new Vector2(-DetailPad, top - 124f);

        var button = _ui.CreateButton(pane, "Load Map", () => LoadRequested?.Invoke(), 52f);
        DetailButton = button;

        var buttonRt = button.GetComponent<RectTransform>();
        buttonRt.anchorMin = new Vector2(0f, 0f);
        buttonRt.anchorMax = new Vector2(1f, 0f);
        buttonRt.pivot = new Vector2(0.5f, 0f);
        buttonRt.offsetMin = new Vector2(DetailPad, DetailPad);
        buttonRt.offsetMax = new Vector2(-DetailPad, DetailPad + 52f);

        var element = button.GetComponent<LayoutElement>();
        if (element != null) element.flexibleWidth = 0f;

        var warnRt = MapEditorUI.NewChild(pane, "Warning", stretch: false);
        warnRt.anchorMin = new Vector2(0f, 0f);
        warnRt.anchorMax = new Vector2(1f, 0f);
        warnRt.pivot = new Vector2(0.5f, 0f);
        warnRt.offsetMin = new Vector2(DetailPad, DetailPad + 60f);
        warnRt.offsetMax = new Vector2(-DetailPad, DetailPad + 118f);

        var warning = _ui.CreateLabel(warnRt, "", 15, TextAlignmentOptions.BottomLeft);
        var warningRt = warning.GetComponent<RectTransform>();
        warningRt.anchorMin = Vector2.zero;
        warningRt.anchorMax = Vector2.one;
        warningRt.offsetMin = Vector2.zero;
        warningRt.offsetMax = Vector2.zero;

        DetailWarning = warning.GetComponent<TMP_Text>();
        DetailWarning.color = new Color(1f, 0.72f, 0.35f);
        DetailWarning.raycastTarget = false;

        SetEmpty();
    }

    private TMP_Text Line(RectTransform pane, string name, float top, float height, int size, Color colour)
    {
        var rt = MapEditorUI.NewChild(pane, name, stretch: false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(DetailPad, top - height);
        rt.offsetMax = new Vector2(-DetailPad, top);

        var label = _ui.CreateLabel(rt, "", size, TextAlignmentOptions.Left);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var text = label.GetComponent<TMP_Text>();
        text.color = colour;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private RectTransform _rows;

    public RawImage DetailShot { get; private set; }
    public GameObject DetailCaption { get; private set; }
    public TMP_Text DetailName { get; private set; }
    public TMP_Text DetailWhen { get; private set; }
    public TMP_Text DetailSize { get; private set; }
    public TMP_Text DetailWarning { get; private set; }
    public GameObject DetailButton { get; private set; }

    public Action LoadRequested;

    private const float RowHeight = 46f;
    private const float RowIcon = 36f;

    public void SetEmpty()
    {
        if (DetailName != null) DetailName.text = "";
        if (DetailWhen != null) DetailWhen.text = "Pick a map to see what is in it.";
        if (DetailSize != null) DetailSize.text = "";
        if (DetailWarning != null) DetailWarning.text = "";
        if (DetailButton != null) DetailButton.SetActive(false);
        if (DetailCaption != null) DetailCaption.SetActive(false);

        ClearRows();

        if (DetailShot == null) return;
        DetailShot.texture = null;
        DetailShot.enabled = false;
    }

    public void SetDetail(string mapName, string when, string size,
        List<(Sprite Icon, string Text)> facts, bool unsaved)
    {
        if (DetailName != null) DetailName.text = mapName;
        if (DetailWhen != null) DetailWhen.text = when;
        if (DetailSize != null) DetailSize.text = size;
        if (DetailButton != null) DetailButton.SetActive(true);

        if (DetailWarning != null)
            DetailWarning.text = unsaved ? "This map has unsaved changes.\nLoading will discard them." : "";

        ClearRows();
        if (facts == null || _rows == null) return;

        for (var i = 0; i < facts.Count; i++)
        {
            var row = MapEditorUI.NewChild(_rows, "Row", stretch: false);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(0f, -(i * RowHeight + RowHeight));
            row.offsetMax = new Vector2(0f, -(i * RowHeight));

            if (facts[i].Icon != null)
            {
                var iconRt = MapEditorUI.NewChild(row, "Icon", stretch: false);
                iconRt.anchorMin = new Vector2(0f, 0.5f);
                iconRt.anchorMax = new Vector2(0f, 0.5f);
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.sizeDelta = new Vector2(RowIcon, RowIcon);
                iconRt.anchoredPosition = new Vector2(0f, 0f);

                var icon = iconRt.gameObject.AddComponent<Image>();
                icon.sprite = facts[i].Icon;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.color = new Color(1f, 1f, 1f, 0.85f);
            }

            var label = _ui.CreateLabel(row, facts[i].Text, 20, TextAlignmentOptions.Left);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(RowIcon + 16f, 0f);
            labelRt.offsetMax = Vector2.zero;

            var text = label.GetComponent<TMP_Text>();
            text.color = new Color(1f, 1f, 1f, 0.82f);
            text.raycastTarget = false;
        }
    }

    public void ShowCaption(bool on)
    {
        if (DetailCaption != null) DetailCaption.SetActive(on);
    }

    private void ClearRows()
    {
        if (_rows == null) return;

        foreach (Transform child in _rows)
            UnityEngine.Object.Destroy(child.gameObject);
    }

    // ---- the full-screen picture -----------------------------------------------------------------

    private GameObject _lightbox;
    private RawImage _lightboxImage;

    private void BuildLightbox(RectTransform parent)
    {
        var root = MapEditorUI.NewChild(parent, "Lightbox", stretch: true);
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var backdrop = root.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0.02f, 0.02f, 0.03f, 0.97f);

        MapEditorUI.AttachButton(root.gameObject, backdrop, HideLightbox);

        var imageRt = MapEditorUI.NewChild(root, "Full", stretch: true);
        imageRt.offsetMin = new Vector2(64f, 64f);
        imageRt.offsetMax = new Vector2(-64f, -64f);

        _lightboxImage = imageRt.gameObject.AddComponent<RawImage>();
        _lightboxImage.raycastTarget = false;

        var hint = _ui.CreateLabel(root, "Click anywhere to close", 16, TextAlignmentOptions.Center);
        var hintRt = hint.GetComponent<RectTransform>();
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.offsetMin = new Vector2(0f, 20f);
        hintRt.offsetMax = new Vector2(0f, 52f);
        hint.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.5f);
        hint.GetComponent<TMP_Text>().raycastTarget = false;

        _lightbox = root.gameObject;
        _lightbox.SetActive(false);
    }

    public bool LightboxOpen => _lightbox != null && _lightbox.activeSelf;

    public void ShowLightbox()
    {
        if (_lightbox == null || DetailShot == null || DetailShot.texture == null) return;

        _lightboxImage.texture = DetailShot.texture;

        var texture = DetailShot.texture;
        var box = ((RectTransform)_lightboxImage.transform.parent).rect;
        var scale = Mathf.Min((box.width - 128f) / texture.width, (box.height - 128f) / texture.height);
        var rt = (RectTransform)_lightboxImage.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);
        rt.anchoredPosition = Vector2.zero;

        _lightbox.SetActive(true);
        _lightbox.transform.SetAsLastSibling();
    }

    public void HideLightbox()
    {
        if (_lightbox != null) _lightbox.SetActive(false);
    }

    private void BuildGrid(RectTransform parent)
    {
        var area = MapEditorUI.NewChild(parent, "GridArea", stretch: true);
        area.offsetMin = new Vector2(_detailWidth + DetailGap, 48f);
        area.offsetMax = new Vector2(-48f, -80f);

        var content = _ui.CreateScrollColumn(area, "MapScroll", out _, spacing: 0f);

        var cells = new GameObject("Cells");
        cells.transform.SetParent(content, false);
        Cells = cells.AddComponent<RectTransform>();

        _grid = cells.AddComponent<GridLayoutGroup>();
        _grid.spacing = new Vector2(Spacing, Spacing);
        _grid.childAlignment = TextAnchor.UpperCenter;
        _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _grid.constraintCount = Columns;

        var fitter = cells.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        SizeCells();
    }

    private void SizeCells()
    {
        if (_grid == null || _ui.CanvasRoot == null) return;

        var available = _ui.CanvasRoot.rect.width - SideInset - ScrollInset - _detailWidth - DetailGap;
        var width = Mathf.Max(200f, (available - Spacing * (Columns - 1)) / Columns);

        _thumbHeight = width * 9f / 16f;
        _grid.cellSize = new Vector2(width, _thumbHeight + CaptionHeight);
    }

    public GameObject CreateCard(string mapName, string savedAt, Action onClick, out RawImage thumb)
    {
        var card = new GameObject("Card_" + mapName);
        card.transform.SetParent(Cells, false);
        card.AddComponent<RectTransform>();

        var plate = card.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.4f;
        plate.color = new Color(1f, 1f, 1f, 0.06f);

        MapEditorUI.AttachButton(card, plate, onClick);
        MapEditorUI.AddHover(card, plate, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.18f), null);

        var thumbRt = MapEditorUI.NewChild(card.transform, "Thumb", stretch: false);
        thumbRt.anchorMin = new Vector2(0f, 1f);
        thumbRt.anchorMax = new Vector2(1f, 1f);
        thumbRt.pivot = new Vector2(0.5f, 1f);
        thumbRt.offsetMin = new Vector2(6f, -(_thumbHeight + 6f));
        thumbRt.offsetMax = new Vector2(-6f, -6f);

        var backing = thumbRt.gameObject.AddComponent<Image>();
        backing.color = new Color(0f, 0f, 0f, 0.55f);
        backing.raycastTarget = false;

        var imageRt = MapEditorUI.NewChild(thumbRt, "Image", stretch: true);
        thumb = imageRt.gameObject.AddComponent<RawImage>();
        thumb.raycastTarget = false;
        thumb.enabled = false;

        var name = _ui.CreateLabel(card.transform, mapName, 19, TextAlignmentOptions.Center);
        var nameRt = name.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0f, 0f);
        nameRt.anchorMax = new Vector2(1f, 0f);
        nameRt.pivot = new Vector2(0.5f, 0f);
        nameRt.offsetMin = new Vector2(6f, 26f);
        nameRt.offsetMax = new Vector2(-6f, 52f);

        var nameText = name.GetComponent<TMP_Text>();
        nameText.enableWordWrapping = false;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.raycastTarget = false;

        var date = _ui.CreateLabel(card.transform, savedAt, 15, TextAlignmentOptions.Center);
        var dateRt = date.GetComponent<RectTransform>();
        dateRt.anchorMin = new Vector2(0f, 0f);
        dateRt.anchorMax = new Vector2(1f, 0f);
        dateRt.pivot = new Vector2(0.5f, 0f);
        dateRt.offsetMin = new Vector2(6f, 4f);
        dateRt.offsetMax = new Vector2(-6f, 26f);

        var dateText = date.GetComponent<TMP_Text>();
        dateText.color = new Color(1f, 1f, 1f, 0.55f);
        dateText.raycastTarget = false;

        return card;
    }

    public GameObject AddNote(string text)
    {
        var note = _ui.CreateLabel(Cells, text, 20, TextAlignmentOptions.Center);
        note.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.6f);
        return note;
    }

    public void ClearCells()
    {
        if (Cells == null) return;

        foreach (Transform child in Cells)
            UnityEngine.Object.Destroy(child.gameObject);
    }

    public void SetTitle(string text)
    {
        if (_title != null) _title.text = text;
    }

    public void Close()
    {
        if (_root == null) return;

        UnityEngine.Object.Destroy(_root);
        _root = null;
        Cells = null;
        _title = null;

        _editor.SetOwnChromeVisible(true);
    }
}
