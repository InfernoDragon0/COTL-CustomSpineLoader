using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Lamb.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

// The game's own DLC map art, cut from its "DLC Map Menu" prefab so custom world maps read as part
// of the game. Only the visuals are taken: every vanilla script is stripped off the clones, since
// they expect the DLC menu, its save data and its authored node graph. CustomNodeVisual and
// CustomLineVisual drive what is left.
internal static class CustomMapSkin
{
    public static bool Ready { get; private set; }
    public static bool Unavailable { get; private set; }
    public static bool Usable => Ready && !Unavailable;

    // Clones run their Awake the moment they are active, so they are cut and stripped under here.
    private static GameObject _holder;

    // Closing the vanilla map unloads the prefab we harvested from, taking these references with
    // it; the next map to open re-loads and re-harvests rather than drawing from dead sources.
    private static bool Lost => Ready && _probe == null;
    private static GameObject _probe;

    public static bool NeedsLoad => !Unavailable && (!Ready || Lost);

    private static readonly Dictionary<string, GameObject> Sources =
        new(StringComparer.OrdinalIgnoreCase);

    private static GameObject _connectionSource;

    // Our node types, each naming the vanilla node styles to borrow art from, best first.
    private static readonly Dictionary<string, string[]> TypeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Base"] = ["Base"],
            ["Dungeon"] = ["Dungeon5", "Dungeon6", "Dungeon1"],
            ["MiniBoss"] = ["Dungeon5_MiniBoss", "Dungeon6_MiniBoss"],
            ["Boss"] = ["Dungeon5_Boss", "Dungeon6_Boss", "Yngya"],
            ["Key"] = ["Key", "Key_2", "Key_3"],
            ["Lock"] = ["Lock", "Lock_2", "Lock_3"],
            ["Reward"] = ["Reward", "GhostResource", "Dungeon5_Story"]
        };

    private static readonly string[] AnyDungeon = ["Dungeon5", "Dungeon6", "Dungeon1", "Base"];

    // ---- loading ---------------------------------------------------------------------------

    // The prefab is an Addressable the game only loads when the DLC map is about to open, so the
    // first custom map of a session waits for it.
    public static IEnumerator EnsureLoaded()
    {
        if (Lost) Forget();
        if (Ready || Unavailable) yield break;

        var ui = MonoSingleton<UIManager>.Instance;
        if (ui == null)
        {
            Fail("the UI manager is not up yet");
            yield break;
        }

        if (ui.DLCWorldMapTemplate == null)
        {
            System.Threading.Tasks.Task task = null;
            try
            {
                task = ui.LoadDLCWorldMapAssets();
            }
            catch (Exception e)
            {
                Fail("loading the DLC map asset threw: " + e.Message);
            }

            if (Unavailable) yield break;

            var deadline = Time.realtimeSinceStartup + 20f;
            while (task != null && !task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        var template = ui.DLCWorldMapTemplate;
        if (template == null)
        {
            Fail("the game's DLC map menu asset did not load");
            yield break;
        }

        try
        {
            Harvest(template);
        }
        catch (Exception e)
        {
            Fail("reading the DLC map prefab threw: " + e.Message);
        }
    }

    private static void Forget()
    {
        Ready = false;
        Sources.Clear();
        _connectionSource = null;
        _probe = null;
    }

    private static void Harvest(UIDLCMapMenuController template)
    {
        if (_holder == null)
        {
            _holder = new GameObject("CT_WorldMapSkin");
            _holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_holder);
        }

        var locations = Traverse.Create(template).Field("_locations").GetValue<DungeonWorldMapIcon[]>();
        if (locations != null)
        {
            foreach (var location in locations)
            {
                if (location == null) continue;

                // One node per style: the map authors many nodes of each type and they only differ
                // in position and wiring.
                var key = location.Type.ToString();
                if (!Sources.ContainsKey(key)) Sources[key] = location.gameObject;
            }
        }

        var connection = Traverse.Create(template).Field("_connectionPrefab").GetValue<DLCMapConnection>();
        _connectionSource = connection != null ? connection.gameObject : null;

        Ready = Sources.Count > 0;
        if (!Ready)
        {
            Fail("the DLC map prefab held no nodes");
            return;
        }

        foreach (var pair in Sources)
        {
            _probe = pair.Value;
            break;
        }

        Plugin.Log.LogInfo($"World map skin: {Sources.Count} vanilla node styles" +
                           (_connectionSource != null ? " and the connection prefab." : ", no connection prefab."));
    }

    private static void Fail(string why)
    {
        Unavailable = true;
        Plugin.Log.LogWarning("World maps are drawing their own visuals - " + why + ".");
    }

    // ---- clones ----------------------------------------------------------------------------

    // Returned inactive and under the holder: the caller reparents it, which is what wakes it.
    public static GameObject CloneNode(string nodeType)
    {
        if (!Usable) return null;

        var source = SourceFor(nodeType);
        if (source == null) return null;

        try
        {
            var clone = UnityEngine.Object.Instantiate(source, _holder.transform);
            clone.name = "CustomNodeArt";
            return clone;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("World map: cloning a vanilla node failed - " + e.Message);
            return null;
        }
    }

    public static GameObject CloneConnection()
    {
        if (!Usable || _connectionSource == null) return null;

        try
        {
            var clone = UnityEngine.Object.Instantiate(_connectionSource, _holder.transform);
            clone.name = "CustomLinkArt";
            return clone;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("World map: cloning a vanilla link failed - " + e.Message);
            return null;
        }
    }

    // The plain dungeon node's own icon, borrowed by the editor dock.
    public static Sprite NodeIconSprite()
    {
        if (!Usable) return null;

        var source = SourceFor("Dungeon");
        var content = source != null ? source.GetComponentInChildren<DungeonMapIconContent>(true) : null;
        if (content == null) return null;

        var icon = Traverse.Create(content).Field("_icon").GetValue<Image>();
        return icon != null ? icon.sprite : null;
    }

    private static GameObject SourceFor(string nodeType)
    {
        var wanted = TypeMap.TryGetValue(nodeType ?? "", out var styles) ? styles : AnyDungeon;

        foreach (var style in wanted)
            if (Sources.TryGetValue(style, out var source) && source != null)
                return source;

        foreach (var style in AnyDungeon)
            if (Sources.TryGetValue(style, out var source) && source != null)
                return source;

        foreach (var pair in Sources)
            if (pair.Value != null)
                return pair.Value;

        return null;
    }

    // Strips the vanilla logic off a clone while it is still inactive, leaving the art behind.
    // DestroyImmediate, not Destroy: a deferred destroy still runs the script's Awake when the
    // clone is parented in this same frame.
    public static void StripLogic(GameObject clone, MonoBehaviour keep)
    {
        if (clone == null) return;

        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || ReferenceEquals(behaviour, keep)) continue;

            if (behaviour is Graphic graphic)
            {
                // The node view owns one click target of its own; art must not shadow it.
                graphic.raycastTarget = false;
                continue;
            }

            if (behaviour is Mask or RectMask2D or LayoutGroup or ContentSizeFitter or AspectRatioFitter)
                continue;

            try
            {
                UnityEngine.Object.DestroyImmediate(behaviour);
            }
            catch (Exception e)
            {
                behaviour.enabled = false;
                Plugin.Log.LogWarning($"World map: {behaviour.GetType().Name} could not be removed " +
                                      $"from the vanilla art ({e.Message}); it was disabled instead.");
            }
        }
    }

    public static void Play(string audioEvent)
    {
        if (string.IsNullOrEmpty(audioEvent)) return;

        try
        {
            UIManager.PlayAudio(audioEvent);
        }
        catch (Exception)
        {
            // A missing FMOD event is not worth a broken map screen.
        }
    }
}
