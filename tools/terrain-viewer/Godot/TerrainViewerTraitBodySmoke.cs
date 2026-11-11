using Arcanum.Data.Objects;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-MAGIC-046 (MA-8 second slice) native observation (stage "trait-body", only when named, with --hero, on a map window with the inventory active): Polymorph (spell 49) cast by a script with no caster
    /// on the first NPC the AI host holds with a human body draws the NPC as a monster body of the species the spell's Trait component chose (class 12, the record's species and palette, its
    /// own cell and facing), through the moving bodies the canvas draws; the END's restore draws its own body again. Body of Air (spell 13) cast on the hero takes every worn item off, and its END chooses the
    /// best items again. ARC_AREA_TRAIT_CAPTURE=PREFIX saves the window before, polymorphed and restored as PREFIX-before.png, -after.png and -restored.png (a local, ignored path).
    /// </summary>
    private async Task RunAreaTraitBodyStageAsync()
    {
        for (var wait = 0; wait < 1800 && (ExtraArt() is null || _createdPrototypeRead is null || !InventoryActive); wait++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || ExtraArt() is not { } extra || _createdPrototypeRead is not { } read)
        { GD.Print("ARC-MAGIC-046 native-smoke trait-body SKIP (needs --hero, an area session, the inventory and the module's content)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-MAGIC-046 trait-body smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TRAIT_BODY_SMOKE_FAILED");
        }
        var map = new SessionMapId(CurrentAreaMap);
        if (HeroPoolPlace() is null) { GD.Print("ARC-MAGIC-046 native-smoke trait-body SKIP (the hero has no committed cell)"); return; }

        var aiBefore = _aiForSmoke;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        _aiForSmoke = true;
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_TRAIT_CAPTURE");
        // Real frames, with the area clock ticked by hand (a native smoke's viewer does not tick it itself; a script's cast with no caster begins at an event of the session's queue).
        // The smoke's own world frames are not mixed in: they run on another clock than the real ones and the grid's observations would arrive out of order.
        async Task Settle(int frames = 3)
        {
            for (var i = 0; i < frames; i++)
            {
                TickAreaClock(_talkSmokeHostMs += 100);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                SyncAreaInventory(); FlushSpellLog(null);
            }
        }
        async Task Shot(string name)
        {
            await Settle(3);
            if (capture is not { Length: > 0 }) return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.SavePng($"{capture}-{name}.png") != Error.Ok) throw new InvalidOperationException("AREA_TRAIT_CAPTURE_FAILED " + name);
        }
        string? entity = null;
        var runs = new List<int>();
        try
        {
            for (var wait = 0; wait < 600 && (AreaAiHostNow() is null || SpellCasterNow() is null); wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaAiHostNow() is { } && SpellCasterNow() is not null, "the AI host and the cast host are built");
            var host = AreaAiHostNow()!;
            // Polymorph's own target mask decides which critter a cast may take (no animals, say): the one chosen is a critter the BEGIN area accepts.
            bool Polymorphable(string id)
            {
                var caster = SpellCasterNow()!;
                return caster.Facts(id) is { } facts && caster.InvocationWorld().TargetMatchesBeginArea(new Arcanum.Simulation.Magic.SpellInvocation(49, null, null, facts, Arcanum.Simulation.Magic.SpellLocation.None));
            }
            // Living hosted NPCs with a human body whose monster body (Polymorph's species 20) reads from the window's archives. The map's opening script kills some NPCs at their first heartbeat, which comes
            // when the hero's window reaches them, so each candidate gets the hero beside it and some frames, and the first one still alive is the target.
            var candidates = host.Scene.Npcs.Where(n => Number(n.Row, CurrentArtSlot) is { } own && own >> 28 is 2 or 14 && n.Row.WorldX is not null && !session.QueryNpcDead(n.Entity)
                && AreaCreatedCritterArt.Load(TraitArtId(own, new(20, 0)), area.ArtSources, extra.MappedName).Status == AreaCreatedCritterArtStatus.Ready)
                // A unique NPC art (class 14) keeps its rotation in other bits than a critter art (0x502880), so those candidates go first when the map has them.
                .OrderBy(n => Number(n.Row, CurrentArtSlot) >> 28 == 14 ? 0 : 1).Take(16).ToArray();
            AreaAiNpc? npc = null;
            if (_worldActor.Session is AreaGridSession grid)
            {
                foreach (var candidate in candidates)
                {
                    var npcCell = LiveWorldCell(candidate.Row);
                    var place = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
                    try { AreaSmokePlace(place, AreaSmokeStandable(place, new(checked((int)(npcCell.X - grid.Chart.OriginX)), checked((int)(npcCell.Y - grid.Chart.OriginY))), 2)); }
                    catch (InvalidOperationException) { continue; }
                    PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
                    await Settle(30);
                    if (session.QueryNpcDead(candidate.Entity) || !Polymorphable(candidate.Entity)) continue;
                    npc = candidate; break;
                }
            }
            if (npc is null) { GD.Print($"ARC-MAGIC-046 native-smoke trait-body NONE (none of {candidates.Length} hosted NPCs with a human body and a readable monster body stays alive and takes Polymorph beside the hero)"); return; }
            entity = npc.Entity;
            var key = npc.Row.Key;
            var before = AreaMovingCommands()?.FirstOrDefault(c => c.Identity == key);
            // A standing NPC is a static body of the map's canvas (no moving command) until it moves; a moving one is drawn as its own human body.
            Require(before is null || before.ArtClass is 2 or 14, $"the critter is first drawn as its own human body (class {before?.ArtClass})");
            GD.Print($"ARC-MAGIC-046 trait-body target={npc.Entity} key={key} own-class={Number(npc.Row, CurrentArtSlot) >> 28} before-moving={(before is null ? "static" : "class " + before.ArtClass)}");
            await Shot("before");
            GD.Print($"ARC-MAGIC-046 trait-body target-dead-after-shot={session.QueryNpcDead(entity)}");

            // A script's cast with no caster at the critter: its BEGIN is an event the next frames fire.
            int Cast(int spell, string target)
            {
                var caster = SpellCasterNow() ?? throw new InvalidOperationException("AREA_TRAIT_BODY_NO_CAST_HOST");
                var attempt = caster.CastByScript(null, spell, target, free: true, unresistable: true);
                if (attempt.Outcome != SpellCastAttemptOutcome.Started)
                {
                    var facts = caster.Facts(target);
                    var probe = facts is null ? null : new Arcanum.Simulation.Magic.SpellInvocation(spell, null, null, facts, Arcanum.Simulation.Magic.SpellLocation.None);
                    GD.Print($"ARC-MAGIC-046 trait-body refused spell={spell} target={target} facts={(facts is null ? "none" : $"type={facts.Type} flags={facts.ObjectFlags:X}/{facts.SpellFlags:X}/{facts.CritterFlags:X}")} begin-area={(probe is null ? "?" : caster.InvocationWorld().TargetMatchesBeginArea(probe))} dead={session.QueryNpcDead(target)}");
                }
                Require(attempt.Outcome == SpellCastAttemptOutcome.Started && attempt.RunId is not null, $"the script cast of spell {spell} started: {attempt.Outcome} {attempt.Run?.Creation?.Status} {attempt.Run?.Creation?.Check}");
                runs.Add(attempt.RunId!.Value);
                return attempt.RunId.Value;
            }
            async Task Until(Func<bool> done, string why)
            {
                for (var i = 0; i < 120 && !done(); i++) await Settle(1);
                Require(done(), why);
            }
            var polymorph = Cast(49, entity!);
            await Until(() => session.QueryTraitArt(entity!) is not null, $"the BEGIN of Polymorph put a monster body on the critter (log {string.Join(",", (SpellCasterNow()?.Log ?? []).TakeLast(10).Select(e => $"{e.Kind}:{e.Value}"))})");
            var art = session.QueryTraitArt(entity!)!.Value;
            await Settle(4);
            var monster = AreaMovingCommands()?.FirstOrDefault(c => c.Identity == key);
            GD.Print($"ARC-MAGIC-046 trait-body entity={entity!} species={art.Species} palette={art.Palette} drawn-class={monster?.ArtClass} traitBodies={_traitBodies.Count}");
            Require(monster is not null && monster.ArtClass == 12, "the critter is drawn with a monster body (art class 12)");
            Require(TryBodyVisual(monster!.Visual, out var visual) && visual.RawArt >> 28 == 12 && (visual.RawArt >> 23 & 31) == art.Species && (visual.RawArt >> 4 & 3) == art.Palette, "its visual is the record's species and palette");
            Require(AreaFrames().ContainsKey(monster.Frame), "its frame is in the map's repository");
            Require(AreaMovingCommands()!.Count(c => c.Identity == key) == 1, "one command draws the critter");
            await Shot("after");

            // The END: the restore draws the critter's own body again.
            SpellCasterNow()!.Act(new(SessionSpellRunActionKind.ForceEnd, polymorph));
            FlushSpellLog(null);
            await Until(() => session.QueryTraitArt(entity!) is null, "the END of Polymorph took the monster body off");
            await Settle(4);
            var restored = AreaMovingCommands()?.FirstOrDefault(c => c.Identity == key);
            Require(restored is null || restored.ArtClass is 2 or 14, "the critter is drawn as its own human body again");
            Require(_traitBodies.Count == 0, "no monster body is kept");
            await Shot("restored");

            // The hero: Body of Air takes every worn item off, its END chooses the best items again.
            var worn = session.QueryEquipment().Worn.Count;
            if (worn == 0) GD.Print("ARC-MAGIC-046 trait-body hero SKIP (the hero wears nothing)");
            else
            {
                var air = Cast(13, SessionSpellCaster.Hero);
                await Until(() => session.QueryTraitArt(SessionSpellCaster.Hero) is not null, "the BEGIN of Body of Air put a body on the hero");
                await Until(() => session.QueryEquipment().Worn.Count == 0, $"every worn item was taken off ({session.QueryEquipment().Worn.Count} left; {_traitEquipmentResult})");
                SpellCasterNow()!.Act(new(SessionSpellRunActionKind.ForceEnd, air));
                FlushSpellLog(null);
                await Until(() => session.QueryTraitArt(SessionSpellCaster.Hero) is null, "the END of Body of Air took the body off");
                await Until(() => session.QueryEquipment().Worn.Count > 0, $"the best items were chosen again ({_traitEquipmentResult})");
                GD.Print($"ARC-MAGIC-046 trait-body hero worn-before={worn} worn-after={session.QueryEquipment().Worn.Count} {_traitEquipmentResult}");
            }
            GD.Print("ARC-MAGIC-046 native-smoke trait-body PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, Species = art.Species }));
        }
        finally
        {
            foreach (var run in runs) if (session.QuerySpellRuns()[run] is not null) SpellCasterNow()?.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            FlushSpellLog(null);
            SyncAreaInventory();
            _aiForSmoke = aiBefore;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            if (!aiBefore) { RetireAiHost(); _aiHostStamp = null; }
        }
    }
}
