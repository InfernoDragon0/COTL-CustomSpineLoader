using System;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// Builds non-interactive placement previews.
//
// Two modes, because the tools need opposite things from the prefab's own scripts:
//
// - disableBehaviours = false (structures, podiums): the scripts RUN, because they are what
//   builds the visuals - placement prefabs enable their renderers on wake, and the podium
//   assigns its mesh materials at runtime (a dead script left it as a pink error mesh). The
//   Interaction components are then destroyed AFTER waking: Interaction.OnDisable/OnDestroy
//   removes them from Interactor's static set, which is what stops Interactor.Update from
//   throwing NullReferenceException on a half-usable preview entry.
//
// - disableBehaviours = true (enemies): everything except Spine is switched off BEFORE waking,
//   because an enemy's scripts are AI and Health - a preview must never think, attack, or join
//   a combat team. Spine components stay enabled so the skeleton mesh initializes.
//
// The clone always starts under an INACTIVE holder so beforeWake can fix up serialized fields
// (a podium's RemoveIfNotFirstLayer self-destroy flag) before any Awake/OnEnable runs.
public static class MapEditorGhost
{
    public static GameObject Create(GameObject source, Transform editorRoot, string name,
        bool disableBehaviours, Action<GameObject> beforeWake = null)
    {
        if (source == null) return null;

        var holder = new GameObject("MapEditor_GhostHolder");
        holder.SetActive(false);
        if (editorRoot != null) holder.transform.SetParent(editorRoot, false);

        var ghost = UnityEngine.Object.Instantiate(source, holder.transform);
        ghost.name = name;

        beforeWake?.Invoke(ghost);

        if (disableBehaviours)
        {
            foreach (var behaviour in ghost.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if ((behaviour.GetType().Namespace ?? "").StartsWith("Spine")) continue;
                behaviour.enabled = false;
            }
        }

        // Out of the holder before it is destroyed; the surviving components wake here.
        ghost.transform.SetParent(null, true);
        ghost.SetActive(true);
        UnityEngine.Object.Destroy(holder);

        if (!disableBehaviours)
            StripGlobalRegistrars(ghost);

        foreach (var collider in ghost.GetComponentsInChildren<Collider2D>(true))
            collider.enabled = false;

        foreach (var body in ghost.GetComponentsInChildren<Rigidbody2D>(true))
            body.simulated = false;

        foreach (var renderer in ghost.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.color = new Color(renderer.color.r, renderer.color.g, renderer.color.b, 0.6f);

        return ghost;
    }

    // Components that publish themselves to a global on wake. The ghost has already built its
    // visuals by the time we get here, so they can go - and they must, because every one of them
    // makes some part of the game believe a real, interactive object exists.
    private static void StripGlobalRegistrars(GameObject ghost)
    {
        var hadPodium = false;

        foreach (var interaction in ghost.GetComponentsInChildren<Interaction>(true))
        {
            if (interaction == null) continue;
            hadPodium |= interaction is Interaction_WeaponSelectionPodium;

            // Immediate, so OnDisable/OnDestroy deregistration happens before Interactor's next
            // Update rather than at end of frame.
            UnityEngine.Object.DestroyImmediate(interaction);
        }

        // The podium registered itself into its static list on wake; OnDestroy does not clean
        // that list, and a dead entry breaks the doors-open check real podiums run.
        if (hadPodium)
            Interaction_WeaponSelectionPodium.Podiums.RemoveAll(p => p == null);

        // Note: PlacementObject is deliberately not touched here. Nothing the editor clones
        // carries one any more - structure previews are built from the structure's own prefab -
        // and if that ever changes, stripping it would break the clone rather than fix it: its
        // Start() is what instantiates the visual it wraps.
    }
}
