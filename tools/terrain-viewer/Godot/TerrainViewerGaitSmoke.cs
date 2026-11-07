using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>ARC-DEV-150: the longest idle time the gait smoke accepts between two steps of one walk, from a step's arrival at its centre to the next
    /// step's start: none (the table gives the next step before the step ends, so they chain), over a route longer than one 8-cell segment.</summary>
    private const double IdleGapLimitSeconds = 0.0;

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
            var far = AreaSmokeStandable(map, start with { X = start.X + 24 }, 3);
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
            // ARC-DEV-150: a walk that goes on stands idle for no time between two steps: the longest time from a step's arrival at its centre to the start of the next one.
            var gapsMeasured = 0;
            double IdleGap()
            {
                var longest = 0.0; MovementEvent? arrived = null;
                foreach (var e in events)
                {
                    if (e.Kind == MovementEventKind.CenterArrived) arrived = e;
                    else if (e.Kind == MovementEventKind.StepStarted && arrived is { } at)
                    { longest = Math.Max(longest, DiagnosticGridActor.Seconds(e.Time.Value - at.Time.Value)); arrived = null; gapsMeasured++; }
                }
                return longest;
            }
            double Seconds(IEnumerable<StepAssessmentTiming> permits) => permits.Sum(p => DiagnosticGridActor.Seconds(p.Duration!.Value.Value));
            RouteFollower Route() => _worldRoute ?? throw new InvalidOperationException("ARC-WORLD-020 gait smoke: no route");
            MovementState State() => _worldActor.Session!.State;
            // Frames at 1/10 s: the presentation is sampled while the route runs.
            // ARC-DEV-149: the table walks the hero, so the area clock is ticked 0.1 s a frame (a frame of this stage) while its run is on the table.
            var runHostMs = 31_000_000L;
            (int Frames, bool RunDrawn, int RunSelector) Drive(CellCoord goal)
            {
                var frames = 0; var runDrawn = false; var selector = 0;
                for (; frames < 400 && !(!HeroWalking && State().Phase == MovementPhase.Settled); frames++)
                {
                    _areaSmokeTime++;
                    if (ShownHeroRunsHost?.WalkRun is not null) TickAreaClock(runHostMs += 100);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                    // ARC-DEV-150: the session holds the hero's location at every frame, not only at the arrival (no commit point is asked).
                    var walking = (AreaGridSession)_worldActor.Session!; var world = walking.Chart.ToWorld(walking.State.CommittedCell); var held = _areaSession!.QueryActor();
                    Require(held.Cell.X == world.X && held.Cell.Y == world.Y && held.Facing == walking.State.Facing, $"the session holds the committed cell at frame {frames}: {held.Cell} against {world}");
                    if (_worldActor.State.Action == ActorDiagnosticAction.Run && _readyActorPlan?.Current is { } drawn)
                    { runDrawn = true; selector = (int)drawn.TextureKey.Selector; }
                }
                Require(State().CommittedCell == goal, $"arrived at {goal}: {DescribeWorldRoute(Route())} at {State().CommittedCell}");
                return (frames, runDrawn, selector);
            }
            // Smoke frames count whole seconds elsewhere; this stage uses tenths from the same counter.
            _areaSmokeTime *= 10; _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, CtrlPressed = true, Position = Screen(far), GlobalPosition = Screen(far) });
            Require(HeroWalkGoal == far && _worldGait == MovementGait.Run, $"the Ctrl click set a running walk: {DescribeWorldRoute(Route())} gait {_worldGait}");
            // ARC-DEV-146 (GT14): the always-run rule makes the click goal 4 on the table, owner pc, the destination in world cells.
            // An automated run has no live flow (the frame step moves only when it is ticked with a literal host time, 50 ms a tick), so the run's ending step is driven here.
            void Settle() { for (var i = 0; i < 10; i++) { _areaSmokeTime++; TickAreaClock(runHostMs += 50); WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10)); } }
            Require(ShownHeroRunsHost?.WalkRun is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.RunToTile } runRun && runRun.Bottom.Tile == WorldTile((AreaGridSession)_worldActor.Session!, far),
                "the Ctrl click is a run of goal 4 on the table: " + ShownHeroRunsHost?.WalkRun?.Bottom);
            var ran = Drive(far);
            Settle();
            Require(ShownHeroRunsHost?.WalkRun is null, "the arrival ended the walk's run on the table: " + ShownHeroRunsHost?.WalkRun?.Bottom);
            var runPermits = Permits(); var runGap = IdleGap(); events.Clear();
            Require(runPermits.Length > 0 && runPermits.All(p => Gait(p, MovementGait.Run)), "every route step ran at the run time");
            // ARC-UI-039: the hero's own body's run ART.
            Require(ran.RunDrawn && (uint)ran.RunSelector == _worldActor.Profile.Run!.Selector, $"the run ART was drawn while running: selector {ran.RunSelector:x}");
            Require(_worldActor.State.Action == ActorDiagnosticAction.Idle, "settled idle");

            _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(start), GlobalPosition = Screen(start) });
            Require(HeroWalkGoal == start && _worldGait == MovementGait.Walk, "a plain click walks back");
            Require(ShownHeroRunsHost?.WalkRun is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.MoveToTile } backRun && backRun.Bottom.Tile == WorldTile((AreaGridSession)_worldActor.Session!, start),
                "the plain click is a run of goal 3 on the table: " + ShownHeroRunsHost?.WalkRun?.Bottom);
            var walked = Drive(start);
            var walkPermits = Permits(); var walkGap = IdleGap(); events.Clear();
            Require(walkPermits.Length > 0 && walkPermits.All(p => Gait(p, MovementGait.Walk)) && !walked.RunDrawn, "every step walked, no run frame drawn");

            InputWorld(new InputEventKey { Keycode = Key.Numlock, PhysicalKeycode = Key.Numlock, Pressed = true });
            InputWorld(new InputEventKey { Keycode = Key.Numlock, PhysicalKeycode = Key.Numlock, Pressed = false });
            Require(_numLockRun, "Num Lock turned the always-run lock on");
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(far), GlobalPosition = Screen(far) });
            Require(_worldGait == MovementGait.Run, "with Num Lock on a plain click runs");
            Drive(far);
            var lockPermits = Permits(); var lockGap = IdleGap(); events.Clear();
            Require(lockPermits.All(p => Gait(p, MovementGait.Run)), "the Num Lock route ran");
            Require(gapsMeasured >= 3 * 16, $"the idle gaps were measured over the routes: {gapsMeasured}");
            Require(Math.Max(runGap, Math.Max(walkGap, lockGap)) <= IdleGapLimitSeconds, $"no idle gap between steps: run {runGap:F3} walk {walkGap:F3} lock {lockGap:F3} s");
            // ARC-DEV-147 (MIG13 S10c): a held stick is a held walk of the table: a goal 3 run of the pc marked as a flight (a straight line), asked for again every 250 ms, stepped by the table on the hero's body in the
            // stick's direction; its release ends the run and the step under way finishes. The first of the eight directions that walks at least three cells is the one measured.
            _numLockRun = false; _alwaysRunOption = false;
            var stickWalked = 0; var stickAsks = 0; var stickAfterRelease = 0; var stickGap = 0.0;
            foreach (var stick in new Vec2[] { new(-1, 0), new(1, 0), new(0, 1), new(0, -1), new(1, 1), new(-1, -1), new(1, -1), new(-1, 1) })
            {
                events.Clear();
                Require(ShownHeroRunsHost?.WalkRun is null && State().Phase == MovementPhase.Settled, "the hero stands before the stick: " + ShownHeroRunsHost?.WalkRun?.Bottom);
                var stickFrom = State().CommittedCell; var asksBefore = _stickRequests; var flight = false; var tableWalk = false;
                _worldTestTime = SimulationTime.At(_areaSmokeTime, 10);
                for (var frames = 0; frames < 24; frames++)
                {
                    _areaSmokeTime++; TickAreaClock(runHostMs += 100);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(stick) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                    var walkRun = ShownHeroRunsHost?.WalkRun;
                    if (walkRun is not null) { tableWalk = true; flight |= walkRun.Flags.HasFlag(Arcanum.Simulation.Goals.GoalRunFlags.Flight) && walkRun.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.MoveToTile; }
                }
                var stickEvents = events.Where(e => e.Kind == MovementEventKind.StepStarted).ToArray();
                var cellAtRelease = State().CommittedCell;
                stickAsks = _stickRequests - asksBefore;
                for (var frames = 0; frames < 20 && (frames == 0 || State().Phase != MovementPhase.Settled || ShownHeroRunsHost?.WalkRun is not null); frames++)
                {
                    _areaSmokeTime++; TickAreaClock(runHostMs += 100);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true, SimulationTime.At(_areaSmokeTime, 10));
                }
                var stickTo = State().CommittedCell;
                stickWalked = Math.Max(Math.Abs(stickTo.X - stickFrom.X), Math.Abs(stickTo.Y - stickFrom.Y));
                if (stickWalked < 3) continue;
                Require(tableWalk && flight, $"the stick is a goal 3 run of the table marked as a flight (0x400): table {tableWalk} flight {flight}");
                Require(stickEvents.Length >= 3 && stickEvents.Select(e => e.Assessment!.Key.Direction).Distinct().Count() == 1 && stickEvents.All(e => Gait(e.Assessment!, MovementGait.Walk)),
                    $"every step of the stick walk is one direction at the walk time: {string.Join(",", stickEvents.Select(e => e.Assessment!.Key.Direction))}");
                // 24 frames of 0.1 s ask at the press and then once more than 250 ms later: 0.3, 0.6, ... 2.1 s (8 times); never once a frame.
                Require(stickAsks >= 6 && stickAsks <= 10, $"the stick asked {stickAsks} times in 2.4 s (every 250 ms, not every frame)");
                // The retarget at every repeat replans at the next step end and chains the next step with no gap, as the click walk's.
                stickGap = IdleGap();
                Require(stickGap <= IdleGapLimitSeconds, $"no idle gap between the stick walk's steps: {stickGap:F3} s");
                stickAfterRelease = Math.Max(Math.Abs(stickTo.X - cellAtRelease.X), Math.Abs(stickTo.Y - cellAtRelease.Y));
                Require(ShownHeroRunsHost?.WalkRun is null && !_stickHeld, "the release ended the walk's run");
                Require(stickAfterRelease <= 1, $"the step under way finished and no other began after the release: {stickAfterRelease} cells");
                break;
            }
            Require(stickWalked >= 3, "the stick walked the hero at least three cells in some direction: " + stickWalked);
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_RUN_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("ARC-WORLD-020 gait smoke: capture failed");
            }
            GD.Print("ARC-WORLD-020 native-smoke run PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Start = map.Chart.ToWorld(start), Goal = map.Chart.ToWorld(far),
                Run = new { Steps = runPermits.Length, Seconds = Math.Round(Seconds(runPermits), 3), Frames = ran.Frames, IdleGap = Math.Round(runGap, 3) },
                Walk = new { Steps = walkPermits.Length, Seconds = Math.Round(Seconds(walkPermits), 3), Frames = walked.Frames, IdleGap = Math.Round(walkGap, 3) },
                NumLockSteps = lockPermits.Length, NumLockIdleGap = Math.Round(lockGap, 3), Stick = new { Cells = stickWalked, Asks = stickAsks, AfterRelease = stickAfterRelease, IdleGap = Math.Round(stickGap, 3) }, Parity = "NOT_VERIFIED (ART-average step time, research ARC-RES-047)" }));
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
