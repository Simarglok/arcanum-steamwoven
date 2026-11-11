using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-MAGIC-050 (FM1, FM3, FM12, ledger TI12): a critter the session created that follows the PC is carried by the PC's change of place like any other follower: the original moves
/// each object whole, so the created critter's record is on the destination map at the arrival cell afterwards (one revision with the move), it is a holder of the destination's
/// seed, a critter ordered to wait or one that does not follow stays on its map, and the move is saved with the objects section. Invented prototype numbers, maps and cells only.
/// </summary>
[TestClass]
public sealed class CreatedCritterCarryTests
{
    private static SessionCreatedObject MadeCritter(GameSessionOwner owner, SessionMapId map, CellCoord cell)
    {
        var made = owner.CreateObject(owner.Version, SessionCreatedKind.Critter, 17087, map, cell);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (made.Disposition, made.Failure));
        return made.Object!;
    }

    [TestMethod]
    public void A_following_created_critter_is_an_object_of_the_destination_after_the_move()
    {
        using var owner = Create();
        var pet = MadeCritter(owner, Exterior, new(101, 200));
        var stayer = MadeCritter(owner, Exterior, new(102, 200));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, pet.Entity).Disposition);
        var list = owner.QueryTransferMoveList(new());
        CollectionAssert.AreEqual(new[] { pet.Entity }, list.Travelling.ToArray());
        var revision = owner.Version.Revision;
        var arrival = new CellCoord(10, 20);
        var moved = owner.ApplyChanges(owner.Version, new ActorPlacement(Cave, arrival, NeighborDirection.XPlus), [], list);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (moved.Disposition, moved.Failure));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for the move and the object");
        Assert.AreEqual(pet with { Map = Cave, Cell = arrival }, owner.QueryCreatedObject(pet.Entity), "the same object, the same number, on the destination");
        CollectionAssert.AreEqual(new[] { pet }.Select(p => p with { Map = Cave, Cell = arrival }).ToArray(), owner.QueryCreatedObjects(Cave).ToArray());
        CollectionAssert.AreEqual(new[] { stayer }, owner.QueryCreatedObjects(Exterior).ToArray(), "a critter that does not follow stays on its map");
        // Back again: the move goes both ways, and the creation order is kept.
        var back = owner.ApplyChanges(owner.Version, new ActorPlacement(Exterior, new(5, 5), NeighborDirection.XPlus), [], owner.QueryTransferMoveList(new()));
        Assert.AreEqual(GameSessionDisposition.Changed, back.Disposition);
        CollectionAssert.AreEqual(new[] { pet.Entity, stayer.Entity }, owner.QueryCreatedObjects(Exterior).Select(o => o.Entity).ToArray(), "the creation order is kept (the pet was made first)");
        Assert.AreEqual(0, owner.QueryCreatedObjects(Cave).Count);
    }

    [TestMethod]
    public void The_moved_critter_is_a_holder_of_the_destinations_seed_and_not_of_the_sources()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [])).Failure);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Cave, new([], [])).Failure);
        var pet = MadeCritter(owner, Exterior, new(101, 200));
        string[] Holders(SessionMapId map) => owner.QueryMapInventory(map)!.Holders.Select(holder => holder.Entity).ToArray();
        CollectionAssert.AreEqual(new[] { pet.Entity }, Holders(Exterior));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, pet.Entity).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, new ActorPlacement(Cave, new(10, 20), NeighborDirection.XPlus), [], owner.QueryTransferMoveList(new())).Disposition);
        CollectionAssert.AreEqual(Array.Empty<string>(), Holders(Exterior));
        CollectionAssert.AreEqual(new[] { pet.Entity }, Holders(Cave));
    }

    [TestMethod]
    public void A_created_critter_ordered_to_wait_or_not_following_stays_and_a_same_map_step_moves_nothing()
    {
        using var owner = Create();
        var pet = MadeCritter(owner, Exterior, new(101, 200));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, pet.Entity).Disposition);
        // The hero's step inside the map moves no object.
        var same = owner.ApplyChanges(owner.Version, new ActorPlacement(Exterior, new(50, 60), NeighborDirection.XPlus), [], owner.QueryTransferMoveList(new()));
        Assert.AreEqual(GameSessionDisposition.Changed, same.Disposition);
        Assert.AreEqual(pet, owner.QueryCreatedObject(pet.Entity));
        // A follower the move list orders to wait stays (the unconscious ones, FM2).
        var waiting = owner.QueryTransferMoveList(new() { NoFatigue = entity => entity == pet.Entity });
        CollectionAssert.AreEqual(Array.Empty<string>(), waiting.Travelling.ToArray());
        var left = owner.ApplyChanges(owner.Version, new ActorPlacement(Cave, new(1, 1), NeighborDirection.XPlus), [], waiting);
        Assert.AreEqual(GameSessionDisposition.Changed, left.Disposition);
        Assert.AreEqual(pet, owner.QueryCreatedObject(pet.Entity), "it waits on the map the PC left");
    }

    [TestMethod]
    public void The_moved_critter_is_saved_on_its_new_map()
    {
        using var owner = Create();
        var pet = MadeCritter(owner, Exterior, new(101, 200));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, pet.Entity).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, new ActorPlacement(Cave, new(10, 20), NeighborDirection.XPlus), [], owner.QueryTransferMoveList(new())).Disposition);
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(pet with { Map = Cave, Cell = new(10, 20) }, back.QueryCreatedObject(pet.Entity));
        Assert.AreEqual(0, back.QueryCreatedObjects(Exterior).Count);
    }
}
