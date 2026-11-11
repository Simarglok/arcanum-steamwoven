using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-044 (E4-c; exe read of 0x452F00, 0x4529D0, 0x451850, 0x4529F0 and the loop 0x4510F0): the control components of a record's action: NoOp does nothing, Terminate ends the target's list, ChargeNBranch pays its
// cost from the run's source and branches when the payment fails, TestNBranch tests the target critter's hit-point damage and branches when the test fails. The branch is the run system's jump (a Branch: of -1 ends the list).
// Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterControlTests
{
    private const string Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5);
    private static readonly SpellArea Area = new(0x4, 0, 0, 0, -1);
    private const int NoOpNumber = 100, TerminateNumber = 101, ChargeNumber = 102, ChargeTooMuchNumber = 103, ChargeLateBranchNumber = 104, ChargeCellsNumber = 105, TestNumber = 106, NoBranchNumber = 107, ArtTestNumber = 108,
        UnknownComparisonNumber = 109, ZeroCostNumber = 110;

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

    private static SessionSpellRecord Spell(int number, SpellComponentType type, SpellComponentData? data, bool cells = false) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral,
            components: [new(SpellAction.Begin, type, Area, 0, 0, data)])
            with { Cost = 10, MaintainCost = 0, MaintainPeriod = 0, Range = 99, DurationSeconds = 30, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true, ChargesFromCells = cells,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static TestBranchData Test(int field, int comparison, int value, int branch) => new(field, comparison, value, branch);

    private static readonly Dictionary<int, SessionSpellRecord> Records = new[]
    {
        Spell(NoOpNumber, SpellComponentType.NoOp, null),
        Spell(TerminateNumber, SpellComponentType.Terminate, null),
        Spell(ChargeNumber, SpellComponentType.ChargeNBranch, new ChargeBranchData(3, 2)),
        Spell(ChargeTooMuchNumber, SpellComponentType.ChargeNBranch, new ChargeBranchData(5000, -1)),
        Spell(ChargeLateBranchNumber, SpellComponentType.ChargeNBranch, new ChargeBranchData(5000, 7)),
        Spell(ChargeCellsNumber, SpellComponentType.ChargeNBranch, new ChargeBranchData(1, -1), cells: true),
        Spell(ZeroCostNumber, SpellComponentType.ChargeNBranch, new ChargeBranchData(0, -1)),
        Spell(TestNumber, SpellComponentType.TestNBranch, Test(SessionSpellCaster.HitPointDamageField, 0, 0, 4)),
        Spell(NoBranchNumber, SpellComponentType.TestNBranch, Test(SessionSpellCaster.HitPointDamageField, 0, 99, -1)),
        Spell(ArtTestNumber, SpellComponentType.TestNBranch, Test(1, 0, 0, -1)),
        Spell(UnknownComparisonNumber, SpellComponentType.TestNBranch, Test(SessionSpellCaster.HitPointDamageField, 9, 0, 3)),
    }.ToDictionary(r => r.Data.Number);

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        return (owner, new SessionSpellCaster(owner, n => Records.GetValueOrDefault(n), new Map(owner), new Events()));
    }

    // A run that never began, parent, source and target the hero (the ending by event would free a run at once; Apply takes the component directly).
    private static SpellRun Run(GameSessionOwner owner, SessionSpellCaster caster, int number, bool source = true)
    {
        var record = Records[number];
        var me = caster.Facts(Hero)!;
        var started = owner.StartSpellRun(owner.Version, new(new(number, me, source ? me : null, me, me.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return owner.QuerySpellRuns()[started.Creation!.Run!.Id]!;
    }

    private static SpellRunTarget OnHero(SessionSpellCaster caster) => new(caster.RunObject(Hero), HeroTile);

    private static bool NotReady(SessionSpellCaster caster) => caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady);

    [TestMethod]
    public void A_no_op_does_nothing_and_a_terminate_ends_the_targets_list()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(SpellComponentOutcome.None, caster.Apply(Run(owner, caster, NoOpNumber), 0, OnHero(caster)));
        Assert.AreEqual(SpellComponentOutcome.EndOfList, caster.Apply(Run(owner, caster, TerminateNumber), 0, OnHero(caster)).JumpTo);
        // Neither takes the target: a tile entry ends the list as well, and neither is "not ready".
        Assert.AreEqual(SpellComponentOutcome.EndOfList, caster.Apply(Run(owner, caster, TerminateNumber), 0, new(null, HeroTile)).JumpTo);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_charge_branch_that_pays_goes_on_and_one_that_cannot_pay_branches()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var fatigue = owner.QueryHeroLife()!.Status.Fatigue;
        var paid = caster.Apply(Run(owner, caster, ChargeNumber), 0, OnHero(caster));
        Assert.IsNull(paid.JumpTo, "the payment of 3 succeeds");
        Assert.IsTrue(owner.QueryHeroLife()!.Status.Fatigue < fatigue, "the cost is paid in fatigue (the source is a critter without worn mana stores here)");
        // A cost no fatigue covers: the branch -1 ends the list, a branch of 7 jumps to 7.
        Assert.AreEqual(SpellComponentOutcome.EndOfList, caster.Apply(Run(owner, caster, ChargeTooMuchNumber), 0, OnHero(caster)).JumpTo);
        Assert.AreEqual(7, caster.Apply(Run(owner, caster, ChargeLateBranchNumber), 0, OnHero(caster)).JumpTo);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_charge_branch_with_no_source_a_zero_cost_or_a_cell_record_without_cells()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.IsNull(caster.Apply(Run(owner, caster, ChargeTooMuchNumber, source: false), 0, OnHero(caster)).JumpTo, "no run source (handle 0): the component does nothing");
        Assert.IsNull(caster.Apply(Run(owner, caster, ZeroCostNumber), 0, OnHero(caster)).JumpTo, "a cost of 0 pays nothing");
        // A record that charges from cells asks the cell source; the hero holds none, so the payment fails and the list ends.
        Assert.AreEqual(SpellComponentOutcome.EndOfList, caster.Apply(Run(owner, caster, ChargeCellsNumber), 0, OnHero(caster)).JumpTo);
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    public void A_test_branch_compares_the_targets_hit_point_damage_and_branches_when_the_test_fails()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var run = Run(owner, caster, TestNumber);
        // No damage yet: "damage equals 0" passes, so the list goes on.
        Assert.IsNull(caster.Apply(run, 0, OnHero(caster)).JumpTo);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 3 }).Disposition);
        var status = owner.QueryHeroLife()!.Status;
        Assert.AreEqual(3, status.MaxHitPoints - status.HitPoints, "the field is the damage the hero took");
        Assert.AreEqual(4, caster.Apply(run, 0, OnHero(caster)).JumpTo, "damage 3 is not 0: the test fails and jumps to Branch 4");
        Assert.AreEqual(SpellComponentOutcome.EndOfList, caster.Apply(Run(owner, caster, NoBranchNumber), 0, OnHero(caster)).JumpTo, "damage 3 is not 99 and the branch -1 ends the list");
        Assert.IsFalse(NotReady(caster));
    }

    [TestMethod]
    [DataRow(0, 3, true)]
    [DataRow(0, 2, false)]
    [DataRow(1, 2, true)]
    [DataRow(1, 3, false)]
    [DataRow(2, 3, true)]
    [DataRow(2, 4, false)]
    [DataRow(3, 4, true)]
    [DataRow(3, 3, false)]
    [DataRow(4, 3, true)]
    [DataRow(4, 2, false)]
    [DataRow(9, 3, false)]
    public void Each_comparison_passes_as_the_original_tests_it(int comparison, int value, bool passes)
    {
        // The damage is 3 (the field); the component's value is on the right: 0 equals, 1 value below the field, 2 value at most the field, 3 field below value, 4 field at most value; any other index fails.
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 3 }).Disposition);
        var record = Spell(111, SpellComponentType.TestNBranch, Test(SessionSpellCaster.HitPointDamageField, comparison, value, -1));
        var scoped = new Dictionary<int, SessionSpellRecord>(Records) { [111] = record };
        var local = new SessionSpellCaster(owner, n => scoped.GetValueOrDefault(n), new Map(owner), new Events());
        var me = local.Facts(Hero)!;
        var started = owner.StartSpellRun(owner.Version, new(new(111, me, me, me, me.Location), Free: true), record.Run, local.InvocationWorld(), owner.SpellRolls);
        var run = owner.QuerySpellRuns()[started.Creation!.Run!.Id]!;
        var outcome = local.Apply(run, 0, new(local.RunObject(Hero), HeroTile));
        Assert.AreEqual(passes, outcome.JumpTo is null, $"comparison {comparison} value {value}");
    }

    [TestMethod]
    public void A_test_branch_does_nothing_on_a_tile_and_is_not_ready_for_a_field_the_host_does_not_read()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.IsNull(caster.Apply(Run(owner, caster, TestNumber), 0, new(null, HeroTile)).JumpTo, "no target object: nothing happens and nothing is logged");
        Assert.IsFalse(NotReady(caster));
        Assert.IsNull(caster.Apply(Run(owner, caster, ArtTestNumber), 0, OnHero(caster)).JumpTo);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady && e.Value == (int)SpellComponentType.TestNBranch), "the art field (1) is not read yet: the component is logged not ready");
    }
}
