using Arcanum.Data.Area;
using Arcanum.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-072 (MIG1, TM3): the record resolver over invented maps, no original data. An entity's base record is found by identity in
/// any map the resolver holds; otherwise the map the caller names as its home is read from the source on demand, bounded by the
/// limits, and a map the source cannot read is asked once.
/// </summary>
[TestClass]
public sealed class RecordResolverTests
{
    // Entity n of map m: a distinct key and id per (map, index).
    private static AreaObjectKey Key(int map, int index) => new(new string('a', 64), map, AreaSourceKind.Mobile, 1, index, index * 100L, (map * 100 + index).ToString("x").PadLeft(64, '0'), 100);
    private static string Entity(int map, int index) => "oid:" + Key(map, index).ObjectIdSha256;

    private static AreaObjectRecord Row(int map, int index) => new(Key(map, index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    // Map m with n NPC holders (indexes 0..n-1).
    private static AreaInventoryMap Map(int map, int npcs = 2) => new(map,
        new(Enumerable.Range(0, npcs).Select(i => new SessionSeedHolder(Entity(map, i), SessionHolderKind.Npc, false, false, [], [])).ToArray(), []),
        Enumerable.Range(0, npcs).ToDictionary(i => Entity(map, i), i => Row(map, i)));

    private sealed class Source(params AreaInventoryMap[] maps) : IMapRecordSource
    {
        public readonly List<int> Asked = [];
        public Func<int, AreaInventoryMap?>? Override;

        public MapRecordsLoad Load(int runtimeMap, CancellationToken token)
        {
            Asked.Add(runtimeMap);
            var found = Override is { } custom ? custom(runtimeMap) : maps.FirstOrDefault(map => map.RuntimeMap == runtimeMap);
            return found is null ? new(null, $"map {runtimeMap} unreadable") : new(found, string.Empty);
        }
    }

    [TestMethod]
    public void A_held_map_resolves_its_entities_by_identity_without_asking_the_source()
    {
        var source = new Source(Map(2));
        var resolver = new RecordResolver([Map(1), Map(3)], source);
        var found = resolver.Resolve(Entity(3, 1));
        Assert.AreEqual(RecordStatus.Found, found.Status);
        Assert.AreEqual(3, found.Record!.HomeMap);
        Assert.AreEqual(Key(3, 1), found.Record.Row.Key);
        Assert.AreEqual(1, resolver.HeldHome(Entity(1, 0)));
        Assert.AreEqual(0, source.Asked.Count);
    }

    [TestMethod]
    public void An_entity_of_a_map_never_held_is_read_on_demand_from_its_named_home_once()
    {
        var source = new Source(Map(7, 3));
        var resolver = new RecordResolver([Map(2)], source);
        Assert.IsNull(resolver.HeldHome(Entity(7, 2)));
        Assert.AreEqual(RecordStatus.UnknownEntity, resolver.Resolve(Entity(7, 2)).Status, "no home named: nothing is read");
        Assert.AreEqual(0, source.Asked.Count);

        var found = resolver.Resolve(Entity(7, 2), home: 7);
        Assert.AreEqual(RecordStatus.Found, found.Status);
        Assert.AreEqual(7, found.Record!.HomeMap);
        Assert.IsTrue(resolver.Holds(7));
        Assert.IsTrue(resolver.IsRead(7));
        Assert.IsFalse(resolver.IsRead(2), "a given map is not one read on demand");
        Assert.AreEqual(7, resolver.HeldHome(Entity(7, 0)), "the whole map is held now");
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(7, 0)).Status);
        CollectionAssert.AreEqual(new[] { 7 }, source.Asked);
    }

    [TestMethod]
    public void A_home_that_does_not_list_the_entity_or_cannot_be_read_gives_a_typed_failure_and_is_not_asked_twice()
    {
        var source = new Source(Map(7));
        var resolver = new RecordResolver([Map(2)], source);
        Assert.AreEqual(RecordStatus.UnknownEntity, resolver.Resolve(Entity(9, 0), home: 7).Status, "map 7 does not list it");
        Assert.AreEqual(RecordStatus.UnknownEntity, resolver.Resolve(Entity(9, 0), home: 2).Status, "nor does the given map 2");

        Assert.AreEqual(RecordStatus.HomeUnread, resolver.Resolve(Entity(8, 0), home: 8).Status);
        Assert.AreEqual(RecordStatus.HomeUnread, resolver.Resolve(Entity(8, 1), home: 8).Status);
        CollectionAssert.AreEqual(new[] { 7, 8 }, source.Asked, "the unread map was asked once");
        Assert.AreEqual(RecordStatus.HomeUnread, new RecordResolver([Map(2)]).Resolve(Entity(7, 0), home: 7).Status, "no source");
    }

    [TestMethod]
    public void A_source_that_answers_another_map_is_refused()
    {
        var source = new Source { Override = _ => Map(5) };
        var resolver = new RecordResolver([Map(2)], source);
        Assert.AreEqual(RecordStatus.HomeUnread, resolver.Resolve(Entity(7, 0), home: 7).Status);
        Assert.IsFalse(resolver.Holds(5));
        Assert.IsFalse(resolver.Holds(7));
    }

    [TestMethod]
    public void Maps_read_on_demand_are_bounded_and_the_least_recently_used_leaves_first()
    {
        var source = new Source(Map(3), Map(4), Map(5));
        var resolver = new RecordResolver([Map(1)], source, new(MaxMaps: 3));
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(3, 0), home: 3).Status);
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(4, 0), home: 4).Status);
        Assert.IsNotNull(resolver.Held(3), "map 3 is used again");
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(5, 0), home: 5).Status);
        CollectionAssert.AreEqual(new[] { 1, 3, 5 }, resolver.Maps.Select(map => map.RuntimeMap).ToArray());
        Assert.IsNull(resolver.HeldHome(Entity(4, 0)), "map 4 went and took its entities with it");
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(4, 1), home: 4).Status, "and is read again when asked");
        CollectionAssert.AreEqual(new[] { 3, 4, 5, 4 }, source.Asked);
    }

    [TestMethod]
    public void Given_maps_that_fill_the_limit_leave_no_room_to_read_and_a_map_with_too_many_rows_is_refused()
    {
        var full = new RecordResolver([Map(1), Map(2)], new Source(Map(3)), new(MaxMaps: 2));
        Assert.AreEqual(RecordStatus.LimitExceeded, full.Resolve(Entity(3, 0), home: 3).Status);

        var rows = new Source(Map(3, 4));
        var small = new RecordResolver([Map(1)], rows, new(MaxMaps: 4, MaxRowsPerMap: 3));
        var refused = small.Resolve(Entity(3, 0), home: 3);
        Assert.AreEqual(RecordStatus.LimitExceeded, refused.Status);
        Assert.IsFalse(small.Holds(3), "a map over the row limit is never partly held");
        Assert.ThrowsExactly<ArgumentException>(() => new RecordResolver([Map(3, 4)], null, new(MaxMaps: 4, MaxRowsPerMap: 3)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RecordResolver([], null, new(MaxMaps: 0)));
        Assert.ThrowsExactly<ArgumentException>(() => new RecordResolver([Map(1), Map(1)]));
    }

    [TestMethod]
    public void Map_reads_a_map_by_number_through_the_same_bounds()
    {
        var source = new Source(Map(6));
        var resolver = new RecordResolver([Map(1)], source);
        Assert.AreEqual(1, resolver.Map(1).Map!.RuntimeMap);
        Assert.AreEqual(6, resolver.Map(6).Map!.RuntimeMap);
        Assert.AreEqual(RecordStatus.HomeUnread, resolver.Map(9).Status);
        CollectionAssert.AreEqual(new[] { 6, 9 }, source.Asked);
    }
}
