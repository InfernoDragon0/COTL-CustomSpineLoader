using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.Tools;

/// Stops and restarts enemy AI for the Pause / Resume enemy AI trigger actions.
public static class TriggerEnemyActions
{
    public const string AllEnemies = "";

    private class Held
    {
        public UnitObject Unit;
        public readonly List<(SkeletonAnimation Spine, float TimeScale)> Skeletons = [];
    }

    private static readonly List<Held> _paused = [];

    public static int PausedCount => _paused.Count;

    public static void SetPaused(string target, bool paused)
    {
        var units = Resolve(target);
        if (units.Count == 0)
        {
            Plugin.Log.LogWarning("MapEditor: pause/resume enemy AI found nothing to act on" +
                                  (string.IsNullOrEmpty(target) ? " in this room." : $" for '{target}'."));
            return;
        }

        foreach (var unit in units)
            if (paused) Pause(unit);
            else Resume(unit);
    }

    private static void Pause(UnitObject unit)
    {
        if (unit == null || IndexOf(unit) >= 0) return;

        var held = new Held { Unit = unit };

        foreach (var spine in unit.GetComponentsInChildren<SkeletonAnimation>(true))
        {
            if (spine == null) continue;
            held.Skeletons.Add((spine, spine.timeScale));
            spine.timeScale = 0f;
        }

        unit.speed = 0f;
        unit.moveVX = 0f;
        unit.moveVY = 0f;
        unit.pathToFollow = null;
        unit.enabled = false;

        _paused.Add(held);
    }

    private static void Resume(UnitObject unit)
    {
        if (unit == null) return;

        var index = IndexOf(unit);
        if (index < 0)
        {
            // Not ours, but a resume should still leave it running.
            unit.enabled = true;
            return;
        }

        Release(_paused[index]);
        _paused.RemoveAt(index);
    }

    /// Called wherever a room's trigger state is reset; a paused enemy must not outlive its room.
    public static void ResumeAll()
    {
        foreach (var held in _paused) Release(held);
        _paused.Clear();
    }

    private static void Release(Held held)
    {
        if (held?.Unit == null) return;

        foreach (var (spine, timeScale) in held.Skeletons)
            if (spine != null) spine.timeScale = timeScale;

        held.Unit.enabled = true;
    }

    private static int IndexOf(UnitObject unit)
    {
        for (var i = _paused.Count - 1; i >= 0; i--)
        {
            if (_paused[i].Unit == null) _paused.RemoveAt(i);
            else if (ReferenceEquals(_paused[i].Unit, unit)) return i;
        }

        return -1;
    }

    private static List<UnitObject> Resolve(string target)
    {
        var units = new List<UnitObject>();

        if (!string.IsNullOrEmpty(target))
        {
            var go = TriggerActions.ResolveObject(target);
            var unit = go != null ? go.GetComponentInChildren<UnitObject>(true) : null;
            if (unit != null) units.Add(unit);
            return units;
        }

        foreach (var unit in RoomEnemies())
            if (!units.Contains(unit)) units.Add(unit);

        return units;
    }

    /// Everything hostile that is currently alive, read off the game's own register.
    public static List<UnitObject> RoomEnemies()
    {
        var units = new List<UnitObject>();

        var team = Health.team2;
        if (team == null) return units;

        foreach (var health in team)
        {
            if (health == null || health.InanimateObject) continue;

            var unit = health.GetComponent<UnitObject>();
            if (unit != null) units.Add(unit);
        }

        return units;
    }
}
