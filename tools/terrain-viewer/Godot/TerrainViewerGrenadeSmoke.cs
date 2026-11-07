using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-249 (B14, smoke plan SM12) native observation (stage "grenade", only when named, map mode with --hero; the plan's map is "crash site cave", which lays grenades on the ground): a grenade of
    // the installed data thrown from the hero's pack explodes where its flight ends. The stage takes a map item whose spells carry the attacks-a-location trigger (0x800000), picks it up through
    // the inventory host, lifts it onto the game cursor and throws it at the tile of a living NPC that has another living NPC within two cells, from a cell three to six cells away with a clear
    // line. At the flight's end the item's event must start the item's spell (a run), the spell's damage must land on (or be resisted by) one of the two NPCs, and the grenade must be gone (not on the map,
    // not in the pack). A grenade whose spell has a damage is taken before a summoning one. NONE on a map without such a grenade or such a pair. Where the grenade lands is the flight's, not this stage's:
    // it is printed, not judged (the run printed a landing three cells from the aimed tile).
    private async Task RunAreaGrenadeSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area || _inventoryHost is null
            || _worldActor.Session is not AreaGridSession grid || AreaLineOfFireNow() is not { } lineMap)
        { GD.Print($"ARC-DEV-249 native-smoke grenade NONE map={_readyMapName} (no hero, combat values or attack map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-249 grenade smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_GRENADE_SMOKE_FAILED");
        }
        for (var i = 0; i < 600 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var records = SpellRecordsNow();
        Require(records is not null, "spell data " + _spellData?.Status + " " + _spellData?.Detail);
        var runtime = CurrentAreaMap;
        if (_inventoryHost.Map(runtime) is not { } inventory) { GD.Print($"ARC-DEV-249 native-smoke grenade NONE map={_readyMapName} (no inventory)"); return; }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));

        // The grenades: item rows of the map (on the ground or in a holder) whose spells carry the trigger.
        var ground = session.QueryMapInventory(new(runtime))!.Ground.Select(g => g.Item.Entity).ToHashSet(StringComparer.Ordinal);
        var grenades = area.Snapshot.Objects.Where(row => row.Type == 14 && row.Scope == AreaScope.Core && inventory.TryGetEntity(row.Key, out _))
            .ToArray();
        var found = new List<(AreaObjectRecord Row, string Entity, int Spell)>();
        foreach (var row in grenades)
            if (CombatFieldReader.Read(row).Fields is { Item: { } item } && inventory.TryGetEntity(row.Key, out var entity))
                foreach (var candidate in item.Spells.Where(s => s >= 0 && s < records!.Length && records[s] is { } r && (r.Data.ItemTriggers & 0x800000) != 0).Take(1))
                    found.Add((row, entity, candidate));
        found = [.. found.OrderByDescending(f => records![f.Spell]!.Of(SpellRunAction.Begin).Any(c => c.Type == SpellComponentType.Damage && c.Damage!.Maximum > 0)).ThenBy(f => f.Entity, StringComparer.Ordinal)];
        if (found.Count == 0) { GD.Print($"ARC-DEV-249 native-smoke grenade NONE map={_readyMapName} (no grenade item row with the trigger among {grenades.Length} item rows)"); return; }

        // The pair: a living NPC with combat values and another within two cells of it.
        var npcs = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
            .Select(row => _combatHost.Target(session, runtime, row.Key))
            .Where(t => t is { Dead: false, Follower: false, Build.Combat: not null } && session.QueryLife(t.Build.Combat!.Target).View?.Status.IsDead != true)
            .Select(t => t!).OrderBy(t => t.Entity, StringComparer.Ordinal).ToArray();
        static int Distance(CellCoord a, CellCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        (AreaCombatTarget A, AreaCombatTarget B)? pair = null;
        foreach (var a in npcs)
            if (npcs.FirstOrDefault(b => b.Entity != a.Entity && Distance(World(a.Row), World(b.Row)) <= 2) is { } b) { pair = (a, b); break; }
        if (pair is not { } chosen) { GD.Print($"ARC-DEV-249 native-smoke grenade NONE map={_readyMapName} (no two living NPCs within two cells of each other among {npcs.Length})"); return; }
        var aAt = World(chosen.A.Row);

        // A cell three to six cells from the first NPC with a clear line, that the hero can stand on and walk to.
        CellCoord? stand = null;
        for (var d = 3; d <= 6 && stand is null; d++)
        for (var dy = -d; dy <= d && stand is null; dy++)
        for (var dx = -d; dx <= d && stand is null; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
            var hero = new CellCoord(aAt.X + dx, aAt.Y + dy);
            if (!lineMap.Contains(hero)) continue;
            var trace = LineOfFireRules.Trace(hero, 0, 0, aAt, lineMap, session.Rules.Combat);
            if (trace.LineFailed) continue;
            var chart = map.Chart.ToChart(new(hero.X, hero.Y));
            if (map.Chart.Contains(chart) && ApproachCell(grid, aAt.X, aAt.Y, 6, cell => cell == chart) == chart) stand = hero;
        }
        if (stand is not { } standAt) { GD.Print($"ARC-DEV-249 native-smoke grenade NONE map={_readyMapName} (no clear line 3..6 cells from the first NPC)"); return; }

        var (row0, grenade, spellNumber) = found[0];
        var spell = records![spellNumber]!;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var scaled = false;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var aLife = chosen.A.Build.Combat!.Target; var bLife = chosen.B.Build.Combat!.Target;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            AreaSmokePlace(map, map.Chart.ToChart(new(standAt.X, standAt.Y)));
            scaled = true; _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            void Frame()
            {
                _areaSmokeTime++;
                SpellSmokeTick();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            session.Heal(session.Version, aLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            session.Heal(session.Version, bLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });

            // Into the pack, onto the cursor, thrown at the first NPC's tile.
            var holderOf = session.QueryMapInventory(new(runtime))!.Holders.FirstOrDefault(h => h.Items.Any(i => i.Entity == grenade))?.Entity;
            GD.Print($"ARC-DEV-249 grenade source item={grenade} proto={row0.PrototypeNumber} holder={holderOf ?? "ground"} onGround={ground.Contains(grenade)}");
            var taken = holderOf is null ? _inventoryHost.PickUp(session, grenade) : _inventoryHost.Loot(session, holderOf, grenade);
            Require(AreaInventoryHost.Moved(taken) && session.QueryPcInventory().Any(i => i.Entity == grenade), $"the hero picked the grenade up ({taken.Disposition} {taken.Failure})");
            SyncAreaInventory();
            Require(HoldItemOnGameCursor(grenade, "smoke") is null && GameCursorItemForSelfTest == grenade, $"the grenade is on the game cursor ({HeldThrowNoticeForSelfTest})");
            _projectileEndsForSelfTest.Clear(); _thrownLandingsForSelfTest.Clear(); _shotEndItemEventsForSelfTest.Clear(); _lastHeldThrowForSelfTest = null;
            var logStart = SpellCasterNow()?.Log.Count ?? 0;
            Require(ThrowGameCursorItem(aAt), $"the throw started ({HeldThrowNoticeForSelfTest})");
            for (var frames = 0; frames < 1500 && _lastHeldThrowForSelfTest is null && HeroRunsHost() is { ThrowRun: not null }; frames++) Frame();
            var thrown = _lastHeldThrowForSelfTest;
            Require(thrown is { Disposition: GameSessionDisposition.Changed }, $"the grenade was thrown ({thrown?.Disposition} {thrown?.Failure}, {HeldThrowNoticeForSelfTest})");
            for (var frames = 0; frames < 1500 && (_projectiles.Count > 0 || HeroRunsHost() is { ThrowRun: not null }); frames++) Frame();
            Require(_projectiles.Count == 0, "the flight ended");
            // The item's event starts the spell as a run, which the animation clock plays out.
            for (var frames = 0; frames < 400 && (SpellCasterNow()?.Log.Count ?? 0) == logStart; frames++) Frame();
            for (var frames = 0; frames < 200; frames++) Frame();
            var log = (SpellCasterNow()?.Log ?? []).Skip(logStart).ToArray();
            var end = _projectileEndsForSelfTest.LastOrDefault();
            var tile = _thrownLandingsForSelfTest.Count > 0 ? _thrownLandingsForSelfTest[0].Tile : default;
            var events = _shotEndItemEventsForSelfTest.ToArray();
            GD.Print($"ARC-DEV-249 grenade throw item={grenade} spell={spellNumber} triggers=0x{spell.Data.ItemTriggers:X} strike={end.Strike} landed={tile.X},{tile.Y} target={aAt.X},{aAt.Y} events={events.Length}"
                + $" started={string.Join(",", events.Select(e => e.Started))} log={string.Join(",", log.Select(e => $"{e.Kind}:{(e.Object is { Length: > 6 } id ? id[^6..] : e.Object)}:{e.Value}"))}");
            var placed = session.QueryPlacedItems().Any(p => p.Entity == grenade); var destroyed = session.IsItemDestroyed(grenade); var inPack = session.QueryPcInventory().Any(i => i.Entity == grenade);
            var hurt = log.Where(e => e.Kind is SpellCastEventKind.Damaged or SpellCastEventKind.Resisted && e.Object is not null && e.Object != SessionSpellCaster.Hero).Select(e => e.Object!).Distinct().ToArray();
            Require(events.Length >= 1 && events.Any(e => e.Started > 0), $"the grenade's item event started a spell run (events {events.Length}, started {string.Join(",", events.Select(e => e.Started))})");
            Require(hurt.Contains(chosen.A.Entity) || hurt.Contains(chosen.B.Entity), $"the grenade's spell hit none of the two NPCs near the aimed tile (hit {hurt.Length} other creatures); log {string.Join(",", log.Select(e => e.Kind))}");
            GD.Print($"ARC-DEV-249 grenade stood={standAt.X},{standAt.Y} landed={tile.X},{tile.Y} aimed={aAt.X},{aAt.Y} hurt={hurt.Length}");
            Require(!placed && !inPack && destroyed, $"the grenade is gone after the explosion (placed {placed}, in the pack {inPack}, destroyed {destroyed})");
            GD.Print("ARC-DEV-249 native-smoke grenade PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                RuntimeMap = runtime, Item = row0.PrototypeNumber, Spell = spellNumber, Strike = end.Strike.ToString(), Stood = $"{standAt.X},{standAt.Y}", Landed = $"{tile.X},{tile.Y}", Aimed = $"{aAt.X},{aAt.Y}",
                Creatures = hurt.Length, Damaged = log.Count(e => e.Kind == SpellCastEventKind.Damaged), Resisted = log.Count(e => e.Kind == SpellCastEventKind.Resisted),
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelSpellCast(null); ClearSpellMissiles("the smoke ended");
            session.Heal(session.Version, aLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            session.Heal(session.Version, bLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            RestoreHeroForSmoke(session, heroBefore);
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            if (GameCursorItemForSelfTest == grenade) _gameCursorItem = null;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
