using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-MAGIC-036 (MA-5, MV1–MV3) native observation (stage "movement", only when named, with --hero, on the Crash Site window): Spatial Distortion's Movement component (type 0, the run's target tile) relocates the hero
    // as the original's immediate path relocation does (0x455350): the one path request of the drop (flags 0x10), at most the eight tiles it returns walked, the hero's walk ended and the hero placed on the last tile
    // at once, with the grid actor, the session's place and the camera's follow in step. The cast host's component is applied by hand to the hero with a run of spell 3 whose target tile is the far cell of a walk
    // (at least 8 cells away), so the hero lands on the eighth tile of the route and not on the far cell; a target tile that is the hero's own tile moves nothing; afterwards a click walks the hero again.
    // The hero and the runs are restored for the later stages.
    private async Task RunAreaMovementSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _worldActor.Session is not AreaGridSession)
        { GD.Print("ARC-MAGIC-036 native-smoke movement NONE map=" + _readyMapName + " (needs a hero)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-MAGIC-036 movement smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MOVEMENT_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var scaled = false;
        var heroBefore = session.QueryHeroLife()!.Record.State;
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
            SessionSpellCaster Caster() => SpellCasterNow() ?? throw new InvalidOperationException("AREA_MOVEMENT_NO_CAST_HOST");
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
                TickAreaClock(_talkSmokeHostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            AreaGridSession Grid() => (AreaGridSession)_worldActor.Session!;
            CellCoord HeroWorld() { var w = Grid().Chart.ToWorld(Grid().State.CommittedCell); return new(checked((int)w.X), checked((int)w.Y)); }
            CellCoord World(CellCoord chart) { var w = Grid().Chart.ToWorld(chart); return new(checked((int)w.X), checked((int)w.Y)); }
            for (var i = 0; i < 5; i++) Frame();

            // The component applied as a BEGIN does: spell 3, the hero the source, parent and target, the run's target tile the world cell asked for.
            string Cast(CellCoord destination, int margin = 0)
            {
                var caster = Caster();
                var hero = caster.RunObject(SessionSpellCaster.Hero) ?? throw new InvalidOperationException("AREA_MOVEMENT_NO_HERO");
                var here = HeroWorld();
                var tile = new SpellLocation(checked((uint)destination.X), checked((uint)destination.Y));
                var run = new SpellRun(0, 3, SpellRunAction.Begin, hero, hero, new SpellLocation(checked((uint)here.X), checked((uint)here.Y)), hero, tile, null, SpellRunFlags.Active) with { Margin = margin };
                var logStart = caster.Log.Count;
                var outcome = caster.Apply(run, 0, new(hero, tile));
                var text = string.Join(",", caster.Log.Skip(logStart).Select(e => $"{e.Kind}:{e.Value}:{e.Detail}"));
                FlushSpellLog(null);
                GD.Print($"ARC-MAGIC-036 movement-cast destination={destination.X},{destination.Y} margin={margin} log=[{text}] outcome={outcome}");
                return text;
            }

            var startWorld = HeroWorld();
            var farWorld = World(far!.Value);
            var expected = ImmediatePathWalk.Tiles(AreaDropMapNow() ?? throw new InvalidOperationException("AREA_MOVEMENT_NO_DROP_MAP"), startWorld, farWorld, DropOwner.PcSide);
            Require(expected.Count == 8, $"the far cell is at least 8 cells away, so the walk is the request's whole cap of 8 tiles: {expected.Count}");
            Require(expected[^1] != farWorld, "the hero is not taken to the far cell, only eight tiles of the way");

            // ---- ARC-INPUT-1936 (GT107): a press with a Shift key held does nothing on the ground; the plain press walks ----
            WorldRouteClick(Screen(far.Value), shift: true);
            Require(HeroRunsHost()?.WalkRun is null && HeroWalkGoal is null && HeroWorld() == startWorld, $"a Shift press sets no walk: {_worldRouteClick}");

            // ---- a walk under way ends, and a resisted run moves nothing ----
            WorldRouteClick(Screen(far.Value));
            Require(HeroRunsHost()?.WalkRun is not null && HeroWalkGoal == far, $"a press walks the hero toward {far}: {_worldRouteClick}");
            // ARC-INPUT-1936: a Shift press on the ground elsewhere changes nothing of the walk under way (the original returns before any cancel or retarget).
            WorldRouteClick(Screen(start), shift: true);
            Require(HeroRunsHost()?.WalkRun is not null && HeroWalkGoal == far, $"a Shift press leaves the walk toward {far} as it was: {_worldRouteClick}");
            // ---- ARC-INPUT-1938 (GT106–GT111): a held left button's polled repeats retarget the walk to the tile under the pointer and mark its run 0x400 ----
            // The first press never marks; the held flag is set at its tail; a repeat is due only once more than 250 ms have passed since the stamp; a press on an object while the flag is 1 does nothing; a release clears the flag.
            // The repeats' first target: an open neighbour of the far cell (another tile the walk can reach).
            var heldRoutes = new AreaRouteSource(map.Steps, map.Chart);
            var heldTile = Enum.GetValues<NeighborDirection>().Select(d => (Cell: RoutePlanner.Neighbor(far.Value, d), Direction: d))
                .First(n => heldRoutes.Contains(n.Cell) && heldRoutes.Assess(far.Value, n.Direction) == RouteStepVerdict.Open).Cell;
            try
            {
                _pointerInWindow = true;
                HeldMouseForSelfTest(true, 10_000, (Screen(far.Value), false));
                NoteLeftPress();
                WorldRouteClick(Screen(far.Value));
                Require(_heldWalkFlag, "a held tile press sets the held flag: " + _worldRouteClick);
                Require((HeroRunsHost()?.WalkRun?.Flags & Arcanum.Simulation.Goals.GoalRunFlags.Flight) == 0, "the first press does not mark the run 0x400");
                HeldMouseForSelfTest(true, 10_250, (Screen(heldTile), false));
                PollHeldMouse();
                Require(HeldMouseRepeats == 0, "250 ms after the stamp no repeat is due (the rule is more than 250 ms)");
                HeldMouseForSelfTest(true, 10_251, (Screen(heldTile), false));
                PollHeldMouse();
                Require(HeldMouseRepeats == 1 && HeroWalkGoal == heldTile && (HeroRunsHost()?.WalkRun?.Flags & Arcanum.Simulation.Goals.GoalRunFlags.Flight) != 0,
                    $"the repeat walks to the tile under the pointer now ({HeroWalkGoal}) and marks the run 0x400 ({HeroRunsHost()?.WalkRun?.Flags}): {_worldRouteClick}");
                HeldMouseForSelfTest(true, 10_400, (Screen(far.Value), false));
                PollHeldMouse();
                Require(HeldMouseRepeats == 1 && HeroWalkGoal == heldTile, "the stamp was reset by the repeat: 149 ms later none is due");
                HeldMouseForSelfTest(true, 10_502, (Screen(far.Value), true)); // 251 ms after the repeat: due, but the pointer is over a control (the stamp still resets)
                PollHeldMouse();
                Require(HeldMouseRepeats == 1 && HeroWalkGoal == heldTile && _heldWalkFlag, "a pointer over a control posts nothing and keeps the flag");
                HeldMouseForSelfTest(true, 10_753, (Screen(far.Value), false));
                PollHeldMouse();
                Require(HeldMouseRepeats == 2 && HeroWalkGoal == far, $"the next repeat retargets in place to {far}: {HeroWalkGoal}");
                HeldMouseForSelfTest(false, 10_754, null);
                PollHeldMouse();
                Require(!_heldWalkFlag && HeroRunsHost()?.WalkRun is not null && HeroWalkGoal == far, "the release clears the flag and the hero keeps walking to the last target");
            }
            finally { HeldMouseForSelfTest(null, null, null); NoteLeftRelease(); }
            for (var i = 0; i < 40; i++) Frame();
            var walking = HeroWorld();
            Require(walking != startWorld && HeroRunsHost()?.WalkRun is not null, "the hero is on its way");
            // The walk's expectation is asked from where the hero is now.
            var onTheWay = ImmediatePathWalk.Tiles(AreaDropMapNow()!, walking, farWorld, DropOwner.PcSide);
            Require(Cast(farWorld, margin: 2).Length == 0 && HeroWorld() == walking && HeroRunsHost()?.WalkRun is not null, "a resisted run (margin 2) moves nothing and ends no walk");

            // ---- the relocation ----
            var log = Cast(farWorld);
            Require(log == $"Movement:{farWorld.X}:{farWorld.Y}", "the host logged the one request: " + log);
            for (var i = 0; i < 4; i++) Frame();
            var landed = HeroWorld();
            Require(onTheWay.Count > 0 && landed == onTheWay[^1], $"the hero stands on the last tile of the route ({onTheWay.Count} tiles) {(onTheWay.Count > 0 ? onTheWay[^1].ToString() : "")}: it stands on {landed}");
            Require(HeroRunsHost()?.WalkRun is null, "the hero's walk ended with the relocation (the route stopped)");
            var place = session.QueryActor();
            Require(place.Cell.X == landed.X && place.Cell.Y == landed.Y, $"the session holds the hero's place {place.Cell}: it stands on {landed}");
            Require(_movementsForSelfTest.Count > 0 && _movementsForSelfTest[^1].Target == SessionSpellCaster.Hero && _movementsForSelfTest[^1].Tiles == onTheWay.Count, "the viewer's host took the request: " + string.Join(";", _movementsForSelfTest.Select(m => $"{m.Target}:{m.Tiles}:{m.Result}")));
            Require(_mapEntered.Count == 0, "every tile the walk entered was handled by the map-transfer step");

            // ---- the hero's own tile is no route; a click walks again ----
            var before = HeroWorld();
            var same = Cast(before);
            Require(same == $"Movement:{before.X}:{before.Y}" && HeroWorld() == before && _movementsForSelfTest[^1].Result == "no route", $"a target tile that is the hero's own tile moves nothing: {same} {_movementsForSelfTest[^1].Result}");

            // ---- Teleportation (spell 4, ARC-MAGIC-038, TP3/TP4): the cast moves nothing itself, the world map opens in its teleport mode, and closing it pays nothing and moves nothing ----
            for (var i = 0; i < 1200 && _worldMapData?.Data is null; i++) { AdoptWorldMapData(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            Require(_worldMapData?.Data is not null, "the world-map data loaded for the teleport check: " + _worldMapData?.Status);
            for (var i = 0; i < 6; i++) Frame();
            var teleportHero = Caster().RunObject(SessionSpellCaster.Hero) ?? throw new InvalidOperationException("AREA_MOVEMENT_NO_HERO");
            var hereNow = HeroWorld();
            var fatigueBefore = Caster().CurrentFatigue(teleportHero);
            var teleportLog = "";
            for (var component = 0; component < 4; component++)
            {
                var caster = Caster();
                var spot = new SpellLocation(checked((uint)hereNow.X), checked((uint)hereNow.Y));
                var run = new SpellRun(0, 4, SpellRunAction.Begin, teleportHero, teleportHero, spot, teleportHero, spot, null, SpellRunFlags.Active);
                var logStart = caster.Log.Count;
                caster.Apply(run, component, new(teleportHero, spot));
                teleportLog += string.Join(",", caster.Log.Skip(logStart).Where(e => e.Kind is SpellCastEventKind.TeleportMap or SpellCastEventKind.Message).Select(e => $"{e.Kind}:{e.Value}"));
                FlushSpellLog(null);
            }
            GD.Print($"ARC-MAGIC-038 teleportation-cast log=[{teleportLog}] map-open={WorldMapOpen} mode={_teleportMode} status={_worldMapStatus}");
            Require(teleportLog == $"{SpellCastEventKind.TeleportMap}:4" || teleportLog == $"{SpellCastEventKind.Message}:{SessionSpellCaster.TeleportRefusedMessage}",
                "the cast logged the teleport request (or the refusal of a banned place): " + teleportLog);
            if (teleportLog.StartsWith(nameof(SpellCastEventKind.TeleportMap), StringComparison.Ordinal))
            {
                Require(_teleportsForSelfTest.Contains("map open spell 4"), "the viewer took the request: " + string.Join(";", _teleportsForSelfTest));
                Require(WorldMapOpen && _teleportMode is { Spell: 4 }, "the world map opened in its teleport mode: " + _worldMapStatus);
                Require(HeroWorld() == hereNow && Caster().CurrentFatigue(teleportHero) == fatigueBefore, "opening the map moves and pays nothing");
                CloseWorldMapByPlayer();
                for (var i = 0; i < 4; i++) Frame();
                Require(!WorldMapOpen && _teleportMode is null, "closing the map ends the teleport mode");
                Require(HeroWorld() == hereNow && Caster().CurrentFatigue(teleportHero) == fatigueBefore, "a map closed with no pick pays nothing and moves nothing");
            }
            else Require(!WorldMapOpen, "a refused teleport leaves the world map closed");

            GD.Print("ARC-MAGIC-038 teleport checked: " + string.Join(";", _teleportsForSelfTest));

            WorldRouteClick(Screen(start));
            Require(HeroRunsHost()?.WalkRun is not null, $"a click walks the hero again: {_worldRouteClick}");

            GD.Print("ARC-MAGIC-036 native-smoke movement PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName ?? "crash-site", From = startWorld.ToString(), Far = farWorld.ToString(),
                Walked = expected.Count, Landed = landed.ToString(), Parity = "NOT_VERIFIED (rules read in the original executable, MV1–MV3; no original-runtime run)" }));
        }
        finally
        {
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
