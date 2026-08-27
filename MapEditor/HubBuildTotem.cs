using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// Anything made from the town's build totem: the placed copy, and the ghost that follows the cursor
// while it is being placed. Both are whole totems as far as a search of the scene is concerned, and
// the ghost is the dangerous one - it has every component the original has, switched off, and it
// stands wherever the author's cursor last was. Copying that produces a totem that looks perfect
// and is deaf, because a disabled interaction never runs and never offers the player anything.
public class HubTotemCopy : MonoBehaviour
{
}

// The build totem, in a hub.
//
// It is not built from scratch: the base has one standing in the same scene a hub runs in, so the
// totem a hub gets is a copy of that one - its art, its interaction, its placement region, its
// build menu. What changes is where it stands, which ground its grid covers, and where the
// buildings the player puts down are written down.
//
// The vanilla one is a scene object rather than a saved structure, which is why it can be copied at
// all: nothing in the save refers to it, and nothing in the save has to be told it now exists.
public static class HubBuildTotem
{
    public const string CloneName = "CultTweaker_HubBuildTotem";

    private static Interaction_PlacementRegion _spawned;

    public static bool Exists => _spawned != null;

    public static PlacementRegion Region => _spawned != null ? _spawned.placementRegion : null;

    public static Vector3 Position => _spawned != null ? _spawned.transform.position : Vector3.zero;

    public static void Forget() => _spawned = null;

    // Everything PlacementRegion.PlayRoutine reaches for without checking. They are all base-scene
    // singletons and a hub runs in the base scene, so this should never fire - but the routine is a
    // coroutine, and a null inside one dies quietly halfway through with the player locked out of
    // input. Better to refuse the interaction and say why.
    public static string MissingDependency()
    {
        if (TownCentre.Instance == null) return "the town centre marker";
        if (HUD_Manager.Instance == null) return "the HUD";
        if (WeatherSystemController.Instance == null) return "the weather system";
        if (PathTileManager.Instance == null) return "the path tiles";
        if (LightingManager.Instance == null) return "the lighting manager";
        if (PlayerFarming.Instance == null) return "the player";
        if (GameManager.GetInstance() == null || GameManager.GetInstance().CamFollowTarget == null)
            return "the camera";
        if (DataManager.Instance != null && DataManager.Instance.MAJOR_DLC &&
            DLCLandController.Instance == null)
            return "the land controller";
        return null;
    }

    // The one in the base, wherever it is and whether or not its room is switched on. Ours is
    // skipped: a hub loaded on top of a hub must not copy the copy.
    private static Interaction_PlacementRegion FindSource()
    {
        Interaction_PlacementRegion best = null;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<Interaction_PlacementRegion>())
        {
            if (candidate == null || candidate.placementRegion == null) continue;

            // Prefabs and assets, not scene objects.
            if (!candidate.gameObject.scene.IsValid()) continue;

            // Ours, whether it is a placed totem or the ghost under the author's cursor. The ghost
            // is the one that bites: its components are switched off, so a copy of it is a totem
            // that never speaks to anybody.
            if (candidate.GetComponentInParent<HubTotemCopy>() != null) continue;
            if (candidate.GetComponentInParent<HubBuildRegion>() != null) continue;
            if (candidate.placementRegion.GetComponent<HubBuildRegion>() != null) continue;

            // Belt and braces for a copy that somehow escaped the marker: a ghost has its
            // components switched off, and the town's own totem never does. Only the component's
            // own flag is worth reading here - the object itself is switched off for the whole
            // time a hub is open, which is exactly why the search has to look at inactive objects
            // in the first place.
            if (!candidate.enabled) continue;

            // The instance flag marks the base's own; anything else is a spare.
            if (candidate.placementRegion.SetAsInstance) return candidate;
            best ??= candidate;
        }

        return best;
    }

    // For the placement ghost: what the totem looks like, before there is one. The same object the
    // real copy starts from, so the ghost is a picture of what will actually be placed.
    public static GameObject FindSourceObject()
    {
        var source = FindSource();
        return source != null ? source.gameObject : null;
    }

    // A ghost is a whole totem too, region and all, and a region claims PlacementRegion.Instance
    // the moment it wakes. A cursor preview must not become the object the rest of the game thinks
    // is the town's build region.
    public static void DisarmGhost(GameObject ghost)
    {
        if (ghost == null) return;

        // Marked first: from here on this is a copy, and the search for the original must not be
        // able to mistake it for one.
        ghost.AddComponent<HubTotemCopy>();

        foreach (var region in ghost.GetComponentsInChildren<PlacementRegion>(true))
            if (region != null) region.SetAsInstance = false;
    }

    // How far the totem itself sits from the root of the object it was copied inside.
    public static Vector3 InteractionOffset(GameObject clone)
    {
        if (clone == null) return Vector3.zero;

        var interaction = clone.GetComponentInChildren<Interaction_PlacementRegion>(true);
        return interaction == null
            ? Vector3.zero
            : interaction.transform.position - clone.transform.position;
    }

    public static GameObject Spawn(Vector3 position, Transform parent)
    {
        if (_spawned != null)
        {
            Plugin.Log.LogWarning("Hub totem: one is already standing; the second was not built.");
            return _spawned.gameObject;
        }

        var source = FindSource();
        if (source == null)
        {
            Plugin.Log.LogWarning("Hub totem: the base's own build totem could not be found, so " +
                                  "there is nothing to copy.");
            return null;
        }

        // The totem is copied. The region is not.
        //
        // "Placement Region" sounds like a component on a post in the ground and is nothing of the
        // sort: in this scene it is the object the base's whole build area hangs off, ground sprite
        // shape, bushes, trees, repair interactions and all. Copying it copied a piece of the town
        // into the hub - the pale slab under the totem, and a stream of null references every frame
        // from repair scripts that were never handed the structure they belong to. Copying the
        // totem alone and copying the region alone is no better: Unity only re-points a reference
        // when both ends are inside the same copy, so the halves end up wired to the original.
        //
        // So only the totem itself is copied, and the region is built here from nothing. A region is
        // not really scenery - it is a polygon, a grid and a few prefab references - and every one
        // of those we can set ourselves.
        var holder = new GameObject("CultTweaker_TotemHolder");
        holder.SetActive(false);
        holder.transform.SetParent(parent, worldPositionStays: false);

        var clone = Object.Instantiate(source.gameObject, holder.transform, worldPositionStays: false);
        clone.name = CloneName;
        clone.AddComponent<HubTotemCopy>();

        var interaction = clone.GetComponent<Interaction_PlacementRegion>();
        if (interaction == null)
        {
            Plugin.Log.LogWarning("Hub totem: the copy came out without its build interaction.");
            Object.Destroy(holder);
            return null;
        }

        var region = BuildRegion(source.placementRegion, clone.transform);
        if (region == null)
        {
            Object.Destroy(holder);
            return null;
        }

        interaction.placementRegion = region;
        IsolateReferences(clone, interaction);
        HideVolumetrics(clone);
        AimPlacementAtTotem(interaction, position);

        // Out of the holder before it is destroyed, and only then does anything wake.
        clone.transform.SetParent(parent, worldPositionStays: false);
        clone.transform.rotation = source.transform.rotation;
        clone.transform.localScale = source.transform.lossyScale;
        clone.transform.position = position;

        var previousInstance = PlacementRegion.Instance;

        clone.SetActive(true);
        Object.Destroy(holder);

        // Belt and braces: if the copy still managed to take the singleton, hand it back.
        if (!ReferenceEquals(PlacementRegion.Instance, previousInstance) &&
            ReferenceEquals(PlacementRegion.Instance, region))
            PlacementRegion.Instance = previousInstance;

        if (!GiveRegionData(region))
        {
            Object.Destroy(clone);
            return null;
        }

        AttachPathTiles(region);

        RegisterWithInteractor(interaction);
        Describe(clone, interaction, region);

        if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(ReportReadiness(interaction));

        _spawned = interaction;
        Plugin.Log.LogInfo($"Hub totem: standing at {position}.");
        return clone;
    }

    // The floor decorations - paths, planks, tiled floors - and why they did nothing in a hub.
    //
    // They are not structures and never touch the buildable grid the way a building does. They are
    // drawn into Unity tilemaps by PathTileManager, which is a singleton that binds itself, in its
    // own Awake, to `GetComponentInParent<PlacementRegion>()`. It lives under the town's region, so
    // that is the region it asks - and it asks it to turn a world position into one of *its* grid
    // cells. A hub is somewhere else in the world entirely, so the answer was always "no such cell"
    // and the tile was quietly dropped.
    //
    // A hub therefore gets its own, parented under its own region so that the same line of code
    // binds to the right one, and carrying its own tilemaps so the town's paths do not come with it.
    private static void AttachPathTiles(PlacementRegion region)
    {
        var source = PathTileManager.Instance;
        if (source == null)
        {
            Plugin.Log.LogInfo("Hub totem: this scene has no path tiles, so floor decorations are " +
                               "not available here.");
            return;
        }

        if (source.GetComponentInParent<HubBuildRegion>() != null) return;

        try
        {
            // Inside a switched-off holder, like everything else here: its Awake claims the
            // singleton and binds its region, and both have to be right before that runs.
            var holder = new GameObject("CultTweaker_PathHolder");
            holder.SetActive(false);
            holder.transform.SetParent(region.transform, worldPositionStays: false);

            var clone = Object.Instantiate(source.gameObject, holder.transform, worldPositionStays: false);
            clone.name = "CultTweaker_HubPathTiles";

            // The same place under our region that the original sits under the town's, so the tile
            // grid lines up with the build grid exactly as it does at home.
            clone.transform.localPosition = source.transform.localPosition;
            clone.transform.localRotation = source.transform.localRotation;
            clone.transform.localScale = source.transform.localScale;

            // A copy of the town's tilemaps arrives holding the town's paths.
            foreach (var tilemap in clone.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true))
                if (tilemap != null) tilemap.ClearAllTiles();

            clone.transform.SetParent(region.transform, worldPositionStays: false);
            clone.SetActive(true);
            Object.Destroy(holder);

            Plugin.Log.LogInfo("Hub totem: floor decorations wired to this hub's own grid.");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Hub totem: floor decorations could not be set up here: " + e.Message);
        }
    }

    // A region, from nothing, standing on the totem: a polygon for the buildable ground, a Structure
    // to hang the grid off, and the handful of prefab references the placement loop instantiates
    // from. Everything else a region carries is either runtime state - which a fresh component
    // brings its own of - or the town's scenery, which is exactly what must not come along.
    private static readonly string[] CopiedFields =
    {
        "PlacementSquare", "BuildSitePrefab", "BuildSiteBuildingProjectPrefab", "PlacementObjectUI",
        "LightingSettings", "overrideLightingProperties"
    };

    private static PlacementRegion BuildRegion(PlacementRegion source, Transform parent)
    {
        if (source == null)
        {
            Plugin.Log.LogWarning("Hub totem: the base's build region could not be read.");
            return null;
        }

        var go = new GameObject("CultTweaker_HubBuildRegion");
        go.transform.SetParent(parent, worldPositionStays: false);

        // The grid's lattice lives in this transform's local space, so its rotation *is* the grid's
        // rotation - the game's tiles sit on a 45-degree diagonal, and a region built upright would
        // put every tile at an angle to the art drawn on it.
        go.transform.localPosition = Vector3.zero;
        go.transform.rotation = source.transform.rotation;
        go.transform.localScale = source.transform.lossyScale;

        var poly = go.AddComponent<PolygonCollider2D>();
        poly.isTrigger = true;
        poly.pathCount = 0;

        var structure = go.AddComponent<Structure>();
        structure.Type = StructureBrain.TYPES.PLACEMENT_REGION;

        // A list with no initialiser behind it, walked by the pathfinding update without a null
        // check. Set by name because it is not in the reference assembly this is built against -
        // that one is a version behind the game.
        EmptyList(structure, "ObstructionColldiers");

        var region = go.AddComponent<PlacementRegion>();
        region.SetAsInstance = false;
        region.structure = structure;
        region.polygonCollider2D = poly;

        // Nothing scatters itself across a hub: those two put weeds and rubble on the town's grid
        // when it is first made, and an author's map is not the town.
        region.PlaceWeeds = false;
        region.PlaceRubble = false;
        region.ResourcesToPlace = [];

        // Vanilla's ceiling is 50, which is a brake on its recursive fill rather than a real limit.
        region.MaxTileCount = int.MaxValue;

        CopyFields(source, region);
        go.AddComponent<HubBuildRegion>();
        return region;
    }

    private static void EmptyList(Component target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance |
                                                         System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.NonPublic);
        if (field == null) return;

        try
        {
            if (field.GetValue(target) == null)
                field.SetValue(target, System.Activator.CreateInstance(field.FieldType));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Hub totem: '{fieldName}' could not be given an empty list: " +
                                  e.Message);
        }
    }

    // Named fields only, never a blanket copy: a region's public surface includes the live grid
    // lookup it is using right now, and sharing that dictionary with the town's own region would
    // have the two of them writing over each other's tiles.
    private static void CopyFields(PlacementRegion source, PlacementRegion target)
    {
        var type = typeof(PlacementRegion);

        foreach (var name in CopiedFields)
        {
            var field = type.GetField(name, System.Reflection.BindingFlags.Instance |
                                            System.Reflection.BindingFlags.Public |
                                            System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                Plugin.Log.LogWarning($"Hub totem: the build region has no '{name}' to copy; the " +
                                      "game may have changed under this.");
                continue;
            }

            try
            {
                field.SetValue(target, field.GetValue(source));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Hub totem: '{name}' could not be copied: {e.Message}");
            }
        }
    }

    // Where the build cursor starts.
    //
    // The totem carries a hard-coded spot to open at - (-7.6, -5.61) - which is a place in the
    // middle of the town's buildable ground, in world coordinates. A hub is somewhere else
    // entirely, so opening the menu sent the cursor, and the camera chasing it, off into the void
    // beside the map; and since the cursor snaps to the nearest tile within a unit and a half of
    // where it lands, a cursor that lands nowhere near the grid cannot place anything.
    private static void AimPlacementAtTotem(Interaction_PlacementRegion interaction, Vector3 position)
    {
        var field = typeof(Interaction_PlacementRegion).GetField("pos",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        if (field == null || field.FieldType != typeof(Vector3))
        {
            Plugin.Log.LogWarning("Hub totem: its build cursor could not be aimed at the hub, so " +
                                  "the menu will open over the town instead.");
            return;
        }

        // Never exactly zero: the totem reads that as "unset" and falls back to the town's spot.
        var aim = position;
        if (aim == Vector3.zero) aim = new Vector3(0.001f, 0f, 0f);

        field.SetValue(interaction, aim);
    }

    // Why a totem placed in the editor did nothing until the map was reloaded.
    //
    // An interaction files itself with the interactor's spatial index from a coroutine that opens
    // with `yield return new WaitForSeconds(0.1f)` - and that is *scaled* time. The editor runs at
    // timeScale 0, so for a totem placed while it is open those 0.1 seconds never pass: the totem
    // stands there, wired correctly and completely unknown to the thing that decides what the
    // player is standing next to. Loading the map instead builds it with the clock running, which
    // is why that route always worked.
    //
    // Filed here directly instead. The waiting coroutine is stopped rather than left to finish when
    // the clock starts again, because the call it makes appends without looking - a totem that
    // registered twice would leave a dead entry behind in the interactor's index when it is
    // destroyed, and a hub is entered more than once.
    private static void RegisterWithInteractor(Interaction interaction)
    {
        if (interaction == null) return;

        try
        {
            interaction.StopAllCoroutines();

            interaction.Position = interaction.transform.position;
            Interactor.RemoveFromRegion(interaction);
            Interactor.AddToRegion(interaction);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("Hub totem: it could not be announced to the interactor, so it " +
                                  "may not respond until the map is reloaded: " + e.Message);
        }
    }

    // The pale slab under a copied totem.
    //
    // The totem's art is sprites; hanging off it are four pieces of 3D geometry that are not art at
    // all - a volumetric light cone, a cylinder and two decal meshes. They only look like anything
    // in the base, where the stencil-lighting rig they belong to is set up to draw them; anywhere
    // else they render as plain white geometry. (The same thing, and the same cause, as the white
    // squares on the F7 portraits.) A hub has no use for the town's lighting props, so they are
    // switched off rather than made to work.
    //
    // Spine draws through a MeshRenderer too, so a skeleton is never touched.
    private static void HideVolumetrics(GameObject clone)
    {
        var hidden = new List<string>();

        foreach (var renderer in clone.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            if (renderer.GetComponent<Spine.Unity.SkeletonRenderer>() != null) continue;

            renderer.enabled = false;
            hidden.Add(renderer.name);
        }

        if (hidden.Count > 0)
            Plugin.Log.LogInfo("Hub totem: the town's lighting props do not light anything here, " +
                               "so they were switched off: " + string.Join(", ", hidden));
    }

    // The bits of itself a totem switches on and off - its lit and unlit states, the tutorial sign,
    // the "new building" badge.
    private static readonly string[] ObjectRefs =
    {
        "ActiveObject", "InactiveObject", "TutorialObject", "NewBuildingAvailableObject",
        "CameraTarget", "TechTree"
    };

    // Anything the copy points at that is not *in* the copy is still pointing at the original, and
    // the totem switches several of these on and off the moment it wakes. Left alone, a hub's totem
    // would be reaching across the world to turn parts of the town's own totem on and off. Each one
    // is given a stub of its own to talk to instead.
    private static void IsolateReferences(GameObject clone, Interaction_PlacementRegion interaction)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                     System.Reflection.BindingFlags.Public |
                                                     System.Reflection.BindingFlags.NonPublic;
        var type = typeof(Interaction_PlacementRegion);
        var stubbed = new List<string>();

        foreach (var name in ObjectRefs)
        {
            var field = type.GetField(name, flags);
            if (field == null || field.FieldType != typeof(GameObject)) continue;

            var value = field.GetValue(interaction) as GameObject;
            if (value != null && value.transform.IsChildOf(clone.transform)) continue;

            field.SetValue(interaction, Stub(clone, name));
            stubbed.Add(name);
        }

        // Played on waking, and a null one throws before the rest of that method runs.
        var animator = type.GetField("newBuildingAnimator", flags);
        if (animator != null && animator.FieldType == typeof(Animator))
        {
            var value = animator.GetValue(interaction) as Animator;
            if (value == null || !value.transform.IsChildOf(clone.transform))
            {
                animator.SetValue(interaction, Stub(clone, "newBuildingAnimator").AddComponent<Animator>());
                stubbed.Add("newBuildingAnimator");
            }
        }

        if (stubbed.Count > 0)
            Plugin.Log.LogInfo("Hub totem: these belong to the town's totem rather than to the " +
                               "copy, so the copy was given its own: " + string.Join(", ", stubbed));
    }

    private static GameObject Stub(GameObject clone, string name)
    {
        var go = new GameObject("CultTweaker_Stub_" + name);
        go.transform.SetParent(clone.transform, worldPositionStays: false);
        go.SetActive(false);
        return go;
    }

    // A moment after the totem is up, once its own Start has run: everything the game consults
    // before it will offer the player a prompt. A totem that stands there and says nothing gives no
    // clue as to which of those it failed, and there are six.
    private static System.Collections.IEnumerator ReportReadiness(Interaction_PlacementRegion interaction)
    {
        // Real seconds: the editor is open at this point, and a scaled wait is exactly the trap
        // that stopped the totem registering itself in the first place.
        yield return new WaitForSecondsRealtime(1.5f);

        if (interaction == null) yield break;

        string label;
        try
        {
            label = interaction.Label;
        }
        catch (System.Exception e)
        {
            label = "<threw: " + e.Message + ">";
        }

        var listed = Interaction.interactions != null && Interaction.interactions.Contains(interaction);
        var building = DataManager.Instance != null && DataManager.Instance.AllowBuilding;

        Plugin.Log.LogInfo($"Hub totem: ready? component {(interaction.enabled ? "on" : "OFF")}, " +
                           $"object {(interaction.gameObject.activeInHierarchy ? "active" : "INACTIVE")}, " +
                           $"label '{label}', interactable {interaction.Interactable}, " +
                           $"listed {listed}, reach {interaction.ActivateDistance}, " +
                           $"AllowBuilding {building}, wolves {Interaction_WolfBase.WolvesActive}, " +
                           $"culling {Interactor.UseInteractionCulling}.");
    }

    // Said once, when a totem is stood up: what the copy actually contains. A hub totem is a copy of
    // a scene object nobody can inspect from here, so when something in it draws wrongly this is the
    // only way to find out what that something is.
    private static void Describe(GameObject clone, Interaction_PlacementRegion interaction,
        PlacementRegion region)
    {
        var parts = new List<string>();
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || parts.Count >= 20) continue;
            parts.Add($"{renderer.name} ({renderer.GetType().Name}" +
                      (renderer.enabled ? "" : ", off") + ")");
        }

        Plugin.Log.LogInfo($"Hub totem: copied '{clone.name}' - interaction on " +
                           $"'{interaction.name}' (reach {interaction.ActivateDistance}), region " +
                           $"'{region.name}'. Renderers: " +
                           (parts.Count == 0 ? "none" : string.Join(", ", parts)));
    }

    // A region reads its grid off the structure data of the Structure it sits on, so it needs one
    // before it can hold a single tile. save: false is the whole point - the totem is ours, it
    // lives in our hub file, and the game's save is not told about it.
    private static bool GiveRegionData(PlacementRegion region)
    {
        var structure = region.structure;
        if (structure == null) structure = region.GetComponent<Structure>();

        if (structure == null)
        {
            Plugin.Log.LogWarning("Hub totem: the region has no Structure to hang its grid on.");
            return false;
        }

        region.structure = structure;

        try
        {
            // Assigning the brain is what the region listens for; it builds its grid off the back
            // of it, which lands in our fill through the CreateFloodFill patch.
            structure.CreateStructure(HubStructureStore.HubLocation, region.transform.position,
                Vector2Int.one, emitParticles: false, save: false);

            // Reading the property is what fills the region's own cached reference to its brain.
            // The build path uses that cache directly, without the lazy getter that would have
            // populated it - so a totem nobody had asked this question of throws on first use.
            _ = region.structureBrain;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError("Hub totem: its region could not be set up: " + e);
            return false;
        }

        if (region.StructureInfo == null)
        {
            Plugin.Log.LogWarning("Hub totem: its region ended up without structure data.");
            return false;
        }

        // Tracked so it is retired with the hub. It is not in the game's save, but it IS in the
        // game's runtime list of what stands in the town room - and that list outlives the scene.
        HubStructureStore.Adopt(structure.Brain);

        // Nothing to buy: the author placed it, so it is already the player's.
        region.StructureInfo.Purchased = true;
        return true;
    }
}
