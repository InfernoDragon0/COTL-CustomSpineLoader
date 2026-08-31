using COTL_API.CustomStructures;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomSpineLoader.APIHelper;

/// <summary>
/// Custom structures are built from a bare sprite, and a bare sprite casts nothing: Unity defaults
/// a SpriteRenderer to <see cref="ShadowCastingMode.Off"/>, and the stock sprite shader has no
/// ShadowCaster pass to run even when it is switched on. Vanilla structures are authored with both.
/// This gives a custom structure the same two things.
/// </summary>
public static class StructureShadows
{
    private const string ShadowPass = "ShadowCaster";
    private const string ShadowKeyword = "_SHADOWCAST_ON";
    private const string ShadowProperty = "_ShadowCastOn";

    private static Material _donor;
    private static bool _lookedForDonor;

    public static void TryEnable(GameObject root, StructureBrain.TYPES type)
    {
        if (root == null) return;
        if (!CustomStructureManager.CustomStructureList.ContainsKey(type)) return;

        Enable(root);
    }

    public static void Enable(GameObject root)
    {
        if (root == null) return;

        var lit = 0;
        var lent = 0;
        var stuck = 0;

        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer == null || renderer.sprite == null) continue;

            var material = renderer.sharedMaterial;

            if (!Casts(material))
            {
                if (LendShadowShader(renderer)) lent++;
                else { stuck++; continue; }

                material = renderer.sharedMaterial;
            }

            if (material != null)
            {
                if (material.HasProperty(ShadowProperty)) material.SetFloat(ShadowProperty, 1f);
                material.EnableKeyword(ShadowKeyword);
            }

            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            lit++;
        }

        if (stuck == 0) return;

        Plugin.Log.LogInfo($"Custom Spine Loader: '{root.name}' casts shadows on {lit} sprite(s)" +
                           (lent > 0 ? $", {lent} of them lent a shadow-casting shader" : "") +
                           $"; {stuck} could not be given one.");
    }

    private static bool Casts(Material material)
    {
        if (material == null || material.shader == null) return false;

        try
        {
            return material.FindPass(ShadowPass) >= 0;
        }
        catch (System.Exception)
        {
            // Older runtimes have no FindPass; the stock sprite shader is the one that matters.
            return !material.shader.name.StartsWith("Sprites/");
        }
    }

    private static bool LendShadowShader(SpriteRenderer renderer)
    {
        var donor = Donor();
        if (donor == null || donor.shader == null) return false;

        // The material instance keeps the sprite's own colour and settings; only the shader - and
        // with it the ShadowCaster pass - comes from the donor.
        var instance = renderer.material;
        if (instance == null) return false;

        var previous = instance.shader;
        instance.shader = donor.shader;

        if (Casts(instance)) return true;

        instance.shader = previous;
        return false;
    }

    private static Material Donor()
    {
        if (_lookedForDonor) return _donor;
        _lookedForDonor = true;

        // A structure already standing in this scene and already casting is the safest source: it
        // is whatever the game itself considers a shadow-casting sprite here. Occlusion variants
        // come last - that shader fades its object out when the player is behind it, which on a
        // structure that only asked for a shadow reads as see-through.
        var best = 0;
        foreach (var renderer in Object.FindObjectsOfType<SpriteRenderer>())
        {
            if (renderer == null || renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
            if (!Casts(renderer.sharedMaterial)) continue;

            var score = MapEditor.SeeThrough.PlainShader(renderer.sharedMaterial) ? 2 : 1;
            if (score <= best) continue;

            _donor = renderer.sharedMaterial;
            best = score;
            if (score == 2) break;
        }

        if (_donor != null)
        {
            Plugin.Log.LogInfo($"Custom Spine Loader: shadows borrow '{_donor.name}' " +
                               $"(shader '{_donor.shader?.name}').");
            return _donor;
        }

        Plugin.Log.LogInfo("Custom Spine Loader: nothing in this scene casts a sprite shadow, so " +
                           "custom structures cannot borrow a shadow shader here.");
        return null;
    }
}
