using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

public class WorldMapEditor : MonoBehaviour, IMapEditorHost
{
    public static WorldMapEditor Instance { get; private set; }

    public bool IsEditing { get; private set; }

    public bool ModalOpen
    {
        get => _modalOpen;
        set
        {
            if (_modalOpen == value) return;
            _modalOpen = value;
            Screen?.SetModalMode(value);
        }
    }

    private bool _modalOpen;

    public MapEditorHistory History { get; } = new();

    public WorldMapScreen Screen { get; private set; }

    public CTWorldMap Map => Screen != null ? Screen.Map : null;

    public string SelectedLayerId;
    public string SelectedNodeId;

    private readonly MapEditorUI _ui = new();
    private readonly List<IMapEditorTool> _tools = [];
    private readonly List<(IMapEditorTool tool, GameObject columnRoot, RectTransform content, Image ring)> _panels = [];
    private readonly List<RectTransform> _blockers = [];
    private IMapEditorTool _activeTool;

    private GameObject _dockGO;
    private GameObject _optionsGO;
    private GameObject _statusGO;
    private RectTransform _shortcutPanel;
    private bool _shortcutsCollapsed;
    private TMPro.TMP_Text _statusText;
    private string _statusMessage = "";
    private StatusSeverity _statusSeverity = StatusSeverity.Info;
    private string _hoverMessage;
    private float _nextErrorAt;
    private float _worldClickBlockedUntil;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (IsEditing) MapNamePrompt.ResetModalState();
    }

    public void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        if (severity is StatusSeverity.Warning or StatusSeverity.Error)
            Plugin.Log.LogWarning("WorldEditor: " + message);

        _statusMessage = message;
        _statusSeverity = severity;

        if (_hoverMessage == null) Paint(message, severity);

        if (!IsEditing) Screen?.SetStatus(message);
    }

    public void ShowHoverStatus(string message)
    {
        if (!IsEditing || string.IsNullOrEmpty(message)) return;
        _hoverMessage = message;
        Paint(message, StatusSeverity.Info);
    }

    public void ClearHoverStatus()
    {
        if (!IsEditing) return;
        _hoverMessage = null;
        Paint(_statusMessage, _statusSeverity);
    }

    private void Paint(string message, StatusSeverity severity)
    {
        if (_statusText == null) return;

        _statusText.text = message ?? "";
        _statusText.color = severity switch
        {
            StatusSeverity.Success => new Color(0.55f, 0.9f, 0.55f),
            StatusSeverity.Warning => new Color(1f, 0.76f, 0.3f),
            StatusSeverity.Error => new Color(1f, 0.42f, 0.42f),
            _ => Color.white
        };
    }

    private bool _quickSaveArmed;
    private float _quickSaveArmedAt;
    private string _quickSavedName;

    private const float QuickSaveArmWindow = 5f;

    internal void NoteSaved(string mapName)
    {
        _quickSavedName = mapName;
        _quickSaveArmed = false;
    }

    public void QuickSave()
    {
        var map = Map;
        if (map == null || string.IsNullOrWhiteSpace(map.MapName)) return;

        if (_quickSaveArmed && Time.unscaledTime - _quickSaveArmedAt > QuickSaveArmWindow)
            _quickSaveArmed = false;

        var known = string.Equals(_quickSavedName, map.MapName, StringComparison.OrdinalIgnoreCase);
        if (!known && !_quickSaveArmed && CTWorldMapSerialization.Exists(map.MapName))
        {
            _quickSaveArmed = true;
            _quickSaveArmedAt = Time.unscaledTime;
            SetStatus($"'{map.MapName}' already exists - Ctrl+S again to overwrite.",
                StatusSeverity.Warning);
            return;
        }

        if (CTWorldMapSerialization.Save(map) == null)
        {
            SetStatus("Save failed, see log.", StatusSeverity.Error);
            return;
        }

        NoteSaved(map.MapName);
        Screen?.MarkSaved();
        SetStatus($"Saved '{map.MapName}'.", StatusSeverity.Success);
    }

    public void ToggleEditMode()
    {
        if (IsEditing) ExitEditMode();
        else if (WorldMapScreen.IsOpen) EnterEditMode(WorldMapScreen.Instance);
    }

    public void EnterEditMode(WorldMapScreen screen)
    {
        if (IsEditing || screen == null || !WorldMapScreen.IsOpen) return;

        Screen = screen;
        Screen.EditMode = true;
        IsEditing = true;
        ModalOpen = false;

        _tools.Clear();
        _tools.Add(new Tools.WorldNodeTool(this));
        _tools.Add(new Tools.WorldLayerTool(this));
        _tools.Add(new Tools.WorldFileTool(this));

        BuildUi();
        SelectTool(_tools[0]);

        Screen.RefreshStates();
        SetStatus($"Editing '{Map.ShownName}'. F6 or Esc returns to play view.");
    }

    public void ExitEditMode()
    {
        if (!IsEditing) return;

        _activeTool?.OnExit();
        _activeTool = null;
        IsEditing = false;
        ModalOpen = false;
        History.Clear();
        _ui.CloseTransientUi();

        if (_dockGO != null) Destroy(_dockGO);
        if (_optionsGO != null) Destroy(_optionsGO);
        if (_statusGO != null) Destroy(_statusGO);
        if (_shortcutPanel != null) Destroy(_shortcutPanel.gameObject);
        _dockGO = null;
        _optionsGO = null;
        _optionsRect = null;
        _optionsContent = null;
        _optionsTitle = null;
        _optionsCollapseButton = null;
        _optionsCollapsed = false;
        _statusGO = null;
        _shortcutPanel = null;
        _statusText = null;
        _hoverMessage = null;
        _panels.Clear();
        _blockers.Clear();
        _tools.Clear();

        if (Screen != null && WorldMapScreen.IsOpen)
        {
            Screen.EditMode = false;
            Screen.RefreshStates();
        }
    }

    internal void OnScreenClosed(WorldMapScreen screen)
    {
        if (!IsEditing || screen != Screen) return;
        ExitEditMode();
        Screen = null;
    }

    private void Update()
    {
        if (!IsEditing || Screen == null) return;

        SettleOptions();

        if (ModalOpen) return;

        var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (ctrl && Input.GetKeyDown(KeyCode.Z))
        {
            if (History.Undo(out var description)) SetStatus("Undid: " + description);
            else SetStatus("Nothing to undo.");
            return;
        }

        if (ctrl && Input.GetKeyDown(KeyCode.S))
        {
            QuickSave();
            return;
        }

        ReportHoveredMapContent();

        try
        {
            _activeTool?.OnUpdate();
        }
        catch (Exception e)
        {
            if (Time.unscaledTime >= _nextErrorAt)
            {
                _nextErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"WorldEditor: tool '{_activeTool?.Name}' failed: {e}");
            }
        }
    }

    private string _mapHover;

    private void ReportHoveredMapContent()
    {
        if (PointerOverEditorUi())
        {
            _mapHover = null;
            return;
        }

        var node = HitNode(PointerContent());
        var hover = node != null ? DescribeNode(node) : null;

        if (hover != null)
        {
            if (hover != _mapHover || _hoverMessage == null) ShowHoverStatus(hover);
        }
        else if (_mapHover != null) ClearHoverStatus();

        _mapHover = hover;
    }

    private static string DescribeNode(CTWorldMapNode node)
    {
        var name = string.IsNullOrWhiteSpace(node.DisplayName) ? node.Id : $"{node.DisplayName} ({node.Id})";
        var text = $"{name}  -  {node.NodeType}, starts {node.InitialState}";

        if (node.IsKey) text += $", grants {node.KeysGranted} key(s)";
        if (node.IsLock) text += $", costs {node.KeysCost} key(s)";

        if (!string.IsNullOrWhiteSpace(node.Target) &&
            !string.Equals(node.TargetKind, "None", StringComparison.OrdinalIgnoreCase))
            text += $"  ->  {node.TargetKind}: {node.Target}";
        else if (node.IsBase) text += ", closes the map";
        else text += ", no destination";

        if (node.Children.Count > 0) text += $"  |  links to {string.Join(", ", node.Children)}";
        return text;
    }

    // ---- ui ---------------------------------------------------------------------------------

    private const float ToolIconSize = 72f;
    private const int DockPadding = 8;
    private const float DockHeight = ToolIconSize + DockPadding * 2;

    private void BuildUi()
    {
        var canvasRoot = Screen.CanvasRoot;

        _ui.Attach(this, canvasRoot);
        _blockers.Clear();

        _dockGO = new GameObject("WorldEditor_Dock");
        _dockGO.transform.SetParent(canvasRoot, false);
        var dockRect = _dockGO.AddComponent<RectTransform>();
        dockRect.anchorMin = dockRect.anchorMax = new Vector2(0.5f, 0f);
        dockRect.pivot = new Vector2(0.5f, 0f);
        dockRect.sizeDelta = new Vector2(0f, DockHeight);
        dockRect.anchoredPosition = new Vector2(0f, 12f);

        VanillaChrome.Dress(_dockGO.AddComponent<Image>());

        var dockLayout = _dockGO.AddComponent<HorizontalLayoutGroup>();
        dockLayout.childAlignment = TextAnchor.MiddleLeft;
        dockLayout.spacing = 6f;
        dockLayout.padding = new RectOffset(DockPadding, DockPadding, DockPadding, DockPadding);
        dockLayout.childControlWidth = false;
        dockLayout.childControlHeight = false;
        dockLayout.childForceExpandWidth = false;
        dockLayout.childForceExpandHeight = false;

        var dockFitter = _dockGO.AddComponent<ContentSizeFitter>();
        dockFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        _blockers.Add(dockRect);

        _optionsGO = new GameObject("WorldEditor_Options");
        _optionsGO.transform.SetParent(canvasRoot, false);
        _optionsRect = _optionsGO.AddComponent<RectTransform>();
        _optionsRect.anchorMin = _optionsRect.anchorMax = new Vector2(1f, 1f);
        _optionsRect.pivot = new Vector2(1f, 1f);
        _optionsRect.sizeDelta = new Vector2(OptionsWidth, OptionsHeight);
        _optionsRect.anchoredPosition = new Vector2(-14f, -70f);

        VanillaChrome.Dress(_optionsGO.AddComponent<Image>());

        _blockers.Add(_optionsRect);

        BuildOptionsHeader();

        foreach (var tool in _tools)
        {
            var localTool = tool;
            var button = _ui.CreateIconButton(_dockGO.transform, DockIcon(tool.Name), ShortLabel(tool.Name),
                () => SelectTool(localTool), out var ring, ToolIconSize, DockHint(tool.Name));
            button.name = "Dock_" + tool.Name;

            var content = _ui.CreateScrollColumn(_optionsContent, "Options_" + tool.Name, out var columnRoot);
            columnRoot.SetActive(false);
            _panels.Add((tool, columnRoot, content, ring));

            BuildToolPanel(tool, content);
        }

        BuildShortcutPanel(canvasRoot);
        BuildStatusBar(canvasRoot);
    }

    private const float OptionsWidth = 360f;
    private const float OptionsHeight = 940f;
    private const float OptionsHeaderHeight = 34f;

    private RectTransform _optionsRect;
    private RectTransform _optionsContent;
    private TMPro.TMP_Text _optionsTitle;
    private GameObject _optionsCollapseButton;
    private bool _optionsCollapsed;

    private void BuildOptionsHeader()
    {
        var header = new GameObject("Header");
        header.transform.SetParent(_optionsRect, false);

        var headerRect = header.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0f, OptionsHeaderHeight);
        headerRect.anchoredPosition = Vector2.zero;
        header.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        var title = _ui.CreateLabel(header.transform, "", 20, TMPro.TextAlignmentOptions.Left);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(12f, 0f);
        titleRect.offsetMax = new Vector2(-36f, 0f);
        _optionsTitle = title.GetComponent<TMPro.TMP_Text>();
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
        BlockWorldClicks();
        _optionsCollapsed = !_optionsCollapsed;

        if (_optionsContent != null) _optionsContent.gameObject.SetActive(!_optionsCollapsed);
        if (_optionsRect != null)
            _optionsRect.sizeDelta = new Vector2(OptionsWidth,
                _optionsCollapsed ? OptionsHeaderHeight : OptionsHeight);

        var label = _optionsCollapseButton != null
            ? _optionsCollapseButton.GetComponentInChildren<TMPro.TMP_Text>()
            : null;
        if (label != null) label.text = _optionsCollapsed ? "+" : "-";
    }

    private void BuildShortcutPanel(RectTransform canvasRoot)
    {
        var go = new GameObject("WorldEditor_Shortcuts");
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

        _blockers.Add(_shortcutPanel);
        RefreshShortcuts();
    }

    private void ToggleShortcutsCollapsed()
    {
        _shortcutsCollapsed = !_shortcutsCollapsed;
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

            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+Z", "Undo last change");
            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+S", "Quicksave");
            _ui.CreateKeyHint(_shortcutPanel, "F6", "Play view");
        }

        _ui.CreateButton(_shortcutPanel, _shortcutsCollapsed ? "Shortcuts   +" : "Shortcuts   -",
            ToggleShortcutsCollapsed, 30f);
    }

    private void BuildStatusBar(RectTransform canvasRoot)
    {
        _statusGO = new GameObject("WorldEditor_Status");
        _statusGO.transform.SetParent(canvasRoot, false);

        var rect = _statusGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);

        rect.offsetMin = new Vector2(284f, 12f + DockHeight + 8f);
        rect.offsetMax = new Vector2(-388f, 12f + DockHeight + 8f);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, 40f);

        VanillaChrome.Dress(_statusGO.AddComponent<Image>());

        var label = _ui.CreateLabel(_statusGO.transform, "", 18, TMPro.TextAlignmentOptions.Left);
        _statusText = label.GetComponent<TMPro.TMP_Text>();
        _statusText.raycastTarget = false;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(14f, 0f);
        labelRect.offsetMax = new Vector2(-14f, 0f);

        _blockers.Add(rect);
        Paint(_statusMessage, _statusSeverity);
    }

    private static Sprite DockIcon(string toolName) => toolName switch
    {
        "World File" => MapEditorIcons.GetToolIconOrNull(toolName) ?? MapEditorIcons.GetToolIconOrNull("Load Map"),
        "World Nodes" => MapEditorIcons.GetToolIconOrNull(toolName) ?? CustomMapSkin.NodeIconSprite(),
        _ => MapEditorIcons.GetToolIconOrNull(toolName)
    };

    private static string ShortLabel(string toolName) =>
        toolName != null && toolName.StartsWith("World ") ? toolName.Substring(6) : toolName;

    private static string DockHint(string toolName) => toolName switch
    {
        "World File" => "World File - Save, load, background, parallax, wipe progress",
        "World Layers" => "World Layers - Add or remove Layers of Sprites and Spines",
        "World Nodes" => "World Nodes - Add or remove Nodes",
        _ => toolName
    };

    public static void Hint(MapEditorDropdown dropdown, string text)
    {
        if (dropdown?.Root == null) return;

        var hover = dropdown.Root.GetComponent<MapEditorHover>() ??
                    dropdown.Root.GetComponentInChildren<MapEditorHover>(true);
        if (hover != null) hover.HoverText = text;
    }

    private void BuildToolPanel(IMapEditorTool tool, RectTransform content)
    {
        try
        {
            tool.BuildPanel(content, _ui);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"WorldEditor: panel for '{tool.Name}' failed to build: {e}");
        }
    }

    public void SelectTool(IMapEditorTool tool)
    {
        if (tool == null || tool == _activeTool) return;

        _activeTool?.OnExit();
        _ui.CloseTransientUi();
        _activeTool = tool;

        foreach (var (candidate, columnRoot, _, ring) in _panels)
        {
            var active = candidate == tool;
            columnRoot.SetActive(active);

            if (ring != null) ring.gameObject.SetActive(active);
        }

        if (_optionsTitle != null) _optionsTitle.text = tool.Name;

        RefreshShortcuts();
        tool.OnEnter();
    }

    // ---- helpers for the tools --------------------------------------------------------------

    public void RegisterUiBlocker(RectTransform rect)
    {
        if (rect != null && !_blockers.Contains(rect)) _blockers.Add(rect);
    }

    public void BlockWorldClicks() => _worldClickBlockedUntil = Time.unscaledTime + 0.2f;

    private int _optionsRebuildFrames;

    public void RequestOptionsResize() => _optionsRebuildFrames = 3;

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

    public bool PointerOverEditorUi()
    {
        if (ModalOpen) return true;
        if (Time.unscaledTime < _worldClickBlockedUntil) return true;

        var mouse = (Vector2)Input.mousePosition;

        for (var i = _blockers.Count - 1; i >= 0; i--)
        {
            var blocker = _blockers[i];
            if (blocker == null)
            {
                _blockers.RemoveAt(i);
                continue;
            }
            if (!blocker.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(blocker, mouse, null)) return true;
        }
        return false;
    }

    public Vector2 PointerContent() => Screen != null ? Screen.PointerContentPosition() : Vector2.zero;

    public CTWorldMapNode HitNode(Vector2 contentPoint)
    {
        if (Map == null) return null;

        CTWorldMapNode best = null;
        var bestDistance = float.MaxValue;

        foreach (var node in Map.Nodes)
        {
            if (node == null) continue;
            var radius = 40f * Mathf.Max(0.2f, node.Scale);
            var distance = Vector2.Distance(contentPoint, new Vector2(node.Position.X, node.Position.Y));
            if (distance <= radius && distance < bestDistance)
            {
                best = node;
                bestDistance = distance;
            }
        }
        return best;
    }

    public void RebuildAll()
    {
        Screen?.RebuildVisuals();
    }

    public void RebuildActivePanel()
    {
        foreach (var (tool, _, content, _) in _panels)
        {
            if (tool != _activeTool) continue;
            _ui.CloseTransientUi();
            foreach (Transform child in content) Destroy(child.gameObject);
            BuildToolPanel(tool, content);
            return;
        }
    }

    public void PushUndo(string description, Func<bool> undo) => History.Push(description, undo);

    public string MintId(string prefix, Func<string, bool> exists)
    {
        for (var i = 1; i < 10000; i++)
        {
            var candidate = prefix + i;
            if (!exists(candidate)) return candidate;
        }
        return prefix + Guid.NewGuid().ToString("N").Substring(0, 6);
    }
}
