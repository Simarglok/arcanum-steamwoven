using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-2415 (ledger TW8, 0x4B65D0): the dispatcher's ammunition check of a thrown weapon with an ammunition type. A PC's units of that type are compared
// with the weapon's consumption; enough are spent at the throw, fewer refuse it before any roll and the item stays on the thrower's tile switched off, with no
// give-back. An NPC with no PC up its leader chain passes at once. The hero's pack weapon is an invented bow (arrows, type 0) listed as kit:1; values are invented.
[TestClass]
public sealed class ThrowAmmunitionStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static EquipmentItem Bow(int? ammoType = 0, int consumption = 2) => new()
    {
        Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
        Weapon = new EquipmentWeapon { Skill = CombatSkill.Bow, Damage = DamageRanges.None with { Normal = new(2, 6) }, SpeedFactor = 10, Range = 10, AmmoType = ammoType, AmmoConsumption = consumption },
    };

    private static GameSessionOwner Hero(int arrows)
    {
        var owner = GameSessionOwner.Create(Descriptor() with
        {
            Hero = HalfElf() with { Kit = [new(Kit[0].Kind) { Weight = new(15) }, new(Kit[1].Kind) { Weight = new(150) }, new(Kit[2].Kind)] },
        }).Owner!;
        if (arrows > 0)
        {
            var seed = new SessionMapInventorySeed([],
                [new SessionSeedItem("arrows", new(new(SessionItemType.Ammo, 7058, 100, 200), 1, 0, 0, arrows, []) { AmmoType = 0 }, new CellCoord(101, 200))]);
            Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "arrows", Accept).Disposition);
        }
        return owner;
    }

    private static SessionCritterCombat Rat() => SessionCritterCombat.From("oid:rat", Npc(hpAdjustment: 5000)).Combat!;

    private static CellCoord Here(GameSessionOwner owner, int dx = 0) => new(owner.QueryActor().Cell.X + dx, owner.QueryActor().Cell.Y);

    private static SessionHeroThrow Throw(GameSessionOwner owner, EquipmentItem bow, bool atRat) =>
        new("kit:1", new(Kit[0].Kind, false, []), Here(owner, 3)) { Combat = bow, Struck = atRat ? Rat() : null, StruckCell = atRat ? Here(owner, 3) : null };

    private static int Arrows(GameSessionOwner owner) => owner.QueryHeroAmmo() is { Count: > 0 } stacks ? stacks.Single().Units : 0;

    [TestMethod]
    public void AThrownWeaponTakesItsConsumptionFromTheHeroStackAtTheThrow()
    {
        foreach (var atRat in new[] { true, false })
        {
            using var owner = Hero(arrows: 5);
            var thrown = owner.ThrowHeroItem(owner.Version, Throw(owner, Bow(), atRat));
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, false), (thrown.Disposition, thrown.Failure, thrown.AmmunitionShort), $"atRat={atRat}");
            Assert.AreEqual(3, Arrows(owner), "the bow takes two arrows a throw");
            Assert.AreEqual(3, thrown.Attack!.Ammunition!.Value.UnitsAfter, "the spend is the attack's");
            Assert.AreEqual(1, thrown.Shots.Count, "the throw goes on");
            Assert.IsFalse(owner.QueryHeroKit().Any(held => held.Entity == "kit:1"), "the item left the hero");
            Assert.IsTrue(owner.QueryItemOff(owner.QueryActor().Map, "kit:1", dataOff: false), "in flight it is switched off");
        }
    }

    [TestMethod]
    public void TheSpendIsAllOrNothingAndTheLastUnitsDeleteTheStack()
    {
        using var owner = Hero(arrows: 2);
        var thrown = owner.ThrowHeroItem(owner.Version, Throw(owner, Bow(), atRat: false));
        Assert.AreEqual((GameSessionFailure.None, false, 0), (thrown.Failure, thrown.AmmunitionShort, Arrows(owner)), "exactly the consumption is enough");
        Assert.IsTrue(thrown.Attack!.Ammunition!.Value.Removed);
    }

    [TestMethod]
    public void TooFewUnitsRefuseTheThrowBeforeAnyRollAndTheItemLiesSwitchedOffOnTheHeroTile()
    {
        foreach (var (arrows, atRat) in new[] { (1, true), (1, false), (0, true), (0, false) })
        {
            using var owner = Hero(arrows);
            var draws = owner.QueryRandom().Draws;
            var cell = owner.QueryActor().Cell;
            var thrown = owner.ThrowHeroItem(owner.Version, Throw(owner, Bow(), atRat));
            var label = $"arrows={arrows} atRat={atRat}";
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (thrown.Disposition, thrown.Failure, thrown.AmmunitionShort), label);
            Assert.IsNull(thrown.Attack, label + ": no attack was made");
            Assert.AreEqual(0, thrown.Shots.Count, label + ": nothing flies");
            Assert.AreEqual(draws + 1, owner.QueryRandom().Draws, label + ": only the hit location is rolled, before the check");
            Assert.AreEqual(arrows, Arrows(owner), label + ": nothing was spent");
            Assert.IsFalse(owner.QueryHeroKit().Any(held => held.Entity == "kit:1"), label + ": the item left the hero (no give-back)");
            Assert.AreEqual(("kit:1", cell), (owner.QueryPlacedItems().Single().Entity, owner.QueryPlacedItems().Single().Cell), label + ": it lies on the hero's tile");
            Assert.IsTrue(owner.QueryItemOff(owner.QueryActor().Map, "kit:1", dataOff: false), label + ": still switched off");
            Assert.AreEqual(0, owner.QueryShotsInFlight().Count, label);
        }
    }

    [TestMethod]
    public void AWieldedBowThrownWithTooFewArrowsLeavesTheHandAndLiesSwitchedOff()
    {
        using var owner = Hero(arrows: 1);
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Bow()), (_, _) => true, Accept).Failure);
        var thrown = owner.ThrowHeroItem(owner.Version, new("kit:1", new(Kit[0].Kind, false, []), Here(owner, 3)));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (thrown.Disposition, thrown.Failure, thrown.AmmunitionShort));
        Assert.IsFalse(owner.QueryEquipment().Worn.Any(worn => worn.Entity == "kit:1"), "the weapon left the hand");
        Assert.AreEqual(1, Arrows(owner));
        Assert.IsTrue(owner.QueryItemOff(owner.QueryActor().Map, "kit:1", dataOff: false));
        Assert.AreEqual(0, owner.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void AWeaponWithoutAnAmmunitionTypeOrConsumptionAsksNothing()
    {
        using var none = Hero(arrows: 0);
        var thrown = none.ThrowHeroItem(none.Version, Throw(none, Bow(ammoType: null), atRat: false));
        Assert.AreEqual((GameSessionFailure.None, false, 1), (thrown.Failure, thrown.AmmunitionShort, thrown.Shots.Count), "no ammunition type, no check");
        Assert.IsNull(thrown.Attack!.Ammunition);
        using var free = Hero(arrows: 0);
        var passes = free.ThrowHeroItem(free.Version, Throw(free, Bow(consumption: 0), atRat: true));
        Assert.AreEqual((GameSessionFailure.None, false, 1), (passes.Failure, passes.AmmunitionShort, passes.Shots.Count), "a consumption of 0 needs no unit");
        Assert.IsNull(passes.Attack!.Ammunition, "and spends none");
    }

    [TestMethod]
    public void AnAmmunitionTypeOutsideTheStackTypesNamesNoStack()
    {
        using var owner = Hero(arrows: 5);
        var thrown = owner.ThrowHeroItem(owner.Version, Throw(owner, Bow(ammoType: 7), atRat: false));
        Assert.IsTrue(thrown.AmmunitionShort, "a type outside 0..3 finds no stack, as for the wielded shot (AMA3)");
        Assert.AreEqual(5, Arrows(owner));
    }

    // ---- an NPC led by a PC ----

    private const string Thrower = NpcThrowStateTests.Thrower, Dagger = NpcThrowStateTests.Dagger, Quiver = "thrower-arrows";

    private static CombatFields ThrownBowFields(int consumption, CombatWeaponSkill skill = CombatWeaponSkill.Melee) =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(5, 5000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 15, 1, 0, 0, 0, (int)EquipmentSlot.Weapon, [10000, 10000, 10000, 10000, 10000], 0, 0, CombatItemArt.ItemArtType << 28, 0),
            new CombatWeaponFields(CombatWeaponFlags.DefaultThrows, skill, 0, 0, [new(2, 6), new(0, 0), new(0, 0), new(0, 0), new(0, 0)],
                [0, 0, 0, 0, 0], 10, 0, 1, 0, 0, 0, 0, consumption, 0, 0, 0, 0, 0, 0), null, null, null, null, null);

    private static SessionCritterCombat NpcCombat(int consumption, CombatWeaponSkill skill = CombatWeaponSkill.Melee)
    {
        var skills = new int[12];
        skills[(int)BasicCombatSkill.Throwing] = 5;
        var build = SessionCritterCombat.From(Thrower, Npc(skills: skills), [ThrownBowFields(consumption, skill)]);
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static GameSessionOwner NpcOwner(bool companion, int units, ulong seed = 1)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), RandomSeed = seed }).Owner!;
        var arrows = new SessionItemFacts(new(SessionItemType.Ammo, 1500, 1500, 31500), 1, 0, 0, units, []) { AmmoType = 0 };
        var seedItems = new SessionMapInventorySeed([new(Thrower, SessionHolderKind.Npc, false, true, [], [Dagger, Quiver])],
            [new(Dagger, Facts(SessionItemType.Weapon, 2001, location: 1004), null), new(Quiver, arrows, null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seedItems).Failure);
        if (companion) Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, Thrower).Disposition);
        return owner;
    }

    private static SessionNpcThrow NpcThrow(int consumption, bool atHero, CombatWeaponSkill skill = CombatWeaponSkill.Melee) => new(NpcCombat(consumption, skill), Dagger, 15, NpcThrowStateTests.ThrowerCell, atHero ? NpcThrowStateTests.HeroCell : new CellCoord(95, 205))
    {
        StruckHero = atHero, StruckCell = atHero ? NpcThrowStateTests.HeroCell : null, ThrowerWorn = NpcThrowStateTests.Wielded(),
    };

    private static int Units(GameSessionOwner owner) => owner.QueryAmmo(Quiver)?.Units ?? 0;

    [TestMethod]
    public void ACompanionsThrownWeaponSpendsItsOwnArrowsAtTheThrow()
    {
        foreach (var atHero in new[] { true, false })
        {
            using var owner = NpcOwner(companion: true, units: 5);
            var thrown = owner.ThrowByNpc(owner.Version, NpcThrow(2, atHero));
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, false), (thrown.Disposition, thrown.Failure, thrown.AmmunitionShort), $"atHero={atHero}");
            Assert.AreEqual(3, Units(owner), "two arrows a throw");
            Assert.AreEqual(3, thrown.Attack!.Ammunition!.Value.UnitsAfter);
        }
    }

    [TestMethod]
    public void ACompanionWithTooFewArrowsThrowsNothingAndTheWeaponLiesSwitchedOffOnItsTile()
    {
        foreach (var atHero in new[] { true, false })
        {
            using var owner = NpcOwner(companion: true, units: 1);
            var draws = owner.QueryRandom().Draws;
            var thrown = owner.ThrowByNpc(owner.Version, NpcThrow(2, atHero));
            var label = $"atHero={atHero}";
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (thrown.Disposition, thrown.Failure, thrown.AmmunitionShort), label);
            Assert.AreEqual((0, 0), (thrown.Shots.Count, thrown.Landed is null ? 0 : 1), label + ": nothing flies or lands");
            Assert.AreEqual(draws + 1, owner.QueryRandom().Draws, label + ": only the hit location is rolled, before the check");
            Assert.AreEqual(1, Units(owner), label + ": nothing was spent");
            Assert.AreEqual(NpcThrowStateTests.ThrowerCell, NpcThrowStateTests.Lying(owner, Dagger), label + ": the weapon lies on the thrower's tile");
            Assert.IsTrue(owner.QueryItemOff(owner.QueryActor().Map, Dagger, dataOff: false), label + ": still switched off");
        }
    }

    [TestMethod]
    public void AThrowerWithNoPcLeaderPassesAtOnceAndSpendsNothing()
    {
        foreach (var atHero in new[] { true, false })
        {
            using var owner = NpcOwner(companion: false, units: 0);
            var thrown = owner.ThrowByNpc(owner.Version, NpcThrow(2, atHero));
            Assert.AreEqual((GameSessionFailure.None, false), (thrown.Failure, thrown.AmmunitionShort), $"atHero={atHero}");
            Assert.IsNull(thrown.Attack?.Ammunition);
            Assert.AreEqual(GameSessionDisposition.Changed, thrown.Disposition, "the throw went on");
        }
    }

    // ---- ARC-COMBAT-2699: the unrolled stream and a non-melee thrower ----

    [TestMethod]
    public void ACompanionsThrowOnAnExhaustedStreamStillSpendsItsArrows()
    {
        foreach (var flight in new[] { false, true })
        {
            using var owner = NpcOwner(companion: true, units: 5);
            Assert.IsTrue(owner.RestoreRandom(new(owner.QueryRandom().Seed, long.MaxValue)));
            var thrown = owner.ThrowByNpc(owner.Version, NpcThrow(2, atHero: false) with { Flight = flight });
            var label = $"flight={flight}";
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, false), (thrown.Disposition, thrown.Failure, thrown.AmmunitionShort), label);
            Assert.IsNull(thrown.Attack!.Resolution, label + ": nothing was rolled");
            Assert.AreEqual(GameSessionFailure.None, thrown.Attack.StepFailure, label);
            Assert.AreEqual(3, Units(owner), label + ": two arrows a throw");
            Assert.AreEqual(3, thrown.Attack.Ammunition!.Value.UnitsAfter, label);
            Assert.AreEqual(thrown.Version, owner.Version, label + ": the result names the version after the spend");
        }
    }

    [TestMethod]
    public void AnUnrolledThrowOfAThrowerWithNoPcLeaderSpendsNothing()
    {
        using var owner = NpcOwner(companion: false, units: 5);
        Assert.IsTrue(owner.RestoreRandom(new(owner.QueryRandom().Seed, long.MaxValue)));
        var thrown = owner.ThrowByNpc(owner.Version, NpcThrow(2, atHero: false));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (thrown.Disposition, thrown.Failure));
        Assert.IsNull(thrown.Attack);
        Assert.AreEqual(5, Units(owner));
    }

    [TestMethod]
    public void ACompanionWithTooFewArrowsRefusesAnUnrolledThrowAsWell()
    {
        using var owner = NpcOwner(companion: true, units: 1);
        Assert.IsTrue(owner.RestoreRandom(new(owner.QueryRandom().Seed, long.MaxValue)));
        var thrown = owner.ThrowByNpc(owner.Version, NpcThrow(2, atHero: false));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (thrown.Disposition, thrown.Failure, thrown.AmmunitionShort));
        Assert.AreEqual(1, Units(owner));
    }

    [TestMethod]
    public void ABowOrFirearmWieldedByACompanionIsThrownAndReachesTheAmmunitionCheck()
    {
        foreach (var skill in new[] { CombatWeaponSkill.Bow, CombatWeaponSkill.Firearms })
            foreach (var atHero in new[] { true, false })
            {
                var label = $"{skill}, atHero={atHero}";
                using var enough = NpcOwner(companion: true, units: 5);
                var thrown = enough.ThrowByNpc(enough.Version, NpcThrow(2, atHero, skill));
                Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, false), (thrown.Disposition, thrown.Failure, thrown.AmmunitionShort), label);
                Assert.AreEqual(3, Units(enough), label + ": two arrows a throw");

                using var short_ = NpcOwner(companion: true, units: 1);
                var refused = short_.ThrowByNpc(short_.Version, NpcThrow(2, atHero, skill));
                Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (refused.Disposition, refused.Failure, refused.AmmunitionShort), label);
                Assert.AreEqual(1, Units(short_), label + ": nothing was spent");
                Assert.IsTrue(short_.QueryItemOff(short_.QueryActor().Map, Dagger, dataOff: false), label + ": the weapon lies switched off");
            }
    }

    [TestMethod]
    public void AThrowingSkillWeaponAndABowOnAnExhaustedStreamBehaveAsTheirSupportAndAmmunitionSay()
    {
        foreach (var atHero in new[] { true, false })
        {
            using var boomerang = NpcOwner(companion: true, units: 5);
            var refused = boomerang.ThrowByNpc(boomerang.Version, NpcThrow(2, atHero, CombatWeaponSkill.Throwing));
            Assert.AreEqual(GameSessionFailure.AttackUnsupported, refused.Failure, $"atHero={atHero}: a throwing-skill weapon is no supported NPC attack");
            Assert.AreEqual(5, Units(boomerang));
        }
        using var owner = NpcOwner(companion: true, units: 5);
        Assert.IsTrue(owner.RestoreRandom(new(owner.QueryRandom().Seed, long.MaxValue)));
        var thrown = owner.ThrowByNpc(owner.Version, NpcThrow(2, atHero: false, CombatWeaponSkill.Bow));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (thrown.Disposition, thrown.Failure));
        Assert.AreEqual(3, Units(owner), "a bow thrown unrolled spends its arrows");
    }
}
