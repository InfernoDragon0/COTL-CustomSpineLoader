using System.Collections.Generic;
using COTL_API.CustomSkins;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.SpineLoaderHelper;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI;

public class PlayerDock
{
    private readonly MapEditorUI _ui;
    private readonly Transform _parent;

    private GameObject _root;
    private readonly List<GameObject> _cards = [];

    private static readonly Dictionary<int, string> _awaiting = [];

    public PlayerDock(MapEditorUI ui, Transform parent)
    {
        _ui = ui;
        _parent = parent;
    }

    // ---- metrics ---------------------------------------------------------------------------------

    private const float CardWidth = 248f;
    private const float CardSpacing = 10f;
    private const float PortraitHeight = 150f;
    private const float BoxSpacing = 8f;
    private const float BoxPadding = 8f;
    private const float CardPadding = 10f;

    private static readonly Color CardPlate = new(0f, 0f, 0f, 0.78f);
    private static readonly Color BoxPlate = new(1f, 1f, 1f, 0.06f);

    // ---- building --------------------------------------------------------------------------------

    public void Rebuild()
    {
        Teardown();
        if (_parent == null) return;

        _root = new GameObject("PlayerDock");
        _root.transform.SetParent(_parent, false);

        var row = _root.AddComponent<RectTransform>();
        row.anchorMin = row.anchorMax = new Vector2(0f, 0f);
        row.pivot = new Vector2(0f, 0f);
        row.anchoredPosition = new Vector2(16f, 16f);
        row.sizeDelta = new Vector2(0f, 0f);

        var layout = _root.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.spacing = CardSpacing;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = _root.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        for (var i = 0; i < 4; i++)
        {
            var player = PlayerSpineLoader.ResolvePlayer(i);
            if (i > 1 && player == null) continue;

            BuildCard(i, player != null);
        }
    }

    public void Teardown()
    {
        _cards.Clear();

        if (_root == null) return;
        Object.Destroy(_root);
        _root = null;
    }

    public void SetVisible(bool visible)
    {
        if (_root != null) _root.SetActive(visible);
    }

    private void BuildCard(int playerId, bool present)
    {
        var card = new GameObject($"Player{playerId + 1}");
        card.transform.SetParent(_root.transform, false);
        _cards.Add(card);

        var rect = card.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(CardWidth, 0f);

        var cardPlate = card.AddComponent<Image>();
        MapEditor.VanillaChrome.Dress(cardPlate);
        cardPlate.raycastTarget = false;

        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = BoxSpacing;
        layout.padding = new RectOffset((int)CardPadding, (int)CardPadding,
            (int)CardPadding, (int)CardPadding);
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        var fitter = card.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Caption(card.transform, $"Player {playerId + 1}", 18, new Color(1f, 0.92f, 0.72f));

        BuildPortrait(card.transform, playerId, present);
        BuildAnimationBox(card.transform, playerId, present);
        BuildTransmogBox(card.transform, playerId);
        BuildFleeceBox(card.transform, playerId, present);
        BuildSpineBox(card.transform, playerId);
    }

    // ---- the four boxes ----------------------------------------------------------------------------

    private RectTransform Box(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();

        Plate(rect, BoxPlate);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 4f;
        layout.padding = new RectOffset((int)BoxPadding, (int)BoxPadding, (int)BoxPadding, (int)BoxPadding);
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return rect;
    }

    private void BuildPortrait(Transform parent, int playerId, bool present)
    {
        var box = Box(parent, "Portrait");

        if (!present)
        {
            Caption(box, playerId == 1 ? "Not in the game yet" : "Not in the game", 14,
                new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        var awaited = _awaiting.TryGetValue(playerId, out var picked) ? picked : null;
        if (string.IsNullOrEmpty(awaited))
        {
            var active = PlayerSpineLoader.ActiveSpineName(playerId);
            if (!string.IsNullOrEmpty(active) && PlayerSpineLoader.IsPreparing(active)) awaited = active;
        }

        if (!string.IsNullOrEmpty(awaited))
        {
            Caption(box, $"Preparing\n{awaited}...", 14, new Color(1f, 0.85f, 0.45f));
            return;
        }

        var width = CardWidth - CardPadding * 2f - BoxPadding * 2f;
        var height = PortraitHeight - BoxPadding * 2f;

        var texture = PlayerPreview.Sync(playerId, width, height) ? PlayerPreview.Of(playerId) : null;
        if (texture == null)
        {
            Caption(box, "No portrait", 14, new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        var imageGO = new GameObject("Spine");
        imageGO.transform.SetParent(box, false);

        var imageRect = imageGO.AddComponent<RectTransform>();
        imageRect.sizeDelta = new Vector2(0f, PortraitHeight - BoxPadding * 2f);

        var element = imageGO.AddComponent<LayoutElement>();
        element.preferredHeight = PortraitHeight - BoxPadding * 2f;
        element.minHeight = element.preferredHeight;

        var image = imageGO.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
    }

    private void BuildAnimationBox(Transform parent, int playerId, bool present)
    {
        if (!present) return;

        var animations = PlayerPreview.Animations(playerId);
        if (animations.Count == 0) return;

        var box = Box(parent, "Animation");
        Caption(box, "Preview animation", 15, new Color(0.98f, 0.94f, 0.85f));

        var dropdown = _ui.CreateDropdown(box, "Preview animation", animations,
            (_, value) => PlayerPreview.Play(playerId, value));

        var current = animations.IndexOf(PlayerPreview.CurrentAnimation(playerId) ?? "");
        if (current >= 0) dropdown.SetSelected(current);
    }

    private void BuildTransmogBox(Transform parent, int playerId)
    {
        var box = Box(parent, "Transmog");

        _ui.CreateToggle(box, "Fleece transmog", Plugin.TransmogOn(playerId), on =>
        {
            Plugin.SetTransmog(playerId, on);

            if (on)
            {
                var index = PlayerSpineLoader.GetFleeceIndex(playerId);
                if (index >= 0) PlayerSpineLoader.ApplyFleece(playerId, index);
            }
            else
            {
                PlayerSpineLoader.ResolvePlayer(playerId)?.SetSkin();
            }

            PlayerPreview.Redress(playerId);
        });
    }

    private void BuildFleeceBox(Transform parent, int playerId, bool present)
    {
        var fleeces = PlayerSpineLoader.FleeceRotation;
        var box = Box(parent, "Fleece");

        if (fleeces.Count == 0)
        {
            Caption(box, "No fleeces found yet", 14, new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        Caption(box, "Fleece", 15, new Color(0.98f, 0.94f, 0.85f));

        var dropdown = _ui.CreateDropdown(box, "Fleece", fleeces, (index, _) =>
        {
            if (!Plugin.TransmogOn(playerId))
            {
                Plugin.Log.LogWarning($"Fleece transmog is off for player {playerId + 1}.");
                return;
            }

            PlayerSpineLoader.ApplyFleece(playerId, index);
        });

        var current = PlayerSpineLoader.GetFleeceIndex(playerId);
        if (current >= 0 && current < fleeces.Count) dropdown.SetSelected(current);

        if (!present) return;

        var config = PlayerSpineLoader.ConfigFor(playerId);
        if (config != null && config.DisableFleeceCycling)
            Caption(box, "This spine keeps its own", 13, new Color(1f, 0.8f, 0.45f));
    }

    private void BuildSpineBox(Transform parent, int playerId)
    {
        var spines = SpineNames();

        if (playerId > 1)
        {
            var note = Box(parent, "Spine");
            Caption(note, "Spine is players 1-2 only", 14, new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        if (spines.Count == 0)
        {
            var note = Box(parent, "Spine");
            Caption(note, "No custom spines registered", 14, new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        var box = Box(parent, "Spine");
        Caption(box, "Spine", 15, new Color(0.98f, 0.94f, 0.85f));

        var dropdown = _ui.CreateDropdown(box, "Spine", spines, (_, value) =>
        {
            _awaiting[playerId] = value;
            Changed?.Invoke();

            PlayerSpineLoader.EnsureLoaded(value, () =>
            {
                _awaiting.Remove(playerId);

                try
                {
                    CustomSkinManager.ChangeSelectedPlayerSpine(value, playerId);

                    PlayerSpineLoader.RememberSpine(playerId, value);

                    Plugin.Log.LogInfo($"Player {playerId + 1} spine set to {value}.");
                    Changed?.Invoke();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("Spine swap failed: " + e.Message);
                }
            });
        });

        var selected = spines.IndexOf(PlayerSpineLoader.ActiveSpineKey(playerId));
        if (selected >= 0) dropdown.SetSelected(selected);
    }

    public System.Action Changed;

    // ---- scaffolding -------------------------------------------------------------------------------

    private const float BoxInnerWidth = CardWidth - CardPadding * 2f - BoxPadding * 2f;

    private void Caption(Transform parent, string text, int size, Color colour)
    {
        var label = _ui.CreateLabel(parent, text, size, TextAlignmentOptions.Center);

        var tmp = label.GetComponent<TMP_Text>();
        tmp.color = colour;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;

        var needed = Mathf.Max(size + 6f, tmp.GetPreferredValues(text, BoxInnerWidth, 0f).y + 4f);

        var element = label.GetComponent<LayoutElement>();
        if (element == null) element = label.AddComponent<LayoutElement>();
        element.preferredHeight = needed;
        element.minHeight = needed;

        if (label.transform is RectTransform rect)
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, needed);
    }

    private static void Plate(RectTransform rect, Color colour)
    {
        var plate = rect.gameObject.AddComponent<Image>();
        plate.sprite = MapEditorUI.RoundedPlate;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 1.6f;
        plate.color = colour;
        plate.raycastTarget = false;
    }

    private static List<string> SpineNames() => CultTweakerPanel.PlayerSpineNames();
}
