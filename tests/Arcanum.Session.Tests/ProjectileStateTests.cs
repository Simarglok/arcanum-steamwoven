using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-016: a shot that flies is decided at once and strikes on arrival (ledger PF rows). The half-elf wields an invented bow
// of range 10 using arrows, as in RangedAttackStateTests; the rats are AttackStateTests' small animal. Values are invented.
[TestClass]
public sealed class ProjectileStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static GameSessionOwner Owner(int arrows = 40)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = new SessionMapInventorySeed([],
            [new SessionSeedItem("arrows", new(new(SessionItemType.Ammo, 7058, 100, 200), 1, 0, 0, arrows, []) { AmmoType = 0 }, new CellCoord(101, 200))]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "arrows", Accept).Disposition);
        var bow = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Bow, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 10, Range = 10, AmmoType = 0, AmmoConsumption = 1 },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, bow),
            (_, _) => true, Accept).Failure);
        return owner;
    }

    private static SessionCritterCombat Rat(string entity = "oid:rat") => SessionCritterCombat.From(entity, AttackStateTests.Npc(hpAdjustment: 5000)).Combat!;

    private static int Hp(GameSessionOwner owner, SessionCritterCombat rat) => owner.QueryLife(rat.Target).View!.Status.HitPoints;

    // Shoots until the outcome is a hit (or a miss): the session's sequence is seeded, so a few shots do it.
    private static SessionAttackResult ShootUntil(GameSessionOwner owner, SessionCritterCombat rat, bool hit, int distance = 3)
    {
        for (var i = 0; i < 60; i++)
        {
            var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = distance, Flight = true });
            Assert.AreEqual(GameSessionFailure.None, shot.Failure);
            var hits = shot.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
            if (hits == hit && shot.Resolution.Outcome != AttackOutcome.CriticalMiss) return shot;
            if (shot.Shot is { } other) owner.EndShot(other);
        }
        Assert.Fail("no such shot");
        return null!;
    }

    [TestMethod]
    public void AHitFliesWithoutDamageAndStrikesOnArrival()
    {
        using var owner = Owner();
        var rat = Rat();
        var before = Hp(owner, rat);
        var shot = ShootUntil(owner, rat, hit: true);
        Assert.IsNotNull(shot.Shot);
        Assert.AreEqual((null, (LifeOutcome?)null), (shot.Resolution!.Damage, shot.Life), "the damage waits for the arrival");
        Assert.AreEqual(before, Hp(owner, rat));
        CollectionAssert.AreEqual(new[] { shot.Shot!.Value }, owner.QueryShotsInFlight().ToArray());
        var arrows = owner.QueryHeroAmmo().Single().Units;
        var landed = owner.LandShot(owner.Version, shot.Shot.Value, new(rat) { Distance = 99 });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (landed.Disposition, landed.Failure));
        Assert.AreEqual((shot.Resolution.Outcome, shot.Resolution.HitLocation), (landed.Resolution!.Outcome, landed.Resolution.HitLocation),
            "the strike keeps the hit decided at the shot");
        Assert.IsNotNull(landed.Resolution.Damage);
        Assert.AreEqual(before - landed.Life!.HitPointDamage, Hp(owner, rat));
        Assert.AreEqual((arrows, (SessionAmmoUse?)null, (SessionShot?)null), (owner.QueryHeroAmmo().Single().Units, landed.Ammunition, landed.Shot),
            "the arrow went at the shot");
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShot(owner.Version, shot.Shot.Value, new(rat)).Failure, "a shot strikes once");
    }

    [TestMethod]
    public void AMissNeverStrikesItsTargetButAnotherCritterTakesAForcedHit()
    {
        using var owner = Owner();
        var rat = Rat();
        var other = Rat("oid:other");
        // Beyond the range the check is impossible: the shot misses, and still flies.
        var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = 11, Flight = true });
        Assert.IsFalse(shot.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit);
        var flying = shot.Shot!.Value;
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShot(owner.Version, flying, new(rat)).Failure);
        var stray = owner.LandShot(owner.Version, flying, new(other) { Aim = HitLocation.Head });
        Assert.AreEqual((GameSessionFailure.None, AttackOutcome.Hit, HitLocation.Torso, false), (stray.Failure, stray.Resolution!.Outcome,
            stray.Resolution.HitLocation, stray.Resolution.Aimed), "a forced hit: torso, not aimed, no critical, whatever the aim");
        Assert.IsNull(stray.Resolution.AttackCheck, "no check is rolled");
        Assert.IsTrue(stray.Life!.HitPointDamage >= 0);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void AShotWhoseFlightEndsIsGoneAndAShotWithoutFlightIsNotKept()
    {
        using var owner = Owner();
        var rat = Rat();
        var plain = owner.AttackCritter(owner.Version, new(rat) { Distance = 3 });
        Assert.AreEqual((SessionShot?)null, plain.Shot);
        var shot = ShootUntil(owner, rat, hit: true);
        var before = Hp(owner, rat);
        Assert.IsTrue(owner.EndShot(shot.Shot!.Value));
        Assert.IsFalse(owner.EndShot(shot.Shot.Value));
        Assert.AreEqual(before, Hp(owner, rat), "a hit that never met its target deals nothing");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShot(owner.Version, shot.Shot.Value, new(rat)).Failure);
        // The shot is not saved: a restored session has none in flight.
        var flying = owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Shot!.Value;
        using var restored = GameSessionOwner.Restore(owner.Capture(), RestoreOptions()).Owner!;
        Assert.AreEqual(0, restored.QueryShotsInFlight().Count);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, restored.LandShot(restored.Version, flying, new(rat)).Failure);
    }

    [TestMethod]
    public void AShotIntoAWallFliesAndTheWallTakesItOnArrival()
    {
        using var owner = Owner(arrows: 3);
        var shot = owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2, Flight = true });
        Assert.AreEqual((AttackOutcome.Hit, (AttackDamage?)null, 2), (shot.Resolution!.Outcome, shot.Resolution.Damage, shot.Ammunition!.Value.UnitsAfter));
        var landed = owner.LandShotOnObject(owner.Version, shot.Shot!.Value, new() { Wall = true });
        Assert.AreEqual((GameSessionFailure.None, DamagePrevention.Wall), (landed.Failure, landed.Resolution!.Damage!.Prevented));
        Assert.AreEqual(2, owner.QueryHeroAmmo().Single().Units);
        // A missed shot at an NPC may strike an object past its destination: a forced hit.
        var miss = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 11, Flight = true });
        var wall = owner.LandShotOnObject(owner.Version, miss.Shot!.Value, new() { Wall = true });
        Assert.AreEqual((GameSessionFailure.None, AttackOutcome.Hit), (wall.Failure, wall.Resolution!.Outcome));
    }

    [TestMethod]
    public void NoMoreThanTheLimitFlyAtOnce()
    {
        using var owner = Owner(arrows: GameSessionOwner.MaxShotsInFlight + 2);
        var rat = Rat();
        for (var i = 0; i < GameSessionOwner.MaxShotsInFlight; i++)
            Assert.IsNotNull(owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Shot);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Failure);
        owner.EndShot(owner.QueryShotsInFlight()[0]);
        Assert.IsNotNull(owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Shot);
    }
}
