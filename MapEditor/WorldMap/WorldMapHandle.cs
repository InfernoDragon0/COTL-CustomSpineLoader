using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

internal class WorldMapHandle : MonoBehaviour
{
    private const float HandleSize = 20f;

    private const float Offset = 6f;

    public static readonly Color ScaleColour = CustomSpineLoader.MapEditor.Tools.MapEditorGizmos.BoxColour;
    public static readonly Color RotateColour = new(0.35f, 0.95f, 0.45f);

    private RectTransform _rect;
    private RectTransform _target;
    private float _minScreenSize;

    private Vector2 _direction = Vector2.one;

    public static WorldMapHandle Create(RectTransform target, RectTransform parent,
        float minScreenSize, Vector2 direction, Color colour)
    {
        if (target == null || parent == null) return null;

        var go = new GameObject("Handle");
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(HandleSize, HandleSize);

        var image = go.AddComponent<Image>();
        image.sprite = MapEditorUI.RoundedPlate;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2f;
        image.color = colour;
        image.raycastTarget = false;

        var handle = go.AddComponent<WorldMapHandle>();
        handle._rect = rect;
        handle._target = target;
        handle._minScreenSize = minScreenSize;
        handle._direction = direction;
        handle.Follow();
        return handle;
    }

    public bool TryScreenBox(out Vector2 centre, out Vector2 half)
    {
        centre = Vector2.zero;
        half = Vector2.zero;
        if (_target == null) return false;

        WorldMapGizmoGeometry.ScreenBox(_target, _minScreenSize, out centre, out half);
        return true;
    }

    public Vector2 ScreenCentre => TryScreenBox(out var centre, out _) ? centre : Vector2.zero;

    public bool ContainsPointer(Vector2 pointer)
    {
        if (_rect == null) return false;

        var reach = HandleSize * CanvasScale * 0.75f;
        var corner = (Vector2)_rect.position;

        return Mathf.Abs(pointer.x - corner.x) <= reach && Mathf.Abs(pointer.y - corner.y) <= reach;
    }

    private float CanvasScale => Mathf.Max(0.0001f, transform.lossyScale.x);

    private const float EdgeMargin = 26f;
    private const float PanelMargin = 400f;
    private const float DockMargin = 150f;

    private void Follow()
    {
        if (_target == null || _rect == null) return;
        if (!TryScreenBox(out var centre, out var half)) return;

        var scale = CanvasScale;
        var corner = centre + new Vector2(half.x * _direction.x, half.y * _direction.y) +
                     new Vector2(Offset * _direction.x, Offset * _direction.y) * scale;

        var inset = EdgeMargin * scale;
        var maxX = Screen.width - PanelMargin * scale;

        corner.x = Mathf.Clamp(corner.x, inset, Mathf.Max(inset, maxX));
        corner.y = Mathf.Clamp(corner.y, DockMargin * scale, Mathf.Max(inset, Screen.height - inset));

        _rect.position = new Vector3(corner.x, corner.y, 0f);
    }

    private void LateUpdate()
    {
        if (_target == null)
        {
            Destroy(gameObject);
            return;
        }

        Follow();
    }
}

internal static class WorldMapGizmoGeometry
{
    private static readonly Vector3[] Corners = new Vector3[4];

    public static void ScreenBox(RectTransform rect, float minScreen, out Vector2 centre,
        out Vector2 half)
    {
        rect.GetWorldCorners(Corners);

        var min = (Vector2)Corners[0];
        var max = min;
        for (var i = 1; i < 4; i++)
        {
            min = Vector2.Min(min, Corners[i]);
            max = Vector2.Max(max, Corners[i]);
        }

        centre = (min + max) * 0.5f;

        var floor = minScreen * 0.5f;
        half = Vector2.Max((max - min) * 0.5f, new Vector2(floor, floor));
    }
}
