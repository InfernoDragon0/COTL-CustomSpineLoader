using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using Spine;

namespace CustomSpineLoader.Patches;

/// Wires custom weapons into the game's weapon plumbing. Everything here is a no-op while no
/// player spine declares a weapon.
[HarmonyPatch]
public static class CustomWeaponPatches
{
    // The registry: an id the game does not know is answered with the custom weapon's data.
    [HarmonyPatch(typeof(EquipmentManager), nameof(EquipmentManager.GetWeaponData))]
    [HarmonyPostfix]
    private static void EquipmentManager_GetWeaponData(EquipmentType weaponType, ref WeaponData __result)
    {
        if (__result != null || CustomWeapons.Count == 0) return;

        var weapon = CustomWeapons.Get(weaponType);
        if (weapon != null) __result = CustomWeapons.EnsureData(weapon);
    }

    // A weapon nobody can describe (a save that names a custom weapon whose spine was removed, or
    // a peer's weapon) would leave the attack code dereferencing null; the sword stands in.
    [HarmonyPatch(typeof(PlayerWeapon), nameof(PlayerWeapon.SetWeapon))]
    [HarmonyPrefix]
    private static void PlayerWeapon_SetWeapon(ref EquipmentType weaponType)
    {
        if (weaponType == EquipmentType.None) return;
        if (EquipmentManager.GetWeaponData(weaponType) != null) return;

        CustomWeapons.NoteUnknown(weaponType);
        weaponType = EquipmentType.Sword;
    }

    // A weapon entering the world (a podium rolled it, a chest or an enemy dropped it) starts its
    // spine loading, so the art is ready before anyone reaches it. Every podium kind writes this
    // property; ground pickups take the other route.
    [HarmonyPatch(typeof(Interaction_WeaponSelectionPodium), nameof(Interaction_WeaponSelectionPodium.TypeOfWeapon),
        MethodType.Setter)]
    [HarmonyPostfix]
    private static void Podium_TypeOfWeapon(EquipmentType value)
    {
        if (CustomWeapons.Count > 0) CustomWeapons.Prefetch(value);
    }

    [HarmonyPatch(typeof(Interaction_WeaponPickUp), nameof(Interaction_WeaponPickUp.SetWeapon))]
    [HarmonyPostfix]
    private static void WeaponPickUp_SetWeapon(EquipmentType TypeOfWeapon)
    {
        if (CustomWeapons.Count > 0) CustomWeapons.Prefetch(TypeOfWeapon);
    }

    // The swipe a light attack spawns is sized to the hit's reach by the game; a hit with its own
    // hitbox radius is resized right after the game's Init. The marker brackets AttackDealDamage so
    // heavy attacks and other swipes (which also go through Swipe.Init) are left alone.
    [HarmonyPatch(typeof(PlayerWeapon), "AttackDealDamage")]
    [HarmonyPrefix]
    private static void PlayerWeapon_AttackDealDamage_Prefix(PlayerWeapon __instance)
    {
        if (CustomWeapons.Count > 0) CustomWeapons.BeginLightHit(__instance.playerFarming);
    }

    [HarmonyPatch(typeof(PlayerWeapon), "AttackDealDamage")]
    [HarmonyFinalizer]
    private static void PlayerWeapon_AttackDealDamage_Finalizer()
    {
        CustomWeapons.EndLightHit();
    }

    [HarmonyPatch(typeof(Swipe), nameof(Swipe.Init))]
    [HarmonyPostfix]
    private static void Swipe_Init(Swipe __instance)
    {
        if (CustomWeapons.Count > 0) CustomWeapons.ResizeSwipe(__instance);
        if (HitboxGizmo.Enabled) HitboxGizmo.Show(__instance, CustomWeapons.InCustomLightHit);
    }

    // The chain attack picks its move by hit index; a hit with a hook pattern is routed to that
    // move's index for the length of the call.
    [HarmonyPatch(typeof(PlayerWeapon), "ChainAttack")]
    [HarmonyPrefix]
    private static void PlayerWeapon_ChainAttack_Prefix(PlayerWeapon __instance, ref CustomWeapons.ChainSwap __state)
    {
        __state = CustomWeapons.BeginChainHit(__instance);
    }

    // Runs after the game swung its one chain and before the routing is put back.
    [HarmonyPatch(typeof(PlayerWeapon), "ChainAttack")]
    [HarmonyPostfix]
    private static void PlayerWeapon_ChainAttack_Postfix(PlayerWeapon __instance)
    {
        if (CustomWeapons.Count > 0) CustomWeapons.SwingExtraHook(__instance);
    }

    [HarmonyPatch(typeof(PlayerWeapon), "ChainAttack")]
    [HarmonyFinalizer]
    private static void PlayerWeapon_ChainAttack_Finalizer(CustomWeapons.ChainSwap __state)
    {
        CustomWeapons.EndChainHit(__state);
    }

    // The chain hook never goes through Swipe, so the hit box gizmo follows it separately. Both
    // Init overloads start a swing.
    [HarmonyPatch]
    private static class ChainHook_Init_Patch
    {
        [HarmonyTargetMethods]
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> Targets()
        {
            foreach (var method in typeof(ChainHook).GetMethods())
                if (method.Name == "Init") yield return method;
        }

        [HarmonyPostfix]
        private static void Postfix(ChainHook __instance)
        {
            if (HitboxGizmo.Enabled) HitboxGizmo.TrackHook(__instance);
        }
    }

    // The chain's swinging hook is a sprite on its own prefab, chosen by exact weapon id.
    [HarmonyPatch(typeof(ChainHook), nameof(ChainHook.SetVisuals))]
    [HarmonyPostfix]
    private static void ChainHook_SetVisuals(ChainHook __instance, EquipmentType equipmentType)
    {
        if (CustomWeapons.Count > 0) CustomWeapons.DressHook(__instance, equipmentType);
    }

    // Names, descriptions and lore come from the config instead of the localisation table.
    [HarmonyPatch(typeof(EquipmentData), nameof(EquipmentData.GetLocalisedTitle))]
    [HarmonyPostfix]
    private static void EquipmentData_GetLocalisedTitle(EquipmentData __instance, ref string __result)
    {
        var weapon = Owner(__instance);
        if (weapon != null) __result = weapon.DisplayName;
    }

    [HarmonyPatch(typeof(EquipmentData), nameof(EquipmentData.GetLocalisedDescription))]
    [HarmonyPostfix]
    private static void EquipmentData_GetLocalisedDescription(EquipmentData __instance, ref string __result)
    {
        var weapon = Owner(__instance);
        if (weapon != null) __result = weapon.Config.Description ?? "";
    }

    [HarmonyPatch(typeof(EquipmentData), nameof(EquipmentData.GetLocalisedLore))]
    [HarmonyPostfix]
    private static void EquipmentData_GetLocalisedLore(EquipmentData __instance, ref string __result)
    {
        var weapon = Owner(__instance);
        if (weapon != null) __result = weapon.Config.Lore ?? "";
    }

    private static CustomWeapon Owner(EquipmentData data)
    {
        if (data == null || CustomWeapons.Count == 0) return null;
        var weapon = CustomWeapons.Get(data.EquipmentType);
        return weapon != null && ReferenceEquals(weapon.Data, data) ? weapon : null;
    }

    // Animation fallback and per-hit speed. Spine throws on an unknown animation name, so a
    // custom name is swapped for the base weapon's before the lookup on any skeleton that lacks
    // it; the entry the player's own attack starts gets the hit's playback speed.
    [HarmonyPatch(typeof(Spine.AnimationState), nameof(Spine.AnimationState.SetAnimation),
        typeof(int), typeof(string), typeof(bool))]
    [HarmonyPrefix]
    private static void AnimationState_SetAnimation_Prefix(Spine.AnimationState __instance,
        ref string animationName, ref string __state)
    {
        __state = animationName;
        CustomWeapons.Remap(__instance, ref animationName);
    }

    [HarmonyPatch(typeof(Spine.AnimationState), nameof(Spine.AnimationState.SetAnimation),
        typeof(int), typeof(string), typeof(bool))]
    [HarmonyPostfix]
    private static void AnimationState_SetAnimation_Postfix(Spine.AnimationState __instance,
        TrackEntry __result, string __state)
    {
        CustomWeapons.ApplyHitSpeed(__instance, __state, __result);
    }

    [HarmonyPatch(typeof(Spine.AnimationState), nameof(Spine.AnimationState.AddAnimation),
        typeof(int), typeof(string), typeof(bool), typeof(float))]
    [HarmonyPrefix]
    private static void AnimationState_AddAnimation_Prefix(Spine.AnimationState __instance, ref string animationName)
    {
        CustomWeapons.Remap(__instance, ref animationName);
    }
}
