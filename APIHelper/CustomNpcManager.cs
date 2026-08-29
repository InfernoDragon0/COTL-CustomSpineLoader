using System;
using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.MapEditor.Npc;
using Spine.Unity;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CustomSpineLoader.APIHelper;

public static class CustomNpcManager
{
    public static Dictionary<string, CustomNpc> CustomNpcList { get; } = [];
    public static Dictionary<string, GameObject> CustomNpcPrefabList { get; } = [];

    public static void Add(CustomNpc npc)
    {
        if (npc == null || string.IsNullOrEmpty(npc.InternalName)) return;

        if (CustomNpcList.ContainsKey(npc.InternalName))
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}' is already registered; replacing it.");

        CustomNpcList[npc.InternalName] = npc;
        Plugin.Log.LogInfo($"Registered custom NPC '{npc.InternalName}'.");
    }

    public static IEnumerator BuildNpcPrefab(CustomNpc npc)
    {
        if (npc == null) yield break;

        AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.LoadAssetAsync<GameObject>(npc.NpcToMimic);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Custom NPC '{npc.InternalName}': mimic '{npc.NpcToMimic}' failed to load: {e.Message}");
            yield break;
        }

        yield return handle;

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Plugin.Log.LogError($"Custom NPC '{npc.InternalName}': mimic '{npc.NpcToMimic}' did not load.");
            yield break;
        }

        CustomNpcPrefabList[npc.InternalName] = handle.Result;
        Plugin.Log.LogInfo($"Custom NPC '{npc.InternalName}' prefab ready.");
    }

    public static GameObject Spawn(string internalName, Vector3 position, Transform parent = null)
    {
        if (!CustomNpcList.TryGetValue(internalName, out var npc) || npc == null)
        {
            Plugin.Log.LogWarning($"Custom NPC '{internalName}' is not registered; cannot spawn.");
            return null;
        }

        if (!CustomNpcPrefabList.TryGetValue(internalName, out var prefab) || prefab == null)
        {
            Plugin.Log.LogWarning($"Custom NPC '{internalName}' has no prefab yet (still loading?); cannot spawn.");
            return null;
        }

        if (parent == null) parent = SceneRefs.ContentRoot;

        GameObject go;
        var holder = new GameObject("CultTweaker_NpcSpawnHolder");
        holder.SetActive(false);

        try
        {
            go = UnityEngine.Object.Instantiate(prefab, holder.transform);
            go.name = "CultTweaker_Npc_" + internalName;

            StripMimicBrains(go, internalName);
            WakeBody(go);

            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.SetActive(true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Custom NPC '{internalName}' failed to instantiate: {e}");
            UnityEngine.Object.Destroy(holder);
            return null;
        }

        UnityEngine.Object.Destroy(holder);

        try
        {
            ApplySpine(go, npc);

            var behaviour = go.AddComponent<CustomNpcBehaviour>();
            behaviour.Initialize(npc);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Custom NPC '{internalName}' setup failed: {e}");
        }

        try
        {
            npc.OnSpawned(go);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom NPC '{internalName}' OnSpawned hook failed: {e.Message}");
        }

        return go;
    }

    private static void StripMimicBrains(GameObject go, string internalName)
    {
        foreach (var unit in go.GetComponentsInChildren<UnitObject>(true))
            if (unit != null) UnityEngine.Object.DestroyImmediate(unit);
        foreach (var health in go.GetComponentsInChildren<Health>(true))
            if (health != null) UnityEngine.Object.DestroyImmediate(health);

        var stripped = 0;

        for (var pass = 0; pass < 4; pass++)
        {
            var remaining = 0;
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if ((behaviour.GetType().Namespace ?? "").StartsWith("Spine")) continue;

                try
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                    stripped++;
                }
                catch (Exception)
                {
                    remaining++;
                }
            }

            if (remaining == 0) break;
        }

        if (stripped > 0)
            Plugin.Log.LogInfo($"Custom NPC '{internalName}': stripped {stripped} mimic behaviour(s).");
    }

    private static void WakeBody(GameObject go)
    {
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
            if (child.gameObject != go && !child.gameObject.activeSelf)
                child.gameObject.SetActive(true);

        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            if (renderer != null && !renderer.enabled)
                renderer.enabled = true;

        foreach (var sprite in go.GetComponentsInChildren<SpriteRenderer>(true))
            if (sprite != null)
                sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 1f);

        foreach (var collider in go.GetComponentsInChildren<Collider2D>(true))
            collider.enabled = false;
        foreach (var body in go.GetComponentsInChildren<Rigidbody2D>(true))
            body.simulated = false;
    }

    private static void ApplySpine(GameObject go, CustomNpc npc)
    {
        var spine = MainSkeleton(go);
        if (spine == null)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}': mimic has no SkeletonAnimation.");
            return;
        }

        if (npc.SpineOverride != null)
        {
            spine.skeletonDataAsset = npc.SpineOverride;
            spine.initialSkinName = string.IsNullOrEmpty(npc.SpineSkinName) ? null : npc.SpineSkinName;
            spine.Initialize(true);
            spine.skeleton?.SetToSetupPose();
            spine.Update(0f);
        }

        if (spine.skeleton != null)
        {
            spine.skeleton.A = 1f;
            spine.skeleton.R = 1f;
            spine.skeleton.G = 1f;
            spine.skeleton.B = 1f;
        }

        PlayIdle(spine, npc);
    }

    public static void PlayIdle(SkeletonAnimation spine, CustomNpc npc)
    {
        if (spine == null || npc == null) return;

        try
        {
            var animation = spine.skeleton?.Data?.FindAnimation(npc.IdleAnimation);
            if (animation != null)
                spine.AnimationState?.SetAnimation(0, npc.IdleAnimation, true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}': idle animation failed: {e.Message}");
        }
    }

    public static SkeletonAnimation MainSkeleton(GameObject go) =>
        go != null ? go.GetComponentInChildren<SkeletonAnimation>(true) : null;
}
