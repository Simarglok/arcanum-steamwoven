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

    private static SessionDialogItemTakePort Port(GameSessionOwner owner, string? npc = "merchant", SessionInsertCheck? check = null, Action<string>? prepare = null) =>
        new(owner, Pc, participant => participant == Npc ? npc : null, check ?? Accept, prepare);

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
    public void A_refusing_insert_check_or_an_unknown_participant_is_rejected_and_nothing_moves()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var full = Port(owner, check: _ => SessionInsertVerdict.NoRoom);
        Assert.AreEqual(DialogCommandResult.Rejected, full.Execute(Take(2850)));
        Assert.AreEqual(GameSessionDisposition.Rejected, full.LastResult!.Value.Disposition);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(3, owner.QueryHeroKit().Count);
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner, npc: null).Execute(Take(2850)), "an NPC the host does not map");
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner, npc: "nobody").Execute(Take(2850)), "a holder the session does not list");
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner).Execute(Take(2850, Other)), "another PC");
        Assert.AreEqual(revision, owner.Version.Revision);
    }
}
