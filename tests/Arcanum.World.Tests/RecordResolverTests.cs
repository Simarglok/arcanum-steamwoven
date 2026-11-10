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

    // A window of map m: NPC holders with the indexes first..first+n-1 (another window of one map lists other NPCs).
    private static AreaInventoryMap Window(int map, int first, int npcs = 2) => new(map,
        new(Enumerable.Range(first, npcs).Select(i => new SessionSeedHolder(Entity(map, i), SessionHolderKind.Npc, false, false, [], [])).ToArray(), []),
        Enumerable.Range(first, npcs).ToDictionary(i => Entity(map, i), i => Row(map, i)));

    private sealed class Source(params AreaInventoryMap[] maps) : IMapRecordSource
    {
        public readonly List<int> Asked = [];
        public readonly List<int> AskedAround = [];
        public Func<int, AreaInventoryMap?>? Override;
        public Func<int, AreaInventoryMap?>? Around;

        public MapRecordsLoad LoadAround(int runtimeMap, CancellationToken token)
        {
            AskedAround.Add(runtimeMap);
            if (Around is not { } custom) return Load(runtimeMap, token);
            return custom(runtimeMap) is { } found ? new(found, string.Empty) : new(null, $"map {runtimeMap} has no other window");
        }

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
        // Map 2 is held but lacks the entity: its other window is asked once (ARC-DEV-337), and the unread map 8 once.
        CollectionAssert.AreEqual(new[] { 7, 2, 8 }, source.Asked, "the unread map was asked once");
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

    [TestMethod]
    public void A_source_that_throws_is_an_unread_map_asked_once_and_a_limit_refusal_keeps_its_status()
    {
        var calls = 0;
        var throwing = new Source { Override = _ => { calls++; throw new InvalidOperationException("boom"); } };
        var resolver = new RecordResolver([Map(1)], throwing);
        Assert.AreEqual(RecordStatus.HomeUnread, resolver.Resolve(Entity(7, 0), home: 7).Status);
        Assert.AreEqual(RecordStatus.HomeUnread, resolver.Resolve(Entity(7, 0), home: 7).Status);
        Assert.AreEqual(1, calls);
        Assert.ThrowsExactly<OperationCanceledException>(() => new RecordResolver([Map(1)], new Source { Override = _ => throw new OperationCanceledException() }).Resolve(Entity(7, 0), home: 7));

        var big = new RecordResolver([Map(1)], new Source(Map(3, 4)), new(MaxMaps: 4, MaxRowsPerMap: 3));
        Assert.AreEqual(RecordStatus.LimitExceeded, big.Resolve(Entity(3, 0), home: 3).Status);
        Assert.AreEqual(RecordStatus.LimitExceeded, big.Resolve(Entity(3, 0), home: 3).Status, "asked again: the same status");
    }

    [TestMethod]
    public void An_entity_listed_by_two_maps_stays_found_when_the_map_the_index_pointed_at_is_evicted()
    {
        // The same entity on maps 3 and 4 (an id the session forbids for items, but the index must not lose it).
        var shared = Entity(3, 0);
        AreaInventoryMap Twin(int map) => new(map,
            new([new SessionSeedHolder(shared, SessionHolderKind.Npc, false, false, [], []), new SessionSeedHolder(Entity(map, 1), SessionHolderKind.Npc, false, false, [], [])], []),
            new Dictionary<string, AreaObjectRecord> { [shared] = Row(3, 0), [Entity(map, 1)] = Row(map, 1) });
        var resolver = new RecordResolver([Map(1)], new Source(Twin(3), Twin(4), Map(5)), new(MaxMaps: 3));
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(shared, home: 3).Status);
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(4, 1), home: 4).Status);
        Assert.AreEqual(3, resolver.HeldHome(shared), "the first map listed it");
        Assert.IsNotNull(resolver.Held(4), "map 4 is used last: map 3 is the least recently used");
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(5, 0), home: 5).Status);
        Assert.IsFalse(resolver.Holds(3));
        Assert.AreEqual(4, resolver.HeldHome(shared), "map 4 still lists it");
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(shared).Status);
    }

    [TestMethod]
    public void A_companion_of_another_window_of_a_held_map_is_found_in_the_window_the_source_reads_around_it_once()
    {
        // ARC-DEV-337 (B52): map 1 (the main world map) is held as the town window; the companion's own window lists other NPCs.
        var source = new Source { Around = _ => Window(1, 10) };
        var resolver = new RecordResolver([Window(1, 0)], source);
        var lookup = resolver.Resolve(Entity(1, 11), home: 1);
        Assert.AreEqual(RecordStatus.Found, lookup.Status, lookup.Detail);
        Assert.AreEqual(1, lookup.Record!.HomeMap);
        Assert.AreEqual(Key(1, 11), lookup.Record.Row.Key);
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(1, 10)).Status, "another NPC of that window resolves by identity alone");
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(1, 11), home: 1).Status);
        CollectionAssert.AreEqual(new[] { 1 }, source.AskedAround, "the window was read once");
        Assert.AreEqual(0, source.Asked.Count, "the whole-map read was not used");
        Assert.AreEqual(1, resolver.HeldHome(Entity(1, 0)), "the held window still answers its own NPCs");
        Assert.IsNull(resolver.HeldHome(Entity(1, 11)), "the extra window is not a held map");
        Assert.AreEqual(1, resolver.Maps.Count, "and not one of the maps the resolver holds");
    }

    [TestMethod]
    public void An_entity_no_other_window_lists_is_a_typed_miss_asked_once_and_a_throwing_source_is_a_miss()
    {
        var source = new Source { Around = _ => Window(1, 10) };
        var resolver = new RecordResolver([Window(1, 0)], source);
        Assert.AreEqual(RecordStatus.UnknownEntity, resolver.Resolve(Entity(1, 20), home: 1).Status);
        Assert.AreEqual(RecordStatus.UnknownEntity, resolver.Resolve(Entity(1, 20), home: 1).Status);
        CollectionAssert.AreEqual(new[] { 1 }, source.AskedAround, "the miss is remembered for the map and the entity");
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(1, 10), home: 1).Status, "another entity asks again and finds its window");

        var none = new Source { Around = _ => null };
        var plain = new RecordResolver([Window(1, 0)], none);
        Assert.AreEqual(RecordStatus.UnknownEntity, plain.Resolve(Entity(1, 10), home: 1).Status, "a source with no other window");
        Assert.AreEqual(RecordStatus.UnknownEntity, new RecordResolver([Window(1, 0)]).Resolve(Entity(1, 10), home: 1).Status, "no source");

        var throwing = new Source { Around = _ => throw new InvalidOperationException("boom") };
        Assert.AreEqual(RecordStatus.UnknownEntity, new RecordResolver([Window(1, 0)], throwing).Resolve(Entity(1, 10), home: 1).Status);

        var wrong = new Source { Around = _ => Window(2, 10) };
        Assert.AreEqual(RecordStatus.UnknownEntity, new RecordResolver([Window(1, 0)], wrong).Resolve(Entity(2, 10), home: 1).Status, "a window of another map is refused");
    }

    [TestMethod]
    public void The_extra_window_is_replaced_by_a_later_read_of_the_same_map()
    {
        var around = Window(1, 10);
        var source = new Source { Around = _ => around };
        var resolver = new RecordResolver([Window(1, 0)], source);
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(1, 10), home: 1).Status);
        around = Window(1, 30);
        Assert.AreEqual(RecordStatus.Found, resolver.Resolve(Entity(1, 31), home: 1).Status);
        Assert.AreEqual(RecordStatus.UnknownEntity, resolver.Resolve(Entity(1, 10)).Status, "the first window left");
    }
}
