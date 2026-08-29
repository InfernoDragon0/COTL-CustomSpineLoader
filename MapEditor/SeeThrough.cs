using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class SeeThroughState : MonoBehaviour
{
    public readonly List<SpriteRenderer> Renderers = [];
    public readonly List<Material> Originals = [];

    public bool Player;
    public bool Fog;
}

public static class SeeThrough
{
    private const StructureBrain.TYPES Donor = StructureBrain.TYPES.DECORATION_CRYSTAL_TREE;

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

            renderer.sharedMaterial = player ? donor : state.Originals[i];

            if (!fog) continue;

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

        var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
        for (var i = renderers.Length - 1; i >= 0; i--)
        {
            var renderer = renderers[i];
            if (renderer != null && renderer.name == RendererName)
                Object.DestroyImmediate(renderer.gameObject);
        }
    }

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
