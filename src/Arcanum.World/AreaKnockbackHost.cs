using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>One knockback under way: the pushed object, the tile the push began on, the destination (four steps away), the line the slide walks and what became of it.</summary>
public sealed class Knockback
{
    public required string Target { get; init; }
    public required CellCoord From { get; init; }
    public required CellCoord Destination { get; init; }
    /// <summary>The table's handle of the push's run: an end or a step of another run of the same object is not this push's.</summary>
    public GoalHandle? Handle { get; init; }
    /// <summary>The 10-pixel line from the pushed object's tile to the destination, built at the goal's third node; null before it.</summary>
    public ProjectileFlight? Flight { get; set; }
    /// <summary>The line's fixed rotation 0..7 the block test looks along (set with the line, 0x4268F0).</summary>
    public int Rotation { get; set; }
    /// <summary>The tick the slide is on (a tick is four line steps).</summary>
    public int Ticks { get; set; }
    /// <summary>The tiles the object has been pushed onto, in order.</summary>
    public List<CellCoord> Path { get; } = [];
}

/// <summary>How a knockback ended: its line ran out, a step was blocked (terrain or a living critter on the tile), the point left the map, or the run was ended from outside (an interrupt such as a stun or a
/// fall, a round's start sweep, a map's close) leaving the object on the tile it reached.</summary>
public enum KnockbackOutcome { Exhausted, Blocked, OffMap, Interrupted, Refused }

/// <summary>A knockback's end for the viewer: the push, how it ended, the tile the object stands on, and the collision damage when a step was blocked (the line steps left, 0 for none).</summary>
public sealed record KnockbackFinished(Knockback Knockback, KnockbackOutcome Outcome, CellCoord Tile, int BumpStepsLeft);

/// <summary>
/// ARC-DEV-141 (MIG13 S8c, goal-table.md §3.5, the S8 read GT59–GT62): what the knockback host needs of the shown map's bodies. The pushed object is an NPC of the map (the hero's slide and the objects' are not
/// ready: the hero's place is the world actor's, S10); everything is by its entity.
/// </summary>
public interface IAreaKnockbackViewer
{
    /// <summary>The pushed object's tile now, or null when it cannot be pushed here (the hero, a companion, an object, an NPC that is gone).</summary>
    CellCoord? Tile(string target);
    /// <summary>The pushed object's own rotation 0..7 (a push from its own tile sends it backward, GT59).</summary>
    int Rotation(string target);
    /// <summary>Whether the shown map holds the tile (a step off it refuses the start, a point off it ends the slide).</summary>
    bool Contains(CellCoord tile);
    /// <summary>The block test of the step from <paramref name="left"/> onto its neighbour <paramref name="tested"/> in the line's rotation (0x42FD70): a blocked tile, a wall, a closed portal or a blocking object is <see cref="KnockbackBlock.Terrain"/>; a living critter on the tile blocks too, whichever step it is (GT61).</summary>
    KnockbackBlock Block(string target, CellCoord left, CellCoord tested);
    /// <summary>Puts the object on <paramref name="tile"/>, its body sliding there over <paramref name="ms"/> milliseconds from <paramref name="nowMs"/> on the world clock of the step.</summary>
    void Slide(string target, CellCoord tile, int ms, long nowMs);
    /// <summary>The collision damage of a blocked step (normal and fatigue, each rolled 1 to <paramref name="maximum"/>, GT61), applied to the object with no attacker.</summary>
    void Bump(string target, int maximum);
}

/// <summary>
/// ARC-DEV-141 (MIG13 S8c, docs/architecture/goal-table.md §3.5, the S8 read GT59–GT62): the host of the shown map's knockbacks as goal runs of the pushed object. The starter (0x434E80, from the AGoal component 43 of
/// a spell or technology) computes the destination four steps away from the source and starts goal 43 on the table with the level-5 interrupt of every run of the object (<see cref="GoalStarters.Generic"/>:
/// the goal's own priority; dying 31 and a knockback underway refuse it). The run pushes goal 64, which slides the object every 35 ms on the animation clock, four 10-pixel line steps a tick, inside the frame step
/// (<see cref="IGoalStepHost"/>): a tile change asks the block test, a block (a critter blocks on any tile change) leaves the object on the tile it left and bumps it, and any end is the end step of the whole run. During the slide the run is a priority-3
/// run (GT62), so a level-3 start, a round's start sweep, a stun or a fall ends it mid-slide. This host answers the knockback ports (<see cref="IAiKnockbackWorld"/>) over the viewer's bodies.
/// </summary>
public sealed class AreaKnockbackHost : IAiKnockbackWorld, IGoalStepHost
{
    private readonly GameSessionOwner _session;
    private readonly IAreaKnockbackViewer _viewer;
    private readonly Action<Knockback, KnockbackFinished> _finished;
    private readonly Action<GoalRunEnd>? _otherEnds;
    private readonly Dictionary<string, Knockback> _slides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (KnockbackOutcome Outcome, int Steps)> _outcome = new(StringComparer.Ordinal);
    private readonly StartPorts _startPorts;
    private long _nowMs;

    /// <param name="finished">Called once for every knockback that ends, after the host acted.</param>
    /// <param name="otherEnds">Where the end nodes of the pushed object's own runs go when the start's interrupt ended them (the AI host's), or null.</param>
    public AreaKnockbackHost(GameSessionOwner session, IAreaKnockbackViewer viewer, Action<Knockback, KnockbackFinished>? finished = null, Action<GoalRunEnd>? otherEnds = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
        _finished = finished ?? ((_, _) => { });
        _otherEnds = otherEnds;
        _startPorts = new(this);
    }

    /// <summary>The session this host's knockbacks belong to.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The runs this host stepped (a test and the diagnostic line read it).</summary>
    public int Steps { get; private set; }

    /// <summary>The knockbacks under way, in the order they began.</summary>
    public IReadOnlyList<Knockback> Active => [.. _slides.Values];

    public int Count => _slides.Count;

    /// <summary>Whether <paramref name="entity"/> is being pushed by this host.</summary>
    public bool Owns(string entity) => _slides.ContainsKey(entity);

    /// <summary>
    /// The starter 0x434E80 for the AGoal component 43 (0x4514E0): the object <paramref name="target"/> is pushed away from the <paramref name="source"/> tile. Nothing starts, and false answers, when the object cannot be
    /// pushed here, the push leaves the map, or the table refuses (dying, a knockback underway, the table full). The run's first step is 5 animation milliseconds later.
    /// </summary>
    public bool Start(string target, CellCoord source)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_viewer.Tile(target) is not { } from) return false;
        if (KnockbackRules.Destination(source, from, _viewer.Rotation(target), _viewer.Contains) is not { } destination) return false;
        var started = _session.StartGoal(new GoalStartRequest(target, GoalStarters.Generic, AiExecutorRules.KnockbackFrame(destination)), _startPorts);
        // The runs the start's interrupt ended are the object's own (its walk, attack, cast): their end nodes are the AI host's.
        foreach (var end in started.Ended) _otherEnds?.Invoke(end);
        // A push of the object that the start's interrupt ended (a slide underway is a priority-3 run) is over, whether or not the new one started: it is reported before the new one is kept.
        if (_slides.TryGetValue(target, out var old) && (started.Started || old.Handle is not { } oldHandle || !_session.QueryGoals().TryGet(oldHandle, out var oldRun) || oldRun.Done))
            Finish(old, KnockbackOutcome.Interrupted, 0);
        if (!started.Started) return false;
        _slides[target] = new Knockback { Target = target, From = from, Destination = destination, Handle = started.Handle };
        return true;
    }

    /// <summary>Every knockback is given up (a map change, a window move, a quick load, a replaced session): the runs end at level 6 and the objects stay where they stand.</summary>
    public void LoseAll(string why)
    {
        foreach (var slide in _slides.Values.ToArray())
        {
            // The table ends the runs itself at a map's close, not at a window move or a smoke's clean-up: the run is ended at level 6 here (neither goal has an end node).
            try { if (slide.Handle is { } handle && _session.QueryGoals().TryGet(handle, out var run) && !run.Done) _session.InterruptGoal(handle, GoalInterruptRules.MaxLevel); }
            catch (ObjectDisposedException) { /* a replaced session: nothing of it is left to end */ }
            Finish(slide, KnockbackOutcome.Interrupted, 0);
        }
    }

    /// <summary>The end of a knockback's run the table ended from outside the stepper (an interrupt, a map's close): neither goal has an end node (GT60), so the object stays on its tile and the push is over.</summary>
    public void EndNodes(GoalRunEnd end)
    {
        // Only the push's own run: an older run of the same object, ended late, is not the newer push's end.
        if (_slides.TryGetValue(end.Run.Owner, out var slide) && slide.Handle == end.Run.Handle) Finish(slide, KnockbackOutcome.Interrupted, 0);
    }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        // A push of another host (a replaced session, a map left) has nothing here: its run ends.
        if (!_slides.TryGetValue(run.Owner, out var mine) || mine.Handle != run.Handle) return SessionGoalStepAnswer.End;
        Steps++;
        _nowMs = clocks.WorldMs;
        var stepped = GoalStepper.StepKnockback(run, this);
        if (_slides.TryGetValue(run.Owner, out var left) && stepped.Answer.NextDelayMs is null)
        {
            // A run that ended without a tick of its own was refused: the object stood on the destination already, or the line was refused.
            var (outcome, steps) = _outcome.GetValueOrDefault(run.Owner, (KnockbackOutcome.Refused, 0));
            Finish(left, outcome, steps);
        }
        return !_session.QueryGoals().TryGet(run.Handle, out var now) || now.Done ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    private void Finish(Knockback slide, KnockbackOutcome outcome, int bumpSteps)
    {
        if (!_slides.Remove(slide.Target)) return;
        _outcome.Remove(slide.Target);
        var tile = _viewer.Tile(slide.Target) ?? slide.From;
        _finished(slide, new(slide, outcome, tile, bumpSteps));
    }

    // ---- the knockback ports ----

    public bool KnockbackAtDestination(string target, CellCoord destination) => _viewer.Tile(target) is { } tile && tile == destination;

    public bool KnockbackBuildLine(string target, CellCoord destination)
    {
        if (!_slides.TryGetValue(target, out var slide) || _viewer.Tile(target) is not { } tile) return false;
        slide.Flight = KnockbackRules.Line(tile, destination);
        slide.Rotation = KnockbackRules.LineRotation(tile, destination);
        return slide.Flight is not null;
    }

    public bool KnockbackSetup(string target) => _slides.TryGetValue(target, out var slide) && slide.Flight is not null;

    public bool KnockbackTick(string target)
    {
        if (!_slides.TryGetValue(target, out var slide) || slide.Flight is not { } flight) return false;
        slide.Ticks++;
        var tick = KnockbackRules.Tick(flight, _viewer.Contains, slide.Rotation, (left, tested) => _viewer.Block(target, left, tested));
        slide.Flight = tick.Flight;
        foreach (var tile in tick.Entered)
        {
            slide.Path.Add(tile);
            _viewer.Slide(target, tile, KnockbackRules.TickMs, _nowMs);
        }
        switch (tick.Kind)
        {
            case KnockbackTickKind.Continue: return true;
            case KnockbackTickKind.Blocked:
                _outcome[target] = (KnockbackOutcome.Blocked, tick.StepsLeft);
                // The collision damage runs the damage path, which can stun, knock down or kill; their starts end this very run (GT62), the step's close sees it done.
                _viewer.Bump(target, KnockbackRules.BumpMaximum(tick.StepsLeft));
                return false;
            case KnockbackTickKind.OffMap:
                _outcome[target] = (KnockbackOutcome.OffMap, 0);
                return false;
            default:
                _outcome[target] = (KnockbackOutcome.Exhausted, 0);
                return false;
        }
    }

    // The table's start ports: the starter 0x434E80 asserts a positive distance and tests nothing else (GT59), so every gate passes; nothing loads; a pushed object that is gone is not started for.
    private sealed class StartPorts(AreaKnockbackHost host) : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => host._viewer.Tile(owner) is null;
    }
}
