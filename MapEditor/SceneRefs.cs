using MMRoomGeneration;
using Pathfinding;
using UnityEngine;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor;

// Null-guarded access to the scene objects the editor drives. Every member here can legitimately
// be null (wrong scene, room not generated yet), so callers must always check.
public static class SceneRefs
{
    public static GenerateRoom Room => GenerateRoom.Instance;

    public static bool HasRoom => GenerateRoom.Instance != null;

    // Parent for everything the editor creates. GenerateRoom exposes this transform specifically
    // for content that is not part of the procedural generation, so our objects survive the
    // generator's own cleanup passes.
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

    // The room's floor collision. Island polygons are parented here with usedByComposite set,
    // and the composite merges them in Outlines mode so only the union's boundary is solid --
    // the player walks inside the island.
    public static CompositeCollider2D RoomComposite => Room != null ? Room.RoomTransform : null;

    // Rebuilds the merged floor outline. Must be called after adding or changing any collider
    // that participates in the composite.
    //
    // Defers to the generator's own SetColliderAndUpdatePathfinding, which is what the game runs
    // to finalise a room. Calling GenerateGeometry directly was not equivalent: it left the
    // geometry type at whatever the last generation step set (Polygons is used as an intermediate
    // during generation, and would make shapes solid again), skipped the Island layer assignment,
    // and rescanned A* without resizing the grid graph, so shapes extending past the original
    // room bounds got no navigation coverage.
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

    // An island collider that is enabled but not flagged usedByComposite stays a solid standalone
    // body rather than contributing to the merged outline, so the player is blocked by its filled
    // area instead of walking on it. That is what traps the player where two original islands
    // overlap. GenerateRoom.CompositeColliders does the same thing but dereferences
    // piece.Collider without a null check, so this does it defensively.
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
