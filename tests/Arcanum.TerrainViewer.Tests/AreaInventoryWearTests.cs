using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-015: the inventory window's paper doll, wear and take-off over the session (invented values, no original data): a
/// hero whose kit torch the start wields, a torch taken from the ground, the doll lines, the wear slots, swap, take-off, refusals and
/// the weight line's facts.</summary>
[TestClass]
public sealed class AreaInventoryWearTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // A generic item (type 14) that uses the torch/shield location (generic flag 0x1), with every slot the combat-field reader needs.
    private static AreaObjectRecord Torch(int index) => new(Key(index), 0, 14, 15199, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null),
            Field(36, 0x1), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 0), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, 0x1), Field(60, 0), Field(61, 0),
        ], true, true, true);

    private static readonly SessionItemKind TorchKind = new(SessionItemType.Generic, 15199, 6000, 6001);

    // A torch (1) on the ground of map 1.
    private static AreaInventoryHost Host() => new([new AreaInventoryMap(1,
        new([], [new SessionSeedItem(Entity(1), new(TorchKind, 1, 0, 0, null, []), new CellCoord(10, 20))]),
        new[] { Torch(1) }.ToDictionary(row => Entity(row.Key.SourceIndex)))]);

    private static readonly EquipmentItem KitTorch = new() { Kind = EquipmentItemKind.Generic, UsesShieldLocation = true, HitPoints = 10, MaxHitPoints = 10 };
    private static readonly IReadOnlyList<SessionWearableItem?> KitWear = [new SessionWearableItem(TorchKind, [], false, KitTorch)];

    private static GameSessionOwner HeroSession()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(TorchKind)]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic", KitWear: KitWear);
        return AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
    }

    private static EquipmentMutationResult Wear(GameSessionOwner session, AreaInventoryHost host, string entity, EquipmentSlot slot) =>
        session.Equip(session.Version, entity, slot, host.Wearable(session, entity, KitWear)!, (_, _) => true, host.InsertCheck);

    [TestMethod]
    public void TheDollListsTheWornItemsAndWearSwapsTheOccupantBackToThePack()
    {
        using var session = HeroSession();
        var host = Host();
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        Assert.AreEqual(GameSessionDisposition.Changed, host.PickUp(session, Entity(1)).Disposition);

        var view = host.View(session, null, 1, kitWear: KitWear);
        CollectionAssert.AreEqual(Enum.GetValues<EquipmentSlot>(), view.Doll.Select(slot => slot.Slot).ToArray(), "the nine slots in slot order");
        Assert.AreEqual("kit:1", view.Doll.Single(slot => slot.Entity is not null).Entity, "the start wields the kit's torch");
        Assert.AreEqual(EquipmentSlot.Shield, view.Doll.Single(slot => slot.Entity is not null).Slot);
        Assert.AreEqual(0, view.Pc.Single(line => line.Entity == "kit:1").WearSlots.Count, "a worn item offers no slot");
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Shield }, view.Pc.Single(line => line.Entity == Entity(1)).WearSlots.ToArray(), "the taken torch's location");
        Assert.IsNotNull(view.Carry, "the hero's weight line");

        var swap = Wear(session, host, Entity(1), EquipmentSlot.Shield);
        Assert.AreEqual((GameSessionDisposition.Changed, "kit:1"), (swap.Disposition, swap.Displaced));
        StringAssert.Contains(AreaInventoryHost.WearText(swap, "torch", EquipmentSlot.Shield, false, entity => entity), "kit:1 went back to the pack");
        view = host.View(session, null, 1, kitWear: KitWear);
        Assert.AreEqual(Entity(1), view.Doll.Single(slot => slot.Slot == EquipmentSlot.Shield).Entity);
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Shield }, view.Pc.Single(line => line.Entity == "kit:1").WearSlots.ToArray(), "the kit torch can go back");

        var wrong = Wear(session, host, "kit:1", EquipmentSlot.Helmet);
        Assert.AreEqual((GameSessionFailure.WearRefused, EquipmentRefusal.WrongType), (wrong.Failure, wrong.Refusal));
        Assert.AreEqual("not worn: the item does not belong in that slot", AreaInventoryHost.WearText(wrong, "torch", EquipmentSlot.Helmet, false, entity => entity));

        var off = session.Unequip(session.Version, EquipmentSlot.Shield, host.InsertCheck);
        Assert.AreEqual(GameSessionDisposition.Changed, off.Disposition);
        Assert.AreEqual("took off torch", AreaInventoryHost.WearText(off, "torch", EquipmentSlot.Shield, true, entity => entity));
        Assert.IsTrue(host.View(session, null, 1, kitWear: KitWear).Doll.All(slot => slot.Entity is null), "nothing worn");
        var again = session.Unequip(session.Version, EquipmentSlot.Shield, host.InsertCheck);
        Assert.AreEqual("nothing worn as shield", AreaInventoryHost.WearText(again, "shield", EquipmentSlot.Shield, true, entity => entity));
    }

    [TestMethod]
    public void WithoutAHeroOrWearValuesNothingCanBeWorn()
    {
        using var heroless = ConversationSessionFixture.Create();
        var host = Host();
        host.Attach(heroless);
        host.PickUp(heroless, Entity(1));
        var view = host.View(heroless, null, 1, kitWear: KitWear);
        Assert.AreEqual(0, view.Doll.Count);
        Assert.IsNull(view.Carry);
        Assert.IsNull(host.Wearable(heroless, Entity(1), KitWear), "no hero to wear it");
        Assert.IsTrue(view.Pc.All(line => line.WearSlots.Count == 0));

        using var session = HeroSession();
        Assert.IsNull(host.Wearable(session, "kit:1", null), "no kit wear values without a hero load");
        Assert.IsNull(host.Wearable(session, "kit:2", KitWear), "not an item the hero holds");
        Assert.AreEqual(0, AreaInventoryHost.WearSlots(null).Count);
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Shield }, AreaInventoryHost.WearSlots(KitWear[0]).ToArray());
        Assert.AreEqual("ring 2", AreaInventoryHost.SlotName(EquipmentSlot.Ring2));
        var ring = new SessionWearableItem(new(SessionItemType.Armor, 15000, 6100, 6101), [], false,
            new EquipmentItem { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Ring }, HitPoints = 1, MaxHitPoints = 1 });
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Ring1, EquipmentSlot.Ring2 }, AreaInventoryHost.WearSlots(ring).ToArray(), "a ring offers both ring slots");
    }
    [TestMethod]
    public void ADragOnTheGridAndTheDollRunsTheSessionsMovesAndAnOccupantComesOntoTheCursor()
    {
        using var session = HeroSession();
        var host = Host();
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        Assert.AreEqual(GameSessionDisposition.Changed, host.PickUp(session, Entity(1)).Disposition);
        var view = host.View(session, null, 1, kitWear: KitWear);
        Assert.AreEqual((Entity(1), 0, 1, 1), view.Grid.Select(item => (item.Entity, item.Slot, item.Width, item.Height)).Single(), "the worn kit torch has no cell");

        // A grid item picked at its cell and released on another cell moves there.
        var held = AreaInventoryDrag.Press(view, new(0, null))!;
        Assert.AreEqual(new AreaInventoryHeld(Entity(1), null, 0), held);
        var drop = AreaInventoryDrag.Release(held, new(25, null));
        Assert.AreEqual((AreaInventoryDropKind.GridMove, (int?)25), (drop.Kind, drop.GridSlot));
        var facts = host.GridMoveItem(session, Entity(1), null)!;
        Assert.IsFalse(facts.NoDrop);
        var moved = session.MoveHeroItem(session.Version, Entity(1), 25, facts, host.InsertCheck);
        Assert.AreEqual("moved torch to row 3, column 6", AreaInventoryHost.GridText(moved, "torch", 25));
        view = host.View(session, null, 1, kitWear: KitWear);
        Assert.AreEqual(25, view.Grid.Single().Slot);

        // A worn item released on a taken cell stays worn; on a free cell it comes off there.
        held = AreaInventoryDrag.Press(view, new(null, EquipmentSlot.Shield))!;
        Assert.AreEqual(new AreaInventoryHeld("kit:1", EquipmentSlot.Shield, 0), held);
        drop = AreaInventoryDrag.Release(held, new(25, null));
        Assert.AreEqual(AreaInventoryDropKind.TakeOffTo, drop.Kind);
        var taken = session.Unequip(session.Version, EquipmentSlot.Shield, host.InsertCheck, gridSlot: 25);
        Assert.AreEqual("torch stayed where it was: no room for it at row 3, column 6", AreaInventoryHost.GridText(taken, "torch", 25));
        Assert.AreEqual(GameSessionDisposition.Changed, session.Unequip(session.Version, EquipmentSlot.Shield, host.InsertCheck, gridSlot: 0).Disposition);

        // The taken torch into the empty shield slot, then the kit torch onto it: the taken one lands in the first free cell, on the cursor.
        Assert.AreEqual(AreaInventoryDropKind.Wear, AreaInventoryDrag.Release(new(Entity(1), null, 0), new(null, EquipmentSlot.Shield)).Kind);
        Assert.AreEqual(GameSessionDisposition.Changed, Wear(session, host, Entity(1), EquipmentSlot.Shield).Disposition);
        var swap = Wear(session, host, "kit:1", EquipmentSlot.Shield);
        Assert.AreEqual(Entity(1), swap.Displaced);
        Assert.AreEqual(0, host.View(session, null, 1, kitWear: KitWear).Grid.Single(item => item.Entity == Entity(1)).Slot, "the cell kit:1 left");
    }

    [TestMethod]
    public void TheHitTestFollowsTheOriginalFrameAndAReleaseOutsideOrOnItsOwnSlotPutsTheItemBack()
    {
        Assert.AreEqual(new AreaInventoryHit(null, EquipmentSlot.Ring1), AreaInventoryDrag.HitTest(250, 110));
        Assert.AreEqual(new AreaInventoryHit(null, EquipmentSlot.Boots), AreaInventoryDrag.HitTest(150, 394));
        Assert.AreEqual(new AreaInventoryHit(0, null), AreaInventoryDrag.HitTest(368, 8));
        Assert.AreEqual(new AreaInventoryHit(119, null), AreaInventoryDrag.HitTest(368 + 319.9f, 8 + 383.9f));
        Assert.AreEqual(AreaInventoryHit.None, AreaInventoryDrag.HitTest(368 + 320, 8));
        Assert.AreEqual(AreaInventoryHit.None, AreaInventoryDrag.HitTest(10, 10));

        // A 2 × 3 item at slot 3 grabbed in its lower right cell keeps that offset; a target above the grid's first row puts it back.
        var view = new AreaInventoryView([], null, null, []) { Grid = [new AreaGridItem("crate", 3, 2, 3, "crate")] };
        var held = AreaInventoryDrag.Press(view, new(24, null))!;
        Assert.AreEqual(21, held.Offset);
        Assert.AreEqual(19, AreaInventoryDrag.Release(held, new(40, null)).GridSlot, "a plain subtraction in grid locations");
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaInventoryDrag.Release(held, new(15, null)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaInventoryDrag.Release(held, AreaInventoryHit.None).Kind);
        Assert.IsNull(AreaInventoryDrag.Press(view, new(0, null)), "an empty cell");
        Assert.IsNull(AreaInventoryDrag.Press(view, new(null, EquipmentSlot.Helmet)), "an empty slot");
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaInventoryDrag.Release(new("ring", EquipmentSlot.Ring1, 0), new(null, EquipmentSlot.Ring1)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.Wear, AreaInventoryDrag.Release(new("ring", EquipmentSlot.Ring1, 0), new(null, EquipmentSlot.Ring2)).Kind);
    }
}
