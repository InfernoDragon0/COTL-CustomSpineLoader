using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

// The world editor host: tool panels overlaid on the play canvas. A separate host from
// RuntimeMapEditor, which is scoped to a dungeon room and its camera rig.
public class WorldMapEditor : MonoBehaviour, IMapEditorHost
{
    public static WorldMapEditor Instance { get; private set; }

    public bool IsEditing { get; private set; }

    // IMapEditorHost: the name dialog blocks the editor's own input while it is up, and the map
    // steps out of the way for it - the dialog is a vanilla menu on a canvas below this one.
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

    // Selection by id, not reference - rebuilds replace the objects underneath it.
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

        // The hovered thing keeps the bar until the cursor leaves it.
        if (_hoverMessage == null) Paint(message, severity);

        // No bar in play view; the screen's own transient line covers it.
        if (!IsEditing) Screen?.SetStatus(message);
    }

    // Fed by every hovered widget through MapEditorHover, and by the map itself in Update.
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

    // Ctrl+S: save under the name the map already has, no dialog. The first press that would land
    // on a file this session did not write only warns - the bar goes orange and the press is armed
    // - so a quicksave can never quietly write over a map somebody else made.
    private bool _quickSaveArmed;
    private float _quickSaveArmedAt;
    private string _quickSavedName;

    private const float QuickSaveArmWindow = 5f;

    // Called by the File tool when its dialog writes, so the next Ctrl+S under that name is not
    // treated as clobbering.
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

        // Nodes first, and so selected first: opening the editor lands on the tool the map is
        // actually made of. Layers dress it and the file tool is housekeeping, so both come after.
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
            // No message: the screen's own badge says the same thing and does not fade.
            Screen.EditMode = false;
            Screen.RefreshStates();
        }
    }

    // The screen closed underneath us (Esc, scene change, travel).
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
            // Once per interval, not per frame - a broken tool at 60Hz would bury the log.
            if (Time.unscaledTime >= _nextErrorAt)
            {
                _nextErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"WorldEditor: tool '{_activeTool?.Name}' failed: {e}");
            }
        }
    }

    // Widgets report their own hover; the map is polled, since its nodes are picked geometrically
    // rather than through the event system.
    private string _mapHover;

    private void ReportHoveredMapContent()
    {
        // Over a panel the widgets report themselves; stepping in here would wipe what they said.
        if (PointerOverEditorUi())
        {
            _mapHover = null;
            return;
        }

        var node = HitNode(PointerContent());
        var hover = node != null ? DescribeNode(node) : null;

        if (hover != null)
        {
            // Re-shown when the bar has fallen back to the status message, so leaving a widget with
            // the cursor still on a node reads the node again.
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

    // The room editor's dock metrics, so the two editors' bottom bars line up.
    private const float ToolIconSize = 72f;
    private const int DockPadding = 8;
    private const float DockHeight = ToolIconSize + DockPadding * 2;

    private void BuildUi()
    {
        var canvasRoot = Screen.CanvasRoot;

        // Itself, not null: the shared widgets route their hover lines and their blocker rects
        // through the attached host, and passing null used to drop both on the floor here.
        _ui.Attach(this, canvasRoot);
        _blockers.Clear();

        // Dock: one flat row of icons across the bottom, the room editor's shape. F6 leaves edit
        // mode, so there is no Play button taking up a slot.
        _dockGO = new GameObject("WorldEditor_Dock");
        _dockGO.transform.SetParent(canvasRoot, false);
        var dockRect = _dockGO.AddComponent<RectTransform>();
        dockRect.anchorMin = dockRect.anchorMax = new Vector2(0.5f, 0f);
        dockRect.pivot = new Vector2(0.5f, 0f);
        dockRect.sizeDelta = new Vector2(0f, DockHeight);
        dockRect.anchoredPosition = new Vector2(0f, 12f);

        // The game's own plate, as the room editor's dock wears; see VanillaChrome.
        VanillaChrome.Dress(_dockGO.AddComponent<Image>());

        var dockLayout = _dockGO.AddComponent<HorizontalLayoutGroup>();
        dockLayout.childAlignment = TextAnchor.MiddleLeft;
        dockLayout.spacing = 6f;
        dockLayout.padding = new RectOffset(DockPadding, DockPadding, DockPadding, DockPadding);
        dockLayout.childControlWidth = false;
        dockLayout.childControlHeight = false;
        dockLayout.childForceExpandWidth = false;
        dockLayout.childForceExpandHeight = false;

        // Horizontal only: a vertical fit collapses the plate for a frame before icons report sizes.
        var dockFitter = _dockGO.AddComponent<ContentSizeFitter>();
        dockFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        _blockers.Add(dockRect);

        // Right options panel with one scroll column per tool. Top-anchored with an explicit
        // height rather than stretched, so collapsing can shrink it to its own header.
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

    // The options panel's own title bar, with the room editor's collapse control: the panel covers
    // a quarter of the map, and placing something under it should not mean leaving the tool.
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

    // The room editor's shortcut list, in the same corner and the same key-cap style: the two
    // editors are different hosts, but a key hint should not move between them.
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

        // Invisible container, but still a click blocker.
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

            // Always last: these work in every tool. Esc does what F6 does and then closes, which
            // is not worth a row of its own.
            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+Z", "Undo last change");
            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+S", "Quicksave");
            _ui.CreateKeyHint(_shortcutPanel, "F6", "Play view");
        }

        // The bar sits at the bottom: the panel grows upward from the corner it is pivoted to.
        _ui.CreateButton(_shortcutPanel, _shortcutsCollapsed ? "Shortcuts   +" : "Shortcuts   -",
            ToggleShortcutsCollapsed, 30f);
    }

    // The editor's only running commentary: what the cursor is over, and what just happened.
    private void BuildStatusBar(RectTransform canvasRoot)
    {
        _statusGO = new GameObject("WorldEditor_Status");
        _statusGO.transform.SetParent(canvasRoot, false);

        var rect = _statusGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);

        // Above the dock, clear of the shortcut list on the left and the options panel on the right.
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

    // Drop "<Tool.Name>.png" into Assets/EditorIcons to give a tool its own icon; until then the
    // dock borrows an icon that already means the right thing, or shows the tool's initials.
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

    // Gives a widget a line for the status bar; widgets carry none by default.
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

            // The GameObject, not the Image: CreateIconButton hands back a border that starts
            // switched off, so enabling the component alone left it invisible.
            if (ring != null) ring.gameObject.SetActive(active);
        }

        if (_optionsTitle != null) _optionsTitle.text = tool.Name;

        RefreshShortcuts();
        tool.OnEnter();
    }

    // ---- helpers for the tools --------------------------------------------------------------

    public void RegisterUiBlocker(RectTransform rect)
    {
        if (rect != null) _blockers.Add(rect);
    }

    // A widget click and the tools' own polling both see the same frame, and a dropdown list can
    // stand outside every registered rect - so pressing a widget also shuts the world out briefly.
    public void BlockWorldClicks() => _worldClickBlockedUntil = Time.unscaledTime + 0.2f;

    private int _optionsRebuildFrames;

    // The options panel here is a fixed height, so unlike the room editor's this only has to settle
    // the content inside the active column. Three frames: Destroy defers to the end of the frame,
    // and a staggered grid fill may still be adding cells.
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
        foreach (var blocker in _blockers)
        {
            if (blocker == null || !blocker.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(blocker, mouse, null)) return true;
        }
        return false;
    }

    public Vector2 PointerContent() => Screen != null ? Screen.PointerContentPosition() : Vector2.zero;

    // Picking is geometric - the editor polls input past the buttons.
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

    // Rebuilt on selection change; widgets hold their initial values otherwise.
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

    // A unique id in a namespace: node1, node2... skipping whatever exists.
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
