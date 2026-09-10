using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using COTL_API.Helpers;
using HarmonyLib;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.SpineLoaderHelper;

/// One weapon declared in a player spine's config.json ("weapons": [...]).
public class PlayerWeaponConfig
{
    /// Identifier within the spine; the registry key is "<spine>/<name>".
    public string Name { get; set; }

    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Lore { get; set; }

    /// Vanilla family whose data, heavy attack, sounds and hitboxes are reused:
    /// Sword, Axe, Hammer, Dagger, Gauntlet, Blunderbuss, Shield or Chain.
    public string BaseWeapon { get; set; } = "Sword";

    /// Skin in this spine holding the weapon's attachments (e.g. "Weapons/FlameSword").
    public string Skin { get; set; }

    /// Vanilla modifier skin layered under ours: Normal, Poison, Critical, ... (WeaponData.Skins).
    public string ModifierSkin { get; set; } = "Normal";

    /// Animation played when the weapon is picked up; the base weapon's when empty.
    public string PickupAnimation { get; set; }

    /// PNG in the spine folder used for the HUD and podium icon; the base weapon's when empty.
    public string Icon { get; set; }

    /// Chain-based weapons only: PNG in the spine folder drawn at the end of the chain while it
    /// swings; the chain's own hook when empty. Also the icon when icon is empty.
    public string HookIcon { get; set; }

    /// Size of the hook picture relative to the chain's own hook (1 = the same size).
    public float HookScale { get; set; } = 1f;

    /// Whether the weapon can be rolled by podiums and chests.
    public bool InPool { get; set; } = true;

    /// The combo chain, one entry per hit; the base weapon's chain when empty.
    public List<PlayerWeaponHitConfig> Combo { get; set; }
}

public class PlayerWeaponHitConfig
{
    /// Animation for this hit; the base weapon's hit at the same position when empty.
    public string Animation { get; set; }

    /// Base damage of the hit before level, fleece and trinket multipliers.
    public float? Damage { get; set; }

    /// Playback speed of the hit's animation (1 = as authored).
    public float Speed { get; set; } = 1f;

    /// How far in front of the player the hit lands, in world units; the base hit's when empty.
    /// Also the hitbox radius unless hitboxRadius says otherwise.
    public float? Range { get; set; }

    /// Radius of the hit's circle collider in world units, independent of range.
    public float? HitboxRadius { get; set; }

    /// Push applied to what the hit strikes; the base hit's when empty.
    public float? Knockback { get; set; }

    /// Forward lunge while swinging: speed and how long it lasts; the base hit's when empty.
    public float? LungeSpeed { get; set; }
    public float? LungeDuration { get; set; }

    /// Camera shake strength on the hit; the base hit's when empty.
    public float? CameraShake { get; set; }

    /// Melee, Heavy, Projectile, Poison, NoKnockBack, Ice or Charm; the base hit's when empty.
    public string AttackType { get; set; }

    /// Whether the next hit can be queued during this one; the base hit's when empty.
    public bool? CanQueueNext { get; set; }

    /// Whether the player may turn while this hit plays; the base hit's when empty.
    public bool? CanTurn { get; set; }

    /// Where the "Attack Deal Damage" event is injected, as a fraction of the animation, when the
    /// animation carries no attack events of its own.
    public float? HitAt { get; set; }

    /// Where "Attack Can Break" is injected, as a fraction of the animation, when missing.
    public float? BreakAt { get; set; }

    /// Chain-based weapons only: what the hook does on this hit and the shape of its sweep.
    public PlayerWeaponHookConfig Hook { get; set; }
}

/// The hook's move on one hit of a Chain-based weapon. The sweep is an ellipse in the screen
/// plane centred beside the player; the game's own chain uses these same numbers.
public class PlayerWeaponHookConfig
{
    /// left: one hook sweeps around the player's left; right: the other hook, the right;
    /// slam: both hooks fly out and slam down (the chain's third hit); fallback: the game's
    /// untuned quick sweep. Empty keeps the chain's own order: left, right, slam, then fallback.
    public string Pattern { get; set; }

    /// Where on the ellipse the hook starts, in degrees from the facing direction.
    public float? StartAngle { get; set; }

    /// How far around the ellipse it travels, in degrees; negative goes the other way.
    public float? Sweep { get; set; }

    /// Horizontal radius of the ellipse, in world units.
    public float? Width { get; set; }

    /// Vertical radius of the ellipse, in world units.
    public float? Height { get; set; }

    /// How far the ellipse's centre sits to the side of the player (left for left, right for right).
    public float? Offset { get; set; }

    /// Seconds the sweep takes at normal attack speed.
    public float? Duration { get; set; }

    /// Seconds after the sweep starts before the hook can hurt, and after which it stops hurting.
    public float? ColliderOn { get; set; }
    public float? ColliderOff { get; set; }

    /// Radius of the ellipse over the hit's time, as [time, multiplier] pairs with time 0..1:
    /// [[0, 0], [0.4, 1], [1, 0.2]] is a thrust that extends and pulls back. Empty keeps the
    /// chain's own curve.
    public List<float[]> RadiusCurve { get; set; }

    /// Size of the hook picture over the hit's time, same shape as radiusCurve.
    public List<float[]> ScaleCurve { get; set; }

    /// How many of the two chains swing on this hit: 1 or 2. The slam always uses both.
    public int Hooks { get; set; } = 1;

    /// Degrees added to the second chain's start angle, so 180 puts it opposite the first.
    public float SecondStartAngle { get; set; } = 180f;

    /// Seconds between the first chain leaving and the second, at normal attack speed.
    public float SecondDelay { get; set; }
}

public sealed class CustomWeapon
{
    public string Key;
    public string SpineName;
    public string Folder;
    public PlayerWeaponConfig Config;
    public EquipmentType Type;
    public EquipmentType BaseType;
    public WeaponData Data;
    public Sprite HookRaw;
    public Sprite HookSprite;
    public bool HookSpriteTried;
    public readonly List<PlayerWeaponHitConfig> Hits = [];

    public string DisplayName =>
        string.IsNullOrEmpty(Config.DisplayName) ? Config.Name : Config.DisplayName;
}

/// Custom weapons declared by player spines. Each is a runtime clone of a vanilla weapon's data
/// under an id the game does not use, so every vanilla path (podium rolls, pickups, heavy attacks,
/// the pickup card) treats it as a weapon. The skin is copied into the live player skin like a
/// fleece, so any spine can wield it; animations it lacks fall back to the base weapon's.
public static class CustomWeapons
{
    private static readonly Dictionary<string, CustomWeapon> ByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<EquipmentType, CustomWeapon> ByType = [];

    /// Custom animation name → base weapon animation name, used when a skeleton lacks the custom one.
    private static readonly Dictionary<string, string> Fallbacks = new(StringComparer.Ordinal);

    /// Animation names that carry a per-hit speed on at least one weapon.
    private static readonly HashSet<string> SpedHits = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Prepared = [];
    private static readonly HashSet<string> Warned = [];

    private static bool _sealed;
    private static bool _building;

    public static int Count => ByKey.Count;

    public static IEnumerable<CustomWeapon> All => ByKey.Values;

    public static CustomWeapon Get(EquipmentType type) =>
        ByType.TryGetValue(type, out var weapon) ? weapon : null;

    public static CustomWeapon ForKey(string key) =>
        !string.IsNullOrEmpty(key) && ByKey.TryGetValue(key, out var weapon) ? weapon : null;

    public static bool IsCustom(EquipmentType type) => ByType.ContainsKey(type);

    // ---- registration -------------------------------------------------------------------------

    public static void Register(string spineName, string folder, PlayerSpineConfig config)
    {
        if (config?.Weapons == null || config.Weapons.Count == 0) return;

        foreach (var weaponConfig in config.Weapons)
        {
            if (weaponConfig == null || string.IsNullOrWhiteSpace(weaponConfig.Name))
            {
                Plugin.Log.LogWarning($"{spineName}: a weapon without a name was skipped.");
                continue;
            }

            if (!TryParseBase(weaponConfig.BaseWeapon, out var baseType))
            {
                Plugin.Log.LogWarning($"{spineName}/{weaponConfig.Name}: baseWeapon '{weaponConfig.BaseWeapon}' " +
                                      "is not Sword, Axe, Hammer, Dagger, Gauntlet, Blunderbuss, Shield or Chain; skipped.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(weaponConfig.Skin))
            {
                Plugin.Log.LogWarning($"{spineName}/{weaponConfig.Name}: no skin named; the base weapon's look is kept.");
            }

            var key = spineName + "/" + weaponConfig.Name;
            if (ByKey.ContainsKey(key))
            {
                Plugin.Log.LogWarning($"{key}: declared twice; the first declaration is kept.");
                continue;
            }

            var weapon = new CustomWeapon
            {
                Key = key,
                SpineName = spineName,
                Folder = folder,
                Config = weaponConfig,
                BaseType = baseType
            };

            if (weaponConfig.Combo != null)
                foreach (var hit in weaponConfig.Combo)
                    if (hit != null) weapon.Hits.Add(hit);

            ByKey[key] = weapon;
        }
    }

    /// Assigns ids once every spine has registered. Ids are hashed from the key, so the same
    /// weapon gets the same id on every machine and across sessions whatever else is installed.
    public static void Seal()
    {
        if (_sealed) return;
        _sealed = true;

        var free = new List<int>();
        for (var value = 1; value < (int)EquipmentType.Tentacles; value++)
            if (!Enum.IsDefined(typeof(EquipmentType), value)) free.Add(value);

        var keys = new List<string>(ByKey.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            var weapon = ByKey[key];
            var index = (int)(Fnv1a(key.ToLowerInvariant()) % (uint)free.Count);
            var probes = 0;
            while (ByType.ContainsKey((EquipmentType)free[index]) && probes++ < free.Count)
                index = (index + 1) % free.Count;

            weapon.Type = (EquipmentType)free[index];
            ByType[weapon.Type] = weapon;

            foreach (var hit in weapon.Hits)
                if (!string.IsNullOrEmpty(hit.Animation) && Math.Abs(hit.Speed - 1f) > 0.001f)
                    SpedHits.Add(hit.Animation);

            Plugin.Log.LogInfo($"Custom weapon {key} registered as id {(int)weapon.Type} " +
                               $"(base {weapon.BaseType}, {weapon.Hits.Count} hit(s) configured).");
        }
    }

    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }

    private static bool TryParseBase(string name, out EquipmentType type)
    {
        type = EquipmentType.Sword;
        if (string.IsNullOrWhiteSpace(name)) return true;

        if (!Enum.TryParse(name.Trim(), true, out EquipmentType parsed)) return false;
        switch (parsed)
        {
            case EquipmentType.Sword:
            case EquipmentType.Axe:
            case EquipmentType.Hammer:
            case EquipmentType.Dagger:
            case EquipmentType.Gauntlet:
            case EquipmentType.Blunderbuss:
            case EquipmentType.Shield:
            case EquipmentType.Chain:
                type = parsed;
                return true;
            default:
                return false;
        }
    }

    // ---- weapon data --------------------------------------------------------------------------

    /// The game's data record for the weapon, cloned from the base weapon's on first use.
    public static WeaponData EnsureData(CustomWeapon weapon)
    {
        if (weapon == null) return null;
        if (weapon.Data != null) return weapon.Data;
        if (_building) return null;

        _building = true;
        try
        {
            var baseData = EquipmentManager.GetWeaponData(weapon.BaseType);
            if (baseData == null)
            {
                WarnOnce(weapon.Key + ":nobase",
                    $"{weapon.Key}: the game has no data for {weapon.BaseType}; the weapon is unavailable.");
                return null;
            }

            var data = UnityEngine.Object.Instantiate(baseData);
            data.name = "CultTweaker_" + weapon.Key;
            SpineFolderLoader.Keep(data);

            data.EquipmentType = weapon.Type;
            data.PrimaryEquipmentType = baseData.PrimaryEquipmentType;

            if (Enum.TryParse(weapon.Config.ModifierSkin ?? "", true, out WeaponData.Skins modifier))
                data.Skin = modifier;
            else
                data.Skin = WeaponData.Skins.Normal;

            if (!string.IsNullOrEmpty(weapon.Config.PickupAnimation))
            {
                data.PickupAnimationKey = weapon.Config.PickupAnimation;
                if (!string.IsNullOrEmpty(baseData.PickupAnimationKey))
                    Fallbacks[weapon.Config.PickupAnimation] = baseData.PickupAnimationKey;
            }

            BuildCombos(weapon, baseData, data);
            LoadIcon(weapon, data);

            weapon.Data = data;
            return data;
        }
        catch (Exception e)
        {
            WarnOnce(weapon.Key + ":build", $"{weapon.Key}: could not build its weapon data ({e.Message}).");
            return null;
        }
        finally
        {
            _building = false;
        }
    }

    private static void BuildCombos(CustomWeapon weapon, WeaponData baseData, WeaponData data)
    {
        var templates = data.Combos;
        if (templates == null || templates.Count == 0)
        {
            WarnOnce(weapon.Key + ":combos", $"{weapon.Key}: {weapon.BaseType} has no combo chain to build on.");
            return;
        }

        if (weapon.Hits.Count == 0)
        {
            data.Speed = baseData.Speed;
            return;
        }

        var combos = new List<PlayerWeapon.WeaponCombos>(weapon.Hits.Count);
        var speedSum = 0f;
        for (var i = 0; i < weapon.Hits.Count; i++)
        {
            var hit = weapon.Hits[i];

            // A hit starts as a copy of the base hit at the same position; a hit that names a hook
            // pattern starts from the base hit of that move instead, so its swipe effect, curves
            // and delays belong to the move it performs rather than the slot it sits in.
            var templateIndex = i % templates.Count;
            if (hit.Hook != null && !string.IsNullOrEmpty(hit.Hook.Pattern))
            {
                var patternIndex = PatternIndex(hit.Hook.Pattern);
                if (patternIndex >= 0) templateIndex = Mathf.Min(patternIndex, templates.Count - 1);
            }
            var template = templates[templateIndex];
            var combo = ShallowCopy(template);

            if (!string.IsNullOrEmpty(hit.Animation))
            {
                combo.Animation = hit.Animation;
                if (!string.IsNullOrEmpty(template.Animation) &&
                    !string.Equals(template.Animation, hit.Animation, StringComparison.Ordinal))
                    Fallbacks[hit.Animation] = template.Animation;
            }

            if (hit.Damage.HasValue) combo.Damage = hit.Damage.Value;
            if (hit.Range.HasValue) combo.RangeRadius = hit.Range.Value;
            if (hit.Knockback.HasValue) combo.HitKnockback = hit.Knockback.Value;
            if (hit.LungeSpeed.HasValue) combo.LungeSpeed = hit.LungeSpeed.Value;
            if (hit.LungeDuration.HasValue) combo.LungeDuration = hit.LungeDuration.Value;
            if (hit.CameraShake.HasValue) combo.CameraShake = hit.CameraShake.Value;
            if (hit.CanQueueNext.HasValue) combo.CanQueueNextAttack = hit.CanQueueNext.Value;
            if (hit.CanTurn.HasValue) combo.CanChangeDirectionDuringAttack = hit.CanTurn.Value;
            if (hit.Hook != null)
            {
                var hook = hit.Hook;
                if (hook.StartAngle.HasValue) combo.StartAngle = hook.StartAngle.Value;
                if (hook.Sweep.HasValue) combo.AngleToMove = hook.Sweep.Value;
                if (hook.Width.HasValue) combo.EllipseRadiusY = hook.Width.Value;   // the game's Y is the horizontal radius
                if (hook.Height.HasValue) combo.EllipseRadiusX = hook.Height.Value; // and X the vertical one
                if (hook.Offset.HasValue) combo.OffsetMultiplier = hook.Offset.Value;
                if (hook.Duration.HasValue) combo.Duration = hook.Duration.Value;
                if (hook.ColliderOn.HasValue) combo.ColliderEnabledDelay = hook.ColliderOn.Value;
                if (hook.ColliderOff.HasValue) combo.ColliderDisabledDelay = hook.ColliderOff.Value;
                else if (hook.Duration.HasValue && combo.ColliderDisabledDelay < hook.Duration.Value)
                    combo.ColliderDisabledDelay = hook.Duration.Value; // the chain's own thrusts stop hurting at 0.28s; a longer sweep stays live throughout

                var radiusCurve = Curve(weapon, i, "radiusCurve", hook.RadiusCurve);
                if (radiusCurve != null) combo.RadiusMultiplierOverTime = radiusCurve;
                var scaleCurve = Curve(weapon, i, "scaleCurve", hook.ScaleCurve);
                if (scaleCurve != null) combo.ScaleMultiplierOverTime = scaleCurve;

                if (!string.IsNullOrEmpty(hook.Pattern) && PatternIndex(hook.Pattern) < 0)
                    WarnOnce(weapon.Key + ":pattern" + i,
                        $"{weapon.Key}: hit {i + 1} hook pattern '{hook.Pattern}' is not left, right, slam or fallback; " +
                        "the chain's own order is kept.");
            }

            if (!string.IsNullOrEmpty(hit.AttackType))
            {
                if (Enum.TryParse(hit.AttackType.Trim(), true, out Health.AttackTypes attackType))
                    combo.AttackType = attackType;
                else
                    WarnOnce(weapon.Key + ":attacktype" + i,
                        $"{weapon.Key}: hit {i + 1} attackType '{hit.AttackType}' is not Melee, Heavy, Projectile, " +
                        "Poison, NoKnockBack, Ice or Charm; the base hit's is kept.");
            }

            speedSum += hit.Speed;
            combos.Add(combo);
        }

        data.Combos = combos;
        data.Speed = baseData.Speed * (speedSum / weapon.Hits.Count);

        if (weapon.BaseType == EquipmentType.Chain && Warned.Add("chain:reference"))
            for (var i = 0; i < baseData.Combos.Count; i++)
            {
                var c = baseData.Combos[i];
                Plugin.Log.LogInfo($"Chain hit {i + 1} reference: startAngle {c.StartAngle}, sweep {c.AngleToMove}, " +
                                   $"width {c.EllipseRadiusY}, height {c.EllipseRadiusX}, offset {c.OffsetMultiplier}, " +
                                   $"duration {c.Duration}, colliderOn {c.ColliderEnabledDelay}, colliderOff {c.ColliderDisabledDelay}, " +
                                   $"range {c.RangeRadius}, damage {c.Damage}.");
            }
    }

    /// An animation curve from [time, value] pairs, smoothed; null when the list is empty or bad.
    private static AnimationCurve Curve(CustomWeapon weapon, int hitIndex, string field, List<float[]> pairs)
    {
        if (pairs == null || pairs.Count == 0) return null;

        var keys = new List<Keyframe>(pairs.Count);
        foreach (var pair in pairs)
        {
            if (pair == null || pair.Length < 2)
            {
                WarnOnce(weapon.Key + ":" + field + hitIndex,
                    $"{weapon.Key}: hit {hitIndex + 1} {field} needs [time, value] pairs; the chain's own curve is kept.");
                return null;
            }

            keys.Add(new Keyframe(Mathf.Clamp01(pair[0]), pair[1]));
        }

        keys.Sort((a, b) => a.time.CompareTo(b.time));
        var curve = new AnimationCurve([.. keys]);
        for (var k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0f);
        return curve;
    }

    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

    private static PlayerWeapon.WeaponCombos ShallowCopy(PlayerWeapon.WeaponCombos source) =>
        (PlayerWeapon.WeaponCombos)MemberwiseCloneMethod.Invoke(source, null);

    private static void LoadIcon(CustomWeapon weapon, WeaponData data)
    {
        var name = string.IsNullOrEmpty(weapon.Config.Icon) ? weapon.Config.HookIcon : weapon.Config.Icon;
        if (string.IsNullOrEmpty(name)) return;

        var path = Path.Combine(weapon.Folder, name);
        if (!File.Exists(path))
        {
            WarnOnce(weapon.Key + ":icon", $"{weapon.Key}: icon '{name}' not found; the base icon is used.");
            return;
        }

        try
        {
            var sprite = LoadSprite(path);
            if (sprite == null) return;

            // Drawn where the base icon was drawn, so match the base icon's size on screen.
            data.WorldSprite = Fit(sprite, data.WorldSprite, 1f);
            data.UISprite = Fit(sprite, data.UISprite, 1f);
        }
        catch (Exception e)
        {
            WarnOnce(weapon.Key + ":icon", $"{weapon.Key}: icon could not be read ({e.Message}); the base icon is used.");
        }
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = TextureHelper.CreateSpriteFromPath(path);
        if (sprite == null) return null;
        SpineFolderLoader.Keep(sprite);
        SpineFolderLoader.Keep(sprite.texture);
        return sprite;
    }

    /// A copy of the sprite whose larger side measures the same as the reference sprite's, times
    /// scale, with the reference's pivot; the sprite itself when there is no reference.
    private static Sprite Fit(Sprite sprite, Sprite reference, float scale)
    {
        if (sprite == null) return null;
        if (reference == null || scale <= 0f) return sprite;

        var size = reference.bounds.size;
        var targetMax = Mathf.Max(size.x, size.y) * scale;
        if (targetMax <= 0f) return sprite;

        var rect = sprite.rect;
        var ppu = Mathf.Max(rect.width, rect.height) / targetMax;
        var pivot = new Vector2(
            reference.rect.width > 0f ? reference.pivot.x / reference.rect.width : 0.5f,
            reference.rect.height > 0f ? reference.pivot.y / reference.rect.height : 0.5f);

        var fitted = Sprite.Create(sprite.texture, rect, pivot, ppu);
        fitted.name = sprite.name + "_fitted";
        SpineFolderLoader.Keep(fitted);
        return fitted;
    }

    /// The hook picture as read from disk, or null to keep the base hook.
    private static Sprite HookRaw(CustomWeapon weapon)
    {
        if (weapon == null || weapon.HookSpriteTried) return weapon?.HookRaw;
        weapon.HookSpriteTried = true;

        if (string.IsNullOrEmpty(weapon.Config.HookIcon)) return null;

        var path = Path.Combine(weapon.Folder, weapon.Config.HookIcon);
        if (!File.Exists(path))
        {
            WarnOnce(weapon.Key + ":hook", $"{weapon.Key}: hookIcon '{weapon.Config.HookIcon}' not found; the chain's hook is used.");
            return null;
        }

        try
        {
            weapon.HookRaw = LoadSprite(path);
        }
        catch (Exception e)
        {
            WarnOnce(weapon.Key + ":hook", $"{weapon.Key}: hookIcon could not be read ({e.Message}); the chain's hook is used.");
        }

        return weapon.HookRaw;
    }

    /// The chain hook looks its sprite up by exact weapon id, so a custom id would leave it blank:
    /// give it the custom hook picture, sized like the chain's own hook, or the base chain's hook
    /// when there is none.
    public static void DressHook(ChainHook hook, EquipmentType type)
    {
        var weapon = Get(type);
        if (weapon == null || hook == null) return;

        // The base hook first: it is the fallback, and its sprite is the size reference.
        if (weapon.BaseType != type) hook.SetVisuals(weapon.BaseType);

        var raw = HookRaw(weapon);
        if (raw == null) return;

        try
        {
            var traverse = Traverse.Create(hook);
            var flat = traverse.Field("hook2DVisual").GetValue<SpriteRenderer>();
            var deep = traverse.Field("hook3DVisual").GetValue<SpriteRenderer>();

            if (weapon.HookSprite == null)
            {
                var reference = flat != null ? flat.sprite : deep != null ? deep.sprite : null;
                weapon.HookSprite = Fit(raw, reference, weapon.Config.HookScale);
                if (reference != null)
                    Plugin.Log.LogInfo($"{weapon.Key}: hook picture fitted to the {weapon.BaseType} hook " +
                                       $"({reference.bounds.size.x:0.00} x {reference.bounds.size.y:0.00} units, scale {weapon.Config.HookScale}).");
            }

            if (flat != null) flat.sprite = weapon.HookSprite;
            if (deep != null) deep.sprite = weapon.HookSprite;
        }
        catch (Exception e)
        {
            WarnOnce(weapon.Key + ":hookset", $"{weapon.Key}: could not set the hook picture ({e.Message}); the chain's hook is used.");
        }
    }

    // ---- weapon pool --------------------------------------------------------------------------

    /// Puts every weapon that wants rolling into the save's weapon pool. Safe to call often.
    public static void EnsureInPool()
    {
        if (ByType.Count == 0) return;
        var save = DataManager.Instance;
        if (save?.WeaponPool == null) return;

        foreach (var weapon in ByType.Values)
        {
            if (!weapon.Config.InPool) continue;
            if (save.WeaponPool.Contains(weapon.Type)) continue;
            if (EnsureData(weapon) == null) continue;

            save.WeaponPool.Add(weapon.Type);
            Plugin.Log.LogInfo($"Custom weapon {weapon.Key} added to the weapon pool.");
        }
    }

    // ---- skin ---------------------------------------------------------------------------------

    /// Dresses a player with the custom weapon they hold, if any. Runs after the game rebuilt
    /// the player skin; the live skin is the composite the game just built.
    public static void DressPlayer(PlayerFarming player, Skin liveSkin)
    {
        if (player == null || ByType.Count == 0) return;
        var weapon = Get(player.currentWeapon);
        if (weapon == null) return;

        var spine = player.Spine;
        if (spine == null || spine.Skeleton == null) return;

        ApplyWeaponSkin(spine, liveSkin ?? spine.Skeleton.Skin, weapon, () =>
        {
            if (player != null && player.currentWeapon == weapon.Type) player.SetSkin();
        });
    }

    /// Public entry for other mods (a multiplayer mirror wearing another player's look): copies
    /// the custom weapon's attachments into the skeleton's current skin. False when the weapon is
    /// not custom or its home spine is still loading (the retry runs when it lands).
    public static bool ApplyWeaponSkin(SkeletonAnimation spine, EquipmentType type, Action retry = null)
    {
        var weapon = Get(type);
        if (weapon == null || spine == null || spine.Skeleton == null) return false;
        return ApplyWeaponSkin(spine, spine.Skeleton.Skin, weapon, retry);
    }

    private static bool ApplyWeaponSkin(SkeletonAnimation spine, Skin liveSkin, CustomWeapon weapon, Action retry)
    {
        var homeData = HomeSkeletonData(weapon, retry);
        if (homeData == null) return false;

        PrepareHome(weapon, homeData);

        if (string.IsNullOrEmpty(weapon.Config.Skin)) return false;

        var sourceData = homeData;
        var skin = homeData.FindSkin(weapon.Config.Skin);
        if (skin == null && spine.Skeleton.Data != homeData)
        {
            skin = spine.Skeleton.Data.FindSkin(weapon.Config.Skin);
            sourceData = spine.Skeleton.Data;
        }

        if (skin == null)
        {
            WarnOnce(weapon.Key + ":skin", $"{weapon.Key}: skin '{weapon.Config.Skin}' is not in {weapon.SpineName}; " +
                                           "the base weapon's look is kept.");
            return false;
        }

        if (liveSkin == null || IsDataSkin(spine.Skeleton.Data, liveSkin))
        {
            var composite = new Skin("CultTweaker weapon");
            if (liveSkin != null) composite.AddSkin(liveSkin);
            spine.Skeleton.SetSkin(composite);
            liveSkin = composite;
        }

        var copied = 0;
        var slots = sourceData.Slots;
        foreach (var entry in new List<Skin.SkinEntry>(skin.Attachments))
        {
            if (entry.SlotIndex < 0 || entry.SlotIndex >= slots.Count) continue;

            var slotName = slots.Items[entry.SlotIndex].Name;
            var targetIndex = spine.Skeleton.FindSlotIndex(slotName);
            if (targetIndex < 0) continue;

            liveSkin.SetAttachment(targetIndex, entry.Name, entry.Attachment);
            copied++;
        }

        spine.Skeleton.SetSlotsToSetupPose();
        spine.Update(0f);

        if (copied == 0)
            WarnOnce(weapon.Key + ":empty", $"{weapon.Key}: skin '{weapon.Config.Skin}' put nothing on this skeleton " +
                                            "(no slot names in common).");
        return copied > 0;
    }

    private static bool IsDataSkin(SkeletonData data, Skin skin)
    {
        var skins = data?.Skins;
        if (skins == null) return false;
        foreach (var candidate in skins)
            if (ReferenceEquals(candidate, skin)) return true;
        return false;
    }

    /// The declaring spine's parsed skeleton, or null while it is still loading (the retry runs
    /// when it lands). Never parses on the main thread: a spine loaded at boot may still have its
    /// parse on the warm-up thread, and asking the asset directly would block for seconds.
    private static SkeletonData HomeSkeletonData(CustomWeapon weapon, Action retry)
    {
        var asset = PlayerSpineLoader.AssetFor(weapon.SpineName);
        if (asset == null)
        {
            if (!PlayerSpineLoader.IsRegistered(weapon.SpineName))
            {
                WarnOnce(weapon.Key + ":home", $"{weapon.Key}: its spine folder {weapon.SpineName} is gone; " +
                                               "the base weapon's look and animations are used.");
                return null;
            }

            Plugin.Log.LogInfo($"{weapon.Key}: loading {weapon.SpineName} for its weapon art.");
            PlayerSpineLoader.EnsureLoaded(weapon.SpineName, retry, announce: false);
            return null;
        }

        if (asset.skeletonData != null) return asset.skeletonData;

        if (retry != null && Plugin.Instance != null && Waiting.Add(weapon.Key))
            Plugin.Instance.StartCoroutine(WaitForParse(weapon, asset, retry));
        return null;
    }

    private static readonly HashSet<string> Waiting = [];

    private static System.Collections.IEnumerator WaitForParse(CustomWeapon weapon, SkeletonDataAsset asset, Action retry)
    {
        var deadline = Time.unscaledTime + 90f;
        while (asset != null && asset.skeletonData == null && Time.unscaledTime < deadline)
            yield return null;

        Waiting.Remove(weapon.Key);

        if (asset != null && asset.skeletonData == null)
        {
            try
            {
                Plugin.Log.LogWarning($"{weapon.Key}: {weapon.SpineName}'s warm-up never landed; parsing it now.");
                asset.GetSkeletonData(false);
            }
            catch (Exception e)
            {
                WarnOnce(weapon.Key + ":parse", $"{weapon.Key}: {weapon.SpineName}'s skeleton could not be read ({e.Message}).");
                yield break;
            }
        }

        try
        {
            retry();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"{weapon.Key}: dressing after the load failed ({e.Message}).");
        }
    }

    /// Starts loading the declaring spine ahead of need (a podium rolled it, a chest dropped it),
    /// so the art is ready by the time it is picked up. Cheap when already loaded.
    public static void Prefetch(EquipmentType type)
    {
        var weapon = Get(type);
        if (weapon == null) return;
        if (PlayerSpineLoader.AssetFor(weapon.SpineName) != null) return;
        if (!PlayerSpineLoader.IsRegistered(weapon.SpineName) || PlayerSpineLoader.IsPreparing(weapon.SpineName)) return;

        Plugin.Log.LogInfo($"{weapon.Key}: loading {weapon.SpineName} ahead of pickup.");
        PlayerSpineLoader.EnsureLoaded(weapon.SpineName, null, announce: false);
    }

    // ---- animations ---------------------------------------------------------------------------

    /// Substitutes the base weapon's animation when the skeleton lacks a custom one. True when
    /// the name was changed.
    public static bool Remap(Spine.AnimationState state, ref string animationName)
    {
        if (Fallbacks.Count == 0 || string.IsNullOrEmpty(animationName) || state?.Data?.SkeletonData == null)
            return false;

        var data = state.Data.SkeletonData;
        if (data.FindAnimation(animationName) != null) return false;
        if (!Fallbacks.TryGetValue(animationName, out var fallback)) return false;
        if (data.FindAnimation(fallback) == null) return false;

        animationName = fallback;
        return true;
    }

    /// Per-hit playback speed for a track entry the player just started.
    public static void ApplyHitSpeed(Spine.AnimationState state, string requestedName, TrackEntry entry)
    {
        if (entry == null || SpedHits.Count == 0 || string.IsNullOrEmpty(requestedName)) return;
        if (!SpedHits.Contains(requestedName)) return;

        var player = PlayerFor(state);
        if (player == null) return;

        var weapon = Get(player.currentWeapon);
        if (weapon == null || weapon.Hits.Count == 0) return;

        PlayerWeaponHitConfig hit = null;
        var combo = CurrentCombo(player);
        if (combo >= 0 && combo < weapon.Hits.Count &&
            string.Equals(weapon.Hits[combo].Animation, requestedName, StringComparison.Ordinal))
            hit = weapon.Hits[combo];
        else
            foreach (var candidate in weapon.Hits)
                if (string.Equals(candidate.Animation, requestedName, StringComparison.Ordinal))
                {
                    hit = candidate;
                    break;
                }

        if (hit == null || hit.Speed <= 0f) return;
        entry.TimeScale = hit.Speed;
    }

    private static AccessTools.FieldRef<PlayerWeapon, int> _currentCombo;
    private static bool _currentComboMissing;

    private static int CurrentCombo(PlayerFarming player)
    {
        if (_currentComboMissing) return -1;
        try
        {
            _currentCombo ??= AccessTools.FieldRefAccess<PlayerWeapon, int>("CurrentCombo");
            var weapon = player.playerWeapon;
            return weapon == null ? -1 : _currentCombo(weapon);
        }
        catch (Exception e)
        {
            _currentComboMissing = true;
            Plugin.Log.LogWarning("Custom weapons: PlayerWeapon.CurrentCombo is not readable (" + e.Message +
                                  "); per-hit speeds match by animation name only.");
            return -1;
        }
    }

    private static PlayerFarming PlayerFor(Spine.AnimationState state)
    {
        if (state == null) return null;

        var players = PlayerFarming.players;
        if (players != null)
            foreach (var player in players)
                if (player != null && player.Spine != null && ReferenceEquals(player.Spine.AnimationState, state))
                    return player;

        var one = PlayerFarming.Instance;
        return one != null && one.Spine != null && ReferenceEquals(one.Spine.AnimationState, state) ? one : null;
    }

    // ---- chain hook patterns ------------------------------------------------------------------

    private static int PatternIndex(string pattern)
    {
        switch ((pattern ?? "").Trim().ToLowerInvariant())
        {
            case "left": return 0;
            case "right": return 1;
            case "slam": return 2;
            case "fallback": return 3;
            default: return -1;
        }
    }

    /// The routing in force during the current chain attack, so the postfix that swings a second
    /// chain can tell which hit asked for it.
    private static ChainSwap _lastSwap;

    public sealed class ChainSwap
    {
        public PlayerWeapon Weapon;
        public List<PlayerWeapon.WeaponCombos> Combos;
        public int Current;
        public int Target;
        public PlayerWeapon.WeaponCombos Saved;
    }

    /// The game's chain attack switches on the hit index to pick the move. To run a hit's chosen
    /// pattern, the hit record is placed at that pattern's index for the duration of the call and
    /// the index pointed there; EndChainHit puts both back. Null when nothing needs swapping.
    public static ChainSwap BeginChainHit(PlayerWeapon playerWeapon)
    {
        if (playerWeapon == null || ByType.Count == 0) return null;
        var player = playerWeapon.playerFarming;
        if (player == null) return null;

        var weapon = Get(player.currentWeapon);
        if (weapon == null || weapon.Hits.Count == 0) return null;

        var current = CurrentCombo(player);
        if (current < 0 || current >= weapon.Hits.Count) return null;

        var hook = weapon.Hits[current].Hook;
        if (hook == null || string.IsNullOrEmpty(hook.Pattern)) return null;

        var target = PatternIndex(hook.Pattern);
        if (target < 0) return null;

        var combos = player.CurrentWeaponInfo?.WeaponData?.Combos;
        if (combos == null || current >= combos.Count) return null;

        if (target >= 3)
        {
            // The fallback is the switch's default arm: any index past the three moves reaches it,
            // but the index must still address a real record.
            if (current >= 3) return null;
            if (combos.Count < 4)
            {
                WarnOnce(weapon.Key + ":fallback" + current,
                    $"{weapon.Key}: hit {current + 1} asks for the fallback sweep, which needs a chain of four or more hits; " +
                    "the chain's own move is used.");
                return null;
            }
            target = combos.Count - 1;
        }

        if (target == current) return null;

        if (Warned.Add(weapon.Key + ":routed" + current))
            Plugin.Log.LogInfo($"{weapon.Key}: hit {current + 1} routed to the {hook.Pattern.Trim().ToLowerInvariant()} move " +
                               $"(sweep {combos[current].AngleToMove}, width {combos[current].EllipseRadiusY}, " +
                               $"height {combos[current].EllipseRadiusX}, duration {combos[current].Duration}).");

        var swap = new ChainSwap
        {
            Weapon = playerWeapon, Combos = combos, Current = current, Target = target, Saved = combos[target]
        };
        combos[target] = combos[current];
        SetCurrentCombo(player, target);
        _lastSwap = swap;
        return swap;
    }

    /// The game swings one chain on a sweep and both only on the slam. A hit asking for two gets
    /// the other chain swung here, on the mirrored side of the player and, by default, half a turn
    /// behind the first. Runs after the game's own swing, while the routing is still in place.
    public static void SwingExtraHook(PlayerWeapon playerWeapon)
    {
        if (playerWeapon == null || ByType.Count == 0) return;

        var player = playerWeapon.playerFarming;
        if (player == null) return;

        var weapon = Get(player.currentWeapon);
        if (weapon == null) return;

        var hit = _lastSwap != null ? _lastSwap.Current : CurrentCombo(player);
        if (hit < 0 || hit >= weapon.Hits.Count) return;

        var config = weapon.Hits[hit].Hook;
        if (config == null || config.Hooks < 2) return;

        var move = CurrentCombo(player);
        if (move == 2) return; // the slam already swings both

        if (move != 0 && move != 1)
        {
            WarnOnce(weapon.Key + ":twohooks" + hit,
                $"{weapon.Key}: hit {hit + 1} asks for two chains, which only the left and right moves can do; one is swung.");
            return;
        }

        var combos = player.CurrentWeaponInfo?.WeaponData?.Combos;
        if (combos == null || move >= combos.Count) return;

        try
        {
            SwingSecond(playerWeapon, player, combos[move], move, config);
        }
        catch (Exception e)
        {
            WarnOnce(weapon.Key + ":twohooksfail",
                $"{weapon.Key}: the second chain could not be swung ({e.Message}); one is swung.");
        }
    }

    private static readonly MethodInfo SpawnChainSwipeVfx =
        AccessTools.Method(typeof(PlayerWeapon), "SpawnChainSwipeVFX", [typeof(Vector3), typeof(float)]);

    /// Everything the second chain's swing needs, read while the hit's record is still in place:
    /// the record is put back the moment the game's attack call returns, so a delayed shot cannot
    /// read it later.
    private sealed class SecondShot
    {
        public PlayerWeapon Weapon;
        public PlayerFarming Player;
        public ChainHook Hook;
        public float Sign;
        public float Offset;
        public float Width;
        public float Height;
        public float StartAngle;
        public float Sweep;
        public float Duration;
        public float Rate;
        public float Range;
        public float BaseDamage;
        public float ColliderOn;
        public float ColliderOff;
        public AnimationCurve RadiusCurve;
        public AnimationCurve ScaleCurve;
        public GameObject Vfx;
    }

    private static void SwingSecond(PlayerWeapon playerWeapon, PlayerFarming player,
        PlayerWeapon.WeaponCombos record, int move, PlayerWeaponHookConfig config)
    {
        var traverse = Traverse.Create(playerWeapon);

        // The game's left move swings chainHook and its right move chainHook1; take the other one.
        var hook = move == 0
            ? traverse.Field("chainHook1").GetValue<ChainHook>()
            : traverse.Field("chainHook").GetValue<ChainHook>();
        if (hook == null) return;

        var rate = player.CurrentWeaponInfo.AttackRateMultiplier + TrinketManager.GetAttackRateMultiplier(player);
        if (rate <= 0f) rate = 1f;

        var shot = new SecondShot
        {
            Weapon = playerWeapon,
            Player = player,
            Hook = hook,
            // Mirrored: the left move centres its ellipse at -side, so the second sits at +side.
            Sign = move == 0 ? 1f : -1f,
            Offset = record.OffsetMultiplier,
            Width = record.EllipseRadiusY,
            Height = record.EllipseRadiusX,
            StartAngle = record.StartAngle + config.SecondStartAngle,
            Sweep = record.AngleToMove,
            Duration = record.Duration,
            Rate = rate,
            Range = record.RangeRadius * player.CurrentWeaponInfo.RangeMultiplier,
            BaseDamage = record.Damage,
            ColliderOn = record.ColliderEnabledDelay,
            ColliderOff = record.ColliderDisabledDelay,
            RadiusCurve = record.RadiusMultiplierOverTime,
            ScaleCurve = record.ScaleMultiplierOverTime,
            Vfx = record.SwipeObject
        };

        var delay = config.SecondDelay / rate;
        if (delay <= 0f || Plugin.Instance == null)
        {
            Fire(shot);
            return;
        }

        Plugin.Instance.StartCoroutine(FireLater(shot, delay));
    }

    private static System.Collections.IEnumerator FireLater(SecondShot shot, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (shot.Hook == null || shot.Player == null || shot.Weapon == null) yield break;

        try
        {
            Fire(shot);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom weapons: the delayed second chain failed (" + e.Message + ").");
        }
    }

    private static void Fire(SecondShot shot)
    {
        // Facing is read as the chain leaves, so a delayed shot follows the player's turn.
        var stateMachine = shot.Weapon.GetComponent<StateMachine>();
        var facing = stateMachine != null ? stateMachine.facingAngle : 0f;
        var forward = new Vector3(Mathf.Cos(facing * Mathf.Deg2Rad), Mathf.Sin(facing * Mathf.Deg2Rad));
        var side = new Vector3(-forward.y, forward.x);
        var centre = side * (shot.Offset * shot.Sign);

        var ellipse = new EllipseMovement(Vector3.up, Vector3.right, centre,
            shot.Width, shot.Height, facing + shot.StartAngle, shot.Sweep,
            shot.Duration / shot.Rate, shot.RadiusCurve);

        var damage = PlayerWeapon.GetDamage(shot.BaseDamage, shot.Player.currentWeaponLevel, shot.Player);
        var flags = (Health.AttackFlags)0;

        var crit = Traverse.Create(shot.Weapon).Method("GetCritChance").GetValue<float>();
        if (UnityEngine.Random.Range(0f, 1f) < crit)
        {
            damage *= 3f;
            flags |= Health.AttackFlags.Crit;
        }

        if (TrinketManager.HasTrinket(TarotCards.Card.Skull, shot.Player)) flags |= Health.AttackFlags.Skull;
        if (TrinketManager.HasTrinket(TarotCards.Card.Spider, shot.Player)) flags |= Health.AttackFlags.Poison;

        shot.Hook.Init(ellipse, damage, shot.Range, flags, animatedHide: true, shot.ScaleCurve,
            chainDamage: true, shot.ColliderOn, shot.ColliderOff);

        if (SpawnChainSwipeVfx == null || shot.Vfx == null) return;
        var position = shot.Weapon.transform.position - Vector3.forward * 0.3f + centre +
                       side * (shot.Sign * (shot.Offset - 0.2f));
        SpawnChainSwipeVfx.Invoke(shot.Weapon, [position, shot.Rate]);
    }

    public static void EndChainHit(ChainSwap swap)
    {
        _lastSwap = null;
        if (swap == null) return;
        try
        {
            if (swap.Combos != null && swap.Target < swap.Combos.Count) swap.Combos[swap.Target] = swap.Saved;
            SetCurrentCombo(swap.Weapon.playerFarming, swap.Current);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom weapons: could not restore the chain hit after a pattern swap (" + e.Message + ").");
        }
    }

    private static void SetCurrentCombo(PlayerFarming player, int value)
    {
        if (_currentComboMissing || player == null) return;
        try
        {
            _currentCombo ??= AccessTools.FieldRefAccess<PlayerWeapon, int>("CurrentCombo");
            var weapon = player.playerWeapon;
            if (weapon != null) _currentCombo(weapon) = value;
        }
        catch (Exception e)
        {
            _currentComboMissing = true;
            Plugin.Log.LogWarning("Custom weapons: PlayerWeapon.CurrentCombo is not writable (" + e.Message + ").");
        }
    }

    // ---- hitbox -------------------------------------------------------------------------------

    private static PlayerFarming _lightHitOf;

    /// Marks the player whose light attack is spawning its swipe (set around the game's
    /// AttackDealDamage), so the swipe resize below never touches heavy attacks or other swipes.
    public static void BeginLightHit(PlayerFarming player) => _lightHitOf = player;

    public static void EndLightHit() => _lightHitOf = null;

    /// True while a player holding a custom weapon is spawning a light hit's swipe.
    public static bool InCustomLightHit => _lightHitOf != null && IsCustom(_lightHitOf.currentWeapon);

    /// Vanilla sizes the swipe's circle to the hit's reach; a hit that names its own hitboxRadius
    /// gets that instead, scaled by the same range multiplier tarot cards apply.
    public static void ResizeSwipe(Swipe swipe)
    {
        var player = _lightHitOf;
        if (player == null || swipe == null || ByType.Count == 0) return;

        var weapon = Get(player.currentWeapon);
        if (weapon == null || weapon.Hits.Count == 0) return;

        var combo = CurrentCombo(player);
        if (combo < 0 || combo >= weapon.Hits.Count) return;

        var hit = weapon.Hits[combo];
        if (!hit.HitboxRadius.HasValue) return;

        if (swipe.damageCollider is CircleCollider2D circle)
        {
            var multiplier = player.CurrentWeaponInfo != null ? player.CurrentWeaponInfo.RangeMultiplier : 1f;
            circle.radius = hit.HitboxRadius.Value * multiplier;
            return;
        }

        WarnOnce(weapon.Key + ":hitbox" + combo,
            $"{weapon.Key}: hit {combo + 1} asks for a hitbox radius but {weapon.BaseType}'s swipe is not a circle; ignored.");
    }

    // ---- attack events ------------------------------------------------------------------------

    private const string EventDealDamage = "Attack Deal Damage";
    private const string EventCanBreak = "Attack Can Break";
    private const string EventFinished = "Attack Has Finished";
    private const string EventUpdateAngle = "Update Angle";

    /// Checks the weapon's animations on its home skeleton once: warns about names it lacks and
    /// gives attack animations the events the combat code waits for when they carry none.
    private static void PrepareHome(CustomWeapon weapon, SkeletonData homeData)
    {
        var tag = weapon.Key + "@" + homeData.GetHashCode();
        if (!Prepared.Add(tag)) return;

        if (!string.IsNullOrEmpty(weapon.Config.PickupAnimation) &&
            homeData.FindAnimation(weapon.Config.PickupAnimation) == null)
            Plugin.Log.LogWarning($"{weapon.Key}: pickup animation '{weapon.Config.PickupAnimation}' is not in " +
                                  $"{weapon.SpineName}; the {weapon.BaseType} pickup plays instead.");

        for (var i = 0; i < weapon.Hits.Count; i++)
        {
            var hit = weapon.Hits[i];
            if (string.IsNullOrEmpty(hit.Animation)) continue;

            var animation = homeData.FindAnimation(hit.Animation);
            if (animation == null)
            {
                Plugin.Log.LogWarning($"{weapon.Key}: hit {i + 1} animation '{hit.Animation}' is not in " +
                                      $"{weapon.SpineName}; the {weapon.BaseType} hit plays instead.");
                continue;
            }

            EnsureAttackEvents(weapon, homeData, animation, hit, i);
        }
    }

    private static void EnsureAttackEvents(CustomWeapon weapon, SkeletonData data, Spine.Animation animation,
        PlayerWeaponHitConfig hit, int index)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var timeline in animation.Timelines)
        {
            if (timeline is not EventTimeline events) continue;
            foreach (var e in events.Events)
                if (e?.Data != null) present.Add(e.Data.Name);
        }

        if (present.Contains(EventDealDamage) && present.Contains(EventCanBreak) && present.Contains(EventFinished))
            return;

        var duration = animation.Duration;
        if (duration <= 0f) return;

        var hitAt = Mathf.Clamp01(hit.HitAt ?? 0.35f) * duration;
        var breakAt = Mathf.Clamp(hit.BreakAt ?? 0.65f, 0f, 1f) * duration;
        if (breakAt < hitAt) breakAt = hitAt;

        var wanted = new List<(string Name, float Time)>();
        if (!present.Contains(EventUpdateAngle)) wanted.Add((EventUpdateAngle, hitAt));
        if (!present.Contains(EventDealDamage)) wanted.Add((EventDealDamage, hitAt));
        if (!present.Contains(EventCanBreak)) wanted.Add((EventCanBreak, breakAt));
        if (!present.Contains(EventFinished)) wanted.Add((EventFinished, duration));
        wanted.Sort((a, b) => a.Time.CompareTo(b.Time));

        var injected = new EventTimeline(wanted.Count);
        for (var i = 0; i < wanted.Count; i++)
        {
            var eventData = data.FindEvent(wanted[i].Name);
            if (eventData == null)
            {
                eventData = new EventData(wanted[i].Name);
                data.Events.Add(eventData);
            }

            injected.SetFrame(i, new Spine.Event(wanted[i].Time, eventData));
        }

        animation.Timelines.Add(injected);

        var names = new List<string>();
        foreach (var w in wanted) names.Add(w.Name);
        Plugin.Log.LogInfo($"{weapon.Key}: hit {index + 1} '{animation.Name}' had no attack events; added " +
                           string.Join(", ", names) + $" (hit at {hitAt:0.00}s, break at {breakAt:0.00}s of {duration:0.00}s).");
    }

    // ---- misc ---------------------------------------------------------------------------------

    private static void WarnOnce(string tag, string message)
    {
        if (Warned.Add(tag)) Plugin.Log.LogWarning(message);
    }

    /// Reports a weapon that a save or another machine names but this install lacks.
    public static void NoteUnknown(EquipmentType type)
    {
        WarnOnce("unknown:" + (int)type,
            $"Weapon id {(int)type} has no data on this install (a custom weapon whose spine is missing?); " +
            "the Sword stands in.");
    }
}
