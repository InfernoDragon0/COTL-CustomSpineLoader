using System.Collections;
using MMTools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor;

// A black sheet over everything, held for as long as a hub is being built.
//
// The trip into a hub is three things nobody should have to watch: the base's own arrival playing
// out in a town that is about to be emptied, the sweep taking that town apart, and the player being
// moved onto whatever floor the hub turned out to have. Holding the game's own transition over them
// was tried and reverted - the arrival sets the player up (state, camera, animation) as part of
// running, and a hub built behind a held cover inherited a half-finished one. This covers the screen
// without touching the game at all: pure UI, nothing paused, the arrival runs to its end exactly as
// it would have, and what the player sees when the sheet drops is the hub.
public static class HubCurtain
{
    private static GameObject _root;
    private static CanvasGroup _group;
    private static Coroutine _fade;
    private static Coroutine _failsafe;

    public const float FadeSeconds = 0.45f;

    // Above the editor's own canvases (5000-5001), because while this is up there is nothing worth
    // showing underneath it.
    private const int SortingOrder = 5100;

    // Nothing in a hub trip should take this long. If it does the screen comes back anyway: a black
    // screen with no way out is worse than an ugly arrival.
    private const float MaxSeconds = 45f;

    public static bool Raised => _group != null && _group.alpha > 0f;

    public static void Raise(string message = null)
    {
        Build();
        if (_group == null) return;

        StopFade();
        _root.SetActive(true);
        _group.alpha = 1f;
        ShowLoading(message);

        if (Plugin.Instance == null) return;

        if (_failsafe != null) Plugin.Instance.StopCoroutine(_failsafe);
        _failsafe = Plugin.Instance.StartCoroutine(Failsafe());
    }

    public static void Lower(float seconds = FadeSeconds)
    {
        if (_group == null || !Raised) return;

        StopFade();

        if (Plugin.Instance == null)
        {
            Hide();
            return;
        }

        _fade = Plugin.Instance.StartCoroutine(FadeOut(seconds));
    }

    // For the arrival, which wants the screen back BEFORE it starts the walk-in rather than at the
    // same time as it.
    public static IEnumerator LowerAndWait(float seconds = FadeSeconds)
    {
        if (_group == null || !Raised) yield break;

        StopFade();
        yield return FadeOut(seconds);
    }

    private static IEnumerator FadeOut(float seconds)
    {
        var from = _group != null ? _group.alpha : 0f;

        // Unscaled: the editor freezes the clock the moment it is handed the room.
        for (var elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            if (_group == null) yield break;
            _group.alpha = Mathf.Lerp(from, 0f, elapsed / seconds);
            yield return null;
        }

        Hide();
        _fade = null;
    }

    private static IEnumerator Failsafe()
    {
        yield return new WaitForSecondsRealtime(MaxSeconds);

        if (!Raised) yield break;

        Plugin.Log.LogWarning($"Hub: the screen was covered for {MaxSeconds:0}s and whatever was " +
                              "being built never said it had finished; uncovering it anyway.");
        Lower();
    }

    private static void StopFade()
    {
        if (_fade != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_fade);
        _fade = null;
    }

    private static void Hide()
    {
        if (_group != null) _group.alpha = 0f;
        if (_root != null) _root.SetActive(false);
    }

    private static void Build()
    {
        if (_root != null) return;

        _root = new GameObject("CultTweaker_HubCurtain");
        Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        _group = _root.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // It covers the screen; it does not take the clicks aimed at what is under it. A sheet that
        // outlived its trip would otherwise be a game that no longer responds.
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var sheet = new GameObject("Sheet");
        sheet.transform.SetParent(_root.transform, false);

        var image = sheet.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;

        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // ---- the loading corner --------------------------------------------------------------------

    // Borrowed from the game's own loading screen, so a covered screen reads as loading rather than
    // as a hang: MMTransition carries the spinning crown (LoadingIcon) and the loading text, and
    // both are cloned into the curtain's bottom-right corner. The text is built fresh rather than
    // cloned - a cloned label can carry a localizer that would overwrite the message - but it wears
    // the vanilla label's font and material.
    private static TextMeshProUGUI _message;
    private static GameObject _icon;
    private static LoadingIcon _iconSpinner;

    private static void ShowLoading(string message)
    {
        EnsureLoadingBits();

        if (_message != null) _message.text = message ?? "";

        if (_iconSpinner != null)
        {
            // OnEnable clears the fill each time the curtain returns; half, as the vanilla screen
            // forces when it has no real progress to show.
            _iconSpinner.ForceFifty = true;
            _iconSpinner.UpdateProgress(0.5f);
        }
    }

    private static void EnsureLoadingBits()
    {
        if (_root == null || _message != null) return;

        var transition = MMTransition.Instance;

        // The spinner, cloned whole: images, fill setup and its own unscaled-time spin.
        if (transition != null && transition.loadingIcon != null)
        {
            _icon = Object.Instantiate(transition.loadingIcon.gameObject, _root.transform, false);
            _icon.name = "LoadingIcon";
            _icon.SetActive(true);

            var rect = _icon.GetComponent<RectTransform>();
            if (rect != null)
            {
                // Its authored anchors belong to the transition canvas; pinned to this one's corner.
                var size = rect.sizeDelta;
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(-110f, 110f);
                if (size.x > 1f && size.y > 1f) rect.sizeDelta = size;
            }

            _iconSpinner = _icon.GetComponent<LoadingIcon>();
        }

        var go = new GameObject("Message");
        go.transform.SetParent(_root.transform, false);

        _message = go.AddComponent<TextMeshProUGUI>();
        _message.alignment = TextAlignmentOptions.Right;
        _message.fontSize = 40f;
        _message.color = Color.white;
        _message.raycastTarget = false;

        var vanilla = transition != null ? transition.LoadingText : null;
        if (vanilla != null)
        {
            if (vanilla.font != null) _message.font = vanilla.font;
            if (vanilla.fontSharedMaterial != null) _message.fontSharedMaterial = vanilla.fontSharedMaterial;
            _message.fontSize = vanilla.fontSize;
            _message.color = vanilla.color;
        }

        var textRect = _message.rectTransform;
        textRect.anchorMin = textRect.anchorMax = new Vector2(1f, 0f);
        textRect.pivot = new Vector2(1f, 0.5f);
        textRect.sizeDelta = new Vector2(900f, 80f);
        textRect.anchoredPosition = new Vector2(_icon != null ? -190f : -60f, 110f);
    }
}
