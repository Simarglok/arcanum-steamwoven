using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's ranged attack (ARC-COMBAT-015): a bow or firearm takes the shared check's distance terms (SC8, RA2) and spends its shot
// (RA3). The half-elf (Perception 8, so 4 free tiles) wields an invented bow of range 10 using arrows (type 0, one a shot); the rat
// is AttackStateTests' small animal. Values are invented.
[TestClass]
public sealed class RangedAttackStateTests
{
    private const int Impossible = 1_000_000;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static EquipmentItem Weapon(CombatSkill skill, int range = 10, int? ammoType = 0, int consumption = 1) => new()
    {
        Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
        Weapon = new EquipmentWeapon
        {
            Skill = skill, Damage = DamageRanges.None with { Normal = new(2, 8) }, SpeedFactor = 10, Range = range,
            AmmoType = ammoType, AmmoConsumption = consumption,
        },
    };

    private static GameSessionOwner Owner(EquipmentItem weapon, int arrows = 40)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = new SessionMapInventorySeed([], arrows == 0 ? [] :
            [new SessionSeedItem("arrows", new(new(SessionItemType.Ammo, 7058, 100, 200), 1, 0, 0, arrows, []) { AmmoType = 0 }, new CellCoord(101, 200))]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        if (arrows > 0) Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "arrows", Accept).Disposition);
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, weapon),
            (_, _) => true, Accept).Failure);
        return owner;
    }

    // Hit points enough for every shot of a test.
    private static SessionCritterCombat Rat() => SessionCritterCombat.From("oid:rat", AttackStateTests.Npc(hpAdjustment: 5000)).Combat!;

    [TestMethod]
    public void ABowAndAFirearmAreSupportedAndTakeTheRangedPath()
    {
        foreach (var skill in new[] { CombatSkill.Bow, CombatSkill.Firearms })
        {
            using var owner = Owner(Weapon(skill));
            var hero = owner.QueryHeroAttack()!;
            Assert.AreEqual((skill, 10, true, true, true), (hero.Skill, hero.Range, hero.Supported, hero.RangedSkill, hero.RangedPath));
            var shot = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 3 });
            Assert.AreEqual((GameSessionFailure.None, AttackRefusal.None), (shot.Failure, shot.Resolution!.Refusal));
            Assert.AreEqual(0, shot.Resolution.Difficulty!.Range, "3 tiles are within half the Perception");
        }
        // A melee weapon keeps its reach and takes no distance term; a reach weapon takes the ranged path without them (RA1, RA2).
        using var melee = Owner(Weapon(CombatSkill.Melee, range: 1, ammoType: null), arrows: 0);
        Assert.AreEqual((true, false, false), (melee.QueryHeroAttack()!.Supported, melee.QueryHeroAttack()!.RangedSkill, melee.QueryHeroAttack()!.RangedPath));
        Assert.AreEqual(GameSessionFailure.InvalidAttack, melee.AttackCritter(melee.Version, new(Rat()) { Distance = 2 }).Failure);
        using var reach = Owner(Weapon(CombatSkill.Melee, range: 15, ammoType: null), arrows: 0);
        Assert.AreEqual((false, true), (reach.QueryHeroAttack()!.RangedSkill, reach.QueryHeroAttack()!.RangedPath));
        var far = reach.AttackCritter(reach.Version, new(Rat()) { Distance = 12, LineOfFire = new(20, true) });
        Assert.AreEqual((GameSessionFailure.None, 0, 0), (far.Failure, far.Resolution!.Difficulty?.Range ?? 0, far.Resolution.Difficulty?.Cover ?? 0),
            "a melee-skill hit at reach has no range limit, penalty or line-of-fire term");
    }

    [TestMethod]
    public void TheShotPaysFivePerTileBeyondHalfThePerceptionAndBeyondItsRangeIsImpossible()
    {
        using var owner = Owner(Weapon(CombatSkill.Bow));
        var seven = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 7 });
        Assert.AreEqual(5 * (7 - 8 / 2), seven.Resolution!.Difficulty!.Range);
        var ten = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 10 });
        Assert.AreEqual(5 * (10 - 4), ten.Resolution!.Difficulty!.Range, "at the range itself only the penalty");
        // Beyond the range the session still runs the shot (the target may have moved since the goal tested its reach); the
        // check is impossible, so it misses, and its arrow is spent.
        var before = owner.QueryHeroAmmo().Single().Units;
        var beyond = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 11 });
        Assert.AreEqual((GameSessionFailure.None, Impossible + 5 * (11 - 4)), (beyond.Failure, beyond.Resolution!.Difficulty!.Range));
        Assert.IsFalse(beyond.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit);
        Assert.AreEqual(before - 1, owner.QueryHeroAmmo().Single().Units);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(Rat()) { Distance = AttackResolver.MaxMagnitude + 1 }).Failure);
    }

    [TestMethod]
    public void TheLineOfFireAddsItsCoverOrMakesTheShotImpossible()
    {
        using var owner = Owner(Weapon(CombatSkill.Firearms));
        var covered = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 2, LineOfFire = new(20, false) });
        Assert.AreEqual((0, 20), (covered.Resolution!.Difficulty!.Range, covered.Resolution.Difficulty.Cover));
        var blocked = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 2, LineOfFire = new(20, true) });
        Assert.AreEqual((Impossible, 0), (blocked.Resolution!.Difficulty!.Range, blocked.Resolution.Difficulty.Cover));
        Assert.IsFalse(blocked.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.AttackCritter(owner.Version, new(Rat()) { LineOfFire = new(-1, false) }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack,
            owner.AttackCritter(owner.Version, new(Rat()) { LineOfFire = new(AttackResolver.MaxMagnitude + 1, false) }).Failure);
    }

    [TestMethod]
    public void EachShotSpendsItsArrowAndTheLastMissingArrowRefuses()
    {
        using var owner = Owner(Weapon(CombatSkill.Bow, consumption: 2), arrows: 5);
        for (var left = 3; left >= 1; left -= 2)
        {
            var shot = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 4 });
            Assert.AreEqual((GameSessionDisposition.Changed, left), (shot.Ammunition!.Value.Disposition, shot.Ammunition.Value.UnitsAfter));
        }
        var refused = owner.AttackCritter(owner.Version, new(Rat()) { Distance = 4 });
        Assert.AreEqual((AttackRefusal.OutOfAmmunition, (SessionAmmoUse?)null), (refused.Resolution!.Refusal, refused.Ammunition));
        Assert.AreEqual(1, owner.QueryHeroAmmo().Single().Units);
    }

    [TestMethod]
    public void AShotIntoAWallSpendsItsArrowAndDamagesNothing()
    {
        // ARC-COMBAT-015 (ledger RH5): the line of fire struck a wall; the shot is hit without a skill roll and the wall takes nothing.
        using var owner = Owner(Weapon(CombatSkill.Bow), arrows: 3);
        var draws = owner.QueryRandom().Draws;
        var wall = owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2 });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, GameSessionFailure.None), (wall.Disposition, wall.Failure, wall.StepFailure));
        Assert.IsNull(wall.Resolution!.Difficulty, "no skill roll");
        Assert.AreEqual((AttackOutcome.Hit, DamageRecipient.Target, DamagePrevention.Wall),
            (wall.Resolution.Outcome, wall.Resolution.Damage!.Recipient, wall.Resolution.Damage.Prevented));
        Assert.AreEqual((2, (ObjectHitOutcome?)null, (SessionAttackWearResult?)null, SessionObjectHostSteps.None),
            (wall.Ammunition!.Value.UnitsAfter, wall.Object, wall.Wear, wall.Host));
        Assert.IsTrue(owner.QueryRandom().Draws > draws, "the shot's draws are spent");
        var crate = new SessionObjectTarget(Exterior, "sector:1:9", new SessionObjectBaseline { Kind = DamageableObjectKind.Scenery, Points = 1000, Material = ObjectMaterial.Cloth });
        Assert.AreEqual(GameSessionFailure.InvalidObject, owner.AttackObject(owner.Version, new() { Wall = true, Target = crate }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidObject, owner.AttackObject(owner.Version, new()).Failure);
        // The last arrows go the same way, and then the wall's shot is refused as any other.
        Assert.AreEqual(1, owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2 }).Ammunition!.Value.UnitsAfter);
        Assert.AreEqual(0, owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2 }).Ammunition!.Value.UnitsAfter);
        var refused = owner.AttackObject(owner.Version, new() { Wall = true, Distance = 2 });
        Assert.AreEqual((AttackRefusal.OutOfAmmunition, (SessionAmmoUse?)null), (refused.Resolution!.Refusal, refused.Ammunition));
    }
}
