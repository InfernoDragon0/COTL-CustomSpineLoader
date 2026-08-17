using System;
using System.Collections.Generic;
using System.Linq;
using COTL_API.CustomStructures;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.BuildMenu;
using TMPro;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

[HarmonyPatch]
public static class MapAssetsTab
{
    public static bool ForceUnlockAll;

    private static AestheticCategory _ourCategory;
    private static bool _injectionFailed;

    public static bool IsOurCategory(AestheticCategory category) =>
        _ourCategory != null && ReferenceEquals(category, _ourCategory);

    // Every structure type known to the game plus everything COTL_API registered at runtime.
    // Runtime-minted enum values do not appear in Enum.GetValues, hence the union.
    public static List<StructureBrain.TYPES> BuildCatalog()
    {
        var all = new HashSet<StructureBrain.TYPES>();

        foreach (StructureBrain.TYPES t in Enum.GetValues(typeof(StructureBrain.TYPES)))
            if (t != StructureBrain.TYPES.NONE) all.Add(t);

        foreach (var t in CustomStructureManager.CustomStructureList.Keys)
            all.Add(t);

        return all.OrderBy(t => t.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool Inject(UIBuildMenuController menu)
    {
        if (menu == null || _injectionFailed) return false;
        if (_ourCategory != null) return true;

        try
        {
            var source = menu._aestheticCategory;
            var navigator = menu._tabNavigatorBase;
            if (source == null || navigator == null || navigator._tabs == null || navigator._tabs.Length == 0)
            {
                Plugin.Log.LogWarning("MapEditor: build menu structure not as expected, skipping Map Assets tab.");
                _injectionFailed = true;
                return false;
            }

            // Instantiate under an inactive holder so Awake does not run until the clones are
            // wired to each other; otherwise MMTab.Awake binds the clone to the original menu.
            var holder = new GameObject("CultTweaker_TabHolder");
            holder.SetActive(false);
            holder.transform.SetParent(source.transform.parent, false);

            var category = UnityEngine.Object.Instantiate(source, holder.transform);
            category.name = "MapAssetsCategory";
            _ourCategory = category;

            var sourceTab = navigator._tabs[navigator._tabs.Length - 1];
            var tab = UnityEngine.Object.Instantiate(sourceTab, holder.transform);
            tab.name = "MapAssetsTab";
            tab._menu = category;

            // Move the clones into the real hierarchy and let them wake up.
            category.transform.SetParent(source.transform.parent, false);
            tab.transform.SetParent(sourceTab.transform.parent, false);
            holder.SetActive(true);
            UnityEngine.Object.Destroy(holder);

            RelabelTab(tab, "Map Assets");

            // Replicate what MMTabNavigatorBase.Start does for tabs that existed at startup.
            var tabs = new List<BuildMenuTab>(navigator._tabs) { tab };
            navigator._tabs = tabs.ToArray();
            tab.Configure();
            tab.OnTabPressed += () => navigator.TransitionTo(tab);

            category.OnBuildingChosen += type => menu.OnBuildingChosen?.Invoke(type);

            Plugin.Log.LogInfo("MapEditor: Map Assets tab injected into the build menu.");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: could not inject Map Assets tab, using vanilla menu only: " + e.Message);
            _injectionFailed = true;
            _ourCategory = null;
            return false;
        }
    }

    private static void RelabelTab(BuildMenuTab tab, string label)
    {
        foreach (var text in tab.GetComponentsInChildren<TMP_Text>(true))
            text.text = label;

        if (tab.Alert != null) tab.Alert.SetActive(false);
    }

    // Fills our cloned page with the full catalog instead of the aesthetic content.
    public static void PopulateMapAssets(AestheticCategory category)
    {
        // Hide the section headers and sibling containers we are not using.
        HideIfPresent(category._dlcHeader, category._majorDlcHeader, category._majorDlcWoolhavenHeader,
            category._majorDlcEwefallHeader, category._majorDlcRotHeader, category._specialEventsHeader);

        var container = category._miscContent;
        if (container == null)
        {
            Plugin.Log.LogWarning("MapEditor: Map Assets tab has no content container.");
            return;
        }

        if (category._miscUnlocked != null) category._miscUnlocked.text = "";

        var catalog = BuildCatalog();

        var previousUnlock = ForceUnlockAll;
        var previousFree = CheatConsole.BuildingsFree;
        ForceUnlockAll = true;
        CheatConsole.BuildingsFree = true;
        try
        {
            category.Populate(catalog, container);
        }
        finally
        {
            ForceUnlockAll = previousUnlock;
            CheatConsole.BuildingsFree = previousFree;
        }

        Plugin.Log.LogInfo($"MapEditor: Map Assets tab populated with {catalog.Count} structure type(s).");
    }

    private static void HideIfPresent(params GameObject[] objects)
    {
        foreach (var go in objects)
            if (go != null) go.SetActive(false);
    }

    // --- Harmony patches -------------------------------------------------------------------

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AestheticCategory), "Populate", new Type[0])]
    private static bool AestheticCategory_Populate(AestheticCategory __instance)
    {
        if (!IsOurCategory(__instance)) return true;
        PopulateMapAssets(__instance);
        return false;
    }

    // The gates that decide whether an entry is listed at all and whether it is clickable.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(StructuresData), nameof(StructuresData.GetUnlocked))]
    private static void StructuresData_GetUnlocked(ref bool __result)
    {
        if (ForceUnlockAll) __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StructuresData), nameof(StructuresData.HiddenUntilUnlocked))]
    private static void StructuresData_HiddenUntilUnlocked(ref bool __result)
    {
        if (ForceUnlockAll) __result = false;
    }

    // These only greyed entries out, but a map builder should be able to place anything.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(StructuresData), nameof(StructuresData.RequiresTempleToBuild))]
    private static void StructuresData_RequiresTempleToBuild(ref bool __result)
    {
        if (ForceUnlockAll) __result = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StructuresData), nameof(StructuresData.RequiresRanchToBuild))]
    private static void StructuresData_RequiresRanchToBuild(ref bool __result)
    {
        if (ForceUnlockAll) __result = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StructuresData), nameof(StructuresData.GetBuildOnlyOne))]
    private static void StructuresData_GetBuildOnlyOne(ref bool __result)
    {
        if (ForceUnlockAll) __result = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StructuresData), nameof(StructuresData.IsUpgradeStructure))]
    private static void StructuresData_IsUpgradeStructure(ref bool __result)
    {
        if (ForceUnlockAll) __result = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BuildMenuItem), "CheckCanAfford")]
    private static void BuildMenuItem_CheckCanAfford(ref bool __result)
    {
        if (ForceUnlockAll) __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBuildMenuController), "Update")]
    private static void UIBuildMenuController_Update(UIBuildMenuController __instance)
    {
        if (ForceUnlockAll && __instance._editBuildingsText != null)
            __instance._editBuildingsText.SetActive(false);
    }
}
