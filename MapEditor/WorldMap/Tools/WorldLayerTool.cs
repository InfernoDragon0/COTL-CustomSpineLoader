using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.WorldMap.Tools;

// The map's art: sprite and spine layers, added from the map's folder, dragged on the canvas.
public class WorldLayerTool : IMapEditorTool, IMapEditorShortcuts
{
    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Select layer / drag"),
        ("RMB", "Select top layer here"),
        ("Ctrl+LMB", "Drag to Clone layer"),
        ("Corners", "B:Resize; Y:Rotate"),
        ("Click empty", "Clear selection"),
        ("Del", "Delete current layer")
    ];

    private readonly WorldMapEditor _editor;

    private bool _dragging;
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPosition;

    // Corner-node resize, the room editor's gesture: the scale follows how far the pointer moves
    // from the layer's centre relative to where the grab started.
    private const float MinScale = 0.05f;
    private const float MaxScale = 4f;

    private bool _scaling;
    private Vector2 _scaleCentre;
    private float _scaleStartDistance;
    private float _scaleStartValue;

    // Rotation, by the other corner: the layer turns with the pointer around its own centre.
    private bool _rotating;
    private Vector2 _rotateCentre;
    private float _rotateStartAngle;
    private float _rotateStartValue;

    public WorldLayerTool(WorldMapEditor editor) => _editor = editor;

    public string Name => "World Layers";

    public void OnEnter() { }

    public void OnExit()
    {
        _dragging = false;
        _scaling = false;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        var map = _editor.Map;
        if (map == null) return;

        ui.CreateHeader(panel, "- Layers -", 22);
        Note(ui, panel, $"From CustomWorldMaps/{map.MapName}/");

        ui.CreateButton(panel, "Refresh art list", () =>
        {
            // Failures are remembered so a missing file is not re-read every rebuild; a refresh is
            // what clears that, so a file dropped in (or fixed) since the panel was built is seen.
            WorldMapAssets.ForgetFailures();

            var sprites = ListPngs(map.MapName).Count;
            var skeletons = ListSpineFolders(map.MapName).Count;
            _editor.RebuildActivePanel();
            _editor.SetStatus($"Map folder re-read: {sprites} png(s), {skeletons} spine folder(s).");
        });

        BuildAddPickers(panel, ui, map);

        var selected = SelectedLayer();
        if (selected == null)
        {
            BuildLayerList(panel, ui);
            return;
        }

        ui.CreateHeader(panel, "- Selected: " + selected.Id + " -", 20);

        ui.CreateSlider(panel, "Parallax distance", 0f, 1f, selected.ParallaxDistance,
            value => selected.ParallaxDistance = value);

        ui.CreateToggle(panel, "Flip horizontally", selected.FlipX, value =>
        {
            selected.FlipX = value;
            ApplyTransform(selected);
        });

        if (selected.IsSpine)
        {
            var animations = WorldMapAssets.AnimationNames(_editor.Map.MapName, selected.Asset);
            if (animations.Count > 0)
            {
                Label(ui, panel, "Animation");
                var animPicker = ui.CreateDropdown(panel, "Animation", animations, (index, value) =>
                {
                    selected.Animation = value;
                    _editor.RebuildAll();
                });
                var current = animations.IndexOf(selected.Animation);
                if (current >= 0) animPicker.SetSelected(current);
                WorldMapEditor.Hint(animPicker,
                    $"Animation - {animations.Count} on this skeleton");
            }

            var skins = WorldMapAssets.SkinNames(_editor.Map.MapName, selected.Asset);
            if (skins.Count > 1)
            {
                Label(ui, panel, "Skin");
                var skinPicker = ui.CreateDropdown(panel, "Skin", skins, (index, value) =>
                {
                    selected.Skin = value;
                    _editor.RebuildAll();
                });
                var current = skins.IndexOf(selected.Skin);
                if (current >= 0) skinPicker.SetSelected(current);
                WorldMapEditor.Hint(skinPicker, $"Skin - {skins.Count} on this skeleton");
            }

            ui.CreateToggle(panel, "Loop animation", selected.Loop, value =>
            {
                selected.Loop = value;
                _editor.RebuildAll();
            });

            ui.CreateSlider(panel, "Animation speed", 0.1f, 3f, selected.TimeScale, value =>
            {
                selected.TimeScale = value;
                _editor.RebuildAll();
            });
        }

        BuildLayerList(panel, ui);
    }

    // ---- adding art ---------------------------------------------------------------------------

    // Two steps rather than one dropdown per kind, the way the trigger tool picks an action and
    // then its target: what kind of layer, then which file. The second list is filled from the
    // first pick and stays filled across panel rebuilds, so adding two sprites is two clicks the
    // second time.
    private static readonly string[] AddKinds = ["Sprite", "Spine"];

    private string _addKind;
    private MapEditorDropdown _addAssetPicker;

    private void BuildAddPickers(RectTransform panel, MapEditorUI ui, CTWorldMap map)
    {
        Label(ui, panel, "Add layer");
        var kindPicker = ui.CreateDropdown(panel, "Add layer", AddKinds, (index, _) =>
        {
            _editor.BlockWorldClicks();
            _addKind = index == 1 ? "Spine" : "Sprite";

            var options = AssetsFor(map.MapName, _addKind);
            _addAssetPicker?.SetOptions(options);

            _editor.SetStatus(options.Count > 0
                ? $"Pick which {_addKind.ToLowerInvariant()} to add."
                : _addKind == "Spine"
                    ? "No spine subfolders in the map folder (skeleton .json + .atlas + pages)."
                    : "No .png files in the map folder yet.");
        });

        if (_addKind != null)
            kindPicker.SetSelected(System.Array.IndexOf(AddKinds, _addKind));

        // Listed on every build - files can be dropped into the folder mid-session.
        var assets = _addKind != null ? AssetsFor(map.MapName, _addKind) : [];

        _addAssetPicker = ui.CreateDropdown(panel, "Select Art", assets, (index, value) =>
        {
            if (_addKind == null || index < 0 || index >= assets.Count) return;
            AddLayer(_addKind, value);
        });

        WorldMapEditor.Hint(_addAssetPicker, _addKind == null
            ? "Select Art - choose type above first"
            : $"Select Art - {assets.Count} {_addKind.ToLowerInvariant()}(s) in the map folder");
    }

    private static List<string> AssetsFor(string mapName, string kind) =>
        string.Equals(kind, "Spine", System.StringComparison.OrdinalIgnoreCase)
            ? ListSpineFolders(mapName)
            : ListPngs(mapName);

    // ---- the layer list ---------------------------------------------------------------------

    private const float RowHeight = 30f;

    // Front at the top, the way a layer stack is usually read: "+" brings a layer forward and
    // moves its row up, "X" deletes it, and the row itself selects.
    private void BuildLayerList(RectTransform panel, MapEditorUI ui)
    {
        var map = _editor.Map;

        ui.CreateHeader(panel, "- Layer order (front at top) -", 20);

        if (map.Layers.Count == 0)
        {
            Note(ui, panel, "No layers yet - add one above.");
            return;
        }

        var ordered = OrderedLayers();
        for (var i = ordered.Count - 1; i >= 0; i--) CreateLayerRow(panel, ui, ordered[i], i);
    }

    private void CreateLayerRow(RectTransform panel, MapEditorUI ui, CTWorldMapLayer layer, int depth)
    {
        var row = new GameObject("Layer_" + layer.Id);
        row.transform.SetParent(panel, false);

        var rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0f, RowHeight);

        var element = row.AddComponent<UnityEngine.UI.LayoutElement>();
        element.minHeight = RowHeight;
        element.preferredHeight = RowHeight;

        var layout = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // A list row, though it is built straight into the panel rather than into a scroll
        // box - so it says so itself, and its -/+/X wear the quiet plate the other lists' do.
        row.AddComponent<MapEditorQuietArea>();

        var plate = row.AddComponent<UnityEngine.UI.Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = UnityEngine.UI.Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.5f;

        var isSelected = string.Equals(layer.Id, _editor.SelectedLayerId,
            System.StringComparison.OrdinalIgnoreCase);
        var idle = isSelected
            ? new Color(MapEditorUI.Accent.r, MapEditorUI.Accent.g, MapEditorUI.Accent.b, 0.45f)
            : new Color(0f, 0f, 0f, 0.55f);
        plate.color = idle;

        var select = row.AddComponent<UnityEngine.UI.Button>();
        select.targetGraphic = plate;
        select.transition = UnityEngine.UI.Selectable.Transition.None;
        select.onClick.AddListener(() =>
        {
            _editor.BlockWorldClicks();
            Select(layer.Id);
            _editor.RebuildActivePanel();
        });

        var label = ui.CreateLabel(row.transform, $"{depth + 1}. {layer.Id}  ({layer.Asset})", 15);
        var labelText = label.GetComponent<TMP_Text>();
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        labelText.margin = new Vector4(8f, 0f, 0f, 0f);
        labelText.raycastTarget = false;

        MapEditorUI.AddHover(row, plate, idle, new Color(1f, 1f, 1f, 0.16f),
            $"{layer.Id} - {layer.Kind} '{layer.Asset}', parallax {layer.ParallaxDistance:0.##}" +
            (layer.FlipX ? ", flipped" : ""));

        RowButton(ui, row, "+", () => MoveLayer(layer, 1));
        RowButton(ui, row, "-", () => MoveLayer(layer, -1));
        RowButton(ui, row, "X", () => DeleteLayer(layer));
    }

    private void RowButton(MapEditorUI ui, GameObject row, string text, System.Action onClick)
    {
        var button = ui.CreateButton(row.transform, text, onClick, RowHeight - 4f);

        // CreateButton flexes to fill a column; here that would push the label out of the row.
        var element = button.GetComponent<UnityEngine.UI.LayoutElement>();
        element.preferredWidth = 30f;
        element.minWidth = 30f;
        element.flexibleWidth = 0f;
    }

    private List<CTWorldMapLayer> OrderedLayers()
    {
        var ordered = new List<CTWorldMapLayer>(_editor.Map.Layers);
        ordered.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return ordered;
    }

    private void MoveLayer(CTWorldMapLayer layer, int direction)
    {
        var ordered = OrderedLayers();
        var index = ordered.IndexOf(layer);
        var target = index + direction;

        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            _editor.SetStatus(direction > 0 ? "Already at the front." : "Already at the back.");
            return;
        }

        // Sort orders are renumbered from the list, so authored gaps and ties cannot stall a move.
        var before = new List<(CTWorldMapLayer layer, int sort)>(ordered.Count);
        foreach (var entry in ordered) before.Add((entry, entry.SortOrder));

        ordered.RemoveAt(index);
        ordered.Insert(target, layer);
        for (var i = 0; i < ordered.Count; i++) ordered[i].SortOrder = i;

        _editor.PushUndo((direction > 0 ? "bring forward " : "send back ") + layer.Id, () =>
        {
            foreach (var (entry, sort) in before) entry.SortOrder = sort;
            _editor.RebuildAll();
            _editor.RebuildActivePanel();
            return true;
        });

        _editor.RebuildAll();
        _editor.RebuildActivePanel();
    }

    private void DeleteLayer(CTWorldMapLayer layer)
    {
        var map = _editor.Map;
        var index = map.Layers.IndexOf(layer);
        if (index < 0) return;

        map.Layers.Remove(layer);
        if (string.Equals(_editor.SelectedLayerId, layer.Id, System.StringComparison.OrdinalIgnoreCase))
            _editor.SelectedLayerId = null;

        _editor.PushUndo("delete layer " + layer.Id, () =>
        {
            map.Layers.Insert(Mathf.Clamp(index, 0, map.Layers.Count), layer);
            _editor.RebuildAll();
            _editor.RebuildActivePanel();
            return true;
        });

        _editor.RebuildAll();
        _editor.RebuildActivePanel();
        _editor.SetStatus($"Deleted layer '{layer.Id}'.");
    }

    public void OnUpdate()
    {
        if (_editor.Map == null) return;

        // A drag whose release was never seen - a modal dialog stops this update for a few frames -
        // would otherwise resume against a stale anchor and fling the layer at the next press.
        if (_dragging && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) _dragging = false;

        if (HandleScaleDrag()) return;
        if (HandleRotateDrag()) return;

        // The same key the room editor deletes a selection with; the list's X buttons stay, since
        // they delete a layer that is not the selected one.
        if (Input.GetKeyDown(KeyCode.Delete))
        {
            var doomed = SelectedLayer();
            if (doomed != null) DeleteLayer(doomed);
            else _editor.SetStatus("No layer selected.");
            return;
        }

        // Right click ignores the selection's own priority and takes the front-most layer here.
        // Without it a backdrop, once selected, is under the cursor everywhere and left click can
        // never reach anything in front of it.
        if (Input.GetMouseButtonDown(1) && !_editor.PointerOverEditorUi())
        {
            var front = HitLayer(preferSelected: false);

            Select(front?.Id);
            _editor.RebuildActivePanel();
            _editor.SetStatus(front != null
                ? $"Selected '{front.Id}' - the front layer here."
                : "Nothing here to select.");
            return;
        }

        if (Input.GetMouseButtonDown(0) && !_editor.PointerOverEditorUi())
        {
            var hit = HitLayer();

            // Ctrl-click clones what is under the cursor and drags the copy off it, the way the
            // room editor's select tool clones an object.
            if (hit != null && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                hit = CloneLayer(hit);

            if (hit != null)
            {
                if (!string.Equals(hit.Id, _editor.SelectedLayerId, System.StringComparison.OrdinalIgnoreCase))
                {
                    Select(hit.Id);
                    _editor.RebuildActivePanel();
                }

                _dragging = true;
                _dragStartPointer = _editor.PointerContent();
                _dragStartPosition = new Vector2(hit.Position.X, hit.Position.Y);
            }
            else if (!string.IsNullOrEmpty(_editor.SelectedLayerId))
            {
                Select(null);
                _editor.RebuildActivePanel();
            }
        }

        var selected = SelectedLayer();
        if (selected == null) return;

        if (_dragging && Input.GetMouseButton(0))
        {
            var target = _dragStartPosition + (_editor.PointerContent() - _dragStartPointer);
            selected.Position.X = target.x;
            selected.Position.Y = target.y;

            // Moved by reference during the drag; the full redraw waits for the release.
            if (_editor.Screen.LayerRects.TryGetValue(selected.Id, out var rect) && rect != null)
                rect.anchoredPosition = target;
        }

        if (_dragging && Input.GetMouseButtonUp(0))
        {
            _dragging = false;
            var moved = new Vector2(selected.Position.X, selected.Position.Y);
            if (Vector2.Distance(moved, _dragStartPosition) > 0.5f)
            {
                var layer = selected;
                var restore = _dragStartPosition;
                _editor.PushUndo("move layer " + layer.Id, () =>
                {
                    layer.Position.X = restore.x;
                    layer.Position.Y = restore.y;
                    _editor.RebuildAll();
                    return true;
                });
            }
            _editor.RebuildAll();
        }
    }

    // True while the yellow corner node owns the mouse. The layer follows the pointer's angle
    // around its own centre, so the grab point stays under the cursor as it swings.
    private bool HandleRotateDrag()
    {
        var handle = _editor.Screen != null ? _editor.Screen.LayerRotateHandle : null;
        var selected = SelectedLayer();

        if (!_rotating)
        {
            if (handle == null || selected == null) return false;
            if (!Input.GetMouseButtonDown(0)) return false;
            if (!handle.ContainsPointer(Input.mousePosition)) return false;

            _rotating = true;
            _rotateCentre = handle.ScreenCentre;
            _rotateStartAngle = PointerAngle(_rotateCentre);
            _rotateStartValue = selected.RotationZ;
            return true;
        }

        if (selected == null || (!Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)))
        {
            _rotating = false;
            return false;
        }

        var value = Mathf.Repeat(
            _rotateStartValue + (PointerAngle(_rotateCentre) - _rotateStartAngle) + 180f, 360f) - 180f;

        selected.RotationZ = value;
        ApplyTransform(selected);
        _editor.ShowHoverStatus($"{selected.Id} rotation {value:0}");

        if (Input.GetMouseButtonUp(0))
        {
            _rotating = false;

            var layer = selected;
            var restore = _rotateStartValue;
            if (!Mathf.Approximately(restore, value))
                _editor.PushUndo("rotate layer " + layer.Id, () =>
                {
                    layer.RotationZ = restore;
                    ApplyTransform(layer);
                    _editor.RebuildActivePanel();
                    return true;
                });

            _editor.ClearHoverStatus();
            _editor.RebuildActivePanel();
        }

        return true;
    }

    private static float PointerAngle(Vector2 centre)
    {
        var delta = (Vector2)Input.mousePosition - centre;
        return Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
    }

    // True while the corner node owns the mouse, so the caller skips selecting and dragging.
    private bool HandleScaleDrag()
    {
        var handle = _editor.Screen != null ? _editor.Screen.LayerHandle : null;
        var selected = SelectedLayer();

        if (!_scaling)
        {
            if (handle == null || selected == null) return false;
            if (!Input.GetMouseButtonDown(0)) return false;
            if (!handle.ContainsPointer(Input.mousePosition)) return false;

            _scaling = true;
            _scaleCentre = handle.ScreenCentre;
            _scaleStartDistance = Mathf.Max(8f,
                Vector2.Distance(Input.mousePosition, _scaleCentre));
            _scaleStartValue = selected.Scale?.X ?? 1f;
            return true;
        }

        // The release can be missed while a modal is up; the drag must not survive it.
        if (selected == null || (!Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)))
        {
            _scaling = false;
            return false;
        }

        var distance = Vector2.Distance(Input.mousePosition, _scaleCentre);
        var value = Mathf.Clamp(_scaleStartValue * (distance / _scaleStartDistance),
            MinScale, MaxScale);

        selected.Scale ??= new SpineLoaderHelper.SerializableVector3 { X = 1f, Y = 1f, Z = 1f };
        selected.Scale.X = value;
        selected.Scale.Y = value;
        ApplyTransform(selected);
        _editor.ShowHoverStatus($"{selected.Id} scale {value:0.##}");

        if (Input.GetMouseButtonUp(0))
        {
            _scaling = false;

            var layer = selected;
            var restore = _scaleStartValue;
            if (!Mathf.Approximately(restore, value))
                _editor.PushUndo("scale layer " + layer.Id, () =>
                {
                    layer.Scale ??= new SpineLoaderHelper.SerializableVector3 { X = 1f, Y = 1f, Z = 1f };
                    layer.Scale.X = restore;
                    layer.Scale.Y = restore;
                    ApplyTransform(layer);
                    _editor.RebuildActivePanel();
                    return true;
                });

            _editor.ClearHoverStatus();
            _editor.RebuildActivePanel();
        }

        return true;
    }

    private void Select(string layerId)
    {
        _editor.SelectedLayerId = layerId;
        _editor.Screen?.HighlightLayer(layerId);
    }

    // A copy of a layer, one step in front of everything, ready to be dragged off the original.
    // Copied through JSON so a field added to the layer later is carried without editing this.
    private CTWorldMapLayer CloneLayer(CTWorldMapLayer source)
    {
        var map = _editor.Map;

        CTWorldMapLayer copy;
        try
        {
            copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CTWorldMapLayer>(
                Newtonsoft.Json.JsonConvert.SerializeObject(source));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("WorldEditor: could not clone the layer: " + e.Message);
            return source;
        }

        if (copy == null) return source;

        var maxSort = 0;
        foreach (var existing in map.Layers) maxSort = Mathf.Max(maxSort, existing.SortOrder);

        copy.Id = _editor.MintId("layer", id => map.Layers.Exists(l =>
            string.Equals(l.Id, id, System.StringComparison.OrdinalIgnoreCase)));
        copy.SortOrder = maxSort + 1;

        map.Layers.Add(copy);
        _editor.PushUndo("clone layer " + source.Id, () =>
        {
            map.Layers.Remove(copy);
            if (_editor.SelectedLayerId == copy.Id) _editor.SelectedLayerId = null;
            _editor.RebuildAll();
            _editor.RebuildActivePanel();
            return true;
        });

        // Drawn before the drag begins: the drag moves the new rect by reference.
        _editor.RebuildAll();
        _editor.SetStatus($"Cloned '{source.Id}' as '{copy.Id}'. Drag to place it.");
        return copy;
    }

    // The front-most layer under the cursor, so clicking a stack picks what the eye sees - except
    // for the selected one, which wins wherever it sits, so a layer behind others stays draggable.
    // preferSelected false is the right-click pick: pure front-most, which is the way back out when
    // the selection is a backdrop the cursor is always inside.
    private CTWorldMapLayer HitLayer(bool preferSelected = true)
    {
        CTWorldMapLayer best = null;

        foreach (var layer in _editor.Map.Layers)
        {
            if (layer == null) continue;
            if (!_editor.Screen.LayerRects.TryGetValue(layer.Id, out var rect) || rect == null) continue;
            if (!ContainsPointer(rect, layer)) continue;

            if (preferSelected &&
                string.Equals(layer.Id, _editor.SelectedLayerId, System.StringComparison.OrdinalIgnoreCase))
                return layer;

            if (best == null || layer.SortOrder >= best.SortOrder) best = layer;
        }

        return best;
    }

    private static readonly Vector3[] Corners = new Vector3[4];

    private static bool ContainsPointer(RectTransform rect, CTWorldMapLayer layer)
    {
        // On a screen-space overlay canvas world corners are already screen pixels.
        rect.GetWorldCorners(Corners);

        var min = (Vector2)Corners[0];
        var max = min;
        for (var i = 1; i < 4; i++)
        {
            min = Vector2.Min(min, Corners[i]);
            max = Vector2.Max(max, Corners[i]);
        }

        // A spine layer's rect says nothing about what it draws, and scaled to skeleton units it
        // can be a pixel across - so the grab area never shrinks below something clickable. Half
        // of the selection frame's own floor, so what is outlined is exactly what can be grabbed.
        var minHalf = WorldMapSelectionFrame.MinFor(layer) * 0.5f;

        var centre = (min + max) * 0.5f;
        var half = Vector2.Max((max - min) * 0.5f, new Vector2(minHalf, minHalf));

        var pointer = (Vector2)Input.mousePosition;
        return Mathf.Abs(pointer.x - centre.x) <= half.x && Mathf.Abs(pointer.y - centre.y) <= half.y;
    }

    private CTWorldMapLayer SelectedLayer()
    {
        if (_editor.Map == null || string.IsNullOrEmpty(_editor.SelectedLayerId)) return null;
        foreach (var layer in _editor.Map.Layers)
            if (layer != null && string.Equals(layer.Id, _editor.SelectedLayerId,
                    System.StringComparison.OrdinalIgnoreCase))
                return layer;
        return null;
    }

    private void AddLayer(string kind, string asset)
    {
        var map = _editor.Map;

        var maxSort = 0;
        foreach (var existing in map.Layers) maxSort = Mathf.Max(maxSort, existing.SortOrder);

        var layer = new CTWorldMapLayer
        {
            Id = _editor.MintId("layer", id => map.Layers.Exists(l =>
                string.Equals(l.Id, id, System.StringComparison.OrdinalIgnoreCase))),
            Kind = kind,
            Asset = asset,
            SortOrder = maxSort + 1
        };

        map.Layers.Add(layer);
        _editor.SelectedLayerId = layer.Id;
        _editor.PushUndo("add layer " + layer.Id, () =>
        {
            map.Layers.Remove(layer);
            if (_editor.SelectedLayerId == layer.Id) _editor.SelectedLayerId = null;
            _editor.RebuildAll();
            _editor.RebuildActivePanel();
            return true;
        });

        WorldMapAssets.ForgetFailures();
        _editor.RebuildAll();
        _editor.RebuildActivePanel();
        _editor.SetStatus($"Added {kind.ToLower()} layer '{asset}'. Drag to place it.");
    }

    private void ApplyTransform(CTWorldMapLayer layer)
    {
        if (_editor.Screen.LayerRects.TryGetValue(layer.Id, out var rect) && rect != null)
        {
            rect.localRotation = Quaternion.Euler(0f, 0f, layer.RotationZ);
            rect.localScale = WorldMapAssets.LayerLocalScale(layer);
        }
    }

    private static List<string> ListPngs(string mapName)
    {
        var result = new List<string>();
        try
        {
            var folder = CTWorldMapSerialization.FolderForRead(mapName);
            if (!Directory.Exists(folder)) return result;
            foreach (var file in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
                result.Add(Path.GetFileName(file));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("WorldEditor: could not list the map's images: " + e.Message);
        }
        return result;
    }

    private static List<string> ListSpineFolders(string mapName)
    {
        var result = new List<string>();
        try
        {
            var folder = CTWorldMapSerialization.FolderForRead(mapName);
            if (!Directory.Exists(folder)) return result;
            foreach (var sub in Directory.GetDirectories(folder))
                if (Directory.GetFiles(sub, "*.atlas", SearchOption.TopDirectoryOnly).Length > 0)
                    result.Add(Path.GetFileName(sub));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("WorldEditor: could not list the map's spine folders: " + e.Message);
        }
        return result;
    }

    private static void Note(MapEditorUI ui, Transform parent, string text)
    {
        var label = ui.CreateLabel(parent, text, 15);
        label.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.7f);
    }

    // A caption above a dropdown: the widget shows its current value once one is picked, and then
    // nothing on it says what it sets.
    private static void Label(MapEditorUI ui, Transform parent, string text)
    {
        var label = ui.CreateLabel(parent, text, 14);
        label.GetComponent<TMP_Text>().color = new Color(1f, 1f, 1f, 0.55f);
    }
}
