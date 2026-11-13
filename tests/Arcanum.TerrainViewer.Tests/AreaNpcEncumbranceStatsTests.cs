using Arcanum.Data.Area;
using Arcanum.Data.Objects;
using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-1651 (FR-MAGIC-007, leftover of ARC-MAGIC-1568): the viewer's side of the NPC encumbrance fatigue timer. When the timer fires, the session asks the pass world for the NPC's stored base stats
/// (<see cref="ISessionNpcStatsWorld.StoredStats"/>): <see cref="AreaCombatHost.StoredStats"/> answers by identity with what a spell wrote laid over the row, <see cref="AreaEventWorld"/> forwards it from the shown map's combat host,
/// and a critter the session created is answered like a map NPC. Synthetic rows on invented sectors, no original data.
/// </summary>
[TestClass]
public sealed class AreaNpcEncumbranceStatsTests
{
    private const int Map = 1;

    [TestMethod]
    public void The_combat_host_gives_the_stored_stats_with_the_written_ones_laid_over_and_nothing_for_an_unknown_object()
    {
        using var f = AreaAiHostRebindTests.Open();
        var stored = f.Combat.StoredStats(f.A);
        Assert.IsNotNull(stored);
        Assert.AreEqual(Arcanum.Data.Combat.CombatFieldReader.BaseStats, stored.Count, "the 28 entries");
        Assert.AreEqual(20, stored[0]);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteCritterStat(f.Session.Version, new(Map), f.A, 0, 7).Disposition);
        Assert.AreEqual(7, f.Combat.StoredStats(f.A)![0], "the strength a spell wrote");
        Assert.AreEqual(20, f.Combat.StoredStats(f.A)![1], "the other entries as stored");
        Assert.AreEqual(20, f.Combat.StoredStats(f.B)![0], "another NPC is unchanged");
        Assert.IsNull(f.Combat.StoredStats("oid:" + new string('f', 64)), "no row, no stats");
        Assert.Throws<ArgumentNullException>(() => f.Combat.StoredStats(null!));
    }

    [TestMethod]
    public void The_event_world_forwards_the_stats_from_the_shown_maps_combat_host_whatever_the_map_it_is_asked_for()
    {
        using var f = AreaAiHostRebindTests.Open();
        var world = new AreaEventWorld(() => f.Combat, () => null);
        Assert.IsInstanceOfType<ISessionNpcStatsWorld>(world);
        CollectionAssert.AreEqual(f.Combat.StoredStats(f.A)!.ToArray(), world.StoredStats(new(Map), f.A)!.ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteCritterStat(f.Session.Version, new(Map), f.A, 0, 9).Disposition);
        Assert.AreEqual(9, world.StoredStats(new(Map), f.A)![0]);
        // By identity, as CritterFacts is: the map argument does not matter while the events fire on the shown map.
        Assert.AreEqual(9, world.StoredStats(new(Map + 5), f.A)![0]);
        Assert.IsNull(world.StoredStats(new(Map), "oid:" + new string('f', 64)));
        Assert.IsNull(new AreaEventWorld(() => null, () => null).StoredStats(new(Map), f.A), "no combat host: no stats, the chain ends");
    }

    [TestMethod]
    public void A_created_critter_is_answered_like_a_map_npc()
    {
        using var f = AreaAiHostRebindTests.Open();
        var made = f.Session.CreateObject(f.Session.Version, SessionCreatedKind.Critter, 28359, new(Map), new(66, 12));
        Assert.AreEqual(GameSessionFailure.None, made.Failure);
        var critter = made.Object!;
        var created = new AreaCreatedCritters(f.Session, row => new(CreatedRowFailure.None,
            AreaAiHostRebindTests.NpcWith(new("created", row.Map.RuntimeMap, AreaSourceKind.Mobile, null, 0, 0, CreatedObjectRows.KeyId(row.Entity), 0), row.Cell, (1, 1))));
        var world = new AreaEventWorld(() => f.Combat, () => null);
        Assert.IsNull(world.StoredStats(new(Map), critter.Entity), "before the resolver knows the critter there is no row");
        f.Inventory.Resolver.SetCreated(created.Maps(new(Map)));
        var stats = world.StoredStats(new(Map), critter.Entity);
        Assert.IsNotNull(stats, "the created critter has a combat row now");
        Assert.AreEqual((Arcanum.Data.Combat.CombatFieldReader.BaseStats, 20), (stats.Count, stats[0]));
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteCritterStat(f.Session.Version, new(Map), critter.Entity, 0, 6).Disposition);
        Assert.AreEqual(6, world.StoredStats(new(Map), critter.Entity)![0], "a written strength lies over the created critter's row too");
        // Destroyed, it leaves the resolver and the chain would end.
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.DestroyCreatedObject(f.Session.Version, critter.Entity).Disposition);
        f.Inventory.Resolver.SetCreated(created.Maps(new(Map)));
        Assert.IsNull(world.StoredStats(new(Map), critter.Entity));
    }
}
