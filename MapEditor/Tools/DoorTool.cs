using System.Collections;
using System.Collections.Generic;
using MMBiomeGeneration;
using MMRoomGeneration;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

public class DoorTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts
{
    public string Name => "Doors";

    private readonly RuntimeMapEditor _editor;

    private Door _dragging;
    private Door _selected;
    private Vector3 _dragOffset;

    // Persists across tool switches, independent of Door.Doors.
    private readonly List<Door> _knownDoors = [];

    // Doors the user deliberately removed; the revive safety net must not resurrect these.
    private readonly HashSet<Door> _removedByTool = [];

    public const string PadName = "CultTweaker_DoorPad";
    private readonly Dictionary<Door, SpriteShapeController> _pads = [];
    // Width stays inside the door's barrier collider: a wider pad let the player slip around
    // the barrier at the sides.
    private const float PadLength = 9f;
    private const float PadWidth = 3f;

    // Solid blockers for removed doors whose walkway floor is carved into the main island's
    // authored shape and therefore cannot be hidden with the door.
    private readonly Dictionary<Door, GameObject> _plugs = [];

    private readonly List<DoorGizmo> _gizmos = [];

    // Doors are large and their pivots are often off-centre, so the grab area is generous.
    private const float GrabRadius = 3f;

    private Canvas _dotCanvas;

    private class DoorGizmo
    {
        public Door Door;
        public GameObject Box;
        public GameObject Dot;
    }

    public DoorTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateLabel(panel, "All four doors are required to save.",
            16, TMPro.TextAlignmentOptions.Center);

        ui.CreateButton(panel, "Enable All Doors", () =>
        {
            var added = AddAllDoors();
            SyncDoorToggles();
            _editor.SetStatus(added > 0
                ? $"Enabled {added} door(s)."
                : "All four doors present.");
        });

        foreach (var direction in AllDirections)
        {
            var captured = direction;
            var row = ui.CreateToggle(panel, captured + " Door",
                IsDoorPresent(FindByDirection(captured)), v => SetDoor(captured, v));

            var toggle = row.GetComponent<MapEditorToggle>();
            if (toggle != null) _doorToggles[captured] = toggle;
        }
    }

    private readonly Dictionary<string, MapEditorToggle> _doorToggles = [];

    // The doors can change behind the panel's back - a blueprint load, the loader's
    // reconciliation, or Enable All - so the boxes are re-read rather than assumed.
    private void SyncDoorToggles()
    {
        foreach (var pair in _doorToggles)
        {
            if (pair.Value == null) continue;
            pair.Value.SetValue(IsDoorPresent(FindByDirection(pair.Key)), notify: false);
        }
    }

    public void OnEnter()
    {
        RememberDoors();
        BuildGizmos();
        SyncDoorToggles();
        _editor.SetStatus($"{_knownDoors.Count} door(s) in this room.");
    }

    public void RememberDoors()
    {
        foreach (var door in Door.Doors)
            if (door != null && !_knownDoors.Contains(door)) _knownDoors.Add(door);
    }

    public Door FindByDirection(string direction)
    {
        RememberDoors();
        foreach (var door in _knownDoors)
            if (door != null && door.direction.ToString() == direction) return door;
        return null;
    }

    // includeInactive matters: a hidden door's hierarchy is inactive, and the default overload
    // returns null there - which made a toggled-off door impossible to toggle back on.
    private static IslandPiece DoorIsland(Door door) =>
        door != null ? door.GetComponentInParent<IslandPiece>(true) : null;

    public static bool IsDoorPresent(Door door) =>
        door != null && door.gameObject.activeInHierarchy;

    public static readonly string[] AllDirections = ["North", "East", "South", "West"];

    public static void RefreshMovementAnchors(Door door)
    {
        if (door == null) return;

        var island = door.GetComponentInParent<IslandPiece>(true);
        var root = island != null ? island.transform : door.transform;

        foreach (var mover in root.GetComponentsInChildren<PlayerDistanceMovement>(true))
        {
            if (mover == null) continue;
            mover.StartPos = mover.objectToMove != null
                ? mover.objectToMove.transform.position
                : mover.transform.position;
        }
    }

    public List<string> MissingDirections()
    {
        var missing = new List<string>();
        foreach (var direction in AllDirections)
            if (!IsDoorPresent(FindByDirection(direction))) missing.Add(direction);
        return missing;
    }

    // Adds every door the room does not have yet, so the four-door rule is one click away.
    public int AddAllDoors()
    {
        var added = 0;
        foreach (var direction in AllDirections)
        {
            if (IsDoorPresent(FindByDirection(direction))) continue;
            if (EnsureDoor(direction, deferCollision: true) != null) added++;
        }

        if (added > 0) SceneRefs.RegenerateRoomCollision();
        BuildGizmos();
        return added;
    }

    private void SetDoor(string direction, bool present)
    {
        var existing = FindByDirection(direction);

        if (!present)
        {
            if (IsDoorPresent(existing))
            {
                RemoveDoor(existing, deferCollision: false);
                _editor.SetStatus($"{direction} door removed.");
            }
        }
        else if (!IsDoorPresent(existing))
        {
            var door = EnsureDoor(direction, deferCollision: false);
            _editor.SetStatus(door != null
                ? $"{direction} door added. Drag it into position."
                : $"Could not add a {direction} door, see log.");
        }

        BuildGizmos();
        SyncDoorToggles();
    }

    // Reactivates a previously removed door, or spawns a fresh door island for the direction.
    // Used by the toggle buttons and by the blueprint loader's reconciliation.
    public Door EnsureDoor(string direction, bool deferCollision)
    {
        var existing = FindByDirection(direction);
        if (existing != null)
        {
            _removedByTool.Remove(existing);

            var island = DoorIsland(existing);
            if (island != null && !island.gameObject.activeSelf) island.gameObject.SetActive(true);
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);

            DestroyPlug(existing);
            RefreshPad(existing, deferCollision);
            if (!deferCollision) SceneRefs.RegenerateRoomCollision();
            _editor.KeepCullingSuspended = true;
            return existing;
        }

        return SpawnDoor(direction, deferCollision);
    }

    public void RemoveDoor(Door door, bool deferCollision)
    {
        if (door == null) return;

        var island = DoorIsland(door);
        if (island != null && island.IsDoor)
        {
            island.gameObject.SetActive(false);
        }
        else
        {
            door.gameObject.SetActive(false);
            CreatePlug(door);
        }

        DestroyPad(door);

        _removedByTool.Add(door);
        if (ReferenceEquals(door, _selected)) _selected = null;

        if (!deferCollision) SceneRefs.RegenerateRoomCollision();
    }

    public void RefreshPad(Door door, bool deferCollision)
    {
        if (door == null || !door.gameObject.activeInHierarchy) return;

        var composite = SceneRefs.RoomComposite;
        if (composite == null) return;

        if (!_pads.TryGetValue(door, out var pad) || pad == null)
        {
            var shapeTool = _editor.GetTool<ShapeTool>();
            pad = shapeTool?.CreateUntrackedShape(composite.transform, PadName + "_" + door.direction);
            if (pad == null) return;

            pad.autoUpdateCollider = false;

            var edge = pad.GetComponent<EdgeCollider2D>();
            if (edge != null) Object.DestroyImmediate(edge);

            _pads[door] = pad;
        }

        var length = PadLengthFor(door);
        BuildPadSpline(pad, door, length);
        PositionPad(pad, door, length);
        BuildPadCollider(pad, door, length);

        pad.RefreshSpriteShape();
        _editor.StartCoroutine(FinalizePad(pad, deferCollision));
    }

    // Pads stay short by default - a doorway apron, not a runway. Only a doorway the loader
    // finds cut off from the floor grows, one step at a time, via ExtendPad.
    private readonly Dictionary<Door, float> _padLengths = [];
    private const float PadMaxLength = 60f;
    private const float PadGrowStep = 8f;

    private float PadLengthFor(Door door) =>
        _padLengths.TryGetValue(door, out var length) ? length : PadLength;

    // Grows one door's pad so it can reach terrain that does not meet the doorway. Returns
    // false once it is already as long as it is allowed to get.
    public bool ExtendPad(Door door, bool deferCollision)
    {
        if (door == null) return false;

        var current = PadLengthFor(door);
        if (current >= PadMaxLength) return false;

        _padLengths[door] = Mathf.Min(current + PadGrowStep, PadMaxLength);
        RefreshPad(door, deferCollision);
        return true;
    }

    // A rectangle the composite can actually merge. Splines are built in world-axis directions
    // on an unrotated transform, so the box is simply the spline's extents.
    private static void BuildPadCollider(SpriteShapeController pad, Door door, float length)
    {
        var dir = door.GetDoorDirection();
        var perp = new Vector3(-dir.y, dir.x, 0f);

        var box = pad.GetComponent<BoxCollider2D>();
        if (box == null) box = pad.gameObject.AddComponent<BoxCollider2D>();

        box.offset = Vector2.zero;
        box.size = new Vector2(
            Mathf.Abs(dir.x) * length + Mathf.Abs(perp.x) * PadWidth,
            Mathf.Abs(dir.y) * length + Mathf.Abs(perp.y) * PadWidth);
        box.enabled = true;
        box.usedByComposite = true;
    }

    private static void BuildPadSpline(SpriteShapeController pad, Door door, float length)
    {
        var dir = door.GetDoorDirection();
        var perp = new Vector3(-dir.y, dir.x, 0f);

        var spline = pad.spline;
        spline.Clear();

        var corners = new[]
        {
            -dir * (length * 0.5f) - perp * (PadWidth * 0.5f),
            dir * (length * 0.5f) - perp * (PadWidth * 0.5f),
            dir * (length * 0.5f) + perp * (PadWidth * 0.5f),
            -dir * (length * 0.5f) + perp * (PadWidth * 0.5f)
        };
        for (var i = 0; i < corners.Length; i++)
        {
            spline.InsertPointAt(i, corners[i]);
            spline.SetTangentMode(i, ShapeTangentMode.Linear);
        }
        spline.isOpenEnded = false;
    }

    private static void PositionPad(SpriteShapeController pad, Door door, float length)
    {
        var dir = door.GetDoorDirection();
        var center = door.transform.position + dir * (length * 0.5f - 1.5f);
        pad.transform.position = new Vector3(center.x, center.y, door.transform.position.z);
    }

    private IEnumerator FinalizePad(SpriteShapeController pad, bool deferCollision)
    {
        yield return null;
        if (pad == null) yield break;

        pad.RefreshSpriteShape();
        if (!deferCollision) SceneRefs.RegenerateRoomCollision();
    }

    // Guarantees every pad's box is present and merged before a load rebuilds the union.
    public void FinalizeAllPads()
    {
        foreach (var pair in _pads)
        {
            var pad = pair.Value;
            if (pad == null) continue;

            var box = pad.GetComponent<BoxCollider2D>();
            if (box == null) continue;

            box.enabled = true;
            box.usedByComposite = true;
        }
    }

    private void DestroyPad(Door door)
    {
        if (!_pads.TryGetValue(door, out var pad)) return;
        _pads.Remove(door);
        if (pad != null) Object.DestroyImmediate(pad.gameObject);
    }

    private void CreatePlug(Door door)
    {
        if (door == null || _plugs.ContainsKey(door)) return;

        var room = SceneRefs.Room;
        var parent = room != null ? room.transform : null;

        var plug = new GameObject("CultTweaker_DoorPlug_" + door.direction);
        if (parent != null) plug.transform.SetParent(parent, false);
        plug.transform.position = door.transform.position;

        var obstacles = LayerMask.NameToLayer("Obstacles");
        if (obstacles >= 0) plug.layer = obstacles;

        var dir = door.GetDoorDirection();
        var box = plug.AddComponent<BoxCollider2D>();
        box.size = Mathf.Abs(dir.x) > 0.5f ? new Vector2(2.5f, PadWidth + 2f) : new Vector2(PadWidth + 2f, 2.5f);

        _plugs[door] = plug;
    }

    private void DestroyPlug(Door door)
    {
        if (!_plugs.TryGetValue(door, out var plug)) return;
        _plugs.Remove(door);
        if (plug != null) Object.DestroyImmediate(plug);
    }

    // The loader's second half of reconciliation: any live door whose direction the blueprint
    // does not list gets hidden, floor patch and all.
    public void RemoveDoorsNotIn(HashSet<string> directions)
    {
        RememberDoors();
        foreach (var door in _knownDoors)
        {
            if (!IsDoorPresent(door)) continue;
            if (directions.Contains(door.direction.ToString())) continue;
            RemoveDoor(door, deferCollision: true);
            Plugin.Log.LogInfo($"MapEditor: {door.direction} door hidden - not part of the blueprint.");
        }
    }

    // Spawns the vanilla door island prefab for the direction: the same object the generator
    // places, so it brings the door trigger, lock controller and walkable floor rectangle.
    private Door SpawnDoor(string direction, bool deferCollision)
    {
        var room = SceneRefs.Room;
        if (room == null) return null;

        if (!System.Enum.TryParse<IslandConnector.Direction>(direction, out var dir))
        {
            Plugin.Log.LogWarning($"MapEditor: unknown door direction '{direction}'.");
            return null;
        }

        IslandPiece prefab = null;
        try
        {
            prefab = room.GetDirectionDoor(dir, GenerateRoom.ConnectionTypes.True);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: door prefab load failed for {direction}: {e.Message}");
        }
        if (prefab == null) return null;

        var parent = room.RoomTransform != null ? room.RoomTransform.transform : room.transform;
        var island = Object.Instantiate(prefab.gameObject, parent);
        island.transform.position = DefaultDoorPosition(dir);

        var piece = island.GetComponent<IslandPiece>();
        if (piece != null && room.Pieces != null) room.Pieces.Add(piece);
        // Vanilla hides every island's authored placeholder sprites during generation; a freshly
        // instantiated door island would otherwise show its flat green editor fill.
        piece?.HideSprites();

        var door = island.GetComponentInChildren<Door>(true);
        if (door == null)
        {
            Plugin.Log.LogWarning($"MapEditor: spawned {direction} door island has no Door component.");
            Object.Destroy(island);
            return null;
        }

        // Set directly, never via Door.Init: Init dereferences the dungeon graph's neighbor
        // entry for this direction, which does not exist for a door the graph never planned.
        door.ConnectionType = GenerateRoom.ConnectionTypes.True;

        if (!HasGraphNeighbor(direction) && door.RoomLockController != null)
        {
            try
            {
                door.RoomLockController.gameObject.SetActive(true);
                door.RoomLockController.DoorUp();
                Plugin.Log.LogInfo($"MapEditor: {direction} door has no room behind it yet; barrier raised.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: could not raise {direction} door barrier: {e.Message}");
            }
        }

        _knownDoors.Add(door);
        _editor.KeepCullingSuspended = true;
        RefreshPad(door, deferCollision);
        if (!deferCollision) SceneRefs.RegenerateRoomCollision();
        return door;
    }

    private static Vector3 DefaultDoorPosition(IslandConnector.Direction dir)
    {
        var composite = SceneRefs.RoomComposite;
        var bounds = composite != null && composite.pathCount > 0
            ? composite.bounds
            : new Bounds(Vector3.zero, new Vector3(20f, 20f, 0f));

        return dir switch
        {
            IslandConnector.Direction.North => new Vector3(bounds.center.x, bounds.max.y, 0f),
            IslandConnector.Direction.East => new Vector3(bounds.max.x, bounds.center.y, 0f),
            IslandConnector.Direction.South => new Vector3(bounds.center.x, bounds.min.y, 0f),
            _ => new Vector3(bounds.min.x, bounds.center.y, 0f)
        };
    }

    public int SealDoorsWithoutNeighbours()
    {
        // Before the biome knows which room the player is in, "no neighbour" is meaningless and
        // sealing on it would brick every door in the room.
        if (BiomeGenerator.Instance == null || BiomeGenerator.Instance.CurrentRoom == null) return 0;

        RememberDoors();

        var sealed_ = 0;
        foreach (var door in _knownDoors)
        {
            if (!IsDoorPresent(door)) continue;

            var type = door.ConnectionType;
            if (type != GenerateRoom.ConnectionTypes.True && type != GenerateRoom.ConnectionTypes.False)
                continue;

            var direction = door.direction.ToString();
            if (HasGraphNeighbor(direction))
            {
                // A door the walk does connect must be usable, even if a previous room sealed it.
                if (type == GenerateRoom.ConnectionTypes.False)
                    door.ConnectionType = GenerateRoom.ConnectionTypes.True;
                continue;
            }

            door.ConnectionType = GenerateRoom.ConnectionTypes.False;
            sealed_++;

            if (door.RoomLockController == null) continue;
            try
            {
                door.RoomLockController.gameObject.SetActive(true);
                door.RoomLockController.DoorUp();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: could not raise the {direction} barrier: {e.Message}");
            }
        }

        if (sealed_ > 0)
            Plugin.Log.LogInfo($"MapEditor: sealed {sealed_} door(s) with no room behind them.");
        return sealed_;
    }

    private static bool HasGraphNeighbor(string direction)
    {
        var current = BiomeGenerator.Instance != null ? BiomeGenerator.Instance.CurrentRoom : null;
        if (current == null) return false;

        var connection = direction switch
        {
            "North" => current.N_Room,
            "East" => current.E_Room,
            "South" => current.S_Room,
            "West" => current.W_Room,
            _ => null
        };
        return connection != null && connection.Room != null &&
               connection.ConnectionType != GenerateRoom.ConnectionTypes.False;
    }

    // Something in the room pipeline still deactivates a repositioned door. Rather than chase
    // every path that can do it, the tool restores any door it finds switched off.
    private void ReviveDisabledDoors()
    {
        for (var i = _knownDoors.Count - 1; i >= 0; i--)
        {
            var door = _knownDoors[i];
            if (door == null)
            {
                _knownDoors.RemoveAt(i);
                continue;
            }

            // Deliberately removed doors stay removed.
            if (_removedByTool.Contains(door)) continue;
            if (door.gameObject.activeSelf) continue;

            door.gameObject.SetActive(true);
            Plugin.Log.LogInfo($"MapEditor: re-activated {door.direction} door that had been disabled.");
        }
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Drag a door"),
        ("LMB", "Click to select")
    ];

    public void OnExit()
    {
        _dragging = null;
        ClearGizmos();
    }

    // Each door gets the same cyan box and yellow centre dot the select tool uses, so selection
    // reads the same everywhere.
    private void BuildGizmos()
    {
        ClearGizmos();

        foreach (var door in _knownDoors)
        {
            if (!IsDoorPresent(door)) continue;

            var box = MapEditorGizmos.CreateSelectionBox(door.gameObject, "MapEditor_DoorBox_" + door.direction);
            var dot = CreateDot(door);
            _gizmos.Add(new DoorGizmo { Door = door, Box = box, Dot = dot });
        }
    }

    private GameObject CreateDot(Door door)
    {
        var go = new GameObject("MapEditor_DoorDot_" + door.direction);
        go.transform.SetParent(DotRoot(), false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(26f, 26f);

        var img = go.AddComponent<Image>();
        img.color = MapEditorGizmos.GripColour;

        var handle = go.AddComponent<DoorDragHandle>();
        handle.Initialize(this, _editor, door);

        // Direction letter above the grip, so it is obvious which way each door leads.
        var label = _editor.UI.CreateLabel(rt, door.direction.ToString().Substring(0, 1), 18,
            TMPro.TextAlignmentOptions.Center);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.5f);
        labelRt.anchoredPosition = new Vector2(0f, 28f);
        labelRt.sizeDelta = new Vector2(40f, 24f);

        // Also stops the click reaching any other tool's world handling.
        _editor.RegisterUiBlocker(rt);
        return go;
    }

    private Transform DotRoot()
    {
        if (_dotCanvas != null) return _dotCanvas.transform;

        var go = new GameObject("MapEditor_DoorHandles");
        go.transform.SetParent(_editor.transform, false);

        _dotCanvas = go.AddComponent<Canvas>();
        _dotCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _dotCanvas.sortingOrder = 5001;
        go.AddComponent<GraphicRaycaster>();
        return go.transform;
    }

    private void ClearGizmos()
    {
        foreach (var g in _gizmos)
        {
            if (g.Box != null) Object.Destroy(g.Box);
            if (g.Dot != null) Object.Destroy(g.Dot);
        }
        _gizmos.Clear();
    }

    private void SyncGizmos()
    {
        var cam = SceneRefs.Cam;

        foreach (var g in _gizmos)
        {
            if (g.Door == null) continue;

            MapEditorGizmos.UpdateSelectionBox(g.Box, g.Door.gameObject);

            var highlighted = ReferenceEquals(g.Door, _dragging);
            if (g.Box != null)
            {
                var line = g.Box.GetComponent<LineRenderer>();
                if (line != null)
                    line.startColor = line.endColor =
                        highlighted ? MapEditorGizmos.GripColour : MapEditorGizmos.BoxColour;
            }

            if (g.Dot == null || cam == null) continue;
            g.Dot.GetComponent<RectTransform>().position =
                cam.WorldToScreenPoint(MapEditorGizmos.GripPosition(g.Door.gameObject));
        }
    }

    public void OnUpdate()
    {
        ReviveDisabledDoors();
        SyncGizmos();
    }

    public void BeginDoorDrag(Door door, Vector3 pointerWorld)
    {
        if (door == null) return;

        _dragging = door;
        _selected = door;
        _dragOffset = door.transform.position - pointerWorld;

        _editor.SetStatus($"Dragging {door.direction} door ({door.ConnectionType}).");
    }

    public void DragDoorTo(Vector3 pointerWorld)
    {
        if (_dragging == null) return;

        _dragging.transform.position = pointerWorld + _dragOffset;

        // A door moved out of its original culling area is deactivated when culling resumes.
        _editor.KeepCullingSuspended = true;
    }

    public void EndDoorDrag()
    {
        if (_dragging == null) return;

        _editor.SetStatus($"Moved {_dragging.direction} door.");

        // The pad follows the door so the doorway stays walkable wherever it was dropped.
        RefreshPad(_dragging, deferCollision: false);
        // ...and the door's own slide effect re-anchors, or it would drift back towards where
        // the room generated it the next time the player walks up to it.
        RefreshMovementAnchors(_dragging);

        _dragging = null;
        SceneRefs.RescanNavigation();
    }

    public void SelectDoor(Door door)
    {
        _selected = door;
        if (door != null)
            _editor.SetStatus($"Selected {door.direction} door ({door.ConnectionType}).");
    }

    public bool IsSelected(Door door) => ReferenceEquals(door, _selected);

    public void ContributeTo(CTNodeBlueprint map)
    {
        RememberDoors();

        map.Doors.Clear();
        foreach (var door in _knownDoors)
        {
            // Only doors that are actually present: absence from the blueprint is what tells the
            // loader to hide that direction's door (and its ground shape) on load.
            if (!IsDoorPresent(door)) continue;
            map.Doors.Add(new MapDoorData
            {
                Direction = door.direction.ToString(),
                Position = MapEditorSerialization.V3(door.transform.position),
                RotationZ = door.transform.eulerAngles.z
            });
        }
    }
}

public class DoorDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    private DoorTool _tool;
    private RuntimeMapEditor _editor;
    private Door _door;

    public void Initialize(DoorTool tool, RuntimeMapEditor editor, Door door)
    {
        _tool = tool;
        _editor = editor;
        _door = door;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null) return;
        _tool.BeginDoorDrag(_door, _editor.ScreenToWorld(eventData.position));
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_tool == null || _editor == null) return;
        _tool.DragDoorTo(_editor.ScreenToWorld(eventData.position));
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _tool?.EndDoorDrag();
    }

    // A click without a drag just selects, so rotation buttons have a target.
    public void OnPointerClick(PointerEventData eventData)
    {
        _tool?.SelectDoor(_door);
    }
}
