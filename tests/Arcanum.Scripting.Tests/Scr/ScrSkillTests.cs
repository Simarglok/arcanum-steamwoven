using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SKILLS-009: get skill (47) and use item with skill (79) of set S11 (ledger SX1–SX4). Programs and levels are
/// invented.</summary>
[TestClass]
public sealed class ScrSkillTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    private static readonly ScrRunOptions S11 = new() { Admission = ScrAdmissionSet.S11 };

    /// <summary>A recording host that also answers skill levels.</summary>
    private sealed class SkillHost(FakeHost inner) : IScrHost, IScrSkillQueries, IScrObjectQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<(ScrObjectRef, int), int> Levels { get; } = new();
        public List<(ScrObjectRef, int)> Asked { get; } = new();
        public int SkillLevel(ScrObjectRef obj, int skill) { Asked.Add((obj, skill)); return Levels.TryGetValue((obj, skill), out var level) ? level : 0; }
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
        // S11 holds S4's guard conditions; these programs use none of them.
        public int QuestState(ScrObjectRef obj, int quest) => throw new AssertFailedException("not asked");
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => throw new AssertFailedException("not asked");
        public int NameOf(ScrObjectRef obj) => throw new AssertFailedException("not asked");
        public bool IsMindControlled(ScrObjectRef obj) => throw new AssertFailedException("not asked");
        public int? WieldedItemName(ScrObjectRef obj, int location) => throw new AssertFailedException("not asked");
        public bool HasReputation(ScrObjectRef obj, int reputation) => throw new AssertFailedException("not asked");
    }

    private static Clause GetSkill(int skill, byte focus, int counter) => C(ScrCatalogue.Action.GetSkill, N(skill), (focus, 0), (Counter, counter));

    [TestMethod]
    public void Get_skill_stores_the_hosts_effective_level_and_zero_outside_the_sixteen_skills_without_asking()
    {
        var host = new SkillHost(new FakeHost());
        host.Levels[(Pc, 6)] = 12;
        host.Levels[(Npc, 13)] = 7;
        host.Inner.Attach(Npc, Use, 2023, Program(
            Do(GetSkill(6, Triggerer, 0)),
            Do(GetSkill(13, Attachee, 1)),
            Do(GetSkill(16, Triggerer, 2)),
            Do(GetSkill(-1, Triggerer, 3)),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))), counters: 0x05050505);
        var run = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, S11);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, "the run goes on after each store");
        Assert.AreEqual(12u | 7u << 8, run.Effects.Writeback!.Slot.Counters, "the levels in counters 0 and 1; 0 for skill 16 and −1");
        CollectionAssert.AreEqual(new[] { (Pc, 6), (Npc, 13) }, host.Asked, "the host is asked only for 0..15, with the number as the script gives it");
    }

    [TestMethod]
    public void Get_skill_on_an_absent_object_stops_the_run_and_its_store_must_not_be_a_number()
    {
        var host = new SkillHost(new FakeHost());
        host.Inner.Attach(Npc, Use, 2023, Program(Do(GetSkill(3, Extra, 0)), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var run = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, S11);
        Assert.AreEqual((ScrStop.MissingObject, 0), (run.Stop, run.StopLine), "no extra object: the run stops at the read, as at every object read");
        Assert.AreEqual(0, host.Asked.Count);

        var literal = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetSkill, N(3), (Triggerer, 0), N(5)))), ScrAdmissionSet.S11);
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 2), (literal.Refusal!.Code, literal.Refusal.Slot), "the store is operand 2");
        Assert.IsTrue(ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetSkill, (Local, 3), (Triggerer, 0), (Local, 0)))), ScrAdmissionSet.S11).IsAdmitted,
            "a computed skill number is admitted");
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(Program(Do(GetSkill(3, Triggerer, 0))), ScrAdmissionSet.S10).Refusal!.Code);
    }

    [TestMethod]
    public void A_set_with_get_skill_needs_a_host_that_answers_it()
    {
        var plain = new FakeHost();
        plain.Attach(Npc, Use, 2023, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var withSkill = new ScrRunOptions
        {
            Admission = new("S7-skill", ScrAdmissionSet.S7.Conditions, ScrAdmissionSet.S7.Actions.Append(ScrCatalogue.Action.GetSkill), ScrAdmissionSet.S7.ValueKinds,
                ScrAdmissionSet.S7.FocusKinds),
        };
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(Use, Npc, Pc, null), plain, withSkill));
        var useOnly = new ScrRunOptions
        {
            Admission = new("S7-use", ScrAdmissionSet.S7.Conditions, ScrAdmissionSet.S7.Actions.Append(ScrCatalogue.Action.UseItemWithSkill), ScrAdmissionSet.S7.ValueKinds,
                ScrAdmissionSet.S7.FocusKinds),
        };
        Assert.AreEqual(ScrStop.ReturnRunDefault, ScrDispatcher.RunObject(new(Use, Npc, Pc, null), plain, useOnly).Stop, "use item with skill asks the host nothing");
        Assert.IsTrue(ScrAdmissionSet.S10.IsSubsetOf(ScrAdmissionSet.S11));
    }

    [TestMethod]
    public void Use_item_with_skill_hands_its_five_operands_to_the_host_and_nothing_without_a_source_or_a_target()
    {
        var host = new SkillHost(new FakeHost());
        host.Inner.Attach(Npc, Use, 2023, Program(
            // source, item, target, skill, modifier: the PC picks the NPC's lock with no item and a modifier of −20.
            Do(C(ScrCatalogue.Action.UseItemWithSkill, (Triggerer, 0), (Extra, 0), (Attachee, 0), N(14), N(-20))),
            Do(C(ScrCatalogue.Action.UseItemWithSkill, (Triggerer, 0), (Attachee, 0), (Extra, 0), N(10), N(0))),
            Do(C(ScrCatalogue.Action.UseItemWithSkill, (Extra, 0), (Attachee, 0), (Triggerer, 0), N(10), N(0))),
            Do(C(ScrCatalogue.Action.UseItemWithSkill, (Attachee, 0), (Triggerer, 0), (Triggerer, 0), (Local, 4), N(99))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        var run = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, S11);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, "the run never waits for the use");
        CollectionAssert.AreEqual(new[] { new ScrSkillUse(Pc, null, Npc, 14, -20), new ScrSkillUse(Npc, Pc, Pc, 0, 99) }, run.Effects.SkillUses.ToArray(),
            "an absent target (line 1) or source (line 2) asks for nothing; an unchecked skill number goes on as read (a fresh local is 0)");
        Assert.AreEqual(0, host.Asked.Count);
        Assert.IsFalse(run.Effects.IsEmpty);
    }
}
