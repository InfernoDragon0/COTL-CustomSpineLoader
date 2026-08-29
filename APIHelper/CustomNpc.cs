using CustomSpineLoader.MapEditor.Npc;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public abstract class CustomNpc
{
    public abstract string InternalName { get; }

    public virtual string DisplayName => InternalName;

    public virtual string NpcToMimic => "Assets/Prefabs/NPC/GhostChildrenNPC/GhostLostLamb.prefab";

    public SkeletonDataAsset SpineOverride;
    public string SpineSkinName = "";

    public virtual string IdleAnimation => "idle";
    public virtual string TalkAnimation => "talk";

    public NpcDialogue Dialogue;

    // ---- extension hooks ---------------------------------------------------------------------

    public virtual void OnSpawned(GameObject instance) { }
    public virtual void OnInteracted(GameObject instance) { }
    public virtual void OnDialogueNode(string nodeId) { }
    public virtual void OnDialogueChoice(string nodeId, int choiceIndex, string choiceId) { }
    public virtual void OnDialogueEnded(string lastNodeId) { }
}
