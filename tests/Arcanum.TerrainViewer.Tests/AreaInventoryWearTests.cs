using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
}
