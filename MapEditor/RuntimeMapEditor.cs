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
// add a enemy placement tool
// add a weapon podium placement tool
// add a trigger collider volume placement tool that can be used to trigger events in the map
// add a npc placement tool
// improve the UI so that it is less wordy and more intuitive
// fix the zoom issue, each time a structure is selected, the zoom gets further
// cant zoom in or out yet with Z X
// check the increment for z axis data for each shape to stack on top of each other
// build menu should allow everything to be built, regardless of materials
// build menu needs to show the unbuildable items
// structures should be able to be cloned with CTRL click drag
public class RuntimeMapEditor : MonoBehaviour
{
    private Canvas _canvas;
    private GameObject _canvasGO;
    private RectTransform _toolOptionsPanel;
    private TMP_Text _statusText;

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

    private TMP_Text _nameLabel;
    private bool _renaming;
    private string _nameBuffer = "";

    private GameObject _resetButton;
    private bool _resetArmed;
    private float _resetArmedAt;
    private const string ResetLabel = "Reset Room";
    private const float ResetArmWindow = 5f;

    public MapData Map { get; } = new();
    public MapEditorUI UI => _ui;

    public bool IsEditing => _editing;

    private const float PanSpeed = 14f;

    private void Awake()
    {
        _ui = new MapEditorUI();
        BuildTools();
        CreateUi();
        _canvas.enabled = false;
    }

    private void BuildTools()
    {
        _tools.Add(new SelectTool(this));
        _tools.Add(new ShapeTool(this));
        _tools.Add(new StructureTool(this));
        _tools.Add(new DoorTool(this));
        _tools.Add(new ClearTool(this));
    }

    private void OnDestroy()
    {
        // The canvas lives on its own GameObject, so destroying the host is not enough.
        // Without this it leaks one canvas per dungeon entry.
        if (_canvasGO != null) Destroy(_canvasGO);
        if (_editing) RestoreGameState();
    }

    public void ToggleEditor()
    {
        if (_canvas == null) return;

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

        SelectTool(_tools.FirstOrDefault());
        SetStatus("Editor open. WASD/arrows pan, scroll zooms.");
    }

    private void ExitEditorMode()
    {
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
        // pressing them cycled the editor's own tool buttons instead of zooming. The scroll
        // wheel is consumed by Rewired's pointer module, so it cannot be the primary control
        // either, though it is still honoured when it reports anything.
        var zoomDelta = 0f;
        if (Input.GetKey(KeyCode.Z)) zoomDelta += 1f;
        if (Input.GetKey(KeyCode.X)) zoomDelta -= 1f;

        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f) zoomDelta -= scroll * 4f;

        if (Mathf.Abs(zoomDelta) > 0.001f)
        {
            _zoom = Mathf.Clamp(_zoom + zoomDelta * ZoomSpeed * dt, MinZoom, MaxZoom);
            CinematicCameraManager.Zoom(_zoom);
        }
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
    public bool PointerOverUi()
    {
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

    public void SetStatus(string message)
    {
        if (_statusText != null) _statusText.text = message;
    }

    private void SelectTool(IMapEditorTool tool)
    {
        if (tool == null || tool == _activeTool) return;

        _activeTool?.OnExit();
        _activeTool = tool;

        // Only the active tool's option column is visible.
        foreach (Transform child in _toolOptionsPanel)
            child.gameObject.SetActive(child.name == "Options_" + tool.Name);

        _activeTool.OnEnter();
        SetStatus(tool.Name + " tool active.");
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

        CreateToolbar();
        CreateOptionsPanel();
        CreateStatusBar();

        foreach (var tool in _tools)
        {
            var content = _ui.CreateScrollColumn(_toolOptionsPanel, "Options_" + tool.Name, out var root);
            tool.BuildPanel(content, _ui);
            root.SetActive(false);
        }
    }

    private void CreateToolbar()
    {
        // Scrollable, and tall enough that its buttons stay inside the panel rect. When the
        // toolbar overflowed, clicks on the lower buttons fell outside every registered UI
        // blocker and were treated as world clicks by whichever tool was active.
        var bar = CreatePanel("Toolbar", new Vector2(0f, 1f), new Vector2(240f, 760f), new Vector2(12f, -12f));
        var column = _ui.CreateScrollColumn(bar, "ToolbarScroll", out _);

        _ui.CreateLabel(column, "Map Builder", 22, TextAlignmentOptions.Center);

        foreach (var tool in _tools)
        {
            var captured = tool;
            _ui.CreateButton(column, tool.Name, () => SelectTool(captured));
        }

        _ui.CreateLabel(column, "—", 14, TextAlignmentOptions.Center);

        _nameLabel = _ui.CreateLabel(column, "Name: " + Map.MapName, 15, TextAlignmentOptions.Center)
            .GetComponent<TMP_Text>();
        _ui.CreateButton(column, "Rename Map", BeginRename);

        _ui.CreateButton(column, "Save Map", SaveMap);
        _resetButton = _ui.CreateButton(column, ResetLabel, ResetRoom);
        _ui.CreateButton(column, "Close (F4)", ToggleEditor);
    }

    private void CreateOptionsPanel()
    {
        // Wide enough for the cloned settings-menu rows, which are authored for the full-width
        // settings panel and squash their labels if given less.
        _toolOptionsPanel = CreatePanel("ToolOptions", new Vector2(1f, 1f), new Vector2(520f, 620f), new Vector2(-12f, -12f));
    }

    private void CreateStatusBar()
    {
        var bar = CreatePanel("StatusBar", new Vector2(0.5f, 0f), new Vector2(900f, 40f), new Vector2(0f, 12f));
        var label = _ui.CreateLabel(bar, "", 18, TextAlignmentOptions.Center);
        var rt = label.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _statusText = label.GetComponent<TMP_Text>();
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

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.72f);

        RegisterUiBlocker(rt);
        return rt;
    }

    // Inline rename driven straight off Input.inputString rather than a TMP_InputField: text
    // fields route through the EventSystem, which this game hands to Rewired's pointer module,
    // whereas raw keyboard input is known to work here.
    private void BeginRename()
    {
        _renaming = true;
        _nameBuffer = Map.MapName ?? "";
        UpdateNameLabel();
        SetStatus("Type a map name. Enter confirms, Escape cancels.");
    }

    private void HandleRenameInput()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            Map.MapName = string.IsNullOrWhiteSpace(_nameBuffer) ? "UntitledMap" : _nameBuffer.Trim();
            _renaming = false;
            UpdateNameLabel();
            SetStatus("Map name set to '" + Map.MapName + "'.");
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _renaming = false;
            UpdateNameLabel();
            SetStatus("Rename cancelled.");
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
        if (_nameLabel == null) return;
        _nameLabel.text = _renaming ? "Name: " + _nameBuffer + "_" : "Name: " + Map.MapName;
    }

    private void SaveMap()
    {
        Map.SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // Snapshot live tool state into the map immediately before writing.
        foreach (var tool in _tools.OfType<IMapDataContributor>())
            tool.ContributeTo(Map);

        var path = MapEditorSerialization.Save(Map);
        SetStatus(path != null ? "Saved to " + path : "Save failed, see log.");
    }

    // Re-enters the dungeon, regenerating the room from scratch. This is the undo story, and it
    // discards unsaved edits, so it takes two presses to fire. The arming lapses after a few
    // seconds so a stray first click cannot leave it primed indefinitely.
    private void ResetRoom()
    {
        if (CustomDungeonManager.CustomDungeonList.Count == 0)
        {
            SetStatus("No custom dungeon registered to reset into.");
            return;
        }

        if (!_resetArmed)
        {
            _resetArmed = true;
            _resetArmedAt = Time.unscaledTime;
            SetResetLabel("Confirm Reset?");
            SetStatus("Reset discards unsaved edits. Press again to confirm.");
            return;
        }

        DisarmReset();
        SetStatus("Resetting room...");
        ExitEditorMode();
        CustomDungeonManager.CustomDungeonList.Values.ElementAt(0).EnterDungeon();
    }

    private void DisarmReset()
    {
        _resetArmed = false;
        SetResetLabel(ResetLabel);
    }

    private void SetResetLabel(string text)
    {
        if (_resetButton == null) return;
        var label = _resetButton.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = text;
    }
}

// Tools that own state which must end up in the saved map implement this.
public interface IMapDataContributor
{
    void ContributeTo(MapData map);
}
