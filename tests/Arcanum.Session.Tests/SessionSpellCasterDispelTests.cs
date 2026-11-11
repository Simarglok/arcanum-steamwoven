using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-034 (MA-3, ledger DP1–DP3; 0x451C40 read for this slice): the Dispel component of Disperse Magick and Dweomer Shield. The runs sourced by, aimed at or listing the object end by event, but the
// processed one and the technological ones; a PC or NPC loses the stoned spell flag by a plain write and a created summoned critter is destroyed. Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterDispelTests
{
    private const string Target = "oid:target";
    private const string Hero = SessionSpellCaster.Hero;
    private const int DispelNumber = 94, PlainNumber = 95, TechnologyNumber = 96;
    private static readonly SpellLocation HeroTile = new(5, 5), TargetTile = new(8, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter critter) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Target => new(id, SpellTargetObject.Npc, TargetTile, HitPoints: 5),
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
        public SpellCastCritter? Critter(string entity) => entity == Target ? critter : null;
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

    private static SpellCastCritter TargetCritter(int critterFlags2 = 0)
    {
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(willpower: 9), critterFlags: 0);
        return new(SessionCritterCombat.From(Target, fields).Combat!, 0, 0, 0, 0, critterFlags2);
    }

    // A maintained spell of one BEGIN component on the object; the maintenance keeps the run alive after its BEGIN, so the guard of the processed run is seen.
    private static SessionSpellRecord Spell(int number, SpellComponentType type, SpellComponentData? data, bool technology = false) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral, technology: technology,
            components: [new(SpellAction.Begin, type, new(0x4, 0, 0, 0, -1), 0, 0, data)])
            with { Cost = 10, MaintainCost = 3, MaintainPeriod = 10, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static readonly Dictionary<int, SessionSpellRecord> Records = new[] { Spell(DispelNumber, SpellComponentType.Dispel, null), Spell(PlainNumber, SpellComponentType.NoOp, null),
        Spell(TechnologyNumber, SpellComponentType.NoOp, null, technology: true) }.ToDictionary(r => r.Data.Number);

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(int critterFlags2 = 0)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        return (owner, new SessionSpellCaster(owner, n => Records.GetValueOrDefault(n), new Map(owner, TargetCritter(critterFlags2)), new Events()));
    }

    // A run of a spell that never began: the ending by event frees it at once (0x4571E0), so it is seen gone.
    private static int Idle(GameSessionOwner owner, SessionSpellCaster caster, int number, string parent, string source, string target)
    {
        var record = Records[number];
        var who = caster.Facts(parent)!;
        var by = caster.Facts(source)!;
        var at = caster.Facts(target)!;
        var started = owner.StartSpellRun(owner.Version, new(new(number, who, by, at, at.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return started.Creation!.Run!.Id;
    }

    private static int CastDispel(GameSessionOwner owner, SessionSpellCaster caster, string to)
    {
        var record = Records[DispelNumber];
        var hero = caster.Facts(Hero)!;
        var at = caster.Facts(to)!;
        var started = owner.StartSpellRun(owner.Version, new(new(DispelNumber, hero, hero, at, at.Location, ParentIsLocalPlayer: true)), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        caster.StartGoal(run, new NoCombat());
        caster.Release(run);
        return run;
    }

    private static string Trail(SessionSpellCaster caster) => string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Value:X}:{e.Object}:{e.Detail}"));

    [TestMethod]
    public void A_dispel_ends_the_runs_sourced_by_or_aimed_at_its_object_but_not_its_own_run_a_technological_one_or_a_bystander()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var sourced = Idle(owner, caster, PlainNumber, Hero, Target, Hero);
        var aimed = Idle(owner, caster, PlainNumber, Hero, Hero, Target);
        var bystander = Idle(owner, caster, PlainNumber, Hero, Hero, Hero);
        var technology = Idle(owner, caster, TechnologyNumber, Hero, Hero, Target);
        var dispel = CastDispel(owner, caster, Target);
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), Trail(caster));
        Assert.IsNull(owner.QuerySpellRuns()[sourced], "a run the object is the source of ends");
        Assert.IsNull(owner.QuerySpellRuns()[aimed], "a run aimed at the object ends");
        Assert.IsNotNull(owner.QuerySpellRuns()[bystander], "a run about another object stays");
        Assert.IsNotNull(owner.QuerySpellRuns()[technology], "the runs of a technological record are never touched (record flag 0x4)");
        Assert.IsNotNull(owner.QuerySpellRuns()[dispel]);
    }

    // The guard seen where an unguarded ending would show at once: a dispelling run that never began (an ending frees it immediately), sourced by the object it dispels.
    [TestMethod]
    public void The_dispelling_run_is_left_alone_even_when_the_object_it_dispels_is_its_source()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var dispeller = Idle(owner, caster, DispelNumber, Hero, Target, Target);
        var sourced = Idle(owner, caster, PlainNumber, Hero, Target, Hero);
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(owner.QuerySpellRuns()[dispeller]!, 0, new(caster.RunObject(Target), TargetTile)));
        caster.EndMaintainedSpells("oid:nobody");
        Assert.IsNull(owner.QuerySpellRuns()[sourced], "another run the object is the source of is freed");
        Assert.IsNotNull(owner.QuerySpellRuns()[dispeller], "the run being processed is not ended by its own Dispel (Dweomer Shield)");
    }

    [TestMethod]
    public void A_dispel_on_a_tile_or_a_missing_object_ends_nothing()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var aimed = Idle(owner, caster, PlainNumber, Hero, Hero, Target);
        var dispellerId = Idle(owner, caster, DispelNumber, Hero, Hero, Hero);
        var dispeller = owner.QuerySpellRuns()[dispellerId]!;
        // The component takes no target object (a tile entry): nothing is queued and nothing is not ready.
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(dispeller, 0, new(null, TargetTile)));
        caster.EndMaintainedSpells("oid:nobody");
        Assert.IsNotNull(owner.QuerySpellRuns()[aimed]);
        Assert.IsFalse(caster.Log.Any(e => e.Kind is SpellCastEventKind.ComponentNotReady or SpellCastEventKind.Dispelled), Trail(caster));
    }

    [TestMethod]
    public void A_dispel_clears_the_stoned_spell_flag_and_the_stoned_object_flag_of_a_critter_and_leaves_other_flags()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Target, SessionObjectFlagField.Spell, SessionSpellCaster.StonedSpellFlag, true, false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Target, SessionObjectFlagField.Object, SessionSpellCaster.ObjectStoned, true, false).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Target, SessionObjectFlagField.Spell, 0x400, true, false).Disposition);
        Assert.IsTrue(caster.Holds(Target).Stoned);
        CastDispel(owner, caster, Target);
        Assert.IsFalse(caster.Holds(Target).Stoned, Trail(caster));
        Assert.AreEqual(new SessionObjectFlags(0x400, 0, 0, 0, 0, 0), owner.QueryObjectFlags(Target), "the stoned spell flag and the stoned object flag (0x43D280, 0x80000) go, the shielded bit stays");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.Dispelled, 0, 0, Target, SessionSpellCaster.DispelStonedCleared | SessionSpellCaster.DispelObjectStonedCleared), caster.Log.Single(e => e.Kind == SpellCastEventKind.Dispelled) with { Run = 0 });
        // A second dispel finds nothing to clear and says nothing.
        var before = caster.Log.Count(e => e.Kind == SpellCastEventKind.Dispelled);
        CastDispel(owner, caster, Target);
        Assert.AreEqual(before, caster.Log.Count(e => e.Kind == SpellCastEventKind.Dispelled));
    }

    // ARC-MAGIC-047 (DP2, the last step of the object half): a set perma-polymorph bit (critter flags 2, 0x400000) calls the Trait restore and is then cleared by a plain write.
    [TestMethod]
    public void A_dispel_undoes_a_perma_polymorph_the_session_wrote_with_the_trait_restore_and_clears_the_bit()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        owner.WriteObjectFlag(owner.Version, Target, SessionObjectFlagField.Critter2, SessionSpellCaster.PermaPolymorphCritterFlag2, true, false);
        owner.WriteTraitArt(owner.Version, Target, new SessionTraitArt(20, 1));
        CastDispel(owner, caster, Target);
        Assert.IsTrue(owner.QueryObjectFlags(Target).IsEmpty, "the bit is gone: " + Trail(caster));
        Assert.IsNull(owner.QueryTraitArt(Target), "the Trait restore took the monster body off");
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitRestored, 0, 20, Target, SessionSpellCaster.TraitStateChanged), caster.Log.Single(e => e.Kind == SpellCastEventKind.TraitRestored), Trail(caster));
        Assert.AreEqual(SessionSpellCaster.DispelPermaPolymorphUndone, caster.Log.Single(e => e.Kind == SpellCastEventKind.Dispelled).Detail, Trail(caster));
        // A second dispel finds no bit: no restore, nothing told.
        var restored = caster.Log.Count(e => e.Kind == SpellCastEventKind.TraitRestored);
        CastDispel(owner, caster, Target);
        Assert.AreEqual(restored, caster.Log.Count(e => e.Kind == SpellCastEventKind.TraitRestored));
    }

    [TestMethod]
    public void A_dispel_undoes_a_perma_polymorph_the_critters_data_holds_and_asks_the_host_to_restore_a_body_the_session_never_kept()
    {
        // The map data of the critter has the bit (a script's permanent polymorph): the restore line still comes, as the original rebuilds the art for any PC or NPC.
        var (owner, caster) = Session(SessionSpellCaster.PermaPolymorphCritterFlag2 | 0x80);
        using var _ = owner;
        CastDispel(owner, caster, Target);
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.TraitRestored, 0, SessionSpellCaster.TraitRestoreValue, Target, 0), caster.Log.Single(e => e.Kind == SpellCastEventKind.TraitRestored), Trail(caster));
        Assert.AreEqual(0x80, owner.QueryObjectFlags(Target).Critter2(SessionSpellCaster.PermaPolymorphCritterFlag2 | 0x80), "only the perma-polymorph bit goes");
    }

    [TestMethod]
    public void A_dispel_of_the_hero_undoes_a_perma_polymorph_the_session_wrote()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        owner.WriteObjectFlag(owner.Version, Hero, SessionObjectFlagField.Critter2, SessionSpellCaster.PermaPolymorphCritterFlag2, true, false);
        CastDispel(owner, caster, Hero);
        Assert.IsTrue(owner.QueryObjectFlags(Hero).IsEmpty, Trail(caster));
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.TraitRestored && e.Object == Hero), "the hero's best items are chosen again by the host");
    }

    [TestMethod]
    public void A_dispel_leaves_the_stoned_object_flag_of_a_critter_whose_spell_flag_is_not_stoned()
    {
        // The object flag is cleared inside the stoned spell flag's branch of 0x451C40 only (ARC-MAGIC-039): without the spell flag the object flag stays and nothing is told.
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Target, SessionObjectFlagField.Object, SessionSpellCaster.ObjectStoned, true, false).Disposition);
        CastDispel(owner, caster, Target);
        Assert.AreEqual(new SessionObjectFlags(0, 0, 0, 0, SessionSpellCaster.ObjectStoned, 0), owner.QueryObjectFlags(Target), Trail(caster));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.Dispelled), Trail(caster));
    }

    [TestMethod]
    public void A_dispel_of_a_stoned_spell_flag_without_the_object_flag_clears_the_spell_flag_alone()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteObjectFlag(owner.Version, Target, SessionObjectFlagField.Spell, SessionSpellCaster.StonedSpellFlag, true, false).Disposition);
        CastDispel(owner, caster, Target);
        Assert.IsFalse(caster.Holds(Target).Stoned, Trail(caster));
        Assert.AreEqual(SessionSpellCaster.DispelStonedCleared, caster.Log.Single(e => e.Kind == SpellCastEventKind.Dispelled).Detail, "0x43D280 with no 0x80000 set changes nothing");
    }

    [TestMethod]
    public void A_dispel_destroys_a_summoned_critter_and_leaves_one_that_is_not_summoned()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var summoned = owner.CreateObject(owner.Version, SessionCreatedKind.Critter, 28310, SessionFixtures.Exterior, new CellCoord(100, 201), 0, 0, SessionCreatedObject.SummonedFlag).Object!;
        var plain = owner.CreateObject(owner.Version, SessionCreatedKind.Critter, 28310, SessionFixtures.Exterior, new CellCoord(101, 201)).Object!;
        var run = new SpellRun(0, DispelNumber, SpellRunAction.Begin, null, null, SpellLocation.None, null, SpellLocation.None, null, SpellRunFlags.Active);
        caster.DispelObject(run, plain.Entity);
        Assert.IsNotNull(owner.QueryCreatedObject(plain.Entity), "an object whose spell flags lack 0x4000 is left");
        caster.DispelObject(run, summoned.Entity);
        Assert.IsNotNull(owner.QueryCreatedObject(summoned.Entity), "the destroy waits for the runs' endings (their summoned lists are read first)");
        caster.EndMaintainedSpells("oid:nobody");
        Assert.IsNull(owner.QueryCreatedObject(summoned.Entity), "the summoned critter is destroyed (0x43CCA0)");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.SummonDestroyed && e.Object == summoned.Entity), Trail(caster));
        Assert.AreEqual(new SpellCastEvent(SpellCastEventKind.Dispelled, 0, 0, summoned.Entity, SessionSpellCaster.DispelDestroyed), caster.Log.Single(e => e.Kind == SpellCastEventKind.Dispelled));
    }
}
