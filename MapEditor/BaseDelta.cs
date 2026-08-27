using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CustomSpineLoader.MapEditor.Tools;
using CustomSpineLoader.SpineLoaderHelper;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// One vanilla object the author took out of the base, or moved somewhere else in it.
//
// It is remembered by what it is and where it stood, not by any identity the game keeps - there is
// none. A scene object has no id, its name is shared with every copy of it, and its index in the
// hierarchy changes with the game's own decoration rolls. Name plus original position pins it: two
// objects of the same kind are never in the same place.
[Serializable]
public class BaseVanillaRef
{
    public string Name = "";
    public string Key = "";                  // the addressable key, when one could be resolved

    // Where it sits in the scene, as names and sibling indices from the root down. Name and position
    // together are not always enough: the base carries several objects called "Sprite Shape", all of
    // them at the origin, so an edit meant for one of them landed on whichever was reached last.
    // The path tells them apart. Absent on entries written before this existed, which fall back to
    // the name and position and gain a path the next time they are saved.
    public string Path = "";

    public SerializableVector3 OriginalPosition;
}

[Serializable]
public class BaseMovedVanilla : BaseVanillaRef
{
    public SerializableVector3 NewPosition;
    public SerializableVector3 Scale;
}

// A building of the player's the author moved. Kept apart from scenery because putting one back is
// not a matter of moving an object: the grid it is stamped on and the data followers navigate by
// both have to be told, and the game's own save must never learn about any of it (see SaveMask).
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

// A piece of the base's own terrain the author reshaped, moved or restyled.
//
// Not a shape to build - that one already exists and belongs to the player - but the spline it
// should be wearing when they next walk in. Found the way every other journal entry is found: by
// what it is called and where it stood before anyone touched it.
[Serializable]
public class BaseEditedShape : BaseVanillaRef
{
    public MapShapeData Shape;
}

[Serializable]
public class CTBaseDelta
{
    public int Slot = -1;

    // Everything the author added, in the same shape a room blueprint uses - so the tools that write
    // it and the pass that rebuilds it are the ones the rest of the editor already has.
    public CTNodeBlueprint Content = new();

    public List<BaseVanillaRef> Removed = [];
    public List<BaseMovedVanilla> Moved = [];
    public List<BaseMovedStructure> MovedStructures = [];
    public List<BaseEditedShape> EditedShapes = [];

    // Buildings the player put up through their own totem on ground this mod added. The game would
    // throw these away on load - they stand outside the polygon it validates against - so they are
    // lifted out of its save as they are built and put back here.
    public List<HubStructureRecord> ModGroundStructures = [];
}

// The base, as a difference from the base.
//
// See BaseSession for why it is a difference and not a picture. This is the file behind it: one per
// save slot, in the mod's own folder, holding what the author added, what they took away and what
// they moved. It is applied on every arrival in the base and re-read from the tools on every save.
public static class BaseDelta
{
    private const string RootFolder = "CustomBaseMaps";

    // How far apart two positions may be and still be the same object. A scene object lands where
    // the room prefab puts it, to the millimetre, so this only has to absorb the epsilon of a
    // round-trip through json.
    private const float MatchRadius = 0.5f;

    private static CTBaseDelta _file = new();
    private static int _loadedSlot = -1;

    // The user-visible slot. Saving a DLC game shifts SAVE_SLOT by ten for the length of one write,
    // and a base file named from that would be a file the next session cannot find.
    public static int Slot => SaveAndLoad.SAVE_SLOT % 10;

    public static string FolderPath => Path.Combine(Plugin.PluginPath, RootFolder);

    public static string PathForSlot(int slot) => Path.Combine(FolderPath, $"base_slot{slot}.json");

    // What the editor edits. Loading it here rather than in the editor keeps one copy of the file in
    // memory: the apply pass and the session are looking at the same object.
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

    // Nothing to rebuild, so nothing to stand an editor up for. Every player who never opens the
    // base editor takes this branch on every trip home.
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

    // Called by the editor's save. The content it hands back is the same object Content gave it, so
    // this is a write rather than a merge.
    public static bool Save(CTNodeBlueprint content)
    {
        var file = Current();
        if (content != null) file.Content = content;

        CollectTouchedShapes(file);

        // A blueprint is a room's whole furniture; a base delta is only what was added to one. The
        // fields a base session never fills are cleared rather than carried, so nothing that was
        // swept up by a tool in another context can ride along into the player's town.
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

    // Written on the spot rather than at the end of the session: a build the player pays for is
    // theirs from the moment they pay, and there is no reliable "end" to a base visit.
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

    // The author's own spawn point, if this slot's base has one: a trigger carrying the "Hub spawn
    // point" action, the same one a hub is marked with.
    //
    // Read out of the file rather than off the live trigger, and that is the whole reason this exists
    // separately from HubSession.SpawnPoint. The player is put down during the base's arrival, which
    // happens long before the delta is applied - so at the moment the answer is needed, the trigger
    // that carries it does not exist in the scene yet. The file always does.
    public static Vector3? SpawnPoint()
    {
        var trigger = SpawnTrigger();
        return trigger == null ? null : MapEditorSerialization.ToVector3(trigger.Position);
    }

    // The trigger itself, for a caller that needs its extent as well as its middle.
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

        // Re-read: the player may have loaded a different slot since the last visit. The terrain
        // this session had a hand on died with the last scene, and so did the buildings this file is
        // holding on the player's behalf.
        _loadedSlot = -1;
        _touchedShapes.Clear();
        _liveModGround.Clear();

        StructureManager.OnStructureRemoved -= ModGroundStructureRemoved;
        StructureManager.OnStructureRemoved += ModGroundStructureRemoved;

        Plugin.Instance.StartCoroutine(ApplyRoutine());
    }

    private static IEnumerator ApplyRoutine()
    {
        // Long enough for the base to finish arriving, short enough that a scene which never gets
        // there gives up rather than waiting for ever.
        var deadline = Time.realtimeSinceStartup + 45f;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (BiomeBaseManager.Instance != null && PlayerFarming.Instance != null &&
                PlayerFarming.Location == FollowerLocation.Base)
                break;

            yield return null;
        }

        // A trip to a hub lands in the base first and then switches the room out from under it.
        // Rebuilding a base delta into a room that is about to be emptied would put the author's
        // work in the hub and then destroy it.
        if (HubSession.Busy) yield break;

        if (BiomeBaseManager.Instance == null || PlayerFarming.Instance == null) yield break;
        if (BaseSession.BaseRoom() == null) yield break;

        // Before anything reads SceneRefs.Room: in this scene that would otherwise be whichever of
        // the four rooms enabled last.
        BaseSession.ClaimRoom();

        var file = Current();
        if (IsEmpty(file))
        {
            // Still worth remembering what the ground was before anything is added to it: the first
            // shape the author draws in this session needs the vanilla outline to append to.
            BaseGround.RememberVanillaGround();
            yield break;
        }

        // The buildings have to be standing before one of them can be moved, and the game places
        // them from its own save over several frames.
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

        // The tools start each visit holding nothing: what they are about to rebuild IS the delta,
        // so once it is up they are tracking exactly what a save should write back.
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

        // A rebuilt shape needs everything a hand-edited one gets, and the collider is the half that
        // was missing. The base's own shapes ship with collision off; turning it on is what makes a
        // reshaped piece of ground walkable, and the collider that appears then has to be folded into
        // the room's outline. Left on its own it is a solid body - which is why reshaped terrain came
        // back from every load shoving the player around, and why toggling collision off and on again
        // in the editor put it right: that path does this, and the load path did not.
        //
        // A frame later, because sprite shape meshes are generated at end of frame and a collider
        // baked before that captures the outline the shape had before the edit.
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

        // Mesh generation is deferred; bake against the real outlines one frame later.
        yield return null;
        foreach (var ctrl in rebuilt) shapeTool?.FinalizeLoadedShape(ctrl);
        report.Add($"{rebuilt.Count} shape(s)");

        // The ground all of it makes walkable, before anything is asked to stand on it - reshaped
        // terrain as much as newly drawn shapes, since an enlarged piece of the base's own ground is
        // just as new to the polygon and to the build totem's grid as a shape that was not there.
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
                    deferNav: true, seeThrough: s.SeeThrough, fogThrough: s.FogThrough);

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
                    t.Width, t.Height, t.Id, t.Action, t.Once, t.Actions, t.LockPlayerControl);
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

        // The room is exactly what the file says, so opening the editor and closing it again has
        // nothing to lose.
        editor.MarkSaved();
    }

    // ---- the player's own buildings -------------------------------------------------------------

    // A move the author made, put back. Everything MoveBuilding does when the player moves a
    // building through the game's own edit mode, minus the part that would write it to their save:
    // the grid is unstamped and restamped, the data followers navigate by is updated, and the whole
    // change is registered with the save mask so it is invisible to every save write.
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

            // Written by a version of this that did not know better - see NoteMoved. A scene-anchored
            // building's position is the key the game finds it by, so this mod must not be writing
            // it; the entry is dropped rather than acted on, and moving it again records it as
            // scenery instead.
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

        // A move that reported success is not the same as a building that ended up somewhere else.
        // The game is still finishing its own arrival around this - buildings are instantiated from
        // an async load, and its placement routine sets a transform from the data when it lands - so
        // anything that puts one back is going to do it in the next frame or two, after this pass
        // has already congratulated itself. Checked out loud, because a building silently sitting
        // where it started is exactly what this looked like from the outside.
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

    // Put every building this mod has moved back where the game left it, before the game's own
    // loader is allowed to look at them.
    //
    // The second half of the safeguard, and the half that does not depend on anything going right.
    // SaveMask keeps moved positions out of the file as it is written; this assumes that failed and
    // repairs the data on the way back in. Together they mean the loader can never act on a position
    // this mod wrote - which matters because its response to one it dislikes is not to correct it
    // but to delete the building outright:
    //
    //   - an entry whose grid cell another entry has already claimed is removed;
    //   - an entry standing outside the base's ground polygon is removed.
    //
    // Neither is recoverable and neither is announced. The journal knows every building we moved and
    // exactly where it stood, so putting the originals back here is cheap certainty rather than a
    // bet - and the move is re-applied a moment later by the apply pass, so nothing is lost by it.
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
            // Asked too early in the game's own start-up to have a list yet.
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

    // A building reported as moved, and still standing where it was put - yet the player says it has
    // not moved. Two things can be true at once there, and neither shows up in a position check:
    //
    //   - the building is more than one Structure. A shrine sits on a SHRINE_BASE and a temple on a
    //     TEMPLE_BASE, both of which are anchored to the scene; moving the one on top leaves the one
    //     underneath where it was, and the pile is what the player calls "the shrine".
    //   - the object moved and its art did not, if the art hangs off something else.
    //
    // So this says what is still standing at the place it came from, and where its own art actually
    // ended up. Diagnosis, not repair - what to do about it depends on which of the two it is.
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

    // Shared by the apply pass and by the Select tool, so a move made by hand and a move read back
    // out of the file do exactly the same thing to the game.
    public static bool MoveStructure(StructureBrain brain, Vector3 to, Vector3? near = null)
    {
        if (brain?.Data == null) return false;

        // The place the journal remembers this building standing, used to tell twins apart when one
        // save entry is worn by more than one object.
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
                // The data moves and nothing visible does. Said out loud because the two look
                // identical from a log that only counts successes, and the building sitting where
                // it started is the whole of what the player sees.
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

    // Tells the navigation graph the building has moved, so followers path around its new footprint
    // rather than the old one. Called by name: the method is on Structure in the shipping game but
    // not in the reference assembly this mod is built against.
    private static System.Reflection.MethodInfo _updateGraphBounds;
    private static bool _lookedForGraphBounds;

    private static void UpdateGraphBounds(Structure structure)
    {
        if (!_lookedForGraphBounds)
        {
            _lookedForGraphBounds = true;
            _updateGraphBounds = HarmonyLib.AccessTools.Method(typeof(Structure), "UpdateGraphBounds",
                [typeof(bool)]);

            if (_updateGraphBounds == null)
                Plugin.Log.LogWarning("Base editor: this game has no Structure.UpdateGraphBounds; a " +
                                      "moved building's footprint will not be re-pathed until the " +
                                      "next full rescan.");
        }

        if (_updateGraphBounds == null)
        {
            SceneRefs.RescanNavigation();
            return;
        }

        _updateGraphBounds.Invoke(structure, [true]);
    }

    // The scene object a brain belongs to.
    //
    // Matched on the building's id, not on the brain being the same object. Those are not the same
    // question: the loader rebinds data to scene objects on every arrival, and a brain fetched from
    // a data entry can easily be a different instance from the one the standing building is holding.
    // When this returned null the data moved and the building did not - which is what left a shrine
    // sitting in the middle of the base while everything else insisted it had been moved.
    private static Structure FindStructureObject(StructureBrain brain, Vector3? near = null)
    {
        if (brain?.Data == null) return null;

        // Every object that answers to this building, not the first one that does.
        //
        // A shrine turns out to have a twin: two Structure components bound to the same save entry,
        // one of them an empty stub with no art at all. Taking the first match moved the stub and
        // left the shrine standing in the middle of the base - which looked exactly like the move
        // failing, because from the outside it was.
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

        // Whichever one the player can actually see. A building with no renderers anywhere under it
        // is not the building; moving it moves nothing anyone will ever notice.
        Structure best = null;
        var bestScore = float.MinValue;

        foreach (var candidate in candidates)
        {
            var score = candidate.GetComponentsInChildren<Renderer>(true).Length > 0 ? 1000f : 0f;

            // Among equals, the one standing where the journal says this building was.
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

    // The building an edited object belongs to.
    //
    // Itself or something inside it first, and only then something it hangs off. A dragged object
    // takes its children with it and leaves its parents where they are, so a Structure found upwards
    // is one that did not move - reading its position as "where the author put it" reports the place
    // it already was.
    private static Structure FindStructure(GameObject go)
    {
        var structure = go.GetComponent<Structure>();
        if (structure != null) return structure;

        structure = go.GetComponentInChildren<Structure>(true);
        return structure != null ? structure : go.GetComponentInParent<Structure>();
    }

    private static StructureBrain FindBrain(BaseMovedStructure move)
    {
        // The id first: it is the one thing about a building the game's own save does keep.
        if (move.StructureId >= 0)
        {
            foreach (var data in StructureManager.StructuresDataAtLocation(FollowerLocation.Base))
                if (data != null && data.ID == move.StructureId)
                    return StructureBrain.GetOrCreateBrain(data);
        }

        // Otherwise the building of that type standing where it stood.
        var from = MapEditorSerialization.ToVector3(move.OriginalPosition);
        foreach (var data in StructureManager.StructuresDataAtLocation(FollowerLocation.Base))
        {
            if (data == null || data.Type.ToString() != move.TypeName) continue;
            if (Vector3.Distance(data.Position, from) > MatchRadius) continue;
            return StructureBrain.GetOrCreateBrain(data);
        }

        return null;
    }

    // Buildings the player put up on ground this mod added. They were lifted out of the game's save
    // as they were built, so nothing else will ever put them back.
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
                // The same call the totem makes, free of charge - it was paid for when it was first
                // put down. By world position where one was recorded: the grid cell is the sentinel
                // for anything the game keeps off its grid, and no lookup can find a tile from that.
                if (record.HasWorld)
                    region.PlaceStructureAtWorldPosition(type,
                        new Vector3(record.WorldX, record.WorldY, 0f), bounds);
                else
                    region.PlaceStructureAtGridPosition(type,
                        new Vector2Int(record.GridX, record.GridY), bounds);

                // Building it produces a site, whichever call was used. One that was already standing
                // when the player left is finished on the spot - they built it once and should not be
                // asked to wait for it again.
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

    // The site that has just been rebuilt for a record, finished on the spot.
    //
    // A frame later than the placement, because the region stamps a site's cell and bounds after the
    // call that creates it, and finishing one before that builds it in the wrong place. Found by
    // where it stands rather than by holding a reference, because what the placement hands back is
    // not the site - the site arrives through AddStructure a moment afterwards.
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

    // Called by the patch that lifts a build on mod ground back out of the game's save list.
    // What this mod is holding on behalf of the player, keyed by the data object the game hands
    // around - the only thing both ends of a build agree on.
    private static readonly Dictionary<StructuresData, HubStructureRecord> _liveModGround = new();

    public static void AdoptModGroundStructure(StructureBrain brain)
    {
        if (brain?.Data == null) return;
        if (brain.Data.Type == StructureBrain.TYPES.PLACEMENT_REGION) return;

        // A build site IS the building as far as this file is concerned, and skipping it was losing
        // people their buildings outright. In a hub nobody lives there, so a site is finished the
        // instant it appears and only the finished thing was ever worth writing down. In the base a
        // follower walks over with an armful of wood - so between the player paying for it and that
        // follower arriving, the site is all there is. Stripped from the game's save and then not
        // recorded here, it was gone on the next visit along with what it cost.
        //
        // So the record is of what is being built rather than of what is standing: a site is written
        // down as its ToBuildType, and restoring it puts the site back for a follower to finish, the
        // same way the player put it there.
        var data = brain.Data;
        var type = data.Type is StructureBrain.TYPES.BUILD_SITE
            or StructureBrain.TYPES.BUILDSITE_BUILDINGPROJECT && data.ToBuildType != StructureBrain.TYPES.NONE
            ? data.ToBuildType
            : data.Type;

        var cell = data.GridTilePosition;
        var file = Current();

        // The site and the building it becomes are two calls about one thing. The second replaces the
        // first rather than joining it, or the spot would come back built twice over.
        //
        // Matched on where it stands, not on which cell it claims. A building the game keeps off its
        // grid carries the sentinel cell instead of a real one, so matching by cell would have made
        // every off-grid building on added ground look like the same building as every other.
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

    // One building or two? By position where the record has one, and only otherwise by cell - and
    // never by a sentinel cell, which means "no cell" rather than a place.
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

    // Demolished, or otherwise taken out of the game. Nothing else would ever drop these: they are
    // not in the player's save, so the game's own bookkeeping never touches this file.
    //
    // Matched on the data object rather than the cell, because a build site retiring as it becomes
    // its building fires this for a cell that has just been recorded again - and by then the site's
    // own record is gone, so there is nothing here to find and nothing to undo.
    private static void ModGroundStructureRemoved(StructuresData data)
    {
        if (data == null || !_liveModGround.TryGetValue(data, out var record)) return;

        _liveModGround.Remove(data);
        if (Current().ModGroundStructures.Remove(record)) SaveQuietly();
    }

    // ---- the journal ----------------------------------------------------------------------------

    // The editor is about to destroy something. Returns true when it was the player's rather than
    // ours, so the caller knows the deletion has been written down and will happen again next visit.
    public static bool NoteRemoved(GameObject go)
    {
        if (go == null || RuntimeMapEditor.Context != EditorContext.Base) return false;

        if (IsMine(go))
        {
            // Ours, so nothing to write down - but if it was a working building, the brain behind
            // it has to go with the object it belonged to.
            RetireBrain(go);
            return false;
        }

        var file = Current();
        var entry = Describe(go);

        file.Removed.RemoveAll(existing => Same(existing, entry));

        // A thing that was moved and is now gone only needs the second half saying.
        file.Moved.RemoveAll(existing => Same(existing, entry));

        file.Removed.Add(entry);
        return true;
    }

    // The editor has finished moving something. `from` is where it was when the gesture started.
    public static void NoteMoved(GameObject go, Vector3 from, Vector3 fromScale)
    {
        if (go == null || RuntimeMapEditor.Context != EditorContext.Base) return;

        if (IsMine(go))
        {
            // Nothing to journal - the tool that placed it writes down where it ended up. But a
            // building of ours navigates by its data like any other, and dragging the art does not
            // move that: followers would keep walking to where it used to be.
            FollowOwnBuilding(go);
            return;
        }

        var file = Current();

        // A building of the player's is not scenery: it has to be moved in the game's own terms or
        // the followers who use it walk to where it used to be.
        //
        // Except when it is anchored to the scene. The game binds those to their scene object by
        // exact position equality when it loads - and deletes the save entry outright when nothing
        // is standing where the data says. Their position is therefore not a fact about them, it is
        // the key they are found by, and this mod does not get to write it. Those move as scenery:
        // the object goes where the author put it and the player's save is never involved. The cost
        // is that followers still walk to where the game thinks it is, which is a strange-looking
        // temple rather than a lost one.
        var structure = FindStructure(go);
        var data = structure?.Brain?.Data;

        if (data != null)
        {
            // Anywhere is allowed. The two destinations the game's own loader objects to - off its
            // ground, or on a cell another building has claimed - are worth saying out loud, because
            // a building standing on one of them is a building the *vanilla* game would delete. It
            // cannot delete this one: the loader is never shown the position, by the mask on the way
            // out and by the repair pass on the way in.
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

            // The one kind that still moves as scenery. Its position is not a fact about it but the
            // key the game finds its scene object by, and a key is not ours to write.
            Plugin.Log.LogInfo($"Base editor: {data.Type} is anchored to the scene, so it moves as " +
                               "scenery and the game's own record of it is left untouched.");
        }

        // A gesture that ended where it began. The caller filters those, but a resize does not move
        // anything and an undo can land a thing back on its own starting point - and an entry whose
        // two positions are the same is a line the apply pass has to look up in order to do nothing.
        if (Vector3.Distance(from, go.transform.position) <= 0.001f &&
            fromScale == go.transform.localScale)
            return;

        var entry = Describe(go);

        // Where it started this session is not where it started originally: an object moved twice
        // must still be findable from where the game itself puts it.
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

    // Would writing this building's new cell put two save entries on the same one?
    //
    // The loader walks the save backwards collecting grid cells, and deletes any entry whose cell it
    // has already seen. Two buildings sharing a cell therefore costs the player one of them - and
    // which one depends on the order they happen to sit in the file.
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

        // The transform has already been dragged; everything the game reasons about the building by
        // has not. Only the data is put right here - the transform is already where the author left
        // it, and snapping it back to the data first (as this used to) made the building visibly jump
        // to wherever it was last moved to before settling, one step behind the drag every time.
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

    // Whether this mod is currently the one running the base: a session is open, or a saved base is
    // being rebuilt on arrival. The two are different states - the apply pass runs with no session -
    // but everything below is true of both.
    public static bool OwnsBase => BaseSession.Active || IsApplying;

    // Brains we made. They are not in the game's save, so nothing but this will ever clear them, and
    // a brain left behind is a building the base thinks it still has the next time anyone walks in.
    private static readonly List<StructureBrain> _ownedBrains = [];

    // Depth rather than a flag: nothing nests today, but a building that converts from an upgrade
    // would.
    private static int _placingOurOwn;

    // Read by the patch that drops objective progress raised by our own placements.
    public static bool PlacingOurOwn => _placingOurOwn > 0;

    // A structure the editor placed, made into a building the game will actually run.
    //
    // Instantiating the prefab gets the art and nothing else: the components on it read
    // Structure.Brain, and without one they have nothing to work with - which is why a bed placed
    // here could not be slept in and a plot could not be farmed. The brain is what makes it a
    // building, and the game will make one without being told to save it: AddStructure's `save` flag
    // is the list write alone, and the brain is created either way.
    //
    // So the building works exactly as the game intends and the player's save never hears about it.
    // Ours is remembered in our own file and put back on the next arrival, like everything else here.
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

        // A type the game has no building for - a custom structure, or one of the decorative types
        // that was never a building. Those stay exactly what they were: scenery.
        if (info == null) return;

        try
        {
            // One tile unless the game says otherwise. Bounds only decide how much of the buildable
            // grid this covers, so the cost of guessing small is that the build totem may let
            // something be placed overlapping it - not that the building fails to work.
            info.CreateStructure(FollowerLocation.Base, position, new Vector2Int(1, 1));

            // CreateStructure jitters every building a little so a row of them does not look
            // stamped. The editor placed this one exactly where it was asked to, and the Brain
            // setter below adds the offset to the transform.
            info.Offset = Vector3.zero;

            // AddStructure tells the objective system a structure was placed. That is right for a
            // building the player paid for and wrong for one the editor put down - and the apply
            // pass re-places every one of them on every arrival, so a quest could tick over and over
            // for work nobody did. The call is scoped so those ticks are dropped.
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

            // So the totem's grid knows the cell is taken.
            try
            {
                brain.AddToGrid();
            }
            catch (Exception)
            {
                // A cell the base's grid does not have - the structure stands off the buildable
                // area. It still works; nothing can be built on top of it because nothing can be
                // built there at all.
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Base editor: {type} could not be made into a working building " +
                                  $"({e.Message}); it stays a decoration.");
        }
    }

    // One of ours has been destroyed, or the base is being left. A brain outlives the object it
    // belongs to, and the game only clears them on quit, death or the menu.
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

    // One of our own buildings has been dragged somewhere else. Same data update the game makes when
    // the player moves a building through its own edit mode - and no journal entry, because nothing
    // about it is the player's to remember.
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

    // Pieces of the player's ground the author has had a hand on this session, and where each one
    // stood before they did. Kept as live references rather than as records: a drag commits its
    // shape on every frame it moves, and writing down forty spline points sixty times a second to
    // describe one gesture is work nobody asked for. What they end up looking like is read once, at
    // the save.
    private static readonly Dictionary<UnityEngine.U2D.SpriteShapeController, BaseVanillaRef> _touchedShapes = new();

    public static void NoteShapeTouched(UnityEngine.U2D.SpriteShapeController ctrl)
    {
        if (ctrl == null || RuntimeMapEditor.Context != EditorContext.Base) return;

        // What the tool made is the tool's, and rides out through the blueprint like any other
        // shape it owns.
        if (IsMine(ctrl.gameObject)) return;

        // Its outline joins the base's ground the moment it is touched: enlarging a piece of the
        // base's own terrain is adding ground, and nothing else would notice it grew.
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

            // An entry written before paths existed learns its own here, so the next arrival can
            // find it exactly rather than by name and position.
            record.Path = identity.Path;

            record.Shape = ShapeTool.Describe(ctrl);
        }
    }

    // ---- identity -------------------------------------------------------------------------------

    // Is this one of ours? Everything the tools place in the base goes under the content root this
    // mod gives the base room, or is tracked by the tool that made it. Anything else is the player's,
    // and touching it is what the journal is for.
    // The tools are looked up through a LINQ scan of the editor's tool list, which is fine once and
    // not fine thousands of times - and this is asked about every renderer in the base whenever
    // something is picked. Held per editor host, so a new one invalidates them.
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

    // Names and sibling indices from the scene root down. The index matters: two children of the
    // same parent can share a name, and in the base they often do.
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

    // The removals and the moves, applied against the scene as the game has just built it.
    //
    // Two passes, because the base does not finish arriving all at once: its decorations are pooled
    // back in from a coroutine and some of its furniture is switched on later still. Anything the
    // first pass could not find is looked for again a moment later, and only then given up on.
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
                // Unscaled: an arrival can land with the clock still stopped.
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

    // Every transform in the scene, by name.
    //
    // The whole scene, not the room: the base's furniture is spread across the scene's roots - the
    // indoctrination ring, the teleporter's collision, the land tiles - and a sweep that started at
    // GenerateRoom found none of it. Built once per pass and shared by every entry, because walking
    // a scene this size per journal line is the kind of thing that turns an arrival into a stutter.
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

    // The object a journal entry is about. Never a guess: a miss leaves the scenery alone rather
    // than moving the wrong thing.
    //
    // The path is asked first and is the answer whenever the scene still has it, because it is the
    // only one of the two that can tell apart objects sharing a name and a position - which in the
    // base is most of the sprite shapes. Name and position are the fallback, for entries written
    // before paths were recorded and for a scene that has been rearranged under one.
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

    // Where the base editor's own work lives.
    //
    // A container of our own, never the room's. Elsewhere the tools build into the room's
    // CustomTransform, which a generated dungeon room hands over empty - but the base ships one
    // already, with the player's own things under it. Since "is it under the content root" is how
    // this editor tells its work from theirs, borrowing that container told it everything in the
    // base was ours: nothing was protected from deletion, and nothing done to the player's own
    // scenery was written down, because the journal skips what it thinks it owns.
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
