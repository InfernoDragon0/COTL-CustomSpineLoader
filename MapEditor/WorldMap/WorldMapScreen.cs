using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

// The custom overworld: a full-screen travel menu over the current scene, world paused underneath.
public class WorldMapScreen : MonoBehaviour
{
    public static WorldMapScreen Instance { get; private set; }

    public static bool IsOpen => Instance != null && Instance._open;

    public CTWorldMap Map { get; private set; }

    // The editor overlays this same canvas; play-mode interactions step aside.
    public bool EditMode
    {
        get => _editMode;
        internal set
        {
            _editMode = value;
            if (value) _editorUsed = true;
            UpdatePlayBadge();
        }
    }

    private bool _editMode;

    // Whether this session of the map has been edited at all. The play-mode badge is an editor
    // affordance: a player who opened the map to travel should not be told about F6.
    private bool _editorUsed;

    public RectTransform ContentRoot { get; private set; }

    private readonly MapEditorUI _ui = new();

    private GameObject _canvasGO;
    private Canvas _canvas;
    private Image _background;
    private TMP_Text _title;
    private RectTransform _layersRoot;
    private RectTransform _linesRoot;
    private RectTransform _nodesRoot;
    private RectTransform _gizmoRoot;
    private bool _open;
    private bool _built;
    private float _savedTimeScale = 1f;

    // ---- content built per open -------------------------------------------------------------

    private readonly List<WorldMapNodeView> _nodeViews = [];
    private readonly List<(RectTransform rect, string fromId, string toId)> _lines = [];
    private readonly List<(RectTransform rect, Vector2 basePos, float distance)> _parallax = [];
    private Dictionary<string, WorldNodeState> _states = new(StringComparer.OrdinalIgnoreCase);

    // The editor drags things by reference rather than rebuilding the canvas every frame.
    internal readonly Dictionary<string, RectTransform> LayerRects = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, RectTransform> NodeRects = new(StringComparer.OrdinalIgnoreCase);

    internal RectTransform CanvasRoot => _canvasGO != null ? _canvasGO.GetComponent<RectTransform>() : null;

    // Pointer position in the map's authored coordinate space.
    internal Vector2 PointerContentPosition()
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(ContentRoot, Input.mousePosition,
            null, out var local);
        return local;
    }

    // Colour sliders fire every drag tick; repainting must not rebuild the layers.
    internal void ApplyBackgroundColor()
    {
        if (_background != null && Map != null)
            _background.color = Map.BackgroundColor?.ToColor() ?? Color.black;
    }

    // The game's naming dialog is a menu on the game's own canvas, which this one covers at
    // sorting order 4500 - so the map hides for it, the way the room editor closes for it.
    internal void SetModalMode(bool modalOpen)
    {
        if (_canvas != null) _canvas.enabled = !modalOpen && _open;
        if (!_open) return;

        // The prompt runs the clock itself (its animations are scaled and would freeze half-open at
        // zero); the pause is re-pinned the moment it closes rather than a frame later.
        Time.timeScale = modalOpen ? (_savedTimeScale <= 0f ? 1f : _savedTimeScale) : 0f;
    }

    internal void SetMap(CTWorldMap map)
    {
        if (map == null) return;
        Map = map;
        MarkSaved();
        RebuildVisuals();
    }

    // One way out, whether it was pressed or clicked: dismiss a prompt, else leave edit mode, else
    // close. Esc and the X button share it so the corner button is never a second, different door.
    internal void StepBack()
    {
        if (_confirmRoot != null && _confirmRoot.activeSelf) HideConfirm();
        else if (EditMode) WorldMapEditor.Instance?.ExitEditMode();
        else RequestClose();
    }

    // Closing must not quietly drop an afternoon's work.
    internal void RequestClose()
    {
        if (!_open) return;

        if (!HasUnsavedEdits)
        {
            Close();
            return;
        }

        ShowConfirm($"Save changes to '{Map.ShownName}' before closing?",
            () =>
            {
                if (CTWorldMapSerialization.Save(Map) == null)
                {
                    SetStatus("Could not save the map - see the log. Nothing was closed.");
                    return;
                }
                MarkSaved();
                Close();
            },
            confirmLabel: "Save & close", altLabel: "Discard", onAlt: Close);
    }

    // ---- confirm strip ----------------------------------------------------------------------

    private GameObject _confirmRoot;
    private TMP_Text _confirmLabel;
    private Action _confirmAction;
    private Action _altAction;
    private GameObject _altButton;
    private TMP_Text _confirmButtonLabel;
    private TMP_Text _altButtonLabel;
    private RectTransform _confirmRect;
    private RectTransform _cancelRect;

    private TMP_Text _status;
    private float _statusUntil;
    private GameObject _playBadge;

    private void UpdatePlayBadge()
    {
        if (_playBadge != null) _playBadge.SetActive(_open && !_editMode && _editorUsed);
    }

    // ---- unsaved work ------------------------------------------------------------------------

    // The map as it last stood on disk. Comparing beats a dirty flag set from every widget: a
    // slider or dropdown that forgot to raise the flag would silently lose the edit.
    private string _savedJson = "";

    internal void MarkSaved() => _savedJson = CTWorldMapSerialization.ToJson(Map);

    internal bool HasUnsavedEdits =>
        Map != null && !string.Equals(CTWorldMapSerialization.ToJson(Map), _savedJson, StringComparison.Ordinal);

    private void Awake()
    {
        Instance = this;

        // The scene can change under an open map (cutscene, death warp); close when it does.
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
        UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (_open) Close();
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_open) Close();
        if (Instance == this) Instance = null;
    }

    // ---- open / close -----------------------------------------------------------------------

    public void Open(string mapName, bool startInEditMode = false)
    {
        if (_open) return;

        // Three systems share the pause and the screen; only one runs at a time.
        if (RuntimeMapEditor.Active != null && RuntimeMapEditor.Active.IsEditing)
        {
            Plugin.Log.LogInfo("World map: close the map editor (F4) first.");
            return;
        }
        if (ModUI.CultTweakerPanel.Active != null && ModUI.CultTweakerPanel.Active.IsOpen)
        {
            Plugin.Log.LogInfo("World map: close the mod panel (F7) first.");
            return;
        }

        // No running game to pause on the title screen.
        if (PlayerFarming.Instance == null)
        {
            Plugin.Log.LogInfo("World map: opens in game, not on the menu.");
            return;
        }

        var map = CTWorldMapSerialization.LoadByName(mapName);
        if (map == null)
        {
            Plugin.Log.LogWarning($"World map '{mapName}' is not saved on this machine.");
            return;
        }

        Map = map;
        MarkSaved();
        EnsureUi();

        _open = true;
        _editorUsed = false;
        UpdatePlayBadge();
        _canvas.enabled = true;
        CustomMapSkin.Play("event:/dlc/ui/map/ewefall_enter");

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        SimulationManager.Pause();

        // The vanilla map art is an Addressable the game only loads on demand, so the first map of
        // a session waits a moment for it rather than opening in our own visuals and popping.
        if (!CustomMapSkin.NeedsLoad)
        {
            BuildContent();
            RefreshStates();
            if (startInEditMode) WorldMapEditorBridge.RequestEditMode(this);
        }
        else
        {
            SetStatus("Preparing the map...");
            StartCoroutine(OpenWhenSkinReady(startInEditMode));
        }
    }

    private System.Collections.IEnumerator OpenWhenSkinReady(bool startInEditMode)
    {
        yield return CustomMapSkin.EnsureLoaded();

        // Closed again while the art loaded.
        if (!_open) yield break;

        BuildContent();
        RefreshStates();
        if (startInEditMode) WorldMapEditorBridge.RequestEditMode(this);
    }

    public void Close()
    {
        if (!_open) return;

        _open = false;
        EditMode = false;
        UpdatePlayBadge();
        CustomMapSkin.Play("event:/dlc/ui/map/ewefall_exit");
        WorldMapEditorBridge.NotifyClosed(this);

        HideConfirm();
        _ui.CloseTransientUi();
        if (_canvas != null) _canvas.enabled = false;

        Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;
        SimulationManager.UnPause();
    }

    private void Update()
    {
        if (!_open) return;

        // The editor's name dialog needs a running clock; the pause stands down while a modal is up.
        var editorModal = WorldMapEditor.Instance != null && WorldMapEditor.Instance.ModalOpen;

        // Reassert the pause - a vanilla menu opened underneath can put the clock back.
        if (!editorModal && Time.timeScale != 0f) Time.timeScale = 0f;

        if (!editorModal && Input.GetKeyDown(KeyCode.Escape))
        {
            StepBack();
            return;
        }

        if (!EditMode) UpdateParallax();

        if (_status != null && _status.gameObject.activeSelf && Time.unscaledTime > _statusUntil)
            _status.gameObject.SetActive(false);
    }

    // ---- canvas -----------------------------------------------------------------------------

    private void EnsureUi()
    {
        if (_built)
        {
            _background.color = Map.BackgroundColor?.ToColor() ?? Color.black;
            _title.text = Map.ShownName;
            return;
        }
        _built = true;

        _canvasGO = new GameObject("WorldMap_Canvas");
        _canvasGO.transform.SetParent(transform, false);

        _canvas = _canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Under the F7 panel (5000) and the vanilla dialogs, over the game's HUD.
        _canvas.sortingOrder = 4500;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        // Clicks are silently swallowed without an EventSystem; the scene may not have one yet.
        if (EventSystem.current == null)
        {
            var events = new GameObject("WorldMap_EventSystem");
            events.transform.SetParent(transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        _ui.Attach(null, _canvasGO.GetComponent<RectTransform>());

        // The backdrop swallows every click that misses a node, so nothing reaches the world.
        var backgroundGO = new GameObject("Background");
        backgroundGO.transform.SetParent(_canvasGO.transform, false);
        var backgroundRect = backgroundGO.AddComponent<RectTransform>();
        Stretch(backgroundRect);
        _background = backgroundGO.AddComponent<Image>();
        _background.color = Map.BackgroundColor?.ToColor() ?? Color.black;

        ContentRoot = NewFullRect(_canvasGO.transform, "Content");
        _layersRoot = NewFullRect(ContentRoot, "Layers");
        _linesRoot = NewFullRect(ContentRoot, "Connections");
        _nodesRoot = NewFullRect(ContentRoot, "Nodes");

        // Last child, so the editor's marks draw over every layer and node whatever their order.
        _gizmoRoot = NewFullRect(ContentRoot, "Gizmos");

        BuildChrome();
    }

    private static RectTransform NewFullRect(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        Stretch(rect);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void BuildChrome()
    {
        var title = _ui.CreateLabel(_canvasGO.transform, "", 30);
        _title = title.GetComponent<TMP_Text>();
        _title.text = Map.ShownName;
        _title.enableWordWrapping = false;
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.sizeDelta = new Vector2(700f, 44f);
        titleRect.anchoredPosition = new Vector2(24f, -18f);

        var close = _ui.CreateButton(_canvasGO.transform, "X", StepBack, 36f);
        var closeRect = close.GetComponent<RectTransform>();
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.sizeDelta = new Vector2(36f, 36f);
        closeRect.anchoredPosition = new Vector2(-18f, -18f);

        // Bottom left, where the room editor's badge sits, and permanent: a line that fades is a
        // line that is gone by the time it is wanted.
        var badge = _ui.CreateLabel(_canvasGO.transform, "Play mode - F6 to show UI", 18);
        _playBadge = badge;
        var badgeText = badge.GetComponent<TMP_Text>();
        badgeText.color = new Color(1f, 0.85f, 0.5f);
        badgeText.enableWordWrapping = false;
        badgeText.raycastTarget = false;
        var badgeRect = badge.GetComponent<RectTransform>();
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 0f);
        badgeRect.pivot = new Vector2(0f, 0f);
        badgeRect.sizeDelta = new Vector2(600f, 28f);
        badgeRect.anchoredPosition = new Vector2(24f, 16f);
        badge.SetActive(false);

        var status = _ui.CreateLabel(_canvasGO.transform, "", 18);
        _status = status.GetComponent<TMP_Text>();
        _status.color = new Color(1f, 0.85f, 0.5f);
        _status.enableWordWrapping = false;
        var statusRect = status.GetComponent<RectTransform>();
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(0f, 0f);
        statusRect.pivot = new Vector2(0f, 0f);
        statusRect.sizeDelta = new Vector2(900f, 28f);
        statusRect.anchoredPosition = new Vector2(24f, 48f);
        status.SetActive(false);

        BuildConfirmStrip();
    }

    // Clear of the editor's dock and status bar, which stand on this same canvas: a prompt behind
    // them reads as nothing happening at all.
    private const float ConfirmBottom = 168f;

    private void BuildConfirmStrip()
    {
        _confirmRoot = new GameObject("Confirm");
        _confirmRoot.transform.SetParent(_canvasGO.transform, false);
        var rect = _confirmRoot.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(560f, 96f);
        rect.anchoredPosition = new Vector2(0f, ConfirmBottom);

        // The outer plate is the border; the fill sits inside it. Red, because every prompt that
        // reaches this strip is a question that costs something to answer wrongly.
        var border = _confirmRoot.AddComponent<Image>();
        border.sprite = MapEditorUI.RoundedPlate;
        border.type = Image.Type.Sliced;
        border.pixelsPerUnitMultiplier = 1.6f;
        border.color = new Color(0.92f, 0.28f, 0.25f, 0.95f);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(_confirmRoot.transform, false);
        var fillRect = fill.AddComponent<RectTransform>();
        Stretch(fillRect);
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);

        var plate = fill.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.6f;
        plate.color = new Color(0f, 0f, 0f, 0.88f);
        plate.raycastTarget = false;

        var label = _ui.CreateLabel(_confirmRoot.transform, "", 20, TextAlignmentOptions.Center);
        _confirmLabel = label.GetComponent<TMP_Text>();
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = new Vector2(-12f, -6f);

        var confirm = _ui.CreateButton(_confirmRoot.transform, "Enter", () =>
        {
            var action = _confirmAction;
            HideConfirm();
            action?.Invoke();
        }, 34f);
        _confirmRect = confirm.GetComponent<RectTransform>();
        _confirmRect.anchorMin = _confirmRect.anchorMax = new Vector2(0.5f, 0f);
        _confirmRect.pivot = new Vector2(1f, 0f);
        _confirmRect.sizeDelta = new Vector2(150f, 34f);
        _confirmButtonLabel = confirm.GetComponentInChildren<TMP_Text>();

        // The middle button only appears when a prompt offers a third way out (discard, skip).
        _altButton = _ui.CreateButton(_confirmRoot.transform, "Discard", () =>
        {
            var action = _altAction;
            HideConfirm();
            action?.Invoke();
        }, 34f);
        var altRect = _altButton.GetComponent<RectTransform>();
        altRect.anchorMin = altRect.anchorMax = new Vector2(0.5f, 0f);
        altRect.pivot = new Vector2(0.5f, 0f);
        altRect.sizeDelta = new Vector2(150f, 34f);
        altRect.anchoredPosition = new Vector2(0f, 10f);
        _altButtonLabel = _altButton.GetComponentInChildren<TMP_Text>();

        var cancel = _ui.CreateButton(_confirmRoot.transform, "Cancel", HideConfirm, 34f);
        _cancelRect = cancel.GetComponent<RectTransform>();
        _cancelRect.anchorMin = _cancelRect.anchorMax = new Vector2(0.5f, 0f);
        _cancelRect.pivot = new Vector2(0f, 0f);
        _cancelRect.sizeDelta = new Vector2(150f, 34f);

        _confirmRoot.SetActive(false);
    }

    private void ShowConfirm(string text, Action onConfirm, string confirmLabel = "Enter",
        string altLabel = null, Action onAlt = null)
    {
        _confirmLabel.text = text;
        _confirmAction = onConfirm;
        _altAction = onAlt;

        if (_confirmButtonLabel != null) _confirmButtonLabel.text = confirmLabel;

        var threeWay = onAlt != null;
        if (_altButton != null) _altButton.SetActive(threeWay);
        if (_altButtonLabel != null && altLabel != null) _altButtonLabel.text = altLabel;

        // Two buttons meet in the middle; three make room for the one between them.
        if (_confirmRect != null) _confirmRect.anchoredPosition = new Vector2(threeWay ? -114f : -8f, 10f);
        if (_cancelRect != null) _cancelRect.anchoredPosition = new Vector2(threeWay ? 114f : 8f, 10f);

        _confirmRoot.SetActive(true);
    }

    private void HideConfirm()
    {
        _confirmAction = null;
        _altAction = null;
        if (_confirmRoot != null) _confirmRoot.SetActive(false);
    }

    public void SetStatus(string message)
    {
        if (_status == null) return;
        _status.text = message;
        _status.gameObject.SetActive(true);
        _statusUntil = Time.unscaledTime + 4f;
    }

    // ---- content ----------------------------------------------------------------------------

    // Torn down and rebuilt whole; assets are cached in WorldMapAssets, so this is GameObjects only.
    public void RebuildVisuals()
    {
        if (!_built || Map == null) return;

        foreach (Transform child in _layersRoot) Destroy(child.gameObject);
        foreach (Transform child in _linesRoot) Destroy(child.gameObject);
        foreach (Transform child in _nodesRoot) Destroy(child.gameObject);
        _nodeViews.Clear();
        _lines.Clear();
        _parallax.Clear();
        HideConfirm();

        BuildContent();
        RefreshStates();
    }

    private void BuildContent()
    {
        foreach (Transform child in _layersRoot) Destroy(child.gameObject);
        foreach (Transform child in _linesRoot) Destroy(child.gameObject);
        foreach (Transform child in _nodesRoot) Destroy(child.gameObject);
        _nodeViews.Clear();
        _lines.Clear();
        _parallax.Clear();
        LayerRects.Clear();
        NodeRects.Clear();

        _background.color = Map.BackgroundColor?.ToColor() ?? Color.black;
        _title.text = Map.ShownName;

        // Layers, back to front. Sibling order is draw order on a canvas.
        var layers = new List<CTWorldMapLayer>(Map.Layers);
        layers.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

        foreach (var layer in layers)
        {
            var rect = BuildLayer(layer);
            if (rect == null) continue;

            if (Mathf.Abs(layer.ParallaxDistance) > 0.001f)
                _parallax.Add((rect, rect.anchoredPosition, layer.ParallaxDistance));
        }

        // Nodes, then their link lines.
        foreach (var node in Map.Nodes)
        {
            var icon = string.IsNullOrEmpty(node.Icon)
                ? null
                : WorldMapAssets.GetSprite(Map.MapName, node.Icon);
            var view = WorldMapNodeView.Create(_nodesRoot, node, icon, _ui, OnNodeClicked);
            _nodeViews.Add(view);
            if (!string.IsNullOrEmpty(node.Id)) NodeRects[node.Id] = view.Rect;
        }

        foreach (var node in Map.Nodes)
        {
            foreach (var childId in node.Children)
            {
                var child = Map.FindNode(childId);
                if (child == null)
                {
                    Plugin.Log.LogWarning($"World map '{Map.MapName}': node '{node.Id}' links to " +
                                          $"'{childId}', which does not exist.");
                    continue;
                }

                var line = WorldMapLine.Create(_linesRoot, $"Link_{node.Id}_{child.Id}");
                WorldMapLine.Place(line,
                    new Vector2(node.Position.X, node.Position.Y),
                    new Vector2(child.Position.X, child.Position.Y));
                _lines.Add((line, node.Id, child.Id));
            }
        }
    }

    private RectTransform BuildLayer(CTWorldMapLayer layer)
    {
        RectTransform rect = null;

        if (layer.IsSprite)
        {
            var sprite = WorldMapAssets.GetSprite(Map.MapName, layer.Asset);
            if (sprite == null) return null;

            var go = new GameObject("Layer_" + (string.IsNullOrEmpty(layer.Id) ? layer.Asset : layer.Id));
            go.transform.SetParent(_layersRoot, false);
            rect = go.AddComponent<RectTransform>();

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = layer.Tint?.ToColor() ?? Color.white;
            image.raycastTarget = false;
            rect.sizeDelta = new Vector2(sprite.texture.width, sprite.texture.height);
        }
        else if (layer.IsSpine)
        {
            var data = WorldMapAssets.GetSkeleton(Map.MapName, layer.Asset);
            var graphic = WorldMapAssets.CreateSpineGraphic(_layersRoot, data,
                "Layer_" + (string.IsNullOrEmpty(layer.Id) ? layer.Asset : layer.Id),
                layer.Skin, layer.Animation, layer.Loop, layer.TimeScale);
            if (graphic == null) return null;

            rect = graphic.rectTransform;
            graphic.color = layer.Tint?.ToColor() ?? Color.white;
        }

        if (rect == null) return null;

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(layer.Position.X, layer.Position.Y);
        rect.localRotation = Quaternion.Euler(0f, 0f, layer.RotationZ);
        rect.localScale = WorldMapAssets.LayerLocalScale(layer);
        if (!string.IsNullOrEmpty(layer.Id)) LayerRects[layer.Id] = rect;
        return rect;
    }

    // ---- states -----------------------------------------------------------------------------

    public void RefreshStates()
    {
        if (Map == null) return;

        if (EditMode)
        {
            foreach (var view in _nodeViews) view.SetEditView();
            foreach (var (rect, _, _) in _lines) WorldMapLine.SetEditState(rect);

            var editor = WorldMapEditor.Instance;
            HighlightNode(editor != null ? editor.SelectedNodeId : null);
            HighlightLayer(editor != null ? editor.SelectedLayerId : null);
            return;
        }

        HighlightLayer(null);

        var record = WorldMapProgress.For(Map.MapName);
        _states = WorldMapStateResolver.Resolve(Map, record);

        foreach (var view in _nodeViews)
        {
            var state = StateOf(view.Data.Id);
            var affordable = view.Data.IsLock && record.KeysHeld >= view.Data.KeysCost;
            view.SetState(state, affordable);
        }

        foreach (var (rect, fromId, toId) in _lines)
            WorldMapLine.SetLinkState(rect, StateOf(fromId), StateOf(toId));
    }

    private WorldMapSelectionFrame _layerFrame;
    private WorldMapHandle _layerHandle;
    private WorldMapHandle _layerRotateHandle;
    private WorldMapHandle _nodeHandle;

    // The corner nodes the tools drag by; null while nothing is selected.
    internal WorldMapHandle LayerHandle => _layerHandle;
    internal WorldMapHandle LayerRotateHandle => _layerRotateHandle;
    internal WorldMapHandle NodeHandle => _nodeHandle;

    // A node's own rect is small and its art hangs outside it; this is the floor its gizmo box uses.
    private const float NodeGizmoMinScreen = 56f;

    // Draws the editor's frame around one layer. Rebuilt rather than moved: a redraw replaces the
    // layer objects the frame hangs from.
    internal void HighlightLayer(string layerId)
    {
        if (_layerFrame != null) Destroy(_layerFrame.gameObject);
        if (_layerHandle != null) Destroy(_layerHandle.gameObject);
        if (_layerRotateHandle != null) Destroy(_layerRotateHandle.gameObject);
        _layerFrame = null;
        _layerHandle = null;
        _layerRotateHandle = null;

        if (string.IsNullOrEmpty(layerId) || Map == null) return;
        if (!LayerRects.TryGetValue(layerId, out var rect) || rect == null) return;

        var layer = Map.Layers.Find(candidate =>
            candidate != null && string.Equals(candidate.Id, layerId, StringComparison.OrdinalIgnoreCase));

        var min = WorldMapSelectionFrame.MinFor(layer);
        _layerFrame = WorldMapSelectionFrame.Create(rect, _gizmoRoot, min);
        _layerHandle = WorldMapHandle.Create(rect, _gizmoRoot, min, new Vector2(1f, 1f),
            WorldMapHandle.ScaleColour);
        _layerRotateHandle = WorldMapHandle.Create(rect, _gizmoRoot, min, new Vector2(-1f, 1f),
            WorldMapHandle.RotateColour);
    }

    // Marks one node as the editor's selection and clears the mark from the rest.
    internal void HighlightNode(string nodeId)
    {
        _markedNodeId = nodeId;
        RepaintMarks();

        if (_nodeHandle != null) Destroy(_nodeHandle.gameObject);
        _nodeHandle = null;

        // Only while editing: in play view a node is travelled to, not resized.
        if (!EditMode || string.IsNullOrEmpty(nodeId)) return;
        if (!NodeRects.TryGetValue(nodeId, out var rect) || rect == null) return;

        _nodeHandle = WorldMapHandle.Create(rect, _gizmoRoot, NodeGizmoMinScreen, new Vector2(1f, 1f),
            WorldMapHandle.ScaleColour);
    }

    private string _markedNodeId;
    private readonly HashSet<string> _requiredMarks = new(StringComparer.OrdinalIgnoreCase);

    // The selected node's unlock gate, drawn on the map: the nodes it waits for wear green, so
    // picking them is done by looking at the map rather than by reading a list of ids.
    internal void SetRequiredMarks(IEnumerable<string> ids)
    {
        _requiredMarks.Clear();
        if (ids != null)
            foreach (var id in ids)
                if (!string.IsNullOrWhiteSpace(id)) _requiredMarks.Add(id);

        RepaintMarks();
    }

    private void RepaintMarks()
    {
        foreach (var view in _nodeViews)
        {
            if (view == null || view.Data == null) continue;

            // The selection wins where a node is both: it is the one being worked on.
            if (!string.IsNullOrEmpty(_markedNodeId) &&
                string.Equals(view.Data.Id, _markedNodeId, StringComparison.OrdinalIgnoreCase))
                view.SetMark(WorldMapNodeView.SelectedMark);
            else if (_requiredMarks.Contains(view.Data.Id))
                view.SetMark(WorldMapNodeView.RequiredMark);
            else
                view.SetMark(null);
        }
    }

    private WorldNodeState StateOf(string id) =>
        id != null && _states.TryGetValue(id, out var state) ? state : WorldNodeState.Hidden;

    // ---- interaction ------------------------------------------------------------------------

    private void OnNodeClicked(WorldMapNodeView view)
    {
        if (!_open || EditMode) return;

        var node = view.Data;
        var record = WorldMapProgress.For(Map.MapName);

        if (node.IsBase)
        {
            Close();
            return;
        }

        switch (view.State)
        {
            // The one prompt left: keys are spent for good, and the vanilla map has no equivalent
            // gesture to copy. Everything else acts on the click, the way its nodes do.
            case WorldNodeState.Locked:
                if (record.KeysHeld < node.KeysCost) return;
                ShowConfirm(node.KeysCost == 1
                        ? $"Spend 1 key to open {node.DisplayName}?"
                        : $"Spend {node.KeysCost} keys to open {node.DisplayName}?",
                    () =>
                    {
                        WorldMapProgress.OpenLock(Map, node);
                        RefreshStates();
                    },
                    confirmLabel: "Spend");
                return;

            case WorldNodeState.Selectable:
            case WorldNodeState.Completed:
                if (string.Equals(node.TargetKind, "None", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(node.Target))
                {
                    // No destination: selecting completes on the spot.
                    if (view.State == WorldNodeState.Completed) return;
                    WorldMapProgress.MarkCompleted(Map, node.Id);
                    RefreshStates();
                    return;
                }

                Travel(node);
                return;
        }
    }

    private void Travel(CTWorldMapNode node)
    {
        CustomMapSkin.Play("event:/dlc/ui/map/node_dungeon_enter");

        APIHelper.CustomDungeon dungeon = null;

        // A hub is not a dungeon: it is the game's own town room, emptied and dressed with the
        // authored blueprint, so it travels through the hub session rather than a CustomDungeon.
        if (string.Equals(node.TargetKind, "Hub", StringComparison.OrdinalIgnoreCase))
        {
            var mapForHub = Map.MapName;
            var error = HubSession.Play(node.Target);
            if (error != null)
            {
                SetStatus(error);
                return;
            }

            Close();

            // Entering a hub never completes the node - a hub has no success path to record.
            WorldMapProgress.AbortTracking();
            Plugin.Log.LogInfo($"World map '{mapForHub}': entering hub '{node.Target}'.");
            return;
        }

        if (string.Equals(node.TargetKind, "DungeonMap", StringComparison.OrdinalIgnoreCase))
        {
            dungeon = CTMapDungeon.Find(node.Target);
            if (dungeon == null)
            {
                SetStatus($"Dungeon map '{node.Target}' is not saved on this machine.");
                return;
            }
        }
        else if (string.Equals(node.TargetKind, "Level", StringComparison.OrdinalIgnoreCase))
        {
            CTLevelBlueprint level = null;
            foreach (var candidate in CTLevelSerialization.LoadAll())
                if (candidate != null && string.Equals(candidate.LevelName, node.Target,
                        StringComparison.OrdinalIgnoreCase))
                {
                    level = candidate;
                    break;
                }

            if (level == null)
            {
                SetStatus($"Level '{node.Target}' is not saved on this machine.");
                return;
            }
            if (CTLevelDungeon.Instance == null)
            {
                SetStatus("The level runner is not registered.");
                return;
            }

            // Bind the run state first, then the dungeon that carries it.
            var error = LevelPlayback.StartForMapNode(level);
            if (error != null)
            {
                SetStatus(error);
                return;
            }

            CTLevelDungeon.Instance.Level = level;
            dungeon = CTLevelDungeon.Instance;
        }
        else
        {
            SetStatus($"Node '{node.Id}' has unknown target kind '{node.TargetKind}'.");
            return;
        }

        // Close FIRST: entering plays a transition and loads a scene, both need the clock running.
        var mapName = Map.MapName;
        Close();

        try
        {
            dungeon.EnterDungeon();

            // AFTER the entry - EnterDungeon's first act is to abort any stale tracking.
            WorldMapProgress.BeginTracking(mapName, node.Id);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"World map: entering '{node.Id}' failed: {e}");
        }
    }

    // ---- parallax ---------------------------------------------------------------------------

    private void UpdateParallax()
    {
        if (_parallax.Count == 0 || Map == null) return;

        // Mouse position as -1..1 from screen centre; layers drift towards it by their distance.
        var mouse = Input.mousePosition;
        var offset = new Vector2(
            Mathf.Clamp(mouse.x / Mathf.Max(1f, Screen.width) * 2f - 1f, -1f, 1f),
            Mathf.Clamp(mouse.y / Mathf.Max(1f, Screen.height) * 2f - 1f, -1f, 1f));

        foreach (var (rect, basePos, distance) in _parallax)
        {
            if (rect == null) continue;
            var target = basePos + offset * (30f * distance * Map.ParallaxStrength);
            rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, target,
                Time.unscaledDeltaTime * 6f);
        }
    }
}

// The unlock cascade; see README "Nodes and unlocking".
public static class WorldMapStateResolver
{
    public static Dictionary<string, WorldNodeState> Resolve(CTWorldMap map, WorldMapRecord record)
    {
        var states = new Dictionary<string, WorldNodeState>(StringComparer.OrdinalIgnoreCase);
        if (map == null || record == null) return states;

        foreach (var node in map.Nodes)
        {
            var state = ParseInitial(node.InitialState);

            if (node.IsLock && record.IsLockOpened(node.Id)) state = WorldNodeState.Completed;
            else if (!node.IsLock && record.IsCompleted(node.Id)) state = WorldNodeState.Completed;
            else if (node.IsLock && state == WorldNodeState.Selectable) state = WorldNodeState.Locked;

            states[node.Id] = state;
        }

        // Completed nodes radiate; opened locks count as completed. Base radiates from the
        // start - it never completes, so without this no run could ever begin.
        foreach (var node in map.Nodes)
        {
            if (!states.TryGetValue(node.Id, out var state)) continue;
            if (state == WorldNodeState.Completed || (node.IsBase && state != WorldNodeState.Hidden))
                Cascade(map, record, states, node, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        // Count gates clamp last, after the cascade.
        foreach (var node in map.Nodes)
        {
            if (node.RequiredCompletedCount <= 0) continue;
            if (!states.TryGetValue(node.Id, out var state) || state != WorldNodeState.Selectable)
                continue;

            var done = 0;
            foreach (var requiredId in node.RequiredNodes)
                if (record.IsCompleted(requiredId)) done++;

            if (done < node.RequiredCompletedCount) states[node.Id] = WorldNodeState.Preview;
        }

        return states;
    }

    private static void Cascade(CTWorldMap map, WorldMapRecord record,
        Dictionary<string, WorldNodeState> states, CTWorldMapNode node, int depth,
        HashSet<string> visited)
    {
        if (node == null || !visited.Add(node.Id)) return;

        foreach (var childId in node.Children)
        {
            var child = map.FindNode(childId);
            if (child == null) continue;

            if (depth == 0)
            {
                if (child.IsLock && !record.IsLockOpened(child.Id))
                {
                    // A closed lock at the frontier: what lies beyond previews but goes no further.
                    Upgrade(states, child.Id, WorldNodeState.Locked);
                    foreach (var grandchildId in child.Children)
                        if (map.FindNode(grandchildId) != null)
                            Upgrade(states, grandchildId, WorldNodeState.Preview);
                    continue;
                }

                Upgrade(states, child.Id, WorldNodeState.Selectable);
                Cascade(map, record, states, child, 1, visited);
            }
            else
            {
                Upgrade(states, child.Id, WorldNodeState.Preview);
            }
        }
    }

    // Locked ranks with Selectable so neither pulls the other down where branches meet.
    private static int Rank(WorldNodeState state) => state switch
    {
        WorldNodeState.Completed => 3,
        WorldNodeState.Selectable => 2,
        WorldNodeState.Locked => 2,
        WorldNodeState.Preview => 1,
        _ => 0
    };

    private static void Upgrade(Dictionary<string, WorldNodeState> states, string id,
        WorldNodeState target)
    {
        if (!states.TryGetValue(id, out var current)) return;
        if (Rank(target) > Rank(current)) states[id] = target;
    }

    private static WorldNodeState ParseInitial(string initial)
    {
        if (string.Equals(initial, "Selectable", StringComparison.OrdinalIgnoreCase))
            return WorldNodeState.Selectable;
        if (string.Equals(initial, "Preview", StringComparison.OrdinalIgnoreCase))
            return WorldNodeState.Preview;
        return WorldNodeState.Hidden;
    }
}

// The screen's only seam to the editor; a build without the editor still shows maps.
internal static class WorldMapEditorBridge
{
    public static void RequestEditMode(WorldMapScreen screen)
    {
        if (WorldMapEditor.Instance == null)
        {
            Plugin.Log.LogWarning("World map: no editor is available; showing the map in play mode.");
            return;
        }

        WorldMapEditor.Instance.EnterEditMode(screen);
    }

    public static void NotifyClosed(WorldMapScreen screen)
    {
        WorldMapEditor.Instance?.OnScreenClosed(screen);
    }
}
