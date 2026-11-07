using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The XP action through the experience port (ARC-DEV-178, ledger DAC9, XP11). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogExperienceTests
{
    internal sealed class Experience : IDialogExperiencePort
    {
        public List<(long Pc, int Level)> Given { get; } = [];
        public DialogCommandResult Result { get; set; } = DialogCommandResult.Applied;

        public DialogCommandResult Execute(QuestExperienceCommand command)
        {
            Given.Add((command.PlayerCharacter.Value, command.Level));
            return Result;
        }
    }

    [TestMethod]
    public void Xp_gives_the_quest_level_experience_from_an_npc_line_and_a_reply()
    {
        var document = Read(Record("10", "?", action: "xp 3") + Record("11", "Thanks.", iq: "1", response: "20", action: "XP12, MM 1")
            + Record("20", "Go well."));
        var experience = new Experience();
        var world = new World();
        var conversation = Start(document, new Ports(), Owner(), world: world, experience: experience);
        Assert.IsNull(conversation.Failure);
        CollectionAssert.AreEqual(new[] { (800L, 3) }, experience.Given, "the NPC line's action");
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[0].Token).Status);
        CollectionAssert.AreEqual(new[] { (800L, 3), (800L, 12) }, experience.Given);
        CollectionAssert.AreEqual(new[] { "MM:1" }, world.Commands);
        var receipts = conversation.Effects.Where(e => e.Command is QuestExperienceCommand).ToArray();
        Assert.AreEqual(2, receipts.Length);
        Assert.IsTrue(receipts.All(e => e.Outcome == DialogCommandAttemptOutcome.Applied));

        var rejected = Start(document, new Ports(), Owner(), world: new World(), experience: new Experience { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        Assert.AreEqual(DialogCommandAttemptOutcome.Rejected, rejected.Effects.Single(e => e.Command is QuestExperienceCommand).Outcome);
    }

    [TestMethod]
    public void Xp_reads_any_operand_as_the_source_does()
    {
        var experience = new Experience();
        var conversation = Start(Read(Record("10", "?", action: "xp-1") + Record("11", "Yes.", iq: "1", response: "0", action: "xp 0")), new Ports(), Owner(),
            experience: experience);
        Assert.IsNull(conversation.Failure);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, conversation.Select(conversation.Options[0].Token).Status);
        CollectionAssert.AreEqual(new[] { (800L, -1), (800L, 0) }, experience.Given);
    }

    [TestMethod]
    public void Xp_needs_the_experience_port_and_is_not_a_condition()
    {
        var plain = Start(Read(Record("10", "?", action: "xp 3")), new Ports(), Owner());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, plain.Frontier!.Kind, "unadmitted without an experience port");
        var condition = Read(Record("10", "?") + Record("11", "Yes.", iq: "1", condition: "xp 3", response: "0"));
        var gate = Start(condition, new Ports(), Owner(), experience: new Experience());
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, gate.Frontier!.Kind, "not a condition");
        var overflow = Start(Read(Record("10", "?", action: "xp 99999999999")), new Ports(), Owner(), experience: new Experience());
        Assert.IsTrue(overflow.Frontier is not null || overflow.Failure is not null, "an operand outside 32 bits is outside the subset");
    }
}
