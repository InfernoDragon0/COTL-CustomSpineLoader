using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using COTL_API.CustomStructures;
using COTL_API.Helpers;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CustomSpineLoader.MapEditor;

public static class MapEditorIcons
{
    private const string IconFolder = "Assets/EditorIcons";
    private const string PlaceholderFile = "Assets/colorwheel.png";

    // Null is a real, cached answer: "no art on disk" must not re-hit the filesystem.
    private static readonly Dictionary<string, Sprite> _diskIcons = [];
    private static Sprite _placeholder;
    private static bool _placeholderTried;

    private static readonly Dictionary<StructureBrain.TYPES, Sprite> _structureIcons = [];
    private static readonly Dictionary<string, Sprite> _propIcons = [];
    private static readonly HashSet<string> _propIconsFailed = [];

    public static Sprite GetToolIcon(string toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return Placeholder;

        if (_diskIcons.TryGetValue(toolName, out var cached))
            return cached != null ? cached : Placeholder;

        var sprite = LoadFromDisk(Path.Combine(Plugin.PluginPath, IconFolder, toolName + ".png"));
        _diskIcons[toolName] = sprite;
        return sprite != null ? sprite : Placeholder;
    }

    // Null rather than the placeholder, for callers that would rather draw something of their own.
    public static Sprite GetToolIconOrNull(string toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return null;

        if (_diskIcons.TryGetValue(toolName, out var cached)) return cached;

        var sprite = LoadFromDisk(Path.Combine(Plugin.PluginPath, IconFolder, toolName + ".png"));
        _diskIcons[toolName] = sprite;
        return sprite;
    }

    public static Sprite Placeholder
    {
        get
        {
            if (_placeholderTried) return _placeholder;
            _placeholderTried = true;
            _placeholder = LoadFromDisk(Path.Combine(Plugin.PluginPath, PlaceholderFile));
            if (_placeholder == null)
                Plugin.Log.LogWarning("MapEditor: no placeholder icon on disk; tool buttons fall back to letter tiles.");
            return _placeholder;
        }
    }

    private static Sprite LoadFromDisk(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var texture = TextureHelper.CreateTextureFromPath(path);
            if (texture == null) return null;
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = Path.GetFileNameWithoutExtension(path);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: icon '{path}' failed to load: {e.Message}");
            return null;
        }
    }

    public static Sprite GetStructureIcon(StructureBrain.TYPES type, Sprite known = null)
    {
        if (_structureIcons.TryGetValue(type, out var cached) && cached != null) return cached;

        var sprite = known;

        // Other mods' structures are not in the scene's placement list; COTL_API keeps their icon.
        if (sprite == null)
        {
            try
            {
                if (CustomStructureManager.CustomStructureList.TryGetValue(type, out var custom))
                    sprite = custom?.Sprite;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: no icon for custom structure {type}: {e.Message}");
            }
        }

        // Game-owned sprite: no hideFlags fiddling, it is managed with the scene.
        _structureIcons[type] = sprite;
        return sprite;
    }

    // ---- prop icons -------------------------------------------------------------------------

    private const int MaxConcurrent = 4;

    private static readonly Queue<(string path, Action<Sprite> callback)> _propQueue = new();
    private static int _inFlight;
    private static bool _draining;

    public static void GetPropIcon(MonoBehaviour host, string prefabPath, Action<Sprite> onLoaded)
    {
        if (onLoaded == null || string.IsNullOrEmpty(prefabPath)) return;

        // Fake-null check: an unloaded addressable sprite must become a reload, not reach Image.sprite.
        if (_propIcons.TryGetValue(prefabPath, out var cached) && cached != null) { onLoaded(cached); return; }
        if (_propIconsFailed.Contains(prefabPath)) { onLoaded(null); return; }

        _propQueue.Enqueue((prefabPath, onLoaded));
        if (host != null && !_draining) host.StartCoroutine(DrainPropQueue());
    }

    // Cancels loads not yet started; they would fill cells that no longer exist.
    public static void CancelPendingPropIcons() => _propQueue.Clear();

    // Bumped per session so stale completions cannot drive _inFlight negative.
    private static int _session;

    private static IEnumerator DrainPropQueue()
    {
        _draining = true;

        while (_propQueue.Count > 0)
        {
            while (_inFlight >= MaxConcurrent) yield return null;

            var (path, callback) = _propQueue.Dequeue();
            _inFlight++;
            var session = _session;
            LoadPropIcon(path, sprite =>
            {
                if (session == _session) _inFlight--;
                try { callback(sprite); }
                catch (Exception e) { Plugin.Log.LogWarning("MapEditor: prop icon callback failed: " + e.Message); }
            });

            // One dispatch per frame keeps the grid's fill visible rather than a burst-then-stall.
            yield return null;
        }

        _draining = false;
    }

    private static void LoadPropIcon(string path, Action<Sprite> done)
    {
        AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.LoadAssetAsync<GameObject>(path);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: prop icon load failed for '{path}': {e.Message}");
            _propIconsFailed.Add(path);
            done(null);
            return;
        }

        // Never released: releasing the handle unloads the sprite the icon still draws.
        handle.Completed += op =>
        {
            Sprite sprite = null;
            try
            {
                if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
                    sprite = op.Result.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: prop icon extraction failed for '{path}': {e.Message}");
            }

            if (sprite != null) _propIcons[path] = sprite;
            else _propIconsFailed.Add(path);
            done(sprite);
        };
    }

    // Scene-sourced icons die with the scene; disk icons survive (flagged not to unload).
    public static void ClearSceneScopedCache()
    {
        _structureIcons.Clear();
        _propQueue.Clear();
        // The drain coroutine died with its host; reset, and bump the session so stragglers are no-ops.
        _session++;
        _inFlight = 0;
        _draining = false;
    }
}
