using Arcanum.Data.Characters;
using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal const string SpellEffectsCode = "ARC-DEV-1148";
    private const int WillpowerEffectNumber = 399; // the last effect number the session admits; no spell of the stage uses it

    // ARC-DEV-1148 (magic audit ARC-RES-103, MA-2 leftover: "no smoke stage names 30, 46, 76 or 77") native observation (stage "spell-effects", only when named, with --hero, on a map with a living NPC that is
    // not invulnerable: the Crash Site window): the four college spells whose Effect component writes a stat change and that no other stage names.
    //  - Shield of Protection (spell 30) and Hasten (spell 77) are cast on the hero by a script with no caster (free, unresistable: the cast host's own run, BEGIN as an event, no casting goal);
    //  - Weaken (spell 46) and Congeal Time (spell 76) are cast at the nearest living free NPC by a script whose caster is the PC (CastByScriptAsHero: the casting goal plays). A parentless cast fails the invocation
    //    check of spell 76 (its BEGIN mask has the non-party bit, which the check's context fails for the target as the "other" object), and a PC parent skips that test; the hero's willpower is raised by a
    //    made-up effect of the stage for the run's checks (the premade hero's willpower is below the spells' minimum: "You lose your concentration", spell.mes 602) and the effect is taken off again.
    // Each run's BEGIN adds its Effect components' entries to the target's list, the effective values move as the rules data says (armour class adjustment up for 30, speed up for 77, strength down and speed
    // down for 46 and 76), and a forced END removes the entries and brings the values back, except for **spell 76, a KNOWN GAP**: its END area names the run's object list (bit 0x80000000), which no host
    // fills, so Congeal Time's effect stays on the NPC after the END (see spell-cast-host.md). The stage expects the leftover and fails when it stops, so the removal is then expected here instead.
    // The hero, the NPC's list and the runs are put back for the later stages.
    private async Task RunAreaSpellEffectsSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area || _worldActor.Session is not AreaGridSession)
        { GD.Print($"{SpellEffectsCode} native-smoke spell-effects NONE map={_readyMapName} (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{SpellEffectsCode} spell-effects smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SPELL_EFFECTS_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var scaled = false;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var runs = new List<int>();
        var willpowerAdded = false;
        SessionLifeTarget? npcTarget = null;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
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
            // A placement rebuilds the cast host, so it is asked for each time.
            SessionSpellCaster Caster() => SpellCasterNow() ?? throw new InvalidOperationException("AREA_SPELL_EFFECTS_NO_CAST_HOST");
            void Frame() => TechSmokeFrame();

            // The nearest living free NPC with a cast-host id and combat values that is not invulnerable (the mask of spell 76 and others leaves invulnerable objects out).
            var here = Caster().Facts(SessionSpellCaster.Hero)?.Location is { } at && at != SpellLocation.None ? new CellCoord(checked((int)at.X), checked((int)at.Y)) : default;
            static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
            var npc = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
                .Select(row => (Row: row, Id: SpellPickId(row.Key), Target: _combatHost.Target(session, runtime, row.Key)))
                .Where(o => o.Id is not null && o.Target is { Dead: false, Follower: false, Build.Combat: not null } && Caster().Facts(o.Id) is { } facts && facts.Location != SpellLocation.None && (facts.ObjectFlags & SpellTargetObject.Invulnerable) == 0)
                .OrderBy(o => Math.Max(Math.Abs(World(o.Row).X - here.X), Math.Abs(World(o.Row).Y - here.Y))).FirstOrDefault();
            Require(npc.Id is not null, "a living free NPC with a cell on the map");
            var npcId = npc.Id!;
            var npcCombat = npc.Target!.Build.Combat!;
            npcTarget = npcCombat.Target;

            int HeroStat(CharacterStat stat) => session.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(stat);
            int NpcStat(CharacterStat stat) => session.WithNpcEffects(npcCombat).EffectiveStat(stat, npcCombat.Loaded);
            string[] HeroList() => [.. session.QueryHeroEffects()!.Effects.Select(e => "effect=" + e.Number).Order(StringComparer.Ordinal)];
            string[] NpcList() => [.. (session.QueryNpcEffects(npcCombat.Target)?.Entries ?? []).Select(e => "effect=" + e.Number).Order(StringComparer.Ordinal)];

            var report = new List<object>();
            void Probe(int spell, string target, Func<CharacterStat, int> stat, Func<string[]> list, CharacterStat watched, int direction, string? caster = null)
            {
                var records = SpellRecordsNow()!;
                Require(spell < records.Length && records[spell] is not null, $"spell {spell}'s record is read");
                var added = records[spell]!.Of(SpellRunAction.Begin).Where(c => c.Type == SpellComponentType.Effect && c.Effect is { Add: true }).Select(c => c.Effect!).ToArray();
                Require(added.Length > 0, $"spell {spell}'s BEGIN adds an effect");
                var valuesBefore = Enum.GetValues<CharacterStat>().ToDictionary(s => s, stat);
                var listBefore = list();
                int run;
                if (caster is null)
                {
                    var attempt = Caster().CastByScript(null, spell, target, free: true, unresistable: true);
                    Require(attempt.Outcome == SpellCastAttemptOutcome.Started && attempt.RunId is not null, $"the script cast of spell {spell} started: {attempt.Outcome} {attempt.Run?.Disposition} {attempt.Run?.Failure}");
                    run = attempt.RunId!.Value;
                }
                else
                {
                    // A script's cast whose caster is the PC (the invocation skips the BEGIN-area mask for a PC parent, which a parentless cast at an NPC fails for a mask with the non-party bit): the casting
                    // goal plays as the hero's own cast does and begins the run once its animation is through.
                    var text = CastByScriptAsHero(target, spell, free: true, unresistable: true);
                    Require(_spellGoal is { } goal && goal.Spell == spell, $"the hero's cast of spell {spell} plays: {text}");
                    run = _spellGoal!.Run;
                }
                runs.Add(run);
                var expected = added.Sum(e => e.Count);
                // A cast with no caster has no casting goal: the run's BEGIN is an event of the session's queue (due now), which the next frames fire.
                for (var i = 0; i < 400 && list().Length < listBefore.Length + expected; i++) { Frame(); FlushSpellLog(null); }
                var gained = list().Length - listBefore.Length;
                Require(list().Length >= listBefore.Length + expected, $"the BEGIN of spell {spell} added its effects within 400 frames: {string.Join(",", listBefore)} -> {string.Join(",", list())} (expected +{expected} of {string.Join(",", added.Select(e => e.Effect))}; goal {_spellGoal?.Run}, notice \"{_spellNotice}\", run {_areaSession?.QuerySpellRuns()[run]?.Action}, log {string.Join(",", Caster().Log.TakeLast(14).Select(e => $"{e.Kind}:{e.Run}:{e.Value}:{e.Detail}"))})");
                foreach (var effect in added) Require(list().Contains("effect=" + effect.Effect), $"effect {effect.Effect} of spell {spell} is on the target: {string.Join(",", list())}");
                var during = Enum.GetValues<CharacterStat>().ToDictionary(s => s, stat);
                var moved = during.Where(p => p.Value != valuesBefore[p.Key]).Select(p => $"{p.Key}:{valuesBefore[p.Key]}->{p.Value}").ToArray();
                GD.Print($"{SpellEffectsCode} spell={spell} target={target} run={run} effects={string.Join(",", added.Select(e => e.Effect))} list={string.Join(",", list())} moved=[{string.Join(" ", moved)}]");
                Require(Math.Sign(during[watched] - valuesBefore[watched]) == direction, $"spell {spell} moves {watched} in direction {direction}: {valuesBefore[watched]} -> {during[watched]}");
                // The hero's casting goal plays out (its rest after the release) before the run is ended, so the END is the run's own and not a free of a run still being cast.
                for (var i = 0; i < 400 && (_spellGoal is not null || HeroRunsHost()?.CastRun is not null); i++) { Frame(); FlushSpellLog(null); }
                Require(_spellGoal is null && HeroRunsHost()?.CastRun is null && _areaSession?.QuerySpellRuns()[run] is not null, $"spell {spell}'s casting goal ended and its run is still there (goal {_spellGoal?.Run}, run {_areaSession?.QuerySpellRuns()[run]?.Action})");
                Caster().Act(new(SessionSpellRunActionKind.ForceEnd, run));
                for (var i = 0; i < 20; i++) { Frame(); FlushSpellLog(null); }
                // ARC-MAGIC-1234: every spell's forced END takes its entries off and brings the stats back; spell 76's END area names the run's object list (stored by its BEGIN gather), which now finds the target.
                Require(list().SequenceEqual(listBefore), $"the END of spell {spell} removed its effects: {string.Join(",", listBefore)} -> {string.Join(",", list())} (log {string.Join(",", Caster().Log.TakeLast(10).Select(e => $"{e.Kind}:{e.Run}:{e.Value}"))})");
                Require(Enum.GetValues<CharacterStat>().All(s => stat(s) == valuesBefore[s]), $"the stats are back at their values after spell {spell}");
                report.Add(new { Spell = spell, EndRemoves = true, Target = target == SessionSpellCaster.Hero ? "hero" : "npc", Effects = added.Select(e => e.Effect).ToArray(), Watched = watched.ToString(), Before = valuesBefore[watched], During = during[watched], Gained = gained });
            }

            // The hero casts the two NPC spells as a PC parent, whose willpower the run's stage checks against the record's minimum (the premade hero's is low: "You lose your concentration", spell.mes 602):
            // a made-up effect of the stage adds to it and comes off again at the end.
            var willpowerEffect = new SessionEffectDefinition(WillpowerEffectNumber, [new EffectChange(EffectAttributeKind.Stat, (int)CharacterStat.Willpower, EffectChangeOperator.Add, 12)]);
            for (var i = 0; i < 5; i++) Frame();
            Probe(30, SessionSpellCaster.Hero, HeroStat, HeroList, CharacterStat.AcAdjustment, 1);
            Probe(77, SessionSpellCaster.Hero, HeroStat, HeroList, CharacterStat.Speed, 1);
            var granted = session.AddHeroEffect(session.Version, willpowerEffect, EffectCause.Item);
            Require(granted.Failure == GameSessionFailure.None, "the stage's willpower effect is added: " + granted.Failure);
            willpowerAdded = true;
            Probe(46, npcId, NpcStat, NpcList, CharacterStat.Strength, -1, SessionSpellCaster.Hero);
            Probe(76, npcId, NpcStat, NpcList, CharacterStat.Speed, -1, SessionSpellCaster.Hero);
            GD.Print("ARC-DEV-1148 native-smoke spell-effects PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Npc = npcId, Spells = report }));
        }
        finally
        {
            if (SpellCasterNow() is { } caster)
                foreach (var run in runs) if (_areaSession?.QuerySpellRuns()[run] is not null) caster.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            HeroRunsHost()?.LoseAll("SMOKE_SPELL_EFFECTS_END");
            if (_areaSession is { } now)
            {
                // A failure before a run's END must not leave the spells' entries on the NPC.
                if (npcTarget is { } left && now.QueryNpcEffects(left) is { } npcList)
                    foreach (var number in new[] { 154, 326, 57 }) { var count = npcList.Count(number); if (count > 0) now.RemoveNpcEffect(now.Version, left, number, count); }
                if (willpowerAdded) now.RemoveHeroEffect(now.Version, WillpowerEffectNumber, 1);
                RestoreHeroForSmoke(now, heroBefore);
            }
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
