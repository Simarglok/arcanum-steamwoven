using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-289 (MIG14 S8, docs/architecture/saved-runs.md §3.5; schema 84): what a spell missile's host knows of a missile in flight, which the World host writes into the session at the
/// launch and at every move (<see cref="GameSessionOwner.MoveMissile"/>), so a save holds it and a load rebuilds the host's missile: the caster's entity (the hero's <c>pc</c> or an NPC),
/// the spell run it releases, the spell, the target object (null for a tile cast), the faced art, the steps per tick and the pace after a leg, the line's two tiles with the next sample,
/// the pixel position and the line's steps (the line is rebuilt from its two tiles, <see cref="ProjectileRules.Resume"/>), the steps taken (the art frame follows them), the art's
/// frames and the legs flown. The original keeps the caster in slot 4 of the run and the path in the run's data (GT52, GT58).
/// </summary>
public sealed record SessionMissileFlight(string Caster, int Run, int Spell, string? Target, uint Art, int StepsPerTick, int PaceMs, CellCoord LineStart, CellCoord LineEnd,
    int Next, long X, long Y, long LineSteps)
{
    /// <summary>The steps the missile took (its art frame follows them).</summary>
    public long Steps { get; init; }
    /// <summary>The frames of the missile's art (1 until the draw read them).</summary>
    public int Frames { get; init; } = 1;
    /// <summary>The legs the missile flew (a leg's setup counts one).</summary>
    public int Legs { get; init; }
}

public sealed partial class GameSessionOwner
{
    // ARC-DEV-289: the bounds a saved missile's flight is held to (the speeds and paces the projectile table and the art can give, with room).
    private const int MaxMissileStepsPerTick = 1_000, MaxMissilePaceMs = 60_000, MaxMissileFrames = 127;

    // A missile is saved when the host wrote a flight the restore accepts, it owns exactly one live run, not the one being stepped, whose bottom is goal 28 for the flight's spell
    // run, and it still holds that spell run (MissileHoldsRun: the missile's arrival begins it). A missile whose run arrived or was lost meanwhile is gone with it.
    private bool MissileIsSaved(SessionMissile missile) =>
        missile.Flight is { } flight && MissileFlightFits(flight)
        && _goals.Runs.Count(run => string.Equals(run.Owner, missile.Entity, StringComparison.Ordinal)) == 1
        && _goals.Runs.Single(run => string.Equals(run.Owner, missile.Entity, StringComparison.Ordinal)) is { Done: false } run
        && AiExecutorRules.IsMissileRunGoal(run.Bottom.Goal) && run.Bottom.SpellRun == flight.Run && _goals.SteppingSlot != run.Handle.Slot && GoalRunIsSavedKind(run)
        && MissileHoldsRun(flight)
        // A dropped missile and the new cast's missile in the reused slot can both fly for one tick: the newer one is the run's.
        && !_missileObjects.Values.Any(other => other.Flight?.Run == flight.Run && SessionMissile.Number(other.Entity) > SessionMissile.Number(missile.Entity));

    /// <summary>
    /// ARC-DEV-289: whether a missile's flight still holds the spell run it names: the run waits for its BEGIN, it is the flight's cast (the same spell, and its parent is the flight's
    /// caster), and no casting goal before its release frame names that slot (a run freed under the missile and its slot taken by a new cast, QC6, is the new cast's: the
    /// dropped missile releases nothing). The save and a cast host built later (its release and missile marks) both ask it.
    /// </summary>
    internal bool MissileHoldsRun(SessionMissileFlight flight) =>
        _spellRuns[flight.Run] is { } run && SpellRunAwaitsBegin(run) && run.Spell == flight.Spell && string.Equals(run.Parent?.Id, flight.Caster, StringComparison.Ordinal)
        && !_goals.Runs.Any(r => !r.Done && r.Frames.Any(f => f.SpellRun == flight.Run && UnreleasedCast(f)));

    // A casting goal that has not reached its release frame: the start goals 26 and 82, or the attempt goals 27 and 83 before their action frame (node 3 follows it).
    private static bool UnreleasedCast(GoalFrame frame) =>
        frame.Goal is GoalType.CastSpell or GoalType.CastSpellWithAnimation || frame.Goal is GoalType.AttemptSpell or GoalType.AttemptSpellWithAnimation && frame.Node < 3;

    // The bounds a saved flight is held to, at the capture (cut, not written) and at the restore (refused).
    private static bool MissileFlightFits(SessionMissileFlight f) =>
        f.Run is >= 0 and < SpellRunTable.Slots && f.Spell >= 0 && f.StepsPerTick is >= 0 and <= MaxMissileStepsPerTick && f.PaceMs is >= 1 and <= MaxMissilePaceMs
        && f.Frames is >= 1 and <= MaxMissileFrames && f.Legs >= 0 && f.Steps >= 0 && DropCellFits(f.LineStart) && DropCellFits(f.LineEnd);

    // A missile's run is saved with its missile only (the goals capture asks this for an owner of the missile origin).
    private bool MissileRunIsSaved(GoalRun run) =>
        _missileObjects.TryGetValue(run.Owner, out var missile) && MissileIsSaved(missile);

    // ARC-DEV-289: the goal runs and the spell runs a save writes, decided together. A spell run is saved when it is settled (S4), or when it waits for its BEGIN and a saved goal
    // run names it (an NPC's casting goal 26, 82, 27 or 83 before or after its release frame, a missile's goal 28) or its BEGIN event is pending (a run cast with no parent, whose
    // BEGIN comes as a magic.run event). A goal run is saved when it passes on its own (GoalRunIsSavedAlone) and every spell run its frames name is saved or its slot is empty (a run
    // freed meanwhile: the goal's end then finds nothing, as it would live). A run waiting for a host callback (Callback, EndCallback) is not saved, so a goal that names it is cut, and
    // so are the waiting runs only that goal named: the test is repeated until nothing more is cut.
    private (HashSet<int> Spells, HashSet<int> Goals) SavedCasts()
    {
        List<GoalRun> candidates = _goals.InUseCount == 0 ? [] : [.. _goals.Runs.Where(GoalRunIsSavedAlone)];
        var begun = _events.QueryPending(SessionEventKinds.SpellRun)
            .Where(e => e.Payload.Values.Length > 2 && e.Payload.Values[2] == (long)SpellRunEventCode.Process).Select(e => (int)e.Payload.Values[1]).ToHashSet();
        while (true)
        {
            var named = candidates.SelectMany(run => run.Frames).Where(f => f.SpellRun is not null).Select(f => f.SpellRun!.Value).ToHashSet();
            var spells = _spellRuns.Runs.Where(r => SpellRunIsSaved(r) || SpellRunAwaitsBegin(r) && (named.Contains(r.Id) || begun.Contains(r.Id))).Select(r => r.Id).ToHashSet();
            // ARC-MAGIC-1280: an overlay run whose eye candy is tied to a spell run that is cut (a run waiting for a host callback, one that waits for a BEGIN nothing names) is cut with it; a tag of a freed run is written as none.
            var kept = candidates.Where(run => run.Frames.All(f => (f.SpellRun is not { } slot || _spellRuns[slot] is null || spells.Contains(slot))
                && (f.EyeCandy is not { Tag: >= 0 } eye || _spellRuns[eye.Tag] is null || spells.Contains(eye.Tag)))).ToList();
            if (kept.Count == candidates.Count) return (spells, kept.Select(run => run.Handle.Slot).ToHashSet());
            candidates = kept;
        }
    }

    // ARC-DEV-289: the shapes of the saved missiles the codec cannot know: on the saved map, numbered at most the counter, each with a flight that can resume, a caster that is a
    // session entity (the hero's only with a hero), a spell run slot of the table, its own run (no two missiles release one run) and bounded counts.
    private static GameSessionFailure ImportMissiles(SessionFlightState saved, SessionMapId map, bool hero)
    {
        // The counter stays where CreateMissile's own increments can go on.
        if (saved.LastMissile is < 0 or > int.MaxValue - 1) return GameSessionFailure.InvalidSnapshot;
        if (saved.Missiles.Count > SessionMissile.MaxMissiles) return GameSessionFailure.BudgetExceeded;
        var numbers = new HashSet<int>();
        var runs = new HashSet<int>();
        foreach (var missile in saved.Missiles)
        {
            if (SessionMissile.Number(missile.Entity) is not { } number || number > saved.LastMissile || !numbers.Add(number) || missile.Map != map) return GameSessionFailure.InvalidSnapshot;
            if (!DropCellFits(missile.Cell) || missile.Flight is not { } f || !MissileFlightFits(f) || !runs.Add(f.Run)) return GameSessionFailure.InvalidSnapshot;
            if (f.Caster == SessionSpellCaster.Hero ? !hero : !SessionValidation.Entity(f.Caster)) return GameSessionFailure.InvalidSnapshot;
            if (f.Target is { } target && !SessionValidation.Entity(target)) return GameSessionFailure.InvalidSnapshot;
            if (ProjectileRules.Resume(f.LineStart, f.LineEnd, f.Next, missile.Cell, f.X, f.Y, f.LineSteps) is null) return GameSessionFailure.InvalidSnapshot;
        }
        return GameSessionFailure.None;
    }

    // ARC-DEV-289: every saved missile has exactly one run of the goals section, its flight (goal 28 for the flight's spell run and target); every missile run names a saved missile.
    private static GameSessionFailure CheckMissileRuns(SessionFlightState saved, GoalRunTable table)
    {
        var entities = saved.Missiles.Select(m => m.Entity).ToHashSet(StringComparer.Ordinal);
        foreach (var run in table.Runs)
            if (run.Owner.StartsWith(SessionMissile.EntityPrefix, StringComparison.Ordinal) && !entities.Contains(run.Owner)) return GameSessionFailure.InvalidSnapshot;
        foreach (var missile in saved.Missiles)
        {
            var runs = table.Runs.Where(run => string.Equals(run.Owner, missile.Entity, StringComparison.Ordinal)).ToArray();
            if (runs.Length != 1 || !AiExecutorRules.IsMissileRunGoal(runs[0].Bottom.Goal) || runs[0].Bottom.SpellRun != missile.Flight!.Run
                || !string.Equals(runs[0].Bottom.Target, missile.Flight.Target, StringComparison.Ordinal)) return GameSessionFailure.InvalidSnapshot;
        }
        return GameSessionFailure.None;
    }

    // ARC-DEV-289: the spell runs that wait for their BEGIN against what waits for them: each is named by a restored goal frame, released by a saved missile or begun by a saved
    // magic.run BEGIN event (the capture writes no other); a goal frame names a slot of the table; a missile's run waits for its BEGIN.
    private static GameSessionFailure CheckCastRuns(SpellRunTable spells, GoalRunTable goals, SessionFlightState flights, SessionEventState events)
    {
        var named = new HashSet<int>();
        foreach (var frame in goals.Runs.SelectMany(run => run.Frames))
            if (frame.SpellRun is { } slot)
            {
                if (slot is < 0 or >= SpellRunTable.Slots) return GameSessionFailure.InvalidSnapshot;
                named.Add(slot);
            }
        // ARC-MAGIC-1280: an overlay's eye candy names a spell run that is held (the capture writes a freed one as no tag).
        foreach (var frame in goals.Runs.SelectMany(run => run.Frames))
            if (frame.EyeCandy is { Tag: >= 0 } eye && spells[eye.Tag] is null) return GameSessionFailure.InvalidSnapshot;
        // A missile's run waits for its BEGIN and is the flight's cast, and no casting goal before its release names that slot (MissileHoldsRun's test on the restored tables).
        foreach (var missile in flights.Missiles)
        {
            var f = missile.Flight!;
            if (spells[f.Run] is not { } run || !SpellRunAwaitsBegin(run) || run.Spell != f.Spell || !string.Equals(run.Parent?.Id, f.Caster, StringComparison.Ordinal)
                || goals.Runs.Any(r => r.Frames.Any(frame => frame.SpellRun == f.Run && UnreleasedCast(frame)))) return GameSessionFailure.InvalidSnapshot;
        }
        var begun = events.Pending.Where(e => e.Kind == SessionEventKinds.SpellRun && e.Values.Length > 2 && e.Values[2] == (long)SpellRunEventCode.Process)
            .Select(e => e.Values[1]).ToHashSet();
        foreach (var run in spells.Runs)
            if (SpellRunAwaitsBegin(run) && !named.Contains(run.Id) && !begun.Contains(run.Id)) return GameSessionFailure.InvalidSnapshot;
        return GameSessionFailure.None;
    }
}
