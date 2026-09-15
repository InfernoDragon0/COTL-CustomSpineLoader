using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class DungeonMapCanvas
{
    private readonly RuntimeMapEditor _editor;
    private readonly MapEditorUI _ui;

    private CTDungeonMap _map;

    public DungeonMapCanvas(RuntimeMapEditor editor, MapEditorUI ui)
    {
        _editor = editor;
        _ui = ui;
    }

    // ---- what the screen offers ------------------------------------------------------------

    public bool IsOpen => _root != null;

    public RectTransform OptionsContent { get; private set; }

    public Action CloseRequested;

    // ---- metrics -----------------------------------------------------------------------------

    private const float OptionsWidth = Chrome.EditorSidebar.Width;
    private const float BarHeight = Chrome.EditorBottomBar.Height;
    private const float DockIconSize = 60f;

    /// The editor draws the game's own node prefab at half size, so DungeonMapBuilder.ViewScale
    /// is its reciprocal: editor units times that land on the overlay at the same relative spacing.
    internal const float NodeScale = 0.5f;
    private const float BigNodeScale = 1.5f;

    private const float NodeRadius = 52f;

    private const float LinkReach = 14f;

    // ---- objects -----------------------------------------------------------------------------

    private GameObject _root;
    private RectTransform _contentRoot;
    private RectTransform _linkLayer;
    private RectTransform _nodeLayer;
    private RectTransform _markLayer;

    private Chrome.EditorTopBar _topBar;
    private Chrome.EditorBottomBar _bottomBar;
    private Chrome.EditorSidebar _sidebar;
    private Chrome.ShortcutCard _card;
    private MapEditorConfirm _confirm;

    private readonly List<RectTransform> _blockers = [];
    private readonly List<(string Key, string Action)> _shortcuts = [];

    public readonly Dictionary<CTDungeonMapNode, RectTransform> NodeRects = [];
    private readonly Dictionary<CTDungeonMapNode, DungeonNodeVisual> _visuals = [];

    private sealed class LinkView
    {
        public RectTransform Rect;
        public Lamb.UI.MMUILineRenderer Line;
        public CTDungeonMapNode From;
        public CTDungeonMapNode To;
    }

    private readonly List<LinkView> _links = [];

    private CTDungeonMapNode _marked;
    private CTDungeonMapNode _startNode;

    private readonly Dictionary<CTDungeonMapNode, int> _layerOf = [];
    private List<DungeonMapBuilder.MapIssue> _issues = [];

    // ---- open / close ------------------------------------------------------------------------

    public void Open(CTDungeonMap map, List<Chrome.EditorDockItem> dock,
        IEnumerable<(string Key, string Action)> shortcuts)
    {
        Close();

        _map = map;
        var canvas = _ui.CanvasRoot;
        if (canvas == null || _map == null) return;

        _shortcuts.Clear();
        if (shortcuts != null) _shortcuts.AddRange(shortcuts);

        if (DungeonMapSkin.NeedsLoad) _editor.StartCoroutine(LoadSkin());

        _root = new GameObject("DungeonMapScreen");
        _root.transform.SetParent(canvas, false);
        Stretch(_root.AddComponent<RectTransform>());

        var backdrop = _root.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.97f);

        BuildChrome();

        _contentRoot = MakeRect(_root.transform, "Content");
        Stretch(_contentRoot);
        _linkLayer = MakeLayer("Connections");
        _nodeLayer = MakeLayer("Nodes");
        _markLayer = MakeLayer("Marks");

        BuildBars(dock);

        _ui.IconPreviewRightOffset = OptionsWidth + 28f;
        _ui.IconPreviewTopOffset = Chrome.EditorTopBar.Height + 12f;

        _editor.SetOwnChromeVisible(false);

        RebuildVisuals();
    }

    private System.Collections.IEnumerator LoadSkin()
    {
        var map = _map;
        yield return DungeonMapSkin.EnsureLoaded();

        if (_root != null && ReferenceEquals(_map, map)) RebuildVisuals();
    }

    public void Close()
    {
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _editor.SetOwnChromeVisible(true);
        }

        _ui.HideIconPreview();
        _ui.IconPreviewRightOffset = MapEditorUI.DefaultIconPreviewRightOffset;
        _ui.IconPreviewTopOffset = MapEditorUI.DefaultIconPreviewTopOffset;

        _root = null;
        _contentRoot = _linkLayer = _nodeLayer = _markLayer = null;

        _topBar = null;
        _bottomBar = null;
        _sidebar = null;
        _card = null;
        _confirm = null;
        OptionsContent = null;

        _blockers.Clear();
        _shortcuts.Clear();
        NodeRects.Clear();
        _visuals.Clear();
        _links.Clear();
        _issues = [];
        _marked = null;
    }

    public void SetVisible(bool visible)
    {
        if (_root != null) _root.SetActive(visible);
    }

    public bool Visible => _root != null && _root.activeSelf;

    // ---- chrome ------------------------------------------------------------------------------

    private void BuildChrome()
    {
        var chrome = DungeonMapSkin.CloneChrome();
        if (chrome == null) return;

        chrome.transform.SetParent(_root.transform, false);
        chrome.SetActive(true);

        var rt = chrome.GetComponent<RectTransform>();
        if (rt != null) Stretch(rt);
    }

    private RectTransform MakeLayer(string name)
    {
        var rt = MakeRect(_contentRoot, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    /// The same three plates as the room editor, from the same classes. The skin clone is already
    /// in place by the time this runs, so the bars sit over it rather than under it.
    private void BuildBars(List<Chrome.EditorDockItem> dock)
    {
        var parent = _root.transform;

        _topBar = new Chrome.EditorTopBar(_ui, parent, _blockers.Add);
        _topBar.RebuildActions(null,
        [
            ("Esc", "Close", () => CloseRequested?.Invoke())
        ]);

        _bottomBar = new Chrome.EditorBottomBar(_ui, parent, _blockers.Add);
        _bottomBar.OnHelp = () => _card?.Toggle();
        _bottomBar.SetHints(_shortcuts);

        if (dock != null)
            foreach (var item in dock)
            {
                var act = item.Do;
                _bottomBar.AddTool(item.Icon, item.Label, () => act?.Invoke(), out _, DockIconSize,
                    item.Hint);
            }

        _bottomBar.LayoutAfterDock();

        _sidebar = new Chrome.EditorSidebar(_ui, parent, _blockers.Add,
            Chrome.EditorTopBar.Height + 12f, BarHeight + 12f, optionsTitle: "Dungeon nodes",
            withLayers: false);

        OptionsContent = _ui.CreateScrollColumn(_sidebar.OptionsContent, "OptionsColumn", out _);

        _card = new Chrome.ShortcutCard(_ui, parent, _blockers.Add, Globals);
        _card.SetTool("Dungeon builder", _shortcuts);

        _confirm = new MapEditorConfirm(_ui, parent, BarHeight + 12f);
    }

    /// True under everything on this screen, so they live on the card rather than in the chip row.
    private static readonly (string Key, string Action)[] Globals =
    [
        ("Ctrl+Z", "Undo last change"),
        ("Ctrl+S", "Quicksave this dungeon"),
        ("Esc", "Close the map")
    ];

    public void LateUpdate()
    {
        _bottomBar?.Tick();
        _topBar?.Tick();
        _sidebar?.Layout(OptionsContent != null ? OptionsContent.rect.height + 12f : 0f, 0f, true);
    }

    // ---- confirm strip ---------------------------------------------------------------------------

    public bool ConfirmOpen => _confirm is { Open: true };

    public void ShowConfirm(string text, Action onConfirm, string confirmLabel = "Enter",
        string altLabel = null, Action onAlt = null)
    {
        _confirm?.Show(text, onConfirm, confirmLabel, altLabel, onAlt);
    }

    public void HideConfirm() => _confirm?.Hide();

    private static void Plate(RectTransform rect)
    {
        VanillaChrome.Dress(rect.gameObject.AddComponent<Image>());
    }

    // ---- what the tool sets --------------------------------------------------------------------

    public void SetTitle(string text) => _topBar?.SetTitle(text);

    public void SetBadge(string text, Color colour) => _topBar?.SetNote(text, colour);

    public void SetHint(string text, StatusSeverity severity = StatusSeverity.Info) =>
        _bottomBar?.SetStatus(text, severity, pulse: true);

    public void SetIssues(List<DungeonMapBuilder.MapIssue> issues)
    {
        _issues = issues ?? [];
        RepaintMarks();
    }

    // ---- pointer ------------------------------------------------------------------------------

    public Vector2 PointerContent()
    {
        if (_contentRoot == null) return Vector2.zero;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(_contentRoot, Input.mousePosition,
            null, out var local);
        return local;
    }

    public bool PointerOverChrome()
    {
        if (_editor.WorldClicksBlocked) return true;

        var mouse = (Vector2)Input.mousePosition;
        foreach (var blocker in _blockers)
        {
            if (blocker == null || !blocker.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(blocker, mouse, null)) return true;
        }

        return false;
    }

    public CTDungeonMapNode HitNode(Vector2 point)
    {
        if (_map == null) return null;

        CTDungeonMapNode best = null;
        var bestDistance = float.MaxValue;

        foreach (var node in _map.Nodes)
        {
            if (node == null) continue;

            var radius = NodeRadius * (IsBig(node) ? BigNodeScale : 1f);
            var distance = Vector2.Distance(point, Position(node));
            if (distance <= radius && distance < bestDistance)
            {
                best = node;
                bestDistance = distance;
            }
        }

        return best;
    }

    public bool HitLink(Vector2 point, out CTDungeonMapNode owner, out string childId)
    {
        owner = null;
        childId = null;
        if (_map == null) return false;

        foreach (var node in _map.Nodes)
        {
            if (node == null) continue;

            foreach (var id in node.Children)
            {
                var child = _map.FindNode(id);
                if (child == null) continue;
                if (DistanceToSegment(point, Position(node), Position(child)) > LinkReach) continue;

                owner = node;
                childId = id;
                return true;
            }
        }

        return false;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.sqrMagnitude;
        if (lengthSquared < 0.001f) return Vector2.Distance(point, a);

        var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        return Vector2.Distance(point, a + ab * t);
    }

    public static Vector2 Position(CTDungeonMapNode node) => new(node.PosX, node.PosY);

    private static bool IsBig(CTDungeonMapNode node) =>
        node.NodeType is "Boss" or "MiniBossFloor" or "FinalBoss" or "LeaderFloor";

    // ---- drawing --------------------------------------------------------------------------------

    public void RebuildVisuals()
    {
        if (_root == null || _map == null) return;

        ClearLayer(_linkLayer);
        ClearLayer(_nodeLayer);
        NodeRects.Clear();
        _visuals.Clear();
        _links.Clear();

        var rows = DungeonMapBuilder.Rows(_map);
        _layerOf.Clear();
        for (var y = 0; y < rows.Count; y++)
            foreach (var node in rows[y])
                _layerOf[node] = y + 1;

        _startNode = rows.Count > 0 && rows[0].Count > 0 ? rows[0][0] : null;

        foreach (var node in _map.Nodes)
        {
            if (node == null) continue;

            foreach (var childId in node.Children)
            {
                var child = _map.FindNode(childId);
                if (child == null) continue;

                BuildLink(node, child);
            }
        }

        foreach (var node in _map.Nodes)
            if (node != null) BuildNode(node);

        RepaintMarks();
    }

    public void HighlightNode(CTDungeonMapNode node)
    {
        _marked = node;
        RepaintMarks();
    }

    private static readonly Color SelectedMark =
        new(MapEditorUI.Accent.r, MapEditorUI.Accent.g, MapEditorUI.Accent.b, 0.9f);

    private static readonly Color BlockingMark = new(1f, 0.42f, 0.42f, 0.95f);
    private static readonly Color AdvisoryMark = new(1f, 0.72f, 0.25f, 0.9f);

    private void RepaintMarks()
    {
        if (_map == null) return;

        ClearLayer(_markLayer);

        foreach (var issue in _issues)
        {
            if (!issue.HasNode || !_map.Nodes.Contains(issue.Node)) continue;
            BuildHalo(issue.Node, issue.IsAdvisory ? AdvisoryMark : BlockingMark);
        }

        foreach (var pair in _visuals)
        {
            if (pair.Value == null) continue;

            var isSelected = ReferenceEquals(pair.Key, _marked);
            pair.Value.SetMark(isSelected ? SelectedMark : null, isSelected);
        }
    }

    private void BuildHalo(CTDungeonMapNode node, Color colour)
    {
        var size = NodeRadius * 2f * (IsBig(node) ? BigNodeScale : 1f) * 1.1f;

        var rt = MakeRect(_markLayer, "Halo_" + node.Id);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Position(node);

        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = MapEditorUI.RoundedPlate;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f;
        image.color = new Color(colour.r, colour.g, colour.b, 0.5f);
        image.raycastTarget = false;
    }

    private void BuildNode(CTDungeonMapNode node)
    {
        var rt = MakeRect(_nodeLayer, "Node_" + node.Id);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(NodeRadius * 2f, NodeRadius * 2f);
        rt.anchoredPosition = Position(node);

        NodeRects[node] = rt;

        var big = IsBig(node);
        var icon = DungeonMapBuilder.IconFor(node.NodeType);

        if (!BuildVanillaArt(node, rt, big, icon)) BuildOwnArt(node, rt, big, icon);

        BuildCaption(node, rt, big);
    }

    private void BuildCaption(CTDungeonMapNode node, RectTransform parent, bool big)
    {
        var bound = !string.IsNullOrEmpty(node.Level);
        var layer = _layerOf.TryGetValue(node, out var index) ? $" (L{index})" : "";

        var caption = _ui.CreateLabel(parent,
            $"{node.NodeType}{layer} - " + (bound ? node.Level : "vanilla floor"),
            15, TextAlignmentOptions.Center);

        var captionRect = caption.GetComponent<RectTransform>();
        captionRect.anchorMin = captionRect.anchorMax = new Vector2(0.5f, 0.5f);
        captionRect.pivot = new Vector2(0.5f, 1f);
        captionRect.sizeDelta = new Vector2(230f, 20f);
        captionRect.anchoredPosition = new Vector2(0f, -(NodeRadius * (big ? BigNodeScale : 1f) + 4f));

        var captionText = caption.GetComponent<TMP_Text>();
        captionText.enableWordWrapping = false;
        captionText.raycastTarget = false;

        captionText.color = bound
            ? new Color(0.004f, 0.835f, 0.635f)
            : new Color(1f, 1f, 1f, 0.62f);
    }

    private bool BuildVanillaArt(CTDungeonMapNode node, RectTransform parent, bool big, Sprite icon)
    {
        var clone = DungeonMapSkin.CloneNode();
        if (clone == null) return false;

        var visual = DungeonNodeVisual.Attach(clone);
        if (visual == null)
        {
            UnityEngine.Object.DestroyImmediate(clone);
            return false;
        }

        clone.transform.SetParent(parent, false);
        clone.SetActive(true);

        var cloneRt = clone.GetComponent<RectTransform>();
        if (cloneRt != null)
        {
            cloneRt.anchorMin = cloneRt.anchorMax = new Vector2(0.5f, 0.5f);
            cloneRt.anchoredPosition = Vector2.zero;
        }

        var scale = NodeScale * (big ? BigNodeScale : 1f);
        clone.transform.localScale = new Vector3(scale, scale, 1f);

        visual.SetIcon(icon);
        visual.ApplyEditView();

        if (ReferenceEquals(_startNode, node)) visual.ShowStartingPin();

        _visuals[node] = visual;
        return true;
    }

    private void BuildOwnArt(CTDungeonMapNode node, RectTransform parent, bool big, Sprite icon)
    {
        var size = NodeRadius * 1.6f * (big ? BigNodeScale : 1f);

        if (icon != null)
        {
            var iconRt = MakeRect(parent, "Icon");
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(size, size);

            var image = iconRt.gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return;
        }

        var plateRt = MakeRect(parent, "Plate");
        plateRt.anchorMin = plateRt.anchorMax = new Vector2(0.5f, 0.5f);
        plateRt.sizeDelta = new Vector2(size, size);

        var plate = plateRt.gameObject.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 2.5f;
        plate.color = new Color(0.16f, 0.2f, 0.26f, 0.95f);
        plate.raycastTarget = false;

        var label = _ui.CreateLabel(plateRt, Shorten(node.NodeType), 12, TextAlignmentOptions.Center);
        Stretch(label.GetComponent<RectTransform>());
        label.GetComponent<TMP_Text>().raycastTarget = false;
    }

    private static string Shorten(string typeName) =>
        string.IsNullOrEmpty(typeName) || typeName.Length <= 9 ? typeName : typeName.Substring(0, 9);

    // ---- links ------------------------------------------------------------------------------

    private static readonly Color LinkColour = new(0.8f, 0.8f, 0.8f, 0.85f);

    private void BuildLink(CTDungeonMapNode from, CTDungeonMapNode to)
    {
        var view = BuildVanillaLink(from, to) ?? BuildPlainLink(from, to);
        if (view == null) return;

        _links.Add(view);
        AimLink(view);
    }

    private LinkView BuildVanillaLink(CTDungeonMapNode from, CTDungeonMapNode to)
    {
        if (!DungeonMapSkin.Usable || DungeonMapSkin.LineMaterial == null) return null;

        try
        {
            var rt = MakeRect(_linkLayer, $"Link_{from.Id}_{to.Id}");
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var line = rt.gameObject.AddComponent<Lamb.UI.MMUILineRenderer>();
            line.Texture = DungeonMapSkin.LineTexture;
            line.material = DungeonMapSkin.LineMaterial;
            line.Width = DungeonMapSkin.VanillaLineWidth * NodeScale;
            line.raycastTarget = false;

            line.Points =
            [
                new Lamb.UI.MMUILineRenderer.BranchPoint(Position(from)),
                new Lamb.UI.MMUILineRenderer.BranchPoint(Position(to))
            ];

            return new LinkView { Rect = rt, Line = line, From = from, To = to };
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Dungeon map: the game's line renderer would not draw a link (" +
                                  e.Message + "); using plain ones.");
            return null;
        }
    }

    private LinkView BuildPlainLink(CTDungeonMapNode from, CTDungeonMapNode to)
    {
        var rt = MakeRect(_linkLayer, $"Link_{from.Id}_{to.Id}");
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);

        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = Dash;
        image.type = Image.Type.Tiled;
        image.raycastTarget = false;

        return new LinkView { Rect = rt, Line = null, From = from, To = to };
    }

    public void RefreshLinks()
    {
        for (var i = _links.Count - 1; i >= 0; i--)
        {
            var view = _links[i];
            if (view.Rect == null)
            {
                _links.RemoveAt(i);
                continue;
            }

            AimLink(view);
        }
    }

    private void AimLink(LinkView view)
    {
        var a = Position(view.From);
        var b = Position(view.To);

        var touchesSelection = _marked != null &&
                               (ReferenceEquals(view.From, _marked) || ReferenceEquals(view.To, _marked));
        var colour = touchesSelection
            ? new Color(MapEditorUI.Accent.r, MapEditorUI.Accent.g, MapEditorUI.Accent.b, 0.95f)
            : LinkColour;

        if (view.Line != null)
        {
            try
            {
                view.Line.Points =
                [
                    new Lamb.UI.MMUILineRenderer.BranchPoint(a),
                    new Lamb.UI.MMUILineRenderer.BranchPoint(b)
                ];
                view.Line.Color = colour;
            }
            catch (Exception)
            {
                view.Line = null;
            }

            return;
        }

        var delta = b - a;
        view.Rect.sizeDelta = new Vector2(delta.magnitude, 5f);
        view.Rect.anchoredPosition = a;
        view.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

        var image = view.Rect.GetComponent<Image>();
        if (image != null) image.color = colour;
    }

    private static Sprite _dash;

    private static Sprite Dash
    {
        get
        {
            if (_dash != null) return _dash;

            var texture = new Texture2D(12, 4, TextureFormat.RGBA32, false)
            {
                name = "CultTweaker_DungeonDash",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };

            var pixels = new Color32[12 * 4];
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 12; x++)
                    pixels[y * 12 + x] = x < 8 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);

            texture.SetPixels32(pixels);
            texture.Apply();

            _dash = Sprite.Create(texture, new Rect(0f, 0f, 12f, 4f), new Vector2(0.5f, 0.5f), 1f, 0,
                SpriteMeshType.FullRect);
            _dash.name = "CultTweaker_DungeonDash";
            _dash.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _dash;
        }
    }

    // ---- scaffolding ------------------------------------------------------------------------------

    public Vector2 ClampToMap(Vector2 point)
    {
        var canvas = _ui.CanvasRoot;
        var size = canvas != null ? canvas.rect.size : new Vector2(1920f, 1080f);

        var halfWidth = size.x * 0.5f - 60f;
        var halfHeight = size.y * 0.5f - 60f;

        return new Vector2(Mathf.Clamp(point.x, -halfWidth, halfWidth),
            Mathf.Clamp(point.y, -halfHeight, halfHeight));
    }

    private static RectTransform MakeRect(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void ClearLayer(RectTransform layer)
    {
        if (layer == null) return;

        for (var i = layer.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(layer.GetChild(i).gameObject);
    }
}
