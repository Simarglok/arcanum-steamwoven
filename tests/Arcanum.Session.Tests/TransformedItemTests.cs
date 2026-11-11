using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-MAGIC-042 (T-m1, ledger TI5): the item lying on the ground becomes a created critter on its own cell and the item is destroyed, in one revision. Invented prototype numbers, maps and cells only.
/// </summary>
[TestClass]
public sealed class TransformedItemTests
{
    private static readonly SessionItemKind Gadget = new(SessionItemType.Food, 10081, 7001, 10081);
    private const int Critter = 28480;

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

    private static string Lay(GameSessionOwner owner) => owner.CreateGroundItem(owner.Version, Gadget, Exterior, new(300, 400), new OpenMap()).Item!;

    [TestMethod]
    public void A_lying_item_becomes_a_critter_on_its_cell_and_is_gone_in_one_revision()
    {
        using var owner = Create();
        var item = Lay(owner);
        var cell = owner.QueryPlacedItems().Single().Cell;
        var revision = owner.Version.Revision;
        var result = owner.TransformPlacedItem(owner.Version, item, Critter);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, item), (result.Disposition, result.Failure, result.Destroyed));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the creation and the destroy are one revision");
        var critter = result.Critter!;
        Assert.AreEqual((SessionCreatedKind.Critter, "oid:critter:1", Critter, Exterior, cell), (critter.Kind, critter.Entity, critter.Prototype, critter.Map, critter.Cell));
        Assert.AreEqual(critter, owner.QueryCreatedObject(critter.Entity));
        Assert.AreEqual(0, owner.QueryPlacedItems().Count, "the item no longer lies there");
        Assert.AreEqual(0, owner.QueryObjectFlags(critter.Entity).CritterSet, "the plain creator writes no encounter flag (the create-object action's alone does)");
    }

    [TestMethod]
    public void A_critter_the_transform_made_survives_a_save()
    {
        using var owner = Create();
        var item = Lay(owner);
        var critter = owner.TransformPlacedItem(owner.Version, item, Critter).Critter!;
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(critter, back.QueryCreatedObject(critter.Entity));
        Assert.AreEqual(0, back.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void An_item_that_does_not_lie_on_the_ground_a_stale_version_and_a_bad_prototype_change_nothing()
    {
        using var owner = Create();
        var item = Lay(owner);
        var stale = owner.Version;
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.TransformPlacedItem(owner.Version, "made:99", Critter).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.TransformPlacedItem(owner.Version, "not an entity", Critter).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.TransformPlacedItem(owner.Version, item, 0).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.TransformPlacedItem(owner.Version, item, SessionCreatedObject.MaxPrototype + 1).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(1, owner.QueryPlacedItems().Count, "the item is still there");
        owner.TransformPlacedItem(owner.Version, item, Critter);
        var second = Lay(owner);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.TransformPlacedItem(stale, second, Critter).Failure);
    }
}
