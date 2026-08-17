using CustomSpineLoader.APIHelper;
using I2.Loc;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Npc;

public class CustomNpcBehaviour : MonoBehaviour
{
    public CustomNpc Definition { get; private set; }

    public void Initialize(CustomNpc definition)
    {
        Definition = definition;

        // No dialogue, no prompt: an NPC with nothing to say is scenery with an idle animation.
        if (definition?.Dialogue != null)
        {
            var interaction = gameObject.AddComponent<CustomNpcInteraction>();
            interaction.Owner = this;
        }
    }
}

// The "E - Talk" prompt. Interactor scans the static Interaction list by distance, so no
// collider is needed; the base class handles prompt display, bark closing and player capture.
public class CustomNpcInteraction : Interaction
{
    public CustomNpcBehaviour Owner;

    private string _label = "Talk";

    private void Start()
    {
        // Without this, Label returns "" until the base-game tutorial building unlock, and the
        // prompt silently never appears in a dungeon.
        IgnoreTutorial = true;
        ActivateDistance = 2f;
        UpdateLocalisation();
    }

    public override void UpdateLocalisation()
    {
        base.UpdateLocalisation();
        try
        {
            var talk = LocalizationManager.GetTranslation("Interactions/Talk");
            if (!string.IsNullOrEmpty(talk)) _label = talk;
        }
        catch (System.Exception)
        {
            // The fallback label is already set.
        }
    }

    public override void GetLabel()
    {
        base.GetLabel();
        Label = _label;
    }

    public override void OnInteract(StateMachine state)
    {
        // Required: closes open barks, records the interacting player, plays the confirm SFX.
        base.OnInteract(state);

        var definition = Owner != null ? Owner.Definition : null;
        if (definition == null) return;

        // Belt and braces - the editor already suppresses Interactor.Update while open.
        if (RuntimeMapEditor.Active != null && RuntimeMapEditor.Active.IsEditing) return;

        try
        {
            definition.OnInteracted(gameObject);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Custom NPC '{definition.InternalName}' OnInteracted failed: {e.Message}");
        }

        NpcDialogueRunner.Play(definition, gameObject);
    }
}
