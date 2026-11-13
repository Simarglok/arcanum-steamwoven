using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1512 (VBG1): the host's back-off. The away path (0x41F3C0 with flag 0x800) gives the first clear line from the side opposite the target, and perform's back-off (0x4AC7B0) starts or retargets
/// the run to its end tile (0x433C80); BACKING_OFF ends when the range is reached or no line is clear. Invented sectors and NPCs (the rebind tests' fixture, hero at (58,12), the NPC at (62,10)).
/// </summary>
[TestClass]
public sealed class AreaAiBackOffTests
{
    private static readonly CellCoord Away = new(63, 9);
    // The reaction facts a fight on the hero needs (the setter's rule 8), as the session tests give them.
    private static readonly SessionDangerReaction Reaction = new(new(60, Arcanum.Simulation.Character.CharacterRace.Human, false, false), new(Arcanum.Simulation.Character.CharacterRace.HalfElf, 0, 0, [], [], [], false), 30);

    // Two sectors, so the line north-east of the NPC (x 62, sector edge 64) has cells to walk on.
    private static Arcanum.Data.Traversal.AreaStepSnapshot Open() => Arcanum.Data.Traversal.AreaStepSnapshot.Union([AreaAiHostRebindTests.Sector(0, 0), AreaAiHostRebindTests.Sector(1, 0)]);

    private static AreaAiHost Fighting(AreaAiHostRebindTests.Fixture f, Arcanum.Data.Traversal.AreaStepSnapshot? steps = null)
    {
        var host = f.Build(f.Scene(0, steps ?? Open(), [AreaAiHostRebindTests.NpcA(f)]));
        var focus = f.Session.SetDangerState(f.Session.Version, new SessionDangerRequest { Entity = f.A, State = DangerState.CombatFocus, Target = SessionCombatant.Hero, Reaction = Reaction });
        Assert.AreEqual(GameSessionDisposition.Changed, focus.Disposition, focus.Failure.ToString());
        return host;
    }

    [TestMethod]
    public void The_away_tile_is_the_first_clear_line_opposite_the_target_range_minus_distance_out()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, Open(), [AreaAiHostRebindTests.NpcA(f)]));
        // The hero is 4 tiles away (distance 4); toward it is south-west, so away is north-east: range 6 puts it 2 tiles out, range 5 one tile out.
        Assert.AreEqual(new CellCoord(64, 8), host.AwayTile(f.A, AiSchedulerRules.Pc, 6));
        Assert.AreEqual(Away, host.AwayTile(f.A, AiSchedulerRules.Pc, 5));
        Assert.IsNull(host.AwayTile(f.A, AiSchedulerRules.Pc, 4), "the range is reached: no tile");
        Assert.IsNull(host.AwayTile(f.B, AiSchedulerRules.Pc, 5), "an NPC the host does not know");
    }

    [TestMethod]
    public void No_clear_line_gives_no_tile_so_the_back_off_ends_in_the_same_pass()
    {
        using var f = AreaAiHostRebindTests.Open();
        // The window's evidence admits no cell around the NPC, so no line is clear.
        var host = Fighting(f, AreaAiHostRebindTests.Sector(5, 5));
        Assert.IsNull(host.AwayTile(f.A, AiSchedulerRules.Pc, 5));
    }

    [TestMethod]
    public void A_back_off_runs_to_the_away_tile_and_a_second_pass_keeps_the_run()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = Fighting(f);
        Assert.IsTrue(f.Session.StartBackOff(f.Session.Version, f.A).Disposition == GameSessionDisposition.Changed);
        Assert.IsNull(host.RunOf(f.A));

        host.Perform(f.A);

        var run = host.RunOf(f.A) ?? throw new AssertFailedException("no run: " + host.TraceOf(f.A)?.Perform);
        Assert.AreEqual((GoalType.RunToTile, Away), (run.Bottom.Goal, run.Bottom.Tile), "the run to the away tile");
        Assert.IsTrue((f.Session.QueryDanger(f.A).Flags & DangerFlags.BackingOff) != 0, "BACKING_OFF stays while the range is short");

        // A second pass with the same away tile keeps the run (0x433C80 finds a run to the same tile: nothing changes).
        host.Perform(f.A);
        Assert.AreSame(run, host.RunOf(f.A), "the same run, not a new one");
    }

    [TestMethod]
    public void A_back_off_with_no_line_clears_the_bit_and_starts_no_run()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = Fighting(f, AreaAiHostRebindTests.Sector(5, 5));
        Assert.IsTrue(f.Session.StartBackOff(f.Session.Version, f.A).Disposition == GameSessionDisposition.Changed);

        host.Perform(f.A);

        Assert.IsNull(host.RunOf(f.A), "no run: no line");
        Assert.IsTrue((f.Session.QueryDanger(f.A).Flags & DangerFlags.BackingOff) == 0, "BACKING_OFF cleared (MR15)");
    }
}
