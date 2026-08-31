using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class SeeThroughState : MonoBehaviour
{
    public readonly List<SpriteRenderer> Renderers = [];
    public readonly List<Material> Originals = [];

    public bool Player;
    public bool Fog;
    public bool Wind;
}

public static class SeeThrough
{
    private const StructureBrain.TYPES Donor = StructureBrain.TYPES.DECORATION_CRYSTAL_TREE;

    private const string RendererName = "ExclusionRenderer";

    private const string FogProperty = "_FadeIntoWoods";
    private const string FogKeyword = "_FADEINTOWOODS_ON";

    // The grass sway: the UberShader's vertex animation, the same switch the game's own grass
    // materials ship with. Strength and direction are global (_WindSpeed, _WindDensity,
    // _WindDiection - the game's own spelling), set per biome, so this is on or off, nothing more.
    private const string WindProperty = "_AnimateVertex";
    private const string WindKeyword = "_ANIMATEVERTEX_ON";
    private const string WindTextureProperty = "_WindTexture";

    private const string GrassDonorPath = "Assets/Prefabs/Grass/Dungeon1_Shrub_Grass.prefab";

    private static Material _donorMaterial;
    private static bool _lookedForDonor;

    private static Material _windDonor;
    private static bool _lookedForWindTexture;

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

    public static bool IsWind(GameObject go)
    {
        var state = go != null ? go.GetComponent<SeeThroughState>() : null;
        return state != null && state.Wind;
    }

    public static void Set(GameObject go, bool player, bool fog, bool wind = false,
        float amount = 0.7f)
    {
        if (go == null) return;

        if (!player && !fog && !wind)
        {
            Remove(go);
            return;
        }

        // See-through and wind share a renderer happily: see-through swaps in the donor material,
        // wind is then set on whatever material the renderer ended up with. The one rule is that a
        // see-through sprite is never lent a different shader, since that is the see-through.

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
        else
        {
            // A structure's brain adds sprites of its own after it is placed, so the list captured
            // on the first pass goes stale. Pick up whatever has appeared since.
            foreach (var renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null || renderer.name == RendererName) continue;
                if (state.Renderers.Contains(renderer)) continue;

                state.Renderers.Add(renderer);
                state.Originals.Add(renderer.sharedMaterial);
            }
        }

        var donor = player ? DonorMaterial() : null;
        if (player && donor == null) player = false;

        var swayed = 0;
        var skipped = new List<string>();

        for (var i = 0; i < state.Renderers.Count; i++)
        {
            var renderer = state.Renderers[i];
            if (renderer == null) continue;

            renderer.sharedMaterial = player ? donor : state.Originals[i];

            if (!fog && !wind) continue;

            var instance = renderer.material;
            if (instance == null) continue;

            if (fog && instance.HasProperty(FogProperty))
            {
                instance.SetFloat(FogProperty, 1f);
                instance.EnableKeyword(FogKeyword);
            }

            if (!wind) continue;

            // A structure built from several sprites often mixes materials, and only some of them
            // are on a shader that can sway. Lend the others the grass shader rather than leaving
            // half the object standing still - unless this sprite is see-through, whose material is
            // the whole point and must not be swapped out from under it.
            if (!instance.HasProperty(WindProperty) && (player || !TryLendWindShader(instance)))
            {
                skipped.Add($"{renderer.name} ('{instance.shader?.name}')");
                continue;
            }

            instance.SetFloat(WindProperty, 1f);
            instance.EnableKeyword(WindKeyword);

            // Setting the switch alone leaves the prop leaning but still: the sway is a product of
            // several material values the prop's own material never had a reason to fill in. Take
            // the lot from a grass material that is already swaying in this room.
            CopyWindSetup(instance);
            swayed++;
        }

        if (wind && swayed == 0)
        {
            wind = false;
            Plugin.Log.LogInfo($"MapEditor: '{go.name}' has no sprite on a shader that can sway " +
                               "(a Spine skeleton or a custom shader cannot take the wind).");
        }
        else if (wind)
        {
            WarnIfNoGlobalWind();

            if (skipped.Count > 0)
                Plugin.Log.LogInfo($"MapEditor: '{go.name}' sways on {swayed} of " +
                                   $"{state.Renderers.Count} sprite(s); still on: " +
                                   string.Join(", ", skipped));
        }

        state.Player = player;
        state.Fog = fog;
        state.Wind = wind;

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

    // ---- wind ---------------------------------------------------------------------------------

    // Everything the vertex animation reads besides the switch itself.
    private static readonly string[] WindFloats =
        ["_Vertex_Offset_X", "_Vertex_Offset_Y", "_Vertex_Flip_Mask", "_WindIntensity",
         "_WindDensity", "_Cutoff"];

    private static readonly string[] WindTextures = [WindTextureProperty, "_VertexMask"];

    // _Vertex_Offset_X/Y are toggle properties: the shader branches on the keyword, not on the
    // float, so setting the number alone leaves half the passes standing still.
    private static readonly string[] WindKeywords =
        [WindKeyword, "_VERTEX_OFFSET_X_ON", "_VERTEX_OFFSET_Y_ON"];

    private static void CopyWindSetup(Material instance)
    {
        if (instance == null) return;

        var donor = WindDonor();
        if (donor != null)
        {
            foreach (var name in WindFloats)
                if (instance.HasProperty(name) && donor.HasProperty(name))
                    instance.SetFloat(name, donor.GetFloat(name));

            foreach (var name in WindTextures)
            {
                if (!instance.HasProperty(name) || !donor.HasProperty(name)) continue;

                var texture = donor.GetTexture(name);
                if (texture != null) instance.SetTexture(name, texture);
            }
        }

        // These are the switches that actually displace the vertices, and a donor material picked
        // out of whatever room you happen to be in may not have them on. Set them outright rather
        // than inheriting somebody else's state.
        foreach (var keyword in WindKeywords) instance.EnableKeyword(keyword);

        if (instance.HasProperty("_Vertex_Offset_X")) instance.SetFloat("_Vertex_Offset_X", 1f);
        if (instance.HasProperty("_Vertex_Offset_Y")) instance.SetFloat("_Vertex_Offset_Y", 1f);

        // Sprite facing rebuilds the quad to face the camera, and it does that after the wind has
        // moved the vertices - so the main pass snaps back while the passes that skip the step (the
        // silhouette) keep swaying. Every material in the game that visibly sways is missing this
        // keyword; every one that stays still has it.
        foreach (var keyword in FacingKeywords) instance.DisableKeyword(keyword);
        if (instance.HasProperty(FacingProperty)) instance.SetFloat(FacingProperty, 0f);
    }

    private const string FacingProperty = "_IgnoreSpriteFacing";

    private static readonly string[] FacingKeywords =
        ["_IGNORESPRITEFACING", "_IGNORESPRITEFACING_ON"];

    // An occlusion variant fades whatever wears it once the player is behind it. Fine on the grass
    // it came from, wrong on a structure that only asked to sway.
    internal static bool PlainShader(Material material) =>
        material?.shader != null &&
        material.shader.name.IndexOf("Occlusion", System.StringComparison.OrdinalIgnoreCase) < 0;

    private static bool TryLendWindShader(Material instance)
    {
        var shader = WindLendShader();
        if (instance == null || shader == null) return false;

        var previous = instance.shader;
        instance.shader = shader;

        if (instance.HasProperty(WindProperty)) return true;

        instance.shader = previous;
        return false;
    }

    private static Shader _windLendShader;
    private static bool _lookedForLendShader;

    /// <summary>
    /// The shader handed to sprites that cannot sway on their own. It has to be the donor's, since
    /// that is the one proven to move in this scene - but if the donor wears an occlusion variant,
    /// which fades its object out when the player stands behind it, look for the plain sibling of
    /// the same shader first so a structure that only asked to sway does not turn see-through.
    /// </summary>
    private static Shader WindLendShader()
    {
        if (_lookedForLendShader) return _windLendShader;
        _lookedForLendShader = true;

        var donor = WindDonor();
        if (donor?.shader == null)
        {
            Plugin.Log.LogInfo("MapEditor: nothing in this scene sways, so sprites that cannot " +
                               "sway on their own are left alone.");
            return null;
        }

        _windLendShader = PlainShader(donor) ? donor.shader : PlainSibling(donor.shader);

        if (_windLendShader == null)
        {
            _windLendShader = donor.shader;
            Plugin.Log.LogWarning($"MapEditor: only '{donor.shader.name}' sways here and it has " +
                                  "no plain sibling, so lent sprites will also fade when the " +
                                  "player is behind them.");
        }
        else
        {
            Plugin.Log.LogInfo($"MapEditor: sprites without a swaying shader borrow " +
                               $"'{_windLendShader.name}'.");
        }

        return _windLendShader;
    }

    private static Shader PlainSibling(Shader shader)
    {
        var name = shader.name;

        foreach (var suffix in new[] { "_RadialOcclusion", "-RadialOcclusion", "_Occlusion" })
        {
            var index = name.IndexOf(suffix, System.StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            var plainName = name.Remove(index, suffix.Length);

            var found = Shader.Find(plainName);
            if (found != null) return found;

            // Shader.Find only sees what the build kept; a shader in use in this scene is certain.
            foreach (var renderer in Object.FindObjectsOfType<SpriteRenderer>())
            {
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material?.shader != null && material.shader.name == plainName)
                    return material.shader;
            }
        }

        return null;
    }

    private static Material WindDonor()
    {
        if (_lookedForWindTexture) return _windDonor;
        _lookedForWindTexture = true;

        // The room is usually full of grass already, and grass in this room is provably swaying -
        // cheaper and biome-correct compared with loading an asset. Take the first one that is
        // actually swaying and leave the question of the fade to the shader that gets lent out:
        // choosing a prettier material that turns out not to move helps nobody.
        foreach (var renderer in Object.FindObjectsOfType<SpriteRenderer>())
        {
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null || !material.HasProperty(WindProperty)) continue;
            if (material.GetFloat(WindProperty) < 0.5f) continue;

            _windDonor = material;
            break;
        }

        if (_windDonor != null)
        {
            Plugin.Log.LogInfo($"MapEditor: wind copied from '{_windDonor.name}' " +
                               $"(shader '{_windDonor.shader?.name}') in this room.");
            return _windDonor;
        }

        try
        {
            var prefab = UnityEngine.AddressableAssets.Addressables
                .LoadAssetAsync<GameObject>(GrassDonorPath).WaitForCompletion();

            foreach (var renderer in prefab != null
                         ? prefab.GetComponentsInChildren<SpriteRenderer>(true)
                         : [])
            {
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material == null || !material.HasProperty(WindProperty)) continue;
                if (material.GetFloat(WindProperty) < 0.5f) continue;

                _windDonor = material;
                break;
            }

            Plugin.Log.LogInfo(_windDonor == null
                ? "MapEditor: no swaying material could be found to copy; the sway may not move."
                : $"MapEditor: wind copied from the grass prefab's '{_windDonor.name}'.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: the grass prefab could not be loaded to copy its " +
                                  "wind: " + e.Message);
        }

        return _windDonor;
    }

    /// <summary>
    /// Speed and density come from the biome, not from the material, so a scene that never set
    /// them leaves the keyword on and the sprite still - worth saying out loud, since that looks
    /// exactly like a broken checkbox.
    /// </summary>
    private static void WarnIfNoGlobalWind()
    {
        var speed = Shader.GetGlobalFloat("_WindSpeed");
        var density = Shader.GetGlobalFloat("_WindDensity");
        if (speed > 0f && density > 0f) return;

        Plugin.Log.LogWarning($"MapEditor: wind is on, but this biome's global wind is speed " +
                              $"{speed:0.##}, density {density:0.##} - nothing will visibly move " +
                              "until the biome sets them.");
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
