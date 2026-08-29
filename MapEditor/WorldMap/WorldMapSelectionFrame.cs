using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

internal class WorldMapSelectionFrame : MonoBehaviour
{
    private const float Thickness = 3f;

    private const float FallbackScreenSize = 140f;

    internal const float MinScreenSize = 68f;

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

    private void Follow()
    {
        if (_target == null || _rect == null) return;

        _rect.position = _target.TransformPoint(_target.rect.center);
        _rect.rotation = _target.rotation;

        _rect.localScale = _target.localScale;
    }

    private void ApplyGeometry()
    {
        if (_target == null || _rect == null) return;

        var scale = Mathf.Max(0.0001f, Mathf.Abs(_target.lossyScale.x));
        var size = _target.rect.size;

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

        image.color = CustomSpineLoader.MapEditor.Tools.MapEditorGizmos.BoxColour;

        image.raycastTarget = false;
        return rect;
    }

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
