using System;
using COTL_API.CustomStructures;
using CustomSpineLoader.APIHelper;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

// Gives a custom structure a Spine skeleton instead of the flat sprite COTL_API paints onto the
// building prefab.
//
// COTL_API builds every custom structure by instantiating one vanilla prefab (Decoration Wreath
// Stick) and swapping its SpriteRenderer's sprite, and that swap is not extensible. So the sprite
// still happens - it stays the build-menu icon - and the skeleton is added afterwards as a child
// of the placed structure, with the sprite renderers switched off. Everything else about the
// structure (its brain, bounds, collapse/repair, flipping) is untouched: the skeleton is just
// another child transform, so the game's own child bookkeeping carries it along.
public static class StructureSpineHelper
{
    // Called for every structure the game places, custom or not, so the cheap checks come first.
    public static void TryAttach(GameObject root, StructureBrain.TYPES type)
    {
        if (root == null) return;
        if (!CustomStructureManager.CustomStructureList.TryGetValue(type, out var registered)) return;
        if (registered is not CultTweakerCustomStructure custom) return;
        if (custom.SpineData == null || custom.SpineConfig == null) return;

        // Placement can run more than once for the same object (a structure re-placed after a
        // location reload keeps its GameObject), and a second skeleton would draw over the first.
        if (root.GetComponentInChildren<CultTweakerStructureSpine>(true) != null) return;

        try
        {
            Attach(root, custom);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Structure '{custom.InternalName}': spine attach failed: {e}");
        }
    }

    private static void Attach(GameObject root, CultTweakerCustomStructure custom)
    {
        var config = custom.SpineConfig;

        // The first sprite renderer is the reference for everything the skeleton has to match:
        // the sorting layer (or it draws behind the ground) and the rotation (or it lies flat in
        // it - see WorldRotationFor).
        var haveSorting = false;
        var sortingLayer = 0;
        var sortingOrder = 0;
        var spriteRotation = Quaternion.identity;

        foreach (var sprite in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sprite == null) continue;
            if (!haveSorting)
            {
                sortingLayer = sprite.sortingLayerID;
                sortingOrder = sprite.sortingOrder;
                spriteRotation = sprite.transform.rotation;
                haveSorting = true;
            }

            if (config.HideSprite) sprite.enabled = false;
        }

        // Inactive first, so SkeletonAnimation's Awake runs once with the skeleton already
        // assigned rather than once empty and again on Initialize.
        var go = new GameObject("CultTweakerSpine");
        go.SetActive(false);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = config.Offset?.ToVector3() ?? Vector3.zero;
        go.transform.rotation = WorldRotationFor(config, haveSorting, spriteRotation);

        var scale = config.Scale?.ToVector3() ?? Vector3.one;
        if (scale.sqrMagnitude < 0.0001f) scale = Vector3.one;
        go.transform.localScale = scale;

        var spine = go.AddComponent<SkeletonAnimation>();
        spine.skeletonDataAsset = custom.SpineData;

        // Checked rather than assigned: Skeleton.SetSkin throws on a name the skeleton does not
        // have, and that would happen inside Initialize with the structure half-built.
        var data = custom.SpineData.GetSkeletonData(false);
        if (!string.IsNullOrEmpty(config.SkinName))
        {
            if (data?.FindSkin(config.SkinName) != null) spine.initialSkinName = config.SkinName;
            else Plugin.Log.LogWarning($"Structure '{custom.InternalName}': the skeleton has no " +
                                       $"skin '{config.SkinName}'; using its default skin.");
        }

        go.SetActive(true);
        spine.Initialize(true);
        spine.Skeleton?.SetToSetupPose();

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null && haveSorting)
        {
            renderer.sortingLayerID = sortingLayer;
            renderer.sortingOrder = sortingOrder;
        }

        PlayAnimation(spine, custom);

        var marker = go.AddComponent<CultTweakerStructureSpine>();
        marker.Spine = spine;
        marker.StructureName = custom.InternalName;

        Plugin.Log.LogInfo($"Structure '{custom.InternalName}': spine attached" +
                           (string.IsNullOrEmpty(config.SkinName) ? "" : $" (skin '{config.SkinName}')") +
                           (string.IsNullOrEmpty(config.Animation) ? "" : $" playing '{config.Animation}'") + ".");
    }

    // Cult of the Lamb is drawn on a tilt: the camera looks down at sixty degrees, and everything
    // that stands upright in the world is authored rotated -60 on X to meet it (300 in the
    // inspector, which is the same angle). Grass, projectiles and one-shot spine effects all do
    // this to themselves in the game's own code.
    //
    // A GameObject created from nothing has no rotation at all, so an unrotated skeleton lies flat
    // on the ground like a decal. The structure's own sprite is the better reference wherever
    // there is one - whatever the building does to face the camera, the skeleton then does too -
    // and the game's tilt only stands in when there is nothing to copy or the sprite was left
    // unrotated. Rotation in the config overrides both.
    private const float WorldTilt = -60f;

    private static Quaternion WorldRotationFor(StructureSpineConfig config, bool haveSprite,
        Quaternion spriteRotation)
    {
        if (config.Rotation != null) return Quaternion.Euler(config.Rotation.ToVector3());

        if (haveSprite && Quaternion.Angle(spriteRotation, Quaternion.identity) > 0.5f)
            return spriteRotation;

        return Quaternion.Euler(WorldTilt, 0f, 0f);
    }

    // An empty animation name is a legitimate answer: the structure then holds its setup pose,
    // which is what a static prop wants.
    public static void PlayAnimation(SkeletonAnimation spine, CultTweakerCustomStructure custom)
    {
        var config = custom?.SpineConfig;
        if (spine == null || config == null || string.IsNullOrEmpty(config.Animation)) return;

        try
        {
            if (spine.Skeleton?.Data?.FindAnimation(config.Animation) == null)
            {
                Plugin.Log.LogWarning($"Structure '{custom.InternalName}': the skeleton has no " +
                                      $"animation '{config.Animation}'; leaving it in its setup pose.");
                return;
            }

            spine.AnimationState?.SetAnimation(0, config.Animation, config.Loop);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Structure '{custom.InternalName}': animation " +
                                  $"'{config.Animation}' failed to start: {e.Message}");
        }
    }
}

// Marks a structure as already skinned, and gives anything that wants to drive the skeleton
// later (an interaction, a season swap) a handle on it.
public class CultTweakerStructureSpine : MonoBehaviour
{
    public SkeletonAnimation Spine;
    public string StructureName;
}
