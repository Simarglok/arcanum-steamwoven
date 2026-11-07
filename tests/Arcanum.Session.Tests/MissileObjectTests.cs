using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-139 (MIG13 S8, docs/architecture/goal-table.md §3.5, TM4): a spell missile is a created object of the directory (<c>missile:n</c>), made and destroyed through the session, moved by its
/// flight, owner of its goal run, cleared with its map, and never saved: a snapshot taken with a missile in flight is the one taken without it, in the same schema and revision. Invented entities only.
/// </summary>
[TestClass]
public sealed class MissileObjectTests
{
    private sealed class Ports : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    [TestMethod]
    public void A_missile_is_created_on_the_active_map_with_the_next_number_and_found_by_the_directory()
    {
        using var owner = Create();
        var first = owner.CreateMissile(new(100, 200));
        var second = owner.CreateMissile(new(101, 200));
        Assert.AreEqual("missile:1", first);
        Assert.AreEqual("missile:2", second);
        Assert.AreEqual(SessionObjectOrigin.Missile, GameSessionOwner.ObjectOriginOf(first!));
        var entry = owner.QueryObject(first!)!;
        Assert.AreEqual((SessionObjectOrigin.Missile, SessionObjectPlace.Ground, Start.Map, new CellCoord(100, 200)), (entry.Origin, entry.Place, entry.Map, entry.Cell));
        CollectionAssert.AreEqual(new[] { first, second }, owner.QueryObjects(SessionObjectOrigin.Missile).Select(e => e.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { first, second }, owner.QueryMissiles().Select(m => m.Entity).ToArray());
    }

    [TestMethod]
    public void A_missile_moves_with_pixel_offsets_and_destroys_through_the_one_destroy_path()
    {
        using var owner = Create();
        var id = owner.CreateMissile(new(100, 200))!;
        Assert.IsTrue(owner.MoveMissile(id, new(103, 201), -7, 12));
        var moved = owner.QueryMissile(id)!;
        Assert.AreEqual((new CellCoord(103, 201), -7, 12), (moved.Cell, moved.OffsetX, moved.OffsetY));
        Assert.AreEqual(new CellCoord(103, 201), owner.QueryObject(id)!.Cell);
        Assert.IsFalse(owner.MoveMissile("missile:99", new(1, 1), 0, 0), "none of that entity");
        Assert.IsFalse(owner.MoveMissile(id, new(-1, 5), 0, 0), "a cell out of range");
        Assert.IsTrue(owner.DestroyMissile(id));
        Assert.IsFalse(owner.DestroyMissile(id), "destroyed once");
        Assert.IsNull(owner.QueryObject(id), "a destroyed missile is nowhere");
        Assert.AreEqual(0, owner.QueryMissiles().Count);
        Assert.AreEqual("missile:2", owner.CreateMissile(new(1, 1)), "a number is not given again");
    }

    [TestMethod]
    public void The_cell_and_the_count_are_bounded()
    {
        using var owner = Create();
        Assert.IsNull(owner.CreateMissile(new(-1, 0)));
        Assert.IsNull(owner.CreateMissile(new(0, 1 << 24)));
        for (var i = 0; i < SessionMissile.MaxMissiles; i++) Assert.IsNotNull(owner.CreateMissile(new(i, 0)));
        Assert.IsNull(owner.CreateMissile(new(0, 0)), "as many missiles as the table has runs");
    }

    [TestMethod]
    public void A_missile_owns_a_goal_run_and_a_map_change_ends_the_run_and_clears_the_missile()
    {
        using var owner = Create();
        var id = owner.CreateMissile(new(100, 200))!;
        var started = owner.StartGoal(new GoalStartRequest(id, GoalStarters.Generic, new GoalFrame(GoalType.ShootSpell, null, new CellCoord(104, 200))), new Ports());
        Assert.IsTrue(started.Started);
        Assert.AreEqual(1, owner.QueryGoals().RunsOf(id).Length);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);
        Assert.AreEqual(0, owner.QueryGoals().InUseCount);
        Assert.AreEqual(0, owner.QueryMissiles().Count, "the missile object goes with the map it flew on");
        var ends = owner.DrainGoalEnds();
        Assert.AreEqual(id, ends.Single().Run.Owner, "the run's end is reported for its host's end node");
    }

    [TestMethod]
    public void A_missile_in_flight_changes_no_snapshot_revision_or_schema()
    {
        using var owner = Create();
        var before = GameSessionSnapshotCodec.Encode(owner.Capture());
        var version = owner.Version;
        var id = owner.CreateMissile(new(100, 200))!;
        owner.MoveMissile(id, new(102, 200), 3, -3);
        owner.StartGoal(new GoalStartRequest(id, GoalStarters.Generic, new GoalFrame(GoalType.ShootSpell, null, new CellCoord(104, 200))), new Ports());
        Assert.AreEqual(version, owner.Version, "no revision");
        var capture = owner.Capture();
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, capture.SchemaVersion);
        var text = GameSessionSnapshotCodec.Encode(capture);
        Assert.AreEqual(Unsequenced(before), Unsequenced(text), "a snapshot with a missile in flight is the one without it");
        StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex("missile"));
        using var restored = Restore(Decode(text));
        Assert.AreEqual(0, restored.QueryMissiles().Count, "a load starts with none");
        Assert.AreEqual(0, restored.QueryGoals().InUseCount);
    }
}
