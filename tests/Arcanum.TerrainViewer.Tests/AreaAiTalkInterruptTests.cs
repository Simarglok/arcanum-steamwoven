using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-2074: the interrupt of the can-speak test (0x4AD800 at flag 0, <see cref="AreaAiHost.InterruptForTalk"/>) answers false, so the test answers 3, only for a run that refuses it (a priority-3 goal at level 2);
/// an NPC with no run, a priority-2 run it ends and an entity the session does not know (no run to refuse) all let the talk go on. Invented sectors, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiTalkInterruptTests
{
    [TestMethod]
    public void An_npc_with_no_run_lets_the_talk_go_on()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        Assert.IsNull(host.RunOf(f.A));
        Assert.IsTrue(host.InterruptForTalk(f.A), "no run, nothing refuses");
    }

    [TestMethod]
    public void A_walk_is_ended_by_the_level_2_interrupt_and_the_talk_goes_on()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        var order = host.OrderWalk(f.A, new CellCoord(40, 10));
        Assert.IsNotNull(host.RunOf(f.A), "the walk order gave the NPC a run: " + order + " " + host.TraceOf(f.A)?.Route);
        Assert.IsTrue(host.InterruptForTalk(f.A), "a priority-2 walk yields to level 2");
        Assert.IsNull(host.RunOf(f.A), "the walk is ended: the interrupt stays when the talk is refused later");
    }

    [TestMethod]
    public void A_priority_3_run_refuses_the_interrupt_and_stays()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        host.PerformAttack(f.A, AiSchedulerRules.Pc);
        var run = host.RunOf(f.A);
        Assert.IsNotNull(run, "the attack gave the NPC a run: " + host.TraceOf(f.A)?.Perform);
        Assert.IsFalse(host.InterruptForTalk(f.A), "a priority-3 attack refuses level 2: the can-speak test answers 3");
        Assert.AreSame(run, host.RunOf(f.A), "a refusal leaves the run as it was");
    }

    [TestMethod]
    public void An_entity_the_session_does_not_know_is_no_run_that_refuses()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        Assert.IsTrue(host.InterruptForTalk("not an entity"), "the session's own refusal of the call (an invalid owner) is not a run's refusal");
    }
}
