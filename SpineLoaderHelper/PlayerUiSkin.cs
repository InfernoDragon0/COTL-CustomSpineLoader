using System;
using System.Collections.Generic;
using System.Linq;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.SpineLoaderHelper;

/// <summary>
/// Puts the player's custom spine onto the game's UI skeletons: the inventory lamb, the Knucklebones
/// and Flockade avatars, and the Flockade result card. Those are SkeletonGraphics, and this game's
/// spine-unity is old enough that a SkeletonGraphic draws with exactly one texture (there is no
/// allowMultipleCanvasRenderers) - a custom spine's several atlas pages cannot go through it. So the
/// graphic is given the custom data and the player's live skin and keeps running the game's own
/// animation calls, but its drawing is hidden; a <see cref="PlayerUiMirror"/> renders that same
/// skeleton offscreen the way the F7 portrait does and lays the result over the graphic. No repack,
/// no first-open pause, and the exact rendering the world and the F7 panel already have.
/// </summary>
public static class PlayerUiSkin
{
    /// <summary>
    /// Dresses <paramref name="graphic"/> as <paramref name="player"/> if that player is wearing a
    /// custom spine, and takes the mirror off again if they no longer are. <paramref name="animations"/>
    /// are the names the screen will ask the skeleton to play; the swap is skipped when the custom spine
    /// lacks any, since an unknown name throws inside the game's own code.
    /// </summary>
    public static bool Apply(SkeletonGraphic graphic, PlayerFarming player, IEnumerable<string> animations, string where)
    {
        if (graphic == null) return false;
        var mirror = graphic.GetComponent<PlayerUiMirror>();

        if (player == null)
        {
            mirror?.Detach();
            return false;
        }

        var playerId = CoopManager.CoopActive ? Mathf.Clamp(player.playerID, 0, 3) : 0;
        if (string.IsNullOrEmpty(PlayerSpineLoader.ActiveSpineKey(playerId)))
        {
            mirror?.Detach();
            return false;
        }

        var live = player.Spine;
        var asset = live != null ? live.skeletonDataAsset : null;
        var liveSkin = live != null && live.Skeleton != null ? live.Skeleton.Skin : null;
        if (asset == null || liveSkin == null) return false;

        var data = asset.GetSkeletonData(true);
        if (data == null) return false;

        var wanted = new List<string>(animations ?? []);
        if (!string.IsNullOrEmpty(graphic.startingAnimation)) wanted.Add(graphic.startingAnimation);
        var missing = wanted.Where(a => !string.IsNullOrEmpty(a) && data.FindAnimation(a) == null).Distinct().ToList();
        if (missing.Count > 0)
        {
            Plugin.Log.LogWarning($"Player spine '{asset.name}' stays off the {where} skeleton: it has no " +
                                  $"'{string.Join("', '", missing)}' animation.");
            return false;
        }

        try
        {
            var original = graphic.skeletonDataAsset;

            if (graphic.skeletonDataAsset != asset || graphic.Skeleton == null || graphic.Skeleton.Data != data)
            {
                // Skeleton.SetSkin(string) throws on a name the data lacks, and Initialize plays the
                // prefab's initialSkinName before anything else gets a say.
                if (!string.IsNullOrEmpty(graphic.initialSkinName) && data.FindSkin(graphic.initialSkinName) == null)
                    graphic.initialSkinName = "";

                // The prefab's RectTransform is sized for the asset it shipped with. If the custom spine
                // was read at a different scale, the graphic is scaled by the ratio so the lamb stays the
                // size the screen laid out for. Swapping again later reads the previous custom scale, so
                // the ratios chain rather than stack.
                var before = original != null ? original.scale : asset.scale;

                graphic.skeletonDataAsset = asset;
                graphic.Initialize(true);

                if (asset.scale > 0f && !Mathf.Approximately(before, asset.scale))
                    graphic.transform.localScale *= before / asset.scale;
            }

            // The live skin itself, not a copy: it is what carries fleece cycling and hidden slots, and
            // it belongs to the same SkeletonData the graphic now runs on.
            var skeleton = graphic.Skeleton;
            skeleton.SetSkin((Skin)null);
            skeleton.SetSkin(liveSkin);
            skeleton.SetSlotsToSetupPose();
            graphic.Update(0f);

            mirror ??= graphic.gameObject.AddComponent<PlayerUiMirror>();
            mirror.Attach(graphic, asset, original, PlayerSpineLoader.ActiveSpineName(playerId), where);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Player spine could not be put on the {where} skeleton: {e.Message}");
            return false;
        }
    }
}

/// <summary>
/// Draws a SkeletonGraphic's skeleton the way the world does. The game keeps updating the graphic -
/// its AnimationState, its Skeleton, its world transforms - and this component only reads the result:
/// each LateUpdate an offscreen SkeletonAnimation that SHARES the graphic's Skeleton object rebuilds
/// its mesh from it (multi-material, original atlas pages) and a camera renders that into a
/// RenderTexture shown by a RawImage child of the graphic. The graphic's own canvas drawing is kept
/// at alpha zero underneath. The RawImage sits in the graphic's local space at the skeleton's bounds,
/// which is where the graphic itself would have drawn.
/// </summary>
internal sealed class PlayerUiMirror : MonoBehaviour
{
    private const float Supersample = 1.5f;
    private const int MaxSide = 2048;

    // Idle bounds, padded this much of the larger side on every edge: win and lose animations jump
    // and lean well outside the idle silhouette, and the texture must not crop them.
    private const float Margin = 0.6f;

    private static readonly Vector3 Stage = new(12000f, -11000f, 0f);
    private const float Spacing = 80f;
    private static int _next;

    private SkeletonGraphic _graphic;
    private SkeletonDataAsset _original;
    private GameObject _root;
    private SkeletonAnimation _spine;
    private Camera _camera;
    private RenderTexture _target;
    private RawImage _image;
    private Rect _region;
    private float _ppu = 100f;
    private Vector3 _position;
    private bool _untinted;
    private string _where;
    private string _name;
    private float _settleUntil;

    public void Attach(SkeletonGraphic graphic, SkeletonDataAsset asset, SkeletonDataAsset original, string name, string where)
    {
        _graphic = graphic;
        _where = where;
        _name = name;
        _original ??= original != asset ? original : null;

        if (_root == null) BuildRig();

        if (_spine == null || _spine.skeletonDataAsset != asset)
        {
            if (_spine != null) Destroy(_spine.gameObject);
            _spine = SkeletonAnimation.NewSkeletonAnimationGameObject(asset);
            _spine.gameObject.name = "Mirror";
            _spine.transform.SetParent(_root.transform, false);
            _spine.transform.position = _position;
            foreach (var child in _spine.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = MapEditor.OffscreenLayer.Value;
            _spine.Initialize(true);
            _spine.enabled = false;
            _untinted = false;
        }

        _spine.skeleton = graphic.Skeleton;

        Frame();
        EnsureImage();
        EnsureTarget();

        graphic.canvasRenderer.SetAlpha(0f);
        _settleUntil = Time.unscaledTime + 2f;
        Draw();

        var filter = _spine.GetComponent<MeshFilter>();
        var renderer = _spine.GetComponent<MeshRenderer>();
        var canvas = graphic.canvas;
        Plugin.Log.LogInfo($"Player spine '{_name}' mirrored onto the {_where} skeleton: region " +
                           $"{_region.width:0.##}x{_region.height:0.##} units at {_region.center} x{_ppu} ppu, texture {_target.width}x{_target.height}; " +
                           $"mesh {(filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertexCount : -1)} verts, " +
                           $"{(renderer != null ? renderer.sharedMaterials.Length : -1)} material(s); " +
                           $"asset scale {(original != null ? original.scale : -1f)}->{asset.scale}, " +
                           $"graphic localScale {graphic.transform.localScale}, lossy {graphic.transform.lossyScale}, " +
                           $"rect {graphic.rectTransform.rect.size} pivot {graphic.rectTransform.pivot}; " +
                           $"canvas {(canvas != null ? canvas.rootCanvas.renderMode.ToString() : "none")} " +
                           $"factor {(canvas != null ? canvas.rootCanvas.scaleFactor : 0f)} " +
                           $"lossy {(canvas != null ? canvas.rootCanvas.transform.lossyScale : Vector3.zero)}; " +
                           $"active {graphic.IsActive()}.");
    }

    /// <summary>Puts the graphic back the way the prefab had it. Used when the player is no longer
    /// wearing a custom spine by the time a screen that outlived the last one is set up again.</summary>
    public void Detach()
    {
        if (_graphic != null)
        {
            _graphic.canvasRenderer.SetAlpha(1f);
            if (_original != null && _graphic.skeletonDataAsset != _original)
            {
                _graphic.skeletonDataAsset = _original;
                _graphic.Initialize(true);
            }
        }

        Destroy(this);
    }

    private void BuildRig()
    {
        _position = Stage + new Vector3(_next++ * Spacing, 0f, 0f);

        _root = new GameObject("CultTweakerUiMirror") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(_root);

        var cameraGO = new GameObject("Camera");
        cameraGO.transform.SetParent(_root.transform, false);
        _camera = cameraGO.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.cullingMask = 1 << MapEditor.OffscreenLayer.Value;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = 500f;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
        _camera.enabled = false;
    }

    /// <summary>
    /// The skeleton's setup-pose bounds, in skeleton units, padded. A SkeletonGraphic draws those
    /// units multiplied by its canvas's referencePixelsPerUnit (MeshGenerator.ScaleVertexData in
    /// UpdateMesh), so the RawImage is placed at region x ppu while the camera frames the region itself.
    /// </summary>
    private void Frame()
    {
        var canvas = _graphic.canvas;
        _ppu = canvas != null && canvas.referencePixelsPerUnit > 0f ? canvas.referencePixelsPerUnit : 100f;

        float x = -0.5f, y = 0f, w = 1f, h = 1f;
        try
        {
            var buffer = new float[8];
            _graphic.Skeleton.GetBounds(out var bx, out var by, out var bw, out var bh, ref buffer);
            if (bw > 0.001f && bh > 0.001f)
            {
                x = bx;
                y = by;
                w = bw;
                h = bh;
            }
        }
        catch (Exception)
        {
        }

        var pad = Mathf.Max(w, h) * Margin;
        _region = new Rect(x - pad, y - pad, w + 2f * pad, h + 2f * pad);
    }

    private void EnsureImage()
    {
        if (_image == null)
        {
            var go = new GameObject("CultTweakerMirror");
            go.transform.SetParent(_graphic.rectTransform, false);
            go.AddComponent<RectTransform>();
            _image = go.AddComponent<RawImage>();
            _image.raycastTarget = false;
        }

        // Anchored to the parent's pivot, which is where the graphic draws its skeleton's origin, so
        // the child's anchored position is measured in the graphic's own local units.
        var parent = _graphic.rectTransform;
        var rect = _image.rectTransform;
        rect.anchorMin = rect.anchorMax = parent.pivot;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = _region.center * _ppu;
        rect.sizeDelta = _region.size * _ppu;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.SetAsLastSibling();
    }

    /// <summary>
    /// Pixels the RawImage covers on screen. Measured through the scale chain rather than world
    /// corners so a canvas of any render mode gives the same answer; the ancestors' scale is part of
    /// it, which is why Attach measures again for a while - the screens are set up mid show-tween,
    /// scaled near zero.
    /// </summary>
    private Vector2Int Measure()
    {
        var canvas = _graphic.canvas != null ? _graphic.canvas.rootCanvas : null;
        var canvasScale = canvas != null ? canvas.transform.lossyScale : Vector3.one;
        var factor = canvas != null ? canvas.scaleFactor : 1f;
        var lossy = _graphic.rectTransform.lossyScale;

        var perUnitX = canvasScale.x > 0.0001f ? Mathf.Abs(lossy.x) / canvasScale.x * factor : 0f;
        var perUnitY = canvasScale.y > 0.0001f ? Mathf.Abs(lossy.y) / canvasScale.y * factor : 0f;

        return new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt(_region.width * _ppu * perUnitX * Supersample), 64, MaxSide),
            Mathf.Clamp(Mathf.RoundToInt(_region.height * _ppu * perUnitY * Supersample), 64, MaxSide));
    }

    private void EnsureTarget()
    {
        var size = Measure();
        if (_target != null && _target.width == size.x && _target.height == size.y) return;
        if (_target != null && size.x <= _target.width * 1.3f && size.y <= _target.height * 1.3f) return;

        if (_target != null)
        {
            _target.Release();
            DestroyImmediate(_target);
        }

        _target = new RenderTexture(size.x, size.y, 16, RenderTextureFormat.ARGB32)
        {
            name = "CultTweakerMirrorTarget",
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        _image.texture = _target;
    }

    private void LateUpdate()
    {
        if (_graphic == null || _spine == null || _target == null) return;
        if (!_graphic.IsActive()) return;

        // Initialize(true) on the graphic hands it a new Skeleton; follow it.
        var skeleton = _graphic.Skeleton;
        if (skeleton == null) return;
        if (_spine.skeleton != skeleton) _spine.skeleton = skeleton;

        _graphic.canvasRenderer.SetAlpha(0f);
        if (Time.unscaledTime < _settleUntil) EnsureTarget();
        Draw();
    }

    private void Draw()
    {
        _spine.LateUpdate();
        if (!_untinted) _untinted = ModUI.PlayerPreview.Untint(_spine);

        _camera.transform.position = _position + new Vector3(_region.center.x, _region.center.y, -100f);
        _camera.orthographicSize = _region.height * 0.5f;
        ModUI.PlayerPreview.RenderNeutral(_camera, _target);
    }

    private void OnDestroy()
    {
        if (_image != null) Destroy(_image.gameObject);
        if (_target != null)
        {
            _target.Release();
            DestroyImmediate(_target);
        }
        if (_root != null) Destroy(_root);

        _image = null;
        _target = null;
        _root = null;
        _spine = null;
        _camera = null;
    }
}
