using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1317 (ledger VAG6): the danger update's enemy-list pick purges entries of objects that no longer exist first, and the host writes the same purge to the saved list. Invented
/// sectors and NPCs (the rebind tests' fixture), no original data.
/// </summary>
[TestClass]
public sealed class AreaAiEnemyPurgeTests
{
    private const string Destroyed = SessionCreatedObject.CritterPrefix + "7";

    [TestMethod]
    public void The_host_knows_the_hero_its_npcs_and_unknown_objects_and_not_a_created_critter_the_session_lost()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        Assert.IsTrue(host.Exists(AiSchedulerRules.Pc), "the hero");
        Assert.IsTrue(host.Exists(f.A), "an NPC of the host");
        Assert.IsTrue(host.Exists(f.B), "an object of no window of the host is kept: only its own map can tell");
        Assert.IsFalse(host.Exists(Destroyed), "a created critter the session no longer holds is gone");
    }

    [TestMethod]
    public void The_danger_update_drops_the_destroyed_entry_from_the_saved_enemy_list_as_the_pick_does()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        var session = f.Session;
        var gone = SessionCombatant.Critter(Destroyed);
        var focus = session.SetDangerState(session.Version, new SessionDangerRequest { Entity = f.A, State = DangerState.CombatFocus, Target = SessionCombatant.Critter(f.B) });
        Assert.AreEqual(GameSessionDisposition.Changed, focus.Disposition, focus.Failure.ToString());
        foreach (var entry in new[] { gone, SessionCombatant.Critter(f.B), SessionCombatant.Hero })
            Assert.AreEqual(GameSessionDisposition.Changed, session.AddDangerEnemy(session.Version, f.A, entry).Disposition);
        CollectionAssert.AreEqual(new[] { gone, SessionCombatant.Critter(f.B), SessionCombatant.Hero }, session.QueryDanger(f.A).Enemies.ToArray());

        host.UpdateDanger(f.A);

        // The pick ran (the focus is an object the host does not hold, so no focus path): the purge fills the hole with the last entry.
        CollectionAssert.AreEqual(new[] { SessionCombatant.Hero, SessionCombatant.Critter(f.B) }, session.QueryDanger(f.A).Enemies.ToArray(), host.TraceOf(f.A)?.Danger);
    }
}
