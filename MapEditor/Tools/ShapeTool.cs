using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class ShapeTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts
{
    public string Name => "Shape";

    private readonly RuntimeMapEditor _editor;
    private readonly List<SpriteShapeController> _shapes = [];
    private readonly List<GameObject> _handles = [];
    private readonly List<SpriteShape> _profiles = [];

    private SpriteShapeController _active;
    private SpriteShapeController _template;
    private Canvas _handleCanvas;

    private int _profileIndex;
    private int _colliderDetail = 16;
    private float _colliderOffset;
    private bool _openEnded;
    private bool _showCollision;
    private bool _toolActive;
    private bool _useVanillaFloor = true;
    private GameObject _collisionOverlay;
    private GameObject _centerHandle;

    private GameObject _collisionToggleRow;
    private MapEditorToggle _collisionToggle;
    private GameObject _vanillaFloorRow;

    // Spline.InsertPointAt throws if a new point lands on an existing one.
    private const float MinPointSpacing = 0.25f;

    public ShapeTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _ui = ui;

        _profileDropdown = ui.CreateDropdown(panel, "Select a profile", [], (index, _) => SelectProfileAt(index));

        // No click-to-add switch: Ctrl-click adds a point. A mode that had to be turned on, used,
        // and remembered to turn off again was a way of asking "did you mean that click?" one step
        // too early - and left on, every stray click in the room grew the shape.

        ui.CreateToggle(panel, "Show Collision", _showCollision, v =>
        {
            _showCollision = v;
            RefreshCollisionOverlay();
        });

        _collisionToggleRow = ui.CreateToggle(panel, "Shape Has Collision", true, SetActiveShapeCollision);
        _collisionToggle = _collisionToggleRow.GetComponent<MapEditorToggle>();

        _vanillaFloorRow = ui.CreateToggle(panel, "Vanilla Floor Collision", _useVanillaFloor,
            SetVanillaFloorCollision);

        ui.CreateToggle(panel, "Open Ended", _openEnded, v =>
        {
            _openEnded = v;
            if (_active != null)
            {
                _active.spline.isOpenEnded = v;
                CommitShape(_active);
            }
        });

        ui.CreateSlider(panel, "Collider Detail", 4f, 64f, _colliderDetail, v =>
        {
            _colliderDetail = Mathf.RoundToInt(v);
            ApplyColliderSettings();
        });

        ui.CreateSlider(panel, "Collider Offset", -1f, 1f, _colliderOffset, v =>
        {
            _colliderOffset = v;
            ApplyColliderSettings();
        });

        // The list goes last, with the button that adds to it. Everything above acts on the shape
        // the list picked, so the list reading top-down as "settings, then the things they apply
        // to" was backwards - and a list that grows is the one thing on the panel that should not
        // be pushing the fixed controls around.
        //
        // It is a list rather than a dropdown because it does the work of three controls: click a
        // row to edit that shape, -/+ to move it behind or in front of its neighbours, X to delete
        // it. A dropdown could only answer the first, and the buttons beside it acted on "whatever
        // is selected" - so you had to read the dropdown to learn what they were about to do.
        ui.CreateHeader(panel, "- Shapes -", 19);
        ui.CreateButton(panel, "New Shape (screen centre)", SpawnShape);

        _shapeBox = ui.CreateScrollBox(panel, "ShapeList", ShapeListHeight, RowHeight);
        _shapeList = _shapeBox.Content;
        RefreshShapeList();

        // No depth buttons and no Center View: the purple node on the shape drags Z the way the
        // Select tool's does, and the camera controls already go where the author wants to look.
    }

    // Must run BEFORE the loader clears the room: template and profiles come from scene objects.
    public void PrepareForLoad()
    {
        CaptureTemplate();
        CollectProfiles();
    }

    // Show Collision goes off too, so a stale toggle cannot redraw over the loaded map.
    public void ResetTracking()
    {
        _shapes.Clear();
        _active = null;
        _showCollision = false;
        ClearHandles();
        if (_collisionOverlay != null)
        {
            Object.Destroy(_collisionOverlay);
            _collisionOverlay = null;
        }
    }

    public SpriteShape FindProfile(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_profiles.Count == 0) CollectProfiles();
        foreach (var p in _profiles)
            if (p != null && p.name == name) return p;
        return null;
    }

    public void OnEnter()
    {
        _toolActive = true;
        CaptureTemplate();
        CollectProfiles();

        // In a dungeon or a hub this switch decides whether the room's own floor or the author's
        // shapes carry the collision, and both answers are reasonable. In the base the room's own
        // floor is the player's town, and switching it off would drop everyone standing on it.
        _vanillaFloorRow?.SetActive(RuntimeMapEditor.Context != EditorContext.Base);

        if (_active == null)
            _active = Object.FindObjectOfType<SpriteShapeController>();

        RebuildHandles();
        UpdateLabels();

        if (_active == null && _allShapes.Count > 0) SelectShapeAt(0);

        // OnExit tears the overlay down; rebuild on re-entry.
        RefreshCollisionOverlay();

        _editor.SetStatus("Drag handles to edit the shape.");
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Drag node"),
        ("RMB", "Delete node"),
        ("Ctrl + LMB", "Add node"),
        ("Drag", "Yellow = move, Purple = depth"),
        ("Del", "Delete selected shape")
    ];

    public void OnExit()
    {
        _toolActive = false;
        ClearHandles();
        if (_collisionOverlay != null) Object.Destroy(_collisionOverlay);
        _collisionOverlay = null;
    }

    public void OnUpdate()
    {
        SyncHandlePositions();

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            DeleteActiveShape();
            return;
        }

        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;
        if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;

        AddPointAt(_editor.MouseWorld());
    }

    // Inactive clone so new shapes can be created after Clear Terrain removed every original.
    private void CaptureTemplate()
    {
        if (_template != null) return;

        var source = FindSourceShape();
        if (source == null) return;

        var clone = Object.Instantiate(source.gameObject, _editor.transform);
        clone.name = "MapEditor_ShapeTemplate";
        clone.SetActive(false);
        _template = clone.GetComponent<SpriteShapeController>();
    }

    private static SpriteShapeController FindSourceShape()
    {
        // Candidates in preference order; a filled one wins over any of them. New shapes are clones
        // of this, so a template taken from an edging or a rope profile would draw a hollow outline
        // where a dungeon's island floor gives a solid piece of ground - which is what the tool is
        // for. Woolhaven's own terrain is exactly that kind of unfilled decoration.
        var candidates = new List<SpriteShapeController>();

        var room = SceneRefs.Room;
        if (room != null)
        {
            if (room.RoomSpriteShape != null) candidates.Add(room.RoomSpriteShape);
            if (room.SpriteShapeControllers != null)
                foreach (var c in room.SpriteShapeControllers)
                    if (c != null) candidates.Add(c);
        }

        foreach (var c in Object.FindObjectsOfType<SpriteShapeController>())
            if (c != null) candidates.Add(c);

        // FindObjectsOfType only sees active objects, and in the base every shape outside the
        // current room is switched off with the room that owns it. Scene objects only - this sweep
        // also reaches assets.
        foreach (var c in Resources.FindObjectsOfTypeAll<SpriteShapeController>())
            if (c != null && c.gameObject.scene.IsValid()) candidates.Add(c);

        // Ground before water: a water profile's fill is the water surface, which over an emptied
        // room draws as nothing at all - the hollow shapes a hub used to get.
        foreach (var candidate in candidates)
            if (DrawsAFill(candidate) && !IsWater(candidate.spriteShape))
            {
                Plugin.Log.LogInfo("MapEditor: shape template from '" + candidate.name +
                                   "' (profile '" + ProfileName(candidate) + "', filled).");
                return candidate;
            }

        foreach (var candidate in candidates)
            if (DrawsAFill(candidate))
            {
                Plugin.Log.LogInfo("MapEditor: shape template from '" + candidate.name +
                                   "' (profile '" + ProfileName(candidate) + "', filled with water).");
                return candidate;
            }

        var fallback = candidates.Count > 0 ? candidates[0] : null;
        if (fallback != null)
            Plugin.Log.LogWarning("MapEditor: no filled sprite shape in this scene; new shapes copy '" +
                                  fallback.name + "' (profile '" + ProfileName(fallback) +
                                  "'), which draws edges only.");
        return fallback;
    }

    // A shape draws its fill from two things: a profile carrying a fill texture, and a fill
    // material in the renderer's first slot (the second is the edges). A clone of an edge-only
    // shape has neither, and comes out as an outline around nothing.
    private void EnsureFill(SpriteShapeController ctrl)
    {
        if (ctrl == null) return;

        if (ctrl.spriteShape == null || ctrl.spriteShape.fillTexture == null || IsWater(ctrl.spriteShape))
        {
            var filled = FirstFilledProfile();
            if (filled != null && filled != ctrl.spriteShape)
            {
                ctrl.spriteShape = filled;
                Plugin.Log.LogInfo($"MapEditor: new shape given the filled profile '{filled.name}'.");
            }
            else
            {
                Plugin.Log.LogWarning("MapEditor: nothing loaded carries a fill texture, so this " +
                                      "shape draws its edges only. Pick another profile below.");
            }
        }

        var renderer = ctrl.GetComponent<UnityEngine.U2D.SpriteShapeRenderer>();
        if (renderer == null) return;

        var materials = renderer.sharedMaterials;
        if (materials.Length >= 2 && materials[0] != null) return;

        var fill = FindFillMaterial();
        if (fill == null) return;

        // Slot 0 fill, slot 1 edges - the order the renderer draws them in.
        var edge = materials.Length > 0 && materials[materials.Length - 1] != null
            ? materials[materials.Length - 1]
            : fill;

        renderer.sharedMaterials = [fill, edge];
        Plugin.Log.LogInfo("MapEditor: new shape given a fill material.");
    }

    // The shoelace sum: negative area means the points run clockwise.
    private static bool WindsClockwise(Spline spline)
    {
        if (spline == null || spline.GetPointCount() < 3) return false;

        var area = 0f;
        var count = spline.GetPointCount();

        for (var i = 0; i < count; i++)
        {
            var a = spline.GetPosition(i);
            var b = spline.GetPosition((i + 1) % count);
            area += a.x * b.y - b.x * a.y;
        }

        return area < 0f;
    }

    private static bool IsWater(SpriteShape profile) =>
        profile != null && profile.name.IndexOf("water", System.StringComparison.OrdinalIgnoreCase) >= 0;

    // Profiles are assets, so this reaches the ones belonging to rooms that are switched off - the
    // dungeon and base island profiles among them. Ground first, water only as a last resort.
    private SpriteShape FirstFilledProfile()
    {
        if (_profiles.Count == 0) CollectProfiles();

        SpriteShape water = null;

        foreach (var profile in _profiles)
        {
            if (profile == null || profile.fillTexture == null) continue;
            if (!IsWater(profile)) return profile;
            water ??= profile;
        }

        foreach (var profile in Resources.FindObjectsOfTypeAll<SpriteShape>())
        {
            if (profile == null || profile.fillTexture == null) continue;
            if (!IsWater(profile)) return profile;
            water ??= profile;
        }

        return water;
    }

    // Borrowed from whatever shape in the scene already draws one; the biome's own material when
    // the room offers it.
    private static Material FindFillMaterial()
    {
        var declared = SceneRefs.ShapeMaterial;
        if (declared != null) return declared;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<UnityEngine.U2D.SpriteShapeRenderer>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;

            var materials = candidate.sharedMaterials;
            if (materials.Length >= 2 && materials[0] != null) return materials[0];
        }

        return null;
    }

    private static string ProfileName(SpriteShapeController ctrl) =>
        ctrl != null && ctrl.spriteShape != null ? ctrl.spriteShape.name : "none";

    // Two ways a shape can carry a fill: the profile's own fill texture, or a fill material on the
    // renderer (the renderer's first material slot is the fill, the second the edges).
    private static bool DrawsAFill(SpriteShapeController ctrl)
    {
        if (ctrl == null || ctrl.spriteShape == null) return false;
        if (ctrl.spriteShape.fillTexture != null) return true;

        var renderer = ctrl.GetComponent<UnityEngine.U2D.SpriteShapeRenderer>();
        return renderer != null && renderer.sharedMaterials.Length > 1 &&
               renderer.sharedMaterials[0] != null;
    }

    // DecorationList does not always populate every slot, so live scene shapes are scanned too.
    private void CollectProfiles()
    {
        _profiles.Clear();

        void Add(SpriteShape s)
        {
            if (s != null && !_profiles.Contains(s)) _profiles.Add(s);
        }

        var deco = SceneRefs.Decorations;
        if (deco != null)
        {
            Add(deco.SpriteShape);
            Add(deco.SpriteShapeSecondary);
            Add(deco.SpriteShapeBack);
        }

        // Custom profiles before the global sweep, so their names resolve to the custom asset.
        foreach (var custom in CustomShapeProfiles.All)
            Add(custom);

        foreach (var ctrl in Object.FindObjectsOfType<SpriteShapeController>())
            Add(ctrl.spriteShape);

        if (_template != null) Add(_template.spriteShape);

        // FindObjectsOfTypeAll reaches assets, not just scene objects.
        foreach (var shape in Resources.FindObjectsOfTypeAll<SpriteShape>())
            Add(shape);

        Plugin.Log.LogInfo($"MapEditor: {_profiles.Count} sprite shape profile(s) available.");
    }

    private void SyncProfileIndex()
    {
        if (_active == null || _active.spriteShape == null) return;

        var index = _profiles.IndexOf(_active.spriteShape);
        if (index >= 0) _profileIndex = index;
    }

    private void SpawnShape()
    {
        // The composite, not the content root: a shape parented anywhere else keeps its own solid
        // collider and pushes the player off instead of being ground to stand on.
        var composite = SceneRefs.EnsureRoomComposite();
        var root = composite != null ? composite.transform : SceneRefs.ContentRoot;
        if (root == null)
        {
            _editor.SetStatus("No room content root.", StatusSeverity.Error);
            return;
        }

        CaptureTemplate();
        if (_template == null)
        {
            _editor.SetStatus("No sprite shape to copy from.", StatusSeverity.Error);
            return;
        }

        // Screen centre, not the cursor - the cursor is over the button that was just clicked.
        var center = _editor.ScreenToWorld(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

        var go = Object.Instantiate(_template.gameObject, root);
        go.name = "CultTweaker_Shape";
        go.AddComponent<CTEditorShape>();
        go.SetActive(true);
        go.transform.position = center;

        var ctrl = go.GetComponent<SpriteShapeController>();

        // The template is whatever the room had to copy, and a room can have nothing but edging -
        // Woolhaven does. A new shape is meant to be a piece of ground, so if what was copied
        // cannot draw a fill it is given a profile and a material that can.
        EnsureFill(ctrl);

        // The template carries the source's baked colliders; strip so this shape bakes its own.
        foreach (var inherited in go.GetComponents<Collider2D>())
            Object.DestroyImmediate(inherited);

        // Wound the same way round as the shape this was copied from. A sprite shape fills the side
        // its spline turns towards, so a square wound against the template's own direction comes
        // out inside-out: edges on the inside, fill spread over everything outside it.
        var clockwise = WindsClockwise(_template.spline);

        var spline = ctrl.spline;
        spline.Clear();
        var corners = clockwise
            ?
            [
                new Vector3(-3f, -3f, 0f),
                new Vector3(-3f, 3f, 0f),
                new Vector3(3f, 3f, 0f),
                new Vector3(3f, -3f, 0f)
            ]
            : new[]
            {
                new Vector3(-3f, -3f, 0f),
                new Vector3(3f, -3f, 0f),
                new Vector3(3f, 3f, 0f),
                new Vector3(-3f, 3f, 0f)
            };

        for (var i = 0; i < corners.Length; i++)
        {
            spline.InsertPointAt(i, corners[i]);
            spline.SetTangentMode(i, ShapeTangentMode.Linear);
        }
        spline.isOpenEnded = _openEnded;

        ctrl.autoUpdateCollider = true;
        ctrl.colliderDetail = _colliderDetail;
        ctrl.colliderOffset = _colliderOffset;

        // CommitShape only maintains collision on shapes that already carry a collider.
        EnsureCollider(ctrl);

        _shapes.Add(ctrl);
        _active = ctrl;

        CommitShape(ctrl);
        RebuildHandles();
        UpdateLabels();
        _editor.SetStatus("Shape created.");
    }

    private MapEditorDropdown _profileDropdown;
    private RectTransform _shapeList;
    private MapEditorScrollBox _shapeBox;
    private MapEditorUI _ui;

    // Every shape in the room, ordered the way they are drawn.
    private readonly List<SpriteShapeController> _allShapes = [];

    private void CollectShapes()
    {
        _allShapes.Clear();
        foreach (var s in _shapes)
            if (s != null) _allShapes.Add(s);
        foreach (var s in Object.FindObjectsOfType<SpriteShapeController>())
            if (s != null && s != _template && !_allShapes.Contains(s)) _allShapes.Add(s);

        // Back to front, so the row at the bottom of the list is the shape drawn over the rest.
        // Sorted by the value that actually decides it - see SortOrderOf.
        _allShapes.Sort((a, b) => SortOrderOf(a).CompareTo(SortOrderOf(b)));
    }

    // **Z does not layer sprite shapes.** The game stacks them with the renderer's sorting layer
    // and order in layer, and leaves Z at a rounding nudge: GenerateRoom.CreateSpriteShape builds
    // every room shape at z = 0.0001 and then sets `sortingLayerName = "Ground"` and
    // `sortingOrder = -1`, and where the game needs to know which shape is on top at a point it
    // compares `spriteShapeRenderer.sortingLayerID`. Unity draws in that order too - sorting layer,
    // then order in layer, and only then camera distance - so a Z nudge reaches the weakest
    // mechanism available, and every shape the tool makes is a clone of one template sharing one
    // layer and one order. That is why moving a shape in Z barely did anything.
    private static int SortOrderOf(SpriteShapeController ctrl)
    {
        var renderer = ctrl != null ? ctrl.spriteShapeRenderer : null;
        return renderer != null ? renderer.sortingOrder : 0;
    }

    private static void SetSortOrder(SpriteShapeController ctrl, int order)
    {
        var renderer = ctrl != null ? ctrl.spriteShapeRenderer : null;
        if (renderer != null) renderer.sortingOrder = order;
    }

    // The layer the game's own room shapes are on, and where ours have to be too.
    private const string GroundLayer = "Ground";

    // Ground is ground, whatever it was cloned from.
    //
    // A shape is made by copying a live one, so it inherits that one's sorting - fine in a dungeon,
    // where the only shapes to copy are the room's own. A town has other kinds, and a copy of one of
    // those can land on the same sorting layer and order as the structures standing on it. Sorting
    // layer decides first, then order in layer, and only then distance - so when the first two tie,
    // the decision falls to a depth difference the game leaves at 0.0001 of a unit. That is a
    // rounding nudge, not a gap, and two surfaces that close flicker over each other as the camera
    // moves. Which is the whole of the terrain-versus-structure fight.
    private static bool _saidWhereGroundWent;

    private static void SettleSorting(SpriteShapeController ctrl)
    {
        var renderer = ctrl != null ? ctrl.spriteShapeRenderer : null;
        if (renderer == null) return;

        if (!HasGroundLayer())
        {
            if (!_saidWhereGroundWent)
            {
                _saidWhereGroundWent = true;
                Plugin.Log.LogWarning("MapEditor: this game has no 'Ground' sorting layer, so " +
                                      "terrain keeps whatever it was copied from and may fight with " +
                                      "structures standing on it.");
            }
            return;
        }

        if (renderer.sortingLayerName == GroundLayer) return;

        if (!_saidWhereGroundWent)
        {
            _saidWhereGroundWent = true;
            Plugin.Log.LogInfo($"MapEditor: terrain copied from a '{renderer.sortingLayerName}' " +
                               $"shape (order {renderer.sortingOrder}); moved to '{GroundLayer}' so " +
                               "structures draw over it.");
        }

        renderer.sortingLayerName = GroundLayer;
    }

    private static bool HasGroundLayer()
    {
        foreach (var layer in SortingLayer.layers)
            if (layer.name == GroundLayer) return true;
        return false;
    }

    private const float RowHeight = 30f;

    // Tall enough to browse a room's terrain in, short enough that the controls above it stay put.
    private const float ShapeListHeight = 300f;

    private void RefreshShapeList()
    {
        if (_shapeList == null || _ui == null) return;

        foreach (Transform child in _shapeList)
            Object.Destroy(child.gameObject);

        CollectShapes();

        if (_allShapes.Count == 0)
        {
            var note = _ui.CreateLabel(_shapeList, "No shapes in this room.", 15,
                TMPro.TextAlignmentOptions.Center);
            note.GetComponent<TMPro.TMP_Text>().color = new Color(1f, 1f, 1f, 0.55f);
        }
        else
        {
            for (var i = 0; i < _allShapes.Count; i++) CreateShapeRow(i, _allShapes[i]);
        }

        _shapeBox?.SetRows(_allShapes.Count);
    }

    private void CreateShapeRow(int index, SpriteShapeController shape)
    {
        var row = new GameObject("Shape_" + index);
        row.transform.SetParent(_shapeList, false);

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
        plate.color = shape == _active
            ? new Color(MapEditorUI.Accent.r, MapEditorUI.Accent.g, MapEditorUI.Accent.b, 0.45f)
            : new Color(0f, 0f, 0f, 0.55f);

        plate.raycastTarget = true;
        var select = row.AddComponent<UnityEngine.UI.Button>();
        select.targetGraphic = plate;
        select.transition = UnityEngine.UI.Selectable.Transition.None;
        select.onClick.AddListener(() =>
        {
            RuntimeMapEditor.Active?.BlockWorldClicks();
            SelectShape(shape);
        });

        var label = _ui.CreateLabel(row.transform,
            $"{shape.name}  [{SortOrderOf(shape)}]  {shape.spline.GetPointCount()} pts", 15);
        var labelText = label.GetComponent<TMPro.TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        labelText.margin = new Vector4(8f, 0f, 0f, 0f);
        labelText.raycastTarget = false;

        RowButton(row, "-", () => NudgeOrder(shape, -1));
        RowButton(row, "+", () => NudgeOrder(shape, 1));
        RowButton(row, "X", () => DeleteShape(shape));
    }

    private void RowButton(GameObject row, string text, System.Action onClick)
    {
        var button = _ui.CreateButton(row.transform, text, onClick, RowHeight - 4f);

        // CreateButton's layout flexes to fill a column; here that would push the label out.
        var element = button.GetComponent<LayoutElement>();
        element.preferredWidth = 30f;
        element.minWidth = 30f;
        element.flexibleWidth = 0f;
    }

    // One step at a time on the pressed shape alone. Deliberately *not* a renumbering of the whole
    // list: the room's own generated shapes are in here too, on orders the biome chose, and
    // rewriting those to tidy up the numbering would restack terrain the author never touched.
    private void NudgeOrder(SpriteShapeController shape, int direction)
    {
        if (shape == null) return;

        SetSortOrder(shape, SortOrderOf(shape) + direction);
        _editor.MarkEdited();
        RefreshShapeList();
        _editor.SetStatus($"{shape.name} draw order {SortOrderOf(shape)} " +
                          (direction < 0 ? "(further back)." : "(further front)."));
    }

    private void SelectShape(SpriteShapeController shape)
    {
        if (shape == null) return;

        _active = shape;
        _openEnded = _active.spline.isOpenEnded;

        RebuildHandles();
        UpdateLabels();
        RefreshCollisionOverlay();
        CenterOnShape();
        _editor.SetStatus($"Editing {_active.name}.");
    }

    private void SelectShapeAt(int index)
    {
        if (index < 0 || index >= _allShapes.Count) return;
        SelectShape(_allShapes[index]);
    }

    private void DeleteActiveShape()
    {
        if (_active == null)
        {
            _editor.SetStatus("No shape selected.");
            return;
        }

        DeleteShape(_active);
    }

    private void DeleteShape(SpriteShapeController shape)
    {
        if (shape == null) return;

        var doomed = shape;

        // A piece of the player's own ground, deleted. Written down before it is destroyed, while
        // there is still something to describe.
        BaseDelta.NoteRemoved(doomed.gameObject);

        _shapes.Remove(doomed);
        if (_active == doomed) _active = null;

        ClearHandles();

        // DestroyImmediate: a deferred destroy leaves the shape in the merged outline until the next change.
        Object.DestroyImmediate(doomed.gameObject);
        SceneRefs.RegenerateRoomCollision();
        BaseGround.RequestRefresh();
        RefreshCollisionOverlay();

        UpdateLabels();
        _editor.MarkEdited();
        _editor.SetStatus("Deleted shape.");
    }

    private void RefreshProfileDropdown()
    {
        if (_profileDropdown == null) return;
        if (_profiles.Count == 0) CollectProfiles();

        var labels = new List<string>(_profiles.Count);
        foreach (var profile in _profiles) labels.Add(profile != null ? profile.name : "(none)");

        _profileDropdown.SetOptions(labels);
        if (_profileIndex >= 0 && _profileIndex < _profiles.Count)
            _profileDropdown.SetSelected(_profileIndex);
    }

    private void SelectProfileAt(int index)
    {
        if (index < 0 || index >= _profiles.Count) return;

        _profileIndex = index;

        if (_active != null)
        {
            _active.spriteShape = _profiles[_profileIndex];
            CommitShape(_active);
        }

        _editor.SetStatus("Profile: " + _profiles[_profileIndex].name);
    }

    // notify: false - selecting a shape must not add or strip a collider as a side effect.
    private void SyncCollisionToggle()
    {
        if (_collisionToggle == null) return;

        _collisionToggle.SetValue(ShapeHasCollision(_active), notify: false);
    }

    private void UpdateLabels()
    {
        SyncCollisionToggle();
        SyncProfileIndex();
        RefreshShapeList();
        RefreshProfileDropdown();
    }

    private void AddPointAt(Vector3 worldPos)
    {
        if (_active == null)
        {
            _editor.SetStatus("No active shape. Spawn one first.");
            return;
        }

        var local = _active.transform.InverseTransformPoint(worldPos);
        var spline = _active.spline;

        for (var i = 0; i < spline.GetPointCount(); i++)
        {
            if (Vector3.Distance(spline.GetPosition(i), local) < MinPointSpacing)
            {
                _editor.SetStatus("Too close to an existing point.", StatusSeverity.Warning);
                return;
            }
        }

        // Insert after the nearest point, not at the end, so the outline stays sensible.
        var insertIndex = NearestPointIndex(spline, local) + 1;
        try
        {
            spline.InsertPointAt(insertIndex, local);
            spline.SetTangentMode(insertIndex, ShapeTangentMode.Linear);
        }
        catch (System.Exception e)
        {
            _editor.SetStatus("Could not add point: " + e.Message, StatusSeverity.Error);
            return;
        }

        CommitShape(_active);
        RebuildHandles();
        UpdateLabels();
    }

    private static int NearestPointIndex(Spline spline, Vector3 local)
    {
        var best = 0;
        var bestDist = float.MaxValue;
        for (var i = 0; i < spline.GetPointCount(); i++)
        {
            var d = Vector3.Distance(spline.GetPosition(i), local);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    public void SetPointWorldPosition(int index, Vector3 worldPos)
    {
        if (_active == null) return;
        var spline = _active.spline;
        if (index < 0 || index >= spline.GetPointCount()) return;

        spline.SetPosition(index, _active.transform.InverseTransformPoint(worldPos));

        // Geometry only while dragging; collision and navigation are rebuilt on release.
        RefreshGeometry(_active);
    }

    public void RemovePoint(int index)
    {
        if (_active == null) return;
        var spline = _active.spline;
        if (index < 0 || index >= spline.GetPointCount()) return;

        // A sprite shape needs at least a triangle to generate geometry.
        if (spline.GetPointCount() <= 3)
        {
            _editor.SetStatus("A shape needs at least 3 points.", StatusSeverity.Warning);
            return;
        }

        spline.RemovePointAt(index);
        CommitShape(_active);
        RebuildHandles();
        UpdateLabels();
    }

    private void ApplyColliderSettings()
    {
        if (_active == null) return;

        _editor.MarkEdited();
        _active.colliderDetail = _colliderDetail;
        _active.colliderOffset = _colliderOffset;
        _active.autoUpdateCollider = true;
        CommitShape(_active);
    }

    private static void EnsureCollider(SpriteShapeController ctrl)
    {
        if (ctrl == null) return;

        if (ctrl.spline.isOpenEnded)
        {
            var poly = ctrl.gameObject.GetComponent<PolygonCollider2D>();
            if (poly != null) Object.DestroyImmediate(poly);
            if (ctrl.gameObject.GetComponent<EdgeCollider2D>() == null)
                ctrl.gameObject.AddComponent<EdgeCollider2D>();
        }
        else
        {
            var edge = ctrl.gameObject.GetComponent<EdgeCollider2D>();
            if (edge != null) Object.DestroyImmediate(edge);
            if (ctrl.gameObject.GetComponent<PolygonCollider2D>() == null)
                ctrl.gameObject.AddComponent<PolygonCollider2D>();
        }

        // Handed to the room's outline in the same breath it is born, before physics has ever
        // stepped over it.
        //
        // A collider is a solid body of its own until something delegates it to the composite, and
        // everything that does so runs a frame later, once the sprite shape's mesh exists. In the
        // editor that gap costs nothing - the clock is stopped. On the way into a base it is a real
        // frame of real physics, and anything standing where the collider appears is shoved off it:
        // that is what pushed the player away from the spawn point when a base with reshaped ground
        // loaded. The geometry catches up at the bake; what matters here is that it never acts alone.
        JoinRoomComposite(ctrl);
    }

    private void RefreshCollisionOverlay()
    {
        if (_collisionOverlay != null)
        {
            Object.Destroy(_collisionOverlay);
            _collisionOverlay = null;
        }

        if (!_showCollision || !_toolActive || !_editor.IsEditing) return;

        _collisionOverlay = new GameObject("MapEditor_CollisionOverlay");
        _collisionOverlay.transform.SetParent(_editor.transform, false);

        // Red: this shape's own contribution.
        if (_active != null)
        {
            var edge = _active.GetComponent<EdgeCollider2D>();
            if (edge != null)
            {
                AddOverlayLine(edge.points, _active.transform, false, "Shape", ShapeColour, 0);
            }
            else
            {
                var poly = _active.GetComponent<PolygonCollider2D>();
                if (poly != null)
                    for (var i = 0; i < poly.pathCount; i++)
                        AddOverlayLine(poly.GetPath(i), _active.transform, true, "Shape", ShapeColour, i);
            }
        }

        // Green: the merged room outline, which is what the player actually collides with.
        var composite = SceneRefs.RoomComposite;
        if (composite == null) return;

        for (var i = 0; i < composite.pathCount; i++)
        {
            var points = new Vector2[composite.GetPathPointCount(i)];
            composite.GetPath(i, points);
            AddOverlayLine(points, composite.transform, true, "Composite", CompositeColour, i);
        }
    }

    private static readonly Color ShapeColour = new(1f, 0.25f, 0.2f, 1f);
    private static readonly Color CompositeColour = new(0.2f, 1f, 0.35f, 1f);

    private void AddOverlayLine(Vector2[] points, Transform space, bool loop, string label, Color colour, int index)
    {
        if (points == null || points.Length < 2) return;

        var go = new GameObject($"{label}Path_{index}");
        go.transform.SetParent(_collisionOverlay.transform, false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = points.Length;
        line.startWidth = line.endWidth = 0.08f;
        line.sharedMaterial = MapEditorGizmos.LineMaterial();
        line.startColor = line.endColor = colour;
        line.sortingOrder = 32000;

        for (var i = 0; i < points.Length; i++)
        {
            var world = space.TransformPoint(new Vector3(points[i].x, points[i].y, 0f));
            line.SetPosition(i, new Vector3(world.x, world.y, world.z - 0.3f));
        }
    }

    // Cheap: geometry only. Safe to call every frame of a drag.
    private static void RefreshGeometry(SpriteShapeController ctrl)
    {
        if (ctrl == null) return;
        ctrl.RefreshSpriteShape();
    }

    public static bool ShapeHasCollision(SpriteShapeController ctrl)
    {
        if (ctrl == null) return false;
        return ctrl.GetComponent<PolygonCollider2D>() != null || ctrl.GetComponent<EdgeCollider2D>() != null;
    }

    private void SetActiveShapeCollision(bool enabled)
    {
        if (_active == null)
        {
            _editor.SetStatus("No shape selected.");
            return;
        }

        _editor.MarkEdited();

        if (enabled)
        {
            EnsureCollider(_active);
            _active.autoUpdateCollider = true;
            _active.colliderDetail = _colliderDetail;
            _active.colliderOffset = _colliderOffset;
            CommitShape(_active);
            _editor.SetStatus("Shape collision on.");
            return;
        }

        _active.autoUpdateCollider = false;
        foreach (var collider in _active.GetComponents<Collider2D>())
            Object.DestroyImmediate(collider);

        SceneRefs.RegenerateRoomCollision();
        RefreshCollisionOverlay();
        _editor.SetStatus("Shape collision off - visual only.");
    }

    // Every edit to a shape lands here - points added, moved and removed, the shape dragged, its
    // depth nudged, its collision switched - so it is the one place the editor needs to hear about
    // to know a close would lose terrain work.
    private void CommitShape(SpriteShapeController ctrl)
    {
        if (ctrl == null) return;

        _editor.MarkEdited();

        // In the base, a shape the tool did not make is a piece of the player's own ground. Nothing
        // is written down here - a drag commits on every frame it moves - only the fact that this
        // shape has been touched, and where it stood before anyone touched it.
        BaseDelta.NoteShapeTouched(ctrl);

        // Visual-only shapes never get a collider: editing decorative geometry must not turn it solid.
        if (!ShapeHasCollision(ctrl))
        {
            ctrl.RefreshSpriteShape();
            return;
        }

        EnsureCollider(ctrl);
        ctrl.RefreshSpriteShape();
        _editor.StartCoroutine(BakeNextFrame(ctrl));
    }

    // Bake a shape's collider and fold it into the room's collision outline.
    //
    // The half of a commit that decides whether a shape is floor or a wall you bounce off. A collider
    // that is not part of the room's composite is a solid body on its own, and the two are
    // indistinguishable until something walks into one. Split out because a shape rebuilt on load has
    // to be given the same treatment a shape edited by hand gets - which it was not, and that is what
    // left reshaped base terrain pushing the player around after every load.
    //
    // Sorting is deliberately left alone here, unlike FinalizeLoadedShape: the base's own terrain
    // came with a draw order the game chose, and this is about collision.
    public void MergeCollisionIntoRoom(SpriteShapeController ctrl)
    {
        if (ctrl == null || !ShapeHasCollision(ctrl)) return;

        ctrl.BakeCollider();
        JoinRoomComposite(ctrl);
    }

    private IEnumerator BakeNextFrame(SpriteShapeController ctrl)
    {
        yield return null;
        if (ctrl == null) yield break;

        ctrl.BakeCollider();
        JoinRoomComposite(ctrl);

        SceneRefs.RegenerateRoomCollision();

        // In the base, collision is only half of what makes a floor. The polygon the game validates
        // positions against, the followers' bounds check and the buildable grid all read a separate
        // outline, and ground that is solid but not in that outline is ground the game keeps
        // teleporting people off.
        BaseGround.RequestRefresh();

        if (ReferenceEquals(ctrl, _active)) RefreshCollisionOverlay();
    }

    private void SetVanillaFloorCollision(bool enabled)
    {
        if (RuntimeMapEditor.Context == EditorContext.Base)
        {
            _editor.SetStatus("The base's own floor stays on; everything standing on it is the " +
                              "player's.", StatusSeverity.Warning);
            return;
        }

        var affected = ApplyVanillaFloorFlag(enabled);

        SceneRefs.RegenerateRoomCollision();
        RefreshCollisionOverlay();

        _editor.SetStatus(enabled
            ? $"Vanilla floor collision restored ({affected} piece(s))."
            : $"Vanilla floor collision disabled ({affected} piece(s)); shapes now define the floor.");
    }

    // No collision rebuild here: the loader batches one rebuild at the end of the load.
    public int ApplyVanillaFloorFlag(bool enabled)
    {
        _useVanillaFloor = enabled;

        var room = SceneRefs.Room;
        if (room?.Pieces == null) return 0;

        var affected = 0;
        foreach (var piece in room.Pieces)
        {
            if (piece == null) continue;

            var collider = piece.Collider;
            if (collider == null) continue;

            collider.enabled = enabled;
            affected++;
        }
        return affected;
    }

    private static void JoinRoomComposite(SpriteShapeController ctrl)
    {
        var composite = SceneRefs.RoomComposite;
        if (composite == null || ctrl == null) return;

        // Only colliders parented under the composite participate in it.
        if (!ctrl.transform.IsChildOf(composite.transform)) return;

        ctrl.gameObject.layer = composite.gameObject.layer;

        var poly = ctrl.GetComponent<PolygonCollider2D>();
        if (poly != null) poly.usedByComposite = true;

        var edge = ctrl.GetComponent<EdgeCollider2D>();
        if (edge != null) edge.usedByComposite = true;
    }

    public void CommitActiveShape() => CommitShape(_active);

    private Transform HandleRoot()
    {
        if (_handleCanvas != null) return _handleCanvas.transform;

        var go = new GameObject("MapEditor_ShapeHandles");
        go.transform.SetParent(_editor.transform, false);

        _handleCanvas = go.AddComponent<Canvas>();
        _handleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _handleCanvas.sortingOrder = 5001;
        go.AddComponent<GraphicRaycaster>();
        return go.transform;
    }

    private void RebuildHandles()
    {
        ClearHandles();
        if (_active == null) return;

        var count = _active.spline.GetPointCount();
        for (var i = 0; i < count; i++)
            _handles.Add(CreateHandle(i));

        _centerHandle = CreateCenterHandle();
        SyncHandlePositions();
    }

    private GameObject CreateCenterHandle()
    {
        var go = new GameObject("ShapeCenterHandle");
        go.transform.SetParent(HandleRoot(), false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(30f, 30f);

        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 0.82f, 0.15f, 0.95f);

        var handle = go.AddComponent<ShapeCenterHandle>();
        handle.Initialize(this, _editor);

        _editor.RegisterUiBlocker(rt);
        return go;
    }

    public Vector3 ActiveShapeCentroid()
    {
        if (_active == null) return Vector3.zero;

        var spline = _active.spline;
        var count = spline.GetPointCount();
        if (count == 0) return _active.transform.position;

        var sum = Vector3.zero;
        for (var i = 0; i < count; i++)
            sum += _active.transform.TransformPoint(spline.GetPosition(i));

        return sum / count;
    }

    public Vector3 ActiveShapePosition => _active != null ? _active.transform.position : Vector3.zero;

    public bool HasActiveShape => _active != null;

    // Z preserved: dragging never changes depth ordering.
    public void SetActiveShapePosition(Vector3 world)
    {
        if (_active == null) return;
        var z = _active.transform.position.z;
        _active.transform.position = new Vector3(world.x, world.y, z);
    }

    private GameObject CreateHandle(int index)
    {
        var go = new GameObject("ShapeHandle_" + index);
        go.transform.SetParent(HandleRoot(), false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(20f, 20f);

        var img = go.AddComponent<Image>();
        img.color = Color.cyan;

        var handle = go.AddComponent<ShapePointHandle>();
        handle.Initialize(this, _editor, index);

        // Clicking a handle must not also drop a new point into the shape.
        _editor.RegisterUiBlocker(rt);
        return go;
    }

    private void SyncHandlePositions()
    {
        if (_active == null) return;
        var cam = SceneRefs.Cam;
        if (cam == null) return;

        var spline = _active.spline;
        for (var i = 0; i < _handles.Count && i < spline.GetPointCount(); i++)
        {
            if (_handles[i] == null) continue;
            var world = _active.transform.TransformPoint(spline.GetPosition(i));
            _handles[i].GetComponent<RectTransform>().position = cam.WorldToScreenPoint(world);
        }

        if (_centerHandle != null)
            _centerHandle.GetComponent<RectTransform>().position = cam.WorldToScreenPoint(ActiveShapeCentroid());
    }

    private void ClearHandles()
    {
        foreach (var h in _handles)
            if (h != null) Object.Destroy(h);
        _handles.Clear();

        if (_centerHandle != null) Object.Destroy(_centerHandle);
        _centerHandle = null;
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.UseVanillaFloorCollision = _useVanillaFloor;

        map.Shapes.Clear();
        foreach (var ctrl in CollectSerializableShapes())
        {
            if (ctrl == null) continue;
            map.Shapes.Add(Describe(ctrl));
        }
    }

    private List<SpriteShapeController> CollectSerializableShapes()
    {
        var list = new List<SpriteShapeController>();

        foreach (var s in _shapes)
            if (s != null && !list.Contains(s)) list.Add(s);

        // In the base, only what this tool made. The sweep below exists so a dungeon room's own
        // authored terrain round-trips through a blueprint, which is the right thing when the
        // blueprint *is* the room. A base file is a difference from a room that already exists, and
        // sweeping there would write down the player's whole town - and then lay a second copy of it
        // over the first on the way back in.
        if (RuntimeMapEditor.Context == EditorContext.Base) return list;

        foreach (var ctrl in Object.FindObjectsOfType<SpriteShapeController>())
        {
            if (ctrl == null || ctrl == _template || list.Contains(ctrl)) continue;
            if (ctrl.gameObject.name.StartsWith(DoorTool.PadName)) continue;
            if (ctrl.GetComponentInParent<RuntimeMapEditor>() != null) continue;
            if (ctrl.GetComponentInParent<MMRoomGeneration.IslandPiece>() != null) continue;
            list.Add(ctrl);
        }

        return list;
    }

    // Untracked, unserialized clone for auxiliary geometry (door pads); caller sets up the
    // spline and collider, FinalizeLoadedShape a frame later bakes it.
    public SpriteShapeController CreateUntrackedShape(Transform parent, string name)
    {
        CaptureTemplate();
        if (_template == null || parent == null) return null;

        var go = Object.Instantiate(_template.gameObject, parent);
        go.name = name;
        go.SetActive(true);

        var ctrl = go.GetComponent<SpriteShapeController>();
        foreach (var inherited in go.GetComponents<Collider2D>())
            Object.DestroyImmediate(inherited);

        SettleSorting(ctrl);
        ctrl.spline.Clear();
        return ctrl;
    }

    public static void EnsureShapeCollider(SpriteShapeController ctrl) => EnsureCollider(ctrl);

    // Recreates a saved shape; self-registers so a save round-trips. Caller must call
    // FinalizeLoadedShape a frame later.
    public SpriteShapeController RebuildShape(MapShapeData data)
    {
        if (data == null || data.Points == null || data.Points.Count < 3) return null;

        var composite = SceneRefs.EnsureRoomComposite();
        var root = composite != null ? composite.transform : SceneRefs.ContentRoot;
        if (root == null) return null;

        CaptureTemplate();
        if (_template == null)
        {
            Plugin.Log.LogWarning("MapEditor: no shape template available, cannot rebuild shape.");
            return null;
        }

        var go = Object.Instantiate(_template.gameObject, root);
        go.name = "CultTweaker_Shape";
        go.AddComponent<CTEditorShape>();
        go.SetActive(true);
        SettleSorting(go.GetComponent<SpriteShapeController>());
        go.transform.position = MapEditorSerialization.ToVector3(data.Position);

        var ctrl = go.GetComponent<SpriteShapeController>();

        foreach (var inherited in go.GetComponents<Collider2D>())
            Object.DestroyImmediate(inherited);

        if (!ApplyShapeData(ctrl, data))
        {
            Object.Destroy(go);
            return null;
        }

        _shapes.Add(ctrl);
        return ctrl;
    }

    // A live shape written down: the same record a save makes of it, wherever the caller means to
    // put that record. The base editor keeps one per piece of the player's own terrain it changed.
    public static MapShapeData Describe(SpriteShapeController ctrl)
    {
        var spline = ctrl.spline;
        var data = new MapShapeData
        {
            Position = MapEditorSerialization.V3(ctrl.transform.position),
            Profile = ctrl.spriteShape != null ? ctrl.spriteShape.name : "",
            IsOpenEnded = spline.isOpenEnded,
            HasCollision = ShapeHasCollision(ctrl),
            ColliderDetail = ctrl.colliderDetail,
            ColliderOffset = ctrl.colliderOffset,
            SortingOrder = SortOrderOf(ctrl)
        };

        for (var i = 0; i < spline.GetPointCount(); i++)
        {
            data.Points.Add(new MapShapePointData
            {
                Position = MapEditorSerialization.V3(spline.GetPosition(i)),
                LeftTangent = MapEditorSerialization.V3(spline.GetLeftTangent(i)),
                RightTangent = MapEditorSerialization.V3(spline.GetRightTangent(i)),
                TangentMode = spline.GetTangentMode(i).ToString(),
                Height = spline.GetHeight(i),
                SpriteIndex = spline.GetSpriteIndex(i),
                Corner = spline.GetCorner(i)
            });
        }

        return data;
    }

    // A saved shape's profile, spline and collider written onto a controller that already exists.
    //
    // Split out of RebuildShape because the base editor needs the second half of it on its own: a
    // shape the player's base came with is not rebuilt from nothing, it is found where it stands and
    // told what the author did to it. Returns false when the data does not describe a usable shape,
    // which is the caller's cue to throw away whatever it made for it.
    public bool ApplyShapeData(SpriteShapeController ctrl, MapShapeData data)
    {
        if (ctrl == null || data?.Points == null) return false;

        var profile = FindProfile(data.Profile);
        if (profile != null) ctrl.spriteShape = profile;
        else Plugin.Log.LogWarning($"MapEditor: profile '{data.Profile}' not found, keeping template profile.");

        // Absent in maps saved before draw order was editable; those keep the template's order.
        if (data.SortingOrder.HasValue) SetSortOrder(ctrl, data.SortingOrder.Value);

        var spline = ctrl.spline;
        spline.Clear();
        var added = 0;
        for (var i = 0; i < data.Points.Count; i++)
        {
            var p = data.Points[i];
            try
            {
                spline.InsertPointAt(added, MapEditorSerialization.ToVector3(p.Position));

                // Tangent mode first: setting it recomputes tangents and would clobber saved values.
                if (System.Enum.TryParse<ShapeTangentMode>(p.TangentMode, out var mode))
                    spline.SetTangentMode(added, mode);
                spline.SetLeftTangent(added, MapEditorSerialization.ToVector3(p.LeftTangent));
                spline.SetRightTangent(added, MapEditorSerialization.ToVector3(p.RightTangent));
                spline.SetHeight(added, p.Height);
                spline.SetSpriteIndex(added, p.SpriteIndex);
                spline.SetCorner(added, p.Corner);
                added++;
            }
            catch (System.Exception e)
            {
                // A coincident point throws; losing one point must not lose the whole shape.
                Plugin.Log.LogWarning($"MapEditor: skipped point {i} of shape '{data.Profile}': {e.Message}");
            }
        }

        if (added < 3)
        {
            Plugin.Log.LogWarning("MapEditor: shape had fewer than 3 usable points, dropped.");
            return false;
        }

        spline.isOpenEnded = data.IsOpenEnded;

        if (data.HasCollision)
        {
            ctrl.autoUpdateCollider = true;
            ctrl.colliderDetail = data.ColliderDetail;
            ctrl.colliderOffset = data.ColliderOffset;
            EnsureCollider(ctrl);
        }
        else
        {
            ctrl.autoUpdateCollider = false;
        }

        ctrl.RefreshSpriteShape();
        return true;
    }

    // Must run a frame after RebuildShape: mesh gen is deferred to end of frame, so baking
    // earlier captures the stale outline.
    public void FinalizeLoadedShape(SpriteShapeController ctrl)
    {
        SettleSorting(ctrl);

        if (ctrl == null || !ShapeHasCollision(ctrl)) return;
        ctrl.BakeCollider();
        JoinRoomComposite(ctrl);
    }

    private void CenterOnShape()
    {
        if (_active == null) return;
        _editor.MoveCameraTo(_active.transform.position);
    }

    // Terrain the base editor's journal must not treat as the player's own.
    public bool IsTracked(GameObject go)
    {
        if (go == null) return false;
        return go.GetComponentInParent<CTEditorShape>() != null;
    }
}

// Marks a piece of terrain as this tool's rather than the room's.
//
// It matters in exactly one place, and only there because the base is edited in place: a dungeon
// room's shapes are all ours by the time a save runs, and a hub's room was emptied first. The base
// keeps its own ground standing beside anything drawn on top of it, and nothing about a sprite shape
// says which of the two it is.
public class CTEditorShape : MonoBehaviour
{
}

// Drag to move a spline point; right-click to delete it.
public class ShapePointHandle : MonoBehaviour, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    private ShapeTool _tool;
    private RuntimeMapEditor _editor;
    private int _index;

    public void Initialize(ShapeTool tool, RuntimeMapEditor editor, int index)
    {
        _tool = tool;
        _editor = editor;
        _index = index;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null) return;
        _tool.SetPointWorldPosition(_index, _editor.ScreenToWorld(eventData.position));
    }

    // Collision and navigation are rebuilt once here, not on every drag frame.
    public void OnEndDrag(PointerEventData eventData)
    {
        _tool?.CommitActiveShape();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_tool == null) return;
        if (eventData.button == PointerEventData.InputButton.Right)
            _tool.RemovePoint(_index);
    }
}

// Drags the whole shape; grab offset captured on mouse-down so it does not snap to the cursor.
public class ShapeCenterHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private ShapeTool _tool;
    private RuntimeMapEditor _editor;
    private Vector3 _grabOffset;

    public void Initialize(ShapeTool tool, RuntimeMapEditor editor)
    {
        _tool = tool;
        _editor = editor;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null || !_tool.HasActiveShape) return;
        _grabOffset = _tool.ActiveShapePosition - _editor.ScreenToWorld(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null) return;
        _tool.SetActiveShapePosition(_editor.ScreenToWorld(eventData.position) + _grabOffset);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _tool?.CommitActiveShape();
    }
}
