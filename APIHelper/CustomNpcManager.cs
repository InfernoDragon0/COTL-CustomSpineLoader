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

// Mirrors COTL_API's CustomEnemyManager (Add / BuildEnemyPrefab / Spawn), with its known traps
// fixed rather than copied:
//  - the registries are PUBLIC and string-keyed, so consumers (NpcTool) read them directly
//    instead of through Harmony Traverse;
//  - Add tolerates duplicates instead of throwing;
//  - the loaded mimic asset is never mutated (all per-instance work happens on the clone);
//  - the spine override is applied unconditionally, not gated behind a controller type.
public static class CustomNpcManager
{
    public static Dictionary<string, CustomNpc> CustomNpcList { get; } = [];
    public static Dictionary<string, GameObject> CustomNpcPrefabList { get; } = [];

    public static void Add(CustomNpc npc)
    {
        if (npc == null || string.IsNullOrEmpty(npc.InternalName)) return;

        // Last registration wins: a plugin reload or a duplicate folder should log, not throw
        // halfway through Awake and take the rest of the mod down with it.
        if (CustomNpcList.ContainsKey(npc.InternalName))
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}' is already registered; replacing it.");

        CustomNpcList[npc.InternalName] = npc;
        Plugin.Log.LogInfo($"Registered custom NPC '{npc.InternalName}'.");
    }

    // Loads the mimic prefab into the prefab list. The loaded ASSET is stored, not a clone -
    // mutating it would corrupt the vanilla prefab for the rest of the session, so every
    // per-instance change lives in Spawn.
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

        // The clone starts under an INACTIVE holder, and every mimic script is destroyed before
        // it ever wakes. This is not optional tidiness: the mimic scripts key their behaviour to
        // save state - GhostNPC.Start turns the whole object off when its rescue conditions are
        // not met - and destroying them with the deferred Destroy after a live Instantiate loses
        // that race, because Awake/Start run first. The editor's ghost previews use the same
        // holder pattern, which is why the preview always showed while the placed NPC vanished.
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

    // The mimic prefab arrives with its own brain: the ghost NPC's rescue logic, its Interaction
    // (which self-registers into Interaction's static list), barks, and on other mimics possibly
    // a UnitObject/Health. All of it goes; the body that remains is the skeleton, its renderers
    // and transforms. Only Spine-namespace behaviours survive, the same allowlist the editor's
    // ghost previews use.
    //
    // Runs while the clone is still under the inactive holder, so DestroyImmediate is safe for
    // everything - no Awake has run, no registration exists to unwind.
    private static void StripMimicBrains(GameObject go, string internalName)
    {
        // A mimic with combat components would put the NPC in Health.team2, where room locks
        // wait for it to die. UnitObject first: it holds the reference to Health.
        foreach (var unit in go.GetComponentsInChildren<UnitObject>(true))
            if (unit != null) UnityEngine.Object.DestroyImmediate(unit);
        foreach (var health in go.GetComponentsInChildren<Health>(true))
            if (health != null) UnityEngine.Object.DestroyImmediate(health);

        var stripped = 0;

        // Interactions derive from MonoBehaviour, so the generic sweep takes them too.
        // Several passes: DestroyImmediate refuses to remove a component another component on
        // the same object [RequireComponent]s, so dependents have to go first.
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

    // The mimic may be authored hidden or ghost-faded (the lost lamb is literally a ghost):
    // child objects toggled off for its own state machine come back on, and renderers regain
    // full strength. The skeleton's own colour is reset separately after the spine override.
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

        // Colliders and physics have no business on scenery that talks.
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

        // Unconditional, unlike CustomEnemyManager - an NPC has no controller branch to hide in.
        if (npc.SpineOverride != null)
        {
            spine.skeletonDataAsset = npc.SpineOverride;
            spine.initialSkinName = string.IsNullOrEmpty(npc.SpineSkinName) ? null : npc.SpineSkinName;
            spine.Initialize(true);
            spine.skeleton?.SetToSetupPose();
            spine.Update(0f);
        }

        // A ghost mimic is authored translucent; the skeleton keeps that tint through a data
        // swap, so it is reset regardless of whether an override was applied.
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
            // Guarded: skeletons name their idle differently, and a missing animation must not
            // be an exception, just a static pose.
            var animation = spine.skeleton?.Data?.FindAnimation(npc.IdleAnimation);
            if (animation != null)
                spine.AnimationState?.SetAnimation(0, npc.IdleAnimation, true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom NPC '{npc.InternalName}': idle animation failed: {e.Message}");
        }
    }

    // First skeleton in the hierarchy. The mimic has no UnitObject left to ask (and NPC prefabs
    // never had the enemy controller's Spine field anyway).
    public static SkeletonAnimation MainSkeleton(GameObject go) =>
        go != null ? go.GetComponentInChildren<SkeletonAnimation>(true) : null;
}
