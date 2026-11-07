using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog's non-negative IN action over the session (ARC-DEV-199, ledger DAC5, TK1–TK3). Over the synthetic seed of InventoryStateTests (virgil holds a ring 2804, a sword 2000, a vest 2800 and
// a ring copy 2804; the merchant holds coins 3200) and the half-elf hero (kit:1 name 4021, kit:2 name 2850, kit:3 name 2851). Entities and numbers are invented.
[TestClass]
public sealed class DialogItemTakePortTests
{
    private static readonly DialogParticipantId Pc = new(1), Npc = new(2), Other = new(3);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static SessionDialogItemTakePort Port(GameSessionOwner owner, string? npc = "merchant", SessionInsertCheck? check = null, Action<string>? prepare = null, Func<string, Arcanum.Simulation.Movement.CellCoord?>? locationOf = null) =>
        new(owner, Pc, participant => participant == Npc ? npc : null, check ?? Accept, prepare, locationOf);

    private static TakeItemCommand Take(int name, DialogParticipantId? pc = null) => new(Npc, pc ?? Pc, name);

    private static string[] List(GameSessionOwner owner, string holder) => owner.QueryHolderItems(holder)!.Select(item => item.Entity).ToArray();

    [TestMethod]
    public void The_first_hero_item_of_that_name_goes_to_the_npc_and_leaves_the_hero()
    {
        using var owner = Hero();
        var port = Port(owner);
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Take(2850)));
        Assert.AreEqual(GameSessionDisposition.Changed, port.LastResult!.Value.Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        CollectionAssert.Contains(List(owner, "merchant"), "kit:2");
        // The item is not there for a second call: nobody holds one, the source's no-effect case.
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Take(2850)));
        Assert.IsNull(port.LastResult);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
    }

    [TestMethod]
    public void Nobody_holding_the_item_is_applied_without_a_change()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Applied, Port(owner).Execute(Take(9999)));
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(3, owner.QueryHeroKit().Count);
    }

    [TestMethod]
    public void A_follower_holding_the_item_is_rejected_because_the_session_has_no_move_between_npcs()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        var prepared = new List<string>();
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner, prepare: prepared.Add).Execute(Take(2804)));
        CollectionAssert.Contains(prepared, "virgil");
        Assert.AreEqual(revision, owner.Version.Revision);
        // The NPC itself being the first follower holding it: the item is with it already.
        var self = Port(owner, npc: "virgil");
        Assert.AreEqual(DialogCommandResult.Applied, self.Execute(Take(2804)));
        Assert.IsNull(self.LastResult);
        Assert.AreEqual(revision, owner.Version.Revision);
        // The hero's own item wins over a follower's.
        Assert.AreEqual(DialogCommandResult.Applied, Port(owner).Execute(Take(2850)));
    }

    [TestMethod]
    public void A_refused_hand_over_to_an_npc_with_no_leader_destroys_the_item()
    {
        // TK2 (ARC-DEV-238): the host's insert check refused, the transfer did not end with the NPC, the NPC has no leader: the item is destroyed and the action is over.
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var port = Port(owner, check: _ => SessionInsertVerdict.NoRoom);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Take(2850)));
        Assert.AreEqual(GameSessionDisposition.Changed, port.LastResult!.Value.Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        CollectionAssert.DoesNotContain(List(owner, "merchant"), "kit:2");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        // It is gone for good: a second call finds nobody holding one.
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Take(2850)));
        Assert.IsNull(port.LastResult);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
    }

    [TestMethod]
    public void A_refused_hand_over_to_an_npc_the_pc_leads_lays_the_item_on_its_cell()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        var revision = owner.Version.Revision;
        var cell = new Arcanum.Simulation.Movement.CellCoord(12, 34);
        var port = Port(owner, npc: "virgil", check: _ => SessionInsertVerdict.NoRoom, locationOf: entity => entity == "virgil" ? cell : null);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Take(2850)));
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("kit:2", cell), (placed.Entity, placed.Cell));
        CollectionAssert.DoesNotContain(List(owner, "virgil"), "kit:2");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for the placement");
        // A leader whose cell the host cannot give, or one the ground cannot hold, is Rejected with the item still held.
        using var other = Hero();
        other.ForceJoinParty(other.Version, "virgil");
        var before = other.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Rejected, Port(other, npc: "virgil", check: _ => SessionInsertVerdict.NoRoom).Execute(Take(2850)), "no cell");
        Assert.AreEqual(DialogCommandResult.Rejected, Port(other, npc: "virgil", check: _ => SessionInsertVerdict.NoRoom, locationOf: _ => new Arcanum.Simulation.Movement.CellCoord(-1, 5)).Execute(Take(2850)), "a cell off the ground");
        Assert.AreEqual(before, other.Version.Revision);
        Assert.AreEqual(3, other.QueryHeroKit().Count);
    }

    [TestMethod]
    public void The_session_refuses_to_settle_what_it_cannot_run_and_other_refusals_stay_rejected()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.SettleRefusedHandover(owner.Version, "kit:99", null).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.SettleRefusedHandover(owner.Version, "", null).Failure);
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SettleRefusedHandover(stale, "kit:1", null).Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.SettleRefusedHandover(stale, "kit:3", null).Failure);
        // Only the host's refusal of the insert is the game's: an unknown holder, an unknown PC and the like fail the conversation as before.
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner, npc: null).Execute(Take(2851)), "an NPC the host does not map");
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner, npc: "nobody").Execute(Take(2851)), "a holder the session does not list");
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner).Execute(Take(2851, Other)), "another PC");
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.AreEqual(2, owner.QueryHeroKit().Count);
    }
}
