using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CustomSpineLoader.MapEditor.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

/// <summary>
/// The room's contents as a tree down the left of the editor, the way VS Code lists a folder: editor
/// groups as folders first, then one collapsible group per kind of thing (structures, props, shapes,
/// enemies, NPCs, podiums), and the triggers in a group of their own. It is there whatever tool is
/// active, except the Level and Dungeon tools, which own the screen. Clicking an item hands it to the
/// Select tool; shift-clicking a second item selects the run between them; clicking a folder selects
/// the whole group; clicking a trigger hands it to the Trigger tool. It shares the left edge with the
/// shortcut hints, so opening one folds the other.
///
/// The list is virtualised: the tree is a flat list of row MODELS, and only the rows inside the
/// viewport (plus a margin) have a GameObject, drawn from a small pool and re-bound as the list
/// scrolls. A base town lists 600+ things; 600 live TMP labels made every hover and scroll tick
/// re-mesh the canvas.
/// </summary>
public class MapEditorLayerPanel
{
    private const float Width = 300f;
    private const float HeaderHeight = 34f;
    private const float Top = 80f;
    private const float BottomReserve = 70f;
    private const float RowHeight = 28f;
    private const float RowSpacing = 2f;
    private const float Pitch = RowHeight + RowSpacing;
    private const float PadTop = 8f;
    private const float PadBottom = 8f;
    private const float PadSide = 10f;
    private const float Indent = 18f;
    private const int ViewMargin = 3;

    // Edits (place, delete, undo, group) refresh the tree through EditCount once they have been quiet
    // for EditSettle - a drag marks an edit every frame. RefreshEvery is the fallback sweep for
    // anything that changes the room without going through the editor's history; it only scans when
    // QuickStamp says the room changed.
    private const float RefreshEvery = 10f;
    private const float EditSettle = 0.4f;

    // The room scan is sliced across frames, this many candidates per Tick.
    private const int ScanPerFrame = 60;

    private static readonly Color RowIdle = new(1f, 1f, 1f, 0.03f);
    private static readonly Color RowHover = new(1f, 1f, 1f, 0.12f);
    private static readonly Color RowLit = new(0.83f, 0.24f, 0.20f, 0.55f);
    private static readonly Color GroupIdle = new(1f, 1f, 1f, 0.07f);
    private static readonly Color FolderIdle = new(0.95f, 0.75f, 0.35f, 0.14f);

    private static readonly string[] KindOrder = ["Structures", "Props", "Shapes", "Enemies", "NPCs", "Podiums"];
    private const string TriggerGroup = "Triggers";
    private const string GroupsSection = "Groups";

    private readonly RuntimeMapEditor _editor;
    private readonly MapEditorUI _ui;
    private readonly RectTransform _panel;
    private readonly GameObject _content;
    private readonly RectTransform _column;
    private readonly ScrollRect _scroll;
    private readonly TMP_Text _collapseLabel;

    private bool _collapsed = true;
    private bool _hiddenForTool;
    private float _nextRefreshAt;
    private float _refreshDueAt;
    private int _seenEdits = -1;
    private string _signature;

    private readonly HashSet<string> _closedGroups = [];
    private GameObject _anchor;

    // ---- what the tree knows -----------------------------------------------------------------------

    private enum RowKind { Empty, Section, Folder, Item, Trigger }

    private sealed class RowModel
    {
        public RowKind Kind;
        public string Text;
        public string Key;
        public bool Open;
        public float Indent;
        public Color Idle;
        public GameObject Go;
        public CTMapTrigger Trigger;
        public List<GameObject> Members;
    }

    private sealed class Kind
    {
        public string Name;
        public readonly List<(GameObject Go, string Label)> Items = [];
    }

    private sealed class Folder
    {
        public string Id;
        public string Name;
        public readonly List<(GameObject Go, string Label)> Items = [];
    }

    private readonly List<RowModel> _rows = [];
    private readonly List<GameObject> _itemOrder = [];
    private readonly Dictionary<GameObject, int> _indexOfObject = [];
    private readonly Dictionary<CTMapTrigger, int> _indexOfTrigger = [];
    private readonly List<int> _sectionRows = [];

    // The last scan's result, kept so folding a section rebuilds the model without another scan.
    private List<Folder> _lastFolders = [];
    private List<Kind> _lastKinds = [];
    private List<CTMapTrigger> _lastTriggers = [];

    // ---- what is on screen -------------------------------------------------------------------------

    private sealed class RowView
    {
        public GameObject Root;
        public RectTransform Rect;
        public Image Plate;
        public MapEditorHover Hover;
        public TMP_Text Label;
        public RectTransform LabelRect;
        public GameObject Caret;
        public TMP_Text CaretLabel;
        public int Index = -1;
    }

    private readonly List<RowView> _pool = [];
    private readonly Dictionary<int, RowView> _shown = [];
    private int _shownFirst = -1;
    private int _shownLast = -2;

    private readonly HashSet<GameObject> _lit = [];
    private CTMapTrigger _litTrigger;
    private GameObject _lastPrimary;

    private GameObject _scrollToObject;
    private CTMapTrigger _scrollToTrigger;
    private string _scrollToHeader;
    private (GameObject Go, CTMapTrigger Trigger)? _revealAfterScan;

    private RectTransform _sticky;
    private TMP_Text _stickyLabel;
    private string _stickyKey;

    // ---- the scan ----------------------------------------------------------------------------------

    private sealed class Scan
    {
        // (candidate, whether it is already a region's child - those are things, never regions again)
        public readonly List<(Transform T, bool InRegion)> Candidates = [];
        public int Index;
        public readonly Dictionary<string, Kind> ByKind = [];
        public readonly Dictionary<string, Folder> ByGroup = [];
        public readonly HashSet<GameObject> Seen = [];
        public HashSet<Transform> Stops;
        public bool InBase;
    }

    private Scan _scan;
    private int _lastStamp = int.MinValue;
    private bool _forceScan;

    public MapEditorLayerPanel(RuntimeMapEditor editor, MapEditorUI ui, Transform canvas)
    {
        _editor = editor;
        _ui = ui;

        var go = new GameObject("Layers");
        go.transform.SetParent(canvas, false);
        _panel = go.AddComponent<RectTransform>();
        _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 1f);
        _panel.pivot = new Vector2(0f, 1f);
        _panel.sizeDelta = new Vector2(Width, HeaderHeight);
        _panel.anchoredPosition = new Vector2(16f, -Top);
        VanillaChrome.Dress(go.AddComponent<Image>());
        editor.RegisterUiBlocker(_panel);

        var header = new GameObject("Header");
        header.transform.SetParent(_panel, false);
        var headerRt = header.AddComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, HeaderHeight);
        headerRt.anchoredPosition = Vector2.zero;
        var headerPlate = header.AddComponent<Image>();
        headerPlate.color = new Color(1f, 1f, 1f, 0.08f);

        var title = ui.CreateLabel(header.transform, "Layers", 21);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = Vector2.zero;
        titleRt.anchorMax = Vector2.one;
        titleRt.offsetMin = new Vector2(12f, 0f);
        titleRt.offsetMax = new Vector2(-36f, 0f);
        var titleText = title.GetComponent<TMP_Text>();
        titleText.enableWordWrapping = false;
        titleText.raycastTarget = false;

        // The whole header toggles, not just the small button: it is the one row that is always there.
        MapEditorUI.AttachButton(header, headerPlate, () => SetCollapsed(!_collapsed));
        MapEditorUI.AddHover(header, headerPlate, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.16f), null);

        var collapse = ui.CreateButton(header.transform, "+", () => SetCollapsed(!_collapsed), 26f);
        var collapseRt = collapse.GetComponent<RectTransform>();
        collapseRt.anchorMin = collapseRt.anchorMax = new Vector2(1f, 0.5f);
        collapseRt.pivot = new Vector2(1f, 0.5f);
        collapseRt.sizeDelta = new Vector2(26f, 26f);
        collapseRt.anchoredPosition = new Vector2(-4f, 0f);
        _collapseLabel = collapse.GetComponentInChildren<TMP_Text>();

        _content = new GameObject("Content");
        _content.transform.SetParent(_panel, false);
        var contentRt = _content.AddComponent<RectTransform>();
        contentRt.anchorMin = Vector2.zero;
        contentRt.anchorMax = Vector2.one;
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = new Vector2(0f, -HeaderHeight);

        _column = ui.CreateScrollColumn(contentRt, "LayerTree", out var scrollRoot, spacing: RowSpacing);
        _column.gameObject.AddComponent<MapEditorQuietArea>();
        _scroll = scrollRoot.GetComponent<ScrollRect>();

        // The column came with a layout group and a size fitter for a real list of children. This
        // list places its few live rows by hand at their index, so the content is sized by hand too.
        UnityEngine.Object.DestroyImmediate(_column.GetComponent<ContentSizeFitter>());
        UnityEngine.Object.DestroyImmediate(_column.GetComponent<VerticalLayoutGroup>());
        SetContentHeight(0);

        BuildSticky(contentRt);

        _content.SetActive(false);
    }

    private void BuildSticky(RectTransform contentRt)
    {
        var go = new GameObject("StickyHeader");
        go.transform.SetParent(contentRt, false);
        _sticky = go.AddComponent<RectTransform>();
        _sticky.anchorMin = new Vector2(0f, 1f);
        _sticky.anchorMax = new Vector2(1f, 1f);
        _sticky.pivot = new Vector2(0.5f, 1f);
        _sticky.offsetMin = new Vector2(PadSide, -RowHeight);
        _sticky.offsetMax = new Vector2(-(PadSide + MapEditorUI.ScrollbarWidth + 2f), 0f);

        // Opaque backing, so the rows sliding underneath do not show through the pinned header.
        var backing = go.AddComponent<Image>();
        backing.sprite = MapEditorUI.RoundedPlate;
        backing.type = Image.Type.Sliced;
        backing.pixelsPerUnitMultiplier = 2.2f;
        backing.color = new Color(0.09f, 0.08f, 0.07f, 0.98f);

        var plateGO = new GameObject("Plate");
        plateGO.transform.SetParent(go.transform, false);
        var plateRt = plateGO.AddComponent<RectTransform>();
        plateRt.anchorMin = Vector2.zero;
        plateRt.anchorMax = Vector2.one;
        plateRt.offsetMin = Vector2.zero;
        plateRt.offsetMax = Vector2.zero;
        var plate = plateGO.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 2.2f;
        plate.color = GroupIdle;

        var label = _ui.CreateLabel(plateGO.transform, "", 16);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(8f, 0f);
        labelRt.offsetMax = new Vector2(-8f, 0f);
        _stickyLabel = label.GetComponent<TMP_Text>();
        _stickyLabel.enableWordWrapping = false;
        _stickyLabel.overflowMode = TextOverflowModes.Ellipsis;
        _stickyLabel.raycastTarget = false;

        MapEditorUI.AttachButton(plateGO, plate, () =>
        {
            if (_stickyKey == null) return;
            var key = _stickyKey;
            ToggleGroup(key);
            _scrollToHeader = key;
        });
        MapEditorUI.AddHover(plateGO, plate, GroupIdle, RowHover, null);

        go.SetActive(false);
    }

    // ---- state ---------------------------------------------------------------------------------

    public bool Collapsed => _collapsed;

    public void SetCollapsed(bool collapsed)
    {
        if (_collapsed == collapsed) return;
        _collapsed = collapsed;

        _content.SetActive(!collapsed);
        if (_collapseLabel != null) _collapseLabel.text = collapsed ? "+" : "-";

        if (!collapsed)
        {
            _editor.CollapseShortcuts();
            Invalidate();
        }
        else
        {
            _panel.sizeDelta = new Vector2(Width, HeaderHeight);
        }
    }

    /// The Level and Dungeon tools take the whole screen, so the tree steps aside for them.
    public void OnToolChanged(IMapEditorTool tool)
    {
        _hiddenForTool = tool is LevelTool or DungeonBuilderTool;
        _panel.gameObject.SetActive(!_hiddenForTool);
        if (!_hiddenForTool) Invalidate();
    }

    public void Tick()
    {
        if (_hiddenForTool || _collapsed) return;

        var now = Time.unscaledTime;

        if (_editor.EditCount != _seenEdits)
        {
            _seenEdits = _editor.EditCount;
            _refreshDueAt = now + EditSettle;
        }

        if (_scan == null)
        {
            if (_forceScan || (_refreshDueAt > 0f && now >= _refreshDueAt))
            {
                _forceScan = false;
                _refreshDueAt = 0f;
                _nextRefreshAt = now + RefreshEvery;
                BeginScan();
            }
            else if (now >= _nextRefreshAt)
            {
                _nextRefreshAt = now + RefreshEvery;
                if (QuickStamp() != _lastStamp) BeginScan();
            }
        }

        PumpScan();
        SyncSelection();
    }

    public void LateUpdate()
    {
        if (_panel == null || !_panel.gameObject.activeSelf || _collapsed) return;

        var canvasHeight = _panel.parent is RectTransform canvas ? canvas.rect.height : 1080f;
        var max = Mathf.Max(HeaderHeight + 60f, canvasHeight - Top - BottomReserve);
        var target = Mathf.Min(_column.rect.height + HeaderHeight + 12f, max);
        if (Mathf.Abs(_panel.sizeDelta.y - target) > 1f) _panel.sizeDelta = new Vector2(Width, target);

        if (_scrollToObject != null || _scrollToTrigger != null || _scrollToHeader != null) ScrollToPending();

        ShowVisibleRows();
        UpdateSticky();
    }

    /// Asks for a fresh read of the room at the next Tick, whatever the stamp says.
    internal void Invalidate()
    {
        _signature = null;
        _forceScan = true;
    }

    /// <summary>
    /// A number that moves whenever something is added to or removed from the room: the child counts
    /// under the selection stops plus the trigger count. Microseconds, against the tens of
    /// milliseconds a real scan of a base town costs - so idle frames never pay for the real one.
    /// </summary>
    private int QuickStamp()
    {
        var stamp = 17;
        foreach (var stop in SelectTool.SelectionStops())
            if (stop != null) stamp = unchecked(stamp * 31 + stop.childCount);

        var triggers = _editor.GetTool<TriggerTool>();
        return unchecked(stamp * 31 + (triggers != null ? triggers.Triggers.Count : 0));
    }

    // ---- reading the room ----------------------------------------------------------------------

    private void BeginScan()
    {
        _lastStamp = QuickStamp();

        var scan = new Scan
        {
            Stops = SelectTool.SelectionStops(),
            InBase = RuntimeMapEditor.Context == EditorContext.Base
        };

        foreach (var stop in scan.Stops)
        {
            if (stop == null) continue;
            for (var i = 0; i < stop.childCount; i++)
            {
                var child = stop.GetChild(i);
                if (!scan.Stops.Contains(child)) scan.Candidates.Add((child, false));
            }
        }

        _scan = scan;
    }

    /// <summary>
    /// The sliced half of what SelectTool.AllSelectableRoots does in one go: IsSelectable, SelectionRoot
    /// and Classify for ScanPerFrame candidates per frame. In the base a placement region is not a
    /// thing but a container, so its children are appended to the queue instead - flagged, so they
    /// are never treated as regions themselves. When the queue is done, the result is compared with
    /// what is showing and only a real change rebuilds.
    /// </summary>
    private void PumpScan()
    {
        var scan = _scan;
        if (scan == null) return;

        var budget = ScanPerFrame;
        try
        {
            while (budget-- > 0 && scan.Index < scan.Candidates.Count)
            {
                var (candidate, inRegion) = scan.Candidates[scan.Index++];
                if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;

                // One level only, as in SelectionRoot: a region's children are the things; checking
                // them as regions again would walk into every large object's parts and never end.
                if (scan.InBase && !inRegion && SelectTool.IsRegion(candidate))
                {
                    for (var j = 0; j < candidate.childCount; j++)
                        scan.Candidates.Add((candidate.GetChild(j), true));
                    continue;
                }

                if (!SelectTool.IsSelectable(candidate.gameObject)) continue;

                var root = SelectTool.SelectionRoot(candidate.gameObject);
                if (root == null || !scan.Seen.Add(root)) continue;

                var kindName = Classify(root, out var label);
                if (kindName == null) continue;

                // A grouped object is listed under its folder, not under its kind: one row per thing.
                var groupId = MapEditorGroups.GroupOf(root);
                if (groupId != null)
                {
                    if (!scan.ByGroup.TryGetValue(groupId, out var folder))
                        scan.ByGroup[groupId] = folder = new Folder
                        {
                            Id = groupId,
                            Name = MapEditorGroups.NameOf(groupId) ?? "Group"
                        };
                    folder.Items.Add((root, label));
                    continue;
                }

                if (!scan.ByKind.TryGetValue(kindName, out var kind))
                    scan.ByKind[kindName] = kind = new Kind { Name = kindName };
                kind.Items.Add((root, label));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: the layer tree could not read the room: " + e.Message);
            _scan = null;
            return;
        }

        if (scan.Index < scan.Candidates.Count) return;
        _scan = null;

        FinishScan(scan);
    }

    private void FinishScan(Scan scan)
    {
        // A group with a single member left is not a group any more; show the object where it belongs.
        foreach (var lone in scan.ByGroup.Values.Where(f => f.Items.Count < 2).ToList())
        {
            scan.ByGroup.Remove(lone.Id);
            foreach (var (go, label) in lone.Items)
            {
                var kindName = Classify(go, out _) ?? "Props";
                if (!scan.ByKind.TryGetValue(kindName, out var kind))
                    scan.ByKind[kindName] = kind = new Kind { Name = kindName };
                kind.Items.Add((go, label));
            }
        }

        var triggerTool = _editor.GetTool<TriggerTool>();
        var triggers = triggerTool != null
            ? triggerTool.Triggers.Where(t => t != null && t.gameObject != null).ToList()
            : [];

        var folders = scan.ByGroup.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var folder in folders) folder.Items.Sort(ByLabel);

        var kinds = new List<Kind>();
        foreach (var name in KindOrder)
            if (scan.ByKind.TryGetValue(name, out var kind) && kind.Items.Count > 0) kinds.Add(kind);
        foreach (var kind in kinds) kind.Items.Sort(ByLabel);

        _lastFolders = folders;
        _lastKinds = kinds;
        _lastTriggers = triggers;

        var signature = SignatureOf(folders, kinds, triggers);
        if (signature != _signature)
        {
            _signature = signature;
            RebuildModel();
        }

        if (_revealAfterScan != null)
        {
            _scrollToObject = _revealAfterScan.Value.Go;
            _scrollToTrigger = _revealAfterScan.Value.Trigger;
            _revealAfterScan = null;
        }
    }

    private static int ByLabel((GameObject Go, string Label) a, (GameObject Go, string Label) b)
    {
        var byLabel = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        return byLabel != 0 ? byLabel : a.Go.GetInstanceID().CompareTo(b.Go.GetInstanceID());
    }

    private string Classify(GameObject root, out string label)
    {
        label = MapEditorGroups.DisplayName(root);

        if (root.GetComponentInChildren<CTMapTrigger>(true) != null) return null;

        if (root.GetComponent<CTEditorShape>() != null || root.GetComponentInChildren<SpriteShapeController>(true) != null)
            return "Shapes";

        if (_editor.GetTool<PodiumTool>()?.IsTracked(root) == true || root.GetComponentInChildren<CTPodiumBehavior>(true) != null)
            return "Podiums";

        if (_editor.GetTool<EnemyTool>()?.IsTracked(root) == true || root.GetComponentInChildren<UnitObject>(true) != null)
            return "Enemies";

        if (_editor.GetTool<NpcTool>()?.IsTracked(root) == true)
            return "NPCs";

        if (_editor.GetTool<StructureTool>()?.TryGetPlacedName(root, out var placed) == true)
        {
            label = placed;
            return "Structures";
        }

        var structure = root.GetComponentInChildren<Structure>(true);
        if (structure != null)
        {
            var type = structure.Brain?.Data != null ? structure.Brain.Data.Type : structure.Type;
            if (type != StructureBrain.TYPES.NONE) label = type.ToString();
            return "Structures";
        }

        return "Props";
    }

    private static string SignatureOf(List<Folder> folders, List<Kind> kinds, List<CTMapTrigger> triggers)
    {
        var text = new StringBuilder();

        foreach (var folder in folders)
        {
            text.Append('@').Append(folder.Id).Append('=').Append(folder.Name);
            foreach (var (go, label) in folder.Items)
                text.Append('|').Append(go.GetInstanceID()).Append(':').Append(label);
        }

        foreach (var kind in kinds)
        {
            text.Append('#').Append(kind.Name);
            foreach (var (go, label) in kind.Items)
                text.Append('|').Append(go.GetInstanceID()).Append(':').Append(label);
        }

        text.Append("#T");
        foreach (var trigger in triggers)
            text.Append('|').Append(trigger.GetInstanceID()).Append(':').Append(trigger.Id);

        return text.ToString();
    }

    // ---- the model -----------------------------------------------------------------------------

    /// <summary>
    /// Turns the last scan into the flat list of rows the tree shows, honouring which sections are
    /// folded. No GameObjects are touched here; ShowVisibleRows draws whichever of these are in view.
    /// </summary>
    private void RebuildModel()
    {
        _rows.Clear();
        _itemOrder.Clear();
        _indexOfObject.Clear();
        _indexOfTrigger.Clear();
        _sectionRows.Clear();

        var folders = _lastFolders.Where(f => f.Items.Any(i => i.Go != null)).ToList();
        var kinds = _lastKinds.Where(k => k.Items.Any(i => i.Go != null)).ToList();
        var triggers = _lastTriggers.Where(t => t != null).ToList();

        if (folders.Count == 0 && kinds.Count == 0 && triggers.Count == 0)
            _rows.Add(new RowModel { Kind = RowKind.Empty, Text = "Nothing placed yet", Idle = new Color(0f, 0f, 0f, 0f) });

        if (folders.Count > 0)
        {
            var sectionOpen = !_closedGroups.Contains(GroupsSection);
            AddSection(GroupsSection, $"{GroupsSection}  ({folders.Count})", sectionOpen);

            if (sectionOpen)
            {
                foreach (var folder in folders)
                {
                    var key = "@" + folder.Id;
                    var open = !_closedGroups.Contains(key);
                    var members = folder.Items.Where(i => i.Go != null).Select(i => i.Go).ToList();

                    _rows.Add(new RowModel
                    {
                        Kind = RowKind.Folder,
                        Key = key,
                        Open = open,
                        Text = $"{folder.Name}  ({members.Count})",
                        Indent = Indent,
                        Idle = FolderIdle,
                        Members = members
                    });
                    if (!open) continue;

                    foreach (var (go, label) in folder.Items)
                        if (go != null) AddItem(go, label, Indent * 2f);
                }
            }
        }

        foreach (var kind in kinds)
        {
            var open = !_closedGroups.Contains(kind.Name);
            var count = kind.Items.Count(i => i.Go != null);
            AddSection(kind.Name, $"{kind.Name}  ({count})", open);
            if (!open) continue;

            foreach (var (go, label) in kind.Items)
                if (go != null) AddItem(go, label, Indent);
        }

        if (triggers.Count > 0)
        {
            var open = !_closedGroups.Contains(TriggerGroup);
            AddSection(TriggerGroup, $"{TriggerGroup}  ({triggers.Count})", open);
            if (open)
            {
                foreach (var trigger in triggers.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase))
                {
                    _indexOfTrigger[trigger] = _rows.Count;
                    _rows.Add(new RowModel
                    {
                        Kind = RowKind.Trigger,
                        Text = string.IsNullOrEmpty(trigger.Id) ? "(trigger)" : trigger.Id,
                        Indent = Indent,
                        Idle = RowIdle,
                        Trigger = trigger
                    });
                }
            }
        }

        SetContentHeight(_rows.Count);

        // Everything on screen is stale: unbind so the next LateUpdate draws the new rows.
        foreach (var view in _shown.Values) Hide(view);
        _shown.Clear();
        _shownFirst = -1;
        _shownLast = -2;
    }

    private void AddSection(string key, string text, bool open)
    {
        _sectionRows.Add(_rows.Count);
        _rows.Add(new RowModel
        {
            Kind = RowKind.Section,
            Key = key,
            Open = open,
            Text = (open ? "-  " : "+  ") + text,
            Idle = GroupIdle
        });
    }

    private void AddItem(GameObject go, string label, float indent)
    {
        _indexOfObject[go] = _rows.Count;
        _itemOrder.Add(go);
        _rows.Add(new RowModel
        {
            Kind = RowKind.Item,
            Text = label,
            Indent = indent,
            Idle = RowIdle,
            Go = go
        });
    }

    private void SetContentHeight(int rows)
    {
        var height = rows == 0 ? PadTop + PadBottom : PadTop + rows * Pitch - RowSpacing + PadBottom;
        _column.sizeDelta = new Vector2(_column.sizeDelta.x, height);
    }

    private static float RowTop(int index) => PadTop + index * Pitch;

    private float ViewTop()
    {
        if (_scroll == null || _scroll.viewport == null) return 0f;
        var contentHeight = _column.rect.height;
        var viewHeight = _scroll.viewport.rect.height;
        return contentHeight > viewHeight ? (1f - _scroll.verticalNormalizedPosition) * (contentHeight - viewHeight) : 0f;
    }

    // ---- the rows on screen --------------------------------------------------------------------

    /// <summary>
    /// Draws the rows inside the viewport plus a margin, and nothing else. Views leaving the window
    /// go back to the pool; views entering take one from it. Rows are placed at their index, so
    /// scrolling costs a handful of re-binds, never a layout pass.
    /// </summary>
    private void ShowVisibleRows()
    {
        if (_scroll == null || _scroll.viewport == null) return;

        var viewTop = ViewTop();
        var viewHeight = _scroll.viewport.rect.height;

        var first = Mathf.Max(0, Mathf.FloorToInt((viewTop - PadTop) / Pitch) - ViewMargin);
        var last = Mathf.Min(_rows.Count - 1, Mathf.CeilToInt((viewTop + viewHeight - PadTop) / Pitch) + ViewMargin);

        if (first == _shownFirst && last == _shownLast) return;
        _shownFirst = first;
        _shownLast = last;

        foreach (var index in _shown.Keys.Where(i => i < first || i > last).ToList())
        {
            Hide(_shown[index]);
            _shown.Remove(index);
        }

        for (var index = first; index <= last; index++)
        {
            if (_shown.ContainsKey(index)) continue;
            var view = Take();
            Bind(view, index);
            _shown[index] = view;
        }
    }

    private RowView Take()
    {
        foreach (var view in _pool)
            if (view.Index < 0) return view;

        var made = CreateView();
        _pool.Add(made);
        return made;
    }

    private void Hide(RowView view)
    {
        view.Index = -1;
        view.Root.SetActive(false);
    }

    private void Bind(RowView view, int index)
    {
        var row = _rows[index];
        view.Index = index;

        var top = -RowTop(index);
        view.Rect.offsetMin = new Vector2(PadSide, top - RowHeight);
        view.Rect.offsetMax = new Vector2(-PadSide, top);

        // The other player's selection wears its own colour here too, as a dot after the name.
        var peerHeld = (row.Kind == RowKind.Item && row.Go != null && Net.EditorPresence.IsPeerSelected(row.Go)) ||
                       (row.Kind == RowKind.Trigger && row.Trigger != null &&
                        Net.EditorPresence.IsPeerSelectedId(row.Trigger.Id));
        view.Label.text = peerHeld
            ? row.Text + " <color=" + Net.EditorPresence.PeerColourTag + ">●</color>"
            : row.Text;
        view.LabelRect.offsetMin = new Vector2(8f + row.Indent, 0f);
        view.Label.color = row.Kind == RowKind.Empty ? new Color(1f, 1f, 1f, 0.6f) : Color.white;

        var lit = (row.Kind == RowKind.Item && row.Go != null && _lit.Contains(row.Go)) ||
                  (row.Kind == RowKind.Trigger && row.Trigger != null && row.Trigger == _litTrigger);
        view.Hover.Idle = lit ? RowLit : row.Idle;
        view.Hover.Hover = row.Kind == RowKind.Empty ? row.Idle : RowHover;
        view.Hover.Apply(false);
        view.Plate.raycastTarget = row.Kind != RowKind.Empty;

        var folder = row.Kind == RowKind.Folder;
        view.Caret.SetActive(folder);
        if (folder) view.CaretLabel.text = row.Open ? "-" : "+";

        view.Root.name = "Row_" + row.Text;
        view.Root.SetActive(true);
    }

    private RowView CreateView()
    {
        var go = new GameObject("Row");
        go.transform.SetParent(_column, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);

        var plate = go.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 2.2f;
        plate.color = RowIdle;

        var label = _ui.CreateLabel(go.transform, "", 16);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(8f, 0f);
        labelRt.offsetMax = new Vector2(-8f, 0f);
        var labelText = label.GetComponent<TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        labelText.raycastTarget = false;

        var view = new RowView
        {
            Root = go,
            Rect = rt,
            Plate = plate,
            Label = labelText,
            LabelRect = labelRt
        };

        MapEditorUI.AttachButton(go, plate, () => OnRowClicked(view));
        view.Hover = MapEditorUI.AddHover(go, plate, RowIdle, RowHover, null);

        // The folder caret: a small button on the row's left edge, so a click there folds the folder
        // instead of selecting it. Hidden on every other kind of row.
        var caret = _ui.CreateButton(go.transform, "-", () => OnCaretClicked(view), 22f);
        var caretRt = caret.GetComponent<RectTransform>();
        caretRt.anchorMin = caretRt.anchorMax = new Vector2(0f, 0.5f);
        caretRt.pivot = new Vector2(0f, 0.5f);
        caretRt.sizeDelta = new Vector2(22f, 22f);
        caretRt.anchoredPosition = new Vector2(Indent - 14f, 0f);
        view.Caret = caret;
        view.CaretLabel = caret.GetComponentInChildren<TMP_Text>();
        caret.SetActive(false);

        return view;
    }

    private void OnRowClicked(RowView view)
    {
        if (view.Index < 0 || view.Index >= _rows.Count) return;
        var row = _rows[view.Index];

        switch (row.Kind)
        {
            case RowKind.Section:
                ToggleGroup(row.Key);
                break;
            case RowKind.Folder:
                _editor.PickManyFromLayers(row.Members);
                break;
            case RowKind.Item:
                OnItemClicked(row.Go);
                break;
            case RowKind.Trigger:
                _editor.PickTriggerFromLayers(row.Trigger);
                break;
        }
    }

    private void OnCaretClicked(RowView view)
    {
        if (view.Index < 0 || view.Index >= _rows.Count) return;
        var row = _rows[view.Index];
        if (row.Kind == RowKind.Folder) ToggleGroup(row.Key);
    }

    /// <summary>
    /// A plain click selects the one item and remembers it as the anchor. A shift-click selects every
    /// item row between the anchor and this one, in the order the tree shows them, folders and kinds
    /// alike - the same way a file list takes a range.
    /// </summary>
    private void OnItemClicked(GameObject go)
    {
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (shift && _anchor != null && _anchor != go)
        {
            var from = _itemOrder.IndexOf(_anchor);
            var to = _itemOrder.IndexOf(go);
            if (from >= 0 && to >= 0)
            {
                var low = Mathf.Min(from, to);
                var high = Mathf.Max(from, to);
                _editor.PickManyFromLayers(_itemOrder.GetRange(low, high - low + 1));
                return;
            }
        }

        _anchor = go;
        _editor.PickFromLayers(go);
    }

    /// Folding needs no scan: the last scan's result is re-listed with the new fold state.
    private void ToggleGroup(string name)
    {
        if (!_closedGroups.Remove(name)) _closedGroups.Add(name);
        RebuildModel();
    }

    // ---- keeping the selection in view ---------------------------------------------------------

    /// <summary>
    /// Opens whatever section the object's row lives in - its group folder or its kind - so the row
    /// exists, then asks for it to be scrolled into view.
    /// </summary>
    private void Reveal(GameObject go, CTMapTrigger trigger)
    {
        var changed = false;

        if (go != null)
        {
            var groupId = MapEditorGroups.GroupOf(go);
            if (groupId != null)
            {
                changed |= _closedGroups.Remove(GroupsSection);
                changed |= _closedGroups.Remove("@" + groupId);
            }
            else
            {
                changed |= _closedGroups.Remove(Classify(go, out _) ?? "Props");
            }
        }
        else if (trigger != null)
        {
            changed |= _closedGroups.Remove(TriggerGroup);
        }

        if (changed) RebuildModel();

        // A brand-new object is not in the last scan yet; the scan that follows the edit will find it
        // and scroll then.
        if ((go != null && !_indexOfObject.ContainsKey(go)) || (trigger != null && !_indexOfTrigger.ContainsKey(trigger)))
        {
            _revealAfterScan = (go, trigger);
            return;
        }

        _scrollToObject = go;
        _scrollToTrigger = trigger;
    }

    private void ScrollToPending()
    {
        var index = -1;
        var toTop = false;

        if (_scrollToHeader != null)
        {
            // A section folded from its pinned header: land with that header at the top.
            foreach (var i in _sectionRows)
                if (_rows[i].Key == _scrollToHeader) index = i;
            toTop = true;
        }
        else if (_scrollToObject != null)
        {
            if (!_indexOfObject.TryGetValue(_scrollToObject, out index)) index = -1;
        }
        else if (_scrollToTrigger != null)
        {
            if (!_indexOfTrigger.TryGetValue(_scrollToTrigger, out index)) index = -1;
        }

        _scrollToObject = null;
        _scrollToTrigger = null;
        _scrollToHeader = null;

        if (index < 0 || _scroll == null || _scroll.viewport == null) return;

        var contentHeight = _column.rect.height;
        var viewHeight = _scroll.viewport.rect.height;
        if (contentHeight <= viewHeight + 1f)
        {
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }

        var rowTop = RowTop(index);
        var range = contentHeight - viewHeight;

        if (toTop)
        {
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp(rowTop, 0f, range) / range;
            return;
        }

        var viewTop = ViewTop();
        if (rowTop >= viewTop && rowTop + RowHeight <= viewTop + viewHeight) return;

        var wantedTop = Mathf.Clamp(rowTop - (viewHeight - RowHeight) * 0.5f, 0f, range);
        _scroll.StopMovement();
        _scroll.verticalNormalizedPosition = 1f - wantedTop / range;
    }

    /// <summary>
    /// Pins a copy of the last section header that has scrolled above the list's top edge, and lets
    /// the next header push it up as it arrives, the way a file explorer keeps a folder name in view.
    /// </summary>
    private void UpdateSticky()
    {
        if (_sticky == null) return;

        if (_scroll == null || _scroll.viewport == null || _sectionRows.Count == 0)
        {
            _sticky.gameObject.SetActive(false);
            _stickyKey = null;
            return;
        }

        var viewTop = ViewTop();
        RowModel active = null;
        var nextTop = float.MaxValue;

        foreach (var i in _sectionRows)
        {
            var top = RowTop(i);
            if (top < viewTop)
            {
                active = _rows[i];
            }
            else
            {
                nextTop = top;
                break;
            }
        }

        if (active == null)
        {
            _sticky.gameObject.SetActive(false);
            _stickyKey = null;
            return;
        }

        var push = Mathf.Max(0f, viewTop + RowHeight - nextTop);
        _sticky.anchoredPosition = new Vector2(0f, push);
        _stickyLabel.text = active.Text;
        _stickyKey = active.Key;
        if (!_sticky.gameObject.activeSelf) _sticky.gameObject.SetActive(true);
    }

    // ---- following the selection ---------------------------------------------------------------

    private void SyncSelection()
    {
        var select = _editor.GetTool<SelectTool>();
        var wanted = new HashSet<GameObject>();
        if (select != null)
            foreach (var go in select.Selection)
                if (go != null && go) wanted.Add(go);

        var trigger = _editor.GetTool<TriggerTool>()?.SelectedTrigger;
        if (trigger != null && !trigger) trigger = null;

        if (trigger == _litTrigger && wanted.SetEquals(_lit)) return;

        // Follow the selection: the primary object (or the trigger) is scrolled into view, opening
        // its section first if that is folded.
        var primary = select != null ? select.Selected : null;
        if (primary != null && !primary) primary = null;
        if ((primary != null || trigger != null) && (primary != _lastPrimary || trigger != _litTrigger))
            Reveal(primary, primary == null ? trigger : null);
        _lastPrimary = primary;

        _lit.Clear();
        _lit.UnionWith(wanted);
        _litTrigger = trigger;

        // Only the rows on screen exist; re-bind them and the new colours show. Rows that scroll in
        // later are bound with the current selection anyway.
        foreach (var pair in _shown) Bind(pair.Value, pair.Key);
    }
}
