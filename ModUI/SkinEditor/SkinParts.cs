using System;
using System.Collections.Generic;
using CustomSpineLoader.SpineLoaderHelper;
using Spine;

namespace CustomSpineLoader.ModUI.SkinEditor;

public static class SkinParts
{
    public static SkeletonData Data()
    {
        var asset = WorshipperData.Instance != null && WorshipperData.Instance.SkeletonData != null
            ? WorshipperData.Instance.SkeletonData.skeletonDataAsset
            : null;
        return asset != null ? asset.GetSkeletonData(true) : null;
    }

    public static List<string> BaseSkinNames()
    {
        var names = new List<string>();
        var data = Data();
        if (data == null || WorshipperData.Instance == null) return names;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var character in WorshipperData.Instance.Characters)
        {
            if (character?.Skin == null) continue;
            foreach (var skin in character.Skin)
                if (skin?.Skin != null && data.FindSkin(skin.Skin) != null && seen.Add(skin.Skin))
                    names.Add(skin.Skin);
        }

        if (names.Count == 0)
            foreach (var skin in data.Skins)
                if (skin != null && seen.Add(skin.Name)) names.Add(skin.Name);

        return names;
    }

    public static string SlotName(int slotIndex)
    {
        var data = Data();
        return data != null && slotIndex >= 0 && slotIndex < data.Slots.Count
            ? data.Slots.Items[slotIndex].Name
            : "?";
    }

    public static List<(int Slot, string Part, string Label)> Of(string baseSkinName)
    {
        var list = new List<(int, string, string)>();
        var data = Data();
        if (data == null) return list;

        var skin = data.FindSkin(baseSkinName ?? "Cat") ?? data.FindSkin("Cat");
        if (skin == null) return list;

        foreach (var entry in skin.Attachments)
            list.Add((entry.SlotIndex, entry.Name, entry.Name));

        list.Sort((a, b) =>
        {
            var bySlot = a.Item1.CompareTo(b.Item1);
            return bySlot != 0 ? bySlot : string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    public static string FreeKey(FollowerSkinConfig config, string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) stem = "layer";
        if (config?.PartConfigs == null || !config.PartConfigs.ContainsKey(stem)) return stem;

        for (var i = 2; i < 1000; i++)
            if (!config.PartConfigs.ContainsKey(stem + i)) return stem + i;

        return stem + Guid.NewGuid().ToString("N").Substring(0, 4);
    }

    public static bool Rekey(FollowerSkinConfig config, string oldKey, string newKey)
    {
        if (config?.PartConfigs == null) return false;
        if (oldKey == newKey || config.PartConfigs.ContainsKey(newKey)) return false;

        var rebuilt = new Dictionary<string, FollowerSkinPartConfig>();
        foreach (var pair in config.PartConfigs)
            rebuilt[pair.Key == oldKey ? newKey : pair.Key] = pair.Value;

        config.PartConfigs = rebuilt;
        return true;
    }
}
