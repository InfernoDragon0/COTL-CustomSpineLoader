using CustomSpineLoader.SpineLoaderHelper;
using HarmonyLib;
using UnityEngine;

namespace CustomSpineLoader.Patches
{
    [HarmonyPatch]
    public class StructureSpinePatches
    {
        // Where a custom structure's skeleton goes on. PlaceStructure is the one point both
        // routes meet: StructureManager.BuildStructure calls it for a structure the player has
        // just built, and LocationManager.InstantiateStructureAsync calls it for every structure
        // restored when a location loads. It also runs after COTL_API's own sprite swap, which
        // is registered on the addressables handle before the game's callback.
        [HarmonyPatch(typeof(LocationManager), nameof(LocationManager.PlaceStructure))]
        [HarmonyPostfix]
        private static void LocationManager_PlaceStructure(StructuresData structure, GameObject g)
        {
            if (structure == null) return;
            StructureSpineHelper.TryAttach(g, structure.Type);
        }
    }
}
