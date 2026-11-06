using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-146 (MIG13 S10b): the hero's walk is a run of the goal table (goal 3, or 4 when the always-run rule makes the hero a runner; owner pc), and so is its turn (goal 53). The tile click's walk starter and the right release's interrupt
// go through the table (AreaHeroRunsHost); the frame step hands the run to the hero host through the router; the run's end node stops the route, whose feet (the route follower and the grid actor) still take the
// steps (goal-table.md S10b: the stepper does not walk the hero yet).
public partial class TerrainViewerApp
{
    private AreaHeroRunsHost? _heroRunsHost;
    private int _heroWalkRunStarts, _heroWalkRunRetargets;

    /// <summary>The hero host of the shown session (null without one: the synthetic fixtures have none). A new session (a reload, a carried session) gives up the old host's runs first.</summary>
    private AreaHeroRunsHost? HeroRunsHost()
    {
        if (_areaSession is not { } session)
        {
            if (_heroRunsHost is not null) { _heroRunsHost.LoseAll("the session ended"); _heroRunsHost = null; }
            return null;
        }
        if (_heroRunsHost is { } host && ReferenceEquals(host.Session, session)) return host;
        _heroRunsHost?.LoseAll("the session changed");
        return _heroRunsHost = new(session, new ViewerHeroRuns(this));
    }

    // The end nodes of a hero walk run the session ended by itself (a round's start sweep, a map's close); the shown host answers for them.
    private void HeroRunEnded(GoalRunEnd end) => _heroRunsHost?.EndNodes(end);

    /// <summary>The walk starter for a tile click on the table (the chart cell of the click, the gait the always-run rule chose); the table's answer.</summary>
    private HeroWalkRunResult? StartHeroWalkRun(CellCoord chartTile, bool run)
    {
        if (HeroRunsHost() is not { } host || _worldActor.Session is not AreaGridSession grid) return null;
        var result = host.Walk(WorldTile(grid, chartTile), run);
        if (result == HeroWalkRunResult.Started) _heroWalkRunStarts++;
        else if (result == HeroWalkRunResult.Retargeted) _heroWalkRunRetargets++;
        return result;
    }

    private static CellCoord WorldTile(AreaGridSession grid, CellCoord chartTile)
    {
        var world = grid.Chart.ToWorld(chartTile);
        return new(checked((int)world.X), checked((int)world.Y));
    }

    private sealed class ViewerHeroRuns(TerrainViewerApp app) : IAreaHeroRunsViewer
    {
        // The movement gate (0x4339A0): the hero can act and, in a round, holds the turn.
        public bool MayStart()
        {
            if (app._areaSession is not { } session) return false;
            var active = session.QueryHeroLife() is { Status.IsActive: true } && !app.HeroLyingDown;
            var round = session.QueryCombatRound();
            return active && (!(app.CombatModeActive && round is not null) || round!.Actor.IsHero);
        }

        // The route is the walk's feet: it follows the run's destination (a goal of the window the walk moved on to counts when the far goal was handed on to it; a route that arrived at the cell
        // toward a far goal while the next window loads is still that walk, the hand-off resumes it).
        public bool Walking(CellCoord destination)
        {
            if (app._worldRoute is not { Target: { } target } route || route.Status is not (RouteFollowStatus.Following or RouteFollowStatus.Arrived)
                || !ReferenceEquals(app._worldRouteSession, app._worldActor.Session) || app._worldActor.Session is not AreaGridSession grid) return false;
            var at = WorldTile(grid, target);
            var far = app._mapFarGoal is { } handed && handed.Goal == new AreaStepCell(destination.X, destination.Y) && handed.Toward == new AreaStepCell(at.X, at.Y);
            if (route.Status == RouteFollowStatus.Arrived) return far;
            return at == destination || far;
        }

        public void StopWalking(CellCoord destination, string why)
        {
            if (Walking(destination)) app.StopWorldRoute(why);
        }

        // Goal 53's timeline is the viewer's (AreaHeroTurn, 30 ms a step); its end node snaps the facing and draws it.
        public bool Turning => app._heroTurn.Active;

        public void EndTurn() => app.InterruptHeroTurn();
    }
}
