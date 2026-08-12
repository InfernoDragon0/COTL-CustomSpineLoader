using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

// Spawns and edits SpriteShapeControllers, and keeps their collision in sync.
//
// New shapes are cloned from a sprite shape that already exists in the room rather than built
// from a bare GameObject. Building one from scratch produced untextured geometry, because a
// working shape needs a matching profile, fill material, sorting layer and renderer settings,
// and only the profile is reachable through GenerateRoom.DecorationList. Cloning inherits all
// of it, so authored terrain matches the biome automatically.
public class ShapeTool : IMapEditorTool, IMapDataContributor
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
    private bool _clickAddsPoints;
    private bool _showCollision;
    private bool _toolActive;
    private bool _useVanillaFloor = true;
    private GameObject _collisionOverlay;
    private GameObject _centerHandle;

    private GameObject _collisionToggleRow;
    private bool _suppressCollisionToggle;

    private const float ZStep = 0.1f;

    private TMP_Text _profileLabel;
    private TMP_Text _shapeLabel;

    // Spline.InsertPointAt throws if a new point lands on an existing one.
    private const float MinPointSpacing = 0.25f;

    public ShapeTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateLabel(panel, "Shape Tool", 20, TextAlignmentOptions.Center);

        ui.CreateButton(panel, "New Shape (screen centre)", SpawnShape);

        _shapeLabel = ui.CreateLabel(panel, "No shape selected", 15, TextAlignmentOptions.Center)
            .GetComponent<TMP_Text>();
        ui.CreateButton(panel, "< Prev Shape", () => CycleShape(-1));
        ui.CreateButton(panel, "Next Shape >", () => CycleShape(1));
        ui.CreateButton(panel, "Delete Shape", DeleteActiveShape);

        _profileLabel = ui.CreateLabel(panel, "Profile: -", 15, TextAlignmentOptions.Center)
            .GetComponent<TMP_Text>();
        ui.CreateButton(panel, "Cycle Profile", CycleProfile);

        // Off by default: having every stray click drop a point made the tool hard to use.
        ui.CreateToggle(panel, "Click adds points", _clickAddsPoints, v =>
        {
            _clickAddsPoints = v;
            _editor.SetStatus(v ? "Left-click in the world adds a point." : "Click-to-add disabled.");
        });

        ui.CreateToggle(panel, "Show Collision", _showCollision, v =>
        {
            _showCollision = v;
            RefreshCollisionOverlay();
        });

        _collisionToggleRow = ui.CreateToggle(panel, "Shape Has Collision", true, v =>
        {
            if (_suppressCollisionToggle) return;
            SetActiveShapeCollision(v);
        });

        ui.CreateToggle(panel, "Vanilla Floor Collision", _useVanillaFloor, SetVanillaFloorCollision);

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

        // Depth ordering. Higher Z sits further back, so "Send Back" increases it.
        ui.CreateButton(panel, "Send Back (Z+)", () => NudgeZ(ZStep));
        ui.CreateButton(panel, "Bring Front (Z-)", () => NudgeZ(-ZStep));

        ui.CreateButton(panel, "Center View On Shape", CenterOnShape);
    }

    public void OnEnter()
    {
        _toolActive = true;
        CaptureTemplate();
        CollectProfiles();

        if (_active == null)
            _active = Object.FindObjectOfType<SpriteShapeController>();

        RebuildHandles();
        UpdateLabels();

        // OnExit tears the overlay down, so it has to be rebuilt on re-entry or the toggle stays
        // on with nothing drawn after switching tools.
        RefreshCollisionOverlay();

        _editor.SetStatus("Shape tool: drag handles to edit, right-click a handle to delete it.");
    }

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

        if (!_clickAddsPoints) return;
        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;

        AddPointAt(_editor.MouseWorld());
    }

    // Keep an inactive clone of a room sprite shape so new shapes can still be created after
    // Clear Terrain has removed every original from the scene.
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
        var room = SceneRefs.Room;
        if (room != null)
        {
            if (room.RoomSpriteShape != null) return room.RoomSpriteShape;
            if (room.SpriteShapeControllers != null)
                foreach (var c in room.SpriteShapeControllers)
                    if (c != null) return c;
        }
        return Object.FindObjectOfType<SpriteShapeController>();
    }

    // Profiles come from the biome definition and from whatever the room actually uses, since
    // DecorationList does not always populate every slot.
    private void CollectProfiles()
    {
        _profiles.Clear();

        void Add(SpriteShape s)
        {
            if (s != null && !_profiles.Contains(s)) _profiles.Add(s);
        }

        // Biome definition first, so the room's own profiles head the list.
        var deco = SceneRefs.Decorations;
        if (deco != null)
        {
            Add(deco.SpriteShape);
            Add(deco.SpriteShapeSecondary);
            Add(deco.SpriteShapeBack);
        }

        foreach (var ctrl in Object.FindObjectsOfType<SpriteShapeController>())
            Add(ctrl.spriteShape);

        if (_template != null) Add(_template.spriteShape);

        // Everything else already loaded in memory, including profiles from biomes that are not
        // currently instantiated. FindObjectsOfTypeAll reaches assets, not just scene objects.
        foreach (var shape in Resources.FindObjectsOfTypeAll<SpriteShape>())
            Add(shape);

        Plugin.Log.LogInfo($"MapEditor: {_profiles.Count} sprite shape profile(s) available.");
    }

    // Keeps the profile index pointing at whatever the selected shape actually uses, so the
    // label and the next Cycle press are relative to that shape rather than the previous one.
    private void SyncProfileIndex()
    {
        if (_active == null || _active.spriteShape == null) return;

        var index = _profiles.IndexOf(_active.spriteShape);
        if (index >= 0) _profileIndex = index;
    }

    private void SpawnShape()
    {
        // Parented under the room's CompositeCollider2D, not the generic content root: a
        // composite only merges colliders on itself and its own children. This is where the
        // generator puts its island pieces too.
        var composite = SceneRefs.RoomComposite;
        var root = composite != null ? composite.transform : SceneRefs.ContentRoot;
        if (root == null)
        {
            _editor.SetStatus("No room content root; cannot spawn a shape here.");
            return;
        }

        CaptureTemplate();
        if (_template == null)
        {
            _editor.SetStatus("No sprite shape in this room to base a new one on.");
            return;
        }

        // Screen centre, not the cursor: the cursor is over the button that was just clicked.
        var center = _editor.ScreenToWorld(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

        var go = Object.Instantiate(_template.gameObject, root);
        go.name = "CultTweaker_Shape";
        go.SetActive(true);
        go.transform.position = center;

        var ctrl = go.GetComponent<SpriteShapeController>();

        // The template carries the source shape's baked colliders. Strip them so the new shape
        // bakes its own rather than inheriting the old outline.
        foreach (var inherited in go.GetComponents<Collider2D>())
            Object.DestroyImmediate(inherited);

        var spline = ctrl.spline;
        spline.Clear();
        var corners = new[]
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

        // Explicit, because CommitShape only maintains collision on shapes that already have a
        // collider and the inherited ones were just stripped.
        EnsureCollider(ctrl);

        _shapes.Add(ctrl);
        _active = ctrl;

        CommitShape(ctrl);
        RebuildHandles();
        UpdateLabels();
        _editor.SetStatus("Spawned shape at screen centre.");
    }

    private void CycleShape(int direction)
    {
        var all = new List<SpriteShapeController>();
        foreach (var s in _shapes)
            if (s != null) all.Add(s);
        foreach (var s in Object.FindObjectsOfType<SpriteShapeController>())
            if (s != null && s != _template && !all.Contains(s)) all.Add(s);

        if (all.Count == 0)
        {
            _editor.SetStatus("No shapes in this room.");
            return;
        }

        var index = _active != null ? all.IndexOf(_active) : -1;
        index = ((index + direction) % all.Count + all.Count) % all.Count;

        _active = all[index];
        _openEnded = _active.spline.isOpenEnded;

        RebuildHandles();
        UpdateLabels();
        RefreshCollisionOverlay();
        CenterOnShape();
        _editor.SetStatus($"Editing shape {index + 1} of {all.Count}: {_active.name}");
    }

    private void DeleteActiveShape()
    {
        if (_active == null)
        {
            _editor.SetStatus("No shape selected.");
            return;
        }

        var doomed = _active;
        _shapes.Remove(doomed);
        _active = null;

        ClearHandles();

        // DestroyImmediate so the collider is gone before the composite is rebuilt; a deferred
        // destroy would leave the removed shape in the merged outline until the next change.
        Object.DestroyImmediate(doomed.gameObject);
        SceneRefs.RegenerateRoomCollision();
        RefreshCollisionOverlay();

        UpdateLabels();
        _editor.SetStatus("Deleted shape.");
    }

    private void CycleProfile()
    {
        if (_profiles.Count == 0) CollectProfiles();
        if (_profiles.Count == 0)
        {
            _editor.SetStatus("No sprite shape profiles available in this biome.");
            return;
        }

        _profileIndex = (_profileIndex + 1) % _profiles.Count;

        if (_active != null)
        {
            _active.spriteShape = _profiles[_profileIndex];
            CommitShape(_active);
        }

        UpdateLabels();
        _editor.SetStatus("Profile: " + _profiles[_profileIndex].name);
    }

    // Reflects the selected shape's actual collision state without re-entering the callback that
    // would otherwise add or strip a collider as a side effect of merely selecting a shape.
    private void SyncCollisionToggle()
    {
        if (_collisionToggleRow == null) return;

        var toggle = _collisionToggleRow.GetComponentInChildren<Lamb.UI.MMToggle>();
        if (toggle == null) return;

        _suppressCollisionToggle = true;
        try
        {
            toggle.Value = ShapeHasCollision(_active);
        }
        finally
        {
            _suppressCollisionToggle = false;
        }
    }

    private void UpdateLabels()
    {
        SyncCollisionToggle();
        SyncProfileIndex();
        if (_profileLabel != null)
            _profileLabel.text = _profiles.Count > 0 && _profileIndex < _profiles.Count
                ? "Profile: " + _profiles[_profileIndex].name
                : "Profile: -";

        if (_shapeLabel != null)
            _shapeLabel.text = _active != null
                ? $"Editing: {_active.name}\n{_active.spline.GetPointCount()} pts, Z {_active.transform.position.z:0.###}"
                : "No shape selected";
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
                _editor.SetStatus("Too close to an existing point.");
                return;
            }
        }

        // Insert after the nearest existing point so the outline stays sensible rather than
        // always appending to the end.
        var insertIndex = NearestPointIndex(spline, local) + 1;
        try
        {
            spline.InsertPointAt(insertIndex, local);
            spline.SetTangentMode(insertIndex, ShapeTangentMode.Linear);
        }
        catch (System.Exception e)
        {
            _editor.SetStatus("Could not add point: " + e.Message);
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
            _editor.SetStatus("A shape needs at least 3 points.");
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
        _active.colliderDetail = _colliderDetail;
        _active.colliderOffset = _colliderOffset;
        _active.autoUpdateCollider = true;
        CommitShape(_active);
    }

    // SpriteShapeController bakes into an EdgeCollider2D (open spline) or PolygonCollider2D
    // (closed) but never creates one. Without the component present BakeCollider silently does
    // nothing, which is why authored shapes had no collision.
    private static void EnsureCollider(SpriteShapeController ctrl)
    {
        if (ctrl == null) return;

        // DestroyImmediate, not Destroy: normal Destroy is deferred to the end of the frame, so
        // the collider being replaced would still be attached when the new one is added and the
        // controller could bake into the stale one.
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
    }

    // Draws the collider the controller actually baked, so a mismatch between the visible shape
    // and its collision is immediately obvious rather than something you discover by walking.
    private void RefreshCollisionOverlay()
    {
        if (_collisionOverlay != null)
        {
            Object.Destroy(_collisionOverlay);
            _collisionOverlay = null;
        }

        // A pending bake coroutine can complete after the tool has been exited or the editor
        // closed, which would rebuild the overlay into a scene with no editor open. That is the
        // stray collision outline that appeared after closing.
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
        line.material = new Material(Shader.Find("Sprites/Default"));
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

    // Many of the room's sprite shapes are purely decorative and carry no collider at all.
    // Collision is therefore treated as a per-shape property read off the object itself, so
    // editing a decorative shape never silently gives it collision it never had.
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

        if (enabled)
        {
            EnsureCollider(_active);
            _active.autoUpdateCollider = true;
            _active.colliderDetail = _colliderDetail;
            _active.colliderOffset = _colliderOffset;
            CommitShape(_active);
            _editor.SetStatus("Collision enabled for this shape.");
            return;
        }

        _active.autoUpdateCollider = false;
        foreach (var collider in _active.GetComponents<Collider2D>())
            Object.DestroyImmediate(collider);

        SceneRefs.RegenerateRoomCollision();
        RefreshCollisionOverlay();
        _editor.SetStatus("Collision removed; this shape is now visual only.");
    }

    // Mesh generation is deferred to the end of the frame, so baking collision immediately after
    // RefreshSpriteShape bakes the *previous* outline - that was the source of the wrong-looking
    // collision. Waiting a frame makes the collider match what is on screen.
    private void CommitShape(SpriteShapeController ctrl)
    {
        if (ctrl == null) return;

        // Visual-only shapes are refreshed but never given a collider, so editing decorative
        // room geometry cannot turn it solid.
        if (!ShapeHasCollision(ctrl))
        {
            ctrl.RefreshSpriteShape();
            return;
        }

        EnsureCollider(ctrl);
        ctrl.RefreshSpriteShape();
        _editor.StartCoroutine(BakeNextFrame(ctrl));
    }

    private IEnumerator BakeNextFrame(SpriteShapeController ctrl)
    {
        yield return null;
        if (ctrl == null) yield break;

        ctrl.BakeCollider();
        JoinRoomComposite(ctrl);

        // Rebuilds the composite outline and the A* grid together.
        SceneRefs.RegenerateRoomCollision();

        if (ReferenceEquals(ctrl, _active)) RefreshCollisionOverlay();
    }

    // The composite merges the vanilla island pieces with authored shapes, so the walkable area
    // is their union and can never shrink below the original room floor -- shrinking a shape
    // inside that floor has no visible effect. Turning this off disables the island colliders so
    // the floor is defined purely by authored shapes, which is what makes shrinking possible.
    //
    // The colliders are disabled rather than having usedByComposite cleared: clearing that flag
    // would turn each island back into a solid standalone body and block the player outright.
    private void SetVanillaFloorCollision(bool enabled)
    {
        _useVanillaFloor = enabled;

        var room = SceneRefs.Room;
        if (room?.Pieces == null)
        {
            _editor.SetStatus("No island pieces in this room.");
            return;
        }

        var affected = 0;
        foreach (var piece in room.Pieces)
        {
            if (piece == null) continue;

            var collider = piece.Collider;
            if (collider == null) continue;

            collider.enabled = enabled;
            affected++;
        }

        SceneRefs.RegenerateRoomCollision();
        RefreshCollisionOverlay();

        _editor.SetStatus(enabled
            ? $"Vanilla floor collision restored ({affected} piece(s))."
            : $"Vanilla floor collision disabled ({affected} piece(s)); shapes now define the floor.");
    }

    // Hands the baked collider to the room's composite instead of leaving it as a standalone
    // body. A lone PolygonCollider2D is solid, so the player was blocked by the whole filled
    // area and shoved out of the shape, and it fought the room's own floor wherever the two
    // overlapped. Merged into the composite the shape becomes part of the walkable island, with
    // only the union's outline solid - the same treatment IslandPiece colliders get in
    // GenerateRoom.CompositeColliders.
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

    // Called by a handle when the user releases the mouse.
    public void CommitActiveShape() => CommitShape(_active);

    // All handles share one canvas; giving each its own would nest a canvas plus a raycaster
    // per spline point.
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

    // Yellow node at the shape's centroid that moves the whole shape rather than one point.
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

    // Average of the spline points in world space.
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

    // Moves the whole shape. Z is preserved so dragging never changes the depth ordering.
    public void SetActiveShapePosition(Vector3 world)
    {
        if (_active == null) return;
        var z = _active.transform.position.z;
        _active.transform.position = new Vector3(world.x, world.y, z);
    }

    private void NudgeZ(float delta)
    {
        if (_active == null)
        {
            _editor.SetStatus("No shape selected.");
            return;
        }

        var p = _active.transform.position;
        _active.transform.position = new Vector3(p.x, p.y, p.z + delta);

        CommitShape(_active);
        UpdateLabels();
        _editor.SetStatus($"Shape Z: {_active.transform.position.z:0.###}");
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

    public void ContributeTo(MapData map)
    {
        map.Shapes.Clear();
        foreach (var ctrl in _shapes)
        {
            if (ctrl == null) continue;

            var spline = ctrl.spline;
            var data = new MapShapeData
            {
                Position = MapEditorSerialization.V3(ctrl.transform.position),
                Profile = ctrl.spriteShape != null ? ctrl.spriteShape.name : "",
                IsOpenEnded = spline.isOpenEnded,
                ColliderDetail = ctrl.colliderDetail,
                ColliderOffset = ctrl.colliderOffset
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

            map.Shapes.Add(data);
        }
    }

    private void CenterOnShape()
    {
        if (_active == null) return;
        _editor.MoveCameraTo(_active.transform.position);
    }
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

    // Collision and navigation are rebuilt once here rather than on every drag frame.
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

// Drags the whole shape. The grab offset is captured on mouse-down so the shape does not snap
// its centroid to the cursor when the drag starts.
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
