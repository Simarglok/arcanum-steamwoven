using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>The spell of the critter summon stage (ARC-RES-101): Summon Undead, whose Summon picks from list 5 by the caster's aptitude.</summary>
    private const int SummonUndeadSpell = 57;

    /// <summary>
    /// ARC-MAGIC-053 (E1-e) native observation (stage "summon-undead", only when named, with --hero, on a map window with the inventory active; it is the player's bug report 2026-11-08 17:19:23, "no summoning animation and no summoned NPC"):
    /// the hero's own run of Summon Undead (spell 57) of the module's record, started free through the session and begun through the cast host, on the real map, prototypes, eye-candy table and art. On an empty tile a few cells from the hero
    /// the Summon makes a critter of list 5 (SUM6), which the AI host holds at once; it is made hidden (OF_DONTDRAW, SUM5), summoned (0x4000) and mind controlled (the record's ObjFlag line), joins the hero's party, has its creation
    /// notice queued (the shown map's tile script runs with it as the triggerer, ARC-DEV-402) and its inventory queued; the summoning eye candy plays on it; at the animation's callback frame the CALLBACK line clears OF_DONTDRAW and the
    /// body is drawn; the death of the summoned creature ends the run that made it (0x459740, SUM7) and the END_CALLBACK Destroy takes it away, the party forgets it and its body goes. The hero's life and the map are left as they were.
    /// <c>ARC_SUMMON_UNDEAD_CAPTURE</c> names a path prefix: the viewer saves four captures of the window (<c>-before</c> the cast, <c>-summoning</c> a quarter of a second into the animation, <c>-shown</c> at the callback, <c>-later</c> a second after it) for the graphics report.
    /// </summary>
    private async Task RunAreaSummonUndeadSmokeAsync()
    {
        const string stage = "summon-undead";
        for (var wait = 0; wait < 1800 && (ExtraArt() is null || _createdPrototypeRead is null || !InventoryActive || SpellRecordsNow() is null); wait++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || ExtraArt() is null || _createdPrototypeRead is null)
        {
            GD.Print($"ARC-MAGIC-053 native-smoke {stage} SKIP (needs --hero, an area session, the inventory and the module's content: session={_areaSession is not null} hero={_areaSession?.QueryHero() is not null} "
                + $"area={_readyArea is not null} inventory={InventoryActive} prototypes={_createdPrototypeRead is not null} art={ExtraArt() is not null} spells={_spellData?.Status})");
            return;
        }
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"ARC-MAGIC-053 {stage} smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SUMMON_UNDEAD_SMOKE_FAILED");
        }
        var capture = System.Environment.GetEnvironmentVariable("ARC_SUMMON_UNDEAD_CAPTURE");
        async Task Capture(string name)
        {
            if (string.IsNullOrWhiteSpace(capture)) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "capture saved");
        }
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var map = new SessionMapId(CurrentAreaMap);
        var aiBefore = _aiForSmoke;
        _aiForSmoke = true;
        var made = new List<string>();
        try
        {
            PortalSmokeClockStart();
            for (var wait = 0; wait < 600 && (SpellCasterNow() is null || AreaAiHostNow() is null); wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(SpellCasterNow() is not null && AreaAiHostNow() is not null, "the cast host and the AI host");
            var caster = SpellCasterNow()!;
            var record = SpellRecordsNow()![SummonUndeadSpell]!;
            var summon = record.Of(SpellRunAction.Begin).SingleOrDefault(c => c.Type == SpellComponentType.Summon)?.Summon;
            Require(summon is { Prototype: 0, List: 5 }, $"the record's one Summon picks from list 5: proto={summon?.Prototype} list={summon?.List}");
            Require(HeroPoolPlace() is not null, "the hero has a committed cell");
            var heroCell = HeroPoolPlace()!.Value.Cell;

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
                var hero = caster.Facts(SessionSpellCaster.Hero)!;
                var started = session.StartSpellRun(session.Version, new(new(SummonUndeadSpell, hero, null, null, new((uint)tile.X, (uint)tile.Y)), Free: true), record.Run, caster.InvocationWorld(), session.SpellRolls);
                Require(started.Disposition == GameSessionDisposition.Changed && started.Creation?.Run is not null, $"the run starts: {started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
                var run = started.Creation!.Run!.Id;
                Require(caster.Act(new(SessionSpellRunActionKind.Begin, run)).Disposition == GameSessionDisposition.Changed, "the BEGIN runs");
                FlushSpellLog(null);
                return run;
            }
            bool Drawn(string entity) => CreatedCrittersRow(entity) is { } row && _groundBodies.ContainsKey(row.Key);
            bool Hidden(string entity) => (session.QueryObjectFlags(entity).Object(0) & SpellTargetObject.DontDraw) != 0;
            Step(3);
            await Capture("before");

            SessionCreatedObject? critter = null;
            var run = -1;
            var noticesBefore = _creationNoticesForSelfTest.Count;
            foreach (var (dx, dy) in new[] { (3, 0), (-3, 0), (0, 3), (0, -3), (3, 3), (-3, -3), (3, -3), (-3, 3), (4, 1), (-4, -1) })
            {
                var tile = new CellCoord(heroCell.X + dx, heroCell.Y + dy);
                var at = caster.Log.Count;
                run = Cast(tile);
                critter = session.QueryCreatedObjects(map).SingleOrDefault(o => o.Run == run);
                if (critter is not null) break;
                GD.Print($"ARC-MAGIC-053 summon-undead tile={tile.X},{tile.Y} no creature: " + string.Join(" ", caster.Log.Skip(at).Select(e => $"{e.Kind}:{e.Value}:{e.Object}:{e.Detail}")));
                caster.Act(new(SessionSpellRunActionKind.ForceEnd, run));
                Step(2);
            }
            Require(critter is not null, "an empty tile near the hero took a creature");
            var entity = critter!.Entity;
            made.Add(entity);
            var proto = critter.Prototype - SessionSpellCaster.PrototypeFileOffset;
            GD.Print($"ARC-MAGIC-053 summon-undead run={run} creature={entity} basic-prototype={proto} cell={critter.Cell.X},{critter.Cell.Y} hero-aptitude={caster.Facts(SessionSpellCaster.Hero)!.Aptitude}");
            Require(SummonLists.EntriesOf(5).Any(e => e.Prototype == proto), "the creature is an entry of list 5 (SUM6)");
            Require(critter.Kind == SessionCreatedKind.Critter && critter.SpellFlags == SessionCreatedObject.SummonedFlag && critter.Summoner == SessionSpellCaster.Hero && critter.Cell != heroCell, "a summoned critter of the hero's run on another tile");
            Require(!caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Run == run), "no line of the record is left undone: " + string.Join(" ", caster.Log.Where(e => e.Run == run).Select(e => $"{e.Kind}:{e.Value}")));
            Require(_summonedCrittersForSelfTest.Contains(entity), "the host was told of the creature");
            Require(_creationNoticesForSelfTest.Skip(noticesBefore).Any(n => n.Entity == entity && n.Cell.X == critter.Cell.X && n.Cell.Y == critter.Cell.Y), "the creation queued its location notification on its cell (TG9)");
            var flags = session.QueryObjectFlags(entity);
            Require((flags.Spell(0) & 0x8000000) != 0 && (flags.Spell(0) & (int)SessionCreatedObject.SummonedFlag) != 0, "summoned and mind controlled");
            Require(session.QueryCompanion(entity).LedByPc, "the mind control made it the hero's follower");
            Require(AreaAiHostNow()!.Scene.Npcs.Any(n => n.Entity == entity), "the AI host holds the creature");
            Require(CreatedCrittersRow(entity) is not null, "the creature has a row");
            Require(session.QuerySpellRuns()[run] is not null, "the run is maintained");
            // Hidden until the summoning animation's callback: the eye candy plays on it and the body is not drawn.
            Step(1);
            Require(ShownEyeCandyHost?.Shown().Any(o => o.Owner == entity) == true, "the summoning eye candy plays on the creature");
            Require(Hidden(entity) && !Drawn(entity), "the creature is hidden (OF_DONTDRAW) while the animation plays");
            Step(5);
            Require(Hidden(entity) && !Drawn(entity), "still hidden a quarter of a second into the animation");
            await Capture("summoning");
            var frames = 0;
            for (; frames < 80 && Hidden(entity); frames++) Step();
            GD.Print($"ARC-MAGIC-053 summon-undead shown-after={frames * 50} ms drawn={Drawn(entity)}");
            Require(!Hidden(entity), "the CALLBACK line cleared OF_DONTDRAW within 4 s");
            Step(2);
            Require(Drawn(entity), "the body is drawn once it shows");
            await Capture("shown");
            Step(20);
            await Capture("later");
            Require(session.QuerySpellRuns()[run] is not null, "the maintained run goes on");
            // The creature is killed through the session's life step (the kill tells the runs the object is gone, 0x459740): the run that made it ends, its END_CALLBACK destroys it, the party forgets it.
            var killed = session.ApplyDamage(session.Version, SessionLifeTarget.Critter(entity, new LifeFacts(), default), new LifeDamage { HitPoints = 100000 });
            Require(killed.Disposition == GameSessionDisposition.Changed && killed.Outcome!.Events.HasFlag(LifeEvents.Died), $"the killing damage kills it ({killed.Failure})");
            var ended = 0;
            for (; ended < 80 && session.QuerySpellRuns()[run] is not null; ended++) Step();
            Step(3);
            Require(session.QuerySpellRuns()[run] is null, "the run ended with its creature");
            Require(session.QueryCreatedObject(entity) is null, "the END_CALLBACK Destroy took the creature away");
            Require(!session.QueryCompanion(entity).LedByPc, "the party forgot it");
            Require(!Drawn(entity) && AreaAiHostNow()!.Scene.Npcs.All(n => n.Entity != entity), "its body is gone and the AI host let go of it");
            GD.Print("ARC-MAGIC-053 native-smoke summon-undead PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, Creature = proto, Cell = $"{critter.Cell.X},{critter.Cell.Y}",
                Parity = "NOT_VERIFIED (rules read in the original executable, ARC-RES-101, ARC-MAGIC-053; the owner compares in game)" }));
        }
        finally
        {
            foreach (var entity in made)
                if (session.QueryCreatedObject(entity) is not null) session.DestroyCreatedObject(session.Version, entity);
            foreach (var run in session.QuerySpellRuns().Runs.Where(r => r.Spell == SummonUndeadSpell && r.Parent?.Id == SessionSpellCaster.Hero).Select(r => r.Id).ToArray())
                SpellCasterNow()?.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            SyncAreaInventory();
            _aiForSmoke = aiBefore;
            if (!aiBefore) { RetireAiHost(); _aiHostStamp = null; }
            RestoreHeroForSmoke(session, heroBefore);
        }
    }
}
