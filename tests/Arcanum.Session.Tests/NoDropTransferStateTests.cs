using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-1283 (ledger NT2, VP5, VPG2): the no-drop item flag (0x20) refuses every transfer between holders at the item's remove check (0x466DA0), after the
// recipient's transfer script and before the insert check, with item.mes 111 for the PC: the loot of a body, a container and a junk pile, the take from a living NPC
// (a steal, a purchase), the hero's give, sale and plant, and the dialog's negative IN (which lays the item at the PC's feet, the original's fallback). Entities
// and numbers are invented, over the synthetic seed of InventoryStateTests.
[TestClass]
public sealed class NoDropTransferStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static SessionItemFacts Sticky(SessionItemType type, int name, int location = 0) => Facts(type, name, location) with { NoDrop = true };

    // The seed with a no-drop item in each kind of holder: a dead body (a plain one and a worn one), a living NPC, a container, and a no-drop item on the ground.
    private static SessionMapInventorySeed NoDropSeed()
    {
        var seed = Seed();
        SessionSeedItem Held(string entity, SessionItemFacts facts) => new(entity, facts, null);
        var items = seed.Items.Select(item => item.Entity == "helmet" ? item with { Facts = item.Facts with { NoDrop = true } } : item).Concat(
        [
            Held("locket", Sticky(SessionItemType.Generic, 5700)),
            Held("trinket", Facts(SessionItemType.Generic, 5701)),
            Held("relic", Sticky(SessionItemType.Generic, 5702)),
            Held("bar", Sticky(SessionItemType.Generic, 5703)),
            Held("gem", Facts(SessionItemType.Generic, 5704)),
            new SessionSeedItem("idol", Sticky(SessionItemType.Generic, 5705), new CellCoord(110, 200)),
        ]).ToArray();
        var holders = seed.Holders.Select(holder => holder.Entity switch
        {
            "corpse" => holder with { Items = [.. holder.Items, "locket", "trinket"] },
            "virgil" => holder with { Items = [.. holder.Items, "relic"] },
            "chest" => holder with { Items = ["bar", "gem"] },
            _ => holder,
        }).ToArray();
        return seed with { Items = items, Holders = holders };
    }

    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, NoDropSeed()).Failure);
        return owner;
    }

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    private static void Refused(GameSessionOwner owner, InventoryMutationResult result, long revision, string why)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemNotDroppable, (string?)null, (SessionInsertVerdict?)null),
            (result.Disposition, result.Failure, result.Item, result.Verdict), why);
        Assert.AreEqual(111, result.PcMessage, why + ": message 111 (100 + code 11, 0x4673F0) for the PC");
        Assert.AreEqual(revision, owner.Version.Revision, why + ": nothing changed");
    }

    [TestMethod]
    public void A_body_refuses_to_give_up_a_no_drop_item_worn_or_not_and_keeps_its_other_items_lootable()
    {
        using var owner = Hero();
        var insertCalls = 0;
        SessionInsertVerdict Counting(SessionInventoryItem _) { insertCalls++; return SessionInsertVerdict.Accepted; }
        Refused(owner, owner.LootItem(owner.Version, "corpse", "locket", Counting), 0, "a plain no-drop item of a body");
        Refused(owner, owner.LootItem(owner.Version, "corpse", "helmet", Counting), 0, "the flag is tested first: a worn no-drop item is refused too");
        Assert.AreEqual(0, insertCalls, "the remove check runs before the insert check");
        CollectionAssert.AreEqual(new[] { "note", "boots", "torn", "helmet", "locket", "trinket" }, List(owner, "corpse"));
        var ok = owner.LootItem(owner.Version, "corpse", "trinket", Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "trinket", (int?)null), (ok.Disposition, ok.Failure, ok.Item, ok.PcMessage));
    }

    [TestMethod]
    public void A_standing_container_and_a_living_npc_refuse_a_no_drop_item_to_a_take_and_a_steal()
    {
        using var owner = Hero();
        Refused(owner, owner.LootItem(owner.Version, "chest", "bar", Accept), 0, "container loot");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "chest", "gem", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "bar" }, List(owner, "chest"));
        Refused(owner, owner.TakeItemFromNpc(owner.Version, "virgil", "relic", Accept), 1, "a take from a living NPC (a purchase's or a steal's move)");
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy", "relic" }, List(owner, "virgil"));
    }

    [TestMethod]
    public void A_barter_purchase_of_a_no_drop_item_pays_nothing()
    {
        using var owner = Hero();
        var gold = owner.QueryGold();
        Refused(owner, owner.BarterBuy(owner.Version, "virgil", "relic", 1, 10, Bullets, new(SessionItemType.Gold, 9056, 3200, 33200), Accept, Accept), 0, "barter buy");
        Assert.AreEqual(gold, owner.QueryGold(), "no coins moved");
    }

    [TestMethod]
    public void The_dialogs_negative_IN_of_a_no_drop_item_lays_it_at_the_PCs_feet_after_the_refusal()
    {
        // The original (0x415BA0 case 9, n < 0): 0x4617F0 fails at the remove check without touching the item; the item's parent is not the PC, so the item is removed
        // from the NPC (0x466E00 tests no flag) and laid on the PC's tile (0x466E50), as for any transfer that did not end with the PC.
        using var owner = Hero();
        var insertCalls = 0;
        var given = owner.GiveItemToPc(owner.Version, "virgil", 5702, _ => { insertCalls++; return SessionInsertVerdict.Accepted; });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "relic", (SessionInsertVerdict?)null), (given.Disposition, given.Failure, given.Item, given.Verdict));
        Assert.AreEqual(GameSessionOwner.NoDropMessage, given.PcMessage);
        Assert.AreEqual(0, insertCalls, "no insert check after the remove check's refusal");
        CollectionAssert.DoesNotContain(List(owner, "virgil"), "relic");
        Assert.AreEqual("relic", owner.QueryPlacedItems().Single().Entity);
        Assert.IsFalse(owner.QueryPcInventory().Any(item => item.Entity == "relic"));
        Assert.AreEqual(1L, owner.Version.Revision, "one revision for the removal and the placement");
    }

    [TestMethod]
    public void The_heros_no_drop_kit_item_is_not_sold_given_or_planted_and_a_taken_one_follows_its_seed_row()
    {
        using var owner = Hero();
        var kit = owner.QueryHeroKit().First();
        // Without the host's prototype flag a kit item has none (the older behaviour).
        owner.AttachItemNoDrop(null);
        var plain = owner.GiveItemToNpc(owner.Version, "virgil", kit.Entity, Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, plain.Disposition);
        Assert.IsNull(plain.PcMessage);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, "virgil", kit.Entity, Accept).Disposition, "back to the hero");
        var revision = owner.Version.Revision;
        owner.AttachItemNoDrop(prototype => prototype == kit.Kind.Prototype);
        Refused(owner, owner.GiveItemToNpc(owner.Version, "virgil", kit.Entity, Accept), revision, "give of a kit item");
        Refused(owner, owner.BarterSell(owner.Version, "virgil", kit.Entity, 1, 0, Bullets, Accept), revision, "sale of a kit item");
        Assert.IsTrue(owner.QueryHeroKit().Any(item => item.Entity == kit.Entity), "the item stays with the hero");

        // A taken item's flag is its seed row's: the idol lies on the ground (a pickup is no transfer between holders) and can then neither be given nor sold.
        owner.AttachItemNoDrop(null);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "idol", Accept).Disposition);
        revision = owner.Version.Revision;
        Refused(owner, owner.GiveItemToNpc(owner.Version, "virgil", "idol", Accept), revision, "give of a taken no-drop item");
        Refused(owner, owner.BarterSell(owner.Version, "virgil", "idol", 1, 0, Bullets, Accept), revision, "sale of a taken no-drop item");
        Refused(owner, owner.BarterSell(owner.Version, "virgil", "idol", 1, 0, Bullets, Accept, container: "chest"), revision, "sale into a container");
    }

    [TestMethod]
    public void A_made_item_in_an_npcs_list_is_refused_by_its_prototypes_flag()
    {
        using var owner = Hero();
        var kind = new SessionItemKind(SessionItemType.Generic, 5900, 5900, 35900);
        var made = owner.AddItemToNpc(owner.Version, "virgil", kind, Accept).Item!;
        owner.AttachItemNoDrop(prototype => prototype == kind.Prototype);
        var revision = owner.Version.Revision;
        Refused(owner, owner.TakeItemFromNpc(owner.Version, "virgil", made, Accept), revision, "a made item");
        owner.AttachItemNoDrop(null);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, "virgil", made, Accept).Disposition);
    }

    [TestMethod]
    public void A_pick_pocket_whose_move_the_remove_check_refuses_fails_without_being_noticed_and_leaves_both_lists()
    {
        var stats = HeroStateTests.Defaults(); stats[2] = 20;
        int[] basic = new int[12];
        basic[(int)Skill.PickPocket] = 20 | (int)SkillTraining.Master << 6;
        var hero = HeroStateTests.HalfElf(stats) with { Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] } };
        var created = GameSessionOwner.Create(Descriptor() with { Hero = hero });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        using var owner = created.Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, NoDropSeed()).Failure);
        var steal = owner.PickPocket(owner.Version, new("virgil") { Perception = 0 }, PickPocketDirection.Steal, "relic", default, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, true, 0, false), (steal.Disposition, steal.Check!.Success, steal.Moved, steal.Noticed));
        Assert.AreEqual((GameSessionFailure.ItemNotDroppable, GameSessionOwner.NoDropMessage), (steal.Move!.Value.Failure, steal.Move.Value.PcMessage));
        CollectionAssert.Contains(List(owner, "virgil"), "relic");
        Assert.IsFalse(owner.QueryPcInventory().Any(item => item.Entity == "relic"));
    }
}
