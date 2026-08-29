using CustomSpineLoader.APIHelper;
using COTL_API.CustomEnemy;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class CustomEnemyPatches
    {
        [HarmonyPatch(typeof(CustomEnemyManager), nameof(CustomEnemyManager.Spawn))]
        [HarmonyPostfix]
        private static void CustomEnemyManager_Spawn(Enemy enemyType, UnitObject __result)
        {
            if (__result == null) return;
            CustomEnemyDressing.Apply(enemyType, __result);
        }
    }
}
