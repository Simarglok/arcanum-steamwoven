using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-049 (E4-e; exe read of 0x451740, 0x455A20, 0x455AC0 and 0x455C30): the Cast component starts another spell as a cast of the run's parent, with no source, the current target object and tile, and no casting goal (goal 73).
// The host keeps the request and starts it once the run rules are idle. Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterNestedTests
{
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5);
    private static readonly SpellArea Area = new(0x4, 0, 0, 0, -1);
    private const int OuterNumber = 120, InnerNumber = 121, SelfNumber = 122, MissingNumber = 123, ChainNumber = 124, LongNumber = 125, OuterLongNumber = 126;

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id == Hero ? new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints) : null;
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static SessionSpellRecord Spell(int number, params SpellComponent[] components) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral, components: [.. components])
            with { Cost = 10, MaintainCost = 0, MaintainPeriod = 0, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static SpellComponent Cast(int spell) => new(SpellAction.Begin, SpellComponentType.Cast, Area, 0, 0, new SpellReferenceData(spell));

    private static readonly Dictionary<int, SessionSpellRecord> Records = new[]
    {
        Spell(OuterNumber, Cast(InnerNumber)),
        Spell(InnerNumber, Damage(SpellAction.Begin, 0, 3, 3, 0)),
        Spell(SelfNumber, Cast(SelfNumber)),
        Spell(MissingNumber, Cast(10000)),
        Spell(ChainNumber, Cast(ChainNumber), Damage(SpellAction.Begin, 0, 1, 1, 0)),
        Spell(LongNumber, Damage(SpellAction.Begin, 0, 1, 1, 0)) with { Run = Spell(LongNumber, Damage(SpellAction.Begin, 0, 1, 1, 0)).Run with { Duration = new(30) } },
        Spell(OuterLongNumber, Cast(LongNumber)),
    }.ToDictionary(r => r.Data.Number);

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        return (owner, new SessionSpellCaster(owner, n => Records.GetValueOrDefault(n), new Map(owner), new Events()));
    }

    // A run of the spell that has not begun, the hero its parent and target and no source (as the nested run of the original has).
    private static SpellRun Start(GameSessionOwner owner, SessionSpellCaster caster, int number)
    {
        var me = caster.Facts(Hero)!;
        var started = owner.StartSpellRun(owner.Version, new(new(number, me, null, me, me.Location), SkipCastGoal: true), Records[number].Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return owner.QuerySpellRuns()[started.Creation!.Run!.Id]!;
    }

    private static SpellRunTarget OnHero(SessionSpellCaster caster) => new(caster.RunObject(Hero), SpellLocation.None);

    private static SpellCastEvent[] Nested(SessionSpellCaster caster) => caster.Log.Where(e => e.Kind == SpellCastEventKind.NestedCast).ToArray();

    [TestMethod]
    public void A_cast_component_starts_the_spell_as_the_parents_cast_at_the_target_without_the_casting_goal()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var life = owner.QueryHeroLife()!.Status.HitPoints;
        var outer = Start(owner, caster, OuterNumber);
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(outer, 0, OnHero(caster)));
        Assert.AreEqual(0, Nested(caster).Length, "the request waits for the run rules to be idle: nothing starts inside the component");
        Assert.AreEqual(life, owner.QueryHeroLife()!.Status.HitPoints);
        // The next action on the runs (here the origin run's end) leaves the run rules idle, and the host starts the nested run after it.
        caster.Act(new(SessionSpellRunActionKind.Free, outer.Id));
        var started = Nested(caster).Single();
        Assert.AreEqual((outer.Id, InnerNumber, Hero), (started.Run, started.Value, started.Object));
        Assert.IsTrue(started.Detail >= 0, "a run slot");
        Assert.AreEqual(3, life - owner.QueryHeroLife()!.Status.HitPoints, "the nested run began (its released goal) and its Damage component hit the target");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady));
    }

    [TestMethod]
    public void The_nested_run_has_the_parent_but_no_source_the_target_and_the_parents_location_as_its_start()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        // The long spell keeps its run on the table after its BEGIN (a duration), so the run the host started can be read.
        var outer = Start(owner, caster, OuterLongNumber);
        caster.Apply(outer, 0, OnHero(caster));
        caster.Act(new(SessionSpellRunActionKind.Free, outer.Id));
        var slot = Nested(caster).Single().Detail;
        var run = owner.QuerySpellRuns()[slot] ?? throw new AssertFailedException("the nested run is on the table");
        Assert.AreEqual(LongNumber, run.Spell);
        Assert.IsNull(run.Source, "the invocation has no source (0x455A20 with a null source handle)");
        Assert.AreEqual(Hero, run.Parent?.Id);
        Assert.AreEqual(Hero, run.Target?.Id);
        Assert.AreEqual(caster.Facts(Hero)!.Location, run.StartLocation, "the run's start location (+0x50) is the invocation's +0x38, the parent's location");
        Assert.AreNotEqual(SpellRunAction.Begin, run.Action, "released at once, its BEGIN has run");
    }

    [TestMethod]
    public void A_target_with_neither_object_nor_tile_starts_nothing_and_a_tile_target_casts_at_the_tile()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var outer = Start(owner, caster, OuterNumber);
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(outer, 0, new(null, SpellLocation.None)));
        caster.Act(new(SessionSpellRunActionKind.Free, outer.Id));
        Assert.AreEqual(0, Nested(caster).Length, "the component returns at once");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady));
        var second = Start(owner, caster, OuterNumber);
        caster.Apply(second, 0, new(null, HeroTile));
        caster.Act(new(SessionSpellRunActionKind.Free, second.Id));
        var tile = Nested(caster).Single();
        Assert.IsNull(tile.Object, "a tile target has no object");
        Assert.IsTrue(tile.Detail >= 0);
    }

    [TestMethod]
    public void A_spell_without_a_record_is_logged_refused_and_starts_nothing()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var runs = owner.QuerySpellRuns().Count;
        var outer = Start(owner, caster, MissingNumber);
        caster.Apply(outer, 0, OnHero(caster));
        caster.Act(new(SessionSpellRunActionKind.Free, outer.Id));
        var refused = Nested(caster).Single();
        Assert.AreEqual((outer.Id, 10000, -1), (refused.Run, refused.Value, refused.Detail), "the spell 10000 (the absent key) has no record and the original's check refuses it");
        Assert.AreEqual(runs, owner.QuerySpellRuns().Count, "no run is left behind");
    }

    [TestMethod]
    public void A_spell_that_casts_itself_stops_at_the_bound()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var outer = Start(owner, caster, ChainNumber);
        caster.Apply(outer, 0, OnHero(caster));
        caster.Act(new(SessionSpellRunActionKind.Free, outer.Id));
        Assert.AreEqual(SessionSpellCaster.MaxNestedCastsPerStart, Nested(caster).Length, "one run a start, up to the bound");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.NestedCastsDropped), "the rest is dropped and logged");
        Assert.IsTrue(owner.QuerySpellRuns().Count < SpellRunTable.Slots);
    }
}
