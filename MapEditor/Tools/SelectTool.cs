using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class SelectTool : IMapEditorTool, IMapEditorShortcuts, IMapDataContributor, Net.IMapEditorLivePreview
{
    public string Name => "Select";

    // ---- multiplayer -------------------------------------------------------------------------

    public bool LiveActive => _gesture.Count > 0 || _cloneDragging;

    public IEnumerable<GameObject> LiveObjects => AllSelected();

    public bool IsSelected(GameObject go) => go != null && (go == _selected || _extra.Contains(go));

    private readonly RuntimeMapEditor _editor;

    // The primary selection, and the rest of a multi-selection. Gizmos frame all of them, moves and
    // depth changes apply to all, resize and the look toggles act on the primary alone.
    private GameObject _selected;
    private readonly List<GameObject> _extra = [];
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
    private GameObject _windGO;
    private MapEditorToggle _windToggle;

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        BuildPreviewBox(panel, ui);

        _detailsGO = ui.CreateLabel(panel, "", 15);
        _details = _detailsGO.GetComponent<TMPro.TMP_Text>();
        _details.color = new Color(1f, 1f, 1f, 0.78f);

        var flip = ui.CreateToggle(panel, "Flipped horizontally", false, SetFlipped);
        _flipToggle = flip.GetComponent<MapEditorToggle>();

        _seeThroughGO = ui.CreateToggle(panel, "See-through", false,
            on => SetLook(player: on, fog: null, wind: null));
        _seeThroughToggle = _seeThroughGO.GetComponent<MapEditorToggle>();
        _seeThroughGO.SetActive(false);

        _fogThroughGO = ui.CreateToggle(panel, "Fog pass-through", false,
            on => SetLook(player: null, fog: on, wind: null));
        _fogThroughToggle = _fogThroughGO.GetComponent<MapEditorToggle>();
        _fogThroughGO.SetActive(false);

        _windGO = ui.CreateToggle(panel, "Affected by Wind", false,
            on => SetLook(player: null, fog: null, wind: on));
        _windToggle = _windGO.GetComponent<MapEditorToggle>();
        _windGO.SetActive(false);

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
            _windGO?.SetActive(false);
            if (resize) _editor.RequestOptionsResize();
            return;
        }

        if (_extra.Count > 0)
        {
            _seeThroughGO?.SetActive(false);
            _fogThroughGO?.SetActive(false);
            _windGO?.SetActive(false);
            _flipToggle?.SetValue(false, notify: false);

            var all = AllSelected();
            var groupId = MapEditorGroups.GroupOf(_selected);
            var wholeGroup = groupId != null && all.All(g => MapEditorGroups.GroupOf(g) == groupId);
            var names = string.Join("\n", all.Take(8).Select(g => "  " + g.name));
            if (all.Count > 8) names += $"\n  ... and {all.Count - 8} more";

            _details.text =
                (wholeGroup
                    ? $"{MapEditorGroups.NameOf(groupId)}   ({all.Count} objects)"
                    : $"{all.Count} objects selected") +
                "\n" + names +
                "\nDrag the yellow grip to move them together; purple shifts depth." +
                (wholeGroup ? "\nCtrl+G ungroups." : "\nCtrl+G makes them a group.");

            MapEditorUI.FitLabelHeight(_detailsGO);
            if (resize) _editor.RequestOptionsResize();
            return;
        }

        var structures = _editor.GetTool<StructureTool>();
        var canSeeThrough = structures?.CanSetSeeThrough(_selected) == true;
        _seeThroughGO?.SetActive(canSeeThrough);
        _fogThroughGO?.SetActive(canSeeThrough);
        _windGO?.SetActive(canSeeThrough);
        if (canSeeThrough)
        {
            _seeThroughToggle?.SetValue(structures.IsSeeThrough(_selected), notify: false);
            _fogThroughToggle?.SetValue(structures.IsFogThrough(_selected), notify: false);
            _windToggle?.SetValue(structures.IsWind(_selected), notify: false);
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
            (MapEditorGroups.GroupOf(_selected) != null
                ? $"\nIn {MapEditorGroups.NameOf(MapEditorGroups.GroupOf(_selected))}" : "") +
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
        ("Shift + LMB", "Add to / remove from selection"),
        ("RMB", "Deselect"),
        ("Ctrl + Drag", "Clone"),
        ("Drag", "Yellow = move, Blue = resize, Purple = depth"),
        ("Shift + Blue", "Stretch one axis"),
        ("Ctrl + G", "Group / ungroup selection"),
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
                var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (picked == null)
                {
                    if (!shift) _editor.SetStatus($"Nothing at {world.x:0.0}, {world.y:0.0}.");
                }
                else if (shift)
                    ToggleInSelection(picked);
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

        if (RuntimeMapEditor.CtrlHeld && Input.GetKeyDown(KeyCode.G))
            GroupOrUngroup();

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
        if (!TryUnionBounds(out var bounds)) return;

        MapEditorGizmos.SetBox(_outline, bounds, _selected.transform.position.z - 0.05f);

        var cam = SceneRefs.Cam;
        if (cam == null) return;

        if (_grip != null)
            _grip.GetComponent<RectTransform>().position = cam.WorldToScreenPoint(bounds.center);

        // One object resizes from its corner; several move and shift depth together but do not resize.
        if (_resizeNode != null)
        {
            _resizeNode.SetActive(_extra.Count == 0);
            _resizeNode.GetComponent<RectTransform>().position =
                cam.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.max.y, bounds.center.z));
        }

        if (_depthNode != null)
            _depthNode.GetComponent<RectTransform>().position =
                cam.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.max.y, bounds.center.z));
    }

    /// The bounds of everything selected together, which is what the box and the grip frame.
    private bool TryUnionBounds(out Bounds bounds)
    {
        bounds = default;
        var found = false;

        foreach (var go in AllSelected())
        {
            if (go == null || !MapEditorGizmos.TryGetBounds(go, out var own)) continue;
            if (!found)
            {
                bounds = own;
                found = true;
            }
            else
            {
                bounds.Encapsulate(own);
            }
        }

        return found;
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

        // Whiteboard marks are drawn over the room, never part of it.
        if (go.GetComponentInParent<CTWhiteboardStroke>() != null) return true;

        return false;
    }

    internal static bool IsSelectable(GameObject go)
    {
        if (go == null || MapEditorProtection.IsProtected(go)) return false;
        if (IsPickIgnored(go)) return false;

        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            if (IsVisibleRenderer(renderer)) return true;

        return false;
    }

    /// <summary>
    /// The containers a selection never climbs past: the room itself, its custom, scenery and heavy
    /// groups, and the editor's own content root. Their direct children are the things one can pick,
    /// which is also what the layer tree lists.
    /// </summary>
    internal static HashSet<Transform> SelectionStops()
    {
        var room = SceneRefs.Room;
        var stops = new HashSet<Transform>();
        if (room != null)
        {
            stops.Add(room.transform);
            if (room.CustomTransform != null) stops.Add(room.CustomTransform.transform);
            if (room.SceneryTransform != null) stops.Add(room.SceneryTransform.transform);
            if (room.HeavyAssetsTransform != null) stops.Add(room.HeavyAssetsTransform);
        }

        var content = SceneRefs.ContentRoot;
        if (content != null) stops.Add(content);
        return stops;
    }

    /// <summary>
    /// Every object a click could land on, once each: the direct children of the selection stops
    /// (stepping one level into a base placement region), filtered by IsSelectable and resolved through
    /// SelectionRoot. The layer tree lists exactly this, and group restore matches against it.
    /// </summary>
    internal static List<GameObject> AllSelectableRoots()
    {
        var roots = new List<GameObject>();
        var seen = new HashSet<GameObject>();
        var stops = SelectionStops();
        var inBase = RuntimeMapEditor.Context == EditorContext.Base;

        void Consider(Transform candidate)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy) return;
            if (!IsSelectable(candidate.gameObject)) return;

            var root = SelectionRoot(candidate.gameObject);
            if (root != null && seen.Add(root)) roots.Add(root);
        }

        foreach (var stop in stops)
        {
            if (stop == null) continue;
            for (var i = 0; i < stop.childCount; i++)
            {
                var child = stop.GetChild(i);
                if (stops.Contains(child)) continue;

                if (inBase && IsRegion(child))
                {
                    for (var j = 0; j < child.childCount; j++) Consider(child.GetChild(j));
                    continue;
                }

                Consider(child);
            }
        }

        return roots;
    }

    internal static GameObject SelectionRoot(GameObject go)
    {
        if (go == null) return null;

        var stops = SelectionStops();

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

    internal static bool IsRegion(Transform parent)
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

    public GameObject Selected => _selected;

    /// Everything selected, primary first. A fresh list each call.
    public IReadOnlyList<GameObject> Selection => AllSelected();

    public int SelectionCount => AllSelected().Count;

    private List<GameObject> AllSelected()
    {
        var list = new List<GameObject>();
        if (_selected != null) list.Add(_selected);
        foreach (var go in _extra)
            if (go != null && go != _selected && !list.Contains(go)) list.Add(go);
        return list;
    }

    /// Selection from outside the world - the layer tree - goes through the same path as a click.
    public void SelectObject(GameObject go) => Select(go);

    /// <summary>
    /// Selects exactly these, widened to whole groups: picking any member of a group picks the group.
    /// The first becomes the primary, which is the one resize and the look toggles act on.
    /// </summary>
    public void SelectMany(IEnumerable<GameObject> objects)
    {
        var wanted = MapEditorGroups.Expand(objects);

        // What the other player holds stays theirs; the rest of the click still lands.
        if (Net.EditorNet.Enabled)
        {
            var held = wanted.RemoveAll(go => !IsSelected(go) && Net.EditorPresence.LockedByPeer(go));
            if (held > 0) _editor.SetStatus(Net.EditorPresence.LockMessage, StatusSeverity.Warning);
        }

        ClearHighlight();
        _extra.Clear();
        _selected = wanted.Count > 0 ? wanted[0] : null;
        for (var i = 1; i < wanted.Count; i++) _extra.Add(wanted[i]);

        AfterSelectionChanged();
    }

    /// Shift-click: adds an object (and its group) to the selection, or takes it out if it is already in.
    public void ToggleInSelection(GameObject go)
    {
        if (go == null) return;

        var current = AllSelected();
        var members = MapEditorGroups.Expand([go]);

        if (members.All(current.Contains)) current.RemoveAll(members.Contains);
        else foreach (var member in members) if (!current.Contains(member)) current.Add(member);

        SelectMany(current);
    }

    private void Select(GameObject go) => SelectMany(go == null ? [] : [go]);

    private void AfterSelectionChanged()
    {
        _internalName = _selected == null ? null : ResolveInternalName(_selected);

        if (_selected == null)
        {
            _editor.SetStatus("Nothing selected.");
            RefreshDetails();
            RefreshPreview();
            return;
        }

        var all = AllSelected();
        foreach (var go in all) ApplyHighlight(go);
        EnsureGizmos();

        var group = MapEditorGroups.GroupOf(_selected);
        _editor.SetStatus(all.Count == 1
            ? "Selected: " + _selected.name
            : group != null && all.All(g => MapEditorGroups.GroupOf(g) == group)
                ? $"Selected {MapEditorGroups.NameOf(group)} ({all.Count} objects). Ctrl+G ungroups."
                : $"Selected {all.Count} objects. Ctrl+G groups them.");

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
    }

    private void EnsureGizmos()
    {
        if (_outline == null) DrawOutline();
        SyncGizmos();
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

    private void DrawOutline()
    {
        _outline = MapEditorGizmos.CreateBox("MapEditor_SelectionOutline", MapEditorGizmos.BoxColour);
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

    private readonly List<(GameObject Go, Vector3 Position, Vector3 Scale)> _gesture = [];

    public void BeginGesture()
    {
        _gesture.Clear();
        foreach (var go in AllSelected())
            _gesture.Add((go, go.transform.position, go.transform.localScale));
    }

    public void EndGesture(string label)
    {
        var moved = _gesture
            .Where(g => g.Go != null &&
                        (g.Go.transform.position != g.Position || g.Go.transform.localScale != g.Scale))
            .ToList();
        _gesture.Clear();
        if (moved.Count == 0) return;

        foreach (var g in moved) BaseDelta.NoteMoved(g.Go, g.Position, g.Scale);

        var what = moved.Count == 1 ? moved[0].Go.name : $"{moved.Count} objects";
        _editor.History.Push($"{label} {what}", () =>
        {
            var any = false;
            foreach (var g in moved)
            {
                if (g.Go == null) continue;

                var undoneFrom = g.Go.transform.position;
                var undoneScale = g.Go.transform.localScale;

                g.Go.transform.position = g.Position;
                g.Go.transform.localScale = g.Scale;
                BaseDelta.NoteMoved(g.Go, undoneFrom, undoneScale);
                any = true;
            }

            _editor.KeepCullingSuspended = true;

            if (any && _selected != null)
            {
                RefreshDetails();
                RefreshPreview();
            }

            return any;
        });
    }

    public bool HasSelection => _selected != null;

    public Vector3 SelectedPosition => _selected != null ? _selected.transform.position : Vector3.zero;

    public void SetSelectedPosition(Vector3 world)
    {
        if (_selected == null) return;

        // The primary lands where asked; everything else keeps its offset from it.
        var from = _selected.transform.position;
        var delta = new Vector3(world.x - from.x, world.y - from.y, 0f);

        foreach (var go in AllSelected())
            if (go != null) go.transform.position += delta;

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

        var z = _depthStartZ + (screenY - _depthStartScreenY) * DepthPerPixel;
        var delta = z - _selected.transform.position.z;

        foreach (var go in AllSelected())
        {
            if (go == null) continue;
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x, p.y, p.z + delta);
        }

        _editor.KeepCullingSuspended = true;
        _editor.MarkEdited();
        _editor.SetStatus(_extra.Count == 0
            ? $"{_selected.name} Z: {z:0.###}"
            : $"{_extra.Count + 1} objects, primary Z: {z:0.###}");
    }

    private void SetLook(bool? player, bool? fog, bool? wind)
    {
        var structures = _editor.GetTool<StructureTool>();
        if (_selected == null || structures == null) return;

        var wasPlayer = structures.IsSeeThrough(_selected);
        var wasFog = structures.IsFogThrough(_selected);
        var wasWind = structures.IsWind(_selected);

        var wantPlayer = player ?? wasPlayer;
        var wantFog = fog ?? wasFog;
        var wantWind = wind ?? wasWind;

        if (wantPlayer == wasPlayer && wantFog == wasFog && wantWind == wasWind) return;

        if (!structures.TrySetSeeThrough(_selected, wantPlayer, wantFog, wantWind))
        {
            _editor.SetStatus("This one cannot take those looks.", StatusSeverity.Warning);
            _seeThroughToggle?.SetValue(wasPlayer, notify: false);
            _fogThroughToggle?.SetValue(wasFog, notify: false);
            _windToggle?.SetValue(wasWind, notify: false);
            return;
        }

        var nowPlayer = structures.IsSeeThrough(_selected);
        var nowFog = structures.IsFogThrough(_selected);
        var nowWind = structures.IsWind(_selected);
        _seeThroughToggle?.SetValue(nowPlayer, notify: false);
        _fogThroughToggle?.SetValue(nowFog, notify: false);
        _windToggle?.SetValue(nowWind, notify: false);

        var target = _selected;
        _editor.History.Push("appearance", () =>
        {
            if (target == null) return false;
            structures.TrySetSeeThrough(target, wasPlayer, wasFog, wasWind);
            if (_selected == target) RefreshDetails();
            return true;
        });

        if (wantWind && !nowWind)
        {
            _editor.SetStatus("This one has no sprite that can sway.", StatusSeverity.Warning);
            return;
        }

        _editor.SetStatus(nowPlayer || nowFog || nowWind
            ? $"See-through {(nowPlayer ? "on" : "off")}, fog {(nowFog ? "on" : "off")}, " +
              $"sway {(nowWind ? "on" : "off")}."
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
        var all = AllSelected();
        if (all.Count == 0)
        {
            _editor.SetStatus("Nothing selected.");
            return;
        }

        var deleted = 0;
        var kept = new List<string>();
        var journalled = false;
        string lastPath = null;

        foreach (var go in all)
        {
            if (go == null) continue;

            if (MapEditorProtection.IsProtected(go) || !MapEditorProtection.CanDelete(go) ||
                Net.EditorPresence.LockedByPeer(go))
            {
                kept.Add(go.name);
                continue;
            }

            lastPath = HierarchyPath(go.transform);
            journalled |= BaseDelta.NoteRemoved(go);
            Object.Destroy(go);
            deleted++;
        }

        ClearHighlight();
        _selected = null;
        _extra.Clear();
        RefreshDetails();
        RefreshPreview();

        if (deleted > 0)
        {
            SceneRefs.RescanNavigation();
            _editor.MarkEdited();
        }

        if (deleted == 0 && kept.Count == 1)
        {
            var go = all[0];
            _editor.SetStatus(MapEditorProtection.IsProtected(go)
                    ? $"'{kept[0]}' is protected."
                    : $"'{kept[0]}' is one of the base's own buildings - it can be moved, not deleted. " +
                      "Demolish it from the build totem.",
                StatusSeverity.Warning);
            return;
        }

        var summary = deleted == 1 && kept.Count == 0
            ? "Deleted " + lastPath
            : $"Deleted {deleted} object(s)";
        if (kept.Count > 0) summary += $"; {kept.Count} protected one(s) left alone";
        if (journalled) summary += " - they stay gone once the base is saved";
        _editor.SetStatus(summary + ".", kept.Count > 0 ? StatusSeverity.Warning : StatusSeverity.Info);
    }

    // ---- groups ---------------------------------------------------------------------------------

    /// <summary>
    /// Ctrl+G. Two or more selected objects become a group; a selection that is exactly one whole
    /// group is taken apart again. Members keep their own places in the hierarchy - see CTEditorGroup.
    /// </summary>
    private void GroupOrUngroup()
    {
        var all = AllSelected();
        if (all.Count < 2)
        {
            _editor.SetStatus("Shift-click two or more objects first, then Ctrl+G groups them.",
                StatusSeverity.Warning);
            return;
        }

        var ids = all.Select(MapEditorGroups.GroupOf).Distinct().ToList();
        if (ids.Count == 1 && ids[0] != null && MapEditorGroups.Members(ids[0]).Count == all.Count)
        {
            var id = ids[0];
            var name = MapEditorGroups.NameOf(id) ?? "the group";
            var members = MapEditorGroups.Members(id);

            MapEditorGroups.Dissolve(id);
            _editor.MarkEdited();
            _editor.History.Push($"ungroup {name}", () =>
            {
                MapEditorGroups.Create(members.Where(m => m != null), name, id);
                return true;
            });

            SelectMany(all);
            _editor.SetStatus($"Ungrouped {name}; the {all.Count} objects stay selected.");
            return;
        }

        var created = MapEditorGroups.Create(all);
        var createdName = MapEditorGroups.NameOf(created);
        _editor.MarkEdited();
        _editor.History.Push($"group {createdName}", () =>
        {
            MapEditorGroups.Dissolve(created);
            return true;
        });

        SelectMany(all);
        _editor.SetStatus($"Grouped {all.Count} objects as {createdName}. Clicking any of them selects " +
                          "the whole group; Ctrl+G again ungroups.");
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        if (map == null) return;
        map.Groups = MapEditorGroups.Capture();
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
