using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1164 (E5-b, ledger WO1–WO10): the world-object conditions and actions of set S37 on synthetic programs: create item (66), transfer item (41), set lock state (53), toggle open or closed (88) with obj is open (36),
/// toggle sector blocked (106) with sector is blocked (45), toggle monster generator (110) with monster generator disabled (46), set written start (94), touch art (123), destroy item named (126) and toggle item inventory display (127).
/// The meaning of each request (the session's lock, frame, sector and generator state, the inventories) is the host's; these tests pin what the interpreter asks, does with the answers and reads back.</summary>
[TestClass]
public sealed class ScrWorldObjectTests
{
    private const byte TriggererKind = 0, AttacheeKind = 1;
    private const int Script = 2830;
    private static readonly ScrAdmissionSet Set = new("test-world-object", ScrAdmissionSet.S2.Conditions.Concat(ScrAdmissionSet.WorldObjectConditions),
        ScrAdmissionSet.S2.Actions.Concat(ScrAdmissionSet.WorldObjectActions), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds, counterIndexWraps: true);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private sealed class WorldHost(FakeHost inner) : IScrHost, IScrWorldObjectQueries
    {
        public FakeHost Inner { get; } = inner;
        public HashSet<ScrObjectRef> Open { get; } = new();
        public HashSet<ScrObjectRef> Openable { get; } = new();
        public HashSet<ScrObjectRef> Unknown { get; } = new();
        public HashSet<(int, int)> Sectors { get; } = new();
        public HashSet<int> Generators { get; } = new();

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

        public bool? IsOpen(ScrObjectRef obj) => Unknown.Contains(obj) ? null : Open.Contains(obj);
        public bool? CanOpen(ScrObjectRef obj) => Unknown.Contains(obj) ? null : Openable.Contains(obj);
        public bool SectorBlocked(int x, int y) => Sectors.Contains((x, y));
        public bool GeneratorBlocked(int slot) => Generators.Contains(slot);
    }

    private static WorldHost Host(ScrProgram program)
    {
        var host = new WorldHost(new FakeHost());
        host.Inner.Attach(Npc, ScrPoint.Heartbeat, Script, program);
        return host;
    }

    private static ScrRunResult Beat(WorldHost host) => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), host, Options);

    private static ScrProgram Test(int condition, params (byte, int)[] operands) => Program(
        L(C(condition, operands), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));

    private static ScrOutcome Holds(bool value) => value ? ScrOutcome.SkipDefault : ScrOutcome.RunDefault;

    [TestMethod]
    public void The_set_adds_exactly_the_three_conditions_and_ten_actions_to_the_npc_state_set()
    {
        CollectionAssert.AreEquivalent(new[] { 36, 45, 46 }, ScrAdmissionSet.S37.Conditions.Except(ScrAdmissionSet.S36.Conditions).ToArray());
        CollectionAssert.AreEquivalent(new[] { 41, 53, 66, 88, 94, 106, 110, 123, 126, 127 }, ScrAdmissionSet.S37.Actions.Except(ScrAdmissionSet.S36.Actions).ToArray());
        Assert.IsTrue(ScrAdmissionSet.S36.IsSubsetOf(ScrAdmissionSet.S37));
        Assert.IsTrue(ScrAdmissionSet.WorldObjectHostActions.All(ScrAdmissionSet.WorldObjectActions.Contains));
    }

    [TestMethod]
    public void A_set_with_a_world_object_command_needs_a_host_that_answers_the_queries()
    {
        var plain = new FakeHost();
        plain.Attach(Npc, ScrPoint.Heartbeat, Script, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Pc, null), plain, Options));
    }

    [TestMethod]
    public void Create_item_names_the_prototype_and_the_holder()
    {
        // WO1: operand 0 the prototype number, operand 1 the first object (the holder); an absent object creates nothing.
        var run = Beat(Host(Program(
            Do(C(ScrCatalogue.Action.CreateItem, N(6001), (AttacheeKind, 0))),
            Do(C(ScrCatalogue.Action.CreateItem, N(6002), (TriggererKind, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)))));
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.CreateItem, Npc, 6001),
            new ScrObjectStateRequest(ScrObjectStateChange.CreateItem, Pc, 6002),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void Transfer_item_names_the_item_the_holder_and_the_new_holder()
    {
        var run = Beat(Host(Program(
            Do(C(ScrCatalogue.Action.TransferItem, N(4012), (AttacheeKind, 0), (TriggererKind, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)))));
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.TransferItem, Npc, 4012, Other: Pc) }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void The_item_and_written_start_actions_hand_their_object_and_number()
    {
        var run = Beat(Host(Program(
            Do(C(ScrCatalogue.Action.DestroyItemNamed, N(77), (AttacheeKind, 0))),
            Do(C(ScrCatalogue.Action.ToggleItemInventoryDisplay, (TriggererKind, 0))),
            Do(C(ScrCatalogue.Action.SetWrittenStart, (AttacheeKind, 0), N(9))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)))));
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.DestroyItemNamed, Npc, 77),
            new ScrObjectStateRequest(ScrObjectStateChange.ToggleItemInventoryDisplay, Pc),
            new ScrObjectStateRequest(ScrObjectStateChange.SetWrittenStart, Npc, 9),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void Set_lock_state_hands_the_object_and_the_value()
    {
        // WO3: the value 1 locks, any other unlocks; the host reads it so, the request keeps the number.
        var run = Beat(Host(Program(
            Do(C(ScrCatalogue.Action.SetLockState, (AttacheeKind, 0), N(1))),
            Do(C(ScrCatalogue.Action.SetLockState, (TriggererKind, 0), N(0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault)))));
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.SetLockState, Npc, 1),
            new ScrObjectStateRequest(ScrObjectStateChange.SetLockState, Pc, 0),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void Obj_is_open_asks_the_host_and_an_absent_object_is_not_open()
    {
        var host = Host(Test(ScrCatalogue.Condition.ObjIsOpen, (AttacheeKind, 0)));
        Assert.AreEqual(Holds(false), Beat(host).Outcome);
        host.Open.Add(Npc);
        Assert.AreEqual(Holds(true), Beat(host).Outcome);
        // The attachee is always there; a triggerer role the event leaves out is an absent object.
        var absent = new WorldHost(new FakeHost());
        absent.Inner.Attach(Npc, ScrPoint.Heartbeat, Script, Test(ScrCatalogue.Condition.ObjIsOpen, (TriggererKind, 0)));
        Assert.AreEqual(Holds(false), ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, null, null), absent, Options).Outcome);
        var unknown = Host(Test(ScrCatalogue.Condition.ObjIsOpen, (AttacheeKind, 0)));
        unknown.Unknown.Add(Npc);
        Assert.AreEqual(ScrStop.MissingObject, Beat(unknown).Stop, "an object the host does not know stops the run");
    }

    [TestMethod]
    public void Toggle_open_or_closed_hands_the_object_and_the_run_reads_the_state_it_leaves()
    {
        // WO4: a closed object that can open is open after the toggle; an open one is closed; one that cannot open stays closed. The run's own later test reads it.
        var program = Program(
            Do(C(ScrCatalogue.Action.ToggleOpenClosed, (AttacheeKind, 0))),
            L(C(ScrCatalogue.Condition.ObjIsOpen, (AttacheeKind, 0)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var opens = Host(program);
        opens.Openable.Add(Npc);
        var run = Beat(opens);
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "closed, can open: open after the toggle");
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.ToggleOpenClosed, Npc) }, run.Effects.ObjectStates.ToArray());

        var closes = Host(program);
        closes.Open.Add(Npc); closes.Openable.Add(Npc);
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(closes).Outcome, "open: closed after the toggle");

        var stays = Host(program);
        Assert.AreEqual(ScrOutcome.RunDefault, Beat(stays).Outcome, "closed and cannot open (one frame): still closed");

        var twice = Host(Program(
            Do(C(ScrCatalogue.Action.ToggleOpenClosed, (AttacheeKind, 0))),
            Do(C(ScrCatalogue.Action.ToggleOpenClosed, (AttacheeKind, 0))),
            L(C(ScrCatalogue.Condition.ObjIsOpen, (AttacheeKind, 0)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))));
        twice.Openable.Add(Npc);
        var back = Beat(twice);
        Assert.AreEqual(ScrOutcome.RunDefault, back.Outcome, "open, then closed again");
        Assert.AreEqual(2, back.Effects.ObjectStates.Count);
    }

    [TestMethod]
    public void Toggle_sector_blocked_puts_the_sector_on_the_list_and_takes_it_off_and_the_run_reads_it()
    {
        // WO5: the toggle's request carries the state it ends in; the run's own sector is blocked test reads it back; the second word is y ORed with x's sign word.
        var program = Program(
            Do(C(ScrCatalogue.Action.ToggleSectorBlocked, N(12), N(34))),
            L(C(ScrCatalogue.Condition.SectorIsBlocked, N(12), N(34)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var free = Host(program);
        var run = Beat(free);
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrWorldRequest(ScrWorldChange.SectorBlocked, 12, 34, true) }, run.Effects.WorldChanges.ToArray());

        var blocked = Host(program);
        blocked.Sectors.Add((12, 34));
        run = Beat(blocked);
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrWorldRequest(ScrWorldChange.SectorBlocked, 12, 34, false) }, run.Effects.WorldChanges.ToArray());

        var negative = Host(Program(Do(C(ScrCatalogue.Action.ToggleSectorBlocked, N(-2), N(5))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        CollectionAssert.AreEqual(new[] { new ScrWorldRequest(ScrWorldChange.SectorBlocked, -2, -1, true) }, Beat(negative).Effects.WorldChanges.ToArray());
    }

    [TestMethod]
    public void Sector_is_blocked_reads_the_host_list()
    {
        var host = Host(Test(ScrCatalogue.Condition.SectorIsBlocked, N(3), N(4)));
        Assert.AreEqual(Holds(false), Beat(host).Outcome);
        host.Sectors.Add((3, 4));
        Assert.AreEqual(Holds(true), Beat(host).Outcome);
    }

    [TestMethod]
    public void Toggle_monster_generator_sets_and_clears_the_slot_block_and_ignores_a_slot_outside_the_table()
    {
        // WO9: the toggle's request carries the state it ends in; condition 46 reads the run's own toggle; a slot outside 0..255 does nothing and is never blocked.
        var program = Program(
            Do(C(ScrCatalogue.Action.ToggleMonsterGenerator, N(7))),
            L(C(ScrCatalogue.Condition.MonsterGenDisabled, N(7)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault)));
        var run = Beat(Host(program));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrWorldRequest(ScrWorldChange.GeneratorBlocked, 7, 0, true) }, run.Effects.WorldChanges.ToArray());

        var blocked = Host(program);
        blocked.Generators.Add(7);
        run = Beat(blocked);
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        CollectionAssert.AreEqual(new[] { new ScrWorldRequest(ScrWorldChange.GeneratorBlocked, 7, 0, false) }, run.Effects.WorldChanges.ToArray());

        var outside = Host(Program(
            Do(C(ScrCatalogue.Action.ToggleMonsterGenerator, N(256))),
            Do(C(ScrCatalogue.Action.ToggleMonsterGenerator, N(-1))),
            L(C(ScrCatalogue.Condition.MonsterGenDisabled, N(256)), C(ScrCatalogue.Action.ReturnSkipDefault), C(ScrCatalogue.Action.ReturnRunDefault))));
        outside.Generators.Add(256);
        run = Beat(outside);
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        Assert.AreEqual(0, run.Effects.WorldChanges.Count);
    }

    [TestMethod]
    public void Touch_art_reads_its_operand_and_hands_nothing()
    {
        var run = Beat(Host(Program(Do(C(ScrCatalogue.Action.TouchArt, N(1234))), Do(C(ScrCatalogue.Action.ReturnRunDefault)))));
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        Assert.IsTrue(run.Effects.IsEmpty);
    }
}
