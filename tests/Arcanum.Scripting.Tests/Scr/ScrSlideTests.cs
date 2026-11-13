using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-RENDER-1171 (ledger SS2–SS4): queue slide (131, one number operand) and end game and play slides (132, no operand) hand ordered requests to the host and the run goes on;
/// the engine-run slide scripts use the narrow <see cref="ScrAdmissionSet.SlideScripts"/>. Programs and numbers are invented, in the shape of the credits script (a counter loop) and the ending scripts.</summary>
[TestClass]
public sealed class ScrSlideTests
{
    private const int Show = 999, Ending = 2921;

    // The slide set holds the PC quest state condition, which a host answers through IScrObjectQueries; every answer here is "nothing".
    private sealed class SlideHost(FakeHost inner) : IScrHost, IScrObjectQueries, IScrStoreQueries
    {
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => inner.Script(number);
        public bool GlobalFlag(int index) => inner.GlobalFlag(index);
        public int StoryState() => inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => inner.IsMonsterGenerator(attachee);
        public ScrDialogState DialogState(ScrObjectRef attachee) => inner.DialogState(attachee);
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => inner.CheckTrapBeforeUse(triggerer, attachee);
        public bool IsTrapObject(ScrObjectRef attachee) => inner.IsTrapObject(attachee);
        public ScrObjectRef? Player() => inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => inner.Leader(critter);
        public bool IsJilted(ScrObjectRef critter) => inner.IsJilted(critter);
        public bool IsPc(ScrObjectRef critter) => inner.IsPc(critter);
        public int GlobalVariable(int index) => 0;
        public int PcVariable(ScrObjectRef pc, int index) => 0;
        public bool PcFlag(ScrObjectRef pc, int index) => false;
        public int QuestState(ScrObjectRef obj, int quest) => 0;
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => null;
    }

    private static ScrRunResult Run(ScrAdmissionSet set, params (int Number, ScrProgram Program)[] scripts)
    {
        var host = new FakeHost();
        foreach (var (number, program) in scripts) host.Scripts[number] = ScrScriptLookup.Of(program);
        return ScrDispatcher.RunCall(new ScrCallEvent(scripts[0].Number, 0, null, null), new SlideHost(host), new() { Admission = set });
    }

    private static Clause Queue((byte, int) slide) => C(ScrCatalogue.Action.QueueSlide, slide);

    [TestMethod]
    public void Queue_slide_hands_each_slide_in_action_order_and_a_local_variable_loop_queues_a_range()
    {
        // The credits script's shape: a local variable starts at 45, each pass queues it and adds 1, the loop ends past 55.
        var program = Program(Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(45))), Do(Queue((Local, 0))), Do(C(ScrCatalogue.Action.Add, (Local, 0), (Local, 0), N(1))),
            L(C(ScrCatalogue.Condition.LessOrEqual, (Local, 0), N(55)), C(ScrCatalogue.Action.Goto, N(1)), C(ScrCatalogue.Action.ReturnRunDefault)));
        var run = Run(ScrAdmissionSet.SlideScripts, (Show, program));
        Assert.AreEqual(ScrStop.ReturnRunDefault, run.Stop);
        CollectionAssert.AreEqual(Enumerable.Range(45, 11).Select(slide => new ScrSlideRequest(ScrSlideKind.Queue, slide)).ToArray(), run.Effects.Slides.ToArray());
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void A_called_script_queues_into_the_same_run_after_the_caller_s_slides()
    {
        // 997 calls an ending script (call script, operands: script, line, triggerer, attachee), as the end-game script does.
        var caller = Program(Do(Queue(N(1))), Do(C(ScrCatalogue.Action.CallScript, N(Ending), N(0), (0, 0), (1, 0))), Do(Queue(N(4))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var callee = Program(Do(Queue(N(37))), Do(Queue(N(38))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var run = Run(ScrAdmissionSet.SlideScripts, (Show, caller), (Ending, callee));
        CollectionAssert.AreEqual(new[] { 1, 37, 38, 4 }, run.Effects.Slides.Select(request => request.Slide).ToArray());
    }

    [TestMethod]
    public void A_global_flag_chooses_the_slide_and_a_run_that_queues_nothing_has_no_effect()
    {
        var program = Program(L(C(ScrCatalogue.Condition.Equal, (GlobalFlag, 2632), N(1)), C(ScrCatalogue.Action.Goto, N(1)), C(ScrCatalogue.Action.Goto, N(3))),
            Do(Queue(N(2))), Do(C(ScrCatalogue.Action.ReturnRunDefault)), Do(Queue(N(0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = new FakeHost();
        host.Scripts[998] = ScrScriptLookup.Of(program);
        var off = ScrDispatcher.RunCall(new ScrCallEvent(998, 0, null, null), new SlideHost(host), new() { Admission = ScrAdmissionSet.SlideScripts });
        CollectionAssert.AreEqual(new[] { 0 }, off.Effects.Slides.Select(request => request.Slide).ToArray());
        host.Flags.Add(2632);
        var on = ScrDispatcher.RunCall(new ScrCallEvent(998, 0, null, null), new SlideHost(host), new() { Admission = ScrAdmissionSet.SlideScripts });
        CollectionAssert.AreEqual(new[] { 2 }, on.Effects.Slides.Select(request => request.Slide).ToArray());
        var empty = Run(ScrAdmissionSet.SlideScripts, (Show, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault)))));
        Assert.AreEqual(0, empty.Effects.Slides.Count);
        Assert.IsTrue(empty.Effects.IsEmpty);
    }

    [TestMethod]
    public void End_game_is_one_request_in_action_order_and_the_run_goes_on()
    {
        var set = new ScrAdmissionSet("end", ScrAdmissionSet.S3.Conditions, ScrAdmissionSet.S3.Actions.Concat([ScrCatalogue.Action.QueueSlide, ScrCatalogue.Action.EndGame]),
            ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
        var run = Run(set, (Show, Program(Do(C(ScrCatalogue.Action.EndGame)), Do(Queue(N(3))), Do(C(ScrCatalogue.Action.ReturnSkipDefault)))));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        CollectionAssert.AreEqual(new[] { new ScrSlideRequest(ScrSlideKind.EndGame, 0), new ScrSlideRequest(ScrSlideKind.Queue, 3) }, run.Effects.Slides.ToArray());
    }

    [TestMethod]
    public void The_slide_actions_are_outside_the_old_sets_and_the_slide_set_is_narrow()
    {
        var program = Program(Do(Queue(N(1))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S53).Refusal!.Code);
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S58).IsAdmitted);
        Assert.IsTrue(ScrAdmissionSet.SlideScripts.IsSubsetOf(ScrAdmissionSet.S58));
        Assert.IsFalse(ScrAdmissionSet.SlideScripts.Actions.Contains(ScrCatalogue.Action.Dialog), "no conversation, teleport or world change is run by a slide script");
        Assert.IsFalse(ScrAdmissionSet.SlideScripts.Actions.Contains(ScrCatalogue.Action.EndGame));
        var endGame = Program(Do(C(ScrCatalogue.Action.EndGame)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(endGame, ScrAdmissionSet.SlideScripts).Refusal!.Code);
    }
}
