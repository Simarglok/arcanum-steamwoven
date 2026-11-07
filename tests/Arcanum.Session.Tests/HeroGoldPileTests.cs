using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-090 (ledger HG1): the hero's gold is a pile in its inventory grid, the record `gold:pc`. Over the synthetic seed of InventoryStateTests (virgil
// with no coins) and the half-elf hero (385 gold, a kit of three items). Entities and numbers are invented.
[TestClass]
public sealed class HeroGoldPileTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9076, 3200, 33200);
    private static readonly SessionGridMoveItem GoldFacts = new(SessionHeroGrid.GoldKind, false, []);

    private static GameSessionOwner Hero(int gold = 385)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf() with { Gold = gold });
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static string Layout(GameSessionOwner owner) =>
        string.Join(" ", owner.QueryHeroGrid().Items.Select(item => $"{item.Entity}@{item.Slot}:{item.Width}x{item.Height}"));

    [TestMethod]
    public void A_hero_with_coins_holds_the_pile_after_its_kit_and_one_without_holds_none()
    {
        using var rich = Hero();
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1 kit:3@2:1x1 gold:pc@3:1x1", Layout(rich), "the pile takes the next free cell, as an item entering the list does");
        Assert.AreEqual(0, rich.QueryHeroGrid().Unplaced.Count);
        using var poor = Hero(0);
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1 kit:3@2:1x1", Layout(poor));
    }

    [TestMethod]
    public void The_pile_appears_with_the_first_coin_and_goes_with_the_last()
    {
        using var owner = Hero(0);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToHero(owner.Version, 15).Disposition);
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1 kit:3@2:1x1 gold:pc@3:1x1", Layout(owner));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToNpc(owner.Version, "virgil", 15, GoldPile, Accept).Disposition);
        Assert.AreEqual(0, owner.QueryGold());
        Assert.IsFalse(owner.QueryHeroGrid().Items.Any(item => item.Entity == SessionHeroGrid.GoldEntity), "the pile is destroyed with its last coin (NT1), its cell frees");
        // Coins that come back take the first free cell, even before a later item's.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToHero(owner.Version, 3).Disposition);
        Assert.AreEqual(3, owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot);
    }

    [TestMethod]
    public void More_coins_keep_the_pile_where_it_lies()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveHeroItem(owner.Version, SessionHeroGrid.GoldEntity, 57, GoldFacts, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToHero(owner.Version, 20).Disposition);
        Assert.AreEqual(57, owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot, "the quantity grows, the cell stays");
        Assert.AreEqual(405, owner.QueryGold());
    }

    [TestMethod]
    public void The_pile_moves_to_a_free_cell_and_a_taken_one_refuses()
    {
        using var owner = Hero();
        var before = owner.Version;
        var moved = owner.MoveHeroItem(owner.Version, SessionHeroGrid.GoldEntity, 11, GoldFacts, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (moved.Disposition, moved.Failure));
        Assert.AreEqual("kit:1@0:1x1 kit:2@1:1x1 kit:3@2:1x1 gold:pc@11:1x1", Layout(owner));
        Assert.AreEqual(before.Revision + 1, owner.Version.Revision, "one revision");
        Assert.AreEqual(385, owner.QueryGold(), "a move changes no coin");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MoveHeroItem(owner.Version, SessionHeroGrid.GoldEntity, 11, GoldFacts, Accept).Disposition, "its own cell");
        Assert.AreEqual(GameSessionFailure.GridCellsTaken, owner.MoveHeroItem(owner.Version, SessionHeroGrid.GoldEntity, 2, GoldFacts, Accept).Failure, "kit:3's cell");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.MoveHeroItem(owner.Version, SessionHeroGrid.GoldEntity, 120, GoldFacts, Accept).Failure, "outside the grid");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.MoveHeroItem(new(owner.Identity, 99), SessionHeroGrid.GoldEntity, 12, GoldFacts, Accept).Failure);
        using var poor = Hero(0);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, poor.MoveHeroItem(poor.Version, SessionHeroGrid.GoldEntity, 12, GoldFacts, Accept).Failure, "no coin, no pile");
    }

    [TestMethod]
    public void The_pile_round_trips_in_the_grid_and_a_restore_refuses_a_pile_without_coins()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveHeroItem(owner.Version, SessionHeroGrid.GoldEntity, 40, GoldFacts, Accept).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "{\"entity\":\"gold:pc\",\"slot\":40,\"width\":1,\"height\":1}");
        using (var restored = Restore(Decode(text)))
        {
            Assert.AreEqual(Layout(owner), Layout(restored));
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        }
        var noCoins = text.Replace("\"gold\":385,", "\"gold\":0,");
        Assert.AreEqual(GameSessionFailure.InvalidGrid, GameSessionOwner.Restore(Decode(noCoins), RestoreOptions()).Failure, "a pile of no coin does not exist");
    }

    [TestMethod]
    public void An_attached_art_source_sizes_the_pile_with_the_gold_kind()
    {
        using var owner = Hero();
        var asked = new List<(string Entity, SessionItemKind Kind)>();
        owner.AttachItemArtSizes((entity, kind) =>
        {
            asked.Add((entity, kind));
            return entity == SessionHeroGrid.GoldEntity ? (64u, 33u) : (32u, 32u);
        });
        Assert.IsTrue(asked.Contains((SessionHeroGrid.GoldEntity, SessionHeroGrid.GoldKind)), "the host is asked for the pile by its kind (prototype file 9076)");
        var pile = owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity);
        Assert.AreEqual((2, 2), (pile.Width, pile.Height), "(pixels - 1) / 32 + 1 per side");
        Assert.AreEqual(385, owner.QueryGold());
    }

    private const string Pile = ",{\"entity\":\"gold:pc\",\"slot\":3,\"width\":1,\"height\":1}";

    [TestMethod]
    public void A_schema_74_save_with_coins_gets_the_pile_in_the_first_free_cell()
    {
        // The golden hero holds 385 coins and the kit in cells 0..2; the schema 74 text is the same without the pile.
        var current = HeroStateTests.HeroGolden;
        StringAssert.Contains(current, Pile);
        var older = SnapshotUpgradeTests.Schema74(current.Replace(Pile, ""));
        StringAssert.Contains(older, "\"schemaVersion\":74,");
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.Snapshot!.SchemaVersion);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded.Snapshot), "the pile takes cell 3, the first free one");

        // A hole before the pile's place is used first.
        var hole = current.Replace("{\"entity\":\"kit:2\",\"slot\":1,\"width\":1,\"height\":1},", "").Replace(Pile, "");
        var holed = GameSessionSnapshotCodec.TryDecode(SnapshotUpgradeTests.Schema74(hole));
        Assert.AreEqual(SnapshotDecodeFailure.None, holed.Failure, holed.Path);
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(holed.Snapshot!), "{\"entity\":\"kit:1\",\"slot\":0,\"width\":1,\"height\":1},{\"entity\":\"gold:pc\",\"slot\":1,\"width\":1,\"height\":1},{\"entity\":\"kit:3\"");

        // A text that lists the pile already is kept; one with no coin gets none.
        var listed = GameSessionSnapshotCodec.TryDecode(SnapshotUpgradeTests.Schema74(current));
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(listed.Snapshot!));
        var poor = GameSessionSnapshotCodec.Encode(HeroStateTests.CreateWith(HeroStateTests.HalfElf() with { Gold = 0 }).Capture());
        var poorOlder = GameSessionSnapshotCodec.TryDecode(SnapshotUpgradeTests.Schema74(poor));
        Assert.AreEqual(poor, GameSessionSnapshotCodec.Encode(poorOlder.Snapshot!));
        // The chain from the oldest readable schema reaches the same state.
        var chain = GameSessionSnapshotCodec.TryDecode(SnapshotUpgradeTests.Schema73(current.Replace(Pile, "")));
        Assert.AreEqual(SnapshotDecodeFailure.None, chain.Failure, chain.Path);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(chain.Snapshot!));
    }

    [TestMethod]
    public void An_item_cannot_take_the_pile_s_cell_and_a_pile_without_a_cell_waits_for_one()
    {
        using var owner = Hero();
        var kit = new SessionGridMoveItem(owner.QueryHeroKit().First(item => item.Entity == "kit:1").Kind, false, []);
        Assert.AreEqual(GameSessionFailure.GridCellsTaken, owner.MoveHeroItem(owner.Version, "kit:1", 3, kit, Accept).Failure, "the pile's cell is taken");
        // A save of a hero with coins whose grid lists no pile restores; the pile takes a cell at the next change (a restore places nothing).
        var text = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace(",{\"entity\":\"gold:pc\",\"slot\":3,\"width\":1,\"height\":1}", "");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(385, restored.QueryGold());
        CollectionAssert.AreEqual(new[] { SessionHeroGrid.GoldEntity }, restored.QueryHeroGrid().Unplaced.ToArray(), "waiting outside the grid");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, restored.MoveHeroItem(restored.Version, SessionHeroGrid.GoldEntity, 9, GoldFacts, Accept).Failure, "not placed yet");
        Assert.AreEqual(GameSessionDisposition.Changed, restored.GiveGoldToHero(restored.Version, 1).Disposition);
        Assert.AreEqual(3, restored.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot, "placed at the next change");
    }

    [TestMethod]
    public void The_upgrade_step_steps_over_a_larger_item_and_a_full_grid_leaves_the_pile_waiting()
    {
        var current = HeroStateTests.HeroGolden;
        // A 2 x 2 item at the head of the grid: the first free cell is 2.
        var big = current.Replace("{\"entity\":\"kit:1\",\"slot\":0,\"width\":1,\"height\":1}", "{\"entity\":\"kit:1\",\"slot\":0,\"width\":2,\"height\":2}")
            .Replace("{\"entity\":\"kit:2\",\"slot\":1,\"width\":1,\"height\":1}", "{\"entity\":\"kit:2\",\"slot\":4,\"width\":1,\"height\":1}")
            .Replace("{\"entity\":\"kit:3\",\"slot\":2,\"width\":1,\"height\":1}", "{\"entity\":\"kit:3\",\"slot\":5,\"width\":1,\"height\":1}").Replace(Pile, "");
        var upgraded = GameSessionSnapshotCodec.TryDecode(SnapshotUpgradeTests.Schema74(big));
        Assert.AreEqual(SnapshotDecodeFailure.None, upgraded.Failure, upgraded.Path);
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(upgraded.Snapshot!), "{\"entity\":\"kit:1\",\"slot\":0,\"width\":2,\"height\":2},{\"entity\":\"gold:pc\",\"slot\":2,\"width\":1,\"height\":1},{\"entity\":\"kit:2\"");
        // A grid with every cell taken by one item: no cell, no pile (the session lets it wait).
        var full = current.Replace("{\"entity\":\"kit:1\",\"slot\":0,\"width\":1,\"height\":1}", "{\"entity\":\"kit:1\",\"slot\":0,\"width\":10,\"height\":12}")
            .Replace(",{\"entity\":\"kit:2\",\"slot\":1,\"width\":1,\"height\":1}", "").Replace(",{\"entity\":\"kit:3\",\"slot\":2,\"width\":1,\"height\":1}", "").Replace(Pile, "");
        var none = GameSessionSnapshotCodec.TryDecode(SnapshotUpgradeTests.Schema74(full));
        Assert.AreEqual(SnapshotDecodeFailure.None, none.Failure, none.Path);
        StringAssert.DoesNotMatch(GameSessionSnapshotCodec.Encode(none.Snapshot!), new System.Text.RegularExpressions.Regex("gold:pc"));
    }
}
