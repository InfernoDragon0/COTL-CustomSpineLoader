using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class SelectionPreview
{
    private const int Size = 256;

    private const float Padding = 1.25f;

    private static Camera _camera;
    private static RenderTexture _target;
    private static int _layer = -1;

    private static readonly List<Transform> _restaged = [];
    private static readonly List<int> _restagedLayers = [];

    public static RenderTexture Texture => _target;

    public static bool Render(GameObject subject)
    {
        if (subject == null) return false;

        var hidden = HideGlows(subject, VisibleLayers());

        if (!TryGetBounds(subject, out var bounds))
        {
            Reveal(hidden);
            return false;
        }

        EnsureRig();
        if (_camera == null || _target == null)
        {
            Reveal(hidden);
            return false;
        }

        Frame(bounds);

        var borrowed = new List<Material>();
        var restore = new List<(Renderer Renderer, Material[] Originals)>();

        Restage(subject);

        try
        {
            EnemyThumbnails.MakeUnlit(subject, borrowed, restore);
            _camera.Render();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: selection preview failed to render: " + e.Message);
            return false;
        }
        finally
        {
            foreach (var entry in restore)
                if (entry.Renderer != null) entry.Renderer.sharedMaterials = entry.Originals;

            foreach (var material in borrowed)
                if (material != null) Object.Destroy(material);

            Unstage();
            Reveal(hidden);
        }

        return true;
    }

    // ---- framing ---------------------------------------------------------------------------

    private static void Frame(Bounds bounds)
    {
        var rotation = SceneRefs.Cam != null ? SceneRefs.Cam.transform.rotation : Quaternion.identity;
        var inverse = Quaternion.Inverse(rotation);

        var centre = bounds.center;
        var extents = bounds.extents;

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (var i = 0; i < 8; i++)
        {
            var corner = centre + new Vector3(
                (i & 1) == 0 ? -extents.x : extents.x,
                (i & 2) == 0 ? -extents.y : extents.y,
                (i & 4) == 0 ? -extents.z : extents.z);

            var local = inverse * corner;
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }

        var extent = Mathf.Max((max.x - min.x) * 0.5f, (max.y - min.y) * 0.5f, 0.05f);
        _camera.orthographicSize = extent * Padding;

        var back = Mathf.Max(50f, max.z - min.z + 10f);
        _camera.transform.rotation = rotation;
        _camera.transform.position = rotation * ((min + max) * 0.5f) - rotation * Vector3.forward * back;
        _camera.farClipPlane = back * 2f + 100f;
    }

    // ---- glows -----------------------------------------------------------------------------

    private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");

    private static readonly string[] EffectShaders =
    [
        "additive", "godray", "glow", "dapple", "decalsprite", "particleshsbc", "lightleak"
    ];

    private static bool IsGlow(Renderer renderer)
    {
        if (renderer is ParticleSystemRenderer) return true;

        if (renderer.GetComponent<IStencilLighting>() != null) return true;

        var parent = renderer.transform.parent;
        if (parent != null && parent.GetComponent<IStencilLighting>() != null) return true;

        foreach (var component in renderer.GetComponents<Component>())
        {
            if (component == null) continue;

            var type = component.GetType();
            if (type.Namespace != null &&
                type.Namespace.StartsWith("BlendModes", System.StringComparison.Ordinal))
                return true;

            if (type.Name.EndsWith("_BlendModeExtension", System.StringComparison.Ordinal)) return true;
        }

        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null) continue;

            if (material.HasProperty(DstBlend) &&
                Mathf.Approximately(material.GetFloat(DstBlend), (float)UnityEngine.Rendering.BlendMode.One))
                return true;

            var shader = material.shader != null ? material.shader.name : "";
            foreach (var family in EffectShaders)
                if (shader.IndexOf(family, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }

    private static int VisibleLayers()
    {
        var cam = SceneRefs.Cam;
        return cam != null ? cam.cullingMask : ~0;
    }

    private static List<Renderer> HideGlows(GameObject subject, int visibleLayers)
    {
        List<Renderer> hidden = null;

        foreach (var renderer in subject.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer == null || !renderer.enabled) continue;

            var offCamera = (visibleLayers & (1 << renderer.gameObject.layer)) == 0;
            if (!offCamera && !IsGlow(renderer)) continue;

            renderer.enabled = false;
            (hidden ??= []).Add(renderer);
        }

        return hidden;
    }

    private static void Reveal(List<Renderer> hidden)
    {
        if (hidden == null) return;

        foreach (var renderer in hidden)
            if (renderer != null) renderer.enabled = true;
    }

    private static void EnsureRig()
    {
        if (_layer < 0) _layer = FindFreeLayer();

        if (_target == null)
        {
            _target = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32)
            {
                name = "CultTweaker_SelectionRT",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1
            };
        }

        if (_camera != null) return;

        var go = new GameObject("CultTweaker_SelectionCamera") { hideFlags = HideFlags.HideAndDontSave };

        _camera = go.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.targetTexture = _target;

        _camera.aspect = 1f;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
        _camera.useOcclusionCulling = false;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = 200f;

        _camera.enabled = false;

        if (_layer >= 0) _camera.cullingMask = 1 << _layer;
    }

    private static int FindFreeLayer() => OffscreenLayer.Value;

    private static void Restage(GameObject subject)
    {
        _restaged.Clear();
        _restagedLayers.Clear();
        if (_layer < 0) return;

        foreach (var child in subject.GetComponentsInChildren<Transform>(true))
        {
            if (child == null) continue;

            _restaged.Add(child);
            _restagedLayers.Add(child.gameObject.layer);
            child.gameObject.layer = _layer;
        }
    }

    private static void Unstage()
    {
        for (var i = 0; i < _restaged.Count; i++)
        {
            var child = _restaged[i];
            if (child != null) child.gameObject.layer = _restagedLayers[i];
        }

        _restaged.Clear();
        _restagedLayers.Clear();
    }

    private static bool TryGetBounds(GameObject subject, out Bounds bounds)
    {
        bounds = default;
        var any = false;

        foreach (var renderer in subject.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer == null || !renderer.enabled) continue;
            if (renderer.bounds.size.sqrMagnitude <= 0.0001f) continue;

            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }

        return any;
    }
}
