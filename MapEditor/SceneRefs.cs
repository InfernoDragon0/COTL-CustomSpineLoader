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

    // A room's collision is one merged outline: colliders that belong to the composite become edges
    // to walk along, while a collider left standing on its own is a solid body that shoves whatever
    // touches it away. A generated dungeon room ships with that composite; Woolhaven's does not -
    // its buildings each carry their own baked collider instead - so a shape drawn in a hub was a
    // solid block the player got pushed out of rather than a piece of ground to stand on. One is
    // built for the room when it has none, and every tool then works there exactly as in a dungeon.
    public static CompositeCollider2D EnsureRoomComposite()
    {
        var room = Room;
        if (room == null) return null;

        // Unity's null covers a destroyed one too: the clear sweep can take the room's own composite
        // with the geometry it was built from.
        if (room.RoomTransform != null) return room.RoomTransform;

        var go = new GameObject("CultTweaker_RoomCollision");
        go.transform.SetParent(room.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;

        var island = LayerMask.NameToLayer("Island");
        if (island >= 0) go.layer = island;

        // A composite merges onto a body; static, so the room does not fall out of the world.
        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        body.simulated = true;

        var composite = go.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
        composite.generationType = CompositeCollider2D.GenerationType.Manual;

        room.RoomTransform = composite;
        Plugin.Log.LogInfo($"MapEditor: room '{room.name}' had no collision composite; built one.");
        return composite;
    }

    public static void RegenerateRoomCollision()
    {
        var room = Room;
        if (room == null) return;

        // Without one the rebuild below throws, and a room with no composite leaves every shape in
        // it solid.
        EnsureRoomComposite();

        EnsurePiecesComposited(room);

        try
        {
            room.SetColliderAndUpdatePathfinding();
        }
        catch (System.Exception e)
        {
            // The type as well as the message: the town room throws one with nothing in it, and a
            // blank warning says nothing about what went wrong.
            Plugin.Log.LogWarning($"MapEditor: room collision rebuild failed ({e.GetType().Name}: " +
                                  $"{e.Message}), falling back.");

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
