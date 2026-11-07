using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog quest log conditions over the session (ARC-DEV-180, ledger DCC4): the state the QU action wrote, read back as the original's quest
// state getter (0x4C4CB0) answers it. Quest numbers are invented.
[TestClass]
public sealed class DialogQuestPortTests
{
    private static readonly DialogParticipantId Pc = new(1), Other = new(2);

    [TestMethod]
    public void An_untouched_quest_is_unknown_and_a_changed_one_reads_back()
    {
        using var owner = Create();
        var port = new SessionDialogQuestPort(owner, Pc);
        Assert.AreEqual(0, port.State(new(Pc, 1003)), "a quest the PC never changed is Unknown");
        Changed(owner.SetQuestState(owner.Version, 1003, SessionQuestState.Mentioned), owner, 1);
        Assert.AreEqual(1, port.State(new(Pc, 1003)));
        Changed(owner.SetQuestState(owner.Version, 1003, SessionQuestState.Accepted), owner, 2);
        Assert.AreEqual(2, port.State(new(Pc, 1003)), "read at each call");
        Changed(owner.SetQuestState(owner.Version, 1003, SessionQuestState.Completed), owner, 3);
        Assert.AreEqual(4, port.State(new(Pc, 1003)));
        Assert.AreEqual(0, port.State(new(Pc, 1004)), "another quest is untouched");
    }

    [TestMethod]
    public void A_botched_quest_reads_six_over_its_kept_state()
    {
        using var owner = Create();
        var port = new SessionDialogQuestPort(owner, Pc);
        Changed(owner.SetQuestState(owner.Version, 1010, SessionQuestState.Botched), owner, 1);
        Assert.AreEqual(6, port.State(new(Pc, 1010)), "botched while still Unknown: the botched bit makes the getter answer 6 (0x4C4D01)");
        Changed(owner.SetQuestState(owner.Version, 1011, SessionQuestState.Accepted), owner, 2);
        Changed(owner.SetQuestState(owner.Version, 1011, SessionQuestState.Botched), owner, 3);
        Assert.AreEqual(6, port.State(new(Pc, 1011)));
    }

    [TestMethod]
    public void A_pc_other_than_the_sessions_throws_and_a_quest_outside_the_numbers_is_refused()
    {
        using var owner = Create();
        var port = new SessionDialogQuestPort(owner, Pc);
        Assert.ThrowsExactly<ArgumentException>(() => port.State(new(Other, 1003)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => port.State(new(Pc, 999)));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SessionDialogQuestPort(null!, Pc));
    }
}
