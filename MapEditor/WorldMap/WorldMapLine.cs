using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor.WorldMap;

public static class WorldMapLine
{
    public const float Thickness = 4f;

    public static readonly Color DimColour = new(1f, 1f, 1f, 0.18f);
    public static readonly Color OpenColour = new(1f, 0.96f, 0.85f, 0.8f);
    public static readonly Color DoneColour = new(1f, 0.85f, 0.4f, 0.9f);

    public static RectTransform Create(Transform parent, string name)
    {
        var vanilla = CreateVanilla(parent, name);
        if (vanilla != null) return vanilla;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.sprite = MapEditorUI.RoundedPlate;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 4f;
        image.color = DimColour;

        image.raycastTarget = false;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        return rect;
    }

    private static RectTransform CreateVanilla(Transform parent, string name)
    {
        var clone = CustomMapSkin.CloneConnection();
        if (clone == null) return null;

        var visual = CustomLineVisual.Attach(clone);
        if (visual == null)
        {
            Object.Destroy(clone);
            return null;
        }

        clone.name = name;
        clone.transform.SetParent(parent, false);
        clone.SetActive(true);

        var rect = clone.transform as RectTransform;
        if (rect == null) rect = clone.AddComponent<RectTransform>();

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        return rect;
    }

    public static void Place(RectTransform line, Vector2 from, Vector2 to)
    {
        if (line == null) return;

        var vanilla = line.GetComponent<CustomLineVisual>();
        if (vanilla != null)
        {
            vanilla.Place(from, to);
            return;
        }

        var delta = to - from;
        line.anchoredPosition = from;
        line.sizeDelta = new Vector2(delta.magnitude, Thickness);
        line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    public static void SetLinkState(RectTransform line, WorldNodeState from, WorldNodeState to)
    {
        if (line == null) return;

        var vanilla = line.GetComponent<CustomLineVisual>();
        if (vanilla != null)
        {
            vanilla.Apply(from, to);
            return;
        }

        line.gameObject.SetActive(from != WorldNodeState.Hidden && to != WorldNodeState.Hidden);

        if (from == WorldNodeState.Completed && to == WorldNodeState.Completed)
            SetColour(line, DoneColour);
        else if (from == WorldNodeState.Completed &&
                 (to == WorldNodeState.Selectable || to == WorldNodeState.Locked))
            SetColour(line, OpenColour);
        else
            SetColour(line, DimColour);
    }

    public static void SetEditState(RectTransform line)
    {
        if (line == null) return;

        var vanilla = line.GetComponent<CustomLineVisual>();
        if (vanilla != null)
        {
            vanilla.ApplyEditView();
            return;
        }

        line.gameObject.SetActive(true);
        SetColour(line, OpenColour);
    }

    public static void SetColour(RectTransform line, Color colour)
    {
        if (line == null) return;
        var image = line.GetComponent<Image>();
        if (image != null) image.color = colour;
    }
}
