using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

// Click an object to select it, then delete it individually. Ctrl-click-drag clones it.
//
// Picking uses physics first, then falls back to renderer bounds, because much of the room
// dressing is purely visual and carries no collider.
//
// Deleting needs no bookkeeping: the blueprint is a full snapshot of what exists at save time,
// so a deleted object is simply absent from it.
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
    private Canvas _gripCanvas;

    public SelectTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateButton(panel, "Delete Selected", DeleteSelected);
        ui.CreateButton(panel, "Deselect", () => Select(null));

        ui.CreateButton(panel, "Send Back (Z+)", () => NudgeZ(ZStep));
        ui.CreateButton(panel, "Bring Front (Z-)", () => NudgeZ(-ZStep));
        ui.CreateButton(panel, "Flip Horizontal", FlipHorizontal);
    }

    private const float ZStep = 0.1f;

    public void OnEnter() => _editor.SetStatus("Click an object to select it.");

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Select object"),
        ("Ctrl", "+ drag to clone"),
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
                // _editor.SetStatus("Click was over the editor UI, ignored.");
            }
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                BeginClone();
            }
            else
            {
                var world = _editor.MouseWorld();
                var picked = PickAtMouse();

                // Reported either way: a click that finds nothing is the main thing to diagnose.
                Plugin.Log.LogInfo($"MapEditor select: click at world {world}, " +
                                   $"physics hit={DescribeHit(Physics2D.OverlapPoint(world))}, " +
                                   $"picked={(picked != null ? picked.name : "<none>")}");

                if (picked == null)
                    _editor.SetStatus($"Nothing at {world.x:0.0}, {world.y:0.0}. See log for details.");
                else
                    Select(picked);
            }
        }

        if (Input.GetKeyDown(KeyCode.Delete))
            DeleteSelected();

        SyncGizmos();
    }

    // Ctrl-click on an object spawns a copy under the same parent and drags it until the button
    // is released. Doors can never be cloned: they are protected, so PickAtMouse never returns
    // them. A cloned editor-placed structure is adopted by the structure tool so it saves with
    // its type; anything else is picked up by the room snapshot's name-based resolution.
    private void BeginClone()
    {
        var source = PickAtMouse();
        if (source == null)
        {
            _editor.SetStatus("Ctrl-click: nothing to clone here.");
            return;
        }

        // A cloned podium carries post-Awake state and self-destroys or misbehaves on enable;
        // the podium tool is the supported way to add more.
        if (source.GetComponentInChildren<Interaction_WeaponSelectionPodium>(true) != null)
        {
            _editor.SetStatus("Weapon podiums cannot be cloned. Use the Podium tool instead.");
            return;
        }

        var clone = Object.Instantiate(source, source.transform.parent);
        clone.transform.position = source.transform.position;

        var structures = _editor.GetTool<StructureTool>();
        var adopted = structures != null && structures.TryAdoptClone(source, clone);

        Select(clone);
        _cloneDragging = true;
        _cloneGrabOffset = clone.transform.position - _editor.MouseWorld();

        _editor.SetStatus(adopted
            ? $"Cloned structure {source.name}. Release to drop."
            : $"Cloned {source.name}. Release to drop.");
    }

    // Returns true while a clone drag is in progress so the normal click handling stays out of
    // the way for that frame.
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

    // Keeps the outline and grip on the selection as it is dragged or nudged.
    private void SyncGizmos()
    {
        if (_selected == null) return;

        MapEditorGizmos.UpdateSelectionBox(_outline, _selected);

        if (_grip == null) return;
        var cam = SceneRefs.Cam;
        if (cam == null) return;

        _grip.GetComponent<RectTransform>().position =
            cam.WorldToScreenPoint(MapEditorGizmos.GripPosition(_selected));
    }

    private static string DescribeHit(Collider2D hit)
    {
        if (hit == null) return "<none>";
        var hasRenderer = hit.GetComponentInChildren<Renderer>(true) != null;
        return $"{hit.gameObject.name} (renderer={hasRenderer}, protected={MapEditorProtection.IsProtected(hit.gameObject)})";
    }

    private GameObject PickAtMouse()
    {
        var world = _editor.MouseWorld();

        // Only trust a physics hit if the thing is actually drawn. The room is littered with
        // invisible trigger and particle colliders that would otherwise swallow every click.
        var hit = Physics2D.OverlapPoint(world);
        if (hit != null && IsSelectable(hit.gameObject))
            return SelectionRoot(hit.gameObject);

        // Fall back to the smallest visible renderer whose bounds contain the point, so clicking
        // overlapping dressing picks the most specific object rather than a huge backdrop.
        GameObject best = null;
        var bestSize = float.MaxValue;

        foreach (var renderer in Object.FindObjectsOfType<Renderer>())
        {
            if (!IsVisibleRenderer(renderer)) continue;
            if (MapEditorProtection.IsProtected(renderer.gameObject)) continue;

            var bounds = renderer.bounds;
            if (world.x < bounds.min.x || world.x > bounds.max.x) continue;
            if (world.y < bounds.min.y || world.y > bounds.max.y) continue;

            var size = bounds.size.x * bounds.size.y;
            if (size < bestSize)
            {
                bestSize = size;
                best = renderer.gameObject;
            }
        }

        return best != null ? SelectionRoot(best) : null;
    }

    // Particle systems are invisible dressing for our purposes and were being picked constantly.
    private static bool IsVisibleRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled) return false;
        if (!renderer.gameObject.activeInHierarchy) return false;
        if (renderer is ParticleSystemRenderer) return false;
        if (IsPickIgnored(renderer.gameObject)) return false;
        return renderer is SpriteRenderer || renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    // Enemy HP bars are sprite objects spawned as SIBLINGS of their enemy, so they are not
    // caught by any enemy check and were being picked as ordinary scenery.
    private static bool IsPickIgnored(GameObject go)
    {
        return go.GetComponentInParent<HPBar>() != null;
    }

    private static bool IsSelectable(GameObject go)
    {
        if (go == null || MapEditorProtection.IsProtected(go)) return false;
        if (IsPickIgnored(go)) return false;

        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            if (IsVisibleRenderer(renderer)) return true;

        return false;
    }

    // Many props are assembled from several sprites under a shared parent, so selecting the exact
    // renderer that was hit grabs only one piece of the building. Walk up to the outermost parent
    // that is still map content, stopping before the room containers so we never select the world.
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

        var current = go.transform;
        var best = current;

        while (current.parent != null && !stops.Contains(current.parent))
        {
            current = current.parent;
            if (MapEditorProtection.IsProtected(current.gameObject)) break;
            best = current;
        }

        return best.gameObject;
    }

    private void Select(GameObject go)
    {
        ClearHighlight();
        _selected = go;

        if (_selected == null)
        {
            _editor.SetStatus("Nothing selected.");
            return;
        }

        ApplyHighlight(_selected);
        _editor.SetStatus("Selected: " + _selected.name);
    }

    // Tinting alone was too subtle to read against the busy biome art, so the selection also gets
    // a bright box drawn around the combined bounds of every renderer under it.
    private void ApplyHighlight(GameObject go)
    {
        foreach (var renderer in go.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null || renderer.material == null) continue;
            if (!renderer.material.HasProperty("_Color")) continue;

            _highlighted.Add(renderer);
            _originalColors.Add(renderer.material.color);
            renderer.material.color = Color.Lerp(renderer.material.color, Color.cyan, 0.45f);
        }

        DrawOutline(go);
    }

    private void DrawOutline(GameObject go)
    {
        _outline = MapEditorGizmos.CreateSelectionBox(go, "MapEditor_SelectionOutline");
        _grip = CreateGrip();
    }

    // Yellow grip at the selection's centre, matching the shape tool's move node.
    private GameObject CreateGrip()
    {
        var go = new GameObject("MapEditor_SelectionGrip");
        go.transform.SetParent(GripRoot(), false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(30f, 30f);

        var img = go.AddComponent<Image>();
        img.color = MapEditorGizmos.GripColour;

        var handle = go.AddComponent<SelectMoveHandle>();
        handle.Initialize(this, _editor);

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

    public bool HasSelection => _selected != null;

    public Vector3 SelectedPosition => _selected != null ? _selected.transform.position : Vector3.zero;

    // Z is preserved so dragging never reorders depth; that is what the Z buttons are for.
    public void SetSelectedPosition(Vector3 world)
    {
        if (_selected == null) return;
        var z = _selected.transform.position.z;
        _selected.transform.position = new Vector3(world.x, world.y, z);

        // Moving anything out of its culling area would otherwise see it deactivated when
        // culling resumes, exactly as happened with doors.
        _editor.KeepCullingSuspended = true;
    }

    private void NudgeZ(float delta)
    {
        if (_selected == null)
        {
            _editor.SetStatus("Nothing selected.");
            return;
        }

        var p = _selected.transform.position;
        _selected.transform.position = new Vector3(p.x, p.y, p.z + delta);
        _editor.SetStatus($"{_selected.name} Z: {_selected.transform.position.z:0.###}");
    }

    // Mirrors the selection horizontally by negating its X scale.
    //
    // Not a rotation: the view is 2.5D and the art is flat, so turning a prop about any axis
    // either tips it over (Z) or swings it edge-on and through its own sorting plane (Y). What
    // "face the other way" actually means here is a mirror, which is also how the game's own
    // build placement offers it - and it is already what MapStructureData.FlipX stores.
    //
    // Doors are refused: their walkable pad, barrier and lock visuals are all built from the
    // direction they face, so a mirrored door would keep its doorway pointing the old way.
    private void FlipHorizontal()
    {
        if (_selected == null)
        {
            _editor.SetStatus("Nothing selected.");
            return;
        }

        if (_selected.GetComponentInChildren<Door>(true) != null ||
            _selected.GetComponentInParent<Door>(true) != null)
        {
            _editor.SetStatus("Doors cannot be flipped.", StatusSeverity.Warning);
            return;
        }

        var scale = _selected.transform.localScale;
        _selected.transform.localScale = new Vector3(-scale.x, scale.y, scale.z);

        // Structures serialise their own mirror flag rather than reading it back off the
        // transform, so the tracked value has to follow or the flip would be lost on save.
        // Props need nothing: the room snapshot records their scale, sign included.
        _editor.GetTool<StructureTool>()?.TryFlip(_selected);

        _editor.KeepCullingSuspended = true;
        _editor.SetStatus("Flipped horizontally.");
    }

    private void ClearHighlight()
    {
        for (var i = 0; i < _highlighted.Count; i++)
        {
            if (_highlighted[i] == null || _highlighted[i].material == null) continue;
            _highlighted[i].material.color = _originalColors[i];
        }
        _highlighted.Clear();
        _originalColors.Clear();

        if (_outline != null) Object.Destroy(_outline);
        _outline = null;

        if (_grip != null) Object.Destroy(_grip);
        _grip = null;
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

        var path = HierarchyPath(_selected.transform);

        ClearHighlight();
        Object.Destroy(_selected);
        _selected = null;

        SceneRefs.RescanNavigation();
        _editor.SetStatus("Deleted " + path);
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

// Drags the whole selected object. Grab offset is captured on mouse-down so the object does not
// snap its centre to the cursor.
public class SelectMoveHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private SelectTool _tool;
    private RuntimeMapEditor _editor;
    private Vector3 _grabOffset;

    public void Initialize(SelectTool tool, RuntimeMapEditor editor)
    {
        _tool = tool;
        _editor = editor;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null || !_tool.HasSelection) return;
        _grabOffset = _tool.SelectedPosition - _editor.ScreenToWorld(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null) return;
        _tool.SetSelectedPosition(_editor.ScreenToWorld(eventData.position) + _grabOffset);
    }
}
