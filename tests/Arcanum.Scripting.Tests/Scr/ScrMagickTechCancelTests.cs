using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1562 (ledger CP7): get magick-tech adjustment (action 80, 0x44933F): <c>(100 − cancel chance) × value / 100</c> into operand 4, the chance the host's answer (0x461620), with the original's 32-bit wrap and its division
/// that truncates toward zero; the set S51 holds it with the adjustment 91 and the eye candy 120. Programs, objects and numbers are invented.</summary>
[TestClass]
public sealed class ScrMagickTechCancelTests
{
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    private static readonly ScrObjectRef Sword = new(1, "kit:3"), Victim = new(1, "kit:4");

    private sealed class Host(FakeHost inner) : IScrHost, IScrMagickTechQueries, IScrCritterQueries
    {
        public Dictionary<ScrObjectRef, bool> Animals { get; } = new();
        public bool? IsAnimal(ScrObjectRef obj) => Animals.TryGetValue(obj, out var animal) ? animal : null;
        public bool? IsInCombat(ScrObjectRef obj) => null;
        public Dictionary<(ScrObjectRef, ScrObjectRef, ScrObjectRef), int> Chances { get; } = new();
        public List<(ScrObjectRef Item, ScrObjectRef Attacker, ScrObjectRef Target)> Asked { get; } = [];
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
        public int? MagickCancelPercent(ScrObjectRef item, ScrObjectRef attacker, ScrObjectRef target)
        {
            Asked.Add((item, attacker, target));
            return Chances.TryGetValue((item, attacker, target), out var chance) ? chance : null;
        }
    }

    private static readonly ScrAdmissionSet Set = new("cancel", ScrAdmissionSet.S5.Conditions.Append(ScrCatalogue.Condition.IsAnimal), ScrAdmissionSet.S5.Actions.Append(ScrCatalogue.Action.GetMagickTechAdjustment),
        ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);

    private static Host With(ScrProgram program, int chance)
    {
        var inner = new FakeHost();
        inner.Attach(Sword, 7, 700, program);
        return new Host(inner) { Chances = { [(Sword, Pc, Victim)] = chance } };
    }

    // The weapon's hit script as the combat step runs it: the attacker is the triggerer, the victim the extra object.
    private static ScrRunResult Hit(IScrHost host) => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), host, new() { Admission = Set });

    [TestMethod]
    public void The_value_is_scaled_by_the_remainder_of_the_cancel_chance_with_the_original_s_arithmetic()
    {
        // (100 − chance) × value / 100 into local 0, truncating toward zero; the 32-bit product wraps; chances outside 0..100 are the host's own numbers.
        (int Chance, int Value, int Expected)[] cases =
            [(0, 40, 40), (40, 100, 60), (100, 77, 0), (50, 3, 1), (50, -3, -1), (60, 10, 4), (-20, 100, 120), (130, 100, -30), (1, int.MaxValue, unchecked(99 * int.MaxValue) / 100)];
        foreach (var (chance, value, expected) in cases)
        {
            var host = With(Program(Do(C(ScrCatalogue.Action.GetMagickTechAdjustment, N(value), (Attachee, 0), (Triggerer, 0), (Extra, 0), (Local, 0))),
                L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(expected)), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault))), chance);
            var run = Hit(host);
            Assert.AreEqual(7, run.Effects.GlobalFlags.Single().Index, $"chance {chance}, value {value}");
            CollectionAssert.AreEqual(new[] { (Sword, Pc, Victim) }, host.Asked.Distinct().ToArray(), "the item, the attacker and the target as the operands name them");
        }
    }

    [TestMethod]
    public void The_result_goes_into_a_counter_or_a_variable_and_never_into_a_literal()
    {
        var counter = With(Program(Do(C(ScrCatalogue.Action.GetMagickTechAdjustment, N(300), (Attachee, 0), (Triggerer, 0), (Extra, 0), (Counter, 1))), Do(C(ScrCatalogue.Action.ReturnRunDefault))), 0);
        Assert.AreEqual(300u & 0xFF, Hit(counter).Effects.Writeback!.Slot.Counters >> 8, "the counter keeps the low byte");
        var literal = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetMagickTechAdjustment, N(1), (Attachee, 0), (Triggerer, 0), (Extra, 0), N(0)))), Set);
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 4), (literal.Refusal!.Code, literal.Refusal.Slot));
    }

    [TestMethod]
    public void An_item_or_critter_the_host_does_not_know_stops_the_run_and_an_absent_object_does_too()
    {
        var unknown = With(Program(Do(C(ScrCatalogue.Action.GetMagickTechAdjustment, N(10), (Attachee, 0), (Triggerer, 0), (Triggerer, 0), (Local, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))), 0);
        var run = Hit(unknown);
        Assert.AreEqual((ScrStop.MissingObject, 0), (run.Stop, run.StopLine), "a pair the host has no answer for");
        var absent = With(Program(Do(C(ScrCatalogue.Action.GetMagickTechAdjustment, N(10), (Attachee, 0), (Triggerer, 0), (Extra, 0), (Local, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))), 0);
        var noExtra = ScrDispatcher.RunObject(new(7, Sword, Pc, null), absent, new() { Admission = Set });
        Assert.AreEqual((ScrStop.MissingObject, 0), (noExtra.Stop, noExtra.StopLine), "no extra object");
    }

    [TestMethod]
    public void Obj_is_animal_asks_the_host_of_each_object_and_an_unknown_object_stops_the_run()
    {
        // If animal(extra) set global flag 7: the hunting weapon's test of the victim (script 28007).
        var program = Program(L(C(ScrCatalogue.Condition.IsAnimal, (Extra, 0)), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        foreach (var (animal, flags) in new[] { (true, 1), (false, 0) })
        {
            var host = With(program, 0);
            host.Animals[Victim] = animal;
            var run = Hit(host);
            Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
            Assert.AreEqual(flags, run.Effects.GlobalFlags.Count, $"animal {animal}");
        }
        var unknown = Hit(With(program, 0));
        Assert.AreEqual((ScrStop.MissingObject, 0), (unknown.Stop, unknown.StopLine), "a victim the host does not know");
        var noExtra = ScrDispatcher.RunObject(new(7, Sword, Pc, null), With(program, 0), new() { Admission = Set });
        Assert.AreEqual(ScrOutcome.RunDefault, noExtra.Outcome, "no extra object names none: the condition fails");
        Assert.IsTrue(noExtra.Effects.GlobalFlags.Count == 0);
    }

    [TestMethod]
    public void The_command_needs_a_host_that_answers_it_and_the_combat_point_set_holds_the_three_commands()
    {
        var plain = new FakeHost();
        plain.Attach(Sword, 7, 700, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), plain, new() { Admission = Set }));
        var animal = new ScrAdmissionSet("animal", ScrAdmissionSet.S5.Conditions.Append(ScrCatalogue.Condition.IsAnimal), ScrAdmissionSet.S5.Actions, ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), plain, new() { Admission = animal }));
        foreach (var action in new[] { ScrCatalogue.Action.GetMagickTechAdjustment, ScrCatalogue.Action.MagickTechAdjustment, ScrCatalogue.Action.PlayScriptEyeCandy })
            Assert.IsTrue(ScrAdmissionSet.S51.Actions.Contains(action), "action " + action);
        Assert.IsTrue(ScrAdmissionSet.S51.Conditions.Contains(ScrCatalogue.Condition.IsAnimal));
        Assert.IsTrue(ScrAdmissionSet.S43.IsSubsetOf(ScrAdmissionSet.S51));
        Assert.IsFalse(ScrAdmissionSet.S43.Actions.Contains(ScrCatalogue.Action.GetMagickTechAdjustment));
    }
}
