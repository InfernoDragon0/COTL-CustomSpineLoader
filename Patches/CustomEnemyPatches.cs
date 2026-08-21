using CustomSpineLoader.APIHelper;
using COTL_API.CustomEnemy;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class CustomEnemyPatches
    {
        // Every route that spawns a custom enemy goes through here - the map editor's enemy tool,
        // the blueprint loader replaying a saved room, and a custom dungeon's own SpawnEnemies -
        // so it is the one place a JSON enemy can be finished off without each caller
        // remembering to.
        [HarmonyPatch(typeof(CustomEnemyManager), nameof(CustomEnemyManager.Spawn))]
        [HarmonyPostfix]
        private static void CustomEnemyManager_Spawn(Enemy enemyType, UnitObject __result)
        {
            if (__result == null) return;
            CustomEnemyDressing.Apply(enemyType, __result);
        }
    }
}
