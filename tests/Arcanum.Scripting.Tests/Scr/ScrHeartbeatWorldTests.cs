using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The heartbeat world commands of set S18 (ARC-SCRIPT-049, ledger HW1–HW4): the conditions obj is dead (22) and obj is switched
/// off (27), distance between (71) and set faction (134), on synthetic programs of the installed heartbeats' shapes (audit slice 1).</summary>
[TestClass]
public sealed class ScrHeartbeatWorldTests
{
    private const byte Triggerer = 0, Attachee = 1, Player = 21;
    private const int Script = 2700;

    // S2 with single-object focus plus the four commands: the viewer's heartbeat set has this shape.
    private static readonly ScrAdmissionSet Set = new("test-heartbeat-world",
        ScrAdmissionSet.S2.Conditions.Concat([ScrCatalogue.Condition.IsDead, ScrCatalogue.Condition.IsSwitchedOff]),
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.DistanceBetween, ScrCatalogue.Action.SetFaction]), ScrAdmissionSet.S2.ValueKinds,
        ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private sealed class WorldHost(FakeHost inner) : IScrHost, IScrObjectStateQueries, IScrWorldQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<ScrObjectRef, bool> Dead { get; } = new();
        public Dictionary<ScrObjectRef, bool> Off { get; } = new();
        public Dictionary<ScrObjectRef, (long X, long Y)> Tiles { get; } = new();
        public List<ScrObjectRef> Scenery { get; } = new();

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
        public bool? IsDead(ScrObjectRef obj) => Dead.TryGetValue(obj, out var dead) ? dead : null;
        public bool? IsSwitchedOff(ScrObjectRef obj) => Off.TryGetValue(obj, out var off) ? off : null;
        public (long X, long Y)? Location(ScrObjectRef obj) => Tiles.TryGetValue(obj, out var tile) ? tile : null;
        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => Scenery.ToArray();
        public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre) => [];
        public int Random(int low, int high) => low;
    }

    private static WorldHost Host(ScrProgram program)
    {
        var host = new WorldHost(new FakeHost());
        host.Inner.Attach(Npc, ScrPoint.Heartbeat, Script, program);
        return host;
    }

    private static ScrRunResult Beat(WorldHost host, ScrObjectRef? triggerer = null) =>
        ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, triggerer ?? Npc, null), host, Options);

    // The condition's true branch skips the default, its false branch runs it.
    private static ScrProgram Test(int condition, byte focus) => Program(
        L(C(condition, (focus, focus == Attachee ? -1 : 0)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));

    [TestMethod]
    public void Dead_and_switched_off_read_the_hosts_answer_for_the_named_object()
    {
        foreach (var condition in new[] { ScrCatalogue.Condition.IsDead, ScrCatalogue.Condition.IsSwitchedOff })
            foreach (var value in new[] { true, false })
            {
                var host = Host(Test(condition, Attachee));
                (condition == ScrCatalogue.Condition.IsDead ? host.Dead : host.Off)[Npc] = value;
                Assert.AreEqual(value ? ScrOutcome.SkipDefault : ScrOutcome.RunDefault, Beat(host).Outcome, $"condition {condition}, {value}");
            }
    }

    [TestMethod]
    public void An_absent_object_fails_the_condition_and_an_unknown_one_stops_the_run()
    {
        // SG6: an empty list is false (the run has no triggerer).
        var host = Host(Test(ScrCatalogue.Condition.IsDead, Triggerer));
        Assert.AreEqual(ScrOutcome.RunDefault, ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, null, null), host, Options).Outcome);
        // The host does not know the attachee: the run stops as for an absent object.
        var unknown = Beat(Host(Test(ScrCatalogue.Condition.IsSwitchedOff, Attachee)));
        Assert.AreEqual(ScrStop.MissingObject, unknown.Stop);
    }

    [TestMethod]
    public void Distance_between_stores_the_larger_axis_difference()
    {
        // 2700's shape: the distance from the attachee to the player into a variable, then a test of it.
        var program = Program(
            Do(C(ScrCatalogue.Action.DistanceBetween, (Attachee, -1), (Player, 0), (Local, 0))),
            L(C(ScrCatalogue.Condition.LessOrEqual, (Local, 0), N(7)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var host = Host(program);
        host.Tiles[Npc] = (100, 200);
        host.Tiles[Pc] = (93, 203);
        Assert.AreEqual(ScrOutcome.SkipDefault, Beat(host).Outcome, "max(7, 3) = 7");
        host.Tiles[Pc] = (100, 208);
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(host).Outcome, "max(0, 8) = 8");
        host.Tiles.Remove(Pc);
        Assert.AreEqual(ScrStop.MissingObject, Beat(host).Stop, "an object without a place stops the run");
    }

    [TestMethod]
    public void Distance_between_into_a_literal_is_refused()
    {
        var program = Program(Do(C(ScrCatalogue.Action.DistanceBetween, (Attachee, -1), (Player, 0), N(3))));
        var refusal = ScrAdmission.Admit(program, Set).Refusal!;
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 2), (refusal.Code, refusal.Slot));
    }

    private static readonly ScrAdmissionSet LocationSet = new("test-heartbeat-location", Set.Conditions, Set.Actions.Append(ScrCatalogue.Action.GetLocation), Set.ValueKinds, Set.FocusKinds);

    [TestMethod]
    public void Get_location_stores_the_first_objects_tile_x_and_y_into_operands_1_and_2()
    {
        // ARC-SCRIPT-1359, GL1: 1717's shape reads where the attachee stands into two variables; each is tested here by its own program.
        ScrOutcome Outcome(int slot, int expected)
        {
            var program = Program(
                Do(C(ScrCatalogue.Action.GetLocation, (Attachee, -1), (Local, 0), (Local, 1))),
                L(C(ScrCatalogue.Condition.Equal, (Local, slot), N(expected)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
            var host = Host(program);
            host.Tiles[Npc] = (480, 515);
            return ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), host, new() { Admission = LocationSet }).Outcome;
        }
        Assert.AreEqual(ScrOutcome.SkipDefault, Outcome(0, 480), "x into operand 1");
        Assert.AreEqual(ScrOutcome.SkipDefault, Outcome(1, 515), "y into operand 2");
        Assert.AreEqual(ScrOutcome.RunDefault, Outcome(0, 515), "the two are not swapped");
    }

    [TestMethod]
    public void Get_location_of_an_absent_object_or_one_without_a_place_stops_the_run_and_a_literal_target_is_refused()
    {
        var program = Program(Do(C(ScrCatalogue.Action.GetLocation, (Attachee, -1), (Local, 0), (Local, 1))));
        var host = Host(program);
        Assert.AreEqual(ScrStop.MissingObject, ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), host, new() { Admission = LocationSet }).Stop, "no place");
        var absent = Host(Program(Do(C(ScrCatalogue.Action.GetLocation, (Triggerer, 0), (Local, 0), (Local, 1)))));
        Assert.AreEqual(ScrStop.MissingObject, ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, null, null), absent, new() { Admission = LocationSet }).Stop, "no triggerer");
        foreach (var slot in new[] { 1, 2 })
        {
            var refused = Program(Do(C(ScrCatalogue.Action.GetLocation, (Attachee, -1), slot == 1 ? N(3) : (Local, 0), slot == 2 ? N(3) : (Local, 1))));
            var refusal = ScrAdmission.Admit(refused, LocationSet).Refusal!;
            Assert.AreEqual((ScrRefusalCode.WriteToNumber, slot), (refusal.Code, refusal.Slot), $"literal in operand {slot}");
        }
    }

    [TestMethod]
    public void S38_adds_only_get_location_over_S37_and_needs_a_host_that_answers_the_world_questions()
    {
        CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.GetLocation }, ScrAdmissionSet.S38.Actions.Except(ScrAdmissionSet.S37.Actions).ToArray());
        Assert.IsTrue(ScrAdmissionSet.S38.Conditions.SetEquals(ScrAdmissionSet.S37.Conditions) && ScrAdmissionSet.S37.IsSubsetOf(ScrAdmissionSet.S38));
        var program = Program(Do(C(ScrCatalogue.Action.GetLocation, (Attachee, -1), (Local, 0), (Local, 1))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S38).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S37).Refusal!.Code);
        // A host that cannot answer the world questions cannot run a set that holds the action.
        var plain = new FakeHost();
        plain.Attach(Npc, ScrPoint.Heartbeat, Script, program);
        Assert.Throws<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), plain, new() { Admission = LocationSet }));
    }

    [TestMethod]
    public void Set_faction_asks_for_the_first_object_and_an_absent_object_asks_nothing()
    {
        // 2756's shape: the attachee joins faction 11.
        var program = Program(
            Do(C(ScrCatalogue.Action.SetFaction, (Triggerer, 0), N(4))),
            Do(C(ScrCatalogue.Action.SetFaction, (Attachee, -1), N(11))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)));
        var run = ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, null, null), Host(program), Options);
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.SetFaction, Npc, 11) }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void S18_admits_the_four_commands_and_needs_a_host_that_answers_them()
    {
        var program = Program(
            L(C(ScrCatalogue.Condition.IsDead, (Attachee, -1)), C(ScrCatalogue.Action.DoNothing), C(ScrCatalogue.Action.DoNothing)),
            L(C(ScrCatalogue.Condition.IsSwitchedOff, (Attachee, -1)), C(ScrCatalogue.Action.DoNothing), C(ScrCatalogue.Action.DoNothing)),
            Do(C(ScrCatalogue.Action.DistanceBetween, (Attachee, -1), (Player, 0), (Local, 0))),
            Do(C(ScrCatalogue.Action.SetFaction, (Attachee, -1), N(11))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S18).IsAdmitted);
        Assert.IsFalse(ScrAdmission.Admit(program, ScrAdmissionSet.S17).IsAdmitted);
        Assert.IsTrue(ScrAdmissionSet.S17.IsSubsetOf(ScrAdmissionSet.S18) && Set.IsSubsetOf(ScrAdmissionSet.S18));
        // A host without the state questions cannot run a set with the conditions (no other question in the set).
        var conditions = new ScrAdmissionSet("test-conditions", ScrAdmissionSet.S2.Conditions.Concat([ScrCatalogue.Condition.IsDead, ScrCatalogue.Condition.IsSwitchedOff]),
            ScrAdmissionSet.S2.Actions, ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
        var plain = new FakeHost();
        plain.Attach(Npc, ScrPoint.Heartbeat, Script, Test(ScrCatalogue.Condition.IsDead, Attachee));
        var refused = Assert.Throws<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), plain, new() { Admission = conditions }));
        StringAssert.Contains(refused.Message, nameof(IScrObjectStateQueries));
    }

    [TestMethod]
    public void A_toggle_or_kill_earlier_in_the_run_counts_for_a_later_test()
    {
        // The original flips the flag and kills at once: toggled twice is on again, a kill is dead.
        foreach (var (action, condition, toggles, held) in new[]
        {
            (ScrCatalogue.Action.ToggleState, ScrCatalogue.Condition.IsSwitchedOff, 1, true), (ScrCatalogue.Action.ToggleState, ScrCatalogue.Condition.IsSwitchedOff, 2, false),
            (ScrCatalogue.Action.Kill, ScrCatalogue.Condition.IsDead, 1, true),
        })
        {
            var lines = Enumerable.Repeat(Do(C(action, (Attachee, -1))), toggles)
                .Append(L(C(condition, (Attachee, -1)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))).ToArray();
            var host = Host(Program(lines));
            host.Dead[Npc] = false; host.Off[Npc] = false;
            var set = new ScrAdmissionSet("test-order", Set.Conditions, Set.Actions.Concat([ScrCatalogue.Action.ToggleState, ScrCatalogue.Action.Kill]), Set.ValueKinds, Set.FocusKinds);
            var run = ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), host, new() { Admission = set });
            Assert.AreEqual(held ? ScrOutcome.SkipDefault : ScrOutcome.RunDefault, run.Outcome, $"action {action} × {toggles}");
        }
    }

    [TestMethod]
    public void A_condition_over_the_scenery_list_holds_only_when_every_object_does()
    {
        // SG6 for an "every" kind (15): all switched off is true; some, or an empty list, is false.
        var set = new ScrAdmissionSet("test-scenery", Set.Conditions, Set.Actions, Set.ValueKinds, Set.FocusKinds.Append(ScrFocusKind.EverySceneryInVicinity));
        ScrObjectRef tree = new(1, "sector:7:1"), rock = new(1, "sector:7:2");
        foreach (var (offs, held) in new[] { (new[] { true, true }, true), (new[] { true, false }, false), (Array.Empty<bool>(), false) })
        {
            var host = Host(Test(ScrCatalogue.Condition.IsSwitchedOff, (byte)ScrFocusKind.EverySceneryInVicinity));
            foreach (var (obj, off) in new[] { tree, rock }.Zip(offs)) { host.Scenery.Add(obj); host.Off[obj] = off; }
            var run = ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), host, new() { Admission = set });
            Assert.AreEqual(held ? ScrOutcome.SkipDefault : ScrOutcome.RunDefault, run.Outcome, string.Join(",", offs));
        }
    }

    [TestMethod]
    public void A_condition_over_the_any_scenery_list_holds_when_one_object_does()
    {
        // ARC-SCRIPT-2321 (AS1, SG6): kind 16 builds the same list as 15 and its condition tail is an "any" kind: some switched off is true, none or an empty list false.
        var set = new ScrAdmissionSet("test-any-scenery", Set.Conditions, Set.Actions, Set.ValueKinds, Set.FocusKinds.Append(ScrFocusKind.AnySceneryInVicinity));
        ScrObjectRef tree = new(1, "sector:7:1"), rock = new(1, "sector:7:2");
        foreach (var (offs, held) in new[] { (new[] { true, true }, true), (new[] { true, false }, true), (new[] { false, false }, false), (Array.Empty<bool>(), false) })
        {
            var host = Host(Test(ScrCatalogue.Condition.IsSwitchedOff, (byte)ScrFocusKind.AnySceneryInVicinity));
            foreach (var (obj, off) in new[] { tree, rock }.Zip(offs)) { host.Scenery.Add(obj); host.Off[obj] = off; }
            var run = ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), host, new() { Admission = set });
            Assert.AreEqual(held ? ScrOutcome.SkipDefault : ScrOutcome.RunDefault, run.Outcome, string.Join(",", offs));
        }
    }
}
