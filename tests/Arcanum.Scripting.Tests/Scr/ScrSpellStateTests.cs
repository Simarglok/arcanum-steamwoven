using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// ARC-SCRIPT-1734 (ledger SC18, WE11, WE12): obj is polymorphed (12, 0x20000), obj is shrunk (13, 0x80000) and obj has mirror image (16, 0x40000), each one bit of the object's spell flags (0x4451B0 cases 0xC, 0xD, 0x10) over the list rule of the other
/// object conditions, and the eye candy actions play spell eye candy (104, 0x456E60) and stop script eye candy (124, 0x44C800), which the set S55 holds. Programs, objects and numbers are invented.
/// </summary>
[TestClass]
public sealed class ScrSpellStateTests
{
    private const byte Triggerer = 0, Attachee = 1;
    private const int Script = 2800;
    private static readonly ScrObjectRef Other = new(1, "npc.2");

    private static readonly ScrAdmissionSet Set = new("test-spell-state", ScrAdmissionSet.S2.Conditions.Concat(ScrAdmissionSet.SpellQueryConditions),
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.PlayScriptEyeCandy, ScrCatalogue.Action.PlaySpellEyeCandy, ScrCatalogue.Action.StopScriptEyeCandy]), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private sealed class FlagsHost(FakeHost inner) : IScrHost, IScrSpellQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<ScrObjectRef, int> Flags { get; } = new();
        public List<string> Asked { get; } = new();
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
        public int? SpellFlags(ScrObjectRef obj) { Asked.Add($"flags {obj.Code}"); return Flags.TryGetValue(obj, out var flags) ? flags : null; }
        public bool? IsInfluencedBy(ScrObjectRef obj, int spell) => throw new InvalidOperationException("not asked");
        public bool? KnowsSpell(ScrObjectRef obj, int spell) => throw new InvalidOperationException("not asked");
        public int? MasteredCollege(ScrObjectRef obj) => throw new InvalidOperationException("not asked");
    }

    private static FlagsHost Host(ScrProgram program)
    {
        var host = new FlagsHost(new FakeHost());
        host.Inner.Attach(Npc, ScrPoint.Heartbeat, Script, program);
        return host;
    }

    private static ScrRunResult Beat(FlagsHost host) => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), host, Options);

    private static ScrProgram Test(int condition, byte focus) => Program(
        L(C(condition, [(focus, focus == Attachee ? -1 : 0)]), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrOutcome Holds(bool value) => value ? ScrOutcome.SkipDefault : ScrOutcome.RunDefault;

    [TestMethod]
    public void Each_condition_tests_its_own_bit_of_the_spell_flags_only()
    {
        const int polymorphed = 0x20000, mirror = 0x40000, shrunk = 0x80000;
        foreach (var (condition, bit) in new[] { (ScrCatalogue.Condition.IsPolymorphed, polymorphed), (ScrCatalogue.Condition.IsShrunk, shrunk), (ScrCatalogue.Condition.HasMirrorImage, mirror) })
        {
            foreach (var (flags, expected) in new[] { (bit, true), (bit | 0x3C | 0x1, true), (0x20000 | 0x40000 | 0x80000, true), (0, false), (0x3C, false), (unchecked((int)0xFFF00000 & ~(polymorphed | mirror | shrunk)), false) })
            {
                var host = Host(Test(condition, Attachee));
                host.Flags[Npc] = flags;
                Assert.AreEqual(Holds(expected), Beat(host).Outcome, $"condition {condition}, flags {flags:X}");
            }
            // The other two bits alone do not count.
            var others = (polymorphed | mirror | shrunk) & ~bit;
            var only = Host(Test(condition, Attachee));
            only.Flags[Npc] = others;
            Assert.AreEqual(ScrOutcome.RunDefault, Beat(only).Outcome, $"condition {condition} with the other two bits {others:X}");
        }
    }

    [TestMethod]
    public void An_absent_object_fails_and_an_unknown_one_stops_the_run()
    {
        foreach (var condition in new[] { ScrCatalogue.Condition.IsPolymorphed, ScrCatalogue.Condition.IsShrunk, ScrCatalogue.Condition.HasMirrorImage })
        {
            var absent = Host(Test(condition, 12));
            Assert.AreEqual(ScrOutcome.RunDefault, Beat(absent).Outcome, $"condition {condition}");
            CollectionAssert.AreEqual(Array.Empty<string>(), absent.Asked, "SG6: an empty list asks nothing");
            Assert.AreEqual(ScrStop.MissingObject, Beat(Host(Test(condition, Attachee))).Stop, $"condition {condition}: a host that does not know the attachee");
        }
    }

    [TestMethod]
    public void The_conditions_are_asked_of_the_object_the_operand_names()
    {
        var host = Host(Test(ScrCatalogue.Condition.IsShrunk, Triggerer));
        host.Flags[Pc] = 0x80000;
        host.Flags[Npc] = 0;
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(host).Outcome);
        CollectionAssert.AreEqual(new[] { "flags pc" }, host.Asked);
    }

    [TestMethod]
    public void The_eye_candy_actions_hand_the_number_and_the_first_object_to_the_host_in_order()
    {
        var program = Program(
            Do(C(ScrCatalogue.Action.PlayScriptEyeCandy, N(30), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.PlaySpellEyeCandy, N(23), (Attachee, -1))),
            Do(C(ScrCatalogue.Action.StopScriptEyeCandy, N(30), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.StopScriptEyeCandy, N(31), (12, 0))),
            Do(C(ScrCatalogue.Action.PlaySpellEyeCandy, N(9), (12, 0))),
            Do(C(ScrCatalogue.Action.StopScriptEyeCandy, N(-5), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.PlaySpellEyeCandy, N(100000), (Attachee, -1))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        var run = Beat(Host(program));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "the run goes on");
        CollectionAssert.AreEqual(new[]
        {
            new ScrEyeCandy(30, Pc),
            new ScrEyeCandy(23, Npc, ScrEyeCandyKind.PlaySpell),
            new ScrEyeCandy(30, Pc, ScrEyeCandyKind.StopScript),
            new ScrEyeCandy(-5, Pc, ScrEyeCandyKind.StopScript),
            new ScrEyeCandy(100000, Npc, ScrEyeCandyKind.PlaySpell),
        }, run.Effects.EyeCandies.ToArray(), "an absent object shows and stops nothing (the original passes a null handle on); the number goes on as read, whatever its sign or size");
    }

    [TestMethod]
    public void S55_adds_the_three_conditions_and_the_two_actions_over_S54()
    {
        foreach (var condition in new[] { ScrCatalogue.Condition.IsPolymorphed, ScrCatalogue.Condition.IsShrunk, ScrCatalogue.Condition.HasMirrorImage })
        {
            var program = Test(condition, Attachee);
            Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S55).IsAdmitted, $"condition {condition}");
            Assert.AreEqual(ScrRefusalCode.ConditionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S54).Refusal!.Code);
        }
        foreach (var action in new[] { ScrCatalogue.Action.PlaySpellEyeCandy, ScrCatalogue.Action.StopScriptEyeCandy })
        {
            var program = Program(Do(C(action, N(1), (Triggerer, 0))));
            Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S55).IsAdmitted, $"action {action}");
            Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S54).Refusal!.Code);
        }
        Assert.IsTrue(ScrAdmissionSet.S54.IsSubsetOf(ScrAdmissionSet.S55));
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Condition.IsPolymorphed, ScrCatalogue.Condition.IsShrunk, ScrCatalogue.Condition.HasMirrorImage }, ScrAdmissionSet.S55.Conditions.Except(ScrAdmissionSet.S54.Conditions).ToArray());
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.PlaySpellEyeCandy, ScrCatalogue.Action.StopScriptEyeCandy }, ScrAdmissionSet.S55.Actions.Except(ScrAdmissionSet.S54.Actions).ToArray());
        var plain = new FakeHost();
        plain.Attach(Npc, ScrPoint.Heartbeat, Script, Test(ScrCatalogue.Condition.IsShrunk, Attachee));
        var refused = Assert.Throws<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), plain, Options));
        StringAssert.Contains(refused.Message, nameof(IScrSpellQueries));
    }
}
