using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-147 (MIG13 S10c): the hero's stick is a held walk of the goal table. The original has no stick; its nearest input is the held left button, whose polled repeats (every 250 ms, GT106) walk the hero to the
// tile under the pointer through the same starter as the press and mark the run as a flight (0x400, a straight line, GT109, GT110). The stick asks the same: its direction picks a tile straight ahead of the hero
// (HeroInputRules.HeldWalkTarget), a WalkRequest with Held asks for it when the stick is pressed, when it turns and every 250 ms while it is held, and the table's walk (planned as a straight line, stepped by the
// stepper over the hero ports) is the hero's mover. The grid actor takes no stick intent any more. Releasing the stick ends the walk (a ReleaseWalkRequest, Steamwoven's decision: the original's release changes
// nothing, GT112). A session with no hero host (the synthetic WORLD diagnostic) keeps the grid actor as the stick's mover.
public partial class TerrainViewerApp
{
    // The direction and the world time of the stick's last request (null while no stick is held).
    private (NeighborDirection Direction, SimulationTime At)? _stickAsked;
    private int _stickRequests;

    /// <summary>The stick walks the hero through the goal table: an AREA session with a hero host. The grid actor is the stick's mover only where there is none.</summary>
    private bool StickWalksOnTable => _worldActor.Session is AreaGridSession && HeroRunsHost() is not null;

    /// <summary>The stick requests the table's walk made (a smoke and the diagnostic line read it).</summary>
    internal int StickWalkRequests => _stickRequests;

    private static double ElapsedMs(SimulationTime from, SimulationTime to)
    {
        var elapsed = to.Value - from.Value;
        return (double)elapsed.Numerator / (double)elapsed.Denominator * 1000.0;
    }

    /// <summary>The chart cell the hero stands on or its step under way enters (a step that began is not cancelled, so the walk goes on from where the body will be).</summary>
    private static CellCoord HeroStepCell(AreaGridSession grid)
    {
        var state = grid.State;
        return state.Segment is { } segment ? state.Phase == MovementPhase.Returning ? segment.From : segment.To : state.CommittedCell;
    }

    /// <summary>
    /// One frame of the stick on the table: <paramref name="direction"/> is the stick's resolved direction (null: neutral, or a gate of the frame holds the hero). A release ends the held walk; a stick held asks for the
    /// walk when it is pressed, when it turns, and once more than <see cref="HeroInputRules.HeldRepeatMs"/> ms have passed since its last request (GT106).
    /// </summary>
    private void ApplyStickWalk(AreaGridSession grid, NeighborDirection? direction, SimulationTime time)
    {
        if (direction is not { } d)
        {
            if (!_stickHeld) return;
            _stickHeld = false; _stickAsked = null;
            var released = HeroRequests.Apply(new ReleaseWalkRequest());
            GD.Print($"ARC-DEV-147 stick released held walk={released.Outcome}");
            return;
        }
        if (!_stickHeld) _stickAsked = null;
        // The stick takes over at once: what the hero was walking to or doing is replaced, as a click replaces it.
        CancelAreaUseApproach("the stick took over"); CancelTalkApproach("the stick took over"); CancelInventoryApproach("the stick took over"); CancelAreaAttack("the stick took over");
        if (!_stickHeld) CancelPortalApproach("the stick took over", keepArmed: true);
        // A clock that went back (a smoke's restored test time) asks again at once.
        var due = true; var turned = _stickAsked is not { } previous || previous.Direction != d;
        if (!turned && ElapsedMs(_stickAsked!.Value.At, time) is var elapsed && elapsed >= 0) due = HeroInputRules.HeldRepeatDue(elapsed);
        if (due)
        {
            _stickAsked = (d, time);
            if (HeroInputRules.HeldWalkTarget(HeroStepCell(grid), d, RouteSourceFor(grid).Contains) is { } target)
            {
                var result = HeroRequests.Apply(new WalkRequest(target, Ctrl: false, Held: true));
                _stickRequests++;
                if (turned) GD.Print($"ARC-DEV-147 stick held walk direction={d} target={target} run={result.Run} outcome={result.Outcome}");
            }
        }
        _stickHeld = true;
    }
}
