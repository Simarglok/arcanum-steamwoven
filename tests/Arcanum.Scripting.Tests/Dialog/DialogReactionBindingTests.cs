using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-WORLD-043: the ME condition reads "met before" through its port; a Player <c>i:</c> reply is an insult that changes the
/// reaction by −10 after the reply's action and ends the talk at a reaction of 20 or below (0x4150D0, 0x4191E0).
/// </summary>
[TestClass]
public sealed class DialogReactionBindingTests
{
    [TestMethod]
    public void Me_reads_met_before_through_its_port_in_condition_order()
    {
        var document = Read(Record("10", "?")
            + Record("11", "new", iq: "1", condition: "ME 0", response: "0")
            + Record("12", "known", iq: "1", condition: "me1", response: "0")
            + Record("13", "both", iq: "1", condition: "LF 0 1, ME 1", response: "0"));
        var met = new Memory { Value = false };
        var first = StartWith(document, new Ports(), new World(), met);
        CollectionAssert.AreEqual(new[] { "new" }, first.Options.Select(o => o.Text).ToArray());
        Assert.AreEqual(2, met.Reads, "the third reply stops at its false LF before ME");

        met.Value = true;
        var ports = new Ports();
        ports.Flags[0] = LocalFlagReadResult.Found(1);
        var second = StartWith(document, ports, new World(), met);
        CollectionAssert.AreEqual(new[] { "known", "both" }, second.Options.Select(o => o.Text).ToArray());
    }

    [TestMethod]
    public void Me_without_its_port_is_a_static_frontier_and_only_0_or_1_is_admitted()
    {
        var gated = Read(Record("10", "?", action: "QU 1 1") + Record("11", "x", iq: "1", condition: "ME 1", response: "0"));
        var ports = new Ports();
        var unavailable = StartWith(gated, ports, new World(), null);
        Assert.AreEqual(DialogFrontierKind.CapabilityUnavailable, unavailable.Frontier!.Kind);
        Assert.AreEqual(new DialogResponseId(11), unavailable.Frontier.Response);
        Assert.AreEqual(0, ports.Commands.Count, "a static frontier precedes every effect");

        foreach (var condition in new[] { "ME 2", "ME -1" })
        {
            // The original passes these without a test: valid data this subset does not run.
            var other = StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0")), new Ports(), new World(), new Memory());
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, other.Frontier!.Kind, condition);
        }
        foreach (var condition in new[] { "ME +1", "ME" })
        {
            var invalid = StartWith(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: condition, response: "0")), new Ports(), new World(), new Memory());
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, invalid.Failure!.Kind, condition);
        }
        var action = StartWith(Read(Record("10", "?", action: "ME 1") + Record("11", "x", iq: "1", response: "0")), new Ports(), new World(), new Memory());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, action.Frontier!.Kind, "ME is a condition only");
    }

    [TestMethod]
    public void A_met_before_port_that_throws_fails_the_conversation()
    {
        var document = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "ME 0", response: "0"));
        var failed = StartWith(document, new Ports(), new World(), new Memory { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.QueryException, failed.Failure!.Kind);
    }

    [TestMethod]
    public void An_insult_is_a_generated_reply_that_keeps_its_transition_and_runs_after_its_action()
    {
        var document = Read(Record("10", "?")
            + Record("11", "i:", iq: "1", response: "20", action: "QU 4 1")
            + Record("20", "Hm.")
            + Record("21", "x", iq: "1", response: "0"));
        var world = new World();
        var ports = new Ports();
        var conversation = StartWith(document, ports, world, null);
        var insult = conversation.Options[0];
        Assert.AreEqual(DialogOptionTextKind.Generated, insult.TextKind);
        Assert.AreEqual(new DialogGeneratedMarker('i'), insult.Marker);
        Assert.AreEqual(DialogOptionTransition.Direct, insult.Transition);
        Assert.IsNull(insult.Block);

        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(insult.Token).Status);
        Assert.AreEqual(new DialogNodeId(20), conversation.CurrentNode, "50 − 10 = 40 stays above 20");
        CollectionAssert.AreEqual(new[] { "QU:4:1:900" }, ports.Commands, "the reply's action runs first");
        CollectionAssert.AreEqual(new[] { "RE:Adjust:-10" }, world.Commands);
        Assert.AreEqual(40, world.Reaction.Value);
        var receipt = conversation.Effects.Last();
        Assert.AreEqual(new ReactionCommand(new DialogParticipantId(900), new DialogParticipantId(800), DialogReactionMode.Adjust, DialogConversation.InsultReaction), receipt.Command);
        Assert.AreEqual((DlgFieldKind.Text, new DialogNodeId(11)), (receipt.Source.FieldKind, receipt.Source.RecordId), "the marker is the insult's source");
        Assert.AreEqual(DialogCommandAttemptOutcome.Applied, receipt.Outcome);
    }

    [TestMethod]
    public void An_insult_ends_the_talk_at_a_reaction_of_20_or_below_without_a_script_continuation()
    {
        var document = Read(Record("10", "?") + Record("11", "I:", iq: "1", response: "-7") + Record("12", "x", iq: "1", response: "0"));
        var ended = new World { Reaction = DialogReactionReadResult.Of(30) };
        var conversation = StartWith(document, new Ports(), ended, null);
        Assert.AreEqual(DialogOptionTransition.ScriptLine, conversation.Options[0].Transition, "either case of the marker");
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.Insulted, null), conversation.End);
        Assert.AreEqual(new DialogNpcLine(new DialogNodeId(10), "?"), conversation.NpcLine, "the NPC's generated reply is not loaded");

        var kept = new World { Reaction = DialogReactionReadResult.Of(31) };
        var survived = StartWith(document, new Ports(), kept, null);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, survived.Select(survived.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(7)), survived.End, "21 stays above 20");
    }

    [TestMethod]
    public void An_insult_reads_the_reaction_its_change_left()
    {
        // The owner's adjustment effects may change the delta: the decision reads the effective reaction after it.
        var document = Read(Record("10", "?") + Record("11", "i:", iq: "1", response: "0"));
        var halved = new World { Reaction = DialogReactionReadResult.Of(25), Scale = 0.5 };
        var conversation = StartWith(document, new Ports(), halved, null);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(20, halved.Reaction.Value);
        Assert.AreEqual(DialogEndKind.Insulted, conversation.End!.Kind);
    }

    [TestMethod]
    public void Insult_refusals_unknown_reaction_and_unsupported_forms_stop_typed()
    {
        var document = Read(Record("10", "?") + Record("11", "i:", iq: "1", response: "0"));
        var stopped = StartWith(document, new Ports(), new World { ReadUnknown = true }, null);
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, stopped.Select(stopped.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.ReactionUnknown, stopped.Frontier!.Kind);

        var rejecting = new World { Reject = true };
        var rejected = StartWith(document, new Ports(), rejecting, null);
        Assert.AreEqual(DialogConversationSelectStatus.Failed, rejected.Select(rejected.Options[0].Token).Status);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Single().Outcome);

        var worldless = DialogConversation.Start(document, Request(), new DialogConversationPorts(new Ports(), new Ports(), Owner()));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, worldless.Options[0].Block!.Kind, "the insult needs the reaction port");

        foreach (var action in new[] { "FL 30", "JO 0 30" })
        {
            var transfer = StartWith(Read(Record("10", "?") + Record("11", "i:", iq: "1", response: "0", action: action) + Record("30", "!")), new Ports(), new World(), null);
            Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, transfer.Options[0].Block!.Kind, action);
        }
        var expert = DialogConversation.Start(Read(Record("10", "?") + Record("11", "i:", iq: "1", response: "0", action: "ET 2 30") + Record("30", "!")),
            Request(), new DialogConversationPorts(new Ports(), new Ports(), Owner(), new World(), Skills: new DialogSkillTests.Skills()));
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, expert.Options[0].Block!.Kind, "ET");
        var prefixed = StartWith(Read(Record("10", "?") + Record("11", "i: more", iq: "1", response: "0")), new Ports(), new World(), null);
        Assert.AreEqual(DialogOptionTextKind.Record, prefixed.Options[0].TextKind, "only an exact marker is an insult");
    }

    [TestMethod]
    public void The_insult_counts_in_the_effect_budget()
    {
        var document = Read(Record("10", "?") + Record("11", "i:", iq: "1", response: "0", action: "QU 1 1"));
        var ports = new Ports();
        var world = new World();
        var conversation = DialogConversation.Start(document, Request(), new DialogConversationPorts(ports, ports, Owner(), world),
            new DialogConversationOptions { MaxEffects = 1 });
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.EffectLimit, conversation.Frontier!.Kind);
        Assert.AreEqual(0, ports.Commands.Count + world.Commands.Count);
    }

    private static DialogConversationRequest Request() => new(new DialogNodeId(10),
        new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
        new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram"));

    private static DialogConversation StartWith(DlgDocument document, Ports ports, IDialogWorldPort world, IDialogMetBeforePort? met) =>
        DialogConversation.Start(document, Request(), new DialogConversationPorts(ports, ports, Owner(), world, MetBefore: met));

    private sealed class Memory : IDialogMetBeforePort
    {
        public bool Value { get; set; }
        public bool Throw { get; init; }
        public int Reads { get; private set; }

        public bool MetBefore(DialogReactionQuery query)
        {
            Reads++;
            Assert.AreEqual(new DialogReactionQuery(new DialogParticipantId(900), new DialogParticipantId(800)), query);
            if (Throw) throw new InvalidOperationException("Synthetic query failure.");
            return Value;
        }
    }

    /// <summary>A reaction owner whose adjustment effects scale every delta, truncated toward zero.</summary>
    private sealed class World : IDialogWorldPort
    {
        public DialogReactionReadResult Reaction { get; set; } = DialogReactionReadResult.Of(50);
        public double Scale { get; init; } = 1;
        public bool Reject { get; init; }
        public bool ReadUnknown { get; init; }
        public List<string> Commands { get; } = [];

        public DialogReactionReadResult ReadReaction(DialogReactionQuery query) => ReadUnknown ? DialogReactionReadResult.Unknown() : Reaction;

        public DialogCommandResult Execute(ReactionCommand command)
        {
            if (Reject) return DialogCommandResult.Rejected;
            Commands.Add($"RE:{command.Mode}:{command.Value}");
            Reaction = DialogReactionReadResult.Of(command.Mode == DialogReactionMode.Target ? command.Value : Reaction.Value + (int)(command.Value * Scale));
            return DialogCommandResult.Applied;
        }

        public DialogCommandResult Execute(GiveItemCommand command) => DialogCommandResult.Applied;
        public DialogCommandResult Execute(MarkAreaCommand command) => DialogCommandResult.Applied;
        public DialogCommandResult Execute(AlignmentCommand command) => DialogCommandResult.Applied;
        public DialogJoinResult Execute(JoinCommand command) { Commands.Add("JO"); return DialogJoinResult.Accepted; }
    }
}
