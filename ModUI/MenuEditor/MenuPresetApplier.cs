using System;
using CustomSpineLoader.MapEditor;
using DG.Tweening;   // Tween.Kill, for disarming the menu's own theme tween
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI.MenuEditor;

public static class MenuPresetApplier
{
    public static CTMenuPreset Active { get; private set; }

    public static void Use(CTMenuPreset preset) => Active = preset;

    public static void LoadConfigured()
    {
        if (Plugin.MainMenuEnabled is { Value: false })
        {
            Active = null;
            return;
        }

        var name = Plugin.MainMenuPreset?.Value ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            Active = null;
            return;
        }

        Active = CTMenuPresetSerialization.LoadByName(name);
        if (Active == null)
            Plugin.Log.LogWarning($"MenuEditor: no main menu preset named '{name}'; the menu is " +
                                  "left as the game draws it.");
    }

    // ---- the passes -------------------------------------------------------------------------------

    public static void ApplyStructural()
    {
        if (!MenuSceneRefs.Ready) return;

        Guarded("the title", ApplyTitle);
        Guarded("the centrepiece", ApplyCentrepiece);
    }

    public static void ApplyLook()
    {
        if (!MenuSceneRefs.Ready) return;

        MenuSceneRefs.CaptureLate();

        Guarded("the look", ApplyStylizer);
        Guarded("the scene effects", ApplyEffects);
    }

    public static void ApplyAll()
    {
        ApplyStructural();
        ApplyLook();
    }

    public static void RevertAll()
    {
        if (!MenuSceneRefs.Ready) return;

        Guarded("the look", MenuSceneRefs.RestoreLook);
        Guarded("the centrepiece", RevertCentrepiece);
        Guarded("the title", RevertTitle);
    }

    private static void Guarded(string what, Action act)
    {
        try
        {
            act();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MenuEditor: could not apply {what}; it is left as the game " +
                                  $"draws it. {e}");
        }
    }

    // ---- the look ---------------------------------------------------------------------------------

    private static void ApplyStylizer()
    {
        var stylizer = MenuSceneRefs.Stylizer;
        if (stylizer == null) return;

        var look = Active?.Look;
        if (look is not { Enabled: true })
        {
            MenuSceneRefs.RestoreLook();
            return;
        }

        var palette = MenuAssets.PaletteByName(look.Palette) ?? MenuSceneRefs.Look.Palette2;
        palette = MenuAssets.Recoloured(palette, look.HueShift, look.Saturation, look.Brightness);

        if (palette != null) stylizer.Palette2 = palette;
        stylizer.UseSecondPalette = true;
        stylizer.LerpPalette = Mathf.Clamp01(look.Blend);

        var controller = MenuSceneRefs.Controller;
        if (controller != null)
        {
            controller.tween?.Kill();
            controller.isMajorDLC = false;
        }

        stylizer.EffectIntensity = look.EffectIntensity;
        stylizer.Dither = look.Dither;
        stylizer.Grain = look.Grain;

    }

    private static void ApplyEffects()
    {
        var look = Active?.Look;
        var driven = look is { Enabled: true };

        var glitch = MenuSceneRefs.GlitchEffect;
        if (glitch != null) glitch.SetActive(driven ? look.Glitch : MenuSceneRefs.Look.Glitch);

        ApplyBackground(driven ? look : null);
    }

    private static void ApplyBackground(CTMenuLook look)
    {
        var overlay = MenuSceneRefs.Background;
        if (overlay == null) return;

        var baseline = MenuSceneRefs.Look;

        ApplyClearColour(look);

        if (look is not { Background: true })
        {
            overlay.enabled = baseline.BackgroundOn;
            overlay.color = baseline.BackgroundColor;
            return;
        }

        var guard = overlay.GetComponent<DarkModeObject>();
        if (guard != null) UnityEngine.Object.Destroy(guard);

        overlay.enabled = true;

        var colour = baseline.BackgroundColor;
        overlay.color = new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(look.BackgroundStrength));
    }

    private static void ApplyClearColour(CTMenuLook look)
    {
        var camera = MenuSceneRefs.MenuCamera;
        if (camera == null) return;

        camera.backgroundColor = look is { Enabled: true, OverrideClear: true }
            ? look.ClearColor?.ToColor() ?? MenuSceneRefs.Look.ClearColor
            : MenuSceneRefs.Look.ClearColor;
    }

    // ---- the centrepiece ---------------------------------------------------------------------------

    private static void ApplyCentrepiece()
    {
        var spine = MenuSceneRefs.Lamb;
        if (spine == null) return;

        var wanted = Active?.Centrepiece;
        if (wanted is not { Enabled: true })
        {
            RevertCentrepiece();
            return;
        }

        var asset = ResolveAsset(wanted);
        if (asset == null) return;

        if (DressNeeded(spine, asset, wanted))
            Dress(spine, asset, wanted.Skin, wanted.Animation, wanted.Loop, wanted.TimeScale);
        else
            spine.timeScale = wanted.TimeScale <= 0f ? 1f : wanted.TimeScale;

        Place(spine, wanted);
    }

    private static int _dressedBind = -1;
    private static SkeletonDataAsset _dressedAsset;
    private static string _dressedSkin;
    private static string _dressedAnimation;
    private static bool _dressedLoop;

    private static bool DressNeeded(SkeletonAnimation spine, SkeletonDataAsset asset,
        CTMenuCentrepiece wanted) =>
        _dressedBind != MenuSceneRefs.BindCount ||
        spine.skeletonDataAsset != asset ||
        _dressedAsset != asset ||
        _dressedSkin != (wanted.Skin ?? "") ||
        _dressedAnimation != (wanted.Animation ?? "") ||
        _dressedLoop != wanted.Loop;

    private static SkeletonDataAsset ResolveAsset(CTMenuCentrepiece wanted)
    {
        var preset = Active?.PresetName ?? "";

        switch (wanted.Source)
        {
            case "Folder":
                var folder = MenuAssets.GetSkeleton(preset, wanted.Asset);
                if (folder == null)
                    Plugin.Log.LogWarning($"MenuEditor: the centrepiece spine '{wanted.Asset}' " +
                                          "could not be loaded; the menu's own is kept.");
                return folder ?? MenuSceneRefs.Centrepiece.Asset;

            default:
                return MenuSceneRefs.Centrepiece.Asset;
        }
    }

    private static void Dress(SkeletonAnimation spine, SkeletonDataAsset asset, string skin,
        string animation, bool loop, float timeScale)
    {
        _dressedBind = MenuSceneRefs.BindCount;
        _dressedAsset = asset;
        _dressedSkin = skin ?? "";
        _dressedAnimation = animation ?? "";
        _dressedLoop = loop;

        spine.skeletonDataAsset = asset;

        var data = asset.GetSkeletonData(false);

        if (!string.IsNullOrEmpty(skin) && data?.FindSkin(skin) == null)
        {
            Plugin.Log.LogWarning($"MenuEditor: the centrepiece skeleton has no skin '{skin}'; " +
                                  "its default skin is used.");
            skin = "";
        }

        spine.initialSkinName = string.IsNullOrEmpty(skin) ? null : skin;
        spine.Initialize(true);
        spine.Skeleton?.SetToSetupPose();

        if (!string.IsNullOrEmpty(animation) && data?.FindAnimation(animation) == null)
        {
            Plugin.Log.LogWarning($"MenuEditor: the centrepiece skeleton has no animation " +
                                  $"'{animation}'; it is left in its setup pose.");
            animation = "";
        }

        if (!string.IsNullOrEmpty(animation))
            spine.AnimationState?.SetAnimation(0, animation, loop);

        spine.timeScale = timeScale <= 0f ? 1f : timeScale;
        spine.Update(0f);
    }

    private static void Place(SkeletonAnimation spine, CTMenuCentrepiece wanted)
    {
        var baseline = MenuSceneRefs.Centrepiece;
        var t = spine.transform;

        var offset = wanted.Offset?.ToVector3() ?? Vector3.zero;
        var scale = wanted.Scale?.ToVector3() ?? Vector3.one;
        var rotation = wanted.Rotation?.ToVector3() ?? Vector3.zero;

        t.localPosition = baseline.Position + offset;

        t.localScale = new Vector3(
            baseline.Scale.x * (scale.x == 0f ? 1f : scale.x),
            baseline.Scale.y * (scale.y == 0f ? 1f : scale.y),
            baseline.Scale.z * (scale.z == 0f ? 1f : scale.z));

        t.localEulerAngles = baseline.Euler + rotation;
    }

    private static void RevertCentrepiece()
    {
        var spine = MenuSceneRefs.Lamb;
        var baseline = MenuSceneRefs.Centrepiece;
        if (spine == null || baseline.Asset == null) return;

        if (spine.skeletonDataAsset != baseline.Asset || spine.initialSkinName != baseline.Skin)
            Dress(spine, baseline.Asset, baseline.Skin, baseline.Animation, true, 1f);

        var t = spine.transform;
        t.localPosition = baseline.Position;
        t.localScale = baseline.Scale;
        t.localEulerAngles = baseline.Euler;
    }

    // ---- the title ----------------------------------------------------------------------------------

    private static void ApplyTitle()
    {
        ApplyEdition(Active?.Title);

        var logo = MenuSceneRefs.Logo;
        if (logo == null || logo.transform is not RectTransform rect) return;

        var wanted = Active?.Title;
        if (wanted is not { Enabled: true })
        {
            RevertTitle();
            return;
        }

        logo.enabled = !wanted.Hidden;
        if (wanted.Hidden) return;

        var baseline = MenuSceneRefs.Title;
        var sprite = string.IsNullOrWhiteSpace(wanted.Image)
            ? baseline.Sprite
            : MenuAssets.GetSprite(Active?.PresetName ?? "", wanted.Image);

        if (sprite != null) logo.sprite = sprite;

        var scale = wanted.Scale <= 0f ? 1f : wanted.Scale;
        var size = baseline.SizeDelta * scale;

        if (wanted.PreserveAspect && sprite != null && sprite.rect.height > 0f)
            size = new Vector2(baseline.SizeDelta.y * scale * (sprite.rect.width / sprite.rect.height),
                baseline.SizeDelta.y * scale);

        Detach(rect, true);

        rect.sizeDelta = size;
        rect.anchoredPosition = baseline.AnchoredPosition + (Vector2)(wanted.Offset?.ToVector3() ?? Vector3.zero);
    }

    private static void Detach(RectTransform rect, bool detached)
    {
        var element = rect.GetComponent<LayoutElement>();

        if (!detached)
        {
            if (element != null) element.ignoreLayout = false;
            return;
        }

        if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
        element.ignoreLayout = true;
    }

    private static void ApplyEdition(CTMenuTitle wanted)
    {
        if (!MenuSceneRefs.LateCaptured) return;

        var edition = MenuSceneRefs.Edition;
        if (edition == null) return;

        var baseline = MenuSceneRefs.Title;

        edition.enabled = baseline.EditionOn && wanted is not { HideEdition: true };

        if (!string.IsNullOrEmpty(wanted?.EditionText))
        {
            foreach (var localize in edition.GetComponents<I2.Loc.Localize>())
            {
                if (localize == null) continue;
                localize.enabled = false;
                UnityEngine.Object.Destroy(localize);
            }

            edition.text = wanted.EditionText;
        }
        else
        {
            edition.text = baseline.EditionText ?? "";
        }

        edition.color = wanted is { TintEdition: true }
            ? wanted.EditionColor?.ToColor() ?? baseline.EditionColor
            : baseline.EditionColor;

        if (edition.transform is not RectTransform rect) return;

        var offset = wanted?.EditionOffset?.ToVector3() ?? Vector3.zero;
        Detach(rect, offset != Vector3.zero);
        rect.anchoredPosition = baseline.EditionPosition + (Vector2)offset;
    }

    private static void RevertTitle()
    {
        ApplyEdition(null);

        var logo = MenuSceneRefs.Logo;
        if (logo == null || logo.transform is not RectTransform rect) return;

        var baseline = MenuSceneRefs.Title;

        Detach(rect, false);

        rect.anchoredPosition = baseline.AnchoredPosition;
        rect.sizeDelta = baseline.SizeDelta;
        logo.enabled = true;

        if (baseline.Sprite != null) logo.sprite = baseline.Sprite;
    }
}
