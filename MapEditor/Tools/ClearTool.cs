using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor.Tools;

public class ClearTool : IMapEditorTool
{
    public string Name => "Clear";

    private readonly RuntimeMapEditor _editor;

    public ClearTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        Arm(ui, panel, "Clear Scenery", ClearScenery);
        Arm(ui, panel, "Clear Terrain", ClearTerrain);
        Arm(ui, panel, "Clear Placed Objects", () => ClearPlaced());

        // Moved here from the Trigger tool: wiping every trigger in the room is a clearing job, and
        // it belongs with the other clearing jobs rather than at the bottom of the panel used to
        // author one trigger at a time.
        Arm(ui, panel, "Clear All Triggers", ClearTriggers, CountTriggers);
    }

    public void OnEnter() => _editor.SetStatus("Scenery removes props; terrain also removes shapes.");

    // ---- arming ---------------------------------------------------------------------------------

    // Every button on this panel asks twice, not just the one that arrived already asking. They all
    // destroy something the room cannot get back - the sweeps do not even leave an undo entry - and
    // they sit close enough together that a slip reaches the wrong one. One careful button among
    // three careless ones was the wrong half of the inconsistency to keep.
    //
    // Two presses rather than a dialog because the panel has no modal of its own: the first press
    // rewrites the button to say what it is about to do, a second within the window does it, and a
    // lapse or a change of tool puts the label back.
    private const float ArmWindow = 4f;

    private class ArmedButton
    {
        public TMPro.TMP_Text Label;
        public string Text;
        public System.Action Run;

        // How many things the press would remove, or -1 where the count is not worth working out
        // up front - a scenery sweep would have to walk the whole room to answer.
        public System.Func<int> Count;

        public float ArmedUntil;
    }

    private readonly List<ArmedButton> _armed = [];

    private void Arm(MapEditorUI ui, RectTransform panel, string text, System.Action run,
        System.Func<int> count = null)
    {
        var entry = new ArmedButton { Text = text, Run = run, Count = count };

        var button = ui.CreateButton(panel, text, () => Pressed(entry));
        entry.Label = button != null ? button.GetComponentInChildren<TMPro.TMP_Text>() : null;

        _armed.Add(entry);
    }

    private void Pressed(ArmedButton entry)
    {
        var live = entry.Count != null ? entry.Count() : -1;
        if (live == 0)
        {
            DisarmAll();
            _editor.SetStatus("Nothing to remove.");
            return;
        }

        if (entry.ArmedUntil > 0f && Time.unscaledTime <= entry.ArmedUntil)
        {
            Disarm(entry);
            entry.Run();
            return;
        }

        // One at a time: arming a second button disarms the first, so there is never more than one
        // press standing by to destroy something.
        DisarmAll();
        entry.ArmedUntil = Time.unscaledTime + ArmWindow;

        if (entry.Label != null)
            entry.Label.text = live > 0 ? $"Remove {live}? Click again" : "Sure? Click again";

        _editor.SetStatus($"Click again within {ArmWindow:0}s - this cannot be undone.",
            StatusSeverity.Warning);
    }

    private void Disarm(ArmedButton entry)
    {
        entry.ArmedUntil = 0f;
        if (entry.Label != null) entry.Label.text = entry.Text;
    }

    private void DisarmAll()
    {
        foreach (var entry in _armed) Disarm(entry);
    }

    private int CountTriggers() => _editor.GetTool<TriggerTool>()?.LiveCount() ?? 0;

    private void ClearTriggers()
    {
        var triggers = _editor.GetTool<TriggerTool>();
        if (triggers == null) return;

        // No History.Clear here, unlike the placed-objects sweep: entries whose trigger has gone
        // report false and are stepped over, so the rest of the session's undo survives.
        var removed = triggers.ClearPlaced();
        _editor.MarkEdited();
        _editor.SetStatus($"Removed {removed} trigger(s).");
    }

    // Only what the editor placed, not what the biome generated.
    public int ClearPlaced()
    {
        var removed = 0;
        removed += _editor.GetTool<StructureTool>()?.ClearPlaced() ?? 0;
        removed += _editor.GetTool<EnemyTool>()?.ClearPlaced() ?? 0;
        removed += _editor.GetTool<NpcTool>()?.ClearPlaced() ?? 0;
        removed += _editor.GetTool<TriggerTool>()?.ClearPlaced() ?? 0;

        // Everything the undo stack referred to has just been destroyed.
        _editor.History.Clear();

        SceneRefs.RescanNavigation();
        _editor.MarkEdited();
        _editor.SetStatus($"Removed {removed} placed object(s).");
        return removed;
    }
    // Leaving the panel drops the arming: a half-finished confirmation must not still be waiting
    // when the tool is opened again later.
    public void OnExit() => DisarmAll();

    public void OnUpdate()
    {
        foreach (var entry in _armed)
        {
            if (entry.ArmedUntil <= 0f || Time.unscaledTime <= entry.ArmedUntil) continue;

            Disarm(entry);
            _editor.SetStatus($"{entry.Text} cancelled.");
        }
    }

    // Public: the blueprint loader clears the whole room before rebuilding it.
    public void ClearScenery()
    {
        var room = SceneRefs.Room;
        if (room == null)
        {
            _editor.SetStatus("No room to clear.", StatusSeverity.Error);
            return;
        }

        var destroyed = 0;
        destroyed += DestroyChildren(room.SceneryTransform != null ? room.SceneryTransform.transform : null);
        destroyed += DestroyChildren(room.HeavyAssetsTransform);

        // Much of the backdrop hangs off the room root, not SceneryTransform; swept separately.
        destroyed += ClearRoomRoot(room, includeTerrain: false);

        SceneRefs.RescanNavigation();
        _editor.MarkEdited();
        _editor.SetStatus($"Cleared {destroyed} scenery object(s).");
    }

    public void ClearTerrain()
    {
        var room = SceneRefs.Room;
        if (room == null)
        {
            _editor.SetStatus("No room to clear.", StatusSeverity.Error);
            return;
        }

        // Scenery first, so the counts below only cover terrain.
        ClearScenery();

        var destroyed = 0;

        if (room.RoomSpriteShape != null && MapEditorProtection.CanDelete(room.RoomSpriteShape.gameObject))
        {
            Object.Destroy(room.RoomSpriteShape.gameObject);
            destroyed++;
        }

        if (room.SpriteShapeControllers != null)
        {
            foreach (var ctrl in new List<SpriteShapeController>(room.SpriteShapeControllers))
            {
                if (ctrl == null || !MapEditorProtection.CanDelete(ctrl.gameObject)) continue;
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
                if (!MapEditorProtection.CanDelete(piece.gameObject)) continue;
                Object.Destroy(piece.gameObject);
                destroyed++;
            }
        }

        destroyed += ClearRoomRoot(room, includeTerrain: true);

        SceneRefs.RescanNavigation();
        _editor.SetStatus($"Cleared {destroyed} terrain object(s). Doors kept.");
    }

    // Sweeps the room root; the structural containers are cleared through their own passes.
    private static int ClearRoomRoot(MMRoomGeneration.GenerateRoom room, bool includeTerrain)
    {
        var keep = new HashSet<Transform>();
        if (room.CustomTransform != null) keep.Add(room.CustomTransform.transform);
        if (room.SceneryTransform != null) keep.Add(room.SceneryTransform.transform);
        if (room.HeavyAssetsTransform != null) keep.Add(room.HeavyAssetsTransform);
        if (room.RoomTransform != null) keep.Add(room.RoomTransform.transform);

        return ClearRecursive(room.transform, keep, includeTerrain, 0);
    }

    // Protected nodes are descended into, not destroyed, so shared-parent dressing still goes.
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

            if (!MapEditorProtection.CanDelete(child.gameObject))
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
            if (!MapEditorProtection.CanDelete(child)) continue;
            Object.Destroy(child);
            destroyed++;
        }
        return destroyed;
    }
}
