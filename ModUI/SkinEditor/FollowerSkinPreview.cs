using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.Helpers;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.SpineLoaderHelper;
using Spine.Unity;
using Spine.Unity.AttachmentTools;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI.SkinEditor;

public class FollowerSkinPreview
{
    private const float Supersample = 2f;
    private static readonly Vector3 Stage = new(12000f, -13000f, 0f);

    private GameObject _root;
    private Camera _camera;
    private SkeletonAnimation _spine;
    private RenderTexture _target;
    private RawImage _image;
    private float _size = 1f;

    private readonly Dictionary<string, (DateTime stamp, Texture2D texture)> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpineAtlasAsset> _atlases = [];

    public bool Ready => _spine != null && _camera != null;

    public bool Build(RectTransform panel)
    {
        if (Ready)
        {
            AttachImage(panel);
            return true;
        }

        var asset = WorshipperData.Instance != null && WorshipperData.Instance.SkeletonData != null
            ? WorshipperData.Instance.SkeletonData.skeletonDataAsset
            : null;
        if (asset == null)
        {
            Plugin.Log.LogWarning("SkinEditor: the follower skeleton is not available here.");
            return false;
        }

        var layer = OffscreenLayer.Value;

        _root = new GameObject("SkinEditor_PreviewRig") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(_root);

        var cameraGO = new GameObject("Camera");
        cameraGO.transform.SetParent(_root.transform, false);
        _camera = cameraGO.AddComponent<Camera>();
        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.cullingMask = 1 << layer;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = 500f;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
        _camera.enabled = false;

        _spine = SkeletonAnimation.NewSkeletonAnimationGameObject(asset);
        if (_spine == null)
        {
            Release();
            return false;
        }

        _spine.gameObject.name = "Follower";
        _spine.transform.SetParent(_root.transform, false);
        _spine.transform.position = Stage;
        foreach (var child in _spine.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        _spine.Initialize(true);
        _spine.timeScale = 1f;
        _spine.UseDeltaTime = false;
        _spine.ForceVisible = true;
        _spine.enabled = false;

        var rect = panel.rect;
        _target = new RenderTexture(
            Mathf.Max(32, Mathf.RoundToInt(rect.width * Supersample)),
            Mathf.Max(32, Mathf.RoundToInt(rect.height * Supersample)),
            16, RenderTextureFormat.ARGB32)
        {
            name = "SkinEditor_PreviewTarget",
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        AttachImage(panel);
        return true;
    }

    private void AttachImage(RectTransform panel)
    {
        if (panel == null || _target == null) return;

        var imageGO = new GameObject("Preview");
        imageGO.transform.SetParent(panel, false);
        var imageRect = imageGO.AddComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = new Vector2(8f, 8f);
        imageRect.offsetMax = new Vector2(-8f, -8f);
        _image = imageGO.AddComponent<RawImage>();
        _image.texture = _target;
        _image.raycastTarget = false;
    }

    public bool HasPacked(CTFollowerSkinDocument document, bool mayUseRegistered)
    {
        if (document?.Config == null) return false;

        var signature = Signature(document);
        if (signature == _worn || Find(signature) != null) return true;

        return mayUseRegistered && Registered(document) != null;
    }

    public void Sleep() => _image = null;

    public List<string> Animations()
    {
        var names = new List<string>();
        var data = _spine != null ? _spine.Skeleton?.Data : null;
        if (data == null) return names;
        foreach (var animation in data.Animations) names.Add(animation.Name);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public string DefaultAnimation()
    {
        var data = _spine != null ? _spine.Skeleton?.Data : null;
        if (data == null) return null;
        foreach (var name in new[] { "idle", "Idle", "idle-front", "idle-down" })
            if (data.FindAnimation(name) != null) return name;
        foreach (var animation in data.Animations)
            if (animation.Name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0) return animation.Name;
        return data.Animations.Count > 0 ? data.Animations.Items[0].Name : null;
    }

    public void Play(string animation, bool loop, float speed)
    {
        if (_spine == null) return;
        _spine.timeScale = speed <= 0f ? 1f : speed;
        if (string.IsNullOrEmpty(animation)) return;

        try
        {
            _wanted = animation;
            _wantedLoop = loop;
            var current = _spine.AnimationState.GetCurrent(0);
            if (current?.Animation?.Name == animation && current.Loop == loop) return;
            _spine.AnimationState.SetAnimation(0, animation, loop);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"SkinEditor: '{animation}' would not play: {e.Message}");
        }
    }

    private sealed class Packed
    {
        public string Signature;
        public Spine.Skin Skin;
        public Material Material;
        public Texture2D Texture;
    }

    private const int PackedLimit = 4;

    private readonly List<Packed> _packed = [];
    private string _worn;

    public string Show(CTFollowerSkinDocument document, int colourSet, bool mayUseRegistered = false)
    {
        if (_spine == null || document?.Config == null) return "The preview rig is not running.";

        var signature = Signature(document);

        if (signature == _worn)
        {
            Recolour(document, colourSet);
            return null;
        }

        var skin = mayUseRegistered ? Registered(document) : null;
        WearingRegistered = skin != null;

        if (skin == null)
        {
            var cached = Find(signature) ?? Pack(document, signature);
            if (cached == null) return "The skin could not be built; the log says why.";

            _packed.Remove(cached);
            _packed.Add(cached);
            Trim();

            skin = cached.Skin;
        }

        Wear(skin, document, colourSet, signature);
        return null;
    }

    private void Wear(Spine.Skin skin, CTFollowerSkinDocument document, int colourSet, string signature)
    {
        var skeleton = _spine.Skeleton;
        skeleton.SetSkin((Spine.Skin)null);
        skeleton.SetSkin(skin);
        skeleton.SetSlotsToSetupPose();
        _worn = signature;

        FollowerSpineLoader.ApplyColours(skeleton, document.Config, colourSet);

        _spine.Update(0f, true);
        _spine.LateUpdate();

        if (Flatten())
        {
            _spine.Update(0f, true);
            _spine.LateUpdate();
        }

        Frame();
        Render();
    }

    private readonly List<Material> _flattened = [];

    private bool Flatten()
    {
        var renderer = _spine != null ? _spine.GetComponent<MeshRenderer>() : null;
        if (renderer == null) return false;

        var flat = FlatMaterial();
        var added = false;

        foreach (var source in renderer.sharedMaterials)
        {
            if (source == null || source.shader == flat.shader) continue;
            if (_spine.CustomMaterialOverride.ContainsKey(source)) continue;

            var plain = new Material(flat)
            {
                name = source.name + " (preview)",
                mainTexture = source.mainTexture,
                hideFlags = HideFlags.HideAndDontSave
            };

            _spine.CustomMaterialOverride[source] = plain;
            _flattened.Add(plain);
            added = true;
        }

        return added;
    }

    public bool WearingRegistered { get; private set; }

    public Spine.Skin Registered(CTFollowerSkinDocument document)
    {
        var key = document.SkinName + "_" + document.VariantName;

        if (!FollowerSpineLoader.CustomFollowerSkins.TryGetValue(key, out var skin) || skin == null) return null;
        if (!FollowerSpineLoader.FollowerSkinOverrides.TryGetValue(key, out var built) || built == null) return null;

        return SameParts(document, built) ? skin : null;
    }

    private static bool SameParts(CTFollowerSkinDocument document,
        List<Tuple<int, string, Texture2D, FollowerSkinPartConfig>> built)
    {
        var parts = document.Config.PartConfigs;
        if (parts == null || parts.Count != built.Count) return false;

        foreach (var entry in built)
        {
            var wanted = entry.Item4;
            var found = false;

            foreach (var part in parts.Values)
            {
                if (part == null || part.SlotIndex != entry.Item1 || part.PartName != entry.Item2) continue;

                found = part.HideSlot == wanted.HideSlot &&
                        Mathf.Approximately(part.ScaleX, wanted.ScaleX) &&
                        Mathf.Approximately(part.ScaleY, wanted.ScaleY) &&
                        Mathf.Approximately(part.Rotation, wanted.Rotation) &&
                        Mathf.Approximately(part.OffsetX, wanted.OffsetX) &&
                        Mathf.Approximately(part.OffsetY, wanted.OffsetY);
                break;
            }

            if (!found) return false;
        }

        return true;
    }

    private void Recolour(CTFollowerSkinDocument document, int colourSet)
    {
        var skeleton = _spine.Skeleton;
        skeleton.SetSlotsToSetupPose();
        FollowerSpineLoader.ApplyColours(skeleton, document.Config, colourSet);

        _spine.Update(0f, true);
        _spine.LateUpdate();
        Render();
    }

    private Packed Find(string signature)
    {
        foreach (var packed in _packed)
            if (packed.Signature == signature && packed.Skin != null && packed.Texture != null) return packed;

        return null;
    }

    private void Trim()
    {
        while (_packed.Count > PackedLimit)
        {
            var oldest = _packed[0];
            _packed.RemoveAt(0);

            if (oldest.Material != null) UnityEngine.Object.Destroy(oldest.Material);
            if (oldest.Texture != null) UnityEngine.Object.Destroy(oldest.Texture);
        }
    }

    private Packed Pack(CTFollowerSkinDocument document, string signature)
    {
        var config = document.Config;
        var overrides = new List<Tuple<int, string, Texture2D, FollowerSkinPartConfig>>();
        foreach (var pair in config.PartConfigs ?? [])
        {
            var part = pair.Value;
            if (part == null) continue;
            var texture = LoadTexture(CTFollowerSkinSerialization.ImagePath(document.SkinName, document.VariantName, pair.Key));
            overrides.Add(new Tuple<int, string, Texture2D, FollowerSkinPartConfig>(part.SlotIndex, part.PartName, texture, part));
        }

        var name = "preview_" + document.SkinName + "_" + document.VariantName;
        var skin = FollowerSpineLoader.ComposeSkin(name, config.OverrideBaseSkin ?? "Cat", overrides, _atlases, FlatMaterial());
        if (skin == null) return null;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var repacked = skin.GetRepackedSkin(name, FlatMaterial(), out var material, out var texture2D);
            clock.Stop();

            if (clock.ElapsedMilliseconds > 40)
                Plugin.Log.LogInfo($"SkinEditor: packing '{name}' took {clock.ElapsedMilliseconds} ms.");

            if (material != null) material.hideFlags = HideFlags.HideAndDontSave;
            if (texture2D != null) texture2D.hideFlags = HideFlags.HideAndDontSave;

            return new Packed
            {
                Signature = signature,
                Skin = repacked,
                Material = material,
                Texture = texture2D
            };
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"SkinEditor: '{name}' could not be packed: {e.Message}");
            return null;
        }
    }

    private static string Signature(CTFollowerSkinDocument document)
    {
        var text = new System.Text.StringBuilder();
        text.Append(document.SkinName).Append('/').Append(document.VariantName);
        text.Append('|').Append(document.Config.OverrideBaseSkin ?? "Cat");

        foreach (var pair in document.Config.PartConfigs ?? [])
        {
            var part = pair.Value;
            if (part == null) continue;

            text.Append('|').Append(pair.Key)
                .Append(':').Append(part.SlotIndex)
                .Append(':').Append(part.PartName)
                .Append(':').Append(part.HideSlot ? 1 : 0)
                .Append(':').Append(part.ScaleX).Append(',').Append(part.ScaleY)
                .Append(',').Append(part.Rotation)
                .Append(',').Append(part.OffsetX).Append(',').Append(part.OffsetY);

            var path = CTFollowerSkinSerialization.ImagePath(document.SkinName, document.VariantName, pair.Key);
            text.Append(':').Append(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L);
        }

        return text.ToString();
    }

    private Material _flat;

    private Material FlatMaterial()
    {
        if (_flat != null) return _flat;

        _flat = new Material(SpineFolderLoader.SpineShader())
        {
            name = "SkinEditor_Preview",
            hideFlags = HideFlags.HideAndDontSave
        };
        return _flat;
    }

    private void DiscardPacked()
    {
        foreach (var packed in _packed)
        {
            if (packed.Material != null) UnityEngine.Object.Destroy(packed.Material);
            if (packed.Texture != null) UnityEngine.Object.Destroy(packed.Texture);
        }

        _packed.Clear();
        _worn = null;

        foreach (var material in _flattened)
            if (material != null) UnityEngine.Object.Destroy(material);

        _flattened.Clear();
    }

    public Texture2D TextureFor(string path) => LoadTexture(path);

    private Texture2D LoadTexture(string path)
    {
        if (!File.Exists(path)) return null;

        var stamp = File.GetLastWriteTimeUtc(path);
        if (_textures.TryGetValue(path, out var cached) && cached.texture != null)
        {
            if (cached.stamp == stamp) return cached.texture;
            UnityEngine.Object.Destroy(cached.texture);
            _textures.Remove(path);
        }

        try
        {
            var texture = TextureHelper.CreateTextureFromPath(path);
            if (texture == null) return null;
            texture.name = Path.GetFileNameWithoutExtension(path);
            texture.hideFlags = HideFlags.HideAndDontSave;
            _textures[path] = (stamp, texture);
            return texture;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"SkinEditor: '{Path.GetFileName(path)}' could not be read: {e.Message}");
            return null;
        }
    }

    private string _wanted;
    private bool _wantedLoop = true;
    private float _reportAt = -1f;

    public void Tick()
    {
        if (_spine == null || _camera == null) return;

        var state = _spine.AnimationState;
        if (state != null && state.GetCurrent(0) == null && !string.IsNullOrEmpty(_wanted))
        {
            try { state.SetAnimation(0, _wanted, _wantedLoop); }
            catch (Exception) { }
        }

        var delta = Time.unscaledDeltaTime;
        if (delta <= 0f || delta > 0.25f) delta = 1f / 60f;

        _spine.Update(delta, true);
        _spine.LateUpdate();
        Render();

        if (_reportAt < 0f) _reportAt = Time.unscaledTime + 1f;
        else if (Time.unscaledTime >= _reportAt && _reportAt < float.MaxValue)
        {
            _reportAt = float.MaxValue;
            var current = state?.GetCurrent(0);
            Plugin.Log.LogInfo($"SkinEditor: preview is playing '{current?.Animation?.Name ?? "nothing"}' " +
                               $"at {current?.TrackTime ?? 0f:0.00}s, timeScale {_spine.timeScale}, " +
                               $"{_spine.GetComponent<MeshRenderer>()?.sharedMaterials.Length ?? 0} material(s).");
        }
    }

    private void Frame()
    {
        try
        {
            var buffer = new float[8];
            _spine.Skeleton.GetBounds(out _, out _, out var wide, out var tall, ref buffer);
            var extent = Mathf.Max(wide, tall) * 0.5f;
            _size = extent > 0.001f ? extent * 1.2f : 1f;
        }
        catch (Exception)
        {
            _size = 1f;
        }
    }

    private static readonly int TimeOfDayColor = Shader.PropertyToID("_TimeOfDayColor");
    private static readonly int GlobalHCol = Shader.PropertyToID("_GlobalHCol");
    private static readonly int GlobalSCol = Shader.PropertyToID("_GlobalSCol");
    private static readonly int GlobalExposure = Shader.PropertyToID("_GlobalExposure");

    private void Render()
    {
        if (_camera == null || _spine == null || _target == null) return;

        _camera.transform.position = Stage + new Vector3(0f, _size * 0.8f, -100f);
        _camera.orthographicSize = _size;
        _camera.targetTexture = _target;

        var timeOfDay = Shader.GetGlobalColor(TimeOfDayColor);
        var highlight = Shader.GetGlobalColor(GlobalHCol);
        var shadow = Shader.GetGlobalColor(GlobalSCol);
        var exposure = Shader.GetGlobalFloat(GlobalExposure);

        Shader.SetGlobalColor(TimeOfDayColor, Color.white);
        Shader.SetGlobalColor(GlobalHCol, Color.white);
        Shader.SetGlobalFloat(GlobalExposure, 1f);

        try
        {
            _camera.Render();
        }
        finally
        {
            Shader.SetGlobalColor(TimeOfDayColor, timeOfDay);
            Shader.SetGlobalColor(GlobalHCol, highlight);
            Shader.SetGlobalColor(GlobalSCol, shadow);
            Shader.SetGlobalFloat(GlobalExposure, exposure);
            _camera.targetTexture = null;
        }
    }

    public void Release()
    {
        foreach (var entry in _textures.Values)
            if (entry.texture != null) UnityEngine.Object.Destroy(entry.texture);
        _textures.Clear();

        DiscardPacked();
        SpineMemory.TrimRepackCaches("skin editor closed");
        if (_flat != null) UnityEngine.Object.Destroy(_flat);
        _flat = null;

        foreach (var atlas in _atlases.Values)
        {
            if (atlas == null) continue;
            foreach (var mat in atlas.materials) if (mat != null) UnityEngine.Object.Destroy(mat);
            UnityEngine.Object.Destroy(atlas);
        }
        _atlases.Clear();

        if (_target != null)
        {
            _target.Release();
            UnityEngine.Object.DestroyImmediate(_target);
        }
        if (_root != null) UnityEngine.Object.Destroy(_root);

        _target = null;
        _root = null;
        _camera = null;
        _spine = null;
        _image = null;
    }
}
