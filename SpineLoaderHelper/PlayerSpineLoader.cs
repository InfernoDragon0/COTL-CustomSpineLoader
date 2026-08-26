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

    // Kept past loading so DisableFleeceCycling and HiddenSlots can be read whenever a player is
    // dressed. Keyed by folder name, which is the half of "<spine>/<skin>" that COTL_API tracks.
    public static Dictionary<string, PlayerSpineConfig> SpineConfigs = [];
    // Which fleece each player is wearing, by player id. The source of truth; the two fields below
    // are the halves of it that survive a restart, kept in step on every write because the config
    // and the boot path are both written in terms of them.
    //
    // Four, because coop seats four - players three and four were previously dressed by whatever
    // player one had picked, since this was two variables and a switch with two cases.
    public static readonly int[] FleeceIndexes = [-1, -1, -1, -1];

    // Raised when a player's look has actually landed on their skeleton, with the player's id.
    //
    // Not the same moment as asking for it. A fleece whose spine is not resident yet starts a load
    // and returns, dressing the player by callback seconds later - so a caller that redraws when
    // ApplyFleece RETURNS redraws the old look, which is why a cross-atlas fleece only appeared
    // when it was picked a second time.
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

        // The two that persist are mirrored, because the config and the boot path are written in
        // terms of them.
        if (playerID == 0) currentFleeceIndexP1 = result;
        else if (playerID == 1) currentFleeceIndexP2 = result;

        Plugin.Log.LogInfo("Player " + (playerID + 1) + " cycled to fleece index " + result + " (" + FleeceRotation[result] + ")");

        return result;
    }

    // ---- deferred skeleton parsing --------------------------------------------------------------

    // Parsing the skeleton JSON is four fifths of what it costs to load a spine: measured at 16.4s
    // of a 20.9s load across 17 skins, against 2.5s of texture decoding and 1.9s of file reading.
    // SkeletonDataAsset does it eagerly when CreateRuntimeInstance is passed initialize:true, on the
    // main thread, before the game can draw anything.
    //
    // It does not have to happen there. Once the Atlas exists, SkeletonJson.ReadSkeletonData builds
    // plain C# objects and touches no Unity API at all, so the parse runs on a background thread
    // while the menu is up and the result is handed to the asset a frame later.
    //
    // Nothing has to wait for it. A spine worn before its turn comes up is parsed by spine-unity
    // itself, from the TextAsset still attached to the asset, exactly as it always was - the
    // warm-up only changes where the cost lands, never whether the spine works.

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
            // The last Unity call the parse needs, so it happens here rather than on the worker.
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

            // Lazy loads queue parses long after the boot batch drained; the pump must wake
            // back up or the result would sit in WarmFinished forever.
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
                // Background, so a half-finished warm-up can never keep the game from closing, and
                // below normal so it yields to whatever the game is doing with the other cores.
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

            // Process-wide, so a collection mid-parse skews it low - but parses run one at a
            // time on this thread, so the delta is a fair per-skeleton attribution.
            var heapBefore = GC.GetTotalMemory(false);
            try
            {
                // Reads the Atlas, never writes it, so sharing it with the main thread is safe.
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

    // Driven from Plugin.Update: handing the parsed data to the asset is a Unity-side write, so it
    // belongs on the main thread even though the work that produced it did not.
    // True once every queued job has landed - the per-frame pump stops taking the lock then.
    // Volatile-free by design: _warmApplied only moves on the main thread, and a stale read of
    // _warmQueued just means one extra harmless pump.
    private static bool _warmDrained;

    public static void PumpWarmUp()
    {
        // Warm-up is a startup job, but this is called from Plugin.Update every frame forever -
        // once the queue has drained there is nothing left to take a lock for.
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
            // Worn before its turn came up, so spine-unity parsed it already. Overwriting now would
            // leave a live Skeleton pointing at data its own asset no longer holds.
            Plugin.Log.LogInfo($"{job.Name} was already parsed on demand; warm-up result dropped.");
            ReleaseJson(job.Asset);
        }
        else
        {
            try
            {
                job.Asset.skeletonData = job.Parsed;

                // GetSkeletonData builds this as part of parsing, and injecting the data above makes
                // it return before it ever gets there - so the AnimationStateData it would have
                // created has to be built here. Without it GetAnimationStateData answers null and
                // SkeletonAnimation.Initialize throws "data cannot be null", which takes down the
                // whole of PlayerFarming.Start with it. FillStateData only fills an existing one.
                job.Asset.stateData = new AnimationStateData(job.Parsed);
                job.Asset.FillStateData();

                // Ask the asset the same two questions spine-unity is about to ask, rather than
                // trusting that the warm-up left it complete.
                if (job.Asset.GetSkeletonData(true) == null || job.Asset.GetAnimationStateData() == null)
                    throw new Exception("the asset was still incomplete afterwards");

                Plugin.Log.LogInfo($"Warmed {job.Name} in {job.ParseMs}ms " +
                                   $"(~{job.ParseBytes / 1048576f:F0}MB of parsed skeleton data).");
                ReleaseJson(job.Asset);
            }
            catch (Exception e)
            {
                // Put it back exactly as spine-unity expects to find it, so the TextAsset route
                // still works and a failed warm-up costs nothing but the time it wasted.
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

    // Once the parsed SkeletonData sits in the asset, the JSON TextAsset it was parsed from is
    // a dead copy of a file that can run to tens of MB - and there is one per installed spine.
    // Nothing reads it again: GetSkeletonData returns the cached data, and neither the game nor
    // COTL_API ever Clear() these assets back to their JSON.
    private static void ReleaseJson(SkeletonDataAsset asset)
    {
        if (asset == null || asset.skeletonData == null || asset.skeletonJSON == null) return;

        var json = asset.skeletonJSON;
        asset.skeletonJSON = SpineFolderLoader.PlaceholderJson();
        UnityEngine.Object.Destroy(json);
        SpineFolderLoader.MarkJsonFreed(asset);
    }

    // ---- active spine -------------------------------------------------------------------------

    // COTL_API keeps SelectedSpine/SelectedSpine2 internal, so they are read through Harmony's
    // traverse rather than by depending on a publicized build of it - the same approach the map
    // editor's enemy picker uses for the custom enemy list.
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

    // "<spine>/<skin>" -> "<spine>". The API tracks a spine for players one and two only, so a
    // third player is reading player one's - which is also the spine they are actually wearing.
    public static string ActiveSpineName(int playerId) => SpineNameFromKey(ActiveSpineKey(playerId));

    public static string SpineNameFromKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";

        var slash = key.IndexOf('/');
        return slash < 0 ? key : key.Substring(0, slash);
    }

    // The choice this mod wrote down, as opposed to the one the API is currently holding. At boot
    // these differ for a while: ours is on disk from the moment it was picked, the API's does not
    // exist until something puts it back.
    public static string RememberedSpineKey(int playerId) =>
        (playerId == 0 ? Plugin.SelectedSpineP1?.Value : Plugin.SelectedSpineP2?.Value) ?? "";

    // Null for the vanilla spine and for any custom one without a config.json, which is the signal
    // to leave everything at its default behaviour.
    public static PlayerSpineConfig ConfigFor(int playerId)
    {
        var name = ActiveSpineName(playerId);
        if (string.IsNullOrEmpty(name)) return null;

        return SpineConfigs.TryGetValue(name, out var config) ? config : null;
    }

    // ---- hidden slots -------------------------------------------------------------------------

    // Strips a slot out of the LIVE skin rather than the loaded SkeletonData. Every path that could
    // put the slot back - the animations that key it, the game's own SetAttachment calls, the
    // fleece - resolves through Skeleton.GetAttachment, which reads the current skin and then the
    // default one; with no entry in either, all of them resolve to nothing.
    //
    // Working on the live skin also keeps the data asset clean, so the other skins in the same
    // file, and the vanilla spine, are unaffected when the player swaps away.
    public static void HideSlots(SkeletonAnimation spine, PlayerSpineConfig config)
    {
        if (spine == null || spine.Skeleton == null) return;
        if (config?.HiddenSlots == null || config.HiddenSlots.Length == 0) return;

        var skin = spine.Skeleton.Skin;
        if (skin == null) return;

        // Between a spine swap and the SetSkin that follows it, the live skin IS one of the
        // SkeletonData's own. Stripping that would edit the loaded asset itself - permanently, for
        // every skin in the same file and every player wearing one.
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

            // Every attachment name on the slot, not just the one in the setup pose: CROWN carries
            // five and CROWN_EYE nine, and an animation can key any of them.
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

            // Clears what the slot is showing at this instant; anything that re-attaches by name
            // from here lands on one of the transparent copies above.
            spine.Skeleton.SetAttachment(slotName, null);
        }
    }

    // A fully transparent stand-in for an attachment, so the NAME still resolves.
    //
    // Deleting the entry instead throws: Skeleton.SetAttachment(slot, name) raises "Attachment not
    // found" when it cannot resolve, and FlyingCrown.Close re-attaches CROWN by name every time the
    // crown flies back or a CROWN_HIDE_CANCEL event fires. The copy matters as much as the alpha -
    // the original attachment belongs to the loaded asset and is shared with every other skin in
    // the same file.
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

    // True when the skeleton is wearing a skin owned by the loaded asset rather than the composite
    // the game builds per player in SetSkin.
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

    // The fleece lives on ANOTHER skin (a vanilla one on the lamb's own skeleton, or a skin from a
    // FleeceCyclingOnly spine we loaded ourselves); wearing it means copying that skin's
    // attachments into the slots the player is currently rendering. Shared by every caller -
    // the F-keys, the panel and the SetSkin patch - so one fix reaches all three.
    public static Skin ResolveFleeceSkin(string fleeceSkinName, SkeletonAnimation targetSpine)
    {
        if (string.IsNullOrEmpty(fleeceSkinName) || targetSpine == null) return null;

        if (!fleeceSkinName.Contains("CultTweaker_"))
            return targetSpine.Skeleton.Data.FindSkin(fleeceSkinName);

        // CultTweaker_<SpineName>_<FleeceName>; the fleece name may itself contain underscores,
        // which is why the split is capped at 3.
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

        // GetSkeletonData, not the skeletonData field: the field stays null until the warm-up
        // thread reaches this spine, and a fleece can be asked for before then. The call parses it
        // on the spot in that case.
        var skeletonData = FleeceCyclingSpines[spineName].Item1.GetSkeletonData(false);
        var skin = skeletonData != null ? skeletonData.FindSkin(split[2]) : null;
        if (skin != null) return skin;

        Plugin.Log.LogWarning("Defaulting to default as Custom Fleece skin not found: " + fleeceSkinName);
        return targetSpine.Skeleton.Data.FindSkin("Lamb");
    }

    // Copies the fleece's attachments into the live skin, slot by slot. A slot the fleece does not
    // fill is CLEARED rather than left alone - otherwise the previous fleece's poncho stays on
    // under the new one.
    //
    // Nine of the fourteen slots below are the poncho, so a hidden slot and a fleece want the same
    // entry: the hidden list wins, and the slot is cleared instead of dressed.
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

        // After the fleece, never before: the loop above writes to some of the same slots.
        HideSlots(spine, config);

        spine.Skeleton.SetSlotsToSetupPose();
        spine.Update(0);
    }

    // players is only populated when coop features are enabled, so solo play lives entirely in
    // Instance and player 0 has to fall back to it.
    public static PlayerFarming ResolvePlayer(int playerId)
    {
        var players = PlayerFarming.players;
        if (players != null && playerId >= 0 && playerId < players.Count && players[playerId] != null)
            return players[playerId];

        return playerId == 0 ? PlayerFarming.Instance : null;
    }

    public static int GetFleeceIndex(int playerId) =>
        playerId >= 0 && playerId < FleeceIndexes.Length ? FleeceIndexes[playerId] : -1;

    // Dresses one player in one fleece. Players beyond the second are dressed but NOT remembered:
    // both the config and the SetSkin patch that re-applies a fleece after a respawn only know
    // about two, so a third player's choice lasts until the game next rebuilds their skin.
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

        // Remembered even by a spine that will not wear it, so the choice is still there when the
        // player swaps to one that does. Nothing is said on screen; the panel carries the note.
        if (persist) RememberFleece(playerId, fleeceIndex);

        if (config != null && config.DisableFleeceCycling)
        {
            HideSlots(player.Spine, config);
            Plugin.Log.LogInfo($"{ActiveSpineName(playerId)} keeps its own fleece; " +
                               $"{fleeceSkinName} remembered for player {playerId + 1} only.");

            // The skeleton changed even though the fleece was refused: HideSlots stripped it.
            AnnounceLook(playerId);
            return false;
        }

        // A custom fleece rides a spine that may not be loaded yet; it dresses itself the
        // moment the load lands rather than failing the cycle.
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

        // Here rather than at the call site, so the deferred landing above announces itself too.
        AnnounceLook(playerId);
        return true;
    }

    private static void RememberFleece(int playerId, int fleeceIndex)
    {
        if (playerId >= 0 && playerId < FleeceIndexes.Length) FleeceIndexes[playerId] = fleeceIndex;

        // Players three and four are remembered for the session but not written to the config: the
        // boot path loads a fleece eagerly per player and there are two entries for it, so a third
        // seat's choice lasts until the game is closed.
        switch (playerId)
        {
            case 0:
                currentFleeceIndexP1 = fleeceIndex;
                Plugin.CurrentFleeceIndexP1.Value = fleeceIndex;
                // The NAME as well as the index: an index only resolves once the rotation is
                // built, but the next boot needs to know which spine to load eagerly before
                // any rotation exists.
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

    // Eighteen installed spines used to pay their full cost at boot - file reads, texture
    // decodes and above all the parsed skeleton data, measured in whole gigabytes - for looks
    // nobody was wearing. The folder scan now only reads each spine's config.json into a
    // registry. The spines actually selected (the API's saved choice per player, and the fleece
    // the config file remembers) load eagerly exactly as before, so the saved look is on the
    // player from the first frame; everything else loads the first time it is picked - file IO
    // on a worker task, texture decodes spread one per frame, the parse on the warm-up thread -
    // and applies itself the moment it is ready, so the game never stalls.
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

    // Accepts a bare spine name or the API's "<spine>/<skin>" key - the panel and the API's
    // saved selection both speak the second form.
    private static SpineEntry FindEntry(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Registry.TryGetValue(name, out var entry)) return entry;

        var slash = name.IndexOf('/');
        return slash > 0 && Registry.TryGetValue(name.Substring(0, slash), out entry) ? entry : null;
    }

    // A spine that is on its way in. The F7 panel says so where the portrait would be, rather than
    // leaving an empty box for the seconds a parse takes.
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

    // The panel's picker: every wearable "<spine>/<skin>" key, loaded or not - the same keys
    // AddPlayerSpine mints, one per skin, so selection works exactly as it always did.
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

    // The fleece rotation is built from the registry rather than the loaded dictionary, so a
    // fleece spine appears in the cycle before it has ever been loaded.
    public static IEnumerable<(string Name, string[] Skins)> FleeceCycleEntries()
    {
        foreach (var entry in Registry.Values)
            if (entry.IsFleece) yield return (entry.Name, entry.Skins);
    }

    // "CultTweaker_<spine>_<fleece>" -> "<spine>", or null for a vanilla fleece.
    public static string SpineNameFromFleece(string fleeceSkinName)
    {
        if (string.IsNullOrEmpty(fleeceSkinName) || !fleeceSkinName.Contains("CultTweaker_")) return null;
        var split = fleeceSkinName.Split(['_'], count: 3);
        return split.Length < 3 ? null : split[1];
    }

    // Runs onReady once the spine is usable. Already loaded - or not ours at all (another mod's
    // spine registered straight with the API) - runs it immediately.
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

        // No coroutine host this early means we are inside startup; load the old way.
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

    // The looks that must exist the moment the player does. Called at startup and again from
    // PlayerFarming.Awake: the API may not have read its save yet when the mod loads, so the
    // selection can appear between the two.
    // Writes the choice down. Called whenever a spine is picked, so it survives the session.
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

    // Puts a remembered spine back at startup.
    //
    // Only when COTL_API has no selection of its own, which is the whole rule: if the API did keep
    // the choice, or the player picked one through the API's own settings, that is the newer answer
    // and this must not talk over it. It is the empty case - the one where the choice was simply
    // lost - that this exists for.
    private static void RestoreSelectedSpine(int playerId)
    {
        var saved = RememberedSpineKey(playerId);
        if (string.IsNullOrEmpty(saved)) return;

        if (!string.IsNullOrEmpty(ActiveSpineKey(playerId))) return;

        // Loaded first, selected second, and that order is the fix.
        //
        // ChangeSelectedPlayerSpine only takes a spine the API has been handed, and a spine is not
        // handed over until it has loaded (Register, at the end of the load). Selecting first meant
        // naming something that did not exist yet: the API kept its default and the player came up
        // in the plain lamb, with the log cheerfully reporting a restore that never happened.
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

            // Read back rather than assumed. The API takes a string and reports nothing, so the only
            // way to know a selection was accepted is to ask what the selection is now - and a
            // restore that silently did nothing is exactly the failure this is here to catch.
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

        // Ours, plus the same folder in any other mod's CultTweaker folder (ModContentPaths). The
        // entry keeps the absolute folder it was found in, so the art loads from wherever it lives.
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

            // Same key AddPlayerSpine registers under, so a selected "<spine>/<skin>" finds it.
            if (entry.Config != null) SpineConfigs[name.Replace("/", "")] = entry.Config;
        }

        LoadedCustomSpines = true;

        // The saved looks load now, synchronously, the way every spine used to - the player must
        // wear their choice on the first frame, not two seconds in.
        //
        // Both sources are asked, and asking only the first is what lost the remembered spine.
        // ActiveSpineName reads COTL_API's selection, and at this point in the boot there isn't one:
        // this runs from Plugin.Awake, long before anything has put a selection back. So the list
        // came out empty, the remembered spine was never loaded here, and by the time
        // RestoreSelectedSpine tried to select it the API had nothing registered under that name to
        // select - leaving the player in the default lamb. Our own note on disk has the answer from
        // the moment it was written, so it is read too.
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

        // Last, so the worker never competes with the loading loop it is queued from.
        StartWarmUp();
    }

    // The synchronous load: everything a spine needs, on the spot, parse queued to the warm-up
    // thread. Used at startup for the saved looks and as the no-host fallback.
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

    // The asynchronous load: file IO on a worker, texture decodes one per frame, parse on the
    // warm-up thread, applied by callback when it lands. The screen text is the "is anything
    // happening?" answer for the seconds the parse takes.
    private static IEnumerator LoadRoutine(SpineEntry entry, bool announce)
    {
        // Not while the F7 panel is up: the caption lands in the bottom-left corner, which is where
        // the player dock stands, and the dock says it in the card of the player it belongs to -
        // which is more use anyway, since it names who is waiting rather than only what for.
        var panelOpen = ModUI.CultTweakerPanel.Active != null && ModUI.CultTweakerPanel.Active.IsOpen;

        if (announce && !panelOpen)
            MapEditor.Tools.TriggerScreenText.Show(MapEditor.Tools.TriggerScreenText.Mode.Caption,
                $"Preparing {entry.Name}...", "", 90f);

        string skeletonText = null, atlasText = null, error = null;
        string[] textureFiles = null;
        List<byte[]> textureBytes = null;

        // Fully qualified: a game assembly ships its own 'Task' type that wins the name.
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

        // Registered before the parse lands so the API can already resolve the name; the wearer
        // is dressed by callback once the data exists, which is what keeps the swap smooth.
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
        // Point filtering to match what TextureHelper always produced for these pages.
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.LoadImage(bytes);
        tex.name = Path.GetFileNameWithoutExtension(file);

        // Runtime-built, no asset backing: an UnloadUnusedAssets sweep that decides nothing
        // references it frees it for good. And a player spine is a whole replacement skeleton
        // that nothing ever repacks, so the decoded CPU copy is dead weight.
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

        // initialize:false - the third argument is what used to parse the whole skeleton JSON
        // on the main thread. The warm-up thread does it instead.
        var asset = SkeletonDataAsset.CreateRuntimeInstance(skele, atlas, false, SkeletonScale);
        SpineFolderLoader.Keep(asset);

        entry.Asset = asset;
        QueueWarmUp(entry.Name, asset, atlas, skeletonText);
    }

    // Hands the spine to whoever owns its kind. Deliberately NOT ChangeSelectedPlayerSpine: the
    // old loader selected every spine as it registered it, which left the last folder worn on
    // every boot regardless of what the player had picked. The API's own saved selection rules.
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

    // Set on a spine that dresses its own body: the fleece writes over Body, the poncho, the rope
    // and the bell, which on a custom rig means lamb artwork replacing the skin's own.
    public bool DisableFleeceCycling { get; set; } = false;

    // Slot names this spine never renders. Hiding a slot in the Spine editor does not export;
    // clearing its setup attachment only lasts until the first animation that keys the slot, and
    // the game re-attaches the crown by name. Listing the slot here replaces its attachments in the
    // live skin with transparent copies instead, which is the one thing all three paths resolve
    // through - see Blank() for why they are replaced rather than removed.
    public string[] HiddenSlots { get; set; } = [];
}
