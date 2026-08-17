using System;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

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

    }
}
