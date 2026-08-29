using System;
using System.Collections;
using MMBiomeGeneration;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

public static class RoomLockNet
{
    private static int _token;

    public static void Arm(MonoBehaviour host)
    {
        if (host == null) return;

        var token = ++_token;
        try
        {
            host.StartCoroutine(Settle(token));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom dungeon: could not arm the room-lock net: " + e.Message);
        }
    }

    public static void Disarm() => _token++;

    private static IEnumerator Settle(int token)
    {
        var deadline = Time.unscaledTime + 15f;

        while (Time.unscaledTime < deadline)
        {
            if (token != _token) yield break;

            var player = PlayerFarming.Instance;
            if (player != null && player.state != null && !player.GoToAndStopping &&
                player.state.CURRENT_STATE != StateMachine.State.InActive)
                break;

            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.5f);
        if (token != _token) yield break;

        Reconcile();
    }

    private static void Reconcile()
    {
        try
        {
            var biome = BiomeGenerator.Instance;
            if (biome == null || biome.CurrentRoom == null) return;

            var open = false;
            var shut = false;

            foreach (var controller in RoomLockController.RoomLockControllers)
            {
                if (controller == null || controller.Standalone) continue;
                if (controller.Open) open = true;
                else shut = true;
            }

            var alive = Health.team2.Count;

            if (alive > 0 && open)
            {
                Plugin.Log.LogInfo($"Custom dungeon: room has {alive} enemy(s) with its doors open; " +
                                   "locking it.");
                RoomLockController.CloseAll();
                return;
            }

            if (alive == 0 && shut)
            {
                Plugin.Log.LogInfo("Custom dungeon: room is locked with nothing in it; opening it.");
                RoomLockController.RoomCompleted();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Custom dungeon: room-lock reconcile failed: " + e.Message);
        }
    }
}
