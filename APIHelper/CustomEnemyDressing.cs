using System;
using System.Reflection;
using COTL_API.CustomEnemy;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

// What a JSON enemy gets after COTL_API has spawned it: its skeleton, its size, its tuned
// numbers, and its boss bar.
//
// It happens here rather than in the spawn itself because COTL_API's Spawn only applies a spine
// override inside its custom-controller branch, and taking that branch means casting the mimic to
// EnemySwordsmanWolf - which would pin every custom enemy to that one prefab and throw away the
// mimic's AI, the very thing a JSON enemy is built on. So the spawn is left alone and the enemy
// is dressed afterwards.
public static class CustomEnemyDressing
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    // Every custom enemy that came through Spawn this room. COTL_API instantiates at the scene
    // root, so nothing about the room's own teardown ever touches them - and custom rooms skip
    // the scenery recycle sweep entirely - which is how a corpse (or a straggler) followed the
    // player through doors. Swept when the biome announces the room is being left.
    private static readonly System.Collections.Generic.List<GameObject> TrackedSpawns = [];

    // Fires at the top of ChangeRoomRoutine, while the departing room is still current: its
    // custom spawns and their corpses are removed. The bodies live at the scene root, where no
    // room teardown ever reaches them - without this they followed the player through doors.
    // (A per-room stash-and-restore was tried so corpses would reappear on revisit, vanilla
    // style, but the restore never landed reliably; removed in favour of the simple rule.)
    public static void OnBiomeLeftRoom()
    {
        for (var i = 0; i < TrackedSpawns.Count; i++)
        {
            var go = TrackedSpawns[i];
            if (go == null) continue;

            try
            {
                UnityEngine.Object.Destroy(go);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Custom enemy: could not clean up a spawn on room change: " + e.Message);
            }
        }
        TrackedSpawns.Clear();

        // The visible skeleton is not the enemy: on death, SpawnDeadBodyOnDeath drops a separate
        // DeadBodySliding prefab that inherits the enemy's parent - the scene root, for anything
        // COTL_API spawned. Vanilla corpses are parented under their room and are left alone
        // here; only the rootless ones are ours.
        try
        {
            foreach (var body in UnityEngine.Object.FindObjectsOfType<DeadBodySliding>())
                if (body != null && body.transform.parent == null)
                    UnityEngine.Object.Destroy(body.gameObject);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom enemy: could not clean up dead bodies on room change: " + e.Message);
        }
    }

    public static void Apply(Enemy type, UnitObject unit)
    {
        if (unit == null) return;

        TrackedSpawns.Add(unit.gameObject);

        // For every custom enemy that comes through Spawn, ours or not: when COTL_API swaps in
        // its own controller it destroys the prefab's original UnitObject, and anything on the
        // prefab that serialized a reference to it is left pointing at a corpse.
        RepairControllerReferences(unit);

        if (!CustomEnemyLoader.Registered.TryGetValue(type, out var enemy) || enemy == null) return;

        var go = unit.gameObject;
        go.name = "CultTweaker_Enemy_" + enemy.InternalName;

        try
        {
            ApplySpine(go, unit, enemy);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': spine failed: {e.Message}");
        }

        try
        {
            if (!Mathf.Approximately(enemy.Scale, 1f))
                go.transform.localScale *= enemy.Scale;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': scale failed: {e.Message}");
        }

        ApplyTuning(unit, enemy);

        if (enemy.BossHealthBar) AttachBossBar(go, unit, enemy);
    }

    // COTL_API's Spawn, when a custom enemy brings its own EnemyController, copies every
    // UnitObject field onto the new controller and then DESTROYS the original - but the mimic
    // prefab's helper components still hold serialized references to it. Cower is the one that
    // bites: its death-knockback coroutine's first statement is AIScriptToDisable.enabled =
    // false, which throws on the destroyed controller AFTER DestroyOnDeath has already been
    // switched off - so the corpse never finishes dying and stands frozen mid-animation.
    //
    // Only the known offenders, by type: a generic "re-point every UnitObject field" sweep would
    // also rewrite legitimate cross-unit references (BarrierEnemy.barrierPartner points at a
    // DIFFERENT unit). Destroy is deferred, so the stale value does not read as null this frame -
    // anything not already pointing at the live controller is re-pointed unconditionally, which
    // is a no-op for enemies that never had their controller swapped.
    private static void RepairControllerReferences(UnitObject unit)
    {
        try
        {
            foreach (var cower in unit.GetComponentsInChildren<Cower>(true))
                if (!ReferenceEquals(cower.AIScriptToDisable, unit)) cower.AIScriptToDisable = unit;

            foreach (var stealth in unit.GetComponentsInChildren<EnemyStealth>(true))
                if (!ReferenceEquals(stealth.AIScriptToDisable, unit)) stealth.AIScriptToDisable = unit;

            foreach (var detect in unit.GetComponentsInChildren<DetectStealth>(true))
                if (!ReferenceEquals(detect.unitObject, unit)) detect.unitObject = unit;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom enemy: could not repair its controller references: " + e.Message);
        }
    }

    // Unconditional, unlike COTL_API's: an enemy that keeps its mimic's brain still wants its own
    // skin. A skin the skeleton does not have is a warning rather than the exception SetSkin
    // would throw halfway through Initialize.
    private static void ApplySpine(GameObject go, UnitObject unit, CultTweakerCustomEnemy enemy)
    {
        var spine = FindSkeleton(go, unit);
        if (spine == null)
        {
            if (enemy.SpineOverride != null)
                Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': the mimic has no SkeletonAnimation.");
            return;
        }

        var skin = enemy.SpineSkinName;

        if (enemy.SpineOverride != null)
        {
            spine.skeletonDataAsset = enemy.SpineOverride;

            var data = enemy.SpineOverride.GetSkeletonData(false);
            if (!string.IsNullOrEmpty(skin) && data?.FindSkin(skin) == null)
            {
                Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': the skeleton has no " +
                                      $"skin '{skin}'; using its default skin.");
                skin = "";
            }

            spine.initialSkinName = string.IsNullOrEmpty(skin) ? null : skin;
            spine.Initialize(true);
            spine.Skeleton?.SetToSetupPose();
            spine.Update(0f);
            return;
        }

        // No override, but a skin: the mimic's own skeleton is being re-dressed, which is how a
        // follower-skinned enemy is made without shipping any art.
        if (string.IsNullOrEmpty(skin)) return;

        try
        {
            if (spine.Skeleton?.Data?.FindSkin(skin) == null)
            {
                Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': the mimic's skeleton " +
                                      $"has no skin '{skin}'.");
                return;
            }

            spine.Skeleton.SetSkin(skin);
            spine.Skeleton.SetSlotsToSetupPose();
            spine.Update(0f);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': skin '{skin}' failed: {e.Message}");
        }
    }

    // The controller's own Spine field first: an enemy prefab can hold more than one skeleton
    // (a mount, a weapon), and the field is the one the AI animates.
    private static SkeletonAnimation FindSkeleton(GameObject go, UnitObject unit)
    {
        var fromField = SkeletonField(unit);
        if (fromField != null) return fromField;

        return go.GetComponentInChildren<SkeletonAnimation>(true);
    }

    // Shared with the map editor's enemy tool, which needs the same "which skeleton does the AI
    // animate" answer for its ghosts and thumbnails - through the same memoised lookup.
    internal static SkeletonAnimation SkeletonField(UnitObject unit)
    {
        if (unit == null) return null;

        try
        {
            if (FindMember(unit.GetType(), "Spine") is System.Reflection.FieldInfo field &&
                field.GetValue(unit) is SkeletonAnimation spine && spine != null)
                return spine;
        }
        catch (Exception)
        {
            // Not every mimic names it that; the caller has a fallback.
        }

        return null;
    }

    // The tuning table, by member name, against the controller itself - which is the UnitObject,
    // so `maxSpeed` and `AttackWithinRange` are both reachable on the same object. A name that
    // does not exist is named in the log rather than swallowed: a typo in a config is otherwise
    // an enemy that quietly ignores half its file.
    private static void ApplyTuning(UnitObject unit, CultTweakerCustomEnemy enemy)
    {
        if (enemy.Tuning == null || enemy.Tuning.Count == 0) return;

        foreach (var pair in enemy.Tuning)
        {
            try
            {
                if (TrySet(unit, pair.Key, pair.Value)) continue;
                if (unit.health != null && TrySet(unit.health, pair.Key, pair.Value)) continue;

                Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': '{pair.Key}' is not a " +
                                      $"field on {unit.GetType().Name}; ignored.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': '{pair.Key}' " +
                                      $"could not be set: {e.Message}");
            }
        }
    }

    // Memoised per (type, member): the lookups run on every spawn, and a room of eight enemies
    // with a six-entry tuning table used to do ~100 uncached reflection walks. Misses are cached
    // too (as null) - a typo'd name asks once, not once per spawn.
    private static readonly System.Collections.Generic.Dictionary<(Type, string), System.Reflection.MemberInfo>
        MemberCache = [];

    private static System.Reflection.MemberInfo FindMember(Type type, string name)
    {
        var key = (type, name);
        if (MemberCache.TryGetValue(key, out var cached)) return cached;

        System.Reflection.MemberInfo member = type.GetField(name, Members);
        member ??= type.GetProperty(name, Members);
        MemberCache[key] = member;
        return member;
    }

    private static bool TrySet(object target, string name, float value)
    {
        var member = FindMember(target.GetType(), name);

        if (member is System.Reflection.FieldInfo field && !field.IsInitOnly)
        {
            var converted = Convert(field.FieldType, value);
            if (converted == null) return false;
            field.SetValue(target, converted);
            return true;
        }

        if (member is System.Reflection.PropertyInfo property && property.CanWrite)
        {
            var converted = Convert(property.PropertyType, value);
            if (converted == null) return false;
            property.SetValue(target, converted, null);
            return true;
        }

        return false;
    }

    // Numbers only, because a JSON tuning table is numbers. A bool member takes 0 or 1, which is
    // less pretty than true/false but keeps the table one type.
    private static object Convert(Type type, float value)
    {
        if (type == typeof(float)) return value;
        if (type == typeof(double)) return (double)value;
        if (type == typeof(int)) return Mathf.RoundToInt(value);
        if (type == typeof(bool)) return !Mathf.Approximately(value, 0f);
        return null;
    }

    private static void AttachBossBar(GameObject go, UnitObject unit, CultTweakerCustomEnemy enemy)
    {
        try
        {
            if (unit.health == null)
            {
                Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': no Health to put a boss bar on.");
                return;
            }

            var bar = go.AddComponent<CustomEnemyBossBar>();
            bar.Initialize(unit.health, enemy.BossBarName);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Custom enemy '{enemy.InternalName}': boss bar failed: {e.Message}");
        }
    }
}

// Drives the game's own boss HUD - the bar across the top with a name on it, the one the bishops
// and minibosses use - for an ordinary enemy.
//
// The bar has to be taken down again by hand. UIBossHUD.Update dereferences its boss every frame
// with no guard for a destroyed one, so an enemy that dies and leaves the bar up throws
// MissingReferenceException every frame for the rest of the run.
public class CustomEnemyBossBar : MonoBehaviour
{
    private Health _health;
    private string _name;
    private bool _shown;

    public void Initialize(Health health, string barName)
    {
        _health = health;
        _name = barName ?? "";

        if (_health != null) _health.OnDie += OnDie;

        Show();
    }

    private void Show()
    {
        if (_shown || _health == null) return;

        try
        {
            UIBossHUD.Play(_health, _name);
            _shown = true;
        }
        catch (Exception e)
        {
            // No HUD in this scene, or no Canvas to parent to. Not fatal - the enemy is fine
            // without a bar.
            Plugin.Log.LogWarning("Custom enemy boss bar could not be shown: " + e.Message);
        }
    }

    private void OnDie(GameObject attacker, Vector3 attackLocation, Health victim,
        Health.AttackTypes attackType, Health.AttackFlags attackFlags) => HideBar();

    private void OnDestroy()
    {
        if (_health != null) _health.OnDie -= OnDie;
        HideBar();
    }

    private void HideBar()
    {
        if (!_shown) return;
        _shown = false;

        try
        {
            // Only if it is still ours: two of these alive at once would otherwise have the first
            // one to die take the survivor's bar down with it.
            if (UIBossHUD.Instance != null && UIBossHUD.Instance.boss == _health) UIBossHUD.Hide();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom enemy boss bar could not be hidden: " + e.Message);
        }
    }
}
