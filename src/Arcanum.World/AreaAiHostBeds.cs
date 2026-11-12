using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-1162 (E3-d, slice 2, ledger SWB16–SWB21): the beds of the shown map as the AI host sees them: the placed beds (scenery of art sub-type 0x16, with the world cell each stands on) and who lies in them
/// (<see cref="AreaBedOccupancy"/>, the one the hero's rest uses too, so a bed an NPC sleeps in refuses the hero with message 10).
/// </summary>
/// <param name="Occupancy">Who lies in each bed.</param>
/// <param name="Placed">The beds of the shown map, each with the world cell it stands on; read at each question.</param>
public sealed record AreaAiBeds(AreaBedOccupancy Occupancy, Func<IReadOnlyList<(AreaObjectKey Key, CellCoord Cell)>> Placed);

public sealed partial class AreaAiHost
{
    // The original's NPC flag 0x200 (the "arrived at its standpoint" latch, SWB21): set once by the standpoint step's near branch, cleared by its far branch and by the unseat. It is host state here, not saved: a rebuilt
    // host finds its NPC awake at the standpoint and seats it again, a sleeper the session's saved critter flag keeps is seated again by <see cref="RecoverSeat"/>.
    private readonly HashSet<string> _arrived = new(StringComparer.Ordinal);

    /// <summary>The NPCs whose standpoint latch (the original's NPC flag 0x200) is set.</summary>
    public IReadOnlySet<string> Arrived => _arrived;

    /// <summary>The bed an NPC lies in, or null.</summary>
    public AreaObjectKey? BedOfSleeper(string npc) => _scene.Beds?.Occupancy.BedOf(npc);

    // What the bed part of a route step did (a short text for the NPC's trace), or null when the step has none.
    private string? BedStep(string npc, NpcRouteStep step, AreaNpcRoute? route)
    {
        if (_scene.Beds is not { } beds || route is null) return null;
        ReconcileBeds(beds);
        if (step is { Outcome: NpcRouteOutcome.AtStandpoint, Standpoint: { } stand }) return SeatAtStandpoint(npc, beds, stand);
        if (step.Outcome != NpcRouteOutcome.Sleeping) return null;
        var recovered = RecoverSeat(npc, beds, route);
        return step.Reseat ? (recovered is null ? "" : recovered + ", ") + Reseat(npc, beds, route) : recovered;
    }

    // The bed on a tile: the finder (0x4AD140) tests neither the hour nor the NPC. The first bed of the shown map's list stands for "the" bed there.
    [OriginalFunction(0x4AD140, OriginalFunctionScope.Partial)]
    private static AreaObjectKey? BedAt(AreaAiBeds beds, RouteLocation tile)
    {
        foreach (var (key, cell) in beds.Placed()) if (cell.X >= 0 && cell.Y >= 0 && (uint)cell.X == tile.X && (uint)cell.Y == tile.Y) return key;
        return null;
    }

    // SWB21, the standpoint step's near branch: the NPC sets its flag 0x200 once (it is clear), looks for a bed on the standpoint tile and seats itself (0x45F2D0); the answer of the seat is not read, so
    // an NPC whose bed is taken keeps the flag, stands there and is not tried again until it leaves its radius.
    private string? SeatAtStandpoint(string npc, AreaAiBeds beds, RouteLocation stand)
    {
        if (!_arrived.Add(npc)) return null;
        if (BedAt(beds, stand) is not { } bed) return "no bed on the standpoint";
        if (!beds.Occupancy.Seat(bed, npc)) return $"bed taken by {beds.Occupancy.Occupant(bed)}";
        if (!WriteSleeping(npc, true))
        {
            beds.Occupancy.Unseat(bed, npc);
            return "sleeping flag refused";
        }
        return "seated";
    }

    // The sleeping flag of the session (critter flag 0x1000) over the data's; the write that gives back the data's value leaves no change behind.
    private bool WriteSleeping(string npc, bool on)
    {
        var data = Fields(npc)?.Critter is { } critter && (critter.Flags & SleepingFlag) != 0;
        return _session.WriteObjectFlag(_session.Version, npc, SessionObjectFlagField.Critter, unchecked((int)SleepingFlag), on, data).Failure == GameSessionFailure.None;
    }

    // A sleeper whose critter flag the session saved (a load) but whose bed holds nobody (the occupant is not saved): the bed of its standpoint takes it again, as the original's occupant field would hold it.
    private string? RecoverSeat(string npc, AreaAiBeds beds, AreaNpcRoute route)
    {
        if (beds.Occupancy.BedOf(npc) is not null || (_session.QueryObjectFlags(npc).CritterSet & unchecked((int)SleepingFlag)) == 0) return null;
        foreach (var stand in NearestFirst(npc, route))
            if (BedAt(beds, new(stand.X, stand.Y)) is { } bed && beds.Occupancy.Occupant(bed) is null && beds.Occupancy.Seat(bed, npc)) return "seated again";
        return null;
    }

    // The two standpoints, the one nearer the NPC's tile first (a sleeper lay in the bed it stood at; the original's saved occupant says which, here the place does).
    private AreaRouteLocation[] NearestFirst(string npc, AreaNpcRoute route)
    {
        if (Tile(npc) is not { } at) return [route.StandpointNight, route.StandpointDay];
        long Distance(AreaRouteLocation stand) => Math.Max(Math.Abs((long)at.X - stand.X), Math.Abs((long)at.Y - stand.Y));
        return Distance(route.StandpointDay) < Distance(route.StandpointNight) ? [route.StandpointDay, route.StandpointNight] : [route.StandpointNight, route.StandpointDay];
    }

    // The occupancy is shared and not saved, the session's sleeping flags are: the first bed question of a host settles them, so a load into another state, a bed taken by the hero or two sleepers of one tile cannot leave the two
    // disagreeing. An NPC of this host that holds a bed without sleeping (a load before the night) or is dead or gone is taken out; then every NPC the session saved asleep is seated again, all of them before any step seats a new one.
    private bool _bedsReconciled;

    private void ReconcileBeds(AreaAiBeds beds)
    {
        if (_bedsReconciled) return;
        _bedsReconciled = true;
        foreach (var npc in _npcs.Keys.ToArray())
            if (beds.Occupancy.BedOf(npc) is { } bed && (!Sleeping(npc) || Gone(npc) || DeadCritter(npc))) Unseat(npc, beds, bed);
        foreach (var npc in _npcs.Keys.ToArray())
            if (RouteOf(npc) is { } route) RecoverSeat(npc, beds, route);
    }

    // A killed NPC leaves its bed at once (the original's handling of a dead occupant is not read: a project choice, so the bed does not refuse the hero for ever).
    private void ReleaseBed(string npc)
    {
        if (_scene.Beds is { } beds && beds.Occupancy.BedOf(npc) is { } bed) Unseat(npc, beds, bed);
    }

    // SWB17 (0x4AD0B0, 0x4AD140): looks for a bed at the night standpoint and, when there is none or its occupant is not the NPC, at the day standpoint; when neither holds the NPC it does nothing, else the unseat.
    [OriginalFunction(0x4AD0B0, OriginalFunctionScope.Partial)]
    private string Reseat(string npc, AreaAiBeds beds, AreaNpcRoute route)
    {
        foreach (var stand in new[] { route.StandpointNight, route.StandpointDay })
            if (BedAt(beds, new(stand.X, stand.Y)) is { } bed && beds.Occupancy.Occupant(bed) == npc) return Unseat(npc, beds, bed);
        // Not the original's (its occupant field is saved, so it cannot lie in no bed): a sleeper the session wrote asleep whose bed was lost (taken after a load, its bed gone) would sleep for ever; it is woken.
        if ((_session.QueryObjectFlags(npc).CritterSet & unchecked((int)SleepingFlag)) != 0 && beds.Occupancy.BedOf(npc) is null)
        {
            _arrived.Remove(npc);
            return WriteSleeping(npc, false) ? "stranded sleeper woken" : "stranded sleeper, flag write refused";
        }
        return "not in a bed at a standpoint";
    }

    // SWB16 (0x45F3A0): the occupant leaves the bed, its sleeping flag and its standpoint latch are cleared.
    private string Unseat(string npc, AreaAiBeds beds, AreaObjectKey bed)
    {
        if (!beds.Occupancy.Unseat(bed, npc)) return "unseat refused";
        _arrived.Remove(npc);
        return WriteSleeping(npc, false) ? "woken" : "woken, flag write refused";
    }

    // SWB18 (c): the attack event's victim and a sleeping witness are woken through the re-seat (0x4AD0B0): a sleeper that lies in a bed at one of its standpoints leaves it, any other stays asleep.
    private void WakeFromBed(string npc)
    {
        if (_scene.Beds is not { } beds || RouteOf(npc) is not { } route || !Sleeping(npc)) return;
        ReconcileBeds(beds);
        var text = Reseat(npc, beds, route);
        Trace(npc).Route = $"woken by an attack: {text}";
    }
}
