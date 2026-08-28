using CustomSpineLoader.MapEditor.Tools;
using HarmonyLib;

namespace CustomSpineLoader.Patches;

// Keeps a map's own weather standing.
//
// WeatherSystemController.Update clears the weather once the player's saved one has run its course:
//
//     if (WeatherType != None && WeatherDuration != -1 &&
//         ElapsedGameTime - WeatherStartingTime > WeatherDuration) ClearLocationWeather();
//
// Every one of those values is the player's, read straight from their save, and a map that sets its
// own weather does not touch them - so a save carrying weather that has already expired makes that
// test true on the very first frame. Worse, in a dungeon the clear cannot switch itself off:
// StopCurrentWeather only writes WeatherType = None when the location is *not* a dungeon, so the
// condition stays true and the clear runs again every frame after. An authored weather set in a
// dungeon was being wiped within a frame or two of being applied, which reads as it never having
// been saved at all.
//
// So while a map is holding the weather, that clear does nothing. Nothing else changes: the timer,
// the season default it would have restored, and the player's own values are all still there, and
// the moment the map lets go they take over again.
[HarmonyPatch]
public static class WeatherPatches
{
    [HarmonyPatch(typeof(WeatherSystemController), "ClearLocationWeather")]
    [HarmonyPrefix]
    private static bool WeatherSystemController_ClearLocationWeather() => !WeatherControl.Holding;
}
