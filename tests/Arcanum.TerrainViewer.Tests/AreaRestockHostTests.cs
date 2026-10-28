using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-096: the viewer's restock host over an invented map (entities, cells, sets and prototypes are invented): the schedule at a
// window open, the holders' facts and screen test, the merchant search in the window-sized area, the persistent flag, the sets and
// the items, and a whole restock through the event world.
[TestClass]
public sealed class AreaRestockHostTests
{
    private const int Axe = 4040, Bullets = 7059;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    private static AreaObjectRecord Row(int index, int type, long x, long y) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static SessionItemFacts Facts(SessionItemType type, int prototype, int? quantity = null) => new(new(type, prototype, 4200, 34200), 10, 0, 0, quantity, []);

    // 0 the merchant at (100,100), whose substitute is 1, the chest at (105,100) (source 5, flags 0x1) holding 2 coins and 3 a persistent
    // ring; 4 a clerk at (101,100), first in the seed's order, whose substitute is another box 5 at (101,101); 6 a far NPC at (100,140)
    // whose substitute is also the chest (beyond the vicinity and 20 tiles).
    private static AreaInventoryMap Map()
    {
        var seed = new SessionMapInventorySeed(
            [
                new(Entity(4), SessionHolderKind.Npc, false, true, [], []),
                new(Entity(0), SessionHolderKind.Npc, false, true, [], []),
                new(Entity(1), SessionHolderKind.Container, false, false, [], [Entity(2), Entity(3)]),
                new(Entity(5), SessionHolderKind.Container, false, false, [], []),
                new(Entity(6), SessionHolderKind.Npc, false, true, [], []),
            ],
            [new(Entity(2), Facts(SessionItemType.Gold, AreaBarterData.GoldPrototype, 30), null), new(Entity(3), Facts(SessionItemType.Armor, 2805), null)]);
        var rows = new[] { Row(0, 16, 100, 100), Row(1, 2, 105, 100), Row(2, 8, 105, 100), Row(3, 7, 105, 100), Row(4, 16, 101, 100), Row(5, 2, 101, 101), Row(6, 16, 100, 140) }
            .ToDictionary(row => Entity(row.Key.SourceIndex));
        var substitutes = new Dictionary<string, AreaSubstituteContainer>
        {
            [Entity(0)] = new(Entity(1), 5, new(105, 100)),
            [Entity(4)] = new(Entity(5), 0, new(101, 101)),
            [Entity(6)] = new(Entity(1), 5, new(105, 100)),
        };
        return new(1, seed, rows, substitutes: substitutes, restock: new Dictionary<string, AreaRestockSeed> { [Entity(1)] = new(5, 0x1) }, persistent: [Entity(3)]);
    }

    private static CombatFields Item(int type, int prototype, CombatAmmoFields? ammo = null) =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields(type, prototype, 0, 0, 3000 + type, 33000 + type, 0, 40, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 10, 100, 0, 0, 0, 0, [], 0, 0, 0, 0), null, ammo, null, null, null, null);

    // Set 5 generates 25 coins, an axe always and bullets always; set 6's loot line names a basic number without a prototype (emptied, its
    // coins 7..9 stay); set 7 has no colon (the project refuses it); 8 is past the table.
    private static AreaBarterData Data() => AreaBarterData.Create(
        MesFile.Parse(Encoding.ASCII.GetBytes("{1}{A: 1,1}{2}{B: 1,1}{3}{C: 1,1}{4}{D: 1,1}{5}{Chest: 25,25 100,4020 100,7039}{6}{Broken: 7,9 100,4021}{7}{No pair here}")).File!,
        null, new Dictionary<int, CombatFields>
        {
            [Axe] = Item((int)SessionItemType.Weapon, Axe),
            [Bullets] = Item((int)SessionItemType.Ammo, Bullets, new CombatAmmoFields(0, 10, 1)),
            [AreaBarterData.GoldPrototype] = Item((int)SessionItemType.Gold, AreaBarterData.GoldPrototype),
        });

    private sealed class Screen : IAreaRestockScreen
    {
        public int? ShownMap { get; set; } = 1;
        public AreaPixelRect? GameWindow { get; set; } = new(0, 0, 800, 600);
        public (double Width, double Height) WindowSize { get; set; } = (800, 600);
        public Dictionary<AreaObjectKey, AreaPixelRect> Bodies { get; } = [];
        public AreaPixelRect? BodyRect(AreaObjectKey key) => Bodies.TryGetValue(key, out var rect) ? rect : null;
    }

    private static (GameSessionOwner Session, AreaInventoryHost Inventory, AreaRestockHost Host, Screen Screen) Host()
    {
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1).Owner!;
        var inventory = new AreaInventoryHost([Map()]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var screen = new Screen();
        return (session, inventory, new AreaRestockHost(session, inventory, Data(), screen), screen);
    }

    [TestMethod]
    public void A_window_open_schedules_a_seed_holder_with_a_source_only()
    {
        var (session, inventory, _, _) = Host();
        using var owned = session;
        Assert.AreEqual(GameSessionDisposition.Changed, AreaRestockHost.Schedule(session, inventory, Entity(1))?.Disposition, "the chest has source 5");
        Assert.AreEqual(Entity(1), session.QueryRestocks().Single().Holder);
        Assert.AreEqual(GameSessionDisposition.NoChange, AreaRestockHost.Schedule(session, inventory, Entity(0))?.Disposition, "the merchant has none");
        Assert.IsNull(AreaRestockHost.Schedule(session, inventory, "junk:1"), "a junk pile is no seed holder");
        Assert.IsNull(AreaRestockHost.Schedule(session, new AreaInventoryHost([]), Entity(1)), "a map without a seed");
    }

    [TestMethod]
    public void The_screen_test_meets_the_game_window_strictly_on_the_shown_map()
    {
        var (session, _, host, screen) = Host();
        using var owned = session;
        var map = new SessionMapId(1);
        Assert.IsFalse(host.OnScreen(map, Entity(1)), "a holder the plan draws no body for is off screen");
        screen.Bodies[Key(1)] = new(790, 590, 20, 20);
        Assert.AreEqual(new RestockHolder(RestockHolderKind.Container, 5, false, false, 0x1, true), host.Facts(map, Entity(1)));
        screen.Bodies[Key(1)] = new(800, 100, 20, 20);
        Assert.IsFalse(host.OnScreen(map, Entity(1)), "a body that only touches the window's edge does not meet it (strict)");
        screen.Bodies[Key(1)] = new(-19.5, -19.5, 20, 20);
        Assert.IsTrue(host.OnScreen(map, Entity(1)));
        screen.ShownMap = 2;
        Assert.IsFalse(host.OnScreen(map, Entity(1)), "another map is shown");
        Assert.AreEqual(new RestockHolder(RestockHolderKind.Npc, 0, false, false, 0, false), host.Facts(map, Entity(0)));
        Assert.IsNull(host.Facts(map, Entity(2)), "an item is no holder");
        Assert.IsNull(host.Facts(new(2), Entity(1)), "a map without a seed");
    }

    [TestMethod]
    public void The_merchant_search_takes_the_first_npc_of_the_window_sized_area_whose_substitute_is_the_container()
    {
        Assert.AreEqual((88, 88, 113, 113), AreaRestockHost.Vicinity(new(100, 100), (800, 600)), "the tiles under an 800 x 600 rectangle's corners");
        var (session, _, host, screen) = Host();
        using var owned = session;
        var map = new SessionMapId(1);
        Assert.AreEqual(Entity(0), host.Merchant(map, Entity(1)), "the clerk before it uses another box; the far NPC is outside");
        Assert.AreEqual(Entity(4), host.Merchant(map, Entity(5)));
        screen.WindowSize = (80, 40);
        Assert.IsNull(host.Merchant(map, Entity(1)), "a window that small reaches no NPC five tiles away");
        Assert.IsNull(host.Merchant(map, Entity(0)), "an NPC is no container");
    }

    [TestMethod]
    public void Sets_items_persistence_and_the_pile_come_from_the_data()
    {
        var (session, _, host, _) = Host();
        using var owned = session;
        var chest = host.Set(5);
        Assert.AreEqual(SessionRestockSetStatus.Read, chest.Status);
        Assert.AreEqual((25, 25), (chest.Set!.MinCoins, chest.Set.MaxCoins));
        CollectionAssert.AreEqual(new[] { new RestockLine(100, 4020), new RestockLine(100, 7039) }, chest.Set.Lines.ToArray());
        var broken = host.Set(6);
        Assert.AreEqual((SessionRestockSetStatus.Read, 7, 9, 0), (broken.Status, broken.Set!.MinCoins, broken.Set.MaxCoins, broken.Set.Lines.Count),
            "an emptied set keeps the coins its loot line read (RK6)");
        Assert.AreEqual(SessionRestockSetStatus.Unsupported, host.Set(7).Status);
        Assert.AreEqual(SessionRestockSetStatus.Unreachable, host.Set(8).Status);
        Assert.AreEqual(SessionRestockSetStatus.Unreachable, host.Set(0).Status);
        Assert.AreEqual(new SessionRestockItem(new(SessionItemType.Weapon, Axe, 3005, 33005)), host.Item(4020));
        Assert.AreEqual(new SessionRestockItem(new(SessionItemType.Ammo, Bullets, 3006, 33006), 1, 10), host.Item(7039));
        Assert.IsNull(host.Item(4021), "no prototype");
        Assert.AreEqual(new SessionItemKind(SessionItemType.Gold, AreaBarterData.GoldPrototype, 3008, 33008), host.PileKind);
        Assert.IsTrue(host.Persistent(new(1), Entity(3)));
        Assert.IsFalse(host.Persistent(new(1), Entity(2)) || host.Persistent(new(1), "made:1") || host.Persistent(new(2), Entity(3)));
    }

    [TestMethod]
    public void An_off_screen_chest_regenerates_through_its_merchant_and_an_on_screen_one_waits()
    {
        var (session, inventory, host, screen) = Host();
        using var owned = session;
        var restock = (AreaRestockHost?)null;
        var world = new AreaEventWorld(() => new AreaCombatHost(inventory), () => null, restock: () => restock);
        AreaRestockHost.Schedule(session, inventory, Entity(1));
        var pump = new AreaEventPump(session, world);
        var waited = pump.Jump(RestockRules.MaxDelayMs).Restocks.Single();
        Assert.IsTrue(waited.Postponed, "no restock host yet: a minute on");
        restock = host;
        screen.Bodies[Key(1)] = new(100, 100, 40, 40);
        var rescheduled = pump.Jump(SessionEventKinds.RestockRetryDelayMs).Restocks.Single();
        Assert.AreEqual(RestockStep.Reschedule, rescheduled.Decision.Step, "on screen");
        Assert.AreEqual(1, session.QueryRestocks().Count);
        screen.Bodies.Clear();
        var fired = pump.Jump(RestockRules.MaxDelayMs).Restocks.Single();
        Assert.AreEqual(new RestockDecision(RestockStep.RegenerateMerchantAndContainer, false), fired.Decision, "the merchant has no source of its own");
        var done = fired.Regenerations.Single();
        Assert.AreEqual((Entity(1), SessionRestockOutcome.Regenerated, 1, 25), (done.Holder, done.Outcome, done.Removed, done.Coins));
        var chest = session.QueryMapInventory(new(1))!.Holders.Single(holder => holder.Entity == Entity(1)).Items.Select(item => item.Facts.Kind.Prototype).ToArray();
        CollectionAssert.AreEqual(new[] { 2805, AreaBarterData.GoldPrototype, Axe, Bullets }, chest, "the ring stays, then coins, axe and bullets");
        Assert.IsTrue(session.IsItemDestroyed(Entity(2)), "the old coins are destroyed");
        Assert.AreEqual(0, session.QueryRestocks().Count);
    }
}
