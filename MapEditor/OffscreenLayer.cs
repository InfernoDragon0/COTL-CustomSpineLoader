using UnityEngine;

namespace CustomSpineLoader.MapEditor;

public static class OffscreenLayer
{
    private const int Unsearched = -1;

    private static int _layer = Unsearched;

    public static int Value
    {
        get
        {
            if (_layer != Unsearched) return _layer;

            _layer = Find();
            return _layer;
        }
    }

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
