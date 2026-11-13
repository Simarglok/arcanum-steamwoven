using Arcanum.Data;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1534 (ledger DSV1–DSV3): the <c>u:</c> skill service reply as the executable's builder (0x416C10 → 0x4AE570) treats it: skill 12 (repair) is never offered (the target the dialog
/// passes is the PC, not an item), takes a slot of the five eligible replies, and the other skills stay a frontier.
/// </summary>
[TestClass]
public sealed class DialogServiceReplyTests
{
    private static DialogConversation Start(string replies) =>
        DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + replies + "{20}{Next node.}{}{}{}{}{}"),
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(new Ports(), new Ports(), Owner(), new World()));

    private static string Reply(int number, string text, string response = "20") => $"{{{number}}}{{{text}}}{{}}{{1}}{{}}{{{response}}}{{}}";

    [TestMethod]
    public void A_repair_service_reply_is_not_offered_and_the_other_replies_are()
    {
        foreach (var text in new[] { "u:12", "U:12", "u: 12", "u:+12", "u:012", "u:12 repair", "U:12x", "u:4294967308" })
        {
            var conversation = Start(Reply(11, text) + Reply(12, "Plain."));
            Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase, text);
            CollectionAssert.AreEqual(new[] { "Plain." }, conversation.Options.Select(option => option.Text).ToArray(), text);
        }
    }

    [TestMethod]
    public void The_left_out_reply_takes_a_slot_of_the_five_eligible_ones()
    {
        var replies = Reply(11, "u:12") + string.Concat(Enumerable.Range(1, 5).Select(index => Reply(11 + index, $"Reply {index}.")));
        var conversation = Start(replies);
        CollectionAssert.AreEqual(new[] { "Reply 1.", "Reply 2.", "Reply 3.", "Reply 4." }, conversation.Options.Select(option => option.Text).ToArray(),
            "the builder counts the first five eligible records before it drops one");
    }

    [TestMethod]
    public void The_other_skill_numbers_and_unreadable_texts_stay_a_frontier()
    {
        foreach (var text in new[] { "u:10", "u:14", "u:5", "u:", "u:x", "u:112", "u:-12", "u:1", "u:4294967296" })
        {
            var conversation = Start(Reply(11, text));
            Assert.AreEqual((DialogConversationPhase.AtFrontier, DialogFrontierKind.UnsupportedGeneratedText), (conversation.Phase, conversation.Frontier!.Kind), text);
        }
    }
}
