using System;
using System.IO;
using System.Linq;
using COTL_API.Helpers;
using CustomSpineLoader.MapEditor.Npc;
using CustomSpineLoader.SpineLoaderHelper;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

// Loads custom NPCs from disk: CustomNpcs/<name>/config.json plus spine assets in the same
// folder, following the mod's established conventions - Loader<T> for the folder scan (like
// items/meals/structures) and FollowerSpines' auto-discovery for the spine files (*.atlas,
// *.png, skeleton *.json excluding config).
public class CustomNpcConfig
{
    public string NpcName = "";

    // English display name over the speech bubble; falls back to NpcName.
    public string DisplayName = "";

    // Spine files are auto-discovered in the folder; these override discovery when set
    // (paths relative to the NPC's folder).
    public string SkeletonPath = "";
    public string AtlasPath = "";
    public string[] TexturePaths = [];

    public string SkinName = "";
    public string IdleAnimation = "idle";
    public string TalkAnimation = "talk";

    // Rarely needed: a different body prefab to clone. The default lost-lamb ghost is right for
    // anything fully re-skinned by the spine override.
    public string NpcToMimic = "";

    public NpcDialogue Dialogue;
}

// Backing-field subclass, the CultTweakerCustomStructure shape: the base exposes read-only
// virtuals, a config entry needs them settable.
public class CultTweakerCustomNpc : CustomNpc
{
    private readonly string _internalName;
    private readonly string _displayName;
    private readonly string _mimic;
    private readonly string _idle;
    private readonly string _talk;

    public override string InternalName => _internalName;
    public override string DisplayName => _displayName;
    public override string NpcToMimic => string.IsNullOrEmpty(_mimic) ? base.NpcToMimic : _mimic;
    public override string IdleAnimation => _idle;
    public override string TalkAnimation => _talk;

    public CultTweakerCustomNpc(string internalName, CustomNpcConfig config)
    {
        _internalName = internalName;
        _displayName = string.IsNullOrEmpty(config.DisplayName) ? config.NpcName : config.DisplayName;
        _mimic = config.NpcToMimic;
        _idle = string.IsNullOrEmpty(config.IdleAnimation) ? "idle" : config.IdleAnimation;
        _talk = string.IsNullOrEmpty(config.TalkAnimation) ? "talk" : config.TalkAnimation;
        SpineSkinName = config.SkinName ?? "";
        Dialogue = config.Dialogue;
    }
}

public class CustomNpcLoader : Loader<CustomNpcConfig>
{
    public CustomNpcLoader() : base("CustomNpcs") { }

    public static void LoadAllCustomNpcs(MonoBehaviour coroutineHost)
    {
        var loader = new CustomNpcLoader();
        var entries = loader.LoadAll();

        foreach (var entry in entries)
        {
            try
            {
                var config = entry.Config;
                if (string.IsNullOrWhiteSpace(config.NpcName))
                {
                    Plugin.Log.LogWarning($"Custom NPC folder '{entry.FolderName}' has no NpcName, skipped.");
                    continue;
                }

                var internalName = "CultTweaker_" + config.NpcName.Replace(" ", "_");
                var npc = new CultTweakerCustomNpc(internalName, config);

                npc.SpineOverride = BuildSpine(entry.FolderPath, config, internalName);

                if (npc.Dialogue != null)
                {
                    if (npc.Dialogue.Validate(internalName)) npc.Dialogue.RegisterTerms(npc);
                    else npc.Dialogue = null;
                }

                CustomNpcManager.Add(npc);
                coroutineHost.StartCoroutine(CustomNpcManager.BuildNpcPrefab(npc));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Custom NPC '{entry.FolderName}' failed to load: {e}");
            }
        }
    }

    // The shared folder recipe (SpineFolderLoader). Null when the folder ships no spine - the
    // NPC then wears its mimic's own skeleton, which is legitimate (a re-dialogued lost lamb
    // needs no art).
    private static Spine.Unity.SkeletonDataAsset BuildSpine(string folder, CustomNpcConfig config,
        string internalName)
    {
        var data = SpineFolderLoader.Build(folder, internalName, config.SkeletonPath, config.AtlasPath,
            config.TexturePaths);

        if (data == null)
            Plugin.Log.LogInfo($"Custom NPC '{internalName}': no spine assets in folder, " +
                               "using the mimic's own skeleton.");

        return data;
    }
}
