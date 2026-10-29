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
        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => [];
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
        // A host without the state questions cannot run a set with the conditions.
        var plain = new FakeHost();
        plain.Attach(Npc, ScrPoint.Heartbeat, Script, program);
        Assert.Throws<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), plain, Options));
    }
}
