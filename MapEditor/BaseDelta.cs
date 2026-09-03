using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.MapEditor.Tools;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

[Serializable]
public class BaseVanillaRef
{
    public string Name = "";
    public string Key = "";                  // the addressable key, when one could be resolved

    public string Path = "";

    public SerializableVector3 OriginalPosition;
}

[Serializable]
public class BaseMovedVanilla : BaseVanillaRef
{
    public SerializableVector3 NewPosition;
    public SerializableVector3 Scale;
}

[Serializable]
public class BaseMovedStructure
{
    public long StructureId = -1;
    public string TypeName = "";
    public SerializableVector3 OriginalPosition;
    public int OriginalGridX;
    public int OriginalGridY;
    public SerializableVector3 NewPosition;
}

[Serializable]
public class BaseEditedShape : BaseVanillaRef
{
    public MapShapeData Shape;
}

[Serializable]
public class CTBaseDelta
{
    public int Slot = -1;

    public CTNodeBlueprint Content = new();

    public List<BaseVanillaRef> Removed = [];
    public List<BaseMovedVanilla> Moved = [];
    public List<BaseMovedStructure> MovedStructures = [];
    public List<BaseEditedShape> EditedShapes = [];

    public List<HubStructureRecord> ModGroundStructures = [];
}

public static class BaseDelta
{
    private const string RootFolder = "CustomBaseMaps";

    private const float MatchRadius = 0.5f;

    private static CTBaseDelta _file = new();
    private static int _loadedSlot = -1;

    public static int Slot => SaveAndLoad.SAVE_SLOT % 10;

    public static string FolderPath => Path.Combine(Plugin.PluginPath, RootFolder);

    public static string PathForSlot(int slot) => Path.Combine(FolderPath, $"base_slot{slot}.json");

    public static CTNodeBlueprint Content => Current().Content;

    public static bool IsApplying { get; private set; }

    private static CTBaseDelta Current()
    {
        var slot = Slot;
        if (_loadedSlot == slot) return _file;

        _file = Read(slot);
        _file.Slot = slot;
        _loadedSlot = slot;
        return _file;
    }

    private static bool IsEmpty(CTBaseDelta file) =>
        file.Removed.Count == 0 && file.Moved.Count == 0 && file.MovedStructures.Count == 0 &&
        file.ModGroundStructures.Count == 0 && file.EditedShapes.Count == 0 &&
        file.Content.Shapes.Count == 0 && file.Content.Structures.Count == 0 &&
        file.Content.Npcs.Count == 0 && file.Content.Triggers.Count == 0 &&
        string.IsNullOrEmpty(file.Content.MusicEvent) &&
        (file.Content.Lighting == null || !file.Content.Lighting.Enabled);

    // ---- storage --------------------------------------------------------------------------------

    private static CTBaseDelta Read(int slot)
    {
        try
        {
            var path = PathForSlot(slot);
            if (!File.Exists(path)) return new CTBaseDelta();

            var parsed = JsonConvert.DeserializeObject<CTBaseDelta>(File.ReadAllText(path));
            if (parsed == null) return new CTBaseDelta();

            parsed.Content ??= new CTNodeBlueprint();
            return parsed;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Base editor: slot {slot}'s file could not be read: {e.Message}");
            return new CTBaseDelta();
        }
    }

    public static bool Save(CTNodeBlueprint content)
    {
        var file = Current();
        if (content != null) file.Content = content;

        CollectTouchedShapes(file);

        file.Content.MapName = $"base_slot{file.Slot}";
        file.Content.SceneName = BaseSession.BaseScene;
        file.Content.Props.Clear();
        file.Content.KeptAuthored.Clear();
        file.Content.Doors.Clear();
        file.Content.Enemies.Clear();
        file.Content.Podiums.Clear();
        file.Content.BuildTotem = null;

        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(PathForSlot(file.Slot), JsonConvert.SerializeObject(file, Formatting.Indented));

            Plugin.Log.LogInfo($"Base editor: slot {file.Slot} saved - {file.Content.Shapes.Count} shape(s), " +
                               $"{file.Content.Structures.Count} structure(s), {file.Content.Npcs.Count} npc(s), " +
                               $"{file.Content.Triggers.Count} trigger(s), {file.Removed.Count} removed, " +
                               $"{file.Moved.Count} moved, {file.MovedStructures.Count} building(s) moved, " +
                               $"{file.EditedShapes.Count} piece(s) of the base's own terrain reshaped.");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Base editor: slot {file.Slot}'s file could not be written: {e.Message}");
            return false;
        }
    }

    private static void SaveQuietly()
    {
        try
        {
            var file = Current();
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(PathForSlot(file.Slot), JsonConvert.SerializeObject(file, Formatting.Indented));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Base editor: slot {Slot}'s file could not be written: {e.Message}");
        }
    }

    // ---- where the player arrives -----------------------------------------------------------------

    public static Vector3? SpawnPoint()
    {
        var trigger = SpawnTrigger();
        return trigger == null ? null : MapEditorSerialization.ToVector3(trigger.Position);
    }

    public static MapTriggerData SpawnTrigger()
    {
        foreach (var trigger in Current().Content.Triggers)
        {
            if (trigger?.Actions == null) continue;

            foreach (var action in trigger.Actions)
            {
                if (action == null || action.Type != TriggerActionType.HubSpawnPoint.ToString()) continue;
                return trigger;
            }
        }

        return null;
    }

    // ---- arrival --------------------------------------------------------------------------------

    public static void OnArrived()
    {
        if (Plugin.Instance == null) return;

        _loadedSlot = -1;
        _touchedShapes.Clear();
        _liveModGround.Clear();

        StructureManager.OnStructureRemoved -= ModGroundStructureRemoved;
        StructureManager.OnStructureRemoved += ModGroundStructureRemoved;

        Plugin.Instance.StartCoroutine(ApplyRoutine());
    }

    private static IEnumerator ApplyRoutine()
    {
        var deadline = Time.realtimeSinceStartup + 45f;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (BiomeBaseManager.Instance != null && PlayerFarming.Instance != null &&
                PlayerFarming.Location == FollowerLocation.Base)
                break;

            yield return null;
        }

        if (HubSession.Busy) yield break;

        if (BiomeBaseManager.Instance == null || PlayerFarming.Instance == null) yield break;
        if (BaseSession.BaseRoom() == null) yield break;

        BaseSession.ClaimRoom();

        var file = Current();
        if (IsEmpty(file))
        {
            BaseGround.RememberVanillaGround();
            yield break;
        }

        LocationManager.LocationManagers.TryGetValue(FollowerLocation.Base, out var manager);
        while (manager != null && !manager.StructuresPlaced && Time.realtimeSinceStartup < deadline)
            yield return null;

        var editor = RuntimeMapEditor.Ensure("RuntimeMapEditorHost_Base");
        if (editor == null)
        {
            Plugin.Log.LogWarning("Base editor: no editor host could be created; the saved base was " +
                                  "not applied.");
            yield break;
        }

        IsApplying = true;
        EnsureContentRoot();
        BaseGround.RememberVanillaGround();

        var report = new List<string>();
        yield return ApplyContent(editor, file, report);

        IsApplying = false;

        Plugin.Log.LogInfo($"Base editor: slot {file.Slot} applied - {string.Join(", ", report)}.");
    }

    private static IEnumerator ApplyContent(RuntimeMapEditor editor, CTBaseDelta file, List<string> report)
    {
        var shapeTool = editor.GetTool<ShapeTool>();
        var structureTool = editor.GetTool<StructureTool>();
        var npcTool = editor.GetTool<NpcTool>();
        var triggerTool = editor.GetTool<TriggerTool>();

        shapeTool?.ResetTracking();
        structureTool?.ResetTracking();
        npcTool?.ResetTracking();
        triggerTool?.ResetTracking();
        editor.History.Clear();

        shapeTool?.PrepareForLoad();

        // ---- what the author took away and moved ------------------------------------------------
        yield return ApplyJournal(file, report);

        // ---- the base's own terrain, reshaped ----------------------------------------------------
        var reshaped = new List<UnityEngine.U2D.SpriteShapeController>();
        if (file.EditedShapes.Count > 0 && shapeTool != null)
        {
            var index = BuildSceneIndex();

            foreach (var edit in file.EditedShapes)
            {
                var go = Find(index, edit);
                var ctrl = go != null ? go.GetComponent<UnityEngine.U2D.SpriteShapeController>() : null;
                if (ctrl == null || edit.Shape == null)
                {
                    Plugin.Log.LogWarning($"Base editor: the terrain called '{edit.Name}' is not " +
                                          "where it was; its edit was skipped.");
                    continue;
                }

                ctrl.transform.position = MapEditorSerialization.ToVector3(edit.Shape.Position);
                if (!shapeTool.ApplyShapeData(ctrl, edit.Shape)) continue;

                Plugin.Log.LogInfo($"Base editor: reshaped the base's own '{edit.Name}' at " +
                                   $"{ctrl.transform.position} ({edit.Shape.Points.Count} point(s)).");
                BaseGround.RegisterReshapedGround(ctrl);
                reshaped.Add(ctrl);
            }
        }
        report.Add($"{reshaped.Count}/{file.EditedShapes.Count} reshaped");

        if (reshaped.Count > 0)
        {
            yield return null;

            foreach (var ctrl in reshaped)
            {
                try
                {
                    shapeTool.MergeCollisionIntoRoom(ctrl);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Base editor: reshaped terrain could not be merged into " +
                                          "the room's collision: " + e.Message);
                }
            }

            SceneRefs.RegenerateRoomCollision();
        }

        // ---- shapes -----------------------------------------------------------------------------
        var rebuilt = new List<UnityEngine.U2D.SpriteShapeController>();
        foreach (var shape in file.Content.Shapes)
        {
            var ctrl = shapeTool?.RebuildShape(shape);
            if (ctrl != null) rebuilt.Add(ctrl);
        }

        yield return null;
        foreach (var ctrl in rebuilt) shapeTool?.FinalizeLoadedShape(ctrl);
        report.Add($"{rebuilt.Count} shape(s)");

        if (rebuilt.Count > 0 || reshaped.Count > 0) yield return BaseGround.ApplyRoutine();

        // ---- structures, npcs, triggers ---------------------------------------------------------
        if (structureTool != null)
        {
            foreach (var s in file.Content.Structures)
            {
                if (!StructureTool.TryResolveType(s.TypeName, s.IsCustom, out var type))
                {
                    Plugin.Log.LogWarning($"Base editor: structure '{s.TypeName}' could not be " +
                                          "resolved, skipped.");
                    continue;
                }

                yield return structureTool.PlaceAt(type, s.IsCustom,
                    MapEditorSerialization.ToVector3(s.Position), s.Rotation, s.FlipX,
                    deferNav: true, seeThrough: s.SeeThrough, fogThrough: s.FogThrough,
                    wind: s.Wind);

                var instance = structureTool.LastPlacedInstance;
                if (instance != null && s.Scale != null && s.Scale.X != 0f)
                    instance.transform.localScale = MapEditorSerialization.ToVector3(s.Scale);
            }
            report.Add($"{file.Content.Structures.Count} structure(s)");
        }

        if (npcTool != null)
        {
            foreach (var n in file.Content.Npcs)
                yield return npcTool.SpawnNpcRoutine(n.Key, MapEditorSerialization.ToVector3(n.Position),
                    n.IsCustom);
            report.Add($"{file.Content.Npcs.Count} npc(s)");
        }

        if (triggerTool != null)
        {
            foreach (var t in file.Content.Triggers)
                triggerTool.CreateTrigger(MapEditorSerialization.ToVector3(t.Position),
                    t.Width, t.Height, t.Id, t.Action, t.Once, t.Actions, t.LockPlayerControl,
                    t.Blocking);
            report.Add($"{file.Content.Triggers.Count} trigger(s)");
        }

        // ---- the player's own buildings ---------------------------------------------------------
        yield return ApplyStructureMoves(file, report);
        RestoreModGroundStructures(file, report);

        // ---- look and sound ---------------------------------------------------------------------
        if (file.Content.Lighting is { Enabled: true }) LightingTool.Apply(file.Content.Lighting);

        if (!string.IsNullOrEmpty(file.Content.MusicEvent))
        {
            try
            {
                AudioManager.Instance?.PlayMusic(file.Content.MusicEvent);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Base editor: music '{file.Content.MusicEvent}' failed to " +
                                      $"play: {e.Message}");
            }
        }

        SceneRefs.RescanNavigation();

        editor.MarkSaved();
    }

    // ---- the player's own buildings -------------------------------------------------------------

    private static IEnumerator ApplyStructureMoves(CTBaseDelta file, List<string> report)
    {
        if (file.MovedStructures.Count == 0) yield break;

        var applied = 0;
        var wanted = new List<BaseMovedStructure>(file.MovedStructures);
        file.MovedStructures.Clear();

        foreach (var move in wanted)
        {
            var brain = FindBrain(move);
            if (brain?.Data == null)
            {
                Plugin.Log.LogInfo($"Base editor: the {move.TypeName} that was moved is no longer in " +
                                   "the base; the move was dropped.");
                continue;
            }

            if (brain.Data.DontLoadMe)
            {
                Plugin.Log.LogWarning($"Base editor: {move.TypeName} is anchored to the scene, so the " +
                                      "recorded move of it has been dropped - move it again to have " +
                                      "it remembered safely.");
                continue;
            }

            file.MovedStructures.Add(move);

            var to = MapEditorSerialization.ToVector3(move.NewPosition);
            if (!MoveStructure(brain, to, MapEditorSerialization.ToVector3(move.OriginalPosition)))
                continue;

            SaveMask.Register(brain.Data, MapEditorSerialization.ToVector3(move.OriginalPosition),
                new Vector2Int(move.OriginalGridX, move.OriginalGridY));
            applied++;
        }

        report.Add($"{applied}/{wanted.Count} building(s) moved");
        if (file.MovedStructures.Count != wanted.Count) SaveQuietly();

        yield return null;
        yield return null;

        foreach (var move in file.MovedStructures)
        {
            var brain = FindBrain(move);
            var structure = FindStructureObject(brain,
                MapEditorSerialization.ToVector3(move.NewPosition));
            if (structure == null) continue;

            var wanted2 = MapEditorSerialization.ToVector3(move.NewPosition);
            var drift = Vector3.Distance(structure.transform.position, wanted2);

            if (drift > 0.75f)
            {
                Plugin.Log.LogWarning($"Base editor: {move.TypeName} was moved to {wanted2} but is " +
                                      $"standing at {structure.transform.position} " +
                                      $"({drift:0.##} away) - something put it back. Its data says " +
                                      $"{brain?.Data?.Position}.");
                continue;
            }

            ReportWhatStayedBehind(move, structure);
        }
    }

    public static void RepairSavedPositions()
    {
        var file = Current();
        if (file.MovedStructures.Count == 0) return;

        List<StructuresData> saved;
        try
        {
            saved = StructureManager.StructuresDataAtLocation(FollowerLocation.Base);
        }
        catch (Exception)
        {
            return;
        }

        if (saved == null) return;

        var repaired = 0;

        foreach (var move in file.MovedStructures)
        {
            if (move.StructureId < 0) continue;

            var original = MapEditorSerialization.ToVector3(move.OriginalPosition);
            var cell = new Vector2Int(move.OriginalGridX, move.OriginalGridY);

            foreach (var data in saved)
            {
                if (data == null || data.ID != move.StructureId) continue;

                if (data.Position == original && data.GridTilePosition == cell) break;

                data.Position = original;
                data.GridTilePosition = cell;
                repaired++;
                break;
            }
        }

        if (repaired > 0)
            Plugin.Log.LogWarning($"Base editor: {repaired} moved building(s) reached the game's save " +
                                  "with this mod's position on them. Put back before the loader saw " +
                                  "them - no building was lost, but the mask that should have hidden " +
                                  "them did not.");
    }

    private static void ReportWhatStayedBehind(BaseMovedStructure move, Structure moved)
    {
        var from = MapEditorSerialization.ToVector3(move.OriginalPosition);

        var leftBehind = new List<string>();
        foreach (var other in Structure.Structures)
        {
            if (other == null || ReferenceEquals(other, moved)) continue;
            if (Vector3.Distance(other.transform.position, from) > 4f) continue;

            var data = other.Brain?.Data;
            leftBehind.Add(data == null
                ? $"{other.name} (no data)"
                : $"{data.Type}#{data.ID}{(data.DontLoadMe ? " [scene-anchored]" : "")} at " +
                  $"{other.transform.position}");
        }

        var art = "no renderers";
        var renderers = moved.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            art = $"art centred on {bounds.center}";
        }

        Plugin.Log.LogInfo($"Base editor: {move.TypeName} stands at {moved.transform.position}, {art}. " +
                           (leftBehind.Count == 0
                               ? "Nothing else was left at the place it came from."
                               : $"Still standing where it came from: {string.Join("; ", leftBehind)}."));
    }

    public static bool MoveStructure(StructureBrain brain, Vector3 to, Vector3? near = null)
    {
        if (brain?.Data == null) return false;

        var structure = FindStructureObject(brain, near ?? brain.Data.Position);

        try
        {
            brain.RemoveFromGrid();

            var region = PlacementRegion.Instance;
            var tile = region != null ? region.GetClosestTileGridTileAtWorldPosition(to) : null;

            brain.Data.Position = to;
            if (tile != null) brain.Data.GridTilePosition = tile.Position;

            if (structure != null)
            {
                structure.transform.position = to;
                brain.AddToGrid();
                UpdateGraphBounds(structure);
            }
            else
            {
                Plugin.Log.LogWarning($"Base editor: {brain.Data.Type}#{brain.Data.ID} has no " +
                                      $"standing object among {Structure.Structures.Count} known " +
                                      "building(s), so only its data moved - it will not appear to " +
                                      "have moved at all.");
                brain.AddToGrid();
            }

            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Base editor: {brain.Data.Type} could not be moved: {e.Message}");
            return false;
        }
    }

    // Called through reflection until the project moved off the 1.5.15 game assemblies, which had no
    // such method; 1.5.26 declares it, so the lookup and its full-rescan fallback are gone.
    private static void UpdateGraphBounds(Structure structure) => structure.UpdateGraphBounds(true);

    private static Structure FindStructureObject(StructureBrain brain, Vector3? near = null)
    {
        if (brain?.Data == null) return null;

        var candidates = new List<Structure>();

        foreach (var structure in Structure.Structures)
        {
            if (structure == null) continue;

            var data = structure.Brain?.Data;
            if (ReferenceEquals(structure.Brain, brain) || (data != null && data.ID == brain.Data.ID))
                candidates.Add(structure);
        }

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        Structure best = null;
        var bestScore = float.MinValue;

        foreach (var candidate in candidates)
        {
            var score = candidate.GetComponentsInChildren<Renderer>(true).Length > 0 ? 1000f : 0f;

            if (near.HasValue)
                score -= Vector3.Distance(candidate.transform.position, near.Value);

            if (score <= bestScore) continue;
            bestScore = score;
            best = candidate;
        }

        Plugin.Log.LogInfo($"Base editor: {brain.Data.Type}#{brain.Data.ID} is worn by " +
                           $"{candidates.Count} objects; moving the one with art at " +
                           $"{(best == null ? "<none>" : best.transform.position.ToString())}.");

        return best;
    }

    private static Structure FindStructure(GameObject go)
    {
        var structure = go.GetComponent<Structure>();
        if (structure != null) return structure;

        structure = go.GetComponentInChildren<Structure>(true);
        return structure != null ? structure : go.GetComponentInParent<Structure>();
    }

    private static StructureBrain FindBrain(BaseMovedStructure move)
    {
        if (move.StructureId >= 0)
        {
            foreach (var data in StructureManager.StructuresDataAtLocation(FollowerLocation.Base))
                if (data != null && data.ID == move.StructureId)
                    return StructureBrain.GetOrCreateBrain(data);
        }

        var from = MapEditorSerialization.ToVector3(move.OriginalPosition);
        foreach (var data in StructureManager.StructuresDataAtLocation(FollowerLocation.Base))
        {
            if (data == null || data.Type.ToString() != move.TypeName) continue;
            if (Vector3.Distance(data.Position, from) > MatchRadius) continue;
            return StructureBrain.GetOrCreateBrain(data);
        }

        return null;
    }

    private static void RestoreModGroundStructures(CTBaseDelta file, List<string> report)
    {
        if (file.ModGroundStructures.Count == 0) return;

        var region = PlacementRegion.Instance;
        if (region == null)
        {
            Plugin.Log.LogWarning("Base editor: the base has no placement region; buildings on added " +
                                  "ground were not restored.");
            return;
        }

        var wanted = new List<HubStructureRecord>(file.ModGroundStructures);
        file.ModGroundStructures.Clear();

        var restored = 0;
        foreach (var record in wanted)
        {
            if (!StructureTool.TryResolveAnyType(record.TypeName, out var type))
            {
                Plugin.Log.LogWarning($"Base editor: '{record.TypeName}' is not a structure this game " +
                                      "knows; it was dropped.");
                continue;
            }

            var bounds = new Vector2Int(Mathf.Max(1, record.BoundsX), Mathf.Max(1, record.BoundsY));

            try
            {
                if (record.HasWorld)
                    region.PlaceStructureAtWorldPosition(type,
                        new Vector3(record.WorldX, record.WorldY, 0f), bounds);
                else
                    region.PlaceStructureAtGridPosition(type,
                        new Vector2Int(record.GridX, record.GridY), bounds);

                if (record.Finished) FinishSiteAt(record);

                restored++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Base editor: {type} could not be rebuilt: {e.Message}");
            }
        }

        SaveQuietly();
        report.Add($"{restored}/{wanted.Count} building(s) on added ground");
    }

    private static void FinishSiteAt(HubStructureRecord record)
    {
        if (Plugin.Instance == null) return;

        var at = record.HasWorld
            ? new Vector3(record.WorldX, record.WorldY, 0f)
            : (Vector3?)null;

        Plugin.Instance.StartCoroutine(FinishNextFrame(at, record.TypeName));
    }

    private static IEnumerator FinishNextFrame(Vector3? at, string typeName)
    {
        yield return null;

        Structures_BuildSite best = null;
        var bestDistance = 2f;

        foreach (var structure in Structure.Structures)
        {
            if (structure?.Brain is not Structures_BuildSite site) continue;
            if (site.Data == null || site.Data.Destroyed) continue;

            if (!at.HasValue)
            {
                best = site;
                break;
            }

            var distance = Vector3.Distance(site.Data.Position, at.Value);
            if (distance > bestDistance) continue;

            bestDistance = distance;
            best = site;
        }

        if (best == null)
        {
            Plugin.Log.LogWarning($"Base editor: the site for {typeName} could not be found to " +
                                  "finish; a follower will have to build it.");
            yield break;
        }

        try
        {
            best.Build();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Base editor: {typeName} could not be finished: {e.Message}");
        }
    }

    private static readonly Dictionary<StructuresData, HubStructureRecord> _liveModGround = new();

    public static void AdoptModGroundStructure(StructureBrain brain)
    {
        if (brain?.Data == null) return;
        if (brain.Data.Type == StructureBrain.TYPES.PLACEMENT_REGION) return;

        var data = brain.Data;
        var type = data.Type is StructureBrain.TYPES.BUILD_SITE
            or StructureBrain.TYPES.BUILDSITE_BUILDINGPROJECT && data.ToBuildType != StructureBrain.TYPES.NONE
            ? data.ToBuildType
            : data.Type;

        var cell = data.GridTilePosition;
        var file = Current();

        for (var i = file.ModGroundStructures.Count - 1; i >= 0; i--)
        {
            var existing = file.ModGroundStructures[i];
            if (!SameSpot(existing, data.Position, cell)) continue;

            file.ModGroundStructures.RemoveAt(i);
            Forget(existing);
        }

        var record = new HubStructureRecord
        {
            TypeName = StructureTool.InternalNameOf(type),
            GridX = cell.x,
            GridY = cell.y,
            WorldX = data.Position.x,
            WorldY = data.Position.y,
            HasWorld = true,
            Finished = type == data.Type,
            BoundsX = Mathf.Max(1, data.Bounds.x),
            BoundsY = Mathf.Max(1, data.Bounds.y),
            Direction = data.Direction,
            Rotation = data.Rotation
        };

        file.ModGroundStructures.Add(record);
        _liveModGround[data] = record;

        SaveQuietly();
    }

    private static bool SameSpot(HubStructureRecord record, Vector3 position, Vector2Int cell)
    {
        if (record.HasWorld)
            return Vector2.Distance(new Vector2(record.WorldX, record.WorldY), position) <= 0.75f;

        const int noCell = -2147483647;
        if (cell.x == noCell || record.GridX == noCell) return false;

        return record.GridX == cell.x && record.GridY == cell.y;
    }

    private static void Forget(HubStructureRecord record)
    {
        foreach (var pair in _liveModGround)
        {
            if (!ReferenceEquals(pair.Value, record)) continue;
            _liveModGround.Remove(pair.Key);
            return;
        }
    }

    private static void ModGroundStructureRemoved(StructuresData data)
    {
        if (data == null || !_liveModGround.TryGetValue(data, out var record)) return;

        _liveModGround.Remove(data);
        if (Current().ModGroundStructures.Remove(record)) SaveQuietly();
    }

    // ---- the journal ----------------------------------------------------------------------------

    public static bool NoteRemoved(GameObject go)
    {
        if (go == null || RuntimeMapEditor.Context != EditorContext.Base) return false;

        if (IsMine(go))
        {
            RetireBrain(go);
            return false;
        }

        var file = Current();
        var entry = Describe(go);

        file.Removed.RemoveAll(existing => Same(existing, entry));

        file.Moved.RemoveAll(existing => Same(existing, entry));

        file.Removed.Add(entry);
        return true;
    }

    public static void NoteMoved(GameObject go, Vector3 from, Vector3 fromScale)
    {
        if (go == null || RuntimeMapEditor.Context != EditorContext.Base) return;

        if (IsMine(go))
        {
            FollowOwnBuilding(go);
            return;
        }

        var file = Current();

        var structure = FindStructure(go);
        var data = structure?.Brain?.Data;

        if (data != null)
        {
            if (!data.DontLoadMe)
            {
                if (!BaseGround.IsOnBaseGround(go.transform.position))
                    Plugin.Log.LogInfo($"Base editor: {data.Type} now stands off the base's own " +
                                       "ground. Followers will not reach it, and the game's save is " +
                                       "never told - it keeps the original place.");
                else if (ClashesOnGrid(data, go.transform.position))
                    Plugin.Log.LogInfo($"Base editor: {data.Type} now shares a build cell with " +
                                       "another building. Harmless here; the game's save keeps the " +
                                       "original place either way.");

                NoteStructureMoved(file, structure, from);
                return;
            }

            Plugin.Log.LogInfo($"Base editor: {data.Type} is anchored to the scene, so it moves as " +
                               "scenery and the game's own record of it is left untouched.");
        }

        if (Vector3.Distance(from, go.transform.position) <= 0.001f &&
            fromScale == go.transform.localScale)
            return;

        var entry = Describe(go);

        var existing = file.Moved.Find(m => Same(m, entry) ||
                                            Vector3.Distance(
                                                MapEditorSerialization.ToVector3(m.NewPosition), from)
                                            <= MatchRadius);

        if (existing != null)
        {
            existing.Path = entry.Path;
            existing.NewPosition = MapEditorSerialization.V3(go.transform.position);
            existing.Scale = MapEditorSerialization.V3(go.transform.localScale);
            return;
        }

        file.Moved.Add(new BaseMovedVanilla
        {
            Name = entry.Name,
            Key = entry.Key,
            Path = entry.Path,
            OriginalPosition = MapEditorSerialization.V3(from),
            NewPosition = MapEditorSerialization.V3(go.transform.position),
            Scale = MapEditorSerialization.V3(go.transform.localScale)
        });
    }

    private static bool ClashesOnGrid(StructuresData data, Vector3 to)
    {
        if (data.IgnoreGrid || data.DoesNotOccupyGrid) return false;

        var region = PlacementRegion.Instance;
        var tile = region != null ? region.GetClosestTileGridTileAtWorldPosition(to) : null;
        if (tile == null) return false;

        return tile.ObjectID != -1 && tile.ObjectID != data.ID;
    }

    private static void NoteStructureMoved(CTBaseDelta file, Structure structure, Vector3 from)
    {
        var data = structure.Brain.Data;
        var to = structure.transform.position;

        var wasPosition = data.Position;
        var wasCell = data.GridTilePosition;

        if (!MoveStructure(structure.Brain, to)) return;

        var existing = file.MovedStructures.Find(m => m.StructureId == data.ID);
        if (existing != null)
        {
            existing.NewPosition = MapEditorSerialization.V3(to);
            SaveMask.Register(data, MapEditorSerialization.ToVector3(existing.OriginalPosition),
                new Vector2Int(existing.OriginalGridX, existing.OriginalGridY));
            return;
        }

        file.MovedStructures.Add(new BaseMovedStructure
        {
            StructureId = data.ID,
            TypeName = data.Type.ToString(),
            OriginalPosition = MapEditorSerialization.V3(wasPosition),
            OriginalGridX = wasCell.x,
            OriginalGridY = wasCell.y,
            NewPosition = MapEditorSerialization.V3(to)
        });

        SaveMask.Register(data, wasPosition, wasCell);

        Plugin.Log.LogInfo($"Base editor: {data.Type} moved from {wasPosition} to {to}. The game's " +
                           "save keeps the original.");
    }

    // ---- making a placed structure a real building ------------------------------------------------

    public static bool OwnsBase => BaseSession.Active || IsApplying;

    private static readonly List<StructureBrain> _ownedBrains = [];

    private static int _placingOurOwn;

    public static bool PlacingOurOwn => _placingOurOwn > 0;

    public static void GiveBrain(GameObject go, StructureBrain.TYPES type, Vector3 position)
    {
        if (go == null || !OwnsBase) return;

        StructuresData info;
        try
        {
            info = StructuresData.GetInfoByType(type, 0);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Base editor: {type} has no building data ({e.Message}); it stays " +
                                  "a decoration.");
            return;
        }

        if (info == null) return;

        try
        {
            info.CreateStructure(FollowerLocation.Base, position, new Vector2Int(1, 1));

            info.Offset = Vector3.zero;

            StructureBrain brain;
            _placingOurOwn++;
            try
            {
                brain = StructureManager.AddStructure(FollowerLocation.Base, info,
                    emitParticles: false, save: false);
            }
            finally
            {
                _placingOurOwn--;
            }

            if (brain == null) return;

            _ownedBrains.Add(brain);

            var structure = FindStructure(go);
            if (structure != null) structure.Brain = brain;

            try
            {
                brain.AddToGrid();
            }
            catch (Exception)
            {
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Base editor: {type} could not be made into a working building " +
                                  $"({e.Message}); it stays a decoration.");
        }
    }

    public static void RetireBrain(GameObject go)
    {
        if (go == null || _ownedBrains.Count == 0) return;

        var structure = FindStructure(go);
        var brain = structure != null ? structure.Brain : null;
        if (brain == null) return;

        if (!_ownedBrains.Remove(brain)) return;
        Retire(brain);
    }

    public static void RetireOwnedBrains()
    {
        foreach (var brain in _ownedBrains) Retire(brain);
        _ownedBrains.Clear();
    }

    private static void FollowOwnBuilding(GameObject go)
    {
        if (_ownedBrains.Count == 0) return;

        var structure = FindStructure(go);
        var brain = structure != null ? structure.Brain : null;
        if (brain?.Data == null || !_ownedBrains.Contains(brain)) return;

        MoveStructure(brain, structure.transform.position);
    }

    private static void Retire(StructureBrain brain)
    {
        if (brain?.Data == null) return;

        try
        {
            StructureManager.RemoveStructure(brain);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Base editor: one of our buildings could not be unregistered: " +
                                  e.Message);
        }
    }

    // ---- the base's own terrain -------------------------------------------------------------------

    private static readonly Dictionary<UnityEngine.U2D.SpriteShapeController, BaseVanillaRef> _touchedShapes = new();

    public static void NoteShapeTouched(UnityEngine.U2D.SpriteShapeController ctrl)
    {
        if (ctrl == null || RuntimeMapEditor.Context != EditorContext.Base) return;

        if (IsMine(ctrl.gameObject)) return;

        BaseGround.RegisterReshapedGround(ctrl);

        if (_touchedShapes.ContainsKey(ctrl)) return;

        _touchedShapes[ctrl] = Describe(ctrl.gameObject);
    }

    private static void CollectTouchedShapes(CTBaseDelta file)
    {
        if (_touchedShapes.Count == 0) return;

        foreach (var pair in _touchedShapes)
        {
            var ctrl = pair.Key;
            var identity = pair.Value;
            if (ctrl == null) continue;

            var record = file.EditedShapes.Find(existing => Same(existing, identity));
            if (record == null)
            {
                record = new BaseEditedShape
                {
                    Name = identity.Name,
                    Key = identity.Key,
                    OriginalPosition = identity.OriginalPosition
                };
                file.EditedShapes.Add(record);
            }

            record.Path = identity.Path;

            record.Shape = ShapeTool.Describe(ctrl);
        }
    }

    // ---- identity -------------------------------------------------------------------------------

    private static RuntimeMapEditor _toolsFrom;
    private static StructureTool _structures;
    private static NpcTool _npcs;
    private static TriggerTool _triggers;
    private static ShapeTool _shapes;

    private static bool BindTools()
    {
        var editor = RuntimeMapEditor.Active;
        if (editor == null) return false;
        if (ReferenceEquals(editor, _toolsFrom)) return true;

        _toolsFrom = editor;
        _structures = editor.GetTool<StructureTool>();
        _npcs = editor.GetTool<NpcTool>();
        _triggers = editor.GetTool<TriggerTool>();
        _shapes = editor.GetTool<ShapeTool>();
        return true;
    }

    public static bool IsMine(GameObject go)
    {
        if (go == null) return false;

        var root = SceneRefs.ContentRoot;
        if (root != null && go.transform.IsChildOf(root)) return true;

        if (!BindTools()) return false;

        if (_structures?.IsTracked(go) == true) return true;
        if (_npcs?.IsTracked(go) == true) return true;
        if (_triggers?.IsTracked(go) == true) return true;
        if (_shapes?.IsTracked(go) == true) return true;

        return false;
    }

    private static BaseVanillaRef Describe(GameObject go) => new()
    {
        Name = go.name,
        Key = RoomSnapshot.TryResolveKey(go, out var key) ? key : "",
        Path = HierarchyPath(go.transform),
        OriginalPosition = MapEditorSerialization.V3(go.transform.position)
    };

    private static string HierarchyPath(Transform t)
    {
        var parts = new List<string>();
        for (var node = t; node != null; node = node.parent)
            parts.Add(node.name + "#" + node.GetSiblingIndex());

        parts.Reverse();
        return string.Join("/", parts);
    }

    private static bool Same(BaseVanillaRef a, BaseVanillaRef b)
    {
        if (!string.IsNullOrEmpty(a.Path) && !string.IsNullOrEmpty(b.Path)) return a.Path == b.Path;

        return a.Name == b.Name &&
               Vector3.Distance(MapEditorSerialization.ToVector3(a.OriginalPosition),
                   MapEditorSerialization.ToVector3(b.OriginalPosition)) <= MatchRadius;
    }

    // ---- putting the journal back -----------------------------------------------------------------

    private static IEnumerator ApplyJournal(CTBaseDelta file, List<string> report)
    {
        var removed = 0;
        var moved = 0;

        var pendingRemovals = new List<BaseVanillaRef>(file.Removed);
        var pendingMoves = new List<BaseMovedVanilla>(file.Moved);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (pendingRemovals.Count == 0 && pendingMoves.Count == 0) break;

            if (attempt > 0)
            {
                yield return new WaitForSecondsRealtime(1.5f);
            }

            var index = BuildSceneIndex();

            for (var i = pendingRemovals.Count - 1; i >= 0; i--)
            {
                var go = Find(index, pendingRemovals[i]);
                if (go == null) continue;

                UnityEngine.Object.Destroy(go);
                pendingRemovals.RemoveAt(i);
                removed++;
            }

            for (var i = pendingMoves.Count - 1; i >= 0; i--)
            {
                var entry = pendingMoves[i];
                var go = Find(index, entry);
                if (go == null) continue;

                go.transform.position = MapEditorSerialization.ToVector3(entry.NewPosition);
                if (entry.Scale != null && entry.Scale.X != 0f)
                    go.transform.localScale = MapEditorSerialization.ToVector3(entry.Scale);

                pendingMoves.RemoveAt(i);
                moved++;
            }
        }

        foreach (var missed in pendingRemovals)
            Plugin.Log.LogWarning($"Base editor: nothing called '{missed.Name}' is near " +
                                  $"{MapEditorSerialization.ToVector3(missed.OriginalPosition)} any " +
                                  "more, so it was not removed again.");

        foreach (var missed in pendingMoves)
            Plugin.Log.LogWarning($"Base editor: nothing called '{missed.Name}' is near " +
                                  $"{MapEditorSerialization.ToVector3(missed.OriginalPosition)} any " +
                                  "more, so it was left where it is.");

        report.Add($"{removed}/{file.Removed.Count} removed");
        report.Add($"{moved}/{file.Moved.Count} moved");
    }

    private class SceneIndex
    {
        public readonly Dictionary<string, List<Transform>> ByName = new();
        public readonly Dictionary<string, Transform> ByPath = new();
    }

    private static SceneIndex BuildSceneIndex()
    {
        var index = new SceneIndex();

        void Sweep(Transform node, string parentPath, int depth)
        {
            if (node == null || depth > 14) return;

            if (!index.ByName.TryGetValue(node.name, out var list))
            {
                list = [];
                index.ByName[node.name] = list;
            }
            list.Add(node);

            var path = parentPath.Length == 0
                ? node.name + "#" + node.GetSiblingIndex()
                : parentPath + "/" + node.name + "#" + node.GetSiblingIndex();
            index.ByPath[path] = node;

            for (var i = 0; i < node.childCount; i++) Sweep(node.GetChild(i), path, depth + 1);
        }

        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            Sweep(root.transform, "", 0);

        return index;
    }

    private static GameObject Find(SceneIndex index, BaseVanillaRef entry)
    {
        if (entry == null) return null;

        if (!string.IsNullOrEmpty(entry.Path) &&
            index.ByPath.TryGetValue(entry.Path, out var exact) && exact != null &&
            exact.name == entry.Name)
            return exact.gameObject;

        if (!index.ByName.TryGetValue(entry.Name, out var candidates)) return null;

        var target = MapEditorSerialization.ToVector3(entry.OriginalPosition);
        GameObject best = null;
        var bestDistance = MatchRadius;

        foreach (var candidate in candidates)
        {
            if (candidate == null) continue;

            var distance = Vector3.Distance(candidate.position, target);
            if (distance > bestDistance) continue;

            bestDistance = distance;
            best = candidate.gameObject;
        }

        return best;
    }

    // ---- the room -------------------------------------------------------------------------------

    public static void EnsureContentRoot()
    {
        if (SceneRefs.ContentRootOverride != null) return;

        var room = SceneRefs.Room;
        if (room == null) return;

        var go = new GameObject("CultTweaker_BaseContent");
        go.transform.SetParent(room.transform, false);
        go.transform.localPosition = Vector3.zero;
        SceneRefs.ContentRootOverride = go.transform;

        var borrowed = room.CustomTransform != null ? room.CustomTransform.name : "(none)";
        Plugin.Log.LogInfo("Base editor: built a content root of its own for the editor's tools; " +
                           $"the room's own '{borrowed}' is left to the player.");
    }
}
