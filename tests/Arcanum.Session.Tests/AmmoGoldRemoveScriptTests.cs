using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2900 (ledger IE3, TQ20, NT1, AM4; exe read 0x464830, 0x4658E0 → 0x43CCA0 → 0x466E00 → 0x467860, 0x578EA0): the remove-item script (point 28) of the ammunition stack a spend empties (the shot, the cell payment, the assembly's component, a script's ammunition
// use), of the hero's gold pile the coins paid to an NPC empty (with its insert-item event first) and of the whole stack the hero drops or throws. Synthetic seeds, invented script numbers.
[TestClass]
public sealed class AmmoGoldRemoveScriptTests
{
    private const int RemoveScript = 702, InsertScript = 703;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionMapInventorySeed Seed() => new(
        [new("merchant", SessionHolderKind.Npc, false, true, [], ["coins", "darts", "slugs"])],
        [
            new("coins", Facts(SessionItemType.Gold, 3200, quantity: 5), null),
            new("darts", Facts(SessionItemType.Ammo, 3301, quantity: 8, scripts: SessionScriptPoint.RemoveItem) with { AmmoType = 1 }, null),
            new("slugs", Facts(SessionItemType.Ammo, 3302, quantity: 4) with { AmmoType = 2 }, null),
        ]);

    private static GameSessionOwner Owner(bool heroGoldScripts = false)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        if (heroGoldScripts) owner.AttachItemScripts(prototype => prototype == SessionHeroGrid.GoldPrototype
            ? new SessionPrototypeScript[] { new(SessionScriptPoint.InsertItem, InsertScript, 0, 0), new(SessionScriptPoint.RemoveItem, RemoveScript, 0, 0) } : null);
        return owner;
    }

    private static RemoveItemScriptTests.Host Hosted(GameSessionOwner owner, params string[] items)
    {
        var host = new RemoveItemScriptTests.Host(owner);
        foreach (var item in items)
        {
            host.Slots[(item, SessionScriptPoint.RemoveItem)] = RemoveScript;
            host.Slots[(item, SessionScriptPoint.InsertItem)] = InsertScript;
        }
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault));
        host.Programs[InsertScript] = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault));
        return host;
    }

    private static (int Point, string Attachee)[] Points(IEnumerable<SessionItemScriptRun> runs) => runs.Select(run => (run.Point, run.Attachee)).ToArray();

    [TestMethod]
    public void A_spend_that_empties_an_ammunition_stack_runs_its_remove_item_script_with_the_holder_as_the_triggerer_and_a_partial_one_runs_none()
    {
        using var owner = Owner();
        var host = Hosted(owner, "darts");
        var part = owner.SpendAmmo(owner.Version, new("merchant"), 1, 3, host);
        Assert.AreEqual((GameSessionDisposition.Changed, false, 0), (part.Disposition, part.Removed, part.Scripts.Count));
        var revision = owner.Version.Revision;
        var spent = owner.SpendAmmo(owner.Version, new("merchant"), 1, 5, host);
        Assert.AreEqual((GameSessionDisposition.Changed, true, revision + 1), (spent.Disposition, spent.Removed, owner.Version.Revision), "one revision with the deletion");
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, "darts") }, Points(spent.Scripts));
        CollectionAssert.AreEqual(new[] { $"npc line 2 of {RemoveScript}" }, spent.Messages.Select(message => message.Text).ToArray(), "the NPC holder is the triggerer");
        Assert.IsTrue(owner.IsItemDestroyed("darts") && ReadFlag(owner, 3) == true, "the result (skip default) is ignored; the run's flag applied with the deletion");
    }

    [TestMethod]
    public void A_spend_that_empties_a_scripted_stack_without_any_host_is_refused_whole()
    {
        using var owner = Owner();
        var revision = owner.Version.Revision;
        var refused = owner.SpendAmmo(owner.Version, new("merchant"), 1, 8);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, revision), (refused.Disposition, refused.Failure, owner.Version.Revision));
        Assert.IsFalse(owner.IsItemDestroyed("darts"));
        var unscripted = owner.SpendAmmo(owner.Version, new("merchant"), 2, 4);
        Assert.AreEqual((GameSessionDisposition.Changed, true), (unscripted.Disposition, unscripted.Removed), "a stack with no remove-item script is spent as before");
    }

    [TestMethod]
    public void A_spend_with_no_host_of_its_own_runs_through_the_standing_host_and_sends_the_lines_to_the_item_run_output()
    {
        using var owner = Owner();
        var host = Hosted(owner, "darts");
        owner.StandingItemScripts = () => host;
        var lines = new List<string>();
        owner.ItemRunOutput = (messages, _) => lines.AddRange(messages.Select(message => message.Text));
        var spent = owner.SpendAmmo(owner.Version, new("merchant"), 1, 8);
        Assert.AreEqual((GameSessionDisposition.Changed, true, 1), (spent.Disposition, spent.Removed, spent.Scripts.Count));
        Assert.AreEqual(0, spent.Messages.Count, "the caller asked for no lines: they go to the output");
        CollectionAssert.AreEqual(new[] { $"npc line 2 of {RemoveScript}" }, lines);
        Assert.IsTrue(ReadFlag(owner, 3));
    }

    [TestMethod]
    public void A_standing_host_that_throws_counts_as_none()
    {
        using var owner = Owner();
        owner.StandingItemScripts = () => throw new InvalidOperationException("no host");
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.SpendAmmo(owner.Version, new("merchant"), 1, 8).Failure);
    }

    [TestMethod]
    public void The_hero_paying_all_its_coins_to_an_npc_runs_the_piles_insert_item_then_remove_item_script_and_a_part_runs_the_insert_item_script_only()
    {
        using var owner = Owner(heroGoldScripts: true);
        var host = Hosted(owner, SessionHeroGrid.GoldEntity);
        var all = owner.QueryGold();
        var part = owner.GiveGoldToNpc(owner.Version, "merchant", 1, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, all - 1), (part.Disposition, owner.QueryGold()));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, SessionHeroGrid.GoldEntity) }, Points(part.Scripts), "a part empties no pile");
        var rest = owner.GiveGoldToNpc(owner.Version, "merchant", all - 1, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (rest.Disposition, owner.QueryGold()));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, SessionHeroGrid.GoldEntity), (SessionScriptPoint.RemoveItem, SessionHeroGrid.GoldEntity) }, Points(rest.Scripts));
        CollectionAssert.AreEqual(new[] { $"npc line 1 of {InsertScript}", $"line 2 of {RemoveScript}" }, rest.Messages.Select(message => message.Text).ToArray(), "the insert-item event has the NPC as the triggerer, the removal the hero");
        Assert.IsTrue(ReadFlag(owner, 3));
    }

    [TestMethod]
    public void A_scripted_hero_gold_pile_given_with_no_host_is_refused_and_the_coins_stay()
    {
        using var owner = Owner(heroGoldScripts: true);
        var all = owner.QueryGold();
        var refused = owner.GiveGoldToNpc(owner.Version, "merchant", all, GoldPile, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, all), (refused.Disposition, refused.Failure, owner.QueryGold()));
    }

    [TestMethod]
    public void The_hero_dropping_its_whole_gold_pile_runs_the_remove_item_script_and_a_part_of_it_runs_none()
    {
        using var owner = Owner(heroGoldScripts: true);
        var host = Hosted(owner, SessionHeroGrid.GoldEntity);
        var all = owner.QueryGold();
        var part = owner.DropHeroGold(owner.Version, 1, GoldPile, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (part.Disposition, part.Scripts.Count));
        var rest = owner.DropHeroGold(owner.Version, all - 1, GoldPile, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (rest.Disposition, owner.QueryGold()));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.RemoveItem, SessionHeroGrid.GoldEntity) }, Points(rest.Scripts), "no drop script and no insert-item event: the pile is destroyed, then a new pile lies on the tile");
    }

    [TestMethod]
    public void The_hero_dropping_its_whole_scripted_gold_pile_with_no_host_is_refused()
    {
        using var owner = Owner(heroGoldScripts: true);
        var all = owner.QueryGold();
        var refused = owner.DropHeroGold(owner.Version, all, GoldPile);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, all), (refused.Disposition, refused.Failure, owner.QueryGold()));
    }

    [TestMethod]
    public void The_dialogs_payment_of_all_the_heros_coins_runs_the_piles_scripts_through_the_ports_host()
    {
        using var owner = Owner(heroGoldScripts: true);
        var host = Hosted(owner, SessionHeroGrid.GoldEntity);
        var (pc, npc) = (new DialogParticipantId(1), new DialogParticipantId(2));
        var port = new SessionDialogGoldPort(owner, pc, participant => participant == npc ? "merchant" : null, GoldPile, Accept, shortPayerPaysNothing: true, itemScripts: host);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GoldCommand(pc, npc, -owner.QueryGold())));
        Assert.AreEqual(0, owner.QueryGold());
        Assert.IsTrue(ReadFlag(owner, 3), "the removal's flag write applied with the payment");
        var refused = Owner(heroGoldScripts: true);
        var withoutHost = new SessionDialogGoldPort(refused, pc, participant => participant == npc ? "merchant" : null, GoldPile, Accept, shortPayerPaysNothing: true);
        Assert.AreEqual(DialogCommandResult.Rejected, withoutHost.Execute(new GoldCommand(pc, npc, -refused.QueryGold())));
        Assert.AreNotEqual(0, refused.QueryGold(), "a scripted pile with no host is not paid out");
        refused.Dispose();
    }
}
