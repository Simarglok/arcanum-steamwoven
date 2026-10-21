using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-COMBAT-008: the worn items the viewer passes to an attack's wear (invented values, no original data): the hero's kit
/// by its durability values, an NPC's items in wear slots that are not broken, and the session's object conditions.</summary>
[TestClass]
public sealed class AreaWearHostTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // A generic item (type 14) with every slot the combat-field reader needs: the common block, the item block and the generic block.
    private static AreaObjectRecord Item(int index, int location) => new(Key(index), 0, 14, 15199, "p", "p", "r", "s", AreaScope.Core, null, null, null,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null),
            Field(36, 0x1), Field(38, 1), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, location), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, 0x1), Field(60, 0), Field(61, 0),
        ], true, true, true);

    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static SessionItemFacts Facts(int location) => new(new(SessionItemType.Generic, 15199, 6000, 6001), 1, 0, location, null, []);

    // An NPC (0) holding a torch in the shield slot (1), a cup (2) and, for the duplicate case, a second torch (3).
    private static (AreaCombatHost Host, AreaInventoryHost Inventory) Host(bool duplicate = false)
    {
        var items = duplicate ? new[] { 1, 2, 3 } : new[] { 1, 2 };
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [.. items.Select(Entity)])],
                [.. items.Select(i => new SessionSeedItem(Entity(i), Facts(i == 2 ? 0 : 1005), null))]),
            new[] { Npc(0) }.Concat(items.Select(i => Item(i, i == 2 ? 0 : 1005))).ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        return (new AreaCombatHost(inventory), inventory);
    }

    [TestMethod]
    public void AnNpcWearsTheItemsInItsWearSlotsUntilOneBreaks()
    {
        using var session = ConversationSessionFixture.Create();
        var (host, inventory) = Host();
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var worn = host.WornItems(session, 1, Entity(0))!;
        Assert.AreEqual(1, worn.Count, "the cup is in no wear slot");
        var torch = worn[EquipmentSlot.Shield];
        Assert.AreEqual((Entity(1), new ItemCondition(10, 0, 0, false)), (torch.Entity, torch.Baseline));

        Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(1), torch.Baseline).Disposition);
        Assert.AreEqual(0, host.WornItems(session, 1, Entity(0))!.Count, "a broken item is taken off");
        Assert.IsNull(host.WornItems(session, 2, Entity(0)), "a map the host does not cover");

        using var other = ConversationSessionFixture.Create();
        var (twice, twiceInventory) = Host(duplicate: true);
        twiceInventory.Attach(other);
        Assert.IsNull(twice.WornItems(other, 1, Entity(0)), "two items in one slot");
    }

    [TestMethod]
    public void TheHerosWornKitNeedsADurabilityValueForEachItem()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Generic, 15199, 6000, 6001))]);
        var torch = new EquipmentItem { Kind = EquipmentItemKind.Generic, UsesShieldLocation = true, HitPoints = 10, MaxHitPoints = 10 };
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic",
            KitWear: [new SessionWearableItem(new(SessionItemType.Generic, 15199, 6000, 6001), [], false, torch)]);
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        Assert.AreEqual(EquipmentSlot.Shield, session.QueryEquipment().Worn.Single().Slot, "the start wields the kit's torch");
        var kit = new SessionWearItem?[] { new("kit:1", new(10, 0, 0, false)) };
        Assert.AreEqual("kit:1", AreaCombatHost.HeroWorn(session, kit)![EquipmentSlot.Shield].Entity);
        Assert.IsNull(AreaCombatHost.HeroWorn(session, null), "no kit values without a hero load");
        Assert.IsNull(AreaCombatHost.HeroWorn(session, [null]), "a kit item whose prototype did not read");
        Assert.IsNull(AreaCombatHost.HeroWorn(session, [new SessionWearItem("kit:2", new(10, 0, 0, false))]), "values of another entity");
        using var heroless = ConversationSessionFixture.Create();
        Assert.AreEqual(0, AreaCombatHost.HeroWorn(heroless, kit)!.Count, "nothing worn");
    }

    [TestMethod]
    public void AnObjectConditionCountsOnlyForAKindThatHasIt()
    {
        using var session = ConversationSessionFixture.Create();
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
            [new(new(1), MapChangeKey.ForEntity("x", GameSessionOwner.ObjectConditionProperty), (long)SessionObjectCondition.Busted)]).Disposition);
        Assert.AreEqual(SessionObjectCondition.Busted, session.QueryObjectCondition(new(1), "x", DamageableObjectKind.Trap));
        Assert.AreEqual(SessionObjectCondition.Whole, session.QueryObjectCondition(new(1), "x", DamageableObjectKind.Portal), "a portal has no busted condition");
        Assert.AreEqual(SessionObjectCondition.Whole, session.QueryObjectCondition(new(1), "y", DamageableObjectKind.Scenery));
        Assert.ThrowsExactly<ArgumentException>(() => session.QueryObjectCondition(new(1), "bad id!", DamageableObjectKind.Scenery));
    }
}
