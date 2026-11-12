namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-1164 (E5-b, ledger WO5): the blocked-sector list of a map (the original's <c>map.sbf</c>, 0x4D2FC0, 0x4D3000, 0x4D3050, 0x4D0EE0) as map changes of the sector scope, so a script's toggle
/// sector blocked (106) and the world-map route's refusal of such a sector (0x4207D0, 0x420900, 0x4209C0) share one place and the save carries it without a schema step. A sector is blocked while its change is 1;
/// a map's loaded list is empty (the installed module ships none, <c>WorldTravelSectors</c>), so unblocking returns the key to its baseline.
/// </summary>
public static class SessionSectorBlock
{
    /// <summary>The property of the sector-scope change that marks a sector blocked.</summary>
    public const string Property = "map.blocked";

    /// <summary>The 64-bit sector id the original packs from the sector's two numbers (0x4CFC50): <paramref name="y"/> shifted up by 26 bits ORed with the low 26 bits of <paramref name="x"/>; null for a sector with a
    /// negative number or an x beyond the 26 bits, which the session's keys do not hold (the packing would alias it with another sector).</summary>
    public static long? Pack(int x, int y) => x < 0 || y < 0 || x > 0x3FFFFFF ? null : ((long)y << 26) | (uint)(x & 0x3FFFFFF);

    /// <summary>The map-change key of a packed sector id.</summary>
    public static MapChangeKey Key(long sector) => MapChangeKey.ForSector(sector, Property);

    /// <summary>The sector's two numbers back from the packed id.</summary>
    public static (int X, int Y) Unpack(long sector) => ((int)(sector & 0x3FFFFFF), (int)(sector >> 26));
}

public sealed partial class GameSessionOwner
{
    /// <summary>ARC-SCRIPT-1164 (WO5, 0x4D0EE0): whether the sector (x, y) is on the map's blocked list; false for a sector the session's keys do not hold.</summary>
    public bool QuerySectorBlocked(SessionMapId map, int x, int y)
    {
        ThrowIfDisposed();
        return SessionSectorBlock.Pack(x, y) is { } sector && QueryMapChange(map, SessionSectorBlock.Key(sector)) == 1;
    }

    /// <summary>ARC-SCRIPT-1164 (WO5): as above for a sector given as the world map's 64-bit numbers; one outside the 32-bit range is on no list.</summary>
    public bool QuerySectorBlocked(SessionMapId map, long x, long y) => x is >= 0 and <= int.MaxValue && y is >= 0 and <= int.MaxValue && QuerySectorBlocked(map, (int)x, (int)y);

    /// <summary>ARC-SCRIPT-1164 (WO5): the sectors on the map's blocked list, in ascending order of their packed ids (what the world-map route reads).</summary>
    public IReadOnlyList<(int X, int Y)> QueryBlockedSectors(SessionMapId map)
    {
        ThrowIfDisposed();
        return QueryMapChanges(map)
            .Where(change => change.Key.Scope == MapChangeScope.Sector && change.Key.Property == SessionSectorBlock.Property && change.Value == 1)
            .Select(change => change.Key.Sector).Order().Select(SessionSectorBlock.Unpack).ToArray();
    }

    /// <summary>
    /// ARC-SCRIPT-1164 (WO5, 0x4EF010 → 0x4D0F00): puts the sector on the map's blocked list or takes it off; a sector already in that state is <see cref="GameSessionDisposition.NoChange"/>. A sector with a negative number
    /// is refused with <see cref="GameSessionFailure.InvalidChangeKey"/> before any effect. One revision.
    /// </summary>
    public GameSessionMutationResult SetSectorBlocked(GameSessionVersion expected, SessionMapId map, int x, int y, bool blocked)
    {
        ThrowIfDisposed();
        if (SessionSectorBlock.Pack(x, y) is not { } sector) return Reject(GameSessionFailure.InvalidChangeKey);
        var key = SessionSectorBlock.Key(sector);
        return blocked ? SetMapChange(expected, map, key, 1) : ClearMapChange(expected, map, key);
    }
}
