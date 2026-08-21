using System;
using System.Collections.Generic;
using COTL_API.CustomEnemy;
using CustomSpineLoader.SpineLoaderHelper;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

// Enemies defined on disk: CustomEnemies/<name>/config.json plus, optionally, a Spine export in
// the same folder.
//
// The AI is a vanilla enemy's. An enemy here names one of the game's own enemy prefabs to mimic
// and keeps its brain wholesale - its states, its attacks, its animations, its death - and then
// changes what is changeable from outside: the skeleton it wears, how much health it has, how big
// it is, and any public number on the controller by name. That is the whole trick, and it is why
// this needs no code: the hard part of an enemy is its behaviour, and the game already wrote 248
// of those.
public class CustomEnemyConfig
{
    public string EnemyName = "";

    // The vanilla enemy whose prefab (and therefore whose entire AI) this one is built from.
    public string Mimic = "Assets/Prefabs/Enemies/Enemy Bat.prefab";

    public float Health = 5f;

    // Spine files are auto-discovered in the folder; set these only when it holds more than one
    // export. No spine at all is legitimate - the enemy then looks like its mimic.
    public string SkeletonPath = "";
    public string AtlasPath = "";
    public string[] TexturePaths = [];
    public string SkinName = "";
    public float SkeletonScale = 0.005f;

    // Multiplies the spawned enemy's transform scale. A brute is the same enemy at 1.6.
    public float Scale = 1f;

    // Shows the boss health bar across the top of the screen while this one is alive.
    public bool BossHealthBar;

    // What that bar is labelled; falls back to EnemyName.
    public string BossBarName = "";

    // Any public field or property on the mimic's own controller, by name:
    //   "AttackWithinRange": 6.0, "maxSpeed": 0.08, "DoubleAttack": 1
    // Numbers because JSON numbers are what a tuning table wants; a bool member takes 0 or 1.
    public Dictionary<string, float> Tuning = [];
}

// The COTL_API base wants its answers as read-only properties; a config entry needs them
// settable, so the same backing-field shape the structures and NPCs use.
public class CultTweakerCustomEnemy : CustomEnemy
{
    private readonly string _internalName;
    private readonly string _mimic;
    private readonly float _health;

    public override string InternalName => _internalName;
    public override string EnemyToMimic => _mimic;
    public override float maxHealth => _health;

    // Deliberately null: a controller of our own would replace the mimic's brain, which is the
    // one thing this is built to keep. It also avoids COTL_API's spawn path casting the mimic to
    // EnemySwordsmanWolf, which pins every custom enemy to that one prefab.
    public override Type EnemyController => null;

    public string DisplayName { get; }
    public float Scale { get; }
    public bool BossHealthBar { get; }
    public string BossBarName { get; }
    public Dictionary<string, float> Tuning { get; }

    public CultTweakerCustomEnemy(string internalName, CustomEnemyConfig config)
    {
        _internalName = internalName;
        _mimic = string.IsNullOrWhiteSpace(config.Mimic)
            ? "Assets/Prefabs/Enemies/Enemy Bat.prefab"
            : config.Mimic;
        _health = config.Health > 0f ? config.Health : 5f;

        DisplayName = string.IsNullOrWhiteSpace(config.EnemyName) ? internalName : config.EnemyName;
        Scale = config.Scale > 0f ? config.Scale : 1f;
        BossHealthBar = config.BossHealthBar;
        BossBarName = string.IsNullOrWhiteSpace(config.BossBarName) ? DisplayName : config.BossBarName;
        Tuning = config.Tuning ?? [];

        SpineSkinName = config.SkinName ?? "";
    }
}

public class CustomEnemyLoader : Loader<CustomEnemyConfig>
{
    public CustomEnemyLoader() : base("CustomEnemies") { }

    // Keyed by the Enum value COTL_API mints, because that is what a spawn is asked for and what
    // the spawn hook has in hand. COTL_API's own list is internal, so this is also the only
    // registry of ours that can be read without reflection.
    public static Dictionary<Enemy, CultTweakerCustomEnemy> Registered { get; } = [];

    public static void LoadAllCustomEnemies(MonoBehaviour coroutineHost)
    {
        var loader = new CustomEnemyLoader();
        var entries = loader.LoadAll();

        foreach (var entry in entries)
        {
            try
            {
                var config = entry.Config;
                if (string.IsNullOrWhiteSpace(config.EnemyName))
                {
                    Plugin.Log.LogWarning($"Custom enemy folder '{entry.FolderName}' has no EnemyName, skipped.");
                    continue;
                }

                var internalName = "CultTweaker_" + config.EnemyName.Replace(" ", "_");
                var enemy = new CultTweakerCustomEnemy(internalName, config);

                enemy.SpineOverride = BuildSpine(entry.FolderPath, config, internalName);

                // Add throws on a name it already has, which a second folder of the same name
                // would be; the enemy is skipped rather than taking the rest of the load down.
                var type = CustomEnemyManager.Add(enemy);
                Registered[type] = enemy;

                coroutineHost.StartCoroutine(CustomEnemyManager.BuildEnemyPrefab(enemy));

                Plugin.Log.LogInfo($"Registered custom enemy '{internalName}' mimicking " +
                                   $"{enemy.EnemyToMimic} ({enemy.maxHealth} HP" +
                                   (enemy.Tuning.Count > 0 ? $", {enemy.Tuning.Count} tuned value(s)" : "") +
                                   (enemy.BossHealthBar ? ", boss bar" : "") + ").");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Custom enemy '{entry.FolderName}' failed to load: {e}");
            }
        }
    }

    private static SkeletonDataAsset BuildSpine(string folder, CustomEnemyConfig config, string internalName)
    {
        var data = SpineFolderLoader.Build(folder, internalName, config.SkeletonPath, config.AtlasPath,
            config.TexturePaths, config.SkeletonScale);

        if (data == null)
            Plugin.Log.LogInfo($"Custom enemy '{internalName}': no spine assets in folder, " +
                               "wearing the mimic's own skeleton.");

        return data;
    }
}
