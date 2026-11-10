using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-072 (MIG1, TM3): the record source of the viewer: the object rows of any map of the game folder's module content stack, by
/// runtime number, read on demand for <see cref="RecordResolver"/>. It reads rows only as far as the resolver is concerned: the strict AREA
/// publication of the map and its whole-map step evidence (the life evidence of its NPCs), no art, no lighting.
/// ARC-DEV-079 (MIG4): the read is <see cref="AreaMapHomes"/>' and is shared through the viewer's <see cref="AreaHomeDirectory"/>, so a map
/// read for a companion's body is not read again for its record. A map that opens only as a window (the main world map: more sector files than
/// a whole-map plan admits) is read as a window around <paramref name="near"/>, a stored cell the session recorded for an NPC of that map
/// (<see cref="AreaVisitors.HomeWindow"/>); without one it is a typed failure (an NPC recorded before that cell was kept resolves only
/// while the viewer holds the window). The resolver holds one window per map (NOT READY: companions of two windows of one map, until MIG9).
/// Unlike a map load it does not compare the viewer's archive arguments with the stack's sources (`RequireSame`) or the expected
/// digests: the stack comes from the same game folder as the viewer's, and the rows are derived data.
/// A map the list does not name, one with no sector files, or one that does not read is a typed failure, never an exception.
/// </summary>
public sealed class AreaMapRecords(TerrainViewerArguments launch, AreaHomeDirectory? homes = null, Func<int, CellCoord?>? near = null) : IMapRecordSource
{
    private readonly IAreaHomeSource _own = new AreaMapHomes(launch, _ => null);

    public MapRecordsLoad Load(int runtimeMap, CancellationToken token)
    {
        var at = near?.Invoke(runtimeMap);
        var load = homes is not null ? homes.Home(runtimeMap, at, token) : _own.Read(runtimeMap, at, token);
        return load.Read is { } read ? new(read.Rows, load.Detail) : new(null, load.Detail);
    }
}

/// <summary>
/// ARC-DEV-075 (MIG2): the record source of a map change inside one run: the maps the run left behind (their rows as the viewer loaded
/// them, a window of the main world map too, which <see cref="AreaMapRecords"/> cannot read) answer first, then the game folder's
/// reader. The viewer tears a map's scene down at every change, so a companion carried from the main map into the Crash Site cave would
/// otherwise have no record on the cave (its home map is a window nothing reads on demand). The maps are derived data of this run only:
/// a quick load to another map or a fresh run starts with none (NOT READY: a companion whose home is a main-map window resolves only after a change of
/// map in the run that loaded it; MIG9 owns reading such a window on demand).
/// </summary>
public sealed class AreaRememberedRecords(IEnumerable<AreaInventoryMap> left, IMapRecordSource? inner) : IMapRecordSource
{
    private readonly Dictionary<int, AreaInventoryMap> _left = left.GroupBy(map => map.RuntimeMap).ToDictionary(group => group.Key, group => group.First());

    public MapRecordsLoad Load(int runtimeMap, CancellationToken token) =>
        _left.TryGetValue(runtimeMap, out var map) ? new(map, "left behind in this run") : inner?.Load(runtimeMap, token) ?? new(null, "no source reads the map");

    /// <summary>ARC-DEV-337 (B52): the windows left behind do not answer here (the resolver asks because the window it holds, possibly this very
    /// one, lacks the NPC): the game folder's reader reads the window around the NPC's stored cell.</summary>
    public MapRecordsLoad LoadAround(int runtimeMap, CancellationToken token) =>
        inner?.LoadAround(runtimeMap, token) ?? new(null, "no source reads the map");
}
