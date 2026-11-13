using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The vitals commands of set S41 (ARC-SCRIPT-1481, ledger VT1–VT4): get hit points (107), get fatigue points (108) and heal poison (128), on synthetic programs of the food use script 30282's shapes.</summary>
[TestClass]
public sealed class ScrVitalTests
{
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    private const int Script = 30282;
    private static readonly ScrObjectRef Eater = Pc, Item = new(1, "item.1");

    private sealed class VitalHost(FakeHost inner) : IScrHost, IScrVitalQueries
    {
        public Dictionary<ScrObjectRef, (int Current, int Maximum)> Hit { get; } = new();
        public Dictionary<ScrObjectRef, (int Current, int Maximum)> Fatigue { get; } = new();
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
        public (int Current, int Maximum)? HitPoints(ScrObjectRef obj) => Hit.TryGetValue(obj, out var v) ? v : null;
        public (int Current, int Maximum)? FatiguePoints(ScrObjectRef obj) => Fatigue.TryGetValue(obj, out var v) ? v : null;
    }

    // The vitals commands over the smallest set that runs the synthetic programs (S41 itself needs every query interface at once).
    private static readonly ScrAdmissionSet Set = new("test-vitals", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.GetHitPoints, ScrCatalogue.Action.GetFatiguePoints, ScrCatalogue.Action.HealPoison]), ScrAdmissionSet.S2.ValueKinds,
        [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject]);

    private static VitalHost Host(ScrProgram program)
    {
        var inner = new FakeHost();
        inner.Attach(Item, ScrPoint.Use, Script, program);
        return new VitalHost(inner);
    }

    private static ScrRunResult Use(VitalHost host) => ScrDispatcher.RunObject(new(ScrPoint.Use, Item, Eater, Eater), host, new() { Admission = Set });

    [TestMethod]
    public void S41_adds_only_the_three_vitals_commands_over_S38()
    {
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.GetHitPoints, ScrCatalogue.Action.GetFatiguePoints, ScrCatalogue.Action.HealPoison },
            ScrAdmissionSet.S41.Actions.Except(ScrAdmissionSet.S38.Actions).ToArray());
        Assert.IsTrue(ScrAdmissionSet.S41.Conditions.SetEquals(ScrAdmissionSet.S38.Conditions) && ScrAdmissionSet.S38.IsSubsetOf(ScrAdmissionSet.S41));
        Assert.AreEqual((107, 108, 128), (ScrCatalogue.Action.GetHitPoints, ScrCatalogue.Action.GetFatiguePoints, ScrCatalogue.Action.HealPoison));
    }

    [TestMethod]
    public void Get_hit_points_and_fatigue_points_store_current_then_maximum_into_operands_1_and_2()
    {
        // VT1, VT2: operand 0 the object (here the extra object, as 30282 reads its target), then the two stores.
        ScrOutcome Outcome(int action, int slot, int expected)
        {
            var program = Program(
                Do(C(action, (Extra, 0), (Local, 4), (Local, 5))),
                L(C(ScrCatalogue.Condition.Equal, (Local, slot), N(expected)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
            var host = Host(program);
            host.Hit[Eater] = (7, 20);
            host.Fatigue[Eater] = (3, 11);
            return Use(host).Outcome;
        }
        Assert.AreEqual(ScrOutcome.SkipDefault, Outcome(ScrCatalogue.Action.GetHitPoints, 4, 7), "current hit points into operand 1");
        Assert.AreEqual(ScrOutcome.SkipDefault, Outcome(ScrCatalogue.Action.GetHitPoints, 5, 20), "maximum hit points into operand 2");
        Assert.AreEqual(ScrOutcome.SkipDefault, Outcome(ScrCatalogue.Action.GetFatiguePoints, 4, 3), "current fatigue into operand 1");
        Assert.AreEqual(ScrOutcome.SkipDefault, Outcome(ScrCatalogue.Action.GetFatiguePoints, 5, 11), "maximum fatigue into operand 2");
        Assert.AreEqual(ScrOutcome.RunDefault, Outcome(ScrCatalogue.Action.GetHitPoints, 4, 20), "the two are not swapped");
    }

    [TestMethod]
    public void A_question_the_host_cannot_answer_ends_the_dispatch_not_modelled_and_an_absent_object_stops_the_run()
    {
        var program = Program(Do(C(ScrCatalogue.Action.GetHitPoints, (Extra, 0), (Local, 4), (Local, 5))));
        var unknown = Host(program);
        Assert.AreEqual(ScrStop.NotModelled, Use(unknown).Stop, "an object the host does not know");
        var absent = Host(program);
        Assert.AreEqual(ScrStop.MissingObject, ScrDispatcher.RunObject(new(ScrPoint.Use, Item, Eater, null), absent, new() { Admission = Set }).Stop, "no extra object");
    }

    [TestMethod]
    public void A_literal_store_is_refused_and_a_host_without_the_vitals_interface_cannot_run_the_set()
    {
        foreach (var action in new[] { ScrCatalogue.Action.GetHitPoints, ScrCatalogue.Action.GetFatiguePoints })
            foreach (var slot in new[] { 1, 2 })
            {
                var refused = Program(Do(C(action, (Extra, 0), slot == 1 ? N(3) : (Local, 4), slot == 2 ? N(3) : (Local, 5))));
                var refusal = ScrAdmission.Admit(refused, Set).Refusal!;
                Assert.AreEqual((ScrRefusalCode.WriteToNumber, slot), (refusal.Code, refusal.Slot), $"action {action} literal in operand {slot}");
            }
        var program = Program(Do(C(ScrCatalogue.Action.GetHitPoints, (Extra, 0), (Local, 4), (Local, 5))));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S38).Refusal!.Code);
        var plain = new FakeHost();
        plain.Attach(Item, ScrPoint.Use, Script, program);
        Assert.Throws<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Use, Item, Eater, Eater), plain, new() { Admission = Set }));
    }

    [TestMethod]
    public void Heal_poison_asks_the_host_for_each_object_of_operand_0_with_the_amount_of_operand_1()
    {
        // VT3: 30282's last action, heal poison of the extra object by the adjusted amount in a local variable.
        var program = Program(
            Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(12))),
            Do(C(ScrCatalogue.Action.HealPoison, (Extra, -1), (Local, 0))),
            Do(C(ScrCatalogue.Action.HealPoison, (Triggerer, 0), N(5))));
        var run = Use(Host(program));
        Assert.AreEqual(ScrStop.FellOffEnd, run.Stop);
        CollectionAssert.AreEqual(new[] { (ScrObjectStateChange.HealPoison, Eater, 12), (ScrObjectStateChange.HealPoison, Eater, 5) },
            run.Effects.ObjectStates.Select(r => (r.Change, r.Object, r.Value)).ToArray());
    }
}
