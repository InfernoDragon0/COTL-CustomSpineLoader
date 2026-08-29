using MMRoomGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public static class MapEditorProtection
{
    public static bool IsProtected(GameObject go)
    {
        if (go == null) return true;

        if (go.GetComponentInParent<PlayerFarming>() != null) return true;
        if (go.GetComponentInChildren<PlayerFarming>(true) != null) return true;
        if (go.GetComponentInParent<Camera>() != null) return true;

        if (go.GetComponentInParent<Door>() != null) return true;
        if (go.GetComponentInParent<RoomLockController>() != null) return true;
        if (go.GetComponentInChildren<Door>(true) != null) return true;
        if (go.GetComponentInChildren<RoomLockController>(true) != null) return true;
        if (go.GetComponentInParent<IslandConnector>() != null) return true;

        var piece = go.GetComponentInParent<IslandPiece>();
        if (piece != null && (piece.IsDoor || piece.IsEntrance)) return true;

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

    public static bool CanDelete(GameObject go)
    {
        if (go == null) return false;
        if (IsProtected(go)) return false;

        if (RuntimeMapEditor.Context != EditorContext.Base) return true;

        if (BaseDelta.IsMine(go)) return true;

        if (IsBuilding(go)) return false;

        if (go.GetComponentInParent<TownCentre>() != null) return false;
        if (go.GetComponentInChildren<TownCentre>(true) != null) return false;

        if (go.GetComponentInParent<Interaction>() != null) return false;
        if (go.GetComponentInChildren<Interaction>(true) != null) return false;

        return true;
    }

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
