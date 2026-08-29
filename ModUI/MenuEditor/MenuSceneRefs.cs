using System;
using Beffio.Dithering;
using Lamb.UI.MainMenu;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;
using LambMainMenu = Lamb.UI.MainMenu.MainMenu;

namespace CustomSpineLoader.ModUI.MenuEditor;

// The only place that knows how the main menu is put together.
//
// Everything else in this feature reads the menu through here, so a game update that moves a field
// costs one file rather than five. Every getter is nullable and every caller is expected to check:
// the menu is rebuilt from scratch on each quit-to-menu, and half of what is wanted here is a
// [SerializeField] on a scene object that may simply not be there on some build.
//
// It also holds the BASELINES - what every value was before this mod touched it. That is what makes
// "offset 0, scale 1, blend 0 means exactly vanilla" true, and it is what Forget() puts back. The
// Stylizer is scene-local (it is a component on the menu camera, and the scene is thrown away), so
// restoring is belt and braces rather than the thing that saves us - but the Palette assets it
// points AT are shared with gameplay and persistent, which is why this file never writes to one.
public static class MenuSceneRefs
{
    public const string SceneName = "Main Menu";

    public static MainMenuController Controller { get; private set; }
    public static UIMainMenuController Ui { get; private set; }
    public static LambMainMenu Menu { get; private set; }
    public static Stylizer Stylizer { get; private set; }
    public static SkeletonAnimation Lamb { get; private set; }
    public static ChangeLogoPerLanguage LogoOwner { get; private set; }
    public static Image Logo { get; private set; }

    // The line under the logo - "Cultist Edition" and the like. Read off the controller by name
    // rather than found: the game already holds it, and the two theme methods recolour it.
    public static TMPro.TMP_Text Edition { get; private set; }
    public static CameraSubtleMovementOnInput CameraDrift { get; private set; }

    public static GameObject GlitchEffect { get; private set; }

    // "DarkModeMenu" - a full-screen Image with a blend-mode effect that turns the gold field dark
    // grey while leaving the lamb and the text alone. The game declares it on MainMenuController as
    // _darkModeInvertImage and then never reads that field: the only thing that drives it is the
    // DarkModeObject on the object itself, off the accessibility setting. Which makes it exactly
    // the lever for recolouring the background without touching the front.
    public static Image Background { get; private set; }

    // The menu camera, which clears to a solid pale cream - and that cream, mapped through the
    // palette, IS the gold field. Nothing else paints it: the key art is floor and candles, and
    // every full-screen image on the canvas is an overlay drawn on top. So this one colour is the
    // background, in the only sense that can be changed without moving the lamb with it.
    public static Camera MenuCamera { get; private set; }

    // The three palettes the menu ships with, offered by name in the picker alongside every other
    // Palette the game has loaded.
    public static Palette DefaultPalette { get; private set; }
    public static Palette GoatPalette { get; private set; }
    public static Palette DlcPalette { get; private set; }

    public static bool Ready => Controller != null;

    // Whether the menu scene is the one running. Cheaper and more honest than remembering a flag,
    // since a scene load can happen without this file hearing about it.
    public static bool InMenuScene =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == SceneName;

    // ---- baselines ------------------------------------------------------------------------------

    public static LookBaseline Look { get; private set; }
    public static CentrepieceBaseline Centrepiece { get; private set; }
    public static TitleBaseline Title { get; private set; }

    public struct LookBaseline
    {
        public Palette Palette;
        public Palette Palette2;
        public float LerpPalette;
        public bool UseSecondPalette;
        public float EffectIntensity;
        public bool Dither;
        public bool Grain;
        public bool Glitch;
        public bool BackgroundOn;
        public Color BackgroundColor;
        public Color ClearColor;
    }

    public struct CentrepieceBaseline
    {
        public SkeletonDataAsset Asset;
        public string Skin;
        public string Animation;
        public Vector3 Position;
        public Vector3 Scale;
        public Vector3 Euler;
    }

    public struct TitleBaseline
    {
        public Sprite Sprite;
        public Vector2 AnchoredPosition;
        public Vector2 SizeDelta;
        public Vector3 Scale;
        public string EditionText;
        public Color EditionColor;
        public bool EditionOn;
    }

    // ---- binding --------------------------------------------------------------------------------

    // The look is captured LATER than the rest, and deliberately.
    //
    // Binding happens on the scene load, before MainMenuController.Start - and Start has opinions
    // about almost everything in here. It writes "_stylizer.LerpPalette = 0f" and calls
    // EnableSecondPalette(); it switches the glitch effect off; the intro that follows switches the
    // snow on for a DLC player and flashes the red overlay to full before fading it out.
    //
    // Reading any of that before Start runs records the prefab's opinion, not the menu's - and then
    // "restoring the baseline" writes those stale values back over the game's own. That is what an
    // overridden palette on a preset with the override switched off actually was.
    //
    // So the whole look is read at the start of the second pass instead, once the intro has had its
    // say and the menu is showing what a player would see if this mod were not here.
    public static bool LateCaptured { get; private set; }

    public static void CaptureLate()
    {
        if (LateCaptured || Stylizer == null) return;
        LateCaptured = true;

        Look = new LookBaseline
        {
            Palette = Stylizer.Palette,
            Palette2 = Stylizer.Palette2,
            LerpPalette = Stylizer.LerpPalette,
            UseSecondPalette = Stylizer.UseSecondPalette,
            EffectIntensity = Stylizer.EffectIntensity,
            Dither = Stylizer.Dither,
            Grain = Stylizer.Grain,
            Glitch = GlitchEffect != null && GlitchEffect.activeSelf,
            BackgroundOn = Background != null && Background.enabled,
            BackgroundColor = Background != null ? Background.color : Color.white,
            ClearColor = MenuCamera != null ? MenuCamera.backgroundColor : Color.black
        };

        // The title is found here rather than at bind for the same reason as everything else in
        // this method: the menu is deactivated until StartInput shows it, and a search that
        // insisted on a live image found nothing at all.
        Logo = FindTitle();
        CaptureTitle();

        Plugin.Log.LogInfo("MenuEditor: title is " +
                           (Logo != null ? Path(Logo.transform) : "MISSING") + ".");

        ReportBackground();
    }

    private static bool _backgroundReported;

    // What is actually drawn behind the lamb, once, so the gold field can be recoloured on its own.
    //
    // The palette cannot do it: the Stylizer maps the whole frame through one lookup table, so
    // shifting the table moves the lamb and the text with the background. But the mapping's INPUT
    // is per-object - whatever painted that region before the effect ran - and changing that makes
    // the dither pick a different entry for that region alone. This says what to reach for.
    private static void ReportBackground()
    {
        if (_backgroundReported || Stylizer == null) return;
        _backgroundReported = true;

        try
        {
            var camera = Stylizer.GetComponent<Camera>();
            if (camera != null)
                Plugin.Log.LogInfo($"MenuEditor: menu camera clears with {camera.clearFlags}, " +
                                   $"colour {camera.backgroundColor}.");

            // Biggest first: whatever fills the screen behind everything is the candidate, and the
            // rest is furniture.
            var sprites = new System.Collections.Generic.List<SpriteRenderer>(
                UnityEngine.Object.FindObjectsOfType<SpriteRenderer>(true));
            sprites.Sort((a, b) => Area(b).CompareTo(Area(a)));

            for (var i = 0; i < sprites.Count && i < 8; i++)
            {
                var s = sprites[i];
                Plugin.Log.LogInfo($"MenuEditor: sprite '{Path(s.transform)}' - " +
                                   $"{(s.sprite != null ? s.sprite.name : "no sprite")}, " +
                                   $"colour {s.color}, size {s.bounds.size.x:0.0}x{s.bounds.size.y:0.0}, " +
                                   $"order {s.sortingOrder}" +
                                   $"{(s.isVisible ? "" : ", not visible")}.");
            }

            if (Ui == null) return;

            var images = new System.Collections.Generic.List<Image>(
                Ui.GetComponentsInChildren<Image>(true));
            images.Sort((a, b) => RectArea(b).CompareTo(RectArea(a)));

            for (var i = 0; i < images.Count && i < 8; i++)
            {
                var image = images[i];
                Plugin.Log.LogInfo($"MenuEditor: canvas image '{Path(image.transform)}' - " +
                                   $"{(image.sprite != null ? image.sprite.name : "no sprite")}, " +
                                   $"colour {image.color}, " +
                                   $"{image.rectTransform.rect.width:0}x{image.rectTransform.rect.height:0}" +
                                   $"{(image.enabled && image.gameObject.activeInHierarchy ? "" : ", hidden")}.");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MenuEditor: could not describe the menu's background: " + e.Message);
        }
    }

    private static float Area(SpriteRenderer r) =>
        r == null ? 0f : r.bounds.size.x * r.bounds.size.y;

    private static float RectArea(Image i) =>
        i == null ? 0f : Mathf.Abs(i.rectTransform.rect.width * i.rectTransform.rect.height);

    private static bool _reported;

    // Called on every "Main Menu" scene load, before MainMenuController.Start has run.
    public static void Bind()
    {
        Forget(restore: false);

        try
        {
            Controller = UnityEngine.Object.FindObjectOfType<MainMenuController>(true);
            Ui = UIMainMenuController.Instance;
            Menu = Ui != null ? Ui.MainMenu : null;

            if (Controller == null)
            {
                Plugin.Log.LogWarning("MenuEditor: no MainMenuController in the menu scene; " +
                                      "the menu is left alone.");
                return;
            }

            // The menu scene holds TWO Stylizers - one on the menu camera and one on the comic
            // camera. This is the menu's own; Camera.main would be a coin toss.
            Stylizer = Controller._stylizer;
            Lamb = Controller.lambSpine;
            CameraDrift = Controller._cameraSubtle;
            GlitchEffect = Controller._glitchEffect;
            Background = Controller._darkModeInvertImage;
            MenuCamera = Stylizer != null ? Stylizer.GetComponent<Camera>() : null;
            DefaultPalette = Controller._defaultPalette;
            GoatPalette = Controller._goatPalette;
            DlcPalette = Controller._DLCPalette;

            // The title is bound in CaptureLate, not here - the menu is deactivated at scene load.
            LogoOwner = null;
            Logo = null;
            Edition = Controller._editionText;

            Capture();
            Report();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MenuEditor: could not read the main menu: " + e);
            Forget(restore: false);
        }
    }

    // The title the player is actually looking at.
    //
    // ChangeLogoPerLanguage looked like the answer and is not: the only one in the scene sits under
    // "Main Menu Controller/StartingLogo", which is the logo on the press-any-button screen, not
    // the one standing over the menu afterwards. The menu's own title is plain art with no script
    // on it, so it is found by looking rather than by asking.
    //
    // _keyArt is UIMainMenuController's name for the title artwork, so anything inside it wins.
    // Failing that, the biggest sprite on the menu canvas is the title by a wide margin.
    // Where the title actually is on this build. Named outright rather than searched for, because
    // searching cannot win here: the menu stacks five full-screen overlays and a pair of save-info
    // plates, and every one of them is bigger than the logo.
    private const string TitlePath =
        "Main Menu Controller/Main Menu/MainMenuContainer/Left/Transform/Top/Logo";

    private static Image FindTitle()
    {
        var known = GameObject.Find(TitlePath);
        if (known != null)
        {
            var image = known.GetComponent<Image>() ?? Largest(known.transform);
            if (image != null)
            {
                Plugin.Log.LogInfo($"MenuEditor: title found at '{Path(image.transform)}'.");
                return image;
            }
        }

        // The path is a fact about one version of one scene, so there is a fallback for the day it
        // stops being true: the biggest sprite on the canvas that is not one of those overlays.
        Plugin.Log.LogWarning($"MenuEditor: nothing at '{TitlePath}'; looking for the title instead.");
        return Guess();
    }

    private static Image Largest(Transform root)
    {
        Image best = null;
        var bestArea = 0f;

        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null) continue;

            var area = RectArea(image);
            if (area <= bestArea) continue;

            bestArea = area;
            best = image;
        }

        return best;
    }

    private static Image Guess()
    {
        Image best = null;
        var bestScore = 0f;

        // Not _keyArt, whatever its name suggests: that is the world set - the floor, the candles,
        // the pentagram - and it is made of SpriteRenderers, so a search for an Image inside it
        // finds nothing whatsoever. The title is on the menu's own canvas.
        var root = Ui != null ? Ui.transform : null;
        if (root == null) return null;

        // Inactive included, and this is the whole reason the first attempt found nothing:
        // UISubmenuBase.Awake calls Hide(immediate: true), so at scene-load time the entire menu
        // is switched off and there is not one active image under it to find.
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null) continue;

            // Not the menu's own furniture: button plates, the navigator highlight and the sidebar
            // icons are sprites too, and all of them are small.
            var rect = image.rectTransform.rect;
            var area = Mathf.Abs(rect.width * rect.height);
            if (area < 40000f) continue;

            // And not the full-screen overlays either. The menu stacks several of them - the fade,
            // the dark-mode invert, the screen flash, the grain texture, the focus hole - and every
            // one is larger than the title, so on size alone they would all beat it.
            if (Mathf.Abs(rect.width) >= 1820f && Mathf.Abs(rect.height) >= 1020f) continue;

            // Something switched off by hand - a logo for an edition this build is not - loses to
            // anything that is on, however big it is.
            var live = image.enabled && image.gameObject.activeInHierarchy;
            var score = area * (live ? 2f : 1f);

            Plugin.Log.LogInfo($"MenuEditor: title candidate '{Path(image.transform)}' - " +
                               $"sprite {image.sprite.name}, {rect.width:0}x{rect.height:0}" +
                               $"{(live ? "" : ", hidden")}.");

            if (score <= bestScore) continue;
            bestScore = score;
            best = image;
        }

        return best;
    }

    private static string Path(Transform t)
    {
        var name = t.name;
        for (var parent = t.parent; parent != null; parent = parent.parent)
            name = parent.name + "/" + name;
        return name;
    }

    private static void Capture()
    {
        if (Lamb != null)
        {
            var t = Lamb.transform;
            Centrepiece = new CentrepieceBaseline
            {
                Asset = Lamb.skeletonDataAsset,
                Skin = Lamb.initialSkinName,
                Animation = Lamb.AnimationName,
                Position = t.localPosition,
                Scale = t.localScale,
                Euler = t.localEulerAngles
            };
        }

        CaptureTitle();
    }

    private static void CaptureTitle()
    {
        if (Logo == null || Logo.transform is not RectTransform rect) return;

        Title = new TitleBaseline
        {
            Sprite = Logo.sprite,
            AnchoredPosition = rect.anchoredPosition,
            SizeDelta = rect.sizeDelta,
            Scale = rect.localScale,
            EditionText = Edition != null ? Edition.text : "",
            EditionColor = Edition != null ? Edition.color : Color.white,
            EditionOn = Edition != null && Edition.enabled
        };
    }

    // Once per session, so the log says what this build's menu actually offered rather than what a
    // decompile said it should. The probe this replaced was thrown away; this half is worth keeping.
    private static void Report()
    {
        if (_reported) return;
        _reported = true;

        Plugin.Log.LogInfo(
            $"MenuEditor: bound to the main menu - stylizer {(Stylizer != null ? Stylizer.gameObject.name : "MISSING")}" +
            $", centrepiece {(Lamb != null ? Lamb.gameObject.name : "MISSING")}" +
            $", title {(Logo != null ? Path(Logo.transform) : "MISSING")}.");

        if (Lamb?.skeletonDataAsset != null)
        {
            var data = Lamb.skeletonDataAsset.GetSkeletonData(false);
            if (data != null)
                Plugin.Log.LogInfo($"MenuEditor: the centrepiece offers skins [{Join(SkinNames())}] " +
                                   $"and animations [{Join(AnimationNames())}].");
        }
    }

    private static string Name(UnityEngine.Object o) => o != null ? o.name : "none";

    private static string Join(System.Collections.Generic.List<string> names) =>
        names == null ? "" : string.Join(", ", names.ToArray());

    // ---- what the centrepiece skeleton offers, read live ------------------------------------------

    public static System.Collections.Generic.List<string> SkinNames(SkeletonDataAsset asset = null)
    {
        var names = new System.Collections.Generic.List<string>();
        var data = (asset ?? Lamb?.skeletonDataAsset)?.GetSkeletonData(false);
        if (data == null) return names;

        foreach (var skin in data.Skins) names.Add(skin.Name);
        return names;
    }

    public static System.Collections.Generic.List<string> AnimationNames(SkeletonDataAsset asset = null)
    {
        var names = new System.Collections.Generic.List<string>();
        var data = (asset ?? Lamb?.skeletonDataAsset)?.GetSkeletonData(false);
        if (data == null) return names;

        foreach (var animation in data.Animations) names.Add(animation.Name);
        return names;
    }

    // ---- letting go -------------------------------------------------------------------------------

    // Called on any scene that is not the menu, and before re-binding. The restore is defensive: the
    // Stylizer belongs to the scene being torn down, so in practice there is nothing left to put
    // back - but that is a fact about this build, not a promise, and the cost of honouring it is
    // three assignments.
    public static void Forget(bool restore = true)
    {
        if (restore && Stylizer != null)
        {
            try
            {
                RestoreLook();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("MenuEditor: could not put the menu's look back: " + e.Message);
            }
        }

        LateCaptured = false;
        Controller = null;
        Ui = null;
        Menu = null;
        Stylizer = null;
        Lamb = null;
        LogoOwner = null;
        Logo = null;
        Edition = null;
        CameraDrift = null;
        GlitchEffect = null;
        Background = null;
        MenuCamera = null;
        DefaultPalette = null;
        GoatPalette = null;
        DlcPalette = null;
    }

    // Every field the look tool can write, back to what it was at bind. Palette2 is assigned, never
    // nulled: Stylizer.DitherRender dereferences it on every frame regardless of UseSecondPalette.
    public static void RestoreLook()
    {
        // Nothing was ever read, so there is nothing to put back - and writing the struct's zeroes
        // over a live Stylizer would black the menu out.
        if (Stylizer == null || !LateCaptured) return;

        if (Look.Palette != null) Stylizer.Palette = Look.Palette;
        if (Look.Palette2 != null) Stylizer.Palette2 = Look.Palette2;
        Stylizer.LerpPalette = Look.LerpPalette;
        Stylizer.UseSecondPalette = Look.UseSecondPalette;
        Stylizer.EffectIntensity = Look.EffectIntensity;
        Stylizer.Dither = Look.Dither;
        Stylizer.Grain = Look.Grain;

        if (GlitchEffect != null) GlitchEffect.SetActive(Look.Glitch);

        if (Background != null)
        {
            Background.enabled = Look.BackgroundOn;
            Background.color = Look.BackgroundColor;
        }

        if (MenuCamera != null) MenuCamera.backgroundColor = Look.ClearColor;
    }
}
