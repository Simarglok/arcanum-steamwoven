using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>One spell missile in flight: the created object that owns the run (<see cref="Entity"/>), the cast it releases (the caster's spell run, the target object or none for a tile), its faced art, its steps per
/// tick and pace, and the flight state the viewer draws. The caster and the line are the host's: the original keeps the caster in slot 4 of the run and the path in the run's data (GT52, GT58).</summary>
public sealed class SpellMissile
{
    public required string Entity { get; init; }
    public required ISpellMissileCaster Caster { get; init; }
    public required string CasterObject { get; init; }
    /// <summary>The map the missile flew on (the viewer's map number): a missile whose map is no longer the shown one is lost.</summary>
    public required int Map { get; init; }
    public required int Run { get; init; }
    public required int Spell { get; init; }
    public required string? Target { get; init; }
    public required uint Art { get; init; }
    public required int StepsPerTick { get; init; }
    public required int PaceMs { get; init; }
    public required ProjectileFlight Flight { get; set; }
    /// <summary>The steps taken; the art's frame follows it.</summary>
    public long Steps { get; set; }
    public int Frames { get; set; } = 1;
    /// <summary>The legs flown (a leg's setup counts one).</summary>
    public int Legs { get; set; }
    /// <summary>Why the host ended the run, when a node asked for the end (a refused line, a step off the map, a freed run).</summary>
    public string? Why { get; set; }
    /// <summary>The spell run was freed under the missile (a run of the same spell began, QC6): nothing of the run is touched.</summary>
    public bool Dropped { get; set; }

    /// <summary>ARC-DEV-289 (MIG14 S8): the flight as the session saves it (<see cref="SessionMissileFlight"/>): the launch's facts and the line's state.</summary>
    public SessionMissileFlight Saved() =>
        new(CasterObject, Run, Spell, Target, Art, StepsPerTick, PaceMs, Flight.Start, Flight.Destination, Flight.Next, Flight.X, Flight.Y, Flight.Steps)
        { Steps = Steps, Frames = Frames, Legs = Legs };

    /// <summary>
    /// ARC-DEV-289: the missile a load restored (<paramref name="saved"/>, with the flight the host wrote before the save), flying on for <paramref name="caster"/>, the cast host of the
    /// shown map (its release and missile marks are rebuilt from the session's missiles); null when the session's missile has no flight or its line does not resume.
    /// </summary>
    public static SpellMissile? Rebuild(SessionMissile saved, ISpellMissileCaster caster)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(caster);
        if (saved.Flight is not { } f || ProjectileRules.Resume(f.LineStart, f.LineEnd, f.Next, saved.Cell, f.X, f.Y, f.LineSteps) is not { } flight) return null;
        return new SpellMissile
        {
            Entity = saved.Entity, Caster = caster, CasterObject = f.Caster, Map = saved.Map.RuntimeMap, Run = f.Run, Spell = f.Spell, Target = f.Target, Art = f.Art,
            StepsPerTick = f.StepsPerTick, PaceMs = f.PaceMs, Flight = flight, Steps = f.Steps, Frames = f.Frames, Legs = f.Legs,
        };
    }
}

/// <summary>How a spell missile ended (<see cref="MissileFinished"/>): it arrived and released the cast, its goal ended without a release (MG44: the run is freed unpaid), or its run was gone already.</summary>
public enum MissileOutcome { Arrived, Lost, Dropped }

/// <summary>A spell missile's end for the viewer: the missile, how it ended and why, the release's result when it arrived (null: the re-check failed), a one-line detail for the diagnostic, and the caster's log length at the
/// moment the host acted (the viewer flushes the lines after it).</summary>
public sealed record MissileFinished(SpellMissile Missile, MissileOutcome Outcome, string Why, SessionSpellRunResult? Begun, string Detail, int LogMark);

/// <summary>
/// ARC-DEV-139 (MIG13 S8, docs/architecture/goal-table.md §3.5, the S8 read GT52, GT56–GT58, GT62, GT64): the host of the shown map's spell missiles as owners of goal runs. A missile is a created object of the
/// session (<see cref="SessionMissile"/>, <c>missile:n</c>; ARC-DEV-289: saved with its flight, adopted again after a load) whose run is goal 28 (<c>ag_shoot_spell</c>) with goal 78 pushed for each leg, stepped on the animation clock inside the frame step
/// (<see cref="IGoalStepHost"/>): a tick of steps every 35 ms, the missile's pace after a leg, then the release (the target re-check and the BEGIN of the spell run) when the missile and its target are on the
/// destination, a new leg when the target moved, the same line again when the missile is not there. A refused line, a step off the map, a freed run or an interrupt (a round's start sweeps the run at level 3,
/// GT62) ends the run: its end node destroys the missile and frees the spell run unpaid (MG44). This host answers the missile ports (<see cref="IAiMissileWorld"/>) over the session and the caster; the line map,
/// the target's place and the draw are the viewer's.
/// </summary>
public sealed class AreaMissileHost : IAiMissileWorld, IGoalStepHost
{
    private readonly GameSessionOwner _session;
    private readonly Func<SpellMissile, Func<CellCoord, bool>?> _lineMap;
    private readonly Func<string, CellCoord?> _targetCell;
    private readonly Action<MissileFinished> _finished;
    private readonly Dictionary<string, SpellMissile> _missiles = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    // ARC-DEV-289: the restored missiles the viewer has not adopted yet (its cast host is not built while the spell data loads): their runs wait instead of ending.
    private readonly HashSet<string> _expected = new(StringComparer.Ordinal);
    private readonly StartPorts _startPorts;

    /// <summary>ARC-DEV-289: how long a restored missile's run waits for its adoption before it asks again.</summary>
    public const int ExpectedWaitMs = 35;

    /// <param name="lineMap">The cells of the shown map for a missile (a flight off them fails), or null when the map is not the one the missile flew on (the missile is lost).</param>
    /// <param name="targetCell">Where a target object stands now (its place on the map), or null when it is gone.</param>
    /// <param name="finished">Called once for every missile that ends, after the host acted.</param>
    public AreaMissileHost(GameSessionOwner session, Func<SpellMissile, Func<CellCoord, bool>?> lineMap, Func<string, CellCoord?> targetCell, Action<MissileFinished> finished)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _lineMap = lineMap ?? throw new ArgumentNullException(nameof(lineMap));
        _targetCell = targetCell ?? throw new ArgumentNullException(nameof(targetCell));
        _finished = finished ?? throw new ArgumentNullException(nameof(finished));
        _startPorts = new(this);
    }

    /// <summary>The session this host's missiles belong to.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The runs this host stepped (a test and the diagnostic line read it).</summary>
    public int Steps { get; private set; }

    /// <summary>The missiles in flight, in the order they left.</summary>
    public IReadOnlyList<SpellMissile> Missiles => [.. _order.Select(e => _missiles[e])];

    public int Count => _missiles.Count;

    /// <summary>Whether <paramref name="entity"/> is a missile of this host.</summary>
    public bool Owns(string entity) => _missiles.ContainsKey(entity);

    /// <summary>The missile of a spell run, or null.</summary>
    public SpellMissile? OfRun(int run) => _order.Select(e => _missiles[e]).FirstOrDefault(m => m.Run == run);

    /// <summary>
    /// The launch after the casting release (0x429C40, 0x4248A0): the missile object is created at <paramref name="start"/> and its run, goal 28 for <paramref name="destination"/>, starts (the starter 0x4246E0: no
    /// gate; its first step comes 5 animation milliseconds later). A missile of an earlier run in the same slot (freed meanwhile) is dropped first. False, with the cast's run freed unpaid (MG44), when the object or
    /// the run could not be made.
    /// </summary>
    [OriginalFunction(0x4248A0, OriginalFunctionScope.Partial)]
    public bool Launch(ISpellMissileCaster caster, string casterObject, int map, CellCoord start, CellCoord destination, int run, int spell, string? target, uint art, int stepsPerTick, int paceMs)
    {
        ArgumentNullException.ThrowIfNull(caster);
        foreach (var stale in _order.Select(e => _missiles[e]).Where(m => m.Run == run).ToArray()) Drop(stale, "its slot was reused");
        var entity = _session.CreateMissile(start);
        var missile = new SpellMissile
        {
            Entity = entity ?? "", Caster = caster, CasterObject = casterObject, Map = map, Run = run, Spell = spell, Target = target, Art = art, StepsPerTick = stepsPerTick, PaceMs = paceMs,
            // A line is built at the goal's third node; this placeholder is never stepped.
            Flight = new(start, start, [(0, 0)], 1, start, 0, 0, 0),
        };
        if (entity is null)
        {
            var mark = caster.Log.Count;
            Finish(missile, MissileOutcome.Lost, "the missile could not be created", mark, $"freed={caster.LoseMissile(run)?.Disposition.ToString() ?? "nothing"}");
            return false;
        }
        _missiles[entity] = missile;
        _order.Add(entity);
        // ARC-DEV-289: the session holds the flight from the launch on, so a save made before the first tick keeps the missile.
        Place(missile);
        var started = _session.StartGoal(new GoalStartRequest(entity, GoalStarters.Generic, AiExecutorRules.MissileFrame(target, destination, run)), _startPorts);
        foreach (var end in started.Ended) EndNodes(end);
        if (!started.Started) { Lose(missile, "its run could not start", killRun: true); return false; }
        return true;
    }

    /// <summary>
    /// ARC-DEV-289 (MIG14 S8): takes a missile a load restored (<see cref="SpellMissile.Rebuild"/>) into the host, so its restored run, which the session already holds, steps here.
    /// False, and nothing taken, for a missile of another session's caster, one the host already holds or one the session does not hold.
    /// </summary>
    public bool Adopt(SpellMissile missile)
    {
        ArgumentNullException.ThrowIfNull(missile);
        if (!ReferenceEquals(missile.Caster.Owner, _session) || _missiles.ContainsKey(missile.Entity) || _session.QueryMissile(missile.Entity) is null) return false;
        _expected.Remove(missile.Entity);
        _missiles[missile.Entity] = missile;
        _order.Add(missile.Entity);
        return true;
    }

    /// <summary>ARC-DEV-289: a restored missile that cannot fly on here (its caster or target is not on the shown map, its line does not resume): it is lost as a missile whose map
    /// was left (MG44: its run freed unpaid, the object destroyed and its run killed).</summary>
    public void GiveUpRestored(SpellMissile missile, string why)
    {
        ArgumentNullException.ThrowIfNull(missile);
        _expected.Remove(missile.Entity);
        if (Adopt(missile) || _missiles.ContainsKey(missile.Entity)) Lose(missile, why, killRun: true);
    }

    /// <summary>ARC-DEV-289: a restored missile the viewer will adopt once it can (<see cref="Adopt"/>, <see cref="GiveUpRestored"/> or <see cref="Forget"/>): until then its run's steps
    /// wait (<see cref="ExpectedWaitMs"/>) instead of ending it as a missile of no host.</summary>
    public void Expect(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!_missiles.ContainsKey(entity)) _expected.Add(entity);
    }

    /// <summary>ARC-DEV-289: an expected missile that will not be adopted (no cast host can come): its run ends at its next step.</summary>
    public void Forget(string entity) => _expected.Remove(entity);

    /// <summary>ARC-DEV-289: the restored missiles waiting for their adoption.</summary>
    public IReadOnlyCollection<string> Expected => _expected;

    /// <summary>Every missile is lost (a map change, a quick load, a replaced session): the spell runs are freed unpaid, the objects destroyed and their runs killed (no end node runs twice).</summary>
    public void LoseAll(string why)
    {
        foreach (var entity in _order.ToArray())
            if (_missiles.TryGetValue(entity, out var missile)) Lose(missile, why, killRun: true);
        _expected.Clear();
    }

    /// <summary>The end nodes of a missile's run the table ended from outside the stepper (an interrupt such as a round's start, a map's close): the missile is destroyed and its cast freed unpaid. A reset runs
    /// none, a killed run skips them (GS21) and a run the stepper finished ran them itself; the missile that remains in any of those cases is lost all the same, since no one else would end it.</summary>
    public void EndNodes(GoalRunEnd end)
    {
        if (!_missiles.ContainsKey(end.Run.Owner)) return;
        if (end.Kind is not (GoalEndKind.Reset or GoalEndKind.Finished)) AiExecutorRules.EndMissileRun(end.Run, this);
        if (_missiles.TryGetValue(end.Run.Owner, out var left)) Lose(left, "its run ended");
    }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        // ARC-DEV-289: a restored missile the viewer adopts later waits; a missile of another host (a replaced session, a map left) has nothing here: its run ends.
        if (!_missiles.ContainsKey(run.Owner))
            return _expected.Contains(run.Owner) && _session.QueryMissile(run.Owner) is not null ? new SessionGoalStepAnswer(ExpectedWaitMs) : SessionGoalStepAnswer.End;
        Steps++;
        var stepped = GoalStepper.StepMissile(run, this);
        if (stepped.End is GoalStepperEnd.Killed or GoalStepperEnd.NodeLimit or GoalStepperEnd.Overflow) AiExecutorRules.EndMissileRun(run, this);
        if (_missiles.TryGetValue(run.Owner, out var left) && stepped.Answer.NextDelayMs is null) Lose(left, "its run ended");
        return !_session.QueryGoals().TryGet(run.Handle, out var now) || now.Done ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    // ---- ends ----

    // The goal's end without a release (0x429E70): the missile is destroyed and the cast's run freed unpaid (MG44, both rule modes).
    private void Lose(SpellMissile missile, string why, bool killRun = false)
    {
        if (!_missiles.ContainsKey(missile.Entity)) return;
        var mark = missile.Caster.Log.Count;
        SessionSpellRunResult? freed = null;
        try { freed = missile.Dropped ? null : missile.Caster.LoseMissile(missile.Run); }
        catch (ObjectDisposedException) { /* a replaced session: nothing of it is left to free */ }
        Finish(missile, missile.Dropped ? MissileOutcome.Dropped : MissileOutcome.Lost, missile.Why ?? why, mark, $"freed={freed?.Disposition.ToString() ?? "nothing"}", killRun);
    }

    // A missile whose run is gone is only removed: nothing of the cast is touched.
    private void Drop(SpellMissile missile, string why)
    {
        if (!_missiles.ContainsKey(missile.Entity)) return;
        Finish(missile, MissileOutcome.Dropped, why, missile.Caster.Log.Count, "dropped", killRun: true);
    }

    private void Finish(SpellMissile missile, MissileOutcome outcome, string why, int mark, string detail = "", bool killRun = false)
    {
        if (missile.Entity.Length > 0)
        {
            _missiles.Remove(missile.Entity);
            _order.Remove(missile.Entity);
            try
            {
                if (killRun) _session.KillGoals(missile.Entity);
                _session.DestroyMissile(missile.Entity);
            }
            catch (ObjectDisposedException)
            {
                // A replaced session: nothing of it is left to clean.
            }
        }
        _finished(new(missile, outcome, why, null, detail, mark));
    }

    // ---- the missile ports ----

    private SpellMissile? Of(string missile) => _missiles.GetValueOrDefault(missile);

    public bool MissileAtDestination(string missile, CellCoord destination) => Of(missile) is { } m && m.Flight.Tile == destination;

    public bool MissileTargetHere(string missile, string? target) =>
        Of(missile) is not { } m || target is null || _targetCell(target) is not { } there || there == m.Flight.Tile;

    public CellCoord? MissileRetarget(string missile, string? target, CellCoord destination) =>
        target is not null && _targetCell(target) is { } there && there != destination ? there : null;

    public bool MissileBuildLine(string missile, CellCoord destination)
    {
        if (Of(missile) is not { } m) return false;
        var leg = SpellMissileRules.Leg(m.Flight.Tile, destination);
        if (leg.Kind == SpellMissileLegKind.Refused) { m.Why = "the line is too long"; return false; }
        if (leg.Flight is { } flight) { m.Flight = flight; Place(m); }
        return true;
    }

    public bool MissileSpeed(string missile) => Of(missile) is not null;

    public bool MissileRelease(string missile)
    {
        if (Of(missile) is not { } m) return false;
        // Its run was freed during the pace wait (QC6): nothing arrives, nothing of the run is touched.
        if (!m.Caster.MissileFlies(m.Run)) { m.Dropped = true; m.Why = "its run was freed"; return false; }
        // The object was destroyed during the pace wait: there is nothing to arrive; the end node loses it and frees the cast.
        if (_session.QueryMissile(m.Entity) is null) { m.Why = "its object was destroyed"; return false; }
        var mark = m.Caster.Log.Count;
        var begun = m.Caster.ArriveMissile(m.Run);
        _missiles.Remove(m.Entity);
        _order.Remove(m.Entity);
        _finished(new(m, MissileOutcome.Arrived, begun is null ? "the target is no longer valid" : "arrived", begun,
            $"begun={begun?.Disposition.ToString() ?? "re-check failed"} failure={begun?.Failure}", mark));
        return begun is not null;
    }

    public void MissileDestroy(string missile) => _session.DestroyMissile(missile);

    public void MissileLost(string missile)
    {
        if (Of(missile) is { } m) Lose(m, "its run ended");
        else _session.DestroyMissile(missile);
    }

    public int MissileLegSetup(string missile)
    {
        if (Of(missile) is not { } m) return GoalRun.DefaultPaceMs;
        m.Legs++;
        // The same line flown again starts its steps at the first (goal 78 pushed again, SM9).
        m.Flight = SpellMissileRules.Replay(m.Flight);
        Place(m);
        return m.PaceMs;
    }

    public MissileTickResult MissileTick(string missile)
    {
        if (Of(missile) is not { } m) return MissileTickResult.OffMap;
        // The object was destroyed under its run (the directory's one destroy path): the missile is lost, its cast freed.
        if (_session.QueryMissile(m.Entity) is null) { m.Why = "its object was destroyed"; return MissileTickResult.OffMap; }
        // Its run was freed meanwhile (a run of the same spell that began cancelled it, QC6): the missile goes with it.
        if (!m.Caster.MissileFlies(m.Run)) { m.Dropped = true; m.Why = "its run was freed"; return MissileTickResult.OffMap; }
        if (_lineMap(m) is not { } contains) { m.Why = "the map or session changed"; return MissileTickResult.OffMap; }
        var tick = SpellMissileRules.Tick(m.Flight, m.StepsPerTick, contains);
        m.Steps += tick.Flight.Steps - m.Flight.Steps;
        m.Flight = tick.Flight;
        Place(m);
        if (tick.OffMap) { m.Why = "it left the map"; return MissileTickResult.OffMap; }
        return tick.LegEnded ? MissileTickResult.LegEnded : MissileTickResult.Continue;
    }

    // The missile's place on its tile with the pixel offsets from that tile's centre (the ordinary object move 0x43E770 per step, GT64), with the flight the session saves (ARC-DEV-289).
    private void Place(SpellMissile m)
    {
        var start = LineOfFireRules.Screen(m.Flight.Start);
        var here = LineOfFireRules.Screen(m.Flight.Tile);
        _session.MoveMissile(m.Entity, m.Flight.Tile, checked((int)(start.X + m.Flight.X - here.X)), checked((int)(start.Y + m.Flight.Y - here.Y)), m.Saved());
    }

    // The table's start ports: a missile's start passes every gate (the starter 0x4246E0 calls none, GT42); nothing loads; a destroyed missile is not started for.
    private sealed class StartPorts(AreaMissileHost host) : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => host._session.QueryMissile(owner) is null;
    }
}
