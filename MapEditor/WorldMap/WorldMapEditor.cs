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

    private Chrome.EditorBottomBar _bottomBar;
    private Chrome.EditorSidebar _sidebar;
    private Chrome.ShortcutCard _card;

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

        if (_hoverMessage == null) Paint(message, severity, pulse: true);

        if (!IsEditing) Screen?.SetStatus(message);
    }

    public void ShowHoverStatus(string message)
    {
        if (!IsEditing || string.IsNullOrEmpty(message)) return;
        _hoverMessage = message;
        Paint(message, StatusSeverity.Info, pulse: false);
    }

    public void ClearHoverStatus()
    {
        if (!IsEditing) return;
        _hoverMessage = null;
        Paint(_statusMessage, _statusSeverity, pulse: false);
    }

    /// A hover message only repaints the words; a real one may also ring the region. Passing a hover
    /// through as a status would clear the ring on whatever the editor last needed answering.
    private void Paint(string message, StatusSeverity severity, bool pulse) =>
        _bottomBar?.SetStatus(message, severity, pulse);

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

        _bottomBar?.Destroy();
        _sidebar?.Destroy();
        _card?.Destroy();
        _bottomBar = null;
        _sidebar = null;
        _card = null;

        _ui.HideIconPreview();
        _ui.IconPreviewRightOffset = MapEditorUI.DefaultIconPreviewRightOffset;
        _ui.IconPreviewTopOffset = MapEditorUI.DefaultIconPreviewTopOffset;

        _hoverMessage = null;
        _panels.Clear();
        _blockers.Clear();
        _tools.Clear();

        // The bar belongs to the screen, so it is put back whether or not the screen is still open -
        // otherwise a map closed while editing would come back up wearing the editor's keys.
        Screen?.SetPlayActions();

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
        PollUnsaved();

        if (ModalOpen) return;

        if (Input.GetKeyDown(KeyCode.F1))
        {
            _card?.Toggle();
            return;
        }

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

    private const float DockIconSize = 60f;

    /// <summary>
    /// The world map wears the same chrome as every other editor, built from the same classes: the
    /// screen's own top bar keeps the map's name, and edit mode adds the tool bar along the bottom
    /// and the option column down the right. It used to draw a centred dock, a status strip wedged
    /// above it and a shortcut column in the corner, all three over the map being edited.
    /// </summary>
    private void BuildUi()
    {
        var canvasRoot = Screen.CanvasRoot;

        _ui.Attach(this, canvasRoot);
        _blockers.Clear();

        if (Screen.TopBar != null) _blockers.Add(Screen.TopBar.Root);
        if (Screen.ConfirmRect != null) _blockers.Add(Screen.ConfirmRect);

        _bottomBar = new Chrome.EditorBottomBar(_ui, canvasRoot, _blockers.Add)
        {
            OnHelp = () => _card?.Toggle()
        };

        _sidebar = new Chrome.EditorSidebar(_ui, canvasRoot, _blockers.Add,
            Chrome.EditorTopBar.Height + 12f, Chrome.EditorBottomBar.Height + 12f,
            optionsTitle: "Tool", withLayers: false);

        foreach (var tool in _tools)
        {
            var localTool = tool;
            var button = _bottomBar.AddTool(DockIcon(tool.Name), ShortLabel(tool.Name),
                () => SelectTool(localTool), out var ring, DockIconSize, DockHint(tool.Name));
            button.name = "Dock_" + tool.Name;

            var content = _ui.CreateScrollColumn(_sidebar.OptionsContent, "Options_" + tool.Name,
                out var columnRoot);
            columnRoot.SetActive(false);
            _panels.Add((tool, columnRoot, content, ring));

            BuildToolPanel(tool, content);
        }

        _bottomBar.LayoutAfterDock();

        _card = new Chrome.ShortcutCard(_ui, canvasRoot, _blockers.Add, Globals);

        _ui.IconPreviewRightOffset = Chrome.EditorSidebar.Width + 28f;
        _ui.IconPreviewTopOffset = Chrome.EditorTopBar.Height + 12f;

        SetEditActions();
        Paint(_statusMessage, _statusSeverity, pulse: true);
    }

    /// True under every tool here, so they sit on the card rather than in the tool's chip row.
    private static readonly (string Key, string Action)[] Globals =
    [
        ("Ctrl+Z", "Undo last change"),
        ("Ctrl+S", "Quicksave the map"),
        ("F1", "This list"),
        ("F6", "Play view"),
        ("Esc", "Leave the editor")
    ];

    /// Escape is read by the screen, not here, so it asks whether the card is what should close.
    internal bool CardOpen => _card is { Open: true };

    internal void HideCard() => _card?.Hide();

    private void SetEditActions()
    {
        var bar = Screen?.TopBar;
        if (bar == null) return;

        bar.SetBadge("editing");
        bar.RebuildActions(null,
        [
            ("Ctrl+Z", "Undo", (Action)(() =>
            {
                if (History.Undo(out var description)) SetStatus("Undid: " + description);
                else SetStatus("Nothing to undo.");
            })),
            ("Ctrl+S", "Save", QuickSave),
            ("F6", "Play view", ExitEditMode)
        ]);
    }

    private float _nextUnsavedCheck;

    /// The dot beside the map's name. Asking costs a serialisation of the whole map, so it is asked
    /// about once a second rather than every frame.
    private void PollUnsaved()
    {
        if (Time.unscaledTime < _nextUnsavedCheck) return;
        _nextUnsavedCheck = Time.unscaledTime + 1f;

        Screen?.TopBar?.SetUnsaved(Screen.HasUnsavedEdits);
    }

    private void LateUpdate()
    {
        if (!IsEditing) return;

        _bottomBar?.Tick();
        _sidebar?.Layout(WantedOptionsHeight(), 0f, true);
    }

    private float WantedOptionsHeight()
    {
        foreach (var (tool, _, content, _) in _panels)
            if (tool == _activeTool && content != null)
                return content.rect.height + 12f;

        return 0f;
    }

    private void RefreshShortcuts()
    {
        var hints = _activeTool is IMapEditorShortcuts source ? source.Shortcuts : null;

        _bottomBar?.SetHints(hints);
        _card?.SetTool(_activeTool?.Name ?? "", hints);
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

        if (_sidebar != null)
        {
            _sidebar.SetOptionsTitle(tool.Name);
            _sidebar.NoteToolChanged();
        }

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
