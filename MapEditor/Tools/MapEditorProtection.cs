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

        return false;
    }

    public static bool IsProtectedPiece(IslandPiece piece)
    {
        return piece == null || piece.IsDoor || piece.IsEntrance;
    }
}
