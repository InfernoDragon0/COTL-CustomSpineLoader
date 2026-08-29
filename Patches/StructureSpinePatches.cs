using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class StructureSpinePatches
    {
        [HarmonyPatch(typeof(LocationManager), nameof(LocationManager.PlaceStructure))]
        [HarmonyPostfix]
        private static void LocationManager_PlaceStructure(StructuresData structure, GameObject g)
        {
            if (structure == null) return;
            StructureSpineHelper.TryAttach(g, structure.Type);
        }
    }
}
