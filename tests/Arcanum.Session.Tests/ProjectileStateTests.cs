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

    private static GameSessionOwner Owner(int arrows = 40, SkillTraining training = SkillTraining.None)
    {
        // ARC-COMBAT-018: the bow's training is in the stored skill's training bits.
        int[] basic = new int[12];
        basic[(int)Arcanum.Simulation.Skills.Skill.Bow] = 4 | (int)training << 6;
        var hero = training == SkillTraining.None ? HalfElf() : HalfElf() with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = hero }).Owner!;
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
            foreach (var other in shot.Shots) owner.EndShot(other);
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

    // ARC-COMBAT-018 (0x4B39B0, ledger PF2, PS rows): from expert training a bow shot flies as two projectiles with the same decided
    // outcome; each strikes on its own and rolls its own damage, and the arrow went once, at the shot.
    [TestMethod]
    public void AnExpertBowShotFliesAsTwoProjectilesThatStrikeOnTheirOwnForOneArrow()
    {
        using var owner = Owner(training: SkillTraining.Expert);
        Assert.AreEqual(SkillTraining.Expert, owner.QueryHeroAttack()!.Training);
        var rat = Rat();
        var before = Hp(owner, rat);
        var arrows = owner.QueryHeroAmmo().Single().Units;
        var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true });
        Assert.AreEqual(2, shot.Shots.Count);
        Assert.AreEqual(shot.Shots[0], shot.Shot);
        CollectionAssert.AreEqual(shot.Shots.ToArray(), owner.QueryShotsInFlight().ToArray());
        Assert.AreEqual(arrows - 1, owner.QueryHeroAmmo().Single().Units, "one arrow for both");
        Assert.IsNull(shot.Scatter);
        foreach (var flying in shot.Shots) owner.EndShot(flying);

        var hit = ShootUntil(owner, rat, hit: true);
        var first = owner.LandShot(owner.Version, hit.Shots[0], new(rat));
        var second = owner.LandShot(owner.Version, hit.Shots[1], new(rat));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionDisposition.Changed), (first.Disposition, second.Disposition));
        Assert.AreEqual((hit.Resolution!.HitLocation, hit.Resolution.HitLocation), (first.Resolution!.HitLocation, second.Resolution!.HitLocation),
            "both carry the hit decided at the shot");
        Assert.AreEqual(before - first.Life!.HitPointDamage - second.Life!.HitPointDamage, Hp(owner, rat), "each rolls its own damage");
        Assert.AreEqual((0, (SessionAmmoUse?)null), (owner.QueryShotsInFlight().Count, second.Ammunition));
    }

    [TestMethod]
    public void TheSecondArrowOnACorpseDoesNothingAndAMissFliesAsTwoStrays()
    {
        using var owner = Owner(training: SkillTraining.Master);
        var rat = Rat();
        var hit = ShootUntil(owner, rat, hit: true);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, rat.Target, new LifeDamage { HitPoints = 100_000 }).Disposition);
        var late = owner.LandShot(owner.Version, hit.Shots[1], new(rat));
        Assert.AreEqual((GameSessionDisposition.NoChange, AttackRefusal.TargetDead), (late.Disposition, late.Resolution!.Refusal), "a corpse takes no damage");
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count, "that arrow is gone");
        owner.EndShot(hit.Shots[0]);

        var other = Rat("oid:other");
        var miss = ShootUntil(owner, other, hit: false);
        Assert.AreEqual(2, miss.Shots.Count);
        foreach (var flying in miss.Shots)
        {
            Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShot(owner.Version, flying, new(other)).Failure, "a miss never strikes its target");
            Assert.AreEqual(AttackOutcome.Hit, owner.LandShot(owner.Version, flying, new(Rat("oid:third"))).Resolution!.Outcome, "each may stray");
        }
    }

    [TestMethod]
    public void BothArrowsMustFitTheLimitAndABowBelowExpertShootsOne()
    {
        using var owner = Owner(arrows: GameSessionOwner.MaxShotsInFlight, training: SkillTraining.Expert);
        var rat = Rat();
        for (var i = 0; i < GameSessionOwner.MaxShotsInFlight / 2 - 1; i++)
            Assert.AreEqual(2, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Shots.Count);
        Assert.AreEqual(GameSessionOwner.MaxShotsInFlight - 2, owner.QueryShotsInFlight().Count);
        Assert.AreEqual(2, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Shots.Count);
        owner.EndShot(owner.QueryShotsInFlight()[0]);
        var units = owner.QueryHeroAmmo().Single().Units;
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Failure,
            "one free place is not enough for two");
        Assert.AreEqual(units, owner.QueryHeroAmmo().Single().Units, "a refused shot spends nothing");
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2, Flight = true }).Failure);

        using var apprentice = Owner(training: SkillTraining.Apprentice);
        Assert.AreEqual(1, apprentice.AttackCritter(apprentice.Version, new(rat) { Distance = 3, Flight = true }).Shots.Count);
        var wall = apprentice.AttackObject(apprentice.Version, new() { Wall = true, Distance = 2, Flight = true });
        Assert.AreEqual(1, wall.Shots.Count);
    }

    [TestMethod]
    public void AnExpertShotAtAWallFliesAsTwoAndEachStrikes()
    {
        using var owner = Owner(training: SkillTraining.Expert);
        var wall = owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2, Flight = true });
        Assert.AreEqual(2, wall.Shots.Count);
        foreach (var flying in wall.Shots)
            Assert.AreEqual(GameSessionFailure.None, owner.LandShotOnObject(owner.Version, flying, new() { Wall = true }).Failure);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }
}
