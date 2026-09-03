using System.Collections.Generic;
using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public class HubTotemCopy : MonoBehaviour
{
}

public static class HubBuildTotem
{
    public const string CloneName = "CultTweaker_HubBuildTotem";

    private static Interaction_PlacementRegion _spawned;

    public static bool Exists => _spawned != null;

    public static PlacementRegion Region => _spawned != null ? _spawned.placementRegion : null;

    public static Vector3 Position => _spawned != null ? _spawned.transform.position : Vector3.zero;

    public static void Forget() => _spawned = null;

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

    private static Interaction_PlacementRegion FindSource()
    {
        Interaction_PlacementRegion best = null;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<Interaction_PlacementRegion>())
        {
            if (candidate == null || candidate.placementRegion == null) continue;

            if (!candidate.gameObject.scene.IsValid()) continue;

            if (candidate.GetComponentInParent<HubTotemCopy>() != null) continue;
            if (candidate.GetComponentInParent<HubBuildRegion>() != null) continue;
            if (candidate.placementRegion.GetComponent<HubBuildRegion>() != null) continue;

            if (!candidate.enabled) continue;

            if (candidate.placementRegion.SetAsInstance) return candidate;
            best ??= candidate;
        }

        return best;
    }

    public static GameObject FindSourceObject()
    {
        var source = FindSource();
        return source != null ? source.gameObject : null;
    }

    public static void DisarmGhost(GameObject ghost)
    {
        if (ghost == null) return;

        ghost.AddComponent<HubTotemCopy>();

        foreach (var region in ghost.GetComponentsInChildren<PlacementRegion>(true))
            if (region != null) region.SetAsInstance = false;
    }

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

        clone.transform.SetParent(parent, worldPositionStays: false);
        clone.transform.rotation = source.transform.rotation;
        clone.transform.localScale = source.transform.lossyScale;
        clone.transform.position = position;

        var previousInstance = PlacementRegion.Instance;

        clone.SetActive(true);
        Object.Destroy(holder);

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
            var holder = new GameObject("CultTweaker_PathHolder");
            holder.SetActive(false);
            holder.transform.SetParent(region.transform, worldPositionStays: false);

            var clone = Object.Instantiate(source.gameObject, holder.transform, worldPositionStays: false);
            clone.name = "CultTweaker_HubPathTiles";

            clone.transform.localPosition = source.transform.localPosition;
            clone.transform.localRotation = source.transform.localRotation;
            clone.transform.localScale = source.transform.localScale;

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

        go.transform.localPosition = Vector3.zero;
        go.transform.rotation = source.transform.rotation;
        go.transform.localScale = source.transform.lossyScale;

        var poly = go.AddComponent<PolygonCollider2D>();
        poly.isTrigger = true;
        poly.pathCount = 0;

        var structure = go.AddComponent<Structure>();
        structure.Type = StructureBrain.TYPES.PLACEMENT_REGION;

        // Set by name until the project moved off the 1.5.15 game assemblies, which did not declare
        // this list; 1.5.26 does. It has no initialiser and the pathfinding update walks it without a
        // null check, so it has to be a list before the region is built. The name's typo is the
        // game's own.
        structure.ObstructionColldiers ??= [];

        var region = go.AddComponent<PlacementRegion>();
        region.SetAsInstance = false;
        region.structure = structure;
        region.polygonCollider2D = poly;

        region.PlaceWeeds = false;
        region.PlaceRubble = false;
        region.ResourcesToPlace = [];

        region.MaxTileCount = int.MaxValue;

        CopyFields(source, region);
        go.AddComponent<HubBuildRegion>();
        return region;
    }

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

        var aim = position;
        if (aim == Vector3.zero) aim = new Vector3(0.001f, 0f, 0f);

        field.SetValue(interaction, aim);
    }

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

    private static readonly string[] ObjectRefs =
    {
        "ActiveObject", "InactiveObject", "TutorialObject", "NewBuildingAvailableObject",
        "CameraTarget", "TechTree"
    };

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

    private static System.Collections.IEnumerator ReportReadiness(Interaction_PlacementRegion interaction)
    {
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
            structure.CreateStructure(HubStructureStore.HubLocation, region.transform.position,
                Vector2Int.one, emitParticles: false, save: false);

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

        HubStructureStore.Adopt(structure.Brain);

        region.StructureInfo.Purchased = true;
        return true;
    }
}
