using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-110: the PC meets the shown map's traps (contract docs/world/trap-state-runtime.md, ledger TT3, TT9, TT10, EX20). A use of a
// trapped object (a container's loot open, a guarded use site) meets its trap before the use script (the use-script host's Traps); the
// walk checks the next tile before each step (0x427110: an unknown trap object spotted now is marked and the walk ends there); a step
// onto a trap object sets it off (0x4305D0) and the walk ends on that tile. A fired native run's effect is the cast host's
// (SessionSpellCaster.ApplyTrapEffect); without a cast host no trap is met. The spot trap roll (S10) is not made, so only the player's
// detect-traps spell flag spots an unknown trap; the 30002 shot's missile (ARC-TECHNOLOGY-1619 applies the shot, ARC-TECHNOLOGY-1635 flies it: TerrainViewerTrapShots.cs), the trap's floating line, its eye candy and the drawing of a spotted
// trap object are later slices.
public partial class TerrainViewerApp
{
    // The trap host of the cached use-script host (TerrainViewerAreaMapTransfer.UseScriptHost builds both).
    private AreaTraps? _areaTraps;

    /// <summary>The shown map's trap host, or null without a session or a shown map.</summary>
    private AreaTraps? AreaTrapsNow() => _readyArea is { } area && UseScriptHost(area.Snapshot) is not null ? _areaTraps : null;

    /// <summary>The cast host's side of a fired trap: the effect through the shown map's cast host, with the cast map's id of the trapped
    /// object and the hero as the triggerer, and the player's spell flags. Null while there is no cast host.</summary>
    private AreaTrapEffects? AreaTrapEffectsNow() =>
        SpellCasterNow() is { } caster && _spellCastMap is { } map
            ? new((row, run, counters, flags) => caster.ApplyTrapEffect(TrapFiring(map, row, SessionSpellCaster.Hero, run, counters, flags)),
                caster.PlayerSpellFlags ?? 0)
            {
                // ARC-DEV-260: a trap set off by a ranged attack or by damage with no attacker has no triggerer.
                ApplyWithoutTriggerer = (row, run, counters, flags) => caster.ApplyTrapEffect(TrapFiring(map, row, null, run, counters, flags)),
                // ARC-WORLD-1469 (TT8, TT9): an NPC or a companion that stepped on a trap object or used a trapped one is the triggerer, and its spell flags gate the step.
                ApplyByCritter = (entity, row, run, counters, flags) => caster.ApplyTrapEffect(TrapFiring(map, row, entity, run, counters, flags)),
                SpellFlagsOf = entity => caster.SpellFlags(new(entity, 0)),
                // ARC-TECHNOLOGY-1619 (TT12, TT15): a 30002 shot lists its shooter candidates around the triggerer and shoots.
                ListsShooters = true,
            }
            : null;

    // The firing of a native trap run with the cast map's ids; a 30002 shot's shooter candidates are the objects around the triggerer with their use scripts (TT12).
    private SessionTrapFiring TrapFiring(AreaSpellCastMap map, AreaObjectRecord row, string? triggerer, Arcanum.Simulation.Traps.NativeTrapRun run, int counters, int flags)
    {
        var firing = new SessionTrapFiring(map.Id(row), triggerer, run, counters, flags);
        if (Arcanum.Simulation.Traps.TrapEffectRules.Of(run.Script, counters) is not { Kind: Arcanum.Simulation.Traps.TrapEffectKind.Shot } || triggerer is null
            || _readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host) return firing;
        return firing with
        {
            Shooters = AreaTrapShooters.Candidates(map.Object(triggerer)?.Location, map.ObjectsInSquare,
                id => map.Row(id) is { } shooter && host.TrapBaseline(shooter) is { } baseline ? (baseline.Script, baseline.Counters) : null),
        };
    }

    /// <summary>
    /// ARC-DEV-260 (ledger OD4, OD15): what the host owes after a hit that brought a door, window, container, scenery, trap or item to 0 hit points:
    /// the object's trap, then its BUST script (<see cref="AreaObjectBreakHost"/>). <paramref name="byHero"/> says the hero's attack broke it,
    /// <paramref name="ranged"/> that the attack was ranged (no trap triggerer); <paramref name="breaker"/> (ARC-TECHNOLOGY-1760) the script entity of an object that is the attacker, the BUST breaker (a trap's shot: the shooter object). A trap's effect can damage objects in turn; those hits are handled by
    /// <see cref="LogObjectHits"/>, which the caller runs after.
    /// </summary>
    private void RunObjectBreakSteps(AreaObjectRecord row, SessionObjectHostSteps steps, bool byHero, bool ranged, string? breaker = null)
    {
        // ARC-SCRIPT-2644: the destroy point the break's gate ran has its awards, floats and creations taken here (the trap and BUST runs below take their own).
        if (_readyArea is { } gated && UseScriptHost(gated.Snapshot) is { } gateHost && gateHost.TakeBreakDestroyRun() is { } destroyRun) Awarded(gateHost, destroyRun);
        // ARC-SCRIPT-2646 (OD7, 0x43F1C0 → 0x424070): the break first interrupts the object's goal runs at level 5, after the trap and before the BUST script.
        var interrupted = false;
        void Interrupt() { if (interrupted) return; interrupted = true; InterruptBrokenObjectRuns(row); }
        RunObjectBreakScripts(row, steps, byHero, ranged, breaker, Interrupt);
        if (AreaObjectBreakHost.Interrupts(steps)) Interrupt();
        // ARC-SCRIPT-2670 (0x43EB30 → 0x43F710): a scenery the break busted has the busted art written and its restart decided at once (the next frame's art scan).
        if (row.Type == SceneryType) ScanSceneryArtNow();
        // ARC-SCRIPT-2620 (OD7–OD11, 0x43F1C0 → 0x43CCA0 → 0x423FF0, 0x459740): a scenery, trap or container the break destroyed ends its goal runs and tells its spell runs, as a script's destroy does (the trap fired before the break, the interrupt first).
        if (AreaObjectBreakHost.EndsRuns(steps) && ShownObjectRef(row) is { } destroyed) EndDestroyedObjectRuns(destroyed);
        // ARC-RENDER-1126 (OD15): the busting or destroying sound plays after the dispatch.
        PlayObjectBreakSounds(row, steps);
    }

    private void RunObjectBreakScripts(AreaObjectRecord row, SessionObjectHostSteps steps, bool byHero, bool ranged, string? breaker = null, Action? interrupt = null)
    {
        if ((steps & (SessionObjectHostSteps.TrapFires | SessionObjectHostSteps.BustScript)) == 0) return;
        if (_areaSession is not { } owner || _readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host) return;
        // While a trap or a script runs, the damage it deals only queues its hits (LogObjectHits stands off): the outer loop takes them, so the
        // depth stays one however a window and its BUST script chain (ODA3).
        var outer = _objectBreakRunning;
        _objectBreakRunning = true;
        AreaObjectBreakRun ran;
        try { ran = AreaObjectBreakHost.Run(host, AreaTrapsNow(), row, steps, byHero, ranged, owner.Rules.Scripts, breaker, interrupt); }
        finally { _objectBreakRunning = outer; }
        if ((ran.Bust ?? ran.TrapScript) is { } scripted) Awarded(host, scripted);
        if (!ran.Any) return;
        GD.Print($"ARC-DEV-260 object-break target={ran.Entity} by-hero={byHero} ranged={ranged} {ran.Describe()}");
        if (ran.Trap is { } met) NoteTrapMeeting(met);
    }

    private bool _objectBreakRunning;

    /// <summary>ARC-SCRIPT-2620: the shown map's script handle of an object row (null before an area is ready).</summary>
    private Arcanum.Scripting.Scr.ScrObjectRef? ShownObjectRef(AreaObjectRecord row) =>
        _readyArea is null || CurrentAreaMap <= 0 ? null : new Arcanum.Scripting.Scr.ScrObjectRef(CurrentAreaMap, AreaUseScriptHost.EntityId(row));

    /// <summary>The PC's step gate (<see cref="AreaGridSession.StepCharge"/>): the walk's trap check of the next tile, then the turn-based
    /// step charge (the walk goal's next-step check 0x4270B0 runs before the step start 0x42E9B0 charges, EX20).</summary>
    private string? HeroStepGate(AdmissionKey key, Arcanum.World.MovementGait gait) => HeroTrapWalkCheck(key) ?? HeroStepCharge(key, gait);

    private string? HeroTrapWalkCheck(AdmissionKey key)
    {
        if (_worldActor.Session is not AreaGridSession grid || AreaTrapsNow() is not { } traps) return null;
        var walk = traps.WalkCheck(WorldCellOf(grid, key.To));
        if (walk.Mark is { Disposition: GameSessionDisposition.Rejected } refused)
            GD.Print($"ARC-WORLD-110 trap walk-check {key.To.X},{key.To.Y} mark refused failure={refused.Failure}: the step goes");
        if (walk.Spotted is not { } row)
        {
            if (walk.Rolls > 0) GD.Print($"ARC-MAGIC-057 trap walk-check {key.To.X},{key.To.Y} spot trap rolls={walk.Rolls}: none spotted, the step goes");
            return null;
        }
        GD.Print("ARC-WORLD-110 trap walk-check " + System.Text.Json.JsonSerializer.Serialize(new { Trap = AreaUseScriptHost.EntityId(row), Cell = new { row.WorldX, row.WorldY },
            Mark = walk.Mark?.Disposition.ToString(), Failure = walk.Mark?.Failure.ToString() }));
        _areaUseStatus = $"trap spotted on the next tile (mes\\trap.mes {AreaTraps.SpottedLine}): the walk stops";
        // The walk goal ends (EX20); a route must not plan around the cell and walk on.
        StopWorldRoute("ROUTE_STOPPED_BY_TRAP");
        RefreshAreaStatus();
        return "TRAP_SPOTTED";
    }

    /// <summary>Per advance: each step the PC started onto a tile meets the first trap object there; a meeting that spots or fires ends the
    /// walk on that tile (TT9).</summary>
    private void NoteTrapSteps(IGridSession session, GridAdvance advance)
    {
        if (session is not AreaGridSession grid || AreaTrapsNow() is not { } traps) return;
        foreach (var evidence in advance.AreaEvidence)
        {
            if ((evidence.Live?.Disposition ?? evidence.Assessment.Disposition) != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open) continue;
            var cell = WorldCellOf(grid, evidence.Key.To);
            // ARC-WORLD-1582 (TT17): the step function ends the walk on any trap object's tile the hero does not step over, whatever the entry point answered.
            var ends = traps.StepEndsWalk(cell);
            var met = traps.Step(cell);
            if (met is not null) NoteTrapMeeting(met);
            if (met?.EndsWalk != true && !ends) continue;
            StopWorldRoute("ROUTE_ENDED_BY_TRAP");
            break;
        }
    }

    /// <summary>ARC-WORLD-1469 (TT8, TT9): the trap meetings the AI host's walkers had (an NPC's or a companion's step onto a trap object, an opener's use of a trapped door) are logged and shown; true when there
    /// were any, whose effects changed bodies.</summary>
    private bool NoteWalkerTraps(AreaAiHost host)
    {
        var meetings = host.TakeTrapMeetings();
        foreach (var notice in meetings)
        {
            GD.Print($"ARC-WORLD-1469 walker-trap walker={notice.Walker} " + System.Text.Json.JsonSerializer.Serialize(new { Trap = AreaUseScriptHost.EntityId(notice.Meeting.Row), Cause = notice.Meeting.Cause.ToString(),
                Outcome = notice.Meeting.Outcome.ToString(), Disposition = notice.Meeting.Result.Mutation.Disposition.ToString(), notice.Meeting.Result.Run?.Script, Removed = notice.Meeting.Result.State.Removed,
                Victims = notice.Meeting.Effect?.Victims.Count }));
            if (notice.Meeting.Outcome is SessionTrapOutcome.NoTrap or SessionTrapOutcome.SteppedOver && !notice.Meeting.Rejected) continue;
            _areaUseStatus = $"{AiName(notice.Walker)}: trap: " + notice.Meeting.Describe();
        }
        if (meetings.Count > 0) RefreshAreaStatus();
        return meetings.Count > 0;
    }

    /// <summary>ARC-WORLD-1469 (TT9): a following companion on its own walk (the AI host does not hold it) arrives on tiles; each meets the first trap object there with the companion as the triggerer. The walk is
    /// cut short at a trap object's tile (ARC-WORLD-1582, TT17): its goal stops there and the follow goal plans again from where the companion stands.</summary>
    private void RunCompanionTraps(IEnumerable<(AreaCompanion Companion, Arcanum.Data.Traversal.AreaStepCell Cell)> stepped)
    {
        if (_aiHost is not { } host) return;
        foreach (var (companion, cell) in stepped)
            if (host.TrapMetOn(companion.Entity, new(checked((int)cell.X), checked((int)cell.Y)))) _areaCompanions?.EndWalkOnTrap(companion.Entity);
        if (NoteWalkerTraps(host)) _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    /// <summary>A guarded use site's run: its trap meeting is logged and shown (ARC-WORLD-110).</summary>
    private AreaScriptedUse Trapped(AreaScriptedUse ran)
    {
        if (ran.Trap is { } met) NoteTrapMeeting(met);
        return ran;
    }

    /// <summary>Logs a trap meeting and shows it on the status line.</summary>
    private void NoteTrapMeeting(AreaTrapMeeting met)
    {
        GD.Print("ARC-WORLD-110 trap " + System.Text.Json.JsonSerializer.Serialize(new { Trap = AreaUseScriptHost.EntityId(met.Row), Cause = met.Cause.ToString(),
            Outcome = met.Outcome.ToString(), Disposition = met.Result.Mutation.Disposition.ToString(), Failure = met.Result.Mutation.Failure.ToString(),
            met.Result.Run?.Script, Removed = met.Result.State.Removed, met.Result.State.Spotted, Victims = met.Effect?.Victims.Count, met.Effect?.Killer }));
        if (met.Outcome is SessionTrapOutcome.NoTrap or SessionTrapOutcome.SteppedOver && !met.Rejected) return;
        _areaUseStatus = "trap: " + met.Describe();
        RefreshAreaStatus();
    }

    private static CellCoord WorldCellOf(AreaGridSession grid, CellCoord chartCell)
    {
        var cell = grid.Chart.ToWorld(chartCell);
        return new(checked((int)cell.X), checked((int)cell.Y));
    }
}
