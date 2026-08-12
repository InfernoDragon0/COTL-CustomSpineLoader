using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

// Repositions the room's doors.
//
// Safe to move freely: Door.OnTriggerEnter2D switches on the Door's own ConnectionType field and
// a private NextRoom index, and never reads world position. The door's PlayerPosition marker is a
// child transform, so it travels with the door and the player still arrives in the right spot.
public class DoorTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Doors";

    private readonly RuntimeMapEditor _editor;

    private Door _dragging;
    private Door _selected;
    private Vector3 _dragOffset;

    // Persists across tool switches, independent of Door.Doors.
    private readonly List<Door> _knownDoors = [];
    private TMPro.TMP_Text _selectedLabel;

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
        ui.CreateLabel(panel, "Door Tool", 20, TMPro.TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Drag a door to reposition it.\nRoom links are unaffected.", 14, TMPro.TextAlignmentOptions.Center);

        _selectedLabel = ui.CreateLabel(panel, "No door selected", 15, TMPro.TextAlignmentOptions.Center)
            .GetComponent<TMPro.TMP_Text>();

        ui.CreateButton(panel, "Rotate Left 15", () => Rotate(15f));
        ui.CreateButton(panel, "Rotate Right 15", () => Rotate(-15f));
        ui.CreateButton(panel, "Rotate Left 90", () => Rotate(90f));
        ui.CreateButton(panel, "Rotate Right 90", () => Rotate(-90f));
        ui.CreateButton(panel, "Reset Rotation", ResetRotation);

        ui.CreateButton(panel, "List Doors", ListDoors);
    }

    public void OnEnter()
    {
        RememberDoors();
        BuildGizmos();
        _editor.SetStatus($"Door tool: {_knownDoors.Count} door(s) in this room. Drag to move.");
    }

    // Door.OnDisable removes the door from Door.Doors, so a door that gets deactivated for any
    // reason vanishes from that list and can never be found again - which is why it looked
    // permanently deleted. Keeping our own references lets us detect and undo it.
    private void RememberDoors()
    {
        foreach (var door in Door.Doors)
            if (door != null && !_knownDoors.Contains(door)) _knownDoors.Add(door);
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

            if (door.gameObject.activeSelf) continue;

            door.gameObject.SetActive(true);
            Plugin.Log.LogInfo($"MapEditor: re-activated {door.direction} door that had been disabled.");
        }
    }

    private void Rotate(float degrees)
    {
        if (_selected == null)
        {
            _editor.SetStatus("No door selected. Drag one first to select it.");
            return;
        }

        _selected.transform.Rotate(0f, 0f, degrees);
        UpdateSelectedLabel();
        _editor.SetStatus($"{_selected.direction} door rotated to {_selected.transform.eulerAngles.z:0.#}°.");
    }

    private void ResetRotation()
    {
        if (_selected == null)
        {
            _editor.SetStatus("No door selected.");
            return;
        }

        _selected.transform.rotation = Quaternion.identity;
        UpdateSelectedLabel();
        _editor.SetStatus($"{_selected.direction} door rotation reset.");
    }

    private void UpdateSelectedLabel()
    {
        if (_selectedLabel == null) return;
        _selectedLabel.text = _selected != null
            ? $"Selected: {_selected.direction}\nZ rot {_selected.transform.eulerAngles.z:0.#}°"
            : "No door selected";
    }

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
            if (door == null) continue;

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

    // Doors are moved through their yellow handle via EventSystem drag events, not by polling
    // Input.GetMouseButton. The polled version moved the door on any held click, so pressing a
    // toolbar button teleported the nearest door to the cursor - which is what made a door
    // appear to vanish when switching tools.
    public void BeginDoorDrag(Door door, Vector3 pointerWorld)
    {
        if (door == null) return;

        _dragging = door;
        _selected = door;
        _dragOffset = door.transform.position - pointerWorld;

        UpdateSelectedLabel();
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

        _editor.SetStatus($"Moved {_dragging.direction} door to {_dragging.transform.position}.");
        _dragging = null;
        SceneRefs.RescanNavigation();
    }

    public void SelectDoor(Door door)
    {
        _selected = door;
        UpdateSelectedLabel();
        if (door != null)
            _editor.SetStatus($"Selected {door.direction} door ({door.ConnectionType}).");
    }

    private void ListDoors()
    {
        if (_knownDoors.Count == 0)
        {
            _editor.SetStatus("No doors in this room.");
            return;
        }

        foreach (var door in _knownDoors)
        {
            if (door == null) continue;
            Plugin.Log.LogInfo($"MapEditor door: {door.direction} type={door.ConnectionType} pos={door.transform.position}");
        }
        _editor.SetStatus($"Logged {_knownDoors.Count} door(s) to the console.");
    }

    public bool IsSelected(Door door) => ReferenceEquals(door, _selected);

    public void ContributeTo(MapData map)
    {
        RememberDoors();

        map.Doors.Clear();
        foreach (var door in _knownDoors)
        {
            if (door == null) continue;
            map.Doors.Add(new MapDoorData
            {
                Direction = door.direction.ToString(),
                Position = MapEditorSerialization.V3(door.transform.position),
                RotationZ = door.transform.eulerAngles.z
            });
        }
    }
}

// Drag grip on a door's yellow marker. Using EventSystem drag events means the door only moves
// while this specific handle is being dragged, so clicks elsewhere - including on editor
// buttons - can never move it.
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
