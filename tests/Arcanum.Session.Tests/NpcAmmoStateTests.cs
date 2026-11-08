using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-283 (RA3, AM4, 0x4B65D0): an NPC led by a PC spends its bow's ammunition from its own stack at the shot, all or nothing, and a shot without
// enough is refused; an NPC with no PC leader spends nothing. Values are invented.
[TestClass]
public sealed class NpcAmmoStateTests
{
    private const string Archer = "oid:archer", Arrows = "archer-arrows";

    private static CombatFields BowFields() =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(5, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 1, 1, 0, 0, 0, (int)EquipmentSlot.Weapon, [10000, 10000, 10000, 10000, 10000], 0, 0, CombatItemArt.ItemArtType << 28, 0),
            new CombatWeaponFields(0, CombatWeaponSkill.Bow, 0, 0, [new(2, 6), new(0, 0), new(0, 0), new(0, 0), new(0, 0)], [0, 0, 0, 0, 0], 10, 0, 12, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0),
            null, null, null, null, null);

    private static (GameSessionOwner Owner, SessionCritterCombat Archer) Open(bool companion, int units)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules() }).Owner!;
        var arrows = new SessionItemFacts(new(SessionItemType.Ammo, 1500, 1500, 31500), 1, 0, 0, units, []) { AmmoType = 0 };
        var seed = new SessionMapInventorySeed([new(Archer, SessionHolderKind.Npc, false, false, [], ["archer-bow", Arrows])],
            [new SessionSeedItem("archer-bow", InventoryStateTests.Facts(SessionItemType.Weapon, 2901, (int)EquipmentSlot.Weapon), null), new SessionSeedItem(Arrows, arrows, null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        if (companion) Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Archer).Disposition);
        var build = SessionCritterCombat.From(Archer, Npc(), [BowFields()]);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return (owner, build.Combat!);
    }

    private static int Units(GameSessionOwner owner) => owner.QueryAmmo(Arrows)?.Units ?? 0;

    [TestMethod]
    public void ACompanionArcherSpendsItsOwnArrowsPerShotAndIsRefusedWithoutEnough()
    {
        var (owner, archer) = Open(companion: true, units: 5);
        var shoot = new SessionNpcAttack(archer) { Distance = 6, LineOfFire = new(0, false) };
        var first = owner.AttackByNpc(owner.Version, shoot);
        Assert.AreEqual(GameSessionDisposition.Changed, first.Disposition, "a spent shot changes the session");
        Assert.AreEqual(3, Units(owner), "the weapon takes two arrows a shot (consumption 2)");
        Assert.AreEqual(3, first.Ammunition!.Value.UnitsAfter);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttackByNpc(owner.Version, shoot).Disposition);
        Assert.AreEqual(1, Units(owner));
        var draws = owner.QueryRandom().Draws;
        var refused = owner.AttackByNpc(owner.Version, shoot);
        Assert.AreEqual((GameSessionDisposition.NoChange, AttackRefusal.OutOfAmmunition), (refused.Disposition, refused.Resolution!.Refusal), "one arrow is too few for a shot of two");
        Assert.AreEqual(1, Units(owner), "all or nothing");
        Assert.IsNull(refused.Ammunition);
        Assert.IsTrue(owner.QueryRandom().Draws >= draws);
    }

    [TestMethod]
    public void AnArcherWithNoPcLeaderSpendsNothingAndIsNeverRefused()
    {
        var (owner, archer) = Open(companion: false, units: 1);
        var shoot = new SessionNpcAttack(archer) { Distance = 6, LineOfFire = new(0, false) };
        for (var i = 0; i < 20; i++)
        {
            var result = owner.AttackByNpc(owner.Version, shoot);
            Assert.AreNotEqual(AttackRefusal.OutOfAmmunition, result.Resolution?.Refusal);
            Assert.IsNull(result.Ammunition);
        }
        Assert.AreEqual(1, Units(owner));
    }
}
