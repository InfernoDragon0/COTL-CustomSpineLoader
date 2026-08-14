using System;
using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor.Tools;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Icons for the enemy grid.
//
// The game has no 2D icons for enemies anywhere - they exist only as Spine skeletons - so the
// thumbnails are rendered here. The game itself does this two different ways, and which one is
// right depends entirely on how many distinct skeletons are on screen:
//
//  - FollowerInformationBox, FollowerCommandWheelPortrait and ~40 other cards put a live
//    SkeletonGraphic straight in the canvas. That works because every one of them shows the SAME
//    follower skeleton and only swaps a skin, so the skeleton data and atlas are already resident
//    and there are never more than a handful on screen.
//
//  - FollowersNameManager bakes instead: one reusable off-screen camera and RenderTexture, one
//    Camera.Render() per item, ReadPixels into a shared buffer, and the result blitted into a
//    single shared atlas texture that every nameplate then draws as a plain Sprite.
//
// A grid of 150+ DIFFERENT enemy skeletons is the second case, so this follows the nameplate
// manager: the rig below is built once and reused, and the finished icons all live in a few
// shared atlas pages, which is what keeps them to a handful of draw calls and lets them clip
// inside a scroll view like any other sprite.
//
// From the SkeletonGraphic side we take the part that matters: the subject is built from the
// prefab's skeletonDataAsset rather than by instantiating the enemy. Cloning a whole enemy - AI,
// health, colliders, particles, child rigs - just to photograph it was by far the most expensive
// thing this file used to do.
public static class EnemyThumbnails
{
    // Source resolution per icon. The grid draws them at 88px.
    private const int Cell = 96;

    // 5x5 icons to a page. Small pages keep each Texture2D.Apply cheap, which matters because
    // ours are applied progressively rather than all at once like the nameplate atlas.
    private const int Columns = 5;
    private const int PageSize = Cell * Columns;
    private const int SlotsPerPage = Columns * Columns;

    // One render every few frames. Low enough to stay invisible as a hitch, high enough that a
    // group of ~150 finishes while the user is still looking at it.
    private const int FramesBetween = 2;

    // Backstop for the case where every layer is named: nothing exists this far out, so the
    // staging camera sees only the subject.
    private static readonly Vector3 Stage = new(5000f, 5000f, 0f);

    private static readonly Dictionary<string, Sprite> _cache = [];
    private static readonly HashSet<string> _failed = [];
    private static readonly Queue<ThumbRequest> _queue = new();
    private static bool _draining;

    private class ThumbRequest
    {
        public string Key;
        public bool IsCustom;
        public Action<Sprite> OnReady;
    }

    public static void Request(MonoBehaviour host, string key, bool isCustom, Action<Sprite> onReady)
    {
        if (onReady == null || string.IsNullOrEmpty(key)) return;

        if (_cache.TryGetValue(key, out var cached)) { onReady(cached); return; }
        if (_failed.Contains(key)) { onReady(null); return; }

        _queue.Enqueue(new ThumbRequest { Key = key, IsCustom = isCustom, OnReady = onReady });
        if (host != null && !_draining) host.StartCoroutine(Drain(host));
    }

    // Called when the grid is rebuilt: the pending cells no longer exist.
    public static void CancelPending() => _queue.Clear();

    public static void ClearSceneScopedCache()
    {
        _queue.Clear();
        // The worker coroutine died with its host, so the flag has to come back down or the
        // next editor would queue requests that nothing picks up. The finished sprites and the
        // rig survive: both are flagged not to unload, and rebuilding them per scene would
        // throw away every icon the player has already waited for.
        _draining = false;
    }

    private static IEnumerator Drain(MonoBehaviour host)
    {
        _draining = true;

        while (_queue.Count > 0)
        {
            var request = _queue.Dequeue();

            if (_cache.TryGetValue(request.Key, out var cached))
            {
                Deliver(request, cached);
                continue;
            }

            yield return Render(request);

            for (var i = 0; i < FramesBetween; i++) yield return null;
        }

        _draining = false;
    }

    private static void Deliver(ThumbRequest request, Sprite sprite)
    {
        try
        {
            request.OnReady(sprite);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: enemy thumbnail callback failed: " + e.Message);
        }
    }

    // ---- the rig ----------------------------------------------------------------------------
    //
    // Built once and kept for the process, exactly like FollowersNameManager's TMP_Render_Camera:
    // HideAndDontSave so a scene change does not take it, and the camera disabled so it only ever
    // renders when we ask it to.

    private static Camera _camera;
    private static RenderTexture _target;
    private static Texture2D _readBuffer;
    private static Transform _stageRoot;
    private static int _stageLayer = -1;

    private static readonly List<Texture2D> _pages = [];
    private static int _nextSlot;

    private static void EnsureRig()
    {
        if (_target == null)
        {
            _target = new RenderTexture(Cell, Cell, 16, RenderTextureFormat.ARGB32)
            {
                name = "CultTweaker_ThumbnailRT",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1
            };
        }

        if (_readBuffer == null)
        {
            _readBuffer = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false)
            {
                name = "CultTweaker_ThumbnailReadback",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        if (_stageLayer < 0) _stageLayer = FindFreeLayer();

        if (_stageRoot == null)
        {
            var root = new GameObject("CultTweaker_ThumbnailStage") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = Stage;
            _stageRoot = root.transform;
        }

        if (_camera == null)
        {
            var go = new GameObject("CultTweaker_ThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
            _camera = go.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.targetTexture = _target;
            // Explicit, and after the target texture: a fresh camera starts on the screen's
            // aspect, which squeezed every skeleton horizontally into the square render texture.
            _camera.aspect = 1f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.useOcclusionCulling = false;
            // Rendered by hand, never as part of the frame.
            _camera.enabled = false;

            // Layer isolation is how the game's own bake camera guarantees it photographs only
            // its subject; parking far out is only the fallback when no layer is spare.
            if (_stageLayer >= 0) _camera.cullingMask = 1 << _stageLayer;
        }
    }

    private static int FindFreeLayer()
    {
        // Downwards: the high layers are the ones projects leave unnamed.
        for (var i = 31; i >= 8; i--)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;

        Plugin.Log.LogInfo("MapEditor: no spare layer for thumbnails; the staging camera relies on distance instead.");
        return -1;
    }

    private static IEnumerator Render(ThumbRequest request)
    {
        GameObject prefab = null;
        yield return EnemyTool.ResolvePrefabRoutine(request.Key, request.IsCustom, p => prefab = p);

        if (prefab == null)
        {
            _failed.Add(request.Key);
            Deliver(request, null);
            yield break;
        }

        EnsureRig();

        GameObject subject = null;
        try
        {
            subject = BuildSubject(prefab, request.Key, request.IsCustom);
            if (subject == null || !TryGetBounds(subject, out var bounds))
            {
                _failed.Add(request.Key);
                Deliver(request, null);
                yield break;
            }

            // Barely any headroom: the subject should fill its tile.
            _camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.02f + 0.01f;
            _camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
            _camera.Render();

            var sprite = Capture(request.Key);
            if (sprite != null) _cache[request.Key] = sprite;
            else _failed.Add(request.Key);
            Deliver(request, sprite);
        }
        finally
        {
            if (subject != null) UnityEngine.Object.Destroy(subject);
        }
    }

    // A bare SkeletonAnimation driven by the prefab's own skeleton data, rather than a clone of
    // the enemy. Falls back to the full ghost for anything that is not Spine-driven (a handful
    // of enemies are plain sprites).
    private static GameObject BuildSubject(GameObject prefab, string key, bool isCustom)
    {
        var source = FindSourceSkeleton(prefab, key, isCustom, out var dataAsset, out var skin);

        if (dataAsset == null)
            return BuildGhostSubject(prefab, key, isCustom);

        var go = new GameObject("CultTweaker_ThumbnailSubject");
        // Inactive first, so Awake runs once with the skeleton already assigned rather than
        // once empty and again on our Initialize.
        go.SetActive(false);
        go.transform.SetParent(_stageRoot, false);

        SkeletonAnimation spine;
        try
        {
            spine = go.AddComponent<SkeletonAnimation>();
            spine.skeletonDataAsset = dataAsset;
            if (!string.IsNullOrEmpty(skin)) spine.initialSkinName = skin;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: thumbnail skeleton setup failed for '{key}': {e.Message}");
            UnityEngine.Object.Destroy(go);
            return BuildGhostSubject(prefab, key, isCustom);
        }

        // Enemies are authored at wildly different skeleton scales; the prefab's own transform
        // scale is part of how the enemy actually looks.
        if (source != null)
        {
            var scale = source.transform.lossyScale;
            if (scale.sqrMagnitude > 0.0001f) go.transform.localScale = scale;
        }

        SetLayer(go);
        go.SetActive(true);

        try
        {
            // Nothing ticks at timeScale 0, so the pose and the mesh are pushed by hand.
            spine.Initialize(true);
            spine.Skeleton?.SetToSetupPose();
            spine.Update(0f);
            spine.LateUpdate();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: thumbnail pose failed for '{key}': {e.Message}");
        }

        return go;
    }

    // Which skeleton data to photograph: a custom enemy's override wins, otherwise the prefab's
    // own controller skeleton (the same field the cursor preview uses).
    private static SkeletonAnimation FindSourceSkeleton(GameObject prefab, string key, bool isCustom,
        out SkeletonDataAsset dataAsset, out string skin)
    {
        dataAsset = null;
        skin = null;

        var source = EnemyTool.MainSkeleton(prefab);
        if (source != null)
        {
            dataAsset = source.skeletonDataAsset;
            skin = source.initialSkinName;
        }

        if (isCustom && EnemyTool.TryGetCustomSkin(key, out var overrideAsset, out var overrideSkin) &&
            overrideAsset != null)
        {
            dataAsset = overrideAsset;
            skin = overrideSkin;
        }

        return source;
    }

    // Last resort for non-Spine enemies: the old full-prefab ghost.
    private static GameObject BuildGhostSubject(GameObject prefab, string key, bool isCustom)
    {
        var ghost = MapEditorGhost.Create(prefab, null, "CultTweaker_Thumbnail", disableBehaviours: true);
        if (ghost == null) return null;

        ghost.transform.SetParent(_stageRoot, false);
        ghost.transform.localPosition = Vector3.zero;

        var spine = EnemyTool.MainSkeleton(ghost);
        if (spine != null)
        {
            // The mimic prefabs carry extra skeleton renderers for ghost/afterimage effects;
            // they would double-expose the thumbnail.
            foreach (var other in ghost.GetComponentsInChildren<SkeletonRenderer>(true))
            {
                if (other == null || ReferenceEquals(other, spine)) continue;
                var mesh = other.GetComponent<MeshRenderer>();
                if (mesh != null) mesh.enabled = false;
            }

            if (isCustom) EnemyTool.ApplyCustomSkin(spine, key);

            try
            {
                spine.Skeleton?.SetToSetupPose();
                spine.Update(0f);
                spine.LateUpdate();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: thumbnail pose failed for '{key}': {e.Message}");
            }
        }

        // Ghosts are faded for use as a cursor preview; a thumbnail wants full strength.
        foreach (var renderer in ghost.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.color = new Color(renderer.color.r, renderer.color.g, renderer.color.b, 1f);

        SetLayer(ghost);
        return ghost;
    }

    private static void SetLayer(GameObject go)
    {
        if (_stageLayer < 0) return;
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = _stageLayer;
    }

    private static bool TryGetBounds(GameObject subject, out Bounds bounds)
    {
        bounds = default;
        var any = false;

        foreach (var renderer in subject.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer == null || !renderer.enabled) continue;
            if (renderer.bounds.size.sqrMagnitude <= 0.0001f) continue;

            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        return any;
    }

    // ---- readback + atlas -------------------------------------------------------------------

    // Reads what the camera drew and files it into a shared atlas page, the way the nameplate
    // manager does: every icon on a page is one texture, so a full grid is a few draw calls
    // instead of one per cell, and there is no per-icon Texture2D to leak.
    private static Sprite Capture(string key)
    {
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = _target;
            _readBuffer.ReadPixels(new Rect(0f, 0f, Cell, Cell), 0, 0);
            _readBuffer.Apply();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: thumbnail readback failed for '{key}': {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
        }

        try
        {
            var slot = _nextSlot++;
            var page = PageFor(slot);
            var x = slot % SlotsPerPage % Columns * Cell;
            var y = PageSize - (slot % SlotsPerPage / Columns + 1) * Cell;

            page.SetPixels32(x, y, Cell, Cell, _readBuffer.GetPixels32());
            page.Apply(false, false);

            var sprite = Sprite.Create(page, new Rect(x, y, Cell, Cell), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            sprite.name = "Thumb_" + key;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: thumbnail atlas write failed for '{key}': {e.Message}");
            return null;
        }
    }

    private static Texture2D PageFor(int slot)
    {
        var index = slot / SlotsPerPage;
        while (_pages.Count <= index) _pages.Add(NewPage(_pages.Count));
        return _pages[index];
    }

    private static Texture2D NewPage(int index)
    {
        var page = new Texture2D(PageSize, PageSize, TextureFormat.RGBA32, false)
        {
            name = "CultTweaker_ThumbnailAtlas_" + index,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
            wrapMode = TextureWrapMode.Clamp
        };

        // Undrawn slots must be transparent, not whatever the allocation happened to contain.
        var blank = new Color32[PageSize * PageSize];
        page.SetPixels32(blank);
        page.Apply(false, false);
        return page;
    }
}
