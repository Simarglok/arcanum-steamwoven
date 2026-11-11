using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-MAGIC-029 (MT21a, ledger SC1–SC5): the spell conditions obj has body spell (14), obj is influenced by spell (35), knows spell (48) and mastered spell college (49), and the action give spell mastery in college
/// (112) of set S31, on synthetic programs. The meaning of the answers (spell book, spell runs, flags) is the host's; these tests pin what the interpreter asks and does with them.</summary>
[TestClass]
public sealed class ScrSpellConditionTests
{
    private const byte Triggerer = 0, Attachee = 1;
    private const int Script = 2800;
    // S2 with single-object focus plus the five commands: the shape a host that answers only the spell questions runs.
    private static readonly ScrAdmissionSet Set = new("test-spell-conditions", ScrAdmissionSet.S2.Conditions.Concat(ScrAdmissionSet.SpellQueryConditions),
        ScrAdmissionSet.S2.Actions.Append(ScrCatalogue.Action.GiveSpellMastery), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private sealed class SpellHost(FakeHost inner) : IScrHost, IScrSpellQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<ScrObjectRef, int> Flags { get; } = new();
        public Dictionary<(ScrObjectRef, int), bool> Influenced { get; } = new();
        public Dictionary<ScrObjectRef, HashSet<int>> Known { get; } = new();
        public Dictionary<ScrObjectRef, int> Mastered { get; } = new();
        public HashSet<ScrObjectRef> Unknown { get; } = new();
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
        public bool? IsInfluencedBy(ScrObjectRef obj, int spell) { Asked.Add($"influenced {obj.Code} {spell}"); if (Unknown.Contains(obj)) return null; return Influenced.TryGetValue((obj, spell), out var held) ? held : false; }
        public bool? KnowsSpell(ScrObjectRef obj, int spell) { Asked.Add($"knows {obj.Code} {spell}"); return Known.TryGetValue(obj, out var spells) ? spells.Contains(spell) : null; }
        public int? MasteredCollege(ScrObjectRef obj) { Asked.Add($"mastered {obj.Code}"); return Mastered.TryGetValue(obj, out var college) ? college : null; }
    }

    private static SpellHost Host(ScrProgram program)
    {
        var host = new SpellHost(new FakeHost());
        host.Inner.Attach(Npc, ScrPoint.Heartbeat, Script, program);
        return host;
    }

    private static ScrRunResult Beat(SpellHost host) => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), host, Options);

    // The condition's true branch skips the default, its false branch runs it.
    private static ScrProgram Test(int condition, byte focus, params (byte, int)[] operands) => Program(
        L(C(condition, [(focus, focus == Attachee ? -1 : 0), .. operands]), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrOutcome Holds(bool value) => value ? ScrOutcome.SkipDefault : ScrOutcome.RunDefault;

    [TestMethod]
    public void Body_spell_tests_the_four_body_bits_of_the_spell_flags_only()
    {
        // SC1: 0x3C = body of air 0x4, earth 0x8, fire 0x10, water 0x20; invisible 0x1, floating 0x2 and every higher bit do not count.
        foreach (var (flags, expected) in new[] { (0x4, true), (0x8, true), (0x10, true), (0x20, true), (0x3C, true), (0x1 | 0x2, false), (0x40, false), (0x7FFFFFC0, false), (unchecked((int)0x80000000), false), (0, false), (0x1 | 0x20, true) })
        {
            var host = Host(Test(ScrCatalogue.Condition.HasBodySpell, Attachee));
            host.Flags[Npc] = flags;
            Assert.AreEqual(Holds(expected), Beat(host).Outcome, $"flags {flags:X}");
        }
    }

    [TestMethod]
    public void Knows_spell_hands_the_object_and_the_spell_as_read_and_takes_the_hosts_answer()
    {
        var host = Host(Test(ScrCatalogue.Condition.KnowsSpell, Attachee, N(7)));
        host.Known[Npc] = [6];
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome);
        host.Known[Npc] = [6, 7];
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(host).Outcome);
        CollectionAssert.AreEqual(new[] { "knows npc.1 7", "knows npc.1 7" }, host.Asked, "the spell number is not range-tested by the script (the host's rule does it, SB5)");
        // The spell may come from a variable.
        var program = Program(
            Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(6))),
            L(C(ScrCatalogue.Condition.KnowsSpell, (Attachee, -1), (Local, 0)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var byLocal = Host(program);
        byLocal.Known[Npc] = [6];
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(byLocal).Outcome);
    }

    [TestMethod]
    public void Mastered_college_compares_the_college_with_the_value_and_minus_one_matches_no_mastery()
    {
        foreach (var (mastered, value, expected) in new[] { (5, 5, true), (5, 6, false), (-1, -1, true), (-1, 0, false), (0, -1, false), (15, 15, true) })
        {
            var host = Host(Test(ScrCatalogue.Condition.MasteredSpellCollege, Attachee, N(value)));
            host.Mastered[Npc] = mastered;
            Assert.AreEqual(Holds(expected), Beat(host).Outcome, $"mastered {mastered}, value {value}");
        }
    }

    [TestMethod]
    public void Influenced_by_spell_takes_the_hosts_answer_for_the_object_and_the_spell()
    {
        var host = Host(Test(ScrCatalogue.Condition.InfluencedBySpell, Attachee, N(40)));
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome);
        host.Influenced[(Npc, 40)] = true;
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(host).Outcome);
        host.Influenced[(Npc, 40)] = false;
        host.Influenced[(Npc, 41)] = true;
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome, "another spell");
    }

    [TestMethod]
    public void An_absent_object_fails_the_conditions_and_an_unknown_one_stops_the_run()
    {
        foreach (var condition in new[] { ScrCatalogue.Condition.HasBodySpell, ScrCatalogue.Condition.InfluencedBySpell, ScrCatalogue.Condition.KnowsSpell, ScrCatalogue.Condition.MasteredSpellCollege })
        {
            // SG6: an empty list is false and asks nothing (the event has no extra object).
            var host = Host(Test(condition, 12, N(0)));
            Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome, $"condition {condition}");
            CollectionAssert.AreEqual(Array.Empty<string>(), host.Asked);
        }
        // The host does not know the attachee: the run stops as for an absent object.
        foreach (var condition in new[] { ScrCatalogue.Condition.HasBodySpell, ScrCatalogue.Condition.KnowsSpell, ScrCatalogue.Condition.MasteredSpellCollege })
            Assert.AreEqual(ScrStop.MissingObject, Beat(Host(Test(condition, Attachee, N(0)))).Stop, $"condition {condition}");
        var influenced = Host(Test(ScrCatalogue.Condition.InfluencedBySpell, Attachee, N(0)));
        influenced.Unknown.Add(Npc);
        Assert.AreEqual(ScrStop.MissingObject, Beat(influenced).Stop, "condition 35");
    }

    [TestMethod]
    public void Give_spell_mastery_hands_over_the_first_object_and_the_college_as_read()
    {
        var program = Program(
            Do(C(ScrCatalogue.Action.GiveSpellMastery, (Triggerer, 0), N(5))),
            Do(C(ScrCatalogue.Action.GiveSpellMastery, (Attachee, -1), N(99))),
            Do(C(ScrCatalogue.Action.GiveSpellMastery, (12, 0), N(3))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault)));
        var run = Beat(Host(program));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "the run goes on");
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.GiveSpellMastery, Pc, 5),
            new ScrObjectStateRequest(ScrObjectStateChange.GiveSpellMastery, Npc, 99),
        }, run.Effects.ObjectStates.ToArray(), "the college is not range-checked; an absent object asks for nothing");
    }

    [TestMethod]
    public void S31_adds_the_five_over_S30_and_a_host_must_answer_the_spell_conditions()
    {
        foreach (var condition in new[] { ScrCatalogue.Condition.HasBodySpell, ScrCatalogue.Condition.InfluencedBySpell, ScrCatalogue.Condition.KnowsSpell, ScrCatalogue.Condition.MasteredSpellCollege })
        {
            var program = Test(condition, Attachee, N(1));
            Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S31).IsAdmitted, $"condition {condition}");
            Assert.AreEqual(ScrRefusalCode.ConditionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S30).Refusal!.Code);
        }
        var mastery = Program(Do(C(ScrCatalogue.Action.GiveSpellMastery, (Triggerer, 0), N(1))));
        Assert.IsTrue(ScrAdmission.Admit(mastery, ScrAdmissionSet.S31).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(mastery, ScrAdmissionSet.S30).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S30.IsSubsetOf(ScrAdmissionSet.S31) && Set.IsSubsetOf(ScrAdmissionSet.S31));
        // A plain host cannot run a set that holds the conditions.
        var plain = new FakeHost();
        plain.Attach(Npc, ScrPoint.Heartbeat, Script, Test(ScrCatalogue.Condition.KnowsSpell, Attachee, N(1)));
        var refused = Assert.Throws<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), plain, Options));
        StringAssert.Contains(refused.Message, nameof(IScrSpellQueries));
    }
}
