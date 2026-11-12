using Arcanum.Data.Objects;
using Arcanum.Session;
using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-WORLD-1159 (E2-c) native observation (stage "encounters", only when named, with --hero, start map, a sector in no named area and with no town map: the wilderness 1384,239). The module's real tables are read, a
    // critter prototype of one of their rows is taken, and a synthetic table set (a 100 per cent chance everywhere, power "easy", one table with one row of three of that critter) takes their place so that the draw
    // cannot miss. Four forced rolls (the clock is jumped to the armed event's deadline; the frame pass fires it): (1) plain: the critters stand on the ring spots of 0x5594E0 next to the hero, are the session's created
    // critters, are hosted by the AI host and have their goods generated, the row's use count rose and the event re-armed; (2) with the rest window open: the roll takes the rest chance and the window closes (0x57B450);
    // (3) with the world map open: the window closes, its opener is refused for 4,000 real milliseconds (0x560720, message 602) and opens after them; (4) a journey: the roll falls during the first step, at the marker, the
    // journey ends in a landing where the marker stood and the critters stand around it and are hosted there. The clock and the hero's place stay where the stage leaves them (the stage runs in its own process).
    private async Task RunAreaEncountersSmokeAsync()
    {
        const string Tag = "ARC-WORLD-1159";
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print(Tag + " encounters smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_ENCOUNTERS_SMOKE_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _createdPrototypeRead is not { } read || _inventoryHost is null)
        { GD.Print(Tag + " native-smoke encounters NONE map=" + _readyMapName + " (needs --hero, an area session, the inventory and the created-prototype reader)"); return; }
        for (var i = 0; i < 1200 && _worldMapData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptWorldMapData(); }
        if (_worldMapData is not { Status: AreaWorldMapDataStatus.Ready, Data: { } data }) { GD.Print($"{Tag} native-smoke encounters NONE (world-map data {_worldMapData?.Status} {_worldMapData?.Detail})"); return; }
        if (area.Snapshot.RuntimeMap != data.StartRuntime) { GD.Print($"{Tag} native-smoke encounters SKIP (map {_readyMapName} is not the start map)"); return; }
        if (data.Encounters.Status != EncounterTablesStatus.Loaded) { GD.Print($"{Tag} native-smoke encounters NONE (the module's tables: {data.Encounters.Detail})"); return; }
        if (EncounterSiteNow() is null) { GD.Print($"{Tag} native-smoke encounters NONE (the hero stands in a named area)"); return; }
        var hero = PcCell();
        var map = new SessionMapId(CurrentAreaMap);
        // A critter prototype of the module's own rows (the file number as the tables hold it).
        var prototype = data.Encounters.Tables.SelectMany(rows => rows).SelectMany(row => row.Groups).Select(group => group.Prototype)
            .FirstOrDefault(number => CreatedObjectRows.Critter(read, number, SessionCreatedObject.CritterPrefix + "1", CurrentAreaMap, 0, 0).Row is not null);
        if (prototype == 0) { GD.Print($"{Tag} native-smoke encounters NONE (no row of the module's tables names a critter prototype that reads)"); return; }
        // The synthetic tables: every chart is one circle round the hero that covers the whole map window; table 0 holds one row.
        var circle = $"{hero.X}, {hero.Y}, 4000, ";
        var tables = EncounterTables.Parse([(EncounterTables.FrequencyBase, circle + "100"), (EncounterTables.PowerBase, circle + "easy"), (EncounterTables.OverrideBase, circle + "0"),
            (EncounterTables.TableCountLine, "0"), (EncounterTables.TableBase, $"100, First:{prototype}, 3-3")]);
        Require(tables.Status == EncounterTablesStatus.Loaded, "the synthetic tables read: " + tables.Detail);
        var aiBefore = _aiForSmoke; var encounterHostBefore = _encounterHost; var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        // As the world-map stage has it: an automated run has no window focus, so the keys of the windows would be ignored.
        _aiForSmoke = true; _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        async Task Pass(int frames = 1)
        {
            for (var i = 0; i < frames; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); TickAreaClock((long)Time.GetTicksMsec()); ProcessAreaAi(); SyncAreaInventory(); }
        }
        long Now() => session.QueryClockSnapshot().GameTimestampMs;
        // The next Random Encounters deadline; the clock is jumped to it less `lead` (the event comes due in the frame pass that follows).
        long Deadline()
        {
            var deadlines = session.QueryRandomEncounterDeadlines();
            Require(deadlines.Count == 1, "one Random Encounters event is pending: " + string.Join(",", deadlines));
            return deadlines[0];
        }
        void JumpToDeadline(long leadMs)
        {
            var jump = Deadline() - leadMs - Now();
            if (jump <= 0) return;
            Require(session.JumpClocks(session.QueryClockSnapshot().Token, jump).Disposition != Arcanum.Simulation.Clock.ClockMutationDisposition.Rejected, "the clock jumped to the deadline");
        }
        var firedBefore = 0;
        async Task<SessionEncounterNotice> Fire(string label, long leadMs = 0)
        {
            firedBefore = _encountersFired;
            JumpToDeadline(leadMs);
            for (var i = 0; i < 60 && _encountersFired == firedBefore; i++) await Pass();
            Require(_encountersFired == firedBefore + 1 && _lastEncounterNotice is { Outcome: SessionEncounterOutcome.Fired } fired, $"{label}: a forced roll fired (outcome {_lastEncounterNotice?.Outcome} roll {_lastEncounterNotice?.Roll})");
            return _lastEncounterNotice!;
        }
        async Task Settle(int frames = 6) => await Pass(frames);
        var made = new List<SessionCreatedObject>();
        async Task DestroyMade()
        {
            foreach (var critter in made) if (session.QueryCreatedObject(critter.Entity) is not null) Require(session.DestroyCreatedObject(session.Version, critter.Entity).Disposition == GameSessionDisposition.Changed, "a made critter is destroyed");
            made.Clear();
            await Settle(2);
        }
        try
        {
            _encounterHost = (data, new AreaEncounterHost(tables, EncounterSiteNow));
            for (var wait = 0; wait < 600 && AreaAiHostNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaAiHostNow() is not null, "the AI host is built");
            await Pass(2);
            Require(session.QueryRandomEncounterDeadlines().Count == 1, "the first event pass armed the event");
            var armed = Deadline() - Now();
            Require(armed is >= 300 * EncounterRules.MinuteMs and <= 700 * EncounterRules.MinuteMs, $"armed 300 to 700 game minutes ahead: {armed / EncounterRules.MinuteMs} min");
            var existing = session.QueryCreatedObjects(map).Select(o => o.Entity).ToHashSet(StringComparer.Ordinal);

            // ---- (1) plain: the critters on the ring spots ----
            var first = await Fire("plain");
            Require(first.Table == 0 && first.Row == EncounterTables.TableBase && first.Made.Count + first.Lost == 3 && first.Made.Count >= 1, $"table 0, row 50000, three creatures rolled: made {first.Made.Count} lost {first.Lost} table {first.Table} row {first.Row}");
            Require(first.Rearmed && session.QueryRandomEncounterDeadlines().Count == 1 && Deadline() > Now(), "the event re-armed itself");
            made.AddRange(first.Made);
            var location = first.Place!.Value.Location;
            Require(location.X == hero.X && location.Y == hero.Y, $"the roll's place is the hero's tile (the map is closed): {location}");
            var now = session.QueryCreatedObjects(map).Where(o => !existing.Contains(o.Entity)).ToArray();
            Require(now.Length == first.Made.Count && now.All(o => o.Kind == SessionCreatedKind.Critter && o.Prototype == prototype), "the session holds exactly the made critters of the prototype");
            Require(first.Made.All(o => Math.Abs(o.Cell.X - hero.X) <= 2 * EncounterRules.Ring && Math.Abs(o.Cell.Y - hero.Y) <= 2 * EncounterRules.Ring), $"each stands near the ring spots: {string.Join(";", first.Made.Select(o => $"{o.Cell.X},{o.Cell.Y}"))} (hero {hero.X},{hero.Y})");
            Require(session.QueryEncounterUses().Any(u => u.Line == EncounterTables.TableBase && u.Count == 1), "the row's use count rose");
            await Settle();
            var host = AreaAiHostNow()!;
            Require(first.Made.All(o => host.Scene.Npcs.Any(n => n.Entity == o.Entity)), "the AI host holds each made critter");
            for (var wait = 0; wait < 900 && _createdInventoryPending.Count > 0; wait++) await Settle(1);
            Require(_createdInventoryPending.Count == 0, "the made critters' goods were taken (the restock host is ready)");
            GD.Print($"{Tag} encounters plain made={first.Made.Count} lost={first.Lost} prototype={prototype} at=({location.X},{location.Y}) cells={string.Join(";", first.Made.Select(o => $"{o.Cell.X},{o.Cell.Y}"))} west={(first.Made.Count > 0 ? (first.Made[0].Cell.X < hero.X).ToString() : "-")}");
            await DestroyMade();

            // ---- (2) the rest window open: the roll takes the rest chance and the window closes ----
            OpenSleepForSelfTest();
            if (!SleepOpenForSelfTest) { GD.Print($"{Tag} native-smoke encounters NONE (the rest window did not open: {_sleepStatus ?? "no notice"})"); return; }
            var second = await Fire("rest");
            Require(second.Place!.Value.RestWindowOpen && !SleepOpenForSelfTest, $"the rest window was open at the roll and is closed after it: open={SleepOpenForSelfTest}");
            made.AddRange(second.Made);
            GD.Print($"{Tag} encounters rest made={second.Made.Count} lost={second.Lost} window-closed={!SleepOpenForSelfTest}");
            await DestroyMade();

            // ---- (3) the world map open: it closes and refuses to open for 4 s ----
            InventoryKey(Key.W);
            Require(WorldMapOpen && _worldMap is { Travelling: false }, "W opened the world map: " + _worldMapStatus);
            var third = await Fire("map");
            Require(!WorldMapOpen && !WorldModal, "the world map closed with the roll");
            var lockedUntil = WorldMapLockedUntilForSelfTest;
            Require(lockedUntil > 0, "the opener is locked");
            made.AddRange(third.Made);
            // The made critters are gone before the wait (they would fight the hero, and a downed hero's window opener refuses).
            await DestroyMade();
            InventoryKey(Key.W);
            Require(!WorldMapOpen && (_worldMapStatus ?? "").Contains("602", StringComparison.Ordinal), "W was refused with message 602: " + _worldMapStatus);
            for (var i = 0; i < 1200 && _clockHostMs <= lockedUntil; i++) await Pass();
            Require(_clockHostMs > lockedUntil, "the lock ran out");
            InventoryKey(Key.W);
            Require(WorldMapOpen, "W opens after the 4,000 ms: " + _worldMapStatus);
            GD.Print($"{Tag} encounters map made={third.Made.Count} lock-ms={WorldMapEncounterLockMs}");

            // ---- (4) a journey: the roll falls at the first step, at the marker, and the landing is there ----
            var worldMap = _worldMap!;
            var here = WorldTravelGeometry.Location(worldMap.Position, data.Width);
            var target = 0;
            foreach (var candidate in Enumerable.Range(1, data.TravelAreas.Count - 1).Where(a => data.TravelAreas[a].Radius > 0 && data.TravelAreas[a].Location != default
                && WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here) > data.TravelAreas[a].Radius).OrderBy(a => WorldTravelGeometry.Distance(data.TravelAreas[a].Location, here)))
            {
                if (worldMap.AddArea(candidate) != AreaWorldMapRefusal.None) continue;
                target = candidate; break;
            }
            Require(target > 0, "an area plans from here");
            RenderWorldMap(null);
            var (steps, _) = worldMap.Estimate();
            Require(steps >= 2, $"the journey has at least two steps: {steps}");
            // The deadline falls inside the first step's hour.
            JumpToDeadline(WorldJourney.MsPerSector / 2);
            firedBefore = _encountersFired;
            InventoryKey(Key.Enter);
            Require(worldMap.Travelling, "Enter started the journey");
            for (var i = 0; i < 600 && _encountersFired == firedBefore && WorldMapOpen; i++) await Pass();
            Require(_encountersFired == firedBefore + 1 && _lastEncounterNotice is { Outcome: SessionEncounterOutcome.Fired }, $"the roll fell during the journey: {_lastEncounterNotice?.Outcome} {_lastEncounterNotice?.Roll}");
            var fourth = _lastEncounterNotice!;
            made.AddRange(fourth.Made);
            var marker = fourth.Place!.Value.Location;
            Require(marker.X != hero.X || marker.Y != hero.Y, $"the roll's place is the marker, not the hero's tile: {marker}");
            // The journey ends in a landing where the marker stood.
            for (var i = 0; i < 36000 && (WorldMapOpen || _mapHandoff is not null); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!WorldMapOpen && _mapHandoff is null && ReferenceEquals(_areaSession, session), "the journey ended in a landing and the session is carried");
            Require(session.QueryRealStep(SessionRealStep.WorldMapStep) is null, "no travel step is left pending");
            var actor = session.QueryActor();
            var off = Math.Max(Math.Abs(actor.Cell.X - marker.X), Math.Abs(actor.Cell.Y - marker.Y));
            Require(actor.Map.RuntimeMap == data.StartRuntime && off <= 64, $"the actor stands {off} tiles from the marker (landing where the marker stood)");
            Require(WorldMapLockedUntilForSelfTest > 0, "the opener is locked after the roll");
            Require(fourth.Made.All(o => Math.Abs(o.Cell.X - marker.X) <= 2 * EncounterRules.Ring && Math.Abs(o.Cell.Y - marker.Y) <= 2 * EncounterRules.Ring), $"the critters stand round the marker: {string.Join(";", fourth.Made.Select(o => $"{o.Cell.X},{o.Cell.Y}"))} (marker {marker.X},{marker.Y})");
            // The window of the landing holds them: each is a created critter of the session and the AI host hosts it there.
            for (var wait = 0; wait < 600 && AreaAiHostNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Settle(12);
            var landed = AreaAiHostNow();
            Require(landed is not null && fourth.Made.All(o => session.QueryCreatedObject(o.Entity) is not null), "the made critters are still the session's");
            var hosted = fourth.Made.Count(o => landed!.Scene.Npcs.Any(n => n.Entity == o.Entity));
            Require(hosted == fourth.Made.Count, $"the AI host of the landing's window holds the made critters: {hosted} of {fourth.Made.Count}");
            GD.Print($"{Tag} encounters journey made={fourth.Made.Count} marker=({marker.X},{marker.Y}) actor-off={off} hosted={hosted}");
            GD.Print("ARC-WORLD-1159 native-smoke encounters PASS " + System.Text.Json.JsonSerializer.Serialize(new { Prototype = prototype, Armed = armed / EncounterRules.MinuteMs, Plain = first.Made.Count, Rest = second.Made.Count, Map = third.Made.Count, Journey = fourth.Made.Count, Target = target, Steps = steps, Off = off }));
        }
        catch (Exception exception) when (exception.Message != "AREA_ENCOUNTERS_SMOKE_FAILED")
        {
            GD.Print(Tag + " encounters smoke FAIL: unexpected " + exception.GetType().Name + " at " + (exception.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim());
            throw;
        }
        finally
        {
            if (WorldMapOpen) EndWorldMap();
            if (SleepOpenForSelfTest) EndSleep();
            _encounterHost = encounterHostBefore; _aiForSmoke = aiBefore; _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
