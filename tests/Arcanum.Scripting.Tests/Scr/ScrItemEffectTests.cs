using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The item-effect commands of set S14 (ARC-SCRIPT-042, ledger WE1–WE6): add and remove effect, the magick-tech adjustment,
/// play script eye candy, call-ex and the identified and rewielding conditions. Programs, objects and numbers are invented.</summary>
[TestClass]
public sealed class ScrItemEffectTests
{
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    private static readonly ScrObjectRef Ring = new(1, "kit:3");
    private static readonly ScrRunOptions S14 = new() { Admission = ScrAdmissionSet.S14 };

    /// <summary>A recording host that also answers the item questions (and the object and skill ones S14 holds through S13).</summary>
    private sealed class ItemHost(FakeHost inner) : IScrHost, IScrObjectQueries, IScrSkillQueries, IScrItemQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<ScrObjectRef, bool> Identified { get; } = new();
        public Dictionary<(ScrObjectRef, ScrObjectRef), int> Ratios { get; } = new();
        public bool Rewielding { get; set; }
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => Inner.Script(number);
        public bool GlobalFlag(int index) => Inner.GlobalFlag(index);
        public int StoryState() => Inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => Inner.IsMonsterGenerator(attachee);
        public ScrDialogState DialogState(ScrObjectRef attachee) => Inner.DialogState(attachee);
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => Inner.CheckTrapBeforeUse(triggerer, attachee);
        public bool IsTrapObject(ScrObjectRef attachee) => Inner.IsTrapObject(attachee);
        public ScrObjectRef? Player() => Inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => Inner.Leader(critter);
        public bool IsJilted(ScrObjectRef critter) => Inner.IsJilted(critter);
        public bool IsPc(ScrObjectRef critter) => Inner.IsPc(critter);
        public int QuestState(ScrObjectRef obj, int quest) => 0;
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => null;
        public int SkillLevel(ScrObjectRef obj, int skill) => 0;
        public bool? IsIdentified(ScrObjectRef obj) => Identified.TryGetValue(obj, out var identified) ? identified : null;
        public int? PowerRatio(ScrObjectRef item, ScrObjectRef owner) => Ratios.TryGetValue((item, owner), out var ratio) ? ratio : null;
        public bool ItemsBeingRewielded() => Rewielding;
    }

    private static ItemHost With(ScrProgram program, int point = 24, uint counters = 0)
    {
        var host = new FakeHost();
        host.Attach(Ring, point, 700, program, counters: counters);
        return new ItemHost(host) { Ratios = { [(Ring, Pc)] = 100 } };
    }

    private static ScrRunResult Wield(IScrHost host, int point = 24, int line = 0) => ScrDispatcher.RunObject(new(point, Ring, Pc, null, line), host, S14);

    [TestMethod]
    public void Effect_actions_ask_for_each_object_with_the_numbers_as_read()
    {
        // Add effect 50 with cause 5 to the wielder, remove 51 from it, an absent extra object names none.
        var host = With(Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(50), N(5))), Do(C(ScrCatalogue.Action.RemoveEffect, (Triggerer, 0), N(51))),
            Do(C(ScrCatalogue.Action.AddEffect, (Extra, 0), N(52), N(5))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var run = Wield(host);
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrEffectRequest(ScrEffectChange.Add, Pc, 50, 5), new ScrEffectRequest(ScrEffectChange.Remove, Pc, 51) },
            run.Effects.EffectChanges.ToArray());
    }

    [TestMethod]
    public void The_magick_tech_adjustment_scales_by_the_power_ratio_with_the_original_s_arithmetic()
    {
        // (ratio × value + 50) / 100 into local 0, then into counter 1 (a byte); a literal store is refused.
        (int Ratio, int Value, int Expected)[] cases = [(50, 3, 2), (100, 20, 20), (1, -150, -1), (1, -149, 0), (100, int.MaxValue, unchecked(100 * int.MaxValue + 50) / 100)];
        foreach (var (ratio, value, expected) in cases)
        {
            var host = With(Program(Do(C(ScrCatalogue.Action.MagickTechAdjustment, N(value), (Attachee, 0), (Triggerer, 0), (Local, 0))),
                L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(expected)), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
            host.Ratios[(Ring, Pc)] = ratio;
            Assert.AreEqual(7, Wield(host).Effects.GlobalFlags.Single().Index, $"ratio {ratio}, value {value}");
        }
        var counter = With(Program(Do(C(ScrCatalogue.Action.MagickTechAdjustment, N(300), (Attachee, 0), (Triggerer, 0), (Counter, 1))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(300u & 0xFF, Wield(counter).Effects.Writeback!.Slot.Counters >> 8, "the counter keeps the low byte");
        var literal = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.MagickTechAdjustment, N(1), (Attachee, 0), (Triggerer, 0), N(0)))), ScrAdmissionSet.S14);
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 3), (literal.Refusal!.Code, literal.Refusal.Slot));
        // An item or owner the host does not know stops the run.
        var unknown = With(Program(Do(C(ScrCatalogue.Action.MagickTechAdjustment, N(1), (Attachee, 0), (Extra, 0), (Local, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual((ScrStop.MissingObject, 0), (Wield(unknown).Stop, Wield(unknown).StopLine));
    }

    [TestMethod]
    public void Identified_rewielding_and_eye_candy()
    {
        // If identified(attachee) show eye candy 30 on the wielder; if rewielding set flag 8; identified(extra) with no extra fails.
        var program = Program(L(C(ScrCatalogue.Condition.Identified, (Attachee, 0)), C(ScrCatalogue.Action.PlayScriptEyeCandy, N(30), (Triggerer, 0)), C(0)),
            L(C(ScrCatalogue.Condition.ItemsBeingRewielded), C(ScrCatalogue.Action.SetGlobalFlag, N(8)), C(0)),
            L(C(ScrCatalogue.Condition.Identified, (Extra, 0)), C(ScrCatalogue.Action.SetGlobalFlag, N(9)), C(0)),
            Do(C(ScrCatalogue.Action.PlayScriptEyeCandy, N(31), (Extra, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = With(program);
        host.Identified[Ring] = true;
        var run = Wield(host);
        CollectionAssert.AreEqual(new[] { new ScrEyeCandy(30, Pc) }, run.Effects.EyeCandies.ToArray());
        Assert.AreEqual(0, run.Effects.GlobalFlags.Count);
        host.Identified[Ring] = false;
        host.Rewielding = true;
        run = Wield(host);
        Assert.AreEqual((0, 8), (run.Effects.EyeCandies.Count, run.Effects.GlobalFlags.Single().Index));
        host.Identified.Clear();
        Assert.AreEqual(ScrStop.MissingObject, Wield(host).Stop, "an object the host does not know");
    }

    [TestMethod]
    public void Call_ex_runs_the_object_s_attachment_from_the_line_and_reports_its_slot()
    {
        // Wield off (25): call-ex the ring's wield-on slot (24) from line 2 with the wielder; the callee sees flag 3 the caller set and
        // removes the effect counter 0 of its own slot names, and sets its local flag 1; the caller sets flag 4 afterwards.
        var host = With(Program(Do(C(ScrCatalogue.Action.ReturnRunDefault)), Do(C(ScrCatalogue.Action.ReturnRunDefault)),
            L(C(ScrCatalogue.Condition.GlobalFlag, N(3)), C(ScrCatalogue.Action.RemoveEffect, (Triggerer, 0), (Counter, 0)), C(0)),
            Do(C(ScrCatalogue.Action.SetLocalFlag, N(1))), Do(C(ScrCatalogue.Action.ReturnRunDefault))), counters: 55);
        host.Inner.Attach(Ring, 25, 701, Program(Do(C(ScrCatalogue.Action.SetGlobalFlag, N(3))),
            Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(24), N(2), (Triggerer, 0))), Do(C(ScrCatalogue.Action.SetGlobalFlag, N(4))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var run = Wield(host, 25);
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome, "the caller's own return");
        CollectionAssert.AreEqual(new[] { new ScrEffectRequest(ScrEffectChange.Remove, Pc, 55) }, run.Effects.EffectChanges.ToArray());
        CollectionAssert.AreEqual(new[] { 3, 4 }, run.Effects.GlobalFlags.Select(f => f.Index).ToArray());
        Assert.AreEqual(new ScrWriteback(ScrSource.Object, Ring, 24, new ScrSlot(700, 1u << 1, 55)), run.Effects.SlotWrites.Single());
        Assert.IsNull(run.Effects.Writeback, "the caller's own slot did not change");

        // A use point refuses the dispatch; a point outside the script array ends it; no slot or script 0 runs nothing (ARC-SCRIPT-047: the
        // dialog point too, which the ring has no slot of).
        foreach (var (point, outcome, stop) in new[] { (1, ScrOutcome.Refused, ScrStop.Refused), (9, ScrOutcome.RunDefault, ScrStop.ReturnRunDefault),
            (31, ScrOutcome.SkipDefault, ScrStop.CallExPoint), (36, ScrOutcome.SkipDefault, ScrStop.CallExPoint), (5, ScrOutcome.RunDefault, ScrStop.ReturnRunDefault) })
        {
            host.Inner.Attach(Ring, 25, 701, Program(Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(point), N(0), (Triggerer, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
            var other = Wield(host, 25);
            Assert.AreEqual((outcome, stop), (other.Outcome, other.Stop), $"point {point}");
            Assert.IsTrue(other.Effects.IsEmpty);
        }
        // An absent object stops the caller's run.
        host.Inner.Attach(Ring, 25, 701, Program(Do(C(ScrCatalogue.Action.CallScriptEx, (Extra, 0), N(24), N(0), (Triggerer, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(ScrStop.MissingObject, Wield(host, 25).Stop);
    }

    [TestMethod]
    public void A_later_call_ex_reads_the_slot_an_earlier_one_left_and_the_event_s_own_slot_wins()
    {
        // Slot 24 increments counter 0; the wield-on event (24) calls itself twice from line 1, then adds 1 to its own copy's counter 0.
        var host = With(Program(Do(C(ScrCatalogue.Action.Goto, N(3))), Do(C(ScrCatalogue.Action.Add, (Counter, 0), (Counter, 0), N(1))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)),
            Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(24), N(1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(24), N(1), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.Add, (Counter, 0), (Counter, 0), N(10))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var run = Wield(host);
        CollectionAssert.AreEqual(new uint[] { 1, 2 }, run.Effects.SlotWrites.Select(w => w.Slot.Counters).ToArray());
        Assert.AreEqual(10u, run.Effects.Writeback!.Slot.Counters, "the event's copy started at 0 and is stored last");
    }

    [TestMethod]
    public void Call_ex_shares_the_call_depth_bound()
    {
        var host = With(Program(Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(24), N(0), (Triggerer, 0)))));
        var run = ScrDispatcher.RunObject(new(24, Ring, Pc, null), host, S14 with { MaxCallDepth = 3 });
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.CallDepth), (run.Outcome, run.Stop));
        Assert.IsTrue(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void A_set_with_item_questions_needs_item_queries()
    {
        var plain = new FakeHost();
        plain.Attach(Ring, 24, 700, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var set = new ScrAdmissionSet("ratio", ScrAdmissionSet.S5.Conditions, ScrAdmissionSet.S5.Actions.Append(ScrCatalogue.Action.MagickTechAdjustment),
            ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(24, Ring, Pc, null), plain, new() { Admission = set }));
        Assert.IsTrue(ScrAdmissionSet.S13.IsSubsetOf(ScrAdmissionSet.S14));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted,
            ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(1), N(5)))), ScrAdmissionSet.S13).Refusal!.Code);
    }
}
