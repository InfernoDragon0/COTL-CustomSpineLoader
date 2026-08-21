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

public class EnemyTool : IMapEditorTool, IMapDataContributor, IMapEditorShortcuts
{
    public string Name => "Enemies";

    // Enemies sit in three address spaces, not one. The two below "Assets/Prefabs/Enemies/" are
    // left over from the pre-Addressables Resources folder and were never re-addressed, so
    // scanning only the first hid 103 prefabs - the bishops, and most of the Dungeon 1-4 roster.
    private static readonly string[] VanillaPrefixes =
        ["Assets/Prefabs/Enemies/", "Assets/Resources_moved/Enemies/", "Enemies/"];

    private static readonly string[] ExcludedFolders = ["Dead Bodies", "Weapons"];

    // The bosses are addressed by what they are rather than who they are; nobody looking for
    // Leshy searches for "Worm Boss". Only the bishops are aliased - the rest read fine.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["Enemy Forest Worm Boss"] = "Leshy (Worm Boss)",
        ["Enemy Forest Frog Boss"] = "Heket (Frog Boss)",
        ["Enemy Jellyfish Boss"] = "Kallamar (Jelly Boss)"
    };

    private readonly RuntimeMapEditor _editor;
    private readonly List<PlacedEnemy> _placed = [];

    // Group name -> list of (label, addressable key). Custom enemies use a synthetic group.
    private static SortedDictionary<string, List<(string label, string key)>> _catalog;

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
        // The group picker leads: nothing below it means anything until a group is chosen.
        _groupKeys.Clear();
        var options = new List<string>();
        foreach (var group in Catalog().Keys)
        {
            _groupKeys.Add(group);
            options.Add($"{group} ({Catalog()[group].Count})");
        }

        // Not part of the cached vanilla catalog: mods register enemies at their own pace, so
        // this group is read live every time it is picked.
        _groupKeys.Add(null);
        options.Add("Custom (mods)");

        _groupDropdown = ui.CreateDropdown(panel, "Choose a group", options, (index, _) => ShowGroupAt(index));

        _grid = ui.CreateIconGrid(panel, "EnemyGrid");

        ui.CreateButton(panel, "Clear Selection", () =>
        {
            _pendingKey = null;
            DestroyPreview();
            _grid?.SetSelected(null);
            _editor.SetStatus("Selection cleared.");
        });
    }

    private MapEditorDropdown _groupDropdown;

    private void ShowGroupAt(int index)
    {
        if (index < 0 || index >= _groupKeys.Count) return;

        var group = _groupKeys[index];
        if (group == null) ShowCustomGroup();
        else ShowGroup(group);
    }

    private readonly List<string> _groupKeys = [];
    private MapEditorGrid _grid;

    public void OnEnter()
    {
        // Open on the first group rather than an empty grid: an empty panel says nothing about
        // what the tool does.
        if (_grid != null && _groupDropdown != null && _groupDropdown.SelectedIndex < 0)
        {
            _groupDropdown.SetSelected(0);
            ShowGroupAt(0);
        }

        _editor.SetStatus("Pick a group, then an enemy.");
    }

    public IEnumerable<(string Key, string Action)> Shortcuts =>
    [
        ("LMB", "Place selected enemy")
    ];

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

    // Everything this tool put in the room, for the clear tool.
    public int ClearPlaced()
    {
        var removed = 0;
        foreach (var placed in _placed)
        {
            if (placed.Instance == null) continue;
            Object.Destroy(placed.Instance);
            removed++;
        }

        _placed.Clear();
        return removed;
    }

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
        var placed = new PlacedEnemy { Key = key, IsCustom = false, Instance = go };
        _placed.Add(placed);
        PushUndo(placed, Path.GetFileNameWithoutExtension(key));
        _editor.SetStatus($"Placed {Path.GetFileNameWithoutExtension(key)}.");
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
            var placed = new PlacedEnemy { Key = internalName, IsCustom = true, Instance = unit.gameObject };
            _placed.Add(placed);
            PushUndo(placed, internalName);
            _editor.SetStatus($"Placed {internalName}.");
            return;
        }

        Plugin.Log.LogWarning($"MapEditor: custom enemy '{internalName}' is not registered (mod missing?), skipped.");
    }

    private void PushUndo(PlacedEnemy placed, string label)
    {
        _editor.History.Push($"place {label}", () =>
        {
            if (!_placed.Remove(placed) || placed.Instance == null) return false;
            Object.Destroy(placed.Instance);
            return true;
        });
    }

    // ---- picker -----------------------------------------------------------------------------

    private void ShowGroup(string group)
    {
        if (_grid == null || !Catalog().TryGetValue(group, out var entries)) return;

        var list = new List<MapEditorGrid.Entry>(entries.Count);
        foreach (var entry in entries) list.Add(CellFor(entry.label, entry.key, isCustom: false));
        Populate(list);
    }

    private void ShowCustomGroup()
    {
        if (_grid == null) return;

        var list = new List<MapEditorGrid.Entry>();
        foreach (var pair in CustomEnemies())
        {
            if (pair.Value == null) continue;
            list.Add(CellFor(pair.Value.InternalName, pair.Value.InternalName, isCustom: true));
        }

        Populate(list);
        if (list.Count == 0) _editor.SetStatus("No custom enemies registered.", StatusSeverity.Warning);
    }

    private void Populate(IList<MapEditorGrid.Entry> entries)
    {
        EnemyThumbnails.CancelPending();
        _grid.Populate(_editor, entries, id =>
        {
            var isCustom = _customIds.Contains(id);
            EnemyThumbnails.Request(_editor, id, isCustom, sprite => _grid?.SetCellIcon(id, sprite));
        });
    }

    private readonly HashSet<string> _customIds = [];

    private MapEditorGrid.Entry CellFor(string label, string key, bool isCustom)
    {
        if (isCustom) _customIds.Add(key);

        return new MapEditorGrid.Entry
        {
            Id = key,
            Display = label,
            OnClick = () =>
            {
                _pendingKey = key;
                _pendingIsCustom = isCustom;
                _pendingLabel = label;
                DestroyPreview();
                _editor.SetStatus($"Selected {label}.");
            }
        };
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
                if (keyObj is not string key || !key.EndsWith(".prefab")) continue;

                var prefix = VanillaPrefixes.FirstOrDefault(key.StartsWith);
                if (prefix == null) continue;

                var relative = key.Substring(prefix.Length);
                var slash = relative.IndexOf('/');
                var group = slash > 0 ? relative.Substring(0, slash) : "Misc";
                if (ExcludedFolders.Contains(group)) continue;

                if (!_catalog.TryGetValue(group, out var list))
                    _catalog[group] = list = [];

                var label = Path.GetFileNameWithoutExtension(key);
                if (Aliases.TryGetValue(label, out var alias)) label = alias;
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
    // depending on a publicized COTL_API build. The dictionary object itself is stable for the
    // session (COTL_API mutates it in place), so the traverse runs once, not per spawn and per
    // thumbnail.
    private static Dictionary<Enemy, CustomEnemy> _customEnemies;

    private static Dictionary<Enemy, CustomEnemy> CustomEnemies()
    {
        if (_customEnemies != null) return _customEnemies;

        try
        {
            var dict = Traverse.Create(typeof(CustomEnemyManager))
                .Property("CustomEnemyList")
                .GetValue<Dictionary<Enemy, CustomEnemy>>();
            return _customEnemies = dict ?? [];
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

    // Resolves an enemy key to its prefab. Shared with the thumbnail renderer, which needs the
    // same two lookups (addressable for vanilla, COTL_API's prefab list for custom).
    internal static IEnumerator ResolvePrefabRoutine(string key, bool isCustom, System.Action<GameObject> done)
    {
        if (isCustom)
        {
            GameObject found = null;
            foreach (var pair in CustomEnemies())
                if (pair.Value != null && pair.Value.InternalName == key &&
                    CustomEnemyManager.CustomEnemyPrefabList.TryGetValue(pair.Key, out var p))
                    found = p;
            done(found);
            yield break;
        }

        AsyncOperationHandle<GameObject> handle;
        try
        {
            handle = Addressables.LoadAssetAsync<GameObject>(key);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: enemy prefab load failed for '{key}': {e.Message}");
            done(null);
            yield break;
        }

        while (!handle.IsDone) yield return null;
        done(handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null);
    }

    internal static bool TryGetCustomSkin(string key, out SkeletonDataAsset asset, out string skin)
    {
        asset = null;
        skin = null;

        foreach (var pair in CustomEnemies())
        {
            if (pair.Value == null || pair.Value.InternalName != key) continue;
            if (pair.Value.SpineOverride == null) return false;

            asset = pair.Value.SpineOverride;
            skin = pair.Value.SpineSkinName;
            return true;
        }

        if (CustomSpineLoader.APIHelper.CustomNpcManager.CustomNpcList.TryGetValue(key, out var npc) &&
            npc?.SpineOverride != null)
        {
            asset = npc.SpineOverride;
            skin = npc.SpineSkinName;
            return true;
        }

        return false;
    }

    internal static void ApplyCustomSkin(SkeletonAnimation spine, string key)
    {
        if (spine == null) return;

        try
        {
            if (!TryGetCustomSkin(key, out var asset, out var skin) || asset == null) return;

            spine.skeletonDataAsset = asset;
            spine.initialSkinName = skin;
            spine.Initialize(true);
            spine.Skeleton.SetToSetupPose();
            spine.Update(0f);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: could not apply custom skin to '{key}': {e.Message}");
        }
    }

    private IEnumerator BuildPreview(string key, bool isCustom)
    {
        GameObject prefab = null;
        yield return ResolvePrefabRoutine(key, isCustom, p => prefab = p);

        // Selection changed while loading.
        if (_previewKey != key || prefab == null) yield break;

        var ghost = MapEditorGhost.Create(prefab, _editor.transform, "CultTweaker_EnemyPreview",
            disableBehaviours: true);
        if (ghost == null) yield break;
        ghost.transform.position = _editor.MouseWorld();

        var spine = MainSkeleton(ghost);
        if (spine != null)
        {
            foreach (var other in ghost.GetComponentsInChildren<Spine.Unity.SkeletonRenderer>(true))
            {
                if (other == null || ReferenceEquals(other, spine)) continue;
                var mesh = other.GetComponent<MeshRenderer>();
                if (mesh != null) mesh.enabled = false;
            }

            if (isCustom) ApplyCustomSkin(spine, key);

            if (spine.Skeleton != null) spine.Skeleton.A = 0.6f;
        }

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
    internal static SkeletonAnimation MainSkeleton(GameObject ghost)
    {
        var unit = ghost.GetComponentInChildren<UnitObject>(true);
        if (unit != null)
        {
            var fromField = APIHelper.CustomEnemyDressing.SkeletonField(unit);
            if (fromField != null) return fromField;
        }

        return ghost.GetComponentInChildren<SkeletonAnimation>(true);
    }

    private void DestroyPreview()
    {
        if (_preview != null) Object.Destroy(_preview);
        _preview = null;
        _previewKey = null;
    }

    // The instance the last spawn produced, so a loader can finish setting it up without
    // every spawn routine having to hand one back.
    public GameObject LastPlacedInstance =>
        _placed.Count > 0 ? _placed[_placed.Count - 1].Instance : null;

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
                Position = MapEditorSerialization.V3(placed.Instance.transform.position),
                Scale = MapEditorSerialization.V3(placed.Instance.transform.lossyScale)
            });
        }
    }
}

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
