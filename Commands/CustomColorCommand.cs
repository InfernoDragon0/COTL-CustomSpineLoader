using System;
using COTL_API.CustomFollowerCommand;
using COTL_API.Helpers;
using Lamb.UI;
using src.UI.Menus;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using src.Extensions;
using TMPro;
using UnityEngine.UI;
using Spine.Unity;
using CustomSpineLoader.SpineLoaderHelper;
using System.Linq;

namespace CustomSpineLoader.Commands;

public class CustomColorCommand : CustomFollowerCommand
{
    public override string InternalName => "CustomColor_Command";

    public override Sprite CommandIcon => TextureHelper.CreateSpriteFromPath(Path.Combine(Plugin.PluginPath, "Assets/colorwheel.png"));
    public override List<FollowerCommandCategory> Categories { get; } = [FollowerCommandCategory.DEFAULT_COMMAND];

    public UIFollowerSummaryMenuController _followerSummaryMenuController;

    public float currentRed = 1f;
    public float currentGreen = 1f;
    public float currentBlue = 1f;
    public float currentAlpha = 1f;

    public float currentScale = 1f;
    public bool isCustomColorEnabled = false;

    public bool isCustomFollowerCostumeEnabled = false;

    public int selectedClothingTypeIndex = 0;
    public int selectedSpecialTypeIndex = 0;
    public int selectedHatTypeIndex = 0;
    public int selectedOutfitTypeIndex = 0;
    public int selectedNecklaceTypeIndex = 0;

    public override string GetTitle(Follower follower)
    {
        return "Customize Follower";
    }

    public override string GetDescription(Follower follower)
    {
        return "Customize Me!";
    }

    public override void Execute(interaction_FollowerInteraction interaction, FollowerCommands finalCommand)
    {
        interaction.StartCoroutine(interaction.FrameDelayCallback(delegate
        {
            interaction.follower.Brain.HardSwapToTask(new FollowerTask_ManualControl());
            _followerSummaryMenuController = MonoSingleton<UIManager>.Instance.ShowFollowerSummaryMenu(interaction.follower);
            
            CreateColorUI();
            _followerSummaryMenuController.OnHidden += () =>
            {
                _followerSummaryMenuController = null;
                if (isCustomColorEnabled)
                {
                    CustomColorHelper.SetCustomColor(interaction.follower.Brain.Info.ID, currentRed, currentGreen, currentBlue, currentAlpha, currentScale);
                    interaction.follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.FindSlot("LEG_LEFT_SKIN").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.FindSlot("LEG_RIGHT_SKIN").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.FindSlot("ARM_RIGHT_SKIN").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.FindSlot("HEAD_SKIN_BTM").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.A = currentAlpha;
                    // interaction.follower.transform.localScale = new Vector3(currentAlpha, currentScale, 1f);
                    interaction.follower.Spine.skeleton.ScaleX = currentScale;
                    interaction.follower.Spine.skeleton.ScaleY = currentScale;

                    Plugin.Log.LogInfo("Scale is set to: " + currentScale + "X is " + interaction.follower.Spine.Skeleton.ScaleX + " Y is " + interaction.follower.Spine.Skeleton.ScaleY);


                    if (isCustomFollowerCostumeEnabled)
                    {
                        CustomColorHelper.SetCustomCostume(
                            interaction.follower.Brain.Info.ID,
                            true,
                            selectedClothingTypeIndex,
                            selectedSpecialTypeIndex,
                            selectedHatTypeIndex,
                            selectedOutfitTypeIndex,
                            selectedNecklaceTypeIndex
                        );
                    }
                    else
                    {
                        CustomColorHelper.SetCustomCostume(
                            interaction.follower.Brain.Info.ID,
                            false,
                            0,
                            0,
                            0,
                            0,
                            0
                        );
                    }
                }
                else
                {
                    CustomColorHelper.RemoveCustomColor(interaction.follower.Brain.Info.ID);
                }

                interaction.Close(true, reshowMenu: false);
            };
            HUD_Manager.Instance.Hide(false, 0);
        }));
        // interaction.Close(true, reshowMenu: false);
    }

    public void CreateColorUI()
    {
        if (_followerSummaryMenuController == null)
        {
            Debug.LogError("Follower Summary Menu Controller is not initialized.");
            return;
        }

        Debug.Log("Creating color picker UI...");
        //first we remove the stuff from menu controller
        Time.timeScale = 1f;
        var background = _followerSummaryMenuController.transform.Find("BlurImageBackground");
        background.gameObject.SetActive(false);
        GameManager.GetInstance().CameraSetOffset(new Vector3(-2f, 0.0f, 0.0f));

        var summaryContainer = _followerSummaryMenuController.transform.Find("FollowerSummaryContainer");
        var left = summaryContainer?.Find("Left");
        var leftTransform = left?.Find("Transform");
        var leftTransformTop = leftTransform?.Find("Top");
        var topHeader = leftTransformTop?.Find("Header");
        var topHeaderTMP = topHeader?.GetComponent<TMP_Text>();
        if (topHeaderTMP != null)
        {
            topHeaderTMP.text = "Customize Follower";
        }

        var leftContent = leftTransform?.Find("Content");
        var leftContentScrollView = leftContent?.Find("Scroll View");
        var leftContentScrollViewViewport = leftContentScrollView?.Find("Viewport");
        var leftContentScrollViewViewportContent = leftContentScrollViewViewport?.Find("Content");

        //follower traits header change to "Custom Color"
        var followerTraitsHeader = leftContentScrollViewViewportContent?.Find("Follower Traits Header");
        var followerTraitsHeaderTMP = followerTraitsHeader?.GetComponent<TMP_Text>();
        if (followerTraitsHeaderTMP != null)
        {
            followerTraitsHeaderTMP.text = "Color preview above.";
        }

        //from the viewport content, disable the following: 
        //Follower Traits Content, Cult Traits Header, Cult Traits Content, Follower Thoughts, Follower Thoughts Content
        var followerTraitsContent = leftContentScrollViewViewportContent?.Find("Follower Traits Content");
        followerTraitsContent?.gameObject.SetActive(false);
        var cultTraitsHeader = leftContentScrollViewViewportContent?.Find("Cult Traits Header");
        var cultTraitsHeaderTMP = cultTraitsHeader?.GetComponent<TMP_Text>();
        if (cultTraitsHeader != null)
        {
            cultTraitsHeaderTMP.text = "Costume Preview on the right.";
        }

        var cultTraitsContent = leftContentScrollViewViewportContent?.Find("Cult Traits Content");
        cultTraitsContent?.gameObject.SetActive(false);
        var followerThoughts = leftContentScrollViewViewportContent?.Find("Follower Thoughts");
        followerThoughts?.gameObject.SetActive(false);
        var followerThoughtsContent = leftContentScrollViewViewportContent?.Find("Follower Thoughts Content");
        followerThoughtsContent?.gameObject.SetActive(false);
        var spacer = leftContentScrollViewViewportContent?.Find("Spacer");

        //then we add the RGB sliders and a checkbox to set enable or disable the custom color
        var sliderTemplate = MonoSingleton<UIManager>.Instance.SettingsMenuControllerTemplate._audioSettings.GetComponentInChildren<ScrollRect>().content.GetChild(0).gameObject;
        var toggleTemplte = MonoSingleton<UIManager>.Instance.SettingsMenuControllerTemplate._graphicsSettings.GetComponentInChildren<ScrollRect>().content.GetChild(4).gameObject;
        var horizontalTemplate = MonoSingleton<UIManager>.Instance.SettingsMenuControllerTemplate._graphicsSettings.GetComponentInChildren<ScrollRect>().content.GetChild(3).gameObject;

        #region CREATE UI HERE

        var toggleCustomColor = UnityEngine.Object.Instantiate(toggleTemplte, leftContentScrollViewViewportContent);
        
        //RGBA Selector
        
        var spacerInstance4 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var sliderRed = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);

        var spacerInstance = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var sliderGreen = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);

        var spacerInstance2 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var sliderBlue = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);

        var spacerInstance3 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var sliderAlpha = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);

        //scale
        var spacerInstance11 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var sliderScale = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);

        //toggle for follow costume enable disable
        var spacerInstance10 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var toggleFollowCostumeEnable = UnityEngine.Object.Instantiate(toggleTemplte, leftContentScrollViewViewportContent);

        //Follower Costume Selector (Special Type and Clothing Type)
        var spacerInstance5 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var horizontalClothingType = UnityEngine.Object.Instantiate(horizontalTemplate, leftContentScrollViewViewportContent);

        var spacerInstance6 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var horizontalSpecialType = UnityEngine.Object.Instantiate(horizontalTemplate, leftContentScrollViewViewportContent);

        //Follower Costume Selector (Hats, Outfit, necklace)
        var spacerInstance7 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var horizontalHatType = UnityEngine.Object.Instantiate(horizontalTemplate, leftContentScrollViewViewportContent);

        var spacerInstance8 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var horizontalOutfitType = UnityEngine.Object.Instantiate(horizontalTemplate, leftContentScrollViewViewportContent);

        var spacerInstance9 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var horizontalNecklaceType = UnityEngine.Object.Instantiate(horizontalTemplate, leftContentScrollViewViewportContent);
        #endregion

        #region SLIDER AND TOGGLE SETUP
        var sliderRedText = sliderRed.GetComponentInChildren<TMP_Text>();
        var sliderGreenText = sliderGreen.GetComponentInChildren<TMP_Text>();
        var sliderBlueText = sliderBlue.GetComponentInChildren<TMP_Text>();
        var sliderAlphaText = sliderAlpha.GetComponentInChildren<TMP_Text>();
        var sliderScaleText = sliderScale.GetComponentInChildren<TMP_Text>();
        var toggleCustomColorText = toggleCustomColor.GetComponentInChildren<TMP_Text>();

        var sliderRedComponent = sliderRed.GetComponentInChildren<MMSlider>();
        var sliderGreenComponent = sliderGreen.GetComponentInChildren<MMSlider>();
        var sliderBlueComponent = sliderBlue.GetComponentInChildren<MMSlider>();
        var sliderAlphaComponent = sliderAlpha.GetComponentInChildren<MMSlider>();
        var sliderScaleComponent = sliderScale.GetComponentInChildren<MMSlider>();
        var toggleCustomColorComponent = toggleCustomColor.GetComponentInChildren<MMToggle>();

        sliderRed.name = "SliderRed";
        sliderGreen.name = "SliderGreen";
        sliderBlue.name = "SliderBlue";
        sliderAlpha.name = "SliderAlpha";
        sliderScale.name = "SliderScale";
        toggleCustomColor.name = "ToggleCustomColor";

        sliderRedText.text = "Red";
        sliderGreenText.text = "Green";
        sliderBlueText.text = "Blue";
        sliderAlphaText.text = "Alpha";
        sliderScaleText.text = "Scale";
        toggleCustomColorText.text = "Enable Customization";

        sliderRedComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Red", value));
        sliderGreenComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Green", value));
        sliderBlueComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Blue", value));
        sliderAlphaComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Alpha", value));
        sliderScaleComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Scale", value));
        toggleCustomColorComponent.OnValueChanged += (value) => OnToggleValueChanged(value, _followerSummaryMenuController._follower);

        sliderRedComponent._increment = 1;
        sliderGreenComponent._increment = 1;
        sliderBlueComponent._increment = 1;
        sliderAlphaComponent._increment = 1;
        sliderScaleComponent._increment = 1;

        sliderRedComponent.value = _followerSummaryMenuController._follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").R * 100f;
        sliderGreenComponent.value = _followerSummaryMenuController._follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").G * 100f;
        sliderBlueComponent.value = _followerSummaryMenuController._follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").B * 100f;
        sliderAlphaComponent.value = _followerSummaryMenuController._follower.Spine.skeleton.A * 100f;

        //scale is from 0.1 to 5.0, a percentage from 0 to 1
        sliderScaleComponent.value = ((currentScale - 0.1f) / 4.9f) * 100f;

        #endregion

        #region Clothing and Special Type Setup
        var toggleFollowCostumeEnableComponent = toggleFollowCostumeEnable.GetComponentInChildren<MMToggle>();
        var toggleFollowCostumeEnableText = toggleFollowCostumeEnable.GetComponentInChildren<TMP_Text>();

        var horizontalClothingTypeText = horizontalClothingType.GetComponentInChildren<TMP_Text>();
        var horizontalSpecialTypeText = horizontalSpecialType.GetComponentInChildren<TMP_Text>();
        var horizontalHatTypeText = horizontalHatType.GetComponentInChildren<TMP_Text>();
        var horizontalOutfitTypeText = horizontalOutfitType.GetComponentInChildren<TMP_Text>();
        var horizontalNecklaceTypeText = horizontalNecklaceType.GetComponentInChildren<TMP_Text>();


        horizontalClothingType.name = "FollowerClothingType";
        horizontalSpecialType.name = "FollowerSpecialOverlayType";
        horizontalHatType.name = "FollowerHatType";
        horizontalOutfitType.name = "FollowerOutfitType";
        horizontalNecklaceType.name = "FollowerNecklaceType";
        toggleFollowCostumeEnable.name = "EnableFollowerCostumeOverride";

        horizontalClothingTypeText.text = "Clothing Type";
        horizontalSpecialTypeText.text = "Special Type";
        horizontalHatTypeText.text = "Hat Type";
        horizontalOutfitTypeText.text = "Outfit Type";
        horizontalNecklaceTypeText.text = "Necklace Type";
        toggleFollowCostumeEnableText.text = "Follower Costume Override";

        //get options for clothing type, use the ENUM and convert to list of strings
        var clothingTypeEnumValues = Enum.GetValues(typeof(FollowerClothingType));
        var clothingTypeOptions = clothingTypeEnumValues.Cast<FollowerClothingType>().Select(x => x.ToString()).ToList();

        var specialTypeEnumValues = Enum.GetValues(typeof(FollowerSpecialType));
        var specialTypeOptions = specialTypeEnumValues.Cast<FollowerSpecialType>().Select(x => x.ToString()).ToList();

        //TODO: this is for a later release, so we mask this first. if the option name in specialTypeOptions contains "Palworld", we change the name to "Unused_enumid"
        for (int i = 0; i < specialTypeOptions.Count; i++)
        {
            if (specialTypeOptions[i].Contains("Palworld"))
            {
                specialTypeOptions[i] = "Unused_" + i;
            }
        }

        var hatTypeEnumValues = Enum.GetValues(typeof(FollowerHatType));
        var hatTypeOptions = hatTypeEnumValues.Cast<FollowerHatType>().Select(x => x.ToString()).ToList();
        var outfitTypeEnumValues = Enum.GetValues(typeof(FollowerOutfitType));
        var outfitTypeOptions = outfitTypeEnumValues.Cast<FollowerOutfitType>().Select(x => x.ToString()).ToList();
        var necklaceTypeValues = DataManager.AllNecklaces.Select(x => x.ToString()).ToList();
        necklaceTypeValues.Insert(0, "Original"); //add none option
        necklaceTypeValues.Insert(1, "None"); //add none option

        toggleFollowCostumeEnableComponent.OnValueChanged += (value) => OnFollowerCostumeToggleChanged(value, _followerSummaryMenuController._follower);

        //get selector an override contents for each
        var horizontalClothingTypeSelector = horizontalClothingType.GetComponentInChildren<MMHorizontalSelector>();
        horizontalClothingTypeSelector._localizeContent = false;
        horizontalClothingTypeSelector.UpdateContent([.. clothingTypeOptions]);
        horizontalClothingTypeSelector.OnSelectionChanged += (index) => { OnFollowerCostumeSelectorsChanged(typeof(FollowerClothingType), index); };

        var horizontalSpecialTypeSelector = horizontalSpecialType.GetComponentInChildren<MMHorizontalSelector>();
        horizontalSpecialTypeSelector._localizeContent = false;
        horizontalSpecialTypeSelector.UpdateContent([.. specialTypeOptions]);
        horizontalSpecialTypeSelector.OnSelectionChanged += (index) => { OnFollowerCostumeSelectorsChanged(typeof(FollowerSpecialType), index); };

        var horizontalHatTypeSelector = horizontalHatType.GetComponentInChildren<MMHorizontalSelector>();
        horizontalHatTypeSelector._localizeContent = false;
        horizontalHatTypeSelector.UpdateContent([.. hatTypeOptions]);
        horizontalHatTypeSelector.OnSelectionChanged += (index) => { OnFollowerCostumeSelectorsChanged(typeof(FollowerHatType), index); };

        var horizontalOutfitTypeSelector = horizontalOutfitType.GetComponentInChildren<MMHorizontalSelector>();
        horizontalOutfitTypeSelector._localizeContent = false;
        horizontalOutfitTypeSelector.UpdateContent([.. outfitTypeOptions]);
        horizontalOutfitTypeSelector.OnSelectionChanged += (index) => { OnFollowerCostumeSelectorsChanged(typeof(FollowerOutfitType), index); };

        //TODO: Necklace Type
        var horizontalNecklaceTypeSelector = horizontalNecklaceType.GetComponentInChildren<MMHorizontalSelector>();
        horizontalNecklaceTypeSelector._localizeContent = false;
        horizontalNecklaceTypeSelector.UpdateContent([.. necklaceTypeValues]);
        horizontalNecklaceTypeSelector.OnSelectionChanged += (index) => { OnFollowerCostumeSelectorsChanged(typeof(InventoryItem.ITEM_TYPE), index); };

        #endregion

        var hasCustomColor = CustomColorHelper.GetCustomColor(_followerSummaryMenuController._follower.Brain.Info.ID);
        if (hasCustomColor != null)
        {
            toggleCustomColorComponent.Value = true;
            toggleCustomColorComponent.UpdateState(true);
            isCustomColorEnabled = true;

            toggleFollowCostumeEnableComponent.Value = hasCustomColor.CustomFollowerCostume;
            toggleFollowCostumeEnableComponent.UpdateState(true);
            isCustomFollowerCostumeEnabled = hasCustomColor.CustomFollowerCostume;

            //set horizontal selectors to current values for cloth, special, hat, outfit, necklace
            horizontalClothingTypeSelector.ContentIndex = Mathf.Clamp(hasCustomColor.FollowerClothingType, 0, clothingTypeOptions.Count - 1);
            horizontalSpecialTypeSelector.ContentIndex = Mathf.Clamp(hasCustomColor.FollowerSpecialType, 0, specialTypeOptions.Count - 1);
            horizontalHatTypeSelector.ContentIndex = Mathf.Clamp(hasCustomColor.FollowerHatType, 0, hatTypeOptions.Count - 1);
            horizontalOutfitTypeSelector.ContentIndex = Mathf.Clamp(hasCustomColor.FollowerOutfitType, 0, outfitTypeOptions.Count - 1);
            horizontalNecklaceTypeSelector.ContentIndex = Mathf.Clamp(hasCustomColor.FollowerNecklaceType, 0, necklaceTypeValues.Count - 1);

            // var indexOverride = Math.Max(0, clothingTypeOptions.IndexOf(indexStringOverride));
            // selector.ContentIndex = indexStringOverride != null ? indexOverride : index;
        }
        else
        {
            toggleCustomColorComponent.Value = false;
            toggleCustomColorComponent.UpdateState(true);
            isCustomColorEnabled = false;

            toggleFollowCostumeEnableComponent.Value = false;
            toggleFollowCostumeEnableComponent.UpdateState(true);
            isCustomFollowerCostumeEnabled = false;

            //get the default values from follower info
            horizontalClothingTypeSelector.ContentIndex = (int)_followerSummaryMenuController._follower.Brain.Info.Clothing;
            horizontalSpecialTypeSelector.ContentIndex = (int)_followerSummaryMenuController._follower.Brain.Info.Special;
            horizontalHatTypeSelector.ContentIndex = (int)_followerSummaryMenuController._follower.Brain.Info.Hat;
            horizontalOutfitTypeSelector.ContentIndex = (int)_followerSummaryMenuController._follower.Brain.Info.Outfit;
            horizontalNecklaceTypeSelector.ContentIndex = 0; //original
        }

        // if (onChange != null) toggleToggle.OnValueChanged += onChange

    }

    public void OnFollowerCostumeSelectorsChanged(Type enumType, int value)
    {
        // Plugin.Log.LogInfo($"Follower costume selector changed: {enumType}, {value}");
        switch (enumType.Name)
        {
            case "FollowerClothingType":
                Plugin.Log.LogInfo($"Setting clothing type to {(FollowerClothingType)value}");
                selectedClothingTypeIndex = value;
                break;
            case "FollowerSpecialType":
                Plugin.Log.LogInfo($"Setting special type to {(FollowerSpecialType)value}");
                selectedSpecialTypeIndex = value;
                break;
            case "FollowerHatType":
                Plugin.Log.LogInfo($"Setting hat type to {(FollowerHatType)value}");
                selectedHatTypeIndex = value;
                break;
            case "FollowerOutfitType":
                Plugin.Log.LogInfo($"Setting outfit type to {(FollowerOutfitType)value}");
                selectedOutfitTypeIndex = value;
                break;
            case "ITEM_TYPE":
                Plugin.Log.LogInfo($"Setting necklace type to {(InventoryItem.ITEM_TYPE)value}");
                selectedNecklaceTypeIndex = value;
                break;
            default:
                Plugin.Log.LogWarning("Unknown enum type for follower costume selector change: " + enumType.Name);
                break;
        }
        if (!isCustomFollowerCostumeEnabled || !isCustomColorEnabled)
        {
            Plugin.Log.LogInfo("Custom follower costume not enabled, skipping costume set.");
            return;
        }

        Plugin.Log.LogInfo("Setting costume override now... overrides are: " +
            $"ClothingType: {(FollowerClothingType)selectedClothingTypeIndex}, " +
            $"SpecialType: {(FollowerSpecialType)selectedSpecialTypeIndex}, " +
            $"HatType: {(FollowerHatType)selectedHatTypeIndex}, " +
            $"OutfitType: {(FollowerOutfitType)selectedOutfitTypeIndex}");
        try
        {
            //snowman special check
            var special = (FollowerSpecialType)selectedSpecialTypeIndex;
            var skinName = _followerSummaryMenuController._follower.Brain.Info.SkinName;
            
            if (special is FollowerSpecialType.Snowman_Bad or FollowerSpecialType.Snowman_Average or FollowerSpecialType.Snowman_Great)
            {
                skinName = GetSnowmanRandomSkin((FollowerSpecialType)selectedSpecialTypeIndex);
                Plugin.Log.LogInfo("Applying snowman skin: " + skinName);
            }

            FollowerBrain.SetFollowerCostume(
                _followerSummaryMenuController._follower.Spine.skeleton,
                _followerSummaryMenuController._follower.Brain.Info.XPLevel,
                skinName,
                _followerSummaryMenuController._follower.Brain.Info.SkinColour,
                (FollowerOutfitType)selectedOutfitTypeIndex,
                (FollowerHatType)selectedHatTypeIndex,
                (FollowerClothingType)selectedClothingTypeIndex,
                _followerSummaryMenuController._follower.Brain.Info.Customisation,
                special,
                GetNecklaceToUse(selectedNecklaceTypeIndex, _followerSummaryMenuController._follower.Brain._directInfoAccess),
                "",
                _followerSummaryMenuController._follower.Brain._directInfoAccess
            );
            Plugin.Log.LogInfo("Costume override applied successfully.");
        }
        catch (Exception)
        {
            Plugin.Log.LogWarning("The costume combinations were invalid, try another!");
        }

    }

    public static string GetSnowmanRandomSkin(FollowerSpecialType snowmanType)
    {
        var prefix = snowmanType switch
        {
            FollowerSpecialType.Snowman_Bad => "Snowman/Bad_",
            FollowerSpecialType.Snowman_Average => "Snowman/Mid_",
            FollowerSpecialType.Snowman_Great => "Snowman/Good_",
            _ => "Snowman/Good_"
        };
        var str = $"{prefix}{UnityEngine.Random.Range(1, 4)}";
        return str;
    }
    
    public static InventoryItem.ITEM_TYPE GetNecklaceToUse(int selectedNecklaceTypeIndex, FollowerInfo follower)
    {
        var boundCheck = selectedNecklaceTypeIndex - 2;
        if (selectedNecklaceTypeIndex > 1 && (boundCheck < 0 || boundCheck >= DataManager.AllNecklaces.Count))
        {
            Plugin.Log.LogWarning("Selected necklace type index is out of bounds, defaulting to Original.");
            return follower.Necklace;
        }
        return selectedNecklaceTypeIndex switch
        {
                0 => follower.Necklace, //Original
                1 => InventoryItem.ITEM_TYPE.NONE, //None
                _ => DataManager.AllNecklaces[selectedNecklaceTypeIndex - 2],
        };
    }
    
    public void OnFollowerCostumeToggleChanged(bool value, Follower follower)
    {
        Debug.Log($"Follower costume toggle changed to {value}");
        isCustomFollowerCostumeEnabled = value;
        if (!isCustomFollowerCostumeEnabled)
        {
            Debug.Log("Custom follower costume disabled, reset costume.");
            FollowerBrain.SetFollowerCostume(
                follower.Spine.skeleton,
                follower.Brain.Info.XPLevel,
                follower.Brain.Info.SkinName,
                follower.Brain.Info.SkinColour,
                follower.Brain.Info.Outfit,
                follower.Brain.Info.Hat,
                follower.Brain.Info.Clothing,
                follower.Brain.Info.Customisation,
                follower.Brain.Info.Special,
                follower.Brain.Info.Necklace,
                "",
                follower.Brain._directInfoAccess
            );
            return;
        }
        else
        {
            OnFollowerCostumeSelectorsChanged(typeof(FollowerClothingType), selectedClothingTypeIndex);
        }
    }

    public void OnToggleValueChanged(bool value, Follower follower)
    {
        Debug.Log($"Toggle value changed to {value}");
        isCustomColorEnabled = value;

        if (!isCustomColorEnabled)
        {
            Debug.Log("Custom color disabled, reset to default colors.");
            FollowerBrain.SetFollowerCostume(
                follower.Spine.skeleton,
                follower.Brain.Info.XPLevel,
                follower.Brain.Info.SkinName,
                follower.Brain.Info.SkinColour,
                follower.Brain.Info.Outfit,
                follower.Brain.Info.Hat,
                follower.Brain.Info.Clothing,
                follower.Brain.Info.Customisation,
                follower.Brain.Info.Special,
                follower.Brain.Info.Necklace,
                "",
                follower.Brain._directInfoAccess
            );
        }
    }

    public void OnSliderValueChanged(string color, float value)
    {
        value /= 100f;
        Debug.Log($"Slider {color} value changed to {value}");
        if (_followerSummaryMenuController == null)
        {
            Debug.LogError("Follower Summary Menu Controller is not initialized.");
            return;
        }
        // You can also update the follower's color here based on the slider values
        var follower = _followerSummaryMenuController._follower;
        var followerInfoBoxSpine = _followerSummaryMenuController._infoBox.FollowerSpine;

        //ARM_LEFT_SKIN, LEG_LEFT_SKIN, LEG_RIGHT_SKIN, ARM_RIGHT_SKIN, HEAD_SKIN_BTM, HEAD_SKIN_BTM_BACK
        var originalColor = follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").GetColor();
        switch (color)
        {
            case "Red":
                currentRed = value;
                // follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                // follower.Spine.skeleton.FindSlot("LEG_LEFT_SKIN").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                // follower.Spine.skeleton.FindSlot("LEG_RIGHT_SKIN").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                // follower.Spine.skeleton.FindSlot("ARM_RIGHT_SKIN").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                // follower.Spine.skeleton.FindSlot("HEAD_SKIN_BTM").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                // follower.Spine.skeleton.FindSlot("HEAD_SKIN_BTM_BACK").R = value;
                followerInfoBoxSpine.skeleton.FindSlot("ARM_LEFT_SKIN").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                followerInfoBoxSpine.skeleton.FindSlot("LEG_LEFT_SKIN").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                followerInfoBoxSpine.skeleton.FindSlot("LEG_RIGHT_SKIN").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                followerInfoBoxSpine.skeleton.FindSlot("ARM_RIGHT_SKIN").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                followerInfoBoxSpine.skeleton.FindSlot("HEAD_SKIN_BTM").R = value; //.SetColor(new Color(value, originalColor.g, originalColor.b, originalColor.a));
                // followerInfoBoxSpine.skeleton.FindSlot("HEAD_SKIN_BTM_BACK").R = value;
                break;
            case "Green":
                currentGreen = value;
                // follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").G = value;
                // follower.Spine.skeleton.FindSlot("LEG_LEFT_SKIN").G = value;
                // follower.Spine.skeleton.FindSlot("LEG_RIGHT_SKIN").G = value;
                // follower.Spine.skeleton.FindSlot("ARM_RIGHT_SKIN").G = value;
                // follower.Spine.skeleton.FindSlot("HEAD_SKIN_BTM").G = value;
                // follower.Spine.skeleton.FindSlot("HEAD_SKIN_BTM_BACK").G = value;
                followerInfoBoxSpine.skeleton.FindSlot("ARM_LEFT_SKIN").G = value;
                followerInfoBoxSpine.skeleton.FindSlot("LEG_LEFT_SKIN").G = value;
                followerInfoBoxSpine.skeleton.FindSlot("LEG_RIGHT_SKIN").G = value;
                followerInfoBoxSpine.skeleton.FindSlot("ARM_RIGHT_SKIN").G = value;
                followerInfoBoxSpine.skeleton.FindSlot("HEAD_SKIN_BTM").G = value;
                // followerInfoBoxSpine.skeleton.FindSlot("HEAD_SKIN_BTM_BACK").G = value;
                break;
            case "Blue":
                currentBlue = value;
                // follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").B = value;
                // follower.Spine.skeleton.FindSlot("LEG_LEFT_SKIN").B = value;
                // follower.Spine.skeleton.FindSlot("LEG_RIGHT_SKIN").B = value;
                // follower.Spine.skeleton.FindSlot("ARM_RIGHT_SKIN").B = value;
                // follower.Spine.skeleton.FindSlot("HEAD_SKIN_BTM").B = value;
                // follower.Spine.skeleton.FindSlot("HEAD_SKIN_BTM_BACK").B = value;
                followerInfoBoxSpine.skeleton.FindSlot("ARM_LEFT_SKIN").B = value;
                followerInfoBoxSpine.skeleton.FindSlot("LEG_LEFT_SKIN").B = value;
                followerInfoBoxSpine.skeleton.FindSlot("LEG_RIGHT_SKIN").B = value;
                followerInfoBoxSpine.skeleton.FindSlot("ARM_RIGHT_SKIN").B = value;
                followerInfoBoxSpine.skeleton.FindSlot("HEAD_SKIN_BTM").B = value;
                // followerInfoBoxSpine.skeleton.FindSlot("HEAD_SKIN_BTM_BACK").B = value;
                break;
            case "Alpha":
                currentAlpha = value;
                // follower.Spine.skeleton.A = value;
                followerInfoBoxSpine.color = new Color(followerInfoBoxSpine.color.r, followerInfoBoxSpine.color.g, followerInfoBoxSpine.color.b, value);
                break;
            case "Scale":
            // scale from 0.1 to 5.0, value is from 0 to 100
                currentScale = (value * 4.9f) + 0.1f;
                followerInfoBoxSpine.transform.localScale = new Vector3(currentScale, currentScale, 1f);
                break;
        }
    }
}