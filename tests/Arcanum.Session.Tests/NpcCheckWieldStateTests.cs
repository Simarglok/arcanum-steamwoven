using Arcanum.Simulation.Ai;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-1284 (ledger NT3, VP1, VP3, VP5, VPG1): the item insert (0x466640) and the removal (0x467860) set NPC flag 0x40 (ONF_CHECK_WIELD) on an NPC holder, so its next AI gate re-wields.
// A container, the hero and a merged gold or ammunition stack set none (the merge returns before the flag). Over the synthetic seed of InventoryStateTests and the half-elf hero. Numbers are invented.
[TestClass]
public sealed class NpcCheckWieldStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static bool Marked(GameSessionOwner owner, string npc) => owner.QueryDanger(npc).Flags.HasFlag(DangerFlags.CheckWield);

    private static void Changed(InventoryMutationResult result)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
    }

    // The NPC's next AI pass clears the bit (its gate's re-wield step), here with a host that wields nothing.
    private static void Clear(GameSessionOwner owner, string npc)
    {
        Assert.AreEqual(GameSessionFailure.None, owner.RunAiPass(owner.Version, npc, new AiSchedulerTests.World().With(npc)).Failure);
        Assert.IsFalse(Marked(owner, npc));
    }

    [TestMethod]
    public void An_item_the_hero_gives_an_npc_marks_it_in_the_same_revision()
    {
        using var owner = Hero();
        Assert.IsFalse(Marked(owner, "virgil"));
        var revision = owner.Version.Revision;
        Changed(owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", Accept));
        Assert.IsTrue(Marked(owner, "virgil"));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision: the bit rides with the move");
        Assert.AreEqual(DangerFlags.CheckWield, owner.QueryDanger("virgil").Flags, "no other bit");
        // The bit is part of the saved NPC state.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())), RestoreOptions());
        Assert.IsTrue(Marked(restored, "virgil"));
    }

    [TestMethod]
    public void A_refused_give_marks_nothing()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionFailure.InsertRefused, owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", _ => SessionInsertVerdict.NoRoom).Failure);
        Assert.IsFalse(Marked(owner, "virgil"));
    }

    [TestMethod]
    public void An_item_taken_from_an_npc_or_stolen_marks_the_giver_and_not_the_hero()
    {
        using var owner = Hero();
        Changed(owner.TakeItemFromNpc(owner.Version, "virgil", "sword", Accept));
        Assert.IsTrue(Marked(owner, "virgil"));
        Assert.IsFalse(Marked(owner, "merchant"));
    }

    [TestMethod]
    public void The_barter_sale_and_purchase_mark_the_merchant()
    {
        using var owner = Hero();
        Changed(owner.BarterSell(owner.Version, "merchant", "kit:1", 1, 0, Bullets, Accept));
        Assert.IsTrue(Marked(owner, "merchant"), "a sale inserts into the merchant's list");
        Clear(owner, "merchant");
        Assert.IsFalse(Marked(owner, "merchant"));
        Changed(owner.BarterBuy(owner.Version, "merchant", "kit:1", 1, 0, Bullets, GoldPile, Accept, Accept));
        Assert.IsTrue(Marked(owner, "merchant"), "a purchase removes from it");
    }

    [TestMethod]
    public void A_sale_into_a_container_marks_no_one()
    {
        using var owner = Hero();
        Changed(owner.BarterSell(owner.Version, "merchant", "kit:1", 1, 0, Bullets, Accept, container: "chest"));
        Assert.IsFalse(Marked(owner, "chest"));
        Assert.IsFalse(Marked(owner, "merchant"), "the NPC's own list did not change");
    }

    [TestMethod]
    public void Loot_from_a_body_marks_nothing_as_no_pass_runs_on_it_and_a_container_loot_marks_no_one()
    {
        using var owner = Hero();
        Changed(owner.LootItem(owner.Version, "corpse", "helmet", Accept));
        Assert.IsFalse(Marked(owner, "corpse"), "a dead NPC keeps no record of the bit");
        Assert.AreEqual(0, owner.QueryDangerRecords().Records.Count);
        Changed(owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", Accept));
        Changed(owner.TakeItemFromNpc(owner.Version, "virgil", "kit:1", Accept));
        Assert.IsTrue(Marked(owner, "virgil"), "a living NPC is marked");
    }

    [TestMethod]
    public void Gold_moved_between_npcs_the_last_ammunition_unit_spent_and_a_stack_merged_into_the_heros_mark_the_giver()
    {
        using var owner = Hero();
        Changed(owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "virgil", 5, GoldPile, Accept));
        Assert.IsTrue(Marked(owner, "virgil"), "a new pile entered virgil's list");
        Assert.IsTrue(Marked(owner, "merchant"), "the emptied pile left the merchant's");
        var stack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 3)).Item!;
        Clear(owner, "virgil");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SpendAmmo(owner.Version, new SessionAmmoHolder("virgil"), 1, 3).Disposition);
        Assert.IsTrue(owner.IsItemDestroyed(stack));
        Assert.IsTrue(Marked(owner, "virgil"), "the spent stack was destroyed out of the list");
    }

    [TestMethod]
    public void Gold_marks_only_when_a_pile_enters_or_leaves_a_list_not_when_a_pile_grows_or_shrinks()
    {
        using var owner = Hero();
        Changed(owner.GiveGoldToNpc(owner.Version, "merchant", 10, GoldPile, Accept));
        Assert.IsFalse(Marked(owner, "merchant"), "a gold stack merges into the held pile");
        Changed(owner.TakeGoldFromNpc(owner.Version, "merchant", 3));
        Assert.IsFalse(Marked(owner, "merchant"), "a part of a pile leaves no item");
        Changed(owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Accept));
        Assert.IsTrue(Marked(owner, "virgil"), "a new pile is inserted");
        Changed(owner.TakeGoldFromNpc(owner.Version, "merchant", 12));
        Assert.IsTrue(Marked(owner, "merchant"), "the emptied pile is destroyed out of the list (0x43CCA0 → 0x467860)");
    }

    [TestMethod]
    public void Ammunition_marks_only_when_a_stack_enters_or_leaves_a_list()
    {
        using var owner = Hero();
        var stack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Assert.IsTrue(Marked(owner, "virgil"), "a stack created into the list is an insert");
        Clear(owner, "virgil");
        Changed(owner.TakeAmmoFromNpc(owner.Version, "virgil", stack, 20, Bullets, Accept));
        Assert.IsFalse(Marked(owner, "virgil"), "units leave a stack that stays");
        Changed(owner.GiveAmmoToNpc(owner.Version, "virgil", "made:2", 5, Bullets, Accept));
        Assert.IsFalse(Marked(owner, "virgil"), "the units join the held stack: the merge returns before the flag");
        Changed(owner.TakeAmmoFromNpc(owner.Version, "virgil", stack, 15, Bullets, Accept));
        Assert.IsTrue(Marked(owner, "virgil"), "the emptied stack is destroyed out of the list");
    }

    [TestMethod]
    public void An_item_created_into_or_deleted_from_an_npcs_list_marks_it_and_a_merged_ammunition_item_does_not()
    {
        using var owner = Hero();
        Changed(owner.AddItemToNpc(owner.Version, "virgil", new(SessionItemType.Generic, 5600, 3600, 33600), Accept));
        Assert.IsTrue(Marked(owner, "virgil"));
        Clear(owner, "virgil");
        Changed(owner.RemoveItemFromNpc(owner.Version, "virgil", "ring-copy"));
        Assert.IsTrue(Marked(owner, "virgil"));
        Clear(owner, "virgil");
        Assert.IsTrue(owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item is not null);
        Clear(owner, "virgil");
        Changed(owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 10)));
        Assert.IsFalse(Marked(owner, "virgil"), "merged into the first stack of the prototype");
    }

    [TestMethod]
    public void A_full_danger_record_budget_leaves_the_move_unmarked_and_unharmed()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), new GameSessionLimits(maxDangerRecords: 1));
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Changed(owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", Accept));
        Assert.IsTrue(Marked(owner, "virgil"));
        Changed(owner.GiveItemToNpc(owner.Version, "merchant", "kit:2", Accept));
        Assert.IsFalse(Marked(owner, "merchant"), "no record is added at the bound; the move itself is not refused");
    }
}
