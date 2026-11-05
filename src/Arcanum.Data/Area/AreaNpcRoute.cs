namespace Arcanum.Data.Area;

/// <summary>A bare location the way the original stores it in an object field: the tile's x in the low word and y in the high word, each an
/// unsigned 32-bit value (the field getters read the 64-bit word; no map, no sector).</summary>
public readonly record struct AreaRouteLocation(uint X, uint Y)
{
    /// <summary>The location of a stored 64-bit field value (x low, y high, the same word the location field holds).</summary>
    public static AreaRouteLocation FromNumber(long number) => new(unchecked((uint)number), unchecked((uint)(number >> 32)));
}

/// <summary>Why <see cref="AreaNpcRouteReader.Read"/> gave no route.</summary>
public enum AreaNpcRouteStatus
{
    /// <summary>The route was read.</summary>
    Ready,
    /// <summary>The record is not an NPC (the NPC block of the object's fields does not exist).</summary>
    NotNpc,
    /// <summary>A field of the route is stored in a shape the reader does not take (see the detail): a waypoint that is not a two-word location,
    /// elements that do not run from 0 without a gap, or a flags, index or map value outside its width.</summary>
    Malformed,
}

/// <summary>
/// The route facts of one NPC row (ARC-DEV-117, MIG9 S4b): the NPC flags (field 0x118), the teleport map (field 243; 0 = never set), the list
/// of waypoints (field 0x11F) with the stored current index (field 0x120), and the day and night standpoints (fields 0x121 and 0x122), each a
/// bare location. An absent or null standpoint field reads as the location (0, 0), as the original's field getter returns it. Derived from the
/// row's effective fields (the instance's own value, else its prototype's); nothing here is state.
/// </summary>
/// <param name="NpcFlags">The NPC flags word; WAYPOINTS_DAY 0x2 and WAYPOINTS_NIGHT 0x4 gate the waypoint step, WANDERS 0x1000 and WANDERS_IN_DARK 0x2000 widen the standpoint step's radius.</param>
/// <param name="TeleportMap">The runtime map the NPC is tied to; 0 when no teleport or home check ever set it.</param>
/// <param name="Waypoints">The waypoint list in index order (elements 0..n-1); empty when the field is absent or empty.</param>
/// <param name="WaypointIndex">The stored index of the current waypoint (0 when the field is absent).</param>
/// <param name="StandpointDay">The day standpoint; (0, 0) when the field is absent.</param>
/// <param name="StandpointNight">The night standpoint; (0, 0) when the field is absent.</param>
/// <param name="HasStandpoints">Whether the row stores at least one of the two standpoint fields (a census fact; the rules read the locations either way).</param>
public sealed record AreaNpcRoute(uint NpcFlags, int TeleportMap, IReadOnlyList<AreaRouteLocation> Waypoints, int WaypointIndex,
    AreaRouteLocation StandpointDay, AreaRouteLocation StandpointNight, bool HasStandpoints);

/// <summary>What <see cref="AreaNpcRouteReader.Read"/> found: the route, or why not (no exception for an ordinary failure).</summary>
public sealed record AreaNpcRouteRead(AreaNpcRouteStatus Status, AreaNpcRoute? Route, string Detail);

/// <summary>
/// ARC-DEV-117 (MIG9 S4b, RL17): reads the fields the waypoint and standpoint steps of the sector fold take from an NPC's object row. The slots
/// are positions in the NPC's field table (critter block 36..68, NPC block 69..93): teleport map 61, NPC flags 69, waypoint list 76, waypoint
/// index 77, day standpoint 78, night standpoint 79 (the object fields 243, 0x118, 0x11F, 0x120, 0x121 and 0x122 of the executable's field table;
/// the leader at slot 70 is field 0x119, which fixes the NPC block's offset). Pure; no Godot, no session.
/// </summary>
public static class AreaNpcRouteReader
{
    /// <summary>The object type number of an NPC.</summary>
    public const int NpcType = 16;
    internal const int TeleportMapSlot = 61, NpcFlagsSlot = 69, WaypointListSlot = 76, WaypointIndexSlot = 77, StandpointDaySlot = 78, StandpointNightSlot = 79;

    /// <summary>The longest waypoint list read; a longer list is refused (project policy; the original's own bound is its sparse array's).</summary>
    public const int MaxWaypoints = 1024;

    /// <summary>The route of an NPC row, or the reason it has none.</summary>
    public static AreaNpcRouteRead Read(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Type != NpcType) return new(AreaNpcRouteStatus.NotNpc, null, $"object type {row.Type}");
        var fields = new Dictionary<int, AreaFieldValue>();
        foreach (var field in row.EffectiveFields) fields.TryAdd(field.Slot, field);

        if (!Scalar(fields, NpcFlagsSlot, out var flags, out var why) || !Scalar(fields, TeleportMapSlot, out var map, out why) ||
            !Scalar(fields, WaypointIndexSlot, out var index, out why) || !Scalar(fields, StandpointDaySlot, out var day, out why) ||
            !Scalar(fields, StandpointNightSlot, out var night, out why))
            return Malformed(why);
        if (flags is < int.MinValue or > uint.MaxValue) return Malformed("NPC flags outside 32 bits");
        if (map is < 0 or > int.MaxValue) return Malformed("teleport map outside 0..int.MaxValue");
        if (index is < 0 or > int.MaxValue) return Malformed("waypoint index outside 0..int.MaxValue");

        var list = new List<AreaRouteLocation>();
        if (fields.TryGetValue(WaypointListSlot, out var waypoints) && waypoints.Presence == AreaPresence.Array)
        {
            if (waypoints.Elements.Count > MaxWaypoints) return Malformed($"{waypoints.Elements.Count} waypoints");
            var expected = 0;
            foreach (var element in waypoints.Elements.OrderBy(e => e.Key))
            {
                if (element.Key != expected++) return Malformed($"waypoint elements are not 0..n-1 (key {element.Key} where {expected - 1} was expected)");
                if (element.Words.Count != 2 || element.Words.Any(w => w is < 0 or > uint.MaxValue)) return Malformed("a waypoint that is not two 32-bit words");
                list.Add(new((uint)element.Words[0], (uint)element.Words[1]));
            }
        }
        var hasStandpoints = Stored(fields, StandpointDaySlot) || Stored(fields, StandpointNightSlot);
        return new(AreaNpcRouteStatus.Ready,
            new(unchecked((uint)flags), (int)map, list.AsReadOnly(), (int)index, AreaRouteLocation.FromNumber(day), AreaRouteLocation.FromNumber(night), hasStandpoints), "");

        static AreaNpcRouteRead Malformed(string detail) => new(AreaNpcRouteStatus.Malformed, null, detail);
    }

    // A scalar field: an absent, null or empty field reads as 0 (the getter's answer for a field the object does not hold); an array is refused.
    private static bool Scalar(Dictionary<int, AreaFieldValue> fields, int slot, out long value, out string why)
    {
        value = 0; why = "";
        if (!fields.TryGetValue(slot, out var field) || field.Presence is AreaPresence.Absent or AreaPresence.Null or AreaPresence.Empty) return true;
        if (field.Presence == AreaPresence.Value && field.Number is { } number) { value = number; return true; }
        why = $"slot {slot} is stored as {field.Presence}";
        return false;
    }

    private static bool Stored(Dictionary<int, AreaFieldValue> fields, int slot) =>
        fields.TryGetValue(slot, out var field) && field.Presence == AreaPresence.Value;
}
