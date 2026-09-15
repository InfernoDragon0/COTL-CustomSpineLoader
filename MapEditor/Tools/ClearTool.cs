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

    /// No panel of its own: clearing is a handful of one-off actions, not a mode to be in, so the
    /// Clear button in the top bar drops them as a menu and each one asks the editor to confirm.
    public void BuildPanel(RectTransform panel, MapEditorUI ui) { }

    public void OnEnter() { }

    public void OnExit() { }

    public void OnUpdate() { }

    // ---- the menu -------------------------------------------------------------------------------

    private readonly List<ClearAction> _actions = [];

    private sealed class ClearAction
    {
        public string Label;
        public string Question;
        public string Confirm;
        public System.Action Run;

        /// How many things this would remove, or -1 when the tool cannot know before it runs.
        public System.Func<int> Count;
    }

    /// <summary>
    /// The four clears as one list a bar button can drop. Each carries the question the confirm
    /// strip will ask, because the strip is the safeguard now: the buttons used to arm themselves on
    /// a first click and run on a second within four seconds, which asked the reader to notice that a
    /// label had changed and to hurry. The strip asks in words and waits.
    /// </summary>
    public List<string> ChooserOptions()
    {
        _actions.Clear();

        _actions.Add(new ClearAction
        {
            Label = "Clear scenery",
            Question = "Remove every prop and scenery piece in this room?",
            Confirm = "Clear scenery",
            Run = ClearScenery
        });

        _actions.Add(new ClearAction
        {
            Label = "Clear terrain",
            Question = "Remove the terrain and the scenery on it? Doors are kept.",
            Confirm = "Clear terrain",
            Run = ClearTerrain
        });

        _actions.Add(new ClearAction
        {
            Label = "Clear placed objects",
            Question = "Remove everything placed in this room, and empty the undo history?",
            Confirm = "Clear placed",
            Run = () => ClearPlaced()
        });

        var triggers = CountTriggers();
        _actions.Add(new ClearAction
        {
            Label = triggers > 0 ? $"Clear all triggers ({triggers})" : "Clear all triggers",
            Question = triggers > 0
                ? $"Remove all {triggers} trigger(s) in this room?"
                : "Remove all triggers in this room?",
            Confirm = "Clear triggers",
            Run = ClearTriggers,
            Count = CountTriggers
        });

        var options = new List<string>(_actions.Count);
        foreach (var action in _actions) options.Add(action.Label);
        return options;
    }

    public void ChooseFromMenu(int index)
    {
        if (index < 0 || index >= _actions.Count) return;

        var action = _actions[index];

        if (action.Count != null && action.Count() == 0)
        {
            _editor.SetStatus("Nothing to remove.");
            return;
        }

        _editor.AskConfirm(action.Question + " This cannot be undone.", action.Confirm, action.Run);
    }

    private int CountTriggers() => _editor.GetTool<TriggerTool>()?.LiveCount() ?? 0;

    private void ClearTriggers()
    {
        var triggers = _editor.GetTool<TriggerTool>();
        if (triggers == null) return;

        var removed = triggers.ClearPlaced();
        _editor.MarkEdited();
        _editor.SetStatus($"Removed {removed} trigger(s).");
    }

    public int ClearPlaced()
    {
        var removed = 0;
        removed += _editor.GetTool<StructureTool>()?.ClearPlaced() ?? 0;
        removed += _editor.GetTool<EnemyTool>()?.ClearPlaced() ?? 0;
        removed += _editor.GetTool<NpcTool>()?.ClearPlaced() ?? 0;
        removed += _editor.GetTool<TriggerTool>()?.ClearPlaced() ?? 0;

        _editor.History.Clear();

        SceneRefs.RescanNavigation();
        _editor.MarkEdited();
        _editor.SetStatus($"Removed {removed} placed object(s).");
        return removed;
    }
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

    private static int ClearRoomRoot(MMRoomGeneration.GenerateRoom room, bool includeTerrain)
    {
        var keep = new HashSet<Transform>();
        if (room.CustomTransform != null) keep.Add(room.CustomTransform.transform);
        if (room.SceneryTransform != null) keep.Add(room.SceneryTransform.transform);
        if (room.HeavyAssetsTransform != null) keep.Add(room.HeavyAssetsTransform);
        if (room.RoomTransform != null) keep.Add(room.RoomTransform.transform);

        return ClearRecursive(room.transform, keep, includeTerrain, 0);
    }

    private static int ClearRecursive(Transform node, HashSet<Transform> keep, bool includeTerrain, int depth)
    {
        if (depth > 6) return 0;

        var destroyed = 0;
        for (var i = node.childCount - 1; i >= 0; i--)
        {
            var child = node.GetChild(i);
            if (child == null || keep.Contains(child)) continue;

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
