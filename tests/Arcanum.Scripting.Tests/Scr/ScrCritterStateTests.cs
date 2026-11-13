using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// ARC-SCRIPT-1733 (ledger CS1–CS12): the nine critter-state and social conditions of the set S53 (obj met PC before 10, obj is invisible 15, obj has surrendered 25, obj is in dialog 26, obj can see obj 28, obj can hear obj 29, obj is invulnerable 30, obj is
/// undead 38, prowling 51; 0x4451B0): the single-object ones follow the list rule of the other object tests, the three two-object ones take the first object of each operand in order. Programs, objects and answers are invented; the host answers as the use host does.
/// </summary>
[TestClass]
public sealed class ScrCritterStateTests
{
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    private static readonly ScrObjectRef Sword = new(1, "kit:3"), Victim = new(1, "kit:4");

    private static readonly int[] OneObject =
    [
        ScrCatalogue.Condition.IsInvisible, ScrCatalogue.Condition.IsSurrendered, ScrCatalogue.Condition.IsInDialog, ScrCatalogue.Condition.IsInvulnerable, ScrCatalogue.Condition.IsUndead, ScrCatalogue.Condition.IsProwling,
    ];

    private static readonly int[] TwoObjects = [ScrCatalogue.Condition.MetPcBefore, ScrCatalogue.Condition.CanSeeObject, ScrCatalogue.Condition.CanHearObject];

    private sealed class Host(FakeHost inner) : IScrHost, IScrCritterStateQueries
    {
        /// <summary>What the host answers per condition type and object; a missing entry is a null answer (an object it does not know).</summary>
        public Dictionary<(int Type, ScrObjectRef Object), bool> Answers { get; } = new();
        public Dictionary<(int Type, ScrObjectRef First, ScrObjectRef Second), bool> Pairs { get; } = new();
        public List<(int Type, ScrObjectRef First, ScrObjectRef? Second)> Asked { get; } = [];

        private bool? One(int type, ScrObjectRef obj)
        {
            Asked.Add((type, obj, null));
            return Answers.TryGetValue((type, obj), out var answer) ? answer : null;
        }

        private bool? Two(int type, ScrObjectRef first, ScrObjectRef second)
        {
            Asked.Add((type, first, second));
            return Pairs.TryGetValue((type, first, second), out var answer) ? answer : null;
        }

        public bool? MetPcBefore(ScrObjectRef npc, ScrObjectRef pc) => Two(ScrCatalogue.Condition.MetPcBefore, npc, pc);
        public bool? IsInvisible(ScrObjectRef obj) => One(ScrCatalogue.Condition.IsInvisible, obj);
        public bool? IsSurrendered(ScrObjectRef obj) => One(ScrCatalogue.Condition.IsSurrendered, obj);
        public bool? IsInDialog(ScrObjectRef obj) => One(ScrCatalogue.Condition.IsInDialog, obj);
        public bool? CanSee(ScrObjectRef observer, ScrObjectRef target) => Two(ScrCatalogue.Condition.CanSeeObject, observer, target);
        public bool? CanHear(ScrObjectRef listener, ScrObjectRef source) => Two(ScrCatalogue.Condition.CanHearObject, listener, source);
        public bool? IsInvulnerable(ScrObjectRef obj) => One(ScrCatalogue.Condition.IsInvulnerable, obj);
        public bool? IsUndead(ScrObjectRef obj) => One(ScrCatalogue.Condition.IsUndead, obj);
        public bool? IsProwling(ScrObjectRef obj) => One(ScrCatalogue.Condition.IsProwling, obj);
        public bool? HasBadAssociates(ScrObjectRef obj) => null; // ARC-SCRIPT-1771: asked in ScrCritterTailTests
        public bool? IsMonsterOfType(ScrObjectRef obj, int type) => null;
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

    // S5 is the base the other critter tests use; the nine conditions are added by name so this test needs no other host answer.
    private static readonly ScrAdmissionSet Set = new("critter-state", ScrAdmissionSet.S5.Conditions.Concat(ScrAdmissionSet.CritterStateConditions), ScrAdmissionSet.S5.Actions, ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);

    private static Host With(ScrProgram program)
    {
        var inner = new FakeHost();
        inner.Attach(Sword, 7, 700, program);
        return new Host(inner);
    }

    private static ScrRunResult Hit(IScrHost host, ScrObjectRef? extra = null) => ScrDispatcher.RunObject(new(7, Sword, Pc, extra ?? Victim), host, new() { Admission = Set });

    // If condition(operands) set global flag 7, else nothing.
    private static ScrProgram Flag(int condition, params (byte, int)[] operands) =>
        Program(L(C(condition, operands), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    [TestMethod]
    public void Each_one_object_condition_asks_the_host_of_the_object_its_operand_names()
    {
        foreach (var type in OneObject)
        foreach (var (answer, flags) in new[] { (true, 1), (false, 0) })
        {
            var host = With(Flag(type, (Extra, 0)));
            host.Answers[(type, Victim)] = answer;
            var run = Hit(host);
            Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome, $"c{type}");
            Assert.AreEqual(flags, run.Effects.GlobalFlags.Count, $"c{type} answers {answer}");
            CollectionAssert.AreEqual(new[] { (type, Victim, (ScrObjectRef?)null) }, host.Asked, $"c{type} asks of the extra object only");
        }
    }

    [TestMethod]
    public void A_one_object_condition_stops_the_run_for_an_object_the_host_does_not_know_and_fails_for_an_absent_one()
    {
        foreach (var type in OneObject)
        {
            var unknown = Hit(With(Flag(type, (Extra, 0))));
            Assert.AreEqual((ScrStop.MissingObject, 0), (unknown.Stop, unknown.StopLine), $"c{type}: the host does not know the extra object");
            var host = With(Flag(type, (Extra, 0)));
            var absent = ScrDispatcher.RunObject(new(7, Sword, Pc, null), host, new() { Admission = Set });
            Assert.AreEqual(0, absent.Effects.GlobalFlags.Count, $"c{type}: no extra object names none, so the condition fails");
            Assert.AreEqual(0, host.Asked.Count, $"c{type}: nothing is asked of an absent object");
        }
    }

    [TestMethod]
    public void The_two_object_conditions_ask_the_first_object_then_the_second_in_operand_order()
    {
        foreach (var type in TwoObjects)
        foreach (var (answer, flags) in new[] { (true, 1), (false, 0) })
        {
            // The attachee first, the triggerer second: reversed, the host would see (Pc, Sword).
            var host = With(Flag(type, (Attachee, 0), (Triggerer, 0)));
            host.Pairs[(type, Sword, Pc)] = answer;
            var run = Hit(host);
            Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome, $"c{type}");
            Assert.AreEqual(flags, run.Effects.GlobalFlags.Count, $"c{type} answers {answer}");
            CollectionAssert.AreEqual(new[] { (type, Sword, (ScrObjectRef?)Pc) }, host.Asked);
        }
    }

    [TestMethod]
    public void A_two_object_condition_stops_the_run_for_a_pair_the_host_does_not_know_or_an_absent_operand()
    {
        foreach (var type in TwoObjects)
        {
            var unknown = Hit(With(Flag(type, (Attachee, 0), (Triggerer, 0))));
            Assert.AreEqual((ScrStop.MissingObject, 0), (unknown.Stop, unknown.StopLine), $"c{type}: the host answers null");
            var host = With(Flag(type, (Attachee, 0), (Extra, 0)));
            var absent = ScrDispatcher.RunObject(new(7, Sword, Pc, null), host, new() { Admission = Set });
            Assert.AreEqual(ScrStop.MissingObject, absent.Stop, $"c{type}: an absent second object");
            Assert.AreEqual(0, host.Asked.Count, $"c{type}: nothing is asked");
        }
    }

    [TestMethod]
    public void A_one_object_condition_over_the_player_kind_reads_the_host_answer_of_the_player()
    {
        // The group kinds need the world's vicinity, which FakeHost does not name, and the list rule is ListHolds' own (pinned by the dead and animal tests); the player kind shows the answer is read per list object.
        var host = With(Flag(ScrCatalogue.Condition.IsUndead, (21, 0)));
        host.Answers[(ScrCatalogue.Condition.IsUndead, Pc)] = true;
        var set = new ScrAdmissionSet("critter-state-player", Set.Conditions, Set.Actions, Set.ValueKinds, Set.FocusKinds.Append(ScrFocusKind.Player));
        var run = ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), host, new() { Admission = set });
        Assert.AreEqual(1, run.Effects.GlobalFlags.Count);
    }

    [TestMethod]
    public void The_commands_need_a_host_that_answers_them_and_S53_adds_exactly_the_nine_conditions_to_S52()
    {
        var plain = new FakeHost();
        plain.Attach(Sword, 7, 700, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), plain, new() { Admission = Set }));
        CollectionAssert.AreEquivalent(ScrAdmissionSet.CritterStateConditions.ToArray(), ScrAdmissionSet.S53.Conditions.Except(ScrAdmissionSet.S52.Conditions).ToArray());
        CollectionAssert.AreEquivalent(new[] { 10, 15, 25, 26, 28, 29, 30, 38, 51 }, ScrAdmissionSet.CritterStateConditions.ToArray());
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S52.Actions.ToArray(), ScrAdmissionSet.S53.Actions.ToArray());
        Assert.IsTrue(ScrAdmissionSet.S52.IsSubsetOf(ScrAdmissionSet.S53));
    }
}
