using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class SelectTool : IMapEditorTool, IMapEditorShortcuts
{
    public string Name => "Select";

    private readonly RuntimeMapEditor _editor;

    private GameObject _selected;
    private bool _cloneDragging;
    private Vector3 _cloneGrabOffset;
    private readonly List<Renderer> _highlighted = [];
    private readonly List<Color> _originalColors = [];
    private GameObject _outline;
    private GameObject _grip;
    private GameObject _resizeNode;
    private GameObject _depthNode;
    private Canvas _gripCanvas;

    private Vector3 _resizeStartScale;
    private Vector3 _resizeStartCentre;
    private Vector3 _resizeStartGrab;

    public SelectTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    // ---- panel ---------------------------------------------------------------------------------

    private const float PreviewBox = 200f;

    private GameObject _previewGO;
    private RawImage _previewImage;
    private GameObject _previewEmpty;
    private GameObject _detailsGO;
    private TMPro.TMP_Text _details;
    private MapEditorToggle _flipToggle;
    private GameObject _seeThroughGO;
    private MapEditorToggle _seeThroughToggle;
    private GameObject _fogThroughGO;
    private MapEditorToggle _fogThroughToggle;

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        BuildPreviewBox(panel, ui);

        _detailsGO = ui.CreateLabel(panel, "", 15);
        _details = _detailsGO.GetComponent<TMPro.TMP_Text>();
        _details.color = new Color(1f, 1f, 1f, 0.78f);

        var flip = ui.CreateToggle(panel, "Flipped horizontally", false, SetFlipped);
        _flipToggle = flip.GetComponent<MapEditorToggle>();

        _seeThroughGO = ui.CreateToggle(panel, "See-through", false, on => SetLook(player: on, fog: null));
        _seeThroughToggle = _seeThroughGO.GetComponent<MapEditorToggle>();
        _seeThroughGO.SetActive(false);

        _fogThroughGO = ui.CreateToggle(panel, "Fog pass-through", false, on => SetLook(player: null, fog: on));
        _fogThroughToggle = _fogThroughGO.GetComponent<MapEditorToggle>();
        _fogThroughGO.SetActive(false);

        RefreshDetails();
    }

    private void BuildPreviewBox(RectTransform panel, MapEditorUI ui)
    {
        _previewGO = new GameObject("SelectionPreview");
        _previewGO.transform.SetParent(panel, false);

        var rect = _previewGO.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(PreviewBox, PreviewBox);

        var element = _previewGO.AddComponent<LayoutElement>();
        element.minHeight = PreviewBox;
        element.preferredHeight = PreviewBox;
        element.flexibleWidth = 1f;

        var plateGO = new GameObject("Plate");
        plateGO.transform.SetParent(_previewGO.transform, false);

        var plateRect = plateGO.AddComponent<RectTransform>();
        plateRect.anchorMin = plateRect.anchorMax = new Vector2(0.5f, 0.5f);
        plateRect.pivot = new Vector2(0.5f, 0.5f);
        plateRect.sizeDelta = new Vector2(PreviewBox, PreviewBox);

        var plate = plateGO.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.6f;
        plate.color = new Color(0f, 0f, 0f, 0.5f);
        plate.raycastTarget = false;

        var imageGO = new GameObject("Image");
        imageGO.transform.SetParent(plateGO.transform, false);

        var imageRect = imageGO.AddComponent<RectTransform>();
        imageRect.anchorMin = imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.sizeDelta = new Vector2(PreviewBox - 16f, PreviewBox - 16f);

        _previewImage = imageGO.AddComponent<RawImage>();
        _previewImage.raycastTarget = false;
        _previewImage.enabled = false;

        _previewEmpty = ui.CreateLabel(plateGO.transform, "Nothing selected", 17,
            TMPro.TextAlignmentOptions.Center);
        var emptyRect = _previewEmpty.GetComponent<RectTransform>();
        emptyRect.anchorMin = Vector2.zero;
        emptyRect.anchorMax = Vector2.one;
        emptyRect.offsetMin = new Vector2(8f, 8f);
        emptyRect.offsetMax = new Vector2(-8f, -8f);

        var emptyText = _previewEmpty.GetComponent<TMPro.TMP_Text>();
        emptyText.color = new Color(1f, 1f, 1f, 0.5f);
        emptyText.raycastTarget = false;
    }

    private void RefreshPreview()
    {
        if (_previewImage == null) return;

        SetHighlightVisible(false);
        var drawn = _selected != null && SelectionPreview.Render(_selected);
        SetHighlightVisible(true);

        _previewImage.enabled = drawn;
        if (drawn) _previewImage.texture = SelectionPreview.Texture;

        if (_previewEmpty == null) return;

        _previewEmpty.SetActive(!drawn);
        if (!drawn)
            _previewEmpty.GetComponent<TMPro.TMP_Text>().text =
                _selected == null ? "Nothing selected" : "Nothing to show for this one";
    }

    private void RefreshDetails(bool resize = true)
    {
        if (_details == null) return;

        var hasSelection = _selected != null;
        if (_detailsGO != null) _detailsGO.SetActive(hasSelection);

        if (!hasSelection)
        {
            _flipToggle?.SetValue(false, notify: false);
            _seeThroughGO?.SetActive(false);
            _fogThroughGO?.SetActive(false);
            if (resize) _editor.RequestOptionsResize();
            return;
        }

        var structures = _editor.GetTool<StructureTool>();
        var canSeeThrough = structures?.CanSetSeeThrough(_selected) == true;
        _seeThroughGO?.SetActive(canSeeThrough);
        _fogThroughGO?.SetActive(canSeeThrough);
        if (canSeeThrough)
        {
            _seeThroughToggle?.SetValue(structures.IsSeeThrough(_selected), notify: false);
            _fogThroughToggle?.SetValue(structures.IsFogThrough(_selected), notify: false);
        }

        var transform = _selected.transform;
        var position = transform.position;
        var scale = transform.localScale;

        var scripts = _selected.GetComponents<MonoBehaviour>().Length;
        var nested = _selected.GetComponentsInChildren<MonoBehaviour>(true).Length - scripts;
        var renderers = _selected.GetComponentsInChildren<Renderer>(true).Length;

        _details.text =
            $"{_selected.name}\n" +
            (string.IsNullOrEmpty(_internalName) ? "" : $"Internal   {_internalName}\n") +
            $"Position   {position.x:0.##}, {position.y:0.##}   (z {position.z:0.###})\n" +
            $"Scale   {Mathf.Abs(scale.x):0.###} x {scale.y:0.###}\n" +
            $"{scripts} script(s), {nested} in children, {renderers} renderer(s)" +
            (MapEditorProtection.IsProtected(_selected) ? "\nProtected - cannot be deleted."
                : !MapEditorProtection.CanDelete(_selected)
                    ? "\nOne of the base's own buildings - can be moved, not deleted."
                    : "");

        MapEditorUI.FitLabelHeight(_detailsGO);
        _flipToggle?.SetValue(scale.x < 0f, notify: false);
        if (resize) _editor.RequestOptionsResize();
    }

    private string _internalName;

    private string ResolveInternalName(GameObject go)
    {
        if (_editor.GetTool<StructureTool>()?.TryGetPlacedName(go, out var placed) == true)
            return placed;

        var structure = go.GetComponentInParent<Structure>();
        if (structure != null)
        {
            var type = structure.Brain?.Data != null ? structure.Brain.Data.Type : structure.Type;
            if (type != StructureBrain.TYPES.NONE)
                return COTL_API.CustomStructures.CustomStructureManager.CustomStructureList
                    .TryGetValue(type, out var custom) && custom != null
                    ? custom.InternalName
                    : type.ToString();
        }

        return RoomSnapshot.TryResolveKey(go, out var key) ? key : null;
    }

    public void OnEnter() => _editor.SetStatus("Click an object to select it.");

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Select Object"),
        ("RMB", "Deselect"),
        ("Ctrl + Drag", "Clone"),
        ("Drag", "Yellow = move, Blue = resize, Purple = depth"),
        ("Shift + LMB", "Blue Node stretch"),
        ("Del", "Delete selected")
    ];

    public void OnExit() => Select(null);

    public void OnUpdate()
    {
        if (HandleCloneDrag()) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (_editor.PointerOverUi())
            {
            }
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                BeginClone();
            }
            else
            {
                var world = _editor.MouseWorld();
                var picked = PickAtMouse();

                if (picked == null)
                    _editor.SetStatus($"Nothing at {world.x:0.0}, {world.y:0.0}.");
                else
                    Select(picked);
            }
        }

        if (Input.GetMouseButtonDown(1) && _selected != null && !_editor.PointerOverUi())
        {
            Select(null);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Delete))
            DeleteSelected();

        if (_selected != null && Input.GetMouseButton(0) && Time.unscaledTime >= _nextDetailsAt)
        {
            _nextDetailsAt = Time.unscaledTime + 0.1f;
            RefreshDetails(resize: false);
        }

        if (_selected != null && Input.GetMouseButtonUp(0))
        {
            RefreshDetails();
            RefreshPreview();
        }

        SyncGizmos();
    }

    private float _nextDetailsAt;

    public bool BeginCloneDrag(Vector3 world) => BeginCloneOf(_selected, world);

    private void BeginClone()
    {
        var world = _editor.MouseWorld();

        var source = _selected != null && PointerOver(_selected, world) ? _selected : PickWorldObject(world);
        BeginCloneOf(source, world);
    }

    private static bool PointerOver(GameObject go, Vector3 world)
    {
        var cam = SceneRefs.Cam;
        return cam != null && DrawnUnder(go, cam.WorldToScreenPoint(world), cam);
    }

    private bool BeginCloneOf(GameObject source, Vector3 world)
    {
        if (source == null)
        {
            _editor.SetStatus("Ctrl-click: nothing to clone here.");
            return false;
        }

        if (source.GetComponentInChildren<Interaction_WeaponSelectionPodium>(true) != null)
        {
            _editor.SetStatus("Weapon podiums cannot be cloned. Use the Podium tool instead.");
            return false;
        }

        SetHighlightVisible(false);
        var clone = Object.Instantiate(source, source.transform.parent);
        SetHighlightVisible(true);

        clone.name = NextCloneName(source);
        clone.transform.position = source.transform.position;

        var structures = _editor.GetTool<StructureTool>();
        var adopted = structures != null && structures.TryAdoptClone(source, clone);

        Select(clone);
        _cloneDragging = true;
        _cloneGrabOffset = clone.transform.position - world;
        _editor.MarkEdited();

        _editor.History.Push($"clone {source.name}", () =>
        {
            if (clone == null) return false;
            if (_selected == clone) Select(null);
            Object.Destroy(clone);
            return true;
        });

        _editor.SetStatus(adopted
            ? $"Cloned structure {source.name}. Release to drop."
            : $"Cloned {source.name}. Release to drop.");
        return true;
    }

    // ---- clone names -------------------------------------------------------------------------

    private static readonly Regex CloneSuffix = new(@"(\(Clone\))+$", RegexOptions.Compiled);
    private static readonly Regex TrailingNumber = new(@"\s+\d+$", RegexOptions.Compiled);

    private static string NextCloneName(GameObject source)
    {
        var stem = NameStem(source.name);
        var taken = SiblingNames(source);

        for (var n = 2; n <= 999; n++)
        {
            var candidate = stem + " " + n;
            if (!taken.Contains(candidate)) return candidate;
        }

        return stem + " copy";
    }

    private static string NameStem(string name)
    {
        var stem = TrailingNumber.Replace(CloneSuffix.Replace(name ?? "", ""), "").TrimEnd();
        return string.IsNullOrEmpty(stem) ? "Object" : stem;
    }

    private static HashSet<string> SiblingNames(GameObject source)
    {
        var names = new HashSet<string>();
        var parent = source.transform.parent;

        if (parent != null)
        {
            foreach (Transform child in parent) names.Add(child.name);
            return names;
        }

        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            names.Add(root.name);

        return names;
    }

    private bool HandleCloneDrag()
    {
        if (!_cloneDragging) return false;

        if (_selected == null || !Input.GetMouseButton(0))
        {
            _cloneDragging = false;
            if (_selected != null)
                _editor.SetStatus($"Placed clone {_selected.name} at {_selected.transform.position}.");
            SyncGizmos();
            return false;
        }

        SetSelectedPosition(_editor.MouseWorld() + _cloneGrabOffset);
        SyncGizmos();
        return true;
    }

    private void SyncGizmos()
    {
        if (_selected == null) return;

        MapEditorGizmos.UpdateSelectionBox(_outline, _selected);

        var cam = SceneRefs.Cam;
        if (cam == null) return;

        if (_grip != null)
            _grip.GetComponent<RectTransform>().position =
                cam.WorldToScreenPoint(MapEditorGizmos.GripPosition(_selected));

        if (_resizeNode != null)
            _resizeNode.GetComponent<RectTransform>().position =
                cam.WorldToScreenPoint(MapEditorGizmos.CornerPosition(_selected));

        if (_depthNode != null)
            _depthNode.GetComponent<RectTransform>().position =
                cam.WorldToScreenPoint(MapEditorGizmos.FarCornerPosition(_selected));
    }

    private GameObject PickAtMouse() => PickWorldObject(_editor.MouseWorld());

    public static GameObject PickWorldObject(Vector3 world)
    {
        var cam = SceneRefs.Cam;
        if (cam == null) return null;

        var screen = (Vector2)cam.WorldToScreenPoint(world);

        var hit = Physics2D.OverlapPoint(world);
        if (hit != null && IsSelectable(hit.gameObject) && DrawnUnder(hit.gameObject, screen, cam))
            return SelectionRoot(hit.gameObject);

        GameObject best = null;
        var bestSize = float.MaxValue;

        foreach (var renderer in Object.FindObjectsOfType<Renderer>())
        {
            if (!IsDrawable(renderer)) continue;
            if (!TryScreenRect(renderer.bounds, cam, out var rect)) continue;
            if (!rect.Contains(screen)) continue;

            var size = rect.width * rect.height;
            if (size >= bestSize) continue;

            if (IsPickIgnored(renderer.gameObject)) continue;
            if (MapEditorProtection.IsProtected(renderer.gameObject)) continue;

            bestSize = size;
            best = renderer.gameObject;
        }

        return best != null ? SelectionRoot(best) : null;
    }

    private static bool DrawnUnder(GameObject go, Vector2 screen, Camera cam)
    {
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsVisibleRenderer(renderer)) continue;
            if (TryScreenRect(renderer.bounds, cam, out var rect) && rect.Contains(screen)) return true;
        }

        return false;
    }

    private static bool TryScreenRect(Bounds bounds, Camera cam, out Rect rect)
    {
        rect = default;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var centre = bounds.center;
        var extents = bounds.extents;

        for (var i = 0; i < 8; i++)
        {
            var corner = centre + new Vector3(
                (i & 1) == 0 ? -extents.x : extents.x,
                (i & 2) == 0 ? -extents.y : extents.y,
                (i & 4) == 0 ? -extents.z : extents.z);

            var point = cam.WorldToScreenPoint(corner);

            if (point.z <= 0f) return false;

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static bool IsVisibleRenderer(Renderer renderer)
    {
        if (!IsDrawable(renderer)) return false;
        return !IsPickIgnored(renderer.gameObject);
    }

    private static bool IsDrawable(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled) return false;
        if (!renderer.gameObject.activeInHierarchy) return false;
        if (renderer is ParticleSystemRenderer) return false;
        return renderer is SpriteRenderer || renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    private static bool IsPickIgnored(GameObject go)
    {
        if (go.GetComponentInParent<HPBar>() != null) return true;

        if (go.GetComponentInParent<CTMapTrigger>() != null) return true;

        return false;
    }

    private static bool IsSelectable(GameObject go)
    {
        if (go == null || MapEditorProtection.IsProtected(go)) return false;
        if (IsPickIgnored(go)) return false;

        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            if (IsVisibleRenderer(renderer)) return true;

        return false;
    }

    private static GameObject SelectionRoot(GameObject go)
    {
        if (go == null) return null;

        var room = SceneRefs.Room;
        var roomRoot = room != null ? room.transform : null;
        var stops = new HashSet<Transform>();
        if (roomRoot != null) stops.Add(roomRoot);
        if (room != null)
        {
            if (room.CustomTransform != null) stops.Add(room.CustomTransform.transform);
            if (room.SceneryTransform != null) stops.Add(room.SceneryTransform.transform);
            if (room.HeavyAssetsTransform != null) stops.Add(room.HeavyAssetsTransform);
        }

        var content = SceneRefs.ContentRoot;
        if (content != null) stops.Add(content);

        var inBase = RuntimeMapEditor.Context == EditorContext.Base;

        var current = go.transform;
        var best = current;

        while (current.parent != null && !stops.Contains(current.parent))
        {
            if (inBase && IsRegion(current.parent)) break;

            current = current.parent;
            if (MapEditorProtection.IsProtected(current.gameObject)) break;
            best = current;
        }

        if (inBase)
        {
            var structure = best.GetComponentInParent<Structure>();
            if (structure != null && !stops.Contains(structure.transform) &&
                !MapEditorProtection.IsProtected(structure.gameObject))
                return structure.gameObject;
        }

        return best.gameObject;
    }

    private const float RegionSize = 9f;

    private static bool IsRegion(Transform parent)
    {
        var renderers = parent.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        var bounds = new Bounds();
        var started = false;

        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (renderer is ParticleSystemRenderer) continue;

            if (!started)
            {
                bounds = renderer.bounds;
                started = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        if (!started) return false;

        return bounds.size.x > RegionSize || bounds.size.y > RegionSize;
    }

    private void Select(GameObject go)
    {
        ClearHighlight();
        _selected = go;

        _internalName = _selected == null ? null : ResolveInternalName(_selected);

        if (_selected == null)
        {
            _editor.SetStatus("Nothing selected.");
            RefreshDetails();
            RefreshPreview();
            return;
        }

        ApplyHighlight(_selected);
        _editor.SetStatus("Selected: " + _selected.name);

        RefreshDetails();
        RefreshPreview();
    }

    private void ApplyHighlight(GameObject go)
    {
        foreach (var renderer in go.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null) continue;

            if (renderer is SpriteRenderer sprite)
            {
                _highlighted.Add(renderer);
                _originalColors.Add(sprite.color);
                continue;
            }

            var shared = renderer.sharedMaterial;
            if (shared == null || !shared.HasProperty("_Color")) continue;

            _highlighted.Add(renderer);
            _originalColors.Add(shared.color);
        }

        SetHighlightVisible(true);
        DrawOutline(go);
    }

    private const float HighlightBlend = 0.45f;

    private static Color Tinted(Color original) => Color.Lerp(original, Color.cyan, HighlightBlend);

    private void SetHighlightVisible(bool on)
    {
        for (var i = 0; i < _highlighted.Count; i++)
        {
            var renderer = _highlighted[i];
            if (renderer == null) continue;

            var original = _originalColors[i];
            var wanted = on ? Tinted(original) : original;

            if (renderer is SpriteRenderer sprite)
            {
                sprite.color = wanted;
                continue;
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", wanted);
            renderer.SetPropertyBlock(block);
        }
    }

    private void DrawOutline(GameObject go)
    {
        _outline = MapEditorGizmos.CreateSelectionBox(go, "MapEditor_SelectionOutline");
        _grip = CreateHandle("Grip", MapEditorGizmos.GripColour, SelectHandle.Mode.Move, 30f);
        _resizeNode = CreateHandle("Resize", ResizeColour, SelectHandle.Mode.Resize, 24f);
        _depthNode = CreateHandle("Depth", DepthColour, SelectHandle.Mode.Depth, 24f);
    }

    private static readonly Color ResizeColour = new(0.25f, 0.85f, 1f, 0.95f);

    private static readonly Color DepthColour = new(0.72f, 0.4f, 1f, 0.95f);

    private GameObject CreateHandle(string name, Color colour, SelectHandle.Mode mode, float size)
    {
        var go = new GameObject("MapEditor_Selection" + name);
        go.transform.SetParent(GripRoot(), false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);

        var img = go.AddComponent<Image>();
        img.color = colour;

        var handle = go.AddComponent<SelectHandle>();
        handle.Initialize(this, _editor, mode);

        _editor.RegisterUiBlocker(rt);
        return go;
    }

    private Transform GripRoot()
    {
        if (_gripCanvas != null) return _gripCanvas.transform;

        var go = new GameObject("MapEditor_SelectHandles");
        go.transform.SetParent(_editor.transform, false);

        _gripCanvas = go.AddComponent<Canvas>();
        _gripCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _gripCanvas.sortingOrder = 5001;
        go.AddComponent<GraphicRaycaster>();
        return go.transform;
    }

    // ---- undoable gestures ----------------------------------------------------------------------

    private GameObject _gestureTarget;
    private Vector3 _gesturePosition;
    private Vector3 _gestureScale;

    public void BeginGesture()
    {
        _gestureTarget = _selected;
        if (_selected == null) return;

        _gesturePosition = _selected.transform.position;
        _gestureScale = _selected.transform.localScale;
    }

    public void EndGesture(string label)
    {
        var target = _gestureTarget;
        _gestureTarget = null;
        if (target == null) return;

        var position = _gesturePosition;
        var scale = _gestureScale;

        if (target.transform.position == position && target.transform.localScale == scale) return;

        BaseDelta.NoteMoved(target, position, scale);

        _editor.History.Push($"{label} {target.name}", () =>
        {
            if (target == null) return false;

            var undoneFrom = target.transform.position;
            var undoneScale = target.transform.localScale;

            target.transform.position = position;
            target.transform.localScale = scale;
            BaseDelta.NoteMoved(target, undoneFrom, undoneScale);
            _editor.KeepCullingSuspended = true;

            if (_selected == target)
            {
                RefreshDetails();
                RefreshPreview();
            }

            return true;
        });
    }

    public bool HasSelection => _selected != null;

    public Vector3 SelectedPosition => _selected != null ? _selected.transform.position : Vector3.zero;

    public void SetSelectedPosition(Vector3 world)
    {
        if (_selected == null) return;
        var z = _selected.transform.position.z;
        _selected.transform.position = new Vector3(world.x, world.y, z);

        _editor.KeepCullingSuspended = true;
        _editor.MarkEdited();
    }

    // ---- resizing -------------------------------------------------------------------------

    public bool BeginResize(Vector3 world)
    {
        if (_selected == null) return false;

        if (_selected.GetComponentInChildren<Door>(true) != null ||
            _selected.GetComponentInParent<Door>(true) != null)
        {
            _editor.SetStatus("Doors cannot be resized.", StatusSeverity.Warning);
            return false;
        }

        _resizeStartScale = _selected.transform.localScale;
        _resizeStartCentre = MapEditorGizmos.GripPosition(_selected);
        _resizeStartGrab = world - _resizeStartCentre;
        _resizeStartGrab.z = 0f;

        if (_resizeStartGrab.magnitude < 0.05f)
        {
            _editor.SetStatus("This object is too small to resize by dragging.", StatusSeverity.Warning);
            return false;
        }

        return true;
    }

    public void ResizeTo(Vector3 world, bool perAxis)
    {
        if (_selected == null) return;

        var grab = world - _resizeStartCentre;
        grab.z = 0f;

        var scale = _resizeStartScale;
        if (perAxis)
        {
            scale.x = AxisScale(_resizeStartScale.x, grab.x, _resizeStartGrab.x);
            scale.y = AxisScale(_resizeStartScale.y, grab.y, _resizeStartGrab.y);
        }
        else
        {
            var factor = Mathf.Clamp(grab.magnitude / _resizeStartGrab.magnitude, 0.02f, 50f);
            scale = _resizeStartScale * factor;
        }

        _selected.transform.localScale = scale;

        var centre = MapEditorGizmos.GripPosition(_selected);
        var drift = _resizeStartCentre - centre;
        var position = _selected.transform.position;
        _selected.transform.position = new Vector3(position.x + drift.x, position.y + drift.y, position.z);

        _editor.KeepCullingSuspended = true;
        _editor.MarkEdited();
        _editor.SetStatus($"{_selected.name} scale {scale.x:0.##} x {scale.y:0.##}");
    }

    private static float AxisScale(float startScale, float grab, float startGrab)
    {
        if (Mathf.Abs(startGrab) < 0.05f) return startScale;
        return startScale * Mathf.Clamp(grab / startGrab, 0.02f, 50f);
    }

    // ---- depth ------------------------------------------------------------------------------

    private const float DepthPerPixel = 0.01f;

    private float _depthStartZ;
    private float _depthStartScreenY;

    public bool BeginDepth(float screenY)
    {
        if (_selected == null) return false;

        _depthStartZ = _selected.transform.position.z;
        _depthStartScreenY = screenY;
        return true;
    }

    public void DepthTo(float screenY)
    {
        if (_selected == null) return;

        var p = _selected.transform.position;
        var z = _depthStartZ + (screenY - _depthStartScreenY) * DepthPerPixel;
        _selected.transform.position = new Vector3(p.x, p.y, z);

        _editor.KeepCullingSuspended = true;
        _editor.MarkEdited();
        _editor.SetStatus($"{_selected.name} Z: {z:0.###}");
    }

    private void SetLook(bool? player, bool? fog)
    {
        var structures = _editor.GetTool<StructureTool>();
        if (_selected == null || structures == null) return;

        var wasPlayer = structures.IsSeeThrough(_selected);
        var wasFog = structures.IsFogThrough(_selected);

        var wantPlayer = player ?? wasPlayer;
        var wantFog = fog ?? wasFog;
        if (wantPlayer == wasPlayer && wantFog == wasFog) return;

        if (!structures.TrySetSeeThrough(_selected, wantPlayer, wantFog))
        {
            _editor.SetStatus("This one cannot take those looks.", StatusSeverity.Warning);
            _seeThroughToggle?.SetValue(wasPlayer, notify: false);
            _fogThroughToggle?.SetValue(wasFog, notify: false);
            return;
        }

        var nowPlayer = structures.IsSeeThrough(_selected);
        var nowFog = structures.IsFogThrough(_selected);
        _seeThroughToggle?.SetValue(nowPlayer, notify: false);
        _fogThroughToggle?.SetValue(nowFog, notify: false);

        var target = _selected;
        _editor.History.Push("appearance", () =>
        {
            if (target == null) return false;
            structures.TrySetSeeThrough(target, wasPlayer, wasFog);
            if (_selected == target) RefreshDetails();
            return true;
        });

        _editor.SetStatus(nowPlayer || nowFog
            ? $"See-through {(nowPlayer ? "on" : "off")}, fog {(nowFog ? "on" : "off")}."
            : "Back to normal.");
    }

    private void SetFlipped(bool flipped)
    {
        if (_selected == null)
        {
            _editor.SetStatus("Nothing selected.");
            _flipToggle?.SetValue(false, notify: false);
            return;
        }

        if (_selected.GetComponentInChildren<Door>(true) != null ||
            _selected.GetComponentInParent<Door>(true) != null)
        {
            _editor.SetStatus("Doors cannot be flipped.", StatusSeverity.Warning);
            _flipToggle?.SetValue(_selected.transform.localScale.x < 0f, notify: false);
            return;
        }

        var scale = _selected.transform.localScale;
        var wanted = flipped ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        if (Mathf.Approximately(scale.x, wanted)) return;

        BeginGesture();
        _selected.transform.localScale = new Vector3(wanted, scale.y, scale.z);

        _editor.GetTool<StructureTool>()?.TryFlip(_selected);

        _editor.KeepCullingSuspended = true;
        _editor.MarkEdited();
        EndGesture("flip");
        _editor.SetStatus(flipped ? "Flipped horizontally." : "Flip removed.");

        RefreshDetails();
        RefreshPreview();
    }

    private void ClearHighlight()
    {
        for (var i = 0; i < _highlighted.Count; i++)
        {
            var renderer = _highlighted[i];
            if (renderer == null) continue;

            if (renderer is SpriteRenderer sprite) sprite.color = _originalColors[i];
            else renderer.SetPropertyBlock(null);
        }
        _highlighted.Clear();
        _originalColors.Clear();

        if (_outline != null) Object.Destroy(_outline);
        _outline = null;

        if (_grip != null) Object.Destroy(_grip);
        _grip = null;

        if (_resizeNode != null) Object.Destroy(_resizeNode);
        _resizeNode = null;

        if (_depthNode != null) Object.Destroy(_depthNode);
        _depthNode = null;
    }

    private void DeleteSelected()
    {
        if (_selected == null)
        {
            _editor.SetStatus("Nothing selected.");
            return;
        }

        if (MapEditorProtection.IsProtected(_selected))
        {
            _editor.SetStatus($"'{_selected.name}' is protected.", StatusSeverity.Warning);
            return;
        }

        if (!MapEditorProtection.CanDelete(_selected))
        {
            _editor.SetStatus($"'{_selected.name}' is one of the base's own buildings - it can be " +
                              "moved, not deleted. Demolish it from the build totem.",
                StatusSeverity.Warning);
            return;
        }

        var path = HierarchyPath(_selected.transform);

        var journalled = BaseDelta.NoteRemoved(_selected);

        ClearHighlight();
        Object.Destroy(_selected);
        _selected = null;

        SceneRefs.RescanNavigation();
        _editor.MarkEdited();
        _editor.SetStatus("Deleted " + path +
                          (journalled ? " - it stays gone once the base is saved." : ""));
    }

    private static string HierarchyPath(Transform t)
    {
        var path = t.name;
        var parent = t.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}

public class SelectHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public enum Mode
    {
        Move,
        Resize,
        Depth
    }

    private SelectTool _tool;
    private RuntimeMapEditor _editor;
    private Mode _mode;
    private Vector3 _grabOffset;
    private bool _resizing;
    private bool _cloning;
    private bool _shifting;

    public void Initialize(SelectTool tool, RuntimeMapEditor editor, Mode mode)
    {
        _tool = tool;
        _editor = editor;
        _mode = mode;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _cloning = false;
        _resizing = false;
        _shifting = false;

        if (_tool == null || _editor == null || !_tool.HasSelection) return;

        var world = _editor.ScreenToWorld(eventData.position);

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            _cloning = _tool.BeginCloneDrag(world);
            if (_cloning) return;
        }

        _tool.BeginGesture();

        switch (_mode)
        {
            case Mode.Move:
                _grabOffset = _tool.SelectedPosition - world;
                break;

            case Mode.Depth:
                _shifting = _tool.BeginDepth(eventData.position.y);
                break;

            default:
                _resizing = _tool.BeginResize(world);
                break;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_tool == null || _cloning) return;

        _tool.EndGesture(_mode switch
        {
            Mode.Move => "move",
            Mode.Depth => "depth of",
            _ => "resize"
        });
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null || _cloning) return;

        if (_mode == Mode.Depth)
        {
            if (_shifting) _tool.DepthTo(eventData.position.y);
            return;
        }

        var world = _editor.ScreenToWorld(eventData.position);

        if (_mode == Mode.Move) _tool.SetSelectedPosition(world + _grabOffset);
        else if (_resizing)
            _tool.ResizeTo(world, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
    }
}
