using MMRoomGeneration;
using Pathfinding;
using UnityEngine;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor;

public static class SceneRefs
{
    public static GenerateRoom RoomOverride { get; set; }

    public static GenerateRoom Room => RoomOverride != null ? RoomOverride : GenerateRoom.Instance;

    public static bool HasRoom => Room != null;

    public static Transform ContentRootOverride { get; set; }

    public static Transform ContentRoot
    {
        get
        {
            if (ContentRootOverride != null) return ContentRootOverride;

            var room = Room;
            if (room == null) return null;
            if (room.CustomTransform != null) return room.CustomTransform.transform;
            if (room.SceneryTransform != null) return room.SceneryTransform.transform;
            return null;
        }
    }

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

    public static CompositeCollider2D EnsureRoomComposite()
    {
        var room = Room;
        if (room == null) return null;

        if (room.RoomTransform != null) return room.RoomTransform;

        var go = new GameObject("CultTweaker_RoomCollision");
        go.transform.SetParent(room.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;

        var island = LayerMask.NameToLayer("Island");
        if (island >= 0) go.layer = island;

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

        EnsureRoomComposite();

        EnsurePiecesComposited(room);

        try
        {
            room.SetColliderAndUpdatePathfinding();
        }
        catch (System.Exception e)
        {
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
