using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog in and ni conditions over the session (ARC-DEV-194, ledger DCC6, IH1–IH3). Over the synthetic seed of InventoryStateTests (virgil holds a ring 2804, a sword 2000, a vest 2800
// and a ring copy 2804; the merchant holds coins 3200; the priest holds a cup 5650) and the half-elf hero. Entities and numbers are invented.
[TestClass]
public sealed class DialogItemHoldPortTests
{
    private static readonly DialogParticipantId Pc = new(1), Npc = new(2), Other = new(3);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static SessionDialogItemHoldPort Port(GameSessionOwner owner, string? npc = "merchant", Func<string, string?>? substitute = null, Action<string>? prepare = null) =>
        new(owner, Pc, participant => participant == Npc ? npc : null, substitute ?? (_ => null), prepare);

    private static DialogItemQuery Ask(int name, DialogParticipantId? pc = null) => new(Npc, pc ?? Pc, name);

    [TestMethod]
    public void The_party_side_reads_the_hero_and_then_each_follower()
    {
        using var owner = Hero();
        var port = Port(owner);
        Assert.IsFalse(port.PartyHolds(Ask(2804)), "the hero holds nothing yet and virgil is not a follower");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        var prepared = new List<string>();
        var follower = Port(owner, prepare: prepared.Add);
        Assert.IsTrue(follower.PartyHolds(Ask(2804)), "a follower's list counts");
        Assert.IsTrue(follower.PartyHolds(Ask(2000)));
        Assert.IsFalse(follower.PartyHolds(Ask(5650)));
        CollectionAssert.Contains(prepared, "virgil", "the follower's home map is attached first");
        // The sword moves to the hero's list: the party still holds it, from the hero's side now.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToPc(owner.Version, "virgil", 2000, Accept).Disposition);
        Assert.IsTrue(follower.PartyHolds(Ask(2000)));
        Assert.IsFalse(owner.QueryHolderItems("virgil")!.Any(item => item.Facts.Kind.Name == 2000));
        Assert.IsFalse(follower.PartyHolds(Ask(2000, Other)), "another PC holds nothing");
    }

    [TestMethod]
    public void The_npc_side_reads_the_npc_and_then_its_substitute_container()
    {
        using var owner = Hero();
        Assert.IsTrue(Port(owner).NpcHolds(Ask(3200)), "the merchant's coins");
        Assert.IsFalse(Port(owner).NpcHolds(Ask(5650)), "no substitute container: only its own list");
        Assert.IsTrue(Port(owner, substitute: npc => npc == "merchant" ? "priest" : null).NpcHolds(Ask(5650)), "the substitute container's cup");
        Assert.IsTrue(Port(owner, substitute: _ => "priest").NpcHolds(Ask(3200)), "its own item first");
        Assert.IsFalse(Port(owner, substitute: _ => "priest").NpcHolds(Ask(9999)));
        Assert.IsFalse(Port(owner, npc: null).NpcHolds(Ask(3200)), "an NPC the host does not map holds nothing");
        Assert.IsFalse(Port(owner, npc: "nobody").NpcHolds(Ask(3200)), "a holder the session does not list holds nothing");
        Assert.IsFalse(Port(owner).NpcHolds(Ask(3200, Other)), "another PC");
    }

    [TestMethod]
    public void The_holder_list_is_the_current_list_and_unknown_holders_are_null()
    {
        using var owner = Hero();
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy" }, owner.QueryHolderItems("virgil")!.Select(item => item.Entity).ToArray());
        Assert.AreEqual(0, owner.QueryHolderItems("chest")!.Count, "a container's list too");
        Assert.IsNull(owner.QueryHolderItems("nobody"));
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryHolderItems(""));
    }
}
