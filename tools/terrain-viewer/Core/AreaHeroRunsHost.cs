using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What the hero's runs host needs of the viewer's body: the movement gate and the route the viewer's feet walk (all tiles are world cells).</summary>
public interface IAreaHeroRunsViewer
{
    /// <summary>The movement gate 0x4339A0: the hero can act and, in a round, holds the turn. A walk is not started (nor retargeted) otherwise.</summary>
    bool MayStart();
    /// <summary>The hero's route follows the world tile <paramref name="destination"/> now.</summary>
    bool Walking(CellCoord destination);
    /// <summary>The walk's end node: stops the route that follows <paramref name="destination"/> (nothing when it follows another goal or none).</summary>
    void StopWalking(CellCoord destination, string why);
    /// <summary>The hero's rotate goal (53) plays now: the viewer's timeline of it (30 ms a 45-degree step, GT102) is running.</summary>
    bool Turning { get; }
    /// <summary>The rotate goal's end node (0x42C650): the facing snaps to the target and the turn stops (nothing without a turn under way).</summary>
    void EndTurn();
}

/// <summary>What a request for a walk run did.</summary>
public enum HeroWalkRunResult
{
    /// <summary>A new run of goal 3 or 4 was started (the interrupts of the walk starter ended what they ended).</summary>
    Started,
    /// <summary>The running walk now goes to the new destination: the same run, its path flag "replan" set (GS14).</summary>
    Retargeted,
    /// <summary>The running walk already goes to that destination: nothing changed.</summary>
    Unchanged,
    /// <summary>The movement gate or a run's refusal stopped the walk (the hero is not the round's participant, a casting run holds the table), or the table could not take it.</summary>
    Refused,
}

/// <summary>
/// ARC-DEV-146 (MIG13 S10b, docs/architecture/goal-table.md §3.5, GT14): the hero's runs on the shown map's goal table, owner <c>pc</c> (<see cref="AiSchedulerRules.Pc"/>). The tile click is
/// goal 3 (move to tile) or, by the always-run rule, goal 4 (run to tile): <see cref="Walk"/> is the walk starter 0x433640 / the run starter 0x433C80 (the movement gate, then a run that walks already gets its
/// destination overwritten in place and its replan flag set, GS14; otherwise a new run starts after the level-3 interrupt of the hero's runs, GS12). The run is the <b>hero's walk as the table knows it</b>:
/// its lifetime, destination, priority 2 and interrupts are the table's (a right release, a round's start sweep, a map's close end it through the table's rules, and its end node stops the route), while
/// the viewer's route follower and the grid actor still plan and take the steps (the stepper does not walk the hero yet: <b>not ready</b>, goal-table.md S10b).
/// The run is stepped inside the frame step (<see cref="IGoalStepHost"/>): each step looks whether the route still follows the run's destination and ends the run when it does not (arrived, stopped, replaced by an
/// approach or another goal), else waits a frame.
/// </summary>
public sealed class AreaHeroRunsHost : IGoalStepHost
{
    /// <summary>The hero's owner string on the table.</summary>
    public const string Hero = AiSchedulerRules.Pc;

    /// <summary>The wait of a step that found the route still walking: a frame (the host's steps are one a frame).</summary>
    public const long PollMs = 16;

    private readonly GameSessionOwner _session;
    private readonly IAreaHeroRunsViewer _viewer;
    private readonly StartPorts _ports;
    private bool _requesting;

    public AreaHeroRunsHost(GameSessionOwner session, IAreaHeroRunsViewer viewer)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
        _ports = new(this);
    }

    /// <summary>The session this host's runs belong to.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The runs this host stepped (a test and the diagnostic line read it).</summary>
    public int Steps { get; private set; }

    /// <summary>The runs that ended because the route no longer followed their destination, or the turn no longer played.</summary>
    public int RouteEnds { get; private set; }

    /// <summary>Whether <paramref name="run"/> is a walk of the hero this host steps: the bottom goal 3 or 4 of the owner <c>pc</c>.</summary>
    public static bool IsHeroWalk(GoalRun run) => run.Owner == Hero && run.Bottom.Goal is GoalType.MoveToTile or GoalType.RunToTile;

    /// <summary>Whether <paramref name="run"/> is a turn of the hero this host steps: the bottom goal 53 (rotate) of the owner <c>pc</c>.</summary>
    public static bool IsHeroTurn(GoalRun run) => run.Owner == Hero && run.Bottom.Goal == GoalType.Rotate;

    /// <summary>Whether <paramref name="run"/> is a run of the hero this host steps: its walk (3, 4) or its turn (53).</summary>
    public static bool IsHeroRun(GoalRun run) => IsHeroWalk(run) || IsHeroTurn(run);

    /// <summary>The hero's turn run on the table now, or null.</summary>
    public GoalRun? TurnRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroTurn);

    /// <summary>The hero's walk run on the table now, or null.</summary>
    public GoalRun? WalkRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroWalk);

    /// <summary>The destination the hero's walk run goes to, or null without one.</summary>
    public CellCoord? WalkDestination => WalkRun?.Bottom.Tile;

    /// <summary>
    /// The highest priority of what the hero's runs do now (0 without one): what a right release's interrupt asks of them. A walk run whose route no longer follows its destination and a turn run whose timeline
    /// is over are ending (their step ends them within a frame) and count for nothing, so a request in that frame does not meet a run that is already gone in effect.
    /// </summary>
    public int HighestPriority => _session.QueryGoals().RunsOf(Hero).Where(Doing).Select(r => GoalDescriptors.Of(r.Top.Goal)?.Priority ?? 0).DefaultIfEmpty(0).Max();

    private bool Doing(GoalRun run) => IsHeroWalk(run) ? run.Bottom.Tile is { } destination && _viewer.Walking(destination) : !IsHeroTurn(run) || _viewer.Turning;

    /// <summary>
    /// The walk starter for a tile click (0x433640, or the run starter 0x433C80 when the always-run rule makes the hero a runner), GT14: the movement gate first; then a run of the hero that walks
    /// (goals 3, 4 and their aliases, <see cref="GoalRunTable.RunRunningGoal"/>) is retargeted in place; otherwise a new run starts after the level-3 interrupt of every run of the hero (a refusal of a
    /// priority-4 or -5 run stops the start). The ends of the runs the start interrupted need no route stop: the caller sets the route's goal itself.
    /// </summary>
    public HeroWalkRunResult Walk(CellCoord destination, bool run)
    {
        if (!_viewer.MayStart()) return HeroWalkRunResult.Refused;
        var goal = run ? GoalType.RunToTile : GoalType.MoveToTile;
        if (_session.QueryGoals().RunRunningGoal(Hero, goal) is { } running)
        {
            var retarget = _session.RetargetGoal(running.Handle, destination);
            return retarget.Outcome switch
            {
                GoalRetargetOutcome.Retargeted => HeroWalkRunResult.Retargeted,
                GoalRetargetOutcome.Unchanged => HeroWalkRunResult.Unchanged,
                _ => HeroWalkRunResult.Refused,
            };
        }
        var request = new GoalStartRequest(Hero, run ? GoalStarters.Run : GoalStarters.Walk, new GoalFrame(goal, tile: destination), run ? GoalRunFlags.Running : GoalRunFlags.None);
        SessionGoalStartResult started;
        _requesting = true;
        try
        {
            started = _session.StartGoal(request, _ports);
            foreach (var end in started.Ended) EndNodes(end);
        }
        finally { _requesting = false; }
        return started.Started ? HeroWalkRunResult.Started : HeroWalkRunResult.Refused;
    }

    /// <summary>
    /// The rotate starter (0x433440, GT100) once its gates passed (the viewer's request host decides them): goal 53 starts through 0x44D4E0, a level-2 interrupt of every run of the hero (its own priority,
    /// GS12), its target rotation in the frame; false when a run refuses it (a spell attempt) or the table cannot take it. The goal's first step comes 5 ms later (GS7).
    /// </summary>
    public bool Turn(int rotation)
    {
        var request = new GoalStartRequest(Hero, GoalStarters.Generic, new GoalFrame(GoalType.Rotate) { Rotation = rotation & 7 });
        _requesting = true;
        try
        {
            var started = _session.StartGoal(request, _ports);
            foreach (var end in started.Ended) EndNodes(end);
            return started.Started;
        }
        finally { _requesting = false; }
    }

    /// <summary>
    /// The level-3 interrupt of every run of the hero (0x424070, GS17): the right button's release, a new ordinary action. The first refusal (a spell attempt of priority 4 or 5) stops it and
    /// answers false; an ended walk's end node stops the route. True when nothing refused.
    /// </summary>
    public bool Interrupt(int level, string why)
    {
        var change = _session.InterruptGoals(Hero, level);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return change.Failure == SessionGoalFailure.None && change.Succeeded;
    }

    /// <summary>
    /// The end nodes of an ended run of the hero (a start's interrupt, a right release, a round's sweep, a map's close, the session's own end): a walk run's end node stops the route that still follows its destination
    /// (the route is the walk's feet, so the hero stops at the cell it is walking to). A walk this host's own start ended asks nothing of the route (the caller sets the next goal).
    /// </summary>
    public void EndNodes(GoalRunEnd end, string? why = null)
    {
        ArgumentNullException.ThrowIfNull(end);
        if (end.Kind == GoalEndKind.Finished) return;
        // The rotate goal's end node snaps the facing to the target, even when the run was interrupted (GT102): the walk that interrupted it draws its own facing after.
        if (IsHeroTurn(end.Run)) { _viewer.EndTurn(); return; }
        if (!_requesting && IsHeroWalk(end.Run) && end.Run.Bottom.Tile is { } destination) _viewer.StopWalking(destination, why ?? "ROUTE_STOPPED_BY_GOAL_END");
    }

    /// <summary>The runs the host drops with the view (a quick load, a replaced session, the map shown elsewhere): the hero's runs end at level 6 and the route stops with them.</summary>
    public void LoseAll(string why)
    {
        try
        {
            var change = _session.InterruptGoals(Hero, GoalInterruptRules.MaxLevel);
            foreach (var step in change.Steps)
                if (step.Ended is { } end) EndNodes(end, why);
        }
        catch (ObjectDisposedException) { /* a replaced session: nothing of it is left to end */ }
    }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (IsHeroTurn(run))
        {
            // The turn's steps are the viewer's timeline (30 ms a step, GT102); the run lives while it plays and ends with its last tick.
            Steps++;
            if (_viewer.Turning) return SessionGoalStepAnswer.After(PollMs);
            RouteEnds++;
            return SessionGoalStepAnswer.End;
        }
        if (!IsHeroWalk(run) || run.Bottom.Tile is not { } destination) return SessionGoalStepAnswer.End;
        Steps++;
        // The route is the walk's feet: the run lives while it follows the run's destination, and ends with the arrival, a stop or a goal that replaced it (an approach, a far goal of another window).
        if (_viewer.Walking(destination)) return SessionGoalStepAnswer.After(PollMs);
        RouteEnds++;
        return SessionGoalStepAnswer.End;
    }

    // The walk starters' gate (0x4339A0) is the viewer's: asked before the start. The table's own gate has nothing more to ask of the hero.
    private sealed class StartPorts(AreaHeroRunsHost host) : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => host._viewer.MayStart();
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }
}
