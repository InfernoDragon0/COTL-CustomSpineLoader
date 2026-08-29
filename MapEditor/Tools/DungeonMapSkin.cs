using System;
using System.Collections;
using HarmonyLib;
using Lamb.UI;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

internal static class DungeonMapSkin
{
    public static bool Ready { get; private set; }
    public static bool Unavailable { get; private set; }
    public static bool Usable => Ready && !Unavailable;

    private static GameObject _holder;

    private static bool Lost => Ready && _nodeSource == null;

    public static bool NeedsLoad => !Unavailable && (!Ready || Lost);

    private static GameObject _nodeSource;

    public static Texture LineTexture { get; private set; }
    public static Material LineMaterial { get; private set; }

    public const float VanillaPitch = 300f;
    public const float VanillaLineWidth = 7.5f;

    // ---- loading ---------------------------------------------------------------------------

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

        if (ui.AdventureMapNodeTemplate == null)
        {
            System.Threading.Tasks.Task task = null;
            try
            {
                task = ui.LoadDungeonAssets();
            }
            catch (Exception e)
            {
                Fail("loading the dungeon map assets threw: " + e.Message);
            }

            if (Unavailable) yield break;

            var deadline = Time.realtimeSinceStartup + 20f;
            while (task != null && !task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        var template = ui.AdventureMapNodeTemplate;
        if (template == null)
        {
            Fail("the game's adventure map node asset did not load");
            yield break;
        }

        try
        {
            Harvest(ui, template);
        }
        catch (Exception e)
        {
            Fail("reading the adventure map prefab threw: " + e.Message);
        }
    }

    private static void Forget()
    {
        Ready = false;
        Unavailable = false;
        _nodeSource = null;
        LineTexture = null;
        LineMaterial = null;
    }

    private static void Harvest(UIManager ui, AdventureMapNode template)
    {
        if (_holder == null)
        {
            _holder = new GameObject("CT_DungeonMapSkin");
            _holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_holder);
        }

        _nodeSource = template.gameObject;

        var overlay = ui.AdventureMapOverlayTemplate;
        if (overlay != null)
        {
            var reader = Traverse.Create(overlay);
            LineTexture = reader.Field("_connectionTexture").GetValue<Texture>();
            LineMaterial = reader.Field("_idleDottedMaterial").GetValue<Material>();
        }

        Ready = _nodeSource != null;
        if (!Ready)
        {
            Fail("the adventure map node prefab was empty");
            return;
        }

        Plugin.Log.LogInfo("Dungeon map skin: the game's own node art" +
                           (LineMaterial != null ? " and its dotted connections." : ", with plain connections."));
    }

    private static void Fail(string why)
    {
        Unavailable = true;
        Plugin.Log.LogWarning("The dungeon map view is drawing its own visuals - " + why + ".");
    }

    // ---- clones ----------------------------------------------------------------------------

    public static GameObject CloneNode()
    {
        if (!Usable) return null;

        try
        {
            var clone = UnityEngine.Object.Instantiate(_nodeSource, _holder.transform);
            clone.name = "DungeonNodeArt";
            return clone;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Dungeon map: cloning a vanilla node failed - " + e.Message);
            return null;
        }
    }

    public static GameObject CloneChrome()
    {
        if (!Usable) return null;

        var ui = MonoSingleton<UIManager>.Instance;
        var overlay = ui != null ? ui.AdventureMapOverlayTemplate : null;
        if (overlay == null) return null;

        try
        {
            var clone = UnityEngine.Object.Instantiate(overlay.gameObject, _holder.transform);
            clone.name = "DungeonMapChrome";

            var controller = clone.GetComponent<UIAdventureMapOverlayController>();
            if (controller != null)
            {
                var reader = Traverse.Create(controller);
                Drop(reader.Field("_nodeContent").GetValue<RectTransform>());
                Drop(reader.Field("_connectionContent").GetValue<RectTransform>());
                Drop(reader.Field("_crownSpineRectTransform").GetValue<RectTransform>());
                Drop(reader.Field("_eyeSpineRectTransform").GetValue<RectTransform>());
                Drop(reader.Field("_shufflePrompt").GetValue<GameObject>());
                Drop(reader.Field("_goopFade").GetValue<Component>());
            }

            StripLogic(clone, null);

            foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true))
                UnityEngine.Object.DestroyImmediate(canvas);

            foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true))
            {
                group.alpha = 1f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            return clone;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Dungeon map: cloning the map screen failed - " + e.Message);
            return null;
        }
    }

    private static void Drop(Component component) => Drop(component != null ? component.gameObject : null);

    private static void Drop(GameObject go)
    {
        if (go != null) UnityEngine.Object.DestroyImmediate(go);
    }

    public static void StripLogic(GameObject clone, MonoBehaviour keep)
    {
        if (clone == null) return;

        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || ReferenceEquals(behaviour, keep)) continue;

            if (behaviour is UnityEngine.UI.Graphic graphic)
            {
                graphic.raycastTarget = false;
                continue;
            }

            if (behaviour is UnityEngine.UI.Mask or UnityEngine.UI.RectMask2D
                or UnityEngine.UI.LayoutGroup or UnityEngine.UI.ContentSizeFitter
                or UnityEngine.UI.AspectRatioFitter)
                continue;

            try
            {
                UnityEngine.Object.DestroyImmediate(behaviour);
            }
            catch (Exception e)
            {
                behaviour.enabled = false;
                Plugin.Log.LogWarning($"Dungeon map: {behaviour.GetType().Name} could not be removed " +
                                      $"from the vanilla art ({e.Message}); it was disabled instead.");
            }
        }
    }
}
