using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The reply step's hostile check through the hostility port (ARC-DEV-193, ledger HR1–HR4, SU10). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogHostileTests
{
    internal sealed class Hostility : IDialogHostilePort
    {
        public List<DialogHostileQuery> Asked { get; } = [];
        public Func<bool> Answer { get; init; } = () => true;
        public bool Hostile(DialogHostileQuery query)
        {
            Asked.Add(query);
            return Answer();
        }
    }

    private sealed class Throwing : IDialogHostilePort
    {
        public bool Hostile(DialogHostileQuery query) => throw new InvalidOperationException();
    }

    // NPC 10 offers one reply with the given text, response and action, next to the node 20 ("Bye.") and the final line node 40.
    private static DialogConversation Pick(string response, string action, IDialogHostilePort? hostile, Ports? ports = null, World? world = null,
        IDialogCombatPort? combat = null, IDialogBarterPort? barter = null, IDialogSchematicPort? schematics = null, string text = "Go on.")
    {
        var document = Read(Record("10", "?") + Record("11", text, iq: "1", response: response, action: action)
            + Record("20", "Bye.") + Record("40", "Farewell."));
        var conversation = Start(document, ports ?? new Ports(), Owner(), world: world ?? new World(), combat: combat, barter: barter, schematics: schematics, hostile: hostile);
        Assert.IsNull(conversation.Failure, action);
        Assert.IsNull(conversation.Options[0].Block, action);
        conversation.Select(conversation.Options[0].Token);
        return conversation;
    }

    [TestMethod]
    public void A_hostile_npc_ends_the_talk_instead_of_entering_the_next_node()
    {
        var hostility = new Hostility();
        var conversation = Pick("20", "", hostility);
        Assert.IsNull(conversation.Failure);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.HostileRefusal, null), conversation.End);
        Assert.AreEqual("?", conversation.NpcLine!.Text, "the generated group 1000 line is not loaded, the next node is not entered");
        // The NPC (900) is asked about the PC (800).
        Assert.AreEqual(new DialogHostileQuery(new DialogParticipantId(900), new DialogParticipantId(800)), hostility.Asked.Single());
    }

    [TestMethod]
    public void A_npc_that_is_not_hostile_lets_the_reply_continue_as_before()
    {
        var hostility = new Hostility { Answer = () => false };
        var conversation = Pick("20", "", hostility);
        Assert.AreEqual(1, hostility.Asked.Count);
        Assert.AreEqual("Bye.", conversation.NpcLine!.Text);
        Assert.IsNull(conversation.End);
    }

    [TestMethod]
    public void The_check_runs_after_the_attack_event_of_the_same_string()
    {
        // co sets a flag and the event runs at the string's end (CA2); the check follows it, so the NPC the attack made hostile turns on the PC.
        var combat = new DialogCombatTests.Combat();
        var hostility = new Hostility { Answer = () => combat.Started.Count > 0 };
        var conversation = Pick("20", "co", hostility, combat: combat);
        Assert.AreEqual(1, combat.Started.Count);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.HostileRefusal, null), conversation.End);
        Assert.AreEqual("?", conversation.NpcLine!.Text);

        // The actions of the string ran before it; the effects stay.
        var ports = new Ports();
        var late = Pick("20", "lf 3 1, co", new Hostility { Answer = () => ports.Commands.Count > 0 && combat.Started.Count > 1 }, ports, combat: combat);
        CollectionAssert.AreEqual(new[] { "LF:3:1:900" }, ports.Commands);
        Assert.AreEqual(DialogEndKind.HostileRefusal, late.End!.Kind);
    }

    [TestMethod]
    public void An_exit_and_a_script_line_also_end_as_the_hostile_refusal_without_the_script_continuation()
    {
        foreach (var response in new[] { "0", "-7" })
        {
            var refused = Pick(response, "", new Hostility());
            Assert.AreEqual(new DialogConversationEnd(DialogEndKind.HostileRefusal, null), refused.End, response);
            var calm = Pick(response, "", new Hostility { Answer = () => false });
            Assert.AreEqual(DialogEndKind.ReturnToScript, calm.End!.Kind, response);
        }
    }

    [TestMethod]
    public void A_barter_reply_is_checked_and_does_not_open_the_window_for_a_hostile_npc()
    {
        var barter = new DialogBarterTests.Barter();
        var refused = Pick("20", "", new Hostility(), barter: barter, text: "b:");
        Assert.AreEqual(DialogEndKind.HostileRefusal, refused.End!.Kind);
        Assert.AreEqual(0, barter.Opened.Count);
        var calm = Pick("20", "", new Hostility { Answer = () => false }, barter: barter, text: "b:");
        Assert.AreEqual(DialogEndKind.BarterWindow, calm.End!.Kind);
        Assert.AreEqual(1, barter.Opened.Count);
    }

    [TestMethod]
    public void The_check_precedes_the_insult()
    {
        // The insult would change the reaction by -10; a hostile NPC ends the talk first, so the reaction stays.
        var world = new World { Reaction = DialogReactionReadResult.Of(40) };
        var refused = Pick("20", "", new Hostility(), world: world, text: "i:");
        Assert.AreEqual(DialogEndKind.HostileRefusal, refused.End!.Kind);
        Assert.AreEqual(40, world.Reaction.Value);
        var calmWorld = new World { Reaction = DialogReactionReadResult.Of(40) };
        Pick("20", "", new Hostility { Answer = () => false }, world: calmWorld, text: "i:");
        Assert.AreEqual(30, calmWorld.Reaction.Value);
    }

    [TestMethod]
    public void A_string_that_moves_the_dialog_itself_returns_0_and_skips_the_check()
    {
        // FL and SU return 0 from the action interpreter (0x4164B9 and the other state stores), so the reply step returns before the check.
        var finalLine = new Hostility();
        var spoken = Pick("20", "fl 40", finalLine);
        Assert.AreEqual(0, finalLine.Asked.Count);
        Assert.AreEqual(DialogEndKind.NpcFinalLine, spoken.End!.Kind);
        Assert.AreEqual("Farewell.", spoken.NpcLine!.Text);

        var window = new Hostility();
        var schematics = new DialogSchematicsTests.Schematics();
        var opened = Pick("20", "su", window, schematics: schematics);
        Assert.AreEqual(0, window.Asked.Count);
        Assert.AreEqual(DialogEndKind.SchematicWindow, opened.End!.Kind);
        Assert.AreEqual(1, schematics.Opened.Count);
    }

    [TestMethod]
    public void The_reply_that_offers_no_choice_is_checked_too()
    {
        // No reply qualifies: the one fallback exit option is picked like a reply with an empty action string (GP5); the check still runs.
        var document = Read(Record("10", "?"));
        var hostility = new Hostility();
        var conversation = Start(document, new Ports(), Owner(), world: new World(), hostile: hostility);
        Assert.AreEqual(DialogOptionTextKind.Fallback, conversation.Options.Single().TextKind);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(1, hostility.Asked.Count);
        Assert.AreEqual(DialogEndKind.HostileRefusal, conversation.End!.Kind);
    }

    [TestMethod]
    public void A_throwing_port_fails_the_conversation_and_without_the_port_nothing_is_checked()
    {
        var failed = Pick("20", "", new Throwing());
        Assert.AreEqual(DialogConversationFailureKind.QueryException, failed.Failure!.Kind);
        Assert.IsNull(failed.End);
        var unchecked_ = Pick("20", "", null);
        Assert.AreEqual("Bye.", unchecked_.NpcLine!.Text, "without the port the reply goes on as before");
    }
}
