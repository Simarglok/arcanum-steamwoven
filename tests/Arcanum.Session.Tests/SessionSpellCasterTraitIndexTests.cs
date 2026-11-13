using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
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
    private const int FirePlus = 200, MagicMinus = 201, DexMinusOne = 210, DexMinusTwo = 211, DexMinusMany = 212, DexPlusFive = 213, IntPlus = 214, DerivedPlus = 215, CharismaPlus = 216, BadIndex = 217, BadField = 218, StrengthPlus = 219, LevelPlus = 220;

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

    private static readonly Dictionary<int, SessionSpellRecord> Records = new[]
    {
        Spell(FirePlus, ResistField, 1, 25), Spell(MagicMinus, ResistField, 4, -200),
        Spell(DexMinusOne, StatField, 1, -1), Spell(DexMinusTwo, StatField, 1, -2), Spell(DexMinusMany, StatField, 1, -20), Spell(DexPlusFive, StatField, 1, 5),
        Spell(IntPlus, StatField, 4, 1), Spell(DerivedPlus, StatField, 9, 3), Spell(CharismaPlus, StatField, 7, 1),
        Spell(BadIndex, StatField, 28, 1), Spell(BadField, 99, 0, 1), Spell(StrengthPlus, StatField, 0, 1), Spell(LevelPlus, StatField, 17, 1),
        IntMinusOne, IntMinusTwo, IntMinusMany, WillMinusOne, WillMinusTwo, LevelMinusMany, LevelPlusMany, ExperiencePlusMany, AlignmentPlusMany, AlignmentMinusMany, FatePlusMany, UnspentMinusMany, Stat25PlusMany,
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
        // The hero (the character owner's stats), an index and a field the host has no state for, strength (the encumbrance recalculation needs the NPC's carried weight), and a PC that is not the hero.
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
        // Magick points and tech points (the rewield of the worn items), poison (the poison timer and the life state), gender and race (the innate effect swap): each is read and not written.
        foreach (var index in new[] { 22, 23, 24, 26, 27 })
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
}
