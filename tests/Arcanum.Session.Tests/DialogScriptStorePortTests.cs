using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-152: <see cref="SessionDialogScriptStorePort"/> reads and writes the session's global variables and the hero's
/// variables and flags as versioned writes; another PC is refused, and a session without a hero reads 0 and ignores a PC write.</summary>
[TestClass]
public sealed class DialogScriptStorePortTests
{
    private static readonly DialogParticipantId Pc = new(1), Other = new(2);

    [TestMethod]
    public void The_port_reads_and_writes_every_store_of_the_session_hero()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var port = new SessionDialogScriptStorePort(owner, Pc);
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, DialogScriptStore.GlobalVariable, 1999, -5)));
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, DialogScriptStore.PcVariable, 65535, 8)));
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, DialogScriptStore.PcFlag, 33, 1)));
        Assert.AreEqual(revision + 3, owner.Version.Revision);
        Assert.AreEqual((-5, 8, true, false), (owner.QueryGlobalVariable(1999), owner.QueryPcVariable(65535), owner.QueryPcFlag(33), owner.QueryPcFlag(32)));
        Assert.AreEqual((-5, 8, 1, 0), (port.Read(new(Pc, DialogScriptStore.GlobalVariable, 1999)), port.Read(new(Pc, DialogScriptStore.PcVariable, 65535)),
            port.Read(new(Pc, DialogScriptStore.PcFlag, 33)), port.Read(new(Pc, DialogScriptStore.PcFlag, 34))));

        // The value already held is applied with no write.
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, DialogScriptStore.PcFlag, 33, 1)));
        Assert.AreEqual(revision + 3, owner.Version.Revision);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, DialogScriptStore.PcFlag, 33, 0)));
        Assert.IsFalse(owner.QueryPcFlag(33));
    }

    [TestMethod]
    public void Another_pc_and_an_invalid_cell_are_refused_and_a_session_without_a_hero_ignores_pc_writes()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var port = new SessionDialogScriptStorePort(owner, Pc);
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new(Other, DialogScriptStore.GlobalVariable, 1, 1)));
        Assert.ThrowsExactly<ArgumentException>(() => port.Read(new(Other, DialogScriptStore.GlobalVariable, 1)));
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new(Pc, DialogScriptStore.PcFlag, 3, 2)), "a PC flag takes 0 or 1");
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new(Pc, DialogScriptStore.GlobalVariable, 2000, 1)));
        Assert.ThrowsExactly<ArgumentNullException>(() => port.Execute(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SessionDialogScriptStorePort(null!, Pc));

        using var heroless = Create();
        var bare = new SessionDialogScriptStorePort(heroless, Pc);
        Assert.AreEqual(DialogCommandResult.Applied, bare.Execute(new(Pc, DialogScriptStore.GlobalVariable, 4, 4)));
        // SA14: no PC to write to, the write is ignored and the dialog goes on.
        Assert.AreEqual(DialogCommandResult.Applied, bare.Execute(new(Pc, DialogScriptStore.PcVariable, 4, 4)));
        Assert.AreEqual(DialogCommandResult.Applied, bare.Execute(new(Pc, DialogScriptStore.PcFlag, 4, 1)));
        Assert.AreEqual((4, 0, 0), (bare.Read(new(Pc, DialogScriptStore.GlobalVariable, 4)), bare.Read(new(Pc, DialogScriptStore.PcVariable, 4)),
            bare.Read(new(Pc, DialogScriptStore.PcFlag, 4))));
    }
}
