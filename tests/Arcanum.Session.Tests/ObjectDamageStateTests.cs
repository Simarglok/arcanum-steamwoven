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
    public void ADamageRecordWithAnAttackerKeepsTheWitnessStepsAndOneWithoutDropsThem()
    {
        // ARC-AI-2112 (GW10): the damage end calls the guard witnesses only when the record has an attacker (a spell's caster); the loud call is mode 1 for a hit that leaves the door whole, mode 0 for one that breaks it.
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        var whole = owner.DamageObject(owner.Version, door, 2, attacked: true);
        Assert.AreEqual(SessionObjectHostSteps.WitnessesHearAttack, whole.Host);
        var broke = owner.DamageObject(owner.Version, door, 6, attacked: true);
        Assert.AreEqual(SessionObjectHostSteps.WitnessesHearBreak, broke.Host, "a door's runs are the door host's: no interrupt step (issue #2669)");
        using var plain = Owner();
        Assert.AreEqual(SessionObjectHostSteps.None, plain.DamageObject(plain.Version, door, 2, attacked: false).Host);
    }

    [TestMethod]
    public void AZeroHitWithAnAttackerOnAWholeDoorChangesNothingButKeepsTheWitnessStep()
    {
        // ARC-AI-2123 (GW1): the damage end calls the witnesses (mode 1) for a hit of 0 that leaves the object whole, so an attacked record keeps the step; an unattacked one has none.
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        var version = owner.Version;
        var zero = owner.DamageObject(version, door, 0, attacked: true);
        Assert.AreEqual((GameSessionDisposition.NoChange, null, SessionObjectHostSteps.WitnessesHearAttack), (zero.Disposition, zero.Hit, zero.Host));
        var negative = owner.DamageObject(version, door, -4, attacked: true);
        Assert.AreEqual((GameSessionDisposition.NoChange, SessionObjectHostSteps.WitnessesHearAttack), (negative.Disposition, negative.Host));
        Assert.AreEqual(0, owner.QueryObject(door).Damage, "nothing was written");
        Assert.AreEqual(version, owner.Version);
        var plain = owner.DamageObject(version, door, 0, attacked: false);
        Assert.AreEqual((GameSessionDisposition.NoChange, SessionObjectHostSteps.None), (plain.Disposition, plain.Host));
    }

    [TestMethod]
    public void ADoorAtZeroHitPointsIsDestroyedAndThenUnavailable()
    {
        using var owner = Owner();
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        var broke = owner.DamageObject(owner.Version, door, 6);
        Assert.AreEqual((true, ObjectBreak.Destroyed), (broke.Hit!.Broken, broke.Hit.Break));
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(door).Condition);
        Assert.AreEqual(SessionObjectHostSteps.None, broke.Host, "a door has no destroy sound and no destroy step (the door host owns its runs)");
        var again = owner.DamageObject(owner.Version, door, 1);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ObjectUnavailable), (again.Disposition, again.Failure));
    }

    // ARC-SCRIPT-2620 (OD7-OD11, BS3c): a busted scenery broken again is destroyed and the host owes the end of its runs; the first break (busted, art swapped) owes none.
    [TestMethod]
    public void ASceneryBrokenAgainIsDestroyedAndNamesTheEndOfItsRuns()
    {
        using var owner = Owner();
        var scenery = Target("sector:1:9", DamageableObjectKind.Scenery);
        var busted = owner.DamageObject(owner.Version, scenery, 6);
        Assert.AreEqual(ObjectBreak.SceneryBusted, busted.Hit!.Break);
        Assert.IsFalse(busted.Host.HasFlag(SessionObjectHostSteps.ObjectDestroyed), "a busted scenery keeps its runs");
        Assert.IsTrue(busted.Host.HasFlag(SessionObjectHostSteps.ObjectBroken), "but the break's interrupt (level 5) still ends its loop (ARC-SCRIPT-2646, OD7)");
        var destroyed = owner.DamageObject(owner.Version, scenery, 6);
        Assert.AreEqual((true, ObjectBreak.Destroyed), (destroyed.Hit!.Broken, destroyed.Hit.Break));
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(scenery).Condition);
        Assert.IsTrue(destroyed.Host.HasFlag(SessionObjectHostSteps.ObjectDestroyed | SessionObjectHostSteps.DestroySound));
    }

    [TestMethod]
    public void ATrappedObjectNamesTheTrapWithTheBreakAndNoWitnesses()
    {
        using var owner = Owner();
        var chest = Target("chest", DamageableObjectKind.Container, with: b => b with { Trapped = true });
        var broke = owner.DamageObject(owner.Version, chest, 100);
        Assert.AreEqual(ObjectBreak.ContainerSpilled, broke.Hit!.Break);
        Assert.AreEqual(SessionObjectHostSteps.TrapFires | SessionObjectHostSteps.SpillEyeCandy | SessionObjectHostSteps.ObjectDestroyed | SessionObjectHostSteps.ObjectBroken, broke.Host, "the trap is the host's, before the break");
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(chest).Condition);
        var pile = owner.QueryMapInventory(Exterior)!.JunkPiles.Single();
        Assert.AreEqual(ChestCell, pile.Cell, "the items lie at the chest's cell, gathered into a pile");
        CollectionAssert.AreEqual(new[] { "knife", "cup" }, pile.Items.Select(item => item.Entity).ToArray());
    }

    // ARC-SCRIPT-2644 (TD2, OD7-OD11): the break's destroy branches ask the host's destroy point first; a refusal keeps the object (the hit's damage and a container's spill are written all the same).
    [TestMethod]
    public void AContainerWhoseDestroyPointRefusesSpillsButStaysAndEndsNoRuns()
    {
        using var owner = Owner();
        var asked = new List<string>();
        owner.AttachObjectDestroyGate(target => { asked.Add(target.Entity); return false; });
        var chest = Target("chest", DamageableObjectKind.Container);
        var broke = owner.DamageObject(owner.Version, chest, 100);
        Assert.AreEqual(GameSessionDisposition.Changed, broke.Disposition);
        Assert.AreEqual(ObjectBreak.ContainerSpilled, broke.Hit!.Break);
        CollectionAssert.AreEqual(new[] { "chest" }, asked);
        Assert.AreEqual(SessionObjectHostSteps.SpillEyeCandy, broke.Host, "no destroy, but the spill eye candy still plays");
        Assert.IsFalse(broke.Host.HasFlag(SessionObjectHostSteps.ObjectDestroyed));
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(chest).Condition, "the object stays");
        Assert.AreEqual(100, owner.QueryObject(chest).Damage, "the damage is written");
        var pile = owner.QueryMapInventory(Exterior)!.JunkPiles.Single();
        Assert.AreEqual(ChestCell, pile.Cell, "the items were dropped before the destroy point answered");
        Assert.AreEqual(2, pile.Items.Count);
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Holders.Single(holder => holder.Entity == "chest").Items.Count, "the kept chest is empty");
        // A second hit asks again and the gate may now let it go.
        owner.AttachObjectDestroyGate(_ => true);
        var again = owner.DamageObject(owner.Version, chest, 1);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(chest).Condition);
        Assert.IsTrue(again.Host.HasFlag(SessionObjectHostSteps.ObjectDestroyed));
    }

    [TestMethod]
    public void ADestroyPointThatBreaksItsOwnContainerLeavesTheOuterBreakNothingToWrite()
    {
        using var owner = Owner();
        var inside = false;
        var calls = 0;
        owner.AttachObjectDestroyGate(target =>
        {
            calls++;
            if (inside) return true;
            inside = true;
            owner.DamageObject(owner.Version, target, 50);
            return false;
        });
        var chest = Target("chest", DamageableObjectKind.Container);
        var broke = owner.DamageObject(owner.Version, chest, 100);
        Assert.AreEqual(GameSessionDisposition.Changed, broke.Disposition);
        Assert.AreEqual(2, calls, "the nested break asked once more, the outer wrote nothing after");
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(chest).Condition);
        Assert.IsTrue(broke.Host.HasFlag(SessionObjectHostSteps.ObjectDestroyed), "the nested break destroyed it");
        var pile = owner.QueryMapInventory(Exterior)!.JunkPiles.Single();
        CollectionAssert.AreEqual(new[] { "knife", "cup" }, pile.Items.Select(item => item.Entity).ToArray(), "each item dropped once");
    }

    [TestMethod]
    public void AGateThatAllowsOrIsAbsentDestroysAsBefore()
    {
        foreach (var gate in new Func<SessionObjectTarget, bool>?[] { null, _ => true })
        {
            using var owner = Owner();
            owner.AttachObjectDestroyGate(gate);
            var trap = Target("sector:1:3", DamageableObjectKind.Trap, with: b => b with { HasDestroyedArt = false });
            var broke = owner.DamageObject(owner.Version, trap, 6);
            Assert.AreEqual(ObjectBreak.Destroyed, broke.Hit!.Break);
            Assert.IsTrue(broke.Host.HasFlag(SessionObjectHostSteps.ObjectDestroyed | SessionObjectHostSteps.DestroySound));
            Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObject(trap).Condition);
        }
    }

    [TestMethod]
    public void ARefusedDestroyOfATrapOrADoorKeepsTheObjectAndTheSoundStillPlays()
    {
        using var owner = Owner();
        var asked = new List<string>();
        owner.AttachObjectDestroyGate(target => { asked.Add(target.Entity); return false; });
        var trap = Target("sector:1:3", DamageableObjectKind.Trap, with: b => b with { HasDestroyedArt = false });
        var broke = owner.DamageObject(owner.Version, trap, 6);
        Assert.AreEqual(SessionObjectHostSteps.DestroySound, broke.Host & (SessionObjectHostSteps.DestroySound | SessionObjectHostSteps.ObjectDestroyed));
        Assert.AreEqual(new SessionObjectState(6, 0, SessionObjectCondition.Whole), owner.QueryObject(trap), "damage written, condition kept");
        var door = Target("sector:1:7", DamageableObjectKind.Portal);
        var shut = owner.DamageObject(owner.Version, door, 6);
        Assert.AreEqual(ObjectBreak.Destroyed, shut.Hit!.Break);
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(door).Condition, "the refused door stays");
        CollectionAssert.AreEqual(new[] { "sector:1:3", "sector:1:7" }, asked);
        // The refusal is the object's own: a fresh hit on the kept trap breaks again and asks again.
        Assert.AreEqual(ObjectBreak.Destroyed, owner.DamageObject(owner.Version, trap, 1).Hit!.Break);
        Assert.AreEqual(3, asked.Count);
    }

    [TestMethod]
    public void ABreakThatDestroysNothingNeverAsksTheGate()
    {
        using var owner = Owner();
        var asked = 0;
        owner.AttachObjectDestroyGate(_ => { asked++; return false; });
        var crate = Target("sector:1:9", DamageableObjectKind.Scenery);
        Assert.AreEqual(ObjectBreak.SceneryBusted, owner.DamageObject(owner.Version, crate, 6).Hit!.Break);
        Assert.AreEqual(SessionObjectCondition.Busted, owner.QueryObject(crate).Condition);
        var window = Target("sector:1:8", DamageableObjectKind.Portal, with: b => b with { Window = true, Openable = new(OpenableKind.Portal, 0, 0, 1) });
        Assert.AreEqual(ObjectBreak.WindowBusted, owner.DamageObject(owner.Version, window, 6).Hit!.Break);
        Assert.AreEqual(0, asked, "a bust or a window's bust is no destroy");
        // A busted scenery broken again takes the destroy branch (0x43F4xx), and the gate answers.
        Assert.AreEqual(ObjectBreak.Destroyed, owner.DamageObject(owner.Version, crate, 1).Hit!.Break);
        Assert.AreEqual(1, asked);
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
        // At 0 hit points a busted window keeps its damage, so a hit for 0 busts it again (as the hero's swing does, ODA3); a window with hit points left does not.
        var window = Target("sector:1:8", DamageableObjectKind.Portal, with: b => b with { Window = true, Openable = new(OpenableKind.Portal, 0, 0, 1) });
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DamageObject(owner.Version, window, 0).Disposition);
        owner.DamageObject(owner.Version, window, 6);
        Assert.AreEqual(ObjectBreak.WindowBusted, owner.DamageObject(owner.Version, window, 0).Hit!.Break);
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
