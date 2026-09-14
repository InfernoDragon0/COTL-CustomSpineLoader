using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Spine;
using Spine.Unity;

namespace CustomSpineLoader.SpineLoaderHelper;

/// Hats and clothes for followers, donated by Spine exports in FollowerSpines/<pack>/. A chosen
/// skin is laid over the composite the game builds for a follower on every costume rebuild, the
/// way a custom fleece is laid over the lamb. Keys are "<pack>/<skin>".
public static class FollowerWardrobe
{
    public const string FolderName = "FollowerSpines";

    private sealed class Pack
    {
        public string Name;
        public string Folder;
        public string[] Hats = [];
        public string[] Clothes = [];
        public SkeletonDataAsset Asset;
        public bool Failed;
    }

    private static readonly Dictionary<string, Pack> Packs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, (string hat, string clothes)> Previews = [];
    private static readonly HashSet<string> Warned = [];

    // Which SkeletonGraphic owns a Skeleton. A SkeletonGraphic draws with one texture, so a follower
    // it shows wearing cross-atlas art has to be mirrored the way the player's UI skeletons are.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Skeleton, SkeletonGraphic> Graphics = new();

    public static void NoteGraphic(SkeletonGraphic graphic)
    {
        if (graphic == null || graphic.Skeleton == null) return;
        Graphics.Remove(graphic.Skeleton);
        Graphics.Add(graphic.Skeleton, graphic);
    }

    private static SkeletonGraphic GraphicOf(Skeleton skeleton) =>
        Graphics.TryGetValue(skeleton, out var graphic) && graphic != null ? graphic : null;

    public static void LoadAll()
    {
        var root = Path.Combine(Plugin.PluginPath, FolderName);
        if (!Directory.Exists(root)) Directory.CreateDirectory(root);

        foreach (var folder in APIHelper.ModContentPaths.DirectoriesIn(FolderName))
        {
            var pack = new Pack { Name = Path.GetFileName(folder), Folder = folder };
            var configPath = Path.Combine(folder, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var config = JsonConvert.DeserializeObject<FollowerSpineConfig>(File.ReadAllText(configPath));
                    pack.Hats = config?.Hats ?? [];
                    pack.Clothes = config?.Clothes ?? [];
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"{FolderName}/{pack.Name}: config.json unreadable ({e.Message}).");
                }
            }

            if (pack.Hats.Length == 0 && pack.Clothes.Length == 0)
            {
                Plugin.Log.LogWarning($"{FolderName}/{pack.Name}: config.json names no hats or clothes; skipped.");
                continue;
            }

            Packs[pack.Name] = pack;
        }

        if (Packs.Count > 0)
            Plugin.Log.LogInfo($"{Packs.Count} follower wardrobe pack(s): {HatKeys().Count} hat(s), " +
                               $"{ClothesKeys().Count} clothes; each loads when a follower first wears it.");
    }

    public static List<string> HatKeys() => Keys(p => p.Hats);
    public static List<string> ClothesKeys() => Keys(p => p.Clothes);

    private static List<string> Keys(Func<Pack, string[]> of)
    {
        var keys = new List<string>();
        foreach (var pack in Packs.Values)
            foreach (var skin in of(pack))
                if (!string.IsNullOrEmpty(skin)) keys.Add(pack.Name + "/" + skin);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    public static string Label(string key)
    {
        if (string.IsNullOrEmpty(key)) return "None";
        var slash = key.IndexOf('/');
        return slash > 0 ? key.Substring(0, slash) + ": " + key.Substring(slash + 1) : key;
    }

    // ---- what a follower wears ---------------------------------------------------------------

    /// A look the customise menu is trying on; consulted before the saved record until EndPreview.
    public static void Preview(int followerId, string hat, string clothes) => Previews[followerId] = (hat, clothes);
    public static void EndPreview(int followerId) => Previews.Remove(followerId);

    public static (string hat, string clothes) LookFor(int followerId)
    {
        if (Previews.TryGetValue(followerId, out var preview)) return preview;
        // Part of the costume override: off when that is, like the hat and clothing types beside it.
        var record = CustomColorHelper.GetCustomColor(followerId);
        return record is { CustomFollowerCostume: true } ? (record.CustomHat, record.CustomClothes) : (null, null);
    }

    /// Runs after the game has composed and set a follower's skin. Custom clothes go on, then the
    /// game's own layers that belong above clothes go back on top, then the custom hat.
    public static void Dress(Skeleton skeleton, FollowerInfo info, int level, FollowerHatType hat,
        FollowerCustomisationType customisation, InventoryItem.ITEM_TYPE necklace)
    {
        if (skeleton == null || info == null || Packs.Count == 0) return;

        var graphic = GraphicOf(skeleton);
        var (hatKey, clothesKey) = LookFor(info.ID);
        if (string.IsNullOrEmpty(hatKey) && string.IsNullOrEmpty(clothesKey))
        {
            // A portrait that showed a dressed follower last time goes back to drawing itself.
            if (graphic != null) graphic.GetComponent<PlayerUiMirror>()?.Detach();
            return;
        }

        var composite = skeleton.Skin;
        if (composite == null || IsDataSkin(skeleton, composite)) return;

        var touched = new HashSet<int>();
        if (Resolve(clothesKey, skeleton, out var clothes, out var clothesData))
        {
            Overlay(skeleton, composite, clothes, clothesData, touched);
            AddGameLayer(skeleton, composite, NecklaceName(necklace, info), touched);
            if (hat != FollowerHatType.None) AddGameLayer(skeleton, composite, HatName(hat, level), touched);
            if (customisation != FollowerCustomisationType.None)
                AddGameLayer(skeleton, composite, CustomisationName(customisation), touched);
        }

        if (Resolve(hatKey, skeleton, out var hatSkin, out var hatData))
            Overlay(skeleton, composite, hatSkin, hatData, touched);

        // Only the attachments are re-resolved. SetSlotsToSetupPose would do it too, but it also
        // resets every slot's colour, and the game has already tinted the follower by now.
        foreach (var index in touched)
        {
            var slot = skeleton.Slots.Items[index];
            var name = slot.Attachment?.Name ?? slot.Data.AttachmentName;
            if (name == null) continue;
            slot.Attachment = composite.GetAttachment(index, name) ?? composite.GetAttachment(index, slot.Data.AttachmentName ?? name);
        }

        if (touched.Count > 0 && graphic != null)
        {
            var mirror = graphic.GetComponent<PlayerUiMirror>() ?? graphic.gameObject.AddComponent<PlayerUiMirror>();
            mirror.Attach(graphic, graphic.skeletonDataAsset, graphic.skeletonDataAsset, hatKey ?? clothesKey, "follower portrait");
        }
    }

    private static bool Resolve(string key, Skeleton target, out Skin skin, out SkeletonData data)
    {
        skin = null;
        data = null;
        if (string.IsNullOrEmpty(key)) return false;

        var slash = key.IndexOf('/');
        if (slash <= 0 || !Packs.TryGetValue(key.Substring(0, slash), out var pack))
        {
            WarnOnce(key, "no such wardrobe pack is installed");
            return false;
        }

        if (!EnsureLoaded(pack, target)) return false;

        data = pack.Asset.GetSkeletonData(false);
        skin = data?.FindSkin(key.Substring(slash + 1));
        if (skin != null) return true;

        WarnOnce(key, "the pack has no skin of that name");
        return false;
    }

    private static bool EnsureLoaded(Pack pack, Skeleton target)
    {
        if (pack.Asset != null) return true;
        if (pack.Failed) return false;

        var watch = Stopwatch.StartNew();
        var game = WorshipperData.Instance != null ? WorshipperData.Instance.SkeletonData : null;
        var scale = game != null && game.skeletonDataAsset != null
            ? game.skeletonDataAsset.scale : SpineFolderLoader.DefaultScale;

        pack.Asset = SpineFolderLoader.Build(pack.Folder, FolderName + "/" + pack.Name, scale: scale);
        // Build parses quietly; asking again out loud is what puts the Spine error in the log.
        var data = pack.Asset?.GetSkeletonData(false);
        if (data == null)
        {
            pack.Failed = true;
            Plugin.Log.LogWarning(pack.Asset == null
                ? $"{FolderName}/{pack.Name} did not load: the folder needs one .json, one .atlas and at least one .png."
                : $"{FolderName}/{pack.Name} did not parse; see the Spine error above it. A blank first line " +
                  "missing from the .atlas, or a region the atlas lacks, are the usual causes.");
            pack.Asset = null;
            return false;
        }

        if (!BonesLineUp(data, target.Data, out var why))
        {
            pack.Failed = true;
            pack.Asset = null;
            Plugin.Log.LogWarning($"{FolderName}/{pack.Name} refused: {why}. Export it from the " +
                                  "follower rig without deleting, adding or reordering bones.");
            return false;
        }

        Plugin.Log.LogInfo($"TIMING {FolderName}/{pack.Name}: loaded in {watch.ElapsedMilliseconds}ms " +
                           $"at scale {scale}.");
        return true;
    }

    /// Weighted meshes index the bone list, so the donor's must be the game's, in order. A shorter
    /// list is fine; a longer or reordered one would deform or throw.
    private static bool BonesLineUp(SkeletonData donor, SkeletonData game, out string why)
    {
        why = null;
        if (donor.Bones.Count > game.Bones.Count)
        {
            why = $"it has {donor.Bones.Count} bones, the follower rig has {game.Bones.Count}";
            return false;
        }

        for (var i = 0; i < donor.Bones.Count; i++)
        {
            if (donor.Bones.Items[i].Name == game.Bones.Items[i].Name) continue;
            why = $"bone {i} is '{donor.Bones.Items[i].Name}', the follower rig has '{game.Bones.Items[i].Name}' there";
            return false;
        }

        return true;
    }

    private static void Overlay(Skeleton skeleton, Skin composite, Skin donor, SkeletonData donorData, HashSet<int> touched)
    {
        var targets = new Dictionary<int, int>();
        var perSlot = new Dictionary<int, int>();

        foreach (var entry in donor.Attachments)
        {
            if (!targets.TryGetValue(entry.SlotIndex, out var target))
            {
                var slotName = donorData.Slots.Items[entry.SlotIndex].Name;
                target = skeleton.FindSlotIndex(slotName);
                targets[entry.SlotIndex] = target;
                if (target < 0) WarnOnce(donor.Name + "/" + slotName, "the follower rig has no such slot");
            }
            if (target < 0) continue;

            composite.SetAttachment(target, entry.Name, entry.Attachment);
            touched.Add(target);
            perSlot[entry.SlotIndex] = perSlot.TryGetValue(entry.SlotIndex, out var n) ? n + 1 : 1;
        }

        // A slot shows only the attachment its setup pose or animations name. A hand-made skin
        // that put one thing on a slot under its own name is taken under the slot's name too.
        foreach (var entry in donor.Attachments)
        {
            if (perSlot[entry.SlotIndex] != 1) continue;
            var target = targets[entry.SlotIndex];
            if (target < 0) continue;

            var setupName = skeleton.Data.Slots.Items[target].AttachmentName;
            if (!string.IsNullOrEmpty(setupName) && setupName != entry.Name)
                composite.SetAttachment(target, setupName, entry.Attachment);
        }
    }

    private static void AddGameLayer(Skeleton skeleton, Skin composite, string skinName, HashSet<int> touched)
    {
        if (string.IsNullOrEmpty(skinName)) return;
        var layer = skeleton.Data.FindSkin(skinName);
        if (layer == null) return;
        composite.AddSkin(layer);
        foreach (var entry in layer.Attachments) touched.Add(entry.SlotIndex);
    }

    private static bool IsDataSkin(Skeleton skeleton, Skin skin)
    {
        var skins = skeleton.Data?.Skins;
        if (skins == null) return false;
        foreach (var candidate in skins)
            if (ReferenceEquals(candidate, skin)) return true;
        return false;
    }

    private static void WarnOnce(string what, string why)
    {
        if (Warned.Add(what)) Plugin.Log.LogWarning($"Follower wardrobe: {what} not worn; {why}.");
    }

    // ---- the game's own layer names (private helpers on FollowerBrain) --------------------------

    private static readonly Func<FollowerHatType, int, string> HatName =
        AccessTools.MethodDelegate<Func<FollowerHatType, int, string>>(
            AccessTools.Method(typeof(FollowerBrain), "GetHatName"));

    private static readonly Func<InventoryItem.ITEM_TYPE, string> NecklaceOf =
        AccessTools.MethodDelegate<Func<InventoryItem.ITEM_TYPE, string>>(
            AccessTools.Method(typeof(FollowerBrain), "GetNecklaceName"));

    private static readonly Func<FollowerCustomisationType, string> CustomisationName =
        AccessTools.MethodDelegate<Func<FollowerCustomisationType, string>>(
            AccessTools.Method(typeof(FollowerBrain), "GetCustomisationName"));

    private static string NecklaceName(InventoryItem.ITEM_TYPE necklace, FollowerInfo info)
    {
        if (necklace != InventoryItem.ITEM_TYPE.NONE) return NecklaceOf(necklace);
        return info.BornInCult ? "Necklaces/Necklace_Bell" : null;
    }
}

/// config.json of a FollowerSpines pack: which of its skins are hats and which are clothes.
public class FollowerSpineConfig
{
    public string[] Hats { get; set; } = [];
    public string[] Clothes { get; set; } = [];
}
