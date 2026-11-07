using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-231 (MIG13 S14a, goal-table.md GT158–GT161): a script's walk to (69) and run to (74) start the NPC's goal 7 / 55 through the generic starter, retarget a walk in place and turn the run flag on
/// by encumbrance. Invented sectors and NPCs, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostScriptWalkTests
{
    private static readonly CellCoord Far = new(150, 20), Farther = new(152, 22);

    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Open()
    {
        var f = AreaAiHostRebindTests.Open(null);
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f), new AreaAiNpc(f.B, f.RowB, new CellCoord(130, 10))]));
        return (f, host);
    }

    [TestMethod]
    public void A_walk_starts_goal_7_with_the_tile_and_no_run_flag_and_the_same_tile_changes_nothing()
    {
        var (f, host) = Open();
        using var fixture = f;
        Assert.AreEqual("started", host.ScriptWalk(f.A, Far, run: false));
        var run = host.RunOf(f.A)!;
        Assert.AreEqual((GoalType.MoveNearTile, Far), (run.Bottom.Goal, run.Bottom.Tile!.Value), "GT159: goal 7 with the tile");
        Assert.AreEqual(GoalRunFlags.None, run.Flags & GoalRunFlags.Running, "GT159: a plain walk");
        Assert.AreEqual("already walking there", host.ScriptWalk(f.A, Far, run: false));
        Assert.AreEqual(1, host.Session.QueryGoals().RunsOf(f.A).Length);
    }

    [TestMethod]
    public void Another_tile_retargets_the_walk_in_place_with_the_same_handle()
    {
        var (f, host) = Open();
        using var fixture = f;
        host.ScriptWalk(f.A, Far, run: false);
        var handle = host.RunOf(f.A)!.Handle;
        Assert.AreEqual("retargeted", host.ScriptWalk(f.A, Farther, run: false));
        var run = host.RunOf(f.A)!;
        Assert.AreEqual((handle, Farther), (run.Handle, run.Bottom.Tile!.Value), "GT159: the destination is overwritten, the run is kept");
    }

    [TestMethod]
    public void A_run_starts_goal_55_with_the_run_flag_and_turns_a_found_walk_into_a_run_even_for_the_same_tile()
    {
        var (f, host) = Open();
        using var fixture = f;
        Assert.AreEqual("started", host.ScriptWalk(f.A, Far, run: true));
        var run = host.RunOf(f.A)!;
        Assert.AreEqual(GoalType.RunNearTile, run.Bottom.Goal);
        Assert.AreEqual(GoalRunFlags.Running, run.Flags & GoalRunFlags.Running, "GT161: the run flag is set below encumbrance 4");

        host.ScriptWalk(f.A, Farther, run: false);
        var walk = host.RunOf(f.A)!;
        Assert.AreEqual((GoalType.RunNearTile, Farther), (walk.Bottom.Goal, walk.Bottom.Tile!.Value), "a walk to another tile retargets a running goal and keeps goal and gait (GT159)");
    }

    [TestMethod]
    public void A_run_to_the_same_tile_of_a_found_walk_sets_the_run_flag()
    {
        var (f, host) = Open();
        using var fixture = f;
        host.ScriptWalk(f.A, Far, run: false);
        Assert.AreEqual(GoalRunFlags.None, host.RunOf(f.A)!.Flags & GoalRunFlags.Running);
        Assert.AreEqual("already walking there", host.ScriptWalk(f.A, Far, run: true));
        Assert.AreEqual(GoalRunFlags.Running, host.RunOf(f.A)!.Flags & GoalRunFlags.Running, "GT161: a walk is turned into a run even when the tile is the same");
    }

    [TestMethod]
    public void A_perform_animation_starts_goal_0_or_goal_60_and_refuses_a_number_outside_0_to_25()
    {
        var (f, host) = Open();
        using var fixture = f;
        Assert.AreEqual("started", host.ScriptAnimate(f.A, 13));
        Assert.AreEqual((GoalType.Animate, 13), (host.RunOf(f.A)!.Bottom.Goal, host.RunOf(f.A)!.Bottom.Animation), "GT156: goal 0 with the animation");
        Assert.AreEqual("started", host.ScriptAnimate(f.A, 10));
        Assert.AreEqual(GoalType.AnimateKneelMagicHands, host.RunOf(f.A)!.Bottom.Goal, "GT156: animation 10 of a critter is goal 60 and replaced the animation run (level 3)");
        Assert.AreEqual(1, host.Session.QueryGoals().RunsOf(f.A).Length);
        StringAssert.Contains(host.ScriptAnimate(f.A, 26), "outside 0..25");
        StringAssert.Contains(host.ScriptAnimate("npc:nobody", 3), "not an NPC");
    }

    [TestMethod]
    public void A_script_steal_starts_the_pickpocket_goal_only_when_the_victim_holds_100_coins()
    {
        var (f, host) = Open();
        using var fixture = f;
        StringAssert.Contains(host.ScriptSteal(f.A, "pc"), "fewer than 100");
        Assert.IsNull(host.RunOf(f.A), "GT164: nothing happens");
        StringAssert.Contains(host.ScriptSteal("npc:nobody", "pc"), "not an NPC");
        var given = host.Session.GiveGoldToHero(host.Session.Version, 150);
        if (given.Disposition == Arcanum.Session.GameSessionDisposition.Rejected) Assert.Inconclusive("the fixture cannot give the hero coins: " + given.Failure);
        Assert.AreEqual("started", host.ScriptSteal(f.A, "pc"));
        Assert.AreEqual((GoalType.UseSkill, AiExecutorRules.PickpocketSkill), (host.RunOf(f.A)!.Bottom.Goal, host.RunOf(f.A)!.Bottom.Skill!.Value), "GT164: goal 33 with skill 5");
        var handle = host.RunOf(f.A)!.Handle;
        Assert.AreEqual("already stealing from the victim", host.ScriptSteal(f.A, "pc"));
        Assert.AreEqual(handle, host.RunOf(f.A)!.Handle, "a repeated beat keeps the run");
    }

    [TestMethod]
    public void A_script_prone_interrupts_the_runs_at_level_4_and_shows_the_fall_with_no_goal()
    {
        var (f, host) = Open();
        using var fixture = f;
        host.ScriptWalk(f.A, Far, run: false);
        Assert.IsNotNull(host.RunOf(f.A));
        Assert.AreEqual("lying", host.ScriptProne(f.A));
        Assert.IsNull(host.RunOf(f.A), "GT187: the walk (priority 2) is ended and no goal starts");
        Assert.AreEqual(7, host.ShownAnimation(f.A), "the fall animation");
        StringAssert.Contains(host.ScriptProne("npc:nobody"), "not an NPC");
    }

    [TestMethod]
    public void A_script_prone_is_refused_by_a_priority_5_run_and_changes_nothing()
    {
        var (f, host) = Open();
        using var fixture = f;
        var dying = host.Session.StartGoal(new(f.A, GoalStarters.NoInterrupt, AiExecutorRules.DyingFrame(7)), new Ports());
        Assert.IsTrue(dying.Started);
        var before = host.ShownAnimation(f.A);
        StringAssert.Contains(host.ScriptProne(f.A), "refused");
        Assert.AreEqual(before, host.ShownAnimation(f.A), "GT187: a refusal does nothing");
    }

    [TestMethod]
    public void A_script_cast_without_a_cast_host_starts_nothing()
    {
        var (f, host) = Open();
        using var fixture = f;
        Assert.AreEqual("no cast host", host.ScriptCast(f.A, f.B, 7, free: false, unresistable: false), "the scene has no cast host");
        Assert.AreEqual("no cast host", host.ScriptCast(null, f.B, 7, free: true, unresistable: true));
        Assert.IsNull(host.RunOf(f.A));
    }

    private sealed class Ports : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    [TestMethod]
    public void A_negative_cell_an_unknown_npc_and_a_priority_3_run_start_nothing()
    {
        var (f, host) = Open();
        using var fixture = f;
        StringAssert.Contains(host.ScriptWalk(f.A, new CellCoord(-1, 5), run: false), "outside the cell range");
        StringAssert.Contains(host.ScriptWalk("npc:nobody", Far, run: false), "not an NPC");
        host.BeginAttack(f.A, AiSchedulerRules.Pc);
        host.PerformAttack(f.A, AiSchedulerRules.Pc);
        var attack = host.RunOf(f.A)!.Handle;
        var answer = host.ScriptWalk(f.A, Far, run: false);
        Assert.AreEqual(attack, host.RunOf(f.A)!.Handle, "GT160: " + answer);
        Assert.AreEqual(GoalType.Attack, host.RunOf(f.A)!.Bottom.Goal, "an attack (priority 3) refuses the walk's level-2 interrupt and the script never learns it");
    }
}
