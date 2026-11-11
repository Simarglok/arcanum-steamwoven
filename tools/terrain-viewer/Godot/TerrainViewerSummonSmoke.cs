using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>The spells of the summon stages (ARC-RES-101): Fireflash and Wall of Fire, and their pieces' prototype files (basic + 20).</summary>
    private const int SummonFlashSpell = 22, SummonWallSpell = 21, SummonFlashFile = 4070, SummonWallFile = 4051;

    /// <summary>
    /// ARC-DEV-352 (E1-b) native observation (stages "summon-flash" and "summon-wall", only when named, with --hero, on a map window with the inventory active): the hero's own run of the module's spell record, started
    /// free through the session and begun through the cast host (the casting goal and the UI are other stages'), on the real map, prototype files, eye-candy table and art.
    /// <b>summon-flash</b> (spell 22): on an empty tile 3 cells from the hero the flash (file 4070) is created, summoned and drawn, the eye candy plays on it and the run waits; the animation's callback (goal 47's action frame) destroys
    /// it within 3 s and the run ends; the same cast at the hero's own tile (a critter stands there) makes no flash and the run ends at once (SUM9, SUM12).
    /// <b>summon-wall</b> (spell 21): cast at the hero's tile, up to five pieces (file 4051) along a line through it, each drawn and looping as goal 77; the hero stands on the centre piece and is spared (its summoner, GT94) while
    /// the burn steps run; the run's interrupt (the maintenance stopped) ends it and its END callback destroys every piece at once; then a piece made with no summoner on the hero's tile burns the hero (a placed fire burns everyone).
    /// The hero's life and the map are left as they were.
    /// </summary>
    private async Task RunAreaSummonSmokeAsync(int spell)
    {
        var stage = spell == SummonFlashSpell ? "summon-flash" : "summon-wall";
        // The module's content (prototypes, extra art, spell data) opens after the map: wait for it.
        for (var wait = 0; wait < 1800 && (ExtraArt() is null || _createdPrototypeRead is null || !InventoryActive || SpellRecordsNow() is null); wait++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || ExtraArt() is null || _createdPrototypeRead is null)
        {
            GD.Print($"ARC-DEV-352 native-smoke {stage} SKIP (needs --hero, an area session, the inventory and the module's content: session={_areaSession is not null} hero={_areaSession?.QueryHero() is not null} "
                + $"area={_readyArea is not null} inventory={InventoryActive} prototypes={_createdPrototypeRead is not null} art={ExtraArt() is not null} spells={_spellData?.Status})");
            return;
        }
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"ARC-DEV-352 {stage} smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SUMMON_SMOKE_FAILED");
        }
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var map = new SessionMapId(CurrentAreaMap);
        // The burn finds its victims through the AI host's critters (the wall only); the flash needs none.
        var aiBefore = _aiForSmoke;
        if (spell == SummonWallSpell) _aiForSmoke = true;
        var made = new List<string>();
        var logStart = SpellCasterNow()?.Log.Count ?? 0;
        try
        {
            PortalSmokeClockStart();
            for (var wait = 0; wait < 600 && SpellCasterNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(SpellCasterNow() is not null && SpellRecordsNow() is { } loaded && spell < loaded.Length && loaded[spell] is not null, "the cast host and the spell's record (spell data " + _spellData?.Status + ")");
            var caster = SpellCasterNow()!;
            var record = SpellRecordsNow()![spell]!;
            var summon = record.Of(SpellRunAction.Begin).SingleOrDefault(c => c.Type == SpellComponentType.Summon)?.Summon;
            Require(summon is not null && summon.Prototype + SessionSpellCaster.PrototypeFileOffset == (spell == SummonFlashSpell ? SummonFlashFile : SummonWallFile),
                $"the record's one Summon names the expected prototype: {summon?.Prototype}");
            var facts = SummonPrototypeOf(summon!.Prototype + SessionSpellCaster.PrototypeFileOffset);
            GD.Print($"ARC-DEV-352 {stage} prototype={summon.Prototype + SessionSpellCaster.PrototypeFileOffset} type={facts?.Type} flags=0x{facts?.ObjectFlags ?? 0:X} scenery-flags=0x{facts?.SceneryFlags ?? 0:X} blit=0x{CreatedPrototype(summon.Prototype + SessionSpellCaster.PrototypeFileOffset)?.Blit ?? 0:X}/{CreatedPrototype(summon.Prototype + SessionSpellCaster.PrototypeFileOffset)?.BlitAlpha ?? 0}");
            Require(facts is { Type: SessionSpellCaster.SceneryObjectType } && (facts.ObjectFlags & SpellTargetObject.NoBlock) != 0, "the piece is a scenery with OF_NO_BLOCK (SUM11)");
            Require(HeroPoolPlace() is not null, "the hero has a committed cell");
            var heroCell = HeroPoolPlace()!.Value.Cell;
            if (spell == SummonWallSpell) Require(AreaAiHostNow() is not null, "the AI host is built (the burn's critters)");

            void Step(int frames = 1)
            {
                for (var i = 0; i < frames; i++)
                {
                    TickAreaClock(_portalSmokeHostMs += 50);
                    ProcessAreaEyeCandy();
                    ProcessEyeCandyCallbacks();
                    ProcessAreaSceneryAnimation();
                    SyncAreaInventory();
                }
            }
            int Cast(CellCoord tile)
            {
                // The hero is the parent (the summoner, SUM1) and the run has no source: the stage's intelligence and willpower checks and the payments are a source critter's (the pre-made heroes' willpower is below these
                // spells' minimum, which a real cast would be refused for), and a sourceless maintained run ticks on by itself.
                var hero = caster.Facts(SessionSpellCaster.Hero)!;
                var started = session.StartSpellRun(session.Version, new(new(spell, hero, null, null, new((uint)tile.X, (uint)tile.Y)), Free: true), record.Run, caster.InvocationWorld(), session.SpellRolls);
                Require(started.Disposition == GameSessionDisposition.Changed && started.Creation?.Run is not null, $"the run starts: {started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
                var run = started.Creation!.Run!.Id;
                Require(caster.Act(new(SessionSpellRunActionKind.Begin, run)).Disposition == GameSessionDisposition.Changed, "the BEGIN runs");
                FlushSpellLog(null);
                return run;
            }
            GD.Print($"ARC-DEV-352 {stage} hero={heroCell.X},{heroCell.Y} lines=" + string.Join(" ", record.Components.Select(c => $"{c.Action}/{c.Index}:{c.Type}:0x{c.Area.Targets:X}/r{c.Area.Radius}/c{c.Area.Count}:0x{c.ApplyTargets:X}")) + " areas=" + string.Join(" ", record.Areas.Select(a => $"0x{a.Targets:X}/r{a.Radius}/c{a.Count}")));
            IReadOnlyList<SessionCreatedObject> Mine(int run) => session.QueryCreatedObjects(map).Where(o => o.Run == run).ToArray();
            bool Drawn(string entity) => _groundBodies.ContainsKey(PlacedKey(area.Snapshot.RuntimeMap, entity));

            if (spell == SummonFlashSpell)
            {
                CellCoord? strike = null;
                int run = -1;
                // An empty tile a few cells from the hero: the flash appears there (a tile the data or a critter fills gives none; the next is tried).
                foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (3, 3), (-3, -3), (3, -3), (-3, 3) })
                {
                    var tile = new CellCoord(heroCell.X + dx, heroCell.Y + dy);
                    var at = caster.Log.Count;
                    run = Cast(tile);
                    var flash = Mine(run).SingleOrDefault();
                    if (flash is null)
                    {
                        GD.Print($"ARC-DEV-352 summon-flash tile={tile.X},{tile.Y} no flash: " + string.Join(" ", caster.Log.Skip(at).Select(e => $"{e.Kind}:{e.Value}:{e.Object}:{e.Detail}")));
                        Step(2);
                        continue;
                    }
                    made.Add(flash.Entity);
                    strike = tile;
                    Require(flash.Prototype == SummonFlashFile && flash.Cell == tile && flash.SpellFlags == SessionCreatedObject.SummonedFlag && flash.Summoner == SessionSpellCaster.Hero, "the flash: file 4070, at the strike tile, summoned by the hero");
                    Require(caster.Log.Skip(at).Any(e => e.Kind == SpellCastEventKind.EyeCandyAdded && e.Object == flash.Entity), "the eye candy is re-aimed to the flash");
                    Require(session.QuerySpellRuns()[run] is not null, "the run waits for the eye candy's callback");
                    Step(1);
                    Require(Drawn(flash.Entity), "the flash is drawn at its cell");
                    Require(ShownEyeCandyHost?.Shown().Any(o => o.Owner == flash.Entity) == true, "the flash's eye candy plays on it");
                    var frames = 0;
                    for (; frames < 60 && session.QueryCreatedObject(flash.Entity) is not null; frames++) Step();
                    GD.Print($"ARC-DEV-352 summon-flash flash={flash.Entity} tile={tile.X},{tile.Y} gone-after={frames * 50} ms run-alive={session.QuerySpellRuns()[run] is not null}");
                    Require(session.QueryCreatedObject(flash.Entity) is null, "the callback destroyed the flash within 3 s");
                    Step(2);
                    Require(session.QuerySpellRuns()[run] is null, "the run ended after its callback");
                    Require(!Drawn(flash.Entity) && ShownEyeCandyHost?.Shown().Any(o => o.Owner == flash.Entity) != true, "neither the flash nor its eye candy is drawn");
                    break;
                }
                Require(strike is not null, "an empty tile near the hero took a flash");
                // The hero's own tile is not empty (a PC blocks): no flash, and the run ends at once.
                var occupied = Cast(heroCell);
                Require(Mine(occupied).Count == 0 && !caster.Log.Any(e => e.Kind == SpellCastEventKind.Summoned && e.Run == occupied && e.Object is { } o && session.QueryCreatedObject(o) is not null), "no flash on the hero's tile (SUM9)");
                Step(2);
                Require(session.QuerySpellRuns()[occupied] is null, "the run without a flash ended at once");
                GD.Print("ARC-DEV-352 native-smoke summon-flash PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, Strike = $"{strike!.Value.X},{strike.Value.Y}",
                    Occupied = $"{heroCell.X},{heroCell.Y}", Parity = "NOT_VERIFIED (rules read in the original executable, ARC-RES-101; the owner compares in game)" }));
                return;
            }

            // Wall of Fire at the hero's tile: the hero stands on the centre piece.
            var wall = Cast(heroCell);
            var pieces = Mine(wall);
            made.AddRange(pieces.Select(p => p.Entity));
            GD.Print($"ARC-DEV-352 summon-wall pieces={pieces.Count} cells={string.Join(";", pieces.Select(p => $"{p.Cell.X},{p.Cell.Y}"))}");
            Require(pieces.Count is >= 1 and <= 5 && pieces.All(p => p.Prototype == SummonWallFile && p.Summoner == SessionSpellCaster.Hero && p.SpellFlags == SessionCreatedObject.SummonedFlag),
                "one to five summoned pieces of file 4051 (fewer only where a blocking immobile or terrain stands)");
            Require(pieces.Any(p => p.Cell == heroCell), "a piece stands on the hero's tile (critters do not stop one)");
            var dxs = pieces.Select(p => p.Cell.X - heroCell.X).ToArray(); var dys = pieces.Select(p => p.Cell.Y - heroCell.Y).ToArray();
            Require(pieces.All(p => Math.Max(Math.Abs(p.Cell.X - heroCell.X), Math.Abs(p.Cell.Y - heroCell.Y)) <= 2), "the pieces lie within two steps of the centre");
            Require(dxs.Distinct().Count() == 1 || dys.Distinct().Count() == 1 || dxs.Zip(dys).All(d => d.First == d.Second) || dxs.Zip(dys).All(d => d.First == -d.Second), "the pieces lie on one line");
            Step(2);
            var host = ShownSceneryHost;
            Require(host is not null && pieces.All(p => host.Owns(p.Entity) && host.GoalOf(p.Entity) == GoalType.AnimateLoopFireDamage), "every piece loops as goal 77 (OSCF_IS_FIRE)");
            Require(pieces.All(p => Drawn(p.Entity)), "every piece is drawn");
            // ARC-MAGIC-054: the piece takes its prototype's additive blit (0x10) as a map body does: drawn additive and unlit.
            Require(pieces.All(p => _groundBodies.TryGetValue(PlacedKey(area.Snapshot.RuntimeMap, p.Entity), out var drawn) && drawn is { Additive: true, Unlit: true, Multiply: false, Alpha: 255 }), "every piece is drawn additive and unlit (blit 0x10)");
            Require(host!.CreatedSource(pieces[0].Entity) == SessionSpellCaster.Hero, "the burn's source is the summoner");
            var frame0 = pieces.Select(p => host.CreatedFrame(p.Entity)).ToArray();
            var fireBefore = host.FireFrames;
            var hpBefore = session.QueryHeroLife()!.Status.HitPoints;
            // ARC-DEV-405: the stage runs after others in one process, and the hero's hit points can differ between the two reads for a reason that is not the wall (possibly a timed effect or recovery an earlier
            // stage left on the hero; the hit points went 25 -> 30 in the full list and the cause was not reproduced). "Spared" is that the burn wrote no hit-point damage, so the check reads the damage the hero carries.
            var damageBefore = session.QueryHeroLife()!.Record.State.HitPointDamage;
            Step(40);
            // ARC-MAGIC-054: ARC_AREA_SUMMON_WALL_CAPTURE=PREFIX saves the drawn wall (a readback of the window's viewport; needs a window, not a headless run) to PREFIX-wall.png, for the owner's side-by-side look; nothing is committed.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_SUMMON_WALL_CAPTURE") is { Length: > 0 } capture)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng($"{capture}-wall.png") == Error.Ok, "the capture is saved");
            }
            Require(pieces.Select(p => host.CreatedFrame(p.Entity)).Zip(frame0).Any(f => f.First != f.Second), "the pieces' art loops");
            Require(host.FireFrames > fireBefore, "the burn steps ran");
            Require(session.QueryHeroLife()!.Record.State.HitPointDamage <= damageBefore, $"the summoner standing in its own wall is spared: damage {damageBefore} -> {session.QueryHeroLife()!.Record.State.HitPointDamage}, hit points {hpBefore} -> {session.QueryHeroLife()!.Status.HitPoints}");
            // The maintenance stops (an interrupt from MAINTAIN): END, its eye candy has no art, the END callback runs at once and destroys every piece.
            Require(caster.Act(new(SessionSpellRunActionKind.Interrupt, wall)).Disposition == GameSessionDisposition.Changed, "the interrupt ends the wall");
            FlushSpellLog(null);
            Require(Mine(wall).Count == 0 && pieces.All(p => session.QueryCreatedObject(p.Entity) is null), "all the pieces went together at the END callback");
            Require(session.QuerySpellRuns()[wall] is null, "the run ended");
            Step(2);
            Require(pieces.All(p => !Drawn(p.Entity) && !host.Owns(p.Entity)), "no piece is drawn or loops any more");
            // A piece with no summoner (a placed one) burns everyone, the hero on its tile too.
            var placed = session.CreateObject(session.Version, SessionCreatedKind.Scenery, SummonWallFile, map, heroCell).Object;
            Require(placed is not null, "a sourceless piece is created");
            made.Add(placed!.Entity);
            var hpPlaced = session.QueryHeroLife()!.Status.HitPoints;
            var burnt = 0;
            for (; burnt < 80 && session.QueryHeroLife()!.Status.HitPoints >= hpPlaced; burnt++) Step();
            GD.Print($"ARC-DEV-352 summon-wall sourceless hp {hpPlaced} -> {session.QueryHeroLife()!.Status.HitPoints} after {burnt * 50} ms");
            Require(session.QueryHeroLife()!.Status.HitPoints < hpPlaced, "a piece without a source burns the hero");
            GD.Print("ARC-DEV-352 native-smoke summon-wall PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, Pieces = pieces.Count,
                Centre = $"{heroCell.X},{heroCell.Y}", Parity = "NOT_VERIFIED (rules read in the original executable, ARC-RES-101; the owner compares in game)" }));
        }
        finally
        {
            foreach (var entity in made)
                if (session.QueryCreatedObject(entity) is not null) session.DestroyCreatedObject(session.Version, entity);
            foreach (var run in session.QuerySpellRuns().Runs.Where(r => r.Spell == spell && r.Parent?.Id == SessionSpellCaster.Hero).Select(r => r.Id).ToArray())
                SpellCasterNow()?.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            ProcessAreaSceneryAnimation();
            SyncAreaInventory();
            _aiForSmoke = aiBefore;
            if (!aiBefore) { RetireAiHost(); _aiHostStamp = null; }
            RestoreHeroForSmoke(session, heroBefore);
            // The flash's Damage line hits the critters within two cells of the strike tile, as the real spell: they are healed for the later stages.
            if (SpellCasterNow() is { } after && _combatHost is { } combat)
                foreach (var hit in after.Log.Skip(Math.Min(logStart, after.Log.Count)).Where(e => e.Kind == SpellCastEventKind.Damaged && e.Object is { } o && o != SessionSpellCaster.Hero).Select(e => e.Object!).Distinct().ToArray())
                    if (combat.Build(session, area.Snapshot.RuntimeMap, hit).Combat is { } struck)
                        session.Heal(session.Version, struck.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
        }
    }
}
