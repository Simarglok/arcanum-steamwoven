using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-145 (MIG13 S10a): the hero's request boundary in the viewer. A ground click is a WalkRequest, the right button's release a
// StopRequest (RR5: the level-3 interrupt and the turn toward the pointer), and both are applied by the world runtime's
// HeroRequestHost through this port over the viewer's own hosts (route, approach intents, attack, body). S10b puts the walk, the
// interrupt and the turn on the goal table behind the same port.
public partial class TerrainViewerApp
{
    private HeroRequestHost? _heroRequestHost;
    private readonly AreaHeroTurn _heroTurn = new();
    private (AreaGridSession Session, CellCoord Cell)? _heroTurnAt;
    // A turn asked for while the hero is still finishing a step: it begins once the hero stands (the route stops at the next centre).
    private int? _heroTurnPending;
    private AreaGridSession? _heroTurnSession;
    private int _heroTurnStarts;

    private HeroRequestHost HeroRequests => _heroRequestHost ??= new(new ViewerHeroActions(this));

    /// <summary>The hero's goal 53 (the turn toward a tile) is running or waiting for the hero to stand.</summary>
    internal bool HeroTurning => _heroTurn.Active || _heroTurnPending is not null;

    /// <summary>
    /// The hero's rotate goal steps on the world clock (30 ms per 45 degrees, GT102): each step is drawn as the turned facing of a settled
    /// hero. A goal asked for while the hero finishes a step begins when it stands. A hero that moves again, falls or changes map ends the
    /// goal; its end node snaps the facing to the target when the interrupt came from the right release (<see cref="InterruptHeroTurn"/>),
    /// and a walk draws its own facing.
    /// </summary>
    private void ProcessHeroTurn()
    {
        if (!HeroTurning) return;
        if (_worldActor.Session is not AreaGridSession grid || HeroLyingDown) { _heroTurnPending = null; _heroTurn.End(); _heroTurnAt = null; return; }
        if (_heroTurnPending is { } target)
        {
            if (!ReferenceEquals(_heroTurnSession, grid) || _stickHeld || _attack is not null || _worldRoute is { Status: Arcanum.Simulation.Navigation.RouteFollowStatus.Following })
            {
                _heroTurnPending = null; return;
            }
            if (grid.State.Phase != MovementPhase.Settled) return;
            _heroTurnPending = null; _heroTurnStarts++;
            _heroTurn.Start(_worldActor.State.Facing, target, WorldMilliseconds());
            _heroTurnAt = (grid, grid.State.CommittedCell);
            GD.Print($"ARC-DEV-145 hero-turn goal=53 from={_heroTurn.Facing} to={target}");
        }
        if (_heroTurnAt is not { } at || !ReferenceEquals(at.Session, grid) || at.Cell != grid.State.CommittedCell || grid.State.Phase != MovementPhase.Settled)
        {
            _heroTurn.End(); _heroTurnAt = null; return;
        }
        if (!_heroTurn.Advance(WorldMilliseconds())) return;
        _worldActor.TurnedFacing = (_heroTurn.Facing, grid.State.Facing);
        PublishWorldSelection();
    }

    /// <summary>The level-3 interrupt of the hero's rotate goal: its end node snaps the facing to the target, drawn at once.</summary>
    private void InterruptHeroTurn()
    {
        _heroTurnPending = null;
        var snapped = _heroTurn.End();
        _heroTurnAt = null;
        if (!snapped || _worldActor.Session is not AreaGridSession grid || grid.State.Phase != MovementPhase.Settled) return;
        _worldActor.TurnedFacing = (_heroTurn.Facing, grid.State.Facing);
        PublishWorldSelection();
    }

    private sealed class ViewerHeroActions(TerrainViewerApp app) : IHeroActions
    {
        public HeroFacts Facts()
        {
            var session = app._areaSession;
            // Chart cells: the pick and the route use the same chart as the hero's committed cell (the world cell is a translation of it).
            CellCoord? cell = app._worldActor.Session is AreaGridSession grid ? grid.State.CommittedCell : null;
            var active = session?.QueryHeroLife() is { Status.IsActive: true } && !app.HeroLyingDown;
            var round = session?.QueryCombatRound();
            var inRound = app.CombatModeActive && round is not null;
            // The hero's actions are viewer hosts, not runs of the table yet: the priority is the one of the goal each would be.
            var priority = app._spellGoal is not null ? 4
                : app._attack is not null || app._talkAfterRoute is not null || app._areaUseAfterRoute is not null || app._inventoryAfterRoute is not null
                    || app._portalUseAfterRoute is not null || app._portalUseWhenSettled is not null || app._pickPocketAfterRoute is not null ? 3
                : app._worldRoute is { Status: Arcanum.Simulation.Navigation.RouteFollowStatus.Following } || app.HeroTurning ? 2 : 0;
            return new(cell, app._worldActor.State.Facing, active, priority, inRound, inRound && round!.Actor.IsHero);
        }

        public HeroInputOptions Options() => new(app._alwaysRunOption, app._numLockRun);

        public bool SetWalkTarget(CellCoord tile, bool run)
        {
            if (app._worldActor.Session is not { } session || app.WorldRoute(session) is not { } route) return false;
            app.CancelAreaUseApproach("a ground click replaced it"); app.CancelTalkApproach("a ground click replaced it"); app.CancelInventoryApproach("a ground click replaced it");
            app._mapFarGoal = null; // a click replaces a far goal handed on across window moves (ARC-WORLD-042)
            var accepted = route.SetTarget(tile, app.WorldTime);
            app.SetWorldGait(run ? MovementGait.Run : MovementGait.Walk, "ground click");
            return accepted;
        }

        public void InterruptOrdinary(string why)
        {
            app.StopWorldRoute("ROUTE_STOPPED_BY_RIGHT_RELEASE");
            app.CancelTalkApproach(why); app.CancelAreaUseApproach(why); app.CancelInventoryApproach(why); app.CancelPortalApproach(why, keepArmed: true);
            app._mapFarGoal = null;
            app.CancelAreaAttack(why);
            app.InterruptHeroTurn();
        }

        public void StartTurn(int rotation)
        {
            if (app._worldActor.Session is not AreaGridSession grid) return;
            app._heroTurnPending = rotation; app._heroTurnSession = grid;
        }
    }
}
