using System;
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
        var result = 0;
        switch (playerID)
        {
            case 0:
                //clamp index to 0 to FleeceRotation
                currentFleeceIndexP1++;
                if (currentFleeceIndexP1 >= FleeceRotation.Count) currentFleeceIndexP1 = 0;
                result = currentFleeceIndexP1;
                break;
            case 1:
                currentFleeceIndexP2++;
                if (currentFleeceIndexP2 >= FleeceRotation.Count) currentFleeceIndexP2 = 0;
                result = currentFleeceIndexP2;
                break;
        }

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
            job.Json = null;

            lock (WarmLock) WarmFinished.Enqueue(job);
        }
    }

    // Driven from Plugin.Update: handing the parsed data to the asset is a Unity-side write, so it
    // belongs on the main thread even though the work that produced it did not.
    public static void PumpWarmUp()
    {
        while (true)
        {
            WarmUpJob job;
            lock (WarmLock)
            {
                if (WarmFinished.Count == 0) return;
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

                Plugin.Log.LogInfo($"Warmed {job.Name} in {job.ParseMs}ms.");
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
                              $"{_warmWatch?.ElapsedMilliseconds ?? 0}ms.");
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
    public static string ActiveSpineName(int playerId)
    {
        var key = ActiveSpineKey(playerId);
        if (string.IsNullOrEmpty(key)) return "";

        var slash = key.IndexOf('/');
        return slash < 0 ? key : key.Substring(0, slash);
    }

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

    public static int GetFleeceIndex(int playerId) => playerId switch
    {
        0 => currentFleeceIndexP1,
        1 => currentFleeceIndexP2,
        _ => -1
    };

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
        return true;
    }

    private static void RememberFleece(int playerId, int fleeceIndex)
    {
        switch (playerId)
        {
            case 0:
                currentFleeceIndexP1 = fleeceIndex;
                Plugin.CurrentFleeceIndexP1.Value = fleeceIndex;
                break;
            case 1:
                currentFleeceIndexP2 = fleeceIndex;
                Plugin.CurrentFleeceIndexP2.Value = fleeceIndex;
                break;
        }
    }
    public static void LoadAllPlayerSpines(Material material = null)
    {
        if (LoadedCustomSpines)
        {
            Plugin.Log.LogInfo("Load Player Spines was called again but already loaded!");
            return;
        }
        //get the plugin path, then find the foler PlayerSkins in it
        var playerFolder = Path.Combine(Plugin.PluginPath, "PlayerSkins");
        //check if the player folder exists
        if (!Directory.Exists(playerFolder))
            Directory.CreateDirectory(playerFolder);

        //get each folder inside the directory
        var folders = Directory.GetDirectories(playerFolder);

        // Timing: which stage of loading a spine actually costs the wait. Reported per folder and
        // totalled at the end.
        var loadWatch = Stopwatch.StartNew();
        long totalReadMs = 0, totalTextureMs = 0, totalAtlasMs = 0, totalParseMs = 0;
        long totalSkeletonBytes = 0, totalTextureBytes = 0;
        var loadedCount = 0;

        foreach (var folder in folders)
        {
            var playerSpineName = Path.GetFileName(folder);

            var spineSkeleton = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Where(x => !x.Contains("config")).ToArray();
            var spineTextures = Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly);
            var spineAtlas = Directory.GetFiles(folder, "*.atlas", SearchOption.TopDirectoryOnly);
            var config = Directory.GetFiles(folder, "config.json", SearchOption.TopDirectoryOnly);

            var defaultSkinName = "Lamb";
            var skinList = new string[0];
            var isFleeceCycleSkin = false;
            PlayerSpineConfig spineConfig = null;

            if (config.Length > 0)
            {
                var configJson = new TextAsset(File.ReadAllText(config[0]));
                var configObj = JsonConvert.DeserializeObject<PlayerSpineConfig>(configJson.text);
                if (configObj != null)
                {
                    spineConfig = configObj;
                    defaultSkinName = configObj.DefaultSkin;
                    skinList = configObj.Skins;
                    isFleeceCycleSkin = configObj.FleeceCyclingOnly;
                    Plugin.Log.LogInfo($"Using default skin: {defaultSkinName}");
                    Plugin.Log.LogInfo($"Using skin list: {string.Join(", ", skinList)}");

                    if (configObj.DisableFleeceCycling)
                        Plugin.Log.LogInfo($"{playerSpineName} keeps its own fleece; transmog will not dress it.");
                    if (configObj.HiddenSlots is { Length: > 0 })
                        Plugin.Log.LogInfo($"{playerSpineName} hides {configObj.HiddenSlots.Length} slot(s): " +
                                           string.Join(", ", configObj.HiddenSlots));
                }
            }

            if (spineSkeleton.Length > 0 && spineTextures.Length > 0 && spineAtlas.Length > 0)
            {
                var stage = Stopwatch.StartNew();

                Plugin.Log.LogInfo("Reading atlas from " + spineAtlas[0]);
                var atlasTxt = new TextAsset(File.ReadAllText(spineAtlas[0]));

                Plugin.Log.LogInfo("Reading skeleton from " + spineSkeleton[0]);

                // Kept as a string for the warm-up thread, and handed to a TextAsset as well: the
                // asset needs it if a spine is worn before the warm-up reaches it.
                var skeletonText = File.ReadAllText(spineSkeleton[0]);
                var skele = new TextAsset(skeletonText);

                var readMs = stage.ElapsedMilliseconds;
                var skeletonBytes = new FileInfo(spineSkeleton[0]).Length;

                stage.Restart();
                var textures = new Texture2D[spineTextures.Length];
                long textureBytes = 0;

                foreach (var textureFile in spineTextures)
                {
                    Plugin.Log.LogInfo("Reading texture from " + textureFile);
                    textureBytes += new FileInfo(textureFile).Length;
                    Texture2D tex = TextureHelper.CreateTextureFromPath(textureFile);
                    tex.name = Path.GetFileNameWithoutExtension(textureFile);
                    textures[Array.IndexOf(spineTextures, textureFile)] = tex;
                }

                var textureMs = stage.ElapsedMilliseconds;

                stage.Restart();
                var mat = material ?? new Material(Shader.Find("Spine/Skeleton")); //TODO: find out what shader cotl uses
                var runtimeAtlasAsset = Spine.Unity.SpineAtlasAsset.CreateRuntimeInstance(atlasTxt, textures, mat, true);
                var atlasMs = stage.ElapsedMilliseconds;

                // initialize:false - the third argument is what used to parse the whole skeleton
                // JSON here on the main thread. The warm-up thread does it instead.
                stage.Restart();
                var runtimeSkeletonAsset = Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance(skele, runtimeAtlasAsset, false, SkeletonScale);
                QueueWarmUp(playerSpineName, runtimeSkeletonAsset, runtimeAtlasAsset, skeletonText);
                var parseMs = stage.ElapsedMilliseconds;

                totalReadMs += readMs;
                totalTextureMs += textureMs;
                totalAtlasMs += atlasMs;
                totalParseMs += parseMs;
                totalSkeletonBytes += skeletonBytes;
                totalTextureBytes += textureBytes;
                loadedCount++;

                Plugin.Log.LogInfo($"TIMING {playerSpineName}: read={readMs}ms " +
                                   $"({skeletonBytes / 1048576f:F1}MB skeleton) textures={textureMs}ms " +
                                   $"({spineTextures.Length} files, {textureBytes / 1048576f:F1}MB) " +
                                   $"atlas={atlasMs}ms create={parseMs}ms " +
                                   $"total={readMs + textureMs + atlasMs + parseMs}ms");

                Plugin.Log.LogInfo("Creating skeleton for " + playerSpineName);
                Plugin.Log.LogInfo("Using material name " + mat.name);

                if (isFleeceCycleSkin)
                {
                    Plugin.Log.LogInfo("Skin: " + playerSpineName + " is added as a fleece cycle skin.");
                    FleeceCyclingSpines.Add(playerSpineName, new(runtimeSkeletonAsset, [.. skinList]));
                }
                else
                {
                    CustomSkinManager.AddPlayerSpine(playerSpineName, runtimeSkeletonAsset, [.. skinList]);
                    CustomSkinManager.ChangeSelectedPlayerSpine(playerSpineName + "/" + defaultSkinName);

                    // Same key AddPlayerSpine registers under, so a selected "<spine>/<skin>" finds it.
                    if (spineConfig != null) SpineConfigs[playerSpineName.Replace("/", "")] = spineConfig;
                }


                // PlayerFarming.Instance.Spine.skeletonDataAsset = runtimeSkeletonAsset;
                // PlayerFarming.Instance.Spine.initialSkinName = Plugin.Instance?.SkinToLoad;
                // PlayerFarming.Instance.Spine.Initialize(true);
            }
            else
            {
                Plugin.Log.LogInfo($"Failed to load player skin {playerSpineName}, ensure that the folder contains at least one of each .json, .png and .atlas file.");
            }

        }

        loadWatch.Stop();

        var accounted = totalReadMs + totalTextureMs + totalAtlasMs + totalParseMs;
        Plugin.Log.LogWarning(
            $"TIMING TOTAL: {loadedCount} player spine(s) in {loadWatch.ElapsedMilliseconds}ms | " +
            $"read {totalReadMs}ms ({totalSkeletonBytes / 1048576f:F0}MB skeleton) | " +
            $"textures {totalTextureMs}ms ({totalTextureBytes / 1048576f:F0}MB png) | " +
            $"atlas {totalAtlasMs}ms | create {totalParseMs}ms | " +
            $"other {loadWatch.ElapsedMilliseconds - accounted}ms");

        LoadedCustomSpines = true;

        // Last, so the worker never competes with the loading loop it is queued from.
        StartWarmUp();
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
