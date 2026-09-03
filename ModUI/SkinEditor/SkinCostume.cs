using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomSpineLoader.ModUI.SkinEditor;

/// <summary>
/// The outfit swap from the Customize Follower command, brought into the skin editor: the preview
/// wears the outfit the game would put on a follower, layered over the skin being edited. It rides on
/// the editor and never on the document - it is a way to look at a skin dressed, not part of the skin.
/// The command's other four pickers (clothing, hat, special, necklace) are deliberately not here:
/// they hang accessories off the follower rather than changing the art a skin actually replaces.
/// </summary>
public class SkinCostume
{
    // Hooded robes and hats are the only things that read a follower's level, and the editor has no
    // follower to read it from; 3 is the middle of the five robe tiers.
    public const int Level = 3;

    public FollowerOutfitType Outfit { get; set; } = FollowerOutfitType.None;

    public bool Any => Outfit != FollowerOutfitType.None;

    /// What the preview is currently dressed in, so a costume change is told apart from an edit.
    public string Key => Any ? Outfit.ToString() : "";

    // Custom is the outfit SpinePatches already rewrites to None on a real follower: there is no skin
    // of that name in the follower atlas, so composing it throws.
    public static List<(string Label, FollowerOutfitType Value)> Outfits() =>
    [
        .. Enum.GetValues(typeof(FollowerOutfitType)).Cast<FollowerOutfitType>()
            .Where(v => v != FollowerOutfitType.Custom)
            .Select(v => (v.ToString(), v))
    ];
}
