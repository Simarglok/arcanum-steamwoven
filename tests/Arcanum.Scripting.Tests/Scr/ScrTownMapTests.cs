using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-DEV-205 (bug B22): unfog town map (113) of set S24 (ledger UF1). Programs and town-map numbers are invented.</summary>
[TestClass]
public sealed class ScrTownMapTests
{
    private const int Use = 1;
    // The commands these programs use (control flow, arithmetic, call script) with unfog town map; S24 itself also holds the commands that need
    // the world, so its host would have to answer more.
    private static readonly ScrAdmissionSet Set = new("town-map", ScrAdmissionSet.S1.Conditions, ScrAdmissionSet.S1.Actions.Append(ScrCatalogue.Action.UnfogTownMap),
        ScrAdmissionSet.S1.ValueKinds, ScrAdmissionSet.S1.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private static Clause Unfog((byte, int) townMap) => C(ScrCatalogue.Action.UnfogTownMap, townMap);

    private static ScrRunResult Run(ScrProgram program)
    {
        var host = new FakeHost();
        host.Attach(Npc, Use, 2023, program);
        return ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, Options);
    }

    [TestMethod]
    public void Unfog_town_map_asks_for_its_number_in_action_order_and_goes_on()
    {
        var run = Run(Program(Do(Unfog(N(7))), Do(C(ScrCatalogue.Action.Assign, (Counter, 0), N(3))), Do(Unfog((Counter, 0))), Do(Unfog(N(7))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, "the action goes on to the next line");
        CollectionAssert.AreEqual(new[] { 7, 3, 7 }, run.Effects.TownMaps.ToArray(), "operand 0 read as a number, each request in action order");
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void Town_map_zero_or_a_negative_number_asks_for_nothing()
    {
        // 0x4BEAB0 returns at once for town map 0; no town map has a negative number.
        var run = Run(Program(Do(Unfog(N(0))), Do(Unfog(N(-4))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(ScrStop.ReturnRunDefault, run.Stop);
        Assert.AreEqual(0, run.Effects.TownMaps.Count);
        Assert.IsTrue(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void Only_S24_and_the_sets_built_on_it_admit_unfog_town_map()
    {
        Assert.IsTrue(ScrAdmissionSet.S23.IsSubsetOf(ScrAdmissionSet.S24));
        Assert.IsFalse(ScrAdmissionSet.S24.IsSubsetOf(ScrAdmissionSet.S23));
        var program = Program(Do(Unfog(N(7))), Do(C(1)));
        var refused = ScrAdmission.Admit(program, ScrAdmissionSet.S23);
        Assert.AreEqual((ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.UnfogTownMap), (refused.Refusal!.Code, refused.Refusal.Type));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S24).IsAdmitted);
    }
}
