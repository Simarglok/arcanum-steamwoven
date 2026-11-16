using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// ARC-SCRIPT-2342 (AC3, ledger AR4 and AR8): the table test of <see cref="ScrAdmissionSet.ListConditions"/>. Every member must be evaluated through the one list rule (<c>ListHolds</c>, the exe's list tail at 0x4455BD): over the group of the
/// triggerer's followers with the triggerer last, kind 13 (every) holds only when every object answers true and kind 14 (any) when one does. The host answers true for the objects it is told hold, false for the others, for every question the
/// 21 conditions ask, so a condition that read only the first object, or only the triggerer, or counted differently would fail one of the four cases. Programs, objects and answers are invented.
/// </summary>
[TestClass]
public sealed class ScrListConditionTableTests
{
    private const byte EveryGroup = 13, AnyGroup = 14;
    private static readonly ScrObjectRef Sword = new(1, "kit:3"), Victim = new(1, "kit:4"), Follower = new(1, "follower:0");

    /// <summary>A host whose every list-condition question answers true for the objects in <see cref="Good"/> and false for the rest.</summary>
    private sealed class Host(FakeHost inner) : IScrHost, IScrGroupQueries, IScrCritterQueries, IScrCritterStateQueries, IScrObjectStateQueries, IScrSpellQueries, IScrStoryQueries
    {
        public HashSet<ScrObjectRef> Good { get; } = [];
        private bool Is(ScrObjectRef obj) => Good.Contains(obj);

        public IReadOnlyList<ScrObjectRef> FollowersOf(ScrObjectRef leader) => [Follower];
        public bool? IsAnimal(ScrObjectRef obj) => Is(obj);
        public bool? IsInCombat(ScrObjectRef obj) => Is(obj);
        public bool? MetPcBefore(ScrObjectRef npc, ScrObjectRef pc) => null;
        public bool? IsInvisible(ScrObjectRef obj) => Is(obj);
        public bool? IsSurrendered(ScrObjectRef obj) => Is(obj);
        public bool? IsInDialog(ScrObjectRef obj) => Is(obj);
        public bool? CanSee(ScrObjectRef observer, ScrObjectRef target) => null;
        public bool? CanHear(ScrObjectRef listener, ScrObjectRef source) => null;
        public bool? IsInvulnerable(ScrObjectRef obj) => Is(obj);
        public bool? IsUndead(ScrObjectRef obj) => Is(obj);
        public bool? IsProwling(ScrObjectRef obj) => Is(obj);
        public bool? HasBadAssociates(ScrObjectRef obj) => Is(obj);
        public bool? IsMonsterOfType(ScrObjectRef obj, int type) => Is(obj);
        public bool? IsDead(ScrObjectRef obj) => Is(obj);
        public bool? IsSwitchedOff(ScrObjectRef obj) => Is(obj);
        public int? SpellFlags(ScrObjectRef obj) => Is(obj) ? -1 : 0;
        public bool? IsInfluencedBy(ScrObjectRef obj, int spell) => Is(obj);
        public bool? KnowsSpell(ScrObjectRef obj, int spell) => Is(obj);
        public int? MasteredCollege(ScrObjectRef obj) => Is(obj) ? 1 : -2;
        public ScrGameTime? Time() => null;
        public int GlobalQuestState(int quest) => 0;
        public bool? RumorKnown(ScrObjectRef obj, int rumor) => false;
        public bool RumorQuelled(int rumor) => false;
        public bool? HasBlessing(ScrObjectRef obj, int number, bool curse) => Is(obj);

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

    private static ScrAdmissionSet SetOf(int condition) => new("list-condition",
        ScrAdmissionSet.S5.Conditions.Append(condition), ScrAdmissionSet.S5.Actions, ScrAdmissionSet.S5.ValueKinds,
        ScrAdmissionSet.S5.FocusKinds.Concat([ScrFocusKind.EveryoneInGroup, ScrFocusKind.AnyoneInGroup]));

    // True when "if condition(group of the kind, 1) set global flag 7" set the flag; the group is the triggerer's follower, then the triggerer (the PC).
    private static bool Holds(int condition, byte kind, params ScrObjectRef[] good)
    {
        var inner = new FakeHost();
        inner.Attach(Sword, 7, 700, Program(L(C(condition, (kind, 0), N(1)), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var host = new Host(inner);
        foreach (var obj in good) host.Good.Add(obj);
        var run = ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), host, new() { Admission = SetOf(condition) });
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome, $"condition {condition} kind {kind}: the run ends whole");
        return run.Effects.GlobalFlags.Count == 1;
    }

    [TestMethod]
    public void Every_list_condition_is_evaluated_through_the_list_rule_of_the_group_kinds()
    {
        Assert.AreEqual(21, ScrAdmissionSet.ListConditions.Count, "the table covers the members the set names; a new member is a new row here");
        foreach (var condition in ScrAdmissionSet.ListConditions.Order())
        {
            // The five guard conditions are the other list members of GF9 (ScrAdmissionSet.GuardConditions) and have their own tests; none is in this table.
            Assert.IsFalse(ScrAdmissionSet.GuardConditions.Contains(condition), $"c{condition} is a guard condition");
            Assert.IsTrue(Holds(condition, AnyGroup, Follower), $"c{condition}: kind 14 is true when the follower alone answers true");
            Assert.IsTrue(Holds(condition, AnyGroup, Pc), $"c{condition}: kind 14 is true when the triggerer, last in the list, alone answers true");
            Assert.IsFalse(Holds(condition, AnyGroup), $"c{condition}: kind 14 is false when none answers true");
            Assert.IsFalse(Holds(condition, EveryGroup, Follower), $"c{condition}: kind 13 is false when the triggerer does not answer true");
            Assert.IsFalse(Holds(condition, EveryGroup, Pc), $"c{condition}: kind 13 is false when the follower does not answer true");
            Assert.IsTrue(Holds(condition, EveryGroup, Follower, Pc), $"c{condition}: kind 13 is true when both answer true");
        }
    }
}
