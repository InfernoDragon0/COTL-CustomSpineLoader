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
    private RectTransform _toolOptionsPanel;
    private RectTransform _optionsContent;
    private TMP_Text _optionsTitle;
    private GameObject _optionsCollapseButton;
    private bool _optionsCollapsed;

    private readonly Dictionary<string, RectTransform> _optionColumns = [];

    private TMP_Text _statusText;
    private Image _statusPanel;
    private Image _statusBorder;

    private RectTransform _shortcutPanel;
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
        _tools.Add(new LightingTool(this));
        _tools.Add(new MusicTool(this));
        _tools.Add(new ClearTool(this));
        _tools.Add(new LoadTool(this));
        _tools.Add(new LevelTool(this));
        _tools.Add(new DungeonBuilderTool(this));
    }

    public T GetTool<T>() where T : class, IMapEditorTool => _tools.OfType<T>().FirstOrDefault();

    public void SelectFirstTool() => SelectTool(_tools.FirstOrDefault());

    public static EditorContext Context =>
        BaseSession.Active ? EditorContext.Base
        : HubSession.Active ? EditorContext.Hub
        : EditorContext.Dungeon;

    private static bool HiddenIn(EditorContext context, IMapEditorTool tool) => context switch
    {
        EditorContext.Hub => tool is EnemyTool or PodiumTool or DoorTool or LevelTool or DungeonBuilderTool,
        EditorContext.Base => tool is EnemyTool or PodiumTool or DoorTool or LevelTool or DungeonBuilderTool
            or ClearTool or LoadTool,
        _ => false
    };

    private List<IMapEditorTool> DockTools()
    {
        var context = Context;
        return context == EditorContext.Dungeon
            ? _tools
            : _tools.Where(t => !HiddenIn(context, t)).ToList();
    }

    public MapEditorHistory History { get; } = new();

    // ---- unsaved work --------------------------------------------------------------------------

    private int _edits;
    private int _savedEdits;

    public void MarkEdited() => _edits++;

    /// Bumps on every recorded edit; the layer tree watches it to know when to re-read the room.
    public int EditCount => _edits;

    public void MarkSaved() => _savedEdits = _edits;

    public bool HasUnsavedEdits => _edits != _savedEdits;

    private void UndoLast()
    {
        if (History.Undo(out var description)) SetStatus("Undid: " + description + ".");
        else SetStatus("Nothing to undo.");
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
        if (_editing) RestoreGameState();

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
        if (string.IsNullOrWhiteSpace(Map.MapName))
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
        WriteMap();
    }

    private bool _chromeHidden;

    private IMapEditorScreenTool ScreenTool =>
        _activeTool is IMapEditorScreenTool { OwnsScreen: true } tool ? tool : null;

    private readonly List<GameObject> _ownChrome = [];
    private bool _ownChromeVisible = true;

    public void SetOwnChromeVisible(bool visible)
    {
        if (_ownChromeVisible == visible) return;
        _ownChromeVisible = visible;

        foreach (var go in _ownChrome)
            if (go != null) go.SetActive(visible);
    }

    public void ToggleChromeHidden()
    {
        if (_canvas == null || !_editing || ModalOpen) return;

        if (ScreenTool != null) return;

        _chromeHidden = !_chromeHidden;
        _canvas.enabled = !_chromeHidden;
        MapEditorGizmos.SetHidden(_chromeHidden);
        ShowPreviewBadge(_chromeHidden);

        if (!_chromeHidden) SetStatus("Editor UI back. F6 hides it again.");
    }

    private void ShowChrome()
    {
        SetOwnChromeVisible(true);
        _chromeHidden = false;
        MapEditorGizmos.SetHidden(false);
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

        if (_renaming)
        {
            HandleRenameInput();
            return;
        }

        ReleaseTypingLocks();

        if (Input.GetKeyDown(KeyCode.Escape) && HandleEscape()) return;

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

    /// The layer tree and the shortcut hints share the left edge; opening the tree folds the hints.
    internal void CollapseShortcuts()
    {
        if (_shortcutsCollapsed) return;
        _shortcutsCollapsed = true;
        RefreshShortcuts();
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

        CycleTool(scroll > 0f ? -1 : 1);
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
        if (_statusText == null) return;

        _statusText.text = message;
        _statusText.color = severity switch
        {
            StatusSeverity.Success => new Color(0.55f, 0.9f, 0.55f),
            StatusSeverity.Warning => new Color(1f, 0.76f, 0.3f),
            StatusSeverity.Error => new Color(1f, 0.42f, 0.42f),
            _ => Color.white
        };

        if (_statusPanel == null) return;

        if (pulse)
        {
            var urgent = severity is StatusSeverity.Warning or StatusSeverity.Error;
            if (_statusBorder != null)
            {
                _statusBorder.gameObject.SetActive(urgent);
                if (urgent)
                    _statusBorder.color = severity == StatusSeverity.Error
                        ? MapEditorUI.Accent
                        : new Color(1f, 0.76f, 0.3f);
            }
            _statusPanel.color = VanillaChrome.Ready
                ? VanillaChrome.Tint
                : new Color(0f, 0f, 0f, urgent ? 0.78f : 0.62f);
        }
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

        _activeTool?.OnExit();
        _activeTool = tool;

        _ui.CloseTransientUi();

        foreach (Transform child in _optionsContent)
            child.gameObject.SetActive(child.name == "Options_" + tool.Name);

        foreach (var pair in _toolRings)
            if (pair.Value != null) pair.Value.gameObject.SetActive(pair.Key == tool.Name);

        if (_optionsTitle != null) _optionsTitle.text = tool.Name;

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

        CreateTitle();
        CreateDock();
        CreateOptionsPanel();
        CreateStatusBar();
        CreateShortcutPanel();
        _layers = new MapEditorLayerPanel(this, _ui, _canvas.transform);

        _ownChrome.Clear();
        foreach (Transform child in _canvas.transform) _ownChrome.Add(child.gameObject);

        _confirm = new MapEditorConfirm(_ui, _canvas.transform, ConfirmBottom);
        History.Changed = MarkEdited;

        foreach (var tool in _tools)
        {
            var content = _ui.CreateScrollColumn(_optionsContent, "Options_" + tool.Name, out var root);
            tool.BuildPanel(content, _ui);
            _optionColumns[tool.Name] = content;
            root.SetActive(false);
        }
    }

    private MapEditorConfirm _confirm;

    private const float ConfirmBottom = DockHeight + 20f + 46f + 12f;

    private const float ToolIconSize = 72f;

    private const int DockPadding = 14;

    private const int DockSidePadding = 24;

    private const float DockHeight = ToolIconSize + DockPadding * 2;

    private float _dockWidth = 600f;

    private RectTransform _dock;
    private EditorContext _dockContext;

    private void RefreshDockForContext()
    {
        if (_dock == null || _dockContext == Context) return;

        var stale = new List<GameObject>();
        foreach (Transform child in _dock) stale.Add(child.gameObject);
        foreach (var child in stale)
        {
            child.transform.SetParent(null, false);
            Destroy(child);
        }

        _toolRings.Clear();

        PopulateDock(_dock);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_dock);
        _dockWidth = _dock.rect.width;

        if (_statusPanel != null)
        {
            var rect = _statusPanel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(_dockWidth, rect.sizeDelta.y);
        }
    }

    private void CreateDock()
    {
        var dock = CreatePanel("Dock", new Vector2(0.5f, 0f), new Vector2(0f, DockHeight), new Vector2(0f, 12f));
        _dock = dock;

        var layout = dock.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(DockSidePadding, DockSidePadding, DockPadding, DockPadding);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        PopulateDock(dock);

        var fitter = dock.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutRebuilder.ForceRebuildLayoutImmediate(dock);
        _dockWidth = dock.rect.width;
    }

    private void PopulateDock(RectTransform dock)
    {
        _dockContext = Context;

        foreach (var tool in DockTools())
        {
            var captured = tool;
            _ui.CreateIconButton(dock, MapEditorIcons.GetToolIcon(tool.Name), tool.Name,
                () => SelectTool(captured), out var ring, ToolIconSize, hoverText: tool.Name);
            _toolRings[tool.Name] = ring;

            if (tool is DoorTool || (_dockContext != EditorContext.Dungeon && tool is NpcTool))
                CreateDockSeparator(dock);

            if (tool is LoadTool)
                _ui.CreateIconButton(dock, MapEditorIcons.GetToolIcon("Save"), "Save", SaveMap,
                    out _, ToolIconSize, hoverText: "Save map");
        }

        if (_dockContext == EditorContext.Base)
        {
            CreateDockSeparator(dock);
            _ui.CreateIconButton(dock, MapEditorIcons.GetToolIcon("Save"), "Save", SaveMap,
                out _, ToolIconSize, hoverText: "Save base");
        }
    }

    private void CreateDockSeparator(Transform parent)
    {
        var go = new GameObject("Separator");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(2f, ToolIconSize);

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 2f;
        element.preferredHeight = ToolIconSize;
        element.minWidth = 2f;

        go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);
    }

    private const float OptionsWidth = 420f;
    private const float OptionsHeaderHeight = 34f;
    private const float OptionsMaxHeight = 940f;

    private void CreateOptionsPanel()
    {
        _toolOptionsPanel = CreatePanel("ToolOptions", new Vector2(1f, 1f),
            new Vector2(OptionsWidth, 400f), new Vector2(-12f, -12f));

        var header = new GameObject("Header");
        header.transform.SetParent(_toolOptionsPanel, false);
        var headerRt = header.AddComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, OptionsHeaderHeight);
        headerRt.anchoredPosition = Vector2.zero;
        header.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        var title = _ui.CreateLabel(header.transform, "", 21, TextAlignmentOptions.Left);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = Vector2.zero;
        titleRt.anchorMax = Vector2.one;
        titleRt.offsetMin = new Vector2(12f, 0f);
        titleRt.offsetMax = new Vector2(-36f, 0f);
        _optionsTitle = title.GetComponent<TMP_Text>();
        _optionsTitle.enableWordWrapping = false;

        _optionsCollapseButton = _ui.CreateButton(header.transform, "–", ToggleOptionsCollapsed, 26f);
        var collapseRt = _optionsCollapseButton.GetComponent<RectTransform>();
        collapseRt.anchorMin = new Vector2(1f, 0.5f);
        collapseRt.anchorMax = new Vector2(1f, 0.5f);
        collapseRt.pivot = new Vector2(1f, 0.5f);
        collapseRt.sizeDelta = new Vector2(26f, 26f);
        collapseRt.anchoredPosition = new Vector2(-4f, 0f);

        var content = new GameObject("Content");
        content.transform.SetParent(_toolOptionsPanel, false);
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

        var label = _optionsCollapseButton != null
            ? _optionsCollapseButton.GetComponentInChildren<TMP_Text>() : null;
        if (label != null) label.text = _optionsCollapsed ? "+" : "–";
    }

    private int _optionsRebuildFrames;

    public void RequestOptionsResize() => _optionsRebuildFrames = 3;

    private void LateUpdate()
    {
        if (!_editing || _toolOptionsPanel == null) return;

        _layers?.LateUpdate();

        RectTransform column = null;
        if (!_optionsCollapsed && _activeTool != null)
            _optionColumns.TryGetValue(_activeTool.Name, out column);

        if (_optionsRebuildFrames > 0)
        {
            _optionsRebuildFrames--;
            if (column != null) LayoutRebuilder.ForceRebuildLayoutImmediate(column);
        }

        var target = OptionsHeaderHeight + 10f;
        if (column != null)
            target = Mathf.Min(column.rect.height + OptionsHeaderHeight + 12f, OptionsMaxHeight);

        var size = _toolOptionsPanel.sizeDelta;
        if (Mathf.Abs(size.y - target) > 1f)
            _toolOptionsPanel.sizeDelta = new Vector2(size.x, target);
    }

    private void CreateStatusBar()
    {
        var bar = CreatePanel("StatusBar", new Vector2(0.5f, 0f), new Vector2(_dockWidth, 46f),
            new Vector2(0f, DockHeight + 20f));
        _statusPanel = bar.GetComponent<Image>();

        _statusBorder = MapEditorUI.AddOutline(bar, MapEditorUI.Accent, inset: 3f);
        _statusBorder.gameObject.SetActive(false);

        var label = _ui.CreateLabel(bar, "", 22, TextAlignmentOptions.Center);
        var rt = label.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _statusText = label.GetComponent<TMP_Text>();
    }

    private void CreateTitle()
    {
        var go = new GameObject("Title");
        go.transform.SetParent(_canvas.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(620f, 52f);
        rt.anchoredPosition = new Vector2(16f, -16f);

        var label = _ui.CreateHeadingLabel(go.transform, TitleText, 34);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        _titleText = label.GetComponent<TMP_Text>();
        _titleText.alignment = TextAlignmentOptions.Left;
        _titleText.enableWordWrapping = false;
        _titleText.raycastTarget = false;
    }

    private TMP_Text _titleText;

    private string TitleText =>
        $"CultTweaker Map Editor  -  {(string.IsNullOrWhiteSpace(Map.MapName) ? "Untitled" : Map.MapName)}";

    private void CreateShortcutPanel()
    {
        var go = new GameObject("Shortcuts");
        go.transform.SetParent(_canvas.transform, false);

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

        RegisterUiBlocker(_shortcutPanel);
    }

    private bool _shortcutsCollapsed;

    private void ToggleShortcutsCollapsed()
    {
        _shortcutsCollapsed = !_shortcutsCollapsed;
        if (!_shortcutsCollapsed) _layers?.SetCollapsed(true);
        RefreshShortcuts();
    }

    private void RefreshShortcuts()
    {
        if (_shortcutPanel == null) return;

        foreach (Transform child in _shortcutPanel)
            Destroy(child.gameObject);

        if (!_shortcutsCollapsed)
        {
            if (_activeTool is IMapEditorShortcuts source)
            {
                foreach (var (key, action) in source.Shortcuts)
                    _ui.CreateKeyHint(_shortcutPanel, key, action);
            }

            _ui.CreateKeyHint(_shortcutPanel, "WASD", "Pan camera");
            _ui.CreateKeyHint(_shortcutPanel, "Z / X", "Zoom in / out");
            _ui.CreateKeyHint(_shortcutPanel, "Wheel", "Switch tool");
            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+Z", "Undo last placement");
            _ui.CreateKeyHint(_shortcutPanel, "Ctrl+S", "Quicksave under this name");
            _ui.CreateKeyHint(_shortcutPanel, "F6", "Hide UI (stays paused)");
            _ui.CreateKeyHint(_shortcutPanel, "F5", "Reset room");
            _ui.CreateKeyHint(_shortcutPanel, "F4", "Close editor");
        }

        _ui.CreateButton(_shortcutPanel, _shortcutsCollapsed ? "Shortcuts   +" : "Shortcuts   -",
            ToggleShortcutsCollapsed, 30f);
    }

    private RectTransform CreatePanel(string name, Vector2 anchor, Vector2 size, Vector2 offset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_canvas.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;

        VanillaChrome.Dress(go.AddComponent<Image>());

        RegisterUiBlocker(rt);
        return rt;
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
        if (_titleText == null) return;
        _titleText.text = _renaming && _promptInTitle
            ? _promptLabel + ": " + _nameBuffer + "_"
            : TitleText;
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
                WriteMap();
            });
    }

    private void WriteMap() => StartCoroutine(WriteMapRoutine());

    private IEnumerator WriteMapRoutine()
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

        if (Context == EditorContext.Hub)
        {
            HubSession.WriteRecord(Map.MapName, Map.MapName);
            SetStatus($"Saved hub '{Map.MapName}'. World map nodes can target it as a Hub.",
                StatusSeverity.Success);
        }

        yield return CaptureSnapshot();

        if (_closeAfterSave)
        {
            _closeAfterSave = false;
            ExitEditorMode();
        }
    }

    private IEnumerator CaptureSnapshot()
    {
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

        _canvas.enabled = true;
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
