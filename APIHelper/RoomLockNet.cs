using System;
using System.Collections;
using MMBiomeGeneration;
using UnityEngine;

namespace CustomSpineLoader.APIHelper;

// The net under room locking in custom dungeons.
//
// Whether a room should be locked has exactly one honest answer - is there anything alive in it? -
// and that answer is not available while the room is still being built. The generator's content
// phases are still running, the encounter system has not finished spawning, and the player has not
// arrived. Every attempt to decide it early has been wrong in one direction or the other: declaring
// the room finished during generation left rooms full of monsters with their doors open, and
// leaving it entirely to the game left rooms with nothing in them sealed shut.
//
// So this decides nothing during generation. It waits for the arrival to finish, looks at what is
// actually standing in the room, and reconciles the doors with it - closing them on a room that
// still has enemies, opening them on one that has none. Both directions are safe to run on a room
// the game already got right: CloseAll and RoomCompleted are idempotent.
public static class RoomLockNet
{
    // A new room's net supersedes the previous one's; a run that ends disarms whatever is pending.
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
        // Realtime throughout: the arrival happens behind a transition that runs at timeScale 0.
        var deadline = Time.unscaledTime + 15f;

        // The walk-in is the signal. PlacePlayer parks every player InActive and walks them in from
        // the doorway, and vanilla's own CloseAll is hung off the end of that walk - so acting
        // before it finishes would be racing the very code this is meant to back up.
        while (Time.unscaledTime < deadline)
        {
            if (token != _token) yield break;

            var player = PlayerFarming.Instance;
            if (player != null && player.state != null && !player.GoToAndStopping &&
                player.state.CURRENT_STATE != StateMachine.State.InActive)
                break;

            yield return null;
        }

        // A beat for the enemies' own spawn-in, and for the doors vanilla decided to move.
        yield return new WaitForSecondsRealtime(0.5f);
        if (token != _token) yield break;

        Reconcile();
    }

    // A lock slip is not worth taking a room arrival down with it.
    private static void Reconcile()
    {
        try
        {
            var biome = BiomeGenerator.Instance;
            if (biome == null || biome.CurrentRoom == null) return;

            // Read the controllers rather than the static DoorsOpen flag: that one is only written
            // by DoorUp and DoorDown, so a room whose doors were never touched leaves it saying
            // whatever the last room said. Standalone controllers are one-way traps that belong to
            // their room's own scripting, not to the room's clear state.
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

            // The other half, and the reason the old early RoomCompleted existed at all: a room
            // with nothing in it is locked by vanilla anyway (doorsWillClose defaults to true), and
            // with nothing to kill there is no way out of it.
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
