using UnityEngine;

namespace CustomSpineLoader.MapEditor;

// The layer the off-screen camera rigs film on.
//
// Three of them want the same thing - the player portraits, the selection portrait and the enemy
// thumbnails - and each used to look for it separately, with its own copy of the search and its own
// idea of what to do when it failed. One of them gave up entirely, one fell back quietly, and one
// logged the same line once per thumbnail. Asked once and remembered here, they agree and it is
// said once.
//
// Searched DOWNWARDS from 31 and all the way to 1, not to 8. Unity's builtin block is 0-7 and index
// 3 is blank in every project - an old "Ignore Collision" slot the engine never reused - so when a
// game names every layer from 8 up, 3 is very often the only one left. Cult of the Lamb 1.5.26 is
// exactly that case: it named the last high layer in an update, and a search that stopped at 8
// concluded there was nothing.
public static class OffscreenLayer
{
    private const int Unsearched = -1;

    private static int _layer = Unsearched;

    // Never negative: worst case it is the default layer, which works because these rigs are kept
    // out of shot by distance rather than by the layer. Isolation is the guarantee, not the
    // mechanism - the cameras are orthographic, a couple of units across, and pointed at a staging
    // position far outside the world.
    public static int Value
    {
        get
        {
            if (_layer != Unsearched) return _layer;

            _layer = Find();
            return _layer;
        }
    }

    // Whether the layer is ours alone. A rig that photographs its subject where it stands - the
    // selection portrait - has nothing but this to keep the room out of the picture, so it is worth
    // being able to ask.
    public static bool IsPrivate => Value != 0;

    private static int Find()
    {
        for (var i = 31; i >= 1; i--)
        {
            if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) continue;

            if (i < 8)
                Plugin.Log.LogInfo($"CultTweaker: every layer from 8 up is named, so the off-screen " +
                                   $"cameras use layer {i} - one of Unity's own blank builtin slots.");

            return i;
        }

        Plugin.Log.LogInfo("CultTweaker: all 32 layers are named, so the off-screen cameras share the " +
                           "default layer. They film far outside the world, so nothing else is in " +
                           "shot - but a preview taken in place will have the room behind it.");
        return 0;
    }
}
