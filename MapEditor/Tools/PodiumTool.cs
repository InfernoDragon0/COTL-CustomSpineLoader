using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

// Per-podium equip behavior. Vanilla treats a room's podiums as choose-one-of-N: equipping
// from one disables all the others. With ClearAllOnEquip false only the used podium is
// consumed - the OnInteract patch below blanks the disable-others list for the call.
public class CTPodiumBehavior : MonoBehaviour
{
    public bool ClearAllOnEquip = true;
}

// Places weapon selection podiums (the pedestals guaranteed in every dungeon's first room).
//
// The game never instantiates these from code except via Interaction_Chest, so the prefab is
// acquired either from a loaded chest's serialized asset reference or by cloning a scene podium
// before anything can destroy it. Both routes keep the instance under an INACTIVE holder while
// fields are fixed up: OnEnableInteraction destroys any podium whose RemoveIfNotFirstLayer flag
// is still set once the run has left its first room, and the inactive window is what lets us
// clear that flag before the check runs.
public class PodiumTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Podiums";

    private readonly RuntimeMapEditor _editor;
    private readonly List<PlacedPodium> _placed = [];

    private GameObject _template;
    private GameObject _holder;
    private GameObject _preview;
    private bool _placing;
    private string _type = "Random";
    private TMP_Text _typeLabel;

    private class PlacedPodium
    {
        public GameObject Instance;
        public string SavedType;
        public bool ClearAllOnEquip = true;
    }

    private bool _clearAllOnEquip = true;

    // The disable-others pass at the tail of OnInteract walks otherWeaponOptions; swapping in
    // an empty array for the duration of the call is the least invasive way to skip it for
    // keep-others podiums while leaving every other podium (and vanilla rooms) untouched.
    [HarmonyPatch(typeof(Interaction_WeaponSelectionPodium),
        nameof(Interaction_WeaponSelectionPodium.OnInteract))]
    private static class Podium_OnInteract_Patch
    {
        private class SwapState
        {
            public Interaction_WeaponSelectionPodium[] Original;
        }

        private static void Prefix(Interaction_WeaponSelectionPodium __instance, ref SwapState __state)
        {
            __state = null;

            var marker = __instance.GetComponentInParent<CTPodiumBehavior>();
            if (marker == null || marker.ClearAllOnEquip) return;

            __state = new SwapState { Original = __instance.otherWeaponOptions };
            __instance.otherWeaponOptions = new Interaction_WeaponSelectionPodium[0];
        }

        private static void Postfix(Interaction_WeaponSelectionPodium __instance, SwapState __state)
        {
            if (__state != null) __instance.otherWeaponOptions = __state.Original;
        }
    }

    public PodiumTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        ui.CreateLabel(panel, "Podium Tool", 20, TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Weapon podiums like the ones\nin each dungeon's first room.", 14, TextAlignmentOptions.Center);

        _typeLabel = ui.CreateLabel(panel, "Type: " + _type, 15, TextAlignmentOptions.Center)
            .GetComponent<TMP_Text>();

        foreach (var type in new[] { "Random", "Weapon", "Curse", "Relic" })
        {
            var captured = type;
            ui.CreateButton(panel, "Type: " + captured, () =>
            {
                _type = captured;
                if (_typeLabel != null) _typeLabel.text = "Type: " + _type;
                DestroyPreview();
                _editor.SetStatus($"Podium type set to {_type}.");
            });
        }

        ui.CreateToggle(panel, "Place on click", _placing, v =>
        {
            _placing = v;
            if (!v) DestroyPreview();
            _editor.SetStatus(v ? "Left-click in the world to place a podium." : "Podium placement off.");
        });

        ui.CreateToggle(panel, "Equip clears all", _clearAllOnEquip, v =>
        {
            _clearAllOnEquip = v;
            _editor.SetStatus(v
                ? "Vanilla behavior: equipping from one podium disables the room's others."
                : "Equipping consumes only that podium; the others stay usable.");
        });

        ui.CreateButton(panel, "Undo Last Podium", UndoLast);
    }

    public void OnEnter() => _editor.SetStatus("Podium tool: pick a type, enable placement, click the world.");
    public void OnExit() => DestroyPreview();

    public void OnUpdate()
    {
        if (!_placing)
        {
            DestroyPreview();
            return;
        }

        UpdatePreview();

        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;
        SpawnPodium(_editor.MouseWorld(), _type, _clearAllOnEquip);
    }

    // Same cursor ghost treatment as the enemy tool.
    private void UpdatePreview()
    {
        if (_preview != null)
        {
            _preview.transform.position = _editor.MouseWorld();
            return;
        }

        var template = AcquireTemplate();
        if (template == null) return;

        // Scripts run so the podium initializes its runtime-assigned materials (a dead script
        // leaves a pink error mesh); the self-destroy flag is cleared before anything wakes.
        _preview = MapEditorGhost.Create(template, _editor.transform, "CultTweaker_PodiumPreview",
            disableBehaviours: false, beforeWake: g =>
            {
                var podium = g.GetComponentInChildren<Interaction_WeaponSelectionPodium>(true);
                if (podium != null)
                {
                    podium.RemoveIfNotFirstLayer = false;
                    podium.Type = ResolveType(_type);
                }
            });
        if (_preview != null) _preview.transform.position = _editor.MouseWorld();
    }

    private void DestroyPreview()
    {
        if (_preview != null) Object.Destroy(_preview);
        _preview = null;
    }

    // The room snapshot skips objects this tool already serializes.
    public bool IsTracked(GameObject go)
    {
        foreach (var placed in _placed)
            if (placed.Instance == go) return true;
        return false;
    }

    // The loader wipes the room; everything tracked here is gone.
    public void ResetTracking() => _placed.Clear();

    // Also the loader's entry point: self-registers, so load then save round-trips.
    public GameObject SpawnPodium(Vector3 position, string typeName, bool clearAllOnEquip = true)
    {
        var template = AcquireTemplate();
        if (template == null)
        {
            _editor.SetStatus("No podium prefab could be found; podiums unavailable.");
            return null;
        }

        var parent = SceneRefs.ContentRoot;
        if (parent == null)
        {
            _editor.SetStatus("No room content root; cannot place a podium.");
            return null;
        }

        var go = Object.Instantiate(template, Holder().transform);
        go.name = "CultTweaker_Podium";

        var podium = go.GetComponentInChildren<Interaction_WeaponSelectionPodium>(true);
        if (podium == null)
        {
            Object.Destroy(go);
            _editor.SetStatus("Podium template had no podium component.");
            return null;
        }

        // Defeats the first-room-only self-destroy in OnEnableInteraction; no Harmony needed.
        podium.RemoveIfNotFirstLayer = false;
        podium.WeaponTaken = false;
        podium.Type = ResolveType(typeName);
        go.AddComponent<CTPodiumBehavior>().ClearAllOnEquip = clearAllOnEquip;

        go.transform.SetParent(parent, true);
        go.transform.position = position;
        go.SetActive(true);

        _placed.Add(new PlacedPodium { Instance = go, SavedType = typeName, ClearAllOnEquip = clearAllOnEquip });
        _editor.SetStatus($"Placed {typeName} podium at {position}.");
        return go;
    }

    // Curse podiums destroy themselves when spells are disabled, so they are downgraded rather
    // than silently vanishing.
    private static Interaction_WeaponSelectionPodium.Types ResolveType(string typeName)
    {
        if (!System.Enum.TryParse<Interaction_WeaponSelectionPodium.Types>(typeName, out var type))
            type = Interaction_WeaponSelectionPodium.Types.Random;

        if (type == Interaction_WeaponSelectionPodium.Types.Curse &&
            DataManager.Instance != null && !DataManager.Instance.EnabledSpells)
        {
            Plugin.Log.LogWarning("MapEditor: spells are disabled, podium downgraded from Curse to Weapon.");
            type = Interaction_WeaponSelectionPodium.Types.Weapon;
        }

        return type;
    }

    // Must run while the scene is still intact, so the loader calls it in its capture phase.
    public GameObject AcquireTemplate()
    {
        if (_template != null) return _template;

        // A loaded chest asset carries the podium prefab as a serialized addressable reference;
        // the getter blocking-loads it. A raw prefab has never run Awake, which is ideal.
        foreach (var chest in Resources.FindObjectsOfTypeAll<Interaction_Chest>())
        {
            try
            {
                var prefab = chest.WeaponPodiumPrefab;
                if (prefab == null) continue;
                _template = prefab;
                Plugin.Log.LogInfo("MapEditor: podium template resolved via chest asset reference.");
                return _template;
            }
            catch (System.Exception)
            {
                // This chest's reference is unset or failed to load; try the next.
            }
        }

        // Fallback: clone a live podium (the entrance room has them) into the inactive holder.
        foreach (var podium in Resources.FindObjectsOfTypeAll<Interaction_WeaponSelectionPodium>())
        {
            if (podium == null || !podium.gameObject.scene.IsValid()) continue;

            var clone = Object.Instantiate(podium.gameObject, Holder().transform);
            clone.name = "MapEditor_PodiumTemplate";
            _template = clone;
            Plugin.Log.LogInfo("MapEditor: podium template cloned from a scene podium.");
            return _template;
        }

        Plugin.Log.LogWarning("MapEditor: no podium template found (no chest asset, no scene podium).");
        return null;
    }

    // Inactive parent = instantiated children never run Awake/OnEnable until released.
    private GameObject Holder()
    {
        if (_holder != null) return _holder;
        _holder = new GameObject("MapEditor_PodiumHolder");
        _holder.SetActive(false);
        _holder.transform.SetParent(_editor.transform, false);
        return _holder;
    }

    private void UndoLast()
    {
        if (_placed.Count == 0)
        {
            _editor.SetStatus("No podiums placed yet.");
            return;
        }

        var last = _placed[_placed.Count - 1];
        _placed.RemoveAt(_placed.Count - 1);
        if (last.Instance != null) Object.Destroy(last.Instance);
        _editor.SetStatus("Removed the last podium.");
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.Podiums.Clear();
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            map.Podiums.Add(new MapPodiumData
            {
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                // The originally chosen type, not the post-roll runtime value, so Random
                // round-trips as Random.
                Type = placed.SavedType,
                ClearAllOnEquip = placed.ClearAllOnEquip
            });
        }

        // The room's own authored podiums (the default first-room pedestals) are captured too:
        // the snapshot deliberately never records podiums as props, so without this they were
        // lost on load. Their live Type is the authored value. FindObjectsOfType skips inactive
        // objects, which excludes the template clone and dormant coop twins; preview ghosts have
        // no podium component left to find.
        foreach (var podium in Object.FindObjectsOfType<Interaction_WeaponSelectionPodium>())
        {
            if (podium == null || IsTrackedOrChild(podium.gameObject)) continue;
            var marker = podium.GetComponentInParent<CTPodiumBehavior>();
            map.Podiums.Add(new MapPodiumData
            {
                Position = MapEditorSerialization.V3(podium.transform.position),
                Type = podium.Type.ToString(),
                ClearAllOnEquip = marker == null || marker.ClearAllOnEquip
            });
        }
    }

    private bool IsTrackedOrChild(GameObject go)
    {
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            if (placed.Instance == go || go.transform.IsChildOf(placed.Instance.transform)) return true;
        }
        return false;
    }
}
