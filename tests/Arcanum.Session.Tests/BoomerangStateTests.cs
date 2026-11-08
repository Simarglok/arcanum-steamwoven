using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-019: a wielded boomerang (the throwing skill, weapon flag 0x40) stays in the hand, is checked with the thrown range of its
// own weight, marks the hero while its projectile is away (no attack starts), turns back at its first end and is back when its
// flight ends (ledger RH3, RH4, BR rows). The half-elf (strength 8) wields an invented boomerang of range 8 whose kit item weighs 150
// (thrown range 400 / 15 / 5 = 5); the rats are AttackStateTests' small animal. Values are invented.
[TestClass]
public sealed class BoomerangStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    internal static GameSessionOwner Owner(int weight = 150)
    {
        var hero = HalfElf() with { Kit = [Kit[0] with { Weight = new(weight) }, Kit[1], Kit[2]] };
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = hero }).Owner!;
        var boomerang = new EquipmentItem
        {
            Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
            Weapon = new EquipmentWeapon { Skill = CombatSkill.Throwing, Damage = DamageRanges.None with { Normal = new(2, 6) }, SpeedFactor = 10, Range = 8 },
        };
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, boomerang),
            (_, _) => true, Accept).Failure);
        return owner;
    }

    internal static SessionCritterCombat Rat(string entity = "oid:rat") => SessionCritterCombat.From(entity, AttackStateTests.Npc(hpAdjustment: 5000)).Combat!;

    private static int Hp(GameSessionOwner owner, SessionCritterCombat rat) => owner.QueryLife(rat.Target).View!.Status.HitPoints;

    internal static bool Wields(GameSessionOwner owner) => owner.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon);

    // Throws until the outcome is a hit (or a miss), ending each other shot (the boomerang is back in the hand).
    internal static SessionAttackResult ThrowUntil(GameSessionOwner owner, SessionCritterCombat rat, bool hit, int distance = 3)
    {
        for (var i = 0; i < 60; i++)
        {
            var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = distance, Flight = true });
            Assert.AreEqual(GameSessionFailure.None, shot.Failure);
            var hits = shot.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
            if (hits == hit && shot.Resolution.Outcome != AttackOutcome.CriticalMiss) return shot;
            Assert.IsTrue(owner.EndShot(shot.Shot!.Value));
        }
        Assert.Fail("no such throw");
        return null!;
    }

    [TestMethod]
    public void ABoomerangIsSupportedStaysInTheHandAndIsCheckedWithItsThrownRange()
    {
        using var owner = Owner();
        var hero = owner.QueryHeroAttack()!;
        Assert.AreEqual((CombatSkill.Throwing, 8, true), (hero.Skill, hero.Range, hero.Supported), "the reach stays the weapon's range");
        var near = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 3, Flight = true });
        Assert.AreEqual(GameSessionFailure.None, near.Failure);
        Assert.IsTrue(Wields(owner), "the boomerang stays in the hand");
        Assert.AreEqual(0, near.Resolution!.Difficulty!.Range, "within the thrown range 5 and half the Perception: no range penalty");
        Assert.AreEqual(1, near.Shots.Count, "one projectile");
        Assert.IsNull(near.Scatter, "a boomerang never scatters (PS3)");
        Assert.IsNull(owner.QueryThrownShot(near.Shot!.Value), "nothing is thrown");
        Assert.IsTrue(owner.EndShot(near.Shot.Value));
        // Six cells lie beyond the thrown range 400 / 15 / 5 = 5, though within the weapon's reach of 8: the check is impossible.
        var far = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 6, Flight = true });
        Assert.IsTrue(far.Resolution!.Difficulty!.Range >= 1_000_000);
        Assert.IsFalse(far.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit);
        Assert.IsNotNull(far.Shot, "the impossible shot still flies");
    }

    [TestMethod]
    public void TheThrownRangeComesFromTheBoomerangsOwnWeight()
    {
        // Weight 0: max(1, 0 / 10) = 1, so the thrown range is 400 / 1 / 5 = 80 and six cells carry no impossible range.
        using var owner = Owner(weight: 0);
        var shot = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 6, Flight = true });
        Assert.AreEqual(GameSessionFailure.None, shot.Failure);
        Assert.IsTrue(shot.Resolution!.Difficulty!.Range < 1_000_000, $"range term {shot.Resolution.Difficulty.Range}");
    }

    [TestMethod]
    public void WhileTheBoomerangIsAwayNoAttackStarts()
    {
        using var owner = Owner();
        var rat = Rat();
        Assert.IsFalse(owner.QueryBoomerangInFlight());
        var shot = owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Shot!.Value;
        Assert.IsTrue(owner.QueryBoomerangInFlight());
        Assert.IsTrue(owner.QueryShotReturns(shot));
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.WeaponInFlight, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Failure);
        Assert.AreEqual(GameSessionFailure.WeaponInFlight, owner.AttackCritter(owner.Version, new(rat) { Distance = 3 }).Failure, "nor an immediate one");
        Assert.AreEqual(GameSessionFailure.WeaponInFlight, owner.AttackObject(owner.Version, new() { Wall = true, Distance = 3, Flight = true }).Failure);
        Assert.AreEqual(revision, owner.Version.Revision, "a refused start changes nothing");
        Assert.IsTrue(owner.EndShot(shot));
        Assert.IsFalse(owner.QueryBoomerangInFlight(), "back in the hand");
        Assert.AreEqual(GameSessionFailure.None, owner.AttackCritter(owner.Version, new(rat) { Distance = 3, Flight = true }).Failure);
    }

    [TestMethod]
    public void AStrikeTurnsTheBoomerangBackAndItStrikesNothingMore()
    {
        using var owner = Owner();
        var rat = Rat();
        var before = Hp(owner, rat);
        var shot = ThrowUntil(owner, rat, hit: true);
        var flying = shot.Shot!.Value;
        var struck = owner.LandShot(owner.Version, flying, new(rat));
        Assert.AreEqual((GameSessionFailure.None, shot.Resolution!.Outcome), (struck.Failure, struck.Resolution!.Outcome));
        Assert.AreEqual(before - struck.Life!.HitPointDamage, Hp(owner, rat));
        CollectionAssert.AreEqual(new[] { flying }, owner.QueryShotsInFlight().ToArray(), "the strike turned it back: it is still in flight");
        Assert.IsFalse(owner.QueryShotReturns(flying), "it returns now");
        Assert.IsTrue(owner.QueryBoomerangInFlight(), "and the hero waits for it");
        Assert.IsFalse(owner.ReturnShot(flying), "it turns once");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShot(owner.Version, flying, new(Rat("oid:other"))).Failure,
            "a returning boomerang strikes nothing");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShotOnObject(owner.Version, flying, new() { Wall = true }).Failure);
        Assert.IsTrue(owner.EndShot(flying));
        Assert.AreEqual((0, false, true), (owner.QueryShotsInFlight().Count, owner.QueryBoomerangInFlight(), Wields(owner)));
    }

    [TestMethod]
    public void AMissTurnsBackAtItsRangeEndAndAStrayOnTheWayOutStillCounts()
    {
        using var owner = Owner();
        var rat = Rat();
        var miss = ThrowUntil(owner, rat, hit: false).Shot!.Value;
        // On the way out a missed boomerang may strike another critter (1 in 3, PF6): a forced hit that turns it back.
        var stray = owner.LandShot(owner.Version, miss, new(Rat("oid:other")));
        Assert.AreEqual((GameSessionFailure.None, AttackOutcome.Hit), (stray.Failure, stray.Resolution!.Outcome));
        Assert.IsFalse(owner.ReturnShot(miss), "the strike turned it already");
        Assert.IsTrue(owner.EndShot(miss));
        var second = ThrowUntil(owner, rat, hit: false).Shot!.Value;
        var revision = owner.Version.Revision;
        Assert.IsTrue(owner.ReturnShot(second), "beyond its range it turns back instead of ending");
        Assert.AreEqual(revision, owner.Version.Revision, "nothing saved changes");
        Assert.IsFalse(owner.ReturnShot(second));
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShot(owner.Version, second, new(Rat("oid:other"))).Failure);
        Assert.IsTrue(owner.EndShot(second));
        Assert.IsFalse(owner.ReturnShot(second), "a shot not in flight does not turn");
    }

    [TestMethod]
    public void AWallStrikeTurnsTheBoomerangBackAndAThrowRequestIsRefused()
    {
        using var owner = Owner();
        // A boomerang is no "default throws" weapon: a throw request for it is refused before any roll (ThrowingHero takes melee only).
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(Rat()) { Distance = 3, Flight = true, Throw = new(150) }).Failure);
        var wall = owner.AttackObject(owner.Version, new() { Wall = true, Distance = 3, Flight = true });
        Assert.AreEqual(GameSessionFailure.None, wall.Failure);
        var flying = wall.Shot!.Value;
        Assert.IsTrue(owner.QueryShotReturns(flying));
        Assert.AreEqual(GameSessionFailure.None, owner.LandShotOnObject(owner.Version, flying, new() { Wall = true }).Failure);
        Assert.IsFalse(owner.QueryShotReturns(flying), "the wall turned it back");
        Assert.IsTrue(owner.QueryBoomerangInFlight());
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandShotOnObject(owner.Version, flying, new() { Wall = true }).Failure, "it strikes once");
        Assert.IsTrue(owner.EndShot(flying));
        Assert.IsFalse(owner.QueryBoomerangInFlight());
    }

    [TestMethod]
    public void ASnapshotWhileTheBoomerangIsAwayRestoresItInTheHandWithoutTheMark()
    {
        using var owner = Owner();
        var shot = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 3, Flight = true }).Shot!.Value;
        using (var restored = GameSessionOwner.Restore(owner.Capture(), RestoreOptions()).Owner!)
        {
            Assert.AreEqual((0, false, true), (restored.QueryShotsInFlight().Count, restored.QueryBoomerangInFlight(), Wields(restored)),
                "the session keeps no projectile and no mark");
            Assert.AreEqual(GameSessionFailure.None, restored.AttackCritter(restored.Version, new(Rat()) { Distance = 3, Flight = true }).Failure);
        }
        Assert.IsTrue(owner.QueryBoomerangInFlight());
        Assert.IsTrue(owner.EndShot(shot));
    }
}
