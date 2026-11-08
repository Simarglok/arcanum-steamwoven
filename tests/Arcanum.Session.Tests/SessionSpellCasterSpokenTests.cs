using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-DEV-304 (B24, GT216, 0x454A10 and 0x455100): the ObjFlag component takes the spoken-with-dead spell flag (spell 56's, 0x2000000) on a dead critter. Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterSpokenTests
{
    private const string Corpse = "oid:corpse";
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), CorpseTile = new(8, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter corpse, bool onMap = true, int dataSpellFlags = 0) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Corpse when onMap => new(id, SpellTargetObject.Npc, CorpseTile, SpellFlags: dataSpellFlags, HitPoints: 0),
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
        public SpellCastCritter? Critter(string entity) => entity == Corpse && onMap ? corpse with { SpellFlags = dataSpellFlags } : null;
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

    private static SpellCastCritter CorpseCritter()
    {
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(willpower: 9), critterFlags: 0);
        return new(SessionCritterCombat.From(Corpse, fields).Combat!, 0, 0, 0, 0, 0);
    }

    // A maintained spell like 56: its BEGIN turns the spoken-with-dead bit on the target object and its END turns it off (areas: the target object).
    private static SessionSpellRecord Spirit(int number = 56, uint forbidden = 0x2000000) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral,
            components: [new(SpellAction.Begin, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(20, 0x2000000, true)),
                new(SpellAction.End, SpellComponentType.ObjFlag, new(0x4, 0, 0, 0, -1), 0, 0, new ObjectFlagData(20, 0x2000000, false))])
            with { Cost = 10, MaintainCost = 3, MaintainPeriod = 10, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 1, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = forbidden, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0x2000000,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(SessionSpellRecord record, bool onMap = true, int dataSpellFlags = 0)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, CorpseCritter(), onMap, dataSpellFlags), new Events());
        return (owner, caster);
    }

    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record)
    {
        var hero = caster.Facts(Hero)!;
        var to = caster.Facts(Corpse)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, hero, hero, to, to.Location, ParentIsLocalPlayer: true)), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        caster.StartGoal(run, new NoCombat());
        caster.Release(run);
        return run;
    }

    // The END component of a run, applied as the run's END action does.
    private static void EndComponent(GameSessionOwner owner, SessionSpellCaster caster, int run) =>
        caster.Apply(owner.QuerySpellRuns()[run]! with { Action = SpellRunAction.End }, 0, new(caster.RunObject(Corpse), CorpseTile));

    [TestMethod]
    public void The_component_translates_the_spoken_with_dead_bit_for_the_session()
    {
        Assert.AreEqual(new SpellObjectFlagComponent(20, 0x2000000, true), Spirit().Components[0].ObjectFlag);
    }

    [TestMethod]
    public void A_spirit_cast_writes_the_flag_on_the_dead_critter_with_no_hold_and_its_end_clears_it()
    {
        var record = Spirit();
        var (owner, caster) = Session(record);
        using var _ = owner;
        Assert.IsFalse(caster.SpokenWithDead(Corpse));
        var run = Cast(owner, caster, record);
        Assert.AreEqual(new SessionObjectFlags(0x2000000, 0, 0, 0), owner.QueryObjectFlags(Corpse), string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Run}:{e.Value}:{e.Object}:{e.Detail}")));
        Assert.IsTrue(caster.SpokenWithDead(Corpse));
        Assert.IsFalse(caster.SpokenWithDead(Hero), "only the target");
        var written = caster.Log.Where(e => e.Kind is SpellCastEventKind.ObjectFlagWritten or SpellCastEventKind.HoldStarted or SpellCastEventKind.ComponentNotReady).ToArray();
        CollectionAssert.AreEqual(new[] { new SpellCastEvent(SpellCastEventKind.ObjectFlagWritten, run, 0x2000000, Corpse, 1) }, written, "no hold, no 'not ready'");
        Assert.AreEqual((false, false, false), caster.Holds(Corpse), "the bit holds nothing");
        Assert.AreEqual(0x2000000, caster.SpellFlags(caster.RunObject(Corpse)!), "the run rules read the written flag (a second cast is forbidden by the record's disallowed target flags)");
        // The END clears it; the corpse's data has no such bit, so nothing is left behind.
        EndComponent(owner, caster, run);
        Assert.IsFalse(caster.SpokenWithDead(Corpse));
        Assert.IsTrue(owner.QueryObjectFlags(Corpse).IsEmpty);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ObjectFlagWritten, run, 0x2000000, Corpse, 0), caster.Log[^1]);
    }

    [TestMethod]
    public void Two_runs_that_provide_the_bit_keep_it_until_the_last_ends()
    {
        var record = Spirit();
        var (owner, caster) = Session(record);
        using var _ = owner;
        var first = Cast(owner, caster, record);
        // The run rules forbid a second spirit (spell flag 0x2000000 among spell 56's disallowed target flags), so the second provider is a run of another spell that sets the same bit.
        var other = Spirit(57, forbidden: 0);
        var caster2 = new SessionSpellCaster(owner, n => n == 56 ? record : n == 57 ? other : null, new Map(owner, CorpseCritter()), new Events());
        Assert.IsTrue(caster2.SpokenWithDead(Corpse));
        var second = Cast(owner, caster2, other);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.ObjectFlagKept, second, 0x2000000, Corpse, 1), caster2.Log.Last(e => e.Kind is SpellCastEventKind.ObjectFlagKept or SpellCastEventKind.ObjectFlagWritten),
            "a set while another run provides the bit does nothing (0x455100)");
        EndComponent(owner, caster2, first);
        Assert.IsTrue(caster2.SpokenWithDead(Corpse), "the second still provides the bit");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ActOnSpellRun(owner.Version, caster2, new(SessionSpellRunActionKind.Free, first)).Disposition);
        EndComponent(owner, caster2, second);
        Assert.IsFalse(caster2.SpokenWithDead(Corpse));
        Assert.IsTrue(owner.QueryObjectFlags(Corpse).IsEmpty);
    }

    [TestMethod]
    public void A_corpse_off_the_map_takes_no_set_but_its_clear_still_comes()
    {
        var record = Spirit();
        var (owner, caster) = Session(record);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        Assert.IsTrue(caster.SpokenWithDead(Corpse));
        // The corpse left this map (a run's END may come after its critter left): the clear needs no critter here.
        var away = new SessionSpellCaster(owner, n => n == 56 ? record : null, new Map(owner, CorpseCritter(), onMap: false), new Events());
        away.Apply(owner.QuerySpellRuns()[run]! with { Action = SpellRunAction.End }, 0, new(new SpellRunObject(Corpse, SpellTargetObject.Npc), CorpseTile));
        Assert.IsTrue(owner.QueryObjectFlags(Corpse).IsEmpty);
    }

    [TestMethod]
    public void A_set_on_a_critter_that_is_not_on_the_map_is_not_taken()
    {
        var record = Spirit();
        var (owner, caster) = Session(record);
        using var _ = owner;
        var hero = caster.Facts(Hero)!;
        var to = caster.Facts(Corpse)!;
        var started = owner.StartSpellRun(owner.Version, new(new(56, hero, hero, to, to.Location, ParentIsLocalPlayer: true)), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        var run = started.Creation!.Run!;
        // The same run met on a map that does not hold the corpse: the component is not taken (logged as not ready), and nothing is written.
        var away = new SessionSpellCaster(owner, n => n == 56 ? record : null, new Map(owner, CorpseCritter(), onMap: false), new Events());
        away.Apply(run with { Action = SpellRunAction.Begin }, 0, new(new SpellRunObject(Corpse, SpellTargetObject.Npc), CorpseTile));
        Assert.IsTrue(away.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), "no critter on this map");
        Assert.IsTrue(owner.QueryObjectFlags(Corpse).IsEmpty);
    }

    [TestMethod]
    public void The_data_s_own_bit_is_read_and_a_clear_over_it_is_a_change()
    {
        var record = Spirit();
        var (owner, caster) = Session(record, dataSpellFlags: 0x2000000);
        using var _ = owner;
        Assert.IsTrue(caster.SpokenWithDead(Corpse), "a corpse whose data stores the flag is spoken with with no cast");
        Assert.IsTrue(owner.QueryObjectFlags(Corpse).IsEmpty);
    }
}
