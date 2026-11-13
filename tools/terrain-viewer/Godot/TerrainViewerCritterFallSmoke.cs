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
    // ARC-DEV-248 (B13, smoke plan SM11) native observation (stage "critter-fall", only when named, map mode with --hero, AI on, the Crash Site): a running animal killed by a spell lies where
    // its body is drawn, the pool of blood and the session on one cell. The hero learns a damaging college spell, the weakest animal is told of the hero's attack and runs at him from
    // seven or more cells, its hit points are cut to one, and the hero casts the spell at it through the pane, the cursor and the click while it is running. From the first frame the
    // session says it is dead (the fall begins) the stage samples the animal's mover and drawn body for three seconds of game time: the mover is not stepping once the step it was in has
    // ended, the drawn cell is the mover's cell and stays one cell for the three seconds, the session's place is that cell, and the pool of blood the dying animation leaves is on it.
    private async Task RunAreaCritterFallSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-DEV-248 native-smoke critter-fall SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-248 critter-fall smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CRITTER_FALL_SMOKE_FAILED");
        }
        for (var i = 0; i < 600 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_spellRecords is not null, "spell data " + _spellData?.Status + " " + _spellData?.Detail);
        var records = _spellRecords!;
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
        // ARC-DEV-293: an earlier stage of one run (ai-cast, ai, ai-follower) leaves its victim in a fight with the hero, and danger is session state: that animal would already be running at the hero
        // before the cast (and the stage counts the cells it runs after the click), so every animal starts calm, as ai-react's do.
        var inDanger = candidates.Count(c => session.QueryDanger(c.Holder.Entity).State != DangerState.None);
        CalmNpcsForSmoke(session, candidates.Select(c => c.Holder.Entity));
        if (candidates.Length == 0) { GD.Print($"ARC-DEV-248 native-smoke critter-fall NO-TARGET map={map.RuntimeMap}"); return; }
        var spell = LearnSmokeSpell(session, records, DamagingSmokeSpell, Require);
        Require(spell is not null, "the hero knows a damaging spell");

        // ARC-UI-1737: ARC_CRITTER_FALL_FILTER=1 runs the stage with the violence filter on (the smoke's own settings file; the stage turns it back off at its end).
        var filterTurnedOn = System.Environment.GetEnvironmentVariable("ARC_CRITTER_FALL_FILTER") == "1" && !_gameSettings.ViolenceFilter;
        if (filterTurnedOn) SaveGameSettings(_gameSettings with { ViolenceFilter = true });
        // ARC_CRITTER_FALL_BRIGHTNESS=N (0 to 9) runs it at that brightness, for the capture's brightness series; turned back to 0 at the end.
        var brightnessTurnedOn = int.TryParse(System.Environment.GetEnvironmentVariable("ARC_CRITTER_FALL_BRIGHTNESS"), out var brightness) && brightness is > 0 and <= Arcanum.Presentation.Display.BrightnessRules.Max
            && _gameSettings.Brightness == 0;
        if (brightnessTurnedOn) SaveGameSettings(_gameSettings with { Brightness = brightness });
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var npcForRestore = candidates[0].Build.Combat!.Target;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // The first animal (weakest first) the hero can stand seven to nine cells from and see: it has a run to make before it bites.
            var target = candidates[0]; CellCoord? start = null; var targetCell = default(CellCoord);
            foreach (var candidate in candidates.Take(24))
            {
                targetCell = map.Chart.ToChart(new(candidate.Row.WorldX!.Value, candidate.Row.WorldY!.Value));
                var body = commands[candidate.Row.Key];
                start = await AreaSmokeFightableStartAsync(map, body, targetCell, 9, 7, false) ?? await AreaSmokeFightableStartAsync(map, body, targetCell, 7, 6, false);
                if (start is not null) { target = candidate; break; }
            }
            if (start is null) { GD.Print($"ARC-DEV-248 native-smoke critter-fall NO-TARGET map={map.RuntimeMap} (no animal the hero can stand 6 to 9 cells from and see)"); return; }
            var entity = target.Holder.Entity;
            var npc = target.Build.Combat!.Target;
            npcForRestore = npc;
            AreaSmokePlace(map, start.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            Require(session.QueryAiTick(entity) is not null, "the animal has its AI tick");
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            // One hit point left, so that a spell that does not resist kills it.
            var status = session.QueryLife(npc).View!.Status;
            Require(session.ApplyDamage(session.Version, npc, new LifeDamage { HitPoints = status.HitPoints - 1 }).Disposition == GameSessionDisposition.Changed, "the animal is cut to one hit point");

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            void Frame()
            {
                _areaSmokeTime++;
                SpellSmokeTick();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            // An earlier stage of the run may have killed this animal and left its pool: only a new pool is this fall's.
            var oldPools = session.QueryBloodPools(new SessionMapId(map.RuntimeMap)).Select(p => p.Entity).ToHashSet();
            host!.HeroAttacked(entity);
            var danger = session.QueryDanger(entity);
            Require(danger.State == DangerState.CombatFocus && danger.Focus == SessionCombatant.Hero, $"the animal fights the hero: {danger.State}");
            // It runs: wait for its first step.
            var frames = 0;
            for (; frames < 200 && host.Movers.Of(entity)?.Stepping(host.NowMs) != true; frames++) Frame();
            Require(host.Movers.Of(entity)?.Stepping(host.NowMs) == true, $"the animal is running after {frames} frames: {host.TraceOf(entity)?.Reason}");
            // ARC-DEV-293: the animal's steps follow the AI's round clock, whose phase depends on the game time the run has reached (a run of earlier stages shifts it), and the cast kills it a fixed
            // 150 ms after the click: whether the animal is then in a step or between two depends on that phase. So the click waits for the start of the animal's next step (its cell changes) and for one frame
            // after it: the death then falls one frame into the step after that, whether or not the animal rests a frame between steps (a run step lasts a few frames).
            var stepFrom = host.Movers.Of(entity)!.Cell; var stepWait = 0;
            for (; stepWait < 200 && host.Movers.Of(entity) is { } before && (before.Cell == stepFrom || !before.Stepping(host.NowMs)); stepWait++) Frame();
            Require(host.Movers.Of(entity) is { } moved && moved.Cell != stepFrom, $"the animal began a second step within {stepWait} frames: {host.TraceOf(entity)?.Reason}");
            Frame();

            // The cast through the pane, the cursor and the click, at the body where it is drawn now.
            InventoryKey(Key.M);
            Require(_spellPane is { Shown: true } && _spellPane.ShownForSelfTest.Any(c => c.Spell == spell!.Data.Number), "M shows the pane with the spell: " + _spellNotice);
            _spellPane!.PressForSelfTest(spell!.Data.Number);
            Require(_spellTargeting?.Spell == spell.Data.Number, "the spell opened the targeting cursor: " + _spellNotice);
            var moving = AreaMovingCommands()?.FirstOrDefault(c => c.Identity == target.Row.Key);
            Require(moving is not null, "the running animal's body is among the moved bodies");
            var point = await HoverPointAsync(this, moving!) ?? throw new InvalidOperationException("ARC-DEV-248 smoke: the running animal is not visible");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.Cast, $"the cast hover {HoverActionForSelfTest}");
            var casts = _spellCasts;
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            Require(_spellCasts == casts + 1, "the click cast: " + _spellNotice);

            // Until the session says it is dead: was it still running?
            var cellsRun = new HashSet<CellCoord>(); bool steppingAtDeath = false; var castFrames = 0;
            for (; castFrames < 1200 && session.QueryLife(npc).View?.Status.IsDead != true; castFrames++)
            {
                Frame();
                if (host.Movers.Of(entity) is { } mover) { cellsRun.Add(mover.Cell); steppingAtDeath = mover.Stepping(host.NowMs); }
            }
            var killed = session.QueryLife(npc).View?.Status.IsDead == true;
            Require(killed, $"the spell killed the animal within {castFrames} frames (hit points {session.QueryLife(npc).View?.Status.HitPoints}); spell log: {string.Join(",", _spellCaster!.Log.TakeLast(6).Select(e => e.Kind))}");
            Require(cellsRun.Count >= 2, $"the animal ran over at least two cells before it died ({cellsRun.Count})");
            Require(steppingAtDeath, "the animal was in the middle of a step when the session said it was dead (the stage needs a running animal)");

            // Three seconds of game time from the first frame the session says it is dead.
            var samples = new List<(int Frame, CellCoord Mover, int DrawX, int DrawY, bool Stepping, CellCoord? Place)>();
            string? cause = null;
            for (var f = 0; f < 60; f++)
            {
                var drawn = AiMoverCommands().FirstOrDefault(c => c.Identity == target.Row.Key);
                var at = host.Movers.Of(entity);
                if (drawn is null || at is null) { cause = $"frame {f}: no drawn body or no mover ({drawn is null}/{at is null})"; break; }
                samples.Add((f, at.Cell, drawn.MapX, drawn.MapY, at.Stepping(host.NowMs), session.QueryNpcPlace(entity)?.World));
                Frame();
            }
            Require(cause is null, cause ?? "");
            GD.Print($"ARC-DEV-248 critter-fall running={steppingAtDeath} cellsRun={cellsRun.Count} castFrames={castFrames} samples="
                + string.Join(" ", samples.Where((_, i) => i % 6 == 0).Select(s => $"{s.Frame}:{s.Mover.X},{s.Mover.Y}/{s.DrawX},{s.DrawY}{(s.Stepping ? "*" : "")}")));
            var first = samples[0];
            var final = samples[^1];
            Require(samples.All(s => s.DrawX == s.Mover.X && s.DrawY == s.Mover.Y), "the drawn cell is the mover's cell in every sample");
            Require(samples.Select(s => s.Mover).Distinct().Count() <= 2, $"the body lay on at most the cell it was entering and the cell it stood on: {string.Join(">", samples.Select(s => s.Mover).Distinct())}");
            var settledAt = samples.FindIndex(s => !s.Stepping);
            Require(settledAt >= 0 && samples.Skip(settledAt).All(s => !s.Stepping && s.Mover == final.Mover && s.DrawX == final.DrawX && s.DrawY == final.DrawY),
                $"the body settled and stayed on one cell (settled at sample {settledAt})");
            Require(settledAt <= 8, $"the step it died in ended within 0.4 s ({settledAt} frames)");
            Require(final.Place == new CellCoord(final.Mover.X, final.Mover.Y), $"the session's place is the drawn cell: {final.Place} against {final.Mover}");

            // The pool of blood the dying animation leaves: on the same cell.
            SessionBloodPool? pool = null;
            for (var f = 0; f < 200 && pool is null; f++)
            {
                pool = session.QueryBloodPools(new SessionMapId(map.RuntimeMap)).FirstOrDefault(p => p.Critter == entity && !oldPools.Contains(p.Entity));
                if (pool is null) Frame();
            }
            // ARC-UI-1737 (OH2): with the violence filter setting on (a settings file naming it; off by default) no pool is made and the dying animation is the fall-down.
            var filterOn = _gameSettings.ViolenceFilter;
            if (filterOn)
            {
                Require(pool is null, "with the violence filter on the dying animation leaves no pool of blood");
                Require(host.ShownAnimation(entity) == DyingAnimationRules.FallDown, $"with the violence filter on the body shows the fall-down: animation {host.ShownAnimation(entity)}");
            }
            else
            {
                Require(pool is not null, "the dying animation left a pool of blood");
                Require(pool!.Cell == new CellCoord(final.Mover.X, final.Mover.Y), $"the pool of blood is on the cell the body lies on: pool {pool.Cell}, body {final.Mover}");
            }
            var lateDrawn = AiMoverCommands().FirstOrDefault(c => c.Identity == target.Row.Key);
            Require(lateDrawn is not null && lateDrawn.MapX == final.DrawX && lateDrawn.MapY == final.DrawY && host.Movers.Of(entity)?.Cell == final.Mover, "the body is still on that cell when the pool appears");
            // ARC_CRITTER_FALL_CAPTURE: the frame with the body (and its pool, or none) saved for the filter on/off composite.
            if (System.Environment.GetEnvironmentVariable("ARC_CRITTER_FALL_CAPTURE") is { Length: > 0 } capture)
            {
                for (var f = 0; f < 6; f++) { Frame(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng(capture) == Error.Ok, "the capture saved to " + capture);
                GD.Print($"ARC-UI-1737 critter-fall capture {capture} body={final.DrawX},{final.DrawY} filter={filterOn} brightness={_gameSettings.Brightness}");
            }
            GD.Print("ARC-DEV-248 native-smoke critter-fall PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, CalmedAtStart = inDanger, Animal = target.Row.PrototypeNumber, Spell = spell.Data.Number, RanOver = cellsRun.Count, SteppingAtDeath = steppingAtDeath, CastFrames = castFrames,
                SettledAfterFrames = settledAt, DiedOn = $"{first.Mover.X},{first.Mover.Y}", LyingOn = $"{final.Mover.X},{final.Mover.Y}", Pool = pool is null ? "none (violence filter on)" : $"{pool.Cell.X},{pool.Cell.Y}", ViolenceFilter = filterOn,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelSpellCast(null); _spellPane?.HidePane(); ClearSpellMissiles("the smoke ended"); _worldActor.TurnedFacing = null;
            HoverReleaseForSelfTest();
            if (session.QueryLife(npcForRestore).View?.Status is { } fallen && fallen.IsDead)
                session.Heal(session.Version, npcForRestore, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            if (filterTurnedOn) SaveGameSettings(_gameSettings with { ViolenceFilter = false });
            if (brightnessTurnedOn) SaveGameSettings(_gameSettings with { Brightness = 0 });
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            RestoreHeroForSmoke(session, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
            // The animal the stage told of the hero's attack does not carry that fight into the next stage.
            CalmNpcsForSmoke(session, candidates.Select(c => c.Holder.Entity));
        }
    }
}
