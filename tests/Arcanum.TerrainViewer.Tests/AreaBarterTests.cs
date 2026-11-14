using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-050: the barter window's model over a session (invented prototypes, prices and entities).
[TestClass]
public sealed class AreaBarterTests
{
    private const int KitSword = 4030, Axe = 4040;

    private static CombatFields Weapon(int prototype, int worth, uint flags = 0) =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Weapon, prototype, 0, 0, 0, 0, 0, 40, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(flags, 10, worth, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);

    private static CombatFields Gold() =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0, 3200, 33200, 0, 1, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 0, 1, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    private static AreaBarterData Data(string buy = "{1}{4010 4020}") => AreaBarterData.Create(Mes("{1}{Smith: 1,1}{2}{End: 1,1}"), Mes(buy),
        new Dictionary<int, CombatFields> { [KitSword] = Weapon(KitSword, 100), [Axe] = Weapon(Axe, 400), [AreaBarterData.GoldPrototype] = Gold() });

    private static GameSessionOwner Session()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 1000, [new SessionHeroKitItem(new(SessionItemType.Weapon, KitSword, 4100, 34100))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        SessionItemFacts Facts(SessionItemType type, int prototype, int location = 0, int? quantity = null) =>
            new(new(type, prototype, 4200, 34200), 10, 0, location, quantity, []);
        var seed = new SessionMapInventorySeed(
            [new("merchant", SessionHolderKind.Npc, false, false, [], ["axe", "vest", "coins"])],
            [new("axe", Facts(SessionItemType.Weapon, Axe), null), new("vest", Facts(SessionItemType.Weapon, Axe, location: 1006), null),
             new("coins", Facts(SessionItemType.Gold, AreaBarterData.GoldPrototype, quantity: 100), null)]);
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(session.QueryActor().Map, seed).Failure);
        return session;
    }

    private static AreaBarter Barter(GameSessionOwner session, AreaBarterData data, List<int> changes, int reaction = 50, bool follower = false,
        int set = 1) =>
        new(session, new AreaInventoryHost([]), data, new AreaBarterMerchant("merchant", "Smith", 100, false, false, data.BuyList(set), follower),
            () => reaction, change => { changes.Add(change); return true; }, (entity, _) => entity);

    [TestMethod]
    public void The_window_quotes_the_heros_items_for_sale_and_the_merchants_unworn_wares_for_purchase()
    {
        using var session = Session();
        var view = Barter(session, Data(), []).View();
        Assert.AreEqual((1000, 100, 10), (view.HeroGold, view.MerchantGold, view.Haggle), "haggle level 0: 4L + 10");
        var kit = view.Hero.Single();
        // Sale: 100 × (5 + 50) / 100.
        Assert.AreEqual(("kit:1", 55, 100), (kit.Entity, kit.Quote.Price, kit.Worth));
        // Buy: 400 + 90 × 100 × 400 / 10000; the worn vest and the coins are not wares.
        Assert.AreEqual(("axe", 760), (view.Wares.Single().Entity, view.Wares.Single().Quote.Price));
        Assert.AreEqual(TradeRefusal.NotDealt, Barter(session, Data(), [], set: 2).View().Hero.Single().Quote.Refusal, "a set without a buy line");
    }

    [TestMethod]
    public void Trades_move_items_and_coins_and_a_follower_trades_for_nothing()
    {
        using var session = Session();
        var changes = new List<int>();
        var barter = Barter(session, Data(), changes);
        var bought = barter.Buy("axe");
        Assert.IsTrue(bought.Moved, bought.Text);
        Assert.AreEqual((240, 860, 760), (session.QueryGold(), session.QueryNpcGold("merchant"), bought.Total));
        var sold = barter.Sell("kit:1");
        Assert.IsTrue(sold.Moved, sold.Text);
        Assert.AreEqual((295, 805), (session.QueryGold(), session.QueryNpcGold("merchant")));
        CollectionAssert.AreEqual(Array.Empty<int>(), changes, "a neutral merchant's reaction does not move");
        var free = Barter(session, Data(), changes, follower: true).Buy("kit:1");
        Assert.AreEqual((true, 295, 805), (free.Moved, session.QueryGold(), session.QueryNpcGold("merchant")));
    }

    // ARC-UI-1926 (0x575200 on a location of 1000 and up, 0x462230): the merchant's worn items are not wares; the doll view's buy takes them, at no price below Master haggle.
    [TestMethod]
    public void A_worn_item_of_the_merchant_is_quoted_with_no_price_and_not_bought_below_master_haggle()
    {
        using var session = Session();
        var barter = Barter(session, Data(), []);
        var view = barter.View();
        var vest = view.WornWares.Single();
        Assert.AreEqual(("vest", 0, TradeRefusal.Worn), (vest.Entity, vest.Quote.Price, vest.Quote.Refusal));
        CollectionAssert.DoesNotContain(view.Wares.Select(line => line.Entity).ToArray(), "vest", "the diagnostic wares leave worn items out");
        var refused = barter.Buy("vest");
        Assert.AreEqual((false, 1000, 100, "won't sell (Worn)"), (refused.Moved, session.QueryGold(), session.QueryNpcGold("merchant"), refused.Text));
        Assert.AreEqual("vest", barter.View().WornWares.Single().Entity, "still worn");
    }

    [TestMethod]
    public void A_followers_worn_item_is_taken_for_nothing_and_leaves_its_slot()
    {
        using var session = Session();
        var barter = Barter(session, Data(), [], follower: true);
        var taken = barter.Buy("vest");
        Assert.IsTrue(taken.Moved, taken.Text);
        Assert.AreEqual((1000, 100), (session.QueryGold(), session.QueryNpcGold("merchant")), "no coins change hands");
        Assert.AreEqual(0, barter.View().WornWares.Count, "it left the slot");
        CollectionAssert.Contains(barter.View().Hero.Select(line => line.Entity).ToArray(), "vest");
    }

    [TestMethod]
    public void A_sale_pays_at_most_the_merchants_coins()
    {
        using var session = Session();
        Assert.AreEqual(GameSessionFailure.None, session.TakeGoldFromNpc(session.Version, "merchant", 70).Failure);
        var sold = Barter(session, Data(), []).Sell("kit:1");
        Assert.AreEqual((true, 30, "sold kit:1 for 30"), (sold.Moved, sold.Total, sold.Text), "the quote 55 capped at 30 coins");
        Assert.AreEqual((1100, 0), (session.QueryGold(), session.QueryNpcGold("merchant")));
    }

    [TestMethod]
    public void Refusals_and_a_loving_merchants_reaction()
    {
        using var session = Session();
        var changes = new List<int>();
        Assert.AreEqual("won't buy (NotDealt)", Barter(session, Data("{1}{4020}"), changes).Sell("kit:1").Text);
        // Reaction 101 (love): the buy price is 80 % of 760 = 608; the merchant's surplus 208 gives 5 + 8 / 100 = 5.
        var loving = Barter(session, Data(), changes, reaction: 101);
        var bought = loving.Buy("axe");
        Assert.AreEqual((true, 608, 5), (bought.Moved, bought.Total, bought.ReactionChange));
        CollectionAssert.AreEqual(new[] { 5 }, changes);
        Assert.IsFalse(loving.Buy("axe").Moved, "no longer a ware");
        using var poor = Session();
        Assert.AreEqual(GameSessionFailure.None, poor.BarterSell(poor.Version, "merchant", "kit:1", 1, 0, default, AreaInventoryHost.DiagnosticInsertCheck).Failure);
        Assert.AreEqual(GameSessionFailure.None, poor.TakeGoldFromNpc(poor.Version, "merchant", 100).Failure);
        Assert.AreEqual("not enough money", Barter(poor, Data(), changes, reaction: -10).Buy("axe").Text, "1520 > 1100");
    }

    // ARC-WORLD-090: a merchant whose set has no buy line and whose chest (set 1) buys the kit sword; the chest holds a magickal axe of
    // worth 400 (complexity 30, not identified) and 50 coins.
    private const int MagicAxe = 4050;

    private static AreaBarterData StockData() => AreaBarterData.Create(Mes("{1}{Smith: 1,1}{2}{End: 1,1}"), Mes("{1}{4010}"),
        new Dictionary<int, CombatFields>
        {
            [KitSword] = Weapon(KitSword, 100), [MagicAxe] = Weapon(MagicAxe, 400) is var axe ? axe with { Item = axe.Item! with { MagickTechComplexity = 30 } } : null!,
            [AreaBarterData.GoldPrototype] = Gold(),
        });

    private static GameSessionOwner StockSession()
    {
        var session = Session();
        SessionItemFacts Facts(SessionItemType type, int prototype, int complexity = 0, int? quantity = null) =>
            new(new(type, prototype, 4200, 34200), 10, complexity, 0, quantity, []);
        var seed = new SessionMapInventorySeed(
            [new("merchant", SessionHolderKind.Npc, false, true, [], ["coins"]), new("chest", SessionHolderKind.Container, false, false, [], ["magic-axe", "chest-coins"])],
            [new("coins", Facts(SessionItemType.Gold, AreaBarterData.GoldPrototype, quantity: 100), null),
             new("magic-axe", Facts(SessionItemType.Weapon, MagicAxe, complexity: 30), null),
             new("chest-coins", Facts(SessionItemType.Gold, AreaBarterData.GoldPrototype, quantity: 50), null)]);
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(session.QueryActor().Map, seed).Failure);
        return session;
    }

    [TestMethod]
    public void A_merchant_with_a_stock_chest_trades_through_it_after_the_open()
    {
        using var session = StockSession();
        var data = StockData();
        var own = new AreaBarterMerchant("merchant", "Smith", 100, false, false, data.BuyList(2), false);
        var near = own.WithSubstitute(new("chest", 1, new(40, 40)), new(60, 20), data);
        Assert.AreEqual(("chest", InventorySourceBuyStatus.Listed), (near.Container, near.ContainerBuyList!.Status), "20 tiles away (Chebyshev)");
        Assert.IsNull(own.WithSubstitute(new("chest", 1, new(40, 40)), new(61, 40), data).Container, "21 tiles away");
        Assert.IsNull((own with { Follower = true }).WithSubstitute(new("chest", 1, new(40, 40)), new(40, 40), data).Container, "a follower has a leader");
        var barter = new AreaBarter(session, new AreaInventoryHost([]), data, near, () => 50, _ => true, (entity, _) => entity);
        // Before the open the axe is not identified: worth 300, so 300 + 90 × 100 × 300 / 10000 = 570.
        Assert.AreEqual(("magic-axe", 570), (barter.View().Wares.Single().Entity, barter.View().Wares.Single().Quote.Price));
        var opened = barter.Open();
        Assert.AreEqual((50, 1), (opened.Coins, opened.Piles));
        var view = barter.View();
        Assert.AreEqual((150, 760), (view.MerchantGold, view.Wares.Single().Quote.Price), "the chest's coins are the merchant's; the axe is identified");
        Assert.AreEqual(55, view.Hero.Single().Quote.Price, "the chest's buy list deals in the sword (BT8)");
        Assert.AreEqual(TradeRefusal.NotDealt, new AreaBarter(session, new AreaInventoryHost([]), data, own, () => 50, _ => true, (entity, _) => entity)
            .View().Hero.Single().Quote.Refusal, "without the chest the merchant buys nothing");
        Assert.IsTrue(barter.Sell("kit:1").Moved);
        Assert.AreEqual((1055, 95), (session.QueryGold(), session.QueryNpcGold("merchant")));
        CollectionAssert.AreEqual(new[] { "magic-axe", "kit:1" }, barter.View().Wares.Select(line => line.Entity).ToArray(), "the sale went into the chest");
        Assert.IsTrue(barter.Buy("magic-axe").Moved);
        Assert.AreEqual((295, 855), (session.QueryGold(), session.QueryNpcGold("merchant")));
        Assert.AreEqual(400, barter.View().Hero.Single(line => line.Entity == "magic-axe").Worth, "a bought item stays identified");
    }

    /// <summary>Opt-in (DAT_ROOT = the game folder): the installed buy lists by status, and the
    /// NPCs of every whole map that are merchants (an inventory source that buys something, their own or their substitute container's), with
    /// their price multipliers. Counts only.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Original_buy_lists_and_the_merchants_of_the_maps()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var module = Path.Combine(root, "modules", "Arcanum.dat");
        var loaded = AreaBarterData.Load(module);
        Assert.AreEqual(AreaBarterDataStatus.Ready, loaded.Status, loaded.Detail);
        var data = loaded.Data!;
        var statuses = Enumerable.Range(1, data.Sources.Count).GroupBy(set => data.BuyList(set).Status).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"data: {loaded.Detail}; buy lists: {string.Join(", ", statuses.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}"))}");
        // First observation 2026-10-28 (the loot lines of 1..123 parse; 35 sets have a buy line that buys something).
        Assert.AreEqual((2, 33, 0), (statuses.GetValueOrDefault(InventorySourceBuyStatus.Everything), statuses.GetValueOrDefault(InventorySourceBuyStatus.Listed),
            statuses.GetValueOrDefault(InventorySourceBuyStatus.Emptied)), "the census changed: update the barter contract");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        // A merchant buys through its own source or, failing that, its substitute container's (0x464200); its wares are the container's
        // list when it has one within reach (ARC-WORLD-090: no leader, 20 tiles, 0x45F650).
        int npcs = 0, own = 0, throughContainer = 0, speakersOwn = 0, speakersContainer = 0, inReach = 0, stock = 0, stockCoins = 0, stockPiles = 0;
        var multipliers = new SortedDictionary<int, int>();
        var perMap = new List<(string Map, int Count)>();
        var detachedPerMap = new List<(string Map, int Count)>();
        foreach (var map in WorldCatalog.Open(stack).Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            TerrainLoadResult result;
            try
            {
                result = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", module,
                    "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
                    "--game-root", root, "--area-map", map.Name]), default);
            }
            catch (Exception error) when (error is InvalidOperationException or Arcanum.Data.Area.AreaException) { continue; }
            if (Arcanum.Data.Inventory.AreaInventoryReader.Read(result.Area!.Snapshot).Inventory is not { } inventory) continue;
            if (inventory.Detached.Count > 0) detachedPerMap.Add((map.Name, inventory.Detached.Count));
            var speakers = AreaConversationHost.FindSpeakers(result.Area.Snapshot).Select(s => s.Row.Key).ToHashSet();
            bool Buys(int set) => data.BuyList(set).Status is InventorySourceBuyStatus.Listed or InventorySourceBuyStatus.Everything;
            var count = 0;
            foreach (var npc in inventory.Holders.Where(h => h.Type == Arcanum.Data.Inventory.AreaInventoryHolderType.Npc))
            {
                npcs++;
                var container = npc.SubstituteInventory?.SourceIndex is { } index && inventory.TryGetHolder(index, out var found) ? found : null;
                var speaker = speakers.Contains(npc.Key);
                if (Buys(npc.InventorySource)) { own++; if (speaker) speakersOwn++; }
                else if (container is not null && Buys(container.InventorySource)) { throughContainer++; if (speaker) speakersContainer++; }
                else continue;
                if (container is { WorldX: { } cx, WorldY: { } cy } && npc is { Leader: null, WorldX: { } nx, WorldY: { } ny } &&
                    Math.Max(Math.Abs(cx - nx), Math.Abs(cy - ny)) <= AreaBarterMerchant.SubstituteRange)
                {
                    inReach++;
                    var items = inventory.ListOf(container.SourceIndex);
                    stock += items.Count;
                    stockPiles += items.Count(item => item.Type == Arcanum.Data.Inventory.AreaItemType.Gold);
                    stockCoins += items.Where(item => item.Type == Arcanum.Data.Inventory.AreaItemType.Gold).Sum(item => item.Quantity ?? 0);
                }
                count++;
                if (CombatFieldReader.Read(result.Area.Snapshot.Objects.Single(o => o.Key == npc.Key)).Fields?.Npc is { } fields)
                    multipliers[fields.RetailPriceMultiplier] = multipliers.GetValueOrDefault(fields.RetailPriceMultiplier) + 1;
            }
            if (count > 0) perMap.Add((map.Name, count));
        }
        var merchants = own + throughContainer;
        Console.WriteLine($"NPCs {npcs}; merchants {merchants} on {perMap.Count} maps: own buy list {own} ({speakersOwn} dialog speakers), through the substitute container {throughContainer} ({speakersContainer} speakers)");
        Console.WriteLine($"multipliers: {string.Join(", ", multipliers.Select(p => $"{p.Key}×{p.Value}"))}");
        Console.WriteLine($"containers in reach {inReach}: {stock} items, {stockPiles} gold piles of {stockCoins} coins");
        Console.WriteLine("held but unlisted (detached) items per map: " + string.Join(", ", detachedPerMap.Select(p => $"{p.Map} {p.Count}")));
        Console.WriteLine("most: " + string.Join(", ", perMap.OrderByDescending(p => p.Count).Take(8).Select(p => $"{p.Map} {p.Count}")));
        // First observation 2026-10-28: no merchant buys through its own source; four (wheel clan 3, shopmap 1) through their containers.
        // Since ARC-DEV-085 (MIG6b, #614) a held item that no holder lists no longer refuses a map's inventory view (it is detached), so the
        // maps that this census skipped for that refusal read: qintarra (one detached item) adds four merchants through their containers (eight in all); its
        // four containers hold 109 items and 4,796 coins. Re-observed 2026-11-07 (ARC-DEV-166).
        Assert.AreEqual((0, 8), (own, throughContainer), "the census changed: update the barter contract");
        // ARC-WORLD-090, first observation 2026-10-28: all four containers lay in reach of a merchant without a leader; since #614 all eight do
        // (the four of qintarra with 109 items, 4 gold piles of 4,796 coins).
        Assert.AreEqual((8, 222, 8, 9881), (inReach, stock, stockPiles, stockCoins), "the census changed: update the barter contract");
    }
}
