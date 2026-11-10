using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-325 (B10, smoke plan SM30) native observation (stage "save-resume", only when named, map mode with --hero, AI on, the Crash Site): the hero holds a maintained spell, the weakest
    // animal (a wolf of the wreck) is told of the hero's attack and runs at him from seven or more cells, and F7 saves in the middle of one of its steps; the run goes on live a few frames, F8 loads.
    // After the load the spell holds its slot and has its tick queued, the animal stands on the cell it was saved on, its run is on the table, and it goes on running at the hero (it comes nearer
    // than it was saved, as the live run did).
    private async Task RunAreaSaveResumeSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-DEV-325 native-smoke save-resume SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-325 save-resume smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SAVE_RESUME_FAILED");
        }
        for (var i = 0; i < 600 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_spellRecords is not null && SpellCasterNow() is not null, "spell data " + _spellData?.Status + " " + _spellData?.Detail);
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is { Monstrous: true } && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core)
            .OrderBy(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        CalmNpcsForSmoke(session, candidates.Select(c => c.Holder.Entity));
        if (candidates.Length == 0) { GD.Print($"ARC-DEV-325 native-smoke save-resume NO-TARGET map={map.RuntimeMap}"); return; }

        var heroBefore = session.QueryHeroLife()!.Record.State;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-325-").FullName;
        _saveLibraryForSelfTest = new(saves);
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var animal = candidates[0];
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var target = candidates[0]; CellCoord? start = null; var targetCell = default(CellCoord);
            foreach (var candidate in candidates.Take(24))
            {
                targetCell = map.Chart.ToChart(new(candidate.Row.WorldX!.Value, candidate.Row.WorldY!.Value));
                var body = commands[candidate.Row.Key];
                start = await AreaSmokeFightableStartAsync(map, body, targetCell, 9, 7, false) ?? await AreaSmokeFightableStartAsync(map, body, targetCell, 7, 6, false);
                if (start is not null) { target = candidate; break; }
            }
            if (start is null) { GD.Print($"ARC-DEV-325 native-smoke save-resume NO-TARGET map={map.RuntimeMap} (no animal the hero can stand 6 to 9 cells from and see)"); return; }
            animal = target;
            var entity = target.Holder.Entity;
            var npc = target.Build.Combat!.Target;
            AreaSmokePlace(map, start.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            Require(session.QueryAiTick(entity) is not null, "the animal has its AI tick");
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 31_000_000L;
            TickAreaClock(hostMs);
            void Frame()
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var heroCell = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            // A place and a mover cell are in the map's world cells, the hero's committed cell in its chart: the distance is taken in the chart.
            int Distance(CellCoord world) { var c = map.Chart.ToChart(new(world.X, world.Y)); return Math.Max(Math.Abs(c.X - heroCell.X), Math.Abs(c.Y - heroCell.Y)); }

            // The maintained spell the hero holds across the save (a slot of the maintain bar).
            var started = StartMaintainedSpellForSmoke(session, SessionSpellCaster.Hero);
            Require(started is not null, "a maintained spell took a slot of the maintain bar before the save");
            var spellRun = started!.Value.Run;
            var spellSlot = session.QueryMaintainBar().SlotOf(spellRun);
            Require(session.QuerySpellRuns()[spellRun] is not null && spellSlot is not null, "the maintained spell holds a slot");

            host!.HeroAttacked(entity);
            var danger = session.QueryDanger(entity);
            Require(danger.State == DangerState.CombatFocus && danger.Focus == SessionCombatant.Hero, $"the animal fights the hero: {danger.State}");
            var frames = 0;
            for (; frames < 200 && host.Movers.Of(entity)?.Stepping(host.NowMs) != true; frames++) Frame();
            Require(host.Movers.Of(entity)?.Stepping(host.NowMs) == true, $"the animal is running after {frames} frames: {host.TraceOf(entity)?.Reason}");
            // Two steps of the run, then a frame in the middle of the third: the save is taken mid-action.
            var cells = new HashSet<CellCoord> { host.Movers.Of(entity)!.Cell };
            for (var f = 0; f < 400 && (cells.Count < 3 || host.Movers.Of(entity)?.Stepping(host.NowMs) != true); f++)
            {
                Frame();
                if (host.Movers.Of(entity) is { } at) cells.Add(at.Cell);
                if (session.QueryLife(npc).View?.Status.IsDead == true) break;
            }
            var savedMover = host.Movers.Of(entity);
            Require(savedMover is not null && savedMover.Stepping(host.NowMs) && cells.Count >= 3, $"the animal is mid-step after at least two steps ({cells.Count} cells)");
            var savedPlace = session.QueryNpcPlace(entity)?.World;
            Require(savedPlace is not null, "the animal has a session place at the save");
            var savedDistance = Distance(savedPlace!.Value);
            Require(savedDistance >= 2, $"the animal is still short of the hero at the save ({savedDistance} cells)");
            var runsAtSave = session.QueryGoals().RunsOf(entity).Where(r => !r.Done).Select(r => $"{r.Bottom.Goal}/{r.Depth}").ToArray();
            Require(runsAtSave.Length > 0, "the running animal holds a goal run at the save");
            var savedKeys = session.Capture().Goals.Runs.Where(r => r.Owner == entity).Select(r => $"{r.Handle.Slot}:{r.Handle.RunId} {r.Bottom.Goal} {r.Depth}").ToArray();
            Require(savedKeys.Length > 0, "the animal's goal run is in the snapshot");

            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            Require(session.Capture().Spells.Runs.Any(r => r.Id == spellRun), "the maintained spell is in the snapshot");

            // The live run goes on: the animal comes nearer.
            for (var f = 0; f < 12; f++) Frame();
            var liveDistance = host.Movers.Of(entity) is { } live ? Distance(live.Cell) : savedDistance;
            Require(liveDistance < savedDistance || session.QueryLife(npc).View?.Status.IsDead == true, $"the live animal came nearer in twelve frames ({savedDistance} -> {liveDistance})");

            var running = _areaSession;
            QuickKey(Key.F8);
            Require(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            Require(!ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load published a new session: " + _quickSaveStatus);
            var loadedSession = _areaSession!;

            // The spell: the same run in the same slot with its tick queued.
            Require(loadedSession.QuerySpellRuns()[spellRun] is not null, "after the load the maintained spell's run is there");
            Require(loadedSession.QueryMaintainBar().SlotOf(spellRun) == spellSlot, "after the load the spell holds the same slot of the maintain bar");
            Require(loadedSession.QueryEvents().Any(e => e.Kind == SessionEventKinds.SpellRun && e.Payload.Values[1] == spellRun && e.Payload.Values[2] == (long)SpellRunEventCode.Tick), "after the load the spell's next tick is queued");

            // The animal: on its saved cell, its run in the table, danger kept.
            var loadedPlace = loadedSession.QueryNpcPlace(entity)?.World;
            Require(loadedPlace == savedPlace, $"after the load the animal stands on its saved cell: saved {savedPlace}, loaded {loadedPlace}");
            var loadedKeys = loadedSession.QueryGoals().Runs.Where(r => r.Owner == entity && !r.Done).Select(r => $"{r.Handle.Slot}:{r.Handle.RunId} {r.Bottom.Goal} {r.Depth}").ToArray();
            var missing = savedKeys.Except(loadedKeys).ToArray();
            Require(missing.Length == 0, "after the load the animal's saved run is in the table under its handle; missing: " + string.Join(" | ", missing) + "; loaded: " + string.Join(" | ", loadedKeys));
            var loadedDanger = loadedSession.QueryDanger(entity);
            Require(loadedDanger.State == DangerState.CombatFocus, $"after the load the animal still fights the hero: {loadedDanger.State}");

            // It goes on running at the hero: frames on the loaded session until it is near or the cap.
            for (var i = 0; i < 5; i++) { AreaSmokeFrame(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var loadedHost = AreaAiHostNow();
            Require(loadedHost is not null, "the loaded session has an AI host");
            var trail = new List<CellCoord> { loadedPlace!.Value };
            for (var f = 0; f < 600 && Distance(trail[^1]) > 1; f++)
            {
                TickAreaClock(hostMs += 100);
                AreaSmokeFrame();
                if (f % 100 == 99) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (loadedSession.QueryNpcPlace(entity)?.World is { } now && now != trail[^1]) trail.Add(now);
                if (loadedSession.QueryLife(npc).View?.Status.IsDead == true) break;
            }
            var endDistance = Distance(trail[^1]);
            Require(trail.Count >= 2, $"after the load the animal ran on from its saved cell (cells {string.Join(">", trail.Select(c => $"{c.X},{c.Y}"))})");
            Require(endDistance < savedDistance, $"after the load the animal came nearer than it was saved ({savedDistance} -> {endDistance})");
            GD.Print("ARC-DEV-325 native-smoke save-resume PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Animal = animal.Row.PrototypeNumber, SavedCell = $"{savedPlace.Value.X},{savedPlace.Value.Y}", SavedDistance = savedDistance, LiveDistanceAfter12 = liveDistance,
                LoadedEndDistance = endDistance, CellsAfterLoad = trail.Count, Runs = runsAtSave, Spell = started.Value.Spell.Data.Number, SpellSlot = spellSlot }));
        }
        finally
        {
            if (_spellCaster is { } caster && _areaSession is { } left)
            {
                foreach (var slot in left.QueryMaintainBar().Slots.Where(s => s.State != MaintainSlotState.Empty)) caster.Act(new(SessionSpellRunActionKind.ForceEnd, slot.Run));
                left.Heal(left.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            }
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            var live = _areaSession ?? session;
            if (live.QueryLife(animal.Build.Combat!.Target).View?.Status is { } fallen && fallen.IsDead)
                live.Heal(live.Version, animal.Build.Combat.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            RestoreHeroForSmoke(live, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
            CalmNpcsForSmoke(live, candidates.Select(c => c.Holder.Entity));
        }
    }
}
