using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-208 (MIG14 S2, smoke plan SM08 / SM30 part) native observation (stage "saved-runs", only when named, map mode with --hero, AI on, a town: --sector 1409,1314): after the
    // town's route NPCs have been walking for ninety seconds of game time, F7 saves and F8 loads on the same map (ARC-DEV-211: with a maintained spell of the hero holding a slot of the maintain bar,
    // which must be the same run in the same slot with its tick queued after the load and ticking on). The goal runs the save held (the NPCs' walks, the doors' and
    // scenery runs, every owner but the hero) are in the loaded session's table under the same handles with the same bottom goal and stack depth, each with its one step queued,
    // and after twenty more seconds of game time on the loaded session the runs still step (a walking NPC's run advances or ends by itself; none is lost by the load).
    private async Task RunAreaSavedRunsSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-208 saved-runs smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SAVED_RUNS_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is null)
        { GD.Print($"ARC-DEV-208 native-smoke saved-runs NONE map={_readyMapName} (needs a hero and combat values)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-208-").FullName;
        _saveLibraryForSelfTest = new(saves);
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps } ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
            AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            if (AreaAiHostNow() is null) { GD.Print("ARC-DEV-208 native-smoke saved-runs NONE (no AI host)"); return; }
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 31_000_000L;
            TickAreaClock(hostMs);
            long Seconds() => _areaSession!.QueryClockSnapshot().GameTimestampMs / 1000;
            async Task Run(long gameSeconds, int cap)
            {
                var until = Seconds() + gameSeconds;
                for (var frame = 0; frame < cap && Seconds() < until; frame++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(hostMs += 50);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                    if (frame % 200 == 199) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            await Run(30, 4000);
            await Run(90, 6000);
            // ARC-DEV-211 (MIG14, SM30 second half, B10): a maintained spell of the hero holds a slot of the maintain bar at the save and must be there, with its next tick queued, after the load.
            Require(SpellCasterNow() is not null && _spellRecords is { Length: > 0 }, "a cast host and the spell data");
            var started = StartMaintainedSpellForSmoke(_areaSession!, SessionSpellCaster.Hero);
            Require(started is not null, "a maintained spell took a slot of the maintain bar before the save");
            var spellRun = started!.Value.Run;
            await Run(5, 600);
            var spellSlot = _areaSession!.QueryMaintainBar().SlotOf(spellRun);
            Require(_areaSession!.QuerySpellRuns()[spellRun] is not null && spellSlot is not null, "the maintained spell is still maintained five seconds on");
            var before = SavedRunKeys(_areaSession!);
            Require(before.Count >= 1, "the town has at least one NPC goal run to save after ninety seconds of game time (found none: the AI did not start a walk)");
            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            var spellAtSave = _areaSession!.QuerySpellRuns()[spellRun];
            Require(_areaSession!.Capture().Spells.Runs.Any(r => r.Id == spellRun), "the maintained spell is in the snapshot");
            var saved = SavedRunKeys(_areaSession!);
            var savedCount = _areaSession!.Capture().Goals.Runs.Count;
            Require(saved.Count == savedCount, $"every run of a saved kind is in the snapshot: table {saved.Count}, snapshot {savedCount}");
            await Run(20, 2000);
            var running = _areaSession;
            QuickKey(Key.F8);
            Require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            Require(!ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load published a new session: " + _quickSaveStatus);
            var loadedSession = _areaSession!;
            var after = SavedRunKeys(loadedSession);
            var missing = saved.Except(after).ToArray();
            Require(missing.Length == 0, "after the load every saved run is in the table under its handle; missing: " + string.Join(" | ", missing));
            Require(loadedSession.QuerySpellRuns()[spellRun] == spellAtSave, "after the load the maintained spell's run is the saved one, field for field");
            Require(loadedSession.QueryMaintainBar().SlotOf(spellRun) == spellSlot, "after the load the spell holds the same slot of the maintain bar");
            var tickAtLoad = loadedSession.QueryEvents().FirstOrDefault(e => e.Kind == SessionEventKinds.SpellRun && e.Payload.Values[1] == spellRun && e.Payload.Values[2] == (long)SpellRunEventCode.Tick);
            Require(tickAtLoad is not null, "after the load the spell's next tick is queued");
            var steps = loadedSession.QueryEvents().Count(e => e.Kind == SessionEventKinds.GoalStep);
            Require(steps >= saved.Count, $"each restored run has its step queued: {steps} steps for {saved.Count} saved runs");
            for (var i = 0; i < 5; i++) { AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            // The loaded session's frames: the smoke's own frame stamp (a tenth of a second of animation time per frame, as the other post-load stages step).
            var resumedAt = Seconds();
            for (var frame = 0; frame < 4000 && Seconds() < resumedAt + 20; frame++)
            {
                TickAreaClock(hostMs += 100);
                AreaSmokeFrame();
                if (frame % 200 == 199) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var later = loadedSession.QueryGoals().Runs.Count(r => !r.Done);
            // The restored spell goes on by itself: a tick that fell due in the twenty seconds has fired (the tick moved on, or the spell ended), and a spell still maintained keeps its slot.
            var tickNow = loadedSession.QueryEvents().FirstOrDefault(e => e.Kind == SessionEventKinds.SpellRun && e.Payload.Values[1] == spellRun && e.Payload.Values[2] == (long)SpellRunEventCode.Tick);
            var nowMs = loadedSession.QueryClockSnapshot().GameTimestampMs;
            Require(tickAtLoad!.DeadlineMs > nowMs || tickNow is null || tickNow.DeadlineMs > tickAtLoad.DeadlineMs, "the restored spell's tick that fell due fired and moved on");
            Require(loadedSession.QuerySpellRuns()[spellRun] is null || loadedSession.QueryMaintainBar().SlotOf(spellRun) == spellSlot, "a spell still maintained keeps its slot of the bar");
            GD.Print("ARC-DEV-208 native-smoke saved-runs PASS " + System.Text.Json.JsonSerializer.Serialize(new { Saved = saved.Count, NpcRuns = loadedSession.QueryGoals().Runs.Count(r => !AiExecutorRules.IsObjectRunGoal(r.Bottom.Goal) && r.Owner != AiSchedulerRules.Pc), Restored = saved.Count - missing.Length, Steps = steps, After20s = later, Spell = started.Value.Spell.Data.Number, SpellSlot = spellSlot, SpellStillThere = loadedSession.QuerySpellRuns()[spellRun] is not null }));
        }
        finally
        {
            // The maintained spell this stage started goes, so a later stage of the same process sees no maintained spell and a rested hero.
            if (_spellCaster is { } host && _areaSession is { } left)
            {
                foreach (var slot in left.QueryMaintainBar().Slots.Where(s => s.State != MaintainSlotState.Empty)) host.Act(new(SessionSpellRunActionKind.ForceEnd, slot.Run));
                left.Heal(left.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            }
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
    }

    // The runs a save holds, as "slot:id owner bottom depth" keys (the hero's, the flights' and the spell runs' are not saved yet: the capture's own filter).
    private static HashSet<string> SavedRunKeys(GameSessionOwner session) =>
        session.Capture().Goals.Runs.Select(r => $"{r.Handle.Slot}:{r.Handle.RunId} {r.Owner} {r.Bottom.Goal} {r.Depth}").ToHashSet(StringComparer.Ordinal);
}
