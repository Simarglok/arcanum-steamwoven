using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
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
    /// <summary>
    /// ARC-DEV-164: the hero can act: it lives and is not lying down. A session without a hero (the viewer's diagnostic PC profile, without <c>--hero</c>, and the synthetic fixtures; the original
    /// never has one) has no life to read, so its actor acts as it did before the walk went through the table (ARC-DEV-146).
    /// </summary>
    private bool HeroActing(GameSessionOwner session) => !HeroLyingDown && (session.QueryHeroLife() is not { } life || life.Status.IsActive);

    private AreaHeroRunsHost? _heroRunsHost;
    private int _heroWalkRunStarts, _heroWalkRunRetargets, _heroStepsGiven;

    /// <summary>ARC-DEV-149: the hero walks: a run of its walk is on the table (the table plans and gives the route its steps), or the route follows a goal of its own (an approach).</summary>
    internal bool HeroWalking => _worldRoute is { Status: RouteFollowStatus.Following } || HeroRunsHost()?.WalkRun is not null;

    /// <summary>ARC-DEV-149: the chart cell the hero's walk goes to: the destination of its run on the table, else the route's own goal (an approach).</summary>
    internal CellCoord? HeroWalkGoal => HeroRunsHost()?.WalkDestination is { } destination && _worldActor.Session is AreaGridSession grid
        ? grid.Chart.ToChart(new AreaStepCell(destination.X, destination.Y)) : _worldRoute?.Target;

    /// <summary>The hero host as far as it belongs to the shown session; null when there is none or when it still belongs to a replaced session (a quick load, a carried session: its session is disposed). The goal-step router and the smokes go through this; <see cref="HeroRunsHost"/> replaces the stale host.</summary>
    private AreaHeroRunsHost? ShownHeroRunsHost => _heroRunsHost is { } host && ReferenceEquals(host.Session, _areaSession) ? host : null;

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

    /// <summary>The walk starter for a tile click on the table (the chart cell of the click, the gait the always-run rule chose); the table's answer. <paramref name="held"/>: a repeat of a held input (the stick, ARC-DEV-147).</summary>
    private HeroWalkRunResult? StartHeroWalkRun(CellCoord chartTile, bool run, bool held = false)
    {
        if (HeroRunsHost() is not { } host || _worldActor.Session is not AreaGridSession grid) return null;
        var result = host.Walk(WorldTile(grid, chartTile), run, held);
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
            var active = app.HeroActing(session);
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

        public void HeroStunEnds() => app.HeroStunEnded();
        public void HeroFalls(bool on) => app.HeroForcedDown(on);
        public bool HeroFellDown => app.HeroFellDownNow;
        public bool HeroStandsUp => app.HeroStandsUpNow;
        public bool HeroStateMayWait(bool getUp) => app.HeroStateMayWait(getUp);

        // The route is the walk's feet: a step the walk gave it (ARC-DEV-149), or a far goal it follows, stops with the run.
        public void StopWalking(CellCoord destination, string why)
        {
            if (Walking(destination) || app._worldRoute is { Given: true, Status: RouteFollowStatus.Following } && ReferenceEquals(app._worldRouteSession, app._worldActor.Session)) app.StopWorldRouteOnly(why);
        }

        // ---- ARC-DEV-149 (MIG13 S10b-2b): the hero ports of the table's walk ----

        private AreaGridSession? Grid => app._worldActor.Session as AreaGridSession;

        private (RouteFollower Route, AreaGridSession Grid)? Feet() => Grid is { } grid && app.WorldRoute(grid) is { } route ? (route, grid) : null;

        private static CellCoord Chart(AreaGridSession grid, CellCoord world) => grid.Chart.ToChart(new AreaStepCell(world.X, world.Y));

        // The cell the hero stands on, or the cell its step under way enters (the follower's step is never cancelled, so the table plans from where the body will be).
        public CellCoord? HeroTile
        {
            get
            {
                if (Grid is not { } grid) return null;
                var state = grid.State;
                var cell = state.Segment is { } segment ? state.Phase == MovementPhase.Returning ? segment.From : segment.To : state.CommittedCell;
                return WorldTile(grid, cell);
            }
        }

        public int HeroEncumbrance => app._areaSession?.QueryHeroCarry()?.Level ?? 0;

        public bool HeroDown => app._areaSession is not { } session || !app.HeroActing(session);

        public bool HeroSettled => Grid is { } grid && grid.State.Phase == MovementPhase.Settled && !app.HeroLyingDown;

        public bool InRound => app.CombatModeActive && app._areaSession?.QueryCombatRound() is not null;

        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight)
        {
            if (Feet() is not { } feet || HeroTile is not { } here) return new(AiHeroPlanOutcome.NoPath);
            var result = feet.Route.PlanWalk(Chart(feet.Grid, here), Chart(feet.Grid, destination), straight);
            if (result.Far) return new(AiHeroPlanOutcome.Far);
            if (result.Plan.Outcome != RouteOutcome.Found) return new(AiHeroPlanOutcome.NoPath);
            return new(AiHeroPlanOutcome.Found, [.. result.Plan.Cells.Select(cell => WorldTile(feet.Grid, cell))], result.Straight);
        }

        // The grid refuses what the terrain, the occupants and the round's action points refuse; the Original setting also ends a walk before a segment's last step onto a living critter (LR5).
        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment)
        {
            if (!lastOfSegment || Grid is not { } grid || app._areaSession?.Rules.Navigation.SegmentEndCritterEndsWalk != true) return AiStepBlock.Clear;
            var chartFrom = Chart(grid, from);
            var chartNext = Chart(grid, next);
            if (AiExecutorRules.Distance(chartFrom, chartNext) != 1) return AiStepBlock.CannotStart;
            return app.RouteSourceFor(grid).StepEntersCritter(chartFrom, RoutePlanner.Toward(chartFrom, chartNext)) ? AiStepBlock.Occupied : AiStepBlock.Clear;
        }

        public int BeginStep(CellCoord from, CellCoord next, bool running)
        {
            if (Feet() is not { } feet) return 0;
            // A route that follows a goal of its own (an approach to an object that replaced the walk) is not the walk's feet: no step is given over it.
            if (feet.Route is { Status: RouteFollowStatus.Following, Given: false }) return 0;
            var chartFrom = Chart(feet.Grid, from);
            var chartNext = Chart(feet.Grid, next);
            if (!feet.Route.GiveStep(chartFrom, chartNext, app.WorldTime)) return 0;
            app._heroStepsGiven++;
            var duration = app._worldActor.AreaGaitTiming?.For(app.StepGait(), RoutePlanner.Toward(chartFrom, chartNext)) ?? DiagnosticGridSession.LiveDuration;
            return (int)Int128.Clamp(duration.Value.Numerator * 1000 / duration.Value.Denominator, 1, 10_000);
        }

        // ARC-DEV-163: the follower holds the given step until the body's step under way ends (it issues the step at that exact end, in the advance that crosses it); the table's next call waits for it.
        public bool StepPending(CellCoord from, CellCoord to)
        {
            if (Feet() is not { } feet) return false;
            var cell = Chart(feet.Grid, to);
            if (feet.Route.GivenStepPending(Chart(feet.Grid, from), cell)) return true;
            // ARC-DEV-181: the follower replanned the given step around a companion standing in it (ARC-WORLD-016): it now follows a route of its own to the very cell, which is the step still on its way, not a refusal.
            return feet.Route is { Status: RouteFollowStatus.Following, Target: { } target } && target == cell;
        }

        // A far goal beyond the planner's reach: the route follower (its corridor) walks the whole goal, as before the table walked the hero.
        public bool FollowRoute(CellCoord destination)
        {
            if (Feet() is not { } feet) return false;
            app._mapFarGoal = null;
            feet.Route.SetTarget(Chart(feet.Grid, destination), app.WorldTime);
            return feet.Route.Status == RouteFollowStatus.Following;
        }

        // ARC-DEV-148: goal 53 is stepped by the table (30 ms a 45-degree step, GT102); the viewer holds the art's rotation and draws it.
        public int HeroFacing => app._worldActor.State.Facing;

        public bool TurnHolds => app.HeroTurnHolds();

        public void ShowFacing(int rotation) => app.ShowHeroFacing(rotation);

        public void SnapFacing(int rotation) => app.SnapHeroFacing(rotation);

        // ---- ARC-DEV-169 (MIG13 S11a): the hero's talk goal 21 and its approach 8 ----

        // The speaker the goal names, standing here: its world cell (where a moving companion stands now). A speaker that is no longer here (gone, dead, carried off) ends the goal.
        public AiHeroTarget? ObjectTarget(string target)
        {
            // ARC-DEV-176: a door or window of the shown map is named by its session entity; a destroyed one is gone.
            if (app._areaDoors is { } doors && doors.PortalOf(target) is { } portal)
                return doors.DoorValid(target) ? new(new(checked((int)portal.Cell.X), checked((int)portal.Cell.Y)), Portal: true) : null;
            if (app.UseKeyOf(target) is { } used)
            {
                // ARC-DEV-175: a thing to pick up or loot (an item on the ground, a body, a container, a junk pile) is an inventory target; a pickup's range is the goal's own (0), a loot's is the container's (1, goal 81 node 1).
                if (IsInventoryTarget(target) && app.InventoryActive && app.AreaInventoryTargetOf(used) is { } inventory)
                {
                    var (ix, iy) = app.LiveWorldCell(inventory.Row);
                    var cell = inventory.Kind == AreaInventoryTargetKind.GroundItem ? StandTile(inventory.Row) : new(checked((int)ix), checked((int)iy));
                    return new(cell, UseRange: inventory.Kind == AreaInventoryTargetKind.GroundItem ? AiExecutorRules.HeroUseRange : AiExecutorRules.HeroContainerUseRange);
                }
                return app.UseObjectRow(used) is { WorldX: { } ux, WorldY: { } uy } ? new(new(checked((int)ux), checked((int)uy)), UseRange: app.UseRange) : null;
            }
            // ARC-DEV-184: the victim of a pick pocket is named by its session entity (the steal window's holder).
            if (app.PickPocketRowOf(target) is { } victim) { var (vx, vy) = app.LiveWorldCell(victim); return new(new(checked((int)vx), checked((int)vy))); }
            if (app.SpeakerOfTarget(target) is not { } speaker || !app.SpeakerPresent(speaker)) return null;
            var (x, y) = app.SpeakerCell?.Invoke(speaker) ?? (speaker.WorldX, speaker.WorldY);
            return new(new(checked((int)x), checked((int)y)));
        }

        // ARC-DEV-175 (GT133): the hero takes an item standing on its tile (goal 15 walks with range 0). An item lying on a cell no hero can stand on (a table's, a counter's) is taken from the standable cell next to it:
        // Steamwoven's rule for the case the original's plan to the exact tile was not read for (the old pickup reached from one cell, so the take stays possible); the goal's "tile" is that cell.
        private CellCoord StandTile(Arcanum.Data.Area.AreaObjectRecord row)
        {
            var (x, y) = app.LiveWorldCell(row);
            var own = new CellCoord(checked((int)x), checked((int)y));
            if (Grid is not { } grid) return own;
            var chart = grid.Chart.ToChart(new(x, y));
            if (grid.Snapshot.IsAdmitted(grid.Chart.ToWorld(chart))) return own;
            return app.ApproachCell(grid, x, y, AreaInventoryHost.Reach) is { } next ? WorldTile(grid, next) : own;
        }

        // The cell next to the speaker the approach walks to (goal 8's plan, range 1), as the objects' approaches pick theirs.
        public CellCoord? ApproachTile(string target, int range)
        {
            if (app._areaDoors?.PortalOf(target) is { } door)
                return Grid is { } doorGrid && app.ApproachCell(doorGrid, door.Cell.X, door.Cell.Y, range) is { } doorCell ? WorldTile(doorGrid, doorCell) : null;
            if (app.UseKeyOf(target) is { } used)
            {
                if (IsInventoryTarget(target) && app.InventoryActive && app.AreaInventoryTargetOf(used) is { } inventory)
                {
                    var (ix, iy) = app.LiveWorldCell(inventory.Row);
                    // Range 0 (a pickup): the item's own tile, or the standable cell next to it when no hero can stand there (see StandTile).
                    if (range <= 0 && inventory.Kind == AreaInventoryTargetKind.GroundItem) return StandTile(inventory.Row);
                    if (range <= 0) return new(checked((int)ix), checked((int)iy));
                    return Grid is { } inventoryGrid && app.ApproachCell(inventoryGrid, ix, iy, range) is { } inventoryCell ? WorldTile(inventoryGrid, inventoryCell) : null;
                }
                return Grid is { } usedGrid && app.UseApproachCell(usedGrid, used) is { } usedCell ? WorldTile(usedGrid, usedCell) : null;
            }
            if (Grid is { } victimGrid && app.PickPocketRowOf(target) is { } victimRow)
            {
                var (vx, vy) = app.LiveWorldCell(victimRow);
                return app.ApproachCell(victimGrid, vx, vy, range) is { } victimCell ? WorldTile(victimGrid, victimCell) : null;
            }
            if (Grid is not { } grid || app.SpeakerOfTarget(target) is not { } speaker) return null;
            var (x, y) = app.SpeakerCell?.Invoke(speaker) ?? (speaker.WorldX, speaker.WorldY);
            return app.ApproachCell(grid, x, y, AiExecutorRules.HeroTalkApproachRange) is { } cell ? WorldTile(grid, cell) : null;
        }

        // ARC-DEV-181 (MIG13 S12a): the attack goals' ports are the viewer's attack (the target, the reach, the swing, the hit and the repeat).
        public AiHeroAttackTarget? AttackTarget(string target) => app.HeroAttackTarget(target);
        public bool AttackPossible(string target) => app.HeroAttackPossible(target);
        public bool InAttackReach(string target) => app.HeroInAttackReach(target);
        public CellCoord? AttackApproachTile(string target) => app.HeroAttackApproachTile(target);
        public bool AttackWalkInReach(string target) => app.HeroAttackWalkInReach(target);
        public AiAttackSwing? HeroBeginAttack(string target) => app.HeroBeginAttack(target);
        public void HeroAttackHit(string target) => app.HeroAttackHit(target);
        public bool HeroAttackRepeats(string target) => app.HeroAttackRepeats(target);
        public void AttackEnds(string target, AiExecutorReason reason) => app.HeroAttackEnds(target, reason);
        public AiAttackSwing? HeroBeginThrow(string item, CellCoord tile) => app.HeroBeginThrow(item, tile);
        public bool HeroThrowItem(string item, CellCoord tile) => app.HeroThrowItem(item, tile);
        public void HeroThrowEnds(string item, AiExecutorReason reason) => app.HeroThrowEnds(item, reason);
        public AiCastGoalStart HeroStartCast(int run) => app.HeroStartCast(run);
        public bool HeroFaceCast(int run) => app.HeroFaceCast(run);
        public AiCastSwing HeroBeginCast(int run) => app.HeroBeginCast(run);
        public bool HeroCastActionFrame(int run) => app.HeroCastActionFrame(run);
        public bool HeroReleaseCast(int run) => app.HeroReleaseCast(run);
        public void HeroCastEnds(int run, HeroCastEnd how) => app.HeroCastEnds(run, how);
        public void HeroCastInterrupted(int run, string why) => app.HeroCastInterrupted(run, why);
        public bool HeroFaceSkill(string target) => app.HeroFaceSkill(target);
        public void HeroPickPocket(string target, string item, bool plant) => app.HeroPickPocketAsked(target, item, plant);
        public void HeroPickLock(string target, string item) => app.HeroPickLockAsked(target, item);

        // ARC-DEV-176: the door goals' ports are the shown map's door host.
        public IAiDoorWorld? Doors => app._areaDoors;

        public void OpenDialog(string target) => app.RequestTalkDialog(target);

        // The use goal's last node (0x428A10): the use is asked for; the viewer applies it at the frame's next safe point.
        public void UseObject(string target) { if (app.UseKeyOf(target) is { } key) { app._useOpenPending = key; app._useOpenInventory = IsInventoryTarget(target); } }

        // ARC-DEV-175: the pickup is asked for as the use is, and applied at the same safe point (ApplyUse reads the target's kind).
        public void PickUpObject(string target) { if (app.UseKeyOf(target) is { } key) { app._useOpenPending = key; app._useOpenInventory = IsInventoryTarget(target); } }
    }
}
