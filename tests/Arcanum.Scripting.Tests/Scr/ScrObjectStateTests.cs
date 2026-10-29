using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Toggle state (56) and kill (58) of set S16 (ARC-WORLD-114, ledger SU6, PM1), on a synthetic replica of the shape of the
/// Crash Site pre-made heroes' heartbeat scripts (docs/research/character-creation-evidence.md, "Unchosen pre-made heroes").</summary>
[TestClass]
public sealed class ScrObjectStateTests
{
    private const byte Attachee = 1, Triggerer = 0;
    private const int Script = 1781, ChosenFlag = 2350;

    // S2 with single-object focus plus the two actions: the hosts' heartbeat set has this shape.
    private static readonly ScrAdmissionSet Set = new("test-heartbeat", ScrAdmissionSet.S3.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.ToggleState, ScrCatalogue.Action.Kill]), ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    // Chosen (flag set): switch the attachee off, remove the script, skip the default. Not chosen: kill the attachee, run the default.
    private static ScrProgram Heartbeat() => Program(
        L(C(ScrCatalogue.Condition.Equal, (GlobalFlag, ChosenFlag), N(1)), C(ScrCatalogue.Action.DoNothing), C(ScrCatalogue.Action.Goto, N(4))),
        Do(C(ScrCatalogue.Action.ToggleState, (Attachee, -1))),
        Do(C(ScrCatalogue.Action.RemoveThisScript)),
        Do(C(ScrCatalogue.Action.ReturnSkipDefault)),
        Do(C(ScrCatalogue.Action.Kill, (Attachee, -1))),
        Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private static FakeHost Host(bool chosen)
    {
        var host = new FakeHost();
        host.Attach(Npc, ScrPoint.Heartbeat, Script, Heartbeat());
        if (chosen) host.Flags.Add(ChosenFlag);
        return host;
    }

    private static ScrRunResult Beat(FakeHost host) => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), host, Options);

    [TestMethod]
    public void An_unchosen_hero_is_killed_and_the_default_runs()
    {
        var run = Beat(Host(chosen: false));
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.Kill, Npc) }, run.Effects.ObjectStates.ToArray());
        Assert.IsNull(run.Effects.Writeback, "the script stays attached");
    }

    [TestMethod]
    public void The_chosen_hero_is_switched_off_and_its_script_removed()
    {
        var run = Beat(Host(chosen: true));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.ToggleOff, Npc) }, run.Effects.ObjectStates.ToArray());
        Assert.AreEqual(0, run.Effects.Writeback!.Slot.Script);
    }

    [TestMethod]
    public void Each_object_of_the_operand_is_named_and_an_absent_object_names_none()
    {
        var host = new FakeHost();
        // The triggerer is absent: nothing; then the attachee twice, in action order.
        host.Attach(Npc, ScrPoint.Heartbeat, Script, Program(
            Do(C(ScrCatalogue.Action.Kill, (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.ToggleState, (Attachee, -1))),
            Do(C(ScrCatalogue.Action.Kill, (Attachee, -1))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var run = ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, null, null), host, Options);
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.ToggleOff, Npc), new ScrObjectStateRequest(ScrObjectStateChange.Kill, Npc) },
            run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void S16_admits_both_actions_and_S15_neither()
    {
        var program = Heartbeat();
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S16).IsAdmitted);
        Assert.IsFalse(ScrAdmission.Admit(program, ScrAdmissionSet.S15).IsAdmitted);
        Assert.IsTrue(ScrAdmissionSet.S15.IsSubsetOf(ScrAdmissionSet.S16));
        Assert.IsTrue(Set.IsSubsetOf(ScrAdmissionSet.S16));
    }
}
