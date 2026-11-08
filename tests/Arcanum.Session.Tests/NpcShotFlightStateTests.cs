using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-282 (PF1, PF2, PF3): an NPC's bow attack with a projectile art flies: the check is decided at the shot, the damage waits for the strike
// (LandNpcShot): the shot's own target takes the carried hit, another one a forced hit, a miss does not strike its own target. Values are invented.
[TestClass]
public sealed class NpcShotFlightStateTests
{
    internal const string Archer = "oid:archer", Bow = "archer-bow", Other = "oid:other";
    private const uint Art = 0x10000;
    private static readonly ItemCondition Sturdy = new(1000, 0, 0, false);

    internal static CombatFields BowFields() =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(5, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 1, 1, 0, 0, 0, (int)EquipmentSlot.Weapon, [10000, 10000, 10000, 10000, 10000], 0, 0, CombatItemArt.ItemArtType << 28, 0),
            new CombatWeaponFields(0, CombatWeaponSkill.Bow, 0, 0, [new(2, 6), new(0, 0), new(0, 0), new(0, 0), new(0, 0)], [0, 0, 0, 0, 0], 10, 0, 12, 0, 0, 0, 10000, 0, 0, 0, 0, 0, 0, 0)
            { MissileArt = unchecked((int)Art) }, null, null, null, null, null);

    internal static SessionCritterCombat Combat(string entity, CombatFields? carried = null)
    {
        var build = SessionCritterCombat.From(entity, Npc(), carried is null ? null : [carried]);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    // The archer and its bow, the map's seed (a restored session takes it again, as the host attaches it at a load).
    internal static SessionMapInventorySeed Seed() =>
        new([new(Archer, SessionHolderKind.Npc, false, false, [], [Bow])],
            [new SessionSeedItem(Bow, InventoryStateTests.Facts(SessionItemType.Weapon, 2901, (int)EquipmentSlot.Weapon), null)]);

    internal static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    internal static Dictionary<EquipmentSlot, SessionWearItem> Worn(bool art = true) =>
        new() { [EquipmentSlot.Weapon] = new SessionWearItem(Bow, Sturdy) { MissileArt = art ? Art : null } };

    internal static SessionNpcAttack Shoot(SessionCritterCombat archer, bool art = true, bool flight = true, SessionCritterCombat? target = null) =>
        new(archer) { Target = target, Distance = 6, LineOfFire = new(0, false), AttackerWorn = Worn(art), Flight = flight };

    // The next shot that is decided as wanted: a hit, or a miss.
    internal static (SessionAttackResult Result, SessionNpcShotFacts Facts) Next(GameSessionOwner owner, SessionNpcAttack shoot, bool hit)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var result = owner.AttackByNpc(owner.Version, shoot);
            if (result.Shot is not { } shot) continue;
            var facts = owner.QueryNpcShot(shot)!.Value;
            if (facts.Hit == hit) return (result, facts);
            Assert.IsTrue(owner.EndShot(shot));
        }
        Assert.Fail("no such shot within the bounded run");
        throw new InvalidOperationException();
    }

    [TestMethod]
    public void ABowWithAProjectileArtFliesAndItsDamageWaitsForTheStrike()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var life = owner.QueryHeroLife()!.Status.HitPoints;
        var (shot, facts) = Next(owner, Shoot(archer), hit: true);
        Assert.AreEqual((GameSessionFailure.None, null), (shot.Failure, shot.Life), "the check is decided; no damage yet");
        Assert.AreEqual(1, shot.Shots.Count);
        Assert.AreEqual((Archer, true, 12), (facts.Shooter, facts.Hit, facts.Range));
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count);
        Assert.AreEqual(life, owner.QueryHeroLife()!.Status.HitPoints);

        var strike = owner.LandNpcShot(owner.Version, shot.Shot!.Value, new(archer) { AttackerWorn = Worn() });
        Assert.AreEqual(GameSessionFailure.None, strike.Failure);
        Assert.AreEqual(DamageRecipient.Target, strike.Recipient);
        Assert.IsTrue(strike.Life!.HitPointDamage > 0);
        Assert.IsTrue(owner.QueryHeroLife()!.Status.HitPoints < life);
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count, "the shot ends with its strike");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShot(owner.Version, shot.Shot.Value, new(archer) { AttackerWorn = Worn() }).Failure, "a second strike is refused");
    }

    [TestMethod]
    public void AMissedShotDoesNotStrikeItsOwnTargetButHitsAnotherCritterForcedWithItsWeapon()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var other = Combat(Other);
        var (shot, facts) = Next(owner, Shoot(archer), hit: false);
        Assert.IsFalse(facts.Hit);
        var id = shot.Shot!.Value;
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShot(owner.Version, id, new(archer) { AttackerWorn = Worn() }).Failure, "the hero was the target of the missed shot");
        Assert.AreEqual(1, owner.QueryShotsInFlight().Count, "a refused strike leaves the shot in flight");
        var stray = owner.LandNpcShot(owner.Version, id, new(archer) { Target = other, AttackerWorn = Worn() });
        Assert.AreEqual(GameSessionFailure.None, stray.Failure);
        Assert.IsTrue(stray.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit, "a stray strike is a forced hit");
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void AShotOfAnotherShooterOrOfNoShooterIsRefused()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var (shot, _) = Next(owner, Shoot(archer), hit: true);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShot(owner.Version, shot.Shot!.Value, new(Combat(Other, BowFields())) { AttackerWorn = Worn() }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.LandNpcShot(owner.Version, new SessionShot(999), new(archer) { AttackerWorn = Worn() }).Failure);
        Assert.IsNull(owner.QueryNpcShot(new SessionShot(999)));
    }

    [TestMethod]
    public void WithoutAnArtOrAFlyingHostTheAttackLandsAtOnce()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        foreach (var shoot in new[] { Shoot(archer, art: false), Shoot(archer, flight: false) })
        {
            var hit = false;
            for (var attempt = 0; attempt < 600 && !hit; attempt++)
            {
                var result = owner.AttackByNpc(owner.Version, shoot);
                Assert.AreEqual(0, result.Shots.Count);
                hit = result.Life is { HitPointDamage: > 0 };
            }
            Assert.IsTrue(hit, "the arrow lands at once");
        }
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void AFullShotBudgetLandsTheAttackAtOnce()
    {
        using var owner = Owner();
        var archer = Combat(Archer, BowFields());
        var shoot = Shoot(archer);
        var flying = 0;
        for (var attempt = 0; attempt < 400 && flying < GameSessionOwner.MaxShotsInFlight; attempt++)
            if (owner.AttackByNpc(owner.Version, shoot).Shot is not null) flying++;
        Assert.AreEqual(GameSessionOwner.MaxShotsInFlight, flying);
        var full = owner.AttackByNpc(owner.Version, shoot);
        Assert.AreEqual(0, full.Shots.Count, "a full shot budget lands the attack at once");
        Assert.AreNotEqual(GameSessionDisposition.Rejected, full.Disposition);
    }
}
