using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1764 (ledger NP9, NP10): queue newspaper (85, two number operands) and float newspaper headline (86, object in operand slot 1: census anomaly X9) hand ordered
/// <see cref="ScrNewspaperRequest"/> effects to the host and the run goes on. Programs and numbers are invented.</summary>
[TestClass]
public sealed class ScrNewspaperTests
{
    private const byte Triggerer = 0, Attachee = 1, Everyone = 8, LocalObject = 11, ExtraObject = 12;
    private const int Reader = 4401;

    // S1 plus the two actions and the single-object focus kinds: every condition of S1 is answered by the fake host.
    private static readonly ScrAdmissionSet News = new("news", ScrAdmissionSet.S1.Conditions, ScrAdmissionSet.S1.Actions.Concat([ScrCatalogue.Action.QueueNewspaper, ScrCatalogue.Action.FloatNewspaperHeadline]),
        ScrAdmissionSet.S1.ValueKinds, [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject, ScrFocusKind.LocalObject]);

    private static ScrRunResult Run(ScrProgram program, ScrAdmissionSet? set = null)
    {
        var host = new FakeHost();
        host.Attach(Npc, ScrPoint.Dialog, Reader, program);
        return ScrDispatcher.RunObject(new(ScrPoint.Dialog, Npc, Pc, null), host, new() { Admission = set ?? News });
    }

    private static Clause Queue((byte, int) issue, (byte, int) front) => C(ScrCatalogue.Action.QueueNewspaper, issue, front);

    [TestMethod]
    public void Queue_newspaper_hands_the_issue_and_the_front_flag_in_action_order()
    {
        var program = Program(Do(Queue(N(5020), N(0))), Do(Queue(N(7), N(1))), Do(Queue(N(7), N(-3))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var run = Run(program);
        Assert.AreEqual(ScrStop.ReturnRunDefault, run.Stop);
        CollectionAssert.AreEqual(new[]
        {
            new ScrNewspaperRequest(ScrNewspaperKind.Queue, 5020, false, null),
            new ScrNewspaperRequest(ScrNewspaperKind.Queue, 7, true, null),
            new ScrNewspaperRequest(ScrNewspaperKind.Queue, 7, true, null),
        }, run.Effects.Newspapers.ToArray());
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void Queue_newspaper_reads_its_operands_as_values_with_a_missing_second_one_zero()
    {
        // Local variable 0 starts at 0 in every run, local variable 1 is set to 12 first: the issue and the flag come from the stores as read (0x44BA70).
        var program = Program(Do(C(ScrCatalogue.Action.Assign, (Local, 1), N(12))), Do(Queue((Local, 1), (Local, 0))), Do(Queue((Local, 0), (Local, 1))));
        var run = Run(program);
        CollectionAssert.AreEqual(new[]
        {
            new ScrNewspaperRequest(ScrNewspaperKind.Queue, 12, false, null),
            new ScrNewspaperRequest(ScrNewspaperKind.Queue, 0, true, null),
        }, run.Effects.Newspapers.ToArray(), "ARC-UI-2055 (TB26): the headline's listener is the run's triggerer, whatever the speaker");
    }

    [TestMethod]
    public void Float_newspaper_headline_names_the_object_of_operand_slot_one_and_ignores_slot_zero()
    {
        // The installed clause's shape: a number in slot 0 (any), the triggerer in slot 1. The attachee and the triggerer differ here, so the slot read shows.
        var program = Program(Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, N(1), (Triggerer, 0))), Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, N(99), (Attachee, 0))));
        var run = Run(program);
        CollectionAssert.AreEqual(new[]
        {
            new ScrNewspaperRequest(ScrNewspaperKind.FloatHeadline, 0, false, Pc, Pc),
            new ScrNewspaperRequest(ScrNewspaperKind.FloatHeadline, 0, false, Npc, Pc),
        }, run.Effects.Newspapers.ToArray());
    }

    [TestMethod]
    public void Float_newspaper_headline_without_an_object_floats_nothing_and_the_run_goes_on()
    {
        // The extra object of this event is absent, and the local object variable was never assigned: no object, no request.
        var program = Program(Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, N(0), (ExtraObject, 0))), Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, N(0), (LocalObject, 0))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        var run = Run(program);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        Assert.AreEqual(0, run.Effects.Newspapers.Count);
        Assert.IsTrue(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void Requests_of_both_kinds_keep_the_action_order()
    {
        var program = Program(Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, N(0), (Attachee, 0))), Do(Queue(N(9), N(1))), Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, N(0), (Triggerer, 0))));
        var run = Run(program);
        CollectionAssert.AreEqual(new[] { ScrNewspaperKind.FloatHeadline, ScrNewspaperKind.Queue, ScrNewspaperKind.FloatHeadline }, run.Effects.Newspapers.Select(request => request.Kind).ToArray());
    }

    [TestMethod]
    public void The_admission_checks_the_object_in_slot_one_and_leaves_slot_zero_unread()
    {
        // Slot 0 of any kind (even one no set knows) is not looked at; slot 1 must be an object kind the set admits.
        var anyKindInSlotZero = Program(Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, (6, 77), (Triggerer, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.IsTrue(ScrAdmission.Admit(anyKindInSlotZero, News).IsAdmitted);
        var numberInSlotOne = Program(Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, N(0), N(5))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var refused = ScrAdmission.Admit(numberInSlotOne, News).Refusal!;
        Assert.AreEqual(ScrRefusalCode.FocusKindNotAdmitted, refused.Code, "a number kind in the object slot is not a focus kind the set admits");
        Assert.AreEqual(1, refused.Slot);
        var vicinity = Program(Do(C(ScrCatalogue.Action.FloatNewspaperHeadline, N(0), (Everyone, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.AreEqual(ScrRefusalCode.FocusKindNotAdmitted, ScrAdmission.Admit(vicinity, News).Refusal!.Code, "a set without the vicinity focus kinds refuses them in slot 1");
        Assert.IsTrue(ScrAdmission.Admit(vicinity, new("news-vicinity", News.Conditions, News.Actions, News.ValueKinds, News.FocusKinds.Append(ScrFocusKind.EveryoneInVicinity))).IsAdmitted);
    }

    [TestMethod]
    public void The_queue_operands_are_numbers_and_an_unknown_value_kind_there_is_refused()
    {
        var program = Program(Do(C(ScrCatalogue.Action.QueueNewspaper, (200, 0), N(0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var refused = ScrAdmission.Admit(program, News).Refusal!;
        Assert.AreEqual((ScrRefusalCode.UnknownValueKind, 0), (refused.Code, refused.Slot));
    }

    [TestMethod]
    public void S59_is_S58_plus_the_two_newspaper_actions_and_the_interpreter_accepts_it()
    {
        Assert.IsTrue(ScrAdmissionSet.S58.IsSubsetOf(ScrAdmissionSet.S59));
        Assert.IsFalse(ScrAdmissionSet.S59.IsSubsetOf(ScrAdmissionSet.S58));
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.QueueNewspaper, ScrCatalogue.Action.FloatNewspaperHeadline }, ScrAdmissionSet.S59.Actions.Except(ScrAdmissionSet.S58.Actions).ToArray());
        Assert.AreEqual(ScrAdmissionSet.S58.Conditions.Count, ScrAdmissionSet.S59.Conditions.Count);
        Assert.IsNotNull(new ScrRunOptions { Admission = ScrAdmissionSet.S59 });
        var program = Program(Do(Queue(N(1), N(0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S58).Refusal!.Code);
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S59).IsAdmitted);
    }
}
