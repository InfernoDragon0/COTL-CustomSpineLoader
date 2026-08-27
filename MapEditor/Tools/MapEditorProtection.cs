using MMRoomGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public static class MapEditorProtection
{
    public static bool IsProtected(GameObject go)
    {
        if (go == null) return true;

        // Player and camera. The player is also checked downwards, as a backstop: the sweeps walk a
        // room's direct children, and where the player hangs off a unit layer inside one of them,
        // testing only upwards destroys the container and the player with it. Callers that want a
        // thorough sweep move the player out of the room first rather than relying on this.
        if (go.GetComponentInParent<PlayerFarming>() != null) return true;
        if (go.GetComponentInChildren<PlayerFarming>(true) != null) return true;
        if (go.GetComponentInParent<Camera>() != null) return true;

        // Doors and room-completion logic.
        if (go.GetComponentInParent<Door>() != null) return true;
        if (go.GetComponentInParent<RoomLockController>() != null) return true;
        if (go.GetComponentInChildren<Door>(true) != null) return true;
        if (go.GetComponentInChildren<RoomLockController>(true) != null) return true;
        if (go.GetComponentInParent<IslandConnector>() != null) return true;

        // Island pieces that act as doors or the room entrance.
        var piece = go.GetComponentInParent<IslandPiece>();
        if (piece != null && (piece.IsDoor || piece.IsEntrance)) return true;

        // The editor's own objects.
        if (go.GetComponentInParent<RuntimeMapEditor>() != null) return true;
        if (go.name.StartsWith("RuntimeMapEditor")) return true;

        if (RuntimeMapEditor.Context == EditorContext.Base && IsBaseEssential(go)) return true;

        return false;
    }

    public static bool IsProtectedPiece(IslandPiece piece)
    {
        return piece == null || piece.IsDoor || piece.IsEntrance;
    }

    // ---- the base --------------------------------------------------------------------------------

    // The base is not a room the editor owns, but rearranging it is the whole point - so what is
    // off-limits here is deliberately tiny. A shrine, a temple, a bed, a dungeon door: all of them
    // can be picked up and put somewhere better. What none of them can be is *deleted*; that is
    // CanDelete's job, and it is where the player's save is actually protected.
    //
    // Only two kinds of thing refuse to be touched at all, and neither is about the save:
    //
    //   - the people. Followers and their pets walk about under their own steam, so putting one
    //     somewhere is not a thing that stays done.
    //   - the placement region. Its buildable grid is a lattice in its own local space, so moving
    //     that object slides every cell in the base out from under every building standing on one.
    //     The totem the player walks up to is a separate object and moves freely.
    private static bool IsBaseEssential(GameObject go)
    {
        if (BaseDelta.IsMine(go)) return false;

        if (go.GetComponentInParent<Follower>() != null) return true;
        if (go.GetComponentInParent<FollowerPet>() != null) return true;
        if (go.GetComponentInChildren<Follower>(true) != null) return true;

        if (go.GetComponentInParent<PlacementRegion>() != null) return true;
        if (go.GetComponentInChildren<PlacementRegion>(true) != null) return true;

        return false;
    }

    // The single answer to "may this be destroyed", asked by every path that destroys something.
    //
    // Separate from IsProtected because the two questions stopped being the same one in the base. A
    // building of the player's is selectable and movable there - that is the whole point of the base
    // editor - but it must never be deletable, whichever tool is asking and whether or not that tool
    // is currently reachable. Anything the game keeps a building record for was paid for, may be
    // holding a follower's job, and has an entry in a save file this mod does not write to; there is
    // no undo for taking one away. The build totem demolishes properly, with the refund.
    public static bool CanDelete(GameObject go)
    {
        if (go == null) return false;
        if (IsProtected(go)) return false;

        if (RuntimeMapEditor.Context != EditorContext.Base) return true;

        // What this editor placed is this editor's to remove.
        if (BaseDelta.IsMine(go)) return true;

        // Everything else in the base is the player's. Buildings were paid for and may be holding a
        // follower's job; the town centre is where anyone lost is put back; anything with an
        // interaction is furniture somebody uses. Moving all of them is fine and is what the editor
        // is for - taking one away is not, and there is no undo for it. Inert scenery is left
        // deletable, because a tree is a decoration.
        if (IsBuilding(go)) return false;

        if (go.GetComponentInParent<TownCentre>() != null) return false;
        if (go.GetComponentInChildren<TownCentre>(true) != null) return false;

        if (go.GetComponentInParent<Interaction>() != null) return false;
        if (go.GetComponentInChildren<Interaction>(true) != null) return false;

        return true;
    }

    // Anything the game would call a building. Both halves matter: the component catches one that
    // has not registered itself yet, and the registry catches one whose hierarchy puts the component
    // somewhere neither a parent walk nor a child walk would look.
    private static bool IsBuilding(GameObject go)
    {
        if (Structure(go) != null) return true;
        if (go.GetComponentInParent<StructureBrain>() != null) return true;

        foreach (var structure in global::Structure.Structures)
        {
            if (structure == null) continue;
            if (structure.transform == go.transform) return true;
            if (structure.transform.IsChildOf(go.transform)) return true;
            if (go.transform.IsChildOf(structure.transform)) return true;
        }

        return false;
    }

    private static Structure Structure(GameObject go)
    {
        var structure = go.GetComponentInParent<Structure>();
        return structure != null ? structure : go.GetComponentInChildren<Structure>(true);
    }
}
