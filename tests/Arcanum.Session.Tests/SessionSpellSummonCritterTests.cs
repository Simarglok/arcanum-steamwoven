using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-053 (E1-e; ARC-RES-101, ledger SUM5-SUM8): the Summon component for critters, on invented records shaped like Summon Undead (spell 57: a list pick by the caster's aptitude, the mind control flag on the
// summoned, the callback that clears OF_DONTDRAW, the END callback that destroys it). The summoned creature is a created critter (oid:critter:n); the map here lists it by the session's own record.
[TestClass]
public sealed class SessionSpellSummonCritterTests
{
    private const int Spell = 57, ListSpell = 53, Proto = 28468, File = Proto + 20;
    private const ulong Tile = 0x8000UL << 32, Empty = 0x40000UL << 32, Summoned = 0x4000UL << 32;
    private const int MindControlled = 0x8000000, DontDraw = 0x100;
    private static readonly SpellLocation HeroTile = new(5, 5), Strike = new(8, 5);

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        private SpellTargetObject? Made(string id) => owner.QueryCreatedObject(id) is { Kind: SessionCreatedKind.Critter } made
            ? new(id, SpellTargetObject.Npc, new((uint)made.Cell.X, (uint)made.Cell.Y), HitPoints: 5, PcLeader: owner.QueryCompanion(id).LedByPc ? SessionSpellCaster.Hero : null) : null;
        public SpellTargetObject? Object(string id) => id == SessionSpellCaster.Hero
            ? new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints) : Made(id);
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 7;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => Made(entity) is null ? null
            : new(SessionCritterCombat.From(entity, AttackStateTests.Npc(stats: AttackStateTests.Stats(willpower: 9), critterFlags: 0)).Combat!, 0, 0, 0, 0, 0);
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class Social : ISpellSocialHost
    {
        public bool ChangeReaction(string npc, int delta) => true;
        public bool Peace(string npc, string with) => true;
        public bool Flee(string npc, string from) => true;
        public bool StopFleeing(string npc) => true;
    }

    private static SpellComponent Line(SpellAction action, SpellComponentType type, ulong area = 0, ulong apply = 0, SpellComponentData? data = null) => new(action, type, new(area, 0, 0, 0, -1), apply, 0, data);

    // Summon Undead's shape (SUM15): the Summon of a list, the mind control flag on the summoned, the callback that shows it (OF_DONTDRAW off), the END callback that destroys it; maintained.
    private static SessionSpellRecord Shaped(int number, SummonData summon, bool eyeCandy = false) => SessionSpellRecords.Translate(
        Record(number: number, info: SpellInfo.Aggressive, components:
            [Line(SpellAction.Begin, SpellComponentType.Summon, Tile | Empty, data: summon),
             Line(SpellAction.Begin, SpellComponentType.ObjFlag, apply: Summoned, data: new ObjectFlagData(SessionSpellCaster.SpellFlagsField, MindControlled, true)),
             .. eyeCandy ? [Line(SpellAction.Begin, SpellComponentType.EyeCandy, apply: Summoned, data: new EyeCandyData(2, true, SessionSpellCaster.PlayCallback))] : Array.Empty<SpellComponent>(),
             Line(SpellAction.Callback, SpellComponentType.ObjFlag, data: new ObjectFlagData(SessionSpellCaster.ObjectFlagsField, DontDraw, false)),
             Line(SpellAction.End, SpellComponentType.EyeCandy, data: new EyeCandyData(2, true, SessionSpellCaster.PlayEndCallback)),
             Line(SpellAction.EndCallback, SpellComponentType.Destroy)])
        with { Cost = 0, MaintainCost = 0, MaintainPeriod = 10, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoResist = true,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, LinesPresent = [true, true, true, true, true],
            Areas = [new(Tile, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(Summoned, 0, 0, 0, -1), new(Summoned, 0, 0, 0, -1), new(Summoned, 0, 0, 0, -1)] });

    private static SessionSummonPrototype? Prototypes(int file) => file >= 20_000 ? new(SpellTargetObject.Npc, 0) : null;

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, List<string> Told, SessionSpellRecord Record) Setup(SessionSpellRecord record, bool plays = false)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var told = new List<string>();
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events())
        {
            SummonPrototypes = Prototypes, SummonMap = SessionFixtures.Exterior, EyeCandyPlays = _ => plays, Social = new Social(), CritterSummoned = told.Add,
        };
        return (owner, caster, told, record);
    }

    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record)
    {
        var hero = caster.Facts(SessionSpellCaster.Hero)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, hero, hero, null, Strike), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Act(new(SessionSpellRunActionKind.Begin, run)).Disposition);
        return run;
    }

    private static IReadOnlyList<SessionCreatedObject> Made(GameSessionOwner owner) => owner.QueryCreatedObjects(SessionFixtures.Exterior);

    private static string Trail(SessionSpellCaster caster) => string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Value:X}:{e.Object}:{e.Detail}"));

    [TestMethod]
    public void A_critter_summon_makes_a_summoned_hidden_creature_at_the_tile_that_follows_the_hero_and_is_told_to_the_host()
    {
        var (owner, caster, told, record) = Setup(Shaped(Spell, new SummonData(Proto, 0, 0, -1)));
        using var _ = owner;
        var run = Cast(owner, caster, record);
        var critter = Made(owner).Single();
        Assert.AreEqual((SessionCreatedKind.Critter, File, 8, 5), (critter.Kind, critter.Prototype, critter.Cell.X, critter.Cell.Y), "the NPC prototype + 20 at the entry's tile, no offset");
        Assert.IsTrue(critter.Entity.StartsWith(SessionCreatedObject.CritterPrefix, StringComparison.Ordinal));
        Assert.AreEqual((SessionCreatedObject.SummonedFlag, SessionSpellCaster.Hero, (int?)run), (critter.SpellFlags, critter.Summoner, critter.Run), "summoned (0x4000) by the run's parent, on the run's list");
        CollectionAssert.AreEqual(new[] { critter.Entity }, told, "the host is told right after the creation (the record's next lines act on it)");
        var flags = owner.QueryObjectFlags(critter.Entity);
        Assert.AreEqual(unchecked((int)SessionCreatedObject.SummonedFlag) | MindControlled, flags.SpellSet, "summoned, and mind controlled by the record's ObjFlag line");
        Assert.AreEqual(GameSessionOwner.SummonedDontDrawFlag, flags.ObjectSet, "OF_DONTDRAW is set by the effect (SUM5) until the callback");
        Assert.AreEqual(GameSessionOwner.SummonedNoPickpocketFlag2, flags.Critter2Set, "OCF2_NO_PICKPOCKET (SUM5)");
        Assert.AreEqual(0, flags.CritterSet & SessionCreatedObject.EncounterCritterFlag, "no encounter flag: it is the script action's");
        Assert.IsTrue(owner.QueryCompanion(critter.Entity).LedByPc, "the mind control line makes it the hero's follower (0x45DE90)");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), Trail(caster));
        // The CALLBACK line shows it (the summoning animation's callback frame).
        Assert.AreEqual(GameSessionDisposition.Changed, caster.EyeCandyCallback(run, 3)?.Disposition ?? GameSessionDisposition.NoChange, Trail(caster));
        Assert.AreEqual(0, owner.QueryObjectFlags(critter.Entity).ObjectSet & DontDraw, "OF_DONTDRAW is off after the callback: " + Trail(caster));
    }

    [TestMethod]
    public void The_list_pick_takes_the_callers_aptitude_and_the_weights_of_the_table_and_a_list_outside_the_table_is_not_ready()
    {
        var (owner, caster, _, record) = Setup(Shaped(ListSpell, new SummonData(0, 0, 0, 5)));
        using var _ = owner;
        Cast(owner, caster, record);
        var aptitude = caster.Facts(SessionSpellCaster.Hero)!.Aptitude;
        Assert.AreEqual(SummonLists.Pick(5, aptitude, (_, _) => 0)!.Value + 20, Made(owner).Single().Prototype, "list 5 is picked by the hero's aptitude, with no draw");

        var (other, otherCaster, otherTold, otherRecord) = Setup(Shaped(ListSpell, new SummonData(0, 0, 0, 9)));
        using var __ = other;
        Cast(other, otherCaster, otherRecord);
        Assert.AreEqual(0, Made(other).Count);
        Assert.IsTrue(otherCaster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.Summon), "no list 9");
        Assert.AreEqual(0, otherTold.Count);

        // A component with a Clear_Faction: or a Palette: on a critter is not ready (no installed record has either, SC12).
        var (third, thirdCaster, _, thirdRecord) = Setup(Shaped(Spell, new SummonData(Proto, 1, 0, -1)));
        using var ___ = third;
        Cast(third, thirdCaster, thirdRecord);
        Assert.AreEqual(0, Made(third).Count);
    }

    [TestMethod]
    public void The_death_of_the_summoned_creature_ends_the_run_that_made_it_and_the_end_callback_destroys_it_and_it_leaves_the_party()
    {
        var (owner, caster, _, record) = Setup(Shaped(Spell, new SummonData(Proto, 0, 0, -1)));
        using var _ = owner;
        var run = Cast(owner, caster, record);
        var critter = Made(owner).Single();
        Assert.AreEqual(SpellRunAction.Maintain, owner.QuerySpellRuns()[run]!.Action, "the summoning is maintained");
        // The kill (0x45DA20) tells the runs the creature is gone (0x459740); the first loop ends the run that lists it (SUM7), whose END reaches its END_CALLBACK Destroy.
        Assert.IsTrue(((ISpellRunHost)caster).SummonedListHolds(owner.QuerySpellRuns()[run]!, critter.Entity), "the run's summoned list holds the creature");
        var queued = owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.SpellRun);
        caster.ObjectGone(critter.Entity);
        Assert.AreEqual(queued + 1, owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.SpellRun), "the run's interrupt is queued (0x4571E0 only queues it): " + Trail(caster));
        // The queued interrupt fires: the run goes to END, whose callback does not wait here, and its END_CALLBACK Destroy takes the creature.
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Act(new(SessionSpellRunActionKind.Interrupt, run)).Disposition);
        Assert.IsNull(owner.QuerySpellRuns()[run], "the run ended with its summoned creature: " + Trail(caster));
        Assert.AreEqual(0, Made(owner).Count, "the END_CALLBACK Destroy took the creature away");
        Assert.IsFalse(owner.QueryCompanion(critter.Entity).LedByPc, "a destroyed follower is no follower");
        Assert.IsTrue(owner.QueryObjectFlags(critter.Entity).IsEmpty, "its written flags went with it");
    }

    [TestMethod]
    public void A_summoned_creature_waits_for_the_summoning_animation_when_the_eye_candy_plays()
    {
        var (owner, caster, _, record) = Setup(Shaped(Spell, new SummonData(Proto, 0, 0, -1), eyeCandy: true), plays: true);
        using var _ = owner;
        var run = Cast(owner, caster, record);
        var critter = Made(owner).Single();
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.EyeCandyAdded && e.Object == critter.Entity), "the eye candy is re-aimed to the summoned creature");
        Assert.AreNotEqual(0, owner.QueryObjectFlags(critter.Entity).ObjectSet & DontDraw, "hidden while the animation plays");
        Assert.AreEqual(3, caster.AwaitedCallback(run) ?? 3);
        Assert.IsNotNull(caster.EyeCandyCallback(run, 3));
        Assert.AreEqual(0, owner.QueryObjectFlags(critter.Entity).ObjectSet & DontDraw, "shown at the callback: " + Trail(caster));
        Assert.IsNotNull(owner.QuerySpellRuns()[run], "the maintained run goes on");
    }

    [TestMethod]
    public void A_created_summoned_critter_is_refused_for_a_bad_summoner_and_leaves_no_trace()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.CreateSummonedCritter(owner.Version, 0, SessionFixtures.Exterior, new(5, 5), null, null).Disposition, "prototype 0");
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.CreateSummonedCritter(owner.Version, File, SessionFixtures.Exterior, new(5, 5), "", null).Disposition, "an empty summoner");
        Assert.AreEqual(0, owner.QueryCreatedObjects(SessionFixtures.Exterior).Count);
        var stale = owner.Version;
        var made = owner.CreateSummonedCritter(owner.Version, File, SessionFixtures.Exterior, new(5, 5), SessionSpellCaster.Hero, 3);
        Assert.AreEqual(GameSessionDisposition.Changed, made.Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.CreateSummonedCritter(stale, File, SessionFixtures.Exterior, new(6, 5), null, null).Failure);
        Assert.AreEqual(1, owner.QueryCreatedObjects(SessionFixtures.Exterior).Count);
    }
}
