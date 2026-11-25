using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// ARC-SCRIPT-2965 (ledger FL4, FL6): the one NPC-line step a script's float line runs with no conversation open. Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogNpcLineTests
{
    private static DialogConversation Prepare(string record, Ports ports, int entry = 20, DialogPcGender gender = DialogPcGender.Male, DialogConversationPorts? wired = null) =>
        DialogConversation.PrepareNpcLine(Read(Record("10", "Hello.") + record),
            new DialogConversationRequest(new DialogNodeId(entry), new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(gender, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            wired ?? new DialogConversationPorts(ports, ports, Owner()));

    [TestMethod]
    public void A_line_with_an_action_string_runs_it_in_order_after_the_plan_and_gives_its_text()
    {
        var ports = new Ports();
        var line = Prepare(Record("20", "Said @npcname@ to @pcname@.", alternate: "Said by lady.", action: "lf 3 1, gf 3 1, lf 4 0"), ports);
        Assert.IsTrue(line.NpcLinePrepared, $"{line.Frontier} {line.Failure}");
        Assert.AreEqual("Said Bram to Ada.", line.NpcLine!.Text);
        Assert.AreEqual(0, ports.Commands.Count, "planning calls no port");
        Assert.IsTrue(line.RunNpcLine());
        CollectionAssert.AreEqual(new[] { "LF:3:1:900", "LF:4:0:900" }, ports.Commands);
        Assert.IsFalse(line.NpcLinePrepared, "it ran");
        CollectionAssert.AreEqual(new[] { "LF@1", "GF(3,True)@1", "LF@1" }, line.Effects.Select(Describe).ToArray());
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, line.Phase);
        Assert.ThrowsExactly<InvalidOperationException>(() => line.RunNpcLine());
    }

    [TestMethod]
    public void A_female_pc_gets_the_alternate_text_and_a_separators_only_action_runs_nothing()
    {
        var ports = new Ports();
        var line = Prepare(Record("20", "Plain.", alternate: "For a lady.", action: " "), ports, gender: DialogPcGender.Female);
        Assert.IsTrue(line.NpcLinePrepared);
        Assert.AreEqual("For a lady.", line.NpcLine!.Text);
        Assert.IsTrue(line.RunNpcLine());
        Assert.AreEqual(0, ports.Commands.Count);
    }

    [TestMethod]
    public void A_missing_record_or_an_operation_that_needs_a_conversation_is_refused_before_any_effect()
    {
        foreach (var action in new[] { "lf 3 1, re 5", "lf 3 1, in 801", "lf 3 1, $$ 5", "lf 3 1, co", "lf 3 1, jo 0 30", "lf 3 1, qu 1 2", "lf 3 1, mm 5", "lf 3 1, nk" })
        {
            var ports = new Ports();
            var line = Prepare(Record("20", "x", action: action), ports);
            Assert.IsFalse(line.NpcLinePrepared, action);
            Assert.IsTrue(line.Frontier is not null || line.Failure is not null, action);
            Assert.AreEqual(0, ports.Commands.Count, action);
            Assert.ThrowsExactly<InvalidOperationException>(() => line.RunNpcLine(), action);
        }
        var missing = Prepare(Record("20", "x"), new Ports(), entry: 99);
        Assert.AreEqual(DialogConversationFailureKind.InvalidEntry, missing.Failure!.Kind);
        Assert.IsFalse(missing.NpcLinePrepared);
    }

    [TestMethod]
    public void Generated_text_is_refused_and_a_port_that_rejects_stops_the_run_where_it_stands()
    {
        foreach (var text in new[] { "g:", "m:x", "r:x", "i:" })
            Assert.IsFalse(Prepare(Record("20", text, action: "lf 3 1"), new Ports()).NpcLinePrepared, text);
        var ports = new Ports { RejectAt = 2 };
        var line = Prepare(Record("20", "x", action: "lf 3 1, lf 4 1, lf 5 1"), ports);
        Assert.IsTrue(line.NpcLinePrepared);
        Assert.IsFalse(line.RunNpcLine());
        Assert.AreEqual(DialogConversationPhase.Failed, line.Phase);
        CollectionAssert.AreEqual(new[] { "LF:3:1:900" }, ports.Commands);
    }

    [TestMethod]
    public void The_line_is_planned_in_any_record_role_and_a_capability_without_its_port_is_a_frontier()
    {
        // A Player record (iq set) floats too; its own text is shown.
        var player = Prepare(Record("20", "Player text.", alternate: "Alt.", iq: "1", response: "0", action: "lf 3 1"), new Ports(), gender: DialogPcGender.Female);
        Assert.IsTrue(player.NpcLinePrepared);
        Assert.AreEqual("Player text.", player.NpcLine!.Text);
        // gf without the flag owner is not in the subset.
        var ports = new Ports();
        var noOwner = Prepare(Record("20", "x", action: "gf 3 1"), ports, wired: new DialogConversationPorts(ports, ports, null));
        Assert.IsFalse(noOwner.NpcLinePrepared);
        Assert.AreEqual(DialogFrontierKind.CapabilityUnavailable, noOwner.Frontier!.Kind);
    }
}
