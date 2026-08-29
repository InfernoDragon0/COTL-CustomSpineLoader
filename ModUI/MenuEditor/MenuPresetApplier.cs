using System;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.SpineLoaderHelper;
using DG.Tweening;   // Tween.Kill, for disarming the menu's own theme tween
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI.MenuEditor;

// The one place a preset is turned into a menu. Both the on-load hook and the editor's live preview
// come through here, so what a player sees while editing is what they get on the next launch.
//
// It runs in TWO passes, because the game writes over one of them:
//
//   A. On the scene load, before MainMenuController.Start. The title, the centrepiece and the two
//      scene effects go on here.
//   B. After MainMenuController.StartInput, which is an animation event at the end of the intro.
//      By then Start has run "LerpPalette = 0" and EnableSecondPalette(), so anything written to
//      the Stylizer before this moment is gone. The look goes on here.
//
// Each area is applied inside its own try/catch: a preset with a broken spine should cost the
// player their centrepiece, not their menu.
public static class MenuPresetApplier
{
    // Whichever preset is being drawn: the one named in the config, or the editor's working copy
    // while it is open.
    public static CTMenuPreset Active { get; private set; }

    public static void Use(CTMenuPreset preset) => Active = preset;

    // Reads the config and loads what it names. Called once per menu load, before pass A.
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

        // The effects have to be read before they are written, and this is the first moment they
        // are worth reading - see MenuSceneRefs.CaptureLate.
        MenuSceneRefs.CaptureLate();

        Guarded("the look", ApplyStylizer);
        Guarded("the scene effects", ApplyEffects);
    }

    // What the editor calls after every change: the menu has long since settled, so both passes are
    // simply done back to back.
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

        // Slot two, never slot one. The menu already runs a two-palette blend sitting at zero, so
        // writing here means the Blend dial is the game's own crossfade and zero is exactly vanilla
        // rather than something we have to reproduce.
        //
        // Assigned, never nulled: DitherRender dereferences Palette2 on every frame whatever
        // UseSecondPalette says, so clearing it is a null reference sixty times a second.
        // The chosen palette, or the one the menu is already blending towards, with the preset's
        // colour shift applied to a copy of it. Never to the asset itself - those are shared with
        // every gameplay scene and outlive this one.
        var palette = MenuAssets.PaletteByName(look.Palette) ?? MenuSceneRefs.Look.Palette2;
        palette = MenuAssets.Recoloured(palette, look.HueShift, look.Saturation, look.Brightness);

        if (palette != null) stylizer.Palette2 = palette;
        stylizer.UseSecondPalette = true;
        stylizer.LerpPalette = Mathf.Clamp01(look.Blend);

        // The theme tween drives the very value we just wrote, and the load menu re-triggers it on
        // every open and close. isMajorDLC is read by nothing but ShowBlueTheme and HideBlueTheme,
        // so clearing it makes both permanent no-ops - and the live tween has to be killed by hand,
        // because it is a DOTween.To over a captured local that DOTween.Kill(target) cannot reach.
        //
        // Only while we are actually driving the palette: a DLC player who is not overriding the
        // look should keep the blue theme they paid for.
        var controller = MenuSceneRefs.Controller;
        if (controller != null)
        {
            controller.tween?.Kill();
            controller.isMajorDLC = false;
        }

        stylizer.EffectIntensity = look.EffectIntensity;
        stylizer.Dither = look.Dither;
        stylizer.Grain = look.Grain;

        // Pixelate is deliberately not offered. It works, but the way it works is to point the menu
        // camera at a render texture and re-present it through a quad - and the menu's canvas is
        // raycast against that camera, so every button's clickable area ends up somewhere other
        // than where the button is drawn. A look that makes the main menu unclickable is not a look.
    }

    private static void ApplyEffects()
    {
        var look = Active?.Look;
        var driven = look is { Enabled: true };

        var glitch = MenuSceneRefs.GlitchEffect;
        if (glitch != null) glitch.SetActive(driven ? look.Glitch : MenuSceneRefs.Look.Glitch);

        ApplyBackground(driven ? look : null);
    }

    // The one thing that repaints the gold field on its own.
    //
    // The palette maps the whole screen at once, so shifting it takes the lamb and the text with
    // it. This overlay does not: it is a full-screen image with a blend mode, drawn behind the
    // menu's own art, and the game already uses it to invert the background when the accessibility
    // dark mode is on. Its blend ignores the image's colour, so it is on or off and nothing else.
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

        // The DarkModeObject beside it re-asserts "enabled" from the accessibility setting whenever
        // that setting changes, which would switch our background off mid-session. Destroying it is
        // the only way to hold the override - the re-assert comes from a static event the component
        // stays subscribed to whether or not it is enabled. The cost is that the dark mode setting
        // stops reaching this menu until the scene is rebuilt, which a quit-to-menu does anyway.
        var guard = overlay.GetComponent<DarkModeObject>();
        if (guard != null) UnityEngine.Object.Destroy(guard);

        overlay.enabled = true;

        // Alpha only. The blend does not read the image's hue - which is why the colour sliders
        // that used to sit here did nothing - but it does read how opaque it is.
        var colour = baseline.BackgroundColor;
        overlay.color = new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(look.BackgroundStrength));
    }

    // The gold field itself. The camera clears to a solid pale cream and the Stylizer maps that
    // into gold, so this colour is the background and nothing else in the scene is - which makes it
    // the one way to recolour it without dragging the lamb and the text along.
    //
    // With the dither on, the result is snapped to the nearest colour the palette holds, so this
    // picks between the palette's own colours rather than painting freely. Effect strength below 1
    // lets the raw colour through.
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

        Dress(spine, asset, wanted.Skin, wanted.Animation, wanted.Loop, wanted.TimeScale);
        Place(spine, wanted);
    }

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

    // The swap recipe the rest of the mod uses: validate first, because Skeleton.SetSkin throws on
    // a name the skeleton does not have and that would happen inside Initialize, with the object
    // half built. Swapping the asset on the skeleton that is already there - rather than building a
    // new one - inherits the menu's placement, its rotation and its LOD component for free.
    private static void Dress(SkeletonAnimation spine, SkeletonDataAsset asset, string skin,
        string animation, bool loop, float timeScale)
    {
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

        // Component-wise, not uniform: the menu authors this skeleton at (2.47, 2.47, 1.23), so a
        // single multiplier would quietly reshape it the moment anything was changed.
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

        // ChangeLogoPerLanguage puts its own sprite back in OnEnable and on every localise event,
        // so disabling it is both what stops that and - because OnEnable calls UpdateLogo - exactly
        // what reverts it later.
        if (MenuSceneRefs.LogoOwner != null) MenuSceneRefs.LogoOwner.enabled = false;

        logo.enabled = !wanted.Hidden;
        if (wanted.Hidden) return;

        var baseline = MenuSceneRefs.Title;
        var sprite = string.IsNullOrWhiteSpace(wanted.Image)
            ? baseline.Sprite
            : MenuAssets.GetSprite(Active?.PresetName ?? "", wanted.Image);

        if (sprite != null) logo.sprite = sprite;

        var scale = wanted.Scale <= 0f ? 1f : wanted.Scale;
        var size = baseline.SizeDelta * scale;

        // A replacement png is rarely the same shape as the game's logo, and stretching it to the
        // rect the game happens to use is never what was wanted. Height is what stays fixed, so a
        // wider title grows sideways rather than being squashed into the old box.
        if (wanted.PreserveAspect && sprite != null && sprite.rect.height > 0f)
            size = new Vector2(baseline.SizeDelta.y * scale * (sprite.rect.width / sprite.rect.height),
                baseline.SizeDelta.y * scale);

        // The logo sits inside the menu's own layout chain (MainMenuContainer/Left/Transform/Top),
        // and a layout group rewrites its child's position and size every time it rebuilds - so
        // without this the offset and size sliders would appear to do nothing at all. The baseline
        // was captured after that layout ran, so standing outside it at offset 0 leaves the title
        // exactly where the game put it.
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
        var edition = MenuSceneRefs.Edition;
        if (edition == null) return;

        var baseline = MenuSceneRefs.Title;

        if (wanted is not { OverrideEdition: true })
        {
            edition.enabled = baseline.EditionOn;
            edition.text = baseline.EditionText ?? "";
            edition.color = baseline.EditionColor;
            return;
        }

        edition.enabled = !wanted.HideEdition;

        if (!string.IsNullOrEmpty(wanted.EditionText))
        {
            // I2 would put the game's own term back on the next language change, and its OnDisable
            // is what unhooks it - so it is disabled before it is destroyed, because Destroy waits
            // until the end of the frame.
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

        edition.color = wanted.TintEdition
            ? wanted.EditionColor?.ToColor() ?? baseline.EditionColor
            : baseline.EditionColor;
    }

    private static void RevertTitle()
    {
        var logo = MenuSceneRefs.Logo;
        if (logo == null || logo.transform is not RectTransform rect) return;

        var baseline = MenuSceneRefs.Title;

        // Back into the layout first, then the numbers: the group is what owns them again.
        Detach(rect, false);

        rect.anchoredPosition = baseline.AnchoredPosition;
        rect.sizeDelta = baseline.SizeDelta;
        rect.localScale = baseline.Scale;
        logo.enabled = true;

        // Re-enabling is the revert: OnEnable calls UpdateLogo, which writes the right sprite for
        // the current language back over ours.
        if (MenuSceneRefs.LogoOwner != null) MenuSceneRefs.LogoOwner.enabled = true;
        else if (baseline.Sprite != null) logo.sprite = baseline.Sprite;
    }
}
