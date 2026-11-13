using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Lighting;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-1561 (ledger RM3c, IE5): the carried light of an NPC whose worn items the session recalculated (invented values, no original data): a guard wearing a torch with the large light flag (item flags 0x800) in its shield slot.
/// The saved light stands until the session recalculates (a magick or tech point write, a best-items wield); then the size follows the worn items and the guard's aptitude, and a torch that left the guard takes its load-time light with it.
/// </summary>
[TestClass]
public sealed class AreaNpcCarriedLightTests
{
    private const int Source = 5, TorchPrototype = 15199, LargeLight = 0x800;
    private static readonly SessionMapId Map = new(1);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A torch (generic item, shield location, flags 0x1 and the light bits) with a complexity (the item's magick or tech adjustment).
    private static AreaObjectRecord Torch(int index, int location, int complexity, long flags) => new(Key(index), 0, 14, TorchPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, flags), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, location), Field(44, 0x60000000), Field(45, complexity),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, 0x1), Field(60, 0), Field(61, 0),
        ], true, true, true);

    // A human NPC (type 16) with stats 8, level 1, no skills, 20 hit points.
    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 8, 8, 8, 8, 8, 8, 8, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47), Field(57, Source), Field(63, 0),
            Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0), Array(92),
        ], true, true, true);

    private sealed class Screen : IAreaRestockScreen
    {
        public int? ShownMap => 1;
        public AreaPixelRect? GameWindow => null;
        public (double Width, double Height) WindowSize => (800, 600);
        public AreaPixelRect? BodyRect(AreaObjectKey key) => null;
    }

    // The guard (0) with a torch (1) in its shield slot and a cup (2) in its pack; the torch's light flags and complexity are the test's.
    private static (GameSessionOwner Session, AreaRestockHost Restock, AreaCombatHost Combat) Setup(long torchFlags, int complexity, bool torch = true)
    {
        var session = ConversationSessionFixture.Create();
        var items = torch ? new[] { Entity(1), Entity(2) } : new[] { Entity(2) };
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], items)],
                [.. torch ? [new SessionSeedItem(Entity(1), new(new(SessionItemType.Generic, TorchPrototype, 6000, 6001), 1, complexity, 1005, null, []), null)] : System.Array.Empty<SessionSeedItem>(),
                    new SessionSeedItem(Entity(2), new(new(SessionItemType.Generic, TorchPrototype, 6000, 6001), 1, 0, 0, null, []), null)]),
            (torch ? new[] { Npc(0), Torch(1, 1005, complexity, torchFlags), Torch(2, 0, 0, 0x1) } : new[] { Npc(0), Torch(2, 0, 0, 0x1) }).ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory, null);
        return (session, new AreaRestockHost(session, inventory, null, new Screen()), combat);
    }

    private static CarriedLightSize? Light(GameSessionOwner session, AreaRestockHost restock, AreaCombatHost combat) =>
        restock.NpcLight(Map, Entity(0), combat);

    [TestMethod]
    public void The_saved_light_stands_until_the_session_recalculates()
    {
        var (session, restock, combat) = Setup(0x1 | LargeLight, 0);
        using var _ = session;
        Assert.IsNull(Light(session, restock, combat), "a guard the session did not dress or write points of keeps its saved light");
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteCritterStat(session.Version, Map, Entity(0), 22, 10).Disposition);
        Assert.AreEqual(CarriedLightSize.Large, Light(session, restock, combat), "a magick point write runs the rewield: the torch's size");
        Assert.IsNull(restock.NpcLight(Map, Entity(2), combat), "an item is no NPC");
    }

    // Each light flag of the item (0x200 small, 0x400 medium, 0x800 large, 0x1000 extra large) gives its own size to a neutral item (no power shift), and the batch the viewer asks gives the same answers.
    [TestMethod]
    [DataRow(0x200, CarriedLightSize.Small)]
    [DataRow(0x400, CarriedLightSize.Medium)]
    [DataRow(0x800, CarriedLightSize.Large)]
    [DataRow(0x1000, CarriedLightSize.ExtraLarge)]
    public void Each_light_flag_gives_its_size(int flag, CarriedLightSize size)
    {
        var (session, restock, combat) = Setup(0x1 | flag, 0);
        using var _ = session;
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteCritterStat(session.Version, Map, Entity(0), 23, 10).Disposition);
        Assert.AreEqual(size, Light(session, restock, combat));
        var batch = restock.NpcLights(Map, [Entity(0), Entity(2)], combat);
        Assert.AreEqual(1, batch.Count, "the item is no NPC");
        Assert.AreEqual(size, batch[Entity(0)]);
    }

    [TestMethod]
    public void The_tech_point_write_recalculates_too()
    {
        var (session, restock, combat) = Setup(0x1 | LargeLight, 0);
        using var _ = session;
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteCritterStat(session.Version, Map, Entity(0), 23, 10).Disposition);
        Assert.AreEqual(CarriedLightSize.Large, Light(session, restock, combat));
        // Another stat is no rewield.
        var (other, otherRestock, otherCombat) = Setup(0x1 | LargeLight, 0);
        using var _2 = other;
        Assert.AreEqual(GameSessionDisposition.Changed, other.WriteCritterStat(other.Version, Map, Entity(0), 19, 10).Disposition);
        Assert.IsNull(Light(other, otherRestock, otherCombat));
    }

    [TestMethod]
    public void The_size_shrinks_with_the_items_power_against_the_npcs_aptitude()
    {
        var (session, restock, combat) = Setup(0x1 | LargeLight, 100);
        using var _ = session;
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteCritterStat(session.Version, Map, Entity(0), 22, 0).Disposition);
        var wielder = restock.Wielder(Map, Entity(0), combat.Critter(1, Entity(0)))!;
        var torch = wielder.Items(new(Entity(1), session.QueryMapInventory(Map)!.Holders.Single(holder => holder.Entity == Entity(0)).Items.Single(item => item.Entity == Entity(1)).Facts))!.Item;
        var expected = EquipmentRules.Light([torch], wielder.Chooser.MagickTechAptitude);
        Assert.AreEqual(100, torch.MagickTechComplexity);
        Assert.AreEqual(expected == Arcanum.Simulation.Combat.LightEmission.None ? CarriedLightSize.None : (CarriedLightSize)(int)expected, Light(session, restock, combat));
        Assert.AreNotEqual(CarriedLightSize.Large, Light(session, restock, combat), "a magickal torch of 100 against an aptitude of an eight-stat guard is dimmer");
    }

    [TestMethod]
    public void A_torch_that_left_the_guard_takes_its_load_time_light_with_it()
    {
        var (session, restock, combat) = Setup(0x1 | LargeLight, 0);
        using var _ = session;
        var wear = combat.WornItems(session, 1, Entity(0))!;
        Assert.AreEqual(GameSessionDisposition.Changed, session.BreakItem(session.Version, Entity(1), wear[EquipmentSlot.Shield].Baseline).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteCritterStat(session.Version, Map, Entity(0), 22, 10).Disposition);
        Assert.AreEqual(CarriedLightSize.None, Light(session, restock, combat), "the unwielded torch's light is cleared by the recalculation");
    }

    [TestMethod]
    public void A_guard_with_no_light_item_keeps_its_saved_light_whatever_the_write()
    {
        var (session, restock, combat) = Setup(0x1, 0);
        using var _ = session;
        Assert.AreEqual(GameSessionDisposition.Changed, session.WriteCritterStat(session.Version, Map, Entity(0), 22, 10).Disposition);
        Assert.IsNull(Light(session, restock, combat), "no worn item has light flags now or at load: 0x467E80 returns at once, the saved light stands");
        var (bare, bareRestock, bareCombat) = Setup(0x1, 0, torch: false);
        using var _2 = bare;
        Assert.AreEqual(GameSessionDisposition.Changed, bare.WriteCritterStat(bare.Version, Map, Entity(0), 23, 10).Disposition);
        Assert.IsNull(Light(bare, bareRestock, bareCombat));
    }
}
