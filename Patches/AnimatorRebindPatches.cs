using HarmonyLib;
using Spine;
using Spine.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class AnimatorRebindPatches
    {
        //AnimationReferenceAssets are ScriptableObjects shared between player instances (P1/P2 coop),
        //so they are never mutated - each (source, target skeleton) pair gets one cached clone instead.
        //The same source must always map to the same clone: SimpleSpineAnimator.Update compares
        //Track.Animation != NorthIdle.Animation and animationData.Animation == animationData.DefaultAnimation.
        private static readonly Dictionary<(AnimationReferenceAsset, SkeletonDataAsset), AnimationReferenceAsset> CloneCache = [];
        private static readonly Dictionary<AnimationReferenceAsset, AnimationReferenceAsset> CloneToOriginal = [];

        private static readonly List<FieldInfo> AnimationRefFields = AccessTools
            .GetDeclaredFields(typeof(SimpleSpineAnimator))
            .Where(f => f.FieldType == typeof(AnimationReferenceAsset))
            .ToList();

        //postfix so this runs after COTL_API's PlayerFarming.Start prefix has swapped skeletonDataAsset
        //and re-initialized the skeleton; also fires on the OnEnable -> Start() hot-swap path
        [HarmonyPatch(typeof(PlayerFarming), nameof(PlayerFarming.Start))]
        [HarmonyPostfix]
        private static void PlayerFarming_Start_RebindAnimations(PlayerFarming __instance)
        {
            try
            {
                var animator = __instance.simpleSpineAnimator;
                if (__instance.Spine == null || animator == null) return;

                var target = __instance.Spine.skeletonDataAsset;
                if (target == null) return;

                var targetData = target.GetSkeletonData(false);
                if (targetData == null)
                {
                    Plugin.Log.LogWarning("Animation rebind: no SkeletonData on " + target.name + ", skipping.");
                    return;
                }

                var changed = false;

                foreach (var data in animator.Animations)
                {
                    changed |= RebindRef(ref data.Animation, target, targetData);
                    changed |= RebindRef(ref data.DefaultAnimation, target, targetData);
                    changed |= RebindRef(ref data.AddAnimation, target, targetData);
                }

                foreach (var field in AnimationRefFields)
                {
                    var src = (AnimationReferenceAsset)field.GetValue(animator);
                    var rebound = Rebind(src, target, targetData);
                    if (ReferenceEquals(rebound, src)) continue;
                    field.SetValue(animator, rebound);
                    changed = true;
                }

                if (!changed) return;

                Plugin.Log.LogInfo("Rebound SimpleSpineAnimator animations to " + target.name + " for player " + __instance.playerID);
                KickAnimator(animator);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to rebind player animations: " + e);
            }
        }

        private static bool RebindRef(ref AnimationReferenceAsset field, SkeletonDataAsset target, SkeletonData targetData)
        {
            var rebound = Rebind(field, target, targetData);
            if (ReferenceEquals(rebound, field)) return false;
            field = rebound;
            return true;
        }

        private static AnimationReferenceAsset Rebind(AnimationReferenceAsset src, SkeletonDataAsset target, SkeletonData targetData)
        {
            if (src == null) return null;

            //translate a previous clone back to its original so cache keys stay (original, target)
            //and reverting to the Default spine restores the exact original assets
            var source = CloneToOriginal.TryGetValue(src, out var original) ? original : src;

            if (source.skeletonDataAsset == target) return source;

            if (CloneCache.TryGetValue((source, target), out var cached))
            {
                if (cached != null) return cached;
                CloneCache.Remove((source, target));
                CloneToOriginal.Remove(cached);
            }

            //runtime assets made by the game (ChangeStateAnimation etc) only carry a name, no animationName
            var animName = string.IsNullOrEmpty(source.animationName) ? source.name : source.animationName;
            var anim = targetData.FindAnimation(animName);
            if (anim == null)
            {
                Plugin.Log.LogWarning("Spine " + target.name + " has no animation '" + animName + "', keeping original binding (may glitch if draw order differs).");
                return source;
            }

            var clone = ScriptableObject.CreateInstance<AnimationReferenceAsset>();
            clone.name = source.name;
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.skeletonDataAsset = target;
            clone.animationName = animName;
            clone.animation = anim;

            CloneCache[(source, target)] = clone;
            CloneToOriginal[clone] = source;
            return clone;
        }

        private static void KickAnimator(SimpleSpineAnimator animator)
        {
            //COTL_API leaves the AnimationState on an empty animation after the swap, and
            //UpdateAnimFromState only fires on a state *change*, so restart the current animation here
            if (animator.state != null)
            {
                animator.UpdateAnimFromState();
                return;
            }

            var fallback = animator.DefaultLoop != null ? animator.DefaultLoop : animator.Idle;
            if (fallback == null) return;
            if (animator.anim.AnimationState.Data.SkeletonData.FindAnimation(fallback.Animation.Name) == null) return;
            animator.Track = animator.anim.AnimationState.SetAnimation(animator.AnimationTrack, fallback, loop: true);
        }
    }
}
