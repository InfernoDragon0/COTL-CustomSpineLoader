using System;
using System.IO;
using COTL_API.CustomEnemy;
using COTL_API.Helpers;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public class BaseCustomEnemy : CustomEnemy
{
    public override string InternalName => "CultTweaker_TestEnemy";
    public override string EnemyToMimic => "Assets/Prefabs/Enemies/DLC/Enemy Swordsman Wolf.prefab";
    public override float maxHealth => 2f;

    public string OverrideSpineName = "SF_Occultist_Scamp";

    public override Type? EnemyController => typeof(CustomEnemyController);

    public static string spineAtlasPath = Path.Combine(Plugin.PluginPath, "Assets/TestEnemy/Human.atlas");
    public static string spineSkeletonPath = Path.Combine(Plugin.PluginPath, "Assets/TestEnemy/Human.json");
    public static string[] spineTexturePaths = [
        Path.Combine(Plugin.PluginPath, "Assets/TestEnemy/Human.png"),
        Path.Combine(Plugin.PluginPath, "Assets/TestEnemy/Human2.png"),
    ];

    public BaseCustomEnemy()
    {
        Plugin.Log.LogInfo("Initializing " + InternalName + " and creating spine override.");
        CreateSpineOverride();
    }

    public void CreateSpineOverride()
    {
        Plugin.Log.LogInfo("Reading atlas from " + spineAtlasPath);
        var atlasTxt = new TextAsset(File.ReadAllText(spineAtlasPath));

        Plugin.Log.LogInfo("Reading skeleton from " + spineSkeletonPath);
        var skele = new TextAsset(File.ReadAllText(spineSkeletonPath));
        var textures = new Texture2D[spineTexturePaths.Length];

        foreach (var textureFile in spineTexturePaths)
        {
            Plugin.Log.LogInfo("Reading texture from " + textureFile);
            Texture2D tex = TextureHelper.CreateTextureFromPath(textureFile);
            tex.name = Path.GetFileNameWithoutExtension(textureFile);
            textures[Array.IndexOf(spineTexturePaths, textureFile)] = tex;
        }

        var mat = new Material(Shader.Find("Spine/Skeleton")); //TODO: find out what shader cotl uses
        var runtimeAtlasAsset = Spine.Unity.SpineAtlasAsset.CreateRuntimeInstance(atlasTxt, textures, mat, true);
        var runtimeSkeletonAsset = Spine.Unity.SkeletonDataAsset.CreateRuntimeInstance(skele, runtimeAtlasAsset, true, 0.005f);
        SpineOverride = runtimeSkeletonAsset;
        SpineSkinName = OverrideSpineName;
    }
}
