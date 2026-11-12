namespace Arcanum.Simulation.Ai;

/// <summary>A bare location the way the original's object fields hold it: the tile's x and y as unsigned 32-bit words (no map, no sector).</summary>
public readonly record struct RouteLocation(uint X, uint Y);

/// <summary>NPC flag bits the route steps read (field 0x118).</summary>
public static class NpcRouteFlags
{
    /// <summary>The NPC keeps a waypoint list by day (WAYPOINTS_DAY).</summary>
    public const uint WaypointsDay = 0x2;
    /// <summary>The NPC keeps a waypoint list by night (WAYPOINTS_NIGHT).</summary>
    public const uint WaypointsNight = 0x4;
    /// <summary>The NPC wanders about its standpoint (WANDERS).</summary>
    public const uint Wanders = 0x1000;
    /// <summary>The NPC wanders about its standpoint in the dark (WANDERS_IN_DARK).</summary>
    public const uint WandersInDark = 0x2000;
}

/// <summary>
/// What the NPC's waypoint or standpoint step reads (ARC-DEV-117, MIG9 S4b): its state, the open map and the hour, its own location and its
/// route fields. The host builds it from the object row, the session and the clock.
/// </summary>
/// <param name="Sleeping">The NPC is sleeping (0x45D870: critter flag 0x1000 of a PC or NPC).</param>
/// <param name="Hidden">Object flags 0x102 (switched off or not drawn).</param>
/// <param name="HasLeader">The NPC has a leader (0x45DE00 answers a handle); the standpoint step reads it itself, the waypoint step relies on its caller.</param>
/// <param name="Hour">The game hour 0..23 (0x45AAA0).</param>
/// <param name="NpcFlags">The NPC flags (field 0x118).</param>
/// <param name="TeleportMap">The teleport map (field 243); 0 = never set, which the first step of a leaderless NPC sets to <see cref="OpenMap"/>.</param>
/// <param name="OpenMap">The runtime map that is open (0x40FF40).</param>
/// <param name="Location">The NPC's own location (field 2).</param>
/// <param name="Waypoints">The waypoint list (field 0x11F), elements 0..n-1.</param>
/// <param name="WaypointIndex">The stored current index (field 0x120).</param>
/// <param name="StandpointDay">The day standpoint (field 0x121).</param>
/// <param name="StandpointNight">The night standpoint (field 0x122).</param>
public sealed record NpcRouteFacts(bool Sleeping, bool Hidden, bool HasLeader, int Hour, uint NpcFlags, int TeleportMap, int OpenMap,
    RouteLocation Location, IReadOnlyList<RouteLocation> Waypoints, int WaypointIndex, RouteLocation StandpointDay, RouteLocation StandpointNight);

/// <summary>What one forced route step decided.</summary>
public enum NpcRouteOutcome
{
    /// <summary>The step did nothing and answered 0 (a hidden NPC, no list, the day or night flag missing, a leader).</summary>
    NoStep,
    /// <summary>The waypoint list's stored index lies outside the list: the original reads the sparse array's answer for an absent element, which is
    /// not read here, so the step does nothing and answers 0 (NOT READY; real data has no such row).</summary>
    IndexOutsideList,
    /// <summary>A sleeping NPC: the step answers 1 and moves nothing. <see cref="NpcRouteStep.Reseat"/> says the original's bed re-seat (0x4AD0B0) ran (the host wakes the sleeper that lies in a bed at one of its standpoints, ARC-WORLD-1162).</summary>
    Sleeping,
    /// <summary>A standpoint step with the NPC already within its radius: the original sets NPC flag 0x200, looks for a bed at the standpoint (<see cref="NpcRouteStep.Standpoint"/>, the host's part, ARC-WORLD-1162) and
    /// starts a wander (0x435E60 or 0x436040) for a wanderer (not implemented here); nothing moves.</summary>
    AtStandpoint,
    /// <summary>The NPC's teleport map is the open map: its animations are interrupted (priority 4) and it is relocated onto <see cref="NpcRouteStep.Target"/>.</summary>
    Relocate,
    /// <summary>The NPC's teleport map is another map: the original requests a teleport of that NPC alone to <see cref="NpcRouteStep.Target"/> on
    /// <see cref="NpcRouteStep.TeleportMap"/> (the change-of-place request, MIG11); nothing here moves it.</summary>
    RequestTeleport,
    /// <summary>ARC-DEV-170 (B06): an unforced step on the open map starts a walk goal to <see cref="NpcRouteStep.Target"/> (the walk starter 0x4AD1B0, mode
    /// <see cref="NpcRouteStep.Mode"/>: 3 for a waypoint and 1 for a standpoint, a value the goal stores and never reads, GT124) and answers 1; the NPC is not moved here.</summary>
    StartWalk,
}

/// <summary>One forced route step's result: the outcome, the target, the new stored waypoint index and the teleport map the step wrote.</summary>
/// <param name="Outcome">What the step did.</param>
/// <param name="Answer">What the original's function returned (1 when it acted or took the NPC over, else 0).</param>
/// <param name="Target">The location to relocate or teleport to (null for the outcomes that move nothing).</param>
/// <param name="TeleportMap">The map a <see cref="NpcRouteOutcome.RequestTeleport"/> goes to; the NPC's effective teleport map otherwise (0 when the step ended before reading it).</param>
/// <param name="NewWaypointIndex">The index the step stored in field 0x120 (null when it did not write one).</param>
/// <param name="TeleportMapSet">The value the step wrote into field 243 (the open map, while the field was 0); null when it did not write.</param>
/// <param name="Mode">A <see cref="NpcRouteOutcome.StartWalk"/>'s mode: 3 for a waypoint, 1 for a standpoint (0 for every other outcome).</param>
/// <param name="Reseat">ARC-WORLD-1162 (SWB17, SWB18): the step took a sleeping NPC over through the bed re-seat (0x4AD0B0): a waypoint step of a sleeper, or a standpoint step of a sleeper farther than its radius from the current standpoint. The host wakes it when it lies in a bed at one of its standpoints.</param>
/// <param name="Standpoint">ARC-WORLD-1162 (SWB21): the current standpoint of an <see cref="NpcRouteOutcome.AtStandpoint"/> step (null for every other outcome): the tile the bed is looked for on.</param>
public sealed record NpcRouteStep(NpcRouteOutcome Outcome, bool Answer, RouteLocation? Target, int TeleportMap, int? NewWaypointIndex, int? TeleportMapSet, int Mode = 0, bool Reseat = false, RouteLocation? Standpoint = null);

/// <summary>
/// ARC-DEV-117 (MIG9 S4b, RL17, ledger WS1–WS12): the waypoint and standpoint steps of a sector fold's first tick, as the original runs them
/// for a **forced** pass of a leaderless NPC (0x4AD200 passes 1 to 0x4ACBB0 and 0x4ACDB0 when the wake-up marker is set): the NPC is put on
/// its day or night waypoint, or, when the waypoint step did nothing, on its day or night standpoint. Pure rules over <see cref="NpcRouteFacts"/>;
/// the host applies the result (relocation, interrupt, stored index). ARC-DEV-170 (B06) adds the unforced pass (the animation-goal side's caller, the non-combat
/// perform of a leaderless NPC, 0x4AC583; ledger WS6, WS7, WS11): <see cref="UnforcedWaypoints"/> and <see cref="UnforcedStandpoint"/> start a walk goal instead of relocating.
/// </summary>
public static class NpcRouteRules
{
    /// <summary>The first hour of the NPC day.</summary>
    public const int DayStartHour = 6;

    /// <summary>The first hour of the NPC night (the day is hour 6 to 20).</summary>
    public const int NightStartHour = 21;

    /// <summary>A waypoint within this many tiles (Chebyshev) of the NPC counts as reached and the step moves on to the next one.</summary>
    public const long WaypointReachedTiles = 3;

    /// <summary>The standpoint radius of an ordinary NPC.</summary>
    public const long StandpointRadius = 1;

    /// <summary>The standpoint radius of an NPC that wanders (flag WANDERS or WANDERS_IN_DARK).</summary>
    public const long WandererStandpointRadius = 4;

    /// <summary>The NPC day test (0x4ACD90): the hour is 6 or more and below 21.</summary>
    [OriginalFunction(0x4ACD90, OriginalFunctionScope.Full)]
    public static bool IsDay(int hour) => hour >= DayStartHour && hour < NightStartHour;

    /// <summary>
    /// The forced waypoint step (0x4ACBB0 with its third argument 1). A hidden NPC that is not sleeping, an empty list, a missing day or night flag
    /// do nothing (answer 0). The step first sets the teleport map to the open map while it is 0. A sleeping NPC is taken over (bed re-seat, not
    /// modelled). Otherwise the current waypoint is the list's element at the stored index; when the NPC is within <see cref="WaypointReachedTiles"/>
    /// tiles (Chebyshev, 64-bit, signed) of it the index moves to the next element, wrapping to 0 after the last, and that element is the target.
    /// The NPC is then relocated onto the target when its teleport map is the open map, else a teleport request goes to that map.
    /// </summary>
    [OriginalFunction(0x4ACBB0, OriginalFunctionScope.Partial)]
    public static NpcRouteStep ForcedWaypoints(NpcRouteFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.Sleeping && facts.Hidden) return Done(NpcRouteOutcome.NoStep, false, 0);
        var count = facts.Waypoints.Count;
        if (count == 0) return Done(NpcRouteOutcome.NoStep, false, 0);
        // 0x45F6D0: the teleport map, written with the open map while it is 0.
        var set = facts.TeleportMap == 0 ? facts.OpenMap : (int?)null;
        var teleportMap = facts.TeleportMap == 0 ? facts.OpenMap : facts.TeleportMap;
        var flag = IsDay(facts.Hour) ? NpcRouteFlags.WaypointsDay : NpcRouteFlags.WaypointsNight;
        if ((facts.NpcFlags & flag) == 0) return Done(NpcRouteOutcome.NoStep, false, teleportMap, set);
        if (facts.Sleeping) return Reseated(teleportMap, set);
        var index = facts.WaypointIndex;
        if (index < 0 || index >= count) return Done(NpcRouteOutcome.IndexOutsideList, false, teleportMap, set);
        int? newIndex = null;
        var target = facts.Waypoints[index];
        if (Distance(facts.Location, target) <= WaypointReachedTiles)
        {
            newIndex = index + 1 == count ? 0 : index + 1;
            target = facts.Waypoints[newIndex.Value];
        }
        return Place(facts, target, teleportMap, newIndex, set);
    }

    /// <summary>
    /// The forced standpoint step (0x4ACDB0 with its third argument 1, after <see cref="ForcedWaypoints"/> did nothing). A hidden NPC that is not
    /// sleeping, or an NPC with a leader (0x4AD060), does nothing. The standpoint is the day or night one; the step sets the teleport map as the
    /// waypoint step does. An NPC within its radius (4 tiles for a wanderer, else 1) is at its standpoint: the original then flags it, looks for a
    /// bed and starts a wander, none of which is modelled. A sleeping NPC farther away is taken over (bed re-seat, not modelled). Any other NPC is
    /// relocated onto the standpoint when its teleport map is the open map, else a teleport request goes to that map. (The original also clears
    /// NPC flag 0x200 on this branch; the flag is not state here, see ledger WS8.)
    /// </summary>
    [OriginalFunction(0x4ACDB0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4AD060, OriginalFunctionScope.Partial)]
    public static NpcRouteStep ForcedStandpoint(NpcRouteFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.Sleeping && facts.Hidden) return Done(NpcRouteOutcome.NoStep, false, 0);
        if (facts.HasLeader) return Done(NpcRouteOutcome.NoStep, false, 0);
        var set = facts.TeleportMap == 0 ? facts.OpenMap : (int?)null;
        var teleportMap = facts.TeleportMap == 0 ? facts.OpenMap : facts.TeleportMap;
        var stand = IsDay(facts.Hour) ? facts.StandpointDay : facts.StandpointNight;
        var radius = (facts.NpcFlags & (NpcRouteFlags.Wanders | NpcRouteFlags.WandersInDark)) != 0 ? WandererStandpointRadius : StandpointRadius;
        if (Distance(facts.Location, stand) <= radius)
            return new(facts.Sleeping ? NpcRouteOutcome.Sleeping : NpcRouteOutcome.AtStandpoint, facts.Sleeping || (facts.NpcFlags & (NpcRouteFlags.Wanders | NpcRouteFlags.WandersInDark)) != 0, null, teleportMap, null, set,
                Standpoint: facts.Sleeping ? null : stand);
        if (facts.Sleeping) return Reseated(teleportMap, set);
        return Place(facts, stand, teleportMap, null, set);
    }

    /// <summary>The mode the waypoint walk starter 0x4AD1B0 gets. The goal stores it in its frame and no node reads it (GT124): the walk ends on the exact tile, and the step takes a waypoint as reached within <see cref="WaypointReachedTiles"/> by its own distance test.</summary>
    public const int WaypointWalkMode = 3;

    /// <summary>The mode the standpoint walk gets.</summary>
    public const int StandpointWalkMode = 1;

    /// <summary>The standpoint step's hour roll: at hour 6 and hour 21 an unforced call goes on only on a 1 in this many roll (0x4CEF00(1, 1000) equal to 1).</summary>
    public const int HourRollSides = 1000;

    /// <summary>
    /// The unforced waypoint step (0x4ACBB0 with its third argument 0), called by the non-combat perform of a leaderless NPC (0x4AC58C). It reads the facts in the order of the forced
    /// step with one more early answer: an unforced call whose teleport map is not the open map answers 0 (after the teleport map was written). It selects the target as the forced step does
    /// (the stored index moves on when the NPC is within <see cref="WaypointReachedTiles"/> of the current waypoint), then, instead of relocating, starts the walk goal to the target with mode
    /// <see cref="WaypointWalkMode"/> and answers 1.
    /// </summary>
    public static NpcRouteStep UnforcedWaypoints(NpcRouteFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.Sleeping && facts.Hidden) return Done(NpcRouteOutcome.NoStep, false, 0);
        var count = facts.Waypoints.Count;
        if (count == 0) return Done(NpcRouteOutcome.NoStep, false, 0);
        var set = facts.TeleportMap == 0 ? facts.OpenMap : (int?)null;
        var teleportMap = facts.TeleportMap == 0 ? facts.OpenMap : facts.TeleportMap;
        if (teleportMap != facts.OpenMap) return Done(NpcRouteOutcome.NoStep, false, teleportMap, set);
        var flag = IsDay(facts.Hour) ? NpcRouteFlags.WaypointsDay : NpcRouteFlags.WaypointsNight;
        if ((facts.NpcFlags & flag) == 0) return Done(NpcRouteOutcome.NoStep, false, teleportMap, set);
        if (facts.Sleeping) return Reseated(teleportMap, set);
        var index = facts.WaypointIndex;
        if (index < 0 || index >= count) return Done(NpcRouteOutcome.IndexOutsideList, false, teleportMap, set);
        int? newIndex = null;
        var target = facts.Waypoints[index];
        if (Distance(facts.Location, target) <= WaypointReachedTiles)
        {
            newIndex = index + 1 == count ? 0 : index + 1;
            target = facts.Waypoints[newIndex.Value];
        }
        return new(NpcRouteOutcome.StartWalk, true, target, teleportMap, newIndex, set, WaypointWalkMode);
    }

    /// <summary>
    /// The unforced standpoint step (0x4ACDB0 with its third argument 0), after <see cref="UnforcedWaypoints"/> answered 0. As the forced step up to the teleport map, with two more early
    /// answers: another teleport map answers 0, and at hour 6 or hour 21 the step goes on only when <paramref name="hourRollHits"/> (a roll of 1 in <see cref="HourRollSides"/>) says so; it is
    /// asked only then. The near branch is the forced step's. An NPC outside its radius, instead of being relocated, starts the walk goal to the standpoint with mode
    /// <see cref="StandpointWalkMode"/> and answers 1 (a sleeper is taken over as in the forced step).
    /// </summary>
    public static NpcRouteStep UnforcedStandpoint(NpcRouteFacts facts, Func<bool> hourRollHits)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(hourRollHits);
        if (!facts.Sleeping && facts.Hidden) return Done(NpcRouteOutcome.NoStep, false, 0);
        if (facts.HasLeader) return Done(NpcRouteOutcome.NoStep, false, 0);
        var set = facts.TeleportMap == 0 ? facts.OpenMap : (int?)null;
        var teleportMap = facts.TeleportMap == 0 ? facts.OpenMap : facts.TeleportMap;
        if (teleportMap != facts.OpenMap) return Done(NpcRouteOutcome.NoStep, false, teleportMap, set);
        if ((facts.Hour == DayStartHour || facts.Hour == NightStartHour) && !hourRollHits()) return Done(NpcRouteOutcome.NoStep, false, teleportMap, set);
        var stand = IsDay(facts.Hour) ? facts.StandpointDay : facts.StandpointNight;
        var wanders = (facts.NpcFlags & (NpcRouteFlags.Wanders | NpcRouteFlags.WandersInDark)) != 0;
        var radius = wanders ? WandererStandpointRadius : StandpointRadius;
        if (Distance(facts.Location, stand) <= radius)
            return new(facts.Sleeping ? NpcRouteOutcome.Sleeping : NpcRouteOutcome.AtStandpoint, facts.Sleeping || wanders, null, teleportMap, null, set, Standpoint: facts.Sleeping ? null : stand);
        if (facts.Sleeping) return Reseated(teleportMap, set);
        return new(NpcRouteOutcome.StartWalk, true, stand, teleportMap, null, set, StandpointWalkMode);
    }

    // The forced tail both steps share: a relocation on the open map, else a teleport request to the NPC's own map.
    private static NpcRouteStep Place(NpcRouteFacts facts, RouteLocation target, int teleportMap, int? newIndex, int? set) =>
        teleportMap == facts.OpenMap
            ? new(NpcRouteOutcome.Relocate, true, target, teleportMap, newIndex, set)
            : new(NpcRouteOutcome.RequestTeleport, true, target, teleportMap, newIndex, set);

    // A sleeper taken over by the step's bed re-seat (0x4AD0B0): the answer is 1 whether the re-seat found its bed or not.
    private static NpcRouteStep Reseated(int teleportMap, int? set) => new(NpcRouteOutcome.Sleeping, true, null, teleportMap, null, set, Reseat: true);

    private static NpcRouteStep Done(NpcRouteOutcome outcome, bool answer, int teleportMap, int? set = null) => new(outcome, answer, null, teleportMap, null, set);

    // The tile distance (0x4B96F0): the larger of the two axis differences, each a signed 64-bit difference of the unsigned 32-bit words.
    private static long Distance(RouteLocation a, RouteLocation b) => Math.Max(Math.Abs((long)a.X - b.X), Math.Abs((long)a.Y - b.Y));
}
