using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-255 (GT195, ledger OD3/OD4/OD7-OD11): damage that is no hero attack (a script, a spell, a burn, a trap) reaches doors, windows,
// containers, scenery and traps through the same object state an attack writes (object.damage, object.condition). Objects, materials and
// hit points are invented.
[TestClass]
public sealed class ObjectDamageStateTests
{
    private static readonly CellCoord ChestCell = new(120, 210);

    private static SessionObjectTarget Target(string entity, DamageableObjectKind kind, int points = 6, ObjectMaterial material = ObjectMaterial.Cloth,
        Func<SessionObjectBaseline, SessionObjectBaseline>? with = null)
    {
        var baseline = new SessionObjectBaseline
        {
            Kind = kind, Points = points, Material = material,
            Openable = kind switch
            {
                DamageableObjectKind.Portal => new SessionOpenableBaseline(OpenableKind.Portal, 0, 0, 0),
                DamageableObjectKind.Container => new SessionOpenableBaseline(OpenableKind.Container, 0, 0, 0),
                _ => null,
            },
            Cell = kind == DamageableObjectKind.Container ? ChestCell : default,
        };
        return new(Exterior, entity, with?.Invoke(baseline) ?? baseline);
    }

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static SessionMapInventorySeed Seed() => new(
        [new("chest", SessionHolderKind.Container, false, false, [], ["cup", "knife"])],
        [
            new("cup", InventoryStateTests.Facts(SessionItemType.Generic, 5650), null),
            new("knife", InventoryStateTests.Facts(SessionItemType.Weapon, 2001), null),
        ]);

    [TestMethod]
    public void DamageGrowsTheHitPointDamageAndAWholeDoorStays()
    {
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        var hit = owner.DamageObject(owner.Version, door, 2);
        Assert.AreEqual(GameSessionDisposition.Changed, hit.Disposition);
        Assert.AreEqual((0, 2, false), (hit.Hit!.DamageBefore, hit.Hit.Damage, hit.Hit.Broken));
        Assert.AreEqual(SessionObjectHostSteps.None, hit.Host, "a hit with no attacker has no witnesses");
        Assert.AreEqual(new SessionObjectState(2, 4, SessionObjectCondition.Whole), owner.QueryObject(door));
        var second = owner.DamageObject(owner.Version, door, 3);
        Assert.AreEqual(new SessionObjectState(5, 1, SessionObjectCondition.Whole), owner.QueryObject(door));
        Assert.IsFalse(second.Hit!.Broken);
    }

    [TestMethod]
    public void ADoorAtZeroHitPointsIsDestroyedAndThenUnavailable()
    {
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        var broke = owner.DamageObject(owner.Version, door, 6);
        Assert.AreEqual((true, ObjectBreak.Destroyed), (broke.Hit!.Broken, broke.Hit.Break));
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(door).Condition);
        Assert.AreEqual(SessionObjectHostSteps.None, broke.Host, "a door has no destroy sound");
        var again = owner.DamageObject(owner.Version, door, 1);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ObjectUnavailable), (again.Disposition, again.Failure));
    }

    [TestMethod]
    public void ATrappedObjectNamesTheTrapWithTheBreakAndNoWitnesses()
    {
        using var owner = Owner();
        var chest = Target("chest", DamageableObjectKind.Container, with: b => b with { Trapped = true });
        var broke = owner.DamageObject(owner.Version, chest, 100);
        Assert.AreEqual(ObjectBreak.ContainerSpilled, broke.Hit!.Break);
        Assert.AreEqual(SessionObjectHostSteps.TrapFires | SessionObjectHostSteps.SpillEyeCandy, broke.Host, "the trap is the host's, before the break");
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(chest).Condition);
        var pile = owner.QueryMapInventory(Exterior)!.JunkPiles.Single();
        Assert.AreEqual(ChestCell, pile.Cell, "the items lie at the chest's cell, gathered into a pile");
        CollectionAssert.AreEqual(new[] { "knife", "cup" }, pile.Items.Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void AWindowBustsThroughTheLockDomainAndBustsAgainOnEveryHit()
    {
        using var owner = Owner();
        var window = Target("sector:1:8", DamageableObjectKind.Portal, with: b => b with { Window = true, Openable = new(OpenableKind.Portal, 0, 0, 1) });
        var broke = owner.DamageObject(owner.Version, window, 6);
        Assert.AreEqual(ObjectBreak.WindowBusted, broke.Hit!.Break);
        Assert.IsTrue(broke.Host.HasFlag(SessionObjectHostSteps.BustScript));
        var state = owner.QueryOpenable(new(Exterior, window.Entity), window.Baseline.Openable!.Value);
        Assert.AreEqual((LockRules.BustedBit, 0), (state.Flags & LockRules.BustedBit, state.Frame), "busted on frame 0");
        Assert.AreEqual(ObjectBreak.WindowBusted, owner.DamageObject(owner.Version, window, 1).Hit!.Break, "the damage is kept (ODA3)");
    }

    [TestMethod]
    public void RespawningSceneryIsSwitchedOffWithAnEvent()
    {
        using var owner = Owner();
        var scenery = Target("sector:1:9", DamageableObjectKind.Scenery, with: b => b with { Respawning = true, RespawnDelay = 5 });
        var broke = owner.DamageObject(owner.Version, scenery, 6);
        Assert.AreEqual(ObjectBreak.SceneryRespawns, broke.Hit!.Break);
        Assert.AreEqual(SessionObjectCondition.Off, owner.QueryObject(scenery).Condition);
        Assert.AreEqual(1, owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.ObjectRespawn));
    }

    [TestMethod]
    public void AnAmountOfZeroOrLessChangesNothingAndABadTargetIsRefused()
    {
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        var version = owner.Version;
        foreach (var amount in new[] { 0, -4 })
        {
            var none = owner.DamageObject(version, door, amount);
            Assert.AreEqual((GameSessionDisposition.NoChange, null), (none.Disposition, none.Hit));
        }
        Assert.AreEqual(version, owner.Version);
        var stale = owner.DamageObject(new GameSessionVersion(owner.Identity, 5), door, 1);
        Assert.AreEqual(GameSessionFailure.StaleVersion, stale.Failure);
        var bad = owner.DamageObject(owner.Version, Target("sector:1:7", DamageableObjectKind.Portal, with: b => b with { Window = false, Openable = null }), 1);
        Assert.AreEqual(GameSessionFailure.InvalidObject, bad.Failure, "a portal needs its lock baseline");
        Assert.AreEqual(GameSessionFailure.InvalidObject, owner.DamageObject(owner.Version, null!, 1).Failure);
    }

    [TestMethod]
    public void TheCombatValuesAreTheMaterialAndClampedResistancesAndABustedSceneryIsInvulnerable()
    {
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal, material: ObjectMaterial.Wood,
            with: b => b with { Resistances = new(120, 3, -5, 40) });
        var (combatant, kind) = owner.QueryObjectCombat(door)!.Value;
        Assert.AreEqual(AttackTargetKind.Portal, kind);
        Assert.AreEqual(ObjectMaterial.Wood, combatant.Material);
        Assert.AreEqual(new DamageResistances(95, 3, 0, 40), combatant.Resistances, "the object resistance getter clamps 0..95");
        Assert.AreEqual(CombatantConditions.None, combatant.Conditions);
        Assert.IsNull(owner.QueryObjectCombat(Target("x", DamageableObjectKind.Portal, with: b => b with { Openable = null })));

        var scenery = Target("sector:1:9", DamageableObjectKind.Scenery, with: b => b with { HasDestroyedArt = true });
        Assert.AreEqual(AttackTargetKind.OtherObject, owner.QueryObjectCombat(scenery)!.Value.Kind);
        Assert.AreEqual(ObjectBreak.SceneryBusted, owner.DamageObject(owner.Version, scenery, 6).Hit!.Break);
        Assert.AreEqual(CombatantConditions.Invulnerable, owner.QueryObjectCombat(scenery)!.Value.Combatant.Conditions, "a busted scenery has object flag 0x400000");
    }

    [TestMethod]
    public void TheDamageStepOfAMetalDoorTakesTheMaterialReductionOfTheStoredValue()
    {
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal, points: 50, material: ObjectMaterial.Metal);
        var (combatant, kind) = owner.QueryObjectCombat(door)!.Value;
        var step = AttackResolver.DamageStep(new()
        {
            Attacker = new Combatant(), Target = combatant, TargetKind = kind, Difficulty = 0,
            Damage = DamageByType.Zero.With(DamageType.Normal, 25),
        });
        Assert.AreEqual(15, step.HitPointDamage, "metal takes 10 from a portal's damage (ledger OD3)");
        Assert.AreEqual(15, owner.DamageObject(owner.Version, door, step.HitPointDamage).Hit!.Damage);
    }

    [TestMethod]
    public void TheStateSurvivesASnapshotRoundTripWithoutASchemaChange()
    {
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        owner.DamageObject(owner.Version, door, 4);
        Assert.AreEqual(4, owner.QueryObject(door).Damage);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(4, restored.QueryObject(door).Damage);
    }
}
