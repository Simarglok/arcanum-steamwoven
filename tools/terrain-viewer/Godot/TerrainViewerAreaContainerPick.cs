using Arcanum.Session;
using Arcanum.Simulation.Goals;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1127 (E8-g): armed lockpicks on a container. The lockpicks' skill goal 33 (skill 14, ARC-DEV-185) names a container by the target string of the loot goals ("inv:", ARC-DEV-175), so the same walk (goal 8 within range 1), the
// turn and the pick at the frame's next safe point serve it; the pick is the session's on the container's lock identity (AreaContainerLoot.Pick, field 57's level). Nothing opens: a picked container opens by the next press, through the
// window's admission (a jammed or still locked one refuses there).
public partial class TerrainViewerApp
{
    /// <summary>The standing container a lock-pick goal's target string names (a "inv:" string), else null.</summary>
    private AreaInventoryTarget? ContainerOfSkillTarget(string target) =>
        IsInventoryTarget(target) && _areaSession is not null && _inventoryHost is not null && UseKeyOf(target) is { } key
            && AreaInventoryTargetOf(key) is { Kind: AreaInventoryTargetKind.Container } container ? container : null;

    /// <summary>The armed lockpicks' press on a container: the skill goal 33 starts (the hero walks within range 1, turns and picks). False when the press is on no container (the caller cancels the pick).</summary>
    private bool TryAreaContainerPick(Vector2 screen, string pick)
    {
        if (ActorCell() is null || InventoryTargetAt(screen) is not { Kind: AreaInventoryTargetKind.Container } target) return false;
        CancelTalkApproach("a use click replaced it"); CancelInventoryApproach("a use click replaced it"); CancelAreaUseApproach("a use click replaced it");
        // ARC-COMBAT-1777 (0x571CB0 → 0x4C9050): the better follower picks instead of the hero unless the option is off or Ctrl is held.
        if (HandOverLockpick(InventoryTargetOf(target.Row.Key), "container")) return true;
        var started = HeroRequests.Apply(new LockpickRequest(InventoryTargetOf(target.Row.Key), pick));
        _lockpickTrying = started.Outcome == HeroRequestOutcome.Done;
        _portalNotice = _lockpickTrying ? "container: picking the lock" : "container: the pick was refused";
        GD.Print($"ARC-UI-1127 container lockpick-click outcome={started.Outcome} entity={target.Entity}");
        RefreshAreaStatus();
        return true;
    }

    /// <summary>The pick goal 67 asked for on a container: the session's answer on the HUD (the original plays no sound for a pick; its witnesses are ARC-AI-2084's).</summary>
    private void PickContainer(AreaInventoryTarget target, string item)
    {
        var session = _areaSession!;
        var lockpick = _inventoryHost?.Lockpick(session, item, _areaKitLockpicks);
        if (lockpick is null) { _portalNotice = "container: the lockpicks are gone"; RefreshAreaStatus(); return; }
        if (_inventoryHost!.Map(CurrentAreaMap) is not { } map) { _portalNotice = "container: the pick is not ready here"; RefreshAreaStatus(); return; }
        ResumeLoadedRound();
        var (x, y) = LiveWorldCell(target.Row);
        // As for a door (ARC-WORLD-1149): the pick's darkness term reads the light at the container's cell.
        var result = AreaContainerLoot.Pick(session, map, target.Entity, lockpick, light: AreaCellLightLevel(checked((int)x), checked((int)y)), world: CombatWorldOrNull());
        if (result is null) { _portalNotice = "container: nothing to pick (it is gone)"; RefreshAreaStatus(); return; }
        _lastContainerPick = result;
        _portalNotice = result.Disposition == GameSessionDisposition.Rejected ? $"container: pick refused ({result.Failure})"
            : $"container: {result.Outcome!.Value.Result switch
            {
                Arcanum.Simulation.Skills.PickLockResult.Unlocked => "the lock opens", Arcanum.Simulation.Skills.PickLockResult.Locked => "locked it",
                Arcanum.Simulation.Skills.PickLockResult.Jammed => "the lock jams", _ => "the pick failed",
            }}{(result.FateUsed ? " (fate point)" : "")}";
        GD.Print($"ARC-UI-1127 container pick entity={target.Entity} disposition={result.Disposition} failure={result.Failure} success={result.Check?.Success} critical={result.Check?.Critical} " +
            $"result={result.Outcome?.Result} locked={result.State?.Locked}");
        // ARC-AI-2084 (0x4C7160 → 0x4AEE50): the guards near the container see or hear the hero's pick, silent (mode 0: the lock opened, mode 1: it failed).
        if (result.Disposition != GameSessionDisposition.Rejected) WitnessesOfLockPick(target.Entity, target.Row, Arcanum.Simulation.Ai.AiSchedulerRules.Pc, result.Outcome);
        RefreshAreaStatus();
    }

    /// <summary>ARC-COMBAT-1776 (0x42A4E0): the lock an NPC's pick-locks goal names (the goal's target string: a door or window's entity, or a container's "inv:" string) as the session picks it, at the light of its cell; null for a target that is none or gone.</summary>
    private SessionPickLockTarget? AiPickLockTarget(string target)
    {
        if (_areaSession is not { } session) return null;
        if (_areaDoors?.PortalOf(target) is { } door && _areaPortals is { } portals) return portals.PickTarget(door, AreaCellLightLevel(door.Cell.X, door.Cell.Y));
        if (ContainerOfSkillTarget(target) is not { } container || _inventoryHost?.Map(CurrentAreaMap) is not { } map) return null;
        var (x, y) = LiveWorldCell(container.Row);
        return AreaContainerLoot.PickTarget(session, map, container.Entity, AreaCellLightLevel(checked((int)x), checked((int)y)));
    }

    /// <summary>The last container pick's session answer, for the smoke.</summary>
    private SessionPickLockResult? _lastContainerPick;
    internal SessionPickLockResult? LastContainerPickForSelfTest => _lastContainerPick;
}
