using CustomSpineLoader.MapEditor.Tools;
using HarmonyLib;

namespace CustomSpineLoader.Patches;

//     if (WeatherType != None && WeatherDuration != -1 &&
//         ElapsedGameTime - WeatherStartingTime > WeatherDuration) ClearLocationWeather();
[HarmonyPatch]
public static class WeatherPatches
{
    [HarmonyPatch(typeof(WeatherSystemController), "ClearLocationWeather")]
    [HarmonyPrefix]
    private static bool WeatherSystemController_ClearLocationWeather() => !WeatherControl.Holding;
}
