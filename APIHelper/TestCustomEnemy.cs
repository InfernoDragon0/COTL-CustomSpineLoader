using System;
using COTL_API.CustomEnemy;

namespace CustomSpineLoader.APIHelper;

public class TestCustomEnemy : CustomEnemy
{
    public override string InternalName => "CultTweaker_TestEnemy";
    public override string EnemyToMimic => "Assets/Prefabs/Enemies/DLC/Enemy Swordsman Wolf.prefab";
    public override float maxHealth => 2f;
}
