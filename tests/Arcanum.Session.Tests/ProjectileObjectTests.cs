using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-140 (MIG13 S8b, docs/architecture/goal-table.md §3.5, TM4): a projectile is a created object of the directory (<c>projectile:n</c>), made and destroyed through the session, moved by its
/// flight, owner of its goal run, cleared with its map, and never saved: a snapshot taken with a projectile in flight is the one taken without it, in the same schema and revision. Invented entities only.
/// </summary>
[TestClass]
public sealed class ProjectileObjectTests
{
    private sealed class Ports : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    [TestMethod]
    public void A_projectile_is_created_on_the_active_map_with_the_next_number_and_found_by_the_directory()
    {
        using var owner = Create();
        var first = owner.CreateProjectile(new(100, 200));
        var second = owner.CreateProjectile(new(101, 200));
        Assert.AreEqual("projectile:1", first);
        Assert.AreEqual("projectile:2", second);
        Assert.AreEqual(SessionObjectOrigin.Projectile, GameSessionOwner.ObjectOriginOf(first!));
        var entry = owner.QueryObject(first!)!;
        Assert.AreEqual((SessionObjectOrigin.Projectile, SessionObjectPlace.Ground, Start.Map, new CellCoord(100, 200)), (entry.Origin, entry.Place, entry.Map, entry.Cell));
        CollectionAssert.AreEqual(new[] { first, second }, owner.QueryObjects(SessionObjectOrigin.Projectile).Select(e => e.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { first, second }, owner.QueryProjectiles().Select(m => m.Entity).ToArray());
    }

    [TestMethod]
    public void A_projectile_moves_with_pixel_offsets_and_destroys_through_the_one_destroy_path()
    {
        using var owner = Create();
        var id = owner.CreateProjectile(new(100, 200))!;
        Assert.IsTrue(owner.MoveProjectile(id, new(103, 201), -7, 12));
        var moved = owner.QueryProjectile(id)!;
        Assert.AreEqual((new CellCoord(103, 201), -7, 12), (moved.Cell, moved.OffsetX, moved.OffsetY));
        Assert.AreEqual(new CellCoord(103, 201), owner.QueryObject(id)!.Cell);
        Assert.IsFalse(owner.MoveProjectile("projectile:99", new(1, 1), 0, 0), "none of that entity");
        Assert.IsFalse(owner.MoveProjectile(id, new(-1, 5), 0, 0), "a cell out of range");
        Assert.IsTrue(owner.DestroyProjectile(id));
        Assert.IsFalse(owner.DestroyProjectile(id), "destroyed once");
        Assert.IsNull(owner.QueryObject(id), "a destroyed projectile is nowhere");
        Assert.AreEqual(0, owner.QueryProjectiles().Count);
        Assert.AreEqual("projectile:2", owner.CreateProjectile(new(1, 1)), "a number is not given again");
    }

    [TestMethod]
    public void The_cell_and_the_count_are_bounded()
    {
        using var owner = Create();
        Assert.IsNull(owner.CreateProjectile(new(-1, 0)));
        Assert.IsNull(owner.CreateProjectile(new(0, 1 << 24)));
        for (var i = 0; i < SessionProjectile.MaxProjectiles; i++) Assert.IsNotNull(owner.CreateProjectile(new(i, 0)));
        Assert.IsNull(owner.CreateProjectile(new(0, 0)), "as many projectiles as the table has runs");
    }

    [TestMethod]
    public void A_projectile_owns_a_goal_run_and_a_map_change_ends_the_run_and_clears_the_projectile()
    {
        using var owner = Create();
        var id = owner.CreateProjectile(new(100, 200))!;
        var started = owner.StartGoal(new GoalStartRequest(id, GoalStarters.Projectile, new GoalFrame(GoalType.Projectile, null, new CellCoord(104, 200))), new Ports());
        Assert.IsTrue(started.Started);
        Assert.AreEqual(1, owner.QueryGoals().RunsOf(id).Length);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.AreEqual(0, owner.QueryGoals().InUseCount);
        Assert.AreEqual(0, owner.QueryProjectiles().Count, "the projectile object goes with the map it flew on");
        var ends = owner.DrainGoalEnds();
        Assert.AreEqual(id, ends.Single().Run.Owner, "the run's end is reported for its host's end node");
    }

    [TestMethod]
    public void The_one_destroy_path_removes_a_projectile_object_and_marks_nothing_else()
    {
        using var owner = Create();
        var id = owner.CreateProjectile(new(100, 200))!;
        var other = owner.CreateProjectile(new(101, 200))!;
        owner.DestroyObject(id);
        Assert.IsNull(owner.QueryProjectile(id));
        Assert.IsNull(owner.QueryObject(id), "a destroyed projectile is nowhere");
        Assert.AreEqual(other, owner.QueryProjectiles().Single().Entity, "the other projectile is untouched");
    }

    [TestMethod]
    public void A_projectile_in_flight_changes_no_snapshot_revision_or_schema()
    {
        using var owner = Create();
        var before = GameSessionSnapshotCodec.Encode(owner.Capture());
        var version = owner.Version;
        var id = owner.CreateProjectile(new(100, 200))!;
        owner.MoveProjectile(id, new(102, 200), 3, -3);
        owner.StartGoal(new GoalStartRequest(id, GoalStarters.Projectile, new GoalFrame(GoalType.Projectile, null, new CellCoord(104, 200))), new Ports());
        Assert.AreEqual(version, owner.Version, "no revision");
        var capture = owner.Capture();
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, capture.SchemaVersion);
        var text = GameSessionSnapshotCodec.Encode(capture);
        Assert.AreEqual(Uncounted(before), Uncounted(text), "a snapshot with a projectile in flight is the one without it");
        StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex("projectile"));
        using var restored = Restore(Decode(text));
        Assert.AreEqual(0, restored.QueryProjectiles().Count, "a load starts with none");
        Assert.AreEqual(0, restored.QueryGoals().InUseCount);
    }
}
