using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-COMBAT-020: the turn-based round in the AREA view, over the session's round domain (ARC-COMBAT-003). Space toggles the
// turn-based setting (on only for an active hero, ledger RT4; off by default, as the original's setting); with the hero in combat
// mode the toggle starts or ends a round, and R entering combat mode with the setting on starts one. In a round each step the hero
// starts, its attack, a pickup, a loot, a door's use, a cast, a heal and a lock pick charge its action points; a refused charge stops
// the action and passes the turn on (ledger TB9, CT1–CT3). E on the hero's own subturn, with the hero idle, ends it (CT5). With the
// map's AI host (ARC-WORLD-095) an NPC acts in its own subturn: its subturn hook asks its AI, its goal run walks and attacks while
// paying action points, and the subturn passes on when it starts nothing more; without one (automated runs keep the AI off) an
// NPC's subturn ends at once (the original's no-animation path, TB5). Each round end moves the clock one game second; the event pump
// fires the recovery timers then. The hero's other commands wait for its own subturn; companions stand while a round runs.
public partial class TerrainViewerApp
{
    // The original's turn-based setting (default off); a user preference since ARC-UI-013, set on when a quick load brings a round.
    private bool _turnBasedSetting;
    private string _turnBasedNotice = "Space toggles turn-based";
    private readonly AreaSectorEntries _sectorEntries = new();
    private int? _sectorEntriesMap;
    // Round events the host has seen (the smoke reads them).
    private readonly List<SessionCombatEvent> _roundLog = [];

    private bool InTurnBasedRound => CombatModeActive && _areaSession?.QueryCombatRound() is not null;

    /// <summary>The combat-mode context of every toggle: the turn-based setting and the round's world. A round that runs is ended by
    /// leaving whatever the host can scan (leaving needs no world).</summary>
    private SessionCombatModeContext CombatModeContext() => _turnBasedSetting && _combatHost is not null && _worldActor.Session is AreaGridSession
        ? new() { TurnBased = true, World = AreaCombatWorldNow() }
        : _areaSession?.QueryCombatRound() is not null ? new() { TurnBased = true } : SessionCombatModeContext.RealTime;

    /// <summary>Space (the original's turn-based toggle, on the press) and E (the end of the hero's subturn, on the release).</summary>
    private bool RouteTurnBasedKey(WorldKeyDecision decision)
    {
        if (!CombatModeActive || decision.Key is not (WorldKey.Space or WorldKey.E)) return false;
        if (decision.Echo || decision.Pressed != (decision.Key == WorldKey.Space)) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _turnBasedNotice = $"{decision.Key} ignored: palette, dialog or UI focus owns input"; RefreshAreaStatus(); return true; }
        if (decision.Key == WorldKey.Space) ToggleTurnBased(); else EndHeroSubturn();
        return true;
    }

    private void ToggleTurnBased()
    {
        var session = _areaSession!;
        if (session.QueryHero() is null || _combatHost is null || _worldActor.Session is not AreaGridSession) { _turnBasedNotice = "turn-based needs a hero (start with --hero)"; RefreshAreaStatus(); return; }
        var requested = !_turnBasedSetting;
        if (requested && session.QueryHeroLife() is not { Status.IsActive: true }) { _turnBasedNotice = "the hero cannot act: turn-based stays off"; RefreshAreaStatus(); return; }
        PlaceHeroForRound();
        // The original refuses while the PC runs an animation it cannot interrupt; the viewer counts an attack and a cast as such.
        var result = session.SwitchTurnBased(session.Version, _turnBasedSetting, requested, AreaCombatWorldNow(), !AttackPlaying && _spellGoal is null);
        if (result.Store) _turnBasedSetting = requested;
        // ARC-UI-013: the same setting as the options window's default combat mode, kept as a user preference.
        if (result.Store) SaveGameSettings(_gameSettings with { TurnBased = requested });
        LogRound(result.Round?.Events ?? []);
        if (result.Round is { Disposition: GameSessionDisposition.Changed } && session.QueryCombatRound() is not null) InterruptForRound();
        _turnBasedNotice = result switch
        {
            { Disposition: GameSessionDisposition.Rejected } => $"turn-based refused: {result.Failure}",
            { Store: false } => "turn-based unchanged: " + (AttackPlaying || _spellGoal is not null ? "the hero's action cannot be interrupted" : "no round could start"),
            _ => $"turn-based {(_turnBasedSetting ? "on" : "off")}" + (session.QueryCombatRound() is { } round ? $": round {round.Turn} began" : HeroInCombatMode ? "" : " (a round starts with combat mode)"),
        };
        GD.Print($"ARC-COMBAT-020 turn-based setting={_turnBasedSetting} disposition={result.Disposition} failure={result.Failure} store={result.Store} round={session.QueryCombatRound()?.Turn}");
        RefreshAreaStatus();
    }

    /// <summary>A round that just began stops the hero's walk and approaches (the original interrupts animations at its start).</summary>
    private void InterruptForRound()
    {
        StopWorldRoute("ROUTE_STOPPED_BY_ROUND");
        CancelAreaUseApproach("a turn-based round began"); CancelTalkApproach("a turn-based round began"); CancelInventoryApproach("a turn-based round began");
        if (!AttackPlaying) CancelAreaAttack("a turn-based round began");
    }

    /// <summary>
    /// GR19 (ARC-DEV-078): a round a load brought takes its load rule (Original rebuilds it as round 1, Steamwoven resumes it)
    /// before the viewer hands the round's world to any other session call, so no commit keeps it as loaded: every frame before
    /// the world steps, and before each input action that may charge or end a subturn (the hero's charges do it in
    /// <see cref="AreaTurnBased.ChargeHero(GameSessionOwner, int, ISessionCombatWorld, out IReadOnlyList{SessionCombatEvent})"/>).
    /// A rebuilt round interrupts the hero's walk and approaches as a round's start does. NoChange once the rule ran.
    /// </summary>
    private void ResumeLoadedRound()
    {
        if (!InTurnBasedRound || _combatHost is null || _worldActor.Session is not AreaGridSession || _areaSession is not { } session) return;
        PlaceHeroForRound();
        var result = session.ResumeLoadedCombatRound(session.Version, AreaCombatWorldNow());
        LogRound(result.Events);
        if (result.Disposition == GameSessionDisposition.Rejected)
        {
            _turnBasedNotice = "loaded round refused: " + result.Failure;
            GD.Print($"ARC-DEV-078 loaded-round failure={result.Failure}");
            RefreshAreaStatus();
            return;
        }
        if (result.Events.Count == 0) return;
        GD.Print($"ARC-DEV-078 loaded-round rules={session.Rules.Name} round={session.QueryCombatRound()?.Turn} actor={session.QueryCombatRound()?.Actor}");
        if (session.QueryCombatRound() is not null) InterruptForRound();
        RefreshAreaStatus();
    }

    /// <summary>E: the hero's own subturn ends when it runs no action (the original's E with the PC idle, ledger CT5).</summary>
    private void EndHeroSubturn()
    {
        ResumeLoadedRound();
        var session = _areaSession!;
        if (session.QueryCombatRound() is not { } round) { _turnBasedNotice = "E: no turn-based round"; RefreshAreaStatus(); return; }
        if (!round.Actor.IsHero) { _turnBasedNotice = "E: not the hero's turn"; RefreshAreaStatus(); return; }
        if (!HeroIdleForRound()) { _turnBasedNotice = "E: the hero's action has not ended"; RefreshAreaStatus(); return; }
        PlaceHeroForRound();
        var result = session.EndCombatTurn(session.Version, SessionCombatant.Hero, AreaCombatWorldNow());
        LogRound(result.Events);
        _turnBasedNotice = result.Disposition == GameSessionDisposition.Rejected ? $"E refused: {result.Failure}" : "the hero ended its turn";
        GD.Print($"ARC-COMBAT-020 end-turn disposition={result.Disposition} failure={result.Failure}");
        RefreshAreaStatus();
    }

    /// <summary>The real time after which the turn-based event falls due once the host asked for it (the original's 0x4B7080 schedules it 2 ms ahead, TL39).</summary>
    private const long TurnBasedEventDelayMs = 2;

    /// <summary>Per frame, before the hero moves: the sector entries, then, while a round runs, the request for the turn-based event. MIG12 S8
    /// (ARC-DEV-127): the step itself (<see cref="RunTurnBasedStep"/>: the NPCs' subturns and the hero's completion check) runs when the session's
    /// real-time turn-based event falls due, in the frame step, so it fires behind the Esc menu as the original's does. The original's callback
    /// asks for the event when an animation slot is freed; the viewer has no slot hook and asks for it every frame a round runs (one pending at
    /// most), which is the step it ran every frame before. A map without a frame step (drawn unlit: no day/night driver, so no event passes run
    /// there either) keeps the step in the frame.</summary>
    private void ProcessTurnBasedRound()
    {
        ObserveSectorEntries();
        if (!InTurnBasedRound || _combatHost is null || _worldActor.Session is not AreaGridSession) return;
        if (_areaAmbient is null) { RunTurnBasedStep(); return; }
        if (_areaSession!.ScheduleRealStep(SessionRealStep.TurnBasedStep, TurnBasedEventDelayMs) != GameSessionDisposition.Rejected) return;
        // A full queue refuses the request: the round must not stall, so the step runs in the frame as it did before MIG12 S8.
        GD.Print("ARC-COMBAT-020 turn-based event refused by the session; stepping in the frame");
        RunTurnBasedStep();
    }

    /// <summary>The turn-based event's host (<see cref="Arcanum.TerrainViewer.Core.AreaRealStepHost"/>): one pass of the round's host, without a repeat
    /// (the next request comes from the frame).</summary>
    private long? RunTurnBasedStepEvent()
    {
        RunTurnBasedStep();
        return null;
    }

    private void RunTurnBasedStep()
    {
        if (!InTurnBasedRound || _combatHost is null || _worldActor.Session is not AreaGridSession) return;
        // The step now runs at the head of the frame (the frame step), before the world's own pass: a frame in which a transfer, reset or reload is
        // pending was skipped whole by AdvanceWorldGrid, and so is the round's step; a loaded round takes its load rule first (GR19).
        if (AreaTransferPending || _worldResetPending || _worldReloadPending || _worldLifecycleSession is not null) return;
        ResumeLoadedRound();
        if (!InTurnBasedRound) return;
        var session = _areaSession!;
        PlaceHeroForRound();
        var step = AreaTurnBased.Step(session, AreaCombatWorldNow(), HeroIdleForRound(), AreaAiHostNow());
        LogRound(step.Events);
        if (step.Failure != GameSessionFailure.None) { _turnBasedNotice = "round step refused: " + step.Failure; GD.Print($"ARC-COMBAT-020 round-step failure={step.Failure}"); }
        if (step.WriteFailure != GameSessionFailure.None) GD.Print($"ARC-WORLD-095 subturn-hook write-failure={step.WriteFailure}");
        if (step.Events.Count > 0) RefreshAreaStatus();
    }

    /// <summary>The hero runs no action: settled, no walk, attack, cast or held stick.</summary>
    private bool HeroIdleForRound() => _worldActor.Session is AreaGridSession grid && grid.State.Phase == MovementPhase.Settled
        && _attack is null && _spellGoal is null && !_stickHeld && _worldRoute is not { Status: RouteFollowStatus.Following };

    /// <summary>The session's actor follows the walking hero only when asked; the round's square is around it.</summary>
    private void PlaceHeroForRound()
    {
        if (_worldActor.Session is not AreaGridSession grid || _areaSession is not { } owner) return;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        var placement = new ActorPlacement(new(CurrentAreaMap), new(checked((int)cell.X), checked((int)cell.Y)), grid.State.Facing);
        if (owner.QueryActor() == placement) return;
        var placed = owner.PlaceActor(owner.Version, placement);
        if (placed.Disposition == GameSessionDisposition.Rejected) GD.Print($"ARC-COMBAT-020 hero-placement refused failure={placed.Failure}");
    }

    /// <summary>
    /// The PC's step charge (<see cref="AreaGridSession.StepCharge"/>): free outside a round; in one, the step's action points (walk 2,
    /// run 1) through the session; a refused charge, another participant's subturn or an inactive hero refuses the step.
    /// </summary>
    private string? HeroStepCharge(AdmissionKey key, Arcanum.TerrainViewer.Core.MovementGait gait)
    {
        if (!InTurnBasedRound || _combatHost is null || _worldActor.Session is not AreaGridSession grid) return null;
        var session = _areaSession!;
        var from = grid.Chart.ToWorld(key.From);
        var placement = new ActorPlacement(new(CurrentAreaMap), new(checked((int)from.X), checked((int)from.Y)), grid.State.Facing);
        if (session.QueryActor() != placement) session.PlaceActor(session.Version, placement);
        var cost = AreaTurnBased.StepCost(gait, HeroFatigue() ?? 0);
        var charge = AreaTurnBased.ChargeHero(session, cost, AreaCombatWorldNow(), out var events);
        _chargeLog.Add(("step", cost, charge));
        LogRound(events);
        GD.Print($"ARC-COMBAT-020 step-charge cost={cost} charge={charge} ap={session.QueryCombatRound()?.ActionPoints} {key.From.X},{key.From.Y}->{key.To.X},{key.To.Y}");
        if (AreaTurnBased.Proceeds(charge)) { RefreshAreaStatus(); return null; }
        _turnBasedNotice = "step refused: " + AreaTurnBased.Refusal(charge);
        // The walk ends (the original fails the move goal); a route must not plan around the cell and walk on in a later turn.
        StopWorldRoute("ROUTE_STOPPED_BY_TURN_BASED");
        RefreshAreaStatus();
        return "TURN_BASED_" + charge.ToString().ToUpperInvariant();
    }

    /// <summary>A hero's action other than a step (attack, pickup, loot, use): true when it goes ahead; a refusal is shown.</summary>
    private bool ChargeHeroAction(int cost, string what)
    {
        if (!InTurnBasedRound || _combatHost is null || _worldActor.Session is not AreaGridSession) return true;
        PlaceHeroForRound();
        var session = _areaSession!;
        var charge = AreaTurnBased.ChargeHero(session, cost, AreaCombatWorldNow(), out var events);
        _chargeLog.Add((what, cost, charge));
        LogRound(events);
        GD.Print($"ARC-COMBAT-020 action-charge what={what} cost={cost} charge={charge} ap={session.QueryCombatRound()?.ActionPoints}");
        if (AreaTurnBased.Proceeds(charge)) { RefreshAreaStatus(); return true; }
        _turnBasedNotice = $"{what} refused: {AreaTurnBased.Refusal(charge)}";
        RefreshAreaStatus();
        return false;
    }

    /// <summary>The round's world for a session call (null outside the AREA view with a combat host and a hero actor).</summary>
    private ISessionCombatWorld? CombatWorldOrNull() => InTurnBasedRound && _combatHost is not null && _worldActor.Session is AreaGridSession ? AreaCombatWorldNow() : null;

    private AreaCombatWorld AreaCombatWorldNow() => new(CurrentAreaMap, AreaScanCritters, AreaCombatantFacts);

    /// <summary>The shown map's critters in scan order: the hero, the map's NPCs at their stored cells (less what the session destroyed
    /// or switched off and the companions away), and the companions here at their live cells.</summary>
    private IReadOnlyList<AreaScanCritter> AreaScanCritters()
    {
        var grid = (AreaGridSession)_worldActor.Session!;
        var hero = grid.Chart.ToWorld(grid.State.CommittedCell);
        var removed = AreaRemovedObjects();
        var group = _areaCompanions;
        var cells = new Dictionary<string, CellCoord>(StringComparer.Ordinal);
        var npcs = new List<(string Entity, Arcanum.Data.Area.AreaObjectKey Key)>();
        foreach (var companion in group?.Companions ?? [])
        {
            var world = grid.Chart.ToWorld(companion.Grid.State.CommittedCell);
            cells[companion.Entity] = new(checked((int)world.X), checked((int)world.Y));
            npcs.Add((companion.Entity, companion.Key));
        }
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map)
            foreach (var holder in map.Seed.Holders)
            {
                if (holder.Kind != SessionHolderKind.Npc || cells.ContainsKey(holder.Entity) || !map.TryGetRow(holder.Entity, out var row)
                    || removed.Contains(row.Key) || group?.Absent.Contains(row.Key) == true || row.WorldX is not { } x || row.WorldY is not { } y) continue;
                // ARC-WORLD-092: an NPC the AI walked stands where it is now.
                cells[holder.Entity] = _aiHost?.Movers.CellOf(holder.Entity, new(checked((int)x), checked((int)y))) ?? new(checked((int)x), checked((int)y));
                npcs.Add((holder.Entity, row.Key));
            }
        return AreaCombatScan.Critters(npcs, new(checked((int)hero.X), checked((int)hero.Y)), entity => cells.TryGetValue(entity, out var cell) ? cell : null, _sectorEntries);
    }

    /// <summary>An NPC's round facts from its stored fields by identity (ARC-DEV-076, MIG3: wherever it stands, no home-map fallback), in its
    /// session life state; null when it has none or the session destroyed or switched it off.</summary>
    private SessionCombatantFacts? AreaCombatantFacts(string entity)
    {
        var owner = _areaSession!; var host = _combatHost!;
        if (host.Build(entity).Combat is not { } combat || host.Fields(entity) is not { } fields) return null;
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetRow(entity, out var row)
            && AreaRemovedObjects().Contains(row.Key)) return null;
        var state = owner.QueryLife(combat.Target).View?.Record.State ?? combat.Loaded;
        return AreaCombatWorld.Facts(combat, fields, state);
    }

    /// <summary>Where the hero and the companions stand, for the scan's in-sector order; cleared with a map change.</summary>
    private void ObserveSectorEntries()
    {
        if (!CombatModeActive || _worldActor.Session is not AreaGridSession grid) return;
        if (_sectorEntriesMap != CurrentAreaMap) { _sectorEntries.Clear(); _sectorEntriesMap = CurrentAreaMap; }
        var hero = grid.Chart.ToWorld(grid.State.CommittedCell);
        _sectorEntries.Observe(null, new(checked((int)hero.X), checked((int)hero.Y)));
        foreach (var companion in _areaCompanions?.Companions ?? [])
        {
            var world = grid.Chart.ToWorld(companion.Grid.State.CommittedCell);
            _sectorEntries.Observe(companion.Entity, new(checked((int)world.X), checked((int)world.Y)));
        }
    }

    /// <summary>A replaced session (a quick load) keeps the setting, and a round it brings turns the setting on (and the saved
    /// preference with it, ARC-UI-013: the original's save carries the setting and its settings file keeps it).</summary>
    private void SyncTurnBasedSetting(GameSessionOwner session)
    {
        if (session.QueryCombatRound() is not null) { _turnBasedSetting = true; SaveGameSettings(_gameSettings with { TurnBased = true }); }
        _sectorEntries.Clear(); _sectorEntriesMap = null;
    }

    private void LogRound(IReadOnlyList<SessionCombatEvent> events)
    {
        foreach (var e in events)
        {
            _roundLog.Add(e);
            GD.Print($"ARC-COMBAT-020 round-event kind={e.Kind} who={e.Combatant?.ToString() ?? "-"} value={e.Value}");
        }
        if (_roundLog.Count > 4096) _roundLog.RemoveRange(0, _roundLog.Count - 4096);
    }

    private string TurnBasedStatus() => !CombatModeActive || _areaSession?.QueryHero() is null ? ""
        : " | " + AreaTurnBased.Status(_turnBasedSetting, _areaSession.QueryCombatRound(), c => c.Entity ?? "hero") + $" ({_turnBasedNotice})";
}
