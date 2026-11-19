using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2879 (ledger IE3, audit TQ20; exe read 0x463B30, 0x463860, 0x464830, 0x4658E0 → 0x467860): the remove-item script (point 28) of the removals that no script or window asks for: the restock's clear (holder as the triggerer, persistent items stay), a dead body's
// inventory walk (the corpse, every item, destroyed or dropped) and a gold pile or ammunition stack a move empties (after its insert-item event). Synthetic seeds, invented script numbers.
[TestClass]
public sealed class RemovePassScriptTests
{
    private const int RemoveScript = 702, SmithSource = 5;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private sealed class World(RemoveItemScriptTests.Host? host) : ISessionRestockWorld, ISessionBodyWorld
    {
        public ISessionItemScriptHost? ItemScriptHost => host;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => new() { HitPointPoints = 10, FatiguePoints = 10 };
        public CellCoord? BodyCell(SessionMapId map, string entity) => new CellCoord(101, 202);
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => entity switch
        {
            "smith" => new(RestockHolderKind.Npc, SmithSource, false, false, 0, false),
            "chest" => new(RestockHolderKind.Container, SmithSource, false, false, RestockRules.RegeneratesAloneFlag, true),
            _ => null,
        };
        public string? RestockMerchant(SessionMapId map, string container) => null;
        public bool Persistent(SessionMapId map, string item) => item == "ring";
        public SessionRestockSet RestockSet(int source) => new(SessionRestockSetStatus.Read, new(0, 0, []));
        public SessionRestockItem? RestockItem(int basicPrototype) => null;
        public SessionItemKind RestockPileKind => GoldPile;
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => _ => SessionInsertVerdict.Accepted;
    }

    private static SessionMapInventorySeed Seed() => new(
        [
            new("smith", SessionHolderKind.Npc, false, true, [], ["hammer", "ring", "cap"]),
            new("chest", SessionHolderKind.Container, false, false, [], ["axe"]),
            new("npc-1", SessionHolderKind.Npc, false, false, [], ["bread", "ghost", "plain"]),
            new("merchant", SessionHolderKind.Npc, false, true, [], ["coins", "darts", "cheap"]),
        ],
        [
            new("hammer", Facts(SessionItemType.Weapon, 2001, location: 1004, scripts: SessionScriptPoint.RemoveItem), null),
            new("ring", Facts(SessionItemType.Armor, 2805, scripts: SessionScriptPoint.RemoveItem), null),
            new("cap", Facts(SessionItemType.Armor, 2806), null),
            new("axe", Facts(SessionItemType.Weapon, 2002, scripts: SessionScriptPoint.RemoveItem), null),
            new("bread", Facts(SessionItemType.Food, 3600, scripts: SessionScriptPoint.RemoveItem), null),
            new("ghost", Facts(SessionItemType.Food, 3601, scripts: SessionScriptPoint.RemoveItem) with { Summoned = true }, null),
            new("plain", Facts(SessionItemType.Food, 3602), null),
            new("coins", Facts(SessionItemType.Gold, 3200, quantity: 5, scripts: [SessionScriptPoint.InsertItem, SessionScriptPoint.RemoveItem]), null),
            new("darts", Facts(SessionItemType.Ammo, 3301, quantity: 8, scripts: SessionScriptPoint.RemoveItem) with { AmmoType = 1 }, null),
            new("cheap", Facts(SessionItemType.Generic, 5650), null),
        ]);

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static RemoveItemScriptTests.Host Hosted(GameSessionOwner owner, params string[] items)
    {
        var host = new RemoveItemScriptTests.Host(owner);
        foreach (var item in items) host.Slots[(item, SessionScriptPoint.RemoveItem)] = RemoveScript;
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        return host;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static SessionRestockRegeneration Restock(GameSessionOwner owner, string holder, ISessionRestockWorld world)
    {
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, holder, hasSource: true).Disposition);
        Advance(owner, RestockRules.MaxDelayMs);
        return owner.ProcessEvents(world).Restocks.Single().Regenerations.Single();
    }

    [TestMethod]
    public void The_restock_clear_runs_the_remove_item_script_of_each_deleted_item_with_the_npc_as_the_triggerer_and_keeps_the_persistent_item_without_one()
    {
        using var owner = Owner();
        var host = Hosted(owner, "hammer", "ring");
        var done = Restock(owner, "smith", new World(host));
        Assert.AreEqual((SessionRestockOutcome.Regenerated, 2), (done.Outcome, done.Removed), "the hammer and the cap go, the persistent ring stays");
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, "hammer") }, done.Removals.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 2, 0, $"npc line 2 of {RemoveScript}") }, done.Removals.Messages.ToArray(), "an NPC triggerer reads its own line variant");
        Assert.AreEqual(0, done.Removals.Skipped);
        Assert.AreEqual((true, true, false), (ReadFlag(owner, 3), owner.IsItemDestroyed("hammer"), owner.IsItemDestroyed("ring")));
    }

    [TestMethod]
    public void The_restock_clear_of_a_container_runs_the_script_with_the_container_as_the_triggerer()
    {
        using var owner = Owner();
        var host = Hosted(owner, "axe");
        var done = Restock(owner, "chest", new World(host));
        CollectionAssert.AreEqual(new[] { "axe" }, done.Removals.Scripts.Select(run => run.Attachee).ToArray());
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 2, 0, $"line 2 of {RemoveScript}") }, done.Removals.Messages.ToArray(), "a container is no critter: the plain line variant");
        Assert.IsTrue(owner.IsItemDestroyed("axe"));
    }

    [TestMethod]
    public void Without_a_script_host_the_clear_goes_on_and_counts_the_removals_it_could_not_run()
    {
        using var owner = Owner();
        var done = Restock(owner, "smith", new World(null));
        Assert.AreEqual((2, 0, 1), (done.Removed, done.Removals.Scripts.Count, done.Removals.Skipped));
        Assert.AreEqual((false, true), (ReadFlag(owner, 3) == true, owner.IsItemDestroyed("hammer")));
    }

    [TestMethod]
    public void A_removal_the_session_cannot_apply_is_counted_and_the_clear_still_deletes_the_item()
    {
        using var owner = Owner();
        var host = Hosted(owner, "hammer");
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var done = Restock(owner, "smith", new World(host));
        Assert.AreEqual((2, 0, 1), (done.Removed, done.Removals.Scripts.Count, done.Removals.Skipped));
        Assert.IsTrue(owner.IsItemDestroyed("hammer"));
    }

    private static string Kill(GameSessionOwner owner, string npc)
    {
        owner.Kill(owner.Version, SessionLifeTarget.Critter(npc, new() { HitPointPoints = 10, FatiguePoints = 10 }, LifeState.Unharmed));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
        return npc;
    }

    [TestMethod]
    public void A_bodys_walk_runs_every_items_remove_item_script_with_the_corpse_as_the_triggerer_before_it_is_destroyed_or_dropped()
    {
        using var owner = Owner();
        var host = Hosted(owner, "bread", "ghost");
        Kill(owner, "npc-1");
        var fired = owner.ProcessEvents(new World(host)).BodyDecays.Single();
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 2, 1), (fired.Outcome, fired.Dropped, fired.Destroyed), "ghost (summoned) is destroyed, bread and plain are laid down");
        CollectionAssert.AreEqual(new[] { "ghost", "bread" }, fired.Removals.Scripts.Select(run => run.Attachee).ToArray(), "the walk goes last item first; the summoned item runs point 28 before its destroy");
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 2, 0, $"npc line 2 of {RemoveScript}"), new SessionScriptMessage(RemoveScript, 2, 0, $"npc line 2 of {RemoveScript}") }, fired.Removals.Messages.ToArray());
        Assert.IsTrue(ReadFlag(owner, 3) == true && owner.IsItemDestroyed("ghost") && !owner.IsItemDestroyed("bread"));
    }

    [TestMethod]
    public void A_bodys_walk_without_a_script_host_counts_the_skipped_removals_and_drops_as_before()
    {
        using var owner = Owner();
        Kill(owner, "npc-1");
        var fired = owner.ProcessEvents(new World(null)).BodyDecays.Single();
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 2, 1, 0, 2), (fired.Outcome, fired.Dropped, fired.Destroyed, fired.Removals.Scripts.Count, fired.Removals.Skipped));
    }

    [TestMethod]
    public void A_gold_pile_a_take_empties_runs_the_insert_item_script_and_then_the_remove_item_script_with_the_merchant_as_the_triggerer()
    {
        using var owner = Owner();
        var host = Hosted(owner, "coins");
        host.Slots[("coins", SessionScriptPoint.InsertItem)] = RemoveScript + 1;
        host.Programs[RemoveScript + 1] = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault));
        var taken = owner.TakeGoldFromNpc(owner.Version, "merchant", 5, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, true), (taken.Disposition, owner.IsItemDestroyed("coins")));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "coins"), (SessionScriptPoint.RemoveItem, "coins") }, taken.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        CollectionAssert.AreEqual(new[] { $"line 1 of {RemoveScript + 1}", $"npc line 2 of {RemoveScript}" }, taken.Messages.Select(message => message.Text).ToArray(), "the insert-item event has the hero as the triggerer, the removal the merchant");
        Assert.AreEqual(true, ReadFlag(owner, 3));
    }

    [TestMethod]
    public void A_gold_pile_a_take_leaves_with_coins_runs_no_remove_item_script_and_an_emptied_one_without_a_host_is_refused()
    {
        using var owner = Owner();
        var host = Hosted(owner, "coins");
        host.Slots[("coins", SessionScriptPoint.InsertItem)] = RemoveScript + 1;
        host.Programs[RemoveScript + 1] = Program(C(ScrCatalogue.Action.ReturnRunDefault));
        var partial = owner.TakeGoldFromNpc(owner.Version, "merchant", 4, scripts: host);
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.InsertItem }, partial.Scripts.Select(run => run.Point).ToArray());
        Assert.IsFalse(owner.IsItemDestroyed("coins"));
        Assert.AreEqual((false, false), (ReadFlag(owner, 3) == true, partial.Disposition != GameSessionDisposition.Changed));
        var refused = owner.TakeGoldFromNpc(owner.Version, "merchant", 1);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, refused.Failure, "the pile has an insert-item script and no host");
    }

    [TestMethod]
    public void An_ammunition_stack_a_take_uses_up_runs_its_remove_item_script_with_the_holder_as_the_triggerer()
    {
        using var owner = Owner();
        var host = Hosted(owner, "darts");
        var taken = owner.TakeAmmoFromNpc(owner.Version, "merchant", "darts", 8, Bullets, _ => SessionInsertVerdict.Accepted, host);
        Assert.AreEqual((GameSessionDisposition.Changed, true), (taken.Disposition, owner.IsItemDestroyed("darts")));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, "darts") }, taken.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        CollectionAssert.AreEqual(new[] { $"npc line 2 of {RemoveScript}" }, taken.Messages.Select(message => message.Text).ToArray());
        // The stack that only loses units runs none.
        using var other = Owner();
        var part = other.TakeAmmoFromNpc(other.Version, "merchant", "darts", 3, Bullets, _ => SessionInsertVerdict.Accepted, Hosted(other, "darts"));
        Assert.AreEqual((GameSessionDisposition.Changed, 0, false), (part.Disposition, part.Scripts.Count, other.IsItemDestroyed("darts")));
    }
}
