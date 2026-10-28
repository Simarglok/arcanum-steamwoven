using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-110: the PC meets the shown map's traps (contract docs/world/trap-state-runtime.md, ledger TT3, TT9, TT10, EX20). A use of a
// trapped object (a container's loot open, a guarded use site) meets its trap before the use script (the use-script host's Traps); the
// walk checks the next tile before each step (0x427110: an unknown trap object spotted now is marked and the walk ends there); a step
// onto a trap object sets it off (0x4305D0) and the walk ends on that tile. A fired native run's effect is the cast host's
// (SessionSpellCaster.ApplyTrapEffect); without a cast host no trap is met. The spot trap roll (S10) is not made, so only the player's
// detect-traps spell flag spots an unknown trap; the 30002 shot, the trap's floating line, its eye candy and the drawing of a spotted
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
            ? new((row, run, counters, flags) => caster.ApplyTrapEffect(new(map.Id(row), SessionSpellCaster.Hero, run, counters, flags)),
                caster.PlayerSpellFlags ?? 0)
            : null;

    /// <summary>The PC's step gate (<see cref="AreaGridSession.StepCharge"/>): the walk's trap check of the next tile, then the turn-based
    /// step charge (the walk goal's next-step check 0x4270B0 runs before the step start 0x42E9B0 charges, EX20).</summary>
    private string? HeroStepGate(AdmissionKey key, Arcanum.TerrainViewer.Core.MovementGait gait) => HeroTrapWalkCheck(key) ?? HeroStepCharge(key, gait);

    private string? HeroTrapWalkCheck(AdmissionKey key)
    {
        if (_worldActor.Session is not AreaGridSession grid || AreaTrapsNow() is not { } traps) return null;
        var walk = traps.WalkCheck(WorldCellOf(grid, key.To));
        if (walk.Mark is { Disposition: GameSessionDisposition.Rejected } refused)
            GD.Print($"ARC-WORLD-110 trap walk-check {key.To.X},{key.To.Y} mark refused failure={refused.Failure}: the step goes");
        if (walk.Spotted is not { } row)
        {
            if (walk.RollsNotReady > 0) GD.Print($"ARC-WORLD-110 trap walk-check {key.To.X},{key.To.Y} rolls-not-ready={walk.RollsNotReady} (spot trap roll: S10)");
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
            if ((evidence.Live?.Disposition ?? evidence.Assessment.Disposition) != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open
                || traps.Step(WorldCellOf(grid, evidence.Key.To)) is not { } met) continue;
            NoteTrapMeeting(met);
            if (!met.EndsWalk) continue;
            StopWorldRoute("ROUTE_ENDED_BY_TRAP");
            break;
        }
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
