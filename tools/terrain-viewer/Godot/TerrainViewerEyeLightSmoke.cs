using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal const string EyeLightCode = "ARC-MAGIC-1229";

    // ARC-MAGIC-1229 (FR-LIGHT-011, ledger IW2-IW5) native observation (stage "eye-light", only when named, with --hero, on a map with a living NPC): Illuminate (spell 65) keeps a live light on its target
    // while the maintained spell holds, and the light follows the production path (the cast host's EyeCandy Add, the overlay run, the carried-light tick, the canvas, the darkness sampler).
    //  - cast on the hero by a script (free, unresistable): the host answers one light (light art 1, colour 226 60 236, the first overlay slot), the canvas holds one carried light more, and the level the
    //    darkness check reads at the hero's cell rises (the clock is moved to a night hour first, as the worn-light stage does: a bright day saturates the level);
    //  - the same on the nearest living NPC whose cell the lighting covers (its light is built where it stands);
    //  - the spell's forced END interrupts the run, the end node writes the slot back: no light, the canvas holds its old count and the level is back at its first value;
    //  - a second cast on the same target would find its slot free again (the four overlay slots of the object are the host's rule, unit tests).
    // The spell runs and the hero's state are put back for the later stages (the clock is not put back, as in worn-light).
    private async Task RunAreaEyeLightSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area || _worldActor.Session is not AreaGridSession
            || _canvas is null || _areaAmbient is not { } driver || area.Lighting is not { Local: not null, CarriedFrame: not null })
        { GD.Print($"{EyeLightCode} native-smoke eye-light NONE map={_readyMapName} (needs --hero, an area session, its combat host and a lit map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{EyeLightCode} eye-light smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_EYE_LIGHT_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var wasEnabled = driver.Enabled;
        var scaled = false;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var runs = new List<int>();
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            driver.Enabled = true;
            // A night ambient, so a light's glow raises the level (a bright day saturates it).
            for (var hours = 0; hours < 24 && Math.Max(driver.Effective.Outdoor.R, Math.Max(driver.Effective.Outdoor.G, driver.Effective.Outdoor.B)) > 140; hours++)
            { driver.AdvanceHours(1, AmbientSector()); LogAreaEvents(driver); }
            PushAreaAmbient();
            var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
                ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
            var runtime = CurrentAreaMap;
            AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var wait = 0; wait < 600 && (SpellCasterNow() is null || SpellRecordsNow() is null); wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(SpellCasterNow() is not null && SpellRecordsNow() is not null, "the map's cast host and the spell records are built (spell data " + _spellData?.Status + ")");
            // A placement restarts the smoke clock in tenths of a second; the frames here are twentieths.
            scaled = true; _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            SessionSpellCaster Caster() => SpellCasterNow() ?? throw new InvalidOperationException("AREA_EYE_LIGHT_NO_CAST_HOST");
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++) { TechSmokeFrame(); FlushSpellLog(null); }
                TickCarriedLight();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            int Carried() => _canvas.AreaCarriedForSelfTest?.CarriedLightCount ?? 0;
            // Only for the thread's before/after picture (never committed): ARC_EYE_LIGHT_CAPTURE names a folder, the hero's view is saved there before the cast and while the spell holds.
            var captureDir = System.Environment.GetEnvironmentVariable("ARC_EYE_LIGHT_CAPTURE");
            async Task Shot(string name)
            {
                if (string.IsNullOrEmpty(captureDir)) return;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                System.IO.Directory.CreateDirectory(captureDir);
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(System.IO.Path.Combine(captureDir, name + ".png"));
            }
            await Frames(5);

            var records = SpellRecordsNow()!;
            Require(records.Length > 65 && records[65] is not null, "spell 65's record is read");
            var entry = EyeCandyTable?.Entry(65, SpellEyeCandyTable.SecondaryDestination);
            Require(entry is { Light: 1 } && entry.LightColor == ((byte)226, (byte)60, (byte)236), $"entry 3 of Illuminate holds light art 1 in (226, 60, 236): {entry}");

            var report = new List<object>();
            async Task Probe(string label, string target, Func<int> level)
            {
                var host = ShownEyeCandyHost;
                var lightsBefore = host?.Lights().Count() ?? 0; var carriedBefore = Carried(); var levelBefore = level();
                if (target == SessionSpellCaster.Hero) await Shot("hero-before");
                var attempt = Caster().CastByScript(null, 65, target, free: true, unresistable: true);
                Require(attempt.Outcome == SpellCastAttemptOutcome.Started && attempt.RunId is not null, $"the script cast of Illuminate on {label} started: {attempt.Outcome} {attempt.Run?.Disposition} {attempt.Run?.Failure}");
                var run = attempt.RunId!.Value;
                runs.Add(run);
                for (var i = 0; i < 400 && (ShownEyeCandyHost?.Lights().Count() ?? 0) <= lightsBefore; i += 5) await Frames(5);
                host = ShownEyeCandyHost;
                var lights = host?.Lights().Where(l => l.Owner == target).ToArray() ?? [];
                Require(lights.Length == 1, $"Illuminate keeps one live light on {label} while it holds ({lights.Length}; log {string.Join(",", Caster().Log.TakeLast(10).Select(e => $"{e.Kind}:{e.Run}:{e.Value}:{e.Detail}"))})");
                Require((lights[0].ArtNumber, lights[0].Color) == (1, ((byte)226, (byte)60, (byte)236)) && lights[0].Slot is >= 0 and < AreaEyeCandyLights.Slots, $"the light is art 1 in (226, 60, 236) in an overlay slot: {lights[0]}");
                await Frames(3);
                var during = level();
                if (target == SessionSpellCaster.Hero) await Shot("hero-during");
                Require(Carried() == carriedBefore + 1, $"the canvas holds the glow ({carriedBefore} -> {Carried()})");
                Require(during > levelBefore, $"the darkness sampler sees the glow on {label} ({levelBefore} -> {during})");
                // The glow goes with the spell: the forced END interrupts the eye candy run at level 6, its end node writes the slot back.
                Require(_areaSession?.QuerySpellRuns()[run] is not null, "the maintained spell is still running");
                Caster().Act(new(SessionSpellRunActionKind.ForceEnd, run));
                for (var i = 0; i < 60 && (ShownEyeCandyHost?.Lights().Count(l => l.Owner == target) ?? 0) > 0; i += 5) await Frames(5);
                await Frames(3);
                Require(!(ShownEyeCandyHost?.Lights().Any(l => l.Owner == target) ?? false), $"the END took the light off {label}");
                Require(Carried() == carriedBefore && level() == levelBefore, $"the canvas count ({Carried()} / {carriedBefore}) and the level ({level()} / {levelBefore}) are back after the END");
                GD.Print($"{EyeLightCode} eye-light target={label} run={run} light={lights[0]} level={levelBefore}->{during}->{levelBefore}");
                report.Add(new { Target = label, Run = run, Slot = lights[0].Slot, Before = levelBefore, During = during });
            }

            var heroCell = HeroWorldCell() ?? throw new InvalidOperationException("AREA_EYE_LIGHT_NO_HERO_CELL");
            await Probe("hero", SessionSpellCaster.Hero, () => AreaCellLightLevel(heroCell.X, heroCell.Y));

            // The nearest living NPC whose cell the lighting covers and whose own level the night leaves room above.
            var here = new CellCoord(heroCell.X, heroCell.Y);
            static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
            var npcs = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
                .Select(row => (Row: row, Id: SpellPickId(row.Key), Target: _combatHost.Target(session, runtime, row.Key)))
                .Where(o => o.Id is not null && o.Target is { Dead: false, Follower: false } && Caster().Facts(o.Id) is { } facts && facts.Location != SpellLocation.None && (facts.ObjectFlags & SpellTargetObject.Invulnerable) == 0
                    && AreaRowLightLevel(o.Row) < Arcanum.Simulation.Combat.AttackRules.MaxLightLevel)
                .OrderBy(o => Math.Max(Math.Abs(World(o.Row).X - here.X), Math.Abs(World(o.Row).Y - here.Y))).Take(8).ToArray();
            var lit = false;
            foreach (var npc in npcs)
            {
                try { await Probe("npc " + npc.Id, npc.Id!, () => AreaRowLightLevel(npc.Row)); lit = true; break; }
                catch (InvalidOperationException) when (npc.Row != npcs[^1].Row) { /* a place the glow cannot raise (a lit cell, a cell outside the window): the next NPC */ }
            }
            if (!lit) GD.Print($"{EyeLightCode} eye-light npc NONE (no living NPC with room above its level among {npcs.Length} candidates)");
            GD.Print("ARC-MAGIC-1229 native-smoke eye-light PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Targets = report }));
        }
        finally
        {
            if (SpellCasterNow() is { } caster)
                foreach (var run in runs) if (_areaSession?.QuerySpellRuns()[run] is not null) caster.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            HeroRunsHost()?.LoseAll("SMOKE_EYE_LIGHT_END");
            _eyeCandyHost?.LoseAll();
            if (_areaSession is { } now) RestoreHeroForSmoke(now, heroBefore);
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            driver.Enabled = wasEnabled; PushAreaAmbient();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
