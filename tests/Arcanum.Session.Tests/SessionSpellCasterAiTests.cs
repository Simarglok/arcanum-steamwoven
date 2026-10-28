using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-WORLD-102: the cast host's AI half over a synthetic map (the hero and an NPC mage): the spell book from the fields or the session's
// record, the record facts, the cost with the fields' mastery, the free maintenance count, under the influence (0x459380), the global
// lists and perform's cast (0x4AC250). Records and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterAiTests
{
    private const string Mage = "oid:mage";
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), MageTile = new(8, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter mage, int lineCost = 0) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Mage => new(id, SpellTargetObject.Npc, MageTile, HitPoints: 20),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(lineCost, null);
        public SpellCastCritter? Critter(string entity) => entity == Mage ? mage : null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    // College 3 at rank 2 with college 3 mastered: spells 15 and 16 known. Intelligence 12, willpower 9.
    private static SpellCastCritter MageCritter()
    {
        var stats = AttackStateTests.Stats(willpower: 9);
        stats[4] = 12;
        var fields = AttackStateTests.Npc(stats: stats, critterFlags: 0);
        var spellTech = new int[25];
        spellTech[3] = 2; spellTech[16] = 3;
        fields = fields with { Critter = fields.Critter! with { SpellTech = spellTech } };
        var combat = SessionCritterCombat.From(Mage, fields).Combat!;
        return new(combat, 0, 0, 0, 0, 0) { Book = SpellBookRules.FromFields(fields.Critter.SpellTech) };
    }

    // Spell 15 (college 3): an aggressive bolt at a critter (BEGIN targets 0x40), cost 10, maintained every 5 s when asked, with item
    // triggers when asked and an offensive AI value of 3.
    private static SessionSpellRecord Bolt(int number = 15, int maintainPeriod = 0, uint itemTriggers = 0, int range = 20) =>
        SessionSpellRecords.Translate(Record(number: number) with
        {
            Cost = 10, MaintainPeriod = maintainPeriod, ItemTriggers = itemTriggers, MinimumWillpower = 8, Range = range, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0,
            AiPriorities = [-1, -1, -1, 3, -1, -1, -1, -1, -1, -1],
        });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(params SessionSpellRecord[] records)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var caster = new SessionSpellCaster(owner, n => records.FirstOrDefault(r => r.Data.Number == n), new Map(owner, MageCritter()), new Events());
        return (owner, caster);
    }

    [TestMethod]
    public void The_npc_book_comes_from_its_fields_and_its_mastery_halves_the_cost()
    {
        var (owner, caster) = Session(Bolt(), Bolt(16), Bolt(17), Bolt(20));
        using var _ = owner;
        Assert.AreEqual((true, true, false), (caster.AiKnows(Mage, 15), caster.AiKnows(Mage, 16), caster.AiKnows(Mage, 17)));
        Assert.AreEqual((2, 0, 0), (caster.AiCollegeLevel(Mage, 3), caster.AiCollegeLevel(Mage, 4), caster.AiCollegeLevel(Mage, 16)));
        // The fields' mastered college 3 halves its spells' cost (0x4B1660), and not college 4's.
        Assert.AreEqual((5, 10), (caster.AiCastCost(Mage, 15), caster.AiCastCost(Mage, 20)));
        Assert.IsFalse(caster.AiKnows("oid:nobody", 15));
        Assert.IsNull(caster.Book("oid:nobody"));
        Assert.AreEqual(owner.QueryHeroSpellBook(), caster.Book(Hero));
    }

    [TestMethod]
    public void The_record_facts_the_global_lists_and_the_free_maintenance_count()
    {
        var (owner, caster) = Session(Bolt(maintainPeriod: 5, itemTriggers: 0x40), Bolt(16));
        using var _ = owner;
        Assert.AreEqual(new AiSpellFacts(true, SpellBookRules.MinimumIntelligence, 8, true), caster.AiSpell(15));
        Assert.AreEqual(new AiSpellFacts(false, SpellBookRules.MinimumIntelligence, 8, false), caster.AiSpell(16));
        Assert.AreEqual(default, caster.AiSpell(99));
        // The offensive global list: both records (equal values: their order is the runtime sort's, AiActionListsTests' concern).
        CollectionAssert.AreEquivalent(new[] { 15, 16 }, caster.AiLists[AiActionList.Offensive].ToArray());
        Assert.AreEqual(0, caster.AiLists[AiActionList.Defensive].Length);
        // Intelligence 12: 3 slots, none used; the hero's from its own intelligence; anything else none.
        Assert.AreEqual(3, caster.AiFreeMaintainSlots(Mage));
        Assert.AreEqual(0, caster.AiFreeMaintainSlots("oid:nobody"));
    }

    [TestMethod]
    public void Perform_cast_starts_the_npc_run_at_its_target_and_under_the_influence_follows_the_active_run()
    {
        var bolt = Bolt();
        var (owner, caster) = Session(bolt);
        using var _ = owner;
        Assert.IsFalse(caster.AiUnderInfluence(Hero, 15));
        var cast = caster.CastByAi(Mage, 15, Hero);
        Assert.AreEqual(SpellCastAttemptOutcome.Started, cast.Outcome, $"{cast.Run?.Failure} {cast.Run?.Creation?.Check?.Refusal}");
        var run = owner.QuerySpellRuns()[cast.RunId!.Value]!;
        Assert.AreEqual((Mage, Mage, Hero, SpellRunAction.Begin), (run.Source?.Id, run.Parent?.Id, run.Target?.Id, run.Action));
        // 0x459380: an active run of the spell with the object as its target.
        Assert.AreEqual((true, false, false), (caster.AiUnderInfluence(Hero, 15), caster.AiUnderInfluence(Hero, 16), caster.AiUnderInfluence(Mage, 15)));
        // Its casting goal is the host's: the start, then the end before the release frees it.
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run.Id, new NoCombat()).Outcome);
        Assert.IsNotNull(caster.EndGoal(run.Id));
        Assert.IsNull(owner.QuerySpellRuns()[run.Id]);
        Assert.IsFalse(caster.AiUnderInfluence(Hero, 15));
    }

    [TestMethod]
    public void Perform_cast_refused_by_its_check_starts_nothing()
    {
        var (owner, caster) = Session(Bolt(range: 1));
        using var _ = owner;
        // The hero stands 3 tiles away, beyond a range of 1: the check (0x4564E0) refuses, and nothing starts.
        var cast = caster.CastByAi(Mage, 15, Hero);
        Assert.AreEqual(SpellCastAttemptOutcome.Refused, cast.Outcome);
        Assert.IsNull(cast.Run, "the perform's own check refused before any run");
        Assert.AreEqual(0, owner.QuerySpellRuns().Count);
        Assert.AreEqual(SpellCastAttemptOutcome.Nothing, caster.CastByAi(Mage, 15, "oid:nobody").Outcome);
        Assert.AreEqual(SpellCastAttemptOutcome.Nothing, caster.CastByAi(Mage, 99, Hero).Outcome);
    }

    [TestMethod]
    public void The_fail_chance_predictor_reads_the_target_and_the_caster_counts_none()
    {
        var (owner, caster) = Session(Bolt());
        using var _ = owner;
        // Resist stat 6 (willpower) with modifier −2: the caster itself is R = 0; the hero, storing no resistance, R = 0 too unless its
        // save (its own d20) is made, which this damaging record answers with at least 50. Without a record: 0.
        Assert.AreEqual(0, caster.AiFailChance(Mage, Mage, 15));
        var chance = caster.AiFailChance(Mage, Hero, 15);
        Assert.IsTrue(chance is 0 or >= 50, $"the predictor's answer {chance}");
        Assert.AreEqual(0, caster.AiFailChance(Mage, Hero, 99));
    }
}
