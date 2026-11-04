using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-038: the opening shop's merchant over an invented shop map (entities, cells, sets, prices and prototypes are invented): the
// merchant search, the shop's session (the clear, the chest's regeneration, the barter open, the restock schedule) and the game's start
// on it (the bought weapon wielded, the teleport to the start map).
[TestClass]
public sealed class AreaShopMerchantTests
{
    private const int KitSword = 4030, Axe = 4040, ShopMap = 14;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    private static AreaObjectRecord Row(int index, int type, long x, long y) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static SessionItemFacts Facts(SessionItemType type, int prototype, int? quantity = null) => new(new(type, prototype, 4200, 34200), 10, 0, 0, quantity, []);

    // 4 an NPC switched off (first in the seed), 0 the merchant at (31,25) holding a dagger (2) and a persistent amulet (3), 1 its chest at
    // (33,25) (source 5) holding old coins (5), 6 a clerk after it.
    private static AreaInventoryMap Map(int runtime = ShopMap) => new(runtime, new SessionMapInventorySeed(
            [
                new(Entity(4), SessionHolderKind.Npc, false, false, [], []),
                new(Entity(0), SessionHolderKind.Npc, false, true, [], [Entity(2), Entity(3)]),
                new(Entity(1), SessionHolderKind.Container, false, false, [], [Entity(5)]),
                new(Entity(6), SessionHolderKind.Npc, false, false, [], []),
            ],
            [new(Entity(2), Facts(SessionItemType.Weapon, KitSword), null), new(Entity(3), Facts(SessionItemType.Armor, 2805), null),
             new(Entity(5), Facts(SessionItemType.Gold, AreaBarterData.GoldPrototype, 30), null)]),
        new[] { Row(0, 16, 31, 25), Row(1, 2, 33, 25), Row(2, 5, 31, 25), Row(3, 6, 31, 25), Row(4, 16, 30, 25), Row(5, 8, 33, 25), Row(6, 16, 35, 25) }
            .ToDictionary(row => Entity(row.Key.SourceIndex)),
        substitutes: new Dictionary<string, AreaSubstituteContainer> { [Entity(0)] = new(Entity(1), 5, new(33, 25)) },
        restock: new Dictionary<string, AreaRestockSeed> { [Entity(1)] = new(5, 0x1) }, persistent: [Entity(3)]);

    private static CombatFields Npc(uint flags = 0)
    {
        var stats = new int[28];
        for (var i = 0; i < 8; i++) stats[i] = 8;
        return new(CombatFieldOrigin.AreaObject, new CombatObjectFields(16, 17102, flags, 0, 0, 0, 0, 1, 2, 0, 4, [0, 0, 0, 0, 0], 0),
            null, null, null, null, null, new CombatCritterFields(0, 0, stats, new int[12], new int[4], new int[25], 0, 0, 0, 1, 0, 0),
            new CombatNpcFields(0, 0, 0, 0, 0, 0, 0, [new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)], RetailPriceMultiplier: 100));
    }

    private static CombatFields? Read(AreaObjectRecord row) => row.Key.SourceIndex switch
    {
        4 => Npc(flags: 0x2),
        0 or 6 => Npc(),
        _ => null,
    };

    private static CombatFields Weapon(int prototype, int worth) =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Weapon, prototype, 0, 0, 0, 0, 0, 40, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 10, worth, 0, 0, 0, 0, [10000, 10000, 10000, 10000, 10000], 0, 0, 0, 0),
            new CombatWeaponFields(CombatWeaponFlags.None, CombatWeaponSkill.Melee, 0, 0, [new(1, 4), new(0, 0), new(0, 0), new(0, 0), new(0, 0)],
                [0, 0, 0, 0, 0], 11, 0, 1, 0, 0, 0, CombatFieldReader.NoAmmo, 0, 0, 0, 0, 0, 0, 0), null, null, null, null, null);

    private static CombatFields Gold() =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields((int)SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0, 3200, 33200, 0, 1, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 0, 1, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    // Set 5 (the chest's) generates 25 coins and an axe (basic 4020).
    private static AreaBarterData Data() => AreaBarterData.Create(Mes("{1}{A: 1,1}{2}{B: 1,1}{3}{C: 1,1}{4}{D: 1,1}{5}{Chest: 25,25 100,4020}"), null,
        new Dictionary<int, CombatFields> { [KitSword] = Weapon(KitSword, 100), [Axe] = Weapon(Axe, 400), [AreaBarterData.GoldPrototype] = Gold() });

    private static AreaShopMap Shop() => AreaShopMap.From(new MapListEntry(5013, "ShopMap", new(31, 29)) { Type = MapListType.Shopping }, Map(), Read).Map!;

    // The new character's session on the shop map's start (as the viewer makes it: the kit hero, the shop map's runtime map and start).
    private static GameSessionOwner Session(AreaShopMap map)
    {
        var hero = new SessionHeroDefinition(16086, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 1000, [new SessionHeroKitItem(new(SessionItemType.Weapon, KitSword, 4100, 34100))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        return AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            new(map.Start.X, map.Start.Y), map.RuntimeMap, load).Owner!;
    }

    [TestMethod]
    public void The_merchant_is_the_first_npc_of_the_seed_that_is_neither_destroyed_nor_off()
    {
        var entry = new MapListEntry(5013, "ShopMap", new(31, 29)) { Type = MapListType.Shopping };
        var shop = AreaShopMap.From(entry, Map(), Read);
        Assert.AreEqual(AreaShopMapStatus.Ready, shop.Status, shop.Detail);
        Assert.AreEqual(("ShopMap", ShopMap, 31, 29, Entity(0)), (shop.Map!.Name, shop.Map.RuntimeMap, shop.Map.Start.X, shop.Map.Start.Y, shop.Map.Merchant),
            "the NPC switched off before it is left out (0x440B40)");
        Assert.AreEqual(AreaShopMapStatus.NoMerchant, AreaShopMap.From(entry, Map(), _ => Npc(flags: 0x1)).Status, "every NPC destroyed");
        Assert.AreEqual(AreaShopMapStatus.Failed, AreaShopMap.From(entry, Map(), row => row.Key.SourceIndex == 4 ? null : Read(row)).Status,
            "an NPC before the merchant whose fields do not read: the project cannot tell whether it is the first");
        Assert.AreEqual(AreaShopMapStatus.Failed, AreaShopMap.From(entry with { Start = null }, Map(), Read).Status, "no start tile");
        Assert.AreEqual(AreaShopMapStatus.Failed, AreaShopMap.From(entry, Map(runtime: 3), Read).Status, "another runtime map");
    }

    [TestMethod]
    public void The_shop_clears_the_merchant_restocks_its_chest_and_opens_barter_on_the_shop_map()
    {
        var map = Shop();
        var data = Data();
        var owner = Session(map);
        // The merchant met the PC before (the original's shop makes it forget, 0x4C0F50).
        var state = new AreaConversationState(owner, new(ShopMap), [map.Merchant], 50);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndReactionConversation(owner.Version, map.Merchant, state.NpcFacts()).Disposition);
        Assert.IsTrue(owner.QueryReaction(map.Merchant, state.NpcFacts(), state.PcFacts).View!.MetBefore);
        using var shop = AreaShopSession.Open(owner, map, data, (entity, _) => entity, "Smith", out var refused) ?? throw new AssertFailedException(refused ?? "refused");
        Assert.IsFalse(owner.QueryReaction(map.Merchant, state.NpcFacts(), state.PcFacts).View!.MetBefore, "the merchant forgot the PC");
        Assert.AreEqual((SessionRestockOutcome.Cleared, 1), (shop.MerchantRegeneration.Regeneration!.Outcome, shop.MerchantRegeneration.Regeneration.Removed),
            "the merchant has no source: its dagger goes, the persistent amulet stays (RK4, RK5)");
        Assert.IsTrue(owner.IsItemDestroyed(Entity(2)) && !owner.IsItemDestroyed(Entity(3)));
        Assert.AreEqual((SessionRestockOutcome.Regenerated, 25), (shop.ContainerRegeneration!.Regeneration!.Outcome, shop.ContainerRegeneration.Regeneration.Coins));
        Assert.AreEqual((Entity(1), 25, 1), (shop.Barter.Merchant.Container, shop.Opened.Coins, shop.Opened.Piles), "the chest's new coins move to the merchant");
        Assert.AreEqual(Entity(1), owner.QueryRestocks().Single().Holder, "the window's open schedules the chest (RK2, RK9)");
        var view = shop.Barter.View();
        Assert.AreEqual((1000, 25), (view.HeroGold, view.MerchantGold));
        var axe = view.Wares.Single(line => line.Kind.Prototype == Axe);
        Assert.IsTrue(shop.Barter.Buy(axe.Entity).Moved);
        Assert.AreEqual(1000 - axe.Quote.Price, owner.QueryGold());
        Assert.IsNotNull(shop.Wearable(axe.Entity), "a bought ware the chest made reads its prototype's wear facts");
        Assert.IsNull(shop.Wearable("kit:1"), "a kit item's facts are the kit's");
        // The game's start: the best weapon (the axe: the kit sword has no facts here) and the teleport to the start map's tile.
        Assert.IsNull(shop.Start([null], new(1), new(500, 600)));
        Assert.AreEqual((1, 500, 600), (owner.QueryActor().Map.RuntimeMap, owner.QueryActor().Cell.X, owner.QueryActor().Cell.Y));
        Assert.AreEqual(axe.Entity, owner.QueryEquipment().Worn.Single().Entity, "the bought axe is wielded (OS5)");
        Assert.AreSame(owner, shop.Take());
        Assert.IsNull(shop.Owner);
        shop.Dispose();
        Assert.AreEqual(1000 - axe.Quote.Price, owner.QueryGold(), "a taken session outlives the shop");
        owner.Dispose();
    }

    [TestMethod]
    public void A_shop_session_not_on_the_shop_map_is_refused_and_the_shop_drops_its_session()
    {
        var map = Shop();
        using var elsewhere = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(), new(31, 29), 1).Owner!;
        Assert.IsNull(AreaShopSession.Open(elsewhere, map, Data(), (entity, _) => entity, "Smith", out var refused));
        StringAssert.Contains(refused, "not the shop map's");
        var owner = Session(map);
        var shop = AreaShopSession.Open(owner, map, Data(), (entity, _) => entity, "Smith", out _)!;
        shop.Dispose();
        Assert.IsNull(shop.Owner);
        Assert.Throws<ObjectDisposedException>(() => owner.QueryGold(), "the shop's Back disposes its session");
    }

    /// <summary>Opt-in (DAT_ROOT = the game folder): the installed map list's shop map, its merchant and the shop's session on it. First
    /// observation 2026-10-28.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Original_shop_map_and_its_merchant()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var module = Path.Combine(root, "modules", "Arcanum.dat");
        using (var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!)
        {
            var list = MapList.Read(stack);
            Assert.AreEqual(("Arcanum1-024-fixed", "ShopMap"), (list.OfType(MapListType.Start)?.Name, list.OfType(MapListType.Shopping)?.Name));
            Assert.AreEqual(2, list.Entries.Count(entry => entry.Type != MapListType.None), "two typed rows (CC-01)");
        }
        var launch = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", module, "--resource", Path.Combine(root, "arcanum2.dat"),
            "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", "Arcanum1-024-fixed"]);
        var load = AreaShopMap.Load(launch);
        Assert.AreEqual(AreaShopMapStatus.Ready, load.Status, load.Detail);
        var map = load.Map!;
        var npcs = map.Inventory.Seed.Holders.Count(holder => holder.Kind == SessionHolderKind.Npc);
        var substitute = map.Inventory.Substitute(map.Merchant);
        Console.WriteLine($"shop map {map.Name} runtime {map.RuntimeMap} start {map.Start}; NPCs {npcs}; merchant {map.Merchant} at ({map.MerchantRow.WorldX},{map.MerchantRow.WorldY}) "
            + $"multiplier {map.MerchantFields.Npc!.RetailPriceMultiplier} own source {map.MerchantFields.Critter?.InventorySource}; "
            + $"container {substitute?.Entity} source {substitute?.InventorySource} at {substitute?.Cell}; merchant list {map.Inventory.Seed.Holders.Single(h => h.Entity == map.Merchant).Items.Count}");
        Assert.AreEqual((14, 31, 29, 19), (map.RuntimeMap, map.Start.X, map.Start.Y, substitute?.InventorySource), "the census's shop merchant (container source 19)");
        var data = AreaBarterData.Load(module).Data!;
        var owner = Session(map);
        using var shop = AreaShopSession.Open(owner, map, data, (entity, _) => entity, "merchant", out var refused) ?? throw new AssertFailedException(refused ?? "refused");
        var view = shop.Barter.View();
        Console.WriteLine($"merchant regeneration {shop.MerchantRegeneration.Regeneration?.Outcome} removed {shop.MerchantRegeneration.Regeneration?.Removed}; "
            + $"container {shop.ContainerRegeneration?.Regeneration?.Outcome} removed {shop.ContainerRegeneration?.Regeneration?.Removed} coins {shop.ContainerRegeneration?.Regeneration?.Coins} "
            + $"created {shop.ContainerRegeneration?.Regeneration?.Created.Count} skipped {shop.ContainerRegeneration?.Regeneration?.Skipped}; moved {shop.Opened.Coins}; "
            + $"wares {view.Wares.Count}, merchant gold {view.MerchantGold}, hero lines {view.Hero.Count}");
        Assert.AreEqual(SessionRestockOutcome.Regenerated, shop.ContainerRegeneration?.Regeneration?.Outcome);
        Assert.IsTrue(view.Wares.Count > 0, "the chest's set made wares");
    }
}
