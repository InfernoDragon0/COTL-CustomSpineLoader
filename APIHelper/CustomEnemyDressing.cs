using System;
using System.Reflection;
using COTL_API.CustomEnemy;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public static class CustomEnemyDressing
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    private static readonly System.Collections.Generic.List<GameObject> TrackedSpawns = [];

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

    private static SkeletonAnimation FindSkeleton(GameObject go, UnitObject unit)
    {
        var fromField = SkeletonField(unit);
        if (fromField != null) return fromField;

        return go.GetComponentInChildren<SkeletonAnimation>(true);
    }

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
        }

        return null;
    }

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
            if (UIBossHUD.Instance != null && UIBossHUD.Instance.boss == _health) UIBossHUD.Hide();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom enemy boss bar could not be hidden: " + e.Message);
        }
    }
}
