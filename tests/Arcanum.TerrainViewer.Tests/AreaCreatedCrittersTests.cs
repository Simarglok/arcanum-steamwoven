using Arcanum.Data.Area;
using Arcanum.Data.Objects;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-359 (E1-c, TM4): a critter the session created has a row the world reads by identity: the record resolver and the combat host answer it as they answer a map NPC, and the AI host runs it
/// (stands it where it was made, gives it its first tick, keeps it through a window move and drops it when the session destroys it). Synthetic rows on invented sectors, no original data.
/// </summary>
[TestClass]
public sealed class AreaCreatedCrittersTests
{
    private const int Map = 1, Prototype = 28359;
    private static readonly CellCoord Spot = new(66, 12);

    // A created critter's row: the test NPC under the key a created entity gets (the real builder reads the prototype; the host tests need no file).
    private static CreatedObjectRowResult Row(SessionCreatedObject made) => new(CreatedRowFailure.None,
        AreaAiHostRebindTests.NpcWith(new("created", made.Map.RuntimeMap, AreaSourceKind.Mobile, null, 0, 0, CreatedObjectRows.KeyId(made.Entity), 0), made.Cell, (1, 1)));

    private static SessionCreatedObject Make(AreaAiHostRebindTests.Fixture f, CellCoord cell)
    {
        var made = f.Session.CreateObject(f.Session.Version, SessionCreatedKind.Critter, Prototype, new(Map), cell);
        Assert.AreEqual(GameSessionFailure.None, made.Failure);
        return made.Object!;
    }

    [TestMethod]
    public void A_created_critters_row_resolves_by_identity_and_builds_combat_facts()
    {
        using var f = AreaAiHostRebindTests.Open();
        var critter = Make(f, Spot);
        var created = new AreaCreatedCritters(f.Session, Row);
        Assert.IsNull(f.Combat.Record(critter.Entity), "before the resolver is given the critters nothing knows it");
        f.Inventory.Resolver.SetCreated(created.Maps(new(Map)));
        var record = f.Combat.Record(critter.Entity);
        Assert.IsNotNull(record, "the resolver answers the created entity without any home map");
        Assert.AreEqual((Map, (long)Spot.X, (long)Spot.Y), (record.HomeMap, record.Row.WorldX!.Value, record.Row.WorldY!.Value));
        Assert.AreEqual(Map, f.Combat.HomeMap(critter.Entity));
        Assert.IsNotNull(f.Combat.Critter(critter.Entity), "the NPC's combat facts build from the row (a holder of the one-row map)");
        // The map's own NPCs are unchanged, and the one-row maps are not held maps.
        Assert.IsNotNull(f.Combat.Record(f.A));
        Assert.IsFalse(f.Inventory.Resolver.Maps.Any(map => map.TryGetRow(critter.Entity, out _)));
        // A critter the session destroyed leaves the resolver at the next set.
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.DestroyCreatedObject(f.Session.Version, critter.Entity).Disposition);
        f.Inventory.Resolver.SetCreated(created.Maps(new(Map)));
        Assert.IsNull(f.Combat.Record(critter.Entity));
    }

    [TestMethod]
    public void The_ai_host_runs_a_created_critter_keeps_it_through_a_window_move_and_drops_it_when_destroyed()
    {
        using var f = AreaAiHostRebindTests.Open();
        var created = new AreaCreatedCritters(f.Session, Row);
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        Assert.AreEqual(0, host.SyncCreated(created.Npcs(new(Map))), "nothing is created yet");
        var critter = Make(f, new(10, 12));
        f.Inventory.Resolver.SetCreated(created.Maps(new(Map)));
        Assert.AreEqual(1, host.SyncCreated(created.Npcs(new(Map))));
        Assert.AreEqual(0, host.SyncCreated(created.Npcs(new(Map))), "a second sync adds nothing");
        Assert.AreEqual(new CellCoord(10, 12), host.Tile(critter.Entity), "it stands on the cell it was made on");
        CollectionAssert.Contains(host.Scene.Npcs.Select(n => n.Entity).ToArray(), critter.Entity);
        Assert.IsNotNull(f.Session.QueryAiTick(critter.Entity), "a created critter's heartbeat chain starts (a delayed first tick)");
        CollectionAssert.Contains(host.CrittersOn(new(10, 12)).ToArray(), critter.Entity);

        // A window move does not forget it, though no window holds its row and it stands idle.
        host.Rebind(f.Scene(64, AreaAiHostRebindTests.Sector(1, 0), []));
        Assert.AreEqual(new CellCoord(10, 12), host.Tile(critter.Entity), "the host's created critters are the map's, not a window's");
        CollectionAssert.Contains(host.Scene.Npcs.Select(n => n.Entity).ToArray(), critter.Entity);

        // The session destroys it: the next sync drops it from the host.
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.DestroyCreatedObject(f.Session.Version, critter.Entity).Disposition);
        Assert.AreEqual(0, host.SyncCreated(created.Npcs(new(Map))));
        Assert.IsNull(host.Tile(critter.Entity), "a destroyed critter is no NPC of the host");
        CollectionAssert.DoesNotContain(host.Scene.Npcs.Select(n => n.Entity).ToArray(), critter.Entity);
    }

    [TestMethod]
    public void A_critter_stands_where_the_session_recorded_it_and_a_prototype_that_does_not_read_is_reported_once()
    {
        using var f = AreaAiHostRebindTests.Open();
        var critter = Make(f, new(10, 12));
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.RecordNpcPlace(f.Session.Version, new(Map), critter.Entity, new(11, 13), NeighborDirection.XPlus).Disposition);
        var created = new AreaCreatedCritters(f.Session, Row);
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        f.Inventory.Resolver.SetCreated(created.Maps(new(Map)));
        host.SyncCreated(created.Npcs(new(Map)));
        Assert.AreEqual(new CellCoord(11, 13), host.Tile(critter.Entity), "a host standing a critter again (a load) puts it on its recorded place");

        // A second critter whose prototype is no critter's: it stays in the session, is left out of the world's rows and is reported once.
        var bad = Make(f, new(20, 12));
        var picky = new AreaCreatedCritters(f.Session, made => made.Entity == bad.Entity ? new(CreatedRowFailure.NotACritter, null) : Row(made));
        CollectionAssert.AreEqual(new[] { critter.Entity }, picky.Npcs(new(Map)).Select(n => n.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { (bad.Entity, CreatedRowFailure.NotACritter) }, picky.TakeNewRefusals().ToArray());
        Assert.AreEqual(0, picky.TakeNewRefusals().Count, "each refusal is taken once");
        Assert.IsNotNull(f.Session.QueryCreatedObject(bad.Entity), "the session still holds it");
    }
}
