using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// What was swapped, so it can be swapped back. Also the honest answer to "is this on?" - a component
// nothing else in the game creates.
public class SeeThroughState : MonoBehaviour
{
    public readonly List<SpriteRenderer> Renderers = [];
    public readonly List<Material> Originals = [];

    public bool Player;
    public bool Fog;
}

// The crystal tree's two tricks, on anything you like.
//
// They are two, and that is the whole reason this took a while to get right:
//
//   - **The player shows through it.** That is the material's doing - a crystal tree draws with a
//     different shader from an ordinary decoration - so the material is taken from a crystal tree
//     rather than conjured by turning flags on. Alongside it goes the game's own
//     StencilLighting_ExcludeSprite, which mirrors the sprite into the lighting pass so the pass
//     can cut the player back out of it.
//   - **The fog passes through it.** That is `_FadeIntoWoods` on the material, which reads like the
//     answer to the first one and is not: on its own it turns a structure the colour of the
//     weather and lets nothing through at all.
//
// Applied to a copy of the material, never the asset: the shared one stands behind every crystal
// tree in the game, and writing to it would change all of them, everywhere, until the game closed.
public static class SeeThrough
{
    // The structure whose look is being borrowed.
    private const StructureBrain.TYPES Donor = StructureBrain.TYPES.DECORATION_CRYSTAL_TREE;

    // The child StencilLighting_ExcludeSprite builds for itself. It has no OnDestroy, so removing
    // the component leaves this behind unless it is taken too.
    private const string RendererName = "ExclusionRenderer";

    private const string FogProperty = "_FadeIntoWoods";
    private const string FogKeyword = "_FADEINTOWOODS_ON";

    private static Material _donorMaterial;
    private static bool _lookedForDonor;

    public static bool IsPlayerThrough(GameObject go)
    {
        var state = go != null ? go.GetComponent<SeeThroughState>() : null;
        return state != null && state.Player;
    }

    public static bool IsFogThrough(GameObject go)
    {
        var state = go != null ? go.GetComponent<SeeThroughState>() : null;
        return state != null && state.Fog;
    }

    // Both effects are set together because they share one material: the base comes from whether
    // the player shows through, and the fog flag goes on top of whichever base that is.
    public static void Set(GameObject go, bool player, bool fog, float amount = 0.7f)
    {
        if (go == null) return;

        if (!player && !fog)
        {
            Remove(go);
            return;
        }

        var state = go.GetComponent<SeeThroughState>();
        if (state == null)
        {
            state = go.AddComponent<SeeThroughState>();

            foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null || renderer.name == RendererName) continue;
                state.Renderers.Add(renderer);
                state.Originals.Add(renderer.sharedMaterial);
            }

            if (state.Renderers.Count == 0)
            {
                Plugin.Log.LogWarning($"MapEditor: '{go.name}' has no sprite to change.");
                Object.DestroyImmediate(state);
                return;
            }
        }

        var donor = player ? DonorMaterial() : null;
        if (player && donor == null) player = false;

        for (var i = 0; i < state.Renderers.Count; i++)
        {
            var renderer = state.Renderers[i];
            if (renderer == null) continue;

            // Back to what it started as first, so turning one effect off does not leave the other
            // one's material behind.
            renderer.sharedMaterial = player ? donor : state.Originals[i];

            if (!fog) continue;

            // Reading .material after pointing at the asset is what makes the copy; the flag lands
            // on this object's copy rather than on every structure sharing that material.
            var instance = renderer.material;
            if (instance == null || !instance.HasProperty(FogProperty)) continue;

            instance.SetFloat(FogProperty, 1f);
            instance.EnableKeyword(FogKeyword);
        }

        state.Player = player;
        state.Fog = fog;

        if (player) AddExclusion(go, amount);
        else RemoveExclusion(go);
    }

    // The lighting half. Loads its own material in Start without checking it, so it is checked here
    // instead - a missing one would throw somewhere nothing can be said about it.
    private static void AddExclusion(GameObject go, float amount)
    {
        if (go.GetComponentInChildren<StencilLighting_ExcludeSprite>(true) != null) return;

        if (Resources.Load<Material>("Materials/StencilLighting_ExcludeSprite") == null)
        {
            Plugin.Log.LogWarning("MapEditor: the game's exclusion material is missing; the player " +
                                  "may not show through cleanly.");
            return;
        }

        var renderer = go.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null) return;

        var exclude = renderer.gameObject.AddComponent<StencilLighting_ExcludeSprite>();
        exclude.ExclusionAmount = Mathf.Clamp01(amount);
    }

    private static void Remove(GameObject go)
    {
        var state = go.GetComponent<SeeThroughState>();
        if (state != null)
        {
            for (var i = 0; i < state.Renderers.Count && i < state.Originals.Count; i++)
                if (state.Renderers[i] != null) state.Renderers[i].sharedMaterial = state.Originals[i];

            Object.DestroyImmediate(state);
        }

        RemoveExclusion(go);
    }

    private static void RemoveExclusion(GameObject go)
    {
        foreach (var exclude in go.GetComponentsInChildren<StencilLighting_ExcludeSprite>(true))
            if (exclude != null) Object.DestroyImmediate(exclude);

        // Backwards: destroying a parent mid-walk would leave the array holding dead entries.
        var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
        for (var i = renderers.Length - 1; i >= 0; i--)
        {
            var renderer = renderers[i];
            if (renderer != null && renderer.name == RendererName)
                Object.DestroyImmediate(renderer.gameObject);
        }
    }

    // Read once, off the crystal tree's own prefab, so this works whether or not the map happens to
    // contain one. Blocking, the way the game's own placement-object loader is: it happens on a
    // click in the editor, never in play.
    private static Material DonorMaterial()
    {
        if (_lookedForDonor) return _donorMaterial;
        _lookedForDonor = true;

        try
        {
            var info = StructuresData.GetInfoByType(Donor, 0);
            var path = info?.PrefabPath;
            if (string.IsNullOrEmpty(path))
            {
                Plugin.Log.LogWarning("MapEditor: the crystal tree has no prefab, so nothing can " +
                                      "be made see-through.");
                return null;
            }

            if (!path.Contains("Assets")) path = "Assets/" + path + ".prefab";

            var prefab = UnityEngine.AddressableAssets.Addressables
                .LoadAssetAsync<GameObject>(path).WaitForCompletion();

            var renderer = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>(true) : null;
            _donorMaterial = renderer != null ? renderer.sharedMaterial : null;

            Plugin.Log.LogInfo(_donorMaterial == null
                ? "MapEditor: the crystal tree's material could not be read."
                : $"MapEditor: see-through borrows the crystal tree's material " +
                  $"('{_donorMaterial.shader?.name}').");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: the crystal tree's material could not be loaded: " +
                                  e.Message);
        }

        return _donorMaterial;
    }
}
