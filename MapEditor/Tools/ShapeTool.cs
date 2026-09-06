using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class ShapeTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts, Net.IMapEditorLivePreview
{
    public string Name => "Shape";

    // ---- multiplayer -------------------------------------------------------------------------

    private bool _liveDragging;

    public bool LiveActive => _liveDragging && _active != null;

    public IEnumerable<GameObject> LiveObjects
    {
        get
        {
            if (_active != null) yield return _active.gameObject;
        }
    }

    /// A peer's record for a shape that already stands here: geometry, profile, order and collider.
    internal void ApplyRemoteShape(SpriteShapeController ctrl, MapShapeData data, bool bake)
    {
        if (ctrl == null || data == null) return;

        var position = MapEditorSerialization.ToVector3(data.Position);
        ctrl.transform.position = new Vector3(position.x, position.y, ctrl.transform.position.z);

        if (!ApplyShapeData(ctrl, data)) return;
        if (!_shapes.Contains(ctrl) && ctrl.GetComponent<CTEditorShape>() != null) _shapes.Add(ctrl);

        if (bake)
        {
            FinalizeLoadedShape(ctrl);
            BaseGround.RequestRefresh();
        }

        if (!ReferenceEquals(ctrl, _active) || !_toolActive) return;
        RebuildHandles();
        UpdateLabels();
        RefreshCollisionOverlay();
    }

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

    private const float MinPointSpacing = 0.25f;

    public ShapeTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _ui = ui;

        _profileDropdown = ui.CreateDropdown(panel, "Select a profile", [], (index, _) => SelectProfileAt(index));

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

        ui.CreateHeader(panel, "- Shapes -", 19);
        ui.CreateButton(panel, "New Shape (screen centre)", SpawnShape);

        _shapeBox = ui.CreateScrollBox(panel, "ShapeList", ShapeListHeight, RowHeight);
        _shapeList = _shapeBox.Content;
        RefreshShapeList();

    }

    public void PrepareForLoad()
    {
        CaptureTemplate();
        CollectProfiles();
    }

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

        _vanillaFloorRow?.SetActive(RuntimeMapEditor.Context != EditorContext.Base);

        if (_active == null)
            _active = Object.FindObjectOfType<SpriteShapeController>();

        RebuildHandles();
        UpdateLabels();

        if (_active == null && _allShapes.Count > 0) SelectShapeAt(0);

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

        foreach (var c in Resources.FindObjectsOfTypeAll<SpriteShapeController>())
            if (c != null && c.gameObject.scene.IsValid()) candidates.Add(c);

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

        var edge = materials.Length > 0 && materials[materials.Length - 1] != null
            ? materials[materials.Length - 1]
            : fill;

        renderer.sharedMaterials = [fill, edge];
        Plugin.Log.LogInfo("MapEditor: new shape given a fill material.");
    }

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

    private static bool DrawsAFill(SpriteShapeController ctrl)
    {
        if (ctrl == null || ctrl.spriteShape == null) return false;
        if (ctrl.spriteShape.fillTexture != null) return true;

        var renderer = ctrl.GetComponent<UnityEngine.U2D.SpriteShapeRenderer>();
        return renderer != null && renderer.sharedMaterials.Length > 1 &&
               renderer.sharedMaterials[0] != null;
    }

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

        foreach (var custom in CustomShapeProfiles.All)
            Add(custom);

        foreach (var ctrl in Object.FindObjectsOfType<SpriteShapeController>())
            Add(ctrl.spriteShape);

        if (_template != null) Add(_template.spriteShape);

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

        var center = _editor.ScreenToWorld(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

        var go = Object.Instantiate(_template.gameObject, root);
        go.name = "CultTweaker_Shape";
        go.AddComponent<CTEditorShape>();
        go.SetActive(true);
        go.transform.position = center;

        var ctrl = go.GetComponent<SpriteShapeController>();

        EnsureFill(ctrl);

        foreach (var inherited in go.GetComponents<Collider2D>())
            Object.DestroyImmediate(inherited);

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

    private readonly List<SpriteShapeController> _allShapes = [];

    private void CollectShapes()
    {
        _allShapes.Clear();
        foreach (var s in _shapes)
            if (s != null) _allShapes.Add(s);
        foreach (var s in Object.FindObjectsOfType<SpriteShapeController>())
            if (s != null && s != _template && !_allShapes.Contains(s)) _allShapes.Add(s);

        _allShapes.Sort((a, b) => SortOrderOf(a).CompareTo(SortOrderOf(b)));
    }

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

    private const string GroundLayer = "Ground";

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

        var element = button.GetComponent<LayoutElement>();
        element.preferredWidth = 30f;
        element.minWidth = 30f;
        element.flexibleWidth = 0f;
    }

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

        if (shape != _active && Net.EditorPresence.LockedByPeer(shape.gameObject))
        {
            _editor.SetStatus(Net.EditorPresence.LockMessage, StatusSeverity.Warning);
            return;
        }

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

    internal bool DeleteShape(SpriteShapeController shape)
    {
        if (shape == null) return false;

        var doomed = shape;

        BaseDelta.NoteRemoved(doomed.gameObject);

        _shapes.Remove(doomed);
        if (_active == doomed) _active = null;

        ClearHandles();

        Object.DestroyImmediate(doomed.gameObject);
        SceneRefs.RegenerateRoomCollision();
        BaseGround.RequestRefresh();
        RefreshCollisionOverlay();

        UpdateLabels();
        _editor.MarkEdited();
        _editor.SetStatus("Deleted shape.");
        return true;
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

        _liveDragging = true;
        spline.SetPosition(index, _active.transform.InverseTransformPoint(worldPos));

        RefreshGeometry(_active);
    }

    public void RemovePoint(int index)
    {
        if (_active == null) return;
        var spline = _active.spline;
        if (index < 0 || index >= spline.GetPointCount()) return;

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

    private void CommitShape(SpriteShapeController ctrl)
    {
        if (ctrl == null) return;

        _editor.MarkEdited();

        BaseDelta.NoteShapeTouched(ctrl);

        if (!ShapeHasCollision(ctrl))
        {
            ctrl.RefreshSpriteShape();
            return;
        }

        EnsureCollider(ctrl);
        ctrl.RefreshSpriteShape();
        _editor.StartCoroutine(BakeNextFrame(ctrl));
    }

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

        if (!ctrl.transform.IsChildOf(composite.transform)) return;

        ctrl.gameObject.layer = composite.gameObject.layer;

        var poly = ctrl.GetComponent<PolygonCollider2D>();
        if (poly != null) poly.usedByComposite = true;

        var edge = ctrl.GetComponent<EdgeCollider2D>();
        if (edge != null) edge.usedByComposite = true;
    }

    public void CommitActiveShape()
    {
        _liveDragging = false;
        CommitShape(_active);
    }

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

    internal GameObject ActiveShapeObject => _active != null ? _active.gameObject : null;

    public void SetActiveShapePosition(Vector3 world)
    {
        if (_active == null) return;
        _liveDragging = true;
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
            var data = Describe(ctrl);
            // Vanilla rooms stack several shapes of one profile at the origin; the prefab's object name
            // and the point count keep their seeds apart so both machines mint the same ids in any order.
            data.Id = Net.EditorIds.Of(ctrl.gameObject,
                Net.EditorIds.Seed("shape", data.Profile + "|" + ctrl.gameObject.name + "|" + (data.Points?.Count ?? 0),
                    ctrl.transform.position));
            map.Shapes.Add(data);
        }
    }

    private List<SpriteShapeController> CollectSerializableShapes()
    {
        var list = new List<SpriteShapeController>();

        foreach (var s in _shapes)
            if (s != null && !list.Contains(s)) list.Add(s);

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

    public bool ApplyShapeData(SpriteShapeController ctrl, MapShapeData data)
    {
        if (ctrl == null || data?.Points == null) return false;

        var profile = FindProfile(data.Profile);
        if (profile != null) ctrl.spriteShape = profile;
        else Plugin.Log.LogWarning($"MapEditor: profile '{data.Profile}' not found, keeping template profile.");

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

    public bool IsTracked(GameObject go)
    {
        if (go == null) return false;
        return go.GetComponentInParent<CTEditorShape>() != null;
    }
}

public class CTEditorShape : MonoBehaviour
{
}

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
