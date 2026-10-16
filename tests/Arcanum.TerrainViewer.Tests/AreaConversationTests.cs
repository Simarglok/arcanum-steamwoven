using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaConversationTests
{
    // Invented synthetic records; no original dialogue.
    private const string Synthetic =
        "{1}{Hello @pcname@.}{Hello, madam.}{}{}{}{in -40, gf 7 1}" +
        "{2}{Friend.}{}{1}{re 50, ra 1}{10}{re 70, al +2}" +
        "{3}{Rude.}{}{1}{re -10}{0}{}" +
        "{10}{Then come along.}{}{}{}{}{mm 4}" +
        "{11}{Lead on.}{}{1}{lf 3 0}{0}{lf 3 1, jo 0 20}" +
        "{20}{Together, then.}{}{}{}{}{}" +
        "{21}{e:}{}{1}{}{0}{}";

    [TestMethod]
    public void Diagnostic_state_owns_typed_effects_and_reaction_gates()
    {
        var state = new AreaConversationState(50);
        var conversation = Start(state, Owner());

        CollectionAssert.AreEqual(new[] { "1. Friend." }, conversation.Options.Select(AreaConversationText.Option).ToArray());
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(70, state.Reaction(0));
        Assert.AreEqual(2, state.Alignment);
        Assert.IsTrue(state.Areas.SetEquals(new[] { 4 }));
        CollectionAssert.AreEqual(new[] { 40 }, state.Items.ToArray());

        Assert.AreEqual(DialogOptionTransition.Join, conversation.Options[0].Transition);
        conversation.Select(conversation.Options[0].Token);
        Assert.IsTrue(state.Follows(0));
        Assert.IsTrue(state.LocalFlag(0, 3));
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode);
        Assert.AreEqual("1. [generated 'e:' phrase; generic text not loaded]", AreaConversationText.Option(conversation.Options.Single()));
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual("[dialog ended; the caller script would continue at SCR line 5 (not executed)]", AreaConversationText.Outcome(conversation));
        CollectionAssert.AreEqual(new[]
        {
            "#0 turn 1: IN item name 40 NPC -> PC [Applied]",
            "#1 turn 1: GF 7 = 1 [Applied]",
            "#2 turn 1: RE target 70 [Applied]",
            "#3 turn 1: AL adjust +2 [Applied]",
            "#4 turn 2: MM area 4 known [Applied]",
            "#5 turn 2: LF 3 = 1 [Applied]",
            "#6 turn 2: JO follow request [Applied]",
        }, conversation.Effects.Select(AreaConversationText.Effect).ToArray());
    }

    [TestMethod]
    public void Low_reaction_and_other_race_change_the_offered_replies()
    {
        var hostile = new AreaConversationState(5);
        CollectionAssert.AreEqual(new[] { "Rude." }, Start(hostile, Owner()).Options.Select(o => o.Text).ToArray());
        var elf = Start(new AreaConversationState(50), Owner(), race: 2);
        Assert.AreEqual(DialogOptionTextKind.Fallback, elf.Options.Single().TextKind);
    }

    [TestMethod]
    public void Text_formatting_marks_generated_blocked_and_frontier_states()
    {
        var document = DlgReader.Read(Encoding.Latin1.GetBytes("{1}{?}{}{}{}{}{}{2}{b:}{}{1}{}{0}{}{3}{k:}{}{1}{}{0}{}{4}{x}{}{1}{}{0}{zz 1}"));
        var state = new AreaConversationState(50);
        var conversation = DialogConversation.Start(document, Request(), new DialogConversationPorts(state, state, Owner(), state));
        CollectionAssert.AreEqual(new[]
        {
            "1. [generated 'b:' phrase; generic text not loaded]  (not supported here: UnsupportedInteraction)",
            "2. [generated 'k:' phrase; generic text not loaded]",
            "3. x  (not supported here: UnsupportedOperation, record 4 Action op 0)",
        }, conversation.Options.Select(AreaConversationText.Option).ToArray());

        var stopped = DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes("{1}{?}{}{}{}{}{wa 1}{2}{x}{}{1}{}{0}{}")), Request(),
            new DialogConversationPorts(state, state, Owner(), state));
        Assert.AreEqual("[stopped at a frontier: UnsupportedOperation at node 1, record 1 Action op 0]", AreaConversationText.Outcome(stopped));
    }

    // Opt-in: ARC_AREA_CONVERSATION_GAME_ROOT names the user-owned installation (read-only). Structural
    // identifiers only; no original text is asserted or printed.
    [TestMethod, TestCategory("RealData")]
    public void Original_virgil_conversation_runs_from_the_scr_entry_to_its_script_exit()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        string P(string relative) => Path.Combine(root, relative);
        var dialogHash = Fingerprint(P("arcanum3.dat"));
        Assert.AreEqual("cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499", dialogHash, "pinned dialog archive");
        using var data = AreaOriginalValidator.OpenFiles(P("modules/Arcanum.dat"), P("arcanum1.dat"), P("arcanum2.dat"), P("modules/Arcanum/maps/qrmvs-420-1zjcnpgn"), P("data/proto"));
        var snapshot = data.LoadStrict();
        var host = new AreaConversationHost(snapshot, P("arcanum2.dat"), P("arcanum3.dat"));
        var plan = AreaScenePlan.Create(snapshot);
        var body = plan.Commands.Last(command => command.Identity == host.Speakers[0].Row.Key && !command.Additive);
        Assert.AreSame(host.Speakers[0], host.SpeakerAt(plan.Commands, body.Destination.X + body.Destination.Width / 2.0, body.Destination.Y + body.Destination.Height / 2.0));
        Assert.IsNull(host.SpeakerAt(plan.Commands, body.Destination.X - 1000, body.Destination.Y - 1000));

        Assert.AreEqual(1, host.Speakers.Count);
        Assert.AreEqual(1324, host.Speakers[0].ScriptId);
        Assert.IsNull(host.Nearest(host.Speakers[0].WorldX + 4, host.Speakers[0].WorldY));
        var start = host.Begin(host.Nearest(host.Speakers[0].WorldX + 3, host.Speakers[0].WorldY - 3));
        Assert.AreEqual(AreaConversationStartStatus.Started, start.Status, start.Detail);
        Assert.AreEqual("script 1324 SCR line 4 -> DLG node 1", start.Detail);
        var conversation = start.Conversation!;
        var nodes = new List<int>();
        while (conversation.Phase == DialogConversationPhase.AwaitingSelection && nodes.Count < 64)
        {
            nodes.Add(conversation.CurrentNode.Value);
            var option = conversation.Options.FirstOrDefault(o => o.Block is null && o.Transition == DialogOptionTransition.Direct)
                ?? conversation.Options.First(o => o.Block is null);
            conversation.Select(option.Token);
        }
        CollectionAssert.AreEqual(new[] { 1, 9, 15, 19, 24, 30, 32, 35, 57, 71, 120, 210, 74 }, nodes);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), conversation.End);
        CollectionAssert.AreEqual(new[]
        {
            "GlobalFlagWriteCommand", "GlobalFlagWriteCommand", "GlobalFlagWriteCommand", "GiveItemCommand", "GlobalFlagWriteCommand",
            "QuestStateCommand", "RumorLogCommand", "ReactionCommand", "RumorLogCommand", "MarkAreaCommand", "MarkAreaCommand",
            "MarkAreaCommand", "JoinCommand", "MarkAreaCommand", "MarkAreaCommand", "AlignmentCommand",
        }, conversation.Effects.Select(e => e.Command.GetType().Name).ToArray());
        Assert.IsTrue(conversation.Effects.All(e => e.Outcome == DialogCommandAttemptOutcome.Applied));
        Assert.IsTrue(host.State.Follows(0));

        // Following now: the SCR picks another entry; that follower dialog needs WA, an explicit frontier.
        var again = host.Begin(host.Speakers[0]);
        Assert.AreEqual("script 1324 SCR line 6 -> DLG node 385", again.Detail);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, again.Conversation!.Frontier!.Kind);
        Assert.AreEqual(0, again.Conversation.Effects.Count);
        Assert.AreEqual(dialogHash, Fingerprint(P("arcanum3.dat")), "input unchanged");
    }

    private static DialogConversation Start(AreaConversationState state, GlobalFlagOwner owner, int race = 0) =>
        DialogConversation.Start(DlgReader.Read(Encoding.Latin1.GetBytes(Synthetic)), Request(race), new DialogConversationPorts(state, state, owner, state));

    private static DialogConversationRequest Request(int race = 0) => new(new DialogNodeId(1),
        new DialogParticipants(new DialogId(5), AreaConversationState.PcParticipant, AreaConversationState.Npc(0)), new DialogScriptLine(4),
        AreaConversationProfile.Diagnostic.Pc with { Race = race }, AreaConversationProfile.Diagnostic.Names);

    private static GlobalFlagOwner Owner() => GlobalFlagOwner.Create(new GlobalFlagOwnerDescriptor(
        new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid())), new OwnerEpoch(Guid.NewGuid()), 0,
        new[] { new FlagId(7) }, new[] { new GlobalFlagBootstrapValue(new FlagId(7), false) })).Owner!;

    private static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
