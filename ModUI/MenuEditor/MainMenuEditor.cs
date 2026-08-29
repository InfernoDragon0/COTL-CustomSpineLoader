using System;
using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor;
using src.UINavigator;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI.MenuEditor;

public class MainMenuEditor : MonoBehaviour, IMapEditorHost
{
    public static MainMenuEditor Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._open;

    private bool _open;

    public CTMenuPreset Preset { get; private set; }
    private string _savedJson = "";

    public bool HasUnsavedEdits =>
        Preset != null && CTMenuPresetSerialization.ToJson(Preset) != _savedJson;

    private readonly MapEditorUI _ui = new();
    private readonly List<IMapEditorTool> _tools = [];
    private readonly List<(IMapEditorTool tool, GameObject columnRoot, RectTransform content, Image ring)> _panels = [];
    private IMapEditorTool _activeTool;

    private GameObject _canvasGO;
    private GameObject _dockGO;
    private GameObject _optionsGO;
    private GameObject _statusGO;
    private RectTransform _optionsRect;
    private RectTransform _optionsContent;
    private RectTransform _shortcutPanel;
    private TMP_Text _optionsTitle;
    private TMP_Text _statusText;
    private GameObject _optionsCollapseButton;
    private bool _optionsCollapsed;
    private bool _shortcutsCollapsed;
    private MapEditorConfirm _confirm;

    private string _statusMessage = "";
    private StatusSeverity _statusSeverity = StatusSeverity.Info;
    private string _hoverMessage;
    private float _nextErrorAt;
    private int _optionsRebuildFrames;

    private bool _cursorWasVisible;
    private CursorLockMode _cursorLock;
    private bool _driftWasBlocked;

    private const float ToolIconSize = 72f;
    private const int DockPadding = 8;
    private const float DockHeight = ToolIconSize + DockPadding * 2;
    private const float OptionsWidth = 380f;
    private const float OptionsHeight = 900f;
    private const float OptionsHeaderHeight = 34f;
    private const float ConfirmBottom = 168f;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---- opening and closing ----------------------------------------------------------------------

    public void Toggle()
    {
        if (_open) RequestClose();
        else Open();
    }

    public void Open()
    {
        if (_open) return;

        if (!MenuSceneRefs.Ready || !MenuSceneRefs.InMenuScene)
        {
            Plugin.Log.LogInfo("MenuEditor: the menu editor opens on the title screen only.");
            return;
        }

        if (RuntimeMapEditor.Active is { IsEditing: true }) return;
        if (CultTweakerPanel.Active is { IsOpen: true }) return;

        _open = true;

        Preset = LoadWorkingCopy();
        _savedJson = CTMenuPresetSerialization.ToJson(Preset);
        MenuPresetApplier.Use(Preset);
        MenuPresetApplier.ApplyAll();

        TakeInput();
        BuildUi();

        _tools.Clear();
        _tools.Add(new Tools.MenuPresetTool(this));
        _tools.Add(new Tools.MenuLookTool(this));
        _tools.Add(new Tools.MenuCentrepieceTool(this));
        _tools.Add(new Tools.MenuTitleTool(this));

        BuildTools();
        SelectTool(_tools[0]);

        SetStatus($"Editing '{Preset.ShownName}'. Esc closes.");
    }

    private CTMenuPreset LoadWorkingCopy()
    {
        var name = Plugin.MainMenuPreset?.Value ?? "";
        var loaded = string.IsNullOrWhiteSpace(name) ? null : CTMenuPresetSerialization.LoadByName(name);
        if (loaded != null) return loaded;

        return new CTMenuPreset { PresetName = CTMenuPresetSerialization.FreeName("untitledmenu") };
    }

    public void RequestClose()
    {
        if (!_open) return;

        if (!HasUnsavedEdits)
        {
            Close();
            return;
        }

        _confirm?.Show($"Save '{Preset.ShownName}' before closing?",
            () =>
            {
                Save();
                Close();
            },
            "Save", "Discard", Close);
    }

    public void Close()
    {
        if (!_open) return;

        _activeTool?.OnExit();
        _activeTool = null;
        _open = false;
        ModalOpen = false;
        _ui.CloseTransientUi();

        MenuPresetApplier.LoadConfigured();
        MenuPresetApplier.RevertAll();
        MenuPresetApplier.ApplyAll();

        if (_canvasGO != null) Destroy(_canvasGO);
        _canvasGO = null;
        _dockGO = null;
        _optionsGO = null;
        _optionsRect = null;
        _optionsContent = null;
        _optionsTitle = null;
        _optionsCollapseButton = null;
        _optionsCollapsed = false;
        _statusGO = null;
        _statusText = null;
        _shortcutPanel = null;
        _hoverMessage = null;
        _confirm = null;
        _panels.Clear();
        _tools.Clear();

        ReleaseInput();
    }

    public void ForceClose()
    {
        if (!_open) return;

        Plugin.Log.LogInfo("MenuEditor: the menu closed underneath the editor; unsaved edits were dropped.");
        _savedJson = CTMenuPresetSerialization.ToJson(Preset);
        Close();
    }

    // ---- the menu's own input ---------------------------------------------------------------------

    private void TakeInput()
    {
        var navigator = MonoSingleton<UINavigatorNew>.Instance;
        if (navigator != null)
        {
            navigator.LockInput = true;
            navigator.LockNavigation = true;
        }

        var group = MenuSceneRefs.Menu != null ? MenuSceneRefs.Menu.GetComponent<CanvasGroup>() : null;
        if (group != null)
        {
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        _cursorWasVisible = Cursor.visible;
        _cursorLock = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        var drift = MenuSceneRefs.CameraDrift;
        if (drift != null)
        {
            _driftWasBlocked = drift.blockMovement;
            drift.blockMovement = true;
        }
    }

    private void ReleaseInput()
    {
        var navigator = MonoSingleton<UINavigatorNew>.Instance;
        if (navigator != null)
        {
            navigator.LockInput = false;
            navigator.LockNavigation = false;
        }

        var group = MenuSceneRefs.Menu != null ? MenuSceneRefs.Menu.GetComponent<CanvasGroup>() : null;
        if (group != null)
        {
            group.blocksRaycasts = true;
            group.interactable = true;
        }

        Cursor.visible = _cursorWasVisible;
        Cursor.lockState = _cursorLock;

        var drift = MenuSceneRefs.CameraDrift;
        if (drift != null) drift.blockMovement = _driftWasBlocked;
    }

    // ---- IMapEditorHost -----------------------------------------------------------------------------

    public bool ModalOpen
    {
        get => _modalOpen;
        set
        {
            if (_modalOpen == value) return;
            _modalOpen = value;

            var navigator = MonoSingleton<UINavigatorNew>.Instance;
            if (navigator == null) return;

            navigator.LockInput = !value;
            navigator.LockNavigation = !value;
        }
    }

    private bool _modalOpen;

    public void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        if (severity is StatusSeverity.Warning or StatusSeverity.Error)
            Plugin.Log.LogWarning("MenuEditor: " + message);

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

    // ---- saving ---------------------------------------------------------------------------------

    public void Save()
    {
        if (Preset == null) return;

        var path = CTMenuPresetSerialization.Save(Preset);
        if (path == null)
        {
            SetStatus("The preset could not be saved; the log says why.", StatusSeverity.Error);
            return;
        }

        _savedJson = CTMenuPresetSerialization.ToJson(Preset);

        if (Plugin.MainMenuPreset != null) Plugin.MainMenuPreset.Value = Preset.PresetName;

        SetStatus($"Saved '{Preset.ShownName}'. It is now the menu's preset.", StatusSeverity.Success);
    }

    public void Refresh()
    {
        MenuPresetApplier.Use(Preset);
        MenuPresetApplier.ApplyAll();
    }

    public void ReloadPreset(CTMenuPreset preset)
    {
        if (preset == null) return;

        Preset = preset;
        _savedJson = CTMenuPresetSerialization.ToJson(Preset);
        Refresh();
        RebuildPanels();
    }

    // ---- the frame ---------------------------------------------------------------------------------

    private void Update()
    {
        if (!_open) return;

        if (_rebuildQueued)
        {
            _rebuildQueued = false;
            DoRebuildPanels();
        }

        SettleOptions();

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

        try
        {
            _activeTool?.OnUpdate();
        }
        catch (Exception e)
        {
            if (Time.unscaledTime >= _nextErrorAt)
            {
                _nextErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"MenuEditor: tool '{_activeTool?.Name}' failed: {e}");
            }
        }
    }

    private void SettleOptions()
    {
        if (_optionsRebuildFrames <= 0) return;
        _optionsRebuildFrames--;

        foreach (var panel in _panels)
        {
            if (panel.tool != _activeTool || panel.content == null) continue;
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.content);
            return;
        }
    }

    // ---- the canvas --------------------------------------------------------------------------------

    private void BuildUi()
    {
        _canvasGO = new GameObject("MenuEditor_Canvas");
        _canvasGO.transform.SetParent(transform, false);

        var canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        canvas.sortingOrder = 4600;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        if (EventSystem.current == null)
        {
            var events = new GameObject("MenuEditor_EventSystem");
            events.transform.SetParent(transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        var canvasRoot = _canvasGO.GetComponent<RectTransform>();
        _ui.Attach(this, canvasRoot);

        BuildDock(canvasRoot);
        BuildOptions(canvasRoot);
        BuildShortcutPanel(canvasRoot);
        BuildStatusBar(canvasRoot);

        _confirm = new MapEditorConfirm(_ui, canvasRoot, ConfirmBottom);
    }

    private void BuildDock(RectTransform canvasRoot)
    {
        _dockGO = new GameObject("MenuEditor_Dock");
        _dockGO.transform.SetParent(canvasRoot, false);

        var dockRect = _dockGO.AddComponent<RectTransform>();
        dockRect.anchorMin = dockRect.anchorMax = new Vector2(0.5f, 0f);
        dockRect.pivot = new Vector2(0.5f, 0f);
        dockRect.sizeDelta = new Vector2(0f, DockHeight);
        dockRect.anchoredPosition = new Vector2(0f, 12f);

        VanillaChrome.Dress(_dockGO.AddComponent<Image>());

        var layout = _dockGO.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 6f;
        layout.padding = new RectOffset(DockPadding, DockPadding, DockPadding, DockPadding);
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = _dockGO.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void BuildOptions(RectTransform canvasRoot)
    {
        _optionsGO = new GameObject("MenuEditor_Options");
        _optionsGO.transform.SetParent(canvasRoot, false);

        _optionsRect = _optionsGO.AddComponent<RectTransform>();
        _optionsRect.anchorMin = _optionsRect.anchorMax = new Vector2(1f, 1f);
        _optionsRect.pivot = new Vector2(1f, 1f);
        _optionsRect.sizeDelta = new Vector2(OptionsWidth, OptionsHeight);
        _optionsRect.anchoredPosition = new Vector2(-14f, -70f);

        VanillaChrome.Dress(_optionsGO.AddComponent<Image>());

        var header = new GameObject("Header");
        header.transform.SetParent(_optionsRect, false);

        var headerRect = header.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0f, OptionsHeaderHeight);
        headerRect.anchoredPosition = Vector2.zero;
        header.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        var title = _ui.CreateLabel(header.transform, "", 20, TextAlignmentOptions.Left);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(12f, 0f);
        titleRect.offsetMax = new Vector2(-36f, 0f);
        _optionsTitle = title.GetComponent<TMP_Text>();
        _optionsTitle.enableWordWrapping = false;
        _optionsTitle.raycastTarget = false;

        _optionsCollapseButton = _ui.CreateButton(header.transform, "-", ToggleOptionsCollapsed, 26f);
        var collapseRect = _optionsCollapseButton.GetComponent<RectTransform>();
        collapseRect.anchorMin = collapseRect.anchorMax = new Vector2(1f, 0.5f);
        collapseRect.pivot = new Vector2(1f, 0.5f);
        collapseRect.sizeDelta = new Vector2(26f, 26f);
        collapseRect.anchoredPosition = new Vector2(-4f, 0f);

        var content = new GameObject("Content");
        content.transform.SetParent(_optionsRect, false);
        _optionsContent = content.AddComponent<RectTransform>();
        _optionsContent.anchorMin = Vector2.zero;
        _optionsContent.anchorMax = Vector2.one;
        _optionsContent.offsetMin = Vector2.zero;
        _optionsContent.offsetMax = new Vector2(0f, -OptionsHeaderHeight);
    }

    private void ToggleOptionsCollapsed()
    {
        _optionsCollapsed = !_optionsCollapsed;

        if (_optionsContent != null) _optionsContent.gameObject.SetActive(!_optionsCollapsed);
        if (_optionsRect != null)
            _optionsRect.sizeDelta = new Vector2(OptionsWidth,
                _optionsCollapsed ? OptionsHeaderHeight : OptionsHeight);

        var label = _optionsCollapseButton != null
            ? _optionsCollapseButton.GetComponentInChildren<TMP_Text>()
            : null;
        if (label != null) label.text = _optionsCollapsed ? "+" : "-";
    }

    private void BuildTools()
    {
        foreach (var tool in _tools)
        {
            var localTool = tool;
            var button = _ui.CreateIconButton(_dockGO.transform, MapEditorIcons.GetToolIconOrNull(tool.Name),
                ShortLabel(tool.Name), () => SelectTool(localTool), out var ring, ToolIconSize,
                DockHint(tool.Name));
            button.name = "Dock_" + tool.Name;

            var content = _ui.CreateScrollColumn(_optionsContent, "Options_" + tool.Name, out var columnRoot);
            columnRoot.SetActive(false);
            _panels.Add((tool, columnRoot, content, ring));

            try
            {
                tool.BuildPanel(content, _ui);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MenuEditor: tool '{tool.Name}' could not build its panel: {e}");
            }
        }
    }

    public void RebuildPanels() => _rebuildQueued = true;

    private bool _rebuildQueued;

    private void DoRebuildPanels()
    {
        foreach (var panel in _panels)
        {
            if (panel.content == null) continue;

            var stale = new List<GameObject>();
            foreach (Transform child in panel.content) stale.Add(child.gameObject);

            foreach (var child in stale)
            {
                child.transform.SetParent(null, false);
                Destroy(child);
            }

            try
            {
                panel.tool.BuildPanel(panel.content, _ui);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MenuEditor: tool '{panel.tool.Name}' could not rebuild: {e}");
            }

            var scroll = panel.columnRoot != null ? panel.columnRoot.GetComponent<ScrollRect>() : null;
            if (scroll == null) continue;

            scroll.StopMovement();
            if (scroll.content != null) scroll.content.anchoredPosition = Vector2.zero;
        }

        RequestOptionsResize();
    }

    public void SelectTool(IMapEditorTool tool)
    {
        if (tool == null) return;

        _activeTool?.OnExit();
        _ui.CloseTransientUi();
        _activeTool = tool;

        foreach (var panel in _panels)
        {
            var active = panel.tool == tool;
            if (panel.columnRoot != null) panel.columnRoot.SetActive(active);

            if (panel.ring != null) panel.ring.gameObject.SetActive(active);
        }

        if (_optionsTitle != null) _optionsTitle.text = tool.Name;
        RefreshShortcuts();
        tool.OnEnter();
    }

    private void BuildShortcutPanel(RectTransform canvasRoot)
    {
        var go = new GameObject("MenuEditor_Shortcuts");
        go.transform.SetParent(canvasRoot, false);

        _shortcutPanel = go.AddComponent<RectTransform>();
        _shortcutPanel.anchorMin = Vector2.zero;
        _shortcutPanel.anchorMax = Vector2.zero;
        _shortcutPanel.pivot = Vector2.zero;
        _shortcutPanel.sizeDelta = new Vector2(252f, 0f);
        _shortcutPanel.anchoredPosition = new Vector2(16f, 16f);

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
            if (_activeTool is IMapEditorShortcuts source)
                foreach (var (key, action) in source.Shortcuts)
                    _ui.CreateKeyHint(_shortcutPanel, key, action);

            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+S", "Save preset");
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
        _statusGO = new GameObject("MenuEditor_Status");
        _statusGO.transform.SetParent(canvasRoot, false);

        var rect = _statusGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);

        rect.offsetMin = new Vector2(284f, 12f + DockHeight + 8f);
        rect.offsetMax = new Vector2(-408f, 12f + DockHeight + 8f);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, 40f);

        VanillaChrome.Dress(_statusGO.AddComponent<Image>());

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

    private static string ShortLabel(string toolName) =>
        toolName != null && toolName.StartsWith("Menu ") ? toolName.Substring(5) : toolName;

    private static string DockHint(string toolName) => toolName switch
    {
        "Menu Look" => "Menu Look - Palette, background colour, grain and the glitch effect",
        "Menu Centrepiece" => "Menu Centrepiece - Which spine stands on the title screen, and where",
        "Menu Title" => "Menu Title - Replace or move the game's logo",
        "Menu Presets" => "Menu Presets - Save, load and switch between saved menus",
        _ => toolName
    };

    Coroutine IMapEditorHost.StartCoroutine(IEnumerator routine) => StartCoroutine(routine);
}
