using MMRoomGeneration;
using Pathfinding;
using UnityEngine;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor;

public static class SceneRefs
{
    public static GenerateRoom Room => GenerateRoom.Instance;

    public static bool HasRoom => GenerateRoom.Instance != null;

    public static Transform ContentRoot
    {
        get
        {
            var room = Room;
            if (room == null) return null;
            if (room.CustomTransform != null) return room.CustomTransform.transform;
            if (room.SceneryTransform != null) return room.SceneryTransform.transform;
            return null;
        }
    }

    // The biome's sprite shape profiles, used so newly spawned terrain matches the room art.
    public static GeneraterDecorations Decorations => Room != null ? Room.DecorationList : null;

    public static SpriteShape ProfileFor(ShapeProfile profile)
    {
        var deco = Decorations;
        if (deco == null) return null;
        return profile switch
        {
            ShapeProfile.Secondary => deco.SpriteShapeSecondary,
            ShapeProfile.Back => deco.SpriteShapeBack,
            _ => deco.SpriteShape
        };
    }

    public static Material ShapeMaterial => Decorations != null ? Decorations.SpriteShapeMaterial : null;

    public static CompositeCollider2D RoomComposite => Room != null ? Room.RoomTransform : null;

    public static void RegenerateRoomCollision()
    {
        var room = Room;
        if (room == null) return;

        EnsurePiecesComposited(room);

        try
        {
            room.SetColliderAndUpdatePathfinding();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: room collision rebuild failed, falling back: " + e.Message);

            var composite = RoomComposite;
            if (composite == null) return;
            composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
            composite.GenerateGeometry();
            RescanNavigation();
        }
    }

    private static void EnsurePiecesComposited(GenerateRoom room)
    {
        if (room.Pieces == null) return;

        foreach (var piece in room.Pieces)
        {
            if (piece == null) continue;

            var collider = piece.Collider;
            if (collider == null || !collider.enabled) continue;

            collider.usedByComposite = true;
        }
    }

    // Terrain layer, copied so editor-authored shapes collide like real room geometry.
    public static int TerrainLayer
    {
        get
        {
            var room = Room;
            if (room != null && room.RoomTransform != null) return room.RoomTransform.gameObject.layer;
            return LayerMask.NameToLayer("Default");
        }
    }

    public static Camera Cam => Camera.main;

    // Rebuild the A* graph so enemies path around newly added or removed geometry.
    public static void RescanNavigation()
    {
        if (AstarPath.active == null) return;
        try
        {
            AstarPath.active.Scan();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: navigation rescan failed: " + e.Message);
        }
    }
}

public enum ShapeProfile
{
    Primary,
    Secondary,
    Back
}
