using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-013: Virgil's original DLG through the conversation runtime with the session party port. The viewer
/// itself is not wired to the party port yet; this checks the Session/Scripting seam on real data. Opt-in:
/// ARC_AREA_CONVERSATION_GAME_ROOT names the user-owned installation (read-only). Structural identifiers only; no
/// original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class VirgilPartyTests
{
    // Virgil's follow inputs as measured by ARC-RES-026 (AI row 5, level 1, alignment 1); the PC is the diagnostic
    // profile (charisma 8, level 1, no Persuasion), so max followers is 2.
    private static readonly FollowThresholds Row5 = new(-1000, 2001, 2001, 30);

    [TestMethod, TestCategory("RealData")]
    public void Original_virgil_joins_waits_and_rejoins_through_the_session_party()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        string P(string relative) => Path.Combine(root, relative);
        using var data = AreaOriginalValidator.OpenFiles(P("modules/Arcanum.dat"), P("arcanum1.dat"), P("arcanum2.dat"), P("modules/Arcanum/maps/qrmvs-420-1zjcnpgn"), P("data/proto"));
        var snapshot = data.LoadStrict();
        var document = VirgilDialog(P("arcanum3.dat"));

        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        using var scripts = ConversationSessionFixture.Scripts(root);
        var host = new AreaConversationHost(snapshot, scripts, session);
        var speaker = host.Speakers.Single();
        var entity = AreaConversationState.EntityId(speaker.Row);
        // Begin binds the speaker's attachment flags; its own conversation is closed before any reply runs.
        var begin = host.Begin(host.Nearest(speaker.WorldX, speaker.WorldY));
        Assert.AreEqual("script 1324 SCR line 4 -> DLG node 1", begin.Detail);
        Assert.IsTrue(begin.Conversation!.Close());
        var party = Port(session, host, entity);

        // First talk: the same walk as the viewer test, now with JO through the session party.
        var first = Start(document, host, session, party, 1, 4);
        var nodes = Walk(first);
        CollectionAssert.AreEqual(new[] { 1, 9, 15, 19, 24, 30, 32, 35, 57, 71, 120, 210, 74 }, nodes);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), first.End);
        var join = first.Effects.Single(effect => effect.Command is JoinCommand);
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, join.Outcome);
        // The walk raises the PC's alignment (AL +5) after the join, so the session arms Virgil's check-leader and
        // "too good" loyalty marks (ARC-WORLD-045, OL17: a PC alignment rise arms every follower in its array).
        var effects = first.Effects.Select(effect => effect.Command).ToList();
        var alignment = effects.FindLastIndex(command => command is AlignmentCommand { Mode: DialogReactionMode.Adjust, Value: > 0 });
        Assert.IsTrue(alignment > effects.FindIndex(command => command is JoinCommand), "the alignment rise follows the join");
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, first.Effects[alignment].Outcome);
        Assert.AreEqual(new SessionCompanion(entity, CompanionStanding.Following, false, false, false,
            CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooGood), session.QueryCompanion(entity));
        Assert.IsFalse(host.State.Follows(0), "the diagnostic follow.pc key is not written through the party port");

        // Waiting: node 385 (the follower entry) now passes its WA conditions and offers only the UW replies.
        Assert.AreEqual(GameSessionDisposition.Changed, session.WaitParty(session.Version, entity).Disposition);
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        Assert.AreEqual(CompanionStanding.Waiting, loaded.QueryCompanion(entity).Standing, "the party survives save and load");
        Assert.AreEqual(CompanionLoyalty.CheckLeader | CompanionLoyalty.TestTooGood, loaded.QueryCompanion(entity).Loyalty,
            "waiting keeps the marks and they survive save and load");
        var rebound = host.Rebind(loaded);
        party = Port(loaded, rebound, entity);
        var waiting = Start(document, rebound, loaded, party, 385, 6);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, waiting.Phase);
        Assert.IsTrue(waiting.Options.Count > 0);
        foreach (var option in waiting.Options)
        {
            CollectionAssert.Contains(new[] { 392, 393 }, option.ResponseId.Value);
            Assert.AreEqual(DialogOptionTransition.Join, option.Transition);
            Assert.AreEqual(new DialogNodeId(518), option.Target);
            Assert.IsNull(option.Block);
        }
        Assert.AreEqual(DialogConversationSelectStatus.Continued, waiting.Select(waiting.Options[0].Token).Status);
        Assert.AreEqual(new DialogNodeId(518), waiting.CurrentNode);
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, waiting.Effects.Single(effect => effect.Command is UnwaitCommand).Outcome);
        Assert.AreEqual(CompanionStanding.Following, loaded.QueryCompanion(entity).Standing);

        // Following, not waiting: node 385 offers the WA 0 replies instead, none of them a UW.
        var following = Start(document, rebound, loaded, party, 385, 6);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, following.Phase);
        Assert.IsTrue(following.Options.All(option => option.ResponseId.Value is >= 386 and <= 391));
    }

    [TestMethod, TestCategory("RealData")]
    public void Original_virgil_refuses_a_pc_at_the_follower_limit()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        string P(string relative) => Path.Combine(root, relative);
        using var data = AreaOriginalValidator.OpenFiles(P("modules/Arcanum.dat"), P("arcanum1.dat"), P("arcanum2.dat"), P("modules/Arcanum/maps/qrmvs-420-1zjcnpgn"), P("data/proto"));
        var snapshot = data.LoadStrict();
        var document = VirgilDialog(P("arcanum3.dat"));
        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        using var scripts = ConversationSessionFixture.Scripts(root);
        var host = new AreaConversationHost(snapshot, scripts, session);
        var speaker = host.Speakers.Single();
        var entity = AreaConversationState.EntityId(speaker.Row);
        Assert.IsTrue(host.Begin(host.Nearest(speaker.WorldX, speaker.WorldY)).Conversation!.Close());
        // Two other non-forced followers fill the diagnostic PC's limit of 2.
        foreach (var other in new[] { "other-1", "other-2" })
            Assert.AreEqual(GameSessionDisposition.Changed, session.JoinParty(session.Version, other, Facts(host, 0), false).Disposition);

        var party = Port(session, host, entity);
        var talk = Start(document, host, session, party, 1, 4);
        Walk(talk);
        Assert.AreEqual(DialogConversationPhase.AtFrontier, talk.Phase);
        Assert.AreEqual(DialogFrontierKind.JoinRefused, talk.Frontier!.Kind);
        Assert.AreEqual(new DialogNodeId(210), talk.Frontier.Node, "the JO reply of node 210");
        Assert.AreEqual(FollowConsent.LimitReached, party.LastRefusal);
        Assert.AreEqual(CompanionStanding.Free, session.QueryCompanion(entity).Standing);
    }

    private static PartyConsentFacts Facts(AreaConversationHost host, int speaker) => new(
        MindControlledByPc: false, PcPersuasion: PersuasionTraining.None, PcCharisma: 8, PcAlignment: host.State.Alignment, PcLevel: 1,
        OtherLeaderCharisma: null, Reaction: host.State.Reaction(speaker), Thresholds: Row5, NpcAlignment: 1, NpcLevel: 1,
        MaxFollowers: FollowConsentRules.MaxFollowers(8, PersuasionTraining.None));

    private static SessionDialogPartyPort Port(GameSessionOwner session, AreaConversationHost host, string entity) =>
        new(session, AreaConversationState.PcParticipant,
            npc => npc == AreaConversationState.Npc(0) ? entity : null,
            npc => npc == AreaConversationState.Npc(0) ? Facts(host, 0) : null);

    private static DialogConversation Start(DlgDocument document, AreaConversationHost host, GameSessionOwner session,
        SessionDialogPartyPort party, int node, int line) =>
        DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(node),
                new DialogParticipants(new DialogId(1324), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)),
                new DialogScriptLine(line), host.Profile.Pc, host.Profile.Names),
            new DialogConversationPorts(host.State, host.State, session.Flags, host.State, party));

    // The viewer test's choice: the first unblocked Direct reply, else the first unblocked one.
    private static List<int> Walk(DialogConversation conversation)
    {
        var nodes = new List<int>();
        while (conversation.Phase == DialogConversationPhase.AwaitingSelection && nodes.Count < 64)
        {
            nodes.Add(conversation.CurrentNode.Value);
            var option = conversation.Options.FirstOrDefault(o => o.Block is null && o.Transition == DialogOptionTransition.Direct)
                ?? conversation.Options.First(o => o.Block is null);
            conversation.Select(option.Token);
        }
        return nodes;
    }

    // Virgil's DLG (script 1324) from the pinned dialog archive; ARC-RES-026 records its payload hash 34a49a2c…5e53a.
    private static DlgDocument VirgilDialog(string archivePath)
    {
        using var archive = DatArchive.Open(archivePath, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
        var member = archive.Entries.Single(entry =>
            Encoding.Latin1.GetString(entry.RawNameBytes.ToArray()).Contains("01324", StringComparison.Ordinal));
        var bytes = archive.ReadEntry(member);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.IsTrue(hash.StartsWith("34a49a2c", StringComparison.Ordinal) && hash.EndsWith("5e53a", StringComparison.Ordinal), "pinned Virgil DLG");
        return DlgReader.Read(bytes);
    }
}
