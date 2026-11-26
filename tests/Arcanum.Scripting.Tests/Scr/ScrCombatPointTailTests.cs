using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// ARC-SCRIPT-1661 (ledger CP11, CP12): obj is in combat (condition 31, 0x4451B0 case 0x1f: a PC or NPC with the combat-mode bit, 0x4B3D50) over the list rule of the other object conditions, and adjust stat (action 137, 0x447220 case 0x89:
/// the number is added to the object's base stat and goes through the stat setter), which the set S52 holds. Programs, objects and numbers are invented; the host answers as the use host does.
/// </summary>
[TestClass]
public sealed class ScrCombatPointTailTests
{
    private const byte Triggerer = 0, Extra = 12;
    private static readonly ScrObjectRef Sword = new(1, "kit:3"), Victim = new(1, "kit:4");

    private sealed class Host(FakeHost inner) : IScrHost, IScrCritterQueries, IScrStatAdjustQueries
    {
        public Dictionary<ScrObjectRef, bool> Combat { get; } = new();
        public HashSet<(ScrObjectRef, int)> Adjustable { get; } = new();
        public List<(ScrObjectRef Object, int Stat)> AskedAdjust { get; } = [];
        public bool? IsAnimal(ScrObjectRef obj) => null;
        public bool? IsInCombat(ScrObjectRef obj) => Combat.TryGetValue(obj, out var combat) ? combat : null;
        public bool CanAdjustStat(ScrObjectRef obj, int stat) { AskedAdjust.Add((obj, stat)); return Adjustable.Contains((obj, stat)); }
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
    }

    // S5 is the base the other combat tests use; the two commands are added by name so this test needs no other host answer.
    private static readonly ScrAdmissionSet Set = new("tail", ScrAdmissionSet.S5.Conditions.Append(ScrCatalogue.Condition.IsInCombat), ScrAdmissionSet.S5.Actions.Append(ScrCatalogue.Action.AdjustStat),
        ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);

    private static Host With(ScrProgram program)
    {
        var inner = new FakeHost();
        inner.Attach(Sword, 7, 700, program);
        return new Host(inner);
    }

    private static ScrRunResult Hit(IScrHost host, ScrAdmissionSet? set = null) => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), host, new() { Admission = set ?? Set });

    [TestMethod]
    public void Obj_is_in_combat_asks_the_host_of_the_object_the_operand_names()
    {
        // If in combat(extra) set global flag 7.
        var program = Program(L(C(ScrCatalogue.Condition.IsInCombat, (Extra, 0)), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        foreach (var (combat, flags) in new[] { (true, 1), (false, 0) })
        {
            var host = With(program);
            host.Combat[Victim] = combat;
            var run = Hit(host);
            Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
            Assert.AreEqual(flags, run.Effects.GlobalFlags.Count, $"in combat {combat}");
        }
        var unknown = Hit(With(program));
        Assert.AreEqual((ScrStop.MissingObject, 0), (unknown.Stop, unknown.StopLine), "a victim the host does not know");
        var noExtra = ScrDispatcher.RunObject(new(7, Sword, Pc, null), With(program), new() { Admission = Set });
        Assert.AreEqual(0, noExtra.Effects.GlobalFlags.Count, "no extra object names none: the condition fails");
    }

    [TestMethod]
    public void Obj_is_in_combat_is_asked_of_an_object_variable_as_the_area_weapons_ask_it_of_the_looped_object()
    {
        // 30064's shape: loop for each critter around the attachee (kind 8), skip it unless it is in combat (kind 10, the current looped object), and say so by adding to a counter... here by a global flag set per looped object that passed.
        // A FakeHost has no vicinity, so the looped object is named through the one-object list of the extra object by assign object first.
        var program = Program(Do(C(ScrCatalogue.Action.AssignObject, (11, 0), (Extra, 0))),
            L(C(ScrCatalogue.Condition.IsInCombat, (11, 0)), C(ScrCatalogue.Action.SetGlobalFlag, N(8)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = With(program);
        host.Combat[Victim] = true;
        var run = ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), host, new() { Admission = new("tail-objects", Set.Conditions, Set.Actions.Append(ScrCatalogue.Action.AssignObject), Set.ValueKinds, Set.FocusKinds.Append(ScrFocusKind.LocalObject)) });
        Assert.AreEqual(8, run.Effects.GlobalFlags.Single().Index);
    }

    [TestMethod]
    public void Obj_is_in_combat_reads_the_player_focus_kind_through_the_host()
    {
        // The list rule (all, or one for the any kinds) is ListHolds', shared with obj is animal and the dead test, whose tests pin it; FakeHost names no group, so only the one-object kinds are exercised here.
        var program = Program(L(C(ScrCatalogue.Condition.IsInCombat, (21, 0)), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = With(program);
        host.Combat[Pc] = true;
        var set = new ScrAdmissionSet("tail-player", Set.Conditions, Set.Actions, Set.ValueKinds, Set.FocusKinds.Append(ScrFocusKind.Player));
        Assert.AreEqual(1, Hit(host, set).Effects.GlobalFlags.Count);
    }

    [TestMethod]
    public void Adjust_stat_hands_the_stat_the_object_and_the_number_to_the_host_in_action_order()
    {
        // Adjust stat 19 of the triggerer by −200, then by +50: two requests in order; the host was asked about the triggerer and stat 19.
        var program = Program(Do(C(ScrCatalogue.Action.AdjustStat, N(19), (Triggerer, 0), N(-200))), Do(C(ScrCatalogue.Action.AdjustStat, N(19), (Triggerer, 0), N(50))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = With(program);
        host.Adjustable.Add((Pc, 19));
        var run = Hit(host);
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { (ScrStateKind.AdjustStat, Pc, 19, -200), (ScrStateKind.AdjustStat, Pc, 19, 50) },
            run.Effects.States.Select(s => (s.Kind, s.Object!.Value, s.Number, s.Value)).ToArray());
    }

    [TestMethod]
    public void Adjust_stat_reads_its_number_from_a_variable_and_a_stat_or_object_the_host_does_not_model_ends_the_dispatch_with_nothing_applied()
    {
        var variable = Program(Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(-30))), Do(C(ScrCatalogue.Action.AdjustStat, N(19), (Triggerer, 0), (Local, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = With(variable);
        host.Adjustable.Add((Pc, 19));
        Assert.AreEqual(-30, Hit(host).Effects.States.Single().Value);
        // The hero's alignment only: strength (0) of the hero, or the alignment of another object, ends as not modelled with no effect (the original would write both).
        foreach (var (who, stat) in new[] { (Pc, 0), (Victim, 19) })
        {
            var refused = With(Program(Do(C(ScrCatalogue.Action.AdjustStat, N(stat), (who == Pc ? Triggerer : Extra, 0), N(5))), Do(C(ScrCatalogue.Action.SetGlobalFlag, N(9))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
            refused.Adjustable.Add((Pc, 19));
            var run = Hit(refused);
            Assert.AreEqual(ScrStop.NotModelled, run.Stop, $"stat {stat} of {who.Code}");
            Assert.AreEqual(0, run.Effects.States.Count + run.Effects.GlobalFlags.Count, "nothing applied");
            CollectionAssert.AreEqual(new[] { (who, stat) }, refused.AskedAdjust);
        }
    }

    [TestMethod]
    public void Adjust_stat_of_an_absent_object_does_nothing_and_asks_nothing()
    {
        var program = Program(Do(C(ScrCatalogue.Action.AdjustStat, N(19), (Extra, 0), N(-200))), Do(C(ScrCatalogue.Action.SetGlobalFlag, N(9))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = With(program);
        var run = ScrDispatcher.RunObject(new(7, Sword, Pc, null), host, new() { Admission = Set });
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        Assert.AreEqual(0, run.Effects.States.Count);
        Assert.AreEqual(9, run.Effects.GlobalFlags.Single().Index, "the run goes on after the line");
        Assert.AreEqual(0, host.AskedAdjust.Count);
    }

    [TestMethod]
    public void The_command_needs_a_host_that_answers_it_and_S52_adds_exactly_the_condition_and_the_action_to_S51()
    {
        var plain = new FakeHost();
        plain.Attach(Sword, 7, 700, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), plain, new() { Admission = Set }));
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Condition.IsInCombat }, ScrAdmissionSet.S52.Conditions.Except(ScrAdmissionSet.S51.Conditions).ToArray());
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.AdjustStat }, ScrAdmissionSet.S52.Actions.Except(ScrAdmissionSet.S51.Actions).ToArray());
        Assert.IsTrue(ScrAdmissionSet.S51.IsSubsetOf(ScrAdmissionSet.S52));
    }

    [TestMethod]
    public void A_get_stat_after_an_adjust_of_the_same_stat_cannot_read_the_pending_write()
    {
        // The host applies the adjustments after the run, so the run's own read of the adjusted stat would see the old value: the dispatch ends as not modelled instead.
        var program = Program(Do(C(ScrCatalogue.Action.AdjustStat, N(19), (Triggerer, 0), N(-200))), Do(C(ScrCatalogue.Action.GetStat, N(19), (Triggerer, 0), (Local, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var inner = new FakeHost();
        inner.Attach(Sword, 7, 700, program);
        var host = new StatHost(inner);
        var set = new ScrAdmissionSet("tail-stat", Set.Conditions, Set.Actions.Append(ScrCatalogue.Action.GetStat), Set.ValueKinds, Set.FocusKinds);
        var run = Hit(host, set);
        Assert.AreEqual(ScrStop.NotModelled, run.Stop);
        Assert.AreEqual(0, run.Effects.States.Count);
    }

    private sealed class StatHost(FakeHost inner) : IScrHost, IScrCritterQueries, IScrStatAdjustQueries, IScrStatQueries
    {
        public bool? IsAnimal(ScrObjectRef obj) => null;
        public bool? IsInCombat(ScrObjectRef obj) => null;
        public bool CanAdjustStat(ScrObjectRef obj, int stat) => obj == Pc && stat == 19;
        public int? EffectiveStat(ScrObjectRef obj, int stat) => 0;
        public bool IsDumbByBackground(ScrObjectRef obj) => false;
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
    }
}
