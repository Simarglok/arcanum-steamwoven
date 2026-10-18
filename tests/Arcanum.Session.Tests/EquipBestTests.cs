using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero wields its best items (ARC-WORLD-034). The kit is the half-elf fixture's: kit:1 a weapon, kit:2 and kit:3 armour;
// item values are invented.
[TestClass]
public sealed class EquipBestTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionWearArtCheck Drawn = (_, _) => true;

    private static readonly EquipmentItem Knife = new()
    {
        Kind = EquipmentItemKind.Weapon,
        Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(10, 30) } },
    };

    private static EquipmentItem Coat(ArmorCoverage coverage = ArmorCoverage.Torso, ArmorSizes sizes = ArmorSizes.Medium) => new()
    {
        Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = coverage, Sizes = sizes, AcAdjustment = 5 },
    };

    private static SessionWearableItem Offer(int kit, EquipmentItem item, int worth = 10) =>
        new(Kit[kit - 1].Kind, [], false, item) { Worth = worth };

    private static SessionWearableLookup Lookup(params SessionWearableItem?[] kit) =>
        entity => entity.StartsWith("kit:", StringComparison.Ordinal) && int.Parse(entity[4..], System.Globalization.CultureInfo.InvariantCulture) is var n
            && n <= kit.Length ? kit[n - 1] : null;

    private static GameSessionOwner Owner() => CreateWith(HalfElf());

    private static (EquipmentSlot, string)[] Worn(GameSessionOwner owner) => owner.QueryEquipment().Worn.Select(w => (w.Slot, w.Entity)).ToArray();

    [TestMethod]
    public void TheStartWieldsTheKitInOneRevision()
    {
        using var owner = Owner();
        var lookup = Lookup(Offer(1, Knife), Offer(2, Coat()), Offer(3, Coat(ArmorCoverage.Ring, ArmorSizes.None)));
        var result = owner.EquipBest(owner.Version, lookup, Drawn, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 1L), (result.Disposition, result.Failure, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Ring1, "kit:3"), (EquipmentSlot.Weapon, "kit:1"), (EquipmentSlot.Armor, "kit:2") }, Worn(owner));
        CollectionAssert.AreEqual(new[]
        {
            new SessionEquipBestStep(EquipmentSlot.Ring1, null, "kit:3", EquipmentRefusal.None, null),
            new SessionEquipBestStep(EquipmentSlot.Weapon, null, "kit:1", EquipmentRefusal.None, null),
            new SessionEquipBestStep(EquipmentSlot.Armor, null, "kit:2", EquipmentRefusal.None, null),
        }, result.Steps.ToArray());

        // Again: the weapon comes off and goes back on, the rest keeps its worth; nothing changes.
        var again = owner.EquipBest(owner.Version, lookup, Drawn, Accept);
        Assert.AreEqual((GameSessionDisposition.NoChange, 1L), (again.Disposition, owner.Version.Revision));
        Assert.AreEqual(new SessionEquipBestStep(EquipmentSlot.Weapon, "kit:1", "kit:1", EquipmentRefusal.None, null), again.Steps.Single());
        Assert.AreEqual(5 + 5, owner.QueryHeroEquipmentCombat()!.ArmorClassAdjustment);
    }

    [TestMethod]
    public void AMoreValuableItemReplacesAKeptOne()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Offer(2, Coat()), Drawn, Accept).Disposition);
        var checkedItems = new List<string>();
        var result = owner.EquipBest(owner.Version, Lookup(null, Offer(2, Coat()), Offer(3, Coat(), worth: 50)), Drawn,
            item => { checkedItems.Add(item.Entity); return SessionInsertVerdict.Accepted; });
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Armor, "kit:3") }, Worn(owner));
        Assert.AreEqual(new SessionEquipBestStep(EquipmentSlot.Armor, "kit:2", "kit:3", EquipmentRefusal.None, null), result.Steps.Single());
        CollectionAssert.AreEqual(new[] { "kit:2" }, checkedItems.ToArray(), "the occupant goes back through the insert check");

        // A worth that does not beat the kept one changes nothing; an unidentified magickal item counts as 300.
        Assert.AreEqual(GameSessionDisposition.NoChange,
            owner.EquipBest(owner.Version, Lookup(null, Offer(2, Coat(), worth: 50), Offer(3, Coat(), worth: 50)), Drawn, Accept).Disposition);
        var magick = Coat() with { MagickTechComplexity = 10 };
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.EquipBest(owner.Version, Lookup(null, Offer(2, magick, worth: 1), Offer(3, Coat(), worth: 50)), Drawn, Accept).Disposition);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Armor, "kit:2") }, Worn(owner));
    }

    [TestMethod]
    public void IneligibleItemsAndRefusedStepsLeaveTheirSlots()
    {
        using var owner = Owner();
        // The art lookup refuses the weapon; armour of another size is never chosen.
        var result = owner.EquipBest(owner.Version, Lookup(Offer(1, Knife), Offer(2, Coat(sizes: ArmorSizes.Large))),
            (slot, _) => slot != EquipmentSlot.Weapon, Accept);
        Assert.AreEqual((GameSessionDisposition.NoChange, 0), (result.Disposition, result.Steps.Count));

        // The occupant has no room to go back to: the swap is refused and the call goes on.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Offer(2, Coat()), Drawn, Accept).Disposition);
        var full = owner.EquipBest(owner.Version, Lookup(Offer(1, Knife), Offer(2, Coat()), Offer(3, Coat(), worth: 50)), Drawn,
            item => item.Entity == "kit:2" ? SessionInsertVerdict.NoRoom : SessionInsertVerdict.Accepted);
        Assert.AreEqual(GameSessionDisposition.Changed, full.Disposition, "the knife is still wielded");
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "kit:1"), (EquipmentSlot.Armor, "kit:2") }, Worn(owner));
        Assert.AreEqual(new SessionEquipBestStep(EquipmentSlot.Armor, null, null, EquipmentRefusal.None, SessionInsertVerdict.NoRoom), full.Steps[1]);
    }

    [TestMethod]
    public void ANoDropWeaponStaysAndAnIneligibleKeptItemComesOff()
    {
        using var owner = Owner();
        var sticky = Knife with { NoDrop = true };
        // A no-drop item cannot be wielded (its remove check fails), so it is saved worn and restored.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon, Offer(1, Knife), Drawn, Accept).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace("\"noDrop\":false", "\"noDrop\":true", StringComparison.Ordinal);
        using var restored = Restore(Decode(text));
        var result = restored.EquipBest(restored.Version, Lookup(Offer(1, sticky)), Drawn, Accept);
        Assert.AreEqual(GameSessionDisposition.NoChange, result.Disposition);
        Assert.AreEqual(new SessionEquipBestStep(EquipmentSlot.Weapon, null, null, EquipmentRefusal.NotDroppable, null), result.Steps.Single());
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "kit:1") }, Worn(restored));

        // Crippled arms make a worn shield ineligible: it comes off.
        using var hurt = Owner();
        var shield = Coat(ArmorCoverage.Shield, ArmorSizes.None);
        Assert.AreEqual(GameSessionDisposition.Changed, hurt.Equip(hurt.Version, "kit:2", EquipmentSlot.Shield, Offer(2, shield), Drawn, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed,
            hurt.ApplyDamage(hurt.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 1, Effects = CriticalHitEffects.CrippleArm }).Disposition);
        var off = hurt.EquipBest(hurt.Version, Lookup(null, Offer(2, shield)), Drawn, Accept);
        Assert.AreEqual(new SessionEquipBestStep(EquipmentSlot.Shield, "kit:2", null, EquipmentRefusal.None, null), off.Steps.Single());
        Assert.AreEqual(0, hurt.QueryEquipment().Worn.Count);
    }

    [TestMethod]
    public void TheBestWeaponWinsATieGoesToTheLaterItem()
    {
        using var owner = Owner();
        var club = new SessionItemFacts(new(SessionItemType.Weapon, 6100, 4100, 6101), 1, 0, 0, null, []);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior,
            new([], [new SessionSeedItem("oid:club", club, new CellCoord(100, 200))])).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "oid:club", Accept).Disposition);
        SessionWearableItem? Club(string entity) => entity == "oid:club" ? new(club.Kind, [], false, Knife) : null;
        var kit = Lookup(Offer(1, Knife));
        var result = owner.EquipBest(owner.Version, entity => kit(entity) ?? Club(entity), Drawn, Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "oid:club") }, Worn(owner), "equal scores: the item taken later");
        var better = Knife with { Weapon = Knife.Weapon! with { Damage = DamageRanges.None with { Normal = new(20, 40) } } };
        owner.EquipBest(owner.Version, entity => entity == "kit:1" ? Offer(1, better) : Club(entity), Drawn, Accept);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Weapon, "kit:1") }, Worn(owner), "a higher score wins");
    }

    [TestMethod]
    public void RefusalsChangeNothing()
    {
        using var none = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.EquipmentUnavailable, none.EquipBest(none.Version, Lookup(), Drawn, Accept).Failure);

        using var owner = Owner();
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Offer(2, Coat()), Drawn, Accept).Disposition);
        void Refused(EquipBestResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure, 0), (result.Disposition, result.Failure, result.Steps.Count));
            Assert.AreEqual(1, owner.Version.Revision);
            CollectionAssert.AreEqual(new[] { (EquipmentSlot.Armor, "kit:2") }, Worn(owner));
        }
        Refused(owner.EquipBest(stale, Lookup(Offer(1, Knife)), Drawn, Accept), GameSessionFailure.StaleVersion);
        Refused(owner.EquipBest(owner.Version, Lookup(Offer(1, Knife with { Weapon = null }), Offer(2, Coat())), Drawn, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.EquipBest(owner.Version, Lookup(Offer(1, Knife) with { Weight = -1 }, Offer(2, Coat())), Drawn, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.EquipBest(owner.Version, Lookup(Offer(2, Knife), Offer(2, Coat())), Drawn, Accept), GameSessionFailure.InventoryMismatch);
        Refused(owner.EquipBest(owner.Version, Lookup(Offer(1, Knife)), Drawn, Accept), GameSessionFailure.InventoryMismatch);
        Refused(owner.EquipBest(owner.Version, Lookup(Offer(1, Knife), Offer(2, Coat(sizes: ArmorSizes.Medium | ArmorSizes.Small))), Drawn, Accept),
            GameSessionFailure.InventoryMismatch);
        bool Meddle(EquipmentSlot slot, SessionWearableItem item)
        {
            owner.Unequip(owner.Version, EquipmentSlot.Armor, Accept);
            return true;
        }
        var meddled = owner.EquipBest(owner.Version, Lookup(Offer(1, Knife), Offer(2, Coat())), Meddle, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (meddled.Disposition, meddled.Failure));
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count, "only the callback's own change");

        // A spell item that is not chosen does not matter.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EquipBest(owner.Version,
            Lookup(Offer(1, Knife), Offer(2, Coat(), worth: 50), Offer(3, Coat()) with { HasItemSpells = true }), Drawn, Accept).Disposition);
    }

    [TestMethod]
    public void AChosenItemWithAScriptOrSpellsIsNotWieldedAndTheCallGoesOn()
    {
        using var owner = Owner();
        var result = owner.EquipBest(owner.Version, Lookup(Offer(1, Knife) with { HasItemSpells = true },
            Offer(2, Coat()) with { ScriptPoints = [SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff] },
            Offer(3, Coat(ArmorCoverage.Helmet, ArmorSizes.None))), Drawn, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, 1L), (result.Disposition, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Helmet, "kit:3") }, Worn(owner));
        CollectionAssert.AreEqual(new[]
        {
            new SessionEquipBestStep(EquipmentSlot.Helmet, null, "kit:3", EquipmentRefusal.None, null),
            new SessionEquipBestStep(EquipmentSlot.Weapon, null, null, EquipmentRefusal.None, null) { Unsupported = "kit:1", Failure = GameSessionFailure.ItemEffectUnsupported },
            new SessionEquipBestStep(EquipmentSlot.Armor, null, null, EquipmentRefusal.None, null) { Unsupported = "kit:2", Failure = GameSessionFailure.ItemScriptUnsupported },
        }, result.Steps.ToArray());
    }
}
