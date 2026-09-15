using System;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.Chrome;

/// <summary>
/// The plate factory the editor's bars and panels are built on. It exists so the room editor and the
/// full-screen canvases share one look and one blocker rule: every plate the editor draws is dressed
/// in the game's panel art and registered as a rect that swallows world clicks. Screens that are not
/// the room editor keep their own blocker list, so the registration is a delegate rather than a call
/// into <c>RuntimeMapEditor</c>.
/// </summary>
public static class EditorChrome
{
    /// <summary>
    /// How far a bar's art is pushed past the screen edge. The game's panel sprite carries a
    /// transparent border of its own, so a plate sized exactly to the screen leaves a visible gap
    /// down each side and along the outer edge. The art hangs over instead, and the bar rect that
    /// everything else is laid out against stays honest.
    /// </summary>
    private const float Bleed = 28f;

    /// A bar pinned across the top or the bottom of its parent, full width.
    public static RectTransform CreateBar(Transform parent, string name, bool top, float height,
        Action<RectTransform> registerBlocker)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;

        var plate = new GameObject("Plate");
        plate.transform.SetParent(rt, false);
        var plateRt = plate.AddComponent<RectTransform>();
        plateRt.anchorMin = Vector2.zero;
        plateRt.anchorMax = Vector2.one;
        plateRt.offsetMin = new Vector2(-Bleed, top ? 0f : -Bleed);
        plateRt.offsetMax = new Vector2(Bleed, top ? Bleed : 0f);

        VanillaChrome.Dress(plate.AddComponent<Image>());

        registerBlocker?.Invoke(rt);
        return rt;
    }

    /// A plate stretched down one edge: the sidebar, with its own top and bottom insets.
    public static RectTransform CreateColumn(Transform parent, string name, float width, float top,
        float bottom, float side, Action<RectTransform> registerBlocker)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(width, 0f);
        rt.offsetMin = new Vector2(rt.offsetMin.x, bottom);
        rt.offsetMax = new Vector2(-side, -top);

        VanillaChrome.Dress(go.AddComponent<Image>());
        registerBlocker?.Invoke(rt);
        return rt;
    }

    /// A child rect with no art of its own, positioned by hand.
    public static RectTransform CreateRegion(Transform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 pivot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    /// A thin vertical rule between groups of buttons.
    public static GameObject CreateSeparator(Transform parent, float height)
    {
        var go = new GameObject("Separator");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(2f, height);

        var element = go.AddComponent<LayoutElement>();
        element.preferredWidth = 2f;
        element.minWidth = 2f;
        element.flexibleWidth = 0f;
        element.preferredHeight = height;

        var image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.18f);
        image.raycastTarget = false;
        return go;
    }

    public static void Destroy(RectTransform rect)
    {
        if (rect != null) UnityEngine.Object.Destroy(rect.gameObject);
    }
}

/// One entry in a bottom bar's dock. The room editor's dock is built from its tools; the level and
/// dungeon screens hand over a list of these instead, so the bar never has to know what a tool is.
public readonly struct EditorDockItem
{
    public readonly string Label;
    public readonly Sprite Icon;
    public readonly string Hint;
    public readonly Action Do;

    public EditorDockItem(string label, Sprite icon, string hint, Action act)
    {
        Label = label;
        Icon = icon;
        Hint = hint;
        Do = act;
    }
}
