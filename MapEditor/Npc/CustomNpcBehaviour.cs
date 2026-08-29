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

        if (definition?.Dialogue != null)
        {
            var interaction = gameObject.AddComponent<CustomNpcInteraction>();
            interaction.Owner = this;
        }
    }
}

public class CustomNpcInteraction : Interaction
{
    public CustomNpcBehaviour Owner;

    private string _label = "Talk";

    private void Start()
    {
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
        }
    }

    public override void GetLabel()
    {
        base.GetLabel();
        Label = _label;
    }

    public override void OnInteract(StateMachine state)
    {
        base.OnInteract(state);

        var definition = Owner != null ? Owner.Definition : null;
        if (definition == null) return;

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
