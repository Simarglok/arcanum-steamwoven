using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-WORLD-2503 (ledger DD1–DD8): the destroy of a created critter (a summon's expiry or dispel) runs the original's inventory-on-destroy (0x4639E0) too: the items with object flag 0x400000 are taken out and dropped on the
/// critter's cell before the rest of its list is destroyed with it. Invented prototype numbers, maps and cells only.
/// </summary>
[TestClass]
public sealed class CreatedCritterDestroyDropTests
{
    private const int Prototype = 28359;
    private static readonly CellCoord Spot = new(101, 200), Moved = new(110, 120);

    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    // A map with two items on the ground; the hero picks both up and gives them to the created critter (a given taken item keeps its origin facts, so the invulnerable flag of the map's item is known).
    private static (GameSessionOwner Owner, SessionCreatedObject Critter) WithGiven(bool gemInvulnerable = true)
    {
        var plain = InventoryStateTests.Facts(SessionItemType.Generic, 5600);
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [], [new SessionSeedItem("gem", plain with { Invulnerable = gemInvulnerable }, Spot), new SessionSeedItem("dull", plain, Spot)])).Failure);
        var critter = owner.CreateCritterNear(owner.Version, Prototype, Exterior, new(100, 201), new OpenMap()).Object!;
        foreach (var entity in new[] { "gem", "dull" })
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, entity, _ => SessionInsertVerdict.Accepted).Disposition, entity);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, critter.Entity, entity, _ => SessionInsertVerdict.Accepted).Disposition, entity);
        }
        return (owner, critter);
    }

    [TestMethod]
    public void AnInvulnerableItemTheHeroGaveTheCritterIsDroppedOnItsCellWithItsDecayAndTheRestGoesWithIt()
    {
        var (owner, critter) = WithGiven();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critter.Entity).Disposition);
        Assert.IsNull(owner.QueryCreatedObject(critter.Entity), "the critter is destroyed");
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("gem", SessionPlacedSource.Pc, Exterior, critter.Cell), (placed.Entity, placed.Source, placed.Map, placed.Cell), "the made critter's creation cell is where it stood");
        Assert.IsTrue(owner.IsItemDestroyed("dull"), "the item without the flag is destroyed with the critter's list");
        Assert.IsFalse(owner.IsItemDestroyed("gem"));
        CollectionAssert.AreEqual(new[] { "gem" }, owner.QueryItemDecays().Select(decay => decay.Entity).ToArray(), "a drop schedules its decay");
        Assert.AreEqual(0, owner.Capture().Inventory.Npcs.Count, "no list stays in the save");
    }

    [TestMethod]
    public void TheDropLandsWhereTheCallerSaysTheCritterStandsNow()
    {
        var (owner, critter) = WithGiven();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critter.Entity, Moved).Disposition);
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual((Exterior, Moved), (placed.Map, placed.Cell));
    }

    [TestMethod]
    public void ACritterWithNoInvulnerableItemIsOnlyDestroyed()
    {
        var (owner, critter) = WithGiven(gemInvulnerable: false);
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critter.Entity).Disposition);
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        Assert.IsTrue(owner.IsItemDestroyed("gem") && owner.IsItemDestroyed("dull"));
        Assert.AreEqual(0, owner.QueryItemDecays().Count);
    }

    [TestMethod]
    public void ARefusedDropDestroysNothing()
    {
        var (owner, critter) = WithGiven();
        using var _ = owner;
        var result = owner.DestroyCreatedObject(owner.Version, critter.Entity, new CellCoord(-1, -1));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.DropTileUnfit), (result.Disposition, result.Failure));
        Assert.IsNotNull(owner.QueryCreatedObject(critter.Entity), "the critter stands");
        Assert.IsFalse(owner.IsItemDestroyed("dull"));
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void TheListDestroyDropsEveryCritterItemsToo()
    {
        var (owner, critter) = WithGiven();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObjects(owner.Version, [critter.Entity]).Disposition);
        Assert.AreEqual("gem", owner.QueryPlacedItems().Single().Entity);
    }

    [TestMethod]
    public void ASecondDestroyChangesNothingAndTheListDestroySkipsAScenery()
    {
        var (owner, critter) = WithGiven();
        using var _ = owner;
        var scenery = owner.CreateObject(owner.Version, SessionCreatedKind.Scenery, 3000, Exterior, new(90, 90), 0, 0).Object!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObjects(owner.Version, [scenery.Entity, critter.Entity]).Disposition);
        Assert.AreEqual("gem", owner.QueryPlacedItems().Single().Entity);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DestroyCreatedObject(owner.Version, critter.Entity).Disposition);
        Assert.AreEqual(1, owner.QueryPlacedItems().Count, "nothing dropped twice");
    }

    [TestMethod]
    public void TheDroppedItemAndItsDecaySurviveASave()
    {
        var (owner, critter) = WithGiven();
        using var _ = owner;
        var before = owner.Version;
        var result = owner.DestroyCreatedObject(before, critter.Entity);
        Assert.AreEqual(before.Revision + 1, result.Version.Revision, "one revision for the drops and the destroy");
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        CollectionAssert.AreEquivalent(owner.QueryPlacedItems().ToArray(), back.QueryPlacedItems().ToArray());
        CollectionAssert.AreEqual(new[] { "gem" }, back.QueryItemDecays().Select(decay => decay.Entity).ToArray());
    }
}
