using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Magic;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1295 (ledger RM3; exe read 0x452D80, 0x4B0740, 0x4B0980, 0x4B0FC0, 0x4B0FD0, 0x407340, 0x4072D0): the TraitIdx component adds its value to an NPC's stored base stat or resistance through the cast host.
// Records, ids and numbers are invented; the NPC is an oid: entity whose stored values the host map gives.
[TestClass]
public sealed partial class SessionSpellCasterTraitIndexTests
{
    private const string Hero = SessionSpellCaster.Hero, Rat = "oid:rat", Box = "oid:box";
    private static readonly SpellLocation HeroTile = new(5, 5), FarTile = new(8, 5);
    private static readonly SpellArea Aim = new(0x4, 0, 0, 0, -1);
    private const int StatField = 220, ResistField = 31;

    // The NPC's stored fields: a human with dexterity 10, a skill keyed on dexterity at 2 points (base level 8), resistances 10, 0, 0, 0, 5.
    private static CombatFields RatFields() => AttackStateTests.Npc(AttackStateTests.Stats(dexterity: 10), resistances: [10, 0, 0, 0, 5], skills: [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    private sealed class Map(GameSessionOwner owner, CombatFields fields) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Rat => new(id, SpellTargetObject.Npc, FarTile, HitPoints: 10),
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
        // The host gives the stored values as the map data has them (the session lays what it wrote over them).
        public SpellCastCritter? Critter(string entity)
        {
            if (entity != Rat) return null;
            var critter = fields.Critter!;
            return new(SessionCritterCombat.From(entity, fields).Combat!, fields.Object.Resistances[4], 0, 0, 0, 0)
            {
                StoredStats = critter.BaseStats, StoredResistances = fields.Object.Resistances, StoredBasicSkills = critter.BasicSkills, StoredTechSkills = critter.TechSkills,
                Book = SpellBookRules.FromFields(critter.SpellTech), StoredDegrees = critter.SpellTech.Count >= 25 ? [.. critter.SpellTech.Skip(17)] : null,
            };
        }
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static SessionSpellRecord Spell(int number, int field, int index, int value) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral,
                components: [new(SpellAction.Begin, SpellComponentType.TraitIdx, Aim, 0, 0, new TraitIndexData(field, index, value))])
            with { Cost = 0, MaintainCost = 0, MaintainPeriod = 0, Range = 99, DurationSeconds = 30, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    // Resistance 1 (fire) +25, resistance 4 (magic) -200, dexterity -1 / -2 / -20 / +5 / +5 again, intelligence +1, a derived stat (9), charisma +1, an index outside field 220.
    private const int FirePlus = 200, MagicMinus = 201, DexMinusOne = 210, DexMinusTwo = 211, DexMinusMany = 212, DexPlusFive = 213, IntPlus = 214, DerivedPlus = 215, CharismaPlus = 216, BadIndex = 217, BadField = 218, StrengthPlus = 219, LevelPlus = 220, StrengthMinusOne = 221, StrengthMinusTen = 222;

    // Intelligence -1 / -2 / -10, willpower -1 / -2, level -5 / +100, experience, alignment, fate points, unspent points and stat 25 by large amounts.
    private static SessionSpellRecord IntMinusOne => Spell(230, StatField, 4, -1);
    private static SessionSpellRecord IntMinusTwo => Spell(231, StatField, 4, -2);
    private static SessionSpellRecord IntMinusMany => Spell(232, StatField, 4, -10);
    private static SessionSpellRecord WillMinusOne => Spell(233, StatField, 6, -1);
    private static SessionSpellRecord WillMinusTwo => Spell(234, StatField, 6, -2);
    private static SessionSpellRecord LevelMinusMany => Spell(235, StatField, 17, -5);
    private static SessionSpellRecord LevelPlusMany => Spell(236, StatField, 17, 100);
    private static SessionSpellRecord ExperiencePlusMany => Spell(237, StatField, 18, 2_100_000_000);
    private static SessionSpellRecord AlignmentPlusMany => Spell(238, StatField, 19, 2000);
    private static SessionSpellRecord AlignmentMinusMany => Spell(239, StatField, 19, -3000);
    private static SessionSpellRecord FatePlusMany => Spell(240, StatField, 20, 200);
    private static SessionSpellRecord UnspentMinusMany => Spell(241, StatField, 21, -10);
    private static SessionSpellRecord Stat25PlusMany => Spell(242, StatField, 25, 2000);
    private static SessionSpellRecord Stat25MinusMany => Spell(243, StatField, 25, -3000);

    private static readonly Dictionary<int, SessionSpellRecord> Records = new[]
    {
        Spell(FirePlus, ResistField, 1, 25), Spell(MagicMinus, ResistField, 4, -200),
        Spell(DexMinusOne, StatField, 1, -1), Spell(DexMinusTwo, StatField, 1, -2), Spell(DexMinusMany, StatField, 1, -20), Spell(DexPlusFive, StatField, 1, 5),
        Spell(IntPlus, StatField, 4, 1), Spell(DerivedPlus, StatField, 9, 3), Spell(CharismaPlus, StatField, 7, 1),
        Spell(BadIndex, StatField, 28, 1), Spell(BadField, 99, 0, 1), Spell(StrengthPlus, StatField, 0, 1), Spell(LevelPlus, StatField, 17, 1), Spell(StrengthMinusOne, StatField, 0, -1), Spell(StrengthMinusTen, StatField, 0, -10),
        Spell(GenderToFemale, StatField, 26, -1), Spell(GenderToMale, StatField, 26, 1), Spell(RaceDwarf, StatField, 27, 1), Spell(RaceTooHigh, StatField, 27, 50), Spell(RaceHuman, StatField, 27, -5), Spell(ConstitutionPlusMany, StatField, 2, 30),
        IntMinusOne, IntMinusTwo, IntMinusMany, Stat25MinusMany, WillMinusOne, WillMinusTwo, LevelMinusMany, LevelPlusMany, ExperiencePlusMany, AlignmentPlusMany, AlignmentMinusMany, FatePlusMany, UnspentMinusMany, Stat25PlusMany,
    }.ToDictionary(r => r.Data.Number);

    private const int GenderToFemale = 270, GenderToMale = 271, RaceDwarf = 272, RaceTooHigh = 273, RaceHuman = 274, ConstitutionPlusMany = 275;

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(CombatFields? fields = null)
    {
        var owner = CreateWith(HalfElf());
        return (owner, new SessionSpellCaster(owner, n => Records.GetValueOrDefault(n), new Map(owner, fields ?? RatFields()), new Events()));
    }

    // A learned NPC: intelligence 14 and willpower 14, the first college at rank 3 (its third spell wants willpower 12), the first tech discipline at degree 4 (wants intelligence 13); the rest as the rat's.
    private static CombatFields Learned(int intelligence = 14, int willpower = 14, int[]? spellTech = null)
    {
        var fields = RatFields();
        var stats = fields.Critter!.BaseStats.ToArray();
        stats[4] = intelligence;
        stats[6] = willpower;
        spellTech ??= [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 4, 0, 0, 0, 0, 0, 0, 0];
        return fields with { Critter = fields.Critter with { BaseStats = stats, SpellTech = spellTech } };
    }

    private static SpellRun Run(int spell) => new(1, spell, SpellRunAction.Begin, new(Hero, SpellObjectFacts.PcType), new(Hero, SpellObjectFacts.PcType), HeroTile, null, FarTile, null, SpellRunFlags.Active);

    private static SpellRunTarget At(string id, int type) => new(new(id, type), FarTile);

    private static bool NotReady(SessionSpellCaster caster) => caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.TraitIdx);

    [TestMethod]
    public void A_resistance_takes_its_stored_value_plus_the_components_unclamped()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.IsTrue(owner.QueryCritterIndexedWrites(Rat).IsEmpty);
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(FirePlus), 0, At(Rat, SpellTargetObject.Npc)));
        var written = owner.QueryCritterIndexedWrites(Rat);
        CollectionAssert.AreEqual(new[] { 25 }, written.Resistances.Values.ToArray(), "0 + 25 in the fire slot");
        Assert.AreEqual(version.Revision + 1, owner.Version.Revision, "one revision");
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, ResistField, Rat, 1) }, caster.Log.ToArray());
        // The next one reads the value it left (25 + 25), and the host's stored magic resistance sees the sum of its own value and the component's.
        caster.Apply(Run(FirePlus), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(50, owner.QueryCritterIndexedWrites(Rat).Resistances[1]);
        caster.Apply(Run(MagicMinus), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(5 - 200, owner.QueryCritterIndexedWrites(Rat).Resistances[4], "no clamp on a resistance: 5 - 200");
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_base_stat_is_clamped_to_the_races_range_and_a_raise_over_the_maximum_stops_there()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(DexPlusFive), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(15, owner.QueryCritterIndexedWrites(Rat).Stats[1], "the stored 10 + 5, not the effective stat");
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, StatField, Rat, 1) }, caster.Log.ToArray());
        caster.Apply(Run(DexPlusFive), 0, At(Rat, SpellTargetObject.Npc));
        caster.Apply(Run(DexPlusFive), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(20, owner.QueryCritterIndexedWrites(Rat).Stats[1], "the human's maximum");
        var version = owner.Version;
        caster.Apply(Run(DexPlusFive), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(version, owner.Version, "already at the maximum: the same value, no change");
    }

    [TestMethod]
    public void Lowering_a_stat_is_refused_when_a_skill_keyed_on_it_needs_the_higher_value()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        // The basic skill 0 keyed on dexterity has 2 points: base level 8, so dexterity 9 (cap 11) holds it and dexterity 8 (cap 7) does not.
        caster.Apply(Run(DexMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(9, owner.QueryCritterIndexedWrites(Rat).Stats[1]);
        var lowered = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(DexMinusOne), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(lowered, owner.Version, "the guard refuses: nothing written");
        Assert.AreEqual(9, owner.QueryCritterIndexedWrites(Rat).Stats[1]);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexRefused, 1, 0, Rat, 1), caster.Log.Last());
        Assert.IsFalse(NotReady(caster));
        Assert.AreNotEqual(version, lowered);
    }

    [TestMethod]
    public void A_big_lowering_is_clamped_to_the_minimum_only_when_the_guard_allows_it()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        // From 10 down by 20 the clamp makes it 1; the guard reads the stat of 1 (cap 3) against the skill's level 8 and refuses.
        caster.Apply(Run(DexMinusMany), 0, At(Rat, SpellTargetObject.Npc));
        Assert.IsTrue(owner.QueryCritterIndexedWrites(Rat).IsEmpty);
        Assert.AreEqual(SpellCastEventKind.TraitIndexRefused, caster.Log.Single().Kind);
        // A stat no skill is keyed on goes to the minimum: charisma 3 + 1 first raises it, nothing guards a raise.
        caster.Apply(Run(CharismaPlus), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(4, owner.QueryCritterIndexedWrites(Rat).Stats[7]);
    }

    [TestMethod]
    public void The_written_values_are_the_sessions_object_state_saved_and_restored_with_it()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        caster.Apply(Run(DexPlusFive), 0, At(Rat, SpellTargetObject.Npc));
        caster.Apply(Run(FirePlus), 0, At(Rat, SpellTargetObject.Npc));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        var written = restored.QueryCritterIndexedWrites(Rat);
        CollectionAssert.AreEqual(new[] { 15 }, written.Stats.Values.ToArray());
        CollectionAssert.AreEqual(new[] { 25 }, written.Resistances.Values.ToArray());
        Assert.AreEqual(owner.QueryCritterIndexedWrites(Rat).Signature, written.Signature);
    }

    [TestMethod]
    public void The_written_entries_lie_over_the_stored_fields_of_the_npc()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var fields = RatFields();
        Assert.AreSame(fields, owner.QueryCritterIndexedWrites(Rat).Apply(fields), "nothing written: the fields themselves");
        caster.Apply(Run(DexPlusFive), 0, At(Rat, SpellTargetObject.Npc));
        caster.Apply(Run(FirePlus), 0, At(Rat, SpellTargetObject.Npc));
        var laid = owner.QueryCritterIndexedWrites(Rat).Apply(fields);
        Assert.AreEqual(15, laid.Critter!.BaseStats[1]);
        Assert.AreEqual(fields.Critter!.BaseStats[0], laid.Critter.BaseStats[0], "the others are the stored ones");
        CollectionAssert.AreEqual(new[] { 10, 25, 0, 0, 5 }, laid.Object.Resistances.ToArray());
        // The combat values built from them carry it: the dexterity is the written one.
        var built = SessionCritterCombat.From(Rat, laid).Combat!;
        Assert.AreEqual(15, built.EffectiveStat(CharacterStat.Dexterity, built.Loaded));
    }

    [TestMethod]
    public void A_derived_stat_is_never_written_and_a_target_that_is_no_critter_does_nothing()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(DerivedPlus), 0, At(Rat, SpellTargetObject.Npc)), "stats 8 to 16 are read, not written");
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(FirePlus), 0, At(Box, SpellTargetObject.Container)), "the executor tests the cached type for a PC or NPC");
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(FirePlus), 0, new(null, FarTile)), "no target object");
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(0, caster.Log.Count);
    }

    [TestMethod]
    public void What_the_session_cannot_do_for_the_stat_is_not_ready()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var version = owner.Version;
        // The hero (the character owner's stats), an index and a field the host has no state for, strength of an NPC the map's seed does not list (its carried weight is unknown), and a PC that is not the hero.
        Assert.IsNull(caster.Apply(Run(BadField), 0, At(Hero, SpellTargetObject.Pc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(BadIndex), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(BadField), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(FirePlus), 0, At("pc:other", SpellTargetObject.Pc)).JumpTo);
        Assert.AreEqual(5, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.TraitIdx));
        Assert.AreEqual(version, owner.Version);
    }

    // ARC-MAGIC-1508 (ledger RM3b, setter case 0x18 → 0x4B0EE0, 0x4B1350): stat 24 is the NPC's poison level, the life state's in the session.
    private static (GameSessionOwner Owner, SessionSpellCaster Caster) PoisonSession(int stored, int constitution, int number, int value, CombatFields? fields = null)
    {
        var owner = CreateWith(HalfElf());
        var rat = fields ?? AttackStateTests.Npc(AttackStateTests.Stats(constitution: constitution, poison: stored), resistances: [0, 0, 0, 0, 0]);
        var records = new Dictionary<int, SessionSpellRecord>(Records) { [number] = Spell(number, StatField, 24, value) };
        return (owner, new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner, rat), new Events()));
    }

    private static int Poison(GameSessionOwner owner, SessionSpellCaster caster) => owner.QueryLife(caster.LifeTarget(Rat)!).View!.Record.State.PoisonLevel;

    [TestMethod]
    public void Poison_adds_to_the_loaded_level_in_the_life_state_and_sets_the_poison_timers()
    {
        var (owner, caster) = PoisonSession(stored: 40, constitution: 3, number: 450, value: 60);
        using var _ = owner;
        Assert.AreEqual(40, Poison(owner, caster), "the map data's entry is the level the NPC loads with");
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(450), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(100, Poison(owner, caster));
        Assert.IsTrue(owner.QueryLife(caster.LifeTarget(Rat)!).View!.Recorded, "the NPC got a life record");
        Assert.IsTrue(owner.QueryCritterIndexedWrites(Rat).IsEmpty, "no stored entry is written: the life state is the level");
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, StatField, Rat, 24) }, caster.Log.ToArray());
        CollectionAssert.AreEquivalent(new[] { SessionEventKinds.NpcPoisonDamage, SessionEventKinds.NpcPoisonRecovery }, owner.QueryEvents().Select(e => e.Kind).ToArray(), "a positive level schedules the damage and recovery ticks (0x4B1350)");
        caster.Apply(Run(450), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(160, Poison(owner, caster), "the second write reads the level the first left");
        Assert.AreEqual(2, owner.QueryEvents().Count, "one pending timer per kind");
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void Poison_is_clamped_to_0_and_1000_and_a_cure_stops_at_zero_without_timers()
    {
        var (owner, caster) = PoisonSession(stored: 900, constitution: 3, number: 451, value: 500);
        using var _ = owner;
        caster.Apply(Run(451), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(1000, Poison(owner, caster));
        var (cured, curer) = PoisonSession(stored: 30, constitution: 3, number: 452, value: -500);
        using var _c = cured;
        curer.Apply(Run(452), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(0, Poison(cured, curer));
        Assert.AreEqual(0, cured.QueryEvents().Count, "a level of 0 schedules nothing");
        var (wrapped, wrapper) = PoisonSession(stored: 10, constitution: 3, number: 453, value: int.MaxValue);
        using var _w = wrapped;
        wrapper.Apply(Run(453), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(0, Poison(wrapped, wrapper), "the 32-bit sum 10 + 2147483647 wraps negative and clamps to 0");
    }

    [TestMethod]
    public void An_effective_constitution_of_twenty_or_more_makes_the_poison_level_zero()
    {
        var (owner, caster) = PoisonSession(stored: 0, constitution: 20, number: 454, value: 300);
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(454), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(0, Poison(owner, caster), "0x4B0EE0: the setter stores 0");
        Assert.AreEqual(0, owner.QueryEvents().Count);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void The_poison_level_is_saved_with_the_life_state_and_restored()
    {
        var (owner, caster) = PoisonSession(stored: 0, constitution: 3, number: 455, value: 250);
        using var _ = owner;
        caster.Apply(Run(455), 0, At(Rat, SpellTargetObject.Npc));
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(250, restored.QueryLife(caster.LifeTarget(Rat)!).View!.Record.State.PoisonLevel);
        Assert.AreEqual(2, restored.QueryEvents().Count);
    }

    // ARC-MAGIC-1509 (ledger RM3b; exe read 0x4B0980 cases 2 and 3 with the table 0x4B0E74, 0x4EA2E0, 0x4B0FD0): gender and race remove the first innate entry of their cause before the store and add the new innate effect after it.
    // Gender 0 (a woman) adds effect 0x14A with the gender cause, gender 1 nothing; a race adds effect 0x40 + race with the race cause.
    private static readonly SessionEffectDefinition Female = new(330, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, 1)]),
        DwarfEffect = new(65, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, 2)]), HumanEffect = new(64, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, 3)]);

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionLifeTarget Target) Innate(bool rules = true, int gender = 1, int race = 0)
    {
        var fields = AttackStateTests.Npc(AttackStateTests.Stats(dexterity: 10, gender: gender, race: race), resistances: [10, 0, 0, 0, 5], skills: [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var (owner, caster) = Session(fields);
        if (rules) owner.AttachInnateEffects([Female, DwarfEffect, HumanEffect]);
        return (owner, caster, SessionCritterCombat.From(Rat, fields).Combat!.Target);
    }

    private static (int Number, EffectCause Cause)[] Held(GameSessionOwner owner, SessionLifeTarget target) => [.. owner.QueryNpcEffects(target)!.Entries.Select(e => (e.Number, e.Cause))];

    [TestMethod]
    public void A_man_made_a_woman_takes_the_female_effect_with_the_gender_cause_and_loses_the_old_gender_entry()
    {
        var (owner, caster, target) = Innate();
        using var _ = owner;
        // An old gender entry (any number) and an unrelated entry: the removal takes the first entry of the gender cause alone (0x4EA2E0).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, DwarfEffect, EffectCause.Gender).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, HumanEffect, EffectCause.Race).Disposition);
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(GenderToFemale), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(0, owner.QueryCritterIndexedWrites(Rat).Stats[26]);
        CollectionAssert.AreEqual(new[] { (64, EffectCause.Race), (330, EffectCause.Gender) }, Held(owner, target));
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_woman_made_a_man_loses_the_gender_entry_and_adds_none()
    {
        var (owner, caster, target) = Innate(gender: 0);
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, Female, EffectCause.Gender).Disposition);
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(GenderToMale), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(1, owner.QueryCritterIndexedWrites(Rat).Stats[26]);
        Assert.AreEqual(0, Held(owner, target).Length);
        // A man made a man (a store of the same value) removes nothing and adds nothing; the same value is no change in the stat.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(GenderToMale), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(0, Held(owner, target).Length);
    }

    [TestMethod]
    public void A_woman_made_a_woman_again_moves_her_female_entry_to_the_end_of_the_list()
    {
        // The setter removes before the store whatever the value, then adds: the entry is re-added after the entries that were behind it.
        var (owner, caster, target) = Innate(gender: 0);
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, Female, EffectCause.Gender).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, HumanEffect, EffectCause.Race).Disposition);
        // Gender 0 is stored already: a store of -1 clamps to 0 (a lowering of nothing).
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(GenderToFemale), 0, At(Rat, SpellTargetObject.Npc)));
        CollectionAssert.AreEqual(new[] { (64, EffectCause.Race), (330, EffectCause.Gender) }, Held(owner, target));
    }

    [TestMethod]
    public void A_race_change_swaps_the_race_effect_and_changes_the_range_of_the_other_stats()
    {
        var (owner, caster, target) = Innate();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, HumanEffect, EffectCause.Race).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, Female, EffectCause.Gender).Disposition);
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(RaceDwarf), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(1, owner.QueryCritterIndexedWrites(Rat).Stats[27]);
        CollectionAssert.AreEqual(new[] { (330, EffectCause.Gender), (65, EffectCause.Race) }, Held(owner, target));
        // The race is stored where the stat ranges read it: a dwarf's constitution goes to 21 where a human's stops at 20.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(ConstitutionPlusMany), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(21, owner.QueryCritterIndexedWrites(Rat).Stats[2]);
    }

    [TestMethod]
    public void A_race_back_to_human_takes_the_human_effect()
    {
        var (owner, caster, target) = Innate(race: 1);
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, DwarfEffect, EffectCause.Race).Disposition);
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(RaceHuman), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(0, owner.QueryCritterIndexedWrites(Rat).Stats[27], "clamped to the minimum 0");
        CollectionAssert.AreEqual(new[] { (64, EffectCause.Race) }, Held(owner, target));
    }

    [TestMethod]
    public void A_gender_or_race_swap_without_the_effects_rule_or_for_the_races_eleventh_slot_is_not_ready_and_writes_nothing()
    {
        // No rule attached: the effect cannot be added, so nothing is removed or stored.
        var (owner, caster, target) = Innate(rules: false);
        using var _o = owner;
        var version = owner.Version;
        Assert.IsNull(caster.Apply(Run(GenderToFemale), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(RaceDwarf), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady));
        Assert.AreEqual(version, owner.Version);
        // A male made a man needs no rule (nothing to add): only the removal and the store run.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(GenderToMale), 0, At(Rat, SpellTargetObject.Npc)));
        // Race 11 (the table's last slot) is a race the character rules do not have: not ready even with the rules.
        var (owner2, caster2, _) = Innate();
        using var _o2 = owner2;
        var version2 = owner2.Version;
        Assert.IsNull(caster2.Apply(Run(RaceTooHigh), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.AreEqual(version2, owner2.Version);
        Assert.IsTrue(NotReady(caster2));
        Assert.AreEqual(1, owner.QueryCritterIndexedWrites(Rat).Stats[26]);
        Assert.AreEqual(0, Held(owner, target).Length);
    }

    // ARC-MAGIC-1631 (ledger RM3b): the setter stores race 11 (clamp maximum 11) and adds effect 75, but every race-keyed table of the original has eleven rows, so an NPC of race 11 would read past them
    // (the reaction table 0x5B684C: row 11 is text and pointers): the component stays not ready and the store that only reaches 11 by an addend leaves the race, its effect and the version as they were (the host cannot attach a rule for effect 75, so the guard and the missing rule refuse alike).
    [TestMethod]
    public void An_orc_pushed_by_one_to_race_11_is_not_ready_and_keeps_its_race_and_race_effect()
    {
        var (owner, caster, target) = Innate(race: 10);
        using var _o = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, DwarfEffect, EffectCause.Race).Disposition);
        var version = owner.Version;
        Assert.IsNull(caster.Apply(Run(RaceDwarf), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsTrue(NotReady(caster));
        Assert.AreEqual(version, owner.Version);
        Assert.IsFalse(owner.QueryCritterIndexedWrites(Rat).Stats.ContainsKey(27), "nothing stored: the race stays the map's 10");
        CollectionAssert.AreEqual(new[] { (65, EffectCause.Race) }, Held(owner, target));
    }

    [TestMethod]
    public void The_innate_rules_the_host_attaches_are_checked()
    {
        var (owner, _, _) = Innate(rules: false);
        using var _o = owner;
        Assert.ThrowsExactly<ArgumentException>(() => owner.AttachInnateEffects([new(158, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, 1)])]));
        Assert.ThrowsExactly<ArgumentException>(() => owner.AttachInnateEffects([new(75, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, 1)])]));
        owner.AttachInnateEffects([Female]);
        Assert.AreEqual(Female, owner.InnateRule(330));
        owner.AttachInnateEffects(null);
        Assert.IsNull(owner.InnateRule(330));
    }

    [TestMethod]
    public void Level_experience_alignment_fate_unspent_points_and_stat_25_are_stored_after_the_clamp_alone()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(LevelPlus), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(2, owner.QueryCritterIndexedWrites(Rat).Stats[17], "level 1 + 1");
        caster.Apply(Run(LevelMinusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(0, owner.QueryCritterIndexedWrites(Rat).Stats[17], "down to the minimum 0, no guard on a stat no skill is keyed on");
        caster.Apply(Run(LevelPlusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(51, owner.QueryCritterIndexedWrites(Rat).Stats[17], "the maximum 51");
        caster.Apply(Run(ExperiencePlusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(2_000_000_000, owner.QueryCritterIndexedWrites(Rat).Stats[18]);
        caster.Apply(Run(AlignmentPlusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(1000, owner.QueryCritterIndexedWrites(Rat).Stats[19]);
        caster.Apply(Run(AlignmentMinusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(-1000, owner.QueryCritterIndexedWrites(Rat).Stats[19]);
        caster.Apply(Run(FatePlusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(100, owner.QueryCritterIndexedWrites(Rat).Stats[20]);
        caster.Apply(Run(UnspentMinusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(0, owner.QueryCritterIndexedWrites(Rat).Stats[21]);
        caster.Apply(Run(Stat25PlusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(1000, owner.QueryCritterIndexedWrites(Rat).Stats[25]);
        caster.Apply(Run(Stat25MinusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(20, owner.QueryCritterIndexedWrites(Rat).Stats[25], "the minimum of stat 25 is 20, not 0");
        Assert.IsFalse(NotReady(caster));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.TraitIndexRefused));
    }

    [TestMethod]
    public void Lowering_intelligence_is_refused_when_a_tech_degree_or_a_known_spell_needs_the_higher_value()
    {
        var (owner, caster) = Session(Learned());
        using var _ = owner;
        // Degree 4 wants intelligence 13: 14 down to 13 holds, down to 12 does not (the base is kept).
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(IntMinusOne.Data.Number), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(13, owner.QueryCritterIndexedWrites(Rat).Stats[4]);
        var version = owner.Version;
        caster.Apply(Run(IntMinusOne.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(version, owner.Version, "refused: nothing written");
        Assert.AreEqual(13, owner.QueryCritterIndexedWrites(Rat).Stats[4]);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexRefused, 1, 2, Rat, 4), caster.Log.Last(), "reason 2: a degree");
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void Lowering_intelligence_below_the_ranks_minimum_is_refused_by_the_spell_check_before_the_degrees()
    {
        // No degrees; the first college at rank 3 wants 5: 14 - 10 = 4 is refused by the spell check (reason 1), 14 - 2 = 12 holds.
        var spellTech = new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0 };
        var (owner, caster) = Session(Learned(spellTech: spellTech));
        using var _ = owner;
        caster.Apply(Run(IntMinusTwo.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(12, owner.QueryCritterIndexedWrites(Rat).Stats[4]);
        var version = owner.Version;
        caster.Apply(Run(IntMinusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexRefused, 1, 1, Rat, 4), caster.Log.Last());
        // The same NPC knowing no spell goes down to 4 (no degree either).
        var (empty, tried) = Session(Learned(spellTech: new int[25]));
        using var _e = empty;
        tried.Apply(Run(IntMinusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(4, empty.QueryCritterIndexedWrites(Rat).Stats[4]);
    }

    [TestMethod]
    public void Lowering_willpower_is_refused_when_a_known_spell_needs_the_higher_value_and_degrees_do_not_count()
    {
        // Rank 3 of the first college wants willpower 12 (6, 9, 12, 15, 18): 14 - 2 = 12 holds, 12 - 1 = 11 does not. The degree of 4 asks for intelligence only.
        var (owner, caster) = Session(Learned());
        using var _ = owner;
        caster.Apply(Run(WillMinusTwo.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(12, owner.QueryCritterIndexedWrites(Rat).Stats[6]);
        var version = owner.Version;
        caster.Apply(Run(WillMinusOne.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexRefused, 1, 1, Rat, 6), caster.Log.Last());
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void Without_a_spell_book_or_degrees_the_guarded_stats_are_not_ready_for_a_lowering_only()
    {
        // Fewer spell-and-tech values than the original's 25: there is no book and no degrees to check, so a lowering of intelligence or willpower is not ready; a raise needs none.
        var (owner, caster) = Session(Learned(spellTech: [0, 0]));
        using var _ = owner;
        var version = owner.Version;
        Assert.IsNull(caster.Apply(Run(IntMinusOne.Data.Number), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(WillMinusOne.Data.Number), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady));
    }

    [TestMethod]
    public void The_effective_stat_of_a_lower_base_lays_the_npcs_effects_over_the_stored_stats()
    {
        var (owner, _) = Session();
        using var _o = owner;
        var combat = SessionCritterCombat.From(Rat, RatFields()).Combat!;
        var stats = RatFields().Critter!.BaseStats;
        Assert.AreEqual(9, owner.QueryNpcEffectiveStat(combat.Target, stats, CharacterStat.Dexterity, 9), "no effects: the base itself");
        Assert.AreEqual(1, owner.QueryNpcEffectiveStat(combat.Target, stats, CharacterStat.Dexterity, -5), "clamped to the race's minimum");
        Assert.IsNull(owner.QueryNpcEffectiveStat(combat.Target, stats, CharacterStat.MagickPoints, 3), "a primary stat only");
        Assert.IsNull(owner.QueryNpcEffectiveStat(combat.Target, [1, 2, 3], CharacterStat.Dexterity, 3), "the 28 stored stats");
    }

    [TestMethod]
    public void The_session_refuses_a_write_that_is_no_mobile_critter_or_has_no_such_entry()
    {
        var (owner, _) = Session();
        using var _o = owner;
        var map = owner.QueryActor().Map;
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteCritterStat(owner.Version, map, Hero, 1, 5).Failure, "the hero's stats are the character owner's");
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteCritterStat(owner.Version, map, "sector:1:2", 1, 5).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteCritterStat(owner.Version, map, Rat, 28, 5).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteCritterResistance(owner.Version, map, Rat, 5, 5).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.WriteCritterResistance(owner.Version with { Revision = owner.Version.Revision + 1 }, map, Rat, 1, 5).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteCritterResistance(owner.Version, map, Rat, 1, 5).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteCritterResistance(owner.Version, map, Rat, 1, 5).Disposition, "the same value again");
    }

    // ARC-MAGIC-1507 (ledger RM3b; exe read 0x4B0980 cases 0x16 and 0x17, 0x465530, 0x467E80, 0x4649C0): magick points (22) and tech points (23) are stored after the clamp 0 to 210, then the rewield runs each worn slot's
    // light recalculation and item script points 25 and 24. Ready when the session lists the NPC and no worn item has a wield script.
    private const int MagickPlus = 260, TechPlus = 261, MagickMinusMany = 262, TechPlusMany = 263;

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Dressed(params SessionSeedItem[] items)
    {
        var owner = CreateWith(HalfElf());
        var attached = owner.AttachMapInventory(Exterior, new SessionMapInventorySeed([new(Rat, SessionHolderKind.Npc, false, false, [], items.Select(item => item.Entity).ToArray())], items));
        Assert.AreEqual(GameSessionFailure.None, attached.Failure);
        var records = new Dictionary<int, SessionSpellRecord>(Records)
        {
            [MagickPlus] = Spell(MagickPlus, StatField, 22, 7), [TechPlus] = Spell(TechPlus, StatField, 23, 9),
            [MagickMinusMany] = Spell(MagickMinusMany, StatField, 22, -500), [TechPlusMany] = Spell(TechPlusMany, StatField, 23, 500),
        };
        return (owner, new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner, RatFields()), new Events()));
    }

    private static SessionSeedItem Gear(string entity, int location, params int[] scripts) => new(entity, Facts(SessionItemType.Armor, 2800 + location, location, scripts: scripts), null);

    [TestMethod]
    public void Magick_and_tech_points_are_stored_after_the_clamp_for_an_npc_whose_worn_items_have_no_wield_script()
    {
        // A worn vest and helmet (other script points do not count), a carried item with a wield script (not worn: the rewield walks the wear slots only).
        var (owner, caster) = Dressed(Gear("vest", 1006), Gear("helmet", 1000, SessionScriptPoint.Examine), Gear("spare", -1, SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff));
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(TechPlus), 0, At(Rat, SpellTargetObject.Npc)));
        var stats = owner.QueryCritterIndexedWrites(Rat).Stats;
        Assert.AreEqual(RatFields().Critter!.BaseStats[22] + 7, stats[22]);
        Assert.AreEqual(RatFields().Critter!.BaseStats[23] + 9, stats[23]);
        caster.Apply(Run(TechPlusMany), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(210, owner.QueryCritterIndexedWrites(Rat).Stats[23], "the maximum 210");
        caster.Apply(Run(MagickMinusMany), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(0, owner.QueryCritterIndexedWrites(Rat).Stats[22], "the minimum 0, no guard on a point pool");
        Assert.IsFalse(NotReady(caster));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.TraitIndexRefused));
    }

    [TestMethod]
    public void A_worn_item_with_a_wield_script_or_an_unlisted_npc_leaves_the_points_not_ready()
    {
        foreach (var point in new[] { SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff })
        {
            var (owner, caster) = Dressed(Gear("vest", 1006), Gear("ring", 1003, point));
            using var _ = owner;
            var version = owner.Version;
            Assert.IsNull(caster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo, $"point {point}");
            Assert.IsNull(caster.Apply(Run(TechPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo, $"point {point}");
            Assert.AreEqual(version, owner.Version);
            Assert.IsTrue(owner.QueryCritterIndexedWrites(Rat).IsEmpty);
            Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady));
        }
        // The session does not list the NPC (no inventory view of its map, or a view without it): nothing says what it wears.
        var (plain, _) = Session();
        using var _p = plain;
        var records = new Dictionary<int, SessionSpellRecord>(Records) { [MagickPlus] = Spell(MagickPlus, StatField, 22, 7) };
        var tried = new SessionSpellCaster(plain, n => records.GetValueOrDefault(n), new Map(plain, RatFields()), new Events());
        Assert.IsNull(tried.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo, "no inventory view");
        Assert.IsTrue(NotReady(tried));
        Assert.IsTrue(plain.QueryCritterIndexedWrites(Rat).IsEmpty);
        // A view that lists other holders only.
        var other = CreateWith(HalfElf());
        using var _x = other;
        Assert.AreEqual(GameSessionFailure.None, other.AttachMapInventory(Exterior, new SessionMapInventorySeed([new("oid:other", SessionHolderKind.Npc, false, false, [], [])], [])).Failure);
        var viewed = new SessionSpellCaster(other, n => records.GetValueOrDefault(n), new Map(other, RatFields()), new Events());
        Assert.IsNull(viewed.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo, "a view without the NPC");
        Assert.IsTrue(other.QueryCritterIndexedWrites(Rat).IsEmpty);
    }

    // ARC-MAGIC-1506 (ledger RM3b; exe read 0x4B0980 case 0, 0x45F790, 0x45F820, 0x461410): strength reads the encumbrance level before the store and recalculates after it. The rat's list weighs what its seed items weigh;
    // a strength of 3 carries 1500 (500 a point), so the levels step at 15 / 30 / 45 / 60 / 75 / 90 % of it.
    private static readonly SessionEffectDefinition Slow = new(158, [new(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, -2)]);

    private static SessionMapInventorySeed RatSeed(params int[] weights) => new(
        [new(Rat, SessionHolderKind.Npc, false, false, [], weights.Select((_, i) => "stone-" + i).ToArray())],
        weights.Select((w, i) => new SessionSeedItem("stone-" + i, InventoryStateTests.Facts(SessionItemType.Generic, 5600 + i) with { Weight = w }, null)).ToArray());

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, SessionLifeTarget Target) Carrying(int strength, int[] weights, bool rule = true, bool seeded = true)
    {
        var fields = RatFields();
        var stats = fields.Critter!.BaseStats.ToArray();
        stats[0] = strength;
        fields = fields with { Critter = fields.Critter with { BaseStats = stats } };
        var (owner, caster) = Session(fields);
        if (seeded) Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, RatSeed(weights)).Disposition);
        if (rule) owner.AttachEncumbranceEffect(Slow);
        return (owner, caster, SessionCritterCombat.From(Rat, fields).Combat!.Target);
    }

    private static int Entries(GameSessionOwner owner, SessionLifeTarget target) => owner.QueryNpcEffects(target)!.Count(158);

    [TestMethod]
    public void A_stronger_npc_that_drops_a_level_loses_the_encumbrance_entries()
    {
        // Strength 2 carries 1000: 200 is level 1 (150 to 299); strength 3 carries 1500, where 200 is below 15 % (225): level 0.
        var (owner, caster, target) = Carrying(2, [200]);
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, Slow, EffectCause.Item, 1).Disposition);
        Assert.AreEqual(1, Entries(owner, target));
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(3, owner.QueryCritterIndexedWrites(Rat).Stats[0]);
        Assert.AreEqual(0, Entries(owner, target), "the recalculation removed every entry of effect 158");
        Assert.IsFalse(NotReady(caster));
        CollectionAssert.AreEqual(new[] { SpellCastEventKind.TraitIndexWritten }, caster.Log.Select(e => e.Kind).ToArray());
    }

    [TestMethod]
    public void A_weaker_npc_that_gains_levels_gets_one_item_entry_a_level()
    {
        // Strength 3 carries 1500 (800 is level 3: 675 to 899); strength 2 carries 1000, where 800 is level 5 (a timer: not ready, below). Strength 4 carries 2000, where 800 is level 2 (600 to 899).
        var (owner, caster, target) = Carrying(3, [300, 500]);
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(4, owner.QueryCritterIndexedWrites(Rat).Stats[0]);
        Assert.AreEqual(2, Entries(owner, target), "level 3 to level 2: two entries");
        Assert.AreEqual(2, owner.QueryNpcEffects(target)!.Entries.Count(e => e.Number == 158 && e.Cause == EffectCause.Item));
    }

    [TestMethod]
    public void A_level_that_does_not_change_runs_no_recalculation()
    {
        var (owner, caster, target) = Carrying(3, [10]);
        using var _ = owner;
        var revision = owner.Version.Revision;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(4, owner.QueryCritterIndexedWrites(Rat).Stats[0]);
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the store alone");
        Assert.AreEqual(0, Entries(owner, target));
        Assert.AreEqual(0, owner.QueryNpcEffects(target)!.Entries.Count);
    }

    [TestMethod]
    public void The_old_entries_of_effect_158_are_replaced_whatever_added_them()
    {
        // 800 at strength 3 is level 3; at 4 it is level 2: a spell's own entry of effect 158 goes with the item entries, and two item entries are added.
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, target, Slow, EffectCause.Spell, 1).Disposition);
        caster.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc));
        var entries = owner.QueryNpcEffects(target)!.Entries.Where(e => e.Number == 158).ToArray();
        CollectionAssert.AreEqual(new[] { EffectCause.Item, EffectCause.Item }, entries.Select(e => e.Cause).ToArray());
    }

    // ARC-MAGIC-1568 (0x45E820 mode 1, 0x45E8D0, 0x45E610): a new level from 4 schedules the NPC's encumbrance fatigue timer, one per NPC.
    private static GameEvent[] RatTimers(GameSessionOwner owner) => owner.QueryEvents().Where(e => e.Kind == SessionEventKinds.NpcEncumbrance).ToArray();

    private sealed class RatWorld(SessionLifeTarget target, IReadOnlyList<int>? stats) : ISessionNpcStatsWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => entity == Rat ? target.Facts : null;
        public IReadOnlyList<int>? StoredStats(SessionMapId map, string entity) => entity == Rat ? stats : null;
    }

    private static IReadOnlyList<int> RatStats(GameSessionOwner owner, int strength)
    {
        var stats = RatFields().Critter!.BaseStats.ToArray();
        stats[0] = strength;
        return owner.QueryCritterIndexedWrites(Rat).OverStats(stats)!;
    }

    [TestMethod]
    public void A_level_with_the_fatigue_timer_writes_and_schedules_the_npcs_timer_once()
    {
        // 800 at strength 3 is level 3; at 2 (1000 carried) it is 80 %, level 5: the entries and the 60 s timer; at 1 (500 carried) it is level 6, where the pending timer stays (WEA2).
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.IsFalse(NotReady(caster));
        Assert.AreEqual(2, owner.QueryCritterIndexedWrites(Rat).Stats[0]);
        Assert.AreEqual(5, Entries(owner, target));
        var timer = RatTimers(owner).Single();
        Assert.AreEqual(Rat, timer.Payload.Entities[0].Code);
        Assert.AreEqual(owner.QueryActor().Map.RuntimeMap, timer.Payload.Entities[0].Map);
        Assert.AreEqual(60_000, timer.DeadlineMs - timer.Payload.Values[0]);
        Assert.IsNotNull(owner.QueryLifeRecord(Rat), "the timer gave the NPC the record of its loaded state");
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(1, owner.QueryCritterIndexedWrites(Rat).Stats[0]);
        Assert.AreEqual(8, Entries(owner, target), "level 6: eight entries");
        CollectionAssert.AreEqual(new[] { timer.DeadlineMs }, RatTimers(owner).Select(e => e.DeadlineMs).ToArray(), "the pending timer is kept");
    }

    [TestMethod]
    public void A_level_below_4_schedules_no_npc_timer()
    {
        var (owner, caster, _) = Carrying(3, [300, 500]);
        using var _o = owner;
        caster.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(0, RatTimers(owner).Length);
        Assert.IsNull(owner.QueryLifeRecord(Rat));
    }

    [TestMethod]
    public void The_npcs_timer_drains_its_fatigue_and_is_scheduled_again_by_its_level()
    {
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        var world = new RatWorld(target, RatStats(owner, 2));
        var before = owner.QueryLifeRecord(Rat)!.Value.State.FatigueDamage;
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 59_999, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(0, owner.ProcessEvents(world).Life.Count, "not yet due");
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 1, ClockAdvanceReason.OrdinaryAdvance);
        var pass = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual(before + 1, owner.QueryLifeRecord(Rat)!.Value.State.FatigueDamage, "max(1, 60 / 80) steps of the fatigue");
        var next = RatTimers(owner).Single();
        Assert.AreEqual(60_000, next.DeadlineMs - next.Payload.Values[0], "level 5 again");
        Assert.IsTrue(owner.QueryEvents().Any(e => e.Kind == SessionEventKinds.NpcFatigue), "the fatigue write set the recovery");
    }

    [TestMethod]
    public void The_npcs_timer_is_not_scheduled_again_when_the_level_has_fallen_below_4()
    {
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        // The world reports a strength of 20 (10000 carried): 800 is level 0 when the timer fires.
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 60_000, ClockAdvanceReason.OrdinaryAdvance);
        var pass = owner.ProcessEvents(new RatWorld(target, RatStats(owner, 20).Select((v, i) => i == 0 ? 20 : v).ToArray()));
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual(1, owner.QueryLifeRecord(Rat)!.Value.State.FatigueDamage, "it does not check the level before it drains");
        Assert.AreEqual(0, RatTimers(owner).Length);
        Assert.AreEqual(0, pass.NpcEncumbranceEnds.Count, "a level below 4 ends the chain by the rule: nothing reported");
    }

    [TestMethod]
    public void A_world_without_the_stored_stats_lets_the_npcs_timer_end_after_one_drain()
    {
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 60_000, ClockAdvanceReason.OrdinaryAdvance);
        var pass = owner.ProcessEvents(new RatWorld(target, null));
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual(1, owner.QueryLifeRecord(Rat)!.Value.State.FatigueDamage);
        Assert.AreEqual(0, RatTimers(owner).Length);
        // ARC-MAGIC-1651: the end is reported.
        Assert.AreEqual(SessionNpcEncumbranceEndReason.NoStoredStats, pass.NpcEncumbranceEnds.Single().Reason);
        Assert.AreEqual(Rat, pass.NpcEncumbranceEnds.Single().Entity);
    }

    private sealed class PlainWorld(SessionLifeTarget target) : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => entity == Rat ? target.Facts : null;
    }

    [TestMethod]
    public void A_pass_world_that_cannot_give_stored_stats_reports_the_npcs_chain_end()
    {
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 60_000, ClockAdvanceReason.OrdinaryAdvance);
        var pass = owner.ProcessEvents(new PlainWorld(target));
        Assert.AreEqual(1, owner.QueryLifeRecord(Rat)!.Value.State.FatigueDamage, "it still drains");
        Assert.AreEqual(SessionNpcEncumbranceEndReason.NoStatsWorld, pass.NpcEncumbranceEnds.Single().Reason);
        Assert.AreEqual(0, RatTimers(owner).Length);
    }

    [TestMethod]
    public void An_npc_the_seed_does_not_list_reports_its_chain_end_with_an_unreadable_level()
    {
        // The timer is scheduled by hand (the recalculation reads the level from the seed, which an unlisted NPC lacks); when it fires the level cannot be read either.
        var (owner, _, target) = Carrying(3, [800], seeded: false);
        using var _o = owner;
        var map = owner.QueryActor().Map;
        owner.ScheduleNpcEncumbranceDrain(target, map, 60_000);
        Assert.AreEqual(1, RatTimers(owner).Length);
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 60_000, ClockAdvanceReason.OrdinaryAdvance);
        var pass = owner.ProcessEvents(new RatWorld(target, RatStats(owner, 2)));
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual(SessionNpcEncumbranceEndReason.LevelUnreadable, pass.NpcEncumbranceEnds.Single().Reason);
        Assert.AreEqual(0, RatTimers(owner).Length);
    }

    // ARC-MAGIC-1686: a timer of the NPC on another map (the one-map rule) makes the next schedule not ready (CanScheduleNpcEncumbrance), so the chain ends with NotScheduled.
    private static void MakeNextScheduleNotReady(GameSessionOwner owner)
    {
        var elsewhere = owner.QueryActor().Map.RuntimeMap + 1;
        Assert.IsTrue(owner.ScheduleFocusWipe(Rat, elsewhere, owner.QueryClockSnapshot().GameTimestampMs), "a stray timer of the NPC on another map");
    }

    [TestMethod]
    public void An_npc_with_a_timer_on_another_map_reports_its_chain_end_as_not_scheduled()
    {
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        MakeNextScheduleNotReady(owner);
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 60_000, ClockAdvanceReason.OrdinaryAdvance);
        var pass = owner.ProcessEvents(new RatWorld(target, RatStats(owner, 2)));
        Assert.AreEqual(GameSessionFailure.None, pass.Failure);
        Assert.AreEqual(0, RatTimers(owner).Length, "level 5 asked for another timer and the schedule was not ready");
        var end = pass.NpcEncumbranceEnds.Single();
        Assert.AreEqual(SessionNpcEncumbranceEndReason.NotScheduled, end.Reason);
        Assert.AreEqual(Rat, end.Entity);
    }

    [TestMethod]
    public void The_frame_step_report_carries_the_npc_encumbrance_chain_ends_of_its_passes()
    {
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        MakeNextScheduleNotReady(owner);
        var step = new FrameStep(owner, new RatWorld(target, RatStats(owner, 2)));
        Assert.AreEqual(0, step.Jump(59_999).NpcEncumbranceEnds.Count, "not yet due");
        var report = step.Jump(1);
        Assert.AreEqual(SessionNpcEncumbranceEndReason.NotScheduled, report.NpcEncumbranceEnds.Single().Reason);
        Assert.AreEqual(Rat, report.NpcEncumbranceEnds.Single().Entity);
        Assert.AreSame(report, step.Last);
        Assert.AreEqual(0, step.Jump(1).NpcEncumbranceEnds.Count, "the ends of one call are not carried to the next");
    }

    [TestMethod]
    public void The_npcs_timer_does_nothing_for_a_dead_npc_and_is_not_scheduled_again()
    {
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, target, new LifeDamage { HitPoints = 1000 }).Disposition);
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 60_000, ClockAdvanceReason.OrdinaryAdvance);
        Assert.AreEqual(GameSessionFailure.None, owner.ProcessEvents(new RatWorld(target, RatStats(owner, 2))).Failure);
        Assert.AreEqual(0, RatTimers(owner).Length);
    }

    [TestMethod]
    public void The_npcs_timer_is_saved_and_restored_and_a_wrong_delay_is_refused()
    {
        var (owner, caster, _) = Carrying(3, [800]);
        using var _o = owner;
        caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(RatTimers(owner).Select(e => (e.DeadlineMs, e.Payload.Entities[0].Code)).ToArray(), RatTimers(restored).Select(e => (e.DeadlineMs, e.Payload.Entities[0].Code)).ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        var timer = RatTimers(owner).Single();
        var deadline = $"\"deadlineMs\":{timer.DeadlineMs}";
        StringAssert.Contains(text, deadline);
        Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(Decode(text.Replace(deadline, $"\"deadlineMs\":{timer.DeadlineMs + 1}")), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void Strength_is_not_ready_without_the_list_or_the_rule_of_effect_158()
    {
        // The NPC is no holder of the map's seed: its carried weight is unknown. And a level change without the rule has no entries to add; the same change that stays at its level writes.
        foreach (var (rule, seeded) in new[] { (true, false), (false, true) })
        {
            var (owner, caster, _) = Carrying(2, [200], rule, seeded);
            using var _o = owner;
            var version = owner.Version;
            Assert.IsNull(caster.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo, $"rule {rule}, seeded {seeded}");
            Assert.IsTrue(NotReady(caster));
            Assert.AreEqual(version, owner.Version);
        }
        var (same, tried, _) = Carrying(3, [10], rule: false);
        using var _s = same;
        Assert.AreEqual(SpellComponentOutcome.None, tried.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(4, same.QueryCritterIndexedWrites(Rat).Stats[0]);
    }

    [TestMethod]
    public void The_level_after_the_store_is_read_with_the_clamped_strength()
    {
        // 10 points down clamp at strength 1 (a minimum of 1 for the primary stats): 500 carried, where 200 is 40 % (level 2); at 3 it was 13 % (level 0). The raw sum, below 1, would carry the 300 floor.
        var (owner, caster, target) = Carrying(3, [200]);
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(StrengthMinusTen), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(1, owner.QueryCritterIndexedWrites(Rat).Stats[0], "3 - 10 clamped to 1");
        Assert.AreEqual(2, Entries(owner, target));
    }

    [TestMethod]
    public void The_weight_of_the_list_counts_the_seed_items_and_the_made_items_the_session_added()
    {
        var (owner, _, target) = Carrying(3, [100, 300]);
        using var _ = owner;
        var stats = RatFields().Critter!.BaseStats.ToArray();
        Assert.AreEqual(400, owner.QueryNpcCarry(Exterior, target, stats, 3)!.Weight);
        Assert.AreEqual(1500, owner.QueryNpcCarry(Exterior, target, stats, 3)!.CarryWeight);
        Assert.AreEqual(1, owner.QueryNpcCarry(Exterior, target, stats, 3)!.Level, "400 of 1500 is 26 %: level 1");
        Assert.AreEqual(1000, owner.QueryNpcCarry(Exterior, target, stats, 2)!.CarryWeight);
        Assert.AreEqual(2, owner.QueryNpcCarry(Exterior, target, stats, 2)!.Level, "400 of 1000 is 40 %");
        Assert.IsNull(owner.QueryNpcCarry(new SessionMapId(80), target, stats, 3), "no seed of that map");
        // A made item the session adds to the NPC weighs its prototype's weight (the session's row, not the seed's facts).
        owner.AttachItemWeights(_ => new SessionItemWeight(50));
        var kind = InventoryStateTests.Facts(SessionItemType.Generic, 5700).Kind;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddItemToNpc(owner.Version, Rat, kind, _ => SessionInsertVerdict.Accepted).Disposition);
        Assert.AreEqual(450, owner.QueryNpcCarry(Exterior, target, stats, 3)!.Weight);
    }

    // ---- ARC-MAGIC-1510: the hero and a follower whose stats the advancement record holds ----

    // The half-elf hero (dexterity effect +1) with a basic skill keyed on dexterity at 2 points, the first college at rank <colleges>, the first tech discipline at degree <degree>.
    private static SessionHeroDefinition SkilledHero(int[]? stats = null, int college = 0, int degree = 0)
    {
        var basic = new int[12];
        basic[0] = 2;
        var colleges = new int[16];
        colleges[0] = college;
        var degrees = new int[8];
        degrees[0] = degree;
        return HalfElf(stats ?? [8, 10, 8, 8, 8, 8, 8, 8, 0, 0]) with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic], Colleges = [.. colleges], TechDegrees = [.. degrees] } };
    }

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) HeroSession(SessionHeroDefinition hero, bool experience = false, IReadOnlyDictionary<int, SessionSpellRecord>? records = null)
    {
        var made = GameSessionOwner.Create(Descriptor() with { Hero = hero, Experience = experience ? ExperienceStateTests.Rules() : null });
        Assert.AreEqual(GameSessionFailure.None, made.Failure);
        var owner = made.Owner!;
        var spells = records ?? Records;
        return (owner, new SessionSpellCaster(owner, n => spells.GetValueOrDefault(n), new Map(owner, RatFields()), new Events()));
    }

    private static SpellComponentOutcome CastOnHero(SessionSpellCaster caster, int spell) => caster.Apply(Run(spell), 0, At(Hero, SpellTargetObject.Pc));

    [TestMethod]
    public void The_heros_base_stat_takes_its_stored_value_plus_the_component_and_is_clamped_to_the_races_range()
    {
        var (owner, caster) = HeroSession(SkilledHero());
        using var _ = owner;
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, DexPlusFive));
        Assert.AreEqual(15, owner.QueryHero()!.Stats[1], "stored 10 + 5, the effect's +1 is not part of the base");
        Assert.AreEqual(16, owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Dexterity));
        Assert.AreEqual(version.Revision + 1, owner.Version.Revision, "one revision");
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, StatField, Hero, 1) }, caster.Log.ToArray());
        // A half-elf's dexterity is capped at 21: 15 + 5 + 5 stops there.
        CastOnHero(caster, DexPlusFive);
        CastOnHero(caster, DexPlusFive);
        Assert.AreEqual(21, owner.QueryHero()!.Stats[1]);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void The_heros_strength_is_stored_and_the_write_goes_through_the_revision_that_settles_encumbrance()
    {
        var (owner, caster) = HeroSession(SkilledHero());
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, StrengthPlus));
        Assert.AreEqual(9, owner.QueryHero()!.Stats[0]);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void Lowering_a_heros_stat_is_refused_when_a_skill_keyed_on_it_needs_the_higher_value_reading_the_effective_stat_after_the_write()
    {
        var (owner, caster) = HeroSession(SkilledHero());
        using var _ = owner;
        // Dexterity 10 (effective 11) down to 9 (effective 10) still holds the skill of 2 points; down to 1 (effective 2) does not.
        CastOnHero(caster, DexMinusOne);
        Assert.AreEqual(9, owner.QueryHero()!.Stats[1]);
        var version = owner.Version;
        CastOnHero(caster, DexMinusMany);
        Assert.AreEqual(version, owner.Version, "the guard refuses: nothing written");
        Assert.AreEqual(9, owner.QueryHero()!.Stats[1]);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexRefused, 1, 0, Hero, 1), caster.Log.Last());
        // The stat the guard asked the character owner about is back at its base.
        Assert.AreEqual(10, owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Dexterity));
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void Lowering_a_heros_intelligence_is_refused_by_a_known_spell_rank_or_a_tech_degree()
    {
        // The first college at rank 3 wants intelligence 5: 8 down to 6 holds, down to 4 does not (reason 1).
        var (owner, caster) = HeroSession(SkilledHero(college: 3));
        using var _ = owner;
        CastOnHero(caster, IntMinusTwo.Data.Number);
        Assert.AreEqual(6, owner.QueryHero()!.Stats[4]);
        var version = owner.Version;
        CastOnHero(caster, IntMinusTwo.Data.Number);
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexRefused, 1, 1, Hero, 4), caster.Log.Last());
        // Degree 4 of the first discipline wants intelligence 13: 14 down to 13 holds, down to 12 does not (reason 2).
        var (second, tried) = HeroSession(SkilledHero([8, 10, 8, 8, 14, 8, 8, 8, 0, 0], degree: 4));
        using var _s = second;
        CastOnHero(tried, IntMinusOne.Data.Number);
        Assert.AreEqual(13, second.QueryHero()!.Stats[4]);
        CastOnHero(tried, IntMinusOne.Data.Number);
        Assert.AreEqual(13, second.QueryHero()!.Stats[4]);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexRefused, 1, 2, Hero, 4), tried.Log.Last());
        Assert.IsFalse(NotReady(caster) || NotReady(tried));
    }

    [TestMethod]
    public void The_heros_alignment_fate_points_and_unspent_points_are_stored_after_the_clamp()
    {
        var (owner, caster) = HeroSession(SkilledHero());
        using var _ = owner;
        CastOnHero(caster, AlignmentPlusMany.Data.Number);
        Assert.AreEqual(1000, owner.QueryHero()!.Alignment);
        CastOnHero(caster, AlignmentMinusMany.Data.Number);
        Assert.AreEqual(-1000, owner.QueryHero()!.Alignment);
        CastOnHero(caster, FatePlusMany.Data.Number);
        Assert.AreEqual(100, owner.QueryHero()!.Fate.Points);
        Assert.AreEqual(100, owner.QueryHeroFate()!.Value.Points);
        CastOnHero(caster, UnspentMinusMany.Data.Number);
        Assert.AreEqual(0, owner.QueryHero()!.UnspentPoints, "5 - 10 clamped to 0");
        Assert.IsFalse(NotReady(caster));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.TraitIndexRefused));
    }

    // ARC-MAGIC-1628: the setter's experience case (0x12) runs the recalculation 0x4A69C0 after the store; the level has no case and is stored alone.
    private const int ExperienceLevelTwo = 651, PoisonPlus = 652, HeroPointsPlus = 653, HeroPointsMinus = 654, HeroTechPlus = 655;

    private static Dictionary<int, SessionSpellRecord> HeroRecords() => new(Records)
    {
        [650] = Spell(650, StatField, 18, 100), [ExperienceLevelTwo] = Spell(ExperienceLevelTwo, StatField, 18, 1500), [PoisonPlus] = Spell(PoisonPlus, StatField, 24, 60),
        [HeroPointsPlus] = Spell(HeroPointsPlus, StatField, 22, 7), [HeroPointsMinus] = Spell(HeroPointsMinus, StatField, 23, -3), [HeroTechPlus] = Spell(HeroTechPlus, StatField, 23, 20),
    };

    [TestMethod]
    public void A_heros_level_is_stored_alone_with_or_without_the_experience_table()
    {
        foreach (var table in new[] { true, false })
        {
            var (owner, caster) = HeroSession(SkilledHero(), experience: table);
            using var _ = owner;
            Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, LevelPlus));
            Assert.AreEqual(2, owner.QueryHero()!.Level, "level 1 + 1");
            // Level 2 - 5 is clamped to 0: no case runs the recalculation, so the 0 experience is not lifted to level 1.
            CastOnHero(caster, LevelMinusMany.Data.Number);
            Assert.AreEqual(0, owner.QueryHero()!.Level);
            Assert.AreEqual(0, owner.QueryHero()!.Experience);
            Assert.AreEqual(0, caster.TakeExperienceAwards().Count);
            Assert.IsFalse(NotReady(caster));
        }
    }

    [TestMethod]
    public void A_heros_experience_that_reaches_a_level_runs_the_recalculation_and_queues_the_award_for_the_host()
    {
        var (owner, caster) = HeroSession(SkilledHero(), experience: true, records: HeroRecords());
        using var _ = owner;
        var before = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, ExperienceLevelTwo));
        var hero = owner.QueryHero()!;
        // 1500 experience reaches level 2 (1010) and not level 3 (2040); the level's points are added to the 5 unspent ones.
        Assert.AreEqual((2, 1500), (hero.Level, hero.Experience));
        Assert.IsTrue(hero.UnspentPoints > 5);
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision");
        var award = caster.TakeExperienceAwards().Single();
        Assert.AreEqual(1, award.LevelsGained);
        Assert.AreEqual(hero.UnspentPoints - 5, award.PointsGained);
        Assert.AreEqual(2, award.LevelUps!.Single().Level, "the level-up message of the level written");
        Assert.IsNotNull(award.Followers, "the followers' step");
        Assert.AreEqual(0, caster.TakeExperienceAwards().Count, "the queue was emptied");
        Assert.IsFalse(NotReady(caster));
        // A further write below level 3 (2040) stores the experience alone: no level, no award.
        var version = owner.Version;
        Assert.IsNull(CastOnHero(caster, 650).JumpTo);
        Assert.AreEqual(1600, owner.QueryHero()!.Experience);
        Assert.AreEqual(version.Revision + 1, owner.Version.Revision);
        Assert.AreEqual(0, caster.TakeExperienceAwards().Count);
    }

    [TestMethod]
    public void A_heros_experience_through_every_level_up_to_the_last_writes_the_threshold()
    {
        var (owner, caster) = HeroSession(SkilledHero(), experience: true);
        using var _ = owner;
        // 2,100,000,000 is clamped to 2,000,000,000: every level of the table is reached up to the last one, where the recalculation writes the table's threshold as the experience (ledger XP5).
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, ExperiencePlusMany.Data.Number));
        var hero = owner.QueryHero()!;
        Assert.AreEqual(50, hero.Level);
        Assert.IsTrue(hero.Experience < 2_000_000_000, "the threshold, not the stored value");
        var award = caster.TakeExperienceAwards().Single();
        Assert.AreEqual(49, award.LevelsGained);
        Assert.AreEqual(49, award.LevelUps!.Count);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_heros_experience_is_not_ready_without_the_experience_table()
    {
        var (owner, caster) = HeroSession(SkilledHero(), records: HeroRecords());
        using var _ = owner;
        var version = owner.Version;
        Assert.IsNull(CastOnHero(caster, 650).JumpTo);
        Assert.AreEqual(0, owner.QueryHero()!.Experience);
        Assert.AreEqual(version, owner.Version);
        Assert.IsTrue(NotReady(caster));
        Assert.AreEqual(GameSessionFailure.ExperienceUnavailable, owner.WriteHeroExperienceStat(owner.Version, 5).Result.Failure);
    }

    [TestMethod]
    public void The_heros_experience_write_refuses_a_stale_version_and_a_value_outside_the_range()
    {
        var (owner, _) = HeroSession(SkilledHero(), experience: true);
        using var _o = owner;
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteHeroExperienceStat(stale, 5).Result.Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.WriteHeroExperienceStat(stale, 6).Result.Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteHeroExperienceStat(owner.Version, -1).Result.Failure);
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, owner.WriteHeroStat(owner.Version, 18, 5).Failure, "experience goes through the recalculating write");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.WriteHeroExperienceStat(owner.Version, 5).Result.Disposition);
        Assert.AreEqual(5, owner.QueryHero()!.Experience);
    }

    [TestMethod]
    public void A_heros_poison_is_its_life_states_level_and_sets_the_poison_timers()
    {
        var (owner, caster) = HeroSession(SkilledHero(), records: HeroRecords());
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, PoisonPlus));
        Assert.AreEqual(60, owner.QueryLife(SessionLifeTarget.Hero).View!.Record.State.PoisonLevel);
        Assert.IsTrue(owner.QueryEvents().Any(e => e.Kind == SessionEventKinds.HeroPoisonDamage), "a positive level schedules the poison damage");
        CastOnHero(caster, PoisonPlus);
        Assert.AreEqual(120, owner.QueryLife(SessionLifeTarget.Hero).View!.Record.State.PoisonLevel, "the second write reads the level the first left");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, StatField, Hero, 24), caster.Log.Last());
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_heros_poison_becomes_zero_for_an_effective_constitution_of_twenty()
    {
        var (owner, caster) = HeroSession(SkilledHero([8, 10, 20, 8, 8, 8, 8, 8, 0, 0]) with { Race = CharacterRace.Human, RaceEffect = null }, records: HeroRecords());
        using var _ = owner;
        // A constitution of 20: the setter's test (0x4B0EE0) stores 0.
        CastOnHero(caster, PoisonPlus);
        Assert.AreEqual(0, owner.QueryLife(SessionLifeTarget.Hero).View!.Record.State.PoisonLevel);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_heros_magick_and_tech_points_are_stored_clamped_when_no_worn_item_has_a_wield_script()
    {
        var (owner, caster) = HeroSession(SkilledHero([8, 10, 8, 8, 8, 8, 8, 8, 5, 2]), records: HeroRecords());
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, HeroPointsPlus));
        Assert.AreEqual(12, owner.QueryHero()!.Stats[8], "5 + 7");
        CastOnHero(caster, HeroPointsMinus);
        Assert.AreEqual(0, owner.QueryHero()!.Stats[9], "2 - 3 clamped to 0");
        Assert.AreEqual(0, owner.QueryHeroStoredStat(23));
        Assert.AreEqual(12, owner.QueryHeroStoredStat(22));
        Assert.IsFalse(NotReady(caster));
        // The hero's wear is empty here.
        Assert.IsTrue(owner.HeroRewieldReady());
    }

    [TestMethod]
    public void A_heros_magick_points_are_not_ready_while_a_worn_item_has_a_wield_script()
    {
        var (owner, caster) = HeroSession(SkilledHero([8, 10, 8, 8, 8, 8, 8, 8, 5, 2]), records: HeroRecords());
        using var _ = owner;
        var kit = owner.QueryHeroKit();
        Assert.IsTrue(kit.Count > 0, "the fixture hero carries a kit");
        var worn = owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, EquipmentStateTests.Wearable(1, EquipmentStateTests.Blade, [SessionScriptPoint.WieldOn]), EquipmentStateTests.Drawn, _ => SessionInsertVerdict.Accepted, new NoScriptHost());
        Assert.AreEqual(GameSessionDisposition.Changed, worn.Disposition, worn.Failure.ToString());
        Assert.IsFalse(owner.HeroRewieldReady());
        // Only the wield points count: an item with an insert point alone leaves the rewield ready, one with wield off alone blocks it.
        Accepts(owner.Unequip(owner.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted, new NoScriptHost()));
        Accepts(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, EquipmentStateTests.Wearable(1, EquipmentStateTests.Blade, [SessionScriptPoint.InsertItem]), EquipmentStateTests.Drawn, _ => SessionInsertVerdict.Accepted, new NoScriptHost()));
        Assert.IsTrue(owner.HeroRewieldReady());
        Accepts(owner.Unequip(owner.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted, new NoScriptHost()));
        Accepts(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, EquipmentStateTests.Wearable(1, EquipmentStateTests.Blade, [SessionScriptPoint.WieldOff]), EquipmentStateTests.Drawn, _ => SessionInsertVerdict.Accepted, new NoScriptHost()));
        Assert.IsFalse(owner.HeroRewieldReady());
        var version = owner.Version;
        Assert.IsNull(CastOnHero(caster, HeroPointsPlus).JumpTo);
        Assert.AreEqual(5, owner.QueryHero()!.Stats[8]);
        Assert.AreEqual(version, owner.Version);
        Assert.IsTrue(NotReady(caster));
    }

    private static void Accepts(EquipmentMutationResult result) => Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, result.Failure.ToString());

    [TestMethod]
    public void A_heros_experience_at_the_last_level_steps_the_followers_with_no_level_even_when_nothing_changes()
    {
        var (owner, caster) = HeroSession(SkilledHero() with { Level = 50 }, experience: true);
        using var _ = owner;
        // Level 50 reaches the 51 step: the threshold is written as the experience, no level is gained, and the followers get the (50, 51) step.
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, ExperiencePlusMany.Data.Number));
        var first = caster.TakeExperienceAwards().Single();
        Assert.AreEqual(0, first.LevelsGained);
        Assert.IsNull(first.LevelUps);
        Assert.IsNotNull(first.Followers);
        var experience = owner.QueryHero()!.Experience;
        Assert.AreEqual(50, owner.QueryHero()!.Level);
        Assert.IsTrue(experience < 2_000_000_000);
        // The same write again leaves the hero as it is (NoChange) and still steps the followers, as the original re-runs the step on every write.
        var version = owner.Version;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, ExperiencePlusMany.Data.Number));
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(experience, owner.QueryHero()!.Experience);
        Assert.IsNotNull(caster.TakeExperienceAwards().Single().Followers);
        Assert.IsFalse(NotReady(caster));
    }

    // A script host with no script attached to anything: the wield runs find nothing to run.
    private sealed class NoScriptHost : ISessionItemScriptHost
    {
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => null;
        public ScrScriptLookup Script(int number) => ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => throw new InvalidOperationException("not read");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, "line", "test");
    }

    [TestMethod]
    public void A_derived_stat_is_read_not_written_for_the_hero_and_an_index_outside_the_array_is_not_ready()
    {
        var (hero, cast) = HeroSession(SkilledHero());
        using var _h = hero;
        var before = hero.Version;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(cast, DerivedPlus), "stats 8 to 16 are read, not written");
        Assert.AreEqual(before, hero.Version);
        Assert.AreEqual(0, cast.Log.Count);
        Assert.IsNull(CastOnHero(cast, BadIndex).JumpTo);
    }

    // The follower's record: the rat's sheet (the stored fields of RatFields) with dexterity 12, level 1, 3 unspent points and the basic skill keyed on dexterity at <skill> points; college 0 at rank 5 so that mastery can create the record.
    private static SessionNpcAdvancement FollowerRecord(int skill = 0)
    {
        var stats = RatFields().Critter!.BaseStats.ToArray();
        stats[1] = 12;
        stats[21] = 3;
        var basic = new int[12];
        basic[0] = skill;
        var colleges = new int[16];
        colleges[0] = 5;
        var sheet = new AdvancementSheet((CharacterRace)stats[27], false, [.. stats.Take(8)], stats[22], stats[23], stats[17], stats[21], [.. basic], [.. new int[4]], [.. colleges], [.. new int[8]], 0, 0, 0, 0);
        return new(Rat, sheet, 0);
    }

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Recorded(int skill = 0)
    {
        var (owner, caster) = Session();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MasterNpcCollege(owner.Version, FollowerRecord(skill), 0).Disposition);
        Assert.IsNotNull(owner.QueryNpcAdvancement(Rat));
        return (owner, caster);
    }

    [TestMethod]
    public void A_followers_stat_in_its_advancement_record_is_read_from_the_record_and_written_to_it_and_to_the_indexed_stats()
    {
        var (owner, caster) = Recorded();
        using var _ = owner;
        var version = owner.Version;
        // The record holds dexterity 12 (the stored field says 10): 12 + 5 = 17 on both.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(DexPlusFive), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(17, owner.QueryNpcAdvancement(Rat)!.Sheet.Stats[1]);
        Assert.AreEqual(17, owner.QueryCritterIndexedWrites(Rat).Stats[1]);
        Assert.AreEqual(version.Revision + 1, owner.Version.Revision, "one revision for both");
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.TraitIndexWritten, 1, StatField, Rat, 1) }, caster.Log.ToArray());
        // Level and unspent points are the sheet's too.
        caster.Apply(Run(LevelPlus), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(2, owner.QueryNpcAdvancement(Rat)!.Sheet.Level);
        caster.Apply(Run(UnspentMinusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(0, owner.QueryNpcAdvancement(Rat)!.Sheet.UnspentPoints, "3 - 10 clamped to 0");
        Assert.AreEqual(0, owner.QueryCritterIndexedWrites(Rat).Stats[21]);
        // The rest of the record stays as it was.
        Assert.AreEqual(5, owner.QueryNpcAdvancement(Rat)!.Sheet.Colleges[0]);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void The_stats_a_record_does_not_hold_and_the_resistances_stay_the_stored_fields_writes()
    {
        var (owner, caster) = Recorded();
        using var _ = owner;
        var sheet = owner.QueryNpcAdvancement(Rat)!.Sheet;
        caster.Apply(Run(AlignmentPlusMany.Data.Number), 0, At(Rat, SpellTargetObject.Npc));
        caster.Apply(Run(FirePlus), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(1000, owner.QueryCritterIndexedWrites(Rat).Stats[19]);
        Assert.AreEqual(25, owner.QueryCritterIndexedWrites(Rat).Resistances[1]);
        Assert.AreEqual(sheet, owner.QueryNpcAdvancement(Rat)!.Sheet, "the record holds neither");
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_lowering_of_a_records_stat_asks_the_guards_with_the_records_skills()
    {
        // The stored skill has 2 points keyed on dexterity (a lowering to 1 is refused for a follower with no record); the record has none, so dexterity goes down to the minimum.
        var (owner, caster) = Recorded(skill: 0);
        using var _ = owner;
        caster.Apply(Run(DexMinusMany), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(1, owner.QueryNpcAdvancement(Rat)!.Sheet.Stats[1]);
        Assert.AreEqual(1, owner.QueryCritterIndexedWrites(Rat).Stats[1]);
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.TraitIndexRefused));
        // With the skill in the record the guard refuses, nothing written.
        var (kept, tried) = Recorded(skill: 2);
        using var _k = kept;
        var version = kept.Version;
        tried.Apply(Run(DexMinusMany), 0, At(Rat, SpellTargetObject.Npc));
        Assert.AreEqual(version, kept.Version);
        Assert.AreEqual(12, kept.QueryNpcAdvancement(Rat)!.Sheet.Stats[1]);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitIndexRefused, 1, 0, Rat, 1), tried.Log.Last());
    }

    [TestMethod]
    public void The_records_write_refuses_what_is_no_record_or_no_sheet_stat_and_a_stale_version()
    {
        var (owner, _) = Session();
        using var _o = owner;
        var map = Exterior;
        Assert.AreEqual(GameSessionFailure.InvalidAdvancement, owner.WriteNpcAdvancementStat(owner.Version, map, Rat, 1, 15).Failure, "no record");
        var (recorded, _) = Recorded();
        using var _r = recorded;
        Assert.AreEqual(GameSessionFailure.InvalidAdvancement, recorded.WriteNpcAdvancementStat(recorded.Version, map, Rat, 19, 15).Failure, "alignment is not the sheet's");
        Assert.AreEqual(GameSessionFailure.InvalidAdvancement, recorded.WriteNpcAdvancementStat(recorded.Version, map, Rat, 1, 99).Failure, "outside the race's range");
        var stale = recorded.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, recorded.WriteNpcAdvancementStat(stale, map, Rat, 1, 15).Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, recorded.WriteNpcAdvancementStat(stale, map, Rat, 1, 16).Failure);
        Assert.AreEqual(15, recorded.QueryNpcAdvancement(Rat)!.Sheet.Stats[1], "a refusal changes nothing");
        Assert.AreEqual(GameSessionDisposition.NoChange, recorded.WriteNpcAdvancementStat(recorded.Version, map, Rat, 1, 15).Disposition);
    }

    // ARC-MAGIC-1628: a follower's gender is no sheet field (the stored field and the innate effect, as for any NPC); its race is the sheet's and the sheet must stay valid.
    private static (GameSessionOwner Owner, SessionSpellCaster Caster) RecordedWithEffects()
    {
        var (owner, caster) = Recorded();
        owner.AttachInnateEffects([Female, DwarfEffect, HumanEffect]);
        return (owner, caster);
    }

    [TestMethod]
    public void A_followers_gender_with_an_advancement_record_is_the_stored_field_and_the_innate_effect()
    {
        var (owner, caster) = RecordedWithEffects();
        using var _ = owner;
        var sheet = owner.QueryNpcAdvancement(Rat)!.Sheet;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(GenderToFemale), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(0, owner.QueryCritterIndexedWrites(Rat).Stats[26]);
        Assert.AreEqual(sheet, owner.QueryNpcAdvancement(Rat)!.Sheet, "the sheet holds no gender");
        var target = caster.LifeTarget(Rat)!;
        CollectionAssert.AreEqual(new[] { (330, EffectCause.Gender) }, Held(owner, target));
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_followers_race_with_an_advancement_record_is_the_sheets_race_and_the_stored_field_and_adds_the_race_effect()
    {
        var (owner, caster) = RecordedWithEffects();
        using var _ = owner;
        var from = (int)owner.QueryNpcAdvancement(Rat)!.Sheet.Race;
        Assert.AreEqual(0, from, "the rat's stored race is human");
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(RaceDwarf), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(CharacterRace.Dwarf, owner.QueryNpcAdvancement(Rat)!.Sheet.Race);
        Assert.AreEqual(1, owner.QueryCritterIndexedWrites(Rat).Stats[27]);
        CollectionAssert.AreEqual(new[] { (65, EffectCause.Race) }, Held(owner, caster.LifeTarget(Rat)!));
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_followers_race_that_would_leave_its_sheet_invalid_is_not_ready_and_nothing_is_written()
    {
        var (owner, caster) = RecordedWithEffects();
        using var _ = owner;
        // A dwarf with strength 21 (its maximum), made human again: the human maximum is 20, so the sheet would no longer be valid.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(RaceDwarf), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteNpcAdvancementStat(owner.Version, Exterior, Rat, 0, 21).Disposition);
        var held = Held(owner, caster.LifeTarget(Rat)!);
        var version = owner.Version;
        var log = caster.Log.Count;
        Assert.IsNull(caster.Apply(Run(RaceHuman), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.AreEqual(version, owner.Version, "nothing written, not even the removal of the old race effect");
        Assert.AreEqual(CharacterRace.Dwarf, owner.QueryNpcAdvancement(Rat)!.Sheet.Race);
        CollectionAssert.AreEqual(held, Held(owner, caster.LifeTarget(Rat)!));
        Assert.IsTrue(caster.Log.Skip(log).Any(e => e.Kind == SpellCastEventKind.ComponentNotReady));
    }
    // ARC-MAGIC-1644 (ledger RM3c, IE7, WE6, TI7; exe read 0x465530, 0x441980): an NPC's rewield runs the wield scripts of its worn items, wield off (25) and then wield on (24), the item as the attachee and the NPC as the
    // triggerer, with the condition "items are being rewielded" true. Script numbers, programs, effects and values are invented (the installed shape of the technological items, TI7).
    private const int NpcWieldOn = 710, NpcWieldOff = 711, Gate = 712, Charm = 50;
    private static readonly SessionEffectDefinition CharmEffect = new(Charm, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1)]);

    private sealed class NpcScriptHost : ISessionItemScriptHost
    {
        public Dictionary<(string, int), int> Slots { get; } = [];
        public Dictionary<int, ScrProgram> Programs { get; } = new()
        {
            [NpcWieldOn] = WieldEventScriptTests.Scaled, [NpcWieldOff] = WieldEventScriptTests.CallOff,
            // Adds a charm only while the items are being rewielded; the other program targets the item (not the wearer).
            [Gate] = WieldEventScriptTests.Program(WieldEventScriptTests.If(WieldEventScriptTests.C(ScrCatalogue.Condition.ItemsBeingRewielded),
                WieldEventScriptTests.C(ScrCatalogue.Action.AddEffect, (WieldEventScriptTests.Triggerer, 0), WieldEventScriptTests.N(Charm), WieldEventScriptTests.N(5)))),
            [713] = WieldEventScriptTests.Program(WieldEventScriptTests.Do(WieldEventScriptTests.C(ScrCatalogue.Action.AddEffect, (WieldEventScriptTests.Attachee, 0), WieldEventScriptTests.N(Charm), WieldEventScriptTests.N(5)))),
        };
        public Dictionary<int, SessionEffectDefinition> Effects { get; } = new() { [Charm] = CharmEffect };

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Slots.TryGetValue((attachee.Code, point), out var script) ? new ScrSlot(script, 0, 0) : null;
        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => throw new InvalidOperationException("the session reads flags itself");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, "line", "test");
        public SessionEffectDefinition? EffectDefinition(int number) => Effects.TryGetValue(number, out var rule) ? rule : null;
    }

    // An NPC wearing a ring (slot 1003) of magickal complexity 40 whose wield points carry the given scripts, with the host's slots and the caster wired to the host.
    private static (GameSessionOwner Owner, SessionSpellCaster Caster, NpcScriptHost Host, SessionLifeTarget Target) Ringed(int on = NpcWieldOn, int off = NpcWieldOff, params SessionSeedItem[] more)
    {
        var ring = new SessionSeedItem("ring", Facts(SessionItemType.Armor, 3803, 1003, 40, null, SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff), null);
        var (owner, caster) = Dressed([ring, .. more]);
        var host = new NpcScriptHost();
        host.Slots[("ring", SessionScriptPoint.WieldOn)] = on;
        host.Slots[("ring", SessionScriptPoint.WieldOff)] = off;
        caster.ItemScripts = () => host;
        return (owner, caster, host, SessionCritterCombat.From(Rat, RatFields()).Combat!.Target);
    }

    // The charms the scaled wield-on script adds: ten times the item's power ratio under the NPC's aptitude, rounded, the aptitude from the stored magick and tech points as the stat getter derives it.
    private static int ExpectedCharms(int magick, int tech, int complexity = 40) =>
        (EquipmentRules.EffectivePowerRatio(complexity, Math.Clamp((50 * magick - 55 * tech) / 10, EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude)) * 10 + 50) / 100;

    private static int Charms(GameSessionOwner owner, SessionLifeTarget target) => owner.QueryNpcEffects(target)!.Entries.Count(e => e.Number == Charm && e.Cause == EffectCause.Item);

    [TestMethod]
    public void An_npcs_rewield_runs_wield_off_then_wield_on_of_its_worn_items_over_the_new_points()
    {
        var (owner, caster, _, target) = Ringed();
        using var o = owner;
        var stats = RatFields().Critter!.BaseStats;
        var version = owner.Version.Revision;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.IsFalse(NotReady(caster));
        var first = ExpectedCharms(stats[22] + 7, stats[23]);
        Assert.IsTrue(first > 0, "the fixture adds charms");
        Assert.AreEqual(stats[22] + 7, owner.QueryCritterIndexedWrites(Rat).Stats[22]);
        Assert.AreEqual(first, Charms(owner, target), "the run read the aptitude with the new magick points laid in; wield off found no counter yet");
        Assert.AreEqual(new SessionItemScriptSlot("ring", SessionScriptPoint.WieldOn, NpcWieldOn, 0, (uint)first), owner.QueryItemScriptSlots().Single(), "counter 0 keeps the count");
        Assert.IsTrue(owner.Version.Revision > version);

        // The second write: wield off (through call-ex into the wield-on script) removes the charms the first run counted, wield on adds the count of the new aptitude: the order is off then on.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(TechPlus), 0, At(Rat, SpellTargetObject.Npc)));
        var second = ExpectedCharms(stats[22] + 7, stats[23] + 9);
        Assert.AreNotEqual(first, second, "the tech points moved the aptitude");
        Assert.AreEqual(second, Charms(owner, target));
        Assert.AreEqual((uint)second, owner.QueryItemScriptSlots().Single().Counters);
        Assert.IsTrue(owner.QueryNpcEffects(target)!.Entries.All(e => e.Cause == EffectCause.Item));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady));
    }

    [TestMethod]
    public void The_condition_items_are_being_rewielded_is_true_in_an_npcs_rewield()
    {
        var (owner, caster, _, target) = Ringed(Gate, 0);
        using var o = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)));
        Assert.AreEqual(1, Charms(owner, target));
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void An_npc_rewield_the_session_cannot_apply_writes_nothing()
    {
        // No rule of the effect the script adds: the run is refused, the stat is not written.
        var (owner, caster, host, target) = Ringed();
        using var primary = owner;
        host.Effects.Clear();
        var version = owner.Version;
        Assert.IsNull(caster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsTrue(NotReady(caster));
        Assert.AreEqual(version, owner.Version);
        Assert.IsTrue(owner.QueryCritterIndexedWrites(Rat).IsEmpty);
        Assert.AreEqual(0, Charms(owner, target));

        // A script that changes another object's effects (the item's) is not the wearer's: refused too.
        var (other, otherCaster, _, otherTarget) = Ringed(713, 0);
        using var _o = other;
        Assert.IsNull(otherCaster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsTrue(NotReady(otherCaster));
        Assert.IsTrue(other.QueryCritterIndexedWrites(Rat).IsEmpty);
        Assert.AreEqual(0, Charms(other, otherTarget));

        // Without a host (or a host that answers none) the same ring leaves the points not ready, as before.
        var (bare, bareCaster, _, _) = Ringed();
        using var _b = bare;
        bareCaster.ItemScripts = () => null;
        Assert.IsNull(bareCaster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsTrue(NotReady(bareCaster));
        Assert.IsTrue(bare.QueryCritterIndexedWrites(Rat).IsEmpty);
    }
    [TestMethod]
    public void An_npc_rewield_walks_the_slots_in_order_and_a_later_refusal_leaves_everything_unapplied()
    {
        // A helmet (slot 1000) whose wield on adds a charm while rewielding, then the ring (slot 1003): the helmet's effect lands before the ring's run reads the list.
        var helm = new SessionSeedItem("helm", Facts(SessionItemType.Armor, 3800, 1000, 0, null, SessionScriptPoint.WieldOn), null);
        var (owner, caster, host, target) = Ringed(NpcWieldOn, NpcWieldOff, helm);
        using var o = owner;
        host.Slots[("helm", SessionScriptPoint.WieldOn)] = Gate;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)));
        var expected = 1 + ExpectedCharms(RatFields().Critter!.BaseStats[22] + 7, RatFields().Critter!.BaseStats[23]);
        Assert.AreEqual(expected, Charms(owner, target), "both items' runs applied together");

        // The ring's script (a later slot) is refused: the helmet's earlier run is not applied either, and the stat is not written.
        var (other, otherCaster, otherHost, otherTarget) = Ringed(713, 0, new SessionSeedItem("helm", Facts(SessionItemType.Armor, 3800, 1000, 0, null, SessionScriptPoint.WieldOn), null));
        using var p = other;
        otherHost.Slots[("helm", SessionScriptPoint.WieldOn)] = Gate;
        var version = other.Version;
        Assert.IsNull(otherCaster.Apply(Run(MagickPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.AreEqual(version, other.Version);
        Assert.AreEqual(0, Charms(other, otherTarget));
        Assert.IsTrue(other.QueryCritterIndexedWrites(Rat).IsEmpty);
    }

    // ARC-MAGIC-1744 (ledger RM3c, IE7): the hero's rewield after a store of magick or tech points runs its worn items' wield scripts as an NPC's does: wield off (25) then wield on (24), the hero as the triggerer, the condition
    // "items are being rewielded" true, the reads over the new points. Spells 653 (magick +7) and 654 (tech -3) are the hero's point spells.
    private static (GameSessionOwner Owner, SessionSpellCaster Caster, NpcScriptHost Host) WornHero(int on = NpcWieldOn, int off = NpcWieldOff, bool equip = true)
    {
        var (owner, caster) = HeroSession(SkilledHero([8, 10, 8, 8, 8, 8, 8, 8, 5, 2]), records: HeroRecords());
        var host = new NpcScriptHost();
        host.Slots[("kit:1", SessionScriptPoint.WieldOn)] = on;
        host.Slots[("kit:1", SessionScriptPoint.WieldOff)] = off;
        caster.ItemScripts = () => host;
        if (equip)
            Accepts(owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, EquipmentStateTests.Wearable(1, EquipmentStateTests.Blade with { MagickTechComplexity = 40 }, [SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff]),
                EquipmentStateTests.Drawn, _ => SessionInsertVerdict.Accepted, host));
        return (owner, caster, host);
    }

    private static int HeroCharms(GameSessionOwner owner) => owner.QueryHeroEffects()!.Effects.Count(e => e.Number == Charm && e.Cause == EffectCause.Item);

    // The charms the scaled wield-on script adds: ten times the item's power ratio under the hero's aptitude as the stat getter reads it now.
    private static int HeroExpectedCharms(GameSessionOwner owner) =>
        (EquipmentRules.EffectivePowerRatio(40, Math.Clamp(owner.QueryHeroScriptStat(GameSessionOwner.ScriptStatAptitude)!.Value, EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude)) * 10 + 50) / 100;

    [TestMethod]
    public void A_heros_rewield_runs_wield_off_then_wield_on_of_its_worn_items_over_the_new_points()
    {
        var (owner, caster, _) = WornHero();
        using var o = owner;
        var equipped = HeroCharms(owner);
        Assert.IsTrue(equipped > 0, "the equip itself ran wield on over the old points");
        Assert.AreEqual(equipped, HeroExpectedCharms(owner));
        Assert.IsFalse(owner.HeroRewieldReady());
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, HeroPointsPlus));
        Assert.IsFalse(NotReady(caster));
        Assert.AreEqual(12, owner.QueryHeroStoredStat(22));
        var first = HeroExpectedCharms(owner);
        Assert.AreNotEqual(equipped, first, "the magick points moved the aptitude the runs read");
        Assert.AreEqual(first, HeroCharms(owner), "wield off took the old charms, wield on added the count of the new aptitude");
        Assert.AreEqual((uint)first, owner.QueryItemScriptSlots().Single(slot => slot.Point == SessionScriptPoint.WieldOn).Counters);
        Assert.IsTrue(owner.QueryHeroEffects()!.Effects.All(e => e.Cause == EffectCause.Item));

        CastOnHero(caster, HeroTechPlus);
        Assert.AreEqual(22, owner.QueryHeroStoredStat(23));
        Assert.AreEqual(HeroExpectedCharms(owner), HeroCharms(owner));
        Assert.AreNotEqual(first, HeroCharms(owner), "the tech points moved it again");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady));
    }

    [TestMethod]
    public void The_condition_items_are_being_rewielded_is_true_in_the_heros_rewield()
    {
        // The gate adds one charm only while rewielding: the equip adds none, the rewield one.
        var (owner, caster, _) = WornHero(Gate, 0);
        using var o = owner;
        Assert.AreEqual(0, HeroCharms(owner));
        CastOnHero(caster, HeroPointsPlus);
        Assert.AreEqual(1, HeroCharms(owner));
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_hero_rewield_the_session_cannot_apply_writes_nothing()
    {
        // No rule of the effect the script adds: the run is refused, the stat is not written.
        var (owner, caster, host) = WornHero(Gate, 0);
        using var primary = owner;
        host.Effects.Clear();
        var version = owner.Version;
        Assert.IsNull(CastOnHero(caster, HeroPointsPlus).JumpTo);
        Assert.IsTrue(NotReady(caster));
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(5, owner.QueryHeroStoredStat(22));
        Assert.AreEqual(0, HeroCharms(owner));

        // Without a host (or a host that answers none) the scripted item leaves the points not ready.
        var (bare, bareCaster, _) = WornHero(Gate, 0);
        using var _b = bare;
        bareCaster.ItemScripts = () => null;
        var bareVersion = bare.Version;
        Assert.IsNull(CastOnHero(bareCaster, HeroPointsPlus).JumpTo);
        Assert.IsTrue(NotReady(bareCaster));
        Assert.AreEqual(bareVersion, bare.Version);
        Assert.AreEqual(5, bare.QueryHeroStoredStat(22));
    }

    [TestMethod]
    public void A_hero_without_a_worn_wield_script_stores_its_points_and_runs_nothing_even_through_a_host()
    {
        var (owner, caster, _) = WornHero(equip: false);
        using var o = owner;
        Assert.IsTrue(owner.HeroRewieldReady());
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(caster, HeroPointsPlus));
        Assert.AreEqual(12, owner.QueryHeroStoredStat(22));
        Assert.AreEqual(0, HeroCharms(owner));
        Assert.IsFalse(NotReady(caster));
    }
}
