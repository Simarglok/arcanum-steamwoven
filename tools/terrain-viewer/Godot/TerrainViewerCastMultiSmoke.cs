using Arcanum.Data.Area;
using Arcanum.Data.Magic;
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
    // ARC-DEV-250 (B12, smoke plan SM08) native observation (stage "cast-multi", only when named, map mode with --hero, AI on, the elven ruins): a damage spell that hits every critter within its radius of the
    // target is cast at one of three animals standing within that radius of it; the AI moves them, so they have usually moved by the time the missile arrives (the stage requires that at least one has).
    // The kit weapon is lent the data's lowest-rank damage spell with a missile and an object-and-radius area (the hero's pre-made kit does not train its college), and the hero casts it through the weapon's pane, the cursor and
    // the click, and the stage watches the cast host's log: every animal the spell hits is hit once and has an effect added on it (the effect is on the object, so it is drawn at the animal's cell at the hit, not at its first cell),
    // an animal the spell did not hit has none, and at least two of the three are hit. The area rule decides who stands in the radius (its centre is the cast's tile); the stage records the animals' distances at the click and
    // at the hit and does not restate the rule. The spell and the animals are named by the data, not by this stage.
    private async Task RunAreaCastMultiSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-DEV-250 native-smoke cast-multi SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-250 cast-multi smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CAST_MULTI_SMOKE_FAILED");
        }
        for (var i = 0; i < 600 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_spellRecords is not null, "spell data " + _spellData?.Status + " " + _spellData?.Detail);
        var records = _spellRecords!;
        // The lowest-rank damage spell with a missile whose BEGIN area is an object with a radius of two or more.
        var spell = records.Where(r => r is not null && r.Data.Number < SpellRecord.CollegeSpells && r.Data.Cost > 0 && r.Data.Enabled && r.Areas is { Length: > 0 }
                && (r.Areas[0].Targets & 0x14) == 0x14 && r.Areas[0].Radius >= 2 && !SpellUiRules.CastsAtOnce(r.Areas[0].Targets) && SpellMissileArt(r.Data.Number) is not null
                && r.Of(SpellRunAction.Begin).Any(c => c.Type == SpellComponentType.Damage && c.Damage!.Maximum > 0))
            .OrderBy(r => r.Data.Number % SpellRecord.Ranks).ThenBy(r => r.Data.Number).FirstOrDefault();
        if (spell is null) { GD.Print("ARC-DEV-250 native-smoke cast-multi NONE (no damage spell with a missile and an object-and-radius area in the data)"); return; }
        var radius = spell.Areas[0].Radius;

        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var npcs = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key) && c.Row.Scope == AreaScope.Core && c.Row.WorldX is not null && c.Row.WorldY is not null)
            .OrderBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        static CellCoord WorldOf(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
        static int Distance(CellCoord a, CellCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        // The target and two animals within the radius of it (the nearer one stays, the farther one will leave).
        int[]? pick = null;
        for (var c = 0; c < npcs.Length && pick is null; c++)
        {
            var near = Enumerable.Range(0, npcs.Length).Where(n => n != c && Distance(WorldOf(npcs[n].Row), WorldOf(npcs[c].Row)) <= radius)
                .OrderBy(n => Distance(WorldOf(npcs[n].Row), WorldOf(npcs[c].Row))).ThenBy(n => npcs[n].Holder.Entity, StringComparer.Ordinal).ToArray();
            if (near.Length >= 2) pick = [c, near[0], near[1]];
        }
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        if (pick is null) { GD.Print($"ARC-DEV-250 native-smoke cast-multi NONE map={map.RuntimeMap} (no three NPCs within {radius} cells of one of them among {npcs.Length})"); return; }

        var group = (Target: npcs[pick[0]], Stays: npcs[pick[1]], Leaves: npcs[pick[2]]);
        // The hero's pre-made kit does not train the spell's college, so the wielded kit weapon is lent the spell (the cast smoke's weapon pane): the cast pays a charge, not fatigue.
        if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon) is not { Item.Weapon: not null } wielded
            || session.QueryHeroKit().All(k => k.Entity != wielded.Entity))
        { GD.Print("ARC-DEV-250 native-smoke cast-multi NONE (the hero wields no kit weapon to lend the spell)"); return; }
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var targetCell = map.Chart.ToChart(new(group.Target.Row.WorldX!.Value, group.Target.Row.WorldY!.Value));
            var start = await AreaSmokeFightableStartAsync(map, commands[group.Target.Row.Key], targetCell, 4, 3, false);
            if (start is null) { GD.Print($"ARC-DEV-250 native-smoke cast-multi NONE map={map.RuntimeMap} (no cell 3..4 cells from the target the hero can stand on and see it from)"); return; }
            AreaSmokePlace(map, start.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            foreach (var member in new[] { group.Target, group.Stays, group.Leaves })
                session.Heal(session.Version, member.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            void Frame()
            {
                _areaSmokeTime++;
                SpellSmokeTick();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var lastTile = new Dictionary<string, CellCoord>();
            CellCoord TileOf(string entity)
            {
                if (host!.Tile(entity) is { } tile) return lastTile[entity] = tile;
                Require(lastTile.TryGetValue(entity, out var known), "the AI host has no tile for " + entity); // an animal killed at the hit may lose its tile: the last known one stands
                return known;
            }

            // The cast through the weapon's pane, the cursor and the click at the target's drawn body.
            _lentItemSpellsForSelfTest = new(wielded.Entity, SessionSpellCaster.Hero, 14, [spell.Data.Number, 10000, 10000, 10000, 10000], -1, 1, ItemSpellPaneRules.IdentifiedFlag); _weaponPaneCache = null;
            InventoryKey(Key.M);
            Require(_spellPane?.WeaponShownForSelfTest is { ButtonShown: true, Open: false } shut && shut.Item == wielded.Entity, "M shows the weapon button: " + _spellPane?.WeaponShownForSelfTest);
            _spellPane!.PressWeaponForSelfTest();
            Require(_spellPane.WeaponShownForSelfTest is { Open: true } pane && pane.Buttons.Any(b => b.Slot == 0 && b.Spell == spell.Data.Number && b.Activates),
                $"the weapon's pane holds the lent spell: {string.Join(",", _spellPane.WeaponShownForSelfTest?.Buttons.Select(b => $"{b.Slot}:{b.Spell}:{b.Activates}") ?? [])}");
            _spellPane.PressItemForSelfTest(0);
            Require(_spellTargeting is { Item: { } source } && source == wielded.Entity && _spellTargeting.Spell == spell.Data.Number, "the slot opened the cursor with the weapon as the source: " + _spellNotice);
            var targetBody = AreaMovingCommands()?.FirstOrDefault(c => c.Identity == group.Target.Row.Key) ?? commands[group.Target.Row.Key];
            var point = await HoverPointAsync(this, targetBody) ?? throw new InvalidOperationException("ARC-DEV-250 smoke: the target is not visible");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.Cast, $"the cast hover {HoverActionForSelfTest}");
            var casts = _spellCasts;
            var logStart = _spellCaster!.Log.Count;
            var tTile = TileOf(group.Target.Holder.Entity); var sTile = TileOf(group.Stays.Holder.Entity); var lTile = TileOf(group.Leaves.Holder.Entity);
            _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            Require(_spellCasts == casts + 1, "the click cast: " + _spellNotice);

            // Until the damage lands (the first Damaged or Resisted event of the run), with the animals' tiles at that frame.
            string[] mine = [group.Target.Holder.Entity, group.Stays.Holder.Entity, group.Leaves.Holder.Entity];
            CellCoord? tHit = null, sHit = null, lHit = null; var hitFrame = -1;
            for (var frame = 0; frame < 1200 && hitFrame < 0; frame++)
            {
                Frame();
                if (_spellCaster.Log.Skip(logStart).Any(e => e.Kind is SpellCastEventKind.Damaged or SpellCastEventKind.Resisted && e.Object is { } o && mine.Contains(o)))
                { hitFrame = frame; tHit = TileOf(mine[0]); sHit = TileOf(mine[1]); lHit = TileOf(mine[2]); }
            }
            Require(hitFrame >= 0, $"the spell's damage landed within 1200 frames; log {string.Join(",", _spellCaster.Log.Skip(logStart).Select(e => e.Kind))}");
            for (var settle = 0; settle < 40; settle++) Frame();
            var log = _spellCaster.Log.Skip(logStart).ToArray();
            int Hits(string entity) => log.Count(e => e.Kind is SpellCastEventKind.Damaged or SpellCastEventKind.Resisted && e.Object == entity);
            int Effects(string entity) => log.Count(e => e.Kind == SpellCastEventKind.EyeCandyAdded && e.Object == entity);
            GD.Print($"ARC-DEV-250 cast-multi spell={spell.Data.Number} radius={radius} hitFrame={hitFrame} target {tTile.X},{tTile.Y}->{tHit!.Value.X},{tHit.Value.Y} stays {sTile.X},{sTile.Y}->{sHit!.Value.X},{sHit.Value.Y}"
                + $" leaves {lTile.X},{lTile.Y}->{lHit!.Value.X},{lHit.Value.Y} hits={Hits(mine[0])}/{Hits(mine[1])}/{Hits(mine[2])} effects={Effects(mine[0])}/{Effects(mine[1])}/{Effects(mine[2])}"
                + $" log={string.Join(",", log.Select(e => $"{e.Kind}:{(e.Object is { Length: > 6 } id ? id[^6..] : e.Object)}"))}");
            // The plan's pass: one effect per hit target, on the target itself (so at its cell at the hit, not at its first cell), none hit twice, and an effect on no animal the spell did not hit.
            // Which animals the radius takes is the area rule's (its centre is the cast's tile, and the animals' cells at the hit decide who stands in it); the stage records both distances and does not restate the rule.
            // Animals the area could take: those within the radius of the click's tile at the hit. With fewer than two of them (the animals ran out of it) the stage has not tested a multi-target hit.
            var inReach = new[] { tHit.Value, sHit.Value, lHit.Value }.Count(t => Distance(tTile, t) <= radius);
            if (inReach < 2) { GD.Print($"ARC-DEV-250 native-smoke cast-multi NONE map={map.RuntimeMap} (only {inReach} of three animals stood within {radius} cells of the click at the hit)"); return; }
            var tiles = new[] { (tTile, tHit.Value), (sTile, sHit.Value), (lTile, lHit.Value) };
            var moved = tiles.Count(t => t.Item1 != t.Item2);
            Require(moved >= 1, "an animal had moved between the cast and the hit");
            var hitAnimals = 0;
            for (var i = 0; i < 3; i++)
            {
                Require(Hits(mine[i]) <= 1, $"animal {i} was hit {Hits(mine[i])} times by one cast");
                if (Hits(mine[i]) == 1) { hitAnimals++; Require(Effects(mine[i]) >= 1, $"animal {i} was hit but has no effect on it"); }
                else Require(Effects(mine[i]) == 0, $"animal {i} was not hit but has {Effects(mine[i])} effects on it");
            }
            Require(Hits(mine[0]) == 1 && Effects(mine[0]) >= 1, $"the target was hit once with an effect on it ({Hits(mine[0])}/{Effects(mine[0])})");
            Require(hitAnimals >= 2, $"the spell hit {hitAnimals} of {inReach} animals standing within {radius} cells of the click's tile at the hit (a multi-target spell hits at least two)");
            var flipped = Enumerable.Range(1, 2).Count(i => (Distance(tTile, tiles[i].Item1) <= radius) != (Distance(tHit.Value, tiles[i].Item2) <= radius));
            GD.Print("ARC-DEV-250 native-smoke cast-multi PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Spell = spell.Data.Number, Radius = radius, HitFrame = hitFrame,
                Moved = moved, HitAnimals = hitAnimals, InsideRadiusChangedBetweenClickAndHit = flipped, Hits = $"{Hits(mine[0])}/{Hits(mine[1])}/{Hits(mine[2])}", Effects = $"{Effects(mine[0])}/{Effects(mine[1])}/{Effects(mine[2])}",
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            _lentItemSpellsForSelfTest = null; _weaponPaneCache = null;
            CancelSpellCast(null); _spellPane?.HidePane(); ClearSpellMissiles("the smoke ended"); _worldActor.TurnedFacing = null;
            HoverReleaseForSelfTest();
            foreach (var member in new[] { group.Target, group.Stays, group.Leaves })
                session.Heal(session.Version, member.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            RestoreHeroForSmoke(session, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
