using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-347 (E1-a, TM4, GR4): a scenery or eye-candy object the session creates from a prototype is a created object of the directory (<c>scenery:n</c>, <c>eyecandy:n</c>), made and
/// destroyed through the session, found by the directory, a resident of its map and sector, saved with the objects section (schema 85) and refused from a damaged save. Invented
/// prototype numbers, maps and cells only.
/// </summary>
[TestClass]
public sealed class CreatedObjectTests
{
    private static GameSessionOwner Owner() => Create();

    private static SessionCreatedObject Made(GameSessionOwner owner, SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX = 0, int offsetY = 0)
    {
        var made = owner.CreateObject(owner.Version, kind, prototype, map, cell, offsetX, offsetY);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (made.Disposition, made.Failure));
        return made.Object!;
    }

    [TestMethod]
    public void A_scenery_and_an_eye_candy_object_take_numbers_of_one_counter_and_the_directory_finds_them()
    {
        using var owner = Owner();
        var revision = owner.Version.Revision;
        var scenery = Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(100, 201), 3, -4);
        var candy = Made(owner, SessionCreatedKind.EyeCandy, 5200, Exterior, new(101, 201));
        Assert.AreEqual(revision + 2, owner.Version.Revision, "one revision each");
        Assert.AreEqual("scenery:1", scenery.Entity);
        Assert.AreEqual("eyecandy:2", candy.Entity, "one counter for both kinds");
        Assert.AreEqual((4100, 3, -4, Exterior, new CellCoord(100, 201)), (scenery.Prototype, scenery.OffsetX, scenery.OffsetY, scenery.Map, scenery.Cell));
        Assert.AreEqual(SessionObjectOrigin.Scenery, GameSessionOwner.ObjectOriginOf(scenery.Entity));
        Assert.AreEqual(SessionObjectOrigin.EyeCandy, GameSessionOwner.ObjectOriginOf(candy.Entity));
        var entry = owner.QueryObject(scenery.Entity)!;
        Assert.AreEqual((SessionObjectOrigin.Scenery, SessionObjectPlace.Ground, Exterior, new CellCoord(100, 201)), (entry.Origin, entry.Place, entry.Map, entry.Cell));
        CollectionAssert.AreEqual(new[] { scenery.Entity }, owner.QueryObjects(SessionObjectOrigin.Scenery).Where(e => e.Origin == SessionObjectOrigin.Scenery).Select(e => e.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { candy.Entity }, owner.QueryObjects(SessionObjectOrigin.EyeCandy).Select(e => e.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { scenery, candy }, owner.QueryCreatedObjects(Exterior).ToArray());
        Assert.AreEqual(0, owner.QueryCreatedObjects(Cave).Count, "map-local: nothing lies on another map");
        Assert.AreEqual(candy, owner.QueryCreatedObject(candy.Entity));
        Assert.IsNull(owner.QueryCreatedObject("scenery:9"));
    }

    [TestMethod]
    public void An_object_is_created_on_a_map_that_is_not_the_open_one()
    {
        using var owner = Owner();
        var made = Made(owner, SessionCreatedKind.Scenery, 4100, Cave, new(5, 6));
        Assert.AreEqual(Cave, owner.QueryObject(made.Entity)!.Map);
        Assert.AreEqual(1, owner.QueryCreatedObjects(Cave).Count);
    }

    [TestMethod]
    public void Destroy_goes_through_the_one_destroy_path_and_a_number_is_not_given_again()
    {
        using var owner = Owner();
        var first = Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1));
        var revision = owner.Version.Revision;
        var gone = owner.DestroyCreatedObject(owner.Version, first.Entity);
        Assert.AreEqual((GameSessionDisposition.Changed, first), (gone.Disposition, gone.Object));
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.IsNull(owner.QueryObject(first.Entity), "a destroyed object is nowhere");
        var again = owner.DestroyCreatedObject(owner.Version, first.Entity);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (again.Disposition, again.Failure));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "a destroy of nothing is no revision");
        Assert.AreEqual("scenery:2", Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1)).Entity);
        Assert.AreEqual("eyecandy:3", Made(owner, SessionCreatedKind.EyeCandy, 5200, Exterior, new(1, 1)).Entity, "the counter is shared");
    }

    [TestMethod]
    public void A_refused_creation_or_destroy_changes_nothing()
    {
        using var owner = Owner();
        var version = owner.Version;
        foreach (var (kind, prototype, map, cell, ox, oy) in new (SessionCreatedKind, int, SessionMapId, CellCoord, int, int)[]
        {
            ((SessionCreatedKind)9, 4100, Exterior, new(1, 1), 0, 0), (SessionCreatedKind.Scenery, 0, Exterior, new(1, 1), 0, 0),
            (SessionCreatedKind.Scenery, SessionCreatedObject.MaxPrototype + 1, Exterior, new(1, 1), 0, 0), (SessionCreatedKind.Scenery, 4100, new(0), new(1, 1), 0, 0),
            (SessionCreatedKind.Scenery, 4100, Exterior, new(-1, 1), 0, 0), (SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1 << 24), 0, 0),
            (SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1), SessionCreatedObject.MaxOffset + 1, 0), (SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1), 0, int.MinValue),
        })
        {
            Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.QueryCreateObjectRefusal(kind, prototype, map, cell, ox, oy));
            var refused = owner.CreateObject(version, kind, prototype, map, cell, ox, oy);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidCreatedObject, (SessionCreatedObject?)null), (refused.Disposition, refused.Failure, refused.Object));
        }
        Assert.IsNull(owner.QueryCreateObjectRefusal(SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1)));
        var first = Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1));
        var stale = owner.CreateObject(version, SessionCreatedKind.Scenery, 4100, Exterior, new(2, 2));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (stale.Disposition, stale.Failure));
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DestroyCreatedObject(version, first.Entity).Failure);
        foreach (var bad in new[] { "", "kit:1", "scenery:0", "scenery:01", "scenery:x", "pool:1", "oid:a" })
            Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.DestroyCreatedObject(owner.Version, bad).Failure, bad);
        Assert.AreEqual(1, owner.QueryCreatedObjects(Exterior).Count);
    }

    [TestMethod]
    public void The_count_is_bounded()
    {
        using var owner = Owner();
        for (var i = 0; i < SessionCreatedObject.MaxObjects; i++) Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(i, 0));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.QueryCreateObjectRefusal(SessionCreatedKind.EyeCandy, 5200, Exterior, new(0, 0)));
        var refused = owner.CreateObject(owner.Version, SessionCreatedKind.EyeCandy, 5200, Exterior, new(0, 0));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (refused.Disposition, refused.Failure));
    }

    [TestMethod]
    public void A_summoned_piece_keeps_its_summoner_run_and_flags_through_a_save_and_the_pieces_go_together()
    {
        using var owner = Owner();
        var pieces = new List<SessionCreatedObject>();
        for (var i = 0; i < 5; i++)
        {
            var made = owner.CreateObject(owner.Version, SessionCreatedKind.Scenery, 4051, Exterior, new(100 + i, 201), 0, 0, SessionCreatedObject.SummonedFlag, "pc", 7);
            Assert.AreEqual(GameSessionDisposition.Changed, made.Disposition, made.Failure.ToString());
            pieces.Add(made.Object!);
        }
        var plain = Made(owner, SessionCreatedKind.Scenery, 4052, Exterior, new(1, 1));
        Assert.IsTrue(pieces.All(p => (p.SpellFlags, p.Summoner, p.Run) == (0x4000u, "pc", 7)));
        Assert.AreEqual((0u, (string?)null, (int?)null), (plain.SpellFlags, plain.Summoner, plain.Run));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"spellFlags\":16384,\"summoner\":\"pc\",\"run\":7}");
        StringAssert.Contains(text, "\"spellFlags\":0,\"summoner\":\"\",\"run\":-1}");
        using var back = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryCreatedObjects(Exterior).ToArray(), back.QueryCreatedObjects(Exterior).ToArray());
        // One Destroy, all five: one revision; a piece already gone is skipped; an entity that names no created object refuses the whole step.
        var revision = back.Version.Revision;
        var list = pieces.Select(p => p.Entity).Append("scenery:99").ToArray();
        var gone = back.DestroyCreatedObjects(back.Version, list);
        Assert.AreEqual(GameSessionDisposition.Changed, gone.Disposition);
        CollectionAssert.AreEqual(pieces.ToArray(), gone.Objects.ToArray());
        Assert.AreEqual(revision + 1, back.Version.Revision);
        CollectionAssert.AreEqual(new[] { plain }, back.QueryCreatedObjects(Exterior).ToArray());
        Assert.AreEqual(GameSessionDisposition.NoChange, back.DestroyCreatedObjects(back.Version, list).Disposition, "all gone already");
        Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, back.DestroyCreatedObjects(back.Version, [plain.Entity, "kit:1"]).Failure);
        Assert.AreEqual(1, back.QueryCreatedObjects(Exterior).Count, "a refused step destroys nothing");
        Assert.AreEqual(GameSessionFailure.StaleVersion, back.DestroyCreatedObjects(owner.Version, [plain.Entity]).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.CreateObject(owner.Version, SessionCreatedKind.Scenery, 4051, Exterior, new(1, 1), 0, 0, 0, "", null).Failure, "an empty summoner");
        Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.CreateObject(owner.Version, SessionCreatedKind.Scenery, 4051, Exterior, new(1, 1), 0, 0, 0, null, -3).Failure, "a negative run");
        // A damaged summoner or run is refused at the restore.
        Assert.IsFalse(Loads(text.Replace("\"summoner\":\"pc\"", "\"summoner\":\"bad entity\"")), "a summoner that is no entity");
        Assert.IsFalse(Loads(text.Replace("\"run\":7", "\"run\":-2")), "a run below none");
    }

    [TestMethod]
    public void The_counter_stops_at_the_integer_bound()
    {
        using var owner = Owner();
        Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture()).Replace("\"lastCreatedNumber\":1", $"\"lastCreatedNumber\":{int.MaxValue - 1}");
        using var back = Restore(Decode(text));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, back.QueryCreateObjectRefusal(SessionCreatedKind.Scenery, 4100, Exterior, new(2, 2)), "the number int.MaxValue is reserved");
        Assert.AreEqual(GameSessionDisposition.Rejected, back.CreateObject(back.Version, SessionCreatedKind.EyeCandy, 5200, Exterior, new(2, 2)).Disposition);
    }

    [TestMethod]
    public void The_created_objects_are_residents_of_their_map_and_sector()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(ResidencyStatus.Open, owner.OpenResidency(Exterior, [new SessionResidentRow("oid:guard", SessionResidentKind.Npc, new CellCoord(10, 20))]).Status);
        var near = Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(12, 22));
        var far = Made(owner, SessionCreatedKind.EyeCandy, 5200, Exterior, new(130, 200));
        var elsewhere = Made(owner, SessionCreatedKind.Scenery, 4100, Cave, new(12, 22));
        var all = owner.QueryResidents(Exterior).Residents;
        Assert.AreEqual(new SessionResident(near.Entity, SessionResidentKind.Scenery, SessionResidentOrigin.Created, near.Cell, null, false, false), all.Single(r => r.Entity == near.Entity));
        Assert.IsTrue(all.Any(r => r.Entity == far.Entity));
        Assert.IsFalse(all.Any(r => r.Entity == elsewhere.Entity), "another map's object is no resident here");
        var sector = all.Single(r => r.Entity == near.Entity).Sector!.Value;
        CollectionAssert.Contains(owner.QueryResidentsIn(Exterior, sector).Residents.Select(r => r.Entity).ToArray(), near.Entity);
        CollectionAssert.DoesNotContain(owner.QueryResidentsIn(Exterior, sector).Residents.Select(r => r.Entity).ToArray(), far.Entity);
        Assert.IsTrue(owner.DestroyCreatedObject(owner.Version, near.Entity).Disposition == GameSessionDisposition.Changed);
        Assert.IsFalse(owner.QueryResidents(Exterior).Residents.Any(r => r.Entity == near.Entity), "a destroyed object is no resident");
    }

    [TestMethod]
    public void The_objects_and_the_counter_survive_a_save_and_a_load()
    {
        using var owner = Owner();
        Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(100, 201), 3, -4);
        var second = Made(owner, SessionCreatedKind.EyeCandy, 5200, Cave, new(7, 8));
        Assert.IsTrue(owner.DestroyCreatedObject(owner.Version, second.Entity).Disposition == GameSessionDisposition.Changed);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"created\":[{\"entity\":\"scenery:1\",\"kind\":0,\"prototype\":4100,\"map\":79,\"x\":100,\"y\":201,\"offsetX\":3,\"offsetY\":-4,\"createdMs\":");
        StringAssert.Contains(text, "\"lastCreatedNumber\":2}");
        var decoded = Decode(text);
        using var back = Restore(decoded);
        CollectionAssert.AreEqual(owner.QueryCreatedObjects(Exterior).ToArray(), back.QueryCreatedObjects(Exterior).ToArray());
        Assert.AreEqual(0, back.QueryCreatedObjects(Cave).Count);
        Assert.AreEqual("scenery:3", Made(back, SessionCreatedKind.Scenery, 4100, Exterior, new(1, 1)).Entity, "the mark of a destroyed number holds");
        using var again = Restore(decoded);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(again.Capture()), "a restored session saves the same text");
    }

    [TestMethod]
    public void A_schema_84_save_loads_with_no_created_object_and_the_counter_at_zero()
    {
        using var owner = Owner();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema84(text);
        StringAssert.Contains(older, "\"schemaVersion\":84,");
        Assert.IsFalse(older.Contains("\"created\"", StringComparison.Ordinal));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(Decode(older)), "the upgrade is exact");
        var twice = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"state\":[]", ",\"state\":[],\"created\":[],\"lastCreatedNumber\":0"));
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, twice.Failure, "a schema 84 text with the new fields is not one: " + twice.Path);
    }

    // Whether a text decodes and restores.
    private static bool Loads(string text) =>
        GameSessionSnapshotCodec.TryDecode(text) is { Failure: SnapshotDecodeFailure.None, Snapshot: { } snapshot } && GameSessionOwner.Restore(snapshot, RestoreOptions()).Owner is not null;

    [TestMethod]
    public void A_damaged_save_is_refused()
    {
        using var owner = Owner();
        Made(owner, SessionCreatedKind.Scenery, 4100, Exterior, new(100, 201));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.IsTrue(Loads(text));
        var row = "{\"entity\":\"scenery:1\",\"kind\":0,\"prototype\":4100,\"map\":79,\"x\":100,\"y\":201,\"offsetX\":0,\"offsetY\":0,";
        StringAssert.Contains(text, row);
        void Refuses(string replacement, string why) => Assert.IsFalse(Loads(text.Replace(row, replacement)), why);
        Refuses(row.Replace("scenery:1", "eyecandy:1"), "an entity of the other kind's prefix");
        Refuses(row.Replace("\"kind\":0", "\"kind\":2"), "a kind that is none");
        Refuses(row.Replace("4100", "0"), "no prototype");
        Refuses(row.Replace("4100", "1000000"), "a prototype out of range");
        Refuses(row.Replace("\"x\":100", "\"x\":-1"), "a negative cell");
        Refuses(row.Replace("\"offsetX\":0", "\"offsetX\":999"), "an offset out of range");
        Refuses(row.Replace("scenery:1", "scenery:01"), "an entity that is not canonical");
        Refuses(row.Replace("\"map\":79", "\"map\":0"), "no map");
        Assert.IsFalse(Loads(System.Text.RegularExpressions.Regex.Replace(text, "(\"scenery:1\".*?\"createdMs\":)\\d+", "${1}9999999999999")), "a created time after the clock");
        var tail = text.IndexOf("\"createdMs\":", StringComparison.Ordinal);
        var end = text.IndexOf('}', tail);
        var created = text[(text.IndexOf(row, StringComparison.Ordinal))..(end + 1)];
        Assert.IsFalse(Loads(text.Replace(created, created + "," + created)), "the same entity twice");
        Assert.IsFalse(Loads(text.Replace("\"lastCreatedNumber\":1", "\"lastCreatedNumber\":-1")), "a negative counter");
        Assert.IsTrue(Loads(text.Replace("\"lastCreatedNumber\":1", "\"lastCreatedNumber\":5")), "a counter above its numbers holds a destroyed one's");
    }
}
