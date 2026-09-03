using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.ModUI;

public static class PlayerPreview
{
    private const float Supersample = 2f;

    private static readonly Vector3 Stage = new(12000f, -12000f, 0f);
    private const float StageSpacing = 60f;

    private sealed class Portrait
    {
        public SkeletonAnimation Spine;
        public RenderTexture Texture;
        public SkeletonDataAsset Asset;
        public Vector3 Position;
        public float Size = 1f;
        public bool Untinted;
    }

    private static readonly Dictionary<int, Portrait> _portraits = [];

    private static GameObject _root;
    private static Camera _camera;
    private static int _layer = -1;

    public static RenderTexture Of(int playerId) =>
        _portraits.TryGetValue(playerId, out var portrait) && portrait.Texture != null
            ? portrait.Texture
            : null;

    // ---- keeping a portrait in step ----------------------------------------------------------------

    public static bool Sync(int playerId, float width, float height)
    {
        var player = SpineLoaderHelper.PlayerSpineLoader.ResolvePlayer(playerId);
        var live = player != null ? player.Spine : null;
        if (live == null || live.skeletonDataAsset == null || live.Skeleton == null) return false;

        try
        {
            if (!EnsureRig()) return false;

            var portrait = Build(playerId, live, width, height);
            if (portrait == null) return false;

            Dress(portrait, live);
            Frame(portrait);
            Render(portrait);
            return true;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"CultTweaker: could not draw player {playerId + 1}: {e.Message}");
            return false;
        }
    }

    public static void Tick()
    {
        if (_camera == null) return;

        var delta = Time.unscaledDeltaTime;

        foreach (var portrait in _portraits.Values)
        {
            if (portrait.Spine == null || portrait.Texture == null) continue;

            portrait.Spine.Update(delta);
            portrait.Spine.LateUpdate();

            if (!portrait.Untinted) portrait.Untinted = Untint(portrait.Spine);

            Render(portrait);
        }
    }

    // ---- the animation picker this exists to make possible --------------------------------------------

    public static List<string> Animations(int playerId)
    {
        var names = new List<string>();
        if (!_portraits.TryGetValue(playerId, out var portrait) || portrait.Spine == null) return names;

        var data = portrait.Spine.Skeleton?.Data;
        if (data == null) return names;

        foreach (var animation in data.Animations) names.Add(animation.Name);
        names.Sort(System.StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static void Redress(int playerId)
    {
        if (!_portraits.TryGetValue(playerId, out var portrait) || portrait.Spine == null) return;

        var player = SpineLoaderHelper.PlayerSpineLoader.ResolvePlayer(playerId);
        var live = player != null ? player.Spine : null;
        if (live == null || live.Skeleton == null) return;

        try
        {
            Dress(portrait, live);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"CultTweaker: player {playerId + 1} would not re-dress: {e.Message}");
        }
    }

    public static string CurrentAnimation(int playerId)
    {
        if (!_portraits.TryGetValue(playerId, out var portrait) || portrait.Spine == null) return null;

        try
        {
            return portrait.Spine.AnimationState?.GetCurrent(0)?.Animation?.Name;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    public static void Play(int playerId, string animation)
    {
        if (!_portraits.TryGetValue(playerId, out var portrait) || portrait.Spine == null) return;
        if (string.IsNullOrEmpty(animation)) return;

        try
        {
            portrait.Spine.AnimationState.SetAnimation(0, animation, true);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"CultTweaker: '{animation}' would not play: {e.Message}");
        }
    }

    // ---- building --------------------------------------------------------------------------------------

    private static Portrait Build(int playerId, SkeletonAnimation live, float width, float height)
    {
        if (_portraits.TryGetValue(playerId, out var existing))
        {
            if (existing.Spine != null && existing.Asset == live.skeletonDataAsset) return existing;

            Discard(existing);
            _portraits.Remove(playerId);
        }

        var spine = SkeletonAnimation.NewSkeletonAnimationGameObject(live.skeletonDataAsset);
        if (spine == null) return null;

        spine.gameObject.name = $"CultTweakerPortrait{playerId + 1}";
        spine.transform.SetParent(_root.transform, false);

        var position = Stage + new Vector3(playerId * StageSpacing, 0f, 0f);
        spine.transform.position = position;

        SetLayer(spine.gameObject, _layer);
        spine.Initialize(true);

        spine.timeScale = 1f;
        spine.enabled = false;

        var portrait = new Portrait
        {
            Spine = spine,
            Asset = live.skeletonDataAsset,
            Position = position,
            Texture = new RenderTexture(
                Mathf.Max(32, Mathf.RoundToInt(width * Supersample)),
                Mathf.Max(32, Mathf.RoundToInt(height * Supersample)),
                16, RenderTextureFormat.ARGB32)
            {
                name = $"CultTweakerPortraitTarget{playerId + 1}",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            }
        };

        _portraits[playerId] = portrait;
        return portrait;
    }

    private static readonly string[] WorldKeywords =
        ["_USEFADEINWOODSCOLOR", "_RECEIVESHADOW", "_USEEMISSION_ON"];

    private static readonly string[] WorldToggles =
        ["_UseFadeInWoodsColor", "_ReceiveShadow", "_UseEmission"];

    internal static bool Untint(SkeletonAnimation spine)
    {
        var renderer = spine != null ? spine.GetComponent<MeshRenderer>() : null;
        if (renderer == null) return false;

        var stock = SpineLoaderHelper.SpineFolderLoader.SpineShader();
        if (stock == null) return true;

        var any = false;

        foreach (var source in renderer.sharedMaterials)
        {
            if (source == null) continue;

            any = true;
            if (spine.CustomMaterialOverride.ContainsKey(source)) continue;

            var plain = new Material(source)
            {
                name = source.name + " (portrait)",
                hideFlags = HideFlags.HideAndDontSave
            };

            var fromSpineShader = source.shader != null && source.shader.name.StartsWith("Spine/");

            if (fromSpineShader)
            {
                plain.shader = stock;
                plain.mainTexture = source.mainTexture;
            }
            else
            {
                foreach (var keyword in WorldKeywords) plain.DisableKeyword(keyword);
                foreach (var toggle in WorldToggles)
                    if (plain.HasProperty(toggle)) plain.SetFloat(toggle, 0f);
            }

            plain.SetColor(TimeOfDayColor, Color.white);
            plain.SetColor(GlobalHCol, Color.white);
            plain.SetFloat(GlobalExposure, 1f);

            if (plain.HasProperty("_EmissionMap"))
                plain.SetTextureScale("_EmissionMap", Vector2.zero);

            spine.CustomMaterialOverride[source] = plain;
        }

        return any;
    }

    private static void Dress(Portrait portrait, SkeletonAnimation live)
    {
        var skeleton = portrait.Spine.Skeleton;

        if (live.Skeleton.Skin != null)
        {
            skeleton.SetSkin((Spine.Skin)null);
            skeleton.SetSkin(live.Skeleton.Skin);
        }

        skeleton.SetSlotsToSetupPose();

        skeleton.ScaleX = Mathf.Abs(skeleton.ScaleX) * (live.Skeleton.ScaleX < 0f ? -1f : 1f);

        if (portrait.Spine.AnimationState.GetCurrent(0) == null)
        {
            var wanted = First(portrait,
                              "idle-front", "idle-down", "idle-side", "idle", "Idle",
                              "idle-back", "idle-up")
                          ?? Current(live);

            if (!string.IsNullOrEmpty(wanted))
            {
                Play(portrait, wanted);
                Plugin.Log.LogInfo($"CultTweaker: player portrait is playing '{wanted}'.");
            }
            else
            {
                Plugin.Log.LogWarning("CultTweaker: the portrait spine has no animation to play.");
            }
        }

        portrait.Spine.Update(0f);
        portrait.Spine.LateUpdate();
    }

    private static void Play(Portrait portrait, string animation)
    {
        try
        {
            portrait.Spine.AnimationState.SetAnimation(0, animation, true);
        }
        catch (System.Exception)
        {
        }
    }

    private static string Current(SkeletonAnimation live)
    {
        try
        {
            return live.AnimationState?.GetCurrent(0)?.Animation?.Name;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static string First(Portrait portrait, params string[] candidates)
    {
        var data = portrait.Spine.Skeleton?.Data;
        if (data == null) return null;

        foreach (var name in candidates)
            if (data.FindAnimation(name) != null) return name;

        foreach (var animation in data.Animations)
            if (animation.Name.IndexOf("idle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return animation.Name;

        return null;
    }

    private static void Frame(Portrait portrait)
    {
        try
        {
            var buffer = new float[8];
            portrait.Spine.Skeleton.GetBounds(out _, out _, out var wide, out var tall, ref buffer);

            var extent = Mathf.Max(wide, tall) * 0.5f;
            portrait.Size = extent > 0.001f ? extent * 1.25f : 1f;
        }
        catch (System.Exception)
        {
            portrait.Size = 1f;
        }
    }

    private static readonly int TimeOfDayColor = Shader.PropertyToID("_TimeOfDayColor");
    private static readonly int GlobalHCol = Shader.PropertyToID("_GlobalHCol");
    private static readonly int GlobalSCol = Shader.PropertyToID("_GlobalSCol");
    private static readonly int GlobalExposure = Shader.PropertyToID("_GlobalExposure");

    private static void Render(Portrait portrait)
    {
        if (_camera == null || portrait.Spine == null || portrait.Texture == null) return;

        _camera.transform.position = portrait.Position + new Vector3(0f, portrait.Size * 0.75f, -100f);
        _camera.orthographicSize = portrait.Size;
        RenderNeutral(_camera, portrait.Texture);
    }

    /// <summary>
    /// Renders one frame with the world's time-of-day tint and exposure switched off, so an offscreen
    /// portrait looks the same at midnight as at noon. Shared with the UI skeleton mirrors.
    /// </summary>
    internal static void RenderNeutral(Camera camera, RenderTexture target)
    {
        if (camera == null || target == null) return;

        camera.targetTexture = target;

        var timeOfDay = Shader.GetGlobalColor(TimeOfDayColor);
        var highlight = Shader.GetGlobalColor(GlobalHCol);
        var shadow = Shader.GetGlobalColor(GlobalSCol);
        var exposure = Shader.GetGlobalFloat(GlobalExposure);

        Shader.SetGlobalColor(TimeOfDayColor, Color.white);
        Shader.SetGlobalColor(GlobalHCol, Color.white);
        Shader.SetGlobalFloat(GlobalExposure, 1f);

        try
        {
            camera.Render();
        }
        finally
        {
            Shader.SetGlobalColor(TimeOfDayColor, timeOfDay);
            Shader.SetGlobalColor(GlobalHCol, highlight);
            Shader.SetGlobalColor(GlobalSCol, shadow);
            Shader.SetGlobalFloat(GlobalExposure, exposure);
            camera.targetTexture = null;
        }
    }

    // ---- the rig -----------------------------------------------------------------------------------------

    private static bool EnsureRig()
    {
        if (_camera != null && _root != null) return true;

        if (_layer < 0) _layer = FreeLayer();

        if (_root == null)
        {
            _root = new GameObject("CultTweakerPortraits") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(_root);
        }

        if (_camera == null)
        {
            var go = new GameObject("CultTweakerPortraitCamera");
            go.transform.SetParent(_root.transform, false);

            _camera = go.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);

            _camera.cullingMask = 1 << _layer;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 500f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;

            _camera.enabled = false;
        }

        return true;
    }

    private static int FreeLayer() => MapEditor.OffscreenLayer.Value;

    private static void SetLayer(GameObject go, int layer)
    {
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            if (child != null) child.gameObject.layer = layer;
    }

    // ---- teardown -----------------------------------------------------------------------------------------

    public static void Release()
    {
        foreach (var portrait in _portraits.Values) Discard(portrait);
        _portraits.Clear();

        if (_root != null) Object.Destroy(_root);
        _root = null;
        _camera = null;
    }

    private static void Discard(Portrait portrait)
    {
        if (portrait == null) return;

        if (portrait.Spine != null) Object.Destroy(portrait.Spine.gameObject);

        if (portrait.Texture != null)
        {
            portrait.Texture.Release();
            Object.DestroyImmediate(portrait.Texture);
        }

        portrait.Spine = null;
        portrait.Texture = null;
    }
}
