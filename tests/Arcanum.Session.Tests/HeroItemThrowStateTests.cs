using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-023: the hero throws one of its items at a location, as the inventory throws it (ledger TT rows, TW rows, WB8). The
// half-elf's kit weighs 15 (the weapon), 150 and 0 (the two armours); the rats are AttackStateTests' small animal. Values are invented.
[TestClass]
public sealed class HeroItemThrowStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly EquipmentItem Sword = new()
    {
        Kind = EquipmentItemKind.Weapon, HitPoints = 20, MaxHitPoints = 20,
        Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(2, 6) }, SpeedFactor = 10, Range = 1 },
    };

    private static GameSessionOwner Owner() => GameSessionOwner.Create(Descriptor() with
    {
        Hero = HalfElf() with { Kit = [new(Kit[0].Kind) { Weight = new(15) }, new(Kit[1].Kind) { Weight = new(150) }, new(Kit[2].Kind)] },
    }).Owner!;

    private static SessionCritterCombat Rat() => SessionCritterCombat.From("oid:rat", AttackStateTests.Npc(hpAdjustment: 5000)).Combat!;

    private static CellCoord Here(GameSessionOwner owner, int dx = 0) => new(owner.QueryActor().Cell.X + dx, owner.QueryActor().Cell.Y);

    private static SessionGridMoveItem Armour(int index = 1, bool noDrop = false, params int[] points) => new(Kit[index].Kind, noDrop, points);

    private static SessionHeroThrow AtRat(GameSessionOwner owner, string item = "kit:2", SessionGridMoveItem? facts = null) =>
        new(item, facts ?? Armour(), Here(owner, 3)) { Struck = Rat(), StruckCell = Here(owner, 3) };

    private static bool Holds(GameSessionOwner owner, string item) => owner.QueryHeroKit().Any(held => held.Entity == item);

    // Throws the armour at the rat until the outcome is a hit (or a miss), picking it up again after each other throw.
    private static SessionHeroThrowResult ThrowUntil(GameSessionOwner owner, bool hit)
    {
        for (var i = 0; i < 80; i++)
        {
            var thrown = owner.ThrowHeroItem(owner.Version, AtRat(owner));
            Assert.AreEqual(GameSessionFailure.None, thrown.Failure);
            var outcome = thrown.Attack!.Resolution!.Outcome;
            if ((outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit) == hit && outcome != AttackOutcome.CriticalMiss) return thrown;
            Assert.IsTrue(owner.EndShot(thrown.Shots[0]));
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "kit:2", Accept).Disposition);
        }
        Assert.Fail("no such throw");
        return null!;
    }

    [TestMethod]
    public void APackItemLeavesTheHeroAndFliesAtItsThrownRangeWhileTheWeaponStaysInTheHand()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Sword),
            (_, _) => true, Accept).Failure);
        var strength = owner.QueryHeroAttack()!.Combatant.Strength;
        var range = ThrowRules.Range(strength, SkillTraining.None, 150);
        Assert.AreEqual(range, owner.QueryHeroThrowRange("kit:2"));
        var revision = owner.Version.Revision;
        var thrown = owner.ThrowHeroItem(owner.Version, AtRat(owner));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (thrown.Disposition, thrown.Failure));
        Assert.IsTrue(owner.Version.Revision > revision);
        Assert.IsFalse(Holds(owner, "kit:2"), "the item left the hero");
        Assert.IsTrue(owner.QueryEquipment().Worn.Any(worn => worn.Entity == "kit:1"), "the wielded weapon stays in the hand");
        Assert.AreEqual(("kit:2", owner.QueryActor().Cell), (owner.QueryPlacedItems().Single().Entity, owner.QueryPlacedItems().Single().Cell));
        Assert.AreEqual(0, owner.QueryItemDecays().Count, "the throw's placement schedules no decay");
        Assert.AreEqual(new SessionThrownShot("kit:2", range), owner.QueryThrownShot(thrown.Shots[0]));
        Assert.IsTrue(owner.QueryItemInFlight("kit:2"));
        Assert.AreEqual(0, thrown.Attack!.Resolution!.Difficulty!.Range, "within the thrown range and half the Perception: no range penalty");
    }

    [TestMethod]
    public void AHitStrikesWithTheDamageOfTheItemsWeightAndTheItemLands()
    {
        using var owner = Owner();
        var rat = Rat();
        var before = owner.QueryLife(rat.Target).View!.Status.HitPoints;
        var thrown = ThrowUntil(owner, hit: true);
        var struck = owner.LandShot(owner.Version, thrown.Shots[0], new(rat));
        Assert.AreEqual(GameSessionFailure.None, struck.Failure);
        Assert.AreEqual(before - struck.Life!.HitPointDamage, owner.QueryLife(rat.Target).View!.Status.HitPoints);
        Assert.IsTrue(struck.Resolution!.Damage!.Rolled.Normal is >= 2 and <= 3,
            $"150 weighs 2 .. 3 normal damage (WB8) without the strength bonus (melee only), rolled {struck.Resolution.Damage.Rolled.Normal}");
        Assert.AreEqual(0, struck.Resolution.Damage.Rolled.Fire + struck.Resolution.Damage.Rolled.Poison + struck.Resolution.Damage.Rolled.Electrical);
        var cell = Here(owner, 3);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, thrown.Shots[0], cell).Disposition);
        Assert.AreEqual((cell, 0, 0), (owner.QueryPlacedItems().Single().Cell, owner.QueryShotsInFlight().Count, owner.QueryItemDecays().Count));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "kit:2", Accept).Disposition);
    }

    [TestMethod]
    public void AMissScattersFromTheLocationAndTheShotHasNoTarget()
    {
        using var owner = Owner();
        var miss = ThrowUntil(owner, hit: false);
        Assert.IsNotNull(miss.Destination, "a missed throw scatters from the location (RH2)");
        Assert.AreEqual(miss.Attack!.Scatter, miss.Destination);
        var stray = owner.LandShot(owner.Version, miss.Shots[0], new(Rat()));
        Assert.AreEqual((GameSessionFailure.None, AttackOutcome.Hit), (stray.Failure, stray.Resolution!.Outcome), "a stray strike is a forced hit");
    }

    [TestMethod]
    public void AThrowAtAnEmptyTileFliesThereUnrolled()
    {
        using var owner = Owner();
        var draws = owner.QueryRandom().Draws;
        var target = Here(owner, 4);
        var thrown = owner.ThrowHeroItem(owner.Version, new("kit:3", Armour(2), target));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, target), (thrown.Disposition, thrown.Failure, thrown.Destination));
        Assert.AreEqual((1, (AttackResolution?)null, draws), (thrown.Shots.Count, thrown.Attack!.Resolution, owner.QueryRandom().Draws));
        Assert.AreEqual(ThrowRules.Range(owner.QueryHeroAttack()!.Combatant.Strength, SkillTraining.None, 0), owner.QueryThrownShot(thrown.Shots[0])!.Value.Range);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, thrown.Shots[0], target).Disposition);
        Assert.AreEqual(target, owner.QueryPlacedItems().Single().Cell);
        Assert.AreEqual(0, owner.QueryItemDecays().Count);
        // A snapshot taken meanwhile keeps a thrown item on the hero's cell, as ARC-COMBAT-017's.
        var again = owner.ThrowHeroItem(owner.Version, new("kit:2", Armour(), target));
        using var restored = GameSessionOwner.Restore(owner.Capture(), RestoreOptions()).Owner!;
        Assert.AreEqual((0, owner.QueryActor().Cell), (restored.QueryShotsInFlight().Count, restored.QueryPlacedItems().Single(p => p.Entity == "kit:2").Cell));
        Assert.IsTrue(owner.EndShot(again.Shots[0]));
    }

    [TestMethod]
    public void AWornWeaponIsThrownFromItsSlotWithTheDamageOfItsWeight()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, new(Kit[0].Kind, [], false, Sword),
            (_, _) => true, Accept).Failure);
        var thrown = owner.ThrowHeroItem(owner.Version, new("kit:1", new(Kit[0].Kind, false, []), Here(owner, 2)));
        Assert.AreEqual(GameSessionFailure.None, thrown.Failure);
        Assert.IsFalse(owner.QueryEquipment().Worn.Any(worn => worn.Slot == EquipmentSlot.Weapon), "it left the hand");
        Assert.AreEqual(ThrowRules.Range(owner.QueryHeroAttack()!.Combatant.Strength, SkillTraining.None, 15), owner.QueryThrownShot(thrown.Shots[0])!.Value.Range);
    }

    [TestMethod]
    public void ARefusedThrowKeepsTheItem()
    {
        using var owner = Owner();
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.ItemNotDroppable, owner.ThrowHeroItem(owner.Version, AtRat(owner, facts: Armour(noDrop: true))).Failure,
            "the remove check (0x466DA0)");
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported,
            owner.ThrowHeroItem(owner.Version, AtRat(owner, facts: Armour(1, false, SessionScriptPoint.Throw))).Failure);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported,
            owner.ThrowHeroItem(owner.Version, AtRat(owner, facts: Armour(1, false, SessionScriptPoint.RemoveItem))).Failure);
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, owner.ThrowHeroItem(owner.Version, AtRat(owner, facts: Armour(2))).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.ThrowHeroItem(owner.Version, AtRat(owner, "kit:9")).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.ThrowHeroItem(owner.Version, AtRat(owner, "kit:1", new(Kit[0].Kind, false, []))).Failure,
            "a listed weapon needs its combat facts");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.ThrowHeroItem(owner.Version, AtRat(owner) with { Combat = Sword }).Failure,
            "an armour has no weapon block");
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.ThrowHeroItem(owner.Version, AtRat(owner) with { StruckCell = null }).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAttack, owner.ThrowHeroItem(owner.Version, AtRat(owner) with { Target = new CellCoord(-1, 0) }).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ThrowHeroItem(owner.Version with { Revision = owner.Version.Revision + 1 }, AtRat(owner)).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsTrue(Holds(owner, "kit:2"));
        var dead = owner.ApplyDamage(owner.Version, Rat().Target, new LifeDamage { HitPoints = 100_000 });
        Assert.AreEqual(GameSessionDisposition.Changed, dead.Disposition);
        var corpse = owner.ThrowHeroItem(owner.Version, AtRat(owner));
        Assert.AreEqual((GameSessionDisposition.NoChange, AttackRefusal.TargetDead, 0), (corpse.Disposition, corpse.Attack!.Resolution!.Refusal, corpse.Shots.Count));
        Assert.IsTrue(Holds(owner, "kit:2"), "a refused attack throws nothing");
    }

    [TestMethod]
    public void AThrowCostsTheHeroTwoInSteamwoven()
    {
        using var owner = Owner();
        Assert.AreEqual(TurnBasedRules.ThrowActionCost(localPc: true, CombatAnomalyProfile.Steamwoven), owner.QueryHeroThrowCost());
        Assert.IsNull(owner.QueryHeroThrowRange("kit:9"));
    }
}
