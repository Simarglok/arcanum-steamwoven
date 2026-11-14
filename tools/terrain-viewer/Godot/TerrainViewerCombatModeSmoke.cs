using Arcanum.Simulation.Ai;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-COMBAT-004) with --hero, on the installed exterior after the run stage or in map mode after
    // the hero stage: R puts the hero in combat mode (status line), a Ctrl click runs a route and every fifth run step
    // costs one fatigue, a dialog start and a map change leave combat mode, and R toggles it again. The hero's fatigue is
    // healed back afterwards.
    private async Task RunAreaCombatModeSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null) return;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var numLock = _numLockRun; var option = _alwaysRunOption;
        _worldFocusSimulationForSelfTest = true;
        var events = new List<MovementEvent>();
        var priorRules = session.Rules.Mode;
        try
        {
            static void Require(bool value, string why)
            {
                if (value) return;
                GD.Print("ARC-COMBAT-004 combat-mode smoke FAIL: " + why);
                throw new InvalidOperationException("AREA_COMBAT_MODE_SMOKE_FAILED");
            }
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            _numLockRun = false; _alwaysRunOption = false;
            Require(mapMode is not null || _areaTransitions?.CurrentMap == 1 && !InCave, "the exterior is published");
            Require(!HeroInCombatMode, "the hero starts out of combat mode");
            void PressR()
            {
                InputWorld(new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = true });
                InputWorld(new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = false });
            }
            PressR();
            Require(HeroInCombatMode, "R entered combat mode");
            // ARC-UI-056: the battle cursor in combat mode; a called-shot key's cursor by the key in Original, by the aimed
            // location in Steamwoven (CUA1).
            SyncCursorArt();
            Require(CursorShownForSelfTest?.Art == AreaCursorArt.Battle, $"the battle cursor in combat mode: {CursorShownForSelfTest}");
            foreach (var rules in new[] { RuleMode.Steamwoven, RuleMode.Original })
            {
                Require(session.SelectRules(rules) != GameSessionDisposition.Rejected, $"{rules} selected");
                var swapped = rules == RuleMode.Original;
                foreach (var (aim, art) in new[] { (HitLocation.Head, AreaCursorArt.CalledHead),
                    (HitLocation.Arm, swapped ? AreaCursorArt.CalledLeg : AreaCursorArt.CalledArm), (HitLocation.Leg, swapped ? AreaCursorArt.CalledArm : AreaCursorArt.CalledLeg) })
                {
                    _attackAimForSelfTest = aim; SyncCursorArt();
                    Require(CursorShownForSelfTest?.Art == art, $"{rules} {aim} key shows art {art}: {CursorShownForSelfTest}");
                }
            }
            _attackAimForSelfTest = null; session.SelectRules(priorRules); SyncCursorArt();
            Require(CursorShownForSelfTest?.Art == AreaCursorArt.Battle, "the battle cursor again once the key is up");
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains("combat mode: ON (real time)", StringComparison.Ordinal), "HUD names combat mode");

            var map = mapMode is { Area: { } area, AreaStepSnapshot: { } mapSteps }
                ? new AreaMapRuntime(area.Snapshot, mapSteps, area.Plan.Chart) : _areaTransitions!.Current;
            var start = AreaSmokeStandable(map, map.Chart.Spawn, 2);
            // ARC-COMBAT-006: the goal is a cell the PC can walk to about 12 cells away (a breadth-first search over open steps),
            // not a fixed offset, which may fall in a wall on another map.
            var far = RunGoal(map, start);
            Require(far is not null, "a cell the PC can walk to at least 8 cells from its start");
            AreaSmokePlace(map, start);
            _worldAdvanceObserverForSelfTest = advance => events.AddRange(advance.Movement.Events);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Vector2 Screen(CellCoord cell)
            {
                var center = AreaCellPicking.Center(cell);
                return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)center.X, (float)center.Y);
            }
            RouteFollower Route() => _worldRoute ?? throw new InvalidOperationException("ARC-COMBAT-004 smoke: no route");
            var before = session.QueryHeroLife()!.Record.State.FatigueDamage;
            _areaSmokeTime *= 10; _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, CtrlPressed = true, Position = Screen(far!.Value), GlobalPosition = Screen(far.Value) });
            Require(HeroWalkGoal == far && _worldGait == MovementGait.Run, $"the Ctrl click set a running walk: {DescribeWorldRoute(Route())}");
            // ARC-DEV-149: the table walks the hero: the area clock is ticked 0.1 s a frame while its run is on the table.
            var walkHostMs = 33_000_000L;
            for (var frames = 0; frames < 600 && !(!HeroWalking && _worldActor.Session!.State.Phase == MovementPhase.Settled); frames++)
            {
                _areaSmokeTime++;
                if (ShownHeroRunsHost?.WalkRun is not null) TickAreaClock(walkHostMs += 100);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
            }
            Require(_worldActor.Session is AreaGridSession arrivedGrid && arrivedGrid.State.CommittedCell == far, "arrived: " + DescribeWorldRoute(Route()));
            var runSteps = events.Count(e => e.Kind == MovementEventKind.StepStarted && e.Assessment!.Provenance.Contains(GaitTiming.RunProvenance));
            var charged = session.QueryHeroLife()!.Record.State.FatigueDamage - before;
            Require(runSteps >= RealTimeCombatRules.RunChargeSteps && charged == runSteps / RealTimeCombatRules.RunChargeSteps,
                $"{runSteps} run steps charged {charged} fatigue");
            Require(session.QueryEvents().Any(e => e.Kind == SessionEventKinds.HeroFatigue), "the fatigue recovery is scheduled");

            // A held stick with Num Lock run is one move: its run steps count across frames (the press starts the counter).
            // ARC-COMBAT-006: the first stick direction that runs at least one charge of steps on this map (a wall may stop one).
            _numLockRun = true;
            var stickSteps = 0; var stickCharged = 0;
            foreach (var stick in new Vec2[] { new(1, -1), new(-1, 1), new(1, 1), new(-1, -1), new(1, 0), new(-1, 0), new(0, 1), new(0, -1) })
            {
                events.Clear();
                var beforeStick = session.QueryHeroLife()!.Record.State.FatigueDamage; var chargesBefore = CombatRunCharges;
                // ARC-DEV-147: the stick is a held walk of the table, whose run is stepped by the area clock (0.1 s a frame here, as the click walk's).
                for (var frames = 0; frames < 40; frames++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(walkHostMs += 100);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(stick) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                }
                // At least one neutral frame: the next direction is a new press, which starts its own run-step count.
                for (var frames = 0; frames < 20 && (frames == 0 || _worldActor.Session!.State.Phase != MovementPhase.Settled || ShownHeroRunsHost?.WalkRun is not null); frames++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(walkHostMs += 100);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                }
                stickSteps = events.Count(e => e.Kind == MovementEventKind.StepStarted && e.Assessment!.Provenance.Contains(GaitTiming.RunProvenance));
                // The stick's walk runs on the area clock, so the fatigue recovers meanwhile: the charges are counted, not read off the fatigue.
                stickCharged = CombatRunCharges - chargesBefore;
                charged += stickCharged;
                if (stickSteps >= RealTimeCombatRules.RunChargeSteps) break;
            }
            _numLockRun = false;
            Require(stickSteps >= RealTimeCombatRules.RunChargeSteps && stickCharged == stickSteps / RealTimeCombatRules.RunChargeSteps,
                $"{stickSteps} stick run steps charged {stickCharged} fatigue");

            Require(LeaveCombatModeForDialog() && !HeroInCombatMode, "a dialog start left combat mode");
            PressR();
            Require(HeroInCombatMode, "R entered combat mode again");
            LeaveCombatModeForMapChange();
            Require(!HeroInCombatMode, "a map change left combat mode");
            PressR(); PressR();
            Require(!HeroInCombatMode, "R toggles off");
            SyncCursorArt();
            Require(CursorShownForSelfTest?.Art == AreaCursorArt.Gauntlet, $"the gauntlet out of combat mode: {CursorShownForSelfTest}");
            // ARC-DEV-147: the held stick's walk ticks the area clock, so the fatigue also recovered by itself meanwhile: what is left over what the hero started with is healed back.
            var left = session.QueryHeroLife()!.Record.State.FatigueDamage - before;
            var healed = left > 0 ? session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = left }) : null;
            Require((healed is null || healed.Disposition != GameSessionDisposition.Rejected) && session.QueryHeroLife()!.Record.State.FatigueDamage <= before, "fatigue healed back");
            GD.Print("ARC-COMBAT-004 native-smoke combat-mode PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Start = map.Chart.ToWorld(start), Goal = map.Chart.ToWorld(far.Value), RunSteps = runSteps, StickRunSteps = stickSteps, Charged = charged,
                Parity = "NOT_VERIFIED (rules checked in the original executable; no original-runtime run)" }));
        }
        finally
        {
            _worldAdvanceObserverForSelfTest = null;
            _areaSmokeTime = (_areaSmokeTime + 9) / 10;
            _numLockRun = numLock; _alwaysRunOption = option; _worldGait = MovementGait.Walk;
            _attackAimForSelfTest = null; session.SelectRules(priorRules);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    /// <summary>
    /// ARC-COMBAT-1935 (ledger VC58, VC59, VC60): with an active NPC near the hero in the combat-focus state with the hero as its focus, the hero in combat mode stays in it for the R key, the
    /// right release and a dialog start (the exit gate's refusal, intgame message 24, no dialog) and the world map's opener (message 600, no map, ARC-UI-1940); once the NPC is calm the same R leaves it. The NPC's fight is put back afterwards.
    /// </summary>
    private void RunAreaCombatExitThreatSmoke(GameSessionOwner session, AreaAiHost host, string entity, IEnumerable<string> pack)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-1935 combat-exit smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_COMBAT_EXIT_SMOKE_FAILED");
        }
        Require(HeroInCombatMode, "the hero is in combat mode");
        Require(host.CombatThreats().Any(t => t.Active && t.Fighting && t.Focus.IsHero), "the NPC is a threat the scan sees");
        const string Refused = "cannot leave combat mode when under attack";
        Vector2 Screen() => _canvas!.GetGlobalTransformWithCanvas() * new Vector2(WorldViewport.X / 2f, WorldViewport.Y / 2f);
        void PressR()
        {
            InputWorld(new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = true });
            InputWorld(new InputEventKey { Keycode = Key.R, PhysicalKeycode = Key.R, Pressed = false });
        }
        _combatModeNotice = "";
        PressR();
        Require(HeroInCombatMode && _combatModeNotice.Contains(Refused, StringComparison.Ordinal), $"R keeps combat mode while the NPC fights the hero: {_combatModeNotice}");
        _combatModeNotice = "";
        WorldRightRelease(Screen());
        Require(HeroInCombatMode && _combatModeNotice.Contains(Refused, StringComparison.Ordinal), $"the right release keeps combat mode: {_combatModeNotice}");
        _combatModeNotice = "";
        Require(!LeaveCombatModeForDialog() && HeroInCombatMode && _combatModeNotice.Contains(Refused, StringComparison.Ordinal), $"a dialog start opens no dialog and keeps combat mode: {_combatModeNotice}");
        // ARC-UI-1940 (WM3, VC60): the world map's opener asks the same gate: message 600 and no map while the NPC fights the hero, the hero still in combat mode.
        Require(WorldMapActive && WindowCanOpen(InterfaceMode.WorldMap), "the world map can be opened here");
        _worldMapStatus = null;
        OpenWorldMap();
        Require(HeroInCombatMode && !WorldMapOpen && _worldMapStatus?.Contains("message 600", StringComparison.Ordinal) == true, $"the world map opener keeps combat mode and shows message 600: open={WorldMapOpen} status={_worldMapStatus}");
        _worldMapStatus = null;
        // The NPC calm: the same key leaves, and the fight is put back for the stage that follows.
        // Every NPC of the map's pack is calmed (a neighbour that joined the fight would keep the gate shut): the scan then finds no threat.
        CalmNpcsForSmoke(session, pack.Append(entity));
        Require(session.QueryDanger(entity).State == DangerState.None, "the NPC is calm: " + session.QueryDanger(entity).State);
        Require(host.CombatThreats().All(t => !(t.Focus.IsHero && t.Active)), $"calm NPCs are no threat: {host.CombatThreats().Count} left");
        PressR();
        Require(!HeroInCombatMode, $"R leaves combat mode once no NPC fights the hero: {_combatModeNotice}");
        PressR();
        Require(HeroInCombatMode, "R enters combat mode again");
        host.HeroAttacked(entity);
        Require(session.QueryDanger(entity) is { State: DangerState.CombatFocus, Focus.IsHero: true }, "the NPC fights the hero again");
        GD.Print("ARC-COMBAT-1935 native-smoke combat-exit PASS (R, right release, dialog start and the world map opener refused under attack; R leaves when calm)");
    }

    /// <summary>The first cell at least 12 cells (Chebyshev) from <paramref name="start"/> on open steps, else the farthest such cell
    /// at least 8 away; null when none.</summary>
    private static CellCoord? RunGoal(AreaMapRuntime map, CellCoord start)
    {
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        var seen = new HashSet<CellCoord> { start }; var queue = new Queue<CellCoord>([start]);
        CellCoord? best = null; var bestDistance = 7;
        while (queue.TryDequeue(out var from) && seen.Count < 20000)
            foreach (var direction in Enum.GetValues<NeighborDirection>())
            {
                var next = RoutePlanner.Neighbor(from, direction);
                if (seen.Contains(next) || !routes.Contains(next) || routes.Assess(from, direction) != RouteStepVerdict.Open) continue;
                seen.Add(next); queue.Enqueue(next);
                var distance = Math.Max(Math.Abs(next.X - start.X), Math.Abs(next.Y - start.Y));
                if (distance >= 12) return next;
                if (distance > bestDistance) { best = next; bestDistance = distance; }
            }
        return best;
    }
}
