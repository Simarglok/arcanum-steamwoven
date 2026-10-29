using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
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
            var priorRules = session.Rules.Mode;
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
            Require(Route().Target == far && _worldGait == MovementGait.Run, $"the Ctrl click set a running route: {DescribeWorldRoute(Route())}");
            for (var frames = 0; frames < 600 && !(Route().Status == RouteFollowStatus.Arrived && _worldActor.Session!.State.Phase == MovementPhase.Settled); frames++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
            }
            Require(Route().Status == RouteFollowStatus.Arrived, "arrived: " + DescribeWorldRoute(Route()));
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
                var beforeStick = session.QueryHeroLife()!.Record.State.FatigueDamage;
                for (var frames = 0; frames < 40; frames++)
                {
                    _areaSmokeTime++;
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(stick) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                }
                // At least one neutral frame: the next direction is a new press, which starts its own run-step count.
                for (var frames = 0; frames < 20 && (frames == 0 || _worldActor.Session!.State.Phase != MovementPhase.Settled); frames++)
                {
                    _areaSmokeTime++;
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                }
                stickSteps = events.Count(e => e.Kind == MovementEventKind.StepStarted && e.Assessment!.Provenance.Contains(GaitTiming.RunProvenance));
                stickCharged = session.QueryHeroLife()!.Record.State.FatigueDamage - beforeStick;
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
            var healed = session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = charged });
            Require(healed.Disposition != GameSessionDisposition.Rejected && session.QueryHeroLife()!.Record.State.FatigueDamage == before, "fatigue healed back");
            GD.Print("ARC-COMBAT-004 native-smoke combat-mode PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Start = map.Chart.ToWorld(start), Goal = map.Chart.ToWorld(far.Value), RunSteps = runSteps, StickRunSteps = stickSteps, Charged = charged,
                Parity = "NOT_VERIFIED (rules checked in the original executable; no original-runtime run)" }));
        }
        finally
        {
            _worldAdvanceObserverForSelfTest = null;
            _areaSmokeTime = (_areaSmokeTime + 9) / 10;
            _numLockRun = numLock; _alwaysRunOption = option; _worldGait = MovementGait.Walk;
            _attackAimForSelfTest = null;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
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
