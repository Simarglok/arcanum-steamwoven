using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
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
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains("combat mode: ON (real time)", StringComparison.Ordinal), "HUD names combat mode");

            var map = mapMode is { Area: { } area, AreaStepSnapshot: { } mapSteps }
                ? new AreaMapRuntime(area.Snapshot, mapSteps, area.Plan.Chart) : _areaTransitions!.Current;
            var start = AreaSmokeStandable(map, map.Chart.Spawn, 2);
            var far = AreaSmokeStandable(map, start with { X = start.X + 12 }, 3);
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
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, CtrlPressed = true, Position = Screen(far), GlobalPosition = Screen(far) });
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
            _numLockRun = true; events.Clear();
            var beforeStick = session.QueryHeroLife()!.Record.State.FatigueDamage;
            for (var frames = 0; frames < 40; frames++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(new(1, -1)) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
            }
            for (var frames = 0; frames < 20 && _worldActor.Session!.State.Phase != MovementPhase.Settled; frames++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
            }
            _numLockRun = false;
            var stickSteps = events.Count(e => e.Kind == MovementEventKind.StepStarted && e.Assessment!.Provenance.Contains(GaitTiming.RunProvenance));
            var stickCharged = session.QueryHeroLife()!.Record.State.FatigueDamage - beforeStick;
            Require(stickSteps >= RealTimeCombatRules.RunChargeSteps && stickCharged == stickSteps / RealTimeCombatRules.RunChargeSteps,
                $"{stickSteps} stick run steps charged {stickCharged} fatigue");
            charged += stickCharged;

            Require(LeaveCombatModeForDialog() && !HeroInCombatMode, "a dialog start left combat mode");
            PressR();
            Require(HeroInCombatMode, "R entered combat mode again");
            LeaveCombatModeForMapChange();
            Require(!HeroInCombatMode, "a map change left combat mode");
            PressR(); PressR();
            Require(!HeroInCombatMode, "R toggles off");
            var healed = session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = charged });
            Require(healed.Disposition != GameSessionDisposition.Rejected && session.QueryHeroLife()!.Record.State.FatigueDamage == before, "fatigue healed back");
            GD.Print("ARC-COMBAT-004 native-smoke combat-mode PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Start = map.Chart.ToWorld(start), Goal = map.Chart.ToWorld(far), RunSteps = runSteps, StickRunSteps = stickSteps, Charged = charged,
                Parity = "NOT_VERIFIED (rules checked in the original executable; no original-runtime run)" }));
        }
        finally
        {
            _worldAdvanceObserverForSelfTest = null;
            _areaSmokeTime = (_areaSmokeTime + 9) / 10;
            _numLockRun = numLock; _alwaysRunOption = option; _worldGait = MovementGait.Walk;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
