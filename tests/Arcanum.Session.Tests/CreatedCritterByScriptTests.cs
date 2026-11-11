using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-371 (E1-c3, ledger PG1, PG2, PG12): a script's create object of a critter prototype makes a created critter on the tile search the action runs, with the critter flag 0x20000 written on it, in one revision.
/// Invented prototype numbers, maps and cells only.
/// </summary>
[TestClass]
public sealed class CreatedCritterByScriptTests
{
    // Every step is open: a path of one step a tile.
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

    private const int Prototype = 28359;

    [TestMethod]
    public void A_critter_is_made_within_three_tiles_of_the_object_with_the_encounter_flag_in_one_revision()
    {
        using var owner = Create();
        var near = new CellCoord(100, 201);
        var revision = owner.Version.Revision;
        var made = owner.CreateCritterNear(owner.Version, Prototype, Exterior, near, new OpenMap());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (made.Disposition, made.Failure));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the creation and its flag are one revision");
        var critter = made.Object!;
        Assert.AreEqual((SessionCreatedKind.Critter, "oid:critter:1", Prototype, Exterior), (critter.Kind, critter.Entity, critter.Prototype, critter.Map));
        Assert.IsTrue(Math.Abs(critter.Cell.X - near.X) <= 3 && Math.Abs(critter.Cell.Y - near.Y) <= 3, "the tile search reaches at most 3 tiles (PG12)");
        Assert.AreEqual(critter, owner.QueryCreatedObject(critter.Entity));
        Assert.AreEqual(critter.Cell, owner.QueryObject(critter.Entity)!.Cell);
        // Only the critter flag 0x20000 is written (PG1), the spell and object flags are untouched.
        var flags = owner.QueryObjectFlags(critter.Entity);
        Assert.AreEqual((SessionCreatedObject.EncounterCritterFlag, 0, 0), (flags.CritterSet, flags.SpellSet, flags.ObjectSet));
        Assert.AreEqual(0x20000 | 0x8, flags.Critter(0x8));
    }

    [TestMethod]
    public void The_tile_comes_from_the_sessions_generator_so_the_same_save_makes_the_same_critters()
    {
        using var a = Create();
        using var b = Create();
        var first = new List<CellCoord>();
        var second = new List<CellCoord>();
        for (var i = 0; i < 6; i++)
        {
            first.Add(a.CreateCritterNear(a.Version, Prototype, Exterior, new(100, 201), new OpenMap()).Object!.Cell);
            second.Add(b.CreateCritterNear(b.Version, Prototype, Exterior, new(100, 201), new OpenMap()).Object!.Cell);
        }
        CollectionAssert.AreEqual(first, second);
        Assert.IsTrue(first.Distinct().Count() > 1, "the draws differ from try to try");
    }

    [TestMethod]
    public void The_flag_and_the_critter_survive_a_save_and_go_when_the_critter_is_destroyed()
    {
        using var owner = Create();
        var critter = owner.CreateCritterNear(owner.Version, Prototype, Exterior, new(100, 201), new OpenMap()).Object!;
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(critter, back.QueryCreatedObject(critter.Entity));
        Assert.AreEqual(owner.QueryObjectFlags(critter.Entity), back.QueryObjectFlags(critter.Entity));
        Assert.AreEqual(SessionCreatedObject.EncounterCritterFlag, back.QueryObjectFlags(critter.Entity).CritterSet);
        // The destroy drops the written flags too, so a long run of created critters does not fill the written-flags bound.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critter.Entity).Disposition);
        Assert.IsTrue(owner.QueryObjectFlags(critter.Entity).IsEmpty);
        Assert.AreEqual(0, owner.QueryAllObjectFlags().Count);
    }

    [TestMethod]
    public void A_refused_creation_draws_nothing_and_changes_nothing()
    {
        using var owner = Create();
        var version = owner.Version;
        var stale = owner.CreateCritterNear(version with { Revision = version.Revision + 1 }, Prototype, Exterior, new(100, 201), new OpenMap());
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (stale.Disposition, stale.Failure));
        var invalid = owner.CreateCritterNear(owner.Version, 0, Exterior, new(100, 201), new OpenMap());
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidCreatedObject), (invalid.Disposition, invalid.Failure));
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(0, owner.QueryCreatedObjects(Exterior).Count);
        // The bound of created objects counts the whole step: a run of N creations is refused before the first when it would pass the bound.
        for (var i = 0; i < SessionCreatedObject.MaxObjects - 1; i++) Assert.AreEqual(GameSessionDisposition.Changed, owner.CreateObject(owner.Version, SessionCreatedKind.Scenery, 4100, Exterior, new(i, 0)).Disposition);
        Assert.IsNull(owner.QueryCreateObjectRefusal(SessionCreatedKind.Critter, Prototype, Exterior, new(100, 201), count: 1));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.QueryCreateObjectRefusal(SessionCreatedKind.Critter, Prototype, Exterior, new(100, 201), count: 2));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CreateCritterNear(owner.Version, Prototype, Exterior, new(100, 201), new OpenMap()).Disposition);
        var full = owner.CreateCritterNear(owner.Version, Prototype, Exterior, new(100, 201), new OpenMap());
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (full.Disposition, full.Failure));
    }
}
