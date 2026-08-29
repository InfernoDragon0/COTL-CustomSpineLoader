using System;
using System.Collections.Generic;
using COTL_API.CustomEnemy;
using CustomSpineLoader.SpineLoaderHelper;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public class CustomEnemyConfig
{
    public string EnemyName = "";

    public string Mimic = "Assets/Prefabs/Enemies/Enemy Bat.prefab";

    public float Health = 5f;

    public string SkeletonPath = "";
    public string AtlasPath = "";
    public string[] TexturePaths = [];
    public string SkinName = "";
    public float SkeletonScale = 0.005f;

    public float Scale = 1f;

    public bool BossHealthBar;

    public string BossBarName = "";

    public Dictionary<string, float> Tuning = [];
}

public class CultTweakerCustomEnemy : CustomEnemy
{
    private readonly string _internalName;
    private readonly string _mimic;
    private readonly float _health;

    public override string InternalName => _internalName;
    public override string EnemyToMimic => _mimic;
    public override float maxHealth => _health;

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
