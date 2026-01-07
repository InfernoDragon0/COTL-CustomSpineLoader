using System;
using System.Collections.Generic;
using System.IO;
using COTL_API.CustomTarotCard;
using COTL_API.Helpers;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public class CustomTarotLoader : Loader<CustomTarotConfig>
{
    public static List<TarotCards.Card> loadedTarots = [];

    public CustomTarotLoader() : base("CustomTarotCards") { }

    public static void LoadAllCustomTarots()
    {
        var loader = new CustomTarotLoader();
        var entries = loader.LoadAll();

        foreach (var entry in entries)
        {
            var cfg = entry.Config;
            var folder = entry.FolderName;
            Plugin.Log.LogInfo("Found custom tarot folder: " + folder);

            var internalName = "CULT_TWEAKER_TAROT_" + (cfg.CardName ?? "UNKNOWN").ToUpper().Replace(" ", "_");

            Plugin.Log.LogInfo("Trying to create custom tarot card : " + cfg.CardName);

            var spritePath = string.IsNullOrEmpty(cfg.SpritePath) ? string.Empty : Path.Combine(entry.FolderPath, cfg.SpritePath);
            if (!string.IsNullOrEmpty(spritePath) && !File.Exists(spritePath))
            {
                Plugin.Log.LogWarning("Sprite file not found for tarot " + cfg.CardName + " at path: " + spritePath + ", skipping sprite (this may be optional)");
            }

            var backSpritePath = string.IsNullOrEmpty(cfg.BackSpritePath) ? string.Empty : Path.Combine(entry.FolderPath, cfg.BackSpritePath);
            if (!string.IsNullOrEmpty(backSpritePath) && !File.Exists(backSpritePath))
            {
                // back sprite optional
            }

            try
            {
                var tarot = new CultTweakerCustomTarot(internalName, cfg, spritePath, backSpritePath);
                Plugin.Log.LogInfo("Successfully created custom tarot with internal name : " + tarot.InternalName);

                loadedTarots.Add(CustomTarotCardManager.Add(tarot));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to create custom tarot : " + cfg.CardName);
                Plugin.Log.LogError(e.ToString());
            }
        }
    }
}

[Serializable]
public class CustomTarotConfig
{
    public string CardName;
    public string SpritePath;
    public string BackSpritePath;
    // Keep category as string to avoid depending on game enums in this helper
    public string Category = "Custom";
    public int TarotCardWeight = 150;
    public int MaxTarotCardLevel = 0;
    public bool IsCursedRelated = false;

    public string Lore = "Custom Tarot created with CultTweaker.";
    public string Description = "This is a custom tarot created with CultTweaker.";
    
    // Gameplay/value overrides mapped from CustomTarotCard virtual methods
    public float SpiritHeartCount = 0f;
    public int SpiritAmmoCount = 0;

    public float WeaponDamageMultiplierIncrease = 0f;
    public float CurseDamageMultiplierIncrease = 0f;
    public float WeaponCritChanceIncrease = 0f;

    // Note: original method had an InventoryItem.ITEM_TYPE parameter; config provides a simple int modifier.
    public int LootIncreaseModifier = 0;

    public float MovementSpeedMultiplier = 0f;
    public float AttackRateMultiplier = 0f;
    public float BlackSoulsMultiplier = 0f;
    public float HealChance = 0f;
    public float NegateDamageChance = 0f;
    public int DamageAllEnemiesAmount = 0;
    public int HealthAmountMultiplier = 0;
    public float AmmoEfficiency = 0f;
    public int BlackSoulsOnDamage = 0;

    // Item to drop can be represented by internal item name (string). If unset, no item will be dropped.
    public string ItemToDropInternalName = null;

    public float ChanceOfGainingBlueHeart = 0f;
    public float ChanceForRelicsMultiplier = 0f;
    public float RelicChargeMultiplier = 0f;
}

// Minimal custom tarot card implementation that reads values from config.
public class CultTweakerCustomTarot(string internalName, CustomTarotConfig cfg, string spritePath, string backSpritePath) : CustomTarotCard
{
    private readonly string _internalName = internalName;
    private readonly string _cardName = cfg.CardName ?? internalName;
    private readonly string _spritePath = string.IsNullOrEmpty(spritePath) ? cfg.SpritePath : spritePath;
    private readonly string _backSpritePath = string.IsNullOrEmpty(backSpritePath) ? cfg.BackSpritePath : backSpritePath;

    private readonly TarotCards.CardCategory _category;
    private readonly int _tarotCardWeight = cfg.TarotCardWeight;
    private readonly int _maxTarotCardLevel = cfg.MaxTarotCardLevel;
    private readonly bool _isCursedRelated = cfg.IsCursedRelated;
    private readonly string _lore = cfg.Lore;
    private readonly string _description = cfg.Description;

    // gameplay values from config
    private readonly float _spiritHeartCount = cfg.SpiritHeartCount;
    private readonly int _spiritAmmoCount = cfg.SpiritAmmoCount;
    private readonly float _weaponDamageMultiplierIncrease = cfg.WeaponDamageMultiplierIncrease;
    private readonly float _curseDamageMultiplierIncrease = cfg.CurseDamageMultiplierIncrease;
    private readonly float _weaponCritChanceIncrease = cfg.WeaponCritChanceIncrease;
    private readonly int _lootIncreaseModifier = cfg.LootIncreaseModifier;
    private readonly float _movementSpeedMultiplier = cfg.MovementSpeedMultiplier;
    private readonly float _attackRateMultiplier = cfg.AttackRateMultiplier;
    private readonly float _blackSoulsMultiplier = cfg.BlackSoulsMultiplier;
    private readonly float _healChance = cfg.HealChance;
    private readonly float _negateDamageChance = cfg.NegateDamageChance;
    private readonly int _damageAllEnemiesAmount = cfg.DamageAllEnemiesAmount;
    private readonly int _healthAmountMultiplier = cfg.HealthAmountMultiplier;
    private readonly float _ammoEfficiency = cfg.AmmoEfficiency;
    private readonly int _blackSoulsOnDamage = cfg.BlackSoulsOnDamage;
    private readonly string _itemToDropInternalName = cfg.ItemToDropInternalName;
    private readonly float _chanceOfGainingBlueHeart = cfg.ChanceOfGainingBlueHeart;
    private readonly float _chanceForRelicsMultiplier = cfg.ChanceForRelicsMultiplier;
    private readonly float _relicChargeMultiplier = cfg.RelicChargeMultiplier;

    public override string InternalName => _internalName;

    public override TarotCards.CardCategory Category => _category;

    public override Sprite CustomSprite => TextureHelper.CreateSpriteFromPath(_spritePath);

    public override Sprite CustomBackSprite => TextureHelper.CreateSpriteFromPath(_backSpritePath);

    public override string Skin => "Custom";

    public override int TarotCardWeight => _tarotCardWeight;

    public override int MaxTarotCardLevel => _maxTarotCardLevel;

    public override string AnimationSuffix => $"Card {InternalName} Animation Suffix not set";

    public override bool IsCursedRelated => _isCursedRelated;

    public override string LocalisedName() => _cardName;

    public override string LocalisedDescription() => _description;

    public override string LocalisedLore() => _lore;

    public override float GetSpiritHeartCount(TarotCards.TarotCard card) => _spiritHeartCount;

    public override int GetSpiritAmmoCount(TarotCards.TarotCard card) => _spiritAmmoCount;

    public override float GetWeaponDamageMultiplerIncrease(TarotCards.TarotCard card) => _weaponDamageMultiplierIncrease;

    public override float GetCurseDamageMultiplerIncrease(TarotCards.TarotCard card) => _curseDamageMultiplierIncrease;

    public override float GetWeaponCritChanceIncrease(TarotCards.TarotCard card) => _weaponCritChanceIncrease;

    public override int GetLootIncreaseModifier(TarotCards.TarotCard card, InventoryItem.ITEM_TYPE itemType) => _lootIncreaseModifier;

    public override float GetMovementSpeedMultiplier(TarotCards.TarotCard card) => _movementSpeedMultiplier;

    public override float GetAttackRateMultiplier(TarotCards.TarotCard card) => _attackRateMultiplier;

    public override float GetBlackSoulsMultiplier(TarotCards.TarotCard card) => _blackSoulsMultiplier;

    public override float GetHealChance(TarotCards.TarotCard card) => _healChance;

    public override float GetNegateDamageChance(TarotCards.TarotCard card) => _negateDamageChance;

    public override int GetDamageAllEnemiesAmount(TarotCards.TarotCard card) => _damageAllEnemiesAmount;

    public override int GetHealthAmountMultiplier(TarotCards.TarotCard card) => _healthAmountMultiplier;

    public override float GetAmmoEfficiency(TarotCards.TarotCard card) => _ammoEfficiency;

    public override int GetBlackSoulsOnDamage(TarotCards.TarotCard card) => _blackSoulsOnDamage;

    public override InventoryItem GetItemToDrop(TarotCards.TarotCard card)
    {
        return null;
    }

    public override float GetChanceOfGainingBlueHeart(TarotCards.TarotCard card) => _chanceOfGainingBlueHeart;

    public override float GetChanceForRelicsMultiplier(TarotCards.TarotCard card) => _chanceForRelicsMultiplier;

    public override float GetRelicChargeMultiplier(TarotCards.TarotCard card) => _relicChargeMultiplier;

    public override void ApplyInstantEffects(TarotCards.TarotCard card) { }
}