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

// Editor host; see MapEditor/README.md.
public class RuntimeMapEditor : MonoBehaviour, IMapEditorHost
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

    private RectTransform _shortcutPanel;

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

    // Hover widgets reach the status bar through this; one editor host per scene.
    public static RuntimeMapEditor Active { get; private set; }

    // The editor is normally created for dungeon scenes by the plugin. A hub or base session is the
    // other way one comes into being: the room it edits is already standing, so the session stands
    // the host up beside it and it goes away with the scene.
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

        // A level run survives scene reload as static state; re-bind it to the new host.
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

    // Back to the tool the editor opens on. For a tool that is a screen rather than a panel,
    // closing the screen means leaving the tool: there is nothing behind it to come back to.
    public void SelectFirstTool() => SelectTool(_tools.FirstOrDefault());

    // Which of the three rooms the editor is standing in. Everything that differs between them -
    // which tools are on the dock, what a save writes, what the load browser lists - asks this
    // rather than testing one session's flag and quietly meaning "not the other one".
    public static EditorContext Context =>
        BaseSession.Active ? EditorContext.Base
        : HubSession.Active ? EditorContext.Hub
        : EditorContext.Dungeon;

    // A hub is a safe town in the game's own base: nothing spawns there, no weapon podium belongs
    // there, it has no doors to a next room, and it is not part of a level or a dungeon graph.
    //
    // The base is all of that and one thing more: it is somewhere that already exists and is not
    // ours to replace. Clearing it and loading a room over it are the two gestures that would do
    // exactly that, so neither is offered - a base session only ever adds to what is standing, and
    // its one file is the slot's own.
    //
    // The tools are still built in every context - the loader and the clear sweeps ask for them by
    // type - they just have no place on the dock or in the wheel.
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

    // Any change to the room that a save would capture. Counted rather than compared against the
    // last written file, which is what the dungeon and world editors do: their maps are small data
    // objects, but a room's save is a multi-frame collection that rewrites the blueprint from the
    // live scene, and running that to answer a question would be a save in all but name.
    //
    // The undo stack raises this for everything that pushes an entry, which is every placement and
    // removal in the editor. The tools that change the room without one - transforms, doors,
    // lighting, shapes, clears - call it themselves.
    public void MarkEdited() => _edits++;

    // The room now matches what is on disk: at open, after a save, and after a load.
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

    // One shared loader so the IsLoading guard covers every consumer.
    private BlueprintLoader _loader;
    public BlueprintLoader Loader => _loader ??= new BlueprintLoader(this);

    public void AdoptBlueprint(CTNodeBlueprint bp)
    {
        if (bp == null) return;
        Map = bp;
        UpdateNameLabel();
    }

    // Handed an emptied Woolhaven by HubSession: the editor opens on it under the hub's name, and
    // the save writes the hub record beside the blueprint.
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

    // Handed the player's own base by BaseSession. Nothing is cleared and nothing is rebuilt: the
    // base is already standing, and what the editor holds is only the difference between it and
    // what this save slot's author has added to it. That difference was applied on arrival, so the
    // tools are already tracking every object in it and a save reads back what is on screen.
    public void BeginBaseEditing()
    {
        var delta = BaseDelta.Content;

        // Named after the file it will be written to, so the title bar says which save slot is being
        // edited rather than "Untitled".
        delta.MapName = $"base_slot{BaseDelta.Slot}";
        AdoptBlueprint(delta);

        if (!_editing) EnterEditorMode();

        SetStatus("Editing the base. Save writes this slot's own file; the game's save is not " +
                  "touched.");
    }

    // The walk-in entry runs on scaled time; the open editor would re-freeze timeScale.
    public void ExitForPlayback()
    {
        if (_editing) ExitEditorMode();
    }

    private void OnDestroy()
    {
        // The canvas is its own GameObject; without this it leaks one per dungeon entry.
        if (_canvasGO != null) Destroy(_canvasGO);
        if (_previewBadgeGO != null) Destroy(_previewBadgeGO);
        if (_editing) RestoreGameState();

        // Scene-harvested sprites die with the scene; a stale cache hands out destroyed sprites.
        MapEditorIcons.ClearSceneScopedCache();
        EnemyThumbnails.ClearSceneScopedCache();

        // Session-owned static state; left set, it bricks the next session (latched modal count,
        // stranded sequence owner, stale lighting table).
        MapNamePrompt.ResetModalState();
        Tools.CTMapTrigger.ResetSequenceState();
        if (!LevelPlayback.Active) Tools.LightingTool.ForgetRoomLighting();

        // A lock left behind by a prompt the scene change interrupted would take every menu in the
        // game down with it until a restart.
        LockNavigator(false);

        if (Active == this) Active = null;
    }

    public void ToggleEditor()
    {
        if (_canvas == null || ModalOpen) return;

        if (!_editing)
        {
            // The host outlives its session in the base's scene: it is what applies a saved base
            // delta on arrival, and it is still there once a hub has been played. F4 must not turn
            // that into a way into the editor from anywhere - a base is edited from the F7 panel,
            // where the guards that decide whether editing it is safe are.
            if (BaseSession.SceneOwnsSessions && Context == EditorContext.Dungeon)
            {
                Plugin.Log.LogInfo("MapEditor: F4 does nothing here - open the base editor from the " +
                                   "F7 panel, or enter a hub.");
                return;
            }

            EnterEditorMode();
            return;
        }

        // Pressing F4 again while the strip is up dismisses the question rather than answering it
        // for the author - the same step-back Esc gives on the dungeon screen.
        if (_confirm != null && _confirm.Open)
        {
            _confirm.Hide();
            SetStatus("Still open.");
            return;
        }

        // A tool with a screen of its own steps back out of that first: F4 closing the editor
        // outright would take an unsaved map with it without ever asking.
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

    // Escape is the other way out of the editor, and closing it is the *last* thing the key means
    // rather than the first. It steps back through what is actually on screen: an open dropdown,
    // then whatever mode the active tool put the editor into, then a tool's own full-screen view
    // (through ToggleEditor, which asks a screen tool to step back before it closes anything), and
    // only with none of those left does it reach the editor itself - where the unsaved-work guard
    // is waiting, exactly as it is for F4.
    //
    // Handled here rather than in each tool because it runs *before* the tool update: a tool that
    // polled Escape for itself would never see the key now, so the ones that need it say so through
    // IMapEditorEscapeHandler rather than racing for it.
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

    // "Close anyway", not "Discard": nothing is reverted and nothing can be. The room is the live
    // scene, and closing the editor only puts the panels away - every edit is still standing, F4
    // brings it all back, and playing the room plays the edited one. What goes unsaved is the file,
    // and that is what the status line says. A button labelled "Discard" would promise an undo the
    // editor cannot perform: there is no earlier room to go back to, only the one on screen.
    private void CloseAnyway()
    {
        ExitEditorMode();
        SetStatus("Closed without saving - the room keeps the changes, no file has them.",
            StatusSeverity.Warning);
    }

    // Set while a save is running on behalf of the close guard, so the editor shuts once the write
    // has actually landed rather than on the way to it - a save that is blocked or cancelled must
    // leave the editor open with the work still in it.
    private bool _closeAfterSave;

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(Map.MapName))
        {
            // Never named. The dialog is the only way to name it, and it closes the editor to show
            // itself; the close then happens when the write lands.
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

        // Straight to the write, past the quicksave's overwrite arming: "Save & close" under a name
        // already on the title bar *is* the confirmation that arming asks for.
        _closeAfterSave = true;
        _quickSavedName = Map.MapName;
        WriteMap();
    }

    private bool _chromeHidden;

    // The active tool while it has a screen of its own up; null the rest of the time.
    private IMapEditorScreenTool ScreenTool =>
        _activeTool is IMapEditorScreenTool { OwnsScreen: true } tool ? tool : null;

    // The editor's own furniture: title, dock, options, status, shortcuts. Collected rather than
    // named one by one - every one of them is a direct child of the canvas, built before anything
    // else is parented to it.
    private readonly List<GameObject> _ownChrome = [];
    private bool _ownChromeVisible = true;

    // Stood down for a tool that draws a screen of its own. The canvas itself stays on: the tool's
    // view hangs from it, so switching the canvas off (which is what F6 does) would take the map
    // with it.
    public void SetOwnChromeVisible(bool visible)
    {
        if (_ownChromeVisible == visible) return;
        _ownChromeVisible = visible;

        foreach (var go in _ownChrome)
            if (go != null) go.SetActive(visible);
    }

    // F6: the panels go away while the room stays frozen, so the scene can be framed and shot
    // without the editor in the picture. Closing the editor restores them.
    public void ToggleChromeHidden()
    {
        if (_canvas == null || !_editing || ModalOpen) return;

        // Nothing to hide behind a full-screen tool, and hiding it would switch off the canvas
        // that tool's own view is drawn on.
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

    // The one thing left on screen while the chrome is hidden: without it there is nothing to say
    // the room is frozen mid-edit, or which key brings the editor back.
    private void ShowPreviewBadge(bool visible)
    {
        if (visible && _previewBadgeGO == null) BuildPreviewBadge();
        if (_previewBadgeGO != null) _previewBadgeGO.SetActive(visible);
    }

    private void BuildPreviewBadge()
    {
        // Its own canvas: the editor's is switched off wholesale, and a label hanging from it
        // would go dark with everything else.
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

        // Bottom left, where the shortcut list sits when the chrome is up.
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

        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Hide(true, 0);

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        TakeCameraControl();

        GetTool<ShapeTool>()?.PrepareForLoad();

        // No baseline is taken here, and that is deliberate. The count starts clean, so opening the
        // editor to look at a room and closing it again asks nothing on its own. Re-baselining on
        // every open would instead mean the editor *forgot*: close an edited room with "Close
        // anyway", open it again, close it again, and it would go quietly - even though the room
        // still holds work no file has. Only a save and a load make the room match a file, so only
        // they move the baseline.
        SelectTool(_tools.FirstOrDefault());
        SetStatus("Editor open.");
    }

    private void ExitEditorMode()
    {
        // Same rule as switching tools: closing the editor ends the typing rather than abandoning
        // it half-entered, so the field is not still wearing a caret when the editor comes back.
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
        // Time first: the HUD's show animation needs a running clock.
        Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;
        ReleaseCameraControl();
        if (HUD_Manager.Instance != null) HUD_Manager.Instance.Show(0, true);
    }

    // Game menus restore timeScale on close; tools that open one call this after it is gone.
    public void ReassertPause()
    {
        if (_editing) Time.timeScale = 0f;
    }

    // Without an EventSystem every click is silently swallowed.
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

        // Game menus restore timeScale on close, un-pausing under the open editor.
        if (Time.timeScale != 0f) Time.timeScale = 0f;

        if (_resetArmed && Time.unscaledTime - _resetArmedAt > ResetArmWindow) DisarmReset();

        if (_renaming)
        {
            HandleRenameInput();
            return;
        }

        // Safety net: a navigator lock stranded by a prompt that ended some other way would take
        // every menu in the game down with it.
        ReleaseTypingLocks();

        if (Input.GetKeyDown(KeyCode.Escape) && HandleEscape()) return;

        if (CtrlHeld && Input.GetKeyDown(KeyCode.Z)) UndoLast();

        if (CtrlHeld && Input.GetKeyDown(KeyCode.S))
        {
            // A tool holding the screen is showing something of its own; Ctrl+S belongs to that,
            // not to the room hidden behind it.
            if (ScreenTool != null) ScreenTool.ScreenQuickSave();
            else QuickSave();
        }

        // Chrome hidden: the room is being looked at, not edited. The camera still pans so the
        // shot can be framed, but no tool acts on a click.
        if (_chromeHidden)
        {
            HandleCameraControls();
            return;
        }

        // The camera keys would pan the room behind a full-screen tool, and the wheel would switch
        // out of it - neither means anything while its own map has the screen.
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
            // Throttled: a broken tool throws again every frame.
            if (Time.unscaledTime >= _nextUpdateErrorAt)
            {
                _nextUpdateErrorAt = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"MapEditor: tool '{_activeTool?.Name}' update failed: " + e);
            }
        }
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
            // The axis is not defined in this build's input manager; stop asking.
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

    // Returns true when the wheel was consumed by an editor list, so it does not also switch tools.
    private bool ScrollUiUnderPointer(float delta)
    {
        if (_canvasGO == null) return false;

        var mouse = (Vector2)Input.mousePosition;
        var scrollRects = _canvasGO.GetComponentsInChildren<ScrollRect>(false);

        // Back to front: an open dropdown is parented last and must win.
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

        // Start from wherever the game's zoom actually is, so opening the editor never jumps.
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

    // Projects a screen point onto the z=0 world plane; correct for ortho and perspective cameras.
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

    // The follow anchor, not the camera transform - the camera sits back along the rig's angle.
    public Vector3 CameraFocus =>
        _cameraAnchor != null ? _cameraAnchor.transform.position : Vector3.zero;

    // Moves the follow anchor, not the camera - the rig overwrites the camera next frame.
    public void MoveCameraTo(Vector3 worldPosition)
    {
        if (_cameraAnchor == null) return;
        _cameraAnchor.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
    }

    private float _worldClickBlockedUntil;

    public void BlockWorldClicks() => _worldClickBlockedUntil = Time.unscaledTime + 0.2f;

    // For a tool that runs its own full-screen surface: PointerOverUi is true everywhere inside one
    // (the backdrop is a blocker), so it cannot tell a widget press from a click on the surface.
    // This is the half of it that still can - every widget calls BlockWorldClicks when pressed.
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

    public void SetStatus(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        _statusMessage = message;
        _statusSeverity = severity;
        ApplyStatus(message, severity, pulse: true);
    }

    // Last real message, restored when hover text clears.
    private string _statusMessage = "";
    private StatusSeverity _statusSeverity = StatusSeverity.Info;

    // A tool holding the screen has a status bar of its own; this one is switched off behind it, so
    // a widget's hover line would be written where nobody can read it.
    public void ShowHoverStatus(string message)
    {
        if (ScreenTool != null) ScreenTool.ScreenHoverStatus(message);
        else ApplyStatus(message, StatusSeverity.Info, pulse: false);
    }

    public void ClearHoverStatus()
    {
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
            _statusPanel.color = new Color(0f, 0f, 0f, urgent ? 0.78f : 0.62f);
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

        // Leaving a tool ends whatever it was having typed into it, exactly as Enter would. The
        // panels stay live while a prompt is open - that is what lets a search result be hovered
        // and clicked - so the dock is reachable mid-word, and walking away used to leave the
        // editor still in text entry with a field nobody could see any more: keystrokes went on
        // being eaten by a search box belonging to the tool just left.
        ConfirmPrompt();

        _activeTool?.OnExit();
        _activeTool = tool;

        // An open dropdown would outlive the previous tool's panel and keep absorbing clicks.
        _ui.CloseTransientUi();

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

        // Without scaling the UI is unreadably small at 4K.
        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        // Attach before building: overlays parent to the canvas root, icon fills need a coroutine host.
        _ui.Attach(this, _canvasGO.GetComponent<RectTransform>());

        CreateTitle();
        CreateDock();
        CreateOptionsPanel();
        CreateStatusBar();
        CreateShortcutPanel();

        // Everything on the canvas at this point is the editor's own furniture; anything parented
        // later belongs to a tool and must survive the chrome standing down.
        _ownChrome.Clear();
        foreach (Transform child in _canvas.transform) _ownChrome.Add(child.gameObject);

        // Built after the sweep, and deliberately: the chrome is put back with SetActive(true) on
        // everything it collected, which would raise a hidden strip along with it. It is only ever
        // shown by F4 in the room editor, which a screen tool intercepts first, so it has no reason
        // to stand down with the rest.
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

    // Clear of the dock and the status bar, which stand on this same screen: a question behind them
    // reads as nothing happening at all.
    private const float ConfirmBottom = DockHeight + 20f + 46f + 12f;

    private const float ToolIconSize = 72f;
    private const int DockPadding = 8;

    private const float DockHeight = ToolIconSize + DockPadding * 2;

    // Resolved once the fitter has run; the status bar sits on top of the dock and matches it.
    private float _dockWidth = 600f;

    private RectTransform _dock;
    private EditorContext _dockContext;

    // The dock is built once at construction, and the context can change under it: the host that
    // applies a saved base on arrival is stood up before anybody has opened the base editor, so its
    // dock was laid out for a dungeon. Rebuilt on the way in whenever the two disagree.
    private void RefreshDockForContext()
    {
        if (_dock == null || _dockContext == Context) return;

        foreach (Transform child in _dock) Destroy(child.gameObject);
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
        layout.padding = new RectOffset(DockPadding, DockPadding, DockPadding, DockPadding);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        PopulateDock(dock);

        // Horizontal only: a vertical fit collapses the plate for a frame before icons report sizes.
        var fitter = dock.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Resolve now: the status bar built next sizes itself off _dockWidth.
        LayoutRebuilder.ForceRebuildLayoutImmediate(dock);
        _dockWidth = dock.rect.width;
    }

    // The icons themselves, in whichever set this context has. The Save button rides beside the
    // Load tool where there is one; in the base there is no load browser - the slot's file is the
    // only one - so it stands on its own at the end.
    private void PopulateDock(RectTransform dock)
    {
        _dockContext = Context;

        foreach (var tool in DockTools())
        {
            var captured = tool;
            _ui.CreateIconButton(dock, MapEditorIcons.GetToolIcon(tool.Name), tool.Name,
                () => SelectTool(captured), out var ring, ToolIconSize, hoverText: tool.Name);
            _toolRings[tool.Name] = ring;

            // The dungeon half ends at the doors - or, with no door tool on the dock, at the one
            // before the lighting tool.
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
    // Raised to clear a boxed icon grid at its full height plus the search field, group picker and
    // caption around it: at 820 the column those sit in started scrolling, which is the very thing
    // boxing the cells was meant to stop. 12 from the top edge plus this still leaves the bottom of
    // the screen clear, and it matches the world and dungeon editors' own panels.
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

        RectTransform column = null;
        if (!_optionsCollapsed && _activeTool != null)
            _optionColumns.TryGetValue(_activeTool.Name, out column);

        // Three frames: Destroy defers to end of frame and staggered fills may still add cells.
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

        // A child Graphic always draws over its parent's, so an outline rather than a backing plate.
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

        // Invisible container, but still a click blocker.
        RegisterUiBlocker(_shortcutPanel);
    }

    private bool _shortcutsCollapsed;

    private void ToggleShortcutsCollapsed()
    {
        _shortcutsCollapsed = !_shortcutsCollapsed;
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

            // Always last: these work in every tool.
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

        var img = go.AddComponent<Image>();
        img.sprite = MapEditorUI.RoundedPlate;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1.6f;
        img.color = new Color(0f, 0f, 0f, 0.62f);

        RegisterUiBlocker(rt);
        return rt;
    }

    // Typing used to disable the EventSystem outright, and that was wrong twice over.
    //
    // It never came back on. `EventSystem.current` is the first *enabled* EventSystem in the
    // scene, so switching off the only one makes `current` null - and the restore path asked for
    // `current` again, found nothing, and switched nothing back on. The panel stayed dead until
    // the scene changed. That is the "search never gives control back" bug.
    //
    // And a search field wants its grid live underneath it: results are there to be hovered for a
    // preview and clicked to pick one, and with the EventSystem off neither reached the panel.
    //
    // What actually had to be shut out was never the EventSystem. It was UINavigatorNew, which
    // polls Rewired's accept binding from its own Update and confirms whatever selectable it is
    // holding (LockNavigator, below), and the input module's submit, which fires at whatever the
    // EventSystem has selected - so the selection is cleared for as long as a prompt is open, and
    // a key press has nothing to land on. Mouse clicks never needed a selection and keep working.
    private static void ClearUiSelection()
    {
        var events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject != null)
            events.SetSelectedGameObject(null);
    }

    private void ReleaseTypingLocks() => LockNavigator(false);

    private bool _navigatorLocked;

    // The other half of keeping the game's UI out of a typed field. Suspending the EventSystem is
    // not enough on its own: UINavigatorNew polls Rewired's accept and cancel bindings from its own
    // Update and confirms whatever it holds as the current selectable, which never touches the
    // EventSystem. LockInput is the switch the game itself uses to gate that block.
    //
    // Only ever set back to false if we were the ones who set it: a stranded lock kills every menu
    // in the game until a restart, and blindly clearing it each frame would undo a lock the game
    // had taken for its own reasons.
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

    // Reads a line of text with the game's own UI shut out of it. onChanged fires on every
    // keystroke, so a caller can filter a list as it is typed; onDone on Enter and onCancelled on
    // Escape. inTitle draws the buffer into the editor's title bar, which suits a rename; a caller
    // drawing its own field passes false and paints from onChanged.
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

    // Ends an open prompt exactly as Enter does. Picking something out of a list the prompt was
    // filtering says "this one" as plainly as the key does, so a caller can finish the typing on
    // the user's behalf instead of making them press Enter and then click.
    // Ends an open prompt as Escape does, for a caller that is taking the prompt's own UI away.
    //
    // Typing locks the game's navigator and clears the EventSystem's selection every frame, and the
    // only things that used to lift those locks were Enter and Escape - both of which need the
    // prompt to still be on screen to be pressed. Closing a screen out from under a live search
    // therefore left the editor typing into a field that no longer existed, with input locked and
    // nothing visible to explain it.
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
        // Every frame, not just on open: a click on a live panel selects what it hit, and the next
        // keystroke would be delivered to it as a submit.
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

    // Ctrl+S: the same save without the dialog, under the name already on the title bar. The first
    // press that would land on a file this session did not write only warns - the bar goes orange
    // and the press is armed - so a quicksave can never silently clobber somebody else's map.
    private bool _quickSaveArmed;
    private float _quickSaveArmedAt;
    private string _quickSavedName;

    private const float QuickSaveArmWindow = 5f;

    private void QuickSave()
    {
        if (_renaming || ModalOpen) return;

        // The base has one file and the slot names it, so there is nothing to arm against: a
        // quicksave cannot land on somebody else's map when there is only ever one of them.
        if (Context == EditorContext.Base)
        {
            SetStatus("Saving the base...");
            WriteMap();
            return;
        }

        if (string.IsNullOrWhiteSpace(Map.MapName))
        {
            // Nothing to quicksave under; the dialog is the only way to name it.
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

    // Everything that makes a save impossible, as one message or null. Shared so the dialog save,
    // the quicksave and the close guard cannot drift apart on what they refuse.
    private string SaveBlock()
    {
        // A hub is a town room, not a dungeon room: it has no doors to a next room, and the four
        // the check wants do not exist in Woolhaven at all. Nor in the base.
        var doorTool = Context == EditorContext.Dungeon ? GetTool<DoorTool>() : null;
        var missing = doorTool?.MissingDirections();
        if (missing != null && missing.Count > 0)
            return $"Cannot save: missing {string.Join(", ", missing)} door(s). " +
                   "Use the Door tool's 'Enable All Doors'.";

        return HubSaveBlock();
    }

    // What a dungeon room's four doors are to a hub: a town room has no doors, but it does have to
    // say where the player lands. Without a spawn point the arrival falls back to the town's own
    // door - a transform the sweep took away - so a hub is not allowed to be saved without one.
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

        // The base has one file per save slot and the slot already names it. There is nothing to
        // ask, so the dialog - and the editor closing to show it - is skipped entirely.
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

        // The base is saved as a difference, never as a picture. A full sweep there would write down
        // the player's entire town - every building, tree and follower hut - and the apply pass would
        // then rebuild a second copy of it on top of the real one. What the tools just contributed is
        // exactly what this session added; the journal beside it is what it took away or moved.
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
                // A failed write must leave the editor open with the work still in it.
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

        // Full-room prop snapshot: everything the tools do not own.
        RoomSnapshot.Collect(Map, this);

        yield return null;

        var write = MapEditorSerialization.SaveAsync(Map);
        while (!write.IsCompleted) yield return null;

        var path = write.Result;
        if (path == null)
        {
            // A failed write must leave the editor open with the work still in it.
            _closeAfterSave = false;
            SetStatus("Save failed, see log.", StatusSeverity.Error);
            yield break;
        }

        // This session wrote it, so Ctrl+S under this name is no longer clobbering anyone.
        _quickSavedName = Map.MapName;
        MarkSaved();

        // Saving in a hub session saves a hub: the record beside the blueprint is what the world
        // map's Hub picker lists and what playback rebuilds. It follows the name the save used, so
        // saving under a new name makes that the hub.
        if (Context == EditorContext.Hub)
        {
            HubSession.WriteRecord(Map.MapName, Map.MapName);
            SetStatus($"Saved hub '{Map.MapName}'. World map nodes can target it as a Hub.",
                StatusSeverity.Success);
        }

        yield return CaptureSnapshot();

        // The close the guard was holding: the write has landed, so there is nothing left to lose.
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

        // The screen must actually render a frame without the UI before it is read back.
        yield return new WaitForEndOfFrame();

        byte[] png = null;
        try
        {
            var full = ScreenCapture.CaptureScreenshotAsTexture();

            // Downscale may return its input unchanged; guard against destroying the same texture twice.
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
    //
    // 1080p, deliberately, and it is a ceiling rather than a target: Downscale never enlarges, so a
    // 1080p screen is written as it is and only a larger one is brought down to this. Going higher
    // was tried and reverted - it costs every author disk and memory for a sharpness only the
    // minority on a bigger monitor would ever see, and a snapshot is a picture of a room, not the
    // room. The earlier 512 and 1280 were each sized for the largest use at the time (a grid cell,
    // then a hover preview) and each outgrown; this one is sized for the common screen instead.
    //
    // Snapshots already on disk keep the width they were taken at - saving a map again is what
    // re-takes it.
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

// Which room the editor has been opened on. Not a mode the author picks: it follows from where the
// session was started, and every tool reads it rather than deciding for itself.
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

// Tools that own state which must end up in the saved map implement this.
public interface IMapDataContributor
{
    void ContributeTo(CTNodeBlueprint map);
}
