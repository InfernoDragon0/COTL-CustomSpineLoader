using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

// A box the player can step into.
//
// Detection runs two ways on purpose, because either alone has a hole in it:
//
//  - A BoxCollider2D marked isTrigger, with an OnTriggerEnter2D that only accepts a collider
//    carrying PlayerFarming - vanilla's own rule (TriggerCallback). This never fires unless the
//    two objects' layers are enabled against each other in the project's collision matrix, and
//    one of them has a Rigidbody2D, neither of which a runtime-created object can guarantee.
//  - A point-in-rectangle poll against every live player. This needs nothing from physics, and
//    is what actually makes the volume work in a room the editor built.
//
// Both paths funnel into Fire(), which is edge-triggered, so a trigger never fires twice for one
// entry no matter which path saw it first.
//
// Firing runs the trigger's action sequence (TriggerActions) and raises Entered for anything else
// that wants to know. The gizmo flashes green as it fires and can be left visible during play, so
// "did it fire?" is answerable without reading the log.
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
        _outline.material = new Material(Shader.Find("Sprites/Default"));
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

        // A trigger that deferred (another sequence owns the players) is NOT marked as entered,
        // so it tries again next frame instead of being swallowed by one that happened to fire
        // first. Fire is cheap when it defers.
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

    // Every player is tested - both halves of a coop pair, plus Instance, which is the only one
    // populated when coop features are off (PlayerFarming only adds itself to `players` when
    // EnableCoopFeatures is set). The body is used where there is one: a player's pivot sits at
    // their feet, so brushing the edge of a volume should count as stepping into it.
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

        // Deferred rather than dropped: two sequences would each be taking and handing back
        // control of the same players, and whichever finished first would unfreeze them in the
        // middle of the other's scene. Walking through two volumes at once is easy to do by
        // accident, and losing one of the scenes to it would look like a broken trigger.
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

        // Hosted on the editor rather than on this component: deleting the volume (or clearing
        // the room) mid-sequence would otherwise kill the coroutine with the players still
        // frozen. The editor object lives for the whole scene.
        var host = RuntimeMapEditor.Active != null ? (MonoBehaviour)RuntimeMapEditor.Active : this;
        _sequenceOwner = this;
        host.StartCoroutine(SequenceRoutine());
    }

    private System.Collections.IEnumerator SequenceRoutine()
    {
        // The sequence copies what it needs up front, so it survives this component being
        // destroyed - but the control lock has to be released either way, hence the finally-like
        // clear below rather than one inside Run.
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

// Places and edits the trigger volumes above.
//
// Click empty space to drop one, click an existing one to select it, then drag its centre to move
// it or its corner to resize it. Volumes are only drawn while this tool is open - they are
// authoring gizmos, not room art.
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

        // ---- action sequence ------------------------------------------------------------
        ui.CreateHeader(panel, "- Actions -", 19);

        // Rows are rebuilt whenever the list changes, so they live in their own container - the
        // panel column holds fixed controls above and below them.
        _actionList = CreateActionListContainer(panel);

        _addDropdown = ui.CreateDropdown(panel, "Add action", ActionLabels, OnAddActionType);

        // One dropdown does every follow-up question (which trigger? which NPC? once or loop?),
        // re-populated and re-opened per stage. A separate row per question would have left three
        // dead controls sitting in the panel whenever nothing was being added.
        _targetDropdown = ui.CreateDropdown(panel, "Target", System.Array.Empty<string>(), OnTargetChosen);

        // Nothing is wired to a trigger yet, so the box lighting up green is the only evidence
        // one fired. Left on by default for exactly that reason.
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
        ui.CreateButton(panel, "Clear All Triggers", () =>
        {
            var removed = ClearPlaced();
            _editor.SetStatus($"Removed {removed} trigger(s).");
        });

        RebuildActionList();
    }

    private MapEditorToggle _onceToggle;
    private MapEditorToggle _lockToggle;

    // ---- action sequence editing -----------------------------------------------------------

    private MapEditorUI _ui;
    private RectTransform _actionList;
    private MapEditorDropdown _addDropdown;
    private MapEditorDropdown _targetDropdown;

    // Index-aligned with TriggerActionType.
    private static readonly string[] ActionLabels =
    [
        "Move players to trigger",
        "Move players to object",
        "Talk to custom NPC",
        "Play animation on players"
    ];

    // Which question the shared target dropdown is currently asking.
    private enum TargetStage
    {
        None,
        Trigger,
        Npc,
        Animation,
        AnimationMode
    }

    private TargetStage _stage;
    private string _pendingAnimation;

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
        plate.color = new Color(0f, 0f, 0f, 0.55f);
        plate.raycastTarget = false;

        // Numbered, because the order is the whole point of a sequence.
        var label = _ui.CreateLabel(row.transform, $"{index + 1}. {action.Describe()}", 15);
        var labelText = label.GetComponent<TMPro.TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        labelText.margin = new Vector4(8f, 0f, 0f, 0f);

        var remove = _ui.CreateButton(row.transform, "X", () => RemoveAction(action), RowHeight - 4f);

        // CreateButton's row layout flexes to fill a column; in a horizontal row that would push
        // the label out entirely.
        var removeElement = remove.GetComponent<LayoutElement>();
        removeElement.preferredWidth = 34f;
        removeElement.minWidth = 34f;
        removeElement.flexibleWidth = 0f;
    }

    private const float RowHeight = 30f;

    private void RemoveAction(TriggerAction action)
    {
        if (_selected == null || action == null) return;

        _selected.Actions.Remove(action);
        RebuildActionList();
        UpdateInfo();
        _editor.SetStatus($"{_selected.Id}: removed action ({_selected.Actions.Count} left).");
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
    }

    private void OnAddActionType(int index, string label)
    {
        _pickingObject = false;
        _stage = TargetStage.None;

        if (_selected == null)
        {
            _editor.SetStatus("Select a trigger first.", StatusSeverity.Warning);
            return;
        }

        switch ((TriggerActionType)index)
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
        }
    }

    private void OpenTargets(TargetStage stage, IList<string> options)
    {
        _stage = stage;
        _targetDropdown.SetOptions(options);
        _targetDropdown.Open();
    }

    // Loop lengths rather than a free number: the only thing an author actually wants to say is
    // "hold this pose for a beat / a while", and a typed seconds field is another modal prompt.
    private static readonly string[] AnimationModes = ["Play once", "Loop 2 seconds", "Loop 5 seconds", "Loop 10 seconds"];
    private static readonly float[] AnimationDurations = [0f, 2f, 5f, 10f];

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
        }
    }

    // The world click that names a Move-to-object target. Physics first, then the smallest
    // visible renderer under the cursor - the same order the select tool picks in, because the
    // room is full of invisible trigger colliders that would otherwise win every click.
    private bool TryPickObject(Vector3 world)
    {
        GameObject picked = null;

        var hit = Physics2D.OverlapPoint(world);
        if (hit != null && hit.GetComponentInParent<CTMapTrigger>() == null) picked = hit.gameObject;

        if (picked == null)
        {
            var bestSize = float.MaxValue;
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is ParticleSystemRenderer) continue;
                if (renderer.GetComponentInParent<CTMapTrigger>() != null) continue;

                var bounds = renderer.bounds;
                if (world.x < bounds.min.x || world.x > bounds.max.x) continue;
                if (world.y < bounds.min.y || world.y > bounds.max.y) continue;

                var size = bounds.size.x * bounds.size.y;
                if (size >= bestSize) continue;
                bestSize = size;
                picked = renderer.gameObject;
            }
        }

        if (picked == null)
        {
            _editor.SetStatus("Nothing there - click an object.", StatusSeverity.Warning);
            return false;
        }

        AddAction(new TriggerAction
        {
            Type = TriggerActionType.MovePlayersToObject,
            Target = TriggerActions.PathOf(picked),
            // Kept as the fallback for when the object is not in the room on a later load.
            Position = picked.transform.position
        });

        _pickingObject = false;
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
        ("Esc", "Cancel target pick")
    ];

    public void OnExit()
    {
        ShowGizmos(false);
        Select(null);
        _pickingObject = false;
        _stage = TargetStage.None;
    }

    public void OnUpdate()
    {
        Prune();

        if (Input.GetKeyDown(KeyCode.Delete)) DeleteSelected();

        // Escape gets out of a mis-started object pick without placing anything.
        if (_pickingObject && Input.GetKeyDown(KeyCode.Escape))
        {
            _pickingObject = false;
            _editor.SetStatus("Target pick cancelled.");
        }

        if (Input.GetMouseButtonDown(0) && !_editor.PointerOverUi())
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

        if (_selected == null)
        {
            SetHandlesActive(false);
            if (_info != null) _info.text = "No trigger selected";
            RebuildActionList();
            return;
        }

        _selected.SetHighlighted(true);
        PushSelectionToPanel();

        // Only on selection, never from the drag path below: rebuilding the rows every frame of a
        // resize would destroy and recreate the whole list continuously.
        RebuildActionList();
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

        // A sequence belonging to the room being replaced must not keep the global lock (or the
        // players' InActive state) into the new one.
        CTMapTrigger.ResetSequenceState();
        RebuildActionList();
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
