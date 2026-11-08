using Arcanum.Data.Magic;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-DEV-295 (B39, ledger XP9/XP10): the experience steps live inside the original's damage function and death notification, so a
// spell's damage pays them as a weapon's does: the pool share of a hit goes to the hero when the damage source is the hero, the kill
// award to a source the hero leads, and a step with no source (a script's damage, a burn) pays nothing. Records and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterExperienceTests
{
    private const string Rat = "oid:rat", Thug = "oid:thug", Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), RatTile = new(6, 5), ThugTile = new(7, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter rat, SpellCastCritter thug) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Rat => new(id, SpellTargetObject.Npc, RatTile, HitPoints: 20),
            Thug => new(id, SpellTargetObject.Npc, ThugTile, HitPoints: 20),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) =>
            new[] { Hero, Rat, Thug }.Select(Object).Where(o => o is not null && o.Location == tile).Select(o => o!).ToArray();
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => entity == Rat ? rat : entity == Thug ? thug : null;
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

    // The thug can cast: intelligence 12 and willpower 9, as the AI tests' mage.
    private static SpellCastCritter Npc(string entity, int worth = 160, int pool = 160, uint objectFlags = 0, uint spellFlags = 0)
    {
        var stats = AttackStateTests.Stats(willpower: 9);
        stats[4] = 12;
        return new(SessionCritterCombat.From(entity, AttackStateTests.Npc(stats: stats, critterFlags: 0, worth: worth, pool: pool, objectFlags: objectFlags, spellFlags: spellFlags)).Combat!,
            0, 0, 0, 0, 0);
    }

    // An aggressive spell at a critter object (BEGIN targets 0x44: the object bit and the critter bit) whose BEGIN deals `damage` normal damage to its target.
    private static SessionSpellRecord Bolt(int damage) =>
        SessionSpellRecords.Translate(Record(number: 90, beginTargets: 0x44, components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x44, 0, 0, 0, -1), 0, 0, new DamageData(0, damage, damage, 0))])
        with { Cost = 0, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0,
            DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0x4, MinimumWillpower = 0 });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(SessionSpellRecord record, SpellCastCritter? rat = null)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, rat ?? Npc(Rat), Npc(Thug)), new Events()));
    }

    // Casts `record` from `parent` at the rat and releases it.
    private static void CastAtRat(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string parent = Hero)
    {
        var from = caster.Facts(parent)!;
        var to = caster.Facts(Rat)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, from, from, to, to.Location, ParentIsLocalPlayer: parent == Hero), Free: true),
            record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Release(run)!.Disposition);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged), string.Join(" | ", caster.Log));
    }

    private static int MaxHitPoints(GameSessionOwner owner, SessionSpellCaster caster, string entity) =>
        owner.QueryLife(caster.LifeTarget(entity)!).View!.Status.MaxHitPoints;

    private static int HeroExperience(GameSessionOwner owner) => owner.QueryHeroExperience()!.Experience;

    [TestMethod]
    public void A_hero_spell_that_hurts_an_npc_pays_the_pool_share_to_the_hero()
    {
        var record = Bolt(3);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var maxHitPoints = MaxHitPoints(owner, caster, Rat);
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record);
        var damage = caster.Log.Single(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat).Value;
        Assert.IsTrue(damage > 0);
        var (award, pool) = ExperienceRules.DamageShare(160, 160, damage, maxHitPoints);
        Assert.IsTrue(award > 0, "the fixture's numbers must pay something");
        Assert.AreEqual(before + award, HeroExperience(owner));
        Assert.AreEqual(new SessionNpcExperience(Rat, 160, pool), owner.QueryNpcExperience(Rat));
        var given = caster.Log.Single(e => e.Kind == SpellCastEventKind.ExperienceGiven);
        Assert.AreEqual((award, 0, Rat), (given.Value, given.Detail, given.Object));
        Assert.AreEqual(award, caster.TakeExperienceAwards().Single().Award);
        Assert.AreEqual(0, caster.TakeExperienceAwards().Count, "the awards are taken once");
    }

    [TestMethod]
    public void A_hero_spell_that_kills_an_npc_pays_the_pool_share_and_a_fifth_of_the_worth_once()
    {
        var record = Bolt(1000);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var maxHitPoints = MaxHitPoints(owner, caster, Rat);
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record);
        var damage = caster.Log.Single(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat).Value;
        var (share, _) = ExperienceRules.DamageShare(160, 160, damage, maxHitPoints);
        var kill = ExperienceRules.KillAward(160);
        Assert.AreEqual(32, kill);
        Assert.AreEqual(before + share + kill, HeroExperience(owner));
        Assert.AreEqual(0, owner.QueryNpcExperience(Rat)!.Value.Worth, "the same NPC never pays twice");
        var given = caster.Log.Single(e => e.Kind == SpellCastEventKind.ExperienceGiven);
        Assert.AreEqual((share, kill), (given.Value, given.Detail));
        Assert.AreEqual(2, caster.TakeExperienceAwards().Count);
    }

    [TestMethod]
    public void A_stoned_npc_loses_no_pool_to_a_spell()
    {
        var record = Bolt(3);
        var (owner, caster) = Session(record, Npc(Rat, spellFlags: 0x10000));
        using var _ = owner;
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat));
        Assert.AreEqual(before, HeroExperience(owner));
        Assert.IsNull(owner.QueryNpcExperience(Rat));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ExperienceGiven));
    }

    [TestMethod]
    public void Another_npcs_spell_empties_the_pool_for_nothing_and_its_kill_pays_nothing()
    {
        // XP10: the pool loses the share whoever attacked and only a PC source gets it; XP9: a kill by a critter the hero does not lead pays nobody and keeps the worth.
        var record = Bolt(1000);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record, Thug);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat));
        Assert.AreEqual(before, HeroExperience(owner));
        var saved = owner.QueryNpcExperience(Rat)!.Value;
        Assert.AreEqual(160, saved.Worth, "the worth stays");
        Assert.IsTrue(saved.Pool < 160, "the pool lost its share");
        Assert.AreEqual(0, caster.TakeExperienceAwards().Count);
    }

    [TestMethod]
    public void A_script_damage_without_a_source_pays_nothing_and_leaves_the_pool()
    {
        var record = Bolt(3);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var before = HeroExperience(owner);
        var hit = caster.ApplyScriptDamage(Rat, 1000, 0, unresistable: true);
        Assert.IsTrue(hit.Applied);
        Assert.AreEqual(before, HeroExperience(owner));
        Assert.IsNull(owner.QueryNpcExperience(Rat), "no source: neither the pool nor the worth changes");
        Assert.AreEqual(0, caster.TakeExperienceAwards().Count);
    }
}
