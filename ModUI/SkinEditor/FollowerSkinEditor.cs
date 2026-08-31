using System;
using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.MapEditor.Tools;
using CustomSpineLoader.SpineLoaderHelper;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI.SkinEditor;

public class FollowerSkinEditor : MonoBehaviour, IMapEditorHost
{
    public static FollowerSkinEditor Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._open;

    private bool _open;

    public CTFollowerSkinDocument Document { get; private set; }
    private string _savedJson = "";

    public bool HasUnsavedEdits =>
        Document != null && CTFollowerSkinSerialization.ToJson(Document.Config) != _savedJson;

    public int ColourSet { get; set; }
    public string Animation { get; set; }
    public bool Loop { get; set; } = true;
    public float Speed { get; set; } = 1f;

    public readonly FollowerSkinPreview Preview = new();

    public string SelectedLayer { get; set; }

    private readonly MapEditorUI _ui = new();

    private sealed class SidePanel
    {
        public IMapEditorTool Tool;
        public RectTransform Rect;
        public RectTransform Content;
        public RectTransform Column;
        public GameObject ColumnRoot;
        public GameObject CollapseButton;
        public bool Collapsed;
        public float Width;
    }

    private readonly List<SidePanel> _sides = [];

    private GameObject _canvasGO;
    private GameObject _statusGO;
    private GameObject _previewGO;
    private RectTransform _shortcutPanel;
    private TMP_Text _statusText;
    private Image _statusPlate;
    private Color _statusPlateColour;
    private bool _busy;
    private TMP_Text _previewCaption;
    private bool _shortcutsCollapsed;
    private MapEditorConfirm _confirm;

    private string _statusMessage = "";
    private StatusSeverity _statusSeverity = StatusSeverity.Info;
    private string _hoverMessage;
    private float _nextErrorAt;
    private int _optionsRebuildFrames;
    private int _rebuildQueued;

    private bool _previewDirty;
    private float _previewDueAt;
    private const float PreviewDebounce = 0.12f;

    private float _prebakeAt = float.MaxValue;
    private const float PrebakeIdle = 1.5f;

    private bool _cursorWasVisible;
    private CursorLockMode _cursorLock;
    private float _savedTimeScale = 1f;
    private const float PausedTimeScale = 0f;

    private const float LeftWidth = 540f;
    private const float RightWidth = 460f;
    private const float PanelHeight = 900f;
    private const float PanelHeaderHeight = 34f;
    private const float ConfirmBottom = 168f;
    private const float PreviewWidth = 320f;
    private const float PreviewHeight = 320f;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        Preview.Release();
        if (Instance == this) Instance = null;
    }

    // ---- opening and closing ----------------------------------------------------------------------

    public void Toggle()
    {
        if (_open) RequestClose();
        else Open();
    }

    public static string WhyNot(bool ignorePanel = false)
    {
        if (MenuEditor.MenuSceneRefs.InMenuScene) return "The skin editor opens in the game, not on the title screen.";
        if (MenuEditor.MainMenuEditor.IsOpen) return "Close the menu editor first.";
        if (RuntimeMapEditor.Active is { IsEditing: true }) return "Close the room editor first.";
        if (MapEditor.WorldMap.WorldMapScreen.IsOpen) return "Close the world map first.";
        if (!ignorePanel && CultTweakerPanel.Active is { IsOpen: true }) return "Close the Cult Tweaker panel first.";
        if (WorshipperData.Instance == null || WorshipperData.Instance.SkeletonData == null)
            return "The follower skeleton is not loaded here.";
        return null;
    }

    public void Open()
    {
        if (_open) return;

        var why = WhyNot();
        if (why != null)
        {
            Plugin.Log.LogInfo("SkinEditor: " + why);
            return;
        }

        _open = true;

        Document = LoadWorkingCopy();
        _savedJson = CTFollowerSkinSerialization.ToJson(Document.Config);
        ColourSet = 0;

        TakeInput();
        BuildUi();

        if (!Preview.Build(_previewGO.GetComponent<RectTransform>()))
        {
            SetStatus("The preview could not be built; the log says why.", StatusSeverity.Error);
        }

        Animation ??= Preview.DefaultAnimation();

        FillPanels();

        if (Preview.HasPacked(Document, !HasUnsavedEdits))
        {
            RefreshNow();
            SetStatus(EditingMessage());
        }
        else
        {
            SetBusy($"Opening {Document.SkinName}, lag will occur!");
            Refresh();
        }
    }

    private static CTFollowerSkinDocument LoadWorkingCopy()
    {
        var skins = CTFollowerSkinSerialization.ListSkins();
        foreach (var skin in skins)
        {
            var variants = CTFollowerSkinSerialization.ListVariants(skin);
            var pick = variants.Contains(CTFollowerSkinSerialization.DefaultVariant)
                ? CTFollowerSkinSerialization.DefaultVariant
                : variants.Count > 0 ? variants[0] : null;
            if (pick == null) continue;
            var loaded = CTFollowerSkinSerialization.Load(skin, pick);
            if (loaded != null) return loaded;
        }

        return NewDocument();
    }

    public static CTFollowerSkinDocument NewDocument() => new()
    {
        SkinName = CTFollowerSkinSerialization.FreeSkinName(),
        VariantName = CTFollowerSkinSerialization.DefaultVariant,
        Config = new FollowerSkinConfig { OverrideBaseSkin = "Cat", PartConfigs = [] }
    };

    public void RequestClose()
    {
        if (!_open) return;

        if (!HasUnsavedEdits)
        {
            Close();
            return;
        }

        _confirm?.Show($"Save '{Document.ShownName}' before closing?",
            () =>
            {
                SaveNow();
                Close();
            },
            "Save", "Discard", Close);
    }

    public void Close()
    {
        if (!_open) return;

        foreach (var side in _sides) side.Tool?.OnExit();
        _open = false;
        ModalOpen = false;
        _ui.CloseTransientUi();

        Preview.Sleep();

        if (_canvasGO != null) Destroy(_canvasGO);
        _canvasGO = null;
        _previewGO = null;
        _previewCaption = null;
        _statusGO = null;
        _statusText = null;
        _shortcutPanel = null;
        _hoverMessage = null;
        _confirm = null;
        _sides.Clear();

        ReleaseInput();
    }

    public void ForceClose()
    {
        if (_open)
        {
            Plugin.Log.LogInfo("SkinEditor: the scene changed underneath the editor; unsaved edits were dropped.");
            _savedJson = CTFollowerSkinSerialization.ToJson(Document.Config);
            Close();
        }

        Preview.Release();
    }

    // ---- the game's own input ---------------------------------------------------------------------

    private void TakeInput()
    {
        _cursorWasVisible = Cursor.visible;
        _cursorLock = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        _savedTimeScale = Time.timeScale;
        Time.timeScale = PausedTimeScale;

        try
        {
            TriggerActions.SetControl(false);
            if (HUD_Manager.Instance != null) HUD_Manager.Instance.Hide(true, 0);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("SkinEditor: could not take control: " + e.Message);
        }
    }

    private void ReleaseInput()
    {
        Cursor.visible = _cursorWasVisible;
        Cursor.lockState = _cursorLock;

        Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;

        try
        {
            TriggerActions.SetControl(true);
            if (HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0, true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("SkinEditor: could not hand control back: " + e.Message);
        }
    }

    // ---- IMapEditorHost -----------------------------------------------------------------------------

    public bool ModalOpen
    {
        get => _modalOpen;
        set
        {
            _modalOpen = value;
            var canvas = _canvasGO != null ? _canvasGO.GetComponent<Canvas>() : null;
            if (canvas != null && _open) canvas.enabled = !value;
            if (!value && _open) Time.timeScale = PausedTimeScale;
        }
    }

    private bool _modalOpen;

    public void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        if (severity is StatusSeverity.Warning or StatusSeverity.Error)
            Plugin.Log.LogWarning("SkinEditor: " + message);

        _statusMessage = message;
        _statusSeverity = severity;
        if (_hoverMessage == null) Paint(message, severity);
    }

    public void ShowHoverStatus(string message)
    {
        if (!_open || string.IsNullOrEmpty(message)) return;
        _hoverMessage = message;
        Paint(message, StatusSeverity.Info);
    }

    public void ClearHoverStatus()
    {
        if (!_open) return;
        _hoverMessage = null;
        Paint(_statusMessage, _statusSeverity);
    }

    public bool Busy => _busy;

    public void SetBusy(string message)
    {
        _busy = true;
        PaintPlate();
        SetStatus(message, StatusSeverity.Warning);
    }

    private void ClearBusy()
    {
        if (!_busy) return;

        _busy = false;
        PaintPlate();
        SetStatus(EditingMessage());
    }

    private string EditingMessage()
    {
        var line = $"Editing '{Document.ShownName}'. Esc closes.";
        return Preview.WearingRegistered
            ? line + " Lag spikes will occur on first edits."
            : line;
    }

    private void PaintPlate()
    {
        if (_statusPlate == null) return;

        _statusPlate.color = _busy
            ? new Color(0.85f, 0.5f, 0.12f, Mathf.Max(_statusPlateColour.a, 0.9f))
            : _statusPlateColour;
    }

    public void RegisterUiBlocker(RectTransform rect) { }

    public void BlockWorldClicks() { }

    public void RequestOptionsResize() => _optionsRebuildFrames = 3;

    private void Paint(string message, StatusSeverity severity)
    {
        if (_statusText == null) return;

        _statusText.text = message ?? "";
        _statusText.color = severity switch
        {
            StatusSeverity.Success => new Color(0.6f, 1f, 0.6f),
            StatusSeverity.Warning => new Color(1f, 0.85f, 0.4f),
            StatusSeverity.Error => new Color(1f, 0.5f, 0.5f),
            _ => Color.white
        };
    }

    // ---- saving and refreshing ---------------------------------------------------------------------

    private bool _saving;

    public void Save()
    {
        if (Document == null || _saving) return;
        _saving = true;

        SetStatus($"Saving '{Document.ShownName}' and rebuilding it for the game - expect a short pause.",
            StatusSeverity.Warning);
        StartCoroutine(SaveAfterPaint());
    }

    private IEnumerator SaveAfterPaint()
    {
        yield return null;
        yield return null;

        try
        {
            SaveNow();
        }
        finally
        {
            _saving = false;
        }
    }

    public void SaveNow()
    {
        if (Document == null) return;

        var path = CTFollowerSkinSerialization.Save(Document);
        if (path == null)
        {
            SetStatus("The skin could not be saved; the log says why.", StatusSeverity.Error);
            return;
        }

        _savedJson = CTFollowerSkinSerialization.ToJson(Document.Config);

        RebuildPanels();

        var reloaded = FollowerSpineLoader.Reload(Document.SkinName);
        SetStatus(reloaded
                ? $"Saved '{Document.ShownName}'. Followers can wear it now."
                : $"Saved '{Document.ShownName}'. It will load with the game next time.",
            StatusSeverity.Success);
    }

    public void Refresh()
    {
        _previewDirty = true;
        _previewDueAt = Time.unscaledTime + PreviewDebounce;
    }

    public void RefreshNow()
    {
        _previewDirty = false;
        if (Document == null || !Preview.Ready) return;

        Document.NormaliseColourSets();
        ColourSet = Mathf.Clamp(ColourSet, 0, Document.ColourSetCount - 1);

        var problem = Preview.Show(Document, ColourSet, !HasUnsavedEdits);
        if (problem != null)
        {
            _busy = false;
            PaintPlate();
            SetStatus(problem, StatusSeverity.Warning);
        }
        else
        {
            ClearBusy();
        }

        Preview.Play(Animation, Loop, Speed);

        if (_previewCaption != null)
            _previewCaption.text = $"{Document.ShownName}   colour set {ColourSet + 1} of {Document.ColourSetCount}";

        _prebakeAt = Preview.WearingRegistered ? Time.unscaledTime + PrebakeIdle : float.MaxValue;
    }

    public void LoadDocument(CTFollowerSkinDocument document, string busyMessage = null)
    {
        if (document == null) return;

        Document = document;
        _savedJson = CTFollowerSkinSerialization.ToJson(Document.Config);
        ColourSet = 0;
        RebuildPanels();

        if (Preview.HasPacked(Document, !HasUnsavedEdits))
        {
            RefreshNow();
            return;
        }

        SetBusy(busyMessage ?? $"Opening {Document.SkinName}, lag will occur!");
        Refresh();
    }

    public void MarkSaved() => _savedJson = CTFollowerSkinSerialization.ToJson(Document.Config);

    // ---- the frame ---------------------------------------------------------------------------------

    private void Update()
    {
        if (!_open) return;

        if (!ModalOpen && !Mathf.Approximately(Time.timeScale, PausedTimeScale)) Time.timeScale = PausedTimeScale;

        if (_rebuildQueued > 0)
        {
            var everything = _rebuildQueued > 1;
            _rebuildQueued = 0;
            DoRebuildPanels(everything);
        }

        SettleOptions();

        if (_previewDirty && Time.unscaledTime >= _previewDueAt) RefreshNow();
        else if (!_busy && !ModalOpen && Time.unscaledTime >= _prebakeAt) Prebake();

        Preview.Tick();

        if (ModalOpen) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_ui.TransientUiOpen) _ui.CloseTransientUi();
            else if (_confirm is { Open: true }) _confirm.Hide();
            else RequestClose();
            return;
        }

        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            Input.GetKeyDown(KeyCode.S))
        {
            Save();
            return;
        }

        foreach (var side in _sides)
        {
            try
            {
                side.Tool?.OnUpdate();
            }
            catch (Exception e)
            {
                if (Time.unscaledTime < _nextErrorAt) continue;

                _nextErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"SkinEditor: panel '{side.Tool?.Name}' failed: {e}");
            }
        }
    }

    private void Prebake()
    {
        _prebakeAt = float.MaxValue;
        if (!Preview.WearingRegistered) return;

        SetBusy($"Getting {Document.SkinName} ready to edit - one pause now instead of on your first edit.");
        StartCoroutine(PrebakeAfterPaint());
    }

    private IEnumerator PrebakeAfterPaint()
    {
        yield return null;
        yield return null;

        if (!_open) yield break;

        var problem = Preview.Show(Document, ColourSet);
        if (problem != null)
        {
            _busy = false;
            PaintPlate();
            SetStatus(problem, StatusSeverity.Warning);
            yield break;
        }

        ClearBusy();
    }

    private void SettleOptions()
    {
        if (_optionsRebuildFrames <= 0) return;
        _optionsRebuildFrames--;

        foreach (var side in _sides)
            if (side.Column != null) LayoutRebuilder.ForceRebuildLayoutImmediate(side.Column);
    }

    // ---- the canvas --------------------------------------------------------------------------------

    private void BuildUi()
    {
        _canvasGO = new GameObject("SkinEditor_Canvas");
        _canvasGO.transform.SetParent(transform, false);

        var canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        if (EventSystem.current == null)
        {
            var events = new GameObject("SkinEditor_EventSystem");
            events.transform.SetParent(transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        var canvasRoot = _canvasGO.GetComponent<RectTransform>();
        _ui.Attach(this, canvasRoot);

        BuildBackdrop(canvasRoot);

        _sides.Clear();
        _sides.Add(BuildSide(canvasRoot, new Tools.SkinSetupPanel(this), true, LeftWidth));
        _sides.Add(BuildSide(canvasRoot, new Tools.SkinLayerPanel(this), false, RightWidth));

        BuildPreviewPanel(canvasRoot);
        BuildShortcutPanel(canvasRoot);
        BuildStatusBar(canvasRoot);

        _confirm = new MapEditorConfirm(_ui, canvasRoot, ConfirmBottom);
    }

    private void BuildBackdrop(RectTransform canvasRoot)
    {
        var go = new GameObject("SkinEditor_Backdrop");
        go.transform.SetParent(canvasRoot, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.82f);
        image.raycastTarget = true;
    }

    private SidePanel BuildSide(RectTransform canvasRoot, IMapEditorTool tool, bool onLeft, float width)
    {
        var go = new GameObject("SkinEditor_" + tool.Name);
        go.transform.SetParent(canvasRoot, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(onLeft ? 0f : 1f, 1f);
        rect.pivot = new Vector2(onLeft ? 0f : 1f, 1f);
        rect.sizeDelta = new Vector2(width, PanelHeight);
        rect.anchoredPosition = new Vector2(onLeft ? 14f : -14f, -70f);

        VanillaChrome.Dress(go.AddComponent<Image>());

        var header = new GameObject("Header");
        header.transform.SetParent(rect, false);

        var headerRect = header.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0f, PanelHeaderHeight);
        headerRect.anchoredPosition = Vector2.zero;
        header.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        var title = _ui.CreateLabel(header.transform, tool.Name, 20, TextAlignmentOptions.Left);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(12f, 0f);
        titleRect.offsetMax = new Vector2(-36f, 0f);
        var titleText = title.GetComponent<TMP_Text>();
        titleText.enableWordWrapping = false;
        titleText.raycastTarget = false;

        SidePanel side = null;
        var collapse = _ui.CreateButton(header.transform, "-", () => ToggleCollapsed(side), 26f);
        var collapseRect = collapse.GetComponent<RectTransform>();
        collapseRect.anchorMin = collapseRect.anchorMax = new Vector2(1f, 0.5f);
        collapseRect.pivot = new Vector2(1f, 0.5f);
        collapseRect.sizeDelta = new Vector2(26f, 26f);
        collapseRect.anchoredPosition = new Vector2(-4f, 0f);

        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(rect, false);
        var content = contentGO.AddComponent<RectTransform>();
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = Vector2.zero;
        content.offsetMax = new Vector2(0f, -PanelHeaderHeight);

        var column = _ui.CreateScrollColumn(content, "Column_" + tool.Name, out var columnRoot);

        side = new SidePanel
        {
            Tool = tool,
            Rect = rect,
            Content = content,
            Column = column,
            ColumnRoot = columnRoot,
            CollapseButton = collapse,
            Width = width
        };

        return side;
    }

    private void ToggleCollapsed(SidePanel side)
    {
        if (side == null) return;

        side.Collapsed = !side.Collapsed;

        if (side.Content != null) side.Content.gameObject.SetActive(!side.Collapsed);
        if (side.Rect != null)
            side.Rect.sizeDelta = new Vector2(side.Width, side.Collapsed ? PanelHeaderHeight : PanelHeight);

        var label = side.CollapseButton != null
            ? side.CollapseButton.GetComponentInChildren<TMP_Text>()
            : null;
        if (label != null) label.text = side.Collapsed ? "+" : "-";
    }

    private void BuildPreviewPanel(RectTransform canvasRoot)
    {
        _previewGO = new GameObject("SkinEditor_Preview");
        _previewGO.transform.SetParent(canvasRoot, false);

        var rect = _previewGO.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(PreviewWidth, PreviewHeight);
        rect.anchoredPosition = new Vector2((LeftWidth - RightWidth) * 0.5f, 60f);

        var caption = _ui.CreateLabel(_previewGO.transform, "", 18, TextAlignmentOptions.Center);
        var captionRect = caption.GetComponent<RectTransform>();
        captionRect.anchorMin = new Vector2(0f, 0f);
        captionRect.anchorMax = new Vector2(1f, 0f);
        captionRect.pivot = new Vector2(0.5f, 1f);
        captionRect.sizeDelta = new Vector2(0f, 30f);
        captionRect.anchoredPosition = new Vector2(0f, -6f);
        _previewCaption = caption.GetComponent<TMP_Text>();
        _previewCaption.raycastTarget = false;
    }

    // ---- the two panels ----------------------------------------------------------------------------

    private void FillPanels()
    {
        foreach (var side in _sides)
        {
            if (side.Column == null) continue;

            try
            {
                side.Tool.BuildPanel(side.Column, _ui);
                side.Tool.OnEnter();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"SkinEditor: panel '{side.Tool.Name}' could not be built: {e}");
            }
        }

        RequestOptionsResize();
    }

    public void RebuildPanels() => _rebuildQueued = 2;

    public void RebuildInspector()
    {
        if (_rebuildQueued < 1) _rebuildQueued = 1;
    }

    private void DoRebuildPanels(bool everything)
    {
        for (var i = 0; i < _sides.Count; i++)
        {
            var side = _sides[i];
            if (side.Column == null) continue;
            if (!everything && i == 0) continue;

            var scroll = side.ColumnRoot != null ? side.ColumnRoot.GetComponent<ScrollRect>() : null;
            var where = everything || scroll == null ? 1f : scroll.verticalNormalizedPosition;

            var stale = new List<GameObject>();
            foreach (Transform child in side.Column) stale.Add(child.gameObject);

            foreach (var child in stale)
            {
                child.transform.SetParent(null, false);
                Destroy(child);
            }

            try
            {
                side.Tool.BuildPanel(side.Column, _ui);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"SkinEditor: panel '{side.Tool.Name}' could not rebuild: {e}");
            }

            if (scroll == null) continue;

            scroll.StopMovement();
            if (scroll.content != null) scroll.content.anchoredPosition = Vector2.zero;
            StartCoroutine(RestoreScroll(scroll, where));
        }

        RequestOptionsResize();
    }

    private static IEnumerator RestoreScroll(ScrollRect scroll, float where)
    {
        yield return null;
        yield return null;

        if (scroll == null) yield break;

        scroll.StopMovement();
        scroll.verticalNormalizedPosition = Mathf.Clamp01(where);
    }

    private void BuildShortcutPanel(RectTransform canvasRoot)
    {
        var go = new GameObject("SkinEditor_Shortcuts");
        go.transform.SetParent(canvasRoot, false);

        _shortcutPanel = go.AddComponent<RectTransform>();
        _shortcutPanel.anchorMin = _shortcutPanel.anchorMax = new Vector2(0.5f, 0f);
        _shortcutPanel.pivot = new Vector2(0.5f, 0f);
        _shortcutPanel.sizeDelta = new Vector2(252f, 0f);
        _shortcutPanel.anchoredPosition = new Vector2((LeftWidth - RightWidth) * 0.5f, 62f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        RefreshShortcuts();
    }

    private void RefreshShortcuts()
    {
        if (_shortcutPanel == null) return;

        foreach (Transform child in _shortcutPanel) Destroy(child.gameObject);

        if (!_shortcutsCollapsed)
        {
            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+S", "Save skin");
            _ui.CreateKeyHint(_shortcutPanel, "Esc", "Close editor");
        }

        _ui.CreateButton(_shortcutPanel, _shortcutsCollapsed ? "Shortcuts   +" : "Shortcuts   -",
            () =>
            {
                _shortcutsCollapsed = !_shortcutsCollapsed;
                RefreshShortcuts();
            }, 30f);
    }

    private void BuildStatusBar(RectTransform canvasRoot)
    {
        _statusGO = new GameObject("SkinEditor_Status");
        _statusGO.transform.SetParent(canvasRoot, false);

        var rect = _statusGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);

        rect.offsetMin = new Vector2(LeftWidth + 28f, 12f);
        rect.offsetMax = new Vector2(-(RightWidth + 28f), 12f);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, 40f);

        _statusPlate = _statusGO.AddComponent<Image>();
        VanillaChrome.Dress(_statusPlate);
        _statusPlateColour = _statusPlate.color;
        PaintPlate();

        var label = _ui.CreateLabel(_statusGO.transform, "", 18, TextAlignmentOptions.Left);
        _statusText = label.GetComponent<TMP_Text>();
        _statusText.raycastTarget = false;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(14f, 0f);
        labelRect.offsetMax = new Vector2(-14f, 0f);
        Paint(_statusMessage, _statusSeverity);
    }

    Coroutine IMapEditorHost.StartCoroutine(IEnumerator routine) => StartCoroutine(routine);
}
