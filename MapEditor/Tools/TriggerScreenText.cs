using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Tools;

// The trigger tool's on-screen text, drawn by the mod rather than borrowed from the game.
//
// It started out on HUD_DisplayName - the dungeon-name text - which was wrong in three ways: it
// forces <uppercase> on whatever it is given, it has exactly two positions (bottom right and
// centre) and neither is where a caption belongs, and it is one line, so a title and its
// subtext could not be two different sizes. This canvas is ours, so all three are just layout.
//
// The font is the game's own. FiraSans SDF is what the intro's "a game by Massive Monster" uses
// (Intro Room 1/Canvas/Game by MM), and it is loaded for the whole session because the HUD uses
// it too, so it can be found among the loaded font assets rather than shipped or reloaded.
public static class TriggerScreenText
{
    public enum Mode
    {
        // Bottom left, left aligned: the "SYSTEM / KEPLER-62 / description" corner.
        Caption,

        // Same pair, top centre.
        Title,

        // Dimmed screen with the pair centred on it.
        Fullscreen
    }

    private const float FadeIn = 0.6f;
    private const float FadeOut = 0.8f;

    // Not black: the point is to read the text over the room rather than instead of it.
    private const float DimAlpha = 0.75f;

    private static ScreenTextOverlay _overlay;

    public static void Show(Mode mode, string title, string subtext, float seconds)
    {
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(subtext)) return;

        try
        {
            // A scene load takes the overlay with it, which is correct - text from the last room
            // has no business in this one - so a missing one is rebuilt rather than an error.
            if (_overlay == null) _overlay = ScreenTextOverlay.Create();
            if (_overlay == null) return;

            _overlay.Play(mode, title ?? "", subtext ?? "", seconds > 0f ? seconds : 3f,
                FadeIn, FadeOut, DimAlpha);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"MapEditor: screen text failed: {e.Message}");
        }
    }

    public static void Hide()
    {
        if (_overlay != null) _overlay.HideNow();
    }
}

// One canvas, reused: a second caption while the first is still up replaces it rather than
// stacking a second copy over it.
public class ScreenTextOverlay : MonoBehaviour
{
    private Canvas _canvas;
    private Image _dim;
    private CanvasGroup _group;
    private RectTransform _block;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _subtext;
    private Coroutine _running;

    // Above the HUD, below the editor's own panels (5000+), which are only up while the game is
    // paused for editing anyway.
    private const int SortingOrder = 4000;

    private const float Margin = 90f;

    public static ScreenTextOverlay Create()
    {
        var host = new GameObject("CultTweaker_ScreenText");
        var overlay = host.AddComponent<ScreenTextOverlay>();
        overlay.Build();
        return overlay;
    }

    private void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;

        // Fixed reference resolution, so a font size authored here means the same thing on every
        // display rather than being whatever the screen happens to be tall.
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // No GraphicRaycaster: this is scenery, and one would eat clicks meant for the game.

        _dim = NewImage("Dim", transform);
        Stretch(_dim.rectTransform);
        _dim.color = new Color(0f, 0f, 0f, 0f);
        _dim.raycastTarget = false;

        var group = new GameObject("Text");
        group.transform.SetParent(transform, false);
        _block = group.AddComponent<RectTransform>();
        _group = group.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var layout = group.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 6f;

        var fitter = group.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Near-white and thinned; the subtext sits a step down in size, weight and brightness so
        // the pair reads as a heading and a note rather than two sentences.
        _title = NewText("Title", _block, 64f, new Color(0.96f, 0.96f, 0.94f), -0.14f);
        _subtext = NewText("Subtext", _block, 20f, new Color(0.62f, 0.63f, 0.66f), -0.08f);
    }

    private static Image NewImage(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private TextMeshProUGUI NewText(string name, Transform parent, float size, Color colour,
        float dilate)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = colour;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;

        // Explicitly neither bold nor weighted: FontWeight only does anything when the font asset
        // ships the weight variants, which this one does not, so the thinning is done below where
        // it actually works.
        text.fontWeight = FontWeight.Regular;
        text.fontStyle = FontStyles.Normal;

        var font = ScreenTextFont.Get();
        if (font != null) text.font = font;

        Thin(text, dilate);

        // The room behind can be any colour; a soft shadow is what keeps the text readable on a
        // pale floor without putting a plate behind it. Kept light, because a heavy shadow reads
        // as weight and undoes the thinning.
        try
        {
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
        catch (Exception)
        {
            // Shadow is cosmetic; a build without it is still legible.
        }

        return text;
    }

    // Thins the glyphs by shrinking the SDF face. Reading `fontMaterial` rather than
    // `fontSharedMaterial` is the important part: shared would edit the game's own FiraSans
    // material and thin every other piece of text set in it, HUD included. fontMaterial hands
    // back a per-object instance instead.
    private static void Thin(TextMeshProUGUI text, float dilate)
    {
        if (Mathf.Approximately(dilate, 0f)) return;

        try
        {
            var material = text.fontMaterial;
            if (material == null) return;
            if (material.HasProperty(ShaderUtilities.ID_FaceDilate))
                material.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: screen text weight adjust failed: " + e.Message);
        }
    }

    public void Play(TriggerScreenText.Mode mode, string title, string subtext, float hold,
        float fadeIn, float fadeOut, float dimAlpha)
    {
        if (_running != null) StopCoroutine(_running);

        _title.text = title;
        _subtext.text = subtext;
        _title.gameObject.SetActive(!string.IsNullOrEmpty(title));
        _subtext.gameObject.SetActive(!string.IsNullOrEmpty(subtext));

        Layout(mode);

        _running = StartCoroutine(Run(mode, hold, fadeIn, fadeOut, dimAlpha));
    }

    // Where the block sits and how it reads. The two text sizes are the point of the layout: a
    // title and a subtext that are obviously not the same kind of line.
    private void Layout(TriggerScreenText.Mode mode)
    {
        switch (mode)
        {
            case TriggerScreenText.Mode.Caption:
                Anchor(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin, Margin), 760f);
                Align(TextAlignmentOptions.BottomLeft, TextAlignmentOptions.TopLeft, 64f, 20f);
                break;

            case TriggerScreenText.Mode.Title:
                Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Margin), 1100f);
                Align(TextAlignmentOptions.Top, TextAlignmentOptions.Top, 64f, 20f);
                break;

            default:
                Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 1200f);
                Align(TextAlignmentOptions.Center, TextAlignmentOptions.Top, 80f, 24f);
                break;
        }
    }

    private void Anchor(Vector2 anchor, Vector2 pivot, Vector2 offset, float width)
    {
        _block.anchorMin = _block.anchorMax = anchor;
        _block.pivot = pivot;
        _block.anchoredPosition = offset;
        _block.sizeDelta = new Vector2(width, 0f);
    }

    private void Align(TextAlignmentOptions titleAlign, TextAlignmentOptions subAlign,
        float titleSize, float subSize)
    {
        _title.alignment = titleAlign;
        _title.fontSize = titleSize;
        _subtext.alignment = subAlign;
        _subtext.fontSize = subSize;
    }

    private IEnumerator Run(TriggerScreenText.Mode mode, float hold, float fadeIn, float fadeOut,
        float dimAlpha)
    {
        var dim = mode == TriggerScreenText.Mode.Fullscreen ? dimAlpha : 0f;

        // Unscaled throughout: a sequence often runs while the game is paused around a
        // conversation, and a scaled fade would sit there at zero alpha until it resumed.
        yield return Fade(0f, 1f, dim, fadeIn);

        var deadline = Time.unscaledTime + Mathf.Max(0f, hold);
        while (Time.unscaledTime < deadline) yield return null;

        yield return Fade(1f, 0f, dim, fadeOut);

        _group.alpha = 0f;
        _dim.color = new Color(0f, 0f, 0f, 0f);
        _running = null;
    }

    private IEnumerator Fade(float from, float to, float dimTarget, float duration)
    {
        if (duration <= 0f)
        {
            _group.alpha = to;
            _dim.color = new Color(0f, 0f, 0f, dimTarget * to);
            yield break;
        }

        var progress = 0f;
        while (progress < duration)
        {
            progress += Time.unscaledDeltaTime;
            var t = Mathf.SmoothStep(from, to, Mathf.Clamp01(progress / duration));
            _group.alpha = t;
            _dim.color = new Color(0f, 0f, 0f, dimTarget * t);
            yield return null;
        }

        _group.alpha = to;
        _dim.color = new Color(0f, 0f, 0f, dimTarget * to);
    }

    public void HideNow()
    {
        if (_running != null) StopCoroutine(_running);
        _running = null;
        _group.alpha = 0f;
        _dim.color = new Color(0f, 0f, 0f, 0f);
    }
}

// FiraSans SDF, the font the intro's "a game by Massive Monster" is set in. Looked up among the
// font assets already in memory rather than loaded: the HUD uses it, so it is always there, and
// Resources.FindObjectsOfTypeAll reaches assets that no live object happens to reference.
public static class ScreenTextFont
{
    private static TMP_FontAsset _font;
    private static bool _searched;

    private static readonly string[] Preferred = ["FiraSans", "Fira Sans", "Fira"];

    public static TMP_FontAsset Get()
    {
        if (_font != null) return _font;
        if (_searched) return _font;

        _searched = true;

        try
        {
            foreach (var wanted in Preferred)
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (candidate == null || candidate.name == null) continue;
                    if (candidate.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    _font = candidate;
                    Plugin.Log.LogInfo($"MapEditor: screen text using font '{candidate.name}'.");
                    return _font;
                }
            }

            // Whatever the game's own UI is set in. Not the same face, but the same session's
            // font rather than TMP's fallback, which ships with the engine and looks it.
            foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
            {
                if (text == null || text.font == null) continue;
                _font = text.font;
                Plugin.Log.LogInfo($"MapEditor: screen text falling back to font '{_font.name}'.");
                return _font;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("MapEditor: screen text font lookup failed: " + e.Message);
        }

        return _font;
    }
}
