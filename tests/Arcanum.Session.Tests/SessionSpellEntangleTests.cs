using System.Collections.Immutable;
using Arcanum.Data.Characters;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-DEV-303 (MT13, ledger EF9, RS10): the Effect component on the hero and the entangle's re-resistance on each hit, through the cast host. An invented map (the hero and an NPC that casts)
// and an invented Entangle-like record: ObjFlag entangled and Effect 62 (speed −5) at BEGIN, both undone at END, a strength save whose modifier the tests choose (+100: a d20 never above
// the save, so the critter always resists again; −100: never).
[TestClass]
public sealed class SessionSpellEntangleTests
{
    private const string Npc = "npc-a";
    // ARC-MAGIC-027: a victim named as the session names an NPC (the session's effect list takes only such entities).
    private const string Victim = "oid:victim";
    private const int Number = 51, Quiet = 52;

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Npc => new(id, SpellTargetObject.Npc, new(7, 5), HitPoints: 10),
            Victim => new(id, SpellTargetObject.Npc, new(8, 5), HitPoints: 10),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        // The NPC is a critter of the map with the stats of the combat fixtures (intelligence 10), so it can cast and be entangled.
        public SpellCastCritter? Critter(string entity) => entity is Npc or Victim ? new(SessionCritterCombat.From(entity, AttackStateTests.Npc(Thinker())).Combat!, 0, 0, 0, 0, 0) : null;

        // The fixtures' stats with an intelligence of 10 (a critter casts from 5).
        private static int[] Thinker() { var stats = AttackStateTests.Stats(); stats[4] = 10; return stats; }
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static SpellComponent Part(SpellAction action, SpellComponentType type, SpellComponentData data) => new(action, type, new(0x4, 0, 0, 0, -1), 0, 0, data);

    // Spell 51: entangled and the speed effect at BEGIN, undone at END; a maintained spell with a strength save of the given modifier.
    private static SessionSpellRecord Entangle(int modifier, int effectCount = 1, int scaled = 0) =>
        SessionSpellRecords.Translate(Record(number: Number, beginTargets: 0x4, info: SpellInfo.Aggressive, components:
            [Part(SpellAction.Begin, SpellComponentType.ObjFlag, new ObjectFlagData(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.EntangledSpellFlag, true)),
             Part(SpellAction.Begin, SpellComponentType.Effect, new EffectData(62, true, effectCount, 6, scaled)),
             Part(SpellAction.End, SpellComponentType.ObjFlag, new ObjectFlagData(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.EntangledSpellFlag, false)),
             Part(SpellAction.End, SpellComponentType.Effect, new EffectData(62, false, effectCount, 6, scaled))])
        with { Cost = 0, MaintainCost = 0, MinimumWillpower = 0, MaintainPeriod = 2, Range = 99, DurationSeconds = 30, DurationStat = -1, DurationTriggerCount = 0, ResistStat = 0, ResistModifier = modifier,
            NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0,
            SpellFlagsSet = SessionSpellCaster.EntangledSpellFlag,
            Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    // A spell with no components and no target area: its action processes, meets no target object and leaves the cached target type as the original does (−1).
    private static SessionSpellRecord NoTarget() =>
        SessionSpellRecords.Translate(Record(number: Quiet, beginTargets: 0, info: SpellInfo.Friendly, components: [])
        with { Cost = 0, MinimumWillpower = 0, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0,
            DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0 });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Setup(RuleMode mode, params SessionSpellRecord[] records)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        owner.SelectRules(mode);
        var caster = new SessionSpellCaster(owner, n => records.FirstOrDefault(r => r.Data.Number == n), new Map(owner), new Events())
        {
            EffectDefinitions = n => n == 62 ? new SessionEffectDefinition(62, [new EffectChange(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, -5)]) : null,
        };
        return (owner, caster);
    }

    // A free run of the record at the hero with the given parent, begun.
    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string parent = Npc, string target = SessionSpellCaster.Hero)
    {
        // A critter that is both parent and source needs the intelligence to cast, which the invented map does not give: the other one is the source.
        var by = caster.Facts(parent)!;
        var source = caster.Facts(parent == SessionSpellCaster.Hero ? Npc : SessionSpellCaster.Hero)!;
        var to = caster.Facts(target)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, by, source, to, to.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        caster.Act(new(SessionSpellRunActionKind.Begin, run));
        return run;
    }

    private static void Pass(GameSessionOwner owner, SessionSpellCaster caster)
    {
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 5, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        caster.ProcessEvents();
    }

    [TestMethod]
    public void The_effect_component_adds_the_effect_at_begin_and_the_typed_removal_takes_it_at_end()
    {
        var record = Entangle(-100);
        var (owner, caster) = Setup(RuleMode.Steamwoven, record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), "ObjFlag and Effect are both taken");
        Assert.AreEqual(1, owner.QueryHeroEffects()!.Count(62));
        Assert.IsTrue(owner.QueryHeroEffects()!.Effects.Any(e => e is { Number: 62, Cause: Arcanum.Simulation.Character.EffectCause.Spell }));
        Assert.IsTrue(caster.Log.Contains(new SpellCastEvent(SpellCastEventKind.EffectAdded, run, 62, SessionSpellCaster.Hero, 1)));
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero));
        caster.Act(new(SessionSpellRunActionKind.Interrupt, run));
        Assert.AreEqual(0, owner.QueryHeroEffects()!.Count(62), "the END's typed removal");
        Assert.IsTrue(caster.Log.Contains(new SpellCastEvent(SpellCastEventKind.EffectRemoved, run, 62, SessionSpellCaster.Hero, 1)));
        Assert.AreEqual((false, false, false), caster.Holds(SessionSpellCaster.Hero));
    }

    [TestMethod]
    public void The_effect_component_works_on_an_NPC_victim_through_its_own_list()
    {
        // ARC-MAGIC-027: since ARC-DEV-348 an NPC has an effect list and the component takes it (it was "not ready" before): the hero entangles the NPC, its list holds effect 62 (speed -5)
        // until the END's typed removal takes it, and the hero's own list is untouched.
        var record = Entangle(-100);
        var (owner, caster) = Setup(RuleMode.Steamwoven, record);
        using var _ = owner;
        var npc = new Map(owner).Critter(Victim)!.Combat;
        var run = Cast(owner, caster, record, parent: SessionSpellCaster.Hero, target: Victim);
        Assert.IsFalse(caster.Log.Any(e => e.Kind is SpellCastEventKind.ComponentNotReady or SpellCastEventKind.EffectNotSaved), string.Join(",", caster.Log.Select(e => e.Kind)));
        Assert.AreEqual(1, owner.QueryNpcEffects(npc.Target)!.Count(62));
        Assert.AreEqual(0, owner.QueryHeroEffects()!.Count(62));
        Assert.IsTrue(caster.Log.Contains(new SpellCastEvent(SpellCastEventKind.EffectAdded, run, 62, Victim, 1)));
        Assert.AreEqual(npc.EffectiveStat(Arcanum.Simulation.Character.CharacterStat.Speed, LifeState.Unharmed) is var plain ? Math.Max(plain - 5, 1) : 0,
            owner.WithNpcEffects(npc).EffectiveStat(Arcanum.Simulation.Character.CharacterStat.Speed, LifeState.Unharmed), "the victim's speed reads under the list");
        caster.Act(new(SessionSpellRunActionKind.Interrupt, run));
        Assert.AreEqual(0, owner.QueryNpcEffects(npc.Target)!.Count(62), "the END's typed removal");
        Assert.IsTrue(caster.Log.Contains(new SpellCastEvent(SpellCastEventKind.EffectRemoved, run, 62, Victim, 1)));
    }

    [TestMethod]
    public void The_count_grows_with_the_scaled_parent_aptitude_and_a_count_of_nothing_adds_nothing()
    {
        // A hero parent: Count 2 and Scaled 50 are 2 + 50 × the parent's aptitude / 100 entries, toward zero; the END removes as many.
        var record = Entangle(-100, effectCount: 2, scaled: 50);
        var (owner, caster) = Setup(RuleMode.Steamwoven, record);
        using var _ = owner;
        var aptitude = caster.RunObject(SessionSpellCaster.Hero)!.Aptitude;
        var expected = 2 + 50 * aptitude / 100;
        var run = Cast(owner, caster, record, parent: SessionSpellCaster.Hero, target: SessionSpellCaster.Hero);
        Assert.AreEqual(Math.Max(expected, 0), owner.QueryHeroEffects()!.Count(62), $"aptitude {aptitude}");
        caster.Act(new(SessionSpellRunActionKind.Interrupt, run));
        Assert.AreEqual(0, owner.QueryHeroEffects()!.Count(62));
        var none = Entangle(-100, effectCount: 0);
        var (o, c) = Setup(RuleMode.Steamwoven, none);
        using var __ = o;
        Cast(o, c, none);
        Assert.AreEqual(0, o.QueryHeroEffects()!.Count(62), "a count of 0 runs no add");
        Assert.IsFalse(c.Log.Any(e => e.Kind is SpellCastEventKind.EffectAdded or SpellCastEventKind.EffectNotSaved));
    }

    [TestMethod]
    public void An_effect_without_a_rule_is_not_ready_and_the_session_refusal_is_logged()
    {
        var record = Entangle(-100);
        var (owner, caster) = Setup(RuleMode.Steamwoven, record);
        using var _ = owner;
        caster.EffectDefinitions = null;
        var run = Cast(owner, caster, record);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ComponentNotReady, run, (int)SpellComponentType.Effect, SessionSpellCaster.Hero),
            caster.Log.Single(e => e.Kind == SpellCastEventKind.ComponentNotReady));
        // An innate number is refused by the session and logged; the spell goes on.
        var (o, c) = Setup(RuleMode.Steamwoven, record);
        using var __ = o;
        var raceEffect = o.QueryHeroEffects()!.Innate[0].Number;
        c.EffectDefinitions = n => new SessionEffectDefinition(raceEffect, [new EffectChange(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, -5)]);
        var innate = Cast(o, c, record);
        Assert.IsTrue(c.Log.Any(e => e.Kind == SpellCastEventKind.EffectNotSaved && e.Run == innate), "the race effect is the hero's own");
        Assert.AreEqual((true, false, false), c.Holds(SessionSpellCaster.Hero));
    }

    [TestMethod]
    public void A_hit_asks_the_entangled_critters_save_again_and_a_made_save_ends_the_run_by_event()
    {
        var record = Entangle(100);
        var (owner, caster) = Setup(RuleMode.Steamwoven, record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero), "a save of 100 + strength: the BEGIN's own save was made, a maintained spell still applies");
        Assert.IsTrue(caster.CritterHit(SessionSpellCaster.Hero), "every d20 is at most the save: it resists again");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.EntangleBroken && e.Run == run && e.Object == SessionSpellCaster.Hero));
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Resisted && e.Run == run && e.Object == SessionSpellCaster.Hero), "'resisted' plays (0x45A520)");
        // 0x4571E0 queues the interrupt 1 ms later: the hold lasts until the event pass.
        Assert.IsNotNull(owner.QuerySpellRuns()[run]);
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero));
        Pass(owner, caster);
        Assert.IsNull(owner.QuerySpellRuns()[run], "the run ended");
        Assert.AreEqual((false, false, false), caster.Holds(SessionSpellCaster.Hero), "its END cleared the flag");
        Assert.AreEqual(0, owner.QueryHeroEffects()!.Count(62), "and took the speed effect off");
        Assert.IsFalse(caster.CritterHit(SessionSpellCaster.Hero), "a critter that is not entangled is not asked");
    }

    [TestMethod]
    public void A_weapons_own_hit_effect_that_plays_ends_the_attack_end_before_the_blood_and_the_hit_effect()
    {
        // ARC-RENDER-1990 (0x4B6930 → 0x4377C0): the attack end asks the weapon's own hit effect first; when it plays, neither the blood nor the hit effect (the entangled victim's save) follows.
        var hit = new AttackDamage(DamageRecipient.Target, new(5, 0, 0, 0, 0), new(5, 0, 0, 0, 0), 5, 0, 0, DamageMultiplier.None, false, CriticalHitEffects.None, CriticalMissEffects.None, false, DamagePrevention.None);
        var asked = new List<(string Victim, int Entry, int Limit)>();
        foreach (var (plays, weapon, veto) in new (bool, int?, bool)[] { (true, 6060, true), (false, 6060, false), (true, null, false), (true, 6040, false) })
        {
            var record = Entangle(100);
            var (owner, caster) = Setup(RuleMode.Steamwoven, record);
            using var _ = owner;
            var run = Cast(owner, caster, record);
            asked.Clear();
            caster.WeaponEyeCandyAdd = (victim, entry, limit) => { asked.Add((victim, entry, limit)); return plays; };
            var ended = caster.AttackEnd(SessionSpellCaster.Hero, hit, false, weapon);
            var broken = caster.Log.Any(e => e.Kind == SpellCastEventKind.EntangleBroken && e.Run == run);
            Assert.AreEqual(!veto, ended, $"plays {plays}, weapon {weapon}: the hit effect asks the save again unless the weapon's effect played");
            Assert.AreEqual(!veto, broken);
            Assert.AreEqual(!veto, caster.Log.Any(e => e.Kind == SpellCastEventKind.HitGore), "the blood follows unless it played");
            // The Tesla Rod (prototype 6060: basic 6040, 11 above the first weapon, kind 3) asks its own entry with the limit 2; a weapon below the list's first and no weapon ask nothing.
            if (weapon == 6060) CollectionAssert.AreEqual(new[] { (SessionSpellCaster.Hero, EyeCandyEntryRef.Make(EyeCandyList.Weapon, 58)!.Value, 2) }, asked);
            else Assert.AreEqual(0, asked.Count);
        }
    }

    [TestMethod]
    public void A_missed_save_the_caster_and_a_critter_without_the_flag_keep_everything_as_it_is()
    {
        var record = Entangle(-100);
        var (owner, caster) = Setup(RuleMode.Steamwoven, record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.IsFalse(caster.CritterHit(SessionSpellCaster.Hero), "a save of at most 0 is never made");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.EntangleHeld && e.Run == run));
        Pass(owner, caster);
        Assert.IsNotNull(owner.QuerySpellRuns()[run]);
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero));
        Assert.IsFalse(caster.CritterHit(Npc), "an NPC without the flag");
        // The caster of the entangle is never asked: a run whose parent is the target holds.
        var own = Entangle(100);
        var (o, c) = Setup(RuleMode.Steamwoven, own);
        using var __ = o;
        var self = Cast(o, c, own, parent: SessionSpellCaster.Hero);
        Assert.IsFalse(c.CritterHit(SessionSpellCaster.Hero));
        Assert.IsNotNull(o.QuerySpellRuns()[self]);
    }

    [TestMethod]
    public void Mg48_the_original_reads_the_type_the_last_run_action_cached_and_steamwoven_the_critters_own()
    {
        // A run action that meets no target object after the entangle resets the cached type: in the Original setting the save is then skipped and the hold stays on every hit.
        foreach (var (mode, resisted) in new[] { (RuleMode.Steamwoven, true), (RuleMode.Original, false) })
        {
            var record = Entangle(100);
            var (owner, caster) = Setup(mode, record, NoTarget());
            using var _ = owner;
            var run = Cast(owner, caster, record);
            var other = Cast(owner, caster, NoTarget(), target: Npc);
            Assert.IsNotNull(owner.QuerySpellRuns()[run]);
            Assert.AreEqual(resisted, caster.CritterHit(SessionSpellCaster.Hero), mode.ToString());
        }
        // Right after the entangle's own BEGIN the cache holds the hero's type (a PC): the Original setting asks the save too.
        var entangle = Entangle(100);
        var (o, c) = Setup(RuleMode.Original, entangle);
        using var __ = o;
        Cast(o, c, entangle);
        Assert.IsTrue(c.CritterHit(SessionSpellCaster.Hero));
    }

    // ARC-MAGIC-045 (SFR3, ER4): a maintained-free spell that writes one spell flag at BEGIN and takes it off at END; <paramref name="provides"/> says whether its record names the flag in its
    // set-flags word (what the run finder 0x459170 reads), so a run that writes the bit without providing it is a bit with no run to find.
    private static SessionSpellRecord Flagged(int number, uint flag, bool provides) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Friendly, components:
            [Part(SpellAction.Begin, SpellComponentType.ObjFlag, new ObjectFlagData(SessionSpellCaster.SpellFlagsField, flag, true)),
             Part(SpellAction.End, SpellComponentType.ObjFlag, new ObjectFlagData(SessionSpellCaster.SpellFlagsField, flag, false))])
        with { Cost = 0, MaintainCost = 0, MinimumWillpower = 0, MaintainPeriod = 2, Range = 99, DurationSeconds = 30, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = provides ? flag : 0u,
            Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static IEnumerable<SpellCastEvent> HitEyeCandies(SessionSpellCaster caster, int from) => caster.Log.Skip(from).Where(e => e.Kind == SpellCastEventKind.EyeCandyAdded);

    [TestMethod]
    public void A_hit_on_a_shielded_critter_plays_the_run_spells_hit_eye_candy_and_changes_nothing_else()
    {
        // 0x459A30 (SFR3): the first active run whose record sets 0x400 and whose target is the critter: entry 6 × spell + 5, play flags 0, caused by the run.
        const int shield = 30;
        var record = Flagged(shield, SessionSpellCaster.ShieldedSpellFlag, provides: true);
        var (owner, caster) = Setup(RuleMode.Steamwoven, record);
        using var _ = owner;
        Assert.IsFalse(caster.CritterHit(SessionSpellCaster.Hero), "no flag: nothing happens");
        var run = Cast(owner, caster, record);
        var hp = owner.QueryHeroLife()!.Status.HitPoints;
        var before = caster.Log.Count;
        Assert.IsFalse(caster.CritterHit(SessionSpellCaster.Hero), "the shield is no entangle: the hit ends nothing");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.EyeCandyAdded, run, shield * 6 + 5, SessionSpellCaster.Hero, 0), HitEyeCandies(caster, before).Single());
        Assert.AreEqual(1, caster.Log.Count - before, "no resisted, held or broken event");
        Assert.AreEqual(hp, owner.QueryHeroLife()!.Status.HitPoints);
        Assert.IsNotNull(owner.QuerySpellRuns()[run]);
        // A second hit plays it again (every hit reaches the effect).
        caster.CritterHit(SessionSpellCaster.Hero);
        Assert.AreEqual(2, HitEyeCandies(caster, before).Count());
        // The END clears the flag: no more eye candy.
        caster.Act(new(SessionSpellRunActionKind.Interrupt, run));
        before = caster.Log.Count;
        caster.CritterHit(SessionSpellCaster.Hero);
        Assert.AreEqual(0, HitEyeCandies(caster, before).Count());
    }

    [TestMethod]
    public void A_shielded_bit_no_run_provides_plays_nothing()
    {
        // The original prints its assertion text and plays nothing when the run finder finds no run.
        var record = Flagged(30, SessionSpellCaster.ShieldedSpellFlag, provides: false);
        var (owner, caster) = Setup(RuleMode.Steamwoven, record);
        using var _ = owner;
        Cast(owner, caster, record);
        var before = caster.Log.Count;
        Assert.IsFalse(caster.CritterHit(SessionSpellCaster.Hero));
        Assert.AreEqual(0, HitEyeCandies(caster, before).Count());
    }

    [TestMethod]
    public void A_hit_on_a_critter_with_magnetic_inversion_plays_the_run_spells_entry_or_the_fixed_one_with_no_run()
    {
        const int spell = 182; const uint flag = SessionSpellCaster.MagneticInversionSpellFlag;
        var provided = Flagged(spell, flag, provides: true);
        var (owner, caster) = Setup(RuleMode.Steamwoven, provided);
        using var _ = owner;
        var run = Cast(owner, caster, provided);
        var before = caster.Log.Count;
        caster.CritterHit(SessionSpellCaster.Hero);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.EyeCandyAdded, run, spell * 6 + 5, SessionSpellCaster.Hero, 0), HitEyeCandies(caster, before).Single());
        // The bit with no providing run: the fixed entry 0x449, no run.
        var bare = Flagged(spell, flag, provides: false);
        var (o, c) = Setup(RuleMode.Steamwoven, bare);
        using var __ = o;
        Cast(o, c, bare);
        before = c.Log.Count;
        c.CritterHit(SessionSpellCaster.Hero);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.EyeCandyAdded, -1, 0x449, SessionSpellCaster.Hero, 0), HitEyeCandies(c, before).Single());
    }

    [TestMethod]
    public void A_shielded_and_entangled_critter_plays_the_shield_eye_candy_before_the_entangle_save()
    {
        var shield = Flagged(30, SessionSpellCaster.ShieldedSpellFlag, provides: true);
        var entangle = Entangle(100);
        var (owner, caster) = Setup(RuleMode.Steamwoven, shield, entangle);
        using var _ = owner;
        Cast(owner, caster, shield);
        var held = Cast(owner, caster, entangle);
        var before = caster.Log.Count;
        Assert.IsTrue(caster.CritterHit(SessionSpellCaster.Hero));
        var kinds = caster.Log.Skip(before).Select(e => e.Kind).ToList();
        Assert.IsTrue(kinds.IndexOf(SpellCastEventKind.EyeCandyAdded) is var eye and >= 0 && eye < kinds.IndexOf(SpellCastEventKind.EntangleBroken), string.Join(",", kinds));
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.EntangleBroken && e.Run == held));
    }
}
