using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The dialog fo and wt conditions and wa, lv, so and sc actions over the session party (ARC-DEV-195, ledger CF1–CF7). Entities and numbers are invented.
[TestClass]
public sealed class DialogCompanionPortTests
{
    private static readonly DialogParticipantId Pc = new(800), Npc = new(900), Other = new(901);
    private static readonly PartyWaitFacts Wait = new(3, PersuasionTraining.None, 8, false);

    private static SessionDialogCompanionPort Port(GameSessionOwner owner, string? entity = "virgil", PartyWaitFacts? wait = null) =>
        new(owner, Pc, participant => participant == Npc ? entity : null, participant => participant == Npc ? wait : null);

    private static CompanionCommand Do(DialogCompanionAction action, DialogParticipantId? pc = null) => new(Npc, pc ?? Pc, action);

    [TestMethod]
    public void The_conditions_read_the_party_record_and_fo_stays_true_while_waiting()
    {
        using var owner = Create();
        var port = Port(owner);
        var query = new DialogPartyQuery(Npc, Pc);
        Assert.IsFalse(port.LedByPc(query));
        Assert.IsFalse(port.Jilted(query));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Assert.IsTrue(port.LedByPc(query));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "virgil").Disposition);
        Assert.IsTrue(port.LedByPc(query), "a waiting NPC still has the PC as leader");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LeaveParty(owner.Version, "virgil", new PartyLeave(Force: true, Jilt: true)).Disposition);
        Assert.IsFalse(port.LedByPc(query));
        Assert.IsTrue(port.Jilted(query));
        Assert.ThrowsExactly<ArgumentException>(() => port.LedByPc(new(Other, Pc)), "an unmapped NPC");
        Assert.ThrowsExactly<ArgumentException>(() => port.Jilted(new(Npc, Other)), "another PC");
    }

    [TestMethod]
    public void Wa_makes_a_follower_wait_with_the_timer_facts_and_lv_forces_it_out_of_the_party()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        var port = Port(owner, wait: Wait);
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.Wait)));
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion("virgil").Standing);
        Assert.IsNotNull(owner.QueryWaitTimer("virgil"), "the wait index 3 schedules the wait timer");
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        // Already waiting changes nothing.
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.Wait)));
        Assert.AreEqual(GameSessionDisposition.NoChange, port.LastResult!.Value.Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);

        // LV: a waiting NPC is forced out as well, its timer removed, not jilted.
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.Leave)));
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion("virgil").Standing);
        Assert.IsFalse(owner.QueryCompanion("virgil").Jilted);
        Assert.IsNull(owner.QueryWaitTimer("virgil"));
        // Leaving while not in the party changes nothing (no leader: the source skips it).
        var free = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.Leave)));
        Assert.AreEqual(free, owner.Version.Revision);
    }

    [TestMethod]
    public void Lv_leaves_even_where_an_unforced_leave_is_refused_because_it_is_forced()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        // The control: an unforced leave of a mind-controlled follower is refused (the refusal tests the forced flag skips, CF3).
        var refused = owner.LeaveParty(owner.Version, "virgil", new PartyLeave(Force: false, MindControlled: true));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.LeaveRefused), (refused.Disposition, refused.Failure));
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion("virgil").Standing);
        Assert.AreEqual(DialogCommandResult.Applied, Port(owner).Execute(Do(DialogCompanionAction.Leave)));
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion("virgil").Standing);
        Assert.AreEqual(0, owner.QueryParty().Followers.Count);
    }

    [TestMethod]
    public void Wa_on_an_npc_that_is_not_in_the_party_is_rejected_because_the_session_cannot_hold_that_state()
    {
        using var owner = Create();
        var port = Port(owner, wait: Wait);
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(Do(DialogCompanionAction.Wait)));
        Assert.AreEqual(GameSessionFailure.NotInParty, port.LastResult!.Value.Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void So_and_sc_set_and_clear_the_spread_out_flag_in_any_standing()
    {
        using var owner = Create();
        var port = Port(owner);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadOut)));
        Assert.IsTrue(owner.QueryCompanion("virgil").SpreadOut, "a free NPC too");
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadOut)));
        Assert.AreEqual(revision, owner.Version.Revision, "already set: no change");
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadClear)));
        Assert.IsFalse(owner.QueryCompanion("virgil").SpreadOut);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadClear)));
        // Following keeps the flag through a leave (spread out kept).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadOut)));
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.Leave)));
        Assert.IsTrue(owner.QueryCompanion("virgil").SpreadOut);
    }

    [TestMethod]
    public void So_starts_the_goal_only_when_it_newly_sets_the_flag()
    {
        using var owner = Create();
        var started = new List<string>();
        var port = new SessionDialogCompanionPort(owner, Pc, participant => participant == Npc ? "virgil" : null, _ => null, started.Add);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadOut)));
        CollectionAssert.AreEqual(new[] { "virgil" }, started);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadOut)));
        Assert.AreEqual(1, started.Count, "already set: the flag is unchanged and no goal starts");
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadClear)));
        Assert.AreEqual(1, started.Count, "sc starts nothing");
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(Do(DialogCompanionAction.SpreadOut)));
        Assert.AreEqual(2, started.Count, "set again after a clear");
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(Do(DialogCompanionAction.SpreadOut, Other)));
        Assert.AreEqual(2, started.Count, "a rejected action starts nothing");
    }

    [TestMethod]
    public void An_unmapped_npc_and_another_pc_are_rejected_without_effect()
    {
        using var owner = Create();
        var revision = owner.Version.Revision;
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner, entity: null).Execute(Do(DialogCompanionAction.SpreadOut)));
        Assert.AreEqual(DialogCommandResult.Rejected, Port(owner).Execute(Do(DialogCompanionAction.SpreadOut, Other)));
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void The_port_drives_the_conversation_runtime_end_to_end()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "virgil").Disposition);
        var document = Arcanum.Data.DlgReader.Read(System.Text.Encoding.Latin1.GetBytes(
            "{10}{?}{}{}{}{}{}" + "{11}{Stay.}{}{1}{fo 1}{0}{wa, so}" + "{12}{Come.}{}{1}{fo 0}{0}{}"));
        var commands = new NoCommands();
        var port = Port(owner, wait: Wait);
        var talk = DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(10), new DialogParticipants(new DialogId(700), Pc, Npc), new DialogScriptLine(0),
                new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Virgil")),
            new DialogConversationPorts(commands, commands, null, Companion: port), new DialogConversationOptions());
        Assert.AreEqual("Stay.", talk.Options.Single().Text, "the NPC follows: only the fo 1 reply");
        talk.Select(talk.Options[0].Token);
        Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion("virgil").Standing);
        Assert.IsTrue(owner.QueryCompanion("virgil").SpreadOut);
    }

    private sealed class NoCommands : IDialogStateQueryPort, IDialogCommandPort
    {
        public LocalFlagReadResult Read(LocalFlagQuery query) => throw new InvalidOperationException();
        public DialogCommandResult Execute(LocalFlagWriteCommand command) => throw new InvalidOperationException();
        public DialogCommandResult Execute(QuestStateCommand command) => throw new InvalidOperationException();
        public DialogCommandResult Execute(RumorLogCommand command) => throw new InvalidOperationException();
    }
}
