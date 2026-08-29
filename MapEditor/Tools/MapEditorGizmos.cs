using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public static class MapEditorGizmos
{
    public static readonly Color BoxColour = new(0.1f, 1f, 1f, 1f);
    public static readonly Color GripColour = new(1f, 0.82f, 0.15f, 0.95f);

    private static GameObject _memoTarget;
    private static int _memoFrame = -1;
    private static bool _memoFound;
    private static Bounds _memoBounds;

    public static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        if (ReferenceEquals(go, _memoTarget) && _memoFrame == Time.frameCount)
        {
            bounds = _memoBounds;
            return _memoFound;
        }

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

        _memoTarget = go;
        _memoFrame = Time.frameCount;
        _memoFound = found;
        _memoBounds = bounds;
        return found;
    }

    public static GameObject CreateSelectionBox(GameObject target, string name)
    {
        if (target == null || !TryGetBounds(target, out _)) return null;

        var go = CreateBox(name, BoxColour);
        UpdateSelectionBox(go, target);
        return go;
    }

    private static Material _lineMaterial;

    public static Material LineMaterial()
    {
        if (_lineMaterial == null)
        {
            _lineMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        return _lineMaterial;
    }

    private static readonly List<GameObject> Drawn = [];
    private static bool _hidden;

    public static void SetHidden(bool hidden)
    {
        _hidden = hidden;

        for (var i = Drawn.Count - 1; i >= 0; i--)
        {
            if (Drawn[i] == null) Drawn.RemoveAt(i);
            else Drawn[i].SetActive(!hidden);
        }
    }

    public static GameObject CreateBox(string name, Color colour)
    {
        var go = new GameObject(name);
        var line = go.AddComponent<LineRenderer>();

        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = 4;
        line.startWidth = line.endWidth = 0.1f;
        line.numCapVertices = 2;
        line.sharedMaterial = LineMaterial();
        line.startColor = line.endColor = colour;
        line.sortingOrder = 32000;

        Drawn.Add(go);
        if (_hidden) go.SetActive(false);
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

        SetBox(box, bounds, target.transform.position.z - 0.05f);
    }

    public static Vector3 GripPosition(GameObject target)
    {
        if (target == null) return Vector3.zero;
        return TryGetBounds(target, out var bounds) ? bounds.center : target.transform.position;
    }

    public static Vector3 CornerPosition(GameObject target)
    {
        if (target == null) return Vector3.zero;
        return TryGetBounds(target, out var bounds)
            ? new Vector3(bounds.max.x, bounds.max.y, bounds.center.z)
            : target.transform.position;
    }

    public static Vector3 FarCornerPosition(GameObject target)
    {
        if (target == null) return Vector3.zero;
        return TryGetBounds(target, out var bounds)
            ? new Vector3(bounds.min.x, bounds.max.y, bounds.center.z)
            : target.transform.position;
    }
}
