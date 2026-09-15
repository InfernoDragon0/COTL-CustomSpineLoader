using System.Collections;
using System.Collections.Generic;
using System.Linq;
using COTL_API.Utility;
using CustomSpineLoader.APIHelper;
using CustomSpineLoader.MapEditor.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

public class RuntimeMapEditor : MonoBehaviour, IMapEditorHost
{
    private Canvas _canvas;
    private GameObject _canvasGO;
    private RectTransform _optionsContent;

    private readonly Dictionary<string, RectTransform> _optionColumns = [];

    private Chrome.EditorTopBar _topBar;
    private Chrome.EditorBottomBar _bottomBar;
    private Chrome.EditorSidebar _sidebar;
    private Chrome.ShortcutCard _shortcutCard;

    private MapEditorLayerPanel _layers;

    private readonly Dictionary<string, Image> _toolRings = [];

    private readonly List<IMapEditorTool> _tools = [];
    private IMapEditorTool _activeTool;

    private MapEditorUI _ui;
    private float _savedTimeScale = 1f;
    private bool _editing;

    private GameObject _cameraAnchor;

    private float _zoom = DefaultZoom;
    private const float DefaultZoom = 12f;
    private const float MinZoom = 4f;
    private const float MaxZoom = 45f;
    private const float ZoomSpeed = 18f;

    private readonly List<RectTransform> _uiBlockers = [];
    private readonly List<BaseBiomeAreaCulling> _suspendedCulling = [];

    private bool _renaming;
    private string _nameBuffer = "";
    private string _promptLabel = "map name";
    private System.Action<string> _promptDone;

    private bool _resetArmed;
    private float _resetArmedAt;
    private const float ResetArmWindow = 5f;

    public CTNodeBlueprint Map { get; private set; } = new();
    public MapEditorUI UI => _ui;

    public bool IsEditing => _editing;

    /// F6 has hidden the editor's UI and gizmos; overlays that belong to the editor hide with them.
    internal bool ChromeHidden => _chromeHidden;

    /// Screen pixels per canvas unit for the editor's own canvas. Anything outside the editor that
    /// wants to place itself against the editor's chrome needs this to convert, and reading it from
    /// the live canvas beats re-deriving it from the scaler's reference size and match value.
    internal float CanvasScale => _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;

    /// A tool whose shortcut hints depend on its state asks for the panel to be rebuilt.
    internal void RefreshShortcutHints() => RefreshShortcuts();

    public bool ModalOpen
    {
        get => _modalOpen;
        set
        {
            _modalOpen = value;
            if (_canvas != null && _editing) _canvas.enabled = !value;
        }
    }

    private bool _modalOpen;

    private const float PanSpeed = 14f;

    public static RuntimeMapEditor Active { get; private set; }

    public static RuntimeMapEditor Ensure(string hostName)
    {
        if (Active != null) return Active;
        return new GameObject(hostName).AddComponent<RuntimeMapEditor>();
    }

    private void Awake()
    {
        Active = this;
        _ui = new MapEditorUI();
        BuildTools();
        CreateUi();
        _canvas.enabled = false;

        LevelPlayback.OnEditorReady(this);
    }

    private void BuildTools()
    {
        _tools.Add(new SelectTool(this));
        _tools.Add(new ShapeTool(this));
        _tools.Add(new StructureTool(this));
        _tools.Add(new EnemyTool(this));
        _tools.Add(new NpcTool(this));
        _tools.Add(new PodiumTool(this));
        _tools.Add(new TriggerTool(this));
        _tools.Add(new DoorTool(this));
        _tools.Add(new WhiteboardTool(this));
        _tools.Add(new LightingTool(this));
        _tools.Add(new MusicTool(this));
        _tools.Add(new ClearTool(this));
        _tools.Add(new LoadTool(this));
        _tools.Add(new LevelTool(this));
        _tools.Add(new DungeonBuilderTool(this));
    }

    public T GetTool<T>() where T : class, IMapEditorTool => _tools.OfType<T>().FirstOrDefault();

    public void SelectFirstTool() => SelectTool(_tools.FirstOrDefault());

    private IMapEditorTool _toolBeforeScreen;

    /// <summary>
    /// Puts the mapper back in the tool they were using before a full-screen editor took over. The
    /// Level and Dungeon screens have no panel of their own to return to, so landing in them after
    /// closing a level would mean an empty sidebar and a tool nobody chose.
    /// </summary>
    public void SelectPreviousTool()
    {
        var back = _toolBeforeScreen;
        _toolBeforeScreen = null;
        SelectTool(back ?? _tools.FirstOrDefault());
    }

    public static EditorContext Context =>
        ContextOverride ?? (BaseSession.Active ? EditorContext.Base
            : HubSession.Active ? EditorContext.Hub
            : EditorContext.Dungeon);

    /// While a peer's base edits are written into a base nobody here is editing, the tools must
    /// still behave as in the base editor; the multiplayer applier sets this for the duration.
    internal static EditorContext? ContextOverride;

    private static bool HiddenIn(EditorContext context, IMapEditorTool tool) => context switch
    {
        EditorContext.Hub => tool is EnemyTool or PodiumTool or DoorTool or LevelTool or DungeonBuilderTool,
        EditorContext.Base => tool is EnemyTool or PodiumTool or DoorTool or LevelTool or DungeonBuilderTool
            or ClearTool or LoadTool,
        _ => false
    };

    /// <summary>
    /// The tools that place something, for this context. The three that take over the whole screen
    /// the moment they are chosen - Load, Level, Dungeon Builder - are not among them: they sit in
    /// the top bar instead, which also keeps the wheel from cycling onto a full-screen editor.
    /// </summary>
    private List<IMapEditorTool> DockTools()
    {
        var context = Context;
        return _tools
            .Where(t => !HiddenIn(context, t) &&
                        t is not (LoadTool or LevelTool or DungeonBuilderTool or ClearTool))
            .ToList();
    }

    public MapEditorHistory History { get; } = new();

    // ---- unsaved work --------------------------------------------------------------------------

    private int _edits;
    private int _savedEdits;

    public void MarkEdited()
    {
        _edits++;
        _topBar?.SetUnsaved(true);
    }

    /// Bumps on every recorded edit; the layer tree watches it to know when to re-read the room.
    public int EditCount => _edits;

    public void MarkSaved()
    {
        _savedEdits = _edits;
        _topBar?.SetUnsaved(false);
    }

    public bool HasUnsavedEdits => _edits != _savedEdits;

    private void UndoLast()
    {
        if (History.Undo(out var description, out var skipped))
        {
            SetStatus(skipped > 0
                ? $"Undid: {description} (skipped {skipped} step(s) whose objects are gone)."
                : "Undid: " + description + ".");
        }
        else
        {
            SetStatus(skipped > 0 ? $"Nothing left to undo; {skipped} stale step(s) dropped." : "Nothing to undo.");
        }
    }

    // ---- multiplayer -----------------------------------------------------------------------------

    /// Every tool writes its part of the room into the blueprint, as a save does.
    internal void ContributeAll()
    {
        foreach (var tool in _tools.OfType<IMapDataContributor>())
            tool.ContributeTo(Map);
    }

    internal IEnumerable<Net.IMapEditorLivePreview> LivePreviews() => _tools.OfType<Net.IMapEditorLivePreview>();

    internal string ActiveToolName => _activeTool != null ? _activeTool.Name : "";

    internal IMapEditorTool ActiveTool => _activeTool;

    /// Switches tools from a panel that is not the dock - the quick pick bar stays on screen under
    /// every tool, and choosing from it has to bring its own tool forward.
    internal void ActivateTool(IMapEditorTool tool) => SelectTool(tool);

    /// What this player has hold of, by presence id, for the peer's locks and colours.
    internal List<string> SelectedIds()
    {
        var ids = new List<string>();

        var select = GetTool<SelectTool>();
        if (select != null)
            foreach (var go in select.Selection)
            {
                var id = go != null ? Net.EditorIds.Of(go) : null;
                if (id != null) ids.Add(id);
            }

        var trigger = GetTool<TriggerTool>()?.SelectedTrigger;
        if (trigger != null && !string.IsNullOrEmpty(trigger.Id)) ids.Add(trigger.Id);

        var doors = GetTool<DoorTool>();
        var door = doors?.SelectedDoor;
        if (door != null) ids.Add("door-" + door.direction);

        var shapes = GetTool<ShapeTool>();
        if (_activeTool == shapes && shapes != null && shapes.HasActiveShape && shapes.ActiveShapeObject != null)
            ids.Add(Net.EditorIds.Of(shapes.ActiveShapeObject));

        return ids;
    }

    internal bool IsLocallySelected(GameObject go)
    {
        if (go == null) return false;
        var select = GetTool<SelectTool>();
        return select != null && select.IsSelected(go);
    }

    internal void RefreshLayers() => _layers?.Invalidate();

    /// <summary>
    /// In the base the editor's blueprint must be the delta's own content - that is what a base save
    /// writes, and what a peer's base changes are applied into - whether or not a base session is open.
    /// </summary>
    internal void EnsureBaseMap()
    {
        if (Net.EditorDocument.EffectiveContext != EditorContext.Base) return;
        var content = BaseDelta.Content;
        if (ReferenceEquals(Map, content)) return;
        content.MapName = $"base_slot{BaseDelta.Slot}";
        AdoptBlueprint(content);
    }

    private bool _holdBaseContext;

    /// The host writes for both: a guest's Save asks the host, which saves and ships the files.
    internal void SaveForPeer(string mapName)
    {
        // A host standing in the base without the editor open still saves the base, not a room.
        if (Net.EditorDocument.EffectiveContext == EditorContext.Base && !BaseSession.Active)
        {
            ContextOverride = EditorContext.Base;
            _holdBaseContext = true;
        }
        EnsureBaseMap();

        if (Context != EditorContext.Base && !string.IsNullOrWhiteSpace(mapName))
        {
            Map.MapName = mapName.Trim();
            UpdateNameLabel();
        }

        var blocked = SaveBlock();
        if (blocked != null)
        {
            ReleaseBaseContext();
            SetStatus(blocked, StatusSeverity.Error);
            Net.EditorNet.Notice($"Could not save for {Net.EditorNet.PeerName}: {blocked}");
            return;
        }

        if (Context == EditorContext.Base || !string.IsNullOrWhiteSpace(Map.MapName))
        {
            _quickSavedName = Map.MapName;
            SetStatus($"{Net.EditorNet.PeerName} asked to save; saving...");
            WriteMap();
            return;
        }

        ReleaseBaseContext();

        if (!_editing)
        {
            Net.EditorNet.Notice($"{Net.EditorNet.PeerName} asked to save, but this room has no name yet; " +
                                 "open the editor and save it once.");
            return;
        }

        SetStatus($"{Net.EditorNet.PeerName} asked to save; pick a name.");
        SaveMap();
    }

    private void ReleaseBaseContext()
    {
        if (!_holdBaseContext) return;
        _holdBaseContext = false;
        ContextOverride = null;
    }

    /// The host saved: this side's copy is now the saved copy too.
    internal void PeerSaved(string mapName)
    {
        if (Context != EditorContext.Base && !string.IsNullOrWhiteSpace(mapName))
        {
            Map.MapName = mapName.Trim();
            UpdateNameLabel();
        }

        _quickSavedName = Map.MapName;
        MarkSaved();
        SetStatus(string.IsNullOrWhiteSpace(mapName)
            ? $"{Net.EditorNet.PeerName} saved the room."
            : $"{Net.EditorNet.PeerName} saved '{mapName}'.", StatusSeverity.Success);

        if (_closeAfterSave)
        {
            _closeAfterSave = false;
            if (_editing) ExitEditorMode();
        }
    }

    private void RequestPeerSave(string mapName)
    {
        Net.EditorNet.RequestSave(mapName);
        SetStatus($"Asked {Net.EditorNet.PeerName} to save" +
                  (string.IsNullOrWhiteSpace(mapName) ? "." : $" '{mapName}'."));
    }

    private void CycleTool(int direction)
    {
        var tools = DockTools();
        if (tools.Count == 0) return;

        var index = _activeTool != null ? tools.IndexOf(_activeTool) : 0;
        if (index < 0) index = 0;

        index = (index + direction % tools.Count + tools.Count) % tools.Count;
        SelectTool(tools[index]);
    }

    private BlueprintLoader _loader;
    public BlueprintLoader Loader => _loader ??= new BlueprintLoader(this);

    public void AdoptBlueprint(CTNodeBlueprint bp)
    {
        if (bp == null) return;
        Map = bp;
        UpdateNameLabel();
    }

    public void BeginHubAuthoring(string hubName)
    {
        var hub = CTLevelSerialization.LoadByName(hubName);
        var existing = HubSession.BlueprintFor(hub);
        var bp = existing != null ? MapEditorSerialization.LoadByName(existing) : null;

        if (bp != null)
        {
            AdoptBlueprint(bp);
            Loader.Load(bp);
        }
        else
        {
            AdoptBlueprint(new CTNodeBlueprint { MapName = hubName });
        }

        if (!_editing) EnterEditorMode();
        SetStatus(bp != null
            ? $"Editing hub '{hubName}'. Save Map keeps it a hub."
            : $"New hub '{hubName}': the town has been cleared. Build it, mark where the player " +
              "arrives with a trigger's 'Hub spawn point' action, then Save Map.");
    }

    public void BeginBaseEditing()
    {
        var delta = BaseDelta.Content;

        delta.MapName = $"base_slot{BaseDelta.Slot}";
        AdoptBlueprint(delta);

        if (!_editing) EnterEditorMode();

        SetStatus("Base Editor Open");
    }

    public void ExitForPlayback()
    {
        if (_editing) ExitEditorMode();
    }

    private void OnDestroy()
    {
        if (_canvasGO != null) Destroy(_canvasGO);
        if (_previewBadgeGO != null) Destroy(_previewBadgeGO);
        if (_editing)
        {
            RestoreGameState();
            Net.EditorNet.LocalEditorChanged(false);
        }

        MapEditorIcons.ClearSceneScopedCache();
        EnemyThumbnails.ClearSceneScopedCache();

        MapNamePrompt.ResetModalState();
        Tools.CTMapTrigger.ResetSequenceState();
        if (!LevelPlayback.Active) Tools.LightingTool.ForgetRoomLighting();

        LockNavigator(false);

        if (Active == this) Active = null;
    }

    public void ToggleEditor()
    {
        if (_canvas == null || ModalOpen) return;

        if (!_editing)
        {
            if (BaseSession.SceneOwnsSessions && Context == EditorContext.Dungeon)
            {
                Plugin.Log.LogInfo("MapEditor: F4 does nothing here - open the base editor from the " +
                                   "F7 panel, or enter a hub.");
                return;
            }

            EnterEditorMode();
            return;
        }

        if (_confirm != null && _confirm.Open)
        {
            _confirm.Hide();
            SetStatus("Still open.");
            return;
        }

        if (ScreenTool != null && ScreenTool.ScreenStepBack()) return;

        if (!HasUnsavedEdits || _confirm == null)
        {
            ExitEditorMode();
            return;
        }

        var named = !string.IsNullOrWhiteSpace(Map.MapName);
        _confirm.Show(
            named ? $"Save changes to '{Map.MapName}' before closing?" : "Save this room before closing?",
            SaveAndClose, confirmLabel: "Save & close", altLabel: "Close anyway", onAlt: CloseAnyway);
    }

    private bool HandleEscape()
    {
        if (_ui.TransientUiOpen)
        {
            _ui.CloseTransientUi();
            return true;
        }

        if (_shortcutCard is { Open: true })
        {
            _shortcutCard.Hide();
            return true;
        }

        if (_activeTool is IMapEditorEscapeHandler handler && handler.HandleEscape()) return true;

        ToggleEditor();
        return true;
    }

    private void CloseAnyway()
    {
        ExitEditorMode();
        SetStatus("Closed without saving - the room keeps the changes, no file has them.",
            StatusSeverity.Warning);
    }

    private bool _closeAfterSave;

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(Map.MapName) && Context != EditorContext.Base)
        {
            _closeAfterSave = true;
            SaveMap();
            return;
        }

        var blocked = SaveBlock();
        if (blocked != null)
        {
            SetStatus(blocked, StatusSeverity.Error);
            return;
        }

        _closeAfterSave = true;
        _quickSavedName = Map.MapName;

        if (Net.EditorNet.ShouldRequestSave)
        {
            // The host writes; this side closes now and is marked saved when the host confirms.
            RequestPeerSave(Context == EditorContext.Base ? null : Map.MapName);
            _closeAfterSave = false;
            ExitEditorMode();
            return;
        }

        WriteMap();
    }

    private bool _chromeHidden;

    private IMapEditorScreenTool ScreenTool =>
        _activeTool is IMapEditorScreenTool { OwnsScreen: true } tool ? tool : null;

    private readonly List<GameObject> _ownChrome = [];
    private readonly List<GameObject> _hiddenChrome = [];
    private bool _ownChromeVisible = true;

    /// <summary>
    /// Puts the editor's own panels away while a full-screen tool owns the screen, and back again
    /// after. What comes back is only what was on screen before: several pieces of chrome decide
    /// their own visibility - the confirm strip, the shortcut card, the quick pick bar - and turning
    /// the whole list back on would raise a dialog nobody asked for.
    /// </summary>
    public void SetOwnChromeVisible(bool visible)
    {
        if (_ownChromeVisible == visible) return;
        _ownChromeVisible = visible;

        if (!visible)
        {
            _hiddenChrome.Clear();

            foreach (var go in _ownChrome)
                if (go != null && go.activeSelf)
                {
                    _hiddenChrome.Add(go);
                    go.SetActive(false);
                }

            return;
        }

        foreach (var go in _hiddenChrome)
            if (go != null) go.SetActive(true);

        _hiddenChrome.Clear();
    }

    public void ToggleChromeHidden()
    {
        if (_canvas == null || !_editing || ModalOpen) return;

        if (ScreenTool != null) return;

        _chromeHidden = !_chromeHidden;
        _canvas.enabled = !_chromeHidden;
        MapEditorGizmos.SetHidden(_chromeHidden);
        GetTool<WhiteboardTool>()?.RefreshVisibility();
        ShowPreviewBadge(_chromeHidden);

        if (!_chromeHidden) SetStatus("Editor UI back. F6 hides it again.");
    }

    private void ShowChrome()
    {
        SetOwnChromeVisible(true);
        _chromeHidden = false;
        MapEditorGizmos.SetHidden(false);
        GetTool<WhiteboardTool>()?.RefreshVisibility();
        ShowPreviewBadge(false);
    }

    private GameObject _previewBadgeGO;

    private void ShowPreviewBadge(bool visible)
    {
        if (visible && _previewBadgeGO == null) BuildPreviewBadge();
        if (_previewBadgeGO != null) _previewBadgeGO.SetActive(visible);
    }

    private void BuildPreviewBadge()
    {
        _previewBadgeGO = new GameObject("RuntimeMapEditor_PreviewBadge");

        var canvas = _previewBadgeGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5001;

        var scaler = _previewBadgeGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var label = _ui.CreateLabel(_previewBadgeGO.transform, "Preview mode - F6 to show UI", 18,
            TextAlignmentOptions.Left);
        var text = label.GetComponent<TMP_Text>();
        text.color = new Color(1f, 0.85f, 0.5f);
        text.raycastTarget = false;

        var rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.sizeDelta = new Vector2(600f, 30f);
        rect.anchoredPosition = new Vector2(24f, 16f);
    }

    private void EnterEditorMode()
    {
        if (!SceneRefs.HasRoom)
        {
            Plugin.Log.LogWarning("MapEditor: no GenerateRoom in this scene, editor unavailable.");
            return;
        }

        EnsureEventSystem();

        RefreshDockForContext();
        RefreshTopBarForContext();
        UpdateNameLabel();

        _editing = true;
        _canvas.enabled = true;

        MapEditorUI.Rehost(this);

        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Hide(true, 0);

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        TakeCameraControl();

        GetTool<ShapeTool>()?.PrepareForLoad();

        SelectTool(_tools.FirstOrDefault());

        // Base and hub sessions arrive with their objects already in the scene, so saved groups are
        // put back here; a blueprint load does the same at the end of its own routine.
        var groups = MapEditorGroups.Restore(Map);
        SetStatus(groups > 0 ? $"Editor open. {groups} group(s) restored." : "Editor open.");

        GetTool<WhiteboardTool>()?.RefreshVisibility();
        Net.EditorNet.LocalEditorChanged(true);
        RefreshCardExtras();
    }

    private void ExitEditorMode()
    {
        ConfirmPrompt();

        _renaming = false;
        ReleaseTypingLocks();
        _confirm?.Hide();

        _activeTool?.OnExit();
        _activeTool = null;
        _editing = false;
        ShowChrome();
        _canvas.enabled = false;
        RestoreGameState();
        GetTool<WhiteboardTool>()?.RefreshVisibility();

        Net.EditorNet.LocalEditorChanged(false);
    }

    private void RestoreGameState()
    {
        Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;
        ReleaseCameraControl();
        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0, true);
    }

    public void ReassertPause()
    {
        if (_editing) Time.timeScale = 0f;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        Plugin.Log.LogWarning("MapEditor: no EventSystem found, creating one.");
        var go = new GameObject("MapEditor_EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private void Update()
    {
        if (!_editing) return;

        if (ModalOpen) return;

        if (Time.timeScale != 0f) Time.timeScale = 0f;

        if (_resetArmed && Time.unscaledTime - _resetArmedAt > ResetArmWindow) DisarmReset();

        // The multiplayer chat box draws over the bottom bar; the hint chips step aside for it.
        _bottomBar?.SetHintsVisible(!Net.EditorNet.ExternalOverlayVisible);

        if (_renaming)
        {
            HandleRenameInput();
            return;
        }

        ReleaseTypingLocks();

        // Someone is typing into the chat: no hotkeys, no camera keys, no tool input.
        if (Net.EditorNet.ExternalTyping)
        {
            try
            {
                _layers?.Tick();
            }
            catch (System.Exception)
            {
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape) && HandleEscape()) return;

        if (Input.GetKeyDown(KeyCode.F1)) _shortcutCard?.Toggle();

        if (CtrlHeld && Input.GetKeyDown(KeyCode.Z)) UndoLast();

        if (CtrlHeld && Input.GetKeyDown(KeyCode.S))
        {
            if (ScreenTool != null) ScreenTool.ScreenQuickSave();
            else QuickSave();
        }

        if (_chromeHidden)
        {
            HandleCameraControls();
            return;
        }

        if (ScreenTool == null)
        {
            try
            {
                HandleCameraControls();
            }
            catch (System.Exception e)
            {
                if (Time.unscaledTime >= _nextUpdateErrorAt)
                {
                    _nextUpdateErrorAt = Time.unscaledTime + 5f;
                    Plugin.Log.LogError("MapEditor: camera controls failed: " + e);
                }
            }
        }

        try
        {
            _activeTool?.OnUpdate();
        }
        catch (System.Exception e)
        {
            if (Time.unscaledTime >= _nextUpdateErrorAt)
            {
                _nextUpdateErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"MapEditor: tool '{_activeTool?.Name}' update failed: " + e);
            }
        }

        try
        {
            QuickPick?.Tick();
        }
        catch (System.Exception e)
        {
            if (Time.unscaledTime >= _nextUpdateErrorAt)
            {
                _nextUpdateErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError("MapEditor: quick pick update failed: " + e);
            }
        }

        try
        {
            _layers?.Tick();
        }
        catch (System.Exception e)
        {
            if (Time.unscaledTime >= _nextUpdateErrorAt)
            {
                _nextUpdateErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError("MapEditor: layer tree update failed: " + e);
            }
        }
    }

    // ---- the layer tree --------------------------------------------------------------------------

    /// The layer tree hands an object over: whatever tool is up, it becomes the Select tool's selection.
    public void PickFromLayers(GameObject go)
    {
        if (go == null) return;
        var select = GetTool<SelectTool>();
        if (select == null) return;

        SelectTool(select);
        select.SelectObject(go);
    }

    /// A shift-range or a group folder in the layer tree: several objects at once.
    public void PickManyFromLayers(IEnumerable<GameObject> objects)
    {
        var select = GetTool<SelectTool>();
        if (select == null) return;

        SelectTool(select);
        select.SelectMany(objects);
    }

    public void PickTriggerFromLayers(CTMapTrigger trigger)
    {
        if (trigger == null) return;
        var triggers = GetTool<TriggerTool>();
        if (triggers == null) return;

        SelectTool(triggers);
        triggers.SelectTrigger(trigger);
    }

    private float _nextUpdateErrorAt;

    private void HandleCameraControls()
    {
        if (_cameraAnchor == null) return;

        var dt = Time.unscaledDeltaTime;
        var move = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move.x += 1f;

        if (move != Vector3.zero)
            _cameraAnchor.transform.position += move.normalized * (PanSpeed * dt);

        var zoomDelta = 0f;
        if (!CtrlHeld)
        {
            if (Input.GetKey(KeyCode.Z)) zoomDelta += 1f;
            if (Input.GetKey(KeyCode.X)) zoomDelta -= 1f;
        }

        if (Mathf.Abs(zoomDelta) > 0.001f)
            _zoom = Mathf.Clamp(_zoom + zoomDelta * ZoomSpeed * dt, MinZoom, MaxZoom);

        HandleWheel();

        ApplyZoom();
    }

    public static bool CtrlHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    private static bool _wheelAxisMissing;

    private static float WheelDelta()
    {
        var delta = Input.mouseScrollDelta.y;
        if (Mathf.Abs(delta) > 0.001f) return delta;

        if (_wheelAxisMissing) return 0f;

        try
        {
            return Input.GetAxis("Mouse ScrollWheel");
        }
        catch (System.Exception)
        {
            _wheelAxisMissing = true;
            return 0f;
        }
    }

    private const float ToolSwitchCooldown = 0.12f;
    private float _lastToolSwitch;

    private void HandleWheel()
    {
        var scroll = WheelDelta();
        if (Mathf.Abs(scroll) < 0.005f) return;

        if (ScrollUiUnderPointer(scroll)) return;

        if (Time.unscaledTime - _lastToolSwitch < ToolSwitchCooldown) return;
        _lastToolSwitch = Time.unscaledTime;

        var direction = scroll > 0f ? -1 : 1;

        // A hotbar on screen is what the wheel is for. It hands the wheel back when it is hidden or
        // has nothing in it, so the wheel is never a key that does nothing.
        if (QuickPick != null && QuickPick.Step(direction)) return;

        CycleTool(direction);
    }

    private bool ScrollUiUnderPointer(float delta)
    {
        if (_canvasGO == null) return false;

        var mouse = (Vector2)Input.mousePosition;
        var scrollRects = _canvasGO.GetComponentsInChildren<ScrollRect>(false);

        for (var i = scrollRects.Length - 1; i >= 0; i--)
        {
            var scroll = scrollRects[i];
            if (scroll == null || scroll.viewport == null || scroll.content == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, mouse, null)) continue;

            var hidden = scroll.content.rect.height - scroll.viewport.rect.height;
            if (hidden > 1f)
                scroll.verticalNormalizedPosition =
                    Mathf.Clamp01(scroll.verticalNormalizedPosition + Mathf.Sign(delta) * 90f / hidden);
            return true;
        }

        return false;
    }

    private void ApplyZoom()
    {
        var gm = GameManager.GetInstance();
        if (gm == null || gm.CamFollowTarget == null) return;
        gm.CameraSetZoom(_zoom);
    }

    private void TakeCameraControl()
    {
        var start = PlayerFarming.Instance != null
            ? PlayerFarming.Instance.transform.position
            : (SceneRefs.Cam != null ? SceneRefs.Cam.transform.position : Vector3.zero);
        start.z = 0f;

        _cameraAnchor = new GameObject("MapEditor_CameraAnchor");
        _cameraAnchor.transform.position = start;

        var gm = GameManager.GetInstance();
        if (gm != null && gm.CamFollowTarget != null)
            _zoom = Mathf.Clamp(gm.CamFollowTarget.targetDistance, MinZoom, MaxZoom);

        CinematicCameraManager.SetCameraLimits(false, default);
        CinematicCameraManager.SetFollowTarget(_cameraAnchor);

        SuspendAreaCulling();
    }

    private void SuspendAreaCulling()
    {
        _suspendedCulling.Clear();

        foreach (var culling in Resources.FindObjectsOfTypeAll<BaseBiomeAreaCulling>())
        {
            if (culling == null || !culling.enabled) continue;
            culling.enabled = false;
            _suspendedCulling.Add(culling);
        }

        if (_suspendedCulling.Count > 0)
            Plugin.Log.LogInfo($"MapEditor: suspended {_suspendedCulling.Count} area culling component(s).");
    }

    private void RestoreAreaCulling()
    {
        if (KeepCullingSuspended)
        {
            Plugin.Log.LogInfo("MapEditor: leaving area culling suspended because objects were repositioned.");
            _suspendedCulling.Clear();
            return;
        }

        foreach (var culling in _suspendedCulling)
            if (culling != null) culling.enabled = true;
        _suspendedCulling.Clear();
    }

    public bool KeepCullingSuspended { get; set; }

    private void ReleaseCameraControl()
    {
        RestoreAreaCulling();
        CinematicCameraManager.ResetCameraTargets();
        CinematicCameraManager.ZoomReset();

        if (_cameraAnchor != null)
        {
            Destroy(_cameraAnchor);
            _cameraAnchor = null;
        }
    }

    public Vector3 ScreenToWorld(Vector2 screenPoint)
    {
        var cam = SceneRefs.Cam;
        if (cam == null) return Vector3.zero;

        var ray = cam.ScreenPointToRay(screenPoint);
        if (Mathf.Abs(ray.direction.z) < 1e-6f) return new Vector3(ray.origin.x, ray.origin.y, 0f);

        var t = -ray.origin.z / ray.direction.z;
        var p = ray.origin + ray.direction * t;
        p.z = 0f;
        return p;
    }

    /// Like ScreenToWorld but intersecting the plane z = depth. Anything drawn off the floor plane
    /// (the whiteboard at z -1) must project the mouse onto its own plane, or the tilted camera
    /// puts it a few pixels away from the cursor.
    public Vector3 ScreenToWorldAtDepth(Vector2 screenPoint, float depth)
    {
        var cam = SceneRefs.Cam;
        if (cam == null) return new Vector3(0f, 0f, depth);

        var ray = cam.ScreenPointToRay(screenPoint);
        if (Mathf.Abs(ray.direction.z) < 1e-6f) return new Vector3(ray.origin.x, ray.origin.y, depth);

        var t = (depth - ray.origin.z) / ray.direction.z;
        var p = ray.origin + ray.direction * t;
        p.z = depth;
        return p;
    }

    public Vector3 MouseWorld() => ScreenToWorld(Input.mousePosition);

    public Vector3 CameraFocus =>
        _cameraAnchor != null ? _cameraAnchor.transform.position : Vector3.zero;

    public void MoveCameraTo(Vector3 worldPosition)
    {
        if (_cameraAnchor == null) return;
        _cameraAnchor.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
    }

    private float _worldClickBlockedUntil;

    public void BlockWorldClicks() => _worldClickBlockedUntil = Time.unscaledTime + 0.2f;

    public bool WorldClicksBlocked => ModalOpen || Time.unscaledTime < _worldClickBlockedUntil;

    public bool PointerOverUi()
    {
        if (ModalOpen) return true;
        if (Time.unscaledTime < _worldClickBlockedUntil) return true;

        var mouse = (Vector2)Input.mousePosition;

        for (var i = _uiBlockers.Count - 1; i >= 0; i--)
        {
            var rect = _uiBlockers[i];
            if (rect == null)
            {
                _uiBlockers.RemoveAt(i);
                continue;
            }
            if (!rect.gameObject.activeInHierarchy) continue;

            if (RectTransformUtility.RectangleContainsScreenPoint(rect, mouse, null))
                return true;
        }

        return false;
    }

    public void RegisterUiBlocker(RectTransform rect)
    {
        if (rect != null && !_uiBlockers.Contains(rect)) _uiBlockers.Add(rect);
    }

    public void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        _statusMessage = message;
        _statusSeverity = severity;
        ApplyStatus(message, severity, pulse: true);
    }

    private string _statusMessage = "";
    private StatusSeverity _statusSeverity = StatusSeverity.Info;

    public void ShowHoverStatus(string message)
    {
        if (!_editing) return;

        if (ScreenTool != null) ScreenTool.ScreenHoverStatus(message);
        else ApplyStatus(message, StatusSeverity.Info, pulse: false);
    }

    public void ClearHoverStatus()
    {
        if (!_editing) return;

        if (ScreenTool != null) ScreenTool.ScreenHoverStatus(null);
        else ApplyStatus(_statusMessage, _statusSeverity, pulse: false);
    }

    private void ApplyStatus(string message, StatusSeverity severity, bool pulse)
    {
        _bottomBar?.SetStatus(message, severity, pulse);
    }

    private Coroutine _musicLoopRoutine;

    public void SetMusicLoop(string eventPath)
    {
        if (_musicLoopRoutine != null)
        {
            StopCoroutine(_musicLoopRoutine);
            _musicLoopRoutine = null;
        }
        if (!string.IsNullOrEmpty(eventPath))
            _musicLoopRoutine = StartCoroutine(MusicLoopRoutine(eventPath));
    }

    private IEnumerator MusicLoopRoutine(string eventPath)
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(1f);

            var audio = AudioManager.Instance;
            if (audio == null) continue;

            var stopped = true;
            try
            {
                var instance = audio.CurrentMusicInstance;
                if (instance.isValid())
                {
                    instance.getPlaybackState(out var state);
                    stopped = state == FMOD.Studio.PLAYBACK_STATE.STOPPED;
                }
            }
            catch (System.Exception)
            {
            }

            if (!stopped) continue;
            try
            {
                audio.PlayMusic(eventPath);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: music loop restart failed: {e.Message}");
            }
        }
    }

    private void SelectTool(IMapEditorTool tool)
    {
        if (tool == null || tool == _activeTool) return;

        ConfirmPrompt();

        if (tool is LevelTool or DungeonBuilderTool && _activeTool is not (LevelTool or DungeonBuilderTool))
            _toolBeforeScreen = _activeTool;

        _activeTool?.OnExit();
        _activeTool = tool;

        _ui.CloseTransientUi();

        foreach (Transform child in _optionsContent)
            child.gameObject.SetActive(child.name == "Options_" + tool.Name);

        foreach (var pair in _toolRings)
            if (pair.Value != null) pair.Value.gameObject.SetActive(pair.Key == tool.Name);

        _sidebar?.SetOptionsTitle(tool.Name);
        _sidebar?.NoteToolChanged();
        _shortcutCard?.Hide();

        RefreshShortcuts();
        _layers?.OnToolChanged(tool);

        _activeTool.OnEnter();
        SetStatus(tool.Name + " tool.");
    }

    private void CreateUi()
    {
        _canvasGO = new GameObject("RuntimeMapEditor_Canvas");
        _canvas = _canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        _ui.Attach(this, _canvasGO.GetComponent<RectTransform>());

        // The hover preview lives in the top-right corner, which is now the top bar and the sidebar.
        _ui.IconPreviewTopOffset = Chrome.EditorTopBar.Height + 12f;
        _ui.IconPreviewRightOffset = Chrome.EditorSidebar.Width + 28f;

        _topBar = new Chrome.EditorTopBar(_ui, _canvas.transform, RegisterUiBlocker);
        _bottomBar = new Chrome.EditorBottomBar(_ui, _canvas.transform, RegisterUiBlocker);
        _bottomBar.OnHelp = () => _shortcutCard?.Toggle();

        _sidebar = new Chrome.EditorSidebar(_ui, _canvas.transform, RegisterUiBlocker,
            Chrome.EditorTopBar.Height + 12f, Chrome.EditorBottomBar.Height + 12f);
        _optionsContent = _sidebar.OptionsContent;

        _shortcutCard = new Chrome.ShortcutCard(_ui, _canvas.transform, RegisterUiBlocker, GlobalShortcuts);

        PopulateDock();

        QuickPick = new MapEditorQuickPick(this, _ui, _canvas.transform, Chrome.EditorBottomBar.Height);
        _layers = new MapEditorLayerPanel(this, _ui, _sidebar);

        _confirm = new MapEditorConfirm(_ui, _canvas.transform, ConfirmBottom);

        // Captured last: a screen tool hides the editor's chrome by walking these, so anything built
        // after this line would still be drawn over the screen that replaced it.
        _ownChrome.Clear();
        foreach (Transform child in _canvas.transform) _ownChrome.Add(child.gameObject);

        History.Changed = MarkEdited;

        foreach (var tool in _tools)
        {
            var content = _ui.CreateScrollColumn(_optionsContent, "Options_" + tool.Name, out var root);
            tool.BuildPanel(content, _ui);
            _optionColumns[tool.Name] = content;
            root.SetActive(false);
        }

        // Shown only once the tools have built their panels: the Structure tool's BuildPanel is what
        // hands the bar its icons and its pinned list, so showing it before this would draw it blank.
        if (QuickPick != null) QuickPick.Visible = true;
    }

    private MapEditorConfirm _confirm;

    /// <summary>
    /// Raises the editor's confirm strip for a tool about to do something it cannot take back. It is
    /// the same strip that asks about unsaved work on close, on purpose: a question worth asking is
    /// worth asking in the one place the reader already knows to look for it.
    /// </summary>
    public void AskConfirm(string question, string confirmLabel, System.Action onConfirm)
    {
        if (_confirm == null)
        {
            onConfirm?.Invoke();
            return;
        }

        _confirm.Show(question, onConfirm, confirmLabel);
    }

    private const float ConfirmBottom = Chrome.EditorBottomBar.Height + 12f;

    private const float ToolIconSize = 60f;

    /// The Structure tool's nine-slot recent bar; it docks onto the bottom bar and that tool fills it.
    public MapEditorQuickPick QuickPick { get; private set; }

    private EditorContext _dockContext;
    private bool _dockBuilt;

    private void RefreshDockForContext()
    {
        if (!_dockBuilt || _dockContext == Context) return;

        _bottomBar?.ClearDock();
        _toolRings.Clear();

        PopulateDock();
        RefreshTopBarForContext();
    }

    /// <summary>
    /// The dock lists only the tools that place something. Load, Level and Dungeon Builder each take
    /// the whole screen the moment they are picked, which makes them file and screen actions rather
    /// than tools; they live in the top bar with Save, and the wheel no longer cycles onto them.
    /// </summary>
    private void PopulateDock()
    {
        if (_bottomBar == null) return;

        _dockContext = Context;
        _dockBuilt = true;

        foreach (var tool in DockTools())
        {
            var captured = tool;
            _bottomBar.AddTool(MapEditorIcons.GetToolIcon(tool.Name), tool.Name,
                () => SelectTool(captured), out var ring, ToolIconSize, tool.Name);
            _toolRings[tool.Name] = ring;

            if (tool is DoorTool || (_dockContext != EditorContext.Dungeon && tool is NpcTool))
                _bottomBar.AddSeparator(ToolIconSize);
        }

        _bottomBar.LayoutAfterDock();
    }

    /// The file actions, and the keys that mean the same thing under every tool.
    private void RefreshTopBarForContext()
    {
        if (_topBar == null) return;

        var context = Context;
        var actions = new List<Chrome.EditorBarAction>();

        var load = GetTool<LoadTool>();
        if (load != null && !HiddenIn(context, load))
            actions.Add(new Chrome.EditorBarAction("Load", MapEditorIcons.GetToolIconOrNull("Load Map"),
                () => SelectTool(load), "Open a saved map"));

        actions.Add(new Chrome.EditorBarAction("Save", MapEditorIcons.GetToolIconOrNull("Save"),
            SaveMap, context switch
            {
                EditorContext.Base => "Save the base",
                EditorContext.Hub => "Save the hub",
                _ => "Save this room"
            }));

        var clear = GetTool<ClearTool>();
        if (clear != null && !HiddenIn(context, clear))
        {
            actions.Add(Chrome.EditorBarAction.Divider);
            actions.Add(new Chrome.EditorBarAction("Clear",
                MapEditorIcons.GetToolIconOrNull("Clear"),
                anchor => OpenChooser(anchor, null, clear.ChooserOptions(), clear.ChooseFromMenu),
                "Remove things from this room"));
        }

        if (context == EditorContext.Dungeon)
        {
            actions.Add(Chrome.EditorBarAction.Divider);

            // These two open a screen for one chosen level or dungeon, so the button asks which
            // straight away rather than opening a panel whose only job is to ask.
            var level = GetTool<LevelTool>();
            if (level != null)
                actions.Add(new Chrome.EditorBarAction("Level",
                    MapEditorIcons.GetToolIconOrNull("Level"),
                    anchor => OpenChooser(anchor, level, level.ChooserOptions(), level.ChooseFromMenu),
                    "Open or make a level"));

            var dungeon = GetTool<DungeonBuilderTool>();
            if (dungeon != null)
                actions.Add(new Chrome.EditorBarAction("Dungeon Builder",
                    MapEditorIcons.GetToolIconOrNull("Dungeon Builder"),
                    anchor => OpenChooser(anchor, dungeon, dungeon.ChooserOptions(), dungeon.ChooseFromMenu),
                    "Open or make a dungeon"));
        }

        // Undo rides with the keys rather than the icons: there is no undo icon to draw, and a key
        // badge says what it is without one.
        var keys = new List<(string, string, System.Action)>
        {
            ("Ctrl+Z", "Undo", UndoLast),
            ("F6", "Hide UI", ToggleChromeHidden)
        };

        if (context != EditorContext.Base) keys.Add(("F5", "Reset", RequestResetRoom));
        keys.Add(("F4", "Close", ToggleEditor));

        _topBar.RebuildActions(actions, keys);
        _topBar.SetBadge(context switch
        {
            EditorContext.Base => "Base",
            EditorContext.Hub => "Hub",
            _ => "Dungeon room"
        });
    }

    /// <summary>
    /// Drops a tool's own chooser from the button that asked for it. The tool is only made active
    /// once something has been chosen: selecting it first would open its panel behind the menu, and
    /// a cancelled menu would leave the editor sitting in a tool the mapper never asked for.
    /// </summary>
    private void OpenChooser(RectTransform anchor, IMapEditorTool tool, List<string> options,
        System.Action<int> choose)
    {
        if (options == null || options.Count == 0)
        {
            SetStatus("Nothing to choose from here.");
            return;
        }

        _ui.ShowMenu(anchor, options, (index, _) =>
        {
            // Clearing is an action rather than a mode, so it passes no tool and the editor stays in
            // whatever the mapper had in hand.
            if (tool != null) SelectTool(tool);
            choose(index);
        });
    }

    /// Everything the shortcut card lists under "Editor": true under every tool, so no tool repeats it.
    private static readonly (string Key, string Action)[] GlobalShortcuts =
    [
        ("WASD", "Pan camera"),
        ("Z / X", "Zoom in / out"),
        ("Wheel", "Switch tool"),
        ("Ctrl+Z", "Undo last change"),
        ("Ctrl+S", "Quicksave under this name"),
        ("F1", "This list"),
        ("F6", "Hide UI (stays paused)"),
        ("F5", "Reset room"),
        ("F4", "Close editor")
    ];

    private int _optionsRebuildFrames;

    public void RequestOptionsResize() => _optionsRebuildFrames = 3;

    /// <summary>
    /// Shares the sidebar between the tool panel and the layer tree, then lets the tree lay its rows
    /// out. The order matters: the tree only draws the rows its viewport can show, so it has to be
    /// told how tall it is before it decides.
    /// </summary>
    private void LateUpdate()
    {
        if (!_editing || _sidebar == null) return;

        RectTransform column = null;
        if (_activeTool != null) _optionColumns.TryGetValue(_activeTool.Name, out column);

        if (_optionsRebuildFrames > 0)
        {
            _optionsRebuildFrames--;
            if (column != null) LayoutRebuilder.ForceRebuildLayoutImmediate(column);
        }

        var wantedOptions = column != null ? column.rect.height + 12f : 0f;
        var wantedLayers = _layers?.WantedHeight ?? 0f;

        _sidebar.Layout(wantedOptions, wantedLayers, _layers?.HiddenForTool ?? false);
        _sidebar.SetLayersSummary(_layers?.Summary);

        _layers?.LateUpdate();
        _bottomBar?.Tick();
        _topBar?.Tick();
    }

    private string TitleText
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Map.MapName) ? "Untitled" : Map.MapName;
            return Context switch
            {
                EditorContext.Base => "Base Editor  ·  " + name,
                EditorContext.Hub => "Hub Editor  ·  " + name,
                _ => "Map Editor  ·  " + name
            };
        }
    }

    /// The active tool's own keys go to the bottom bar; the card gets them too, under the globals.
    private void RefreshShortcuts()
    {
        var hints = _activeTool is IMapEditorShortcuts source
            ? new List<(string Key, string Action)>(source.Shortcuts)
            : [];

        _bottomBar?.SetHints(hints);
        _shortcutCard?.SetTool(_activeTool?.Name, hints, ToolNote());

        RefreshCardExtras();
    }

    /// Prose for what a key badge cannot carry: the Select tool's handles are told apart by colour.
    private string ToolNote() =>
        _activeTool is SelectTool
            ? "Handles: yellow moves, blue resizes, purple changes depth."
            : null;

    private void RefreshCardExtras()
    {
        if (_shortcutCard == null) return;

        var extras = new List<(string Label, System.Action Do)>();

        if (Net.EditorNet.Enabled)
            extras.Add(($"Resync with {Net.EditorNet.PeerName}", () =>
            {
                Net.EditorSnapshot.RequestResync();
                SetStatus(Net.EditorNet.IsHost
                    ? $"Sent the room to {Net.EditorNet.PeerName} again."
                    : $"Asked {Net.EditorNet.PeerName} for the room again.");
                _shortcutCard.Hide();
            }));

        // Debug: with NetVerbose on, the room can be run through the peer-snapshot path on one machine.
        if (Plugin.EditorNetVerbose.Value)
            extras.Add(("Sync self-test", () => Net.EditorApply.SelfTest(this)));

        _shortcutCard.SetExtras(extras);
    }

    private static void ClearUiSelection()
    {
        var events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject != null)
            events.SetSelectedGameObject(null);
    }

    private void ReleaseTypingLocks() => LockNavigator(false);

    private bool _navigatorLocked;

    private void LockNavigator(bool locked)
    {
        if (_navigatorLocked == locked) return;

        try
        {
            var navigator = MonoSingleton<src.UINavigator.UINavigatorNew>.Instance;
            if (navigator == null) return;

            navigator.LockInput = locked;
            _navigatorLocked = locked;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not " + (locked ? "lock" : "unlock") +
                                  " the UI navigator: " + e.Message);
        }
    }

    public void PromptText(string label, string initial, System.Action<string> onDone,
        System.Action<string> onChanged = null, System.Action onCancelled = null, bool inTitle = true)
    {
        _renaming = true;
        ClearUiSelection();
        LockNavigator(true);

        _promptLabel = label;
        _nameBuffer = initial ?? "";
        _promptDone = onDone;
        _promptChanged = onChanged;
        _promptCancelled = onCancelled;
        _promptInTitle = inTitle;

        UpdateNameLabel();
        onChanged?.Invoke(_nameBuffer);
        SetStatus($"Type a {label} - Enter confirms, Escape cancels.");
    }

    public bool IsTyping => _renaming;

    private System.Action<string> _promptChanged;
    private System.Action _promptCancelled;
    private bool _promptInTitle = true;

    private void EndPrompt()
    {
        _renaming = false;
        ReleaseTypingLocks();
        _promptChanged = null;
        _promptCancelled = null;
        _promptInTitle = true;
    }

    public void CancelPrompt()
    {
        if (!_renaming) return;

        var cancelled = _promptCancelled;
        _promptDone = null;
        EndPrompt();
        UpdateNameLabel();
        cancelled?.Invoke();
    }

    public void ConfirmPrompt()
    {
        if (!_renaming) return;

        var done = _promptDone;
        _promptDone = null;
        EndPrompt();
        done?.Invoke(_nameBuffer);
        UpdateNameLabel();
    }

    private void HandleRenameInput()
    {
        ClearUiSelection();

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ConfirmPrompt();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            var cancelled = _promptCancelled;
            _promptDone = null;
            EndPrompt();
            UpdateNameLabel();
            cancelled?.Invoke();
            SetStatus("Cancelled.");
            return;
        }

        var before = _nameBuffer;

        foreach (var c in Input.inputString)
        {
            if (c == '\b')
            {
                if (_nameBuffer.Length > 0) _nameBuffer = _nameBuffer.Substring(0, _nameBuffer.Length - 1);
            }
            else if (c != '\n' && c != '\r')
            {
                if (_nameBuffer.Length < 40) _nameBuffer += c;
            }
        }

        if (_nameBuffer == before) return;

        UpdateNameLabel();
        _promptChanged?.Invoke(_nameBuffer);
    }

    private void UpdateNameLabel()
    {
        if (_topBar == null) return;

        _topBar.SetTitle(_renaming && _promptInTitle
            ? _promptLabel + ": " + _nameBuffer + "_"
            : TitleText);

        _topBar.SetUnsaved(HasUnsavedEdits);
    }

    private bool _quickSaveArmed;
    private float _quickSaveArmedAt;
    private string _quickSavedName;

    private const float QuickSaveArmWindow = 5f;

    private void QuickSave()
    {
        if (_renaming || ModalOpen) return;

        if (Context == EditorContext.Base)
        {
            if (Net.EditorNet.ShouldRequestSave)
            {
                RequestPeerSave(null);
                return;
            }

            SetStatus("Saving the base...");
            WriteMap();
            return;
        }

        if (string.IsNullOrWhiteSpace(Map.MapName))
        {
            SaveMap();
            return;
        }

        var blocked = SaveBlock();
        if (blocked != null)
        {
            SetStatus(blocked, StatusSeverity.Error);
            return;
        }

        if (_quickSaveArmed && Time.unscaledTime - _quickSaveArmedAt > QuickSaveArmWindow)
            _quickSaveArmed = false;

        var known = string.Equals(_quickSavedName, Map.MapName, System.StringComparison.OrdinalIgnoreCase);
        if (!known && !_quickSaveArmed && MapEditorSerialization.Exists(Map.MapName))
        {
            _quickSaveArmed = true;
            _quickSaveArmedAt = Time.unscaledTime;
            SetStatus($"'{Map.MapName}.json' already exists - Ctrl+S again to overwrite.",
                StatusSeverity.Warning);
            return;
        }

        _quickSaveArmed = false;
        _quickSavedName = Map.MapName;

        if (Net.EditorNet.ShouldRequestSave)
        {
            RequestPeerSave(Map.MapName);
            return;
        }

        SetStatus($"Saving '{Map.MapName}'...");
        WriteMap();
    }

    private string SaveBlock()
    {
        var doorTool = Context == EditorContext.Dungeon ? GetTool<DoorTool>() : null;
        var missing = doorTool?.MissingDirections();
        if (missing != null && missing.Count > 0)
            return $"Cannot save: missing {string.Join(", ", missing)} door(s). " +
                   "Use the Door tool's 'Enable All Doors'.";

        return HubSaveBlock();
    }

    private static string HubSaveBlock()
    {
        if (!HubSession.Active || HubSession.SpawnPoint().HasValue) return null;

        return "Cannot save: a hub needs a trigger carrying the 'Hub spawn point' action - that is " +
               "where the player arrives.";
    }

    private void SaveMap()
    {
        var blocked = SaveBlock();
        if (blocked != null)
        {
            SetStatus(blocked, StatusSeverity.Error);
            Plugin.Log.LogWarning($"MapEditor: save blocked - '{Map.MapName}': {blocked}");
            _closeAfterSave = false;
            return;
        }

        if (Context == EditorContext.Base)
        {
            if (Net.EditorNet.ShouldRequestSave)
            {
                RequestPeerSave(null);
                if (_closeAfterSave)
                {
                    _closeAfterSave = false;
                    ExitEditorMode();
                }
                return;
            }

            WriteMap();
            return;
        }

        var previousTool = _activeTool;
        var previousCamera = _cameraAnchor != null ? _cameraAnchor.transform.position : (Vector3?)null;
        var previousZoom = _zoom;
        string chosen = null;

        ExitEditorMode();

        MapNamePrompt.Show(this, Map.MapName, "Save Map",
            name => chosen = name,
            () =>
            {
                EnterEditorMode();

                if (previousCamera.HasValue) MoveCameraTo(previousCamera.Value);
                _zoom = previousZoom;
                if (previousTool != null) SelectTool(previousTool);

                if (string.IsNullOrWhiteSpace(chosen))
                {
                    _closeAfterSave = false;
                    SetStatus("Save cancelled.");
                    return;
                }

                Map.MapName = chosen.Trim();
                UpdateNameLabel();

                if (Net.EditorNet.ShouldRequestSave)
                {
                    RequestPeerSave(Map.MapName);
                    if (_closeAfterSave)
                    {
                        _closeAfterSave = false;
                        ExitEditorMode();
                    }
                    return;
                }

                WriteMap();
            });
    }

    private void WriteMap() => StartCoroutine(WriteMapRoutine());

    private IEnumerator WriteMapRoutine()
    {
        try
        {
            yield return WriteMapBody();
        }
        finally
        {
            ReleaseBaseContext();
        }
    }

    private IEnumerator WriteMapBody()
    {
        SetStatus("Saving...");
        yield return null;

        Map.SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        foreach (var tool in _tools.OfType<IMapDataContributor>())
            tool.ContributeTo(Map);

        yield return null;

        if (Context == EditorContext.Base)
        {
            var written = BaseDelta.Save(Map);
            UpdateNameLabel();

            SetStatus(written
                    ? $"Saved the base for slot {BaseDelta.Slot}. The game's own save is untouched."
                    : "The base file could not be written, see log.",
                written ? StatusSeverity.Success : StatusSeverity.Error);

            if (!written)
            {
                _closeAfterSave = false;
                yield break;
            }

            MarkSaved();

            Net.EditorNet.LocalSaved(Map.MapName, [$"{BaseDelta.RootFolder}/base_slot{BaseDelta.Slot}.json"]);

            if (_closeAfterSave)
            {
                _closeAfterSave = false;
                ExitEditorMode();
            }

            yield break;
        }

        RoomSnapshot.Collect(Map, this);

        yield return null;

        var write = MapEditorSerialization.SaveAsync(Map);
        while (!write.IsCompleted) yield return null;

        var path = write.Result;
        if (path == null)
        {
            _closeAfterSave = false;
            SetStatus("Save failed, see log.", StatusSeverity.Error);
            yield break;
        }

        _quickSavedName = Map.MapName;
        MarkSaved();

        var shipped = new List<string>
        {
            MapEditorSerialization.FolderName + "/" + MapEditorSerialization.Sanitize(Map.MapName) + ".json"
        };

        if (Context == EditorContext.Hub)
        {
            HubSession.WriteRecord(Map.MapName, Map.MapName);
            shipped.Add(CTLevelSerialization.FolderName + "/" + MapEditorSerialization.Sanitize(Map.MapName) + ".json");
            SetStatus($"Saved hub '{Map.MapName}'. World map nodes can target it as a Hub.",
                StatusSeverity.Success);
        }

        yield return CaptureSnapshot();
        if (_snapshotWritten)
            shipped.Add(MapEditorSerialization.FolderName + "/" + MapEditorSerialization.Sanitize(Map.MapName) + ".png");

        Net.EditorNet.LocalSaved(Map.MapName, shipped);

        if (_closeAfterSave)
        {
            _closeAfterSave = false;
            ExitEditorMode();
        }
    }

    private bool _snapshotWritten;

    private IEnumerator CaptureSnapshot()
    {
        _snapshotWritten = false;
        var tool = _activeTool;
        tool?.OnExit();
        _canvas.enabled = false;

        yield return new WaitForEndOfFrame();

        byte[] png = null;
        try
        {
            var full = ScreenCapture.CaptureScreenshotAsTexture();

            var scaled = Downscale(full, SnapshotWidth);
            if (!ReferenceEquals(scaled, full)) Destroy(full);

            png = scaled.EncodeToPNG();
            Destroy(scaled);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: snapshot capture failed: " + e.Message);
        }

        // A host saving on a guest's behalf may not be editing; the canvas stays as it was.
        _canvas.enabled = _editing;
        tool?.OnEnter();

        if (png != null)
        {
            var pngPath = System.IO.Path.Combine(MapEditorSerialization.RootPath,
                MapEditorSerialization.Sanitize(Map.MapName) + ".png");

            var write = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    System.IO.File.WriteAllBytes(pngPath, png);
                    return true;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("MapEditor: snapshot write failed: " + e.Message);
                    return false;
                }
            });

            while (!write.IsCompleted) yield return null;
            if (write.Result) Plugin.Log.LogInfo("MapEditor: snapshot saved to " + pngPath);
            _snapshotWritten = write.Result;
        }

        SetStatus($"Saved '{Map.MapName}'.", StatusSeverity.Success);
    }

    private const int SnapshotWidth = 1920;

    private static Texture2D Downscale(Texture2D source, int width)
    {
        if (source == null || source.width <= width) return source;

        var height = Mathf.Max(1, Mathf.RoundToInt(width * (float)source.height / source.width));

        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            result.Apply();
            return result;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    public void RequestResetRoom()
    {
        if (_editing && !ModalOpen && !_renaming) ResetRoom();
    }

    private void ResetRoom()
    {
        var held = Net.EditorNet.WhyNotWorldChange();
        if (held != null)
        {
            SetStatus(held, StatusSeverity.Warning);
            return;
        }

        if (CustomDungeonManager.CustomDungeonList.Count == 0)
        {
            SetStatus("No custom dungeon to reset into.", StatusSeverity.Error);
            return;
        }

        if (!_resetArmed)
        {
            _resetArmed = true;
            _resetArmedAt = Time.unscaledTime;
                SetStatus("Reset discards unsaved edits - press again.", StatusSeverity.Warning);
            return;
        }

        DisarmReset();
        SetStatus("Resetting room...");
        ExitEditorMode();
        var editorDungeon = CustomDungeonManager.CustomDungeonList.Values.FirstOrDefault();
        if (editorDungeon != null) editorDungeon.EnterDungeon();
    }

    private void DisarmReset() => _resetArmed = false;
}

[HarmonyLib.HarmonyPatch(typeof(Interactor), "Update")]
internal static class Interactor_Update_Patch
{
    private static bool Prefix() =>
        RuntimeMapEditor.Active == null || !RuntimeMapEditor.Active.IsEditing;
}

public enum EditorContext
{
    Dungeon,
    Hub,
    Base
}

public enum StatusSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public interface IMapDataContributor
{
    void ContributeTo(CTNodeBlueprint map);
}
