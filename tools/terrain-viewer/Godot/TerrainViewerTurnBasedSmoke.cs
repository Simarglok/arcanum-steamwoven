using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Charges the host made (step, attack, pickup, loot, use) with their cost and outcome, for the smoke.
    private readonly List<(string What, int Cost, AreaHeroCharge Charge)> _chargeLog = [];

    // Native observation (ARC-COMBAT-020, the turn-based round) with --hero, after the cast stage: the hero stands three cells from the
    // living NPC with the fewest hit points. Space turns the setting on (no round out of combat mode); R enters combat mode and round 1
    // begins with the NPC first, whose subturn takes no press and ends at once; on the hero's subturn a one-cell walk costs 2 action
    // points; E ends it and round 2 begins one game second later with the points granted again; next to the NPC a click attacks it and
    // the attack's start charges its cost; a long walk then spends the rest, overexerts and is refused, which passes the turn on (round
    // 3). Space turns the setting off (the round ends, combat mode stays), the game clock flows and the hero's fatigue recovers through
    // the event pump; R leaves combat mode. The NPC, the hero's fatigue and the setting are restored for the later stages.
    private async Task RunAreaTurnBasedSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-COMBAT-020 native-smoke turn-based SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-020 turn-based smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TURN_BASED_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core)
            .OrderBy(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) { GD.Print($"ARC-COMBAT-020 native-smoke turn-based NO-TARGET map={map.RuntimeMap}"); return; }

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var numLock = _numLockRun; var option = _alwaysRunOption; var setting = _turnBasedSetting;
        _worldFocusSimulationForSelfTest = true;
        var target = candidates[0];
        var npc = target.Build.Combat!.Target;
        var npcBefore = session.QueryLife(npc).View!.Status.HitPoints;
        var fatigueBefore = session.QueryHeroLife()!.Record.State.FatigueDamage;
        var critter = SessionCombatant.Critter(target.Holder.Entity);
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            _numLockRun = false; _alwaysRunOption = false;
            if (HeroInCombatMode) ToggleCombatMode();
            _turnBasedSetting = false; _chargeLog.Clear(); _roundLog.Clear();
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            CellCoord? Near(int range)
            {
                foreach (var (dx, dy) in new[] { (range, 0), (-range, 0), (0, range), (0, -range) })
                {
                    var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
                    if (!map.Chart.Contains(near)) continue;
                    try { return AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { }
                }
                return null;
            }
            var start = Near(3) ?? AreaSmokeStandable(map, targetCell, 2);
            AreaSmokePlace(map, start);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            void Press(Key key, bool pressed) => InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
            void Tap(Key key) { Press(key, true); Press(key, false); }
            Vector2 Screen(CellCoord cell)
            {
                var center = AreaCellPicking.Center(cell);
                return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)center.X, (float)center.Y);
            }
            void Frame() { _areaSmokeTime++; WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10)); }
            void FramesUntil(Func<bool> done, int limit = 400) { for (var i = 0; i < limit && !done(); i++) Frame(); }
            bool HeroTurn() => session.QueryCombatRound() is { Actor.IsHero: true };
            SessionCombatRound Round() => session.QueryCombatRound() ?? throw new InvalidOperationException("ARC-COMBAT-020 smoke: no round");
            _areaSmokeTime *= 10; _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);

            // Space out of combat mode: the setting only.
            Tap(Key.Space);
            Require(_turnBasedSetting && session.QueryCombatRound() is null, "Space turned the setting on without a round: " + _turnBasedNotice);
            // R: combat mode and round 1, the NPC first (the party goes last).
            Tap(Key.R);
            Require(HeroInCombatMode && session.QueryCombatRound() is { Turn: 1 } first && first.Participants.Contains(critter), "R began round 1 with the NPC: " + _combatModeNotice);
            var clock = session.QueryClockSnapshot().GameTimestampMs;
            var far = RunGoal(map, start);
            Require(far is not null, "a far cell to walk to");
            var ignored = false;
            if (!Round().Actor.IsHero)
            {
                _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(far!.Value), GlobalPosition = Screen(far.Value) });
                ignored = _worldRoute is not { Status: RouteFollowStatus.Following } && _worldRouteClick.Contains("another participant", StringComparison.Ordinal);
                Require(ignored, "a press on the NPC's subturn is ignored: " + _worldRouteClick);
            }
            FramesUntil(HeroTurn, 20);
            Require(HeroTurn(), "the NPC's subturn passed to the hero");
            var grant = Round().ActionPoints;
            Require(grant >= TurnBasedRules.MinActionPoints, $"the hero's grant {grant}");

            // One walk step.
            var grid = (AreaGridSession)_worldActor.Session!;
            var step = Enum.GetValues<NeighborDirection>().Select(d => (Direction: d, Cell: RoutePlanner.Neighbor(grid.State.CommittedCell, d)))
                .First(n => new AreaRouteSource(map.Steps, map.Chart).Assess(grid.State.CommittedCell, n.Direction) == RouteStepVerdict.Open && n.Cell != targetCell).Cell;
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(step), GlobalPosition = Screen(step) });
            FramesUntil(() => _worldRoute is not { Status: RouteFollowStatus.Following } && _worldActor.Session!.State.Phase == MovementPhase.Settled);
            Require(((AreaGridSession)_worldActor.Session!).State.CommittedCell == step, "the hero walked one cell");
            Require(HeroTurn() && Round().ActionPoints == grant - TurnBasedRules.WalkStepCost, $"the step cost 2: {grant} -> {Round().ActionPoints}");

            // E ends the subturn; round 2 one game second later, the grant again.
            Tap(Key.E);
            FramesUntil(HeroTurn, 20);
            Require(Round().Turn == 2 && HeroTurn() && Round().ActionPoints == grant, $"E passed the turn: round {Round().Turn}, AP {Round().ActionPoints}");
            Require(session.QueryClockSnapshot().GameTimestampMs == clock + TurnBasedRules.RoundGameTimeMs, "one game second per round");

            // Next to the NPC a click attacks it; the start charges the attack's cost.
            var adjacent = Near(1);
            Require(adjacent is not null, "a cell next to the NPC");
            AreaSmokePlace(map, adjacent!.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            _areaSmokeTime *= 10; _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            var point = await HoverPointAsync(this, commands[target.Row.Key]) ?? throw new InvalidOperationException("ARC-COMBAT-020 smoke: NPC not visible");
            var cost = TurnBasedRules.AttackCost(session.QueryHeroAttack()!.WeaponSpeed);
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            FramesUntil(() => _attack is null, 400);
            HoverReleaseForSelfTest();
            var attack = _chargeLog.LastOrDefault(c => c.What == "attack");
            Require(attack.What == "attack" && attack.Cost == cost && AreaTurnBased.Proceeds(attack.Charge), $"the attack charged {cost}: {attack}");
            var npcAfter = session.QueryLife(npc).View!.Status.HitPoints;

            // A long walk spends the rest, overexerts and is refused: the turn passes on to round 3.
            if (!HeroTurn()) FramesUntil(HeroTurn, 20);
            var turn = Round().Turn;
            // With odd points left the walk's last step overexerts: a pickup's 1 point makes even points odd first.
            if (Round().ActionPoints % 2 == 0) Require(ChargeHeroAction(TurnBasedRules.PickUpCost, "pickup") && HeroTurn(), "a pickup's point");
            var fatigue = session.QueryHeroLife()!.Record.State.FatigueDamage;
            grid = (AreaGridSession)_worldActor.Session!;
            far = RunGoal(map, grid.State.CommittedCell);
            Require(far is not null, "a far cell from the NPC");
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(far!.Value), GlobalPosition = Screen(far.Value) });
            FramesUntil(() => _worldRoute is not { Status: RouteFollowStatus.Following } && _worldActor.Session!.State.Phase == MovementPhase.Settled);
            var refused = _chargeLog.LastOrDefault(c => c.What == "step");
            Require(refused.Charge == AreaHeroCharge.Refused && _worldRoute?.StopReason == "ROUTE_STOPPED_BY_TURN_BASED", $"the walk was refused: {refused} {DescribeWorldRoute(_worldRoute!)}");
            FramesUntil(HeroTurn, 20);
            Require(Round().Turn == turn + 1 && HeroTurn(), $"the refusal passed the turn on: round {Round().Turn}");
            var overexerted = _chargeLog.Count(c => c.Charge == AreaHeroCharge.Overexerted);
            var fatigueAfterWalk = session.QueryHeroLife()!.Record.State.FatigueDamage;
            Require(overexerted == 1 && fatigueAfterWalk > fatigue, $"one overexertion took fatigue: {overexerted}, {fatigue} -> {fatigueAfterWalk}");

            // Space off: the round ends, combat mode stays; the clock flows and the fatigue recovers.
            Tap(Key.Space);
            Require(!_turnBasedSetting && session.QueryCombatRound() is null && HeroInCombatMode, "Space ended the round: " + _turnBasedNotice);
            var damage = session.QueryHeroLife()!.Record.State.FatigueDamage;
            Require(damage > 0 && _areaAmbient is not null, $"fatigue to recover ({damage}) and the clock driver ({_areaAmbient is not null})");
            Require(session.QueryEvents().Any(e => e.Kind == SessionEventKinds.HeroFatigue), "the fatigue recovery is on the queue");
            var host = 30_000_000L;
            TickAreaClock(host);
            for (var i = 0; i < 4000 && session.QueryHeroLife()!.Record.State.FatigueDamage >= damage; i++) TickAreaClock(host += 250);
            Require(session.QueryHeroLife()!.Record.State.FatigueDamage < damage, $"the event pump recovered fatigue: {damage} -> {session.QueryHeroLife()!.Record.State.FatigueDamage}");
            var recovered = $"{damage}->{session.QueryHeroLife()!.Record.State.FatigueDamage}";
            Tap(Key.R);
            Require(!HeroInCombatMode, "R left combat mode");
            RefreshAreaStatus();
            GD.Print("ARC-COMBAT-020 native-smoke turn-based PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Target = target.Row.PrototypeNumber, Grant = grant, AttackCost = cost, Attack = attack.Charge.ToString(),
                HitPoints = $"{npcBefore}->{npcAfter}", Steps = _chargeLog.Count(c => c.What == "step"), Overexerted = overexerted, Ignored = ignored,
                Rounds = _roundLog.Count(e => e.Kind == SessionCombatEventKind.RoundBegan), Fatigue = $"{fatigue}->{fatigueAfterWalk}", Recovered = recovered,
                Parity = "NOT_VERIFIED (charges and keys read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            if (session.QueryCombatRound() is not null) session.EndCombatRound(session.Version);
            _turnBasedSetting = setting;
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            CancelAreaAttack(null);
            var npcNow = session.QueryLife(npc).View?.Status;
            if (npcNow is { } now && (now.HitPoints != npcBefore || now.IsDead))
                session.Heal(session.Version, npc, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            var damage = session.QueryHeroLife()!.Record.State.FatigueDamage - fatigueBefore;
            if (damage > 0) session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = damage });
            PublishWorldSelection();
            _areaSmokeTime = (_areaSmokeTime + 9) / 10;
            _numLockRun = numLock; _alwaysRunOption = option; _worldGait = MovementGait.Walk;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
