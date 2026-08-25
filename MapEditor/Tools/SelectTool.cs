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

    // Captured on resize mouse-down; each frame scales from this, never the previous frame.
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

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        BuildPreviewBox(panel, ui);

        _detailsGO = ui.CreateLabel(panel, "", 15);
        _details = _detailsGO.GetComponent<TMPro.TMP_Text>();
        _details.color = new Color(1f, 1f, 1f, 0.78f);

        // No Deselect button: right-click does it, from anywhere in the room rather than from one
        // spot in the panel. The button also had to appear and disappear with the selection, which
        // moved everything under it every time something was picked.

        // No depth buttons: the purple node on the gizmo drags Z directly, which is the same
        // gesture as everything else on the outline and shows the result as it happens. A pair of
        // buttons that stepped by a tenth meant looking away from the object to nudge it.

        // A checkbox, not a button: flipped is a state of the thing, and a button gave no way to
        // tell whether what you were looking at was flipped already.
        var flip = ui.CreateToggle(panel, "Flipped horizontally", false, SetFlipped);
        _flipToggle = flip.GetComponent<MapEditorToggle>();

        // No delete button: Del does it, and the button sat one slip away from the controls above.
        RefreshDetails();
    }

    private void BuildPreviewBox(RectTransform panel, MapEditorUI ui)
    {
        _previewGO = new GameObject("SelectionPreview");
        _previewGO.transform.SetParent(panel, false);

        // The row is as wide as the panel, because that is the only width a layout group will give
        // a child it controls. The plate inside it is not: it is a centred square, so the box the
        // portrait sits in has the portrait's own shape. A plate stretched the full width of the
        // panel around a square picture reads as a stretched picture, whatever the picture is doing.
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

        // Square and centred: the render texture is square, so anything else squashes the portrait.
        var imageRect = imageGO.AddComponent<RectTransform>();
        imageRect.anchorMin = imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.sizeDelta = new Vector2(PreviewBox - 16f, PreviewBox - 16f);

        // RawImage, not Image: the portrait is a RenderTexture the camera writes into, and there
        // is no sprite to make of it.
        _previewImage = imageGO.AddComponent<RawImage>();
        _previewImage.raycastTarget = false;
        _previewImage.enabled = false;

        // The box always says something; an empty plate reads as a thing that failed to load.
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

    // One camera render per change of selection, never per frame - see SelectionPreview.
    private void RefreshPreview()
    {
        if (_previewImage == null) return;

        // The selection tint comes off for the shot and goes straight back on.
        SetHighlightVisible(false);
        var drawn = _selected != null && SelectionPreview.Render(_selected);
        SetHighlightVisible(true);

        _previewImage.enabled = drawn;
        if (drawn) _previewImage.texture = SelectionPreview.Texture;

        if (_previewEmpty == null) return;

        // Something selected that has nothing to photograph is worth saying out loud - a trigger
        // volume or an empty parent looks identical to a broken preview otherwise.
        _previewEmpty.SetActive(!drawn);
        if (!drawn)
            _previewEmpty.GetComponent<TMPro.TMP_Text>().text =
                _selected == null ? "Nothing selected" : "Nothing to show for this one";
    }

    // What is selected, in the terms the gizmos move it by. Refreshed on selection and while a
    // drag is running, so the numbers are the ones on screen.
    // resize: the options panel only needs re-measuring when a row appears, disappears or changes
    // height. During a drag the readout keeps the same shape, so the throttled refresh skips it
    // rather than asking for a layout rebuild ten times a second.
    private void RefreshDetails(bool resize = true)
    {
        if (_details == null) return;

        // With nothing selected the box says so and the readout goes away rather than standing
        // there empty.
        var hasSelection = _selected != null;
        if (_detailsGO != null) _detailsGO.SetActive(hasSelection);

        if (!hasSelection)
        {
            _flipToggle?.SetValue(false, notify: false);
            if (resize) _editor.RequestOptionsResize();
            return;
        }

        var transform = _selected.transform;
        var position = transform.position;
        var scale = transform.localScale;

        var scripts = _selected.GetComponents<MonoBehaviour>().Length;
        var nested = _selected.GetComponentsInChildren<MonoBehaviour>(true).Length - scripts;
        var renderers = _selected.GetComponentsInChildren<Renderer>(true).Length;

        // No rotation line: the view is 2.5D and the art is flat, so nothing here is ever turned -
        // the tool refuses to rotate for the same reason, and a row that always reads 0 is noise.
        _details.text =
            $"{_selected.name}\n" +
            $"Position   {position.x:0.##}, {position.y:0.##}   (z {position.z:0.###})\n" +
            $"Scale   {Mathf.Abs(scale.x):0.###} x {scale.y:0.###}\n" +
            $"{scripts} script(s), {nested} in children, {renderers} renderer(s)" +
            (MapEditorProtection.IsProtected(_selected) ? "\nProtected - cannot be deleted." : "");

        MapEditorUI.FitLabelHeight(_detailsGO);
        _flipToggle?.SetValue(scale.x < 0f, notify: false);
        if (resize) _editor.RequestOptionsResize();
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

                if (picked == null)
                    _editor.SetStatus($"Nothing at {world.x:0.0}, {world.y:0.0}.");
                else
                    Select(picked);
            }
        }

        // Right-click clears the selection from anywhere in the room. Polled rather than taken from
        // the EventSystem: this game installs Rewired's pointer module, which drops the right
        // button, and the editor already works around that everywhere else it reads one. Skipped
        // over the editor's own panels - a right-click there is aimed at the panel, and dropping
        // the selection out from under the controls being used on it would be a surprise.
        if (Input.GetMouseButtonDown(1) && _selected != null && !_editor.PointerOverUi())
        {
            Select(null);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Delete))
            DeleteSelected();

        // The numbers follow a drag, throttled: it is one label, but it is a text mesh rebuild and
        // the gizmos move every frame. The portrait waits for the button to come up - one render
        // per gesture rather than one per frame.
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

    // Ctrl over one of the selection's own handles still clones, which is why this takes a world
    // point rather than reading the mouse. The handles sit *on* the thing being cloned - the grip
    // right in its middle - and they are UI blockers, so the polled Ctrl path in OnUpdate never sees
    // that click. Requiring the author to find a bare pixel of a selected object made Ctrl-drag
    // unusable on anything small, and unusable at all on whatever was under the grip.
    // From a handle there is nothing to work out: the gizmo belongs to the selection, so that is
    // what the gesture is copying. Guessing again from the pointer would be a worse answer than the
    // one already in hand - and the handles cover the object, so the pointer is over it by
    // definition.
    public bool BeginCloneDrag(Vector3 world) => BeginCloneOf(_selected, world);

    private void BeginClone()
    {
        var world = _editor.MouseWorld();

        // The selection wins whenever the pointer is over it. Re-picking from scratch meant a clone
        // that started on the very thing you had selected could still copy something else: picking
        // takes the smallest sprite under the cursor, and a room is full of grass, decals and
        // splashes lying over the object you actually want. Ctrl-drag on a selection is not an
        // invitation to re-choose.
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

        // A cloned podium carries post-Awake state and self-destroys on enable; use the podium tool.
        if (source.GetComponentInChildren<Interaction_WeaponSelectionPodium>(true) != null)
        {
            _editor.SetStatus("Weapon podiums cannot be cloned. Use the Podium tool instead.");
            return false;
        }

        // The tint comes off for the copy, exactly as it does for the portrait. The highlight is a
        // colour written onto the source's own renderers, so Instantiate copies it like any other
        // property - and then Select records the clone's tinted colours as its *originals* and
        // tints again on top. Cloning a clone compounded it, so each generation came out darker
        // than the last and never washed out on deselect, because the tint had become the object's
        // real colour. Anything cloned before this fix keeps its baked tint: re-clone from an
        // original to get a clean one.
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

        // Undoable like a placement, because that is what it is. The entry reports false once the
        // clone is gone by other means (deleted, cleared, loaded over) and the stack steps past it.
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

    // Unity names a copy "<name>(Clone)", and a copy of a copy "<name>(Clone)(Clone)" - a suffix
    // that grows without bound, is written into the saved blueprint, and tells you nothing except
    // how many times somebody pressed Ctrl. A number does the same job in one word and stays
    // readable at the twentieth copy.
    private static readonly Regex CloneSuffix = new(@"(\(Clone\))+$", RegexOptions.Compiled);
    private static readonly Regex TrailingNumber = new(@"\s+\d+$", RegexOptions.Compiled);

    // Counted among the copy's own siblings rather than the whole scene: names only have to tell
    // things apart where they sit together, and a room-wide sweep on every Ctrl-drag would cost
    // more than the answer is worth.
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

    // Both suffixes come off, so cloning "Torch 4" gives "Torch 5" rather than "Torch 4 2", and a
    // clone made before this existed still lands back on its real name.
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

    // True while a clone drag is in progress; the caller skips normal click handling then.
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

    // Picks what the pointer is over **on screen**, which stops being the same question as "what is
    // under this world point" the moment anything leaves the ground.
    //
    // The room's ground is the world XY plane and the camera is pitched 45 degrees over it, so a
    // change in Z lifts an object up the screen - Z here is height, not only sort order. Two things
    // stayed behind when that happened. A 2D collider has no Z at all, so it sits on the ground
    // while the art it belongs to rises (the game's own convention for a raised platform), and the
    // sweep below compared a z=0 world point against world XY bounds. Both meant a lifted object
    // could only be selected by clicking the empty floor where it used to be, while its outline
    // drew correctly around the art. The outline was right; this was wrong.
    //
    // `world` stays the parameter because every caller has the pointer as a ground point already,
    // and projecting it back to the screen returns the pixel it came from.
    public static GameObject PickWorldObject(Vector3 world)
    {
        var cam = SceneRefs.Cam;
        if (cam == null) return null;

        var screen = (Vector2)cam.WorldToScreenPoint(world);

        // Collider precision first, as before - but only when the thing it found is also under the
        // cursor on screen. For anything on the ground that is every time, so this is the same pick
        // it always was; for a lifted object it rejects the collider left behind on the floor.
        var hit = Physics2D.OverlapPoint(world);
        if (hit != null && IsSelectable(hit.gameObject) && DrawnUnder(hit.gameObject, screen, cam))
            return SelectionRoot(hit.gameObject);

        GameObject best = null;
        var bestSize = float.MaxValue;

        foreach (var renderer in Object.FindObjectsOfType<Renderer>())
        {
            if (!IsVisibleRenderer(renderer)) continue;
            if (MapEditorProtection.IsProtected(renderer.gameObject)) continue;
            if (!TryScreenRect(renderer.bounds, cam, out var rect)) continue;
            if (!rect.Contains(screen)) continue;

            // Smallest on screen wins, so a small prop in front of a large backdrop is reachable.
            var size = rect.width * rect.height;
            if (size < bestSize)
            {
                bestSize = size;
                best = renderer.gameObject;
            }
        }

        return best != null ? SelectionRoot(best) : null;
    }

    // Is any of this object's art actually beneath the pointer?
    private static bool DrawnUnder(GameObject go, Vector2 screen, Camera cam)
    {
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsVisibleRenderer(renderer)) continue;
            if (TryScreenRect(renderer.bounds, cam, out var rect) && rect.Contains(screen)) return true;
        }

        return false;
    }

    // The screen-space box of a world box: all eight corners projected, because a pitched camera
    // turns a world-axis-aligned box into a skewed shape and only the corners bound it.
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

            // Behind the camera: the projection mirrors through the origin and the rect it would
            // build covers half the screen.
            if (point.z <= 0f) return false;

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static bool IsVisibleRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled) return false;
        if (!renderer.gameObject.activeInHierarchy) return false;
        if (renderer is ParticleSystemRenderer) return false;
        if (IsPickIgnored(renderer.gameObject)) return false;
        return renderer is SpriteRenderer || renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    // Things this tool is not the one for. Checked on both picking paths, so neither the physics
    // hit nor the renderer sweep can reach them.
    private static bool IsPickIgnored(GameObject go)
    {
        // Enemy HP bars are spawned as siblings of their enemy, so no enemy check catches them.
        if (go.GetComponentInParent<HPBar>() != null) return true;

        // Triggers have a tool of their own that draws them, lists them and edits their actions.
        // Picking one here selected an invisible volume that could then be dragged, resized or
        // deleted out from under that tool.
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
            RefreshDetails();
            RefreshPreview();
            return;
        }

        ApplyHighlight(_selected);
        _editor.SetStatus("Selected: " + _selected.name);

        // Both read the object as it now stands. The portrait lifts the highlight tint for itself,
        // so the order these run in does not matter.
        RefreshDetails();
        RefreshPreview();
    }

    // Tint per renderer, never via renderer.material.color - that clones the material and the
    // clone survives deselection.
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

    // The highlight is a tint on the object *itself*, not an overlay - so a portrait taken while it
    // is on is a picture of a cyan object. Rather than reorder the calls (the tint is also on during
    // a drag and a flip, both of which re-render), the tint is lifted for the length of one render
    // and put straight back. Nothing else observes the gap: no frame is drawn inside it.
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

    // Same blue as the trigger tool's resize node.
    private static readonly Color ResizeColour = new(0.25f, 0.85f, 1f, 0.95f);

    // Purple: the third gesture on the outline needs a colour that is neither the yellow grip nor
    // the blue corner, and reads at 24px against the room's greens and browns.
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

    // One undo entry per *gesture*, not per frame. A drag rewrites the transform sixty times a
    // second; what the author did was move the thing once, so the position and scale are captured
    // when the drag starts and a single entry is pushed when it ends - and only if anything
    // actually changed, so a click that grazes a handle does not fill the stack with no-ops.
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

        _editor.History.Push($"{label} {target.name}", () =>
        {
            if (target == null) return false;

            target.transform.position = position;
            target.transform.localScale = scale;
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

    // Z preserved: dragging never reorders depth.
    public void SetSelectedPosition(Vector3 world)
    {
        if (_selected == null) return;
        var z = _selected.transform.position.z;
        _selected.transform.position = new Vector3(world.x, world.y, z);

        // Anything moved out of its culling area would be deactivated when culling resumes.
        _editor.KeepCullingSuspended = true;
        _editor.MarkEdited();
    }

    // ---- resizing -------------------------------------------------------------------------

    // False when the grab landed on the centre - no direction to scale along, ratio divides by nothing.
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

        // Grow about the visible centre, not the pivot - pivots often sit on a corner or floor line.
        var centre = MapEditorGizmos.GripPosition(_selected);
        var drift = _resizeStartCentre - centre;
        var position = _selected.transform.position;
        _selected.transform.position = new Vector3(position.x + drift.x, position.y + drift.y, position.z);

        _editor.KeepCullingSuspended = true;
        _editor.MarkEdited();
        _editor.SetStatus($"{_selected.name} scale {scale.x:0.##} x {scale.y:0.##}");
    }

    // A grab with almost no reach along this axis leaves it alone: the ratio there is noise.
    private static float AxisScale(float startScale, float grab, float startGrab)
    {
        if (Mathf.Abs(startGrab) < 0.05f) return startScale;
        return startScale * Mathf.Clamp(grab / startGrab, 0.02f, 50f);
    }

    // ---- depth ------------------------------------------------------------------------------

    // Screen pixels to Z. A tenth of a unit every ten pixels: the same step the old Send Back /
    // Bring Front buttons took, so a short drag is a nudge and a long one reorders the room.
    private const float DepthPerPixel = 0.01f;

    private float _depthStartZ;
    private float _depthStartScreenY;

    // Captured on mouse-down; every frame works from this, never the previous frame, so a drag
    // that wanders back over its own start lands exactly where it began.
    public bool BeginDepth(float screenY)
    {
        if (_selected == null) return false;

        _depthStartZ = _selected.transform.position.z;
        _depthStartScreenY = screenY;
        return true;
    }

    // Up is away. The old buttons named it: Z+ was "Send Back", so dragging the node up the screen
    // pushes the object behind its neighbours and down brings it in front of them.
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

    // Sets the flip rather than toggling it, so the checkbox and the object cannot drift apart:
    // ticking it always means flipped, whatever the object was.
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

        var path = HierarchyPath(_selected.transform);

        ClearHighlight();
        Object.Destroy(_selected);
        _selected = null;

        SceneRefs.RescanNavigation();
        _editor.MarkEdited();
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

// Drags the selection's move (centre) or resize (corner) node; offsets captured on mouse-down.
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

        // Ctrl means clone whatever the pointer is over, on any of the three handles: they cover the
        // object they belong to, so treating a handle as off-limits to Ctrl is what made Ctrl-drag
        // look broken. The tool takes it from here - the clone follows the mouse from its own
        // polled update - so this handle does nothing more for the rest of the gesture.
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            _cloning = _tool.BeginCloneDrag(world);
            if (_cloning) return;
        }

        // One entry for the whole drag, pushed on release if the object actually ended up
        // somewhere else.
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
                // A refused resize latches off for the whole drag, so the warning fires once rather
                // than every frame.
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

        // Screen space, not world: depth is the one axis the pointer cannot be projected onto, so
        // the gesture is "how far up the screen have you dragged" and nothing else.
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
