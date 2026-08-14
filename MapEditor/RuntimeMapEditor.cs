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

// TODO:
// add a trigger collider volume placement tool that can be used to trigger events in the map
// add a npc placement tool
// improve the UI so that it is less wordy and more intuitive
// fix the zoom issue, each time a structure is selected, the zoom gets further
// cant zoom in or out yet with Z X
// check the increment for z axis data for each shape to stack on top of each other
public class RuntimeMapEditor : MonoBehaviour
{
    private Canvas _canvas;
    private GameObject _canvasGO;
    private RectTransform _toolOptionsPanel;
    private RectTransform _optionsContent;
    private TMP_Text _optionsTitle;
    private GameObject _optionsCollapseButton;
    private bool _optionsCollapsed;

    // Per-tool option column, kept so the panel can size itself to whichever one is showing.
    private readonly Dictionary<string, RectTransform> _optionColumns = [];

    private TMP_Text _statusText;
    private Image _statusPanel;
    private Image _statusBorder;

    // The current tool's controls, listed down the left edge.
    private RectTransform _shortcutPanel;

    // Selection ring per tool button on the dock.
    private readonly Dictionary<string, Image> _toolRings = [];

    private readonly List<IMapEditorTool> _tools = [];
    private IMapEditorTool _activeTool;

    private MapEditorUI _ui;
    private float _savedTimeScale = 1f;
    private bool _editing;

    private GameObject _cameraAnchor;

    // CamFollowTarget.targetDistance, whose game default is 12. Not an orthographic size.
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

    // True while a vanilla modal (the naming dialog) is up. Its canvas sorts around 100 and the
    // editor's at 5000, so the editor would draw over it and its raycaster would win every
    // click - the whole UI hides for the duration instead.
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

    // The hover components on buttons and grid cells need a way back to the status bar without
    // every widget carrying a reference; there is only ever one editor host per scene.
    public static RuntimeMapEditor Active { get; private set; }

    private void Awake()
    {
        Active = this;
        _ui = new MapEditorUI();
        BuildTools();
        CreateUi();
        _canvas.enabled = false;

        // A level run survives the scene reload as static state; the new host re-binds it
        // (and starts the entrance-room load if the run was waiting on this scene entry).
        LevelPlayback.OnEditorReady(this);
    }

    private void BuildTools()
    {
        _tools.Add(new SelectTool(this));
        _tools.Add(new ShapeTool(this));
        _tools.Add(new StructureTool(this));
        _tools.Add(new EnemyTool(this));
        _tools.Add(new PodiumTool(this));
        _tools.Add(new DoorTool(this));
        _tools.Add(new LightingTool(this));
        _tools.Add(new MusicTool(this));
        _tools.Add(new ClearTool(this));
        _tools.Add(new LoadTool(this));
        _tools.Add(new LevelTool(this));
    }

    public T GetTool<T>() where T : class, IMapEditorTool => _tools.OfType<T>().FirstOrDefault();

    // Shared by every tool that places something, so Ctrl+Z is the only undo the user needs.
    public MapEditorHistory History { get; } = new();

    private void UndoLast()
    {
        if (History.Undo(out var description)) SetStatus("Undid: " + description + ".");
        else SetStatus("Nothing to undo.");
    }

    // The wheel switches tools rather than zooming: zoom is on Z/X, and a wheel that changed the
    // view fought with every scrollable list in the editor.
    private void CycleTool(int direction)
    {
        if (_tools.Count == 0) return;

        var index = _activeTool != null ? _tools.IndexOf(_activeTool) : 0;
        index = (index + direction % _tools.Count + _tools.Count) % _tools.Count;
        SelectTool(_tools[index]);
    }

    // One loader shared by every consumer (Load Map tool, level playback), so the IsLoading
    // guard actually covers concurrent load attempts.
    private BlueprintLoader _loader;
    public BlueprintLoader Loader => _loader ??= new BlueprintLoader(this);

    // Swaps the working blueprint for one that was just loaded, so a subsequent Save round-trips.
    public void AdoptBlueprint(CTNodeBlueprint bp)
    {
        if (bp == null) return;
        Map = bp;
        UpdateNameLabel();
    }

    // The loader needs to close the editor (restore time, HUD, camera) before the walk-in entry
    // can run: GoToAndStop paths on scaled time, and Update would re-freeze timeScale otherwise.
    public void ExitForPlayback()
    {
        if (_editing) ExitEditorMode();
    }

    private void OnDestroy()
    {
        // The canvas lives on its own GameObject, so destroying the host is not enough.
        // Without this it leaks one canvas per dungeon entry.
        if (_canvasGO != null) Destroy(_canvasGO);
        if (_editing) RestoreGameState();

        // Icons harvested from the scene (build-menu sprites, rendered enemy thumbnails) die
        // with it; keeping them cached would hand the next scene destroyed sprites.
        MapEditorIcons.ClearSceneScopedCache();
        EnemyThumbnails.ClearSceneScopedCache();

        if (Active == this) Active = null;
    }

    public void ToggleEditor()
    {
        if (_canvas == null || ModalOpen) return;

        if (_editing) ExitEditorMode();
        else EnterEditorMode();
    }

    private void EnterEditorMode()
    {
        if (!SceneRefs.HasRoom)
        {
            Plugin.Log.LogWarning("MapEditor: no GenerateRoom in this scene, editor unavailable.");
            return;
        }

        EnsureEventSystem();

        _editing = true;
        _canvas.enabled = true;

        // Hide the HUD before freezing time, and snap rather than animate. COTL_API's ShowHUD
        // passes Snap: false, so the fade-out was still running when timeScale hit zero and the
        // HUD stayed frozen on screen over the editor.
        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Hide(true, 0);

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        TakeCameraControl();

        // Capture the shape template and profiles NOW, while the room is intact: the shape tool
        // otherwise captures lazily on first entry, and clearing the terrain before ever opening
        // it would leave no sprite shape in the scene to base new shapes on.
        GetTool<ShapeTool>()?.PrepareForLoad();

        SelectTool(_tools.FirstOrDefault());
        SetStatus("Editor open.");
    }

    private void ExitEditorMode()
    {
        _renaming = false;
        RestoreEventSystem();

        _activeTool?.OnExit();
        _activeTool = null;
        _editing = false;
        _canvas.enabled = false;
        RestoreGameState();
    }

    private void RestoreGameState()
    {
        // Time first: the HUD's show animation needs a running clock.
        Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;
        ReleaseCameraControl();
        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0, true);
    }

    // The build menu restores timeScale when it closes, which silently un-pauses the game while
    // the editor is still open. Tools that open game menus call this once the menu is gone.
    public void ReassertPause()
    {
        if (_editing) Time.timeScale = 0f;
    }

    // Clicks are silently swallowed without an EventSystem, which is easy to miss when debugging.
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        Plugin.Log.LogWarning("MapEditor: no EventSystem found, creating one.");
        var go = new GameObject("MapEditor_EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    // Each subsystem is isolated: a throw in camera handling must never stop tools from updating.
    private void Update()
    {
        if (!_editing) return;

        // Game menus opened from a tool (the build menu in particular) restore timeScale when
        // they close, which would silently un-pause the world under the open editor.
        if (Time.timeScale != 0f) Time.timeScale = 0f;

        if (_resetArmed && Time.unscaledTime - _resetArmedAt > ResetArmWindow) DisarmReset();

        // While naming, the keyboard belongs to the text field: panning and tools must not also
        // consume the same keystrokes.
        if (_renaming)
        {
            HandleRenameInput();
            return;
        }

        // The naming dialog is driving; nothing here should also respond to the same input.
        if (ModalOpen) return;

        // Safety net: nothing should leave the EventSystem suppressed once text entry is over.
        RestoreEventSystem();

        if (CtrlHeld && Input.GetKeyDown(KeyCode.Z)) UndoLast();

        try
        {
            HandleCameraControls();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError("MapEditor: camera controls failed: " + e);
        }

        try
        {
            _activeTool?.OnUpdate();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"MapEditor: tool '{_activeTool?.Name}' update failed: " + e);
        }
    }

    // Panning drives the game's own camera rig through a dummy follow target, rather than moving
    // Camera.main directly: CameraFollowTarget re-asserts the camera position every frame, so
    // direct writes are silently reverted.
    //
    // timeScale is 0 while editing, so everything here must use unscaled time.
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

        // Z/X rather than Q/E: the game's UI navigator binds Q/E to page-left/page-right, so
        // pressing them cycled the editor's own tool buttons instead of zooming. Ctrl is
        // excluded so Ctrl+Z undoes rather than zooming at the same time.
        var zoomDelta = 0f;
        if (!CtrlHeld)
        {
            if (Input.GetKey(KeyCode.Z)) zoomDelta += 1f;
            if (Input.GetKey(KeyCode.X)) zoomDelta -= 1f;
        }

        if (Mathf.Abs(zoomDelta) > 0.001f)
            _zoom = Mathf.Clamp(_zoom + zoomDelta * ZoomSpeed * dt, MinZoom, MaxZoom);

        HandleWheel();

        // Asserted EVERY frame, and via CameraSetZoom rather than CameraSetTargetZoom: the
        // camera only chases targetDistance with scaled deltaTime, which is 0 while the editor
        // is paused, so a target-only write never becomes visible. Meanwhile game menus opened
        // from the editor drift the real distance through their own unscaled zoom path, which
        // is why the view crept further out on every build-menu open. Setting distance and
        // target together each frame makes Z/X work under pause and pins the drift.
        ApplyZoom();
    }

    public static bool CtrlHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    // Both wheel sources are live in this game - Rewired's pointer module reads
    // Input.mouseScrollDelta, and MouseManager reads the legacy "Mouse ScrollWheel" axis - so
    // either will do. What differs between them, and between mice, is SCALE: one notch is 1.0
    // through mouseScrollDelta but around 0.1 through the axis. The fallback is kept for the
    // case where the delta reads flat.
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
            // The axis is not defined in this build's input manager; stop asking.
            _wheelAxisMissing = true;
            return 0f;
        }
    }

    // Any wheel movement is one step. Summing the delta and waiting for it to reach 1.0 is what
    // broke this: a mouse reporting ~0.1 per notch needed ten notches to change tool, which read
    // as the wheel doing nothing at all while the lists - which scroll proportionally - were
    // fine. A short cooldown does the de-bouncing instead, at any scale.
    private const float ToolSwitchCooldown = 0.12f;
    private float _lastToolSwitch;

    private void HandleWheel()
    {
        var scroll = WheelDelta();
        if (Mathf.Abs(scroll) < 0.005f) return;

        // Over one of the editor's own lists the wheel belongs to that list. It cannot be left
        // to uGUI either: this game installs Rewired's pointer module, which never delivers
        // scroll events to our ScrollRects, so it is routed by hand - the same reason
        // PointerOverUi tests our own rects instead of asking the EventSystem.
        if (ScrollUiUnderPointer(scroll)) return;

        if (Time.unscaledTime - _lastToolSwitch < ToolSwitchCooldown) return;
        _lastToolSwitch = Time.unscaledTime;

        CycleTool(scroll > 0f ? -1 : 1);
    }

    // Scrolls whichever of the editor's own lists the cursor is over. Returns true when the
    // wheel was consumed, so it does not also switch tools.
    private bool ScrollUiUnderPointer(float delta)
    {
        if (_canvasGO == null) return false;

        var mouse = (Vector2)Input.mousePosition;
        var scrollRects = _canvasGO.GetComponentsInChildren<ScrollRect>(false);

        // Back to front: an open dropdown list is parented last and must win over the panel
        // it is drawn on top of.
        for (var i = scrollRects.Length - 1; i >= 0; i--)
        {
            var scroll = scrollRects[i];
            if (scroll == null || scroll.viewport == null || scroll.content == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, mouse, null)) continue;

            var hidden = scroll.content.rect.height - scroll.viewport.rect.height;
            // A fixed step per wheel event rather than one proportional to the delta: the delta's
            // scale varies by an order of magnitude between input sources and mice, which would
            // otherwise make the same flick scroll ten times further on one setup than another.
            // Still consumed when there is nothing to scroll - the cursor is over a list.
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

    // Hands the camera to a dummy object we can move freely, and lifts the room's camera bounds
    // so the view can leave the play area.
    private void TakeCameraControl()
    {
        var start = PlayerFarming.Instance != null
            ? PlayerFarming.Instance.transform.position
            : (SceneRefs.Cam != null ? SceneRefs.Cam.transform.position : Vector3.zero);
        start.z = 0f;

        _cameraAnchor = new GameObject("MapEditor_CameraAnchor");
        _cameraAnchor.transform.position = start;

        // Start from wherever the game's zoom actually is, so opening the editor never jumps.
        var gm = GameManager.GetInstance();
        if (gm != null && gm.CamFollowTarget != null)
            _zoom = Mathf.Clamp(gm.CamFollowTarget.targetDistance, MinZoom, MaxZoom);

        CinematicCameraManager.SetCameraLimits(false, default);
        CinematicCameraManager.SetFollowTarget(_cameraAnchor);

        SuspendAreaCulling();
    }

    // BaseBiomeAreaCulling deactivates whole areas whose precomputed bounds fall outside the
    // camera viewport. Editing breaks its assumptions in two ways: the camera roams far from the
    // player, and moving an object (a door especially) puts it outside the area it was registered
    // to, so it vanishes the next time the view shifts. Suspended for the duration of the session.
    private void SuspendAreaCulling()
    {
        _suspendedCulling.Clear();

        // FindObjectsOfTypeAll rather than FindObjectsOfType: the latter skips components on
        // inactive objects, which left some culling live and still deactivating areas as the
        // editor camera roamed.
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
        // BaseBiomeAreaCulling reparents objects into a culling area based on position, and the
        // area's bounds never move. A door dragged out of its original area therefore gets
        // deactivated along with that area the moment culling resumes, which looks exactly like
        // the door being deleted. Once anything has been moved, culling stays off for the rest of
        // the session; it is only a performance optimisation.
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

    // Set by tools that move objects out of their original culling area.
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

    // Projects a screen point onto the z=0 world plane. Correct for both orthographic and
    // perspective cameras, replacing the two inconsistent conversions the prototype used.
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

    // Recentres the view. Moves the follow anchor rather than the camera, which the game's rig
    // would otherwise overwrite on the next frame.
    public void MoveCameraTo(Vector3 worldPosition)
    {
        if (_cameraAnchor == null) return;
        _cameraAnchor.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
    }

    // True when the cursor is over editor chrome, so tools do not also act on the click.
    //
    // Deliberately not EventSystem.IsPointerOverGameObject(): this game installs Rewired's own
    // pointer input module, under which that call reported true almost everywhere and silently
    // rejected every world click. Testing our own rects is exact and input-module independent.
    // Set whenever editor chrome is clicked. A press on a button must not also register as a
    // world click for whichever tool is active a moment later; the window covers the gap
    // between the button firing on pointer-up and the tool seeing the same press.
    private float _worldClickBlockedUntil;

    public void BlockWorldClicks() => _worldClickBlockedUntil = Time.unscaledTime + 0.2f;

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

            // Screen-space overlay canvas, so the camera argument must be null.
            if (RectTransformUtility.RectangleContainsScreenPoint(rect, mouse, null))
                return true;
        }

        return false;
    }

    // Chrome that should absorb clicks rather than passing them through to the world.
    public void RegisterUiBlocker(RectTransform rect)
    {
        if (rect != null && !_uiBlockers.Contains(rect)) _uiBlockers.Add(rect);
    }

    // The status bar is the editor's only feedback channel, and a blocked save read exactly like
    // a routine "tool active" line. Severity colours the text and, for anything the user has to
    // act on, darkens the bar and pulses it once so it cannot be missed.
    public void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        _statusMessage = message;
        _statusSeverity = severity;
        ApplyStatus(message, severity, pulse: true);
    }

    // What the bar was saying before the cursor wandered over a button, so hovering can be
    // undone without losing the last real message.
    private string _statusMessage = "";
    private StatusSeverity _statusSeverity = StatusSeverity.Info;

    // Wordless icons need somewhere to say their name; the status bar is it.
    public void ShowHoverStatus(string message) => ApplyStatus(message, StatusSeverity.Info, pulse: false);

    public void ClearHoverStatus() => ApplyStatus(_statusMessage, _statusSeverity, pulse: false);

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

        // The alert border tracks the last real message, not whatever the cursor is hovering
        // over, and it stays up until something good replaces it. A blink was easy to miss and
        // stopped drawing attention the moment it finished.
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
            _statusPanel.color = new Color(0f, 0f, 0f, urgent ? 0.78f : 0.62f);
        }
    }

    // Restarts the blueprint's music event whenever it stops; FMOD events only loop when
    // authored to, so this is what makes one-shot tracks usable as room music. Null stops
    // the loop (a load without MusicLoop, or one with no music at all).
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
                // Instance released mid-check; treat as stopped and restart below.
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

        _activeTool?.OnExit();
        _activeTool = tool;

        // A dropdown list left open over the previous tool's panel would outlive the panel and
        // keep absorbing clicks.
        _ui.CloseTransientUi();

        // Only the active tool's option column is visible.
        foreach (Transform child in _optionsContent)
            child.gameObject.SetActive(child.name == "Options_" + tool.Name);

        foreach (var pair in _toolRings)
            if (pair.Value != null) pair.Value.gameObject.SetActive(pair.Key == tool.Name);

        if (_optionsTitle != null) _optionsTitle.text = tool.Name;

        RefreshShortcuts();

        _activeTool.OnEnter();
        SetStatus(tool.Name + " tool.");
    }

    private void CreateUi()
    {
        _canvasGO = new GameObject("RuntimeMapEditor_Canvas");
        _canvas = _canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;

        // The prototype left this at defaults, which renders the panel unreadably small at 4K.
        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        // Floating overlays (dropdown lists) parent to the canvas root, and async icon fills
        // need a coroutine host, so the UI layer has to know both before anything is built.
        _ui.Attach(this, _canvasGO.GetComponent<RectTransform>());

        CreateTitle();
        CreateDock();
        CreateOptionsPanel();
        CreateStatusBar();
        CreateShortcutPanel();

        foreach (var tool in _tools)
        {
            var content = _ui.CreateScrollColumn(_optionsContent, "Options_" + tool.Name, out var root);
            tool.BuildPanel(content, _ui);
            _optionColumns[tool.Name] = content;
            root.SetActive(false);
        }
    }

    // The tool dock: one flat row of square icon buttons across the bottom of the screen, with
    // the map's own actions at its right end. It replaces the tall left-hand column, which ate a
    // quarter of the screen and had to scroll - and a scrolled-away button sat outside every
    // registered blocker rect, so clicking it placed an object in the world instead.
    private const float DockHeight = 112f;
    private const float DockWidth = 1100f;
    private const float ToolIconSize = 72f;

    private void CreateDock()
    {
        var dock = CreatePanel("Dock", new Vector2(0.5f, 0f), new Vector2(DockWidth, DockHeight), new Vector2(0f, 12f));

        var layout = dock.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        foreach (var tool in _tools)
        {
            var captured = tool;
            // The icons carry no text, so the status bar names whichever one the cursor is over.
            _ui.CreateIconButton(dock, MapEditorIcons.GetToolIcon(tool.Name), tool.Name,
                () => SelectTool(captured), out var ring, ToolIconSize, hoverText: tool.Name);
            _toolRings[tool.Name] = ring;

            // Everything up to and including the door tool builds the room itself; what follows
            // is mood, housekeeping and blueprints. The gap is the whole grouping story - a
            // Photoshop-style flyout would have hidden tools behind a press-and-hold.
            if (tool is DoorTool) CreateDockSeparator(dock);
        }

        CreateDockSeparator(dock);

        // Save is the only map-level action left on the dock, and it is an icon like the tools.
        // Rename went with the naming dialog (Save asks for the name now), and Close and Reset
        // are keys, which the shortcut list already spells out.
        _ui.CreateIconButton(dock, MapEditorIcons.GetToolIcon("Save"), "Save", SaveMap,
            out _, ToolIconSize, hoverText: "Save map");
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
    private const float OptionsMaxHeight = 820f;

    // Narrower than before (the settings-menu row clones it had to accommodate are gone) and it
    // now sizes itself to the tool showing in it, so a two-button tool no longer reserves the
    // same 620px of screen as the lighting editor.
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

        // The tool name lives here now that the dock buttons are wordless icons.
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

    // The panel follows the active tool's content height rather than sitting at a fixed size.
    // ContentSizeFitter has already resolved that height by LateUpdate.
    // Nested ContentSizeFitters (the grid inside the tool's column) do not settle on their own
    // when children are destroyed: the column kept its old height until something else - a
    // scroll - forced a rebuild, so swapping a 150-entry group for a 12-entry one left the
    // panel oversized. Tools that rebuild their contents ask for a rebuild here instead.
    private int _optionsRebuildFrames;

    public void RequestOptionsResize() => _optionsRebuildFrames = 3;

    private void LateUpdate()
    {
        if (!_editing || _toolOptionsPanel == null) return;

        RectTransform column = null;
        if (!_optionsCollapsed && _activeTool != null)
            _optionColumns.TryGetValue(_activeTool.Name, out column);

        // Three frames, because Destroy is deferred to the end of the current one and the
        // staggered fill can still be adding cells.
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
        // Sits directly above the dock: the two together are the editor's only bottom chrome.
        var bar = CreatePanel("StatusBar", new Vector2(0.5f, 0f), new Vector2(DockWidth, 46f),
            new Vector2(0f, DockHeight + 20f));
        _statusPanel = bar.GetComponent<Image>();

        // A frame rather than a plate behind: a child Graphic always draws over its parent's,
        // so a solid backing would have covered the bar instead of outlining it.
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

    // Says whose editor this is and that it is live - the world behind it is still the game,
    // and at a glance a paused dungeon looks much like a running one.
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

        var label = _ui.CreateHeader(go.transform, TitleText, 34);
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

    // The map name lives here now rather than in an editable label on the dock: it is only ever
    // set through the save dialog, so it is a readout, not a control.
    private string TitleText =>
        $"CultTweaker Map Editor  -  {(string.IsNullOrWhiteSpace(Map.MapName) ? "Untitled" : Map.MapName)}";

    // The controls that are not buttons - what the mouse does, what Delete does - listed down
    // the left edge in the game's own prompt style, and rebuilt whenever the tool changes.
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

        // Each row paints its own plate, so the container stays invisible; it is still a click
        // blocker, because a hint sitting over the map must not double as a placement target.
        RegisterUiBlocker(_shortcutPanel);
    }

    private void RefreshShortcuts()
    {
        if (_shortcutPanel == null) return;

        foreach (Transform child in _shortcutPanel)
            Destroy(child.gameObject);

        if (_activeTool is IMapEditorShortcuts source)
        {
            foreach (var (key, action) in source.Shortcuts)
                _ui.CreateKeyHint(_shortcutPanel, key, action);
        }

        // Always last: these work in every tool.
        _ui.CreateKeyHint(_shortcutPanel, "WASD", "Pan camera");
        _ui.CreateKeyHint(_shortcutPanel, "Z / X", "Zoom in / out");
        _ui.CreateKeyHint(_shortcutPanel, "Wheel", "Switch tool");
        _ui.CreateKeyHint(_shortcutPanel, "Ctrl+Z", "Undo last placement");
        _ui.CreateKeyHint(_shortcutPanel, "F5", "Reset room");
        _ui.CreateKeyHint(_shortcutPanel, "F4", "Close editor");
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

        // Rounded, like every panel the game itself draws.
        var img = go.AddComponent<Image>();
        img.sprite = MapEditorUI.RoundedPlate;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1.6f;
        img.color = new Color(0f, 0f, 0f, 0.62f);

        RegisterUiBlocker(rt);
        return rt;
    }

    // Inline text entry driven straight off Input.inputString rather than a TMP_InputField: text
    // fields route through the EventSystem, which this game hands to Rewired's pointer module,
    // whereas raw keyboard input is known to work here. Generic so any tool can prompt for text.
    // Text entry is modal, and it has to be: MMButton derives from Unity's Button, so it handles
    // ISubmitHandler, and this game's input module raises submit from the interact key. Typing an
    // "e" into a map name therefore re-pressed whatever button was focused - usually Rename, the
    // one that opened the prompt. Clearing the selection was not enough, because focus comes back.
    // Suspending the whole EventSystem for the duration is what actually holds: the raw
    // Input.inputString read below does not need it, and Enter or Escape always gets out.
    private bool _eventSystemSuspended;

    private void SuspendEventSystem()
    {
        if (_eventSystemSuspended || EventSystem.current == null) return;

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.enabled = false;
        _eventSystemSuspended = true;
    }

    private void RestoreEventSystem()
    {
        if (!_eventSystemSuspended) return;
        _eventSystemSuspended = false;

        if (EventSystem.current != null) EventSystem.current.enabled = true;
    }

    public void PromptText(string label, string initial, System.Action<string> onDone)
    {
        _renaming = true;
        SuspendEventSystem();
        _promptLabel = label;
        _nameBuffer = initial ?? "";
        _promptDone = onDone;
        UpdateNameLabel();
        SetStatus($"Type a {label} - Enter confirms, Escape cancels.");
    }

    private void BeginRename()
    {
        PromptText("map name", Map.MapName, value =>
        {
            Map.MapName = string.IsNullOrWhiteSpace(value) ? "UntitledMap" : value.Trim();
            SetStatus("Name: " + Map.MapName);
        });
    }

    private void HandleRenameInput()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            _renaming = false;
            RestoreEventSystem();
            var done = _promptDone;
            _promptDone = null;
            done?.Invoke(_nameBuffer);
            UpdateNameLabel();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _renaming = false;
            RestoreEventSystem();
            _promptDone = null;
            UpdateNameLabel();
            SetStatus("Cancelled.");
            return;
        }

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

        UpdateNameLabel();
    }

    private void UpdateNameLabel()
    {
        if (_titleText == null) return;
        _titleText.text = _renaming
            ? _promptLabel + ": " + _nameBuffer + "_"
            : TitleText;
    }

    // Save asks for the name through the game's own naming dialog, which is where the overwrite
    // warning lives now - it can say what will be replaced while the name is still being typed,
    // instead of the old press-Save-twice guard that only spoke after the fact.
    private void SaveMap()
    {
        // Four-door rule: a level drops this room into whatever slot the generated walk hands
        // it, and the walk decides which sides need a door. A blueprint missing one has no door
        // where the level needs it, and the run dead-ends - so it cannot be saved incomplete.
        // Checked before the dialog opens: no point naming a map that cannot be written.
        var doorTool = GetTool<DoorTool>();
        var missing = doorTool?.MissingDirections();
        if (missing != null && missing.Count > 0)
        {
            SetStatus($"Cannot save: missing {string.Join(", ", missing)} door(s). " +
                      "Use the Door tool's 'Enable All Doors'.", StatusSeverity.Error);
            Plugin.Log.LogWarning($"MapEditor: save blocked - '{Map.MapName}' is missing " +
                                  $"{string.Join(", ", missing)} door(s). All four are required.");
            return;
        }

        MapNamePrompt.Show(this, Map.MapName, "Save Map", name =>
        {
            Map.MapName = string.IsNullOrWhiteSpace(name) ? "UntitledMap" : name.Trim();
            UpdateNameLabel();
            WriteMap();
        });
    }

    private void WriteMap() => StartCoroutine(WriteMapRoutine());

    // Saving used to happen entirely inside one frame: sweep the room, resolve every object to a
    // prefab key, serialise, write, then screenshot and PNG-encode the whole screen. That is a
    // very visible hitch. The work is the same, but it is now spread over several frames with
    // the file writes handed to a thread, so no single frame carries all of it.
    private IEnumerator WriteMapRoutine()
    {
        SetStatus("Saving...");
        yield return null;

        Map.SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // Snapshot live tool state into the map immediately before writing.
        foreach (var tool in _tools.OfType<IMapDataContributor>())
            tool.ContributeTo(Map);

        yield return null;

        // Then everything the tools do not own: the full-room prop snapshot.
        RoomSnapshot.Collect(Map, this);

        yield return null;

        var write = MapEditorSerialization.SaveAsync(Map);
        while (!write.IsCompleted) yield return null;

        var path = write.Result;
        if (path == null)
        {
            SetStatus("Save failed, see log.", StatusSeverity.Error);
            yield break;
        }

        yield return CaptureSnapshot();
    }

    // Captures the room with every piece of editor chrome hidden and writes it next to the
    // json as <mapname>.png - the stable pairing a future preview UI reads. Exiting the active
    // tool is what clears its handles, gizmos and cursor previews from the world.
    private IEnumerator CaptureSnapshot()
    {
        var tool = _activeTool;
        tool?.OnExit();
        _canvas.enabled = false;

        // The screen must actually render a frame without the UI before it is read back.
        yield return new WaitForEndOfFrame();

        byte[] png = null;
        try
        {
            var full = ScreenCapture.CaptureScreenshotAsTexture();

            // Downscaled before encoding. A full-resolution PNG of the whole screen is several
            // megabytes and the encode alone is most of the old hitch - and the preview is drawn
            // at 168px in the load grid, so none of that resolution was ever visible.
            var scaled = Downscale(full, SnapshotWidth);
            Destroy(full);

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

            // Pure System.IO, so it is safe off the main thread.
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

    // Preview width; height follows the screen's aspect.
    private const int SnapshotWidth = 512;

    private static Texture2D Downscale(Texture2D source, int width)
    {
        if (source == null || source.width <= width) return source;

        var height = Mathf.Max(1, Mathf.RoundToInt(width * (float)source.height / source.width));

        // Through the GPU: a bilinear blit costs nothing next to resampling on the CPU.
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

    // Re-enters the dungeon, regenerating the room from scratch. This is the undo story, and it
    // discards unsaved edits, so it takes two presses to fire. The arming lapses after a few
    // seconds so a stray first click cannot leave it primed indefinitely.
    // F5 while the editor is open. Outside it, F5 still means "enter the test dungeon".
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
        CustomDungeonManager.CustomDungeonList.Values.ElementAt(0).EnterDungeon();
    }

    private void DisarmReset() => _resetArmed = false;
}

// The world's interaction handling has no business running while the editor owns the screen.
//
// Interactor.Update reads the interact key every frame, so typing a map name fired the game's
// "E" action - opening a prompt over the editor and swallowing the keystroke before the rename
// buffer ever saw it. Nothing in the room is meant to be interactive during editing anyway, and
// the same suppression keeps its build-placement branch (which dereferences a PlacementRegion
// that dungeons do not have) from running at all.
[HarmonyLib.HarmonyPatch(typeof(Interactor), "Update")]
internal static class Interactor_Update_Patch
{
    private static bool Prefix() =>
        RuntimeMapEditor.Active == null || !RuntimeMapEditor.Active.IsEditing;
}

// How loudly the status bar should say something.
public enum StatusSeverity
{
    Info,
    Success,
    Warning,
    Error
}

// Tools that own state which must end up in the saved map implement this.
public interface IMapDataContributor
{
    void ContributeTo(CTNodeBlueprint map);
}
