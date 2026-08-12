using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using COTL_API.CustomEnemy;
using HarmonyLib;
using Spine.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CustomSpineLoader.MapEditor.Tools;

// Spawns enemies, both vanilla and custom.
//
// There is no vanilla enemy factory or enum-to-prefab table: enemies are simply addressable
// prefabs under Assets/Prefabs/Enemies/**, so the catalog is enumerated straight from the
// Addressables locators (minus the Dead Bodies and Weapons folders, which are corpses and
// projectiles). Custom enemies come from COTL_API's CustomEnemyManager and are keyed by their
// InternalName, never the runtime-minted Enemy enum value.
//
// Placed enemies are LIVE: frozen only while the editor holds timeScale at 0, acting the moment
// it closes. They join Health.team2, so room-lock doors may close until they are dealt with.
public class EnemyTool : IMapEditorTool, IMapDataContributor
{
    public string Name => "Enemies";

    private const string VanillaPrefix = "Assets/Prefabs/Enemies/";
    private static readonly string[] ExcludedFolders = ["Dead Bodies", "Weapons"];

    private readonly RuntimeMapEditor _editor;
    private readonly List<PlacedEnemy> _placed = [];

    // Group name -> list of (label, addressable key). Custom enemies use a synthetic group.
    private static SortedDictionary<string, List<(string label, string key)>> _catalog;

    private readonly List<GameObject> _listButtons = [];
    private RectTransform _panel;
    private MapEditorUI _ui;
    private TMP_Text _selectionLabel;

    private string _pendingKey;
    private bool _pendingIsCustom;
    private string _pendingLabel;

    private GameObject _preview;
    private string _previewKey;

    private class PlacedEnemy
    {
        public string Key;
        public bool IsCustom;
        public GameObject Instance;
    }

    public EnemyTool(RuntimeMapEditor editor)
    {
        _editor = editor;
    }

    public void BuildPanel(RectTransform panel, MapEditorUI ui)
    {
        _panel = panel;
        _ui = ui;

        ui.CreateLabel(panel, "Enemy Tool", 20, TextAlignmentOptions.Center);
        ui.CreateLabel(panel, "Placed enemies are live and\nact once the editor closes.", 14, TextAlignmentOptions.Center);

        _selectionLabel = ui.CreateLabel(panel, "Nothing selected", 15, TextAlignmentOptions.Center)
            .GetComponent<TMP_Text>();

        ui.CreateButton(panel, "Clear Selection", () =>
        {
            _pendingKey = null;
            DestroyPreview();
            _editor.SetStatus("Enemy selection cleared.");
            UpdateSelectionLabel();
        });
        ui.CreateButton(panel, "Undo Last Enemy", UndoLast);

        ui.CreateLabel(panel, "— Groups —", 14, TextAlignmentOptions.Center);
        foreach (var group in Catalog().Keys)
        {
            var captured = group;
            ui.CreateButton(panel, $"{captured} ({Catalog()[captured].Count})", () => ShowGroup(captured));
        }

        // Not part of the cached vanilla catalog: mods register enemies at their own pace, so
        // this group is read live on every click.
        ui.CreateButton(panel, "Custom (mods)", ShowCustomGroup);
    }

    public void OnEnter()
    {
        _editor.SetStatus("Enemy tool: pick a group, pick an enemy, click the world to place it.");
        UpdateSelectionLabel();
    }

    public void OnExit() => DestroyPreview();

    public void OnUpdate()
    {
        if (string.IsNullOrEmpty(_pendingKey)) return;

        UpdatePreviewPosition();

        if (!Input.GetMouseButtonDown(0) || _editor.PointerOverUi()) return;

        var world = _editor.MouseWorld();
        _editor.StartCoroutine(SpawnEnemyRoutine(_pendingKey, _pendingIsCustom, world, withVfx: false));
    }

    // The room snapshot skips objects this tool already serializes.
    public bool IsTracked(GameObject go)
    {
        foreach (var placed in _placed)
            if (placed.Instance == go) return true;
        return false;
    }

    public void ResetTracking() => _placed.Clear();

    // Also the loader's entry point: self-registers so load then save round-trips. withVfx runs
    // the game's teleport-in effect (and EnemyRoundsBase registration); it is skipped for editor
    // placement because its coroutine is frozen under timeScale 0 and would leave the enemy
    // invisible until the editor closes.
    public IEnumerator SpawnEnemyRoutine(string key, bool isCustom, Vector3 position, bool withVfx)
    {
        if (isCustom)
        {
            SpawnCustom(key, position);
            yield break;
        }

        var parent = SceneRefs.ContentRoot;
        AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.InstantiateAsync(key, parent, false);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not instantiate enemy '{key}': {e.Message}");
            yield break;
        }

        while (!handle.IsDone) yield return null;

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Plugin.Log.LogWarning($"MapEditor: enemy load failed for '{key}'.");
            yield break;
        }

        var go = handle.Result;
        go.transform.position = position;

        if (withVfx)
        {
            try
            {
                EnemySpawner.CreateWithAndInitInstantiatedEnemy(position, parent, go);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("MapEditor: enemy spawn VFX failed, enemy placed directly: " + e.Message);
            }
        }

        go.AddComponent<EnemyContainment>();
        _placed.Add(new PlacedEnemy { Key = key, IsCustom = false, Instance = go });
        _editor.SetStatus($"Placed {Path.GetFileNameWithoutExtension(key)} at {position}.");
    }

    private void SpawnCustom(string internalName, Vector3 position)
    {
        foreach (var pair in CustomEnemies())
        {
            if (pair.Value == null || pair.Value.InternalName != internalName) continue;

            var unit = CustomEnemyManager.Spawn(pair.Key, position);
            if (unit == null)
            {
                Plugin.Log.LogWarning($"MapEditor: CustomEnemyManager.Spawn returned null for '{internalName}'.");
                return;
            }

            unit.gameObject.AddComponent<EnemyContainment>();
            _placed.Add(new PlacedEnemy { Key = internalName, IsCustom = true, Instance = unit.gameObject });
            _editor.SetStatus($"Placed custom enemy {internalName} at {position}.");
            return;
        }

        Plugin.Log.LogWarning($"MapEditor: custom enemy '{internalName}' is not registered (mod missing?), skipped.");
    }

    private void UndoLast()
    {
        if (_placed.Count == 0)
        {
            _editor.SetStatus("No enemies placed yet.");
            return;
        }

        var last = _placed[_placed.Count - 1];
        _placed.RemoveAt(_placed.Count - 1);
        if (last.Instance != null) Object.Destroy(last.Instance);
        _editor.SetStatus("Removed the last enemy.");
    }

    // ---- picker -----------------------------------------------------------------------------

    // One group's entries are materialised as buttons on demand; building all ~180 at once
    // stutters and most are never scrolled to.
    private void ShowGroup(string group)
    {
        foreach (var b in _listButtons)
            if (b != null) Object.Destroy(b);
        _listButtons.Clear();

        if (_panel == null || _ui == null || !Catalog().TryGetValue(group, out var entries)) return;

        _listButtons.Add(_ui.CreateLabel(_panel, $"— {group} —", 14, TextAlignmentOptions.Center));
        foreach (var entry in entries)
            AddEnemyButton(entry.label, entry.key, isCustom: false);
    }

    private void ShowCustomGroup()
    {
        foreach (var b in _listButtons)
            if (b != null) Object.Destroy(b);
        _listButtons.Clear();

        if (_panel == null || _ui == null) return;

        _listButtons.Add(_ui.CreateLabel(_panel, "— Custom (mods) —", 14, TextAlignmentOptions.Center));

        var any = false;
        foreach (var pair in CustomEnemies())
        {
            if (pair.Value == null) continue;
            AddEnemyButton(pair.Value.InternalName, pair.Value.InternalName, isCustom: true);
            any = true;
        }

        if (!any)
            _listButtons.Add(_ui.CreateLabel(_panel, "No custom enemies registered.", 14, TextAlignmentOptions.Center));
    }

    private void AddEnemyButton(string label, string key, bool isCustom)
    {
        _listButtons.Add(_ui.CreateButton(_panel, label, () =>
        {
            _pendingKey = key;
            _pendingIsCustom = isCustom;
            _pendingLabel = label;
            DestroyPreview();
            UpdateSelectionLabel();
            _editor.SetStatus($"Selected {label}. Left-click in the world to place it.");
        }));
    }

    private void UpdateSelectionLabel()
    {
        if (_selectionLabel != null)
            _selectionLabel.text = string.IsNullOrEmpty(_pendingKey) ? "Nothing selected" : "Placing: " + _pendingLabel;
    }

    private static SortedDictionary<string, List<(string label, string key)>> Catalog()
    {
        if (_catalog != null) return _catalog;

        _catalog = [];
        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator?.Keys == null) continue;
            foreach (var keyObj in locator.Keys)
            {
                if (keyObj is not string key) continue;
                if (!key.StartsWith(VanillaPrefix) || !key.EndsWith(".prefab")) continue;

                var relative = key.Substring(VanillaPrefix.Length);
                var slash = relative.IndexOf('/');
                var group = slash > 0 ? relative.Substring(0, slash) : "Misc";
                if (ExcludedFolders.Contains(group)) continue;

                if (!_catalog.TryGetValue(group, out var list))
                    _catalog[group] = list = [];

                var label = Path.GetFileNameWithoutExtension(key);
                if (!list.Any(e => e.Item2 == key)) list.Add((label, key));
            }
        }

        foreach (var list in _catalog.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));

        Plugin.Log.LogInfo($"MapEditor: enemy catalog holds {_catalog.Sum(g => g.Value.Count)} entries " +
                           $"in {_catalog.Count} group(s).");
        return _catalog;
    }

    // CustomEnemyList is internal to COTL_API, so it is read via Harmony's traverse rather than
    // depending on a publicized COTL_API build.
    private static Dictionary<Enemy, CustomEnemy> CustomEnemies()
    {
        try
        {
            var dict = Traverse.Create(typeof(CustomEnemyManager))
                .Property("CustomEnemyList")
                .GetValue<Dictionary<Enemy, CustomEnemy>>();
            return dict ?? [];
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not read COTL_API custom enemy list: " + e.Message);
            return [];
        }
    }

    // ---- preview ----------------------------------------------------------------------------

    private void UpdatePreviewPosition()
    {
        if (_preview != null && _previewKey == _pendingKey)
        {
            _preview.transform.position = _editor.MouseWorld();
            return;
        }

        DestroyPreview();
        _previewKey = _pendingKey;
        _editor.StartCoroutine(BuildPreview(_pendingKey, _pendingIsCustom));
    }

    private IEnumerator BuildPreview(string key, bool isCustom)
    {
        GameObject prefab = null;

        if (isCustom)
        {
            foreach (var pair in CustomEnemies())
                if (pair.Value != null && pair.Value.InternalName == key &&
                    CustomEnemyManager.CustomEnemyPrefabList.TryGetValue(pair.Key, out var p))
                    prefab = p;
        }
        else
        {
            AsyncOperationHandle<GameObject> handle;
            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(key);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"MapEditor: preview load failed for '{key}': {e.Message}");
                yield break;
            }
            while (!handle.IsDone) yield return null;
            if (handle.Status == AsyncOperationStatus.Succeeded) prefab = handle.Result;
        }

        // Selection changed while loading.
        if (_previewKey != key || prefab == null) yield break;

        var ghost = MapEditorGhost.Create(prefab, _editor.transform, "CultTweaker_EnemyPreview",
            disableBehaviours: true);
        if (ghost == null) yield break;
        ghost.transform.position = _editor.MouseWorld();

        // The controller's own Spine field is the authoritative skeleton - the mimic prefab
        // carries additional SkeletonRenderers (ghost/afterimage effects) that must not render
        // in a preview, or the base skin shows through under the override.
        var spine = MainSkeleton(ghost);
        if (spine != null)
        {
            foreach (var other in ghost.GetComponentsInChildren<Spine.Unity.SkeletonRenderer>(true))
            {
                if (other == null || ReferenceEquals(other, spine)) continue;
                var mesh = other.GetComponent<MeshRenderer>();
                if (mesh != null) mesh.enabled = false;
            }

            // Custom enemies re-skin their mimic prefab at spawn; mirror CustomEnemyManager.Spawn
            // exactly on the ghost. SetToSetupPose + Update(0) are what actually push the new
            // skin into the mesh - the normal skeleton update never runs while the editor holds
            // timeScale 0.
            if (isCustom)
            {
                try
                {
                    foreach (var pair in CustomEnemies())
                    {
                        if (pair.Value == null || pair.Value.InternalName != key) continue;
                        if (pair.Value.SpineOverride == null) continue;

                        spine.skeletonDataAsset = pair.Value.SpineOverride;
                        spine.initialSkinName = pair.Value.SpineSkinName;
                        spine.Initialize(true);
                        spine.Skeleton.SetToSetupPose();
                        spine.Update(0f);
                        break;
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"MapEditor: could not apply custom skin to preview of '{key}': {e.Message}");
                }
            }

            if (spine.Skeleton != null) spine.Skeleton.A = 0.6f;
        }

        // Two of these coroutines can overlap when the selection changes mid-load; only the one
        // still matching the current key may install its ghost, and anything already installed
        // must be destroyed rather than orphaned - that was the leaked preview object.
        if (_previewKey != key)
        {
            Object.Destroy(ghost);
            yield break;
        }

        if (_preview != null) Object.Destroy(_preview);
        _preview = ghost;
    }

    // The enemy controller's serialized Spine field, read generically since the concrete
    // controller type varies per enemy; first skeleton in the hierarchy as fallback.
    private static SkeletonAnimation MainSkeleton(GameObject ghost)
    {
        var unit = ghost.GetComponentInChildren<UnitObject>(true);
        if (unit != null)
        {
            try
            {
                var fromField = Traverse.Create(unit).Field("Spine").GetValue<SkeletonAnimation>();
                if (fromField != null) return fromField;
            }
            catch (System.Exception)
            {
                // Controller has no Spine field; fall through.
            }
        }

        return ghost.GetComponentInChildren<SkeletonAnimation>(true);
    }

    private void DestroyPreview()
    {
        if (_preview != null) Object.Destroy(_preview);
        _preview = null;
        _previewKey = null;
    }

    public void ContributeTo(CTNodeBlueprint map)
    {
        map.Enemies.Clear();
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            map.Enemies.Add(new MapEnemyData
            {
                Key = placed.Key,
                IsCustom = placed.IsCustom,
                Position = MapEditorSerialization.V3(placed.Instance.transform.position)
            });
        }
    }
}

// Keeps a spawned enemy on the authored floor. Vanilla rooms contain enemies through a mix of
// closed room-lock barriers and unit-position correction that custom maps do not get, so
// knockback or wandering could carry them across the composite outline and out of bounds.
// The walkable A* graph mirrors the built collision exactly: anything that strays more than a
// node-and-a-half off it is snapped back to the nearest walkable point.
public class EnemyContainment : MonoBehaviour
{
    private const float CheckInterval = 0.5f;
    private const float MaxOffGraphSqr = 2.25f; // 1.5 units

    private float _next;

    private void Update()
    {
        // Scaled time: frozen while the editor is open, which is correct - nothing moves then.
        if (Time.time < _next) return;
        _next = Time.time + CheckInterval;

        if (AstarPath.active == null) return;

        var nearest = AstarPath.active.GetNearest(transform.position);
        if (nearest.node == null || !nearest.node.Walkable) return;

        var walkable = (Vector3)nearest.position;
        var offset = walkable - transform.position;
        offset.z = 0f;

        if (offset.sqrMagnitude > MaxOffGraphSqr)
            transform.position = new Vector3(walkable.x, walkable.y, transform.position.z);
    }
}
