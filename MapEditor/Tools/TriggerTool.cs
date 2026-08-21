using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class CTMapTrigger : MonoBehaviour
{
    public string Id = "";

    // The free-text action name the tool stored before sequences existed. Kept for blueprint
    // compatibility; Actions is what actually runs.
    public string Action = "";

    public bool Once = true;
    public Vector2 Size = new(4f, 3f);

    // Played in order when a player steps in.
    public readonly List<TriggerAction> Actions = [];

    // Freeze the players for the sequence, except around actions that need their input.
    public bool LockPlayerControl = true;

    // Fired when a player enters. Nothing subscribes yet.
    public static event System.Action<CTMapTrigger> Entered;

    // Every live volume, so the tool can retint or re-show them all without owning the list.
    public static readonly List<CTMapTrigger> All = [];

    // Keeps the volumes drawn after the editor closes. On by default: with no behaviours wired up
    // yet, seeing the box light up is the only way to tell a trigger works.
    public static bool ShowInPlay = true;

    public bool Tripped { get; private set; }

    private BoxCollider2D _box;
    private GameObject _gizmo;
    private LineRenderer _outline;
    private SpriteRenderer _fillRenderer;
    private Transform _fill;
    private bool _inside;
    private bool _toolVisible;
    private bool _highlighted;
    private float _flashUntil;

    private void Awake()
    {
        _box = gameObject.GetComponent<BoxCollider2D>();
        if (_box == null) _box = gameObject.AddComponent<BoxCollider2D>();
        _box.isTrigger = true;

        All.Add(this);
        Refresh();
    }

    private void OnDestroy() => All.Remove(this);

    // Vanilla's own test (TriggerCallback): the collider has to belong to a player.
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;
        if (other.GetComponent<PlayerFarming>() == null &&
            other.GetComponentInParent<PlayerFarming>() == null) return;

        if (_inside) return;
        _inside = Fire();
    }

    public Rect WorldRect
    {
        get
        {
            var centre = transform.position;
            return new Rect(centre.x - Size.x * 0.5f, centre.y - Size.y * 0.5f, Size.x, Size.y);
        }
    }

    // Re-applies the size to the collider and the gizmo. Called whenever the tool resizes or
    // moves the volume.
    public void Refresh()
    {
        Size = new Vector2(Mathf.Max(0.5f, Size.x), Mathf.Max(0.5f, Size.y));

        if (_box != null) _box.size = Size;
        if (_gizmo == null) return;

        if (_fill != null) _fill.localScale = new Vector3(Size.x, Size.y, 1f);
        if (_outline == null) return;

        var half = Size * 0.5f;
        var z = -0.05f;
        _outline.SetPositions([
            new Vector3(-half.x, -half.y, z),
            new Vector3(half.x, -half.y, z),
            new Vector3(half.x, half.y, z),
            new Vector3(-half.x, half.y, z)
        ]);
    }

    // Shown while the trigger tool is open, and - unless the tool says otherwise - during play
    // too, where it is the only sign a trigger did anything.
    public void ShowGizmo(bool toolVisible)
    {
        _toolVisible = toolVisible;
        ApplyVisibility();
    }

    public void ApplyVisibility()
    {
        var visible = _toolVisible || ShowInPlay;
        if (visible && _gizmo == null) BuildGizmo();
        if (_gizmo != null && _gizmo.activeSelf != visible) _gizmo.SetActive(visible);
        ApplyTint();
    }

    public void SetHighlighted(bool highlighted)
    {
        _highlighted = highlighted;
        ApplyTint();
    }

    private void ApplyTint()
    {
        if (_outline == null) return;

        var colour = Idle;
        if (_flashUntil > Time.unscaledTime) colour = Firing;
        else if (_highlighted) colour = Selected;
        else if (Tripped) colour = Spent;

        _outline.startColor = _outline.endColor = colour;
        if (_fillRenderer != null)
            _fillRenderer.color = new Color(colour.r, colour.g, colour.b,
                _flashUntil > Time.unscaledTime ? 0.35f : 0.12f);
    }

    private static readonly Color Idle = new(0.25f, 0.85f, 1f, 0.9f);
    private static readonly Color Selected = new(1f, 0.82f, 0.15f, 1f);
    private static readonly Color Firing = new(0.35f, 1f, 0.4f, 1f);
    private static readonly Color Spent = new(0.45f, 0.6f, 0.5f, 0.75f);

    private void BuildGizmo()
    {
        _gizmo = new GameObject("Gizmo");
        _gizmo.transform.SetParent(transform, false);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(_gizmo.transform, false);
        _fill = fill.transform;

        _fillRenderer = fill.AddComponent<SpriteRenderer>();
        _fillRenderer.sprite = Blank;
        _fillRenderer.color = new Color(0.25f, 0.85f, 1f, 0.12f);
        _fillRenderer.sortingOrder = 31990;

        var line = new GameObject("Outline");
        line.transform.SetParent(_gizmo.transform, false);

        _outline = line.AddComponent<LineRenderer>();
        _outline.useWorldSpace = false;
        _outline.loop = true;
        _outline.positionCount = 4;
        _outline.startWidth = _outline.endWidth = 0.09f;
        _outline.numCapVertices = 2;
        _outline.sharedMaterial = MapEditorGizmos.LineMaterial();
        _outline.startColor = _outline.endColor = Idle;
        _outline.sortingOrder = 31991;

        Refresh();
    }

    // One world unit square, so the fill scales straight from the trigger's size.
    private static Sprite _blank;

    private static Sprite Blank
    {
        get
        {
            if (_blank != null) return _blank;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                name = "CultTweaker_TriggerFill",
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            var pixels = new Color32[16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();

            _blank = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f),
                4f, 0, SpriteMeshType.FullRect);
            _blank.name = "CultTweaker_TriggerFill";
            _blank.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _blank;
        }
    }

    private void Update()
    {
        // Nothing fires while the map is being authored: the player is parked wherever the
        // editor was opened, often standing in the volume being drawn.
        if (RuntimeMapEditor.Active != null && RuntimeMapEditor.Active.IsEditing)
        {
            // Standing in it at F4 time must not count as an entry the moment play resumes.
            _inside = AnyPlayerInside();
            return;
        }

        var inside = AnyPlayerInside();

        if (inside && !_inside) _inside = Fire();
        else _inside = inside;

        // The flash has to be taken back down, and only this component knows when it lapses.
        if (_flashing && _flashUntil <= Time.unscaledTime)
        {
            _flashing = false;
            ApplyTint();
        }
    }

    private bool _flashing;

    private bool AnyPlayerInside()
    {
        var rect = WorldRect;

        var players = PlayerFarming.players;
        if (players != null)
            foreach (var player in players)
                if (Inside(rect, player)) return true;

        return Inside(rect, PlayerFarming.Instance);
    }

    private static bool Inside(Rect rect, PlayerFarming player)
    {
        if (player == null) return false;

        var position = player.transform.position;
        if (rect.Contains(new Vector2(position.x, position.y))) return true;

        var body = player.circleCollider2D;
        if (body == null || !body.enabled) return false;

        var bounds = body.bounds;
        return rect.Overlaps(new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y));
    }

    // Returns whether the entry was consumed. False means "not now" - the trigger stays armed and
    // un-entered, so the poll retries next frame.
    private bool Fire()
    {
        if (Once && Tripped) return true;

        if (Actions.Count > 0 && SequencePlaying) return false;

        Tripped = true;

        // Visible proof: with a trigger whose sequence is a single quiet action, the flash is
        // still the fastest way to tell a working volume from a broken one.
        _flashUntil = Time.unscaledTime + 1.2f;
        _flashing = true;
        ApplyTint();

        Plugin.Log.LogInfo($"MapEditor: trigger '{Id}' entered" +
                           (Actions.Count == 0 ? " (no actions)." : $" -> {Actions.Count} action(s)."));

        try
        {
            Entered?.Invoke(this);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: trigger '{Id}' handler failed: {e.Message}");
        }

        StartSequence();
        return true;
    }

    // One sequence at a time, globally - see Fire for why.
    private static CTMapTrigger _sequenceOwner;

    public static bool SequencePlaying => _sequenceOwner != null;

    private void StartSequence()
    {
        if (Actions.Count == 0 || _sequenceOwner != null) return;

        var host = RuntimeMapEditor.Active != null ? (MonoBehaviour)RuntimeMapEditor.Active : this;
        _sequenceOwner = this;
        host.StartCoroutine(SequenceRoutine());
    }

    private System.Collections.IEnumerator SequenceRoutine()
    {
        yield return TriggerActions.Run(this);

        _sequenceOwner = null;
    }

    // Called when the room is torn down: a sequence whose coroutine died with the scene would
    // otherwise leave the global owner set and block every trigger from then on.
    public static void ResetSequenceState()
    {
        if (_sequenceOwner == null) return;
        _sequenceOwner = null;
        TriggerActions.SetControl(true);
    }

    // Re-arms a one-shot trigger, for testing the same volume more than once per room.
    public void Rearm()
    {
        Tripped = false;
        _inside = false;
        _flashing = false;
        _flashUntil = 0f;
        ApplyTint();
    }
}

public class TriggerTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts
{
    public string Name => "Triggers";

    private readonly RuntimeMapEditor _editor;
    private readonly List<CTMapTrigger> _triggers = [];

    private CTMapTrigger _selected;
    private int _nextId = 1;

    private Canvas _handleCanvas;
    private GameObject _moveHandle;
    private GameObject _resizeHandle;

    private Slider _widthSlider;
    private Slider _heightSlider;
    private TMPro.TMP_Text _info;
    private bool _syncingSliders;

    private const float DefaultWidth = 4f;
    private const float DefaultHeight = 3f;

    public TriggerTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _ui = ui;
        _panel = panel;

        _info = ui.CreateLabel(panel, "No trigger selected", 17, TMPro.TextAlignmentOptions.Center)
            .GetComponent<TMPro.TMP_Text>();

        _widthSlider = ui.CreateSlider(panel, "Width", 0.5f, 40f, DefaultWidth, v => Resize(v, null))
            .GetComponentInChildren<Slider>();
        _heightSlider = ui.CreateSlider(panel, "Height", 0.5f, 40f, DefaultHeight, v => Resize(null, v))
            .GetComponentInChildren<Slider>();

        _onceToggle = ui.CreateToggle(panel, "Fire once", true, value =>
        {
            if (_syncingSliders || _selected == null) return;
            _selected.Once = value;
            _editor.SetStatus(value ? $"{_selected.Id} fires once." : $"{_selected.Id} fires every entry.");
        }).GetComponent<MapEditorToggle>();

        _lockToggle = ui.CreateToggle(panel, "Lock control while playing", true, value =>
        {
            if (_syncingSliders || _selected == null) return;
            _selected.LockPlayerControl = value;
            _editor.SetStatus(value
                ? $"{_selected.Id} freezes the players until an action needs them."
                : $"{_selected.Id} leaves the players in control.");
        }).GetComponent<MapEditorToggle>();

        // Grouped with the other two: it is a checkbox like they are, and it sat below the
        // action list where nothing else of its kind lived.
        ui.CreateToggle(panel, "Show volumes in play", CTMapTrigger.ShowInPlay, value =>
        {
            CTMapTrigger.ShowInPlay = value;
            foreach (var trigger in CTMapTrigger.All)
                if (trigger != null) trigger.ApplyVisibility();
            _editor.SetStatus(value ? "Volumes stay visible in play." : "Volumes hidden in play.");
        });

        ui.CreateButton(panel, "Re-arm All Triggers", () =>
        {
            var count = 0;
            foreach (var trigger in _triggers)
            {
                if (trigger == null) continue;
                trigger.Rearm();
                count++;
            }
            _editor.SetStatus($"Re-armed {count} trigger(s).");
        });

        ui.CreateButton(panel, "Delete Selected", DeleteSelected);

        // The label is kept: it is what says the button is armed.
        _clearAllLabel = ui.CreateButton(panel, ClearAllLabel, ClearAllPressed)
            .GetComponentInChildren<TMPro.TMP_Text>();

        ui.CreateHeader(panel, "- Actions -", 19);

        _addDropdown = ui.CreateDropdown(panel, "Add action", ActionLabels, OnAddActionType);

        _targetDropdown = ui.CreateDropdown(panel, "Target", System.Array.Empty<string>(), OnTargetChosen);

        // Rows are rebuilt whenever the list changes, so they live in their own container.
        _actionList = CreateActionListContainer(panel);

        RebuildActionList();
        UpdateActionControls();
    }

    private MapEditorToggle _onceToggle;
    private MapEditorToggle _lockToggle;

    // ---- clear-all confirmation ---------------------------------------------------------------

    private TMPro.TMP_Text _clearAllLabel;
    private float _armedUntil;

    private const string ClearAllLabel = "Clear All Triggers";
    private const float ArmWindow = 4f;

    // Two presses rather than a dialog: the panel has no modal of its own, and one stray click
    // that deletes every volume in the room is not recoverable - placing a trigger is on the undo
    // stack, but this wipe clears the stack along with the triggers.
    private void ClearAllPressed()
    {
        var live = LiveCount();
        if (live == 0)
        {
            Disarm();
            _editor.SetStatus("No triggers to remove.");
            return;
        }

        if (!Armed)
        {
            _armedUntil = Time.unscaledTime + ArmWindow;
            if (_clearAllLabel != null) _clearAllLabel.text = $"Delete all {live}? Click again";
            _editor.SetStatus($"Click again within {ArmWindow:0}s to delete all {live} trigger(s).",
                StatusSeverity.Warning);
            return;
        }

        Disarm();
        var removed = ClearPlaced();
        _editor.SetStatus($"Removed {removed} trigger(s).");
    }

    private bool Armed => _armedUntil > 0f && Time.unscaledTime <= _armedUntil;

    private void Disarm()
    {
        _armedUntil = 0f;
        if (_clearAllLabel != null) _clearAllLabel.text = ClearAllLabel;
    }

    // The window lapsing has to put the button's own wording back, so a stale "Click again" is
    // never what the next click answers.
    private void TickArmWindow()
    {
        if (_armedUntil <= 0f || Time.unscaledTime <= _armedUntil) return;

        Disarm();
        _editor.SetStatus("Clear all cancelled.");
    }

    private int LiveCount()
    {
        var live = 0;
        foreach (var trigger in _triggers)
            if (trigger != null) live++;

        return live;
    }

    // ---- action sequence editing -----------------------------------------------------------

    private MapEditorUI _ui;
    private RectTransform _panel;
    private RectTransform _actionList;
    private MapEditorDropdown _addDropdown;
    private MapEditorDropdown _targetDropdown;

    // The row the user clicked, whose target is outlined in the world.
    private TriggerAction _selectedAction;

    // The Add-action dropdown lists groups, not actions: there are enough actions now that one
    // flat list was longer than the panel. Index-aligned with ActionGroups; a group holding a
    // single action skips the submenu and starts it directly.
    private static readonly string[] ActionLabels =
    [
        "Player actions",
        "Camera actions",
        "Screen text",
        "Ambient actions",
        "Wait for seconds"
    ];

    private static readonly TriggerActionType[][] ActionGroups =
    [
        [
            TriggerActionType.MovePlayersToTrigger,
            TriggerActionType.MovePlayersToObject,
            TriggerActionType.StartConversation,
            TriggerActionType.PlayPlayerAnimation
        ],
        [
            TriggerActionType.CameraLookAtObject,
            TriggerActionType.CameraLookAtTrigger,
            TriggerActionType.CameraOffset,
            TriggerActionType.CameraOffsetReset,
            TriggerActionType.CameraZoom,
            TriggerActionType.CameraZoomReset,
            TriggerActionType.CameraEffect,
            TriggerActionType.PlayCutscene
        ],
        [
            TriggerActionType.ShowCaption,
            TriggerActionType.ShowTitleText,
            TriggerActionType.ShowFullscreenText
        ],
        [
            TriggerActionType.ApplyLighting,
            TriggerActionType.ChangeMusic
        ],
        [
            TriggerActionType.Wait
        ]
    ];

    // What a group's submenu calls each action.
    private static string ActionName(TriggerActionType type) => type switch
    {
        TriggerActionType.MovePlayersToTrigger => "Move players to trigger",
        TriggerActionType.MovePlayersToObject => "Move players to object",
        TriggerActionType.StartConversation => "Talk to custom NPC",
        TriggerActionType.PlayPlayerAnimation => "Play animation on players",
        TriggerActionType.ApplyLighting => "Apply lighting",
        TriggerActionType.ChangeMusic => "Change music",
        TriggerActionType.Wait => "Wait for seconds",
        TriggerActionType.CameraOffset => "Set camera offset",
        TriggerActionType.CameraOffsetReset => "Reset camera offset",
        TriggerActionType.CameraZoom => "Set camera zoom",
        TriggerActionType.CameraZoomReset => "Reset camera zoom",
        TriggerActionType.CameraLookAtObject => "Look at object",
        TriggerActionType.CameraLookAtTrigger => "Look at trigger",
        TriggerActionType.CameraEffect => "Play camera effect",
        TriggerActionType.PlayCutscene => "Play cutscene",
        TriggerActionType.ShowCaption => "Caption (bottom right)",
        TriggerActionType.ShowTitleText => "Title (top of screen)",
        TriggerActionType.ShowFullscreenText => "Fullscreen text (centre)",
        _ => type.ToString()
    };

    // Which question the shared target dropdown is currently asking.
    private enum TargetStage
    {
        None,
        Trigger,
        Npc,
        Animation,
        AnimationMode,
        Lighting,
        LightingFade,
        Music,

        // Which action inside the group the user picked.
        Category,

        // Shared by everything that ends with "for how long": waits, camera holds, text.
        Seconds,
        Zoom,
        Effect,
        LookTrigger,
        Cutscene,
        CutsceneSkip
    }

    private TargetStage _stage;
    private string _pendingAnimation;
    private string _pendingLighting;

    // The half-finished action a Seconds or object-pick step is going to complete.
    private TriggerActionType _pendingType;
    private string _pendingText;
    private string _pendingSubtext;
    private string _pendingTarget;
    private Vector3 _pendingPosition;

    // The actions of the group currently open in the submenu, index-aligned with its labels.
    private readonly List<TriggerActionType> _groupTypes = [];

    private static readonly string[] SecondsLabels =
        ["0.5 seconds", "1 second", "2 seconds", "3 seconds", "5 seconds", "8 seconds"];

    private static readonly float[] SecondsValues = [0.5f, 1f, 2f, 3f, 5f, 8f];

    // 10 is the rig's own resting distance, so the list reads as "how far in from normal".
    private static readonly string[] CutsceneSkipModes =
        ["Skippable (Esc)", "Cannot be skipped"];

    private static readonly string[] ZoomLabels =
    [
        "1 (closest)", "2", "3", "4", "5", "6", "7", "8", "9", "10 (default)"
    ];

    // Display names are what the dropdown shows; the ids that go into the action are kept
    // alongside, because an NPC's display name is not what the registry is keyed by.
    private readonly List<string> _targetKeys = [];

    // Set while the tool is waiting for a world click to name a Move-to-object target.
    private bool _pickingObject;

    private RectTransform CreateActionListContainer(RectTransform panel)
    {
        var go = new GameObject("ActionList");
        go.transform.SetParent(panel, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, 0f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        // The panel column does not control its children's height, so the container has to
        // report its own - otherwise every row would stack inside a zero-height rect.
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return rt;
    }

    private void RebuildActionList()
    {
        if (_actionList == null || _ui == null) return;

        foreach (Transform child in _actionList)
            Object.Destroy(child.gameObject);

        var actions = _selected?.Actions;

        if (_selected == null)
        {
            AddActionNote("Select a trigger to edit its actions.");
        }
        else if (actions.Count == 0)
        {
            AddActionNote("No actions - entering does nothing.");
        }
        else
        {
            for (var i = 0; i < actions.Count; i++)
                CreateActionRow(i, actions[i]);
        }

        // Destroyed children do not leave the layout until end of frame, and nested fitters do
        // not settle on their own - the editor's own rebuild pass handles both.
        _editor.RequestOptionsResize();
    }

    private void AddActionNote(string text)
    {
        var label = _ui.CreateLabel(_actionList, text, 15, TMPro.TextAlignmentOptions.Center);
        label.GetComponent<TMPro.TMP_Text>().color = new Color(1f, 1f, 1f, 0.55f);
    }

    private void CreateActionRow(int index, TriggerAction action)
    {
        var row = new GameObject("Action_" + index);
        row.transform.SetParent(_actionList, false);

        var rowRt = row.AddComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(0f, RowHeight);

        var element = row.AddComponent<LayoutElement>();
        element.minHeight = RowHeight;
        element.preferredHeight = RowHeight;

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var plate = row.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.5f;
        plate.color = action == _selectedAction
            ? new Color(MapEditorUI.Accent.r, MapEditorUI.Accent.g, MapEditorUI.Accent.b, 0.45f)
            : new Color(0f, 0f, 0f, 0.55f);

        plate.raycastTarget = true;
        var select = row.AddComponent<Button>();
        select.targetGraphic = plate;
        select.transition = Selectable.Transition.None;
        select.onClick.AddListener(() =>
        {
            RuntimeMapEditor.Active?.BlockWorldClicks();
            SelectAction(action);
        });

        // Numbered, because the order is the whole point of a sequence.
        var label = _ui.CreateLabel(row.transform, $"{index + 1}. {action.Describe()}", 15);
        var labelText = label.GetComponent<TMPro.TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        labelText.margin = new Vector4(8f, 0f, 0f, 0f);
        labelText.raycastTarget = false;

        RowButton(row, "-", () => MoveAction(action, -1));
        RowButton(row, "+", () => MoveAction(action, 1));
        RowButton(row, "X", () => RemoveAction(action));
    }

    private void RowButton(GameObject row, string text, System.Action onClick)
    {
        var button = _ui.CreateButton(row.transform, text, onClick, RowHeight - 4f);

        // CreateButton's row layout flexes to fill a column; in a horizontal row that would push
        // the label out entirely.
        var element = button.GetComponent<LayoutElement>();
        element.preferredWidth = 30f;
        element.minWidth = 30f;
        element.flexibleWidth = 0f;
    }

    private const float RowHeight = 30f;

    private void MoveAction(TriggerAction action, int direction)
    {
        if (_selected == null || action == null) return;

        var actions = _selected.Actions;
        var index = actions.IndexOf(action);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= actions.Count)
        {
            _editor.SetStatus(direction < 0 ? "Already first." : "Already last.");
            return;
        }

        actions.RemoveAt(index);
        actions.Insert(target, action);
        RebuildActionList();
        _editor.SetStatus($"{_selected.Id}: action moved to position {target + 1}.");
    }

    private void RemoveAction(TriggerAction action)
    {
        if (_selected == null || action == null) return;

        if (_selectedAction == action) SelectAction(null);

        _selected.Actions.Remove(action);
        RebuildActionList();
        UpdateInfo();
        _editor.SetStatus($"{_selected.Id}: removed action ({_selected.Actions.Count} left).");
    }

    // ---- action target highlight -------------------------------------------------------------

    private void SelectAction(TriggerAction action)
    {
        // A second click on the same row clears it, so the outline can be dismissed without
        // selecting something else.
        _selectedAction = _selectedAction == action ? null : action;

        RebuildActionList();
        SyncTargetHighlight();

        if (_selectedAction != null)
            _editor.SetStatus($"Target: {_selectedAction.Describe()}.");
    }

    private readonly List<GameObject> _targetBoxes = [];
    private readonly List<Bounds> _targetBounds = [];

    private static readonly Color TargetColour = new(0.35f, 1f, 0.4f, 1f);

    // Rebuilt every frame rather than once on selection: a target can be dragged (or, for the
    // players, moved by the game) while the outline is up.
    private void SyncTargetHighlight()
    {
        _targetBounds.Clear();
        if (_selectedAction != null && _gizmosVisible) CollectTargetBounds(_selectedAction, _targetBounds);

        for (var i = 0; i < _targetBounds.Count; i++)
        {
            if (i >= _targetBoxes.Count)
                _targetBoxes.Add(MapEditorGizmos.CreateBox("MapEditor_TriggerTarget", TargetColour));

            var box = _targetBoxes[i];
            if (box == null) continue;

            if (!box.activeSelf) box.SetActive(true);
            MapEditorGizmos.SetBox(box, _targetBounds[i]);
        }

        for (var i = _targetBounds.Count; i < _targetBoxes.Count; i++)
            if (_targetBoxes[i] != null && _targetBoxes[i].activeSelf) _targetBoxes[i].SetActive(false);
    }

    // The resolved target for the selected row, re-resolved at most twice a second: the resolve
    // itself is a scene-wide sweep on a miss (ResolveObject falls back to every Transform in the
    // scene, the NPC case to every CustomNpcBehaviour), and it used to run per frame. Bounds are
    // still read live each frame so a dragged target tracks; only the lookup is throttled.
    private static TriggerAction _resolvedFor;
    private static string _resolvedTarget;
    private static GameObject _resolvedObject;
    private static float _nextResolveAt;

    private static GameObject ResolveTargetCached(TriggerAction action, System.Func<GameObject> resolve)
    {
        var stale = _resolvedFor != action || _resolvedTarget != action.Target;
        if (!stale && _resolvedObject != null) return _resolvedObject;
        if (!stale && Time.unscaledTime < _nextResolveAt) return _resolvedObject;

        _resolvedFor = action;
        _resolvedTarget = action.Target;
        _resolvedObject = resolve();
        _nextResolveAt = Time.unscaledTime + 0.5f;
        return _resolvedObject;
    }

    private static void CollectTargetBounds(TriggerAction action, List<Bounds> into)
    {
        switch (action.Type)
        {
            case TriggerActionType.MovePlayersToTrigger:
            {
                var trigger = TriggerActions.FindTrigger(action.Target);
                // From the volume's own rectangle: a trigger has no renderer to measure, and its
                // gizmo is only there while this tool is open.
                if (trigger != null)
                    into.Add(new Bounds(trigger.transform.position,
                        new Vector3(trigger.Size.x, trigger.Size.y, 0.1f)));
                break;
            }

            case TriggerActionType.CameraLookAtTrigger:
            {
                var trigger = TriggerActions.FindTrigger(action.Target);
                if (trigger != null)
                    into.Add(new Bounds(trigger.transform.position,
                        new Vector3(trigger.Size.x, trigger.Size.y, 0.1f)));
                break;
            }

            case TriggerActionType.CameraLookAtObject:
            case TriggerActionType.MovePlayersToObject:
            {
                var go = ResolveTargetCached(action, () => TriggerActions.ResolveObject(action.Target));
                if (go != null && MapEditorGizmos.TryGetBounds(go, out var bounds)) into.Add(bounds);
                // The object is not in this room; the authored position is still where the players
                // would be sent, so it is marked instead of showing nothing.
                else into.Add(new Bounds(action.Position, new Vector3(1.5f, 1.5f, 0.1f)));
                break;
            }

            case TriggerActionType.StartConversation:
            {
                var go = ResolveTargetCached(action, () =>
                {
                    foreach (var npc in Object.FindObjectsOfType<Npc.CustomNpcBehaviour>())
                    {
                        if (npc == null || npc.Definition == null) continue;
                        if (npc.Definition.InternalName == action.Target) return npc.gameObject;
                    }
                    return null;
                });
                if (go != null && MapEditorGizmos.TryGetBounds(go, out var bounds)) into.Add(bounds);
                break;
            }

            case TriggerActionType.PlayPlayerAnimation:
            {
                // Every player, because that is who performs it.
                foreach (var player in TriggerActions.LivePlayers())
                {
                    if (MapEditorGizmos.TryGetBounds(player.gameObject, out var bounds)) into.Add(bounds);
                    else into.Add(new Bounds(player.transform.position, new Vector3(1f, 2f, 0.1f)));
                }
                break;
            }
        }
    }

    private void ClearTargetHighlight()
    {
        _selectedAction = null;
        foreach (var box in _targetBoxes)
            if (box != null) Object.Destroy(box);
        _targetBoxes.Clear();
    }

    private void AddAction(TriggerAction action)
    {
        if (_selected == null || action == null) return;

        _selected.Actions.Add(action);
        RebuildActionList();
        UpdateInfo();
        _editor.SetStatus($"{_selected.Id}: added {action.Describe()}.");

        _stage = TargetStage.None;
        _addDropdown?.SetSelected(-1);
        UpdateActionControls();
    }

    private void OnAddActionType(int index, string label)
    {
        _pickingObject = false;
        _stage = TargetStage.None;
        UpdateActionControls();

        if (_selected == null)
        {
            _editor.SetStatus("Select a trigger first.", StatusSeverity.Warning);
            return;
        }

        if (index < 0 || index >= ActionGroups.Length) return;

        var group = ActionGroups[index];

        // A group of one is a category in name only; opening a submenu to show its single entry
        // would be one click of nothing.
        if (group.Length == 1)
        {
            StartAction(group[0]);
            return;
        }

        _groupTypes.Clear();
        var labels = new List<string>(group.Length);
        foreach (var type in group)
        {
            _groupTypes.Add(type);
            labels.Add(ActionName(type));
        }

        OpenTargets(TargetStage.Category, labels);
    }

    // Everything a chosen action needs before it can be added: a target to pick, a duration to
    // choose, a caption to type, or nothing at all.
    private void StartAction(TriggerActionType type)
    {
        _pendingType = type;
        _pendingText = null;
        _pendingSubtext = null;
        _pendingTarget = null;

        switch (type)
        {
            case TriggerActionType.MovePlayersToTrigger:
            {
                _targetKeys.Clear();
                foreach (var trigger in _triggers)
                {
                    // Its own volume is excluded: walking the players into the trigger that just
                    // fired is a loop waiting to happen.
                    if (trigger == null || trigger == _selected) continue;
                    _targetKeys.Add(trigger.Id);
                }

                if (_targetKeys.Count == 0)
                {
                    _editor.SetStatus("Place another trigger to move to first.", StatusSeverity.Warning);
                    return;
                }

                OpenTargets(TargetStage.Trigger, _targetKeys);
                break;
            }

            case TriggerActionType.MovePlayersToObject:
                _pickingObject = true;
                _editor.SetStatus("Click the object in the world to move the players to.");
                break;

            case TriggerActionType.CameraLookAtObject:
                _pickingObject = true;
                _editor.SetStatus("Click the object for the camera to look at.");
                break;

            case TriggerActionType.CameraLookAtTrigger:
            {
                _targetKeys.Clear();
                foreach (var trigger in _triggers)
                    if (trigger != null) _targetKeys.Add(trigger.Id);

                if (_targetKeys.Count == 0)
                {
                    _editor.SetStatus("No triggers to look at.", StatusSeverity.Warning);
                    return;
                }

                OpenTargets(TargetStage.LookTrigger, _targetKeys);
                break;
            }

            case TriggerActionType.Wait:
                OpenTargets(TargetStage.Seconds, SecondsLabels);
                break;

            case TriggerActionType.CameraZoom:
                OpenTargets(TargetStage.Zoom, ZoomLabels);
                break;

            case TriggerActionType.CameraEffect:
                OpenTargets(TargetStage.Effect, TriggerCameraActions.Effects);
                break;

            case TriggerActionType.PlayCutscene:
            {
                // The folder first, so a custom video of the same name as a vanilla one is the
                // one offered; both are played the same way from here on.
                _targetKeys.Clear();
                var labels = new List<string>();

                foreach (var custom in APIHelper.CustomCutsceneLoader.Names())
                {
                    _targetKeys.Add(custom);
                    labels.Add(custom);
                }

                foreach (var vanilla in APIHelper.CustomCutsceneLoader.VanillaCutscenes)
                {
                    if (_targetKeys.Contains(vanilla)) continue;
                    _targetKeys.Add(vanilla);
                    labels.Add(vanilla + "  (vanilla)");
                }

                if (_targetKeys.Count == 0)
                {
                    _editor.SetStatus("No cutscenes: drop an .mp4 in the CustomCutscenes folder.",
                        StatusSeverity.Warning);
                    return;
                }

                OpenTargets(TargetStage.Cutscene, labels);
                break;
            }

            // Nothing left to ask.
            case TriggerActionType.CameraOffsetReset:
            case TriggerActionType.CameraZoomReset:
                AddAction(new TriggerAction { Type = type });
                break;

            case TriggerActionType.CameraOffset:
                BeginOffsetCapture();
                break;

            case TriggerActionType.ShowCaption:
            case TriggerActionType.ShowTitleText:
            case TriggerActionType.ShowFullscreenText:
                PromptForText(type);
                break;

            case TriggerActionType.StartConversation:
            {
                _targetKeys.Clear();
                var names = new List<string>();

                // Read live: an NPC registered by another mod is as valid a target as ours, and
                // the registry is the only place both appear.
                foreach (var pair in APIHelper.CustomNpcManager.CustomNpcList)
                {
                    if (pair.Value == null) continue;
                    _targetKeys.Add(pair.Key);
                    names.Add(pair.Value.DisplayName);
                }

                if (_targetKeys.Count == 0)
                {
                    _editor.SetStatus("No custom NPCs are registered to talk to.", StatusSeverity.Warning);
                    return;
                }

                OpenTargets(TargetStage.Npc, names);
                break;
            }

            case TriggerActionType.PlayPlayerAnimation:
            {
                // Straight off the player's own skeleton: typing names by hand produced silent
                // no-ops, because an animation the skeleton does not have simply never plays.
                var animations = TriggerActions.PlayerAnimationNames();
                if (animations.Count == 0)
                {
                    _editor.SetStatus("The player's skeleton is not loaded; cannot list animations.",
                        StatusSeverity.Warning);
                    return;
                }

                OpenTargets(TargetStage.Animation, animations);
                break;
            }

            case TriggerActionType.ApplyLighting:
            {
                // Vanilla is always on the list - a trigger that only puts the biome's own
                // lighting back is a legitimate sequence ender.
                _targetKeys.Clear();
                _targetKeys.Add("");
                var names = new List<string> { "Vanilla lighting" };

                foreach (var profile in LightingProfiles.Names())
                {
                    _targetKeys.Add(profile);
                    names.Add(profile);
                }

                OpenTargets(TargetStage.Lighting, names);
                break;
            }

            case TriggerActionType.ChangeMusic:
            {
                // Same FMOD enumeration the music tool uses, so both pickers show one list.
                var tracks = MusicTool.MusicEvents();
                if (tracks.Count == 0)
                {
                    _editor.SetStatus("No music events found.", StatusSeverity.Warning);
                    return;
                }

                _targetKeys.Clear();
                var names = new List<string>(tracks.Count);
                foreach (var track in tracks)
                {
                    _targetKeys.Add(track);
                    names.Add(MusicTool.ShortName(track));
                }

                OpenTargets(TargetStage.Music, names);
                break;
            }
        }
    }

    private void OpenTargets(TargetStage stage, IList<string> options)
    {
        _stage = stage;
        _targetDropdown.SetOptions(options);

        UpdateActionControls();
        if (_panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);

        _targetDropdown.Open();
    }

    // The add-action controls belong to a selected trigger, and the target dropdown only to a
    // half-finished action, so both come and go rather than sitting there inert.
    private void UpdateActionControls()
    {
        var addRoot = _addDropdown?.Root;
        if (addRoot != null && addRoot.activeSelf != (_selected != null))
            addRoot.SetActive(_selected != null);

        var targetRoot = _targetDropdown?.Root;
        var wantTarget = _selected != null && _stage != TargetStage.None;
        if (targetRoot != null && targetRoot.activeSelf != wantTarget)
            targetRoot.SetActive(wantTarget);

        _editor.RequestOptionsResize();
    }

    // Loop lengths rather than a free number: the only thing an author actually wants to say is
    // "hold this pose for a beat / a while", and a typed seconds field is another modal prompt.
    private static readonly string[] AnimationModes = ["Play once", "Loop 2 seconds", "Loop 5 seconds", "Loop 10 seconds"];
    private static readonly float[] AnimationDurations = [0f, 2f, 5f, 10f];

    // Same shape for the lighting swap: how long it cross-fades over. Instant is last because it
    // is the old cut, kept for a lightning-strike moment rather than as the normal answer. A
    // negative duration is what TriggerAction reads as "no fade".
    private static readonly string[] LightingFadeModes =
        ["Fade 1 second", "Fade 2 seconds", "Fade 4 seconds", "Instant (no fade)"];

    private static readonly float[] LightingFadeDurations = [1f, 2f, 4f, -1f];

    private void OnTargetChosen(int index, string value)
    {
        if (_selected == null) return;

        switch (_stage)
        {
            case TargetStage.Trigger:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.MovePlayersToTrigger,
                    Target = index >= 0 && index < _targetKeys.Count ? _targetKeys[index] : value
                });
                break;

            case TargetStage.Npc:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.StartConversation,
                    Target = index >= 0 && index < _targetKeys.Count ? _targetKeys[index] : value
                });
                break;

            case TargetStage.Animation:
                // Second question, same dropdown.
                _pendingAnimation = value;
                OpenTargets(TargetStage.AnimationMode, AnimationModes);
                break;

            case TargetStage.AnimationMode:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.PlayPlayerAnimation,
                    Target = _pendingAnimation,
                    Loop = index > 0,
                    Duration = index >= 0 && index < AnimationDurations.Length ? AnimationDurations[index] : 0f
                });
                _pendingAnimation = null;
                break;

            case TargetStage.Lighting:
                // Slot 0 is "Vanilla lighting", whose key is the empty string - so the pending
                // target is legitimately blank here, and only the stage says it was answered.
                _pendingLighting = index >= 0 && index < _targetKeys.Count ? _targetKeys[index] : value;
                OpenTargets(TargetStage.LightingFade, LightingFadeModes);
                break;

            case TargetStage.LightingFade:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.ApplyLighting,
                    Target = _pendingLighting ?? "",
                    Duration = index >= 0 && index < LightingFadeDurations.Length
                        ? LightingFadeDurations[index]
                        : TriggerAction.DefaultLightingFade
                });
                _pendingLighting = null;
                break;

            case TargetStage.Music:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.ChangeMusic,
                    // The label is the short name; the action needs the full FMOD path.
                    Target = index >= 0 && index < _targetKeys.Count ? _targetKeys[index] : value
                });
                break;

            case TargetStage.Category:
                if (index >= 0 && index < _groupTypes.Count) StartAction(_groupTypes[index]);
                break;

            case TargetStage.LookTrigger:
                _pendingTarget = index >= 0 && index < _targetKeys.Count ? _targetKeys[index] : value;
                OpenTargets(TargetStage.Seconds, SecondsLabels);
                break;

            case TargetStage.Zoom:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.CameraZoom,
                    Amount = index >= 0 ? index + 1f : 10f
                });
                break;

            case TargetStage.Cutscene:
                _pendingTarget = index >= 0 && index < _targetKeys.Count ? _targetKeys[index] : value;
                OpenTargets(TargetStage.CutsceneSkip, CutsceneSkipModes);
                break;

            case TargetStage.CutsceneSkip:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.PlayCutscene,
                    Target = _pendingTarget ?? "",
                    // Loop is the skippable flag for this action: a cutscene has nothing to loop.
                    Loop = index == 0
                });
                _pendingTarget = null;
                break;

            case TargetStage.Effect:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.CameraEffect,
                    Target = value,
                    Duration = 1.5f
                });
                break;

            case TargetStage.Seconds:
                FinishTimedAction(index >= 0 && index < SecondsValues.Length
                    ? SecondsValues[index]
                    : 2f);
                break;
        }
    }

    // The last step of every action that ends with a duration. What it builds depends on which
    // action asked the question, which is what _pendingType is for.
    private void FinishTimedAction(float seconds)
    {
        switch (_pendingType)
        {
            case TriggerActionType.Wait:
                AddAction(new TriggerAction { Type = TriggerActionType.Wait, Duration = seconds });
                break;

            case TriggerActionType.CameraLookAtObject:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.CameraLookAtObject,
                    Target = _pendingTarget ?? "",
                    Position = _pendingPosition,
                    Duration = seconds
                });
                break;

            case TriggerActionType.CameraLookAtTrigger:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.CameraLookAtTrigger,
                    Target = _pendingTarget ?? "",
                    Duration = seconds
                });
                break;

            case TriggerActionType.ShowCaption:
            case TriggerActionType.ShowTitleText:
            case TriggerActionType.ShowFullscreenText:
                AddAction(new TriggerAction
                {
                    Type = _pendingType,
                    Target = _pendingText ?? "",
                    Subtext = _pendingSubtext ?? "",
                    Duration = seconds
                });
                break;

            default:
                _editor.SetStatus("Nothing was waiting on a duration.", StatusSeverity.Warning);
                break;
        }

        _pendingText = null;
        _pendingSubtext = null;
        _pendingTarget = null;
    }

    // ---- camera offset capture ----------------------------------------------------------------

    private bool _capturingOffset;
    private Vector3 _offsetOrigin;

    // The offset is authored by eye: the view snaps back to where the camera normally sits (on
    // the players), the author pans it to the framing they want, and V takes the difference. It
    // is stored relative to the follow target, not as a world position, because at run time the
    // players are somewhere else entirely.
    private void BeginOffsetCapture()
    {
        var players = TriggerActions.LivePlayers();
        _offsetOrigin = players.Count > 0
            ? players[0].transform.position
            : _selected != null ? _selected.transform.position : Vector3.zero;

        _editor.MoveCameraTo(_offsetOrigin);
        _capturingOffset = true;
        _editor.SetStatus("Camera reset to the players. Pan with WASD, then press V to set the " +
                          "offset (Esc cancels).");
    }

    private void TickOffsetCapture()
    {
        if (!_capturingOffset) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _capturingOffset = false;
            _editor.SetStatus("Camera offset cancelled.");
            return;
        }

        if (!Input.GetKeyDown(KeyCode.V)) return;

        var offset = _editor.CameraFocus - _offsetOrigin;
        offset.z = 0f;
        _capturingOffset = false;

        AddAction(new TriggerAction { Type = TriggerActionType.CameraOffset, Position = offset });
    }

    // ---- caption text ---------------------------------------------------------------------------

    // The same dialog the map save uses, with its overwrite check disabled - there is nothing to
    // collide with - and a longer limit, because a caption is a sentence rather than a name.
    // Two dialogs, because the text is two lines of different sizes. The second can be left
    // empty - a title on its own is a perfectly good caption.
    private void PromptForText(TriggerActionType type)
    {
        var titled = false;

        MapNamePrompt.Show(_editor, "", "TITLE TEXT",
            title =>
            {
                if (string.IsNullOrWhiteSpace(title)) return;

                titled = true;
                _pendingText = title.Trim();
                _pendingType = type;
            },
            // The second dialog opens from the first one's *close*, not its confirm: opening it
            // from the confirm meant two of the game's name menus alive at once, with the one
            // going away taking the editor's modal state with it - so the editor read WASD and
            // tool shortcuts underneath the dialog still being typed into.
            onClosed: () =>
            {
                if (!titled)
                {
                    _editor.SetStatus("No title entered; nothing was added.", StatusSeverity.Warning);
                    return;
                }

                PromptForSubtext();
            },
            existsCheck: _ => false, existsNoun: "text", characterLimit: 60);
    }

    private void PromptForSubtext()
    {
        var answered = false;

        MapNamePrompt.Show(_editor, "", "SUBTEXT (OPTIONAL)",
            subtext =>
            {
                answered = true;
                _pendingSubtext = string.IsNullOrWhiteSpace(subtext) ? "" : subtext.Trim();
            },
            // Cancelled is a legitimate answer here - a title on its own is a caption - so the
            // duration question is asked either way.
            onClosed: () =>
            {
                if (!answered) _pendingSubtext = "";
                OpenTargets(TargetStage.Seconds, SecondsLabels);
            },
            existsCheck: _ => false, existsNoun: "text", characterLimit: 160);
    }

    private bool TryPickObject(Vector3 world)
    {
        var picked = SelectTool.PickWorldObject(world);

        // One exception the select tool has no reason to make: our own volumes are gizmos, not
        // scenery, and clicking one means the trigger, not a place to walk to.
        if (picked != null && picked.GetComponentInParent<CTMapTrigger>() != null) picked = null;

        if (picked == null)
        {
            _editor.SetStatus("Nothing there - click an object.", StatusSeverity.Warning);
            return false;
        }

        _pickingObject = false;

        // The camera asks a second question - how long to hold the shot - so it goes through the
        // duration step; moving the players is complete as soon as the object is named.
        if (_pendingType == TriggerActionType.CameraLookAtObject)
        {
            _pendingTarget = TriggerActions.PathOf(picked);
            _pendingPosition = picked.transform.position;
            OpenTargets(TargetStage.Seconds, SecondsLabels);
            return true;
        }

        AddAction(new TriggerAction
        {
            Type = TriggerActionType.MovePlayersToObject,
            Target = TriggerActions.PathOf(picked),
            // Kept as the fallback for when the object is not in the room on a later load.
            Position = picked.transform.position
        });

        UpdateActionControls();
        return true;
    }

    public void OnEnter()
    {
        ShowGizmos(true);
        _editor.SetStatus("Click to place a trigger, or click one to select it.");
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Place or select trigger"),
        ("Drag", "Centre moves, corner resizes"),
        ("Del", "Delete selected"),
        ("V", "Set camera offset while framing"),
        ("Esc", "Cancel target pick")
    ];

    public void OnExit()
    {
        ShowGizmos(false);
        Select(null);
        // Leaving the tool answers the question: the button must not still be armed on return.
        Disarm();
        _pickingObject = false;
        _capturingOffset = false;
        _stage = TargetStage.None;
        ClearTargetHighlight();
        UpdateActionControls();
    }

    public void OnUpdate()
    {
        Prune();
        TickArmWindow();

        if (Input.GetKeyDown(KeyCode.Delete)) DeleteSelected();

        TickOffsetCapture();

        // Escape gets out of a mis-started object pick without placing anything.
        if (_pickingObject && Input.GetKeyDown(KeyCode.Escape))
        {
            _pickingObject = false;
            _stage = TargetStage.None;
            UpdateActionControls();
            _editor.SetStatus("Target pick cancelled.");
        }

        // While framing an offset the world is the viewfinder: a click there would drop a new
        // trigger behind the shot being composed.
        if (Input.GetMouseButtonDown(0) && !_capturingOffset && !_editor.PointerOverUi())
        {
            var world = _editor.MouseWorld();

            // While picking a target, a click names an object instead of placing or selecting a
            // volume - otherwise every attempt to point at scenery would drop a new trigger on it.
            if (_pickingObject)
            {
                TryPickObject(world);
                return;
            }

            var hit = PickAt(world);
            if (hit != null) Select(hit);
            else Select(CreateTrigger(world, DefaultWidth, DefaultHeight), placed: true);
        }

        SyncHandles();
        SyncTargetHighlight();
    }

    // ---- placement --------------------------------------------------------------------------

    // Also the loader's entry point: self-registers so load then save round-trips.
    public CTMapTrigger CreateTrigger(Vector3 position, float width, float height,
        string id = null, string action = "", bool once = true,
        List<MapTriggerActionData> actions = null, bool lockPlayerControl = true)
    {
        var parent = SceneRefs.ContentRoot;
        if (parent == null)
        {
            _editor.SetStatus("No room content root; cannot place a trigger.", StatusSeverity.Error);
            return null;
        }

        var go = new GameObject("CultTweaker_Trigger");
        go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(position.x, position.y, 0f);

        var trigger = go.AddComponent<CTMapTrigger>();
        trigger.Id = string.IsNullOrWhiteSpace(id) ? NextId() : id;
        trigger.Action = action ?? "";
        trigger.Once = once;
        trigger.LockPlayerControl = lockPlayerControl;
        trigger.Actions.AddRange(TriggerActions.FromData(actions, trigger.Id));
        trigger.Size = new Vector2(Mathf.Max(0.5f, width), Mathf.Max(0.5f, height));
        trigger.Refresh();
        trigger.ShowGizmo(_gizmosVisible);

        _triggers.Add(trigger);

        _editor.History.Push($"place trigger {trigger.Id}", () =>
        {
            if (!_triggers.Remove(trigger) || trigger == null) return false;
            if (_selected == trigger) Select(null);
            Object.Destroy(trigger.gameObject);
            return true;
        });

        return trigger;
    }

    private string NextId()
    {
        // Ids only have to be unique within the room, and they are what a later phase will use to
        // address one trigger from a level or another trigger.
        string candidate;
        do
        {
            candidate = "Trigger" + _nextId++;
        } while (_triggers.Exists(t => t != null && t.Id == candidate));

        return candidate;
    }

    // Smallest volume containing the point, so a trigger drawn inside another can still be picked.
    private CTMapTrigger PickAt(Vector3 world)
    {
        CTMapTrigger best = null;
        var bestArea = float.MaxValue;

        foreach (var trigger in _triggers)
        {
            if (trigger == null || !trigger.WorldRect.Contains(world)) continue;

            var area = trigger.Size.x * trigger.Size.y;
            if (area >= bestArea) continue;
            bestArea = area;
            best = trigger;
        }

        return best;
    }

    private void Select(CTMapTrigger trigger, bool placed = false)
    {
        if (_selected != null) _selected.SetHighlighted(false);
        _selected = trigger;

        // The highlighted action belonged to the trigger being left behind.
        _selectedAction = null;
        SyncTargetHighlight();

        // A half-answered "which target?" question does not survive changing triggers either.
        _stage = TargetStage.None;
        _pickingObject = false;

        if (_selected == null)
        {
            SetHandlesActive(false);
            if (_info != null) _info.text = "No trigger selected";
            RebuildActionList();
            UpdateActionControls();
            return;
        }

        _selected.SetHighlighted(true);
        PushSelectionToPanel();

        // Only on selection, never from the drag path below: rebuilding the rows every frame of a
        // resize would destroy and recreate the whole list continuously.
        RebuildActionList();
        UpdateActionControls();
        _editor.SetStatus(placed
            ? $"Placed {_selected.Id}."
            : $"Selected {_selected.Id} ({_selected.Size.x:0.#} x {_selected.Size.y:0.#}).");
    }

    private void DeleteSelected()
    {
        if (_selected == null)
        {
            _editor.SetStatus("No trigger selected.");
            return;
        }

        var id = _selected.Id;
        _triggers.Remove(_selected);
        Object.Destroy(_selected.gameObject);
        Select(null);
        _editor.SetStatus($"Deleted {id}.");
    }

    // ---- panel <-> selection ------------------------------------------------------------------

    private void PushSelectionToPanel()
    {
        UpdateInfo();

        if (_selected == null) return;

        // SetValueWithoutNotify would still be re-entered through the slider's own drag, so the
        // guard covers both directions.
        _syncingSliders = true;
        _widthSlider?.SetValueWithoutNotify(_selected.Size.x);
        _heightSlider?.SetValueWithoutNotify(_selected.Size.y);
        _onceToggle?.SetValue(_selected.Once, notify: false);
        _lockToggle?.SetValue(_selected.LockPlayerControl, notify: false);
        _syncingSliders = false;
    }

    private void Resize(float? width, float? height)
    {
        if (_syncingSliders) return;

        if (_selected == null)
        {
            _editor.SetStatus("Select a trigger first.", StatusSeverity.Warning);
            return;
        }

        _selected.Size = new Vector2(width ?? _selected.Size.x, height ?? _selected.Size.y);
        _selected.Refresh();
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        if (_info == null) return;

        _info.text = _selected == null
            ? "No trigger selected"
            : $"{_selected.Id}  -  {_selected.Size.x:0.#} x {_selected.Size.y:0.#}" +
              $"  -  {_selected.Actions.Count} action(s)";
    }

    // ---- handles ------------------------------------------------------------------------------

    // Screen-space grips rather than world-space quads: the same approach the select tool uses,
    // so they stay the same size at any zoom and are registered click blockers.
    private void SyncHandles()
    {
        if (_selected == null)
        {
            SetHandlesActive(false);
            return;
        }

        var cam = SceneRefs.Cam;
        if (cam == null) return;

        EnsureHandles();
        SetHandlesActive(true);

        var centre = _selected.transform.position;
        var corner = centre + new Vector3(_selected.Size.x * 0.5f, _selected.Size.y * 0.5f, 0f);

        _moveHandle.GetComponent<RectTransform>().position = cam.WorldToScreenPoint(centre);
        _resizeHandle.GetComponent<RectTransform>().position = cam.WorldToScreenPoint(corner);
    }

    private bool _gizmosVisible;

    private void ShowGizmos(bool visible)
    {
        _gizmosVisible = visible;
        foreach (var trigger in _triggers)
            if (trigger != null) trigger.ShowGizmo(visible);

        if (!visible) SetHandlesActive(false);
    }

    private void SetHandlesActive(bool active)
    {
        if (_moveHandle != null) _moveHandle.SetActive(active);
        if (_resizeHandle != null) _resizeHandle.SetActive(active);
    }

    private void EnsureHandles()
    {
        if (_handleCanvas == null)
        {
            var go = new GameObject("MapEditor_TriggerHandles");
            go.transform.SetParent(_editor.transform, false);

            _handleCanvas = go.AddComponent<Canvas>();
            _handleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _handleCanvas.sortingOrder = 5001;
            go.AddComponent<GraphicRaycaster>();
        }

        _moveHandle ??= CreateHandle("Move", MapEditorGizmos.GripColour, TriggerHandle.Mode.Move, 30f);
        _resizeHandle ??= CreateHandle("Resize", new Color(0.25f, 0.85f, 1f, 0.95f),
            TriggerHandle.Mode.Resize, 24f);
    }

    private GameObject CreateHandle(string name, Color colour, TriggerHandle.Mode mode, float size)
    {
        var go = new GameObject("MapEditor_Trigger" + name);
        go.transform.SetParent(_handleCanvas.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);

        var image = go.AddComponent<Image>();
        image.sprite = MapEditorUI.RoundedPlate;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 3f;
        image.color = colour;

        go.AddComponent<TriggerHandle>().Initialize(this, _editor, mode);
        _editor.RegisterUiBlocker(rt);
        go.SetActive(false);
        return go;
    }

    // Called by the drag handles.
    internal void DragTo(TriggerHandle.Mode mode, Vector3 world, Vector3 grabOffset)
    {
        if (_selected == null) return;

        if (mode == TriggerHandle.Mode.Move)
        {
            var target = world + grabOffset;
            _selected.transform.position = new Vector3(target.x, target.y, 0f);
        }
        else
        {
            var centre = _selected.transform.position;
            _selected.Size = new Vector2(
                Mathf.Clamp(Mathf.Abs(world.x - centre.x) * 2f, 0.5f, 80f),
                Mathf.Clamp(Mathf.Abs(world.y - centre.y) * 2f, 0.5f, 80f));
        }

        _selected.Refresh();
        PushSelectionToPanel();
    }

    internal Vector3 SelectedPosition => _selected != null ? _selected.transform.position : Vector3.zero;

    // ---- bookkeeping ---------------------------------------------------------------------------

    private void Prune()
    {
        for (var i = _triggers.Count - 1; i >= 0; i--)
            if (_triggers[i] == null) _triggers.RemoveAt(i);
    }

    // The room snapshot skips objects this tool already serializes.
    public bool IsTracked(GameObject go)
    {
        if (go == null) return false;
        return go.GetComponentInChildren<CTMapTrigger>(true) != null ||
               go.GetComponentInParent<CTMapTrigger>() != null;
    }

    public void ResetTracking()
    {
        _triggers.Clear();
        _selected = null;
        SetHandlesActive(false);
        ClearTargetHighlight();

        // A sequence belonging to the room being replaced must not keep the global lock (or the
        // players' InActive state) into the new one.
        CTMapTrigger.ResetSequenceState();
        RebuildActionList();
        UpdateActionControls();
    }

    // Everything this tool put in the room, for the clear tool.
    public int ClearPlaced()
    {
        var removed = 0;
        foreach (var trigger in _triggers)
        {
            if (trigger == null) continue;
            Object.Destroy(trigger.gameObject);
            removed++;
        }

        _triggers.Clear();
        Select(null);
        CTMapTrigger.ResetSequenceState();
        return removed;
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.Triggers.Clear();
        foreach (var trigger in _triggers)
        {
            if (trigger == null) continue;
            map.Triggers.Add(new MapTriggerData
            {
                Id = trigger.Id,
                Action = trigger.Action,
                Position = MapEditorSerialization.V3(trigger.transform.position),
                Width = trigger.Size.x,
                Height = trigger.Size.y,
                Once = trigger.Once,
                Actions = TriggerActions.ToData(trigger.Actions),
                LockPlayerControl = trigger.LockPlayerControl
            });
        }
    }
}

// Drags the selected trigger's centre or corner. The grab offset is captured on mouse-down so a
// move does not snap the volume's centre onto the cursor.
public class TriggerHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    public enum Mode
    {
        Move,
        Resize
    }

    private TriggerTool _tool;
    private RuntimeMapEditor _editor;
    private Mode _mode;
    private Vector3 _grabOffset;

    public void Initialize(TriggerTool tool, RuntimeMapEditor editor, Mode mode)
    {
        _tool = tool;
        _editor = editor;
        _mode = mode;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null) return;
        _grabOffset = _mode == Mode.Move
            ? _tool.SelectedPosition - _editor.ScreenToWorld(eventData.position)
            : Vector3.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null) return;
        _tool.DragTo(_mode, _editor.ScreenToWorld(eventData.position), _grabOffset);
    }
}
