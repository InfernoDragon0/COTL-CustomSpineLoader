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
    private bool _clickAddsPoints;
    private bool _showCollision;
    private bool _toolActive;
    private bool _useVanillaFloor = true;
    private GameObject _collisionOverlay;
    private GameObject _centerHandle;

    private GameObject _collisionToggleRow;

    private const float ZStep = 0.1f;

    // Spline.InsertPointAt throws if a new point lands on an existing one.
    private const float MinPointSpacing = 0.25f;

    public ShapeTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateButton(panel, "New Shape (screen centre)", SpawnShape);

        _shapeDropdown = ui.CreateDropdown(panel, "Select a shape", [], (index, _) => SelectShapeAt(index));
        ui.CreateButton(panel, "Delete Shape (Del)", DeleteActiveShape);

        _profileDropdown = ui.CreateDropdown(panel, "Select a profile", [], (index, _) => SelectProfileAt(index));

        ui.CreateToggle(panel, "Click adds points", _clickAddsPoints, v =>
        {
            _clickAddsPoints = v;
            _editor.SetStatus(v ? "Click-to-add enabled." : "Click-to-add disabled.");
        });

        ui.CreateToggle(panel, "Show Collision", _showCollision, v =>
        {
            _showCollision = v;
            RefreshCollisionOverlay();
        });

        _collisionToggleRow = ui.CreateToggle(panel, "Shape Has Collision", true, SetActiveShapeCollision);

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

        // Higher Z sits further back, so "Send Back" increases it.
        ui.CreateButton(panel, "Send Back (Z+)", () => NudgeZ(ZStep));
        ui.CreateButton(panel, "Bring Front (Z-)", () => NudgeZ(-ZStep));

        ui.CreateButton(panel, "Center View On Shape", CenterOnShape);
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
        ("LMB", "Add node (if enabled)"),
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

        if (!_clickAddsPoints) return;
        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;

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
        var composite = SceneRefs.RoomComposite;
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
        go.SetActive(true);
        go.transform.position = center;

        var ctrl = go.GetComponent<SpriteShapeController>();

        // The template carries the source's baked colliders; strip so this shape bakes its own.
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

        // CommitShape only maintains collision on shapes that already carry a collider.
        EnsureCollider(ctrl);

        _shapes.Add(ctrl);
        _active = ctrl;

        CommitShape(ctrl);
        RebuildHandles();
        UpdateLabels();
        _editor.SetStatus("Shape created.");
    }

    private MapEditorDropdown _shapeDropdown;
    private MapEditorDropdown _profileDropdown;

    // Every shape in the room, in the stable order the dropdown indexes into.
    private readonly List<SpriteShapeController> _allShapes = [];

    private void CollectShapes()
    {
        _allShapes.Clear();
        foreach (var s in _shapes)
            if (s != null) _allShapes.Add(s);
        foreach (var s in Object.FindObjectsOfType<SpriteShapeController>())
            if (s != null && s != _template && !_allShapes.Contains(s)) _allShapes.Add(s);
    }

    private void RefreshShapeDropdown()
    {
        if (_shapeDropdown == null) return;

        CollectShapes();

        var labels = new List<string>(_allShapes.Count);
        for (var i = 0; i < _allShapes.Count; i++)
            labels.Add($"{i + 1}. {_allShapes[i].name} ({_allShapes[i].spline.GetPointCount()} pts)");

        _shapeDropdown.SetOptions(labels);
        if (_active != null) _shapeDropdown.SetSelected(_allShapes.IndexOf(_active));
    }

    private void SelectShapeAt(int index)
    {
        if (index < 0 || index >= _allShapes.Count) return;

        _active = _allShapes[index];
        _openEnded = _active.spline.isOpenEnded;

        RebuildHandles();
        UpdateLabels();
        RefreshCollisionOverlay();
        CenterOnShape();
        _editor.SetStatus($"Editing {_active.name}.");
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

        // DestroyImmediate: a deferred destroy leaves the shape in the merged outline until the next change.
        Object.DestroyImmediate(doomed.gameObject);
        SceneRefs.RegenerateRoomCollision();
        RefreshCollisionOverlay();

        UpdateLabels();
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
        if (_collisionToggleRow == null) return;

        var toggle = _collisionToggleRow.GetComponent<MapEditorToggle>();
        if (toggle == null) return;

        toggle.SetValue(ShapeHasCollision(_active), notify: false);
    }

    private void UpdateLabels()
    {
        SyncCollisionToggle();
        SyncProfileIndex();
        RefreshShapeDropdown();
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

    private void CommitShape(SpriteShapeController ctrl)
    {
        if (ctrl == null) return;

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

    private IEnumerator BakeNextFrame(SpriteShapeController ctrl)
    {
        yield return null;
        if (ctrl == null) yield break;

        ctrl.BakeCollider();
        JoinRoomComposite(ctrl);

        SceneRefs.RegenerateRoomCollision();

        if (ReferenceEquals(ctrl, _active)) RefreshCollisionOverlay();
    }

    private void SetVanillaFloorCollision(bool enabled)
    {
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

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.UseVanillaFloorCollision = _useVanillaFloor;

        map.Shapes.Clear();
        foreach (var ctrl in CollectSerializableShapes())
        {
            if (ctrl == null) continue;

            var spline = ctrl.spline;
            var data = new MapShapeData
            {
                Position = MapEditorSerialization.V3(ctrl.transform.position),
                Profile = ctrl.spriteShape != null ? ctrl.spriteShape.name : "",
                IsOpenEnded = spline.isOpenEnded,
                HasCollision = ShapeHasCollision(ctrl),
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

    private List<SpriteShapeController> CollectSerializableShapes()
    {
        var list = new List<SpriteShapeController>();

        foreach (var s in _shapes)
            if (s != null && !list.Contains(s)) list.Add(s);

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

        ctrl.spline.Clear();
        return ctrl;
    }

    public static void EnsureShapeCollider(SpriteShapeController ctrl) => EnsureCollider(ctrl);

    // Recreates a saved shape; self-registers so a save round-trips. Caller must call
    // FinalizeLoadedShape a frame later.
    public SpriteShapeController RebuildShape(MapShapeData data)
    {
        if (data == null || data.Points == null || data.Points.Count < 3) return null;

        var composite = SceneRefs.RoomComposite;
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
        go.SetActive(true);
        go.transform.position = MapEditorSerialization.ToVector3(data.Position);

        var ctrl = go.GetComponent<SpriteShapeController>();

        foreach (var inherited in go.GetComponents<Collider2D>())
            Object.DestroyImmediate(inherited);

        var profile = FindProfile(data.Profile);
        if (profile != null) ctrl.spriteShape = profile;
        else Plugin.Log.LogWarning($"MapEditor: profile '{data.Profile}' not found, keeping template profile.");

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
            Object.Destroy(go);
            Plugin.Log.LogWarning("MapEditor: shape had fewer than 3 usable points, dropped.");
            return null;
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
        _shapes.Add(ctrl);
        return ctrl;
    }

    // Must run a frame after RebuildShape: mesh gen is deferred to end of frame, so baking
    // earlier captures the stale outline.
    public void FinalizeLoadedShape(SpriteShapeController ctrl)
    {
        if (ctrl == null || !ShapeHasCollision(ctrl)) return;
        ctrl.BakeCollider();
        JoinRoomComposite(ctrl);
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
