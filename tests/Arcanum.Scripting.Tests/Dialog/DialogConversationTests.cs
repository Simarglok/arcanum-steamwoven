using System.Text;
using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

// All records, identifiers, names and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogConversationTests
{
    [TestMethod]
    public void Conversation_runs_entry_reply_target_and_exit_in_order_with_global_flags()
    {
        var document = Read(
            Record("10", "Hello @pcname@.", "Hello, madam.", action: "GF 3 1, QU 77 2")
            + Record("11", "Ask about the road.", iq: "1", condition: "GF 3 1", response: "20", action: "gf 4 0")
            + Record("12", "Leave.", iq: "1", response: "0")
            + Record("20", "The road is long, @pcname@.", action: "RU 1007")
            + Record("21", "Goodbye.", iq: "1", condition: "lf 2 1", response: "0", action: "LF 5 1"));
        var owner = Owner();
        var ports = new Ports { Flags = { [2] = LocalFlagReadResult.Found(1) } };

        var conversation = Start(document, ports, owner, entry: 10, callerLine: 4);

        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        Assert.AreEqual(1, conversation.Turn);
        Assert.AreEqual(new DialogNpcLine(new DialogNodeId(10), "Hello Ada."), conversation.NpcLine);
        CollectionAssert.AreEqual(new[] { "Ask about the road.", "Leave." }, conversation.Options.Select(o => o.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, conversation.Options.Select(o => o.Number).ToArray());
        Assert.AreEqual(DialogOptionTransition.Direct, conversation.Options[0].Transition);
        Assert.AreEqual(new DialogNodeId(20), conversation.Options[0].Target);
        Assert.AreEqual(DialogOptionTransition.Exit, conversation.Options[1].Transition);

        var first = conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationSelectStatus.Continued, first.Status);
        Assert.AreEqual(2, conversation.Turn);
        Assert.AreEqual(new DialogNpcLine(new DialogNodeId(20), "The road is long, Ada."), conversation.NpcLine);
        Assert.AreEqual("Goodbye.", conversation.Options.Single().Text);

        var last = conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, last.Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(5)), conversation.End);
        Assert.AreEqual(0, conversation.Options.Count);
        CollectionAssert.AreEqual(new[] { "QU:77:2:900", "RU:1007", "LF:5:1:900" }, ports.Commands);
        CollectionAssert.AreEqual(
            new[] { "GF(3,True)@1", "QU@1", "GF(4,False)@1", "RU@2", "LF@2" },
            conversation.Effects.Select(Describe).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, conversation.Effects.Select(e => e.Ordinal).ToArray());
        Assert.IsTrue(conversation.Effects.All(e => e.Outcome == DialogCommandAttemptOutcome.Applied));
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, conversation.Effects[0].GlobalFlag!.Disposition);
        Assert.AreEqual(GlobalFlagWriteDisposition.NoChange, conversation.Effects[2].GlobalFlag!.Disposition);
        Assert.AreEqual(1, owner.Version.Revision);
        Assert.IsTrue(owner.CaptureSnapshot().Read(new(new(Guid.NewGuid()), owner.Version, new FlagId(3))).Value);
        Assert.AreEqual(DialogConversationSelectStatus.WrongPhase, conversation.Select(default).Status);
    }

    [TestMethod]
    public void Female_pc_hears_the_alternate_line_and_gender_selector_filters_replies()
    {
        var document = Read(
            Record("10", "Sir\x85", "Madam\x85")
            + Record("11", "As a man.", "1", iq: "1", response: "0")
            + Record("12", "As a woman.", " 0 ", iq: "1", response: "0")
            + Record("13", "Anyone.", iq: "1", response: "0"));

        var female = Start(document, new Ports(), Owner(), gender: DialogPcGender.Female);
        var male = Start(document, new Ports(), Owner(), gender: DialogPcGender.Male);

        Assert.AreEqual("Madam…", female.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "As a woman.", "Anyone." }, female.Options.Select(o => o.Text).ToArray());
        Assert.AreEqual("Sir…", male.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "As a man.", "Anyone." }, male.Options.Select(o => o.Text).ToArray());
    }

    [TestMethod]
    public void Intelligence_gates_use_signed_minimum_and_dumb_maximum()
    {
        var document = Read(
            Record("10", "?")
            + Record("11", "Clever.", iq: "5", response: "0")
            + Record("12", "Simple.", iq: "-4", response: "0"));

        CollectionAssert.AreEqual(new[] { "Clever." }, Start(document, new Ports(), Owner(), intelligence: 5).Options.Select(o => o.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "Simple." }, Start(document, new Ports(), Owner(), intelligence: 4).Options.Select(o => o.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "Simple." }, Start(document, new Ports(), Owner(), intelligence: 9, dumb: true).Options.Select(o => o.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "Simple." }, Start(document, new Ports(), Owner(), intelligence: 4, dumb: true).Options.Select(o => o.Text).ToArray());
    }

    [TestMethod]
    public void Five_eligible_replies_stop_enumeration_and_none_eligible_gives_the_fallback_exit()
    {
        var replies = string.Concat(Enumerable.Range(11, 7).Select(id => Record(id.ToString(), $"R{id}", iq: "1", condition: $"LF {id - 11} 1", response: "0")));
        var document = Read(Record("10", "?") + replies);
        var all = new Ports();
        for (var index = 0; index < 7; index++) all.Flags[index] = LocalFlagReadResult.Found(1);

        var capped = Start(document, all, Owner());
        CollectionAssert.AreEqual(new[] { "R11", "R12", "R13", "R14", "R15" }, capped.Options.Select(o => o.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, all.Queries.Select(q => q.Index).ToArray());

        var none = Start(document, new Ports(), Owner(), callerLine: 8);
        var fallback = none.Options.Single();
        Assert.AreEqual(DialogOptionTextKind.Fallback, fallback.TextKind);
        Assert.AreEqual(DialogOptionTransition.Exit, fallback.Transition);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, none.Select(fallback.Token).Status);
        Assert.AreEqual(new DialogScriptLine(9), none.End!.ScriptLine);
    }

    [TestMethod]
    public void Negative_response_returns_to_the_named_script_line()
    {
        var document = Read(Record("10", "?") + Record("11", "Back.", iq: "1", response: "-6", action: "QU 1 1"));
        var ports = new Ports();
        var conversation = Start(document, ports, Owner(), callerLine: 2);

        Assert.AreEqual(DialogOptionTransition.ScriptLine, conversation.Options[0].Transition);
        Assert.AreEqual(new DialogScriptLine(6), conversation.Options[0].ScriptLine);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(6)), conversation.End);
        CollectionAssert.AreEqual(new[] { "QU:1:1:900" }, ports.Commands);
    }

    [TestMethod]
    public void Global_flag_conditions_need_known_bits_and_an_owner()
    {
        var gated = Read(Record("10", "?", action: "QU 1 1") + Record("11", "If.", iq: "1", condition: "GF 9 1", response: "0"));
        var ports = new Ports();
        var unknown = Start(gated, ports, Owner());
        Assert.AreEqual(DialogConversationPhase.AtFrontier, unknown.Phase);
        Assert.AreEqual(DialogFrontierKind.GlobalFlagUnknown, unknown.Frontier!.Kind);
        CollectionAssert.AreEqual(new[] { "QU:1:1:900" }, ports.Commands, "The entry effect precedes the dynamic gate.");

        var ownerless = new Ports();
        var unavailable = Start(gated, ownerless, null);
        Assert.AreEqual(DialogFrontierKind.CapabilityUnavailable, unavailable.Frontier!.Kind);
        Assert.AreEqual(new DialogResponseId(11), unavailable.Frontier.Response);
        Assert.AreEqual(0, ownerless.Commands.Count, "A static frontier precedes every effect.");
        Assert.AreEqual(0, unavailable.Effects.Count);
    }

    [TestMethod]
    public void Generated_markers_offer_typed_options_and_block_interactions()
    {
        var document = Read(
            Record("10", "?")
            + Record("11", "e:", iq: "1", response: "0")
            + Record("12", "B:", iq: "1", response: "5", action: "QU 1 1")
            + Record("13", "e: not a marker", iq: "1", response: "0"));
        var ports = new Ports();
        var conversation = Start(document, ports, Owner());

        var farewell = conversation.Options[0];
        Assert.AreEqual(DialogOptionTextKind.Generated, farewell.TextKind);
        Assert.AreEqual(new DialogGeneratedMarker('e'), farewell.Marker);
        Assert.AreEqual(DialogOptionTransition.Exit, farewell.Transition);
        Assert.IsNull(farewell.Block);
        var barter = conversation.Options[1];
        Assert.AreEqual(DialogOptionTransition.Unsupported, barter.Transition);
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedInteraction, barter.Block!.Kind);
        Assert.AreEqual(DialogOptionTextKind.Record, conversation.Options[2].TextKind);
        Assert.AreEqual("e: not a marker", conversation.Options[2].Text);

        Assert.AreEqual(DialogConversationSelectStatus.UnsupportedTransition, conversation.Select(barter.Token).Status);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        Assert.AreEqual(0, ports.Commands.Count);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(farewell.Token).Status);
    }

    [TestMethod]
    public void State_dependent_markers_and_generated_npc_lines_are_static_frontiers()
    {
        var quest = Read(Record("10", "?", action: "QU 1 1") + Record("11", "q:12", iq: "1", response: "0"));
        var npc = Read(Record("10", "g: generated", action: "QU 1 1") + Record("11", "x", iq: "1", response: "0"));
        var ports = new Ports();

        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start(quest, ports, Owner()).Frontier!.Kind);
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start(npc, ports, Owner()).Frontier!.Kind);
        Assert.AreEqual(0, ports.Commands.Count);
    }

    [TestMethod]
    public void Npc_generated_markers_are_read_from_the_record_text_for_either_gender()
    {
        var document = Read(Record("10", "g: generated", "Plain for her.") + Record("11", "x", iq: "1", response: "0"));
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start(document, new Ports(), Owner(), gender: DialogPcGender.Female).Frontier!.Kind);
        var undecodable = Read(Record("10", "G:x\x81", "Plain for her.") + Record("11", "x", iq: "1", response: "0"));
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start(undecodable, new Ports(), Owner(), gender: DialogPcGender.Female).Frontier!.Kind);
        var plain = Read(Record("10", "Plain for him.", "Plain for her.") + Record("11", "x", iq: "1", response: "0"));
        Assert.AreEqual("Plain for her.", Start(plain, new Ports(), Owner(), gender: DialogPcGender.Female).NpcLine!.Text);
    }

    [TestMethod]
    public void Unsupported_operations_stop_before_effects_or_block_only_their_reply()
    {
        var entry = Read(Record("10", "?", action: "QU 1 1, ZZ 801") + Record("11", "x", iq: "1", response: "0"));
        var ports = new Ports();
        var stopped = Start(entry, ports, Owner());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, stopped.Frontier!.Kind);
        Assert.AreEqual(1, stopped.Frontier.Source!.Value.OperationOrdinal);
        Assert.AreEqual(0, ports.Commands.Count);

        var condition = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "PS 40", response: "0"));
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Start(condition, new Ports(), Owner()).Frontier!.Kind);
        var worldless = Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "RE 40", response: "0"));
        Assert.AreEqual(DialogFrontierKind.CapabilityUnavailable, Start(worldless, new Ports(), Owner()).Frontier!.Kind);

        var replies = Read(
            Record("10", "?")
            + Record("11", "Join me.", iq: "1", response: "0", action: "JO 1 2")
            + Record("12", "Onward.", iq: "1", response: "20")
            + Record("13", "Plain.", iq: "1", response: "0")
            + Record("20", "!", action: "MM 3"));
        var blockedPorts = new Ports();
        var conversation = Start(replies, blockedPorts, Owner());
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, conversation.Options[0].Block!.Kind);
        Assert.AreEqual(DialogOptionBlockKind.TargetUnsupported, conversation.Options[1].Block!.Kind);
        Assert.IsNull(conversation.Options[2].Block);
        Assert.AreEqual(DialogConversationSelectStatus.UnsupportedTransition, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(DialogConversationSelectStatus.UnsupportedTransition, conversation.Select(conversation.Options[1].Token).Status);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        Assert.AreEqual(0, blockedPorts.Commands.Count);
    }

    [TestMethod]
    public void Grammar_admits_separator_only_fields_and_rejects_malformed_operations()
    {
        var blank = Read(Record("10", "?", action: " , ") + Record("11", "x", iq: "1", condition: " ", response: "0", action: "\t"));
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, Start(blank, new Ports(), Owner()).Phase);

        foreach (var action in new[] { "GF 3 2", "GF -1 1", "LF 32 1", "QU 1 7", "QU -3 1", "QU 0 1", "RU 999", "GF 3", "GF 3 +1", "GF3 1QU 1 1" })
        {
            var ports = new Ports();
            var failed = Start(Read(Record("10", "?", action: action) + Record("11", "x", iq: "1", response: "0")), ports, Owner());
            Assert.AreEqual(DialogConversationPhase.Failed, failed.Phase, action);
            Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, failed.Failure!.Kind, action);
            Assert.AreEqual(0, ports.Commands.Count, action);
        }
        var attached = Start(Read(Record("10", "?", action: "gf3 1;qu 5\t6,") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner());
        Assert.AreEqual(2, attached.Effects.Count);

        // The source reads operands with whitespace-skipping atoi: a non-space separator inside one
        // operation would read a different value there, so these forms are unsupported, never reinterpreted.
        foreach (var action in new[] { "GF 3,1", "LF 3;1", "qu(5,6)", "RU:1000", "RE,+5" })
        {
            var ports = new Ports();
            var stopped = Start(Read(Record("10", "?", action: action) + Record("11", "x", iq: "1", response: "0")), ports, Owner(), world: new World());
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, stopped.Frontier?.Kind, action);
            Assert.AreEqual(0, ports.Commands.Count, action);
        }
    }

    [TestMethod]
    public void Text_decoding_and_substitution_fail_closed()
    {
        var accented = Start(Read(Record("10", "Caf\xE9 @NPCNAME@ / @PcName@") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner());
        Assert.AreEqual("Café Bram / Ada", accented.NpcLine!.Text);

        var undefined = Start(Read(Record("10", "\x81") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner());
        Assert.AreEqual(DialogTextFailureKind.UndefinedByte, undefined.Failure!.TextFailure);
        var control = Start(Read(Record("10", "a\x01") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner());
        Assert.AreEqual(DialogTextFailureKind.ControlCharacter, control.Failure!.TextFailure);
        var open = Start(Read(Record("10", "@pcname") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner());
        Assert.AreEqual(DialogTextFailureKind.UnterminatedToken, open.Failure!.TextFailure);
        var unknown = Start(Read(Record("10", "?") + Record("11", "@gold@", iq: "1", response: "0")), new Ports(), Owner());
        Assert.AreEqual(DialogFrontierKind.UnsupportedTextToken, unknown.Frontier!.Kind);
        var limited = Start(Read(Record("10", "abcdef") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner(),
            options: new DialogConversationOptions { MaxTextBytes = 5 });
        Assert.AreEqual(DialogTextFailureKind.TextLimitExceeded, limited.Failure!.TextFailure);
    }

    [TestMethod]
    public void Changed_gates_make_selection_stale_and_refresh_rotates_tokens()
    {
        var document = Read(Record("10", "?") + Record("11", "If.", iq: "1", condition: "LF 1 1", response: "0", action: "QU 1 1"));
        var ports = new Ports { Flags = { [1] = LocalFlagReadResult.Found(1) } };
        var conversation = Start(document, ports, Owner());
        var old = conversation.Options[0].Token;

        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.RefreshOptions().Status);
        Assert.AreEqual(DialogConversationSelectStatus.NotOffered, conversation.Select(old).Status);
        ports.Flags[1] = LocalFlagReadResult.Found(0);
        Assert.AreEqual(DialogConversationSelectStatus.StaleSelection, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(DialogOptionTextKind.Fallback, conversation.Options.Single().TextKind);
        Assert.AreEqual(0, ports.Commands.Count);
    }

    [TestMethod]
    public void Group_order_mismatch_and_structural_limits_are_static()
    {
        var reordered = Read(Record("10", "?") + Record("12", "b", iq: "1", response: "0") + Record("11", "a", iq: "1", response: "0"));
        Assert.AreEqual(DialogFrontierKind.GroupOrderMismatch, Start(reordered, new Ports(), Owner()).Frontier!.Kind);

        var foreign = Read(Record("10", "?") + Record("30", "late", iq: "1", response: "0") + Record("20", "!") + Record("21", "x", iq: "1", response: "0"));
        Assert.AreEqual(DialogFrontierKind.GroupOrderMismatch, Start(foreign, new Ports(), Owner()).Frontier!.Kind);

        var wide = Read(Record("10", "?") + string.Concat(Enumerable.Range(11, 4).Select(id => Record(id.ToString(), "x", iq: "1", response: "0"))));
        var limited = Start(wide, new Ports(), Owner(), options: new DialogConversationOptions { MaxStructuralReplies = 3 });
        Assert.AreEqual(DialogConversationFailureKind.StructuralReplyLimit, limited.Failure!.Kind);

        var missing = Read(Record("10", "?") + Record("11", "x", iq: "1", response: "99"));
        Assert.AreEqual(DialogConversationFailureKind.MissingTarget, Start(missing, new Ports(), Owner()).Failure!.Kind);
        var player = Read(Record("10", "?") + Record("11", "x", iq: "1", response: "11"));
        Assert.AreEqual(DialogConversationFailureKind.WrongTargetRole, Start(player, new Ports(), Owner()).Failure!.Kind);
        Assert.AreEqual(DialogConversationFailureKind.WrongEntryRole, Start(player, new Ports(), Owner(), entry: 11).Failure!.Kind);
        Assert.AreEqual(DialogConversationFailureKind.InvalidEntry, Start(player, new Ports(), Owner(), entry: 12).Failure!.Kind);
    }

    [TestMethod]
    public void Cycles_stop_at_the_turn_limit_before_effects()
    {
        var document = Read(Record("10", "?", action: "QU 1 1") + Record("11", "again", iq: "1", response: "10"));
        var ports = new Ports();
        var conversation = Start(document, ports, Owner(), options: new DialogConversationOptions { MaxTurns = 3 });

        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.TurnLimit, conversation.Frontier!.Kind);
        Assert.AreEqual(3, ports.Commands.Count);
    }

    [TestMethod]
    public void Rejected_and_throwing_commands_fail_with_the_applied_prefix_kept()
    {
        var document = Read(Record("10", "?", action: "QU 1 1, QU 2 1, QU 3 1") + Record("11", "x", iq: "1", response: "0"));
        var rejected = Start(document, new Ports { RejectAt = 2 }, Owner());
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        CollectionAssert.AreEqual(new[] { DialogCommandAttemptOutcome.Applied, DialogCommandAttemptOutcome.Rejected }, rejected.Effects.Select(e => e.Outcome).ToArray());

        var thrown = Start(document, new Ports { ThrowAt = 1 }, Owner());
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Indeterminate, thrown.Effects.Single().Outcome);

        var owner = Owner();
        owner.Dispose();
        var disposed = Start(Read(Record("10", "?", action: "GF 3 1") + Record("11", "x", iq: "1", response: "0")), new Ports(), owner);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, disposed.Failure!.Kind);
        Assert.AreEqual(GlobalFlagReason.OwnerDisposed, disposed.Failure.GlobalFlagReason);
    }

    [TestMethod]
    public void Host_close_ends_without_effects_or_script_continuation()
    {
        var conversation = Start(Read(Record("10", "?") + Record("11", "x", iq: "1", response: "0", action: "QU 1 1")), new Ports(), Owner());
        Assert.IsTrue(conversation.Close());
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ClosedByHost, null), conversation.End);
        Assert.IsFalse(conversation.Close());
        Assert.AreEqual(0, conversation.Effects.Count);
    }

    [TestMethod]
    public void Effect_budget_is_checked_before_a_step_starts()
    {
        var document = Read(Record("10", "?", action: "QU 1 1") + Record("11", "x", iq: "1", response: "20", action: "QU 2 1") + Record("20", "!", action: "QU 3 1"));
        var ports = new Ports();
        var conversation = Start(document, ports, Owner(), options: new DialogConversationOptions { MaxEffects = 2 });

        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.EffectLimit, conversation.Frontier!.Kind);
        CollectionAssert.AreEqual(new[] { "QU:1:1:900" }, ports.Commands);
    }

    [TestMethod]
    public void Reaction_and_race_gates_are_inclusive_and_signed()
    {
        var document = Read(
            Record("10", "?")
            + Record("11", "r50", iq: "1", condition: "RE 50", response: "0")
            + Record("12", "r51", iq: "1", condition: "re51", response: "0")
            + Record("13", "r-50", iq: "1", condition: "RE -50", response: "0")
            + Record("14", "r-49", iq: "1", condition: "RE-49", response: "0")
            + Record("15", "human", iq: "1", condition: "RA 1", response: "0")
            + Record("16", "dwarf", iq: "1", condition: "RA 2", response: "0")
            + Record("17", "not human", iq: "1", condition: "RA -1", response: "0")
            + Record("18", "not dwarf", iq: "1", condition: "RA -2", response: "0"));

        var world = new World();
        CollectionAssert.AreEqual(new[] { "r50", "r-50", "human", "not dwarf" },
            Start(document, new Ports(), Owner(), world: world, options: null).Options.Select(o => o.Text).ToArray());
        Assert.AreEqual(4, world.Reads);
        CollectionAssert.AreEqual(new[] { "r50", "r-50", "dwarf", "not human" },
            Start(document, new Ports(), Owner(), world: new World(), race: 1).Options.Select(o => o.Text).ToArray());

        var unknown = Start(document, new Ports(), Owner(), world: new World { Reaction = DialogReactionReadResult.Unknown() });
        Assert.AreEqual(DialogFrontierKind.ReactionUnknown, unknown.Frontier!.Kind);
    }

    [TestMethod]
    public void World_commands_are_typed_and_ordered_with_target_and_delta_reaction()
    {
        var document = Read(
            Record("10", "?", action: "in -801, mm 7, mm8, al +3, AL -2, al 5")
            + Record("11", "a", iq: "1", response: "20", action: "RE 35, RE +5, re -10, RE -0")
            + Record("20", "!")
            + Record("21", "b", iq: "1", condition: "RE 30", response: "0"));
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world);
        conversation.Select(conversation.Options[0].Token);

        CollectionAssert.AreEqual(new[] { "IN:801:900>800", "MM:7", "MM:8", "AL:Adjust:3", "AL:Adjust:-2", "AL:Target:5", "RE:Target:35", "RE:Adjust:5", "RE:Adjust:-10", "RE:Adjust:0" }, world.Commands);
        Assert.AreEqual(30, world.Reaction.Value);
        Assert.AreEqual("b", conversation.Options.Single().Text);
        Assert.AreEqual(new ReactionCommand(new DialogParticipantId(900), new DialogParticipantId(800), DialogReactionMode.Target, 35), conversation.Effects[6].Command);
    }

    [TestMethod]
    public void Join_continues_at_its_node_and_a_refusal_is_a_frontier()
    {
        var document = Read(
            Record("10", "?")
            + Record("11", "Come.", iq: "1", response: "0", action: "LF 1 1, JO 0 30")
            + Record("30", "Together.", action: "MM 2")
            + Record("31", "x", iq: "1", response: "0"));
        var world = new World();
        var ports = new Ports();
        var conversation = Start(document, ports, Owner(), world: world);
        var join = conversation.Options[0];
        Assert.AreEqual(DialogOptionTransition.Join, join.Transition);
        Assert.AreEqual(new DialogNodeId(30), join.Target);

        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(join.Token).Status);
        Assert.AreEqual(new DialogNodeId(30), conversation.CurrentNode);
        Assert.AreEqual("Together.", conversation.NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { "JO", "MM:2" }, world.Commands);
        CollectionAssert.AreEqual(new[] { "LF:1:1:900" }, ports.Commands);

        var refused = Start(document, new Ports(), Owner(), world: new World { Join = DialogJoinResult.Refused });
        Assert.AreEqual(DialogConversationSelectStatus.AtFrontier, refused.Select(refused.Options[0].Token).Status);
        Assert.AreEqual(DialogFrontierKind.JoinRefused, refused.Frontier!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, refused.Effects.Last().Outcome);
    }

    [TestMethod]
    public void Final_line_runs_its_node_action_shows_it_and_ends_without_script()
    {
        var document = Read(
            Record("10", "?")
            + Record("11", "Bye.", iq: "1", response: "20", action: "lf 3 1, fl 40")
            + Record("20", "unused")
            + Record("40", "Farewell, @pcname@.", action: "QU 9 1"));
        var ports = new Ports();
        var conversation = Start(document, ports, Owner(), world: new World());
        Assert.AreEqual(DialogOptionTransition.FinalLine, conversation.Options[0].Transition);
        Assert.AreEqual(new DialogNodeId(40), conversation.Options[0].Target);

        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options[0].Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.NpcFinalLine, null), conversation.End);
        Assert.AreEqual(new DialogNpcLine(new DialogNodeId(40), "Farewell, Ada."), conversation.NpcLine);
        CollectionAssert.AreEqual(new[] { "LF:3:1:900", "QU:9:1:900" }, ports.Commands);
    }

    [TestMethod]
    public void Unadmitted_world_forms_are_blocked_or_invalid()
    {
        foreach (var (action, kind) in new[]
        {
            ("JO 0 30, MM 1", DialogOptionBlockKind.UnsupportedOperation),
            ("FL 40, FL 40", DialogOptionBlockKind.UnsupportedOperation),
            ("RE >35", DialogOptionBlockKind.UnsupportedOperation),
            ("AL <5", DialogOptionBlockKind.UnsupportedOperation),
            ("IN 801", DialogOptionBlockKind.UnsupportedOperation),
            ("IN 0", DialogOptionBlockKind.UnsupportedOperation),
            ("JO 2 30", DialogOptionBlockKind.UnsupportedOperation),
            ("FL 11", DialogOptionBlockKind.TargetInvalid),
            ("FL 99", DialogOptionBlockKind.TargetInvalid),
            ("MM -1", DialogOptionBlockKind.InvalidOperation),
            ("FL 0", DialogOptionBlockKind.InvalidOperation),
            ("JO 0,30", DialogOptionBlockKind.UnsupportedOperation),
        })
        {
            var document = Read(Record("10", "?") + Record("11", "x", iq: "1", response: "0", action: action) + Record("30", "!") + Record("40", "!"));
            var world = new World();
            var conversation = Start(document, new Ports(), Owner(), world: world);
            Assert.AreEqual(kind, conversation.Options[0].Block?.Kind, action);
            Assert.AreEqual(DialogConversationSelectStatus.UnsupportedTransition, conversation.Select(conversation.Options[0].Token).Status, action);
            Assert.AreEqual(0, world.Commands.Count, action);
        }
        var npcJoin = Start(Read(Record("10", "?", action: "JO 0 10") + Record("11", "x", iq: "1", response: "0")), new Ports(), Owner(), world: new World());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, npcJoin.Frontier!.Kind);
        var signed = Start(Read(Record("10", "?") + Record("11", "x", iq: "1", condition: "RE +5", response: "0")), new Ports(), Owner(), world: new World());
        Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, signed.Failure!.Kind);
    }

    private static string Describe(DialogEffectReceipt receipt) => receipt.Command switch
    {
        GlobalFlagWriteCommand global => $"GF({global.Flag},{global.Value})@{receipt.Turn}",
        QuestStateCommand => $"QU@{receipt.Turn}",
        RumorLogCommand => $"RU@{receipt.Turn}",
        LocalFlagWriteCommand => $"LF@{receipt.Turn}",
        _ => "?",
    };

    internal static DialogConversation Start(
        DlgDocument document, Ports ports, GlobalFlagOwner? owner, int entry = 10, int callerLine = 0,
        DialogPcGender gender = DialogPcGender.Male, int intelligence = 8, bool dumb = false, DialogConversationOptions? options = null,
        World? world = null, int race = 0, IDialogPartyPort? party = null, IDialogInventoryPort? inventory = null, IDialogSkillPort? skills = null,
        IDialogFatePort? fate = null, IDialogSchematicPort? schematics = null, IDialogBarterPort? barter = null, IDialogExperiencePort? experience = null,
        IDialogGoldPort? gold = null, IDialogQuestPort? quests = null, IDialogCombatPort? combat = null, IDialogStoryPort? story = null,
        IDialogAreaPort? areas = null, IDialogMapAreaPort? mapArea = null) =>
        DialogConversation.Start(document,
            new DialogConversationRequest(
                new DialogNodeId(entry),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(callerLine),
                new DialogPcFacts(gender, intelligence, dumb, race),
                new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(ports, ports, owner, world, party, inventory, Skills: skills, Fate: fate, Schematics: schematics, Barter: barter, Experience: experience, Gold: gold, Quests: quests, Combat: combat, Story: story, Areas: areas, MapArea: mapArea), options);

    internal static GlobalFlagOwner Owner()
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.Parse("11111111-1111-1111-1111-111111111111")), new ModuleId(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var created = GlobalFlagOwner.Create(new GlobalFlagOwnerDescriptor(identity, new OwnerEpoch(Guid.Parse("33333333-3333-3333-3333-333333333333")), 0,
            new[] { new FlagId(3), new FlagId(4), new FlagId(9) },
            new[] { new GlobalFlagBootstrapValue(new FlagId(3), false), new GlobalFlagBootstrapValue(new FlagId(4), false) }));
        return created.Owner!;
    }

    internal static DlgDocument Read(string text) => DlgReader.Read(Encoding.Latin1.GetBytes(text));

    internal static string Record(string id, string text = "", string alternate = "", string iq = "", string condition = "", string response = "", string action = "") =>
        $"{{{id}}}{{{text}}}{{{alternate}}}{{{iq}}}{{{condition}}}{{{response}}}{{{action}}}";

    internal sealed class World : IDialogWorldPort
    {
        public DialogReactionReadResult Reaction { get; set; } = DialogReactionReadResult.Of(50);
        public DialogJoinResult Join { get; init; } = DialogJoinResult.Accepted;
        public List<string> Commands { get; } = [];
        public int Reads { get; private set; }

        public DialogReactionReadResult ReadReaction(DialogReactionQuery query)
        {
            Reads++;
            Assert.AreEqual(new DialogReactionQuery(new DialogParticipantId(900), new DialogParticipantId(800)), query);
            return Reaction;
        }

        public DialogCommandResult Execute(ReactionCommand command)
        {
            Commands.Add($"RE:{command.Mode}:{command.Value}");
            Reaction = DialogReactionReadResult.Of(command.Mode == DialogReactionMode.Target ? command.Value : Reaction.Value + command.Value);
            return DialogCommandResult.Applied;
        }

        public DialogCommandResult Execute(GiveItemCommand command) { Commands.Add($"IN:{command.ItemName}:{command.Npc.Value}>{command.PlayerCharacter.Value}"); return DialogCommandResult.Applied; }
        public DialogCommandResult Execute(MarkAreaCommand command) { Commands.Add($"MM:{command.Area}"); return DialogCommandResult.Applied; }
        public DialogCommandResult Execute(AlignmentCommand command) { Commands.Add($"AL:{command.Mode}:{command.Value}"); return DialogCommandResult.Applied; }
        public DialogJoinResult Execute(JoinCommand command) { Commands.Add("JO"); return Join; }
    }

    internal sealed class Ports : IDialogStateQueryPort, IDialogCommandPort
    {
        public Dictionary<int, LocalFlagReadResult> Flags { get; } = [];
        public List<string> Commands { get; } = [];
        public List<LocalFlagQuery> Queries { get; } = [];
        public int? RejectAt { get; init; }
        public int? ThrowAt { get; init; }

        public LocalFlagReadResult Read(LocalFlagQuery query)
        {
            Queries.Add(query);
            return Flags.TryGetValue(query.Index, out var result) ? result : LocalFlagReadResult.Missing();
        }

        public DialogCommandResult Execute(LocalFlagWriteCommand command) => Execute($"LF:{command.Index}:{command.Value}:{command.Npc.Value}");
        public DialogCommandResult Execute(QuestStateCommand command) => Execute($"QU:{command.QuestId}:{command.State}:{command.NpcContext.Value}");
        public DialogCommandResult Execute(RumorLogCommand command) => Execute($"RU:{command.RumorId}");

        private DialogCommandResult Execute(string value)
        {
            var ordinal = Commands.Count + 1;
            if (RejectAt == ordinal) return DialogCommandResult.Rejected;
            if (ThrowAt == ordinal) throw new InvalidOperationException("Synthetic command failure.");
            Commands.Add(value);
            return DialogCommandResult.Applied;
        }
    }
}
