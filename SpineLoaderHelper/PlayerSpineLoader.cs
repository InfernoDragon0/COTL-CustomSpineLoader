using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using COTL_API;
using COTL_API.Helpers;
using COTL_API.CustomSkins;
using HarmonyLib;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Spine;
using Spine.Unity;

namespace CustomSpineLoader.SpineLoaderHelper;

public class PlayerSpineLoader
{
    public static List<string> FleeceRotation = []; //string of skin names that have fleeces
    public static Dictionary<string, Tuple<SkeletonDataAsset, List<string>>> FleeceCyclingSpines = []; //spineName: Skel and list of skin names

    public static Dictionary<string, PlayerSpineConfig> SpineConfigs = [];
    public static readonly int[] FleeceIndexes = [-1, -1, -1, -1];

    public static Action<int> LookChanged;

    private static void AnnounceLook(int playerId)
    {
        try
        {
            LookChanged?.Invoke(playerId);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("A look-changed listener threw: " + e.Message);
        }
    }

    public static int currentFleeceIndexP1 = -1;
    public static int currentFleeceIndexP2 = -1;

    public static string currentFleeceSpineNameP1 = "";
    public static string currentFleeceSpineNameP2 = "";

    public static bool LoadedCustomSpines = false;
    public static bool LoadedFleeceCycling = false;

    public static List<(string, string)> FleeceOverrideSlots = [ //(slot index, slot name)
        ("images/PonchoLeft", "PonchoLeft"),
        ("images/PonchoRight", "PonchoRight"),
        ("images/PonchoLeft", "PonchoLeft2"),
        ("images/PonchoRight", "PonchoRight2"),
        ("images/PonchoExtra", "PonchoExtra"),
        ("images/PonchoRightCorner2", "PonchoRightCorner"),
        ("images/PonchoRightCorner", "PonchoRightCorner"),
        ("images/PonchoShoulder", "PonchoShoulder"),
        ("images/PonchoShoulder2", "PonchoShoulder_Right"),
        ("RopeTopLeft", "images/RopeTopLeft"),
        ("RopeTopRight", "images/RopeTopRight"),
        ("images/Rope", "images/Rope"),
        ("images/Bell", "Bell"),
        ("images/Body", "Body")
    ]; //Tuple<string, string>

    public static int CycleNextFleece(int playerID)
    {
        if (FleeceRotation.Count == 0) return -1;
        if (playerID < 0 || playerID >= FleeceIndexes.Length) return -1;

        var result = FleeceIndexes[playerID] + 1;
        if (result >= FleeceRotation.Count || result < 0) result = 0;

        FleeceIndexes[playerID] = result;

        if (playerID == 0) currentFleeceIndexP1 = result;
        else if (playerID == 1) currentFleeceIndexP2 = result;

        Plugin.Log.LogInfo("Player " + (playerID + 1) + " cycled to fleece index " + result + " (" + FleeceRotation[result] + ")");

        return result;
    }

    // ---- deferred skeleton parsing --------------------------------------------------------------

    private const float SkeletonScale = 0.005f;

    private sealed class WarmUpJob
    {
        public string Name;
        public SkeletonDataAsset Asset;
        public Atlas Atlas;
        public string Json;
        public SkeletonData Parsed;
        public string Error;
        public long ParseMs;
        public long ParseBytes;
    }

    private static readonly object WarmLock = new();
    private static readonly Queue<WarmUpJob> WarmPending = new();
    private static readonly Queue<WarmUpJob> WarmFinished = new();
    private static Thread _warmThread;
    private static Stopwatch _warmWatch;
    private static int _warmQueued;
    private static int _warmApplied;

    private static void QueueWarmUp(string name, SkeletonDataAsset asset, SpineAtlasAsset atlasAsset, string json)
    {
        if (asset == null || atlasAsset == null || string.IsNullOrEmpty(json)) return;

        Atlas atlas;
        try
        {
            atlas = atlasAsset.GetAtlas();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"{name}: atlas unavailable, its skeleton will parse when first worn ({e.Message}).");
            return;
        }

        if (atlas == null) return;

        lock (WarmLock)
        {
            WarmPending.Enqueue(new WarmUpJob { Name = name, Asset = asset, Atlas = atlas, Json = json });
            _warmQueued++;

            _warmDrained = false;
        }
    }

    private static void StartWarmUp()
    {
        lock (WarmLock)
        {
            if (_warmThread != null || WarmPending.Count == 0) return;

            _warmWatch = Stopwatch.StartNew();
            _warmThread = new Thread(WarmUpLoop)
            {
                Name = "CultTweaker spine warm-up",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.BelowNormal
            };
            _warmThread.Start();
        }
    }

    private static void WarmUpLoop()
    {
        while (true)
        {
            WarmUpJob job;
            lock (WarmLock)
            {
                if (WarmPending.Count == 0)
                {
                    _warmThread = null;
                    return;
                }

                job = WarmPending.Dequeue();
            }

            var watch = Stopwatch.StartNew();

            var heapBefore = GC.GetTotalMemory(false);
            try
            {
                var reader = new SkeletonJson(new AtlasAttachmentLoader(job.Atlas)) { Scale = SkeletonScale };
                job.Parsed = reader.ReadSkeletonData(new StringReader(job.Json));
            }
            catch (Exception e)
            {
                job.Error = e.Message;
            }

            job.ParseMs = watch.ElapsedMilliseconds;
            job.ParseBytes = GC.GetTotalMemory(false) - heapBefore;
            job.Json = null;

            lock (WarmLock) WarmFinished.Enqueue(job);
        }
    }

    private static bool _warmDrained;

    public static void PumpWarmUp()
    {
        if (_warmDrained) return;

        while (true)
        {
            WarmUpJob job;
            lock (WarmLock)
            {
                if (WarmFinished.Count == 0)
                {
                    if (_warmApplied >= _warmQueued) _warmDrained = true;
                    return;
                }
                job = WarmFinished.Dequeue();
            }

            ApplyWarmUp(job);
        }
    }

    private static void ApplyWarmUp(WarmUpJob job)
    {
        _warmApplied++;

        if (job.Parsed == null)
        {
            Plugin.Log.LogWarning($"Warm-up failed for {job.Name} ({job.Error}); it will parse when first worn.");
        }
        else if (job.Asset == null)
        {
            Plugin.Log.LogInfo($"{job.Name} went away before its warm-up landed.");
        }
        else if (job.Asset.skeletonData != null)
        {
            Plugin.Log.LogInfo($"{job.Name} was already parsed on demand; warm-up result dropped.");
            ReleaseJson(job.Asset);
        }
        else
        {
            try
            {
                job.Asset.skeletonData = job.Parsed;

                job.Asset.stateData = new AnimationStateData(job.Parsed);
                job.Asset.FillStateData();

                if (job.Asset.GetSkeletonData(true) == null || job.Asset.GetAnimationStateData() == null)
                    throw new Exception("the asset was still incomplete afterwards");

                Plugin.Log.LogInfo($"Warmed {job.Name} in {job.ParseMs}ms " +
                                   $"(~{job.ParseBytes / 1048576f:F0}MB of parsed skeleton data).");
                ReleaseJson(job.Asset);
            }
            catch (Exception e)
            {
                job.Asset.skeletonData = null;
                job.Asset.stateData = null;

                Plugin.Log.LogWarning($"Could not store the warmed skeleton for {job.Name} ({e.Message}); " +
                                      "it will parse when first worn.");
            }
        }

        if (_warmApplied < _warmQueued) return;

        _warmWatch?.Stop();
        Plugin.Log.LogWarning($"TIMING WARM-UP: {_warmApplied} skeleton(s) parsed off the main thread in " +
                              $"{_warmWatch?.ElapsedMilliseconds ?? 0}ms. Managed heap now " +
                              $"{GC.GetTotalMemory(false) / 1048576f:F0}MB; Unity native " +
                              $"{UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:F0}MB " +
                              $"used of {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576f:F0}MB reserved.");
    }

    private static void ReleaseJson(SkeletonDataAsset asset)
    {
        if (asset == null || asset.skeletonData == null || asset.skeletonJSON == null) return;

        var json = asset.skeletonJSON;
        asset.skeletonJSON = SpineFolderLoader.PlaceholderJson();
        UnityEngine.Object.Destroy(json);
        SpineFolderLoader.MarkJsonFreed(asset);
    }

    // ---- active spine -------------------------------------------------------------------------

    public static string ActiveSpineKey(int playerId)
    {
        try
        {
            return Traverse.Create(typeof(CustomSkinManager))
                .Field(playerId == 1 ? "SelectedSpine2" : "SelectedSpine")
                .GetValue<string>() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static string ActiveSpineName(int playerId) => SpineNameFromKey(ActiveSpineKey(playerId));

    public static string SpineNameFromKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";

        var slash = key.IndexOf('/');
        return slash < 0 ? key : key.Substring(0, slash);
    }

    public static string RememberedSpineKey(int playerId) =>
        (playerId == 0 ? Plugin.SelectedSpineP1?.Value : Plugin.SelectedSpineP2?.Value) ?? "";

    public static PlayerSpineConfig ConfigFor(int playerId)
    {
        var name = ActiveSpineName(playerId);
        if (string.IsNullOrEmpty(name)) return null;

        return SpineConfigs.TryGetValue(name, out var config) ? config : null;
    }

    // ---- hidden slots -------------------------------------------------------------------------

    public static void HideSlots(SkeletonAnimation spine, PlayerSpineConfig config)
    {
        if (spine == null || spine.Skeleton == null) return;
        if (config?.HiddenSlots == null || config.HiddenSlots.Length == 0) return;

        var skin = spine.Skeleton.Skin;
        if (skin == null) return;

        if (IsDataSkin(spine, skin))
        {
            Plugin.Log.LogWarning("Slots not hidden: the player is wearing a skin straight from the " +
                                  "spine file, which must not be edited. The next skin rebuild will hide them.");
            return;
        }

        foreach (var slotName in config.HiddenSlots)
        {
            if (string.IsNullOrEmpty(slotName)) continue;

            var slotIndex = spine.Skeleton.FindSlotIndex(slotName);
            if (slotIndex < 0)
            {
                Plugin.Log.LogWarning("Hidden slot not found on this skeleton: " + slotName);
                continue;
            }

            foreach (var entry in skin.Attachments.ToList())
            {
                if (entry.SlotIndex != slotIndex) continue;

                var blanked = Blank(entry.Attachment);
                if (blanked == null)
                {
                    Plugin.Log.LogWarning($"Cannot hide {entry.Name} on {slotName}: " +
                                          $"{entry.Attachment?.GetType().Name} has no colour to clear.");
                    continue;
                }

                skin.SetAttachment(slotIndex, entry.Name, blanked);
            }

            spine.Skeleton.SetAttachment(slotName, null);
        }
    }

    private static Attachment Blank(Attachment attachment)
    {
        switch (attachment?.Copy())
        {
            case RegionAttachment region:
                region.A = 0f;
                return region;

            case MeshAttachment mesh:
                mesh.A = 0f;
                return mesh;

            default:
                return null;
        }
    }

    private static bool IsDataSkin(SkeletonAnimation spine, Skin skin)
    {
        var skins = spine.Skeleton.Data?.Skins;
        if (skins == null) return false;

        foreach (var candidate in skins)
            if (ReferenceEquals(candidate, skin))
                return true;

        return false;
    }

    private static HashSet<string> HiddenSlotNames(PlayerSpineConfig config)
    {
        if (config?.HiddenSlots == null || config.HiddenSlots.Length == 0) return null;
        return [.. config.HiddenSlots];
    }

    // ---- fleece application -------------------------------------------------------------------

    public static Skin ResolveFleeceSkin(string fleeceSkinName, SkeletonAnimation targetSpine)
    {
        if (string.IsNullOrEmpty(fleeceSkinName) || targetSpine == null) return null;

        if (!fleeceSkinName.Contains("CultTweaker_"))
            return targetSpine.Skeleton.Data.FindSkin(fleeceSkinName);

        var split = fleeceSkinName.Split(['_'], count: 3);
        if (split.Length < 3)
        {
            Plugin.Log.LogWarning("Invalid custom fleece skin name: " + fleeceSkinName);
            return null;
        }

        var spineName = split[1];
        if (!FleeceCyclingSpines.ContainsKey(spineName))
        {
            Plugin.Log.LogWarning("Invalid spine skin name: " + fleeceSkinName + " for spine: " + spineName);
            return null;
        }

        var skeletonData = FleeceCyclingSpines[spineName].Item1.GetSkeletonData(false);
        var skin = skeletonData != null ? skeletonData.FindSkin(split[2]) : null;
        if (skin != null) return skin;

        Plugin.Log.LogWarning("Defaulting to default as Custom Fleece skin not found: " + fleeceSkinName);
        return targetSpine.Skeleton.Data.FindSkin("Lamb");
    }

    public static void ApplyFleeceAttachments(SkeletonAnimation spine, Skin fleeceSkin,
        PlayerSpineConfig config = null)
    {
        if (spine == null || fleeceSkin == null) return;

        var hidden = HiddenSlotNames(config);
        var currentSkin = spine.Skeleton.Skin;

        foreach (var slot in FleeceOverrideSlots)
        {
            var slotIndex = spine.Skeleton.FindSlotIndex(slot.Item1);
            var attachment = hidden != null && hidden.Contains(slot.Item1)
                ? null
                : fleeceSkin.GetAttachment(slotIndex, slot.Item2);

            if (attachment == null)
                currentSkin.RemoveAttachment(slotIndex, slot.Item2);
            else
                currentSkin.SetAttachment(slotIndex, slot.Item2, attachment);
        }

        HideSlots(spine, config);

        spine.Skeleton.SetSlotsToSetupPose();
        spine.Update(0);
    }

    public static PlayerFarming ResolvePlayer(int playerId)
    {
        var players = PlayerFarming.players;
        if (players != null && playerId >= 0 && playerId < players.Count && players[playerId] != null)
            return players[playerId];

        return playerId == 0 ? PlayerFarming.Instance : null;
    }

    public static int GetFleeceIndex(int playerId) =>
        playerId >= 0 && playerId < FleeceIndexes.Length ? FleeceIndexes[playerId] : -1;

    public static bool ApplyFleece(int playerId, int fleeceIndex, bool persist = true)
    {
        if (fleeceIndex < 0 || fleeceIndex >= FleeceRotation.Count)
        {
            Plugin.Log.LogWarning($"Fleece index {fleeceIndex} is out of range (0-{FleeceRotation.Count - 1}).");
            return false;
        }

        var player = ResolvePlayer(playerId);
        if (player == null || player.Spine == null)
        {
            Plugin.Log.LogInfo($"Player {playerId + 1} is not in the game; no fleece applied.");
            return false;
        }

        var fleeceSkinName = FleeceRotation[fleeceIndex];
        var config = ConfigFor(playerId);

        if (persist) RememberFleece(playerId, fleeceIndex);

        if (config != null && config.DisableFleeceCycling)
        {
            HideSlots(player.Spine, config);
            Plugin.Log.LogInfo($"{ActiveSpineName(playerId)} keeps its own fleece; " +
                               $"{fleeceSkinName} remembered for player {playerId + 1} only.");

            AnnounceLook(playerId);
            return false;
        }

        var fleeceSpineName = SpineNameFromFleece(fleeceSkinName);
        if (fleeceSpineName != null && !FleeceCyclingSpines.ContainsKey(fleeceSpineName) &&
            Registry.ContainsKey(fleeceSpineName))
        {
            var id = playerId;
            var index = fleeceIndex;
            EnsureLoaded(fleeceSpineName, () => ApplyFleece(id, index, persist: false));
            return false;
        }

        var fleeceSkin = ResolveFleeceSkin(fleeceSkinName, player.Spine);
        if (fleeceSkin == null)
        {
            Plugin.Log.LogWarning("Fleece skin could not be resolved: " + fleeceSkinName);
            return false;
        }

        ApplyFleeceAttachments(player.Spine, fleeceSkin, config);

        Plugin.Log.LogInfo($"Player {playerId + 1} is wearing {fleeceSkinName}.");

        AnnounceLook(playerId);
        return true;
    }

    private static void RememberFleece(int playerId, int fleeceIndex)
    {
        if (playerId >= 0 && playerId < FleeceIndexes.Length) FleeceIndexes[playerId] = fleeceIndex;

        switch (playerId)
        {
            case 0:
                currentFleeceIndexP1 = fleeceIndex;
                Plugin.CurrentFleeceIndexP1.Value = fleeceIndex;
                if (Plugin.CurrentFleeceNameP1 != null)
                    Plugin.CurrentFleeceNameP1.Value = FleeceRotation[fleeceIndex];
                break;
            case 1:
                currentFleeceIndexP2 = fleeceIndex;
                Plugin.CurrentFleeceIndexP2.Value = fleeceIndex;
                if (Plugin.CurrentFleeceNameP2 != null)
                    Plugin.CurrentFleeceNameP2.Value = FleeceRotation[fleeceIndex];
                break;
        }
    }
    // ---- lazy loading -------------------------------------------------------------------------

    private enum SpineState { NotLoaded, Loading, Ready }

    private sealed class SpineEntry
    {
        public string Name;
        public string Folder;
        public PlayerSpineConfig Config;
        public string DefaultSkin = "Lamb";
        public string[] Skins = [];
        public bool IsFleece;
        public SpineState State;
        public SkeletonDataAsset Asset;
        public readonly List<Action> OnReady = [];
    }

    private static readonly Dictionary<string, SpineEntry> Registry = new(StringComparer.OrdinalIgnoreCase);

    private static SpineEntry FindEntry(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Registry.TryGetValue(name, out var entry)) return entry;

        var slash = name.IndexOf('/');
        return slash > 0 && Registry.TryGetValue(name.Substring(0, slash), out entry) ? entry : null;
    }

    public static bool IsPreparing(string name)
    {
        var entry = FindEntry(name);
        return entry != null && entry.State == SpineState.Loading;
    }

    public static bool IsLoaded(string name)
    {
        var entry = FindEntry(name);
        return entry != null && entry.State == SpineState.Ready;
    }

    public static List<string> RegisteredSpineNames()
    {
        var names = new List<string>();
        foreach (var entry in Registry.Values)
        {
            if (entry.IsFleece) continue;

            var skins = entry.Skins is { Length: > 0 } ? entry.Skins : [entry.DefaultSkin];
            foreach (var skin in skins)
                names.Add(entry.Name.Replace("/", "") + "/" + skin);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static IEnumerable<(string Name, string[] Skins)> FleeceCycleEntries()
    {
        foreach (var entry in Registry.Values)
            if (entry.IsFleece) yield return (entry.Name, entry.Skins);
    }

    public static string SpineNameFromFleece(string fleeceSkinName)
    {
        if (string.IsNullOrEmpty(fleeceSkinName) || !fleeceSkinName.Contains("CultTweaker_")) return null;
        var split = fleeceSkinName.Split(['_'], count: 3);
        return split.Length < 3 ? null : split[1];
    }

    public static void EnsureLoaded(string name, Action onReady = null, bool announce = true)
    {
        var entry = FindEntry(name);
        if (entry == null || entry.State == SpineState.Ready)
        {
            onReady?.Invoke();
            return;
        }

        if (onReady != null) entry.OnReady.Add(onReady);
        if (entry.State == SpineState.Loading) return;

        if (Plugin.Instance == null)
        {
            LoadEntryNow(entry, null);
            entry.State = SpineState.Ready;
            FireCallbacks(entry);
            return;
        }

        entry.State = SpineState.Loading;
        Plugin.Instance.StartCoroutine(LoadRoutine(entry, announce));
    }

    public static void RememberSpine(int playerId, string spineKey)
    {
        switch (playerId)
        {
            case 0:
                if (Plugin.SelectedSpineP1 != null) Plugin.SelectedSpineP1.Value = spineKey ?? "";
                break;
            case 1:
                if (Plugin.SelectedSpineP2 != null) Plugin.SelectedSpineP2.Value = spineKey ?? "";
                break;
        }
    }

    private static void RestoreSelectedSpine(int playerId)
    {
        var saved = RememberedSpineKey(playerId);
        if (string.IsNullOrEmpty(saved)) return;

        if (!string.IsNullOrEmpty(ActiveSpineKey(playerId))) return;

        var name = SpineNameFromKey(saved);
        if (!string.IsNullOrEmpty(name) && Registry.ContainsKey(name) && !IsLoaded(name))
        {
            EnsureLoaded(name, () => ApplySavedSpine(playerId, saved));
            return;
        }

        ApplySavedSpine(playerId, saved);
    }

    private static void ApplySavedSpine(int playerId, string saved)
    {
        try
        {
            CustomSkinManager.ChangeSelectedPlayerSpine(saved, playerId);

            var actual = ActiveSpineKey(playerId);
            if (string.Equals(actual, saved, StringComparison.Ordinal))
            {
                Plugin.Log.LogInfo($"Player {playerId + 1} spine restored to {saved}.");
                ResolvePlayer(playerId)?.SetSkin();
            }
            else
            {
                Plugin.Log.LogWarning($"Player {playerId + 1}: spine '{saved}' was not accepted - the " +
                                      $"selection is '{(string.IsNullOrEmpty(actual) ? "<none>" : actual)}'. " +
                                      "It is most likely not registered under that name.");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not restore player {playerId + 1}'s spine " +
                                  $"'{saved}': {e.Message}");
        }
    }

    public static void EnsureSelectedLoaded()
    {
        for (var playerId = 0; playerId < 2; playerId++)
        {
            var id = playerId;

            RestoreSelectedSpine(playerId);

            var spine = ActiveSpineName(playerId);
            if (!string.IsNullOrEmpty(spine) && Registry.ContainsKey(spine) && !IsLoaded(spine))
                EnsureLoaded(spine, () => ResolvePlayer(id)?.SetSkin());

            var fleece = SpineNameFromFleece(playerId == 0
                ? Plugin.CurrentFleeceNameP1?.Value : Plugin.CurrentFleeceNameP2?.Value);
            if (fleece != null && Registry.ContainsKey(fleece) && !IsLoaded(fleece))
                EnsureLoaded(fleece, () =>
                {
                    var index = GetFleeceIndex(id);
                    if (index >= 0) ApplyFleece(id, index, persist: false);
                });
        }
    }

    public static void LoadAllPlayerSpines(Material material = null)
    {
        if (LoadedCustomSpines)
        {
            Plugin.Log.LogInfo("Load Player Spines was called again but already loaded!");
            return;
        }

        var playerFolder = Path.Combine(Plugin.PluginPath, "PlayerSkins");
        if (!Directory.Exists(playerFolder))
            Directory.CreateDirectory(playerFolder);

        foreach (var folder in APIHelper.ModContentPaths.DirectoriesIn("PlayerSkins"))
        {
            var name = Path.GetFileName(folder);
            var entry = new SpineEntry { Name = name, Folder = folder };

            var configPath = Path.Combine(folder, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var configObj = JsonConvert.DeserializeObject<PlayerSpineConfig>(File.ReadAllText(configPath));
                    if (configObj != null)
                    {
                        entry.Config = configObj;
                        entry.DefaultSkin = string.IsNullOrEmpty(configObj.DefaultSkin) ? "Lamb" : configObj.DefaultSkin;
                        entry.Skins = configObj.Skins ?? [];
                        entry.IsFleece = configObj.FleeceCyclingOnly;
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"{name}: config.json unreadable ({e.Message}); defaults used.");
                }
            }

            Registry[name] = entry;

            if (entry.Config != null) SpineConfigs[name.Replace("/", "")] = entry.Config;
        }

        LoadedCustomSpines = true;

        var loadWatch = Stopwatch.StartNew();
        var eager = 0;
        foreach (var name in new[]
                 {
                     ActiveSpineName(0), ActiveSpineName(1),
                     SpineNameFromKey(RememberedSpineKey(0)), SpineNameFromKey(RememberedSpineKey(1)),
                     SpineNameFromFleece(Plugin.CurrentFleeceNameP1?.Value),
                     SpineNameFromFleece(Plugin.CurrentFleeceNameP2?.Value)
                 })
        {
            if (string.IsNullOrEmpty(name) || !Registry.TryGetValue(name, out var entry)) continue;
            if (entry.State != SpineState.NotLoaded) continue;

            LoadEntryNow(entry, material);
            entry.State = SpineState.Ready;
            eager++;
        }

        Plugin.Log.LogWarning($"TIMING TOTAL: {Registry.Count} player spine(s) registered, {eager} " +
                              $"loaded eagerly in {loadWatch.ElapsedMilliseconds}ms; the rest load when picked.");

        StartWarmUp();
    }

    private static void LoadEntryNow(SpineEntry entry, Material material)
    {
        var stage = Stopwatch.StartNew();

        var skeletonFile = Directory.GetFiles(entry.Folder, "*.json", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => !f.Contains("config"));
        var atlasFile = Directory.GetFiles(entry.Folder, "*.atlas", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var textureFiles = Directory.GetFiles(entry.Folder, "*.png", SearchOption.TopDirectoryOnly);

        if (skeletonFile == null || atlasFile == null || textureFiles.Length == 0)
        {
            Plugin.Log.LogWarning($"Failed to load player skin {entry.Name}: the folder needs one " +
                                  ".json, one .atlas and at least one .png.");
            return;
        }

        var skeletonText = File.ReadAllText(skeletonFile);
        var atlasText = File.ReadAllText(atlasFile);

        var textures = new Texture2D[textureFiles.Length];
        for (var i = 0; i < textureFiles.Length; i++)
            textures[i] = LoadSpineTexture(textureFiles[i], File.ReadAllBytes(textureFiles[i]));

        BuildEntryAsset(entry, material, atlasText, skeletonText, textures);
        Register(entry);

        Plugin.Log.LogInfo($"TIMING {entry.Name}: loaded in {stage.ElapsedMilliseconds}ms " +
                           $"({new FileInfo(skeletonFile).Length / 1048576f:F1}MB skeleton, " +
                           $"{textureFiles.Length} texture(s)).");
    }

    private static IEnumerator LoadRoutine(SpineEntry entry, bool announce)
    {
        var panelOpen = ModUI.CultTweakerPanel.Active != null && ModUI.CultTweakerPanel.Active.IsOpen;

        if (announce && !panelOpen)
            MapEditor.Tools.TriggerScreenText.Show(MapEditor.Tools.TriggerScreenText.Mode.Caption,
                $"Preparing {entry.Name}...", "", 90f);

        string skeletonText = null, atlasText = null, error = null;
        string[] textureFiles = null;
        List<byte[]> textureBytes = null;

        var reads = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var skeletonFile = Directory.GetFiles(entry.Folder, "*.json", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => !f.Contains("config"));
                var atlasFile = Directory.GetFiles(entry.Folder, "*.atlas", SearchOption.TopDirectoryOnly).FirstOrDefault();
                textureFiles = Directory.GetFiles(entry.Folder, "*.png", SearchOption.TopDirectoryOnly);

                if (skeletonFile == null || atlasFile == null || textureFiles.Length == 0)
                {
                    error = "the folder needs one .json, one .atlas and at least one .png";
                    return;
                }

                skeletonText = File.ReadAllText(skeletonFile);
                atlasText = File.ReadAllText(atlasFile);
                textureBytes = new List<byte[]>(textureFiles.Length);
                foreach (var file in textureFiles) textureBytes.Add(File.ReadAllBytes(file));
            }
            catch (Exception e)
            {
                error = e.Message;
            }
        });

        while (!reads.IsCompleted) yield return null;

        if (error != null)
        {
            Plugin.Log.LogError($"Player spine {entry.Name} failed to load: {error}");
            entry.State = SpineState.NotLoaded;
            entry.OnReady.Clear();
            if (announce)
                MapEditor.Tools.TriggerScreenText.Show(MapEditor.Tools.TriggerScreenText.Mode.Caption,
                    $"{entry.Name} failed to load", "see the log", 4f);
            yield break;
        }

        var textures = new Texture2D[textureFiles.Length];
        for (var i = 0; i < textureFiles.Length; i++)
        {
            textures[i] = LoadSpineTexture(textureFiles[i], textureBytes[i]);
            yield return null;
        }

        BuildEntryAsset(entry, null, atlasText, skeletonText, textures);
        StartWarmUp();

        Register(entry);

        var deadline = Time.unscaledTime + 60f;
        while (entry.Asset != null && entry.Asset.skeletonData == null && Time.unscaledTime < deadline)
            yield return null;

        entry.State = SpineState.Ready;

        if (announce)
            MapEditor.Tools.TriggerScreenText.Show(MapEditor.Tools.TriggerScreenText.Mode.Caption,
                $"{entry.Name} is ready", "", 2.5f);

        FireCallbacks(entry);
    }

    private static Texture2D LoadSpineTexture(string file, byte[] bytes)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.LoadImage(bytes);
        tex.name = Path.GetFileNameWithoutExtension(file);

        SpineFolderLoader.Keep(tex);
        SpineFolderLoader.Seal(tex);
        return tex;
    }

    private static void BuildEntryAsset(SpineEntry entry, Material material, string atlasText,
        string skeletonText, Texture2D[] textures)
    {
        var atlasTxt = new TextAsset(atlasText);
        var skele = new TextAsset(skeletonText);

        var mat = material != null ? material : new Material(SpineFolderLoader.SpineShader());
        var atlas = SpineAtlasAsset.CreateRuntimeInstance(atlasTxt, textures, mat, true);
        SpineFolderLoader.Keep(mat);
        SpineFolderLoader.Keep(atlas);

        var asset = SkeletonDataAsset.CreateRuntimeInstance(skele, atlas, false, SkeletonScale);
        SpineFolderLoader.Keep(asset);

        entry.Asset = asset;
        QueueWarmUp(entry.Name, asset, atlas, skeletonText);
    }

    private static void Register(SpineEntry entry)
    {
        if (entry.Asset == null) return;

        if (entry.IsFleece)
        {
            if (!FleeceCyclingSpines.ContainsKey(entry.Name))
            {
                Plugin.Log.LogInfo("Skin: " + entry.Name + " is added as a fleece cycle skin.");
                FleeceCyclingSpines.Add(entry.Name, new(entry.Asset, [.. entry.Skins]));
            }
        }
        else
        {
            CustomSkinManager.AddPlayerSpine(entry.Name, entry.Asset, [.. entry.Skins]);
        }
    }

    private static void FireCallbacks(SpineEntry entry)
    {
        var callbacks = entry.OnReady.ToArray();
        entry.OnReady.Clear();
        foreach (var callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Spine ready callback for {entry.Name} failed: {e.Message}");
            }
        }
    }

}

public class PlayerSpineConfig
{
    public string DefaultSkin { get; set; }
    public string[] Skins { get; set; }
    public bool FleeceCyclingOnly { get; set; } = false;

    public bool DisableFleeceCycling { get; set; } = false;

    public string[] HiddenSlots { get; set; } = [];
}
