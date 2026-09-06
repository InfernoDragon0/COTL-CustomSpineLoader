using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

/// <summary>
/// One freehand line on the whiteboard. A tag plus a LineRenderer; never selectable, never part of
/// the room's own content, only a planning mark drawn in world space over the room.
/// </summary>
public class CTWhiteboardStroke : MonoBehaviour
{
    public string Id = "";
    public string Colour = "#FFFFFF";
    public float Width = 0.15f;
    public readonly List<Vector2> Points = [];

    internal LineRenderer Line;
}

/// <summary>
/// Draw over the world, in the world: planning marks that live in the room's blueprint (so they
/// save, load and travel to the other player like everything else) but that no tool can pick up.
/// A stroke is one drag; undo removes it. Erasing cuts points out of strokes, splitting them, and
/// one erase drag is one undo step too.
/// </summary>
public class WhiteboardTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts, Net.IMapEditorLivePreview
{
    public string Name => "Whiteboard";

    private const string RootName = "CultTweaker_Whiteboard";
    private const float StrokeZ = -1f;   // in front of everything on the floor
    private const int SortingOrder = 31900;
    private const int MaxPointsPerStroke = 4000;

    private enum Mode { None, Draw, Erase }

    // Preferences survive tool switches and editor sessions.
    private static bool _show = true;
    private static Mode _mode = Mode.Draw;
    private static float _drawSize = 0.15f;
    private static float _eraseSize = 0.6f;
    private static int _colourIndex;

    public static readonly Color[] Palette =
    [
        new(1f, 1f, 1f),                 // white
        new(0.13f, 0.13f, 0.13f),        // near black
        new(1f, 0.23f, 0.19f),           // red
        new(1f, 0.58f, 0f),              // orange
        new(1f, 0.84f, 0.04f),           // yellow
        new(0.2f, 0.78f, 0.35f),         // green
        new(0.2f, 0.68f, 0.9f),          // sky blue
        new(0.69f, 0.32f, 0.87f),        // purple
        new(1f, 0.18f, 0.58f)            // pink
    ];

    private readonly RuntimeMapEditor _editor;
    private Transform _root;

    private MapEditorToggle _showToggle;
    private MapEditorToggle _drawToggle;
    private MapEditorToggle _eraseToggle;
    private readonly List<Image> _swatchRings = [];

    // The gesture in progress.
    private CTWhiteboardStroke _current;
    private bool _drawing;
    private bool _erasing;
    private Vector2 _lastErase;
    private Dictionary<string, MapStrokeData> _eraseBefore;
    private List<string> _eraseCreated;

    private GameObject _ring;
    private LineRenderer _ringLine;

    public WhiteboardTool(RuntimeMapEditor editor) => _editor = editor;

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", _mode == Mode.Erase ? "Erase" : "Draw a stroke"),
        ("Ctrl+Z", "Undo the last stroke or erase")
    ];

    // ---- panel -----------------------------------------------------------------------------------

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _showToggle = ui.CreateToggle(panel, "Show whiteboard", _show, v =>
        {
            _show = v;
            ApplyVisibility();
        }).GetComponent<MapEditorToggle>();

        _drawToggle = ui.CreateToggle(panel, "Draw", _mode == Mode.Draw,
            v => SetMode(v ? Mode.Draw : Mode.None)).GetComponent<MapEditorToggle>();

        ui.CreateSlider(panel, "Draw size", 0.05f, 0.6f, _drawSize, v => _drawSize = v);

        BuildSwatches(panel);

        _eraseToggle = ui.CreateToggle(panel, "Erase", _mode == Mode.Erase,
            v => SetMode(v ? Mode.Erase : Mode.None)).GetComponent<MapEditorToggle>();

        ui.CreateSlider(panel, "Erase size", 0.2f, 3f, _eraseSize, v => _eraseSize = v);

        ui.CreateButton(panel, "Clear all", ClearAll, emphasis: MapEditorEmphasis.Quiet);
    }

    /// Removes every stroke as one undoable step; the peer sees the deletions with the next diff.
    private void ClearAll()
    {
        if (_drawing) FinishStroke();
        if (_erasing) FinishErase();

        var before = new List<MapStrokeData>();
        foreach (var stroke in AllStrokes())
            before.Add(Describe(stroke));

        if (before.Count == 0)
        {
            _editor.SetStatus("The whiteboard is already empty.", StatusSeverity.Info);
            return;
        }

        foreach (var record in before) RemoveStroke(record.Id);

        _editor.History.Push("whiteboard clear", () =>
        {
            var any = false;
            foreach (var record in before) any |= ApplyRecord(record) != null;
            return any;
        });
        _editor.MarkEdited();
        _editor.SetStatus("Cleared " + before.Count + " whiteboard stroke(s).", StatusSeverity.Info);
    }

    private void SetMode(Mode mode)
    {
        _mode = mode;

        // Draw and erase are one hand: picking up one puts the other down.
        if (mode != Mode.Draw) _drawToggle?.SetValue(false, notify: false);
        if (mode != Mode.Erase) _eraseToggle?.SetValue(false, notify: false);

        if (mode != Mode.None && !_show)
        {
            _show = true;
            _showToggle?.SetValue(true, notify: false);
            ApplyVisibility();
        }

        _editor.RefreshShortcutHints();
    }

    private void BuildSwatches(RectTransform panel)
    {
        _swatchRings.Clear();

        var row = new GameObject("Swatches");
        row.transform.SetParent(panel, false);
        row.AddComponent<RectTransform>().sizeDelta = new Vector2(360f, MapEditorUI.RowHeight);
        var layoutElement = row.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = MapEditorUI.RowHeight;
        layoutElement.minHeight = MapEditorUI.RowHeight;

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        for (var i = 0; i < Palette.Length; i++)
        {
            var index = i;

            // The selection ring is the outer plate and the colour sits inset on top of it: UI
            // children always draw over their parent, so a ring drawn as a child would cover the
            // colour entirely.
            var swatch = new GameObject("Swatch_" + i);
            swatch.transform.SetParent(row.transform, false);
            swatch.AddComponent<RectTransform>().sizeDelta = new Vector2(30f, 30f);
            var element = swatch.AddComponent<LayoutElement>();
            element.preferredWidth = 30f;
            element.preferredHeight = 30f;

            var ring = swatch.AddComponent<Image>();
            ring.sprite = MapEditorUI.RoundedPlate;
            ring.type = Image.Type.Sliced;
            ring.pixelsPerUnitMultiplier = 2.2f;
            ring.color = new Color(1f, 1f, 1f, 0.9f);
            ring.raycastTarget = false;
            _swatchRings.Add(ring);

            var colourRt = MapEditorUI.NewChild(swatch.transform, "Colour", stretch: true);
            colourRt.offsetMin = new Vector2(3f, 3f);
            colourRt.offsetMax = new Vector2(-3f, -3f);
            var image = colourRt.gameObject.AddComponent<Image>();
            image.sprite = MapEditorUI.RoundedPlate;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2.6f;
            image.color = Palette[i];

            MapEditorUI.AttachButton(swatch, image, () =>
            {
                _colourIndex = index;
                RefreshSwatchRings();
                if (_mode == Mode.None) SetMode(Mode.Draw);
                _drawToggle?.SetValue(true, notify: false);
            });
        }

        RefreshSwatchRings();
    }

    private void RefreshSwatchRings()
    {
        for (var i = 0; i < _swatchRings.Count; i++)
            if (_swatchRings[i] != null) _swatchRings[i].enabled = i == _colourIndex;
    }

    private static Color CurrentColour => Palette[Mathf.Clamp(_colourIndex, 0, Palette.Length - 1)];

    // ---- tool lifecycle --------------------------------------------------------------------------

    public void OnEnter()
    {
        Root(false);
        ApplyVisibility();
    }

    public void OnExit()
    {
        if (_drawing) FinishStroke();
        if (_erasing) FinishErase();
        if (_ring != null) _ring.SetActive(false);
    }

    /// The loader is about to rebuild the room; nothing we held survives.
    public void ResetTracking()
    {
        _current = null;
        _drawing = false;
        _erasing = false;
        _eraseBefore = null;
        _eraseCreated = null;
        _root = null;
    }

    public void OnUpdate()
    {
        ApplyVisibility();

        if (_editor.ModalOpen)
        {
            if (_ring != null) _ring.SetActive(false);
            return;
        }

        // Strokes live on the StrokeZ plane, so the mouse is projected onto that plane rather than
        // the floor: with the game's tilted camera the two planes land on different screen rows,
        // and a stroke projected onto the floor would draw a little above the cursor.
        var mouse = (Vector2)_editor.ScreenToWorldAtDepth(Input.mousePosition, StrokeZ);
        var overUi = _editor.PointerOverUi();
        UpdateRing(mouse, overUi);

        // A gesture that started in the world continues wherever the mouse goes until release.
        if (_drawing)
        {
            if (Input.GetMouseButton(0)) ExtendStroke(mouse);
            else FinishStroke();
            return;
        }

        if (_erasing)
        {
            if (Input.GetMouseButton(0)) EraseAlong(mouse);
            else FinishErase();
            return;
        }

        if (overUi || !Input.GetMouseButtonDown(0) || _mode == Mode.None) return;

        if (_mode == Mode.Draw) BeginStroke(mouse);
        else BeginErase(mouse);
    }

    // ---- visibility --------------------------------------------------------------------------------

    /// Strokes are a planning overlay: shown while the editor is open and the whiteboard is on.
    internal void RefreshVisibility() => ApplyVisibility();

    private void ApplyVisibility()
    {
        var root = Root(false);
        if (root == null) return;

        var visible = _show && _editor.IsEditing && !_editor.ChromeHidden;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
    }

    private Transform Root(bool create)
    {
        var content = SceneRefs.ContentRoot;
        if (content == null) return null;

        if (_root != null && _root.parent == content) return _root;

        var found = content.Find(RootName);
        if (found == null && create)
        {
            var go = new GameObject(RootName);
            go.transform.SetParent(content, false);
            found = go.transform;
        }

        _root = found;
        return _root;
    }

    private IEnumerable<CTWhiteboardStroke> AllStrokes()
    {
        var root = Root(false);
        if (root == null) yield break;

        foreach (var stroke in root.GetComponentsInChildren<CTWhiteboardStroke>(true))
            if (stroke != null) yield return stroke;
    }

    // ---- drawing -----------------------------------------------------------------------------------

    private float Spacing => Mathf.Max(0.03f, _drawSize * 0.3f);

    private void BeginStroke(Vector2 at)
    {
        var root = Root(true);
        if (root == null)
        {
            _editor.SetStatus("No room to draw on.", StatusSeverity.Error);
            return;
        }

        var go = new GameObject("Stroke");
        go.transform.SetParent(root, false);

        var stroke = go.AddComponent<CTWhiteboardStroke>();
        stroke.Id = Net.EditorIds.Of(go);
        stroke.Colour = "#" + ColorUtility.ToHtmlStringRGB(CurrentColour);
        stroke.Width = _drawSize;
        stroke.Points.Add(at);
        Rebuild(stroke);

        _current = stroke;
        _drawing = true;
    }

    private void ExtendStroke(Vector2 at)
    {
        if (_current == null)
        {
            _drawing = false;
            return;
        }

        var last = _current.Points[_current.Points.Count - 1];
        if ((at - last).sqrMagnitude < Spacing * Spacing) return;

        if (_current.Points.Count >= MaxPointsPerStroke)
        {
            // A very long scribble becomes several strokes; each is its own undo step.
            FinishStroke();
            BeginStroke(at);
            return;
        }

        _current.Points.Add(at);
        AppendPoint(_current, at);
    }

    private void FinishStroke()
    {
        _drawing = false;
        var stroke = _current;
        _current = null;
        if (stroke == null) return;

        // A click without a drag is a dot: two points a hair apart so the line has a length.
        if (stroke.Points.Count == 1)
        {
            stroke.Points.Add(stroke.Points[0] + new Vector2(stroke.Width * 0.25f, 0f));
            Rebuild(stroke);
        }

        var id = stroke.Id;
        _editor.History.Push("whiteboard stroke", () => RemoveStroke(id));
        _editor.MarkEdited();
    }

    // ---- erasing -----------------------------------------------------------------------------------

    private void BeginErase(Vector2 at)
    {
        _erasing = true;
        _lastErase = at;
        _eraseBefore = new Dictionary<string, MapStrokeData>();
        _eraseCreated = [];
        EraseSegment(at, at);
    }

    private void EraseAlong(Vector2 to)
    {
        EraseSegment(_lastErase, to);
        _lastErase = to;
    }

    /// Points within the eraser's reach of the path the mouse just took are cut out. What remains of a
    /// stroke keeps its id on the first surviving run; further runs become strokes of their own.
    private void EraseSegment(Vector2 from, Vector2 to)
    {
        var radius = _eraseSize * 0.5f;
        var strokes = new List<CTWhiteboardStroke>(AllStrokes());

        foreach (var stroke in strokes)
        {
            if (stroke == null || stroke.Points.Count == 0) continue;

            var keep = new bool[stroke.Points.Count];
            var removed = 0;
            for (var i = 0; i < stroke.Points.Count; i++)
            {
                var near = DistanceToSegment(stroke.Points[i], from, to) <= radius + stroke.Width * 0.5f;
                keep[i] = !near;
                if (near) removed++;
            }
            if (removed == 0) continue;

            if (!_eraseBefore.ContainsKey(stroke.Id) && !_eraseCreated.Contains(stroke.Id))
                _eraseBefore[stroke.Id] = Describe(stroke);

            var runs = new List<List<Vector2>>();
            List<Vector2> run = null;
            for (var i = 0; i < keep.Length; i++)
            {
                if (!keep[i])
                {
                    run = null;
                    continue;
                }
                if (run == null) runs.Add(run = []);
                run.Add(stroke.Points[i]);
            }
            runs.RemoveAll(r => r.Count < 2);

            if (runs.Count == 0)
            {
                RemoveStroke(stroke.Id);
                continue;
            }

            stroke.Points.Clear();
            stroke.Points.AddRange(runs[0]);
            Rebuild(stroke);

            for (var r = 1; r < runs.Count; r++)
            {
                var piece = ApplyRecord(new MapStrokeData
                {
                    Id = "",
                    Colour = stroke.Colour,
                    Width = stroke.Width,
                    Points = Flatten(runs[r])
                });
                if (piece != null) _eraseCreated.Add(piece.Id);
            }
        }
    }

    private void FinishErase()
    {
        _erasing = false;
        var before = _eraseBefore;
        var created = _eraseCreated;
        _eraseBefore = null;
        _eraseCreated = null;

        if (before == null || before.Count == 0) return;

        _editor.History.Push("whiteboard erase", () =>
        {
            var any = false;
            if (created != null)
                foreach (var id in created) any |= RemoveStroke(id);
            foreach (var record in before.Values)
                any |= ApplyRecord(record) != null;
            return any;
        });
        _editor.MarkEdited();
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var length = ab.sqrMagnitude;
        if (length < 1e-8f) return Vector2.Distance(p, a);
        var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / length);
        return Vector2.Distance(p, a + ab * t);
    }

    // ---- records -----------------------------------------------------------------------------------

    internal CTWhiteboardStroke FindStroke(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var go = Net.EditorIds.Find(id);
        var stroke = go != null ? go.GetComponent<CTWhiteboardStroke>() : null;
        if (stroke != null) return stroke;

        foreach (var candidate in AllStrokes())
            if (candidate.Id == id) return candidate;
        return null;
    }

    /// Creates or updates a stroke from its record: the loader, a peer's change, and undo all use it.
    internal CTWhiteboardStroke ApplyRecord(MapStrokeData record)
    {
        if (record == null) return null;

        var stroke = string.IsNullOrEmpty(record.Id) ? null : FindStroke(record.Id);
        if (stroke == null)
        {
            var root = Root(true);
            if (root == null) return null;

            var go = new GameObject("Stroke");
            go.transform.SetParent(root, false);
            stroke = go.AddComponent<CTWhiteboardStroke>();

            if (string.IsNullOrEmpty(record.Id)) stroke.Id = Net.EditorIds.Of(go);
            else
            {
                Net.EditorIds.Adopt(go, record.Id);
                stroke.Id = record.Id;
            }
        }

        stroke.Colour = string.IsNullOrEmpty(record.Colour) ? "#FFFFFF" : record.Colour;
        stroke.Width = record.Width > 0f ? record.Width : 0.15f;
        stroke.Points.Clear();
        var flat = record.Points ?? [];
        for (var i = 0; i + 1 < flat.Count; i += 2)
            stroke.Points.Add(new Vector2(flat[i], flat[i + 1]));

        Rebuild(stroke);
        ApplyVisibility();
        return stroke;
    }

    internal bool RemoveStroke(string id)
    {
        var stroke = FindStroke(id);
        if (stroke == null) return false;

        if (_current == stroke)
        {
            _current = null;
            _drawing = false;
        }

        Net.EditorIds.Forget(stroke.gameObject);
        stroke.gameObject.SetActive(false);
        Object.Destroy(stroke.gameObject);
        return true;
    }

    internal static MapStrokeData Describe(CTWhiteboardStroke stroke) => new()
    {
        Id = stroke.Id,
        Colour = stroke.Colour,
        Width = stroke.Width,
        Points = Flatten(stroke.Points)
    };

    private static List<float> Flatten(List<Vector2> points)
    {
        var flat = new List<float>(points.Count * 2);
        foreach (var p in points)
        {
            flat.Add(p.x);
            flat.Add(p.y);
        }
        return flat;
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.Whiteboard.Clear();
        foreach (var stroke in AllStrokes())
            if (stroke.Points.Count >= 2) map.Whiteboard.Add(Describe(stroke));
    }

    // ---- live preview ------------------------------------------------------------------------------

    public bool LiveActive => _drawing || _erasing;

    public IEnumerable<GameObject> LiveObjects
    {
        get
        {
            if (_drawing && _current != null) yield return _current.gameObject;

            if (!_erasing) yield break;
            if (_eraseBefore != null)
                foreach (var id in _eraseBefore.Keys)
                {
                    var stroke = FindStroke(id);
                    if (stroke != null) yield return stroke.gameObject;
                }
            if (_eraseCreated != null)
                foreach (var id in _eraseCreated)
                {
                    var stroke = FindStroke(id);
                    if (stroke != null) yield return stroke.gameObject;
                }
        }
    }

    // ---- rendering ---------------------------------------------------------------------------------

    private static void Rebuild(CTWhiteboardStroke stroke)
    {
        var line = EnsureLine(stroke);
        line.positionCount = stroke.Points.Count;
        for (var i = 0; i < stroke.Points.Count; i++)
            line.SetPosition(i, new Vector3(stroke.Points[i].x, stroke.Points[i].y, StrokeZ));
    }

    private static void AppendPoint(CTWhiteboardStroke stroke, Vector2 point)
    {
        var line = EnsureLine(stroke);
        line.positionCount = stroke.Points.Count;
        line.SetPosition(stroke.Points.Count - 1, new Vector3(point.x, point.y, StrokeZ));
    }

    private static LineRenderer EnsureLine(CTWhiteboardStroke stroke)
    {
        var line = stroke.Line;
        if (line == null)
        {
            line = stroke.GetComponent<LineRenderer>() ?? stroke.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.numCornerVertices = 5;
            line.numCapVertices = 5;
            line.textureMode = LineTextureMode.Stretch;
            line.sharedMaterial = MapEditorGizmos.LineMaterial();
            line.sortingOrder = SortingOrder;
            line.receiveShadows = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stroke.Line = line;
        }

        line.startWidth = line.endWidth = stroke.Width;
        var colour = ColorUtility.TryParseHtmlString(stroke.Colour, out var parsed) ? parsed : Color.white;
        line.startColor = line.endColor = colour;
        return line;
    }

    private void UpdateRing(Vector2 mouse, bool overUi)
    {
        var show = _mode != Mode.None && !overUi && _show;
        if (!show)
        {
            if (_ring != null && _ring.activeSelf) _ring.SetActive(false);
            return;
        }

        if (_ring == null) BuildRing();
        if (!_ring.activeSelf) _ring.SetActive(true);

        var radius = _mode == Mode.Erase ? _eraseSize * 0.5f : Mathf.Max(0.04f, _drawSize * 0.5f);
        var colour = _mode == Mode.Erase ? new Color(1f, 0.35f, 0.3f, 0.9f) : CurrentColour;
        _ringLine.startColor = _ringLine.endColor = colour;

        const int segments = 32;
        for (var i = 0; i < segments; i++)
        {
            var angle = i / (float)segments * Mathf.PI * 2f;
            _ringLine.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
        _ring.transform.position = new Vector3(mouse.x, mouse.y, StrokeZ);
    }

    private void BuildRing()
    {
        _ring = new GameObject("MapEditor_WhiteboardRing");
        _ring.transform.SetParent(_editor.transform, false);

        _ringLine = _ring.AddComponent<LineRenderer>();
        _ringLine.useWorldSpace = false;
        _ringLine.loop = true;
        _ringLine.positionCount = 32;
        _ringLine.startWidth = _ringLine.endWidth = 0.03f;
        _ringLine.sharedMaterial = MapEditorGizmos.LineMaterial();
        _ringLine.sortingOrder = SortingOrder + 1;
    }
}
