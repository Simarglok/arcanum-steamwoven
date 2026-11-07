using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

// The actions np (queue a newspaper issue) and or (set the NPC's origin) through their ports (ARC-DEV-230, ledger NQ1–NQ6). Records and phrases are invented synthetic fixtures.
[TestClass]
public sealed class DialogNewspaperTests
{
    internal sealed class Newspaper : IDialogNewspaperPort
    {
        public List<QueueNewspaperCommand> Queued { get; } = [];
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public bool Throw { get; init; }
        public DialogCommandResult Execute(QueueNewspaperCommand command)
        {
            if (Throw) throw new InvalidOperationException("newspaper");
            Queued.Add(command);
            return Result;
        }
    }

    internal sealed class Origins : IDialogOriginPort
    {
        public List<SetOriginCommand> Set { get; } = [];
        public DialogCommandResult Result { get; init; } = DialogCommandResult.Applied;
        public bool Throw { get; init; }
        public DialogCommandResult Execute(SetOriginCommand command)
        {
            if (Throw) throw new InvalidOperationException("origin");
            Set.Add(command);
            return Result;
        }
    }

    // An NPC line with the action, followed by a quiet reply.
    private static DialogConversation Line(string action, IDialogNewspaperPort? newspaper = null, IDialogOriginPort? origins = null, Ports? ports = null) =>
        Start(Read(Record("10", "Hm.", action: action) + Record("11", "Ok.", iq: "1", response: "0")), ports ?? new Ports(), Owner(), newspaper: newspaper, origins: origins);

    [TestMethod]
    public void Np_queues_the_first_operand_with_the_front_flag_of_the_second_in_an_npc_line_and_a_reply()
    {
        var newspaper = new Newspaper();
        var line = Line("np 1002, NP 1003 1, np 1004 0, np 1005 -7, np 7 2", newspaper);
        Assert.IsNull(line.Failure);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, line.Phase);
        // A missing second operand reads 0; any other value (also a negative one) is the front flag.
        CollectionAssert.AreEqual(new[]
        {
            new QueueNewspaperCommand(1002, false), new QueueNewspaperCommand(1003, true), new QueueNewspaperCommand(1004, false),
            new QueueNewspaperCommand(1005, true), new QueueNewspaperCommand(7, true),
        }, newspaper.Queued.ToArray());
    }

    [TestMethod]
    public void Np_in_a_reply_runs_when_the_reply_is_picked_and_does_not_end_or_skip_the_hostile_check()
    {
        var newspaper = new Newspaper();
        var hostile = new DialogHostileTests.Hostility { Answer = () => false };
        var conversation = Start(Read(Record("10", "?") + Record("11", "Go.", iq: "1", response: "20", action: "np 9001 1") + Record("20", "Bye.")), new Ports(), Owner(), newspaper: newspaper, hostile: hostile);
        Assert.AreEqual(0, newspaper.Queued.Count, "nothing before the reply is picked");
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(new QueueNewspaperCommand(9001, true), newspaper.Queued.Single());
        Assert.AreEqual(1, hostile.Asked.Count, "the string returns normally");
        Assert.AreEqual("Bye.", conversation.NpcLine!.Text);
    }

    [TestMethod]
    public void Or_sets_the_origin_of_the_dialog_npc_with_the_value_as_read()
    {
        var origins = new Origins();
        var line = Line("or 3, OR -2, or 0", origins: origins);
        Assert.IsNull(line.Failure);
        Assert.AreEqual(new SetOriginCommand(new(900), new(800), 3), origins.Set[0]);
        CollectionAssert.AreEqual(new[] { 3, -2, 0 }, origins.Set.Select(c => c.Origin).ToArray());
        Assert.IsTrue(origins.Set.All(c => c.Npc == new DialogParticipantId(900) && c.PlayerCharacter == new DialogParticipantId(800)));
    }

    [TestMethod]
    public void The_two_actions_run_in_field_order_with_the_other_actions()
    {
        var newspaper = new Newspaper();
        var origins = new Origins();
        var ports = new Ports();
        Line("lf 1 1, np 20, or 4, lf 2 1", newspaper, origins, ports);
        CollectionAssert.AreEqual(new[] { "LF:1:1:900", "LF:2:1:900" }, ports.Commands);
        Assert.AreEqual(1, newspaper.Queued.Count);
        Assert.AreEqual(1, origins.Set.Count);
    }

    [TestMethod]
    public void Without_the_port_a_bad_operand_a_condition_or_a_stray_number_the_code_stays_outside_the_subset()
    {
        static DialogConversation Alone(string action, IDialogNewspaperPort? newspaper = null, IDialogOriginPort? origins = null) =>
            Start(Read(Record("10", "Hm.", action: action)), new Ports(), Owner(), newspaper: newspaper, origins: origins);
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Alone("np 5").Frontier!.Kind, "np without the port");
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Alone("or 5").Frontier!.Kind, "or without the port");
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Alone("or 5 6", origins: new Origins()).Frontier!.Kind, "or reads no second operand: the stray number is outside the subset");
        // An operand beyond 32 bits is damage, as for every other code; the lowest 32-bit value is an ordinary operand (nothing is negated).
        Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, Alone("np 99999999999", new Newspaper()).Failure?.Kind);
        // A negative issue is damage (the source's second-operand reader would take the sign as its own), as for sk and tr.
        Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, Alone("np -5", new Newspaper()).Failure?.Kind);
        // The source reads a missing operand as 0 (an installed dialog writes a bare np): the issue 0 and the value 0.
        var bare = new Newspaper();
        Alone("np", bare);
        Alone("lf 1 1, NP ", bare);
        CollectionAssert.AreEqual(new[] { new QueueNewspaperCommand(0, false), new QueueNewspaperCommand(0, false) }, bare.Queued.ToArray());
        // A separator right behind the code is outside the subset, as for every code with an operand.
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, Alone("np,", new Newspaper()).Frontier!.Kind);
        var origin = new Origins();
        Alone("or", origins: origin);
        Assert.AreEqual(0, origin.Set.Single().Origin);
        // Not conditions.
        foreach (var condition in new[] { "np 5", "or 5" })
        {
            var gate = Start(Read(Record("10", "?") + Record("11", "Go.", iq: "1", condition: condition, response: "0")), new Ports(), Owner(), newspaper: new Newspaper(), origins: new Origins());
            Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, gate.Frontier?.Kind, condition);
        }
        // A plus sign is damage, as for every other operand.
        var plus = Alone("np +5", new Newspaper());
        Assert.AreEqual(DialogConversationFailureKind.InvalidOperation, plus.Failure?.Kind);
    }

    [TestMethod]
    public void A_refusing_or_throwing_port_fails_the_conversation()
    {
        var rejected = Line("np 5", new Newspaper { Result = DialogCommandResult.Rejected });
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, rejected.Failure!.Kind);
        var thrown = Line("or 5", origins: new Origins { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrown.Failure!.Kind);
        var thrownQueue = Line("np 5", new Newspaper { Throw = true });
        Assert.AreEqual(DialogConversationFailureKind.CommandIndeterminate, thrownQueue.Failure!.Kind);
    }
}
