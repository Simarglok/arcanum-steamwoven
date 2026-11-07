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
    // ARC-COMBAT-021 native observation (with --hero, on the installed exterior and in map mode, after the audio stage, as it moves the
    // game clock on by the recovery's minutes, which the audio's night checks would see): a fatigue
    // packet leaves the hero unconscious; it falls (the body's fall-down ART, last frame drawn and uploaded), a press on the ground and the
    // stick do nothing; the session clock flows until the recovery brings it round, and it gets up (the get-up ART) and stands, taking a
    // press again. The hero's fatigue is healed back afterwards. ARC_AREA_KNOCKDOWN_CAPTURE saves in-app readbacks (down, standing) to
    // the caller's ignored path.
    private async Task RunAreaHeroKnockdownSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession)
        { GD.Print($"ARC-COMBAT-021 native-smoke knockdown NONE map={_readyMapName} (needs a hero)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-021 knockdown smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_KNOCKDOWN_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var before = session.QueryHeroLife()!.Record.State.FatigueDamage;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = mapMode is { Area: { } area, AreaStepSnapshot: { } mapSteps }
                ? new AreaMapRuntime(area.Snapshot, mapSteps, area.Plan.Chart) : _areaTransitions!.Current;
            var start = AreaSmokeStandable(map, map.Chart.Spawn, 2);
            var far = RunGoal(map, start);
            Require(far is not null, "a cell the PC can walk to at least 8 cells from its start");
            AreaSmokePlace(map, start);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            SyncHeroBody();
            var profile = _worldActor.Profile;
            Require(profile.FallDown is not null && profile.GetUp is not null, "the hero's body has its fall-down and get-up ARTs: " + _heroBodyStatus);
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_KNOCKDOWN_CAPTURE");
            async Task Capture(string name)
            {
                if (string.IsNullOrWhiteSpace(capture)) return;
                _camera!.ResetWorld(WorldAnchor, WorldViewport, 2);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "capture saved");
            }
            Vector2 Screen(CellCoord cell)
            {
                var center = AreaCellPicking.Center(cell);
                return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)center.X, (float)center.Y);
            }
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            void Frame(Vec2 stick = default)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(stick) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var grid = (AreaGridSession)_worldActor.Session!;

            // The fatigue packet: current fatigue to −3, as in the owner's report.
            var fatigue = session.QueryHeroLife()!.Status.Fatigue;
            Require(fatigue > 0 && !HeroLyingDown, $"the hero starts conscious and standing (fatigue {fatigue})");
            var knocked = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = fatigue + 3 });
            Require(knocked.Disposition != GameSessionDisposition.Rejected && session.QueryHeroLife()!.Status is { IsUnconscious: true, IsDead: false },
                $"the packet left the hero unconscious: {knocked.Disposition} {knocked.Failure} fatigue {session.QueryHeroLife()!.Status.Fatigue}");
            var lowest = session.QueryHeroLife()!.Status.Fatigue;
            var fell = 0;
            for (var i = 0; i < 100 && _heroKnockdown.Stage != AreaKnockdownStage.Down; i++)
            {
                Frame();
                if (_readyActor?.Action == ActorDiagnosticAction.FallDown) fell++;
            }
            Require(_heroKnockdown.Stage == AreaKnockdownStage.Down && fell > 1, $"the hero fell over {fell} drawn frames: {_heroKnockdown.Stage}");
            Frame();
            Require(_worldActor.DownPose is { Action: ActorDiagnosticAction.FallDown } pose && pose.Frame == profile.FallDown!.FramesPerGroup - 1
                && _readyActor is { Action: ActorDiagnosticAction.FallDown } drawn && drawn.Frame == pose.Frame && _cache.TryGet(drawn.TextureKey, out _),
                $"the fall's last frame is drawn and uploaded: {_worldActor.DownPose} {_readyActor?.Action} {_readyActor?.Frame}");
            RefreshAreaStatus();
            Require(GetSelfTestSnapshot().Hud.Contains("hero: unconscious", StringComparison.Ordinal), "the status line names the unconscious hero");
            await Capture("down");

            // Lying, the hero takes no press and does not walk on the stick.
            var cell = grid.State.CommittedCell;
            WorldRouteClick(Screen(far!.Value));
            Require(_worldRouteClick == "ignored (the hero is down)" && !HeroWalking,
                $"a ground press while down does nothing: {_worldRouteClick}");
            for (var i = 0; i < 20; i++) Frame(new Vec2(1, 0));
            for (var i = 0; i < 5; i++) Frame();
            Require(grid.State.CommittedCell == cell && grid.State.Phase == MovementPhase.Settled && _heroKnockdown.Stage == AreaKnockdownStage.Down,
                $"the stick did not move the lying hero: {cell} -> {grid.State.CommittedCell}");

            // The session clock flows: the fatigue recovery brings the hero round, and it gets up.
            Require(session.QueryEvents().Any(e => e.Kind == SessionEventKinds.HeroFatigue) && _areaAmbient is not null,
                $"the fatigue recovery is on the queue and the clock driver is there ({_areaAmbient is not null})");
            // Game-time jumps of 10 s (as a world-map travel step): the events that come due fire, the recovery among them.
            var jumps = 0;
            for (; jumps < 1000 && session.QueryHeroLife()!.Status.IsUnconscious; jumps++) { _areaAmbient!.AdvanceMilliseconds(10_000, AmbientSector()); LogAreaEvents(_areaAmbient); }
            var after = session.QueryHeroLife()!.Status;
            Require(!after.IsUnconscious && after.Fatigue > 0, $"the recovery brought the hero round after {jumps * 10} game s: fatigue {after.Fatigue}");
            var rose = 0;
            for (var i = 0; i < 200 && _heroKnockdown.Lying; i++)
            {
                Frame();
                if (_readyActor?.Action == ActorDiagnosticAction.GetUp) rose++;
            }
            Frame();
            Require(_heroKnockdown.Stage == AreaKnockdownStage.Standing && rose > 1 && _worldActor.DownPose is null && _readyActor?.Action == ActorDiagnosticAction.Idle,
                $"the hero got up over {rose} drawn frames and stands: {_heroKnockdown.Stage} {_readyActor?.Action}");
            await Capture("standing");
            WorldRouteClick(Screen(far.Value));
            Require(HeroWalkGoal == far && _worldRouteClick != "ignored (the hero is down)", $"the standing hero takes a press again: {_worldRouteClick}");
            // ARC-DEV-187 (MIG13 S13a): a stun result on the walking hero starts its stun goal (57) on the table, which interrupts the walk at once (B11's mechanism);
            // the goal's first step ends it and clears the stunned bit, and the hero takes a press again.
            Require(HeroRunsHost()?.WalkRun is not null, "the hero is walking before the stun");
            var stunned = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Effects = CriticalHitEffects.Stun });
            Require(stunned.Disposition == GameSessionDisposition.Changed && stunned.Outcome!.Events.HasFlag(LifeEvents.Stunned), $"the stun landed: {stunned.Disposition} {stunned.Failure}");
            HeroStunned(session);
            Frame(); // the interrupted walk's end node runs with the AI host's frame
            Require(HeroRunsHost()?.StunRun is not null && HeroRunsHost()?.WalkRun is null && !HeroWalking, "the stun goal interrupted the walk");
            Require(!session.QueryHeroLife()!.Status.IsActive, "the stunned hero cannot act while the goal runs");
            // The goal's own step needs the area clock (a unit test covers it); the smoke cuts the goal, whose end node clears the bit as a reset or a quick load does.
            HeroRunsHost()!.LoseAll("SMOKE_STUN_CUT");
            Require(HeroRunsHost()?.StunRun is null && session.QueryHeroLife()!.Status.IsActive, "the cut stun goal's end node cleared the bit");
            // ARC-DEV-189 (MIG13 S13c): a knock-down result leaves the hero conscious and it still falls: the session starts goal 62, which interrupts the walk, forces the fall and ends when it lies;
            // the viewer's own rule then gets the conscious hero up.
            WorldRouteClick(Screen(far.Value));
            Require(HeroRunsHost()?.WalkRun is not null, "the hero walks before the knock-down");
            var crit = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { Effects = CriticalHitEffects.Knockdown, HitPoints = 1 });
            Require(crit.Outcome is { } critOutcome && critOutcome.Events.HasFlag(LifeEvents.KnockedDown), $"the knock-down landed: {crit.Disposition} {crit.Failure}");
            Require(HeroRunsHost()?.DownRun?.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.KnockDown && HeroRunsHost()?.WalkRun is null, "goal 62 interrupted the walk");
            var crumpled = false; var stood = false;
            for (var i = 0; i < 600 && !(stood && HeroRunsHost()?.DownRun is null); i++)
            {
                Frame(); _areaAmbient!.AdvanceMilliseconds(50, AmbientSector());
                crumpled |= _heroKnockdown.Stage is AreaKnockdownStage.Falling or AreaKnockdownStage.Down;
                // The run spans the fall: while the stage is Falling goal 62 is still on the table.
                if (_heroKnockdown.Stage == AreaKnockdownStage.Falling) Require(HeroRunsHost()?.DownRun?.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.KnockDown, "goal 62 lives while the fall plays");
                stood |= crumpled && _heroKnockdown.Stage == AreaKnockdownStage.Standing;
            }
            Require(crumpled && stood && HeroRunsHost()?.DownRun is null && !_heroKnockdown.Lying, $"the conscious hero fell and got up: fell {crumpled} rose {stood} stage {_heroKnockdown.Stage}");
            HeroRunsHost()?.Interrupt(Arcanum.Simulation.Goals.GoalInterruptRules.OrdinaryActions, "SMOKE_DONE"); StopWorldRoute("SMOKE_DONE");
            GD.Print("ARC-COMBAT-021 native-smoke knockdown PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName ?? "crash-site", Body = _heroBodyStatus, FellFrames = fell, RoseFrames = rose, Fatigue = $"{fatigue}->{lowest}->{after.Fatigue}", RecoveredAfterGameSeconds = jumps * 10,
                Parity = "NOT_VERIFIED (rules read in the original executable, UC1–UC5; no original-runtime run)" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            var damage = session.QueryHeroLife()!.Record.State.FatigueDamage;
            if (damage > before) session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = damage - before });
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
