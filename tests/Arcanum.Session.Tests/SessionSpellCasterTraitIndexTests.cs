using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1295 (ledger RM3; exe read 0x452D80, 0x4B0740, 0x4B0980, 0x4B0FC0, 0x4B0FD0, 0x407340, 0x4072D0): the TraitIdx component adds its value to an NPC's stored base stat or resistance through the cast host.
// Records, ids and numbers are invented; the NPC is an oid: entity whose stored values the host map gives.
[TestClass]
public sealed class SessionSpellCasterTraitIndexTests
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
        IntMinusOne, IntMinusTwo, IntMinusMany, Stat25MinusMany, WillMinusOne, WillMinusTwo, LevelMinusMany, LevelPlusMany, ExperiencePlusMany, AlignmentPlusMany, AlignmentMinusMany, FatePlusMany, UnspentMinusMany, Stat25PlusMany,
    }.ToDictionary(r => r.Data.Number);

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
        Assert.IsNull(caster.Apply(Run(FirePlus), 0, At(Hero, SpellTargetObject.Pc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(BadIndex), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(BadField), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(StrengthPlus), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsNull(caster.Apply(Run(FirePlus), 0, At("pc:other", SpellTargetObject.Pc)).JumpTo);
        Assert.AreEqual(5, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.TraitIdx));
        Assert.AreEqual(version, owner.Version);
    }

    [TestMethod]
    public void The_stats_whose_setter_does_more_than_store_are_not_ready_with_the_reason_in_the_ledger()
    {
        // Poison (the poison timer and the life state), gender and race (the innate effect swap): each is read and not written (magick and tech points run the rewield, ARC-MAGIC-1507, below).
        foreach (var index in new[] { 24, 26, 27 })
        {
            var (owner, _) = Session();
            using var _o = owner;
            var number = 400 + index;
            var records = new Dictionary<int, SessionSpellRecord>(Records) { [number] = Spell(number, StatField, index, 1) };
            var tried = new SessionSpellCaster(owner, n => records.GetValueOrDefault(n), new Map(owner, RatFields()), new Events());
            var version = owner.Version;
            Assert.IsNull(tried.Apply(Run(number), 0, At(Rat, SpellTargetObject.Npc)).JumpTo, $"stat {index}");
            Assert.AreEqual(version, owner.Version, $"stat {index}");
            Assert.IsTrue(NotReady(tried), $"stat {index}");
        }
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

    [TestMethod]
    public void A_level_with_the_fatigue_timer_is_not_ready_and_writes_nothing()
    {
        // 800 at strength 2 (1000 carried) is 80 %, level 5 (75 to 89 %): the timer of 0x45E820 mode 1, which the session keeps for the hero alone.
        var (owner, caster, target) = Carrying(3, [800]);
        using var _ = owner;
        var version = owner.Version;
        Assert.IsNull(caster.Apply(Run(StrengthMinusOne), 0, At(Rat, SpellTargetObject.Npc)).JumpTo);
        Assert.IsTrue(NotReady(caster));
        Assert.AreEqual(version, owner.Version);
        Assert.IsTrue(owner.QueryCritterIndexedWrites(Rat).IsEmpty);
        Assert.AreEqual(0, Entries(owner, target));
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
    public void The_heros_strength_write_changes_its_encumbrance_through_the_revision_as_every_strength_change_does()
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

    [TestMethod]
    public void A_heros_level_or_experience_that_reaches_no_level_is_stored_and_one_that_does_is_not_ready()
    {
        var (owner, caster) = HeroSession(SkilledHero(), experience: true);
        using var _ = owner;
        CastOnHero(caster, LevelPlus);
        Assert.AreEqual(2, owner.QueryHero()!.Level, "level 1 + 1; 0 experience reaches no level of the table");
        var version = owner.Version;
        // 2,100,000,000 experience clamped to 2,000,000,000 would run the recalculation 0x4A69C0 up to level 50 (messages, scheme, followers' step): not ready, nothing written.
        Assert.IsNull(CastOnHero(caster, ExperiencePlusMany.Data.Number).JumpTo);
        Assert.AreEqual(0, owner.QueryHero()!.Experience);
        // Level 0 with 0 experience already reaches level 1: the recalculation would raise it again.
        Assert.IsNull(CastOnHero(caster, LevelMinusMany.Data.Number).JumpTo);
        Assert.AreEqual(2, owner.QueryHero()!.Level);
        Assert.AreEqual(2, caster.Log.Count(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.TraitIdx));
        Assert.AreEqual(version.Revision, owner.Version.Revision);
    }

    [TestMethod]
    public void A_heros_level_is_not_ready_without_the_experience_table()
    {
        var (owner, caster) = HeroSession(SkilledHero());
        using var _ = owner;
        var version = owner.Version;
        Assert.IsNull(CastOnHero(caster, LevelPlus).JumpTo);
        Assert.AreEqual(1, owner.QueryHero()!.Level);
        Assert.AreEqual(version, owner.Version);
        Assert.IsTrue(NotReady(caster));
    }

    [TestMethod]
    public void What_the_heros_state_does_not_hold_is_not_ready_and_a_derived_stat_is_read_not_written()
    {
        // Magick and tech points (the rewield runs the worn items' scripts), poison, gender, race, stat 25 and a resistance (the hero stores none).
        foreach (var index in new[] { 22, 23, 24, 25, 26, 27 })
        {
            var number = 600 + index;
            var records = new Dictionary<int, SessionSpellRecord>(Records) { [number] = Spell(number, StatField, index, 1) };
            var (owner, caster) = HeroSession(SkilledHero(), records: records);
            using var _ = owner;
            var version = owner.Version;
            Assert.IsNull(CastOnHero(caster, number).JumpTo, $"stat {index}");
            Assert.AreEqual(version, owner.Version, $"stat {index}");
            Assert.IsTrue(NotReady(caster), $"stat {index}");
        }
        var (hero, cast) = HeroSession(SkilledHero());
        using var _h = hero;
        var before = hero.Version;
        Assert.AreEqual(SpellComponentOutcome.None, CastOnHero(cast, DerivedPlus), "stats 8 to 16 are read, not written");
        Assert.AreEqual(before, hero.Version);
        Assert.AreEqual(0, cast.Log.Count);
        Assert.IsNull(CastOnHero(cast, FirePlus).JumpTo);
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
}
