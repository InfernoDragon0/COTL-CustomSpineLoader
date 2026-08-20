using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public static class MapEditorGizmos
{
    public static readonly Color BoxColour = new(0.1f, 1f, 1f, 1f);
    public static readonly Color GripColour = new(1f, 0.82f, 0.15f, 0.95f);

    // Combined bounds of every visible renderer under `go`.
    public static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = new Bounds();
        var found = false;

        foreach (var renderer in go.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!renderer.gameObject.activeInHierarchy) continue;
            if (renderer is ParticleSystemRenderer) continue;
            if (!(renderer is SpriteRenderer || renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    // Box drawn at the object's own depth rather than a fixed offset in front of everything,
    // so it sits in the scene with the object instead of hovering over it.
    public static GameObject CreateSelectionBox(GameObject target, string name)
    {
        if (target == null || !TryGetBounds(target, out _)) return null;

        var go = CreateBox(name, BoxColour);
        UpdateSelectionBox(go, target);
        return go;
    }

    // A box with no target of its own, for callers that know their own bounds - the trigger tool
    // marks an action's target this way, and a trigger volume has no renderer to measure.
    public static GameObject CreateBox(string name, Color colour)
    {
        var go = new GameObject(name);
        var line = go.AddComponent<LineRenderer>();

        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = 4;
        line.startWidth = line.endWidth = 0.1f;
        line.numCapVertices = 2;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = colour;
        line.sortingOrder = 32000;
        return go;
    }

    public static void SetBox(GameObject box, Bounds bounds, float z = -0.05f)
    {
        var line = box != null ? box.GetComponent<LineRenderer>() : null;
        if (line == null) return;

        line.SetPositions([
            new Vector3(bounds.min.x, bounds.min.y, z),
            new Vector3(bounds.max.x, bounds.min.y, z),
            new Vector3(bounds.max.x, bounds.max.y, z),
            new Vector3(bounds.min.x, bounds.max.y, z)
        ]);
    }

    public static void UpdateSelectionBox(GameObject box, GameObject target)
    {
        if (box == null || target == null || !TryGetBounds(target, out var bounds)) return;

        // Anchored to the target's own Z so the outline is coplanar with what it is marking.
        SetBox(box, bounds, target.transform.position.z - 0.05f);
    }

    // Where a drag grip should sit: the centre of the object's footprint.
    public static Vector3 GripPosition(GameObject target)
    {
        if (target == null) return Vector3.zero;
        return TryGetBounds(target, out var bounds) ? bounds.center : target.transform.position;
    }

    // Where a resize node should sit: the top-right of the same footprint, so the node is on the
    // corner of the outline the box already draws.
    public static Vector3 CornerPosition(GameObject target)
    {
        if (target == null) return Vector3.zero;
        return TryGetBounds(target, out var bounds)
            ? new Vector3(bounds.max.x, bounds.max.y, bounds.center.z)
            : target.transform.position;
    }
}
