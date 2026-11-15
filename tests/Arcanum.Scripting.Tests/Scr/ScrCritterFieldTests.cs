using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// ARC-SCRIPT-1772 (ledger CQ1–CQ8): the critter getters get social class (38), get origin (39) and get faction (89), toggle invulnerability (57) and rename (92) of the set S56 (exe 0x447220 cases 0x26, 0x27, 0x59, 0x39 and 0x5C): a getter stores the host's
/// field of the first object into its number operand, the two writes queue object-state requests and a later condition of the same run reads their effect. Programs, objects and answers are invented; the host answers from tables.
/// </summary>
[TestClass]
public sealed class ScrCritterFieldTests
{
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    private static readonly ScrObjectRef Sword = new(1, "kit:3"), Victim = new(1, "kit:4");
    private static readonly int[] Getters = [ScrCatalogue.Action.GetSocialClass, ScrCatalogue.Action.GetOrigin, ScrCatalogue.Action.GetFaction];

    private sealed class Host(FakeHost inner) : IScrHost, IScrCritterFieldQueries, IScrCritterStateQueries, IScrObjectQueries, IScrGroupQueries
    {
        /// <summary>What the host answers per action type and object; a missing entry is a null answer (an object it does not know).</summary>
        public Dictionary<(int Type, ScrObjectRef Object), int> Fields { get; } = new();
        public Dictionary<ScrObjectRef, bool> Invulnerable { get; } = new();
        public Dictionary<ScrObjectRef, int> Names { get; } = new();
        public Dictionary<ScrObjectRef, int> Armor { get; } = new();
        public Dictionary<ScrObjectRef, bool> Undead { get; } = new();
        public List<ScrObjectRef> Followers { get; } = [];
        public List<(int Type, ScrObjectRef Object)> Asked { get; } = [];

        private int? Field(int type, ScrObjectRef obj)
        {
            Asked.Add((type, obj));
            return Fields.TryGetValue((type, obj), out var value) ? value : null;
        }

        public int? SocialClassOf(ScrObjectRef obj) => Field(ScrCatalogue.Action.GetSocialClass, obj);
        public int? OriginOf(ScrObjectRef obj) => Field(ScrCatalogue.Action.GetOrigin, obj);
        public int? FactionOf(ScrObjectRef obj) => Field(ScrCatalogue.Action.GetFaction, obj);
        public bool? IsInvulnerable(ScrObjectRef obj) => Invulnerable.TryGetValue(obj, out var value) ? value : null;
        public bool? MetPcBefore(ScrObjectRef npc, ScrObjectRef pc) => null;
        public bool? IsInvisible(ScrObjectRef obj) => null;
        public bool? IsSurrendered(ScrObjectRef obj) => null;
        public bool? IsInDialog(ScrObjectRef obj) => null;
        public bool? CanSee(ScrObjectRef observer, ScrObjectRef target) => null;
        public bool? CanHear(ScrObjectRef listener, ScrObjectRef source) => null;
        public bool? IsUndead(ScrObjectRef obj) => Undead.TryGetValue(obj, out var value) ? value : null;
        public int? ArmorOf(ScrObjectRef obj) { Asked.Add((ScrCatalogue.Action.GetArmor, obj)); return Armor.TryGetValue(obj, out var value) ? value : null; }
        public IReadOnlyList<ScrObjectRef> FollowersOf(ScrObjectRef leader) => Followers.ToArray();
        public bool? IsProwling(ScrObjectRef obj) => null;
        public bool? HasBadAssociates(ScrObjectRef obj) => null;
        public bool? IsMonsterOfType(ScrObjectRef obj, int type) => null;
        public int QuestState(ScrObjectRef obj, int quest) => 0;
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => Names.TryGetValue(obj, out var name) ? name : 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => null;
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

    // S5 is the base the other critter tests use; the five commands, set faction (the in-run read-back) and the two conditions that read what the writes change are added by name.
    private static readonly ScrAdmissionSet Set = new("critter-field",
        ScrAdmissionSet.S5.Conditions.Concat([ScrCatalogue.Condition.IsInvulnerable, ScrCatalogue.Condition.IsNamed]),
        ScrAdmissionSet.S5.Actions.Concat(ScrAdmissionSet.CritterFieldActions).Concat([ScrCatalogue.Action.ToggleInvulnerability, ScrCatalogue.Action.Rename, ScrCatalogue.Action.SetFaction]),
        ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);

    // ARC-SCRIPT-2326: get armor (32) and the group kinds in the list condition is undead (38).
    private static readonly ScrAdmissionSet ArmorSet = new("critter-armor", Set.Conditions.Append(ScrCatalogue.Condition.IsUndead), Set.Actions.Concat(ScrAdmissionSet.CritterArtActions), Set.ValueKinds,
        Set.FocusKinds.Concat(ScrAdmissionSet.GroupFocusKinds), counterIndexWraps: true);

    private static Host With(ScrProgram program)
    {
        var inner = new FakeHost();
        inner.Attach(Sword, 7, 700, program);
        return new Host(inner);
    }

    private static ScrRunResult Hit(IScrHost host, ScrObjectRef? extra = null) => ScrDispatcher.RunObject(new(7, Sword, Pc, extra ?? Victim), host, new() { Admission = Set });

    private static ScrProgram Store(int type, byte obj) => Program(Do(C(type, (obj, 0), (Counter, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    // If condition(operands) set global flag 7, else nothing.
    private static ScrProgram Flag(Clause before, int condition, params (byte, int)[] operands) =>
        Program(Do(before), L(C(condition, operands), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    [TestMethod]
    public void Each_getter_stores_the_hosts_field_of_the_first_object_into_its_number_operand_and_goes_on()
    {
        var value = 21;
        foreach (var type in Getters)
        {
            var host = With(Store(type, Extra));
            host.Fields[(type, Victim)] = value;
            var run = Hit(host);
            Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome, $"a{type}");
            Assert.AreEqual((uint)value, run.Effects.Writeback!.Slot.Counters & 0xFF, $"a{type} stores in counter 0");
            CollectionAssert.AreEqual(new[] { (type, Victim) }, host.Asked, $"a{type} asks of the extra object only");
            value += 5;
        }
    }

    [TestMethod]
    public void A_getter_stops_the_run_for_an_absent_object_and_ends_the_dispatch_not_modelled_for_one_the_host_does_not_know()
    {
        foreach (var type in Getters)
        {
            var unknown = Hit(With(Store(type, Extra)));
            Assert.AreEqual(ScrStop.NotModelled, unknown.Stop, $"a{type}: the host answers null");
            Assert.IsTrue(unknown.Effects.IsEmpty, $"a{type}: nothing applied");
            var host = With(Store(type, Extra));
            var absent = ScrDispatcher.RunObject(new(7, Sword, Pc, null), host, new() { Admission = Set });
            Assert.AreEqual((ScrStop.MissingObject, 0), (absent.Stop, absent.StopLine), $"a{type}: an absent object stops the run at the read");
            Assert.AreEqual(0, host.Asked.Count);
            var literal = ScrAdmission.Admit(Program(Do(C(type, (Attachee, 0), N(5)))), Set);
            Assert.AreEqual((ScrRefusalCode.WriteToNumber, 1), (literal.Refusal!.Code, literal.Refusal.Slot), $"a{type}: the store is operand 1");
        }
    }

    [TestMethod]
    public void Toggle_invulnerability_queues_one_request_per_toggle_and_a_later_test_of_the_run_reads_the_flipped_state()
    {
        var toggle = C(ScrCatalogue.Action.ToggleInvulnerability, (Extra, 0));
        foreach (var (before, flipped) in new[] { (false, 1), (true, 0) })
        {
            var host = With(Flag(toggle, ScrCatalogue.Condition.IsInvulnerable, (Extra, 0)));
            host.Invulnerable[Victim] = before;
            var run = Hit(host);
            CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.ToggleInvulnerability, Victim) }, run.Effects.ObjectStates.ToArray());
            Assert.AreEqual(flipped, run.Effects.GlobalFlags.Count, $"the flag was {before}, the toggle flips it, the test reads it");
        }
        // Two toggles of the same object end where they began; the two requests apply in order.
        var twice = With(Program(Do(toggle), Do(toggle), L(C(ScrCatalogue.Condition.IsInvulnerable, (Extra, 0)), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        twice.Invulnerable[Victim] = true;
        var again = Hit(twice);
        Assert.AreEqual(2, again.Effects.ObjectStates.Count);
        Assert.AreEqual(1, again.Effects.GlobalFlags.Count, "flipped twice, still invulnerable");
    }

    [TestMethod]
    public void Toggle_invulnerability_of_an_absent_object_does_nothing()
    {
        var host = With(Program(Do(C(ScrCatalogue.Action.ToggleInvulnerability, (Extra, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var run = ScrDispatcher.RunObject(new(7, Sword, Pc, null), host, new() { Admission = Set });
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        Assert.AreEqual(0, run.Effects.ObjectStates.Count);
    }

    [TestMethod]
    public void Rename_queues_the_name_number_of_the_first_object_and_a_later_is_named_of_the_run_reads_it()
    {
        var rename = C(ScrCatalogue.Action.Rename, (Extra, 0), N(6409));
        var host = With(Flag(rename, ScrCatalogue.Condition.IsNamed, (Extra, 0), N(6409)));
        host.Names[Victim] = 1;
        var run = Hit(host);
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.Rename, Victim, 6409) }, run.Effects.ObjectStates.ToArray());
        Assert.AreEqual(1, run.Effects.GlobalFlags.Count, "the object now answers its new name number");
        var other = With(Flag(rename, ScrCatalogue.Condition.IsNamed, (Extra, 0), N(1)));
        other.Names[Victim] = 1;
        Assert.AreEqual(0, Hit(other).Effects.GlobalFlags.Count, "and no longer the old one");
        var unchanged = With(Flag(C(0), ScrCatalogue.Condition.IsNamed, (Extra, 0), N(1)));
        unchanged.Names[Victim] = 1;
        Assert.AreEqual(1, Hit(unchanged).Effects.GlobalFlags.Count, "without a rename the host's number stands");
    }

    [TestMethod]
    public void Rename_of_an_absent_object_does_nothing_and_a_variable_name_is_read_once()
    {
        var host = With(Program(Do(C(ScrCatalogue.Action.Rename, (Extra, 0), N(5))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var absent = ScrDispatcher.RunObject(new(7, Sword, Pc, null), host, new() { Admission = Set });
        Assert.AreEqual(0, absent.Effects.ObjectStates.Count);
        var variable = With(Program(Do(C(ScrCatalogue.Action.Assign, (Counter, 1), N(77))), Do(C(ScrCatalogue.Action.Rename, (Extra, 0), (Counter, 1))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.Rename, Victim, 77) }, Hit(variable).Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void A_faction_the_run_itself_set_is_not_read_back_and_ends_the_dispatch_not_modelled()
    {
        var host = With(Program(Do(C(ScrCatalogue.Action.SetFaction, (Extra, 0), N(4))), Do(C(ScrCatalogue.Action.GetFaction, (Extra, 0), (Counter, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        host.Fields[(ScrCatalogue.Action.GetFaction, Victim)] = 9;
        var run = Hit(host);
        Assert.AreEqual(ScrStop.NotModelled, run.Stop, "the host applies the set after the run, so the getter cannot know it");
        Assert.IsTrue(run.Effects.IsEmpty);
        var origin = With(Program(Do(C(ScrCatalogue.Action.SetFaction, (Extra, 0), N(4))), Do(C(ScrCatalogue.Action.GetOrigin, (Extra, 0), (Counter, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        origin.Fields[(ScrCatalogue.Action.GetOrigin, Victim)] = 9;
        Assert.AreEqual(ScrOutcome.RunDefault, Hit(origin).Outcome, "an origin is not changed by a faction");
    }

    [TestMethod]
    public void The_commands_need_a_host_that_answers_them_and_S56_adds_exactly_the_five_actions_to_S55()
    {
        var plain = new FakeHost();
        plain.Attach(Sword, 7, 700, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var getters = new ScrAdmissionSet("getters-only", ScrAdmissionSet.S5.Conditions, ScrAdmissionSet.S5.Actions.Concat(ScrAdmissionSet.CritterFieldActions), ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), plain, new() { Admission = getters }));
        CollectionAssert.AreEquivalent(new[] { 38, 39, 89 }, ScrAdmissionSet.CritterFieldActions.ToArray());
        CollectionAssert.AreEquivalent(new[] { 38, 39, 57, 89, 92 }, ScrAdmissionSet.S56.Actions.Except(ScrAdmissionSet.S55.Actions).ToArray());
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S55.Conditions.ToArray(), ScrAdmissionSet.S56.Conditions.ToArray());
        Assert.IsTrue(ScrAdmissionSet.S55.IsSubsetOf(ScrAdmissionSet.S56));
        Assert.IsTrue(ScrAdmissionSet.S56.IsSubsetOf(ScrAdmissionSet.S56));
        Assert.IsNotNull(new ScrRunOptions { Admission = ScrAdmissionSet.S56 });
    }

    [TestMethod]
    public void Get_armor_stores_the_armour_part_of_the_first_object_and_stops_like_the_other_getters()
    {
        var host = With(Store(ScrCatalogue.Action.GetArmor, Extra));
        host.Armor[Victim] = 4;
        var run = ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), host, new() { Admission = ArmorSet });
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        Assert.AreEqual(4u, run.Effects.Writeback!.Slot.Counters & 0xFF);
        CollectionAssert.AreEqual(new[] { (ScrCatalogue.Action.GetArmor, Victim) }, host.Asked);

        var unknown = ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), With(Store(ScrCatalogue.Action.GetArmor, Extra)), new() { Admission = ArmorSet });
        Assert.AreEqual(ScrStop.NotModelled, unknown.Stop);
        var absent = ScrDispatcher.RunObject(new(7, Sword, Pc, null), With(Store(ScrCatalogue.Action.GetArmor, Extra)), new() { Admission = ArmorSet });
        Assert.AreEqual((ScrStop.MissingObject, 0), (absent.Stop, absent.StopLine));
        var literal = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetArmor, (Attachee, 0), N(5)))), ArmorSet);
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 1), (literal.Refusal!.Code, literal.Refusal.Slot));
        Assert.IsFalse(ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetArmor, (Attachee, 0), (Counter, 0)))), Set).IsAdmitted, "the set before S62 refuses it");
        Assert.IsTrue(ScrAdmissionSet.S61.IsSubsetOf(ScrAdmissionSet.S62) && !ScrAdmissionSet.S62.IsSubsetOf(ScrAdmissionSet.S61));
    }

    [TestMethod]
    public void Get_armor_of_a_group_kind_reads_the_first_follower_or_the_triggerer()
    {
        var follower = new ScrObjectRef(1, "follower:0");
        var host = With(Store(ScrCatalogue.Action.GetArmor, 14));
        host.Followers.Add(follower);
        host.Armor[follower] = 6; host.Armor[Pc] = 3;
        Assert.AreEqual(6u, ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), host, new() { Admission = ArmorSet }).Effects.Writeback!.Slot.Counters & 0xFF, "the first follower");
        var alone = With(Store(ScrCatalogue.Action.GetArmor, 13));
        alone.Armor[Pc] = 3;
        Assert.AreEqual(3u, ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), alone, new() { Admission = ArmorSet }).Effects.Writeback!.Slot.Counters & 0xFF, "no followers: the triggerer");
    }

    [TestMethod]
    public void A_list_condition_takes_a_group_kind_by_the_list_rule_with_the_triggerer_last()
    {
        var follower = new ScrObjectRef(1, "follower:0");
        ScrRunResult Run(byte kind, bool followerUndead, bool triggererUndead, bool noFollower = false)
        {
            var host = With(Flag(C(0), ScrCatalogue.Condition.IsUndead, (kind, 0)));
            if (!noFollower) { host.Followers.Add(follower); host.Undead[follower] = followerUndead; }
            host.Undead[Pc] = triggererUndead;
            return ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), host, new() { Admission = ArmorSet });
        }
        Assert.AreEqual(1, Run(14, true, false).Effects.GlobalFlags.Count, "kind 14: one undead follower is enough");
        Assert.AreEqual(0, Run(14, false, false).Effects.GlobalFlags.Count, "kind 14: none");
        Assert.AreEqual(1, Run(14, false, true).Effects.GlobalFlags.Count, "kind 14: the triggerer is in the list");
        Assert.AreEqual(0, Run(13, true, false).Effects.GlobalFlags.Count, "kind 13: the triggerer is not undead");
        Assert.AreEqual(1, Run(13, true, true).Effects.GlobalFlags.Count, "kind 13: all of them");
        Assert.AreEqual(1, Run(13, false, true, noFollower: true).Effects.GlobalFlags.Count, "kind 13: the triggerer alone");
        var oneObject = ScrAdmission.Admit(Program(L(C(ScrCatalogue.Condition.MetPcBefore, (14, 0), (Triggerer, 0)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))),
            new("group-first-object", ScrAdmissionSet.S5.Conditions.Append(ScrCatalogue.Condition.MetPcBefore), ScrAdmissionSet.S5.Actions, ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds.Concat(ScrAdmissionSet.GroupFocusKinds)));
        Assert.AreEqual(ScrRefusalCode.FocusKindNotAdmitted, oneObject.Refusal!.Code, "a first-object condition keeps the group kinds out");
    }
}
