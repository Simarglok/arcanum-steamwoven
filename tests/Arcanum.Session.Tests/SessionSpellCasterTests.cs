using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-014: the cast host over a synthetic map (the hero and a box): the casting goal, the release frame's BEGIN with its payment
// and Damage component, and MG39–MG41 in both modes. Records and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterTests
{
    private static readonly SpellLocation HeroTile = new(5, 5), BoxTile = new(6, 5);

    private sealed class Map(GameSessionOwner owner, bool boxDestroyed = true) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            "box" => new(id, SpellTargetObject.Scenery, BoxTile, boxDestroyed ? SpellTargetObject.Destroyed : 0),
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

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    // A self spell (BEGIN area self, cost 7, instant) whose BEGIN deals 3 normal damage to its target.
    private static SessionSpellRecord SelfBolt(ulong beginTargets = 0x1) => SessionSpellRecords.Translate(Record(number: 90, beginTargets: beginTargets,
        info: SpellInfo.Friendly, components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x1, 0, 0, 0, -1), 0, 0, new DamageData(0, 3, 3, 0))])
        with { DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0,
            DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(RuleMode mode, SessionSpellRecord record, bool boxDestroyed = true)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        owner.SelectRules(mode);
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, boxDestroyed), new Events()));
    }

    private static int Start(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string target = SessionSpellCaster.Hero, bool free = false)
    {
        var hero = caster.Facts(SessionSpellCaster.Hero)!;
        var to = caster.Facts(target)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, hero, hero, to, to.Location, ParentIsLocalPlayer: true), Free: free),
            record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return started.Creation!.Run!.Id;
    }

    private static (int Fatigue, int HitPoints) Hero(GameSessionOwner owner) => owner.QueryHeroLife()!.Status is var s ? (s.Fatigue, s.HitPoints) : default;

    [TestMethod]
    public void A_cast_pays_at_its_release_frame_and_its_damage_component_hurts_the_target()
    {
        var record = SelfBolt();
        var (owner, caster) = Session(RuleMode.Steamwoven, record);
        using var _ = owner;
        var before = Hero(owner);
        var run = Start(owner, caster, record);
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
        Assert.AreEqual(before, Hero(owner), "nothing paid before the release frame");
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Release(run)!.Disposition);
        Assert.AreEqual((before.Fatigue - 7, before.HitPoints - 3), Hero(owner), "the cost and the damage");
        Assert.IsNull(owner.QuerySpellRuns()[run], "an instant spell ends and is freed at once");
        // The component applies during the pass; the host pays the stage's payment from the call's notes after it.
        CollectionAssert.AreEquivalent(new[] { SpellCastEventKind.FatiguePaid, SpellCastEventKind.Damaged, SpellCastEventKind.Freed },
            caster.Log.Select(e => e.Kind).ToArray());
    }

    [TestMethod]
    public void Mg39_a_goal_refused_at_its_start_frees_the_run_charged_in_the_original_and_unpaid_in_Steamwoven()
    {
        foreach (var (mode, paid) in new[] { (RuleMode.Original, 7), (RuleMode.Steamwoven, 0) })
        {
            var record = SelfBolt();
            var (owner, caster) = Session(mode, record);
            using var _ = owner;
            var before = Hero(owner).Fatigue;
            var run = Start(owner, caster, record);
            caster.EndGoal(run, refused: true);
            Assert.AreEqual((before - paid, (SpellRun?)null), (Hero(owner).Fatigue, owner.QuerySpellRuns()[run]), $"{mode}");
        }
    }

    [TestMethod]
    public void Mg40_a_free_run_ended_before_its_begin_is_charged_in_the_original_only()
    {
        foreach (var (mode, paid) in new[] { (RuleMode.Original, 7), (RuleMode.Steamwoven, 0) })
        {
            var record = SelfBolt();
            var (owner, caster) = Session(mode, record);
            using var _ = owner;
            var before = Hero(owner).Fatigue;
            var run = Start(owner, caster, record, free: true);
            caster.EndGoal(run);
            Assert.AreEqual(before - paid, Hero(owner).Fatigue, $"{mode}");
        }
    }

    [TestMethod]
    public void Mg41_a_target_gone_at_the_release_frame_keeps_the_run_in_the_original_and_frees_it_in_Steamwoven()
    {
        foreach (var (mode, kept) in new[] { (RuleMode.Original, true), (RuleMode.Steamwoven, false) })
        {
            // A BEGIN area that takes objects (0x4): the box, destroyed by the release frame, no longer passes.
            var record = SelfBolt(beginTargets: 0x4);
            var (owner, caster) = Session(mode, record, boxDestroyed: false);
            using var _ = owner;
            var run = Start(owner, caster, record, "box");
            var gone = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events());
            Assert.IsFalse(gone.TargetStillValid(owner.QuerySpellRuns()[run]!));
            Assert.IsNull(gone.Release(run), "nothing begins");
            Assert.AreEqual(kept, owner.QuerySpellRuns()[run] is not null, $"{mode}");
        }
    }
}
