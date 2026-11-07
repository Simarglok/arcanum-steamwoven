using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog story state and known areas over the session (ARC-DEV-191, ledger DCC7, DAC7, SS1, WT2). Numbers are invented.
[TestClass]
public sealed class DialogStoryPortTests
{
    private static readonly DialogParticipantId Pc = new(1), Other = new(2);

    [TestMethod]
    public void The_story_state_only_rises()
    {
        using var owner = Create();
        var port = new SessionDialogStoryPort(owner, Pc);
        Assert.AreEqual(0, port.State(), "0 at a new game");
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, 5)));
        Assert.AreEqual(5, port.State());
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, 3)), "a lower value is a successful no-change (0x4450DD keeps the larger)");
        Assert.AreEqual(5, port.State());
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, -9)));
        Assert.AreEqual(5, port.State());
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, 5)));
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new(Pc, 12)));
        Assert.AreEqual(12, port.State());
        Assert.ThrowsExactly<ArgumentException>(() => port.Execute(new(Other, 20)));
    }

    [TestMethod]
    public void Known_areas_read_the_set_the_session_holds()
    {
        using var owner = Create();
        var port = new SessionDialogAreaPort(owner, Pc);
        Assert.IsFalse(port.Known(new(Pc, 7)));
        Changed(owner.DiscoverArea(owner.Version, 7), owner, 1);
        Assert.IsTrue(port.Known(new(Pc, 7)));
        Assert.IsFalse(port.Known(new(Pc, 8)));
        Assert.ThrowsExactly<ArgumentException>(() => port.Known(new(Other, 7)));
    }
}
