using CustomSpineLoader.SpineLoaderHelper;
using Flockade;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.Menus.PlayerMenu;
using src.UI.Menus;
using UnityEngine;

namespace CustomSpineLoader.Patches;

/// <summary>
/// The screens that draw the player with a UI skeleton of their own, and so never see the custom
/// spine COTL_API puts on the world one. Each hook runs after the game has finished setting the
/// screen's SkeletonGraphic up, and hands it to <see cref="PlayerUiSkin"/> with the animation names
/// that screen is going to ask for.
/// </summary>
[HarmonyPatch]
public static class PlayerUiSpinePatches
{
    // The inventory / character screen. Init builds "Lamb_<fleece>" or "Goat" on the vanilla data.
    [HarmonyPatch(typeof(CharacterMenu), nameof(CharacterMenu.Init))]
    [HarmonyPostfix]
    private static void CharacterMenu_Init(CharacterMenu __instance)
    {
        PlayerUiSkin.Apply(__instance._skeletonGraphic, CharacterMenu.OpeningPlayerFarming, null, "inventory");
    }

    // Knucklebones. The eight animation names come from properties that already pick the goat or lamb
    // variant from the PlayerFarming that Configure just stored.
    [HarmonyPatch(typeof(KBPlayer), nameof(KBPlayer.Configure),
        [typeof(PlayerFarming), typeof(Vector2), typeof(Vector2)])]
    [HarmonyPostfix]
    private static void KBPlayer_Configure(KBPlayer __instance, PlayerFarming playerFarming)
    {
        PlayerUiSkin.Apply(__instance._lambSpine, playerFarming,
        [
            __instance._playerIdleAnimation,
            __instance._playDiceAnimation,
            __instance._playerTakeDiceAnimation,
            __instance._playerLostDiceAnimation,
            __instance._playerWonAnimation,
            __instance._playerWonLoop,
            __instance._playerLostAnimation,
            __instance._playerLostLoop
        ], "Knucklebones");
    }

    // Flockade. The animation names are serialized on the prefab, one per AnimationState.
    [HarmonyPatch(typeof(FlockadePlayer), nameof(FlockadePlayer.Configure),
        [typeof(FlockadeGameBoardSide), typeof(FlockadeGamePieceBag), typeof(FlockadeControlPrompts),
            typeof(UIMenuBase), typeof(PlayerFarming)])]
    [HarmonyPostfix]
    private static void FlockadePlayer_Configure(FlockadePlayer __instance, PlayerFarming playerFarming)
    {
        PlayerUiSkin.Apply(__instance._avatar, playerFarming,
        [
            __instance._idleAnimation,
            __instance._losePieceOrPointsAnimation,
            __instance._loseRoundAnimation,
            __instance._loseGameAnimation,
            __instance._playPieceAnimation,
            __instance._winPieceOrPointsAnimation,
            __instance._winRoundAnimation,
            __instance._winGameAnimation
        ], "Flockade");
    }

    // The Flockade result card copies the winner's data asset and starting animation onto its own
    // SkeletonGraphic, but not the skin or the override texture, so it is dressed again here.
    [HarmonyPatch(typeof(FlockadeEndGameAnnouncement), "SetAvatar")]
    [HarmonyPostfix]
    private static void FlockadeEndGameAnnouncement_SetAvatar(FlockadeEndGameAnnouncement __instance,
        FlockadePlayerBase winner)
    {
        if (winner is not FlockadePlayer player || player.PlayerFarming == null) return;
        PlayerUiSkin.Apply(__instance._spineAvatar, player.PlayerFarming, null, "Flockade result");
    }
}
