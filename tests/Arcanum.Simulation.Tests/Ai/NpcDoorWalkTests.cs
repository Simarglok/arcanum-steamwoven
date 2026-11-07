using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Ai.ExecutorRulesTests;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-DEV-209 (MIG14 S3, goal-table.md GT49): a walker (goals 5, 61, 80) that meets a closed idle door on its next step pushes the opener goal 11 with a 50 ms restart; goal 11 becomes 12,
/// which charges the opener 2 action points in a round, starts the door's own run and pops; the walk resumes at its first node. A refused door ends the whole run (GT39). Over an invented grid
/// and a fake door world.
/// </summary>
[TestClass]
public sealed class NpcDoorWalkTests
{
    private const string Npc = "oid:bandit", Door = "oid:door";
    private static readonly CellCoord DoorCell = new(12, 10);

    private sealed class Ports : IAiDoorWorld
    {
        public bool Open, Busy, Refuses;
        public readonly List<GoalType> Started = [];
        public readonly List<string> Log = [];
        public List<string>? Shared;
        public bool DoorValid(string door) => true;
        public bool DoorOpen(string door) => Open;
        public bool DoorBusy(string door) => Busy;
        public bool DoorRefusesOpen(string opener, string door) => Refuses;
        public bool UseDoor(string opener, string door) { Log.Add("use " + opener); Shared?.Add("use " + opener); return true; }
        public bool DoorHeld(string door) => false;
        public bool DoorStaysOpen(string door) => false;
        public bool DoorRound => false;
        public bool DoorwayOccupied(string door) => false;
        public bool StartDoorRun(string door, GoalType goal, string opener) { Started.Add(goal); Busy = true; return true; }
        public bool DoorStep(string door, bool opening) => true;
        public int DoorStepMs(string door) => 100;
        public void PlayDoorSound(string door, DoorSound sound) { }
        public void SnapDoorClosed(string door) { }
    }

    private static (World World, Ports Ports) Scene(bool door = true)
    {
        var world = new World();
        world.At[Npc] = new(10, 10);
        var ports = new Ports();
        world.DoorPorts = ports;
        ports.Shared = world.Calls;
        if (door) world.DoorAt[DoorCell] = Door;
        return (world, ports);
    }

    private static GoalRun WalkRun() => new(Npc, default, new GoalFrame(GoalType.AttemptMove) { Path = [new(11, 10), DoorCell, new(13, 10)] });

    [TestMethod]
    public void A_walker_at_a_closed_door_pushes_the_opener_with_a_50_ms_restart_and_resumes_after_it()
    {
        var (world, ports) = Scene();
        var steps = Drive(WalkRun(), world, CombatAnomalyProfile.Steamwoven);
        // The first step is onto the cell before the door; the second meets the door.
        var push = steps.First(s => s.Reason == AiExecutorReason.DoorPush);
        Assert.AreEqual(AiExecutorRules.DoorPushDelayMs, push.DelayMs, "GT49 node 8: the push restarts after 50 ms");
        Assert.AreEqual(GoalType.OpenDoor, push.Run!.Top.Goal);
        Assert.AreEqual(Door, push.Run.Top.Target);
        Assert.AreEqual(GoalType.AttemptMove, push.Run.Frames[0].Goal, "the walk waits beneath");
        CollectionAssert.AreEqual(new[] { GoalType.AnimateDoorOpen }, ports.Started, "the door's own run was started once, by goal 12");
        var calls = world.Calls.Where(c => c.StartsWith("step ", StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(new[] { "step 11,10", "step 12,10", "step 13,10" }, calls, "the walk went on through the door cell after the opener popped");
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].End);
    }

    [TestMethod]
    public void The_door_is_opened_before_the_step_into_its_cell_begins()
    {
        var (world, ports) = Scene();
        Drive(WalkRun(), world, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(1, ports.Started.Count);
        CollectionAssert.AreEqual(new[] { "use " + Npc }, ports.Log, "the use node ran once for the NPC");
        var use = world.Calls.IndexOf("use " + Npc);
        var intoDoor = world.Calls.IndexOf("step 12,10");
        Assert.IsTrue(use >= 0 && intoDoor > use, $"the use ({use}) comes before the step into the door cell ({intoDoor})");
    }

    [TestMethod]
    public void A_critter_on_the_step_is_tested_before_the_door_and_the_door_stays_shut()
    {
        // GT49: the occupied test (nodes 4 to 6) precedes the door test (node 7), so the walker waits at the critter without opening the door.
        var (world, ports) = Scene();
        world.Blocked.Add(DoorCell);
        var steps = Drive(WalkRun(), world, CombatAnomalyProfile.Steamwoven);
        Assert.IsTrue(steps.All(s => s.Reason != AiExecutorReason.DoorPush));
        Assert.AreEqual(0, ports.Started.Count);
        CollectionAssert.DoesNotContain(world.Calls, "use " + Npc);
    }

    [TestMethod]
    public void A_door_with_a_run_of_its_own_or_already_open_is_walked_through_without_a_push()
    {
        foreach (var setup in new Action<Ports>[] { p => p.Busy = true, p => p.Open = true })
        {
            var (world, ports) = Scene();
            setup(ports);
            var steps = Drive(WalkRun(), world, CombatAnomalyProfile.Steamwoven);
            Assert.IsTrue(steps.All(s => s.Reason != AiExecutorReason.DoorPush));
            Assert.AreEqual(0, ports.Started.Count);
            Assert.AreEqual(3, world.Calls.Count(c => c.StartsWith("step ", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public void A_walk_that_crosses_no_door_never_asks_the_opener_goals()
    {
        var (world, ports) = Scene(door: false);
        var steps = Drive(WalkRun(), world, CombatAnomalyProfile.Steamwoven);
        Assert.IsTrue(steps.All(s => s.Reason != AiExecutorReason.DoorPush));
        Assert.AreEqual(0, ports.Started.Count);
    }

    [TestMethod]
    public void A_door_that_refuses_the_open_attempt_ends_the_whole_walk_before_the_step()
    {
        var (world, ports) = Scene();
        ports.Refuses = true;
        world.Round = true; world.Actor = Npc; world.Points[Npc] = 60;
        var steps = Drive(WalkRun(), world, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].End, "GT39: a refused attempt ends the opener's whole run");
        Assert.AreEqual(0, ports.Started.Count);
        CollectionAssert.AreEqual(new[] { "step 11,10" }, world.Calls.Where(c => c.StartsWith("step ", StringComparison.Ordinal)).ToArray(), "the NPC never stepped into the door cell");
        Assert.AreEqual(1, world.Calls.Count(c => c == "ap -2"), "only the one tile step before the door was paid; the refused attempt charges nothing");
    }

    [TestMethod]
    public void In_a_round_the_current_participant_pays_two_action_points_for_the_use()
    {
        int Charges(bool door)
        {
            var (world, _) = Scene(door);
            world.Round = true; world.Actor = Npc; world.Points[Npc] = 60;
            Drive(WalkRun(), world, CombatAnomalyProfile.Steamwoven);
            return world.Calls.Count(c => c == "ap -2");
        }
        Assert.AreEqual(1, Charges(door: true) - Charges(door: false), "goal 12's use node charges the opener 2 action points, once");
    }

    [TestMethod]
    public void A_world_without_door_ports_ends_the_opener_run()
    {
        var (world, _) = Scene();
        world.DoorPorts = null;
        // No ports: ClosedDoorOn answers none (the fake needs them), so give the walker a frame of the opener goal directly.
        var run = new GoalRun(Npc, default, new GoalFrame(GoalType.OpenDoor, Door));
        var steps = Drive(run, world, CombatAnomalyProfile.Steamwoven);
        Assert.AreEqual(AiRunEnd.Killed, steps[^1].End);
    }

    [TestMethod]
    public void The_four_opener_goals_are_interpreted_by_the_npc_executor()
    {
        foreach (var goal in new[] { GoalType.OpenDoor, GoalType.AttemptOpenDoor, GoalType.CloseDoor, GoalType.AttemptCloseDoor })
            Assert.IsTrue(AiExecutorRules.Interprets(goal), goal.ToString());
    }
}
