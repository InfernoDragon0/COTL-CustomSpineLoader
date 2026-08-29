using System;
using COTL_API.CustomStructures;
using CustomSpineLoader.APIHelper;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

public static class StructureSpineHelper
{
    public static void TryAttach(GameObject root, StructureBrain.TYPES type)
    {
        if (root == null) return;
        if (!CustomStructureManager.CustomStructureList.TryGetValue(type, out var registered)) return;
        if (registered is not CultTweakerCustomStructure custom) return;
        if (custom.SpineData == null || custom.SpineConfig == null) return;

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

    private const float WorldTilt = -60f;

    private static Quaternion WorldRotationFor(StructureSpineConfig config, bool haveSprite,
        Quaternion spriteRotation)
    {
        if (config.Rotation != null) return Quaternion.Euler(config.Rotation.ToVector3());

        if (haveSprite && Quaternion.Angle(spriteRotation, Quaternion.identity) > 0.5f)
            return spriteRotation;

        return Quaternion.Euler(WorldTilt, 0f, 0f);
    }

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

public class CultTweakerStructureSpine : MonoBehaviour
{
    public SkeletonAnimation Spine;
    public string StructureName;
}
