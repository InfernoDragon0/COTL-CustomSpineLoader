using System.Collections.Generic;
using COTL_API.CustomSkins;
using CustomSpineLoader.MapEditor;
using CustomSpineLoader.SpineLoaderHelper;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomSpineLoader.ModUI;

// The players, along the bottom left: one card each, with their portrait and the three things worth
// setting about how they look.
//
// A card rather than a run of rows in the long panel on the right, because these controls belong to
// a particular player and there is no reading order that makes that obvious in a single column - the
// old panel had four headers and the same three fields under each, and which player you were editing
// was whatever header you had last scrolled past. Here the picture is the label.
public class PlayerDock
{
    private readonly MapEditorUI _ui;
    private readonly Transform _parent;

    private GameObject _root;
    private readonly List<GameObject> _cards = [];

    // Which spine each player is waiting on. Static because the dock is torn down and built again
    // while the wait is still going on, and the answer has to outlive the card that asked.
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
            // One and two always have a card: the panel is also how player two's look is set up
            // BEFORE they join, and a card that appears and disappears as a controller connects is
            // worse than one that says they are not in the game yet. Three and four have no such
            // story - nothing about them can be set until they are here - so they stay hidden.
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

        // The card is the outermost surface a player's controls sit on, so it wears the
        // game's plate; the boxes nested inside it stay plain, or it is planks on planks.
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

    // Each control gets a plate of its own so the card reads as four things rather than one stack:
    // who this is, what they are wearing, whether they are wearing it, and what they are.
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

        // Sized by what is in it, rather than by a number picked here. Every box used to carry a
        // fixed height, and every one of those numbers was a few pixels short of a caption plus a
        // 44-high dropdown plus padding - which is what the lists were spilling out of.
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

        // A spine still being parsed has no skeleton to draw yet. The game announces that with a
        // caption in the bottom-left corner, which is where this dock now stands - so the card says
        // it instead, in the space the portrait will fill.
        //
        // Two ways to be waiting: this card asked for a spine and is holding the name until it
        // lands, or something else started a load of the spine the player is already wearing.
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

        // The box's inside, so the render target is the shape it will be drawn at and the lamb is
        // not stretched to fit.
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

    // Drives the portrait's own skeleton and nothing else - the player in the world is not touched.
    // That is the whole reason the portrait is a skeleton of its own rather than a shot of the live
    // one: PlayerFarming's state machine owns the player's AnimationState and would overwrite
    // anything set here on its next state change.
    //
    // Built after the portrait, because the list of animations comes from the portrait's spine.
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
                // Off means the game's own fleece comes back, which it does on the next skin
                // rebuild - so ask for one rather than leaving the old fleece on screen.
                PlayerSpineLoader.ResolvePlayer(playerId)?.SetSkin();
            }

            // In place, not a rebuild: nothing about the card's controls has changed. Turning
            // transmog on goes through ApplyFleece, which announces itself, but turning it OFF is a
            // plain SetSkin the loader knows nothing about - so it is said here.
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

            // No redraw asked for here on purpose. ApplyFleece announces itself through
            // PlayerSpineLoader.LookChanged when the fleece actually lands on the skeleton, which
            // for a fleece whose spine has to load first is seconds after this returns.
            PlayerSpineLoader.ApplyFleece(playerId, index);
        });

        var current = PlayerSpineLoader.GetFleeceIndex(playerId);
        if (current >= 0 && current < fleeces.Count) dropdown.SetSelected(current);

        if (!present) return;

        // The picker still works and still remembers, it just does not dress this spine - so say so
        // rather than leaving a control that looks broken.
        var config = PlayerSpineLoader.ConfigFor(playerId);
        if (config != null && config.DisableFleeceCycling)
            Caption(box, "This spine keeps its own", 13, new Color(1f, 0.8f, 0.45f));
    }

    private void BuildSpineBox(Transform parent, int playerId)
    {
        var spines = SpineNames();

        // COTL_API tracks a selected spine for players one and two only; a third player's choice
        // would be written into player one's slot, so the picker is not offered for them.
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
            // Recorded and redrawn BEFORE the load starts. The card used to be rebuilt only from
            // the landing callback, so the one state it was meant to report - waiting - was always
            // already over by the time anything asked. The spine cannot be read back off the player
            // either, since the selection does not change until the load lands.
            _awaiting[playerId] = value;
            Changed?.Invoke();

            PlayerSpineLoader.EnsureLoaded(value, () =>
            {
                _awaiting.Remove(playerId);

                try
                {
                    CustomSkinManager.ChangeSelectedPlayerSpine(value, playerId);

                    // Written down as well as applied: the API's selection does not survive a
                    // restart on its own.
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

    // Raised after anything that changes how a player looks, so the portraits are retaken.
    public System.Action Changed;

    // ---- scaffolding -------------------------------------------------------------------------------

    // The width a box gives its contents. Known up front, which is the point - see Caption.
    private const float BoxInnerWidth = CardWidth - CardPadding * 2f - BoxPadding * 2f;

    private void Caption(Transform parent, string text, int size, Color colour)
    {
        var label = _ui.CreateLabel(parent, text, size, TextAlignmentOptions.Center);

        var tmp = label.GetComponent<TMP_Text>();
        tmp.color = colour;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;

        // Measured against the width the box WILL give it, not the width it happens to have while
        // the card is still being built. The general helper measures the latter, which is right in
        // the editor's wide panel and wrong in a card this narrow: a caption that wraps to two lines
        // was given one line of height and drew over whatever came next.
        var needed = Mathf.Max(size + 6f, tmp.GetPreferredValues(text, BoxInnerWidth, 0f).y + 4f);

        var element = label.GetComponent<LayoutElement>();
        if (element == null) element = label.AddComponent<LayoutElement>();
        element.preferredHeight = needed;
        element.minHeight = needed;

        // The operative line: these layout groups do not control child height, so what they read is
        // the rect's own size rather than the LayoutElement above it.
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

    // The panel's own list, kept here so the dock does not depend on the panel.
    private static List<string> SpineNames() => CultTweakerPanel.PlayerSpineNames();
}
