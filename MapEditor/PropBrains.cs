using UnityEngine;

namespace CustomSpineLoader.MapEditor;

/// A prefab that carries a `Structure` expects a `StructureBrain` behind it, and every `Interaction`
/// on it reads through that brain — `Structure.Structure_Info` is literally `Brain?.Data`. The prop
/// path spawns the art only, so a placed poop has no brain, and cleaning it throws inside
/// `Interaction_Poop.OnInteract`: the `Activating` getter guards against a null brain but its setter
/// does not. Either supply the brain (in the base, where one can exist) or switch the interaction
/// off — a prompt that cannot work is worse than no prompt.
public static class PropBrains
{
    public static void Adopt(GameObject go)
    {
        if (go == null) return;

        var structures = go.GetComponentsInChildren<Structure>(true);
        if (structures == null || structures.Length == 0) return;

        foreach (var structure in structures)
        {
            if (structure == null || structure.Brain != null) continue;

            // Type is serialised on the prefab, unlike Structure_Info, which needs the brain we are
            // trying to make. GiveBrain is a no-op away from the base, which is what leaves the
            // dungeon and hub cases to the sweep below.
            if (structure.Type != StructureBrain.TYPES.NONE)
                BaseDelta.GiveBrain(structure.gameObject, structure.Type, structure.transform.position);

            if (structure.Brain != null) continue;

            Silence(structure.gameObject);
        }
    }

    private static void Silence(GameObject go)
    {
        var interactions = go.GetComponentsInChildren<Interaction>(true);
        if (interactions == null) return;

        foreach (var interaction in interactions)
        {
            if (interaction == null || !interaction.enabled) continue;
            interaction.enabled = false;
        }
    }
}
