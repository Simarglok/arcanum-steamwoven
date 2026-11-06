using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-WORLD-020) on the installed exterior: a Ctrl ground click runs the route (every step at
    // the human run time, the run ART drawn while moving), a plain click walks back at the walk time, and Num Lock
    // makes the plain click run. Runs before the follow stage, while Virgil stands free on his T0 cell.
    private async Task RunAreaGaitSmokeAsync()
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var numLock = _numLockRun; var option = _alwaysRunOption;
        _worldFocusSimulationForSelfTest = true;
        var events = new List<MovementEvent>();
        try
        {
            static void Require(bool value, string why)
            {
                if (value) return;
                GD.Print("ARC-WORLD-020 gait smoke FAIL: " + why);
                throw new InvalidOperationException("AREA_GAIT_SMOKE_FAILED");
            }
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            _numLockRun = false; _alwaysRunOption = false;
            Require(_areaTransitions?.CurrentMap == 1 && !InCave, "the exterior is published");
            var map = _areaTransitions!.Current;
            var start = AreaSmokeStandable(map, map.Chart.Spawn, 2);
            var far = AreaSmokeStandable(map, start with { X = start.X + 10 }, 3);
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
            StepAssessmentTiming[] Permits() => events.Where(e => e.Kind == MovementEventKind.StepStarted).Select(e => e.Assessment!).ToArray();
            bool Gait(StepAssessmentTiming permit, MovementGait gait) => permit.Provenance.Contains(GaitTiming.Provenance(gait))
                && permit.Duration == _worldActor.AreaGaitTiming!.For(gait, permit.Key.Direction);
            double Seconds(IEnumerable<StepAssessmentTiming> permits) => permits.Sum(p => DiagnosticGridActor.Seconds(p.Duration!.Value.Value));
            RouteFollower Route() => _worldRoute ?? throw new InvalidOperationException("ARC-WORLD-020 gait smoke: no route");
            MovementState State() => _worldActor.Session!.State;
            // Frames at 1/10 s: the presentation is sampled while the route runs.
            (int Frames, bool RunDrawn, int RunSelector) Drive(CellCoord goal)
            {
                var frames = 0; var runDrawn = false; var selector = 0;
                for (; frames < 400 && !(Route().Status == RouteFollowStatus.Arrived && State().Phase == MovementPhase.Settled); frames++)
                {
                    _areaSmokeTime++;
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                    if (_worldActor.State.Action == ActorDiagnosticAction.Run && _readyActorPlan?.Current is { } drawn)
                    { runDrawn = true; selector = (int)drawn.TextureKey.Selector; }
                }
                Require(Route().Status == RouteFollowStatus.Arrived && State().CommittedCell == goal, $"arrived at {goal}: {DescribeWorldRoute(Route())} at {State().CommittedCell}");
                return (frames, runDrawn, selector);
            }
            // Smoke frames count whole seconds elsewhere; this stage uses tenths from the same counter.
            _areaSmokeTime *= 10; _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, CtrlPressed = true, Position = Screen(far), GlobalPosition = Screen(far) });
            Require(Route().Target == far && _worldGait == MovementGait.Run, $"the Ctrl click set a running route: {DescribeWorldRoute(Route())} gait {_worldGait}");
            // ARC-DEV-146 (GT14): the always-run rule makes the click goal 4 on the table, owner pc, the destination in world cells.
            // An automated run has no live flow (the frame step moves only when it is ticked with a literal host time, 50 ms a tick), so the run's ending step is driven here.
            var runHostMs = 31_000_000L;
            void Settle() { for (var i = 0; i < 10; i++) { _areaSmokeTime++; TickAreaClock(runHostMs += 50); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10)); } }
            Require(ShownHeroRunsHost?.WalkRun is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.RunToTile } runRun && runRun.Bottom.Tile == WorldTile((AreaGridSession)_worldActor.Session!, far),
                "the Ctrl click is a run of goal 4 on the table: " + ShownHeroRunsHost?.WalkRun?.Bottom);
            var ran = Drive(far);
            Settle();
            Require(ShownHeroRunsHost?.WalkRun is null, "the arrival ended the walk's run on the table: " + ShownHeroRunsHost?.WalkRun?.Bottom);
            var runPermits = Permits(); events.Clear();
            Require(runPermits.Length > 0 && runPermits.All(p => Gait(p, MovementGait.Run)), "every route step ran at the run time");
            // ARC-UI-039: the hero's own body's run ART.
            Require(ran.RunDrawn && (uint)ran.RunSelector == _worldActor.Profile.Run!.Selector, $"the run ART was drawn while running: selector {ran.RunSelector:x}");
            Require(_worldActor.State.Action == ActorDiagnosticAction.Idle, "settled idle");

            _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(start), GlobalPosition = Screen(start) });
            Require(Route().Target == start && _worldGait == MovementGait.Walk, "a plain click walks back");
            Require(ShownHeroRunsHost?.WalkRun is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.MoveToTile } backRun && backRun.Bottom.Tile == WorldTile((AreaGridSession)_worldActor.Session!, start),
                "the plain click is a run of goal 3 on the table: " + ShownHeroRunsHost?.WalkRun?.Bottom);
            var walked = Drive(start);
            var walkPermits = Permits(); events.Clear();
            Require(walkPermits.Length > 0 && walkPermits.All(p => Gait(p, MovementGait.Walk)) && !walked.RunDrawn, "every step walked, no run frame drawn");

            InputWorld(new InputEventKey { Keycode = Key.Numlock, PhysicalKeycode = Key.Numlock, Pressed = true });
            InputWorld(new InputEventKey { Keycode = Key.Numlock, PhysicalKeycode = Key.Numlock, Pressed = false });
            Require(_numLockRun, "Num Lock turned the always-run lock on");
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(far), GlobalPosition = Screen(far) });
            Require(_worldGait == MovementGait.Run, "with Num Lock on a plain click runs");
            Drive(far);
            var lockPermits = Permits(); events.Clear();
            Require(lockPermits.All(p => Gait(p, MovementGait.Run)), "the Num Lock route ran");
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_RUN_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("ARC-WORLD-020 gait smoke: capture failed");
            }
            GD.Print("ARC-WORLD-020 native-smoke run PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Start = map.Chart.ToWorld(start), Goal = map.Chart.ToWorld(far),
                Run = new { Steps = runPermits.Length, Seconds = Math.Round(Seconds(runPermits), 3), Frames = ran.Frames },
                Walk = new { Steps = walkPermits.Length, Seconds = Math.Round(Seconds(walkPermits), 3), Frames = walked.Frames },
                NumLockSteps = lockPermits.Length, Parity = "NOT_VERIFIED (ART-average step time, research ARC-RES-047)" }));
        }
        finally
        {
            _worldAdvanceObserverForSelfTest = null;
            _areaSmokeTime = (_areaSmokeTime + 9) / 10; // back to whole seconds for the next stages
            _numLockRun = numLock; _alwaysRunOption = option; _worldGait = MovementGait.Walk;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
