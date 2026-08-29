using System;
using Beffio.Dithering;
using Lamb.UI.MainMenu;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;
using LambMainMenu = Lamb.UI.MainMenu.MainMenu;

namespace CustomSpineLoader.ModUI.MenuEditor;

public static class MenuSceneRefs
{
    public const string SceneName = "Main Menu";

    public static MainMenuController Controller { get; private set; }
    public static UIMainMenuController Ui { get; private set; }
    public static LambMainMenu Menu { get; private set; }
    public static Stylizer Stylizer { get; private set; }
    public static SkeletonAnimation Lamb { get; private set; }
    public static Image Logo { get; private set; }

    public static TMPro.TMP_Text Edition { get; private set; }
    public static CameraSubtleMovementOnInput CameraDrift { get; private set; }

    public static GameObject GlitchEffect { get; private set; }

    public static Image Background { get; private set; }

    public static Camera MenuCamera { get; private set; }

    public static bool Ready => Controller != null;

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
        public string EditionText;
        public Color EditionColor;
        public bool EditionOn;
        public Vector2 EditionPosition;
    }

    // ---- binding --------------------------------------------------------------------------------

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

        Logo = FindTitle();
        CaptureTitle();

        Plugin.Log.LogInfo("MenuEditor: title is " +
                           (Logo != null ? Path(Logo.transform) : "MISSING") + ".");
    }

    private static float RectArea(Image i) =>
        i == null ? 0f : Mathf.Abs(i.rectTransform.rect.width * i.rectTransform.rect.height);

    private static bool _reported;

    public static int BindCount { get; private set; }

    public static void Bind()
    {
        Forget(restore: false);
        BindCount++;

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

            Stylizer = Controller._stylizer;
            Lamb = Controller.lambSpine;
            CameraDrift = Controller._cameraSubtle;
            GlitchEffect = Controller._glitchEffect;
            Background = Controller._darkModeInvertImage;
            MenuCamera = Stylizer != null ? Stylizer.GetComponent<Camera>() : null;

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

    private const string TitlePath =
        "Main Menu Controller/Main Menu/MainMenuContainer/Left/Transform/Top/Logo";

    private static Image FindTitle()
    {
        var known = GameObject.Find(TitlePath);
        if (known != null)
        {
            var image = known.GetComponent<Image>() ?? Largest(known.transform);
            if (image != null) return image;
        }

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

        var root = Ui != null ? Ui.transform : null;
        if (root == null) return null;

        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null) continue;

            var rect = image.rectTransform.rect;
            var area = Mathf.Abs(rect.width * rect.height);
            if (area < 40000f) continue;

            if (Mathf.Abs(rect.width) >= 1820f && Mathf.Abs(rect.height) >= 1020f) continue;

            var live = image.enabled && image.gameObject.activeInHierarchy;
            var score = area * (live ? 2f : 1f);

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

    }

    private static void CaptureTitle()
    {
        if (Logo == null || Logo.transform is not RectTransform rect) return;

        Title = new TitleBaseline
        {
            Sprite = Logo.sprite,
            AnchoredPosition = rect.anchoredPosition,
            SizeDelta = rect.sizeDelta,
            EditionText = Edition != null ? Edition.text : "",
            EditionColor = Edition != null ? Edition.color : Color.white,
            EditionOn = Edition != null && Edition.enabled,
            EditionPosition = Edition != null && Edition.transform is RectTransform editionRect
                ? editionRect.anchoredPosition
                : Vector2.zero
        };
    }

    private static void Report()
    {
        if (_reported) return;
        _reported = true;

        Plugin.Log.LogInfo(
            $"MenuEditor: bound to the main menu - stylizer {(Stylizer != null ? Stylizer.gameObject.name : "MISSING")}" +
            $", centrepiece {(Lamb != null ? Lamb.gameObject.name : "MISSING")}.");
    }

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
        Logo = null;
        Edition = null;
        CameraDrift = null;
        GlitchEffect = null;
        Background = null;
        MenuCamera = null;
    }

    public static void RestoreLook()
    {
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
