using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-DEV-233 (MIG13 S14b, GT162): heal fatigue (76) of set S26 hands one request per object of its operand, in list order, with the amount, and the run goes on.</summary>
[TestClass]
public sealed class ScrHealFatigueTests
{
    private const byte Triggerer = 0;
    private const int TileScript = 1724;
    private static readonly ScrAdmissionSet Set = new("test-heal-fatigue", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.HealFatigue, ScrCatalogue.Action.PerformAnimation, ScrCatalogue.Action.StealCoins]), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private static ScrRunResult Step(ScrProgram program)
    {
        var host = new FakeHost();
        host.Scripts[TileScript] = ScrScriptLookup.Of(program);
        return ScrDispatcher.RunTile(new(new ScrSlot(TileScript, 0, 0), Npc), host, Options);
    }

    [TestMethod]
    public void Heal_fatigue_asks_for_the_object_and_the_amount_and_the_run_goes_on()
    {
        var run = Step(Program(Do(C(ScrCatalogue.Action.HealFatigue, (Triggerer, 0), N(12))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "GT154: the action goes on to the next line");
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.HealFatigue, Npc, 12) }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void Perform_animation_asks_for_the_first_object_and_the_animation_as_read_and_the_run_goes_on()
    {
        var run = Step(Program(Do(C(ScrCatalogue.Action.PerformAnimation, (Triggerer, 0), N(10))), Do(C(ScrCatalogue.Action.PerformAnimation, (Triggerer, 0), N(99))),
            Do(C(ScrCatalogue.Action.PerformAnimation, (1, -1), N(3))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "GT154");
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.PerformAnimation, Npc, 10), new ScrObjectStateRequest(ScrObjectStateChange.PerformAnimation, Npc, 99) },
            run.Effects.ObjectStates.ToArray(), "the host refuses a number outside 0..25; an absent object asks for nothing");
    }

    [TestMethod]
    public void Steal_coins_asks_for_the_thief_and_the_victim_and_nothing_without_both_and_the_run_goes_on()
    {
        var run = Step(Program(Do(C(ScrCatalogue.Action.StealCoins, (Triggerer, 0), (Triggerer, 0))), Do(C(ScrCatalogue.Action.StealCoins, (Triggerer, 0), (1, -1))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "GT154");
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.StealCoins, Npc, Other: Npc) }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void S27_adds_the_steal_over_S26()
    {
        var program = Program(Do(C(ScrCatalogue.Action.StealCoins, (Triggerer, 0), (Triggerer, 0))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S27).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S26).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S26.IsSubsetOf(ScrAdmissionSet.S27));
    }

    [TestMethod]
    public void S26_adds_the_action_over_S25()
    {
        var program = Program(Do(C(ScrCatalogue.Action.HealFatigue, (Triggerer, 0), N(1))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S26).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S25).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S25.IsSubsetOf(ScrAdmissionSet.S26) && Set.IsSubsetOf(ScrAdmissionSet.S27));
    }
}
