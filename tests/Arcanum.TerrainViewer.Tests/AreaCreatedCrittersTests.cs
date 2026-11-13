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
        // The hero can target it: by identity and by the key its row carries, through the session's holder view of its map (the one-row map has no session seed).
        var target = f.Combat.Target(f.Session, critter.Entity);
        Assert.IsNotNull(target, "a created critter is an NPC holder of its map for the combat host");
        Assert.AreEqual((critter.Entity, false), (target.Entity, target.Dead));
        Assert.AreEqual(critter.Entity, f.Combat.Target(f.Session, record.Row.Key)?.Entity);
        Assert.AreEqual(critter.Entity, f.Combat.Target(f.Session, Map, record.Row.Key)?.Entity);
        Assert.IsNotNull(f.Combat.Holder(f.Session, critter.Entity));
        // The map's own NPCs are unchanged, and the one-row maps are not held maps.
        Assert.IsNotNull(f.Combat.Record(f.A));
        Assert.IsFalse(f.Inventory.Resolver.Maps.Any(map => map.TryGetRow(critter.Entity, out _)));
        // A critter the session destroyed leaves the resolver at the next set.
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.DestroyCreatedObject(f.Session.Version, critter.Entity).Disposition);
        f.Inventory.Resolver.SetCreated(created.Maps(new(Map)));
        Assert.IsNull(f.Combat.Record(critter.Entity));
    }

    // The placement's map for a generator of cap 1: the copy stands on the generator's own tile, nothing is asked of the map.
    private sealed class NoStepMap : Arcanum.Simulation.Items.IDropMap
    {
        public CellCoord? Step(CellCoord from, int rotation) => null;
        public Arcanum.Simulation.Items.DropPath Path(CellCoord from, CellCoord to, Arcanum.Simulation.Items.DropOwner owner) => Arcanum.Simulation.Items.DropPath.None;
        public bool HasCritter(CellCoord cell) => false;
    }

    // ARC-WORLD-1526 (GS5): a monster generator's copy is built from the generator's row (its instance fields, unmarked), not from its prototype; without a known generator row it falls back to the prototype.
    [TestMethod]
    public void A_generators_copy_is_built_from_the_generators_row_and_falls_back_to_the_prototype()
    {
        using var f = AreaAiHostRebindTests.Open();
        // The generator: the fixture's NPC with the generator's marks on both flag words and an instance faction.
        AreaFieldValue Own(int slot, long number) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, number, [], null);
        var generator = f.RowA with
        {
            DirectFields = [Own(18, 0x400002), Own(69, 0x80000), Own(81, 31)],
            EffectiveFields = [.. f.RowA.EffectiveFields.Where(field => field.Slot is not (18 or 69 or 81)), Own(18, 0x400002), Own(69, 0x80000), Own(81, 31)],
        };
        var run = new SessionGeneratorRun
        {
            Entity = f.A, Map = new(Map), Cell = Spot, Prototype = Prototype, Rate = 3, DropMap = new NoStepMap(),
            Packed = new Arcanum.Simulation.Ai.GeneratorSettings(Arcanum.Simulation.Ai.GeneratorSettings.ByDay, 9, 1, 5, 0).Pack(),
        };
        var copy = f.Session.RunGenerator(f.Session.Version, run).Made.Single();
        Assert.AreEqual(f.A, copy.Summoner);
        var scripted = Make(f, Spot);   // a critter no generator made: always the prototype's row.
        var asked = new List<(string, int)>();
        var missing = new Func<int, Arcanum.Data.Characters.PrototypeFile>(_ => new(Arcanum.Data.Characters.PrototypeFileStatus.Missing, null));
        var built = new AreaCreatedCritters(f.Session, missing, (entity, map) => { asked.Add((entity, map)); return entity == f.A ? generator : null; });
        built.Maps(new(Map));
        CollectionAssert.AreEqual(new[] { (f.A, Map) }, asked, "only the generator's copy asks for a template");
        var row = built.Row(new(Map), copy.Entity)!;
        long Field(string _, int slot) => row.EffectiveFields.Single(field => field.Slot == slot).Number!.Value;
        Assert.AreEqual((31L, 0L, 0x2000L), (Field("faction", 81), Field("npc flags", 69), Field("object flags", 18)), "the generator's faction, its marks cleared, dynamic set");
        Assert.AreEqual((long)Spot.X | ((long)Spot.Y << 32), Field("location", 1));
        Assert.IsNull(built.Row(new(Map), scripted.Entity), "a critter that is no generator's copy has no row without its prototype (the test's lookup has none)");
        CollectionAssert.AreEqual(new[] { scripted.Entity }, built.Refused.Keys.ToArray());
        // No template known (the generator's row unresolved): the copy falls back to its prototype's row, which this lookup does not have.
        var fallback = new AreaCreatedCritters(f.Session, missing, (_, _) => null);
        fallback.Maps(new(Map));
        Assert.AreEqual(CreatedRowFailure.PrototypeMissing, fallback.Refused[copy.Entity]);
        // A copy that is no generator's (no template given at all) behaves as before.
        var plain = new AreaCreatedCritters(f.Session, missing);
        plain.Maps(new(Map));
        Assert.AreEqual(CreatedRowFailure.PrototypeMissing, plain.Refused[copy.Entity]);
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

    // ARC-DEV-371 (PG1): the critter flag 0x20000 the create action writes makes a created critter an encounter creature, which the tick destroys when it is left without a next tick; the host
    // queues the session's destroy (the tick holds the session) and a map NPC or a created critter without the flag is none.
    [TestMethod]
    public void A_critter_a_script_made_is_an_encounter_creature_and_its_destroy_is_queued_for_the_session()
    {
        using var f = AreaAiHostRebindTests.Open();
        var created = new AreaCreatedCritters(f.Session, Row);
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        var plain = Make(f, new(10, 12));
        var encounter = f.Session.CreateCritterNear(f.Session.Version, Prototype, new(Map), new(12, 12), new OpenDropMap()).Object!;
        f.Inventory.Resolver.SetCreated(created.Maps(new(Map)));
        Assert.AreEqual(2, host.SyncCreated(created.Npcs(new(Map))));
        Assert.IsTrue(host.IsEncounterCreature(encounter.Entity), "the flag 0x20000 the action wrote");
        Assert.IsFalse(host.IsEncounterCreature(plain.Entity), "a created critter without the flag (the Summon's, the test's) is none");
        Assert.IsFalse(host.IsEncounterCreature(f.A), "a map NPC is none");
        Assert.AreEqual(0, host.TakeDestroyedCreated().Count);
        host.Destroy(encounter.Entity);
        host.Destroy(encounter.Entity);
        host.Destroy(f.A);
        CollectionAssert.AreEqual(new[] { encounter.Entity }, host.TakeDestroyedCreated().ToArray(), "queued once, a map NPC's destroy is not the session's to do");
        Assert.AreEqual(0, host.TakeDestroyedCreated().Count);
        // The viewer destroys it in the session; the next sync drops it.
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.DestroyCreatedObject(f.Session.Version, encounter.Entity).Disposition);
        host.SyncCreated(created.Npcs(new(Map)));
        Assert.IsNull(host.Tile(encounter.Entity));
        Assert.IsFalse(host.IsEncounterCreature(encounter.Entity));
    }

    private sealed class OpenDropMap : Arcanum.Simulation.Items.IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public Arcanum.Simulation.Items.DropPath Path(CellCoord from, CellCoord to, Arcanum.Simulation.Items.DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to; at = Step(at, steps[^1])!.Value) steps.Add(Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y))));
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    [TestMethod]
    public void A_critter_that_followed_the_pc_to_another_map_is_built_again_there()
    {
        // ARC-MAGIC-050: the session moves the object whole; its row, its holder map and its AI NPC are the destination's, and the map it left lists none.
        using var f = AreaAiHostRebindTests.Open();
        var critter = Make(f, Spot);
        var created = new AreaCreatedCritters(f.Session, Row);
        Assert.AreEqual(Map, created.Npcs(new(Map)).Single().Row.Key.RuntimeMap);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ForceJoinParty(f.Session.Version, critter.Entity).Disposition);
        var arrival = new CellCoord(70, 14);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ApplyChanges(f.Session.Version, new ActorPlacement(new(Map + 1), arrival, NeighborDirection.XPlus), [], f.Session.QueryTransferMoveList(new())).Disposition);
        Assert.AreEqual(0, created.Npcs(new(Map)).Count, "the map it left lists none");
        var there = created.Npcs(new(Map + 1)).Single();
        Assert.AreEqual((critter.Entity, Map + 1, (long)arrival.X, (long)arrival.Y), (there.Entity, there.Row.Key.RuntimeMap, there.Row.WorldX!.Value, there.Row.WorldY!.Value));
        Assert.AreEqual(Map + 1, created.Maps(new(Map + 1)).Single().RuntimeMap);
        Assert.IsNotNull(created.Row(new(Map + 1), critter.Entity));
        Assert.IsNull(created.Row(new(Map), critter.Entity));
    }
}
