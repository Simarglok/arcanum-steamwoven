using System.Text;
using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// A synthetic DLG driven through the real conversation runtime with the session's party port.
[TestClass]
public sealed class SessionDialogPartyPortTests
{
    private static readonly DialogParticipantId Pc = new(800);
    private static readonly DialogParticipantId Virgil = new(900);
    private static readonly DialogParticipantId Stranger = new(901);

    // Node 10 offers JO, WA-gated UW and a wait-free exit; node 30 follows the join.
    private static readonly DlgDocument Document = DlgReader.Read(Encoding.Latin1.GetBytes(
        Record("10", "?")
        + Record("11", "Come with me.", iq: "1", condition: "WA 0", response: "0", action: "JO 0 30")
        + Record("12", "Come along again.", iq: "1", condition: "WA 1", response: "0", action: "UW 0 30")
        + Record("30", "Together.")));

    [TestMethod]
    public void JoinThenWaitThenUnwaitThroughTheConversation()
    {
        using var owner = Create();
        var port = Port(owner, maxFollowers: 2);
        var talk = Talk(port, Virgil);
        Assert.AreEqual("Come with me.", talk.Options.Single().Text);
        Assert.AreEqual(DialogConversationSelectStatus.Continued, talk.Select(talk.Options[0].Token).Status);
        Assert.AreEqual(CompanionStanding.Following, owner.QueryCompanion("virgil").Standing);
        Assert.IsNull(port.LastRefusal);

        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "virgil").Disposition);
        talk = Talk(port, Virgil);
        Assert.AreEqual("Come along again.", talk.Options.Single().Text);
        Assert.AreEqual(DialogConversationSelectStatus.Continued, talk.Select(talk.Options[0].Token).Status);
        CollectionAssert.AreEqual(new[] { "virgil" }, owner.QueryParty().Followers.Select(f => f.Entity).ToArray());
        Assert.IsFalse(owner.QueryParty().Followers[0].Forced, "JO 0 / UW 0 never force");
    }

    [TestMethod]
    public void ARefusalStopsAtTheFrontierAndKeepsTheReason()
    {
        using var owner = Create();
        var port = Port(owner, maxFollowers: 1);
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.JoinParty(owner.Version, "sogg", PartyStateTests.Facts(), false).Disposition);

        var talk = Talk(port, Virgil);
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, talk.Select(talk.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.JoinRefused, talk.Frontier!.Kind);
        Assert.AreEqual(FollowConsent.LimitReached, port.LastRefusal);
        Assert.AreEqual(CompanionStanding.Free, owner.QueryCompanion("virgil").Standing);
    }

    [TestMethod]
    public void UnmappedParticipantsAndAnotherPcAreRejectedWithoutEffect()
    {
        using var owner = Create();
        var port = Port(owner, maxFollowers: 2);
        Assert.AreEqual(DialogJoinResult.Rejected, port.Execute(new JoinCommand(Stranger, Pc)));
        Assert.AreEqual(DialogJoinResult.Rejected, port.Execute(new JoinCommand(Virgil, new DialogParticipantId(5))));
        Assert.AreEqual(DialogJoinResult.Rejected, port.Execute(new UnwaitCommand(Virgil, Pc)), "not waiting");
        Assert.ThrowsExactly<ArgumentException>(() => port.IsWaiting(new(Stranger, Pc)));
        Assert.ThrowsExactly<ArgumentException>(() => port.IsWaiting(new(Virgil, Virgil)));
        Assert.AreEqual(0L, owner.Version.Revision);

        var noFacts = new SessionDialogPartyPort(owner, Pc, npc => npc == Virgil ? "virgil" : null, _ => null);
        Assert.AreEqual(DialogJoinResult.Rejected, noFacts.Execute(new JoinCommand(Virgil, Pc)));
        Assert.AreEqual(0L, owner.Version.Revision);
    }

    // ARC-WORLD-2148 (0x415BA0 case 0x15): UW of a speaker an NPC leads runs the dialog's own check with the hero as the player first (the leader's charisma against the hero's), then the leader's check.
    [TestMethod]
    public void UnwaitOfASpeakerAnNpcLeadsRunsTheOuterCheckWithTheHeroFirst()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinLeader(owner.Version, "virgil", "oid:mage").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WaitParty(owner.Version, "virgil", new PartyWaitFacts(2, PersuasionTraining.None, 10, false)).Disposition);
        NpcLeaderUnwaitFacts Leader(int charisma) => new(PersuasionTraining.None, 0, 5, 3, new(-1000, 2001, 2001, 30), 0, 1, false, false, charisma);
        var heroCharisma = PartyStateTests.Facts().PcCharisma;
        SessionDialogPartyPort PortWith(int charisma) => new(owner, Pc, npc => npc == Virgil ? "virgil" : null, npc => npc == Virgil ? PartyStateTests.Facts() : null,
            npc => npc == Virgil ? Leader(charisma) : null);
        var version = owner.Version;
        var refused = PortWith(heroCharisma);
        Assert.AreEqual(DialogJoinResult.Refused, refused.Execute(new UnwaitCommand(Virgil, Pc)));
        Assert.AreEqual(FollowConsent.AlreadyInGroup, refused.LastRefusal);
        Assert.AreEqual(version, owner.Version, "a refusal changes nothing");
        Assert.IsTrue(owner.QueryCompanion("virgil").WaitFlag);
        var accepted = PortWith(heroCharisma - 1);
        Assert.AreEqual(DialogJoinResult.Accepted, accepted.Execute(new UnwaitCommand(Virgil, Pc)));
        Assert.IsFalse(owner.QueryCompanion("virgil").WaitFlag);
        Assert.AreEqual("oid:mage", owner.QueryLeader("virgil"));
        // Without the dialog's facts of the speaker nothing is unwaited.
        Assert.AreEqual(DialogJoinResult.Rejected, new SessionDialogPartyPort(owner, Pc, npc => npc == Virgil ? "virgil" : null, _ => null, _ => Leader(1)).Execute(new UnwaitCommand(Virgil, Pc)));
    }

    private static SessionDialogPartyPort Port(GameSessionOwner owner, int maxFollowers) => new(owner, Pc,
        npc => npc == Virgil ? "virgil" : null,
        npc => npc == Virgil ? PartyStateTests.Facts(maxFollowers) : null);

    private static DialogConversation Talk(SessionDialogPartyPort party, DialogParticipantId npc)
    {
        var commands = new NoCommands();
        return DialogConversation.Start(Document,
            new DialogConversationRequest(new DialogNodeId(10), new DialogParticipants(new DialogId(700), Pc, npc),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Virgil")),
            new DialogConversationPorts(commands, commands, null, Party: party), new DialogConversationOptions());
    }

    private static string Record(string id, string text = "", string alternate = "", string iq = "", string condition = "", string response = "", string action = "") =>
        $"{{{id}}}{{{text}}}{{{alternate}}}{{{iq}}}{{{condition}}}{{{response}}}{{{action}}}";

    private sealed class NoCommands : IDialogStateQueryPort, IDialogCommandPort
    {
        public LocalFlagReadResult Read(LocalFlagQuery query) => throw new InvalidOperationException();
        public DialogCommandResult Execute(LocalFlagWriteCommand command) => throw new InvalidOperationException();
        public DialogCommandResult Execute(QuestStateCommand command) => throw new InvalidOperationException();
        public DialogCommandResult Execute(RumorLogCommand command) => throw new InvalidOperationException();
    }
}
