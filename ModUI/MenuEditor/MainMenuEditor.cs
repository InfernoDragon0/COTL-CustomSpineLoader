using System;
using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor;
using src.UINavigator;
using Chrome = CustomSpineLoader.MapEditor.Chrome;
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

    private Chrome.EditorTopBar _topBar;
    private Chrome.EditorBottomBar _bottomBar;
    private Chrome.EditorSidebar _sidebar;
    private Chrome.ShortcutCard _card;
    private MapEditorConfirm _confirm;

    private string _statusMessage = "";
    private StatusSeverity _statusSeverity = StatusSeverity.Info;
    private string _hoverMessage;
    private float _nextErrorAt;
    private int _optionsRebuildFrames;

    private bool _cursorWasVisible;
    private CursorLockMode _cursorLock;
    private bool _driftWasBlocked;

    private const float DockIconSize = 60f;
    private const float ConfirmBottom = Chrome.EditorBottomBar.Height + 12f;

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
        _topBar = null;
        _bottomBar = null;
        _sidebar = null;
        _card = null;
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
        if (_hoverMessage == null) Paint(message, severity, pulse: true);
    }

    public void ShowHoverStatus(string message)
    {
        if (!_open || string.IsNullOrEmpty(message)) return;
        _hoverMessage = message;
        Paint(message, StatusSeverity.Info, pulse: false);
    }

    public void ClearHoverStatus()
    {
        if (!_open) return;
        _hoverMessage = null;
        Paint(_statusMessage, _statusSeverity, pulse: false);
    }

    public void RegisterUiBlocker(RectTransform rect) { }

    public void BlockWorldClicks() { }

    public void RequestOptionsResize() => _optionsRebuildFrames = 3;

    /// A hover message only repaints the words; a real one may also ring the region, which is how
    /// the editor says "this one needs you", and a hover must not clear that.
    private void Paint(string message, StatusSeverity severity, bool pulse) =>
        _bottomBar?.SetStatus(message, severity, pulse);

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
        PollUnsaved();

        if (ModalOpen) return;

        if (Input.GetKeyDown(KeyCode.F1))
        {
            _card?.Toggle();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_ui.TransientUiOpen) _ui.CloseTransientUi();
            else if (_card is { Open: true }) _card.Hide();
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

        BuildChrome(canvasRoot);

        _confirm = new MapEditorConfirm(_ui, canvasRoot, ConfirmBottom);
    }

    /// <summary>
    /// The same three plates every other editor wears, from the same classes. This screen used to
    /// draw its own dock, options panel, status strip and shortcut column, all four a near-copy of
    /// the room editor's - which is how they drifted apart in padding, width and behaviour.
    /// </summary>
    private void BuildChrome(RectTransform canvasRoot)
    {
        _topBar = new Chrome.EditorTopBar(_ui, canvasRoot, null);
        _topBar.SetTitle(Preset != null ? Preset.ShownName : "Menu");
        _topBar.SetBadge("menu preset");
        _topBar.RebuildActions(
        [
            new Chrome.EditorBarAction("Save", MapEditorIcons.GetToolIconOrNull("Save"), Save,
                "Save this preset and make it the menu's")
        ],
        [
            ("Esc", "Close", (Action)RequestClose)
        ]);

        _bottomBar = new Chrome.EditorBottomBar(_ui, canvasRoot, null)
        {
            OnHelp = () => _card?.Toggle()
        };

        _sidebar = new Chrome.EditorSidebar(_ui, canvasRoot, null,
            Chrome.EditorTopBar.Height + 12f, Chrome.EditorBottomBar.Height + 12f,
            optionsTitle: "Tool", withLayers: false);

        _card = new Chrome.ShortcutCard(_ui, canvasRoot, null, Globals);

        _ui.IconPreviewRightOffset = Chrome.EditorSidebar.Width + 28f;
        _ui.IconPreviewTopOffset = Chrome.EditorTopBar.Height + 12f;
    }

    /// True under every tool here, so they sit on the card rather than in the tool's chip row.
    private static readonly (string Key, string Action)[] Globals =
    [
        ("Ctrl+S", "Save the preset"),
        ("F1", "This list"),
        ("Esc", "Close the editor")
    ];

    private void BuildTools()
    {
        foreach (var tool in _tools)
        {
            var localTool = tool;
            var button = _bottomBar.AddTool(MapEditorIcons.GetToolIconOrNull(tool.Name),
                ShortLabel(tool.Name), () => SelectTool(localTool), out var ring, DockIconSize,
                DockHint(tool.Name));
            button.name = "Dock_" + tool.Name;

            var content = _ui.CreateScrollColumn(_sidebar.OptionsContent, "Options_" + tool.Name,
                out var columnRoot);
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

        _bottomBar.LayoutAfterDock();
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

        if (_sidebar != null)
        {
            _sidebar.SetOptionsTitle(tool.Name);
            _sidebar.NoteToolChanged();
        }

        RefreshShortcuts();
        tool.OnEnter();
    }

    private void RefreshShortcuts()
    {
        var hints = _activeTool is IMapEditorShortcuts source ? source.Shortcuts : null;

        _bottomBar?.SetHints(hints);
        _card?.SetTool(_activeTool?.Name ?? "", hints);
    }

    private void LateUpdate()
    {
        if (!_open) return;

        _bottomBar?.Tick();
        _topBar?.Tick();
        _sidebar?.Layout(WantedOptionsHeight(), 0f, true);
    }

    private float WantedOptionsHeight()
    {
        foreach (var panel in _panels)
            if (panel.tool == _activeTool && panel.content != null)
                return panel.content.rect.height + 12f;

        return 0f;
    }

    private float _nextUnsavedCheck;

    /// The dot beside the preset's name. Asking means serialising the preset, so it is asked twice a
    /// second rather than every frame.
    private void PollUnsaved()
    {
        if (Time.unscaledTime < _nextUnsavedCheck) return;
        _nextUnsavedCheck = Time.unscaledTime + 0.5f;

        _topBar?.SetUnsaved(HasUnsavedEdits);
        _topBar?.SetTitle(Preset != null ? Preset.ShownName : "Menu");
    }

    private static string ShortLabel(string toolName) =>
        toolName != null && toolName.StartsWith("Menu ") ? toolName.Substring(5) : toolName;

    private static string DockHint(string toolName) => toolName switch
    {
        "Menu Look" => "Menu Look - Palette, background color, grain and the glitch effect",
        "Menu Centrepiece" => "Menu Centrepiece - Which spine stands on the title screen, and where",
        "Menu Title" => "Menu Title - Replace or move the game's logo",
        "Menu Presets" => "Menu Presets - Save, load and switch between saved menus",
        _ => toolName
    };

    Coroutine IMapEditorHost.StartCoroutine(IEnumerator routine) => StartCoroutine(routine);
}
