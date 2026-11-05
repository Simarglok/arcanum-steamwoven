using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>
/// ARC-DEV-117 (MIG9 S4b): the route fields of an NPC row (teleport map, NPC flags, waypoint list and index, day and night standpoints) over
/// invented rows, no original data. The slots are the NPC field table's positions (61, 69, 76..79).
/// </summary>
[TestClass]
public sealed class AreaNpcRouteTests
{
    private static AreaObjectKey Key() => new(new string('a', 64), 5, AreaSourceKind.Mobile, 1, 0, 0L, new string('0', 64), 100);

    private static AreaFieldValue Number(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    private static AreaFieldValue Waypoints(params (int Key, long X, long Y)[] elements) =>
        new(76, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. elements.Select(e => new AreaSparseElement(e.Key, [e.X, e.Y]))], null);

    private static AreaObjectRecord Row(int type, params AreaFieldValue[] fields) =>
        new(Key(), 0, type, 1, "p", "p", "r", "s", AreaScope.Core, null, 10, 20, AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);

    private static long Location(uint x, uint y) => unchecked((long)(((ulong)y << 32) | x));

    [TestMethod]
    public void A_full_route_is_read_in_list_order()
    {
        var read = AreaNpcRouteReader.Read(Row(16, Number(61, 7), Number(69, 0x6), Number(77, 1), Number(78, Location(100, 200)), Number(79, Location(300, 400)),
            Waypoints((1, 11, 12), (0, 5, 6), (2, 21, 22))));
        Assert.AreEqual(AreaNpcRouteStatus.Ready, read.Status, read.Detail);
        var route = read.Route!;
        Assert.AreEqual(7, route.TeleportMap);
        Assert.AreEqual(0x6u, route.NpcFlags);
        Assert.AreEqual(1, route.WaypointIndex);
        Assert.AreEqual(new AreaRouteLocation(100, 200), route.StandpointDay);
        Assert.AreEqual(new AreaRouteLocation(300, 400), route.StandpointNight);
        CollectionAssert.AreEqual(new[] { new AreaRouteLocation(5, 6), new(11, 12), new(21, 22) }, route.Waypoints.ToArray());
        Assert.IsTrue(route.HasStandpoints);
    }

    [TestMethod]
    public void Absent_fields_read_as_zero_and_an_empty_list()
    {
        var route = AreaNpcRouteReader.Read(Row(16)).Route!;
        Assert.AreEqual(0, route.TeleportMap);
        Assert.AreEqual(0u, route.NpcFlags);
        Assert.AreEqual(0, route.Waypoints.Count);
        Assert.AreEqual(0, route.WaypointIndex);
        Assert.AreEqual(default, route.StandpointDay);
        Assert.AreEqual(default, route.StandpointNight);
        Assert.IsFalse(route.HasStandpoints);
    }

    [TestMethod]
    public void A_location_keeps_the_full_unsigned_words()
    {
        var route = AreaNpcRouteReader.Read(Row(16, Number(78, Location(uint.MaxValue, 0x80000000u)))).Route!;
        Assert.AreEqual(new AreaRouteLocation(uint.MaxValue, 0x80000000u), route.StandpointDay);
    }

    [TestMethod]
    public void A_negative_stored_flags_word_reads_as_its_unsigned_bits()
    {
        Assert.AreEqual(0x80000000u, AreaNpcRouteReader.Read(Row(16, Number(69, int.MinValue))).Route!.NpcFlags);
    }

    [TestMethod]
    public void A_record_that_is_not_an_npc_has_no_route()
    {
        var read = AreaNpcRouteReader.Read(Row(15));
        Assert.AreEqual(AreaNpcRouteStatus.NotNpc, read.Status);
        Assert.IsNull(read.Route);
    }

    [TestMethod]
    public void Malformed_fields_are_refused_with_a_reason_not_thrown()
    {
        // A gap in the list, a waypoint of three words, a negative index, a map past int, an array where a scalar belongs.
        var gap = AreaNpcRouteReader.Read(Row(16, Waypoints((0, 1, 1), (2, 2, 2))));
        var wide = AreaNpcRouteReader.Read(Row(16, new AreaFieldValue(76, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(0, [1, 2, 3])], null)));
        var negative = AreaNpcRouteReader.Read(Row(16, Number(77, -1)));
        var map = AreaNpcRouteReader.Read(Row(16, Number(61, long.MaxValue)));
        var array = AreaNpcRouteReader.Read(Row(16, new AreaFieldValue(77, AreaPresence.Array, AreaValueOrigin.Instance, null, [], null)));
        var scalarList = AreaNpcRouteReader.Read(Row(16, Number(76, 5)));
        foreach (var read in new[] { gap, wide, negative, map, array, scalarList })
        {
            Assert.AreEqual(AreaNpcRouteStatus.Malformed, read.Status);
            Assert.IsFalse(string.IsNullOrEmpty(read.Detail));
        }
    }

    [TestMethod]
    public void A_list_over_the_bound_is_refused()
    {
        var elements = Enumerable.Range(0, AreaNpcRouteReader.MaxWaypoints + 1).Select(i => (i, (long)i, 0L)).ToArray();
        Assert.AreEqual(AreaNpcRouteStatus.Malformed, AreaNpcRouteReader.Read(Row(16, Waypoints(elements))).Status);
        Assert.AreEqual(AreaNpcRouteStatus.Ready, AreaNpcRouteReader.Read(Row(16, Waypoints(elements[..AreaNpcRouteReader.MaxWaypoints]))).Status);
    }
}
