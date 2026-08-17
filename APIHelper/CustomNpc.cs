using CustomSpineLoader.MapEditor.Npc;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

// The custom NPC base, modelled on COTL_API's CustomEnemy but non-combat.
//
// Differences from CustomEnemy, all deliberate:
//  - No Enemy enum and no GuidManager minting: vanilla systems key enemies by that enum, but
//    nothing vanilla ever needs to address one of our NPCs, so the registry is keyed by the
//    InternalName string - which is also what map blueprints store, so there is no unstable
//    integer anywhere.
//  - No maxHealth, no controller type, no team. The mimic prefab is a lost-lamb ghost with no
//    UnitObject/Health at all, and Spawn strips any that a different mimic might carry: an NPC
//    must never join Health.team2, or room locks would wait for it to die.
//  - SpineOverride is applied unconditionally at spawn. CustomEnemyManager only applies it
//    inside its EnemyController branch, which for an NPC (no controller) would mean never.
public abstract class CustomNpc
{
    public abstract string InternalName { get; }

    // Shown as the character name over the speech bubble. Registered as a localization term at
    // load, so this is the English text, not a term key.
    public virtual string DisplayName => InternalName;

    // The prefab cloned as the body. The lost lamb ghost is the simplest standalone Spine NPC
    // in the shipped catalog: one skeleton, no combat components, no room dependencies.
    public virtual string NpcToMimic => "Assets/Prefabs/NPC/GhostChildrenNPC/GhostLostLamb.prefab";

    // Plain fields, like CustomEnemy's: subclasses assign them from their constructor.
    public SkeletonDataAsset SpineOverride;
    public string SpineSkinName = "";

    public virtual string IdleAnimation => "idle";
    public virtual string TalkAnimation => "talk";

    // Parsed from the NPC's config.json; null means the NPC has nothing to say (the Talk
    // interaction is skipped entirely).
    public NpcDialogue Dialogue;

    // ---- extension hooks ---------------------------------------------------------------------
    // The seams for coded behaviours (quests, shops, map triggers) that JSON cannot express.
    // Dialogue routing itself is data-driven; these fire alongside it.

    public virtual void OnSpawned(GameObject instance) { }
    public virtual void OnInteracted(GameObject instance) { }
    public virtual void OnDialogueNode(string nodeId) { }
    public virtual void OnDialogueChoice(string nodeId, int choiceIndex, string choiceId) { }
    public virtual void OnDialogueEnded(string lastNodeId) { }
}
