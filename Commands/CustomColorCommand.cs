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
    public bool isCustomColorEnabled = false;

    public override string GetTitle(Follower follower)
    {
        return "Custom Color";
    }

    public override string GetDescription(Follower follower)
    {
        return "Recolor Me!";
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
                    CustomColorHelper.SetCustomColor(interaction.follower.Brain.Info.ID, currentRed, currentGreen, currentBlue, currentAlpha);
                    interaction.follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.FindSlot("LEG_LEFT_SKIN").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.FindSlot("LEG_RIGHT_SKIN").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.FindSlot("ARM_RIGHT_SKIN").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.FindSlot("HEAD_SKIN_BTM").SetColor(new Color(currentRed, currentGreen, currentBlue, 1));
                    interaction.follower.Spine.skeleton.A = currentAlpha;
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
        var summaryContainer = _followerSummaryMenuController.transform.Find("FollowerSummaryContainer");
        var left = summaryContainer?.Find("Left");
        var leftTransform = left?.Find("Transform");
        var leftTransformTop = leftTransform?.Find("Top");
        var topHeader = leftTransformTop?.Find("Header");
        var topHeaderTMP = topHeader?.GetComponent<TMP_Text>();
        if (topHeaderTMP != null)
        {
            topHeaderTMP.text = "Custom Color";
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
            followerTraitsHeaderTMP.text = "Select Custom Color";
        }

        //from the viewport content, disable the following: 
        //Follower Traits Content, Cult Traits Header, Cult Traits Content, Follower Thoughts, Follower Thoughts Content
        var followerTraitsContent = leftContentScrollViewViewportContent?.Find("Follower Traits Content");
        followerTraitsContent?.gameObject.SetActive(false);
        var cultTraitsHeader = leftContentScrollViewViewportContent?.Find("Cult Traits Header");
        var cultTraitsHeaderTMP = cultTraitsHeader?.GetComponent<TMP_Text>();
        if (cultTraitsHeaderTMP != null)
        {
            cultTraitsHeaderTMP.text = "Body Color";
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

        var sliderRed = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);
        //spacer
        var spacerInstance = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var sliderGreen = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);

        var spacerInstance2 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var sliderBlue = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);

        var spacerInstance3 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var sliderAlpha = UnityEngine.Object.Instantiate(sliderTemplate, leftContentScrollViewViewportContent);

        var spacerInstance4 = UnityEngine.Object.Instantiate(spacer, leftContentScrollViewViewportContent);
        var toggleCustomColor = UnityEngine.Object.Instantiate(toggleTemplte, leftContentScrollViewViewportContent);

        var sliderRedText = sliderRed.GetComponentInChildren<TMP_Text>();
        var sliderGreenText = sliderGreen.GetComponentInChildren<TMP_Text>();
        var sliderBlueText = sliderBlue.GetComponentInChildren<TMP_Text>();
        var sliderAlphaText = sliderAlpha.GetComponentInChildren<TMP_Text>();
        var toggleCustomColorText = toggleCustomColor.GetComponentInChildren<TMP_Text>();

        var sliderRedComponent = sliderRed.GetComponentInChildren<MMSlider>();
        var sliderGreenComponent = sliderGreen.GetComponentInChildren<MMSlider>();
        var sliderBlueComponent = sliderBlue.GetComponentInChildren<MMSlider>();
        var sliderAlphaComponent = sliderAlpha.GetComponentInChildren<MMSlider>();
        var toggleCustomColorComponent = toggleCustomColor.GetComponentInChildren<MMToggle>();

        sliderRed.name = "SliderRed";
        sliderGreen.name = "SliderGreen";
        sliderBlue.name = "SliderBlue";
        sliderAlpha.name = "SliderAlpha";
        toggleCustomColor.name = "ToggleCustomColor";

        sliderRedText.text = "Red";
        sliderGreenText.text = "Green";
        sliderBlueText.text = "Blue";
        sliderAlphaText.text = "Alpha";
        toggleCustomColorText.text = "Enable Custom Color";

        sliderRedComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Red", value));
        sliderGreenComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Green", value));
        sliderBlueComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Blue", value));
        sliderAlphaComponent.onValueChanged.AddListener(value => OnSliderValueChanged("Alpha", value));
        toggleCustomColorComponent.OnValueChanged += OnToggleValueChanged;

        sliderRedComponent._increment = 1;
        sliderGreenComponent._increment = 1;
        sliderBlueComponent._increment = 1;
        sliderAlphaComponent._increment = 1;

        sliderRedComponent.value = _followerSummaryMenuController._follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").R * 100f;
        sliderGreenComponent.value = _followerSummaryMenuController._follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").G * 100f;
        sliderBlueComponent.value = _followerSummaryMenuController._follower.Spine.skeleton.FindSlot("ARM_LEFT_SKIN").B * 100f;
        sliderAlphaComponent.value = _followerSummaryMenuController._follower.Spine.skeleton.A * 100f;

        var hasCustomColor = CustomColorHelper.GetCustomColor(_followerSummaryMenuController._follower.Brain.Info.ID);
        if (hasCustomColor != null)
        {
            toggleCustomColorComponent.Value = true;
            toggleCustomColorComponent.UpdateState(true);
            isCustomColorEnabled = true;
        }
        else
        {
            toggleCustomColorComponent.Value = false;
            toggleCustomColorComponent.UpdateState(true);
            isCustomColorEnabled = false;
        }
        
        // if (onChange != null) toggleToggle.OnValueChanged += onChange

    }

    public void OnToggleValueChanged(bool value)
    {
        Debug.Log($"Toggle value changed to {value}");
        isCustomColorEnabled = value;
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
        }



    }
}