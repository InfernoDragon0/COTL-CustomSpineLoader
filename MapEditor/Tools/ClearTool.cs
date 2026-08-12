using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor.Tools;

// Bulk-wipes the procedurally generated room contents, in two levels.
//
// Biome lighting, BiomeVolume and parallax are deliberately left alone: removing them makes the
// scene unreadable and they are not part of what "background objects" means here.
public class ClearTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Clear";

    private readonly RuntimeMapEditor _editor;
    private string _clearedLevel = "None";

    public ClearTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateLabel(panel, "Clear Tool", 20, TMPro.TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Player, camera and doors\nare always preserved.", 14, TMPro.TextAlignmentOptions.Center);
        ui.CreateButton(panel, "Clear Scenery", ClearScenery);
        ui.CreateButton(panel, "Clear Terrain Too", ClearTerrain);
    }

    public void OnEnter() => _editor.SetStatus("Clear tool: scenery removes props, terrain also removes sprite shapes.");
    public void OnExit() { }
    public void OnUpdate() { }

    private void ClearScenery()
    {
        var room = SceneRefs.Room;
        if (room == null)
        {
            _editor.SetStatus("No room to clear.");
            return;
        }

        var destroyed = 0;
        destroyed += DestroyChildren(room.SceneryTransform != null ? room.SceneryTransform.transform : null);
        destroyed += DestroyChildren(room.HeavyAssetsTransform);

        // Much of the biome backdrop hangs directly off the room root ("Entrance Room Dungeon
        // 1(Clone)") rather than under SceneryTransform, so it has to be swept separately.
        destroyed += ClearRoomRoot(room, includeTerrain: false);

        _clearedLevel = "Scenery";
        SceneRefs.RescanNavigation();
        _editor.SetStatus($"Cleared {destroyed} scenery object(s).");
    }

    private void ClearTerrain()
    {
        var room = SceneRefs.Room;
        if (room == null)
        {
            _editor.SetStatus("No room to clear.");
            return;
        }

        // Scenery first, so the counts below only cover terrain.
        ClearScenery();

        var destroyed = 0;

        if (room.RoomSpriteShape != null && !MapEditorProtection.IsProtected(room.RoomSpriteShape.gameObject))
        {
            Object.Destroy(room.RoomSpriteShape.gameObject);
            destroyed++;
        }

        if (room.SpriteShapeControllers != null)
        {
            foreach (var ctrl in new List<SpriteShapeController>(room.SpriteShapeControllers))
            {
                if (ctrl == null || MapEditorProtection.IsProtected(ctrl.gameObject)) continue;
                Object.Destroy(ctrl.gameObject);
                destroyed++;
            }
        }

        if (room.Pieces != null)
        {
            foreach (var piece in new List<MMRoomGeneration.IslandPiece>(room.Pieces))
            {
                if (piece == null) continue;
                if (MapEditorProtection.IsProtectedPiece(piece)) continue;
                if (MapEditorProtection.IsProtected(piece.gameObject)) continue;
                Object.Destroy(piece.gameObject);
                destroyed++;
            }
        }

        destroyed += ClearRoomRoot(room, includeTerrain: true);

        _clearedLevel = "Terrain";
        SceneRefs.RescanNavigation();
        _editor.SetStatus($"Cleared terrain: {destroyed} object(s) removed. Doors preserved.");
    }

    // Sweeps the room root itself. The structural containers are skipped: CustomTransform holds
    // the editor's own content, and the other three are cleared through their own passes.
    private static int ClearRoomRoot(MMRoomGeneration.GenerateRoom room, bool includeTerrain)
    {
        var keep = new HashSet<Transform>();
        if (room.CustomTransform != null) keep.Add(room.CustomTransform.transform);
        if (room.SceneryTransform != null) keep.Add(room.SceneryTransform.transform);
        if (room.HeavyAssetsTransform != null) keep.Add(room.HeavyAssetsTransform);
        if (room.RoomTransform != null) keep.Add(room.RoomTransform.transform);

        return ClearRecursive(room.transform, keep, includeTerrain, 0);
    }

    // A node that holds a door is not destroyed, but is descended into, so background dressing
    // sharing a parent with a door still gets removed.
    private static int ClearRecursive(Transform node, HashSet<Transform> keep, bool includeTerrain, int depth)
    {
        if (depth > 6) return 0;

        var destroyed = 0;
        for (var i = node.childCount - 1; i >= 0; i--)
        {
            var child = node.GetChild(i);
            if (child == null || keep.Contains(child)) continue;

            // Terrain shapes only go on the deeper clear.
            if (!includeTerrain && child.GetComponentInChildren<SpriteShapeController>(true) != null)
            {
                destroyed += ClearRecursive(child, keep, false, depth + 1);
                continue;
            }

            if (MapEditorProtection.IsProtected(child.gameObject))
            {
                destroyed += ClearRecursive(child, keep, includeTerrain, depth + 1);
                continue;
            }

            Object.Destroy(child.gameObject);
            destroyed++;
        }
        return destroyed;
    }

    private static int DestroyChildren(Transform parent)
    {
        if (parent == null) return 0;

        var destroyed = 0;
        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            if (MapEditorProtection.IsProtected(child)) continue;
            Object.Destroy(child);
            destroyed++;
        }
        return destroyed;
    }

    public void ContributeTo(MapData map)
    {
        map.Cleared = _clearedLevel;
    }
}
