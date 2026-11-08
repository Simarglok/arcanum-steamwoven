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
    // ARC-DEV-301 (the 2026-11-08 entangle report, triage T1) native observation (stage "spell-end", only when named, map mode with --hero, AI on, a map with an NPC whose fields store a magick college level: the Crash Site window):
    // the NPC casts a spell with a timed eye candy on the hero (the vines of the entangle, spell 51, first; any enabled timed spell whose BEGIN adds an eye candy otherwise). The original ends the runs of a critter when it dies
    // (the kill 0x45DA20 calls 0x459740: the runs it is the parent of end by event) and when it loses consciousness (the fatigue setter 0x45D5A0 calls 0x457450: the runs it is the source of end by event), and an ended
    // run removes its eye candy. The stage plays both: the NPC dies (a lethal packet through the session's life step), the run's eye candy is removed on the hero; then the NPC is raised, casts again and is knocked out
    // (a fatigue packet), and the run ends again. The NPC, the hero and the casts' runs are restored for the later stages.
    private async Task RunAreaSpellEndSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-DEV-301 native-smoke spell-end SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-301 spell-end smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SPELL_END_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var candidates = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity), Fields: _combatHost.Fields(map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key)
                && c.Fields?.Critter?.SpellTech.Take(SpellBookRules.Colleges).Any(rank => rank > 0) == true)
            .OrderByDescending(c => c.Build.Combat!.Level).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        if (candidates.Length == 0) { GD.Print($"ARC-DEV-301 native-smoke spell-end NONE map={map.RuntimeMap} (no NPC with magick levels)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var target = candidates[0];
        var entity = target.Holder.Entity;
        var npcLife = target.Build.Combat!.Target;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var scaled = false;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
            // A standable cell 3..5 cells from the NPC.
            CellCoord? start = null;
            foreach (var (dx, dy) in new[] { (4, 0), (-4, 0), (0, 4), (0, -4), (3, 0), (-3, 0), (0, 3), (0, -3), (5, 0), (-5, 0), (0, 5), (0, -5) })
            {
                var near = new CellCoord(targetCell.X + dx, targetCell.Y + dy);
                if (!map.Chart.Contains(near)) continue;
                CellCoord cell;
                try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                if (Math.Max(Math.Abs(cell.X - targetCell.X), Math.Abs(cell.Y - targetCell.Y)) >= 3) { start = cell; break; }
            }
            Require(start is not null, "a standable cell a few cells from the NPC");
            AreaSmokePlace(map, start!.Value);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            for (var wait = 0; wait < 600 && SpellCasterNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var caster = SpellCasterNow();
            Require(caster is not null, "the map's cast host is built (spell data " + _spellData?.Status + ")");
            var records = _spellRecords;
            Require(records is not null, "spell data " + _spellData?.Status + " " + _spellData?.Detail);
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            host!.HeroAttacked(entity);
            Require(session.QueryDanger(entity).State == DangerState.CombatFocus, "the NPC fights the hero: " + session.QueryDanger(entity).State);

            // The spells whose BEGIN adds an eye candy, the entangle first (an instant one ends by itself and is passed over).
            var spells = records!.Where(r => r is not null && r.Data.Number < SpellRecord.CollegeSpells && r.Data.Enabled
                    && r.Of(SpellRunAction.Begin).Any(c => c.Type == SpellComponentType.EyeCandy && c.EyeCandy is { Add: true }))
                .Select(r => r.Data.Number).OrderBy(n => n == 51 ? 0 : 1).ThenBy(n => n).Take(48).ToList();
            // A resisted cast ends at once: the entangle is tried three times.
            spells.InsertRange(0, [51, 51]);
            Require(spells.Count > 0, "the data holds a spell that adds an eye candy");
            var notes = new List<string>();

            _areaSmokeTime *= 20; scaled = true; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            void Frame()
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            // The NPC casts spells until one starts a run of its own that begins and puts an eye candy on the hero; its run and the eye candy entries.
            (int Spell, int Run, int Entries)? Cast(string why)
            {
                foreach (var candidate in spells)
                {
                    session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                    // A raised or woken NPC first gets up (its goal 63) and stands idle before it can cast.
                    for (var idle = 0; idle < 900 && host.RunOf(entity) is not null; idle++) Frame();
                    host.HeroAttacked(entity);
                    var logStart = caster!.Log.Count;
                    host.PerformChoice(entity, new AiPassChoice(AiPassAction.Cast, candidate, 0, null, AiSchedulerRules.Pc));
                    if (host.RunOf(entity)?.Bottom.SpellRun is not { } run || session.QuerySpellRuns()[run]?.Spell != candidate) { notes.Add($"{candidate}: no run ({host.TraceOf(entity)?.Cast})"); continue; }
                    for (var frame = 0; frame < 1500; frame++)
                    {
                        Frame();
                        if (session.QuerySpellRuns()[run] is not { Parent.Id: { } parent } r || parent != entity) break;
                        if (caster.Log.Skip(logStart).Any(e => e.Kind == SpellCastEventKind.EyeCandyAdded && e.Run == run && e.Object == AiSchedulerRules.Pc || e.Kind == SpellCastEventKind.EyeCandyAdded && e.Run == run && e.Object == SessionSpellCaster.Hero)
                            && r.Action != SpellRunAction.Begin)
                        {
                            var entries = caster.Log.Skip(logStart).Count(e => e.Kind == SpellCastEventKind.EyeCandyAdded && e.Run == run);
                            GD.Print($"ARC-DEV-301 spell-end {why}: the NPC cast spell {candidate}, run {run} with {entries} eye candy entries on the hero after {frame} frames");
                            return (candidate, run, entries);
                        }
                    }
                    // Not this one (no eye candy on the hero, or the run ended by itself): clear it and try the next.
                    notes.Add($"{candidate}: run {run} {session.QuerySpellRuns()[run]?.Action.ToString() ?? "gone"}, eye candy {string.Join(",", caster.Log.Skip(logStart).Where(e => e.Kind == SpellCastEventKind.EyeCandyAdded).Select(e => e.Object))}");
                    if (session.QuerySpellRuns()[run] is not null) caster.Act(new(SessionSpellRunActionKind.ForceEnd, run));
                }
                return null;
            }
            bool Ended(int run, int spell) => session.QuerySpellRuns()[run] is not { Parent.Id: { } p } r || p != entity || r.Spell != spell;

            // 1. Death.
            var first = Cast("death");
            Require(first is not null, $"the NPC cast one of the spells {string.Join(",", spells)} and put an eye candy on the hero: {string.Join(" | ", notes)}");
            var (spell1, run1, entries1) = first!.Value;
            var removedBefore = caster!.Log.Count(e => e.Kind == SpellCastEventKind.EyeCandyRemoved);
            var logAt = caster.Log.Count;
            Require(session.QuerySpellRuns()[run1] is { } alive && alive.Parent?.Id == entity, "before the kill the run lives and the NPC is its parent");
            var kill = session.ApplyDamage(session.Version, npcLife, new LifeDamage { HitPoints = 1_000_000 });
            Require(kill.Outcome is { } killed && killed.Events.HasFlag(LifeEvents.Died), $"the lethal packet killed the NPC: {kill.Disposition} {kill.Failure}");
            var deathFrames = 0;
            for (; deathFrames < 600 && !Ended(run1, spell1); deathFrames++) Frame();
            var removed = caster.Log.Skip(logAt).Count(e => e.Kind == SpellCastEventKind.EyeCandyRemoved && e.Run == run1);
            GD.Print($"ARC-DEV-301 spell-end death: run {run1} ended={Ended(run1, spell1)} after {deathFrames} frames, eye candy entries removed={removed} of {entries1}");
            Require(Ended(run1, spell1), "the NPC's death ended the run it was the parent of (0x45DA20 → 0x459740)");
            Require(deathFrames <= 10, $"the ending came with the death (within 10 frames of 50 ms, far from the spell's own duration), not by the run's own expiry: {deathFrames} frames");
            Require(removed >= entries1, $"the ended run removed its {entries1} eye candy entries from the hero: {removed}");

            // 2. Loss of consciousness.
            var second = Cast("knock-out");
            Require(second is not null, $"the raised NPC cast again and put an eye candy on the hero: {string.Join(" | ", notes)}");
            var (spell2, run2, entries2) = second!.Value;
            var logAt2 = caster.Log.Count;
            Require(session.QuerySpellRuns()[run2] is { } alive2 && alive2.Parent?.Id == entity && alive2.Source?.Id == entity, "before the packet the run lives and the NPC is its parent and source");
            var out_ = session.ApplyDamage(session.Version, npcLife, new LifeDamage { Fatigue = 1_000_000 });
            Require(out_.Outcome is { } outcome && !outcome.Events.HasFlag(LifeEvents.Died), $"the fatigue packet left the NPC alive: {out_.Disposition} {out_.Failure}");
            Require(session.QueryLife(npcLife).View is { Status.IsUnconscious: true }, "the NPC is unconscious: " + session.QueryLife(npcLife).View?.Status);
            var outFrames = 0;
            for (; outFrames < 600 && !Ended(run2, spell2); outFrames++) Frame();
            var removed2 = caster.Log.Skip(logAt2).Count(e => e.Kind == SpellCastEventKind.EyeCandyRemoved && e.Run == run2);
            GD.Print($"ARC-DEV-301 spell-end knock-out: run {run2} ended={Ended(run2, spell2)} after {outFrames} frames, eye candy entries removed={removed2} of {entries2}");
            Require(Ended(run2, spell2), "the NPC's loss of consciousness ended the run it was the source of (0x45D5A0 → 0x457450)");
            Require(outFrames <= 10, $"the ending came with the knock-out (within 10 frames), not by the run's own expiry: {outFrames} frames");
            Require(removed2 >= entries2, $"the ended run removed its {entries2} eye candy entries from the hero: {removed2}");
            GD.Print("ARC-DEV-301 native-smoke spell-end PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Death = new { Spell = spell1, Run = run1, Entries = entries1, Removed = removed, Frames = deathFrames },
                KnockOut = new { Spell = spell2, Run = run2, Entries = entries2, Removed = removed2, Frames = outFrames },
                Hold = "NOT_TESTED (the entangle's hold flag is the spell components' MT13, not written yet)",
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            foreach (var run in session.QuerySpellRuns().Runs.Where(r => r.Parent?.Id == entity).Select(r => r.Id).ToArray()) SpellCasterNow()?.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            CalmNpcsForSmoke(session, [entity]);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            RestoreHeroForSmoke(session, heroBefore);
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
        await Task.CompletedTask;
    }
}
