using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class CTMapTrigger : MonoBehaviour
{
    public string Id = "";

    public string Action = "";

    public bool Once = true;
    public Vector2 Size = new(4f, 3f);

    public readonly List<TriggerAction> Actions = [];

    public bool LockPlayerControl = true;

    // A blocking volume is an invisible wall: a solid collider on the Obstacles layer, which is what
    // both the player and the enemies (and the grid graph, once rescanned) treat as terrain.
    public bool Blocking;

    public static event System.Action<CTMapTrigger> Entered;

    public static readonly List<CTMapTrigger> All = [];

    public static bool ShowInPlay = true;

    public bool Tripped { get; private set; }

    private BoxCollider2D _box;
    private GameObject _blocker;
    private BoxCollider2D _blockerBox;
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

    private static float _mutedUntil;

    public static void MuteFiring(float seconds) =>
        _mutedUntil = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);

    private static bool Muted => Time.realtimeSinceStartup < _mutedUntil;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Muted) return;
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

    public void Refresh()
    {
        Size = new Vector2(Mathf.Max(0.5f, Size.x), Mathf.Max(0.5f, Size.y));

        if (_box != null) _box.size = Size;
        ApplyBlocking();
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

    public void ApplyBlocking()
    {
        if (!Blocking)
        {
            if (_blocker != null && _blocker.activeSelf) _blocker.SetActive(false);
            return;
        }

        if (_blocker == null) BuildBlocker();
        if (_blockerBox != null) _blockerBox.size = Size;
        if (_blocker != null && !_blocker.activeSelf) _blocker.SetActive(true);
    }

    private void BuildBlocker()
    {
        _blocker = new GameObject("Blocker");
        _blocker.transform.SetParent(transform, false);

        // Standing on its own - no composite, no rigidbody - the box is a static solid, the same
        // trick the door plugs use to seal a doorway.
        var obstacles = LayerMask.NameToLayer("Obstacles");
        if (obstacles >= 0) _blocker.layer = obstacles;

        _blockerBox = _blocker.AddComponent<BoxCollider2D>();
        _blockerBox.size = Size;
    }

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

    public void RefreshTint() => ApplyTint();

    private void ApplyTint()
    {
        if (_outline == null) return;

        var colour = Blocking ? Blocked : Once ? Idle : Repeating;
        if (_flashUntil > Time.unscaledTime) colour = Firing;
        else if (_highlighted) colour = Selected;
        else if (Tripped && !Blocking) colour = Once ? Spent : Fired;

        _outline.startColor = _outline.endColor = colour;
        if (_fillRenderer != null)
            _fillRenderer.color = new Color(colour.r, colour.g, colour.b,
                _flashUntil > Time.unscaledTime ? 0.35f : 0.12f);
    }

    private static readonly Color Idle = new(0.25f, 0.85f, 1f, 0.9f);
    private static readonly Color Repeating = new(0.78f, 0.42f, 1f, 0.9f);
    private static readonly Color Selected = new(1f, 0.82f, 0.15f, 1f);
    private static readonly Color Firing = new(0.35f, 1f, 0.4f, 1f);
    private static readonly Color Spent = new(0.45f, 0.6f, 0.5f, 0.75f);
    private static readonly Color Fired = new(0.5f, 0.4f, 0.62f, 0.75f);
    private static readonly Color Blocked = new(1f, 0.24f, 0.2f, 0.95f);

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
        if (RuntimeMapEditor.Active != null && RuntimeMapEditor.Active.IsEditing)
        {
            _inside = AnyPlayerInside();
            return;
        }

        var inside = AnyPlayerInside();

        if (Muted) { }  // see MuteFiring: _inside stays false so the entry lands when it lifts
        else if (inside && !_inside) _inside = Fire();
        else _inside = inside;

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

    private bool Fire()
    {
        if (Once && Tripped) return true;

        if (Actions.Count > 0 && SequencePlaying) return false;

        Tripped = true;

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

    public static void ResetSequenceState()
    {
        if (_sequenceOwner == null) return;
        _sequenceOwner = null;
        TriggerActions.SetControl(true);
    }

    public void Rearm()
    {
        Tripped = false;
        _inside = false;
        _flashing = false;
        _flashUntil = 0f;
        ApplyTint();
    }
}

public class TriggerTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts, IMapEditorEscapeHandler,
    Net.IMapEditorLivePreview
{
    public string Name => "Triggers";

    // ---- multiplayer -------------------------------------------------------------------------

    public bool LiveActive => _gestureTarget != null || _cloneDragging;

    public IEnumerable<GameObject> LiveObjects
    {
        get
        {
            if (_selected != null) yield return _selected.gameObject;
        }
    }

    internal CTMapTrigger FindTrigger(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var trigger in _triggers)
            if (trigger != null && trigger.Id == id) return trigger;
        return null;
    }

    /// Takes a trigger out of the room and out of the books; false if it was not ours.
    internal bool RemoveTrigger(CTMapTrigger trigger)
    {
        if (trigger == null || !_triggers.Remove(trigger)) return false;
        if (_selected == trigger) Select(null);
        Object.Destroy(trigger.gameObject);
        return true;
    }

    /// Writes a record over a live trigger: place, size, flags and the whole action list.
    internal void ApplyRecord(CTMapTrigger trigger, MapTriggerData record)
    {
        if (trigger == null || record == null) return;

        var position = MapEditorSerialization.ToVector3(record.Position);
        trigger.transform.position = new Vector3(position.x, position.y, 0f);
        trigger.Size = new Vector2(Mathf.Max(0.5f, record.Width), Mathf.Max(0.5f, record.Height));
        trigger.Action = record.Action ?? "";
        trigger.Once = record.Once;
        trigger.LockPlayerControl = record.LockPlayerControl;

        var wasBlocking = trigger.Blocking;
        trigger.Blocking = record.Blocking;

        trigger.Actions.Clear();
        trigger.Actions.AddRange(TriggerActions.FromData(record.Actions, trigger.Id));

        trigger.Refresh();
        trigger.RefreshTint();
        if (wasBlocking != record.Blocking) SceneRefs.RescanNavigation();

        if (_selected == trigger)
        {
            PushSelectionToPanel();
            RebuildActionList();
            UpdateActionControls();
        }
    }

    internal static MapTriggerData Describe(CTMapTrigger trigger) => new()
    {
        Id = trigger.Id,
        Action = trigger.Action,
        Position = MapEditorSerialization.V3(trigger.transform.position),
        Width = trigger.Size.x,
        Height = trigger.Size.y,
        Once = trigger.Once,
        Actions = TriggerActions.ToData(trigger.Actions),
        LockPlayerControl = trigger.LockPlayerControl,
        Blocking = trigger.Blocking
    };

    private readonly RuntimeMapEditor _editor;
    private readonly List<CTMapTrigger> _triggers = [];

    private CTMapTrigger _selected;
    private int _nextId = 1;

    private Canvas _handleCanvas;
    private GameObject _moveHandle;
    private GameObject _resizeHandle;

    private TMPro.TMP_Text _info;

    private bool _syncingWidgets;

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

        _onceToggle = ui.CreateToggle(panel, "Fire once", true, value =>
        {
            if (_syncingWidgets || _selected == null) return;
            _selected.Once = value;

            _selected.RefreshTint();
            _editor.SetStatus(value ? $"{_selected.Id} fires once." : $"{_selected.Id} fires every entry.");
        }).GetComponent<MapEditorToggle>();

        _lockToggle = ui.CreateToggle(panel, "Lock control while playing", true, value =>
        {
            if (_syncingWidgets || _selected == null) return;
            _selected.LockPlayerControl = value;
            _editor.SetStatus(value
                ? $"{_selected.Id} freezes the players until an action needs them."
                : $"{_selected.Id} leaves the players in control.");
        }).GetComponent<MapEditorToggle>();

        _blockToggle = ui.CreateToggle(panel, "Blocking volume", false, value =>
        {
            if (_syncingWidgets || _selected == null) return;
            _selected.Blocking = value;

            _selected.ApplyBlocking();
            _selected.RefreshTint();
            SceneRefs.RescanNavigation();

            _editor.SetStatus(value
                ? $"{_selected.Id} is an invisible wall to players and enemies."
                : $"{_selected.Id} is walk-through again.");
        }).GetComponent<MapEditorToggle>();

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

        ui.CreateHeader(panel, "- Actions -", 19);

        _addDropdown = ui.CreateDropdown(panel, "Add action", ActionLabels, OnAddActionType);

        _targetDropdown = ui.CreateDropdown(panel, "Target", System.Array.Empty<string>(), OnTargetChosen);

        _actionBox = ui.CreateScrollBox(panel, "ActionList", ActionListHeight, RowHeight);
        _actionList = _actionBox.Content;

        RebuildActionList();
        UpdateActionControls();
    }

    private MapEditorToggle _onceToggle;
    private MapEditorToggle _lockToggle;
    private MapEditorToggle _blockToggle;

    public int LiveCount()
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
    private MapEditorScrollBox _actionBox;
    private MapEditorDropdown _addDropdown;
    private MapEditorDropdown _targetDropdown;

    private TriggerAction _selectedAction;

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
            TriggerActionType.PlayPlayerAnimation,
            TriggerActionType.PlayObjectAnimation,
            TriggerActionType.HubSpawnPoint
        ],
        [
            TriggerActionType.CameraLookAtObject,
            TriggerActionType.CameraLookAtTrigger,
            TriggerActionType.CameraOffset,
            TriggerActionType.CameraOffsetReset,
            TriggerActionType.CameraZoom,
            TriggerActionType.CameraZoomReset,
            TriggerActionType.CameraEffect,
            TriggerActionType.CameraShake,
            TriggerActionType.PlayCutscene
        ],
        [
            TriggerActionType.ShowCaption,
            TriggerActionType.ShowTitleText,
            TriggerActionType.ShowFullscreenText
        ],
        [
            TriggerActionType.ApplyLighting,
            TriggerActionType.ChangeMusic,
            TriggerActionType.OpenWorldMap,
            TriggerActionType.ReturnToBase
        ],
        [
            TriggerActionType.Wait
        ]
    ];

    private static string ActionName(TriggerActionType type) => type switch
    {
        TriggerActionType.MovePlayersToTrigger => "Move players to trigger",
        TriggerActionType.MovePlayersToObject => "Move players to object",
        TriggerActionType.StartConversation => "Talk to custom NPC",
        TriggerActionType.PlayPlayerAnimation => "Play animation on players",
        TriggerActionType.PlayObjectAnimation => "Play animation on object",
        TriggerActionType.ApplyLighting => "Apply lighting",
        TriggerActionType.OpenWorldMap => "Open world map",
        TriggerActionType.ReturnToBase => "Return to base",
        TriggerActionType.HubSpawnPoint => "Hub spawn point (player arrives here)",
        TriggerActionType.ChangeMusic => "Change music",
        TriggerActionType.Wait => "Wait for seconds",
        TriggerActionType.CameraOffset => "Set camera offset",
        TriggerActionType.CameraOffsetReset => "Reset camera offset",
        TriggerActionType.CameraZoom => "Set camera zoom",
        TriggerActionType.CameraZoomReset => "Reset camera zoom",
        TriggerActionType.CameraLookAtObject => "Look at object",
        TriggerActionType.CameraLookAtTrigger => "Look at trigger",
        TriggerActionType.CameraEffect => "Play camera effect",
        TriggerActionType.CameraShake => "Camera shake",
        TriggerActionType.PlayCutscene => "Play cutscene",
        TriggerActionType.ShowCaption => "Caption (bottom right)",
        TriggerActionType.ShowTitleText => "Title (top of screen)",
        TriggerActionType.ShowFullscreenText => "Fullscreen text (centre)",
        _ => type.ToString()
    };

    private enum TargetStage
    {
        None,
        Trigger,
        Npc,
        Animation,
        AnimationMode,
        ObjectAnimation,
        ObjectAnimationMode,
        ObjectAnimationEnd,
        Lighting,
        LightingFade,
        Music,

        Category,

        Seconds,
        Zoom,
        Effect,
        Shake,
        LookTrigger,
        Cutscene,
        CutsceneSkip,
        WorldMap
    }

    private TargetStage _stage;
    private string _pendingAnimation;
    private string _pendingLighting;

    private TriggerActionType _pendingType;
    private string _pendingText;
    private string _pendingSubtext;
    private string _pendingTarget;
    private Vector3 _pendingPosition;

    private readonly List<TriggerActionType> _groupTypes = [];

    private static readonly string[] SecondsLabels =
        ["0.5 seconds", "1 second", "2 seconds", "3 seconds", "5 seconds", "8 seconds"];

    private static readonly float[] SecondsValues = [0.5f, 1f, 2f, 3f, 5f, 8f];

    private static readonly string[] CutsceneSkipModes =
        ["Skippable (Esc)", "Cannot be skipped"];

    private static readonly string[] ZoomLabels =
    [
        "1 (closest)", "2", "3", "4", "5", "6", "7", "8", "9", "10 (default)"
    ];

    private readonly List<string> _targetKeys = [];

    private bool _pickingObject;

    private const float ActionListHeight = 300f;

    private void RebuildActionList()
    {
        if (_actionList == null || _ui == null) return;

        foreach (Transform child in _actionList)
            Object.Destroy(child.gameObject);

        var actions = _selected?.Actions;
        var rows = 0;

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

            rows = actions.Count;
        }

        _actionBox?.ScrollToTop();
        _actionBox?.SetRows(rows);
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
        _selectedAction = _selectedAction == action ? null : action;

        RebuildActionList();
        SyncTargetHighlight();

        if (_selectedAction != null)
            _editor.SetStatus($"Target: {_selectedAction.Describe()}.");
    }

    private readonly List<GameObject> _targetBoxes = [];
    private readonly List<Bounds> _targetBounds = [];

    private static readonly Color TargetColour = new(0.35f, 1f, 0.4f, 1f);

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
            case TriggerActionType.PlayObjectAnimation:
            {
                var go = ResolveTargetCached(action, () => TriggerActions.ResolveObject(action.Target));
                if (go != null && MapEditorGizmos.TryGetBounds(go, out var bounds)) into.Add(bounds);
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

            case TriggerActionType.PlayObjectAnimation:
                _pickingObject = true;
                _editor.SetStatus("Click the object to animate - it needs a spine.");
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

            case TriggerActionType.CameraShake:
                OpenTargets(TargetStage.Shake, TriggerCameraActions.ShakeLabels);
                break;

            case TriggerActionType.PlayCutscene:
            {
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

            case TriggerActionType.CameraOffsetReset:
            case TriggerActionType.CameraZoomReset:
            case TriggerActionType.ReturnToBase:
            case TriggerActionType.HubSpawnPoint:
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

            case TriggerActionType.OpenWorldMap:
            {
                var maps = CTWorldMapSerialization.ListNames();
                if (maps.Count == 0)
                {
                    _editor.SetStatus("No world maps: save one, or drop a folder in CustomWorldMaps.",
                        StatusSeverity.Warning);
                    return;
                }

                _targetKeys.Clear();
                foreach (var map in maps) _targetKeys.Add(map);

                OpenTargets(TargetStage.WorldMap, maps);
                break;
            }

            case TriggerActionType.ChangeMusic:
            {
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

    private static readonly string[] AnimationModes = ["Play once", "Loop 2 seconds", "Loop 5 seconds", "Loop 10 seconds"];
    private bool _pendingLoop;
    private float _pendingDuration;

    private static readonly string[] AnimationEndModes = ["Resume original animation on end", "Freeze last frame on end"];
    private static readonly float[] AnimationDurations = [0f, 2f, 5f, 10f];

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
                _pendingAnimation = value;
                OpenTargets(TargetStage.AnimationMode, AnimationModes);
                break;

            case TargetStage.ObjectAnimation:
                _pendingAnimation = value;
                OpenTargets(TargetStage.ObjectAnimationMode, AnimationModes);
                break;

            case TargetStage.ObjectAnimationMode:
                _pendingLoop = index > 0;
                _pendingDuration = index >= 0 && index < AnimationDurations.Length ? AnimationDurations[index] : 0f;
                OpenTargets(TargetStage.ObjectAnimationEnd, AnimationEndModes);
                break;

            case TargetStage.ObjectAnimationEnd:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.PlayObjectAnimation,
                    Target = _pendingTarget ?? "",
                    Subtext = _pendingAnimation,
                    Position = _pendingPosition,
                    Loop = _pendingLoop,
                    Duration = _pendingDuration,
                    FreezeAtEnd = index > 0
                });
                _pendingAnimation = null;
                _pendingTarget = null;
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

            case TargetStage.WorldMap:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.OpenWorldMap,
                    Target = index >= 0 && index < _targetKeys.Count ? _targetKeys[index] : value
                });
                break;

            case TargetStage.Music:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.ChangeMusic,
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

            case TargetStage.Shake:
                _pendingType = TriggerActionType.CameraShake;
                _pendingTarget = value;
                _pendingPosition = new Vector3(TriggerCameraActions.ShakeStrength(index), 0f, 0f);
                OpenTargets(TargetStage.Seconds, SecondsLabels);
                break;

            case TargetStage.Seconds:
                FinishTimedAction(index >= 0 && index < SecondsValues.Length
                    ? SecondsValues[index]
                    : 2f);
                break;
        }
    }

    private void FinishTimedAction(float seconds)
    {
        switch (_pendingType)
        {
            case TriggerActionType.Wait:
                AddAction(new TriggerAction { Type = TriggerActionType.Wait, Duration = seconds });
                break;

            case TriggerActionType.CameraShake:
                AddAction(new TriggerAction
                {
                    Type = TriggerActionType.CameraShake,
                    Target = _pendingTarget ?? "",
                    Amount = _pendingPosition.x,
                    Duration = seconds
                });
                _pendingTarget = null;
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

    public bool HandleEscape()
    {
        if (_capturingOffset)
        {
            _capturingOffset = false;
            _editor.SetStatus("Camera offset cancelled.");
            return true;
        }

        if (_pickingObject)
        {
            _pickingObject = false;
            _stage = TargetStage.None;
            UpdateActionControls();
            _editor.SetStatus("Target pick cancelled.");
            return true;
        }

        return false;
    }

    private void TickOffsetCapture()
    {
        if (!_capturingOffset) return;

        if (!Input.GetKeyDown(KeyCode.V)) return;

        var offset = _editor.CameraFocus - _offsetOrigin;
        offset.z = 0f;
        _capturingOffset = false;

        AddAction(new TriggerAction { Type = TriggerActionType.CameraOffset, Position = offset });
    }

    // ---- caption text ---------------------------------------------------------------------------

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

        if (picked != null && picked.GetComponentInParent<CTMapTrigger>() != null) picked = null;

        if (picked == null)
        {
            _editor.SetStatus("Nothing there - click an object.", StatusSeverity.Warning);
            return false;
        }

        _pickingObject = false;

        if (_pendingType == TriggerActionType.PlayObjectAnimation)
        {
            var animations = TriggerActions.ObjectAnimationNames(picked);
            if (animations.Count == 0)
            {
                _pickingObject = true;
                _editor.SetStatus($"'{picked.name}' has no spine animations - pick another object.",
                    StatusSeverity.Warning);
                return false;
            }

            _pendingTarget = TriggerActions.PathOf(picked);
            _pendingPosition = picked.transform.position;
            OpenTargets(TargetStage.ObjectAnimation, animations);
            return true;
        }

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
        ("Ctrl + Drag", "Copy trigger and its actions"),
        ("Del", "Delete selected"),
        ("V", "Set camera offset while framing"),
        ("Esc", "Cancel target pick")
    ];

    public void OnExit()
    {
        ShowGizmos(false);
        Select(null);
        _cloneDragging = false;
        _pickingObject = false;
        _capturingOffset = false;
        _stage = TargetStage.None;
        ClearTargetHighlight();
        UpdateActionControls();
    }

    public void OnUpdate()
    {
        Prune();

        if (HandleCloneDrag()) return;

        if (Input.GetKeyDown(KeyCode.Delete)) DeleteSelected();

        TickOffsetCapture();

        if (Input.GetMouseButtonDown(0) && !_capturingOffset && !_editor.PointerOverUi())
        {
            var world = _editor.MouseWorld();

            if (_pickingObject)
            {
                TryPickObject(world);
                return;
            }

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                BeginClone(world);
                return;
            }

            var hit = PickAt(world);
            if (hit != null) Select(hit);
            else Select(CreateTrigger(world, DefaultWidth, DefaultHeight), placed: true);
        }

        SyncHandles();
        SyncTargetHighlight();
    }

    // ---- cloning ------------------------------------------------------------------------------

    private bool _cloneDragging;
    private Vector3 _cloneGrabOffset;

    internal bool BeginCloneDrag(Vector3 world) => CloneFrom(_selected, world);

    private void BeginClone(Vector3 world)
    {
        var source = _selected != null && _selected.WorldRect.Contains(world) ? _selected : PickAt(world);

        if (source == null)
        {
            _editor.SetStatus("Ctrl-click: no trigger here to copy.");
            return;
        }

        CloneFrom(source, world);
    }

    private bool CloneFrom(CTMapTrigger source, Vector3 world)
    {
        if (source == null) return false;

        var clone = CreateTrigger(source.transform.position, source.Size.x, source.Size.y,
            id: null, action: source.Action, once: source.Once,
            actions: TriggerActions.ToData(source.Actions),
            lockPlayerControl: source.LockPlayerControl, blocking: source.Blocking);

        if (clone == null) return false;

        foreach (var action in clone.Actions)
            if (string.Equals(action.Target, source.Id, System.StringComparison.Ordinal))
                action.Target = clone.Id;

        Select(clone);
        _cloneDragging = true;
        _cloneGrabOffset = clone.transform.position - world;
        _editor.MarkEdited();

        _editor.SetStatus($"Copied {source.Id} to {clone.Id} " +
                          $"({clone.Actions.Count} action(s)). Release to drop.");
        return true;
    }

    private bool HandleCloneDrag()
    {
        if (!_cloneDragging) return false;

        if (_selected == null || !Input.GetMouseButton(0))
        {
            _cloneDragging = false;
            if (_selected != null) _editor.SetStatus($"Placed {_selected.Id}.");
            SyncHandles();
            return false;
        }

        var target = _editor.MouseWorld() + _cloneGrabOffset;
        _selected.transform.position = new Vector3(target.x, target.y, 0f);
        _selected.Refresh();

        SyncHandles();
        return true;
    }

    // ---- placement --------------------------------------------------------------------------

    public CTMapTrigger CreateTrigger(Vector3 position, float width, float height,
        string id = null, string action = "", bool once = true,
        List<MapTriggerActionData> actions = null, bool lockPlayerControl = true,
        bool blocking = false)
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
        trigger.Blocking = blocking;
        trigger.Actions.AddRange(TriggerActions.FromData(actions, trigger.Id));
        trigger.Size = new Vector2(Mathf.Max(0.5f, width), Mathf.Max(0.5f, height));
        trigger.Refresh();
        trigger.ShowGizmo(_gizmosVisible);

        _triggers.Add(trigger);
        Net.EditorIds.Adopt(go, trigger.Id);

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
        // Two players placing triggers at once must not both mint "Trigger3": the guest's carry a
        // letter so the two sequences never meet.
        var suffix = Net.EditorNet.Enabled && !Net.EditorNet.IsHost ? "g" : "";

        string candidate;
        do
        {
            candidate = "Trigger" + _nextId++ + suffix;
        } while (_triggers.Exists(t => t != null && t.Id == candidate));

        return candidate;
    }

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

    public IReadOnlyList<CTMapTrigger> Triggers => _triggers;

    public CTMapTrigger SelectedTrigger => _selected;

    public void SelectTrigger(CTMapTrigger trigger)
    {
        if (trigger == null || !_triggers.Contains(trigger)) return;
        Select(trigger);
    }

    private void Select(CTMapTrigger trigger, bool placed = false)
    {
        if (trigger != null && trigger != _selected && Net.EditorPresence.LockedByPeer(trigger.gameObject))
        {
            _editor.SetStatus(Net.EditorPresence.LockMessage, StatusSeverity.Warning);
            return;
        }

        if (_selected != null) _selected.SetHighlighted(false);
        _selected = trigger;

        _selectedAction = null;
        SyncTargetHighlight();

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

        _syncingWidgets = true;
        _onceToggle?.SetValue(_selected.Once, notify: false);
        _lockToggle?.SetValue(_selected.LockPlayerControl, notify: false);
        _blockToggle?.SetValue(_selected.Blocking, notify: false);
        _syncingWidgets = false;
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

    private CTMapTrigger _gestureTarget;
    private Vector3 _gesturePosition;
    private Vector2 _gestureSize;

    internal void BeginGesture()
    {
        _gestureTarget = _selected;
        if (_selected == null) return;

        _gesturePosition = _selected.transform.position;
        _gestureSize = _selected.Size;
    }

    internal void EndGesture(string label)
    {
        var target = _gestureTarget;
        _gestureTarget = null;
        if (target == null) return;

        var position = _gesturePosition;
        var size = _gestureSize;

        if (target.transform.position == position && target.Size == size) return;

        // A wall that moved is a wall the enemies have to path around somewhere else.
        if (target.Blocking) SceneRefs.RescanNavigation();

        _editor.MarkEdited();
        _editor.History.Push($"{label} {target.Id}", () =>
        {
            if (target == null) return false;

            target.transform.position = position;
            target.Size = size;
            target.Refresh();
            if (_selected == target) PushSelectionToPanel();
            return true;
        });
    }

    // ---- bookkeeping ---------------------------------------------------------------------------

    private void Prune()
    {
        for (var i = _triggers.Count - 1; i >= 0; i--)
            if (_triggers[i] == null) _triggers.RemoveAt(i);
    }

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

        CTMapTrigger.ResetSequenceState();
        RebuildActionList();
        UpdateActionControls();
    }

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
            map.Triggers.Add(Describe(trigger));
        }
    }
}

public class TriggerHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
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

    private bool _cloning;

    public void OnBeginDrag(PointerEventData eventData)
    {
        _cloning = false;
        if (_tool == null || _editor == null) return;

        var world = _editor.ScreenToWorld(eventData.position);

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            _cloning = _tool.BeginCloneDrag(world);
            if (_cloning) return;
        }

        _tool.BeginGesture();
        _grabOffset = _mode == Mode.Move ? _tool.SelectedPosition - world : Vector3.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null || _cloning) return;
        _tool.DragTo(_mode, _editor.ScreenToWorld(eventData.position), _grabOffset);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_tool == null || _cloning) return;
        _tool.EndGesture(_mode == Mode.Move ? "move" : "resize");
    }
}
