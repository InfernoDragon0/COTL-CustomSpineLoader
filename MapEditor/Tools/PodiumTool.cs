using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

public class CTPodiumBehavior : MonoBehaviour
{
    public bool ClearAllOnEquip = true;

    private Interaction_WeaponSelectionPodium _podium;
    private float _nextCheck;

    private void Awake() => _podium = GetComponentInChildren<Interaction_WeaponSelectionPodium>(true);

    private void Update()
    {
        if (ClearAllOnEquip) return;
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + 0.25f;

        if (_podium == null)
        {
            _podium = GetComponentInChildren<Interaction_WeaponSelectionPodium>(true);
            if (_podium == null) return;
        }

        if (_podium.WeaponTaken || _podium.activated) return;

        if (_podium.Interactable && _podium.enabled &&
            (_podium.podiumOn == null || _podium.podiumOn.activeSelf)) return;

        Restore();
    }

    private void Restore()
    {
        _podium.enabled = true;
        _podium.Interactable = true;
        if (_podium.Lighting != null) _podium.Lighting.SetActive(true);
        if (_podium.IconSpriteRenderer != null) _podium.IconSpriteRenderer.enabled = true;
        if (_podium.podiumOn != null) _podium.podiumOn.SetActive(true);
        if (_podium.podiumOff != null) _podium.podiumOff.SetActive(false);
        if (_podium.particleEffect != null) _podium.particleEffect.Play();
        if (_podium.AvailableGoop != null) _podium.AvailableGoop.Play("Show");

        Plugin.Log.LogInfo("MapEditor: podium kept usable after another podium was equipped.");
    }
}

public class PodiumTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts
{
    public string Name => "Podiums";

    private readonly RuntimeMapEditor _editor;
    private readonly List<PlacedPodium> _placed = [];

    private GameObject _template;
    private GameObject _holder;
    private GameObject _preview;
    private bool _placing;
    private string _type = "Random";

    private class PlacedPodium
    {
        public GameObject Instance;
        public string SavedType;
        public bool ClearAllOnEquip = true;
    }

    private bool _clearAllOnEquip = true;

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

        private static void Finalizer(Interaction_WeaponSelectionPodium __instance, SwapState __state)
        {
            if (__state != null) __instance.otherWeaponOptions = __state.Original;
        }
    }

    [HarmonyPatch(typeof(Interaction_WeaponSelectionPodium), "IsPodiumInSameRoom")]
    private static class Podium_IsPodiumInSameRoom_Patch
    {
        private static void Postfix(Interaction_WeaponSelectionPodium otherPodium, ref bool __result)
        {
            if (!__result || otherPodium == null) return;

            var marker = otherPodium.GetComponentInParent<CTPodiumBehavior>(true);
            if (marker != null && !marker.ClearAllOnEquip) __result = false;
        }
    }

    public PodiumTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _typeDropdown = ui.CreateDropdown(panel, "Podium type", Types, (index, type) =>
        {
            if (index < 0 || index >= Types.Length) return;

            _type = type;
            _placing = true;
            DestroyPreview();
            _editor.SetStatus($"Selected {type} podium.");
        });

        ui.CreateToggle(panel, "Equip clears all", _clearAllOnEquip, v =>
        {
            _clearAllOnEquip = v;
            var count = ApplyBehaviorToRoom(v);
            _editor.SetStatus(v
                ? $"Equip clears all podiums ({count} updated)."
                : $"Equip clears one podium only ({count} updated).");
        });

    }

    private static readonly string[] Types = ["Random", "Weapon", "Curse", "Relic"];

    private MapEditorDropdown _typeDropdown;

    public void OnEnter()
    {
        ApplyBehaviorToRoom(_clearAllOnEquip, onlyUnmarked: true);

        if (_typeDropdown != null && _typeDropdown.SelectedIndex < 0)
        {
            _typeDropdown.SetSelected(0);
            _type = Types[0];
            _placing = true;
        }

        _editor.SetStatus("Click the world to place a podium.");
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Place selected podium")
    ];

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

    private void UpdatePreview()
    {
        if (_preview != null)
        {
            _preview.transform.position = _editor.MouseWorld();
            return;
        }

        var template = AcquireTemplate();
        if (template == null) return;

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

    public int ApplyBehaviorToRoom(bool clearAllOnEquip, bool onlyUnmarked = false)
    {
        var count = 0;
        foreach (var podium in Object.FindObjectsOfType<Interaction_WeaponSelectionPodium>())
        {
            if (podium == null) continue;

            var marker = podium.GetComponentInParent<CTPodiumBehavior>(true);
            if (marker == null) marker = podium.gameObject.AddComponent<CTPodiumBehavior>();
            else if (onlyUnmarked) continue;

            marker.ClearAllOnEquip = clearAllOnEquip;
            count++;
        }

        if (!onlyUnmarked)
            foreach (var placed in _placed) placed.ClearAllOnEquip = clearAllOnEquip;

        if (count > 0)
            Plugin.Log.LogInfo($"MapEditor: podium clear-all set to {clearAllOnEquip} on {count} podium(s).");
        return count;
    }

    public bool IsTracked(GameObject go)
    {
        foreach (var placed in _placed)
            if (placed.Instance == go) return true;
        return false;
    }

    public void ResetTracking() => _placed.Clear();

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

        podium.RemoveIfNotFirstLayer = false;
        podium.WeaponTaken = false;
        podium.Type = ResolveType(typeName);
        go.AddComponent<CTPodiumBehavior>().ClearAllOnEquip = clearAllOnEquip;

        go.transform.SetParent(parent, true);
        go.transform.position = position;
        go.SetActive(true);

        var placed = new PlacedPodium { Instance = go, SavedType = typeName, ClearAllOnEquip = clearAllOnEquip };
        _placed.Add(placed);
        _editor.History.Push($"place {typeName} podium", () =>
        {
            if (!_placed.Remove(placed) || placed.Instance == null) return false;
            Object.Destroy(placed.Instance);
            return true;
        });
        _editor.SetStatus($"Placed {typeName} podium at {position}.");
        return go;
    }

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

    public GameObject AcquireTemplate()
    {
        if (_template != null) return _template;

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
            }
        }

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

    private GameObject Holder()
    {
        if (_holder != null) return _holder;
        _holder = new GameObject("MapEditor_PodiumHolder");
        _holder.SetActive(false);
        _holder.transform.SetParent(_editor.transform, false);
        return _holder;
    }

    public GameObject LastPlacedInstance =>
        _placed.Count > 0 ? _placed[_placed.Count - 1].Instance : null;

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.Podiums.Clear();
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            map.Podiums.Add(new MapPodiumData
            {
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                Scale = MapEditorSerialization.V3(placed.Instance.transform.lossyScale),
                Type = placed.SavedType,
                ClearAllOnEquip = LiveClearAll(placed.Instance, placed.ClearAllOnEquip)
            });
        }

        foreach (var podium in Object.FindObjectsOfType<Interaction_WeaponSelectionPodium>())
        {
            if (podium == null || IsTrackedOrChild(podium.gameObject)) continue;
            map.Podiums.Add(new MapPodiumData
            {
                Position = MapEditorSerialization.V3(podium.transform.position),
                Scale = MapEditorSerialization.V3(podium.transform.lossyScale),
                Type = podium.Type.ToString(),
                ClearAllOnEquip = LiveClearAll(podium.gameObject, _clearAllOnEquip)
            });
        }
    }

    private static bool LiveClearAll(GameObject podium, bool fallback)
    {
        if (podium == null) return fallback;
        var marker = podium.GetComponentInParent<CTPodiumBehavior>(true)
                     ?? podium.GetComponentInChildren<CTPodiumBehavior>(true);
        return marker != null ? marker.ClearAllOnEquip : fallback;
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
