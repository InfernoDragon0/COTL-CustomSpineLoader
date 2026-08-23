using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

// The corner nodes the room editor's select tool works by, in canvas terms: small squares riding
// the corners of whatever is selected. The blue one on the right scales; the yellow one on the left
// rotates. They hang from the screen's gizmo root like the selection frame, so nothing the map
// draws can cover them, and the tools do the dragging - a handle only says where it is and whether
// the pointer is on it.
internal class WorldMapHandle : MonoBehaviour
{
    // Screen pixels. Big enough to hit at a glance, small enough not to hide what it sits on.
    private const float HandleSize = 20f;

    // How far outside the corner it sits, so it reads as a handle on the box rather than part of it.
    private const float Offset = 6f;

    public static readonly Color ScaleColour = CustomSpineLoader.MapEditor.Tools.MapEditorGizmos.BoxColour;
    public static readonly Color RotateColour = CustomSpineLoader.MapEditor.Tools.MapEditorGizmos.GripColour;

    private RectTransform _rect;
    private RectTransform _target;
    private float _minScreenSize;

    // Which corner of the box it rides: (1,1) top right, (-1,1) top left.
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

    // The box the handle sits on, in screen pixels - the same box the selection frame draws and the
    // tools pick against, so all three agree however far the target is scaled down.
    public bool TryScreenBox(out Vector2 centre, out Vector2 half)
    {
        centre = Vector2.zero;
        half = Vector2.zero;
        if (_target == null) return false;

        WorldMapGizmoGeometry.ScreenBox(_target, _minScreenSize, out centre, out half);
        return true;
    }

    public Vector2 ScreenCentre => TryScreenBox(out var centre, out _) ? centre : Vector2.zero;

    // Generous by a few pixels: this is a grab test, not a hit test on the art.
    public bool ContainsPointer(Vector2 pointer)
    {
        if (_rect == null) return false;

        var reach = HandleSize * CanvasScale * 0.75f;
        var corner = (Vector2)_rect.position;

        return Mathf.Abs(pointer.x - corner.x) <= reach && Mathf.Abs(pointer.y - corner.y) <= reach;
    }

    private float CanvasScale => Mathf.Max(0.0001f, transform.lossyScale.x);

    // How far inside the screen edge the handle is kept, in reference pixels: enough that it is
    // never half off the edge, and clear of the editor's own chrome on the right and the bottom.
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

        // A background layer is bigger than the screen and its true corner is somewhere off in the
        // dark. The handle rides the corner until the corner leaves the screen, then holds at the
        // edge - still on the same side of the centre, so the drag still reads the same way.
        var inset = EdgeMargin * scale;
        var maxX = Screen.width - PanelMargin * scale;

        corner.x = Mathf.Clamp(corner.x, inset, Mathf.Max(inset, maxX));
        corner.y = Mathf.Clamp(corner.y, DockMargin * scale, Mathf.Max(inset, Screen.height - inset));

        // On a screen-space overlay canvas a world position IS a screen pixel.
        _rect.position = new Vector3(corner.x, corner.y, 0f);
    }

    private void LateUpdate()
    {
        // A redraw replaces the object the handle marks; the screen makes a new handle for the new
        // one, and this is the old one's own cue to go.
        if (_target == null)
        {
            Destroy(gameObject);
            return;
        }

        Follow();
    }
}

// One screen-space box for the frame, the handles and the tools' picking, so a layer is never
// outlined in one place, grabbed in another and scaled from a third.
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

        // minScreen is already in screen pixels - world corners on an overlay canvas are pixels.
        var floor = minScreen * 0.5f;
        half = Vector2.Max((max - min) * 0.5f, new Vector2(floor, floor));
    }
}
