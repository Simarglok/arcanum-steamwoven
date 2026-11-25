using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.CreatedItemScriptTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2911 (ledger NT1, NT2; exe 0x464830 → 0x464970 → 0x4617F0 → 0x461810): the new gold pile or ammunition stack of a move into an NPC or a container is made on the holder's tile and moved in; when its get script (point 4)
// skips the default or the insert check refuses, 0x461810 returns 0 and the object stays on the ground, the coins or units already out of the giver, and the call still reports success. Over the synthetic seed of InventoryStateTests
// (virgil alive without a pile, the merchant with a pile of 5 coins) and of MerchantStockStateTests (the smith, a chest with piles and bullets, an empty box); cells, numbers and scripts are invented.
[TestClass]
public sealed class HolderGroundPileTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionInsertCheck Refuse = _ => SessionInsertVerdict.NoRoom;
    private static readonly CellCoord VirgilCell = new(412, 308), ArcherCell = new(440, 300), SmithCell = new(100, 200);

    private static GameSessionOwner Hero(bool cells = true, bool scripts = false)
    {
        var owner = CreateWith(HalfElf());
        var seed = Seed();
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed with
        {
            Holders = [.. seed.Holders, new("archer", SessionHolderKind.Npc, false, false, [], ["arrows"])],
            Items = [.. seed.Items, new SessionSeedItem("arrows", Facts(SessionItemType.Ammo, 3300, quantity: 30) with { AmmoType = 0 }, null)],
        }).Failure);
        if (scripts) owner.AttachItemScripts(Prototypes);
        if (cells) owner.AttachHolderCells(holder => holder switch { "virgil" => VirgilCell, "archer" => ArcherCell, _ => null });
        return owner;
    }

    private static SessionPlacedItem OnGround(GameSessionOwner owner, string entity) => owner.QueryPlacedItems().Single(item => item.Entity == entity);

    private static SessionItemKind GoldPile => new(SessionItemType.Gold, GoldProto, 3200, 33200);
    private static SessionItemKind Bullets => new(SessionItemType.Ammo, AmmoProto, 3301, 33301);

    [TestMethod]
    public void A_pile_the_insert_check_refuses_lies_on_the_npcs_tile_with_the_coins_out_of_the_hero()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var gold = owner.QueryGold();
        var given = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Refuse);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1", SessionInsertVerdict.NoRoom, revision + 1),
            (given.Disposition, given.Failure, given.Item, given.Verdict, owner.Version.Revision), "the call reports success with one revision; the verdict is the refusal");
        Assert.AreEqual((gold - 40, 0), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "the coins left the hero, the NPC got none");
        var placed = OnGround(owner, "made:1");
        Assert.AreEqual((SessionPlacedSource.Made, Exterior, VirgilCell, SessionItemType.Gold), (placed.Source, placed.Map, placed.Cell, placed.Kind.Type));
        Assert.AreEqual(40, owner.QueryMapInventory(Exterior)!.Ground.Single(ground => ground.Item.Entity == "made:1").Item.Facts.Quantity);
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy" }, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "virgil").Items.Select(item => item.Entity).ToArray());
        Assert.AreEqual(0, owner.QueryItemDecays().Count, "a created pile is no drop: no decay timer");
        // The hero picks the pile up as any ground pile and gets the coins back.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "made:1", Accept).Disposition);
        Assert.AreEqual((gold, 0), (owner.QueryGold(), owner.QueryPlacedItems().Count));
    }

    [TestMethod]
    public void Without_a_cell_source_or_a_cell_for_the_holder_the_refused_pile_refuses_the_move_as_before()
    {
        foreach (var attached in new[] { false, true })
        {
            using var owner = Hero(cells: attached);
            if (attached) owner.AttachHolderCells(_ => null);
            var revision = owner.Version.Revision;
            var gold = owner.QueryGold();
            var refused = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Refuse);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, SessionInsertVerdict.NoRoom), (refused.Disposition, refused.Failure, refused.Verdict));
            Assert.AreEqual((revision, gold, 0, 0), (owner.Version.Revision, owner.QueryGold(), owner.QueryNpcGold("virgil"), owner.QueryPlacedItems().Count));
        }
    }

    [TestMethod]
    public void A_cell_the_ground_cannot_hold_or_a_full_item_budget_refuses_the_move_as_before()
    {
        using var owner = Hero();
        owner.AttachHolderCells(_ => new CellCoord(-1, 5));
        Assert.AreEqual(GameSessionFailure.InsertRefused, owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Refuse).Failure);
        using var full = CreateWith(HalfElf(), new GameSessionLimits(maxPcItems: 3));
        Assert.AreEqual(GameSessionFailure.None, full.AttachMapInventory(Exterior, Seed()).Failure);
        full.AttachHolderCells(_ => VirgilCell);
        Assert.AreEqual(GameSessionFailure.InsertRefused, full.GiveGoldToNpc(full.Version, "virgil", 40, GoldPile, Refuse).Failure, "the hero's three kit items fill its budget: a ground item would not fit");
    }

    [TestMethod]
    public void A_get_script_that_skips_the_default_leaves_the_new_pile_on_the_ground_applies_the_get_run_and_runs_no_insert_script()
    {
        using var owner = Hero(scripts: true);
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 5), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var gold = owner.QueryGold();
        var given = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1", (int?)SessionScriptPoint.Get, (SessionInsertVerdict?)null),
            (given.Disposition, given.Failure, given.Item, given.DeclinedBy, given.Verdict));
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.Get }, given.Scripts.Select(run => run.Point).ToArray(), "the insert-item script is not run for an object never inserted");
        Assert.AreEqual(true, ReadFlag(owner, 3), "the run's effects apply: the object exists either way");
        CollectionAssert.AreEqual(new[] { "npc reads 5" }, given.Messages.Select(message => message.Text).ToArray());
        Assert.AreEqual((gold - 40, 0, VirgilCell), (owner.QueryGold(), owner.QueryNpcGold("virgil"), OnGround(owner, "made:1").Cell));
    }

    [TestMethod]
    public void An_insert_check_refusal_after_the_get_script_keeps_the_get_runs_effects_and_the_pile_lies_on_the_ground()
    {
        using var owner = Hero(scripts: true);
        var refused = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Refuse, new Host(owner));
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1", (SessionInsertVerdict?)SessionInsertVerdict.NoRoom, (int?)null), (refused.Disposition, refused.Item, refused.Verdict, refused.DeclinedBy));
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.Get }, refused.Scripts.Select(run => run.Point).ToArray());
        Assert.AreEqual(true, ReadFlag(owner, 3));
        Assert.AreEqual(0, owner.QueryNpcGold("virgil"));
    }

    [TestMethod]
    public void A_get_run_the_session_cannot_apply_still_refuses_the_whole_move_with_nothing_on_the_ground()
    {
        using var owner = Hero(scripts: true);
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var revision = owner.Version.Revision;
        var refused = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (refused.Disposition, refused.Failure));
        Assert.AreEqual((revision, 0), (owner.Version.Revision, owner.QueryPlacedItems().Count));
    }

    [TestMethod]
    public void Coins_made_from_nothing_lie_on_the_ground_when_refused_and_the_hero_pays_nothing()
    {
        using var owner = Hero();
        var gold = owner.QueryGold();
        var made = owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", 7, GoldPile, Refuse);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1", SessionInsertVerdict.NoRoom), (made.Disposition, made.Item, made.Verdict));
        Assert.AreEqual((gold, 0, 7), (owner.QueryGold(), owner.QueryNpcGold("virgil"), owner.QueryMapInventory(Exterior)!.Ground.Single(ground => ground.Item.Entity == "made:1").Item.Facts.Quantity));
    }

    [TestMethod]
    public void Coins_moved_between_npcs_leave_the_source_and_lie_on_the_destinations_tile_when_refused()
    {
        using var owner = Hero();
        owner.AttachHolderCells(holder => holder == "archer" ? ArcherCell : null);
        var moved = owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "archer", 5, GoldPile, Refuse);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1", SessionInsertVerdict.NoRoom), (moved.Disposition, moved.Item, moved.Verdict));
        Assert.AreEqual((0, 0), (owner.QueryNpcGold("merchant"), owner.QueryNpcGold("archer")), "the merchant's pile is destroyed, the archer got none");
        Assert.AreEqual((ArcherCell, 5), (OnGround(owner, "made:1").Cell, owner.QueryMapInventory(Exterior)!.Ground.Single(ground => ground.Item.Entity == "made:1").Item.Facts.Quantity));
    }

    [TestMethod]
    public void A_new_ammunition_stack_the_insert_check_refuses_lies_on_the_ground_with_its_units_out_of_the_hero()
    {
        using var owner = Hero();
        Assert.AreEqual("made:1", owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, Accept).Item);
        var given = owner.GiveAmmoToNpc(owner.Version, "virgil", "made:1", 5, Bullets, Refuse);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:2", SessionInsertVerdict.NoRoom), (given.Disposition, given.Item, given.Verdict));
        Assert.AreEqual((25, 5), (owner.QueryAmmo("made:1")!.Value.Units, owner.QueryAmmo("made:2")!.Value.Units));
        Assert.AreEqual(VirgilCell, OnGround(owner, "made:2").Cell);
        Assert.IsFalse(owner.QueryNpcInventory("virgil").Any(item => item.Entity == "made:2"), "not in the NPC's list");
    }

    [TestMethod]
    public void A_get_script_that_skips_the_default_leaves_the_new_stack_on_the_ground_and_the_source_stack_pays_its_units()
    {
        using var owner = Hero(scripts: true);
        var host = new Host(owner);
        Assert.AreEqual("made:1", owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, Accept, host).Item);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        var given = owner.GiveAmmoToNpc(owner.Version, "virgil", "made:1", 30, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:2", (int?)SessionScriptPoint.Get), (given.Disposition, given.Item, given.DeclinedBy));
        Assert.IsTrue(owner.IsItemDestroyed("made:1"), "the hero's stack went at its last unit");
        Assert.AreEqual(30, owner.QueryAmmo("made:2")!.Value.Units);
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "made:1"), (SessionScriptPoint.Get, "made:2") }, given.Scripts.Select(run => (run.Point, run.Attachee)).ToArray(),
            "the source stack's own insert-item script, then the new stack's get script; no insert-item script for the stack left on the ground");
    }

    [TestMethod]
    public void A_pile_or_stack_that_joins_an_existing_one_creates_no_object_and_ignores_a_refusing_check()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToNpc(owner.Version, "merchant", 10, GoldPile, Refuse).Disposition);
        Assert.AreEqual((15, 0), (owner.QueryNpcGold("merchant"), owner.QueryPlacedItems().Count), "the pile grows with no insert check (NT1)");
    }

    private static GameSessionOwner Smith(bool smithPile, RuleMode mode = RuleMode.Steamwoven)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = mode }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, MerchantStockStateTests.Seed(smithPile)).Failure);
        owner.AttachHolderCells(holder => holder switch { "smith" => SmithCell, "chest" => new CellCoord(101, 201), _ => null });
        return owner;
    }

    [TestMethod]
    public void The_barter_open_leaves_the_merchants_new_pile_on_its_tile_when_the_check_refuses_it()
    {
        using var owner = Smith(smithPile: false);
        var opened = owner.OpenBarter(owner.Version, "smith", "chest", identify: true, GoldPile, Refuse);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 42, 2, "made:1"), (opened.Disposition, opened.Failure, opened.Coins, opened.Piles, opened.Pile));
        Assert.AreEqual(0, owner.QueryNpcGold("smith"), "the merchant got no pile");
        Assert.IsTrue(owner.IsItemDestroyed("gold-a") && owner.IsItemDestroyed("gold-b"), "the container's piles are out of the chest (0x464830)");
        var placed = OnGround(owner, "made:1");
        Assert.AreEqual((SmithCell, 42), (placed.Cell, owner.QueryMapInventory(Exterior)!.Ground.Single(ground => ground.Item.Entity == "made:1").Item.Facts.Quantity));
        Assert.IsTrue(owner.QueryItemIdentified("made:1"), "the new pile is identified as a merchant's pile is");
    }

    [TestMethod]
    public void Units_sold_into_a_container_that_make_a_new_stack_lie_on_the_containers_tile_when_the_check_refuses_it()
    {
        // AMA5 (Original mode): the container's stack lookup by type never finds one, so the units enter as a new stack.
        using var owner = Smith(smithPile: true, RuleMode.Original);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.BarterBuy(owner.Version, "smith", "bullets", 15, 0, Bullets, GoldPile, Accept, Accept, container: "chest").Disposition);
        var mine = owner.QueryHeroAmmo().Single().Entity;
        var sold = owner.BarterSell(owner.Version, "smith", mine, 10, 0, Bullets, Refuse, container: "chest");
        Assert.AreEqual(GameSessionDisposition.Changed, sold.Disposition);
        Assert.AreEqual((5, 5), (owner.QueryAmmo("bullets")!.Value.Units, owner.QueryAmmo(mine)!.Value.Units), "the hero's units went, the chest's stack is as it was");
        var lying = owner.QueryPlacedItems().Single(item => item.Kind.Type == SessionItemType.Ammo);
        Assert.AreEqual((new CellCoord(101, 201), 10), (lying.Cell, owner.QueryAmmo(lying.Entity)!.Value.Units));
    }

    [TestMethod]
    public void A_sale_of_units_whose_new_stack_is_left_on_the_ground_by_a_skipped_get_script_is_still_paid()
    {
        using var owner = Hero(scripts: true);
        var host = new Host(owner);
        owner.AttachHolderCells(holder => holder == "merchant" ? new CellCoord(90, 90) : null);
        Assert.AreEqual("made:1", owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, Accept, host).Item);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        var gold = owner.QueryGold();
        var sold = owner.BarterSell(owner.Version, "merchant", "made:1", 2, 2, Bullets, Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:2", (int?)SessionScriptPoint.Get), (sold.Disposition, sold.Item, sold.DeclinedBy));
        Assert.AreEqual((gold + 4, 1), (owner.QueryGold(), owner.QueryNpcGold("merchant")), "the original's call reports success, so the 4 coins are paid");
        Assert.AreEqual((28, 2, new CellCoord(90, 90)), (owner.QueryAmmo("made:1")!.Value.Units, owner.QueryAmmo("made:2")!.Value.Units, OnGround(owner, "made:2").Cell));
    }
}
