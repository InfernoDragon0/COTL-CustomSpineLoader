using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

// The editor's marker for the selected layer: four thin edges drawn around it. It is NOT parented
// to the layer - it hangs from the screen's gizmo root, which is the last thing drawn, and copies
// the layer's transform each frame. Parented to the layer it would draw at that layer's depth, and
// anything in front of the layer would cover the frame around it.
internal class WorldMapSelectionFrame : MonoBehaviour
{
    private const float Thickness = 3f;

    // How big the frame draws, in screen pixels, around a layer whose rect says nothing about what
    // it draws.
    private const float FallbackScreenSize = 140f;

    // The floor the frame shrinks to, in screen pixels. Also the tools' grab box (they halve it),
    // so what is outlined is exactly what can be clicked however far a layer is scaled down.
    internal const float MinScreenSize = 68f;

    // Spine layers get a bigger one: the skeleton draws well outside the rect it reports, so a box
    // that fits a sprite reads as too tight around a spine.
    internal const float SpineMinScreenSize = 110f;

    internal static float MinFor(CTWorldMapLayer layer) =>
        layer != null && layer.IsSpine ? SpineMinScreenSize : MinScreenSize;

    private readonly RectTransform[] _edges = new RectTransform[4];
    private RectTransform _rect;
    private RectTransform _target;
    private float _minScreenSize = MinScreenSize;
    private float _appliedScale = -1f;

    public static WorldMapSelectionFrame Create(RectTransform target, RectTransform parent,
        float minScreenSize)
    {
        if (target == null || parent == null) return null;

        var go = new GameObject("SelectionFrame");
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);

        var frame = go.AddComponent<WorldMapSelectionFrame>();
        frame._rect = rect;
        frame._target = target;
        frame._minScreenSize = minScreenSize;

        for (var i = 0; i < 4; i++) frame._edges[i] = frame.CreateEdge(rect, i);
        frame.Follow();
        frame.ApplyGeometry();
        frame.ApplyThickness();
        return frame;
    }

    // Position, rotation and scale copied from the layer, since the frame no longer inherits them.
    // The centre is taken from the rect rather than the pivot, so an off-centre pivot still frames
    // what is drawn.
    private void Follow()
    {
        if (_target == null || _rect == null) return;

        _rect.position = _target.TransformPoint(_target.rect.center);
        _rect.rotation = _target.rotation;

        // Both roots sit under the content rect at identity, so the layer's own local scale is the
        // frame's too - including a negative x from a flip, which only mirrors the box.
        _rect.localScale = _target.localScale;
    }

    // Sized in screen pixels rather than the layer's own units, and re-run every frame: the scale
    // slider moves the layer under the frame, and a frame that tracked it down to nothing would
    // leave nothing to aim at.
    private void ApplyGeometry()
    {
        if (_target == null || _rect == null) return;

        var scale = Mathf.Max(0.0001f, Mathf.Abs(_target.lossyScale.x));
        var size = _target.rect.size;

        // A rect that says nothing (a spine graphic's does not) gets a frame of its own size.
        if (size.x <= 1f || size.y <= 1f)
            size = new Vector2(FallbackScreenSize, FallbackScreenSize) / scale;
        else
            size = Vector2.Max(size, new Vector2(_minScreenSize, _minScreenSize) / scale);

        if ((_rect.sizeDelta - size).sqrMagnitude > 0.0001f) _rect.sizeDelta = size;
    }

    private RectTransform CreateEdge(RectTransform parent, int index)
    {
        var go = new GameObject("Edge" + index);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        switch (index)
        {
            case 0: // top
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                break;
            case 1: // bottom
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                break;
            case 2: // left
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                break;
            default: // right
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0.5f);
                break;
        }

        var image = go.AddComponent<Image>();

        // The room editor's selection colour: one editor's gizmos should not be a different
        // colour from the other's.
        image.color = CustomSpineLoader.MapEditor.Tools.MapEditorGizmos.BoxColour;

        // Scenery: the tools pick layers geometrically and must not be shadowed by the marker.
        image.raycastTarget = false;
        return rect;
    }

    // The frame rides the layer's own scale, so a layer scaled down to a fifth would draw a
    // hairline; the edges are divided by that scale to keep an even weight on screen.
    private void ApplyThickness()
    {
        if (_target == null) return;

        var scale = Mathf.Max(0.0001f, Mathf.Abs(_target.lossyScale.x));
        if (Mathf.Approximately(scale, _appliedScale)) return;
        _appliedScale = scale;

        var weight = Thickness / scale;
        if (_edges[0] != null) _edges[0].sizeDelta = new Vector2(0f, weight);
        if (_edges[1] != null) _edges[1].sizeDelta = new Vector2(0f, weight);
        if (_edges[2] != null) _edges[2].sizeDelta = new Vector2(weight, 0f);
        if (_edges[3] != null) _edges[3].sizeDelta = new Vector2(weight, 0f);
    }

    private void LateUpdate()
    {
        // A redraw replaces the layer objects; the frame outlives its target for a frame and then
        // has nothing to mark. The screen makes a new one for the new object.
        if (_target == null)
        {
            Destroy(gameObject);
            return;
        }

        Follow();
        ApplyGeometry();
        ApplyThickness();
    }
}
