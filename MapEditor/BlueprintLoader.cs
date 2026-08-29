using System.Collections;
using System.Collections.Generic;
using CustomSpineLoader.SpineLoaderHelper;
using CustomSpineLoader.MapEditor.Tools;
using MMBiomeGeneration;
using MMRoomGeneration;
using Pathfinding;
using UnityEngine;
using UnityEngine.U2D;

namespace CustomSpineLoader.MapEditor;

public class BlueprintLoader
{
    private readonly RuntimeMapEditor _editor;

    public bool IsLoading { get; private set; }

    private int _propsSpawned;
    private int _propsFailed;

    public BlueprintLoader(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void Load(CTNodeBlueprint bp, string preferredEntryDirection = null)
    {
        if (bp == null || IsLoading) return;
        _editor.StartCoroutine(LoadRoutine(bp, preferredEntryDirection));
    }

    private IEnumerator LoadRoutine(CTNodeBlueprint bp, string preferredEntryDirection = null)
    {
        IsLoading = true;
        var room = SceneRefs.Room;
        if (room == null)
        {
            _editor.SetStatus("No room to load into.", StatusSeverity.Error);
            IsLoading = false;
            yield break;
        }

        Plugin.Log.LogInfo($"MapEditor: loading blueprint '{bp.MapName}' " +
                           $"({bp.Shapes.Count} shapes, {bp.Props.Count} props, {bp.Structures.Count} structures, " +
                           $"{bp.Enemies.Count} enemies, {bp.Npcs.Count} npcs, {bp.Podiums.Count} podiums, " +
                           $"{bp.Triggers.Count} triggers).");
        _editor.SetStatus($"Loading '{bp.MapName}'...");

        var shapeTool = _editor.GetTool<ShapeTool>();
        var structureTool = _editor.GetTool<StructureTool>();
        var doorTool = _editor.GetTool<DoorTool>();
        var enemyTool = _editor.GetTool<EnemyTool>();
        var npcTool = _editor.GetTool<NpcTool>();
        var triggerTool = _editor.GetTool<TriggerTool>();
        var podiumTool = _editor.GetTool<PodiumTool>();
        var clearTool = _editor.GetTool<ClearTool>();

        // ---- Phase 0: capture everything that clearing would destroy -----------------------
        shapeTool?.PrepareForLoad();
        podiumTool?.AcquireTemplate();

        var keptObjects = ParkKeptAuthored(bp, room);
        ParkAuthoredLeftovers(room);

        clearTool?.ClearTerrain();
        ClearEditorContent();
        ClearStrayShapes();
        ClearRoomTransformStrays(room);
        ClearEnemies();

        shapeTool?.ResetTracking();
        structureTool?.ResetTracking();
        enemyTool?.ResetTracking();
        npcTool?.ResetTracking();
        triggerTool?.ResetTracking();
        podiumTool?.ResetTracking();

        _editor.History.Clear();

        yield return null;

        RestoreKeptAuthored(keptObjects, room);
        yield return SpawnMissingKeptAuthored(bp, room, keptObjects);

        shapeTool?.ApplyVanillaFloorFlag(bp.UseVanillaFloorCollision);

        // ---- Phase 2: shapes ---------------------------------------------------------------
        var rebuiltShapes = new List<SpriteShapeController>();
        foreach (var shapeData in bp.Shapes)
        {
            var ctrl = shapeTool?.RebuildShape(shapeData);
            if (ctrl != null) rebuiltShapes.Add(ctrl);
        }

        yield return null;
        foreach (var ctrl in rebuiltShapes)
            shapeTool?.FinalizeLoadedShape(ctrl);

        // ---- Phase 3: props ----------------------------------------------------------------
        _propsSpawned = 0;
        _propsFailed = 0;
        yield return SpawnProps(bp, room);

        // ---- Phase 4: structures -----------------------------------------------------------
        if (structureTool != null)
        {
            foreach (var s in bp.Structures)
            {
                if (!StructureTool.TryResolveType(s.TypeName, s.IsCustom, out var type))
                {
                    Plugin.Log.LogWarning($"MapEditor: structure '{s.TypeName}' could not be resolved, skipped.");
                    continue;
                }
                yield return structureTool.PlaceAt(type, s.IsCustom,
                    MapEditorSerialization.ToVector3(s.Position), s.Rotation, s.FlipX,
                    deferNav: true, seeThrough: s.SeeThrough, fogThrough: s.FogThrough);
                ApplySavedScale(structureTool.LastPlacedInstance, s.Scale);
            }
        }

        if (doorTool != null)
        {
            var wanted = new HashSet<string>();
            foreach (var d in bp.Doors)
            {
                wanted.Add(d.Direction);

                var door = doorTool.EnsureDoor(d.Direction, deferCollision: true);
                if (door == null)
                {
                    Plugin.Log.LogWarning($"MapEditor: {d.Direction} door could not be created, skipped.");
                    continue;
                }
                var target = MapEditorSerialization.ToVector3(d.Position);
                var island = door.GetComponentInParent<IslandPiece>(true);
                if (island != null && island.transform != door.transform)
                    island.transform.position += target - door.transform.position;
                else
                    door.transform.position = target;

                door.transform.eulerAngles = new Vector3(0f, 0f, d.RotationZ);

                doorTool.RefreshPad(door, deferCollision: true);
                DoorTool.RefreshMovementAnchors(door);
            }

            doorTool.RemoveDoorsNotIn(wanted);

            _editor.KeepCullingSuspended = true;
        }

        // ---- Phase 6: enemies --------------------------------------------------------------
        if (enemyTool != null)
        {
            foreach (var e in bp.Enemies)
            {
                yield return enemyTool.SpawnEnemyRoutine(e.Key, e.IsCustom,
                    MapEditorSerialization.ToVector3(e.Position), withVfx: true);
                ApplySavedScale(enemyTool.LastPlacedInstance, e.Scale);
            }
        }

        // ---- Phase 6b: NPCs ----------------------------------------------------------------
        if (npcTool != null)
        {
            foreach (var n in bp.Npcs)
            {
                yield return npcTool.SpawnNpcRoutine(n.Key, MapEditorSerialization.ToVector3(n.Position),
                    n.IsCustom);
                ApplySavedScale(npcTool.LastPlacedInstance, n.Scale);
            }
        }

        // ---- Phase 7: podiums --------------------------------------------------------------
        if (podiumTool != null)
        {
            foreach (var p in bp.Podiums)
            {
                podiumTool.SpawnPodium(MapEditorSerialization.ToVector3(p.Position), p.Type, p.ClearAllOnEquip);
                ApplySavedScale(podiumTool.LastPlacedInstance, p.Scale);
            }
        }

        if (triggerTool != null)
        {
            foreach (var t in bp.Triggers)
                triggerTool.CreateTrigger(MapEditorSerialization.ToVector3(t.Position),
                    t.Width, t.Height, t.Id, t.Action, t.Once, t.Actions, t.LockPlayerControl);
        }

        yield return null;
        doorTool?.FinalizeAllPads();

        yield return RebuildCollisionAndWait(room);

        yield return ConnectStrandedDoors(doorTool, room);

        doorTool?.SealDoorsWithoutNeighbours();

        yield return RebuildBuildTotem(bp, structureTool);

        CustomRoomPatches.Mark(room);

        try
        {
            if (!CustomRoomPatches.HasBackSprite(room)) room.CreateBackgroundSpriteShape();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: backdrop recreation failed: " + e.Message);
        }

        // ---- Phase 9: hand over and walk the player in -------------------------------------
        for (var overlay = GameObject.Find("MapEditor_CollisionOverlay"); overlay != null;
             overlay = GameObject.Find("MapEditor_CollisionOverlay"))
            Object.DestroyImmediate(overlay);

        _editor.AdoptBlueprint(bp);
        _editor.ExitForPlayback();

        if (!string.IsNullOrEmpty(bp.MusicEvent))
        {
            try
            {
                AudioManager.Instance?.PlayMusic(bp.MusicEvent);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: blueprint music '{bp.MusicEvent}' failed to play: {e.Message}");
            }
        }
        _editor.SetMusicLoop(bp.MusicLoop && !string.IsNullOrEmpty(bp.MusicEvent) ? bp.MusicEvent : null);

        if (bp.Lighting != null && bp.Lighting.Enabled) LightingTool.Apply(bp.Lighting);
        else { LightingTool.ForgetCurrentRoom(); LightingTool.ClearOverride(); }

        WeatherControl.ForRoom(bp.Weather);

        LevelPlayback.OnContentReady();

        yield return PlayerEntryRoutine(room, bp, doorTool, preferredEntryDirection);

        Plugin.Log.LogInfo($"MapEditor: blueprint '{bp.MapName}' loaded - " +
                           $"{_propsSpawned}/{bp.Props.Count} prop(s) rebuilt" +
                           (_propsFailed > 0 ? $", {_propsFailed} FAILED (see warnings above)" : "") + ".");

        _editor.MarkSaved();
        IsLoading = false;
    }

    private IEnumerator RebuildBuildTotem(CTNodeBlueprint bp, StructureTool structureTool)
    {
        HubBuildTotem.Forget();
        if (bp.BuildTotem == null) yield break;

        var playing = HubSession.Active && !HubSession.IsAuthoring;

        if (playing) HubStructureStore.Begin(HubSession.HubName ?? bp.MapName);

        var totem = HubBuildTotem.Spawn(MapEditorSerialization.ToVector3(bp.BuildTotem.Position),
            SceneRefs.ContentRoot);
        if (totem == null) yield break;

        structureTool?.AdoptTotem(totem);

        yield return null;

        if (!playing) yield break;

        HubStructureStore.Restore();

        HubStructureStore.RestorePaths();
    }

    private void ClearEditorContent()
    {
        var root = SceneRefs.ContentRoot;
        if (root == null) return;

        for (var i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i).gameObject;
            if (!MapEditorProtection.CanDelete(child)) continue;
            Object.Destroy(child);
        }
    }

    private class KeptObject
    {
        public Transform Transform;
        public Transform OriginalParent;
        public MapKeptData Data;
    }

    private GameObject _keepHolder;

    private Transform KeepHolder
    {
        get
        {
            if (_keepHolder == null)
            {
                _keepHolder = new GameObject("MapEditor_KeepHolder");
                _keepHolder.transform.SetParent(_editor.transform, false);
            }
            return _keepHolder.transform;
        }
    }

    private List<KeptObject> ParkKeptAuthored(CTNodeBlueprint bp, GenerateRoom room)
    {
        var result = new List<KeptObject>();
        if (bp.KeptAuthored.Count == 0) return result;

        foreach (var data in bp.KeptAuthored)
        {
            var parent = ParentFor(data.Parent, room);
            var child = parent != null ? parent.Find(data.Name) : null;

            if (child == null) child = KeepHolder.Find(data.Name);

            if (child == null)
            {
                Plugin.Log.LogInfo($"MapEditor: kept authored object '{data.Name}' not present in this room.");
                continue;
            }

            result.Add(new KeptObject { Transform = child, OriginalParent = parent, Data = data });
            child.SetParent(KeepHolder, true);
        }

        return result;
    }

    private void ParkAuthoredLeftovers(GenerateRoom room)
    {
        var roots = new List<Transform>();
        if (room.SceneryTransform != null) roots.Add(room.SceneryTransform.transform);
        if (room.HeavyAssetsTransform != null) roots.Add(room.HeavyAssetsTransform);
        roots.Add(room.transform);

        var containers = new HashSet<Transform>();
        if (room.SceneryTransform != null) containers.Add(room.SceneryTransform.transform);
        if (room.HeavyAssetsTransform != null) containers.Add(room.HeavyAssetsTransform);
        if (room.CustomTransform != null) containers.Add(room.CustomTransform.transform);
        if (room.RoomTransform != null) containers.Add(room.RoomTransform.transform);

        var pool = ObjectPool.instance;
        var parked = 0;

        foreach (var root in roots)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null || containers.Contains(child)) continue;

                if (child.name.EndsWith("(Clone)")) continue;
                if (pool != null && pool.spawnedObjects.ContainsKey(child.gameObject)) continue;
                if (MapEditorProtection.IsProtected(child.gameObject)) continue;
                if (child.GetComponentInChildren<Door>(true) != null) continue;
                if (child.GetComponentInChildren<SpriteShapeController>(true) != null) continue;

                child.SetParent(KeepHolder, true);
                child.gameObject.SetActive(false);
                parked++;
            }
        }

        if (parked > 0)
            Plugin.Log.LogInfo($"MapEditor: parked {parked} authored object(s) not kept by this blueprint.");
    }

    private static void RestoreKeptAuthored(List<KeptObject> kept, GenerateRoom room)
    {
        foreach (var k in kept)
        {
            if (k.Transform == null) continue;

            k.Transform.SetParent(k.OriginalParent != null ? k.OriginalParent : room.transform, true);
            k.Transform.gameObject.SetActive(true);
            k.Transform.position = MapEditorSerialization.ToVector3(k.Data.Position);
            k.Transform.eulerAngles = new Vector3(0f, k.Data.RotationY, k.Data.RotationZ);
            if (k.Data.Scale != null && k.Data.Scale.X != 0f)
                k.Transform.localScale = MapEditorSerialization.ToVector3(k.Data.Scale);
        }
    }

    private static IEnumerator SpawnMissingKeptAuthored(CTNodeBlueprint bp, GenerateRoom room,
        List<KeptObject> restored)
    {
        if (bp.KeptAuthored.Count == 0) yield break;

        var present = new HashSet<string>();
        foreach (var k in restored)
            if (k.Data != null) present.Add(k.Data.Name);

        var missing = new List<MapKeptData>();
        foreach (var data in bp.KeptAuthored)
            if (!present.Contains(data.Name)) missing.Add(data);

        if (missing.Count == 0) yield break;

        if (string.IsNullOrEmpty(bp.SourceRoom))
        {
            Plugin.Log.LogInfo($"MapEditor: {missing.Count} authored object(s) are absent from this room and the " +
                               "blueprint predates source-room tracking - re-save it to bring them across.");
            yield break;
        }

        GameObject sourcePrefab = null;
        yield return RoomSnapshot.LoadPrefabByNameRoutine(bp.SourceRoom, go => sourcePrefab = go);
        if (sourcePrefab == null)
        {
            Plugin.Log.LogWarning($"MapEditor: source room prefab '{bp.SourceRoom}' not found; " +
                                  $"{missing.Count} authored object(s) stay missing.");
            yield break;
        }

        var copied = 0;
        foreach (var data in missing)
        {
            var source = RoomSnapshot.FindChildByName(sourcePrefab.transform, data.Name);
            if (source == null)
            {
                Plugin.Log.LogInfo($"MapEditor: authored object '{data.Name}' is not in '{bp.SourceRoom}' either.");
                continue;
            }

            var parent = ParentFor(data.Parent, room) ?? room.transform;
            var copy = Object.Instantiate(source.gameObject, parent);
            copy.name = data.Name;
            copy.transform.position = MapEditorSerialization.ToVector3(data.Position);
            copy.transform.eulerAngles = new Vector3(0f, data.RotationY, data.RotationZ);
            if (data.Scale != null && data.Scale.X != 0f)
                copy.transform.localScale = MapEditorSerialization.ToVector3(data.Scale);
            copied++;
        }

        Plugin.Log.LogInfo($"MapEditor: copied {copied}/{missing.Count} authored object(s) from '{bp.SourceRoom}'.");
    }

    private static void ClearRoomTransformStrays(GenerateRoom room)
    {
        var composite = room != null ? room.RoomTransform : null;
        if (composite == null) return;

        for (var i = composite.transform.childCount - 1; i >= 0; i--)
        {
            var child = composite.transform.GetChild(i);
            if (child == null) continue;
            if (!MapEditorProtection.CanDelete(child.gameObject)) continue;
            if (child.GetComponent<IslandPiece>() != null) continue;
            if (child.GetComponentInChildren<Door>(true) != null) continue;
            Object.Destroy(child.gameObject);
        }
    }

    private static void ClearEnemies()
    {
        var cleared = 0;
        foreach (var unit in Object.FindObjectsOfType<UnitObject>())
        {
            if (unit == null || !MapEditorProtection.CanDelete(unit.gameObject)) continue;
            if (unit.health == null || unit.health.team != Health.Team.Team2) continue;
            Object.Destroy(unit.gameObject);
            cleared++;
        }
        if (cleared > 0) Plugin.Log.LogInfo($"MapEditor: cleared {cleared} enemy(ies) before load.");

        foreach (var bar in Object.FindObjectsOfType<HPBar>())
            if (bar != null) Object.Destroy(bar.gameObject);
    }

    private static void ClearStrayShapes()
    {
        foreach (var ctrl in Object.FindObjectsOfType<SpriteShapeController>())
        {
            if (ctrl == null || !MapEditorProtection.CanDelete(ctrl.gameObject)) continue;
            Object.Destroy(ctrl.gameObject);
        }
    }

    private IEnumerator SpawnProps(CTNodeBlueprint bp, GenerateRoom room)
    {
        var pending = 0;

        var islandsByIndex = new Dictionary<int, Transform>();

        for (var index = 0; index < bp.Props.Count; index++)
        {
            var prop = bp.Props[index];

            var parent = prop.ParentIslandIndex >= 0 &&
                         islandsByIndex.TryGetValue(prop.ParentIslandIndex, out var islandParent)
                ? islandParent
                : ParentFor(prop.Parent, room);
            if (parent == null)
            {
                Plugin.Log.LogWarning($"MapEditor: no '{prop.Parent}' parent for prop '{prop.Key}', skipped.");
                continue;
            }

            if (prop.IsIslandRef)
            {
                GameObject prefab = null;
                yield return RoomSnapshot.ResolveIslandRoutine(room, prop.Key, go => prefab = go);
                if (prefab == null)
                {
                    _propsFailed++;
                    Plugin.Log.LogWarning($"MapEditor: island prefab '{prop.Key}' could not be resolved from this " +
                                          "room's lists, the addressables catalog or loaded assets - the floor it " +
                                          "carries will be missing.");
                    continue;
                }
                _propsSpawned++;
                var island = Object.Instantiate(prefab, parent);
                ApplyPropTransform(island, prop, room);
                island.GetComponent<IslandPiece>()?.HideSprites();
                islandsByIndex[index] = island.transform;
                continue;
            }

            var captured = prop;
            pending++;
            try
            {
                ObjectPool.Spawn(prop.Key,
                    MapEditorSerialization.ToVector3(prop.Position),
                    Quaternion.Euler(0f, 0f, prop.RotationZ),
                    parent,
                    go =>
                    {
                        pending--;
                        if (go == null)
                        {
                            _propsFailed++;
                            Plugin.Log.LogWarning($"MapEditor: prop '{captured.Key}' spawned nothing.");
                            return;
                        }
                        _propsSpawned++;
                        ApplyPropTransform(go, captured, room);
                    },
                    prop.IsAddressable);
            }
            catch (System.Exception e)
            {
                pending--;
                _propsFailed++;
                Plugin.Log.LogWarning($"MapEditor: prop '{prop.Key}' failed to spawn: {e.Message}");
            }
        }

        var deadline = Time.unscaledTime + 10f;
        while (pending > 0 && Time.unscaledTime < deadline) yield return null;
        if (pending > 0)
            Plugin.Log.LogWarning($"MapEditor: {pending} prop(s) still loading after timeout; they may appear late.");
    }

    private static void ApplySavedScale(GameObject go, SerializableVector3 scale)
    {
        if (go == null || scale == null || scale.X == 0f) return;
        go.transform.localScale = MapEditorSerialization.ToVector3(scale);
    }

    private static void ApplyPropTransform(GameObject go, MapPropData prop, GenerateRoom room)
    {
        go.transform.position = MapEditorSerialization.ToVector3(prop.Position);
        go.transform.eulerAngles = new Vector3(0f, prop.RotationY, prop.RotationZ);
        if (prop.Scale != null && prop.Scale.X != 0f)
            go.transform.localScale = MapEditorSerialization.ToVector3(prop.Scale);

        var piece = go.GetComponent<IslandPiece>();
        if (piece != null && room != null && room.Pieces != null && !room.Pieces.Contains(piece))
            room.Pieces.Add(piece);
    }

    private static Transform ParentFor(string tag, GenerateRoom room)
    {
        return tag switch
        {
            "Heavy" => room.HeavyAssetsTransform,
            "Custom" => SceneRefs.ContentRoot,
            "Island" => room.RoomTransform != null ? room.RoomTransform.transform : null,
            "Room" => room.transform,
            _ => room.SceneryTransform != null ? room.SceneryTransform.transform : null
        };
    }

    private static IEnumerator RebuildCollisionAndWait(GenerateRoom room)
    {
        room.GeneratedPathing = false;
        SceneRefs.RegenerateRoomCollision();

        var deadline = Time.unscaledTime + 8f;
        while (!room.GeneratedPathing && Time.unscaledTime < deadline) yield return null;

        if (!room.GeneratedPathing)
        {
            Plugin.Log.LogWarning("MapEditor: pathfinding rebuild timed out, forcing a scan.");
            SceneRefs.RescanNavigation();
            room.GeneratedPathing = true;
        }

        var composite = SceneRefs.RoomComposite;
        if (composite != null)
        {
            var livePieces = 0;
            if (room.Pieces != null)
                foreach (var piece in room.Pieces)
                    if (piece != null) livePieces++;

            Plugin.Log.LogInfo($"MapEditor: rebuilt collision - composite outline has {composite.pathCount} " +
                               $"path(s), {livePieces} island piece(s) registered. More than one path means " +
                               "disconnected floor regions that block movement between them.");

            AuditRoomColliders(composite);

            if (composite.pathCount > 1) ReportStrandedDoors(composite);
        }
    }

    private static void AuditRoomColliders(CompositeCollider2D composite)
    {
        var repaired = 0;
        var strays = new List<string>();

        foreach (var collider in composite.transform.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null || collider is CompositeCollider2D) continue;
            if (!collider.enabled || collider.isTrigger || collider.usedByComposite) continue;

            var isFloor = collider.GetComponent<SpriteShapeController>() != null ||
                          collider.GetComponentInParent<IslandPiece>() != null;
            if (isFloor)
            {
                if (collider is EdgeCollider2D)
                {
                    collider.enabled = false;
                    repaired++;
                    continue;
                }

                collider.usedByComposite = true;
                repaired++;
                continue;
            }

            if (strays.Count < 15)
                strays.Add($"{collider.name} ({collider.GetType().Name}, " +
                           $"layer {LayerMask.LayerToName(collider.gameObject.layer)}, " +
                           $"at {collider.transform.position})");
        }

        if (repaired > 0)
        {
            Plugin.Log.LogWarning($"MapEditor: {repaired} floor collider(s) were solid instead of merged into the " +
                                  "room outline - repaired and rebuilding the union.");
            composite.GenerateGeometry();
            SceneRefs.RescanNavigation();
            Plugin.Log.LogInfo($"MapEditor: union now has {composite.pathCount} path(s).");
        }

        if (strays.Count > 0)
            Plugin.Log.LogWarning("MapEditor: solid colliders inside the room that are not part of the floor " +
                                  "outline (these will block the player): " + string.Join("; ", strays));
    }

    private IEnumerator ConnectStrandedDoors(DoorTool doorTool, GenerateRoom room)
    {
        if (doorTool == null || AstarPath.active == null) yield break;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var composite = SceneRefs.RoomComposite;
            if (composite == null) yield break;

            var stranded = StrandedDoors(composite);
            if (stranded.Count == 0)
            {
                if (attempt > 0) Plugin.Log.LogInfo("MapEditor: every doorway now connects to the room's floor.");
                yield break;
            }

            var grew = false;
            foreach (var door in stranded)
                if (doorTool.ExtendPad(door, deferCollision: true)) grew = true;

            if (!grew)
            {
                foreach (var door in stranded)
                    Plugin.Log.LogWarning($"MapEditor: the {door.direction} doorway is still cut off from the " +
                                          "room's floor even at full pad length - extend the terrain to meet it.");
                yield break;
            }

            yield return null;
            doorTool.FinalizeAllPads();
            yield return RebuildCollisionAndWait(room);
        }
    }

    private static List<Door> StrandedDoors(CompositeCollider2D composite)
    {
        var result = new List<Door>();
        if (AstarPath.active == null) return result;

        var centre = AstarPath.active.GetNearest(composite.bounds.center).node;
        if (centre == null) return result;

        foreach (var door in Door.Doors)
        {
            if (!DoorTool.IsDoorPresent(door)) continue;

            var inside = door.transform.position + door.GetDoorDirection() * 3f;
            var node = AstarPath.active.GetNearest(inside).node;
            if (node == null) continue;

            if (!PathUtilities.IsPathPossible(node, centre)) result.Add(door);
        }
        return result;
    }

    private static void ReportStrandedDoors(CompositeCollider2D composite)
    {
        if (AstarPath.active == null) return;

        var centre = AstarPath.active.GetNearest(composite.bounds.center).node;
        if (centre == null) return;

        foreach (var door in Door.Doors)
        {
            if (!DoorTool.IsDoorPresent(door)) continue;

            var inside = door.transform.position + door.GetDoorDirection() * 3f;
            var node = AstarPath.active.GetNearest(inside).node;
            if (node == null) continue;

            if (!PathUtilities.IsPathPossible(node, centre))
                Plugin.Log.LogWarning($"MapEditor: the {door.direction} doorway is cut off from the room's floor - " +
                                      "walking through it will strand the player. Extend the terrain to meet it.");
        }
    }

    private IEnumerator PlayerEntryRoutine(GenerateRoom room, CTNodeBlueprint bp, DoorTool doorTool,
        string preferredEntryDirection)
    {
        var player = PlayerFarming.Instance;
        if (player == null) yield break;

        var door = PickEntryDoor(bp, doorTool, preferredEntryDirection);

        if (door == null || door.PlayerPosition == null)
        {
            Plugin.Log.LogWarning("MapEditor: no door to enter through; player left in place.");
            yield break;
        }

        var deadline = Time.unscaledTime + 8f;
        while (room != null && !room.GeneratedPathing && Time.unscaledTime < deadline) yield return null;

        PlayerFarming.SetStateForAllPlayers(StateMachine.State.InActive);

        var dir = door.GetDoorDirection();
        var doorway = door.PlayerPosition.position;

        var start = SnapToWalkable(doorway + dir * 2f) ?? doorway;
        player.transform.position = start;
        player.state.facingAngle = Vector3.Angle(Vector3.right, dir);

        var target = SnapToWalkable(doorway + dir * 7.3f) ?? start;

        door.Used = true;

        player.GoToAndStop(target, null, IdleOnEnd: true, DisableCollider: true,
            GoToCallback: () =>
            {
                PlayerFarming.SetCollidersActive(collidersActive: true);

                try
                {
                    door.PlayerFinishedEnteringDoor();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("MapEditor: entry door hand-off failed: " + e.Message);
                }

                _editor.StartCoroutine(ReleaseDoorAfterEntry(door));
                _editor.StartCoroutine(FinishArrival());
                Plugin.Log.LogInfo("MapEditor: player entry complete.");
            },
            maxDuration: -1f, forcePositionOnTimeout: true);
    }

    private static IEnumerator ReleaseDoorAfterEntry(Door door)
    {
        yield return new WaitForSeconds(1f);
        if (door != null) door.Used = false;
    }

    private static IEnumerator FinishArrival()
    {
        yield return new WaitForSeconds(0.3f);

        var manager = GameManager.GetInstance();
        if (manager != null)
        {
            try
            {
                manager.OnConversationEnd(SetPlayerToIdle: false);
                manager.CameraSetOffset(Vector3.zero);
                manager.AddPlayerToCamera();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: conversation hand-off failed: " + e.Message);
            }
        }

        try
        {
            PlayerFarming.ResetMainPlayer();
            CoopManager.RefreshCoopPlayerRewired();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: player input hand-off failed: " + e.Message);
        }

        yield return new WaitForSeconds(0.5f);
        var biome = BiomeGenerator.Instance;
        if (biome != null && biome.CurrentRoom != null) biome.CurrentRoom.Active = true;
    }

    private static Vector3? SnapToWalkable(Vector3 probe)
    {
        if (AstarPath.active == null) return null;

        var nearest = AstarPath.active.GetNearest(probe);
        if (nearest.node == null || !nearest.node.Walkable) return null;

        var p = (Vector3)nearest.position;
        return new Vector3(p.x, p.y, 0f);
    }

    private static Door PickEntryDoor(CTNodeBlueprint bp, DoorTool doorTool, string preferredDirection)
    {
        if (preferredDirection != null && doorTool != null &&
            bp.Doors.Exists(d => d.Direction == preferredDirection))
        {
            var preferred = doorTool.FindByDirection(preferredDirection);
            if (preferred != null)
            {
                Plugin.Log.LogInfo($"MapEditor: entering via preferred {preferredDirection} door.");
                return preferred;
            }
        }

        Door entrance = null;
        try
        {
            entrance = Door.GetEntranceDoor();
        }
        catch (System.Exception)
        {
        }

        if (entrance != null && bp.Doors.Exists(d => d.Direction == entrance.direction.ToString()))
            return entrance;

        if (doorTool != null)
        {
            foreach (var d in bp.Doors)
            {
                var match = doorTool.FindByDirection(d.Direction);
                if (match != null)
                {
                    Plugin.Log.LogInfo($"MapEditor: entering via blueprint {d.Direction} door.");
                    return match;
                }
            }
        }

        if (entrance != null) return entrance;
        return Door.Doors.Count > 0 ? Door.Doors[0] : null;
    }
}
