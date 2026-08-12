using MMRoomGeneration;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// Shared rule for what must never be destroyed by the clear or delete tools.
//
// Doors are themselves IslandPieces and carry the RoomLockController, so destroying one
// soft-locks the room: it can never be completed or exited.
public static class MapEditorProtection
{
    public static bool IsProtected(GameObject go)
    {
        if (go == null) return true;

        // Player and camera.
        if (go.GetComponentInParent<PlayerFarming>() != null) return true;
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

        return false;
    }

    public static bool IsProtectedPiece(IslandPiece piece)
    {
        return piece == null || piece.IsDoor || piece.IsEntrance;
    }
}
