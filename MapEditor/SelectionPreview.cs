using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// A portrait of whatever the select tool is holding, photographed where it stands.
//
// Deliberately not the thumbnail rig (EnemyThumbnails): that one stages a *prefab* on a spare
// layer and reads the pixels back into an atlas, which is right for a catalogue of hundreds and
// wrong for one live object that must not be moved. This points a disabled camera at the object in
// place, renders once into a RenderTexture, and hands that texture straight to a RawImage - no
// readback, no atlas, and nothing at all per frame. The cost is one camera render per change of
// selection.
public static class SelectionPreview
{
    private const int Size = 256;

    // Room to breathe around the subject, as a fraction of its largest side.
    private const float Padding = 1.25f;

    private static Camera _camera;
    private static RenderTexture _target;
    private static int _layer = -1;

    private static readonly List<Transform> _restaged = [];
    private static readonly List<int> _restagedLayers = [];

    public static RenderTexture Texture => _target;

    // Renders the object into the shared texture. False when there is nothing to photograph, in
    // which case the caller should hide its box rather than show a stale picture.
    public static bool Render(GameObject subject)
    {
        if (subject == null) return false;

        // Glows come out first, and before the framing is measured: they are usually far bigger
        // than the thing they light, so leaving them in shrinks the subject to fit a halo.
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

        // Layer isolation is what keeps the neighbours out of the portrait, and the unlit swap is
        // what keeps the room's lighting out of it - a dungeon is dark, and a portrait lit by the
        // room it stands in is a black square. Both are the thumbnail rig's tricks; the only
        // difference is that this subject is alive, so both are undone in the same call. Nothing
        // else ever observes either change: no physics step, no cull, no frame.
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

    // The room's camera is **pitched**, and that is what was squashing the portrait long after the
    // plate and the RawImage had both been made square. `CameraFollowTarget` settles the game
    // camera on `Euler(-45, 0, 0)` and the room's art stands up to meet it (`BillboardFacingCamera`
    // and friends) - so a sprite that looks upright on screen is a quad tilted 45 degrees in the
    // world. A camera looking straight down -Z at that quad sees it edge-on by 45 degrees and draws
    // it at cos(45) - about 71% - of its height. Nothing about the UI was stretched; the picture was
    // taken from the wrong angle.
    //
    // So the portrait is not a different projection of the room, it is the same view from closer:
    // it borrows the room camera's rotation, and measures the subject in *that* camera's space,
    // because with a pitched camera world height and screen height are not the same quantity.
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

        // Far enough back to clear the subject's own depth, which a pitched view spreads out.
        var back = Mathf.Max(50f, max.z - min.z + 10f);
        _camera.transform.rotation = rotation;
        _camera.transform.position = rotation * ((min + max) * 0.5f) - rotation * Vector3.forward * back;
        _camera.farClipPlane = back * 2f + 100f;
    }

    // ---- glows -----------------------------------------------------------------------------

    private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");

    // Shader families the game blends by hand, from its *_BlendModeExtension classes.
    private static readonly string[] EffectShaders =
    [
        "additive", "godray", "glow", "dapple", "decalsprite", "particleshsbc", "lightleak"
    ];

    // A light is a mask of *brightness*, not a picture: the texture is a soft white blob and what
    // it covers gets lighter. Drawn as an ordinary alpha-blended sprite - which is what the unlit
    // swap makes of it, since Sprites/Default is alpha-blended - it becomes a solid disc sitting
    // over the subject. So lights are left out of the portrait rather than drawn wrongly: a picture
    // of a prop does not need its halo.
    //
    // Finding them by blend factor does not work here, and that is worth writing down. The game
    // blends through the **BlendModes** package: its extensions (DappleLighting, Godrays,
    // DecalSprite) declare `_SrcBlend = One, _DstBlend = Zero` - which reads as *opaque* - and do
    // the real blending themselves. So the factors say "solid" for exactly the things that are not,
    // which is also why they came out as solid discs. The reliable signal is the package's own
    // components, with the shader families it names as a second net.
    private static bool IsGlow(Renderer renderer)
    {
        // Godrays and the like are particle systems, and a particle system frozen at whatever the
        // room left it on is not a portrait of anything.
        if (renderer is ParticleSystemRenderer) return true;

        // The game's stencil lighting: an IStencilLighting component owns a *child* mesh renderer
        // holding the light's decal quad (StencilLighting_DecalSprite.Init picks it up with
        // GetComponentInChildren<MeshRenderer>). The quad is a brightness mask drawn against a
        // stencil buffer nothing fills in a one-camera render, so on its own it is a flat slab.
        // Checking the parent as well as the renderer itself is what finds it, since the component
        // sits on the holder rather than on the renderer.
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

            // Still worth asking: a genuinely additive material is one that adds to what is behind
            // it, and those exist outside the package too.
            if (material.HasProperty(DstBlend) &&
                Mathf.Approximately(material.GetFloat(DstBlend), (float)UnityEngine.Rendering.BlendMode.One))
                return true;

            var shader = material.shader != null ? material.shader.name : "";
            foreach (var family in EffectShaders)
                if (shader.IndexOf(family, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }

    // Which layers the room's own camera draws. Anything outside that mask is machinery the player
    // never sees, and the portrait should not see it either.
    //
    // This is the rule that was missing, and it is the one that matters most. Every lit sprite in
    // the game grows a hidden twin: StencilLighting_ExcludeSprite.Start builds a child called
    // "ExclusionRenderer" carrying a copy of the same sprite on the "Lighting_NoRender" layer, kept
    // out of the frame by the main camera's culling mask alone. Restage sweeps *every* child onto
    // the portrait layer, which promoted that twin into view - a second copy of the subject wearing
    // the exclusion material, drawn over the real one. That is the smear that survived hiding the
    // glows: it is not a light on top of the subject, it is the subject again.
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

        // HideAndDontSave already carries it across scene loads, the way the thumbnail rig's
        // camera does; DontDestroyOnLoad on top of it only earns a warning.
        var go = new GameObject("CultTweaker_SelectionCamera") { hideFlags = HideFlags.HideAndDontSave };

        _camera = go.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.targetTexture = _target;

        // Explicit, and after the target texture: a fresh camera starts on the screen's aspect,
        // which squeezes the subject horizontally into a square render texture.
        _camera.aspect = 1f;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
        _camera.useOcclusionCulling = false;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = 200f;

        // Rendered by hand, never as part of the frame.
        _camera.enabled = false;

        if (_layer >= 0) _camera.cullingMask = 1 << _layer;
    }

    private static int FindFreeLayer()
    {
        // Downwards: the high layers are the ones projects leave unnamed. Without one the portrait
        // still works, it just has the room in the background.
        for (var i = 31; i >= 8; i--)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;

        return -1;
    }

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
