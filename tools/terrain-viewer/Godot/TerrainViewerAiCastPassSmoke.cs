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
    // What one cast (an NPC's or the hero's) did, as the cast host's log and the run table show it (ARC-MAGIC-1835).
    private sealed record AiCastPassSide(bool Started, string? Why, int Frames, bool Stuck, SortedDictionary<string, int> Kinds, int Paid, bool Alive, bool Maintained);

    // ARC-MAGIC-1835 (E1-f follow-up, docs/research/magic-audit-res-103.md) native observation (stage "ai-cast-pass", only when named, map mode with --hero, AI on, a map with an NPC whose fields store a magick college level:
    // the Crash Site window): one pass of the NPC cast over every spell with an AI list entry (the 80 college spells carry up to 60 of them, in the lists Flee, Summon, Defensive, Offensive, the three heals, cure poison, fatigue recover
    // and Resurrect). For each spell and the target the chooser would give it (self for Flee, Summon and the heals, the hero for Offensive, the pass target or self for a Defensive spell by its second value, a dead ally for Resurrect)
    // the NPC performs the cast as a pass choice (`PerformChoice`, 0x4AC250) and the stage watches the run to its end: the run starts, the casting goal ends, the missile lands, nothing is left waiting. Then the hero casts the same
    // spell at the same kind of target through the cast host's script entry (`CastSpell` with `CastByScript`, free and resistable: the spell UI's cost and line-of-fire tests (0x57C110) would refuse the dear spells of a hero with 36 fatigue, which is not a difference of the run) and the two casts' events are compared (the kinds of event the run's components raised, the eye candy and summons counted; the payment, the
    // messages and the hero-only events set aside): every difference is printed as a DIFF row, which is a ledger row or an issue (docs/world/ai-host.md). The stage fails on
    // a stuck cast (a run that never leaves BEGIN, a goal or missile that never ends) or a run left behind, not on a difference; the NPC, the hero and the runs are restored for the later stages.
    private async Task RunAreaAiCastPassSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        { GD.Print("ARC-MAGIC-1835 native-smoke ai-cast-pass SKIP (needs --hero, an area session and its combat host)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-MAGIC-1835 ai-cast-pass smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_AI_CAST_PASS_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var inventory = _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        var npcs = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity), Fields: _combatHost.Fields(map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null && commands.ContainsKey(c.Row.Key) && c.Row.WorldX is not null && c.Row.WorldY is not null).ToArray();
        var candidates = npcs.Where(c => c.Fields?.Critter?.SpellTech.Take(SpellBookRules.Colleges).Any(rank => rank > 0) == true)
            .OrderByDescending(c => c.Build.Combat!.Level).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal).ToArray();
        ForgetNpcPlacesForSmoke(session, map.RuntimeMap);
        if (candidates.Length == 0) { GD.Print($"ARC-MAGIC-1835 native-smoke ai-cast-pass NONE map={map.RuntimeMap} (no NPC with magick levels)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var target = candidates[0];
        var entity = target.Holder.Entity;
        var npcLife = target.Build.Combat!.Target;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var scaled = false;
        var baseline = new HashSet<int>();
        var madeBefore = new HashSet<string>(StringComparer.Ordinal);
        (string Entity, SessionLifeTarget Life)? ally = null;
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
            caster!.RefusalDiagnostics = true;
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            foreach (var run in session.QuerySpellRuns().Runs) baseline.Add(run.Id);
            foreach (var made in session.QueryCreatedObjects(new(map.RuntimeMap))) madeBefore.Add(made.Entity);
            // The flags the session holds on the three critters now: a spell's own writes (paralysis, stone, entangle, invisibility ...) outlive a run that is freed, so each pair starts from these.
            var flagsOf = new Dictionary<string, SessionObjectFlags>();

            // The dead ally of the Resurrect list: the nearest other NPC of the map within eight cells of the caster, if any.
            {
                var best = int.MaxValue;
                foreach (var other in npcs.Where(c => c.Holder.Entity != entity))
                {
                    var cell = map.Chart.ToChart(new(other.Row.WorldX!.Value, other.Row.WorldY!.Value));
                    var distance = Math.Max(Math.Abs(cell.X - targetCell.X), Math.Abs(cell.Y - targetCell.Y));
                    if (distance <= 8 && distance < best) { best = distance; ally = (other.Holder.Entity, other.Build.Combat!.Target); }
                }
            }

            // The pairs: every spell of an AI list with the kind of target the chooser gives it ("self", "foe", "ally": a dead ally); a spell in two lists with the same kind is run once.
            var pairs = new List<(int Spell, string Kind, string Lists)>();
            foreach (var list in Enum.GetValues<AiActionList>())
                foreach (var spell in caster!.AiLists[list])
                {
                    var kind = list switch
                    {
                        AiActionList.Offensive => "foe",
                        AiActionList.Defensive => caster.AiLists.DefensiveSecond(spell) != 0 ? "foe" : "self",
                        AiActionList.Resurrect => "ally",
                        _ => "self",
                    };
                    var index = pairs.FindIndex(p => p.Spell == spell && p.Kind == kind);
                    if (index >= 0) pairs[index] = pairs[index] with { Lists = pairs[index].Lists + "+" + list };
                    else pairs.Add((spell, kind, list.ToString()));
                }
            Require(pairs.Count > 0, "the AI lists name spells");

            _areaSmokeTime *= 20; scaled = true; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            void Frame()
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            void RestoreFlags(string id)
            {
                if (!flagsOf.TryGetValue(id, out var was)) { flagsOf[id] = session.QueryObjectFlags(id); return; }
                var now = session.QueryObjectFlags(id);
                // An extra set bit is off again (the data lacks it unless the baseline had it cleared, which the write then keeps); an extra cleared bit is on again (the data has it unless the baseline had it set): with the data's
                // value given, the write drops the change instead of leaving a record of the opposite write, so the critter's written flags end as they began.
                void Field(SessionObjectFlagField field, int nowSet, int nowCleared, int wasSet, int wasCleared)
                {
                    if ((nowSet & ~wasSet & ~wasCleared) is not 0 and var setOff) session.WriteObjectFlag(session.Version, id, field, setOff, false, false);
                    if ((nowSet & ~wasSet & wasCleared) is not 0 and var setOffKept) session.WriteObjectFlag(session.Version, id, field, setOffKept, false);
                    if ((nowCleared & ~wasCleared & ~wasSet) is not 0 and var clearedOn) session.WriteObjectFlag(session.Version, id, field, clearedOn, true, true);
                    if ((nowCleared & ~wasCleared & wasSet) is not 0 and var clearedOnKept) session.WriteObjectFlag(session.Version, id, field, clearedOnKept, true);
                }
                Field(SessionObjectFlagField.Spell, now.SpellSet, now.SpellCleared, was.SpellSet, was.SpellCleared);
                Field(SessionObjectFlagField.Critter, now.CritterSet, now.CritterCleared, was.CritterSet, was.CritterCleared);
                Field(SessionObjectFlagField.Object, now.ObjectSet, now.ObjectCleared, was.ObjectSet, was.ObjectCleared);
                Field(SessionObjectFlagField.Critter2, now.Critter2Set, now.Critter2Cleared, was.Critter2Set, was.Critter2Cleared);
                Field(SessionObjectFlagField.Blit, now.BlitSet, now.BlitCleared, was.BlitSet, was.BlitCleared);
                Field(SessionObjectFlagField.Npc, now.NpcSet, now.NpcCleared, was.NpcSet, was.NpcCleared);
            }
            foreach (var id in ally is { } first ? new[] { entity, SessionSpellCaster.Hero, first.Entity } : new[] { entity, SessionSpellCaster.Hero }) RestoreFlags(id);
            // Leaving combat sets the NPC's DEMAINTAIN_SPELLS bit (0x4A8570 step 15 clears it at its next AI pass and ends the spells it maintains, the original's rule, ledger UE5), and the pass of a cast's busy NPC runs only after
            // the casting goal: a stale bit left by the reset would end the next cast's maintained run (ARC-MAGIC-2654). A flee exit sets no bit and clears FIGHTING, so the calm goes through it and the next exit is of an NPC that is not fighting.
            void CalmWithoutDemaintain(IEnumerable<string> ids)
            {
                foreach (var id in ids)
                    if (session.QueryDanger(id).State != DangerState.None)
                    {
                        session.SetDangerState(session.Version, new SessionDangerRequest { Entity = id, State = DangerState.Flee });
                        session.SetDangerState(session.Version, new SessionDangerRequest { Entity = id, State = DangerState.None });
                        Require(!session.QueryDanger(id).Flags.HasFlag(DangerFlags.Fighting) && !session.QueryDanger(id).Flags.HasFlag(DangerFlags.DemaintainSpells), $"the calm of {id} left FIGHTING or DEMAINTAIN_SPELLS set ({session.QueryDanger(id).Flags}; a flee request may have turned into combat focus)");
                    }
            }
            // The runs end as the original ends a caster's maintained spells (0x457450: by event, so an END_CALLBACK Destroy takes a summoned creature away; a forced end would leave it fighting the hero), then what is left is forced to end.
            void EndRuns()
            {
                foreach (var source in new[] { entity, SessionSpellCaster.Hero }) caster!.Act(new(SessionSpellRunActionKind.Demaintain, Object: source));
                for (var wait = 0; wait < 80 && session.QuerySpellRuns().Runs.Any(r => !baseline.Contains(r.Id)); wait++) Frame();
                foreach (var left in session.QuerySpellRuns().Runs.Select(r => r.Id).Where(id => !baseline.Contains(id)).ToArray()) caster!.Act(new(SessionSpellRunActionKind.ForceEnd, left));
            }
            // ARC-MAGIC-2655: a Summon's creature (and a wall's pieces) outlives its freed run and, left standing, crowds the NPC's tiles until a later Summon at the NPC finds no empty tile in radius 2 (the check's own BEGIN-area fallback, 0x4564E0),
            // so every object the pass made is destroyed again between two casts.
            void DestroyMade()
            {
                foreach (var made in session.QueryCreatedObjects(new(map.RuntimeMap)).Where(m => !madeBefore.Contains(m.Entity)).ToArray())
                    if (session.QueryCreatedObject(made.Entity) is not null) session.DestroyCreatedObject(session.Version, made.Entity);
            }
            // Between two casts: every run the stage made (or an NPC's own AI cast in the meantime) ends, the NPC goes calm and idle, then the three critters are whole, alive and free of what the spells wrote on them.
            void Reset()
            {
                var named = ally is { } held ? new[] { entity, held.Entity } : new[] { entity };
                EndRuns(); DestroyMade(); CalmWithoutDemaintain(named);
                for (var idle = 0; idle < 900 && (host!.RunOf(entity) is not null || _spellGoal is not null || _spellMissiles.Count > 0); idle++) Frame();
                EndRuns();
                foreach (var id in named.Append(SessionSpellCaster.Hero)) RestoreFlags(id);
                session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                if (ally is { } friend) session.Heal(session.Version, friend.Life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                CalmWithoutDemaintain(named);
            }
            AiCastPassSide Observe(string parent, int spell, Action cast, Func<bool> goalPlaying, Func<string?> why)
            {
                var before = session.QuerySpellRuns().Runs.Select(r => r.Id).ToHashSet();
                var logAt = caster!.Log.Count;
                var freeSlotsBefore = parent == SessionSpellCaster.Hero ? 0 : caster.AiFreeMaintainSlots(parent);
                cast();
                // A run that begins and is freed at once (a resisted cast) is no longer in the table, but its events name it.
                var run = session.QuerySpellRuns().Runs.Where(r => !before.Contains(r.Id) && r.Parent?.Id == parent && r.Spell == spell).Select(r => (int?)r.Id).FirstOrDefault()
                    ?? caster.Log.Skip(logAt).Where(e => e.Run >= 0 && !before.Contains(e.Run)).Select(e => (int?)e.Run).FirstOrDefault();
                if (run is not { } id) return new(false, why(), 0, false, [], 0, false, false);
                var frames = 0;
                for (; frames < 1500; frames++)
                {
                    if (session.QuerySpellRuns()[id] is not { Action: SpellRunAction.Begin } && !goalPlaying() && _spellMissiles.Count == 0) break;
                    Frame();
                }
                var stuck = frames >= 1500;
                for (var settle = 0; settle < 20; settle++) Frame();
                var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var paid = 0;
                // Run numbers are reused once a run is freed (the NPC's own AI casts in the same frames take the number again), so the run's events end with its first Freed.
                foreach (var e in caster.Log.Skip(logAt).Where(e => e.Run == id))
                {
                    kinds[e.Kind.ToString()] = kinds.GetValueOrDefault(e.Kind.ToString()) + 1;
                    if (e.Kind == SpellCastEventKind.FatiguePaid) paid += e.Value;
                    if (e.Kind == SpellCastEventKind.Freed) break;
                }
                // The hero's maintained run sits on its maintain bar; an NPC has no bar, its run holds one of the maintenance slots its intelligence gives (0x450B40, the AI's eligibility test counts them).
                var held = parent == SessionSpellCaster.Hero ? session.QueryMaintainBar().SlotOf(id) is not null : session.QuerySpellRuns()[id] is not null && caster.AiFreeMaintainSlots(parent) < freeSlotsBefore;
                return new(true, null, frames, stuck, kinds, paid, session.QuerySpellRuns()[id] is not null, held);
            }
            // The kinds a difference of caster or target explains by themselves (the payment, the messages for the local player, the hero-only effect and identify events, experience) are set aside; damage and heal are compared by presence.
            var aside = new HashSet<string> { "FatiguePaid", "Message", "Freed", "ExperienceGiven", "ExperienceNotSaved", "EffectAdded", "EffectRemoved", "EffectNotSaved", "IdentifyCritter", "IdentifyContainer", "IdentifyItem", "ItemIdentified", "ItemIdentifyNotSaved" };
            var byPresence = new HashSet<string> { "Damaged", "Resisted", "Healed", "DamageVetoed" };
            static string Show(AiCastPassSide s) => s.Started
                ? $"started frames={s.Frames}{(s.Stuck ? " STUCK" : "")} alive={s.Alive} maintained={s.Maintained} paid={s.Paid} [{string.Join(",", s.Kinds.Select(k => $"{k.Key}:{k.Value}"))}]"
                : "no run (" + s.Why + ")";

            var maintainedSelf = new HashSet<int> { 10, 15, 20, 25, 30, 40, 45 };
            int ran = 0, differs = 0, notRun = 0, refused = 0, notComparable = 0;
            var differing = new List<int>();
            var unexplained = new List<int>();
            foreach (var (spell, kind, lists) in pairs)
            {
                // Resurrect needs a dead ally; without one nearby the pair is not run.
                if (kind == "ally" && ally is null) { notRun++; GD.Print($"ARC-MAGIC-1835 ai-cast-pass spell={spell} lists={lists} target={kind} NOT-RUN (no other NPC within eight cells to be the dead ally)"); continue; }
                string HeroTarget() => kind == "ally" ? ally!.Value.Entity : SessionSpellCaster.Hero;
                void Kill() { if (kind == "ally") session.ApplyDamage(session.Version, ally!.Value.Life, new LifeDamage { HitPoints = 1_000_000 }); }
                AiCastPassSide NpcCast(string at)
                {
                    host!.HeroAttacked(entity);
                    return Observe(entity, spell, () => host.PerformChoice(entity, new AiPassChoice(AiPassAction.Cast, spell, 0, null, at)),
                        () => host.RunOf(entity)?.Frames.Any(g => g.SpellRun is not null) == true, () => host.TraceOf(entity)?.Cast is { } trace && trace.StartsWith($"cast {spell} ", StringComparison.Ordinal) ? trace : "no cast of this spell in the trace (the perform did not reach the cast)");
                }

                // The NPC's own cast, at the target the chooser would give it (its run must settle and leave nothing); the comparison with the hero's cast is between the same target (the hero: a cast at self for a self spell), so
                // that only the caster differs (a PC target and an NPC target are written differently, SO and XR5): an NPC cast at the hero too when its own target is another.
                // ARC-MAGIC-2655: a healing or cure spell's area asks for a damaged or poisoned target (0x100000, 0x400000: the invocation check refuses a whole one), as the chooser's own pick would be, so its target is hurt first.
                void Hurt(SessionLifeTarget life)
                {
                    if (lists.Contains("Healing", StringComparison.Ordinal)) session.ApplyDamage(session.Version, life, new LifeDamage { HitPoints = 10 });
                    if (lists.Contains("CurePoison", StringComparison.Ordinal)) session.ApplyDamage(session.Version, life, new LifeDamage { Poison = 5 });
                }
                Reset(); Kill(); if (kind == "self") Hurt(npcLife);
                var npc = NpcCast(kind == "self" ? entity : kind == "foe" ? AiSchedulerRules.Pc : ally!.Value.Entity);
                AiCastPassSide npcAtHero = npc;
                if (kind == "self") { Reset(); Hurt(SessionLifeTarget.Hero); npcAtHero = NpcCast(AiSchedulerRules.Pc); }
                Reset(); Kill(); if (kind == "self") Hurt(SessionLifeTarget.Hero);
                var heroCast = Observe(SessionSpellCaster.Hero, spell, () => CastSpell(spell, HeroTarget(), SpellLocation.None, null, c => c.CastByScript(SessionSpellCaster.Hero, spell, HeroTarget(), true, false)),
                    () => _spellGoal is not null, () => _spellNotice + " | hero " + session.QueryHeroLife()?.Status);
                FlushSpellLog(null);
                Reset();

                // The NPC's invocation check tests its BEGIN area for a parent that is no PC (0x4564E0) and the hero's does not, so an NPC refusal is no difference of the run: it is counted apart (an issue names why). A hero run that
                // the release's re-check freed with nothing done (a tile spell cast at an object) has nothing to compare.
                var differences = new List<string>();
                string? apart = null;
                var npcComponents = npcAtHero.Kinds.Keys.Where(k => !aside.Contains(k)).Any();
                var heroComponents = heroCast.Kinds.Keys.Where(k => !aside.Contains(k)).Any();
                if (!npcAtHero.Started && heroCast.Started && npcAtHero.Why?.Contains("check NoValidTarget", StringComparison.Ordinal) == true)
                {
                    var reason = ExplainBeginRefusal(npcAtHero.Why);
                    apart = "NPC-REFUSED (" + (reason ?? "UNEXPLAINED") + ") " + npcAtHero.Why.Split(" | ")[0].Trim(); refused++;
                    if (reason is null) unexplained.Add(spell);
                }
                else if (npcAtHero.Started && heroCast.Started && npcComponents && !heroComponents) { apart = "HERO-NOT-COMPARABLE (its run was freed at the release's re-check with nothing done)"; notComparable++; }
                else if (npcAtHero.Started != heroCast.Started) differences.Add($"started npc={npcAtHero.Started} hero={heroCast.Started}");
                else if (npcAtHero.Started)
                {
                    foreach (var name in npcAtHero.Kinds.Keys.Union(heroCast.Kinds.Keys).Where(k => !aside.Contains(k)).Order(StringComparer.Ordinal))
                    {
                        var a = npcAtHero.Kinds.GetValueOrDefault(name); var b = heroCast.Kinds.GetValueOrDefault(name);
                        if (byPresence.Contains(name) ? (a > 0) != (b > 0) : a != b) differences.Add($"{name} npc={a} hero={b}");
                    }
                    if (npcAtHero.Alive != heroCast.Alive || npcAtHero.Maintained != heroCast.Maintained) differences.Add($"after the cast alive npc={npcAtHero.Alive} hero={heroCast.Alive}, maintained npc={npcAtHero.Maintained} hero={heroCast.Maintained}");
                }
                ran++;
                if (differences.Count > 0) { differs++; differing.Add(spell); }
                GD.Print($"ARC-MAGIC-1835 ai-cast-pass spell={spell} lists={lists} target={kind} npc: {Show(npc)}" + (ReferenceEquals(npc, npcAtHero) ? "" : " | npc at the hero: " + Show(npcAtHero))
                    + " | hero: " + Show(heroCast) + (apart is not null ? " | " + apart : "") + (differences.Count > 0 ? " | DIFF " + string.Join("; ", differences) : ""));
                Require(!npcAtHero.Stuck, $"the NPC's cast of spell {spell} at the hero never settled: {Show(npcAtHero)}");
                Require(!npc.Stuck, $"the NPC's cast of spell {spell} never settled: {Show(npc)}");
                Require(!heroCast.Stuck, $"the hero's cast of spell {spell} never settled: {Show(heroCast)}");
                // ARC-MAGIC-2654 (ledger AP4): a maintained Defensive spell an NPC casts at itself keeps its run and its maintenance slot while the NPC fights; only leaving combat ends it (the AI gate's demaintain, UE5).
                if (maintainedSelf.Contains(spell) && kind == "self") Require(npc.Started && npc.Alive && npc.Maintained, $"the NPC's maintained spell {spell} cast at itself keeps its run and slot: {Show(npc)}");
            }
            var leftOver = session.QuerySpellRuns().Runs.Where(r => !baseline.Contains(r.Id)).Select(r => r.Id).ToArray();
            Require(leftOver.Length == 0, $"no run is left after the pass: {string.Join(",", leftOver)}");
            Require(ran > 0, "at least one pair ran");
            Require(unexplained.Count == 0, $"every NPC refusal by the BEGIN-area test is explained by an area bit the original tests too (ARC-MAGIC-2655); not explained: {string.Join(",", unexplained)}");
            GD.Print("ARC-MAGIC-1835 native-smoke ai-cast-pass PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                map.RuntimeMap, Caster = target.Row.PrototypeNumber, Pairs = pairs.Count, Ran = ran, NotRun = notRun, NpcRefused = refused, NotComparable = notComparable, DifferingPairs = differs, DifferingSpells = differing.Distinct(),
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            if (SpellCasterNow() is { } diagnosed) diagnosed.RefusalDiagnostics = false;
            foreach (var left in session.QuerySpellRuns().Runs.Select(r => r.Id).Where(id => !baseline.Contains(id)).ToArray()) SpellCasterNow()?.Act(new(SessionSpellRunActionKind.ForceEnd, left));
            session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (ally is { } friend) session.Heal(session.Version, friend.Life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
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
    // ARC-MAGIC-2655: the invocation check (0x4564E0) tests a non-PC parent's target against the spell's BEGIN mask (IV6); the refusal note names the mask and the bits whose removal alone lets the target pass. The reasons below are rules of
    // the area test (0x4F2D20) that the original applies to the same target: they explain a refusal; null is a refusal no listed bit explains.
    private static string? ExplainBeginRefusal(string why)
    {
        var match = System.Text.RegularExpressions.Regex.Match(why, "begin-mask=([0-9A-F]+) failing=([0-9A-F]+)");
        if (!match.Success) return null;
        var mask = Convert.ToUInt64(match.Groups[1].Value, 16);
        var failing = Convert.ToUInt64(match.Groups[2].Value, 16);
        var reasons = new List<string>();
        // A mask whose only object bit is the self bit (low 0x15 = 0x1) and that also holds tile bits (Flee 3) has no single bit to remove: with the self bit gone the tile tests fail on the missing tile, so the mask alone names the reason.
        if ((failing & 0x1) != 0 || (failing == 0 && ((uint)mask & 0x15) == 0x1)) reasons.Add("self-only area, target is not the caster");
        if ((failing & 0x20000000) != 0) reasons.Add("area excludes PCs, target is the PC");
        if ((failing & (0xC000000UL << 32)) != 0) reasons.Add("party-only area, target has no PC leader");
        if ((failing & 0x100000) != 0) reasons.Add("area needs a damaged target");
        if ((failing & 0x400000) != 0) reasons.Add("area needs a poisoned target");
        if ((failing & 0x200000) != 0) reasons.Add("area needs a damaged or poisoned target");
        if ((failing & 0x1000000) != 0) reasons.Add("area needs a flesh target");
        return reasons.Count == 0 ? null : string.Join("; ", reasons);
    }
}
