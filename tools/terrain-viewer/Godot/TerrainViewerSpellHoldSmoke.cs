using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-304 (SM09, B11) native observation (stage "hold-hero", only when named, with --hero, on the Crash Site window): the spells that write the hold flags (ARC-DEV-302) hold the hero
    // as the executable's rules say (the S13b read, GT145–GT153). A spell cast by a script with no caster (so no casting goal replaces the walk) writes each flag through the cast host's ObjFlag
    // component: the stone (spell 127: spell flag 0x10000), the paralysis (spell 113: critter flag 0x40) and the entangle (spell 140: spell flag 0x1000000). With the hero's walk under way:
    //  - stone interrupts nothing: the walk run stays, the hero stops after the step it is on, a press is dropped (it cannot act, GT150), and when the run's END clears the flag the walk goes on to its old target;
    //  - paralysis interrupts the walk (goal 3 at the bottom, GT146) and holds the hero the same way; a press is dropped; after the END a press walks again;
    //  - entangle interrupts the click walk (goal 3) as paralysis does, in both rule modes (each is run), and a new walk fails at its plan (GT149); the D8 difference (Original keeps the steps of a goal 4, 55 or 56 run) needs such a run, which this stage does not produce.
    // The hero and the runs are restored for the later stages.
    private async Task RunAreaHoldHeroSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession)
        { GD.Print("ARC-DEV-304 native-smoke hold-hero NONE map=" + _readyMapName + " (needs a hero)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-304 hold-hero smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_HOLD_HERO_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var scaled = false;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var holdRuns = new List<int>();
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
            for (var wait = 0; wait < 600 && SpellCasterNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(SpellCasterNow() is not null, "the map's cast host is built (spell data " + _spellData?.Status + ")");
            // A placement rebuilds the cast host (the runs of a replaced host are given up), so the host is asked for each time.
            SessionSpellCaster Caster() => SpellCasterNow() ?? throw new InvalidOperationException("AREA_HOLD_HERO_NO_CAST_HOST");
            Vector2 Screen(CellCoord cell)
            {
                var center = AreaCellPicking.Center(cell);
                return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)center.X, (float)center.Y);
            }
            // A placement restarts the smoke clock in tenths of a second; the frames here are twentieths, so the clock is scaled again after each.
            void Scale() { _areaSmokeTime *= 20; scaled = true; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20); }
            Scale();
            void Frame()
            {
                _areaSmokeTime++;
                TickAreaClock(_talkSmokeHostMs += 50); // the clock a placement's own frames tick, so it never runs backwards
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            // A placement builds a new grid session, so it is asked for each time.
            AreaGridSession Grid() => (AreaGridSession)_worldActor.Session!;
            var report = new List<object>();

            // A hold run: a script's cast with no caster at the hero, begun at once (no casting goal), its log taken as the viewer takes a cast's (the hold's interrupt among it).
            int Hold(int spell)
            {
                var caster = Caster();
                var attempt = caster.CastByScript(null, spell, SessionSpellCaster.Hero, free: true, unresistable: true);
                Require(attempt.Outcome == SpellCastAttemptOutcome.Started && attempt.RunId is not null, $"the script cast of spell {spell} started: {attempt.Outcome} {attempt.Run?.Creation?.Status} {attempt.Run?.Creation?.Check}");
                var run = attempt.RunId!.Value;
                // A cast with no caster has no casting goal: the run's BEGIN is an event of the session's queue (due now), which the next frames fire.
                holdRuns.Add(run);
                for (var i = 0; i < 60 && !HoldWritten(spell); i++) { Frame(); FlushSpellLog(null); }
                GD.Print($"ARC-DEV-304 hold spell={spell} run={run} holds={HeroHolds()} run-state={session.QuerySpellRuns()[run]?.Action.ToString() ?? "gone"}");
                Require(HoldWritten(spell), $"the BEGIN of spell {spell} wrote its flag within 60 frames");
                return run;
            }
            bool HoldWritten(int spell) => spell switch { 127 => HeroHolds().Stoned, 113 => HeroHolds().Paralysed, 140 => HeroHolds().Entangled, _ => false };
            void EndHold(int run)
            {
                Caster().Act(new(SessionSpellRunActionKind.ForceEnd, run));
                FlushSpellLog(null);
            }
            // The hero walks toward the far cell; some frames make it commit its first steps.
            WorldAnchor Walk()
            {
                WorldRouteClick(Screen(far!.Value));
                Require(HeroRunsHost()?.WalkRun is not null && HeroWalkGoal == far, $"a press walks the hero toward {far}: {_worldRouteClick}");
                var begun = Grid().State.Anchor;
                var moved = 0;
                for (var i = 0; i < 400 && moved < 12; i++) { Frame(); if (Grid().State.Anchor != begun) { moved++; begun = Grid().State.Anchor; } }
                Require(moved >= 12 && HeroRunsHost()?.WalkRun is not null, $"the hero is on its way (anchor {Grid().State.Anchor}, walk run {HeroRunsHost()?.WalkRun is not null}, held {HeroHolds()}, moved {moved})");
                return Grid().State.Anchor;
            }
            WorldAnchor Settle(string what)
            {
                var last = Grid().State.Anchor; var still = 0; var trail = new List<string>();
                for (var i = 0; i < 120 && still < 20; i++)
                {
                    Frame();
                    if (Grid().State.Anchor == last) still++; else { still = 0; last = Grid().State.Anchor; }
                    if (i % 10 == 0) trail.Add($"{(double)last.U.Numerator / (double)last.U.Denominator:F2},{(double)last.V.Numerator / (double)last.V.Denominator:F2}/{Grid().State.Phase}/walk={HeroRunsHost()?.WalkRun is not null}/held={HeroStepsHeld()}/same={ReferenceEquals(_areaSession, session)}/runs={session.QuerySpellRuns().Runs.Count()}/flags={session.QueryObjectFlags(SessionSpellCaster.Hero)}");
                }
                Require(still >= 20, $"{what}: the hero stands still:\n{string.Join("\n", trail.Take(4))}\n log {string.Join(",", Caster().Log.TakeLast(14).Select(e => $"{e.Kind}:{e.Run}:{e.Value}:{e.Detail}"))}");
                return last;
            }
            void Reset()
            {
                HeroRunsHost()?.Interrupt(Arcanum.Simulation.Goals.GoalInterruptRules.OrdinaryActions, "SMOKE_RESET"); StopWorldRoute("SMOKE_RESET");
                for (var i = 0; i < 20; i++) Frame();
                scaled = false; AreaSmokePlace(map, start); Scale();
                for (var i = 0; i < 5; i++) Frame();
                Require(HeroRunsHost()?.WalkRun is null, "no walk run before the hold");
            }

            // ---- stone: nothing is interrupted, the hero is held and cannot act, the walk goes on after the END ----
            Reset();
            Walk();
            var stone = Hold(127);
            Require(HeroHolds() == (false, true, false), $"the stone is on the hero: {HeroHolds()}");
            Require(HeroRunsHost()?.WalkRun is not null, "the stone interrupted nothing: the walk run stays (GT146)");
            Require(!session.QueryHeroLife()!.Status.IsActive, "a stoned hero cannot act (0x45D790)");
            var stoneCell = Settle("stone");
            WorldRouteClick(Screen(start));
            Require(HeroWalkGoal == far && Grid().State.Anchor == stoneCell, $"a press on a stoned hero is dropped: {_worldRouteClick}");
            for (var i = 0; i < 40; i++) Frame();
            Require(Grid().State.Anchor == stoneCell, "still held after 40 more frames");
            EndHold(stone);
            Require(HeroHolds() == (false, false, false) && session.QueryHeroLife()!.Status.IsActive, $"the END took the stone off: {HeroHolds()}");
            var resumed = false;
            for (var i = 0; i < 600 && !resumed; i++) { Frame(); resumed = Grid().State.Anchor != stoneCell; }
            Require(resumed && HeroWalkGoal == far, $"the walk resumes toward its old target {far} after the hold: cell {Grid().State.Anchor} goal {HeroWalkGoal}");
            report.Add(new { Hold = "stone", Spell = 127, Interrupted = false, Resumed = true });

            // ---- paralysis: the walk is interrupted (goal 3), the hero cannot act, a press after the END walks again ----
            Reset();
            Walk();
            var paralysis = Hold(113);
            Require(HeroHolds() == (false, false, true), $"the paralysis is on the hero: {HeroHolds()}");
            Require(HeroRunsHost()?.WalkRun is null, "the paralysis interrupted the walk run (GT146)");
            Require(!session.QueryHeroLife()!.Status.IsActive, "a paralysed hero cannot act");
            var paralysisCell = Settle("paralysis");
            WorldRouteClick(Screen(far!.Value));
            Require(HeroRunsHost()?.WalkRun is null && Grid().State.Anchor == paralysisCell, $"a press on a paralysed hero is dropped: {_worldRouteClick}");
            EndHold(paralysis);
            Require(HeroHolds() == (false, false, false) && session.QueryHeroLife()!.Status.IsActive, $"the END took the paralysis off: {HeroHolds()}");
            WorldRouteClick(Screen(far.Value));
            Require(HeroRunsHost()?.WalkRun is not null, $"a press after the END walks again: {_worldRouteClick}");
            var walkedOn = false;
            for (var i = 0; i < 200 && !walkedOn; i++) { Frame(); walkedOn = Grid().State.Anchor != paralysisCell; }
            Require(walkedOn, "the hero moved after the paralysis ended");
            report.Add(new { Hold = "paralysis", Spell = 113, Interrupted = true, WalksAgain = true });

            // ---- entangle: the walk is interrupted too; Steamwoven holds the steps, Original lets a running hero run on (D8): both settings are run ----
            var rulesBefore = session.Rules.Mode;
            foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
            {
            session.SelectRules(RuleMode.Steamwoven);
            Reset();
            Walk();
            var entangle = Hold(140);
            // The rule mode is chosen once the run has begun: the cast path reads no switch, the hero's steps do (D8).
            Require(session.SelectRules(mode) != GameSessionDisposition.Rejected, $"{mode} selected");
            Require(HeroHolds() == (true, false, false), $"the entangle is on the hero: {HeroHolds()}");
            Require(session.QueryHeroLife()!.Status.IsActive, "an entangled hero can still act (GT150): entangled is not in can-act");
            var walkRunKept = HeroRunsHost()?.WalkRun is not null;
            var entangledCell = Grid().State.Anchor;
            for (var i = 0; i < 80; i++) Frame();
            var cellAfter = Grid().State.Anchor;
            var stood = false;
            for (var i = 0; i < 120 && !stood; i++)
            {
                var before = Grid().State.Anchor;
                for (var k = 0; k < 20; k++) Frame();
                stood = Grid().State.Anchor == before;
            }
            // The click walk is a goal-3 run, which the entangle ends at once in both settings (GT146, GT153), so the hero stands within a step in Original as in Steamwoven. The settings differ only for a hero
            // running a goal 4, 55 or 56 (D8, GT153 a): Original keeps its steps, Steamwoven holds them; the stage has no such run, so that difference is not exercised here.
            Require(!walkRunKept, $"{mode}: the entangle ended the goal-3 walk run at once (GT153)");
            Require(stood, $"{mode}: the hero stands within a step of the hold's start");
            // A new walk of an entangled hero fails at its plan (GT149) in both settings: the press leaves the hero where it is.
            var restingCell = Grid().State.Anchor;
            if (stood)
            {
                WorldRouteClick(Screen(start));
                for (var i = 0; i < 40; i++) Frame();
                Require(Grid().State.Anchor == restingCell && HeroRunsHost()?.WalkRun is null, $"a new walk of the entangled hero fails at its plan: {_worldRouteClick}");
            }
            EndHold(entangle);
            Require(HeroHolds() == (false, false, false), "the END took the entangle off");
            report.Add(new { Hold = "entangle", Spell = 140, Mode = mode.ToString(), WalkRunKeptAtOnce = walkRunKept, MovedAfterTheHoldStart = cellAfter != entangledCell, StoodWithin = stood });
            }
            session.SelectRules(rulesBefore);

            GD.Print("ARC-DEV-304 native-smoke hold-hero PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName ?? "crash-site", Holds = report,
                Parity = "NOT_VERIFIED (rules read in the original executable, GT145–GT153; no original-runtime run)" }));
        }
        finally
        {
            foreach (var run in holdRuns) if (session.QuerySpellRuns()[run] is not null) SpellCasterNow()?.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            FlushSpellLog(null);
            HeroRunsHost()?.Interrupt(Arcanum.Simulation.Goals.GoalInterruptRules.OrdinaryActions, "SMOKE_DONE"); StopWorldRoute("SMOKE_DONE");
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            RestoreHeroForSmoke(session, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
