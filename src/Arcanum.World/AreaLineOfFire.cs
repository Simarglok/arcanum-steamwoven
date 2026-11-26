using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-COMBAT-012: the shown map as the original's line of fire reads it (<see cref="ILineOfFireMap{TObject}"/>, 0x4ADE00 and its step
/// cost 0x43FE00 with the projectile flags; ledger RA rows), in session (world) cells over the map's T0 step evidence, built like
/// <see cref="AreaDropMap"/>: the evidence's admitted tiles are the map, the walls' pieces are the step evidence's
/// (<see cref="AreaWallPieces"/>), doors and windows open or busted by the session, and the objects a projectile meets are the map's
/// rows (containers, scenery, projectiles, PCs, NPCs and traps: the original's mask 0x3801F; items on the ground are not in it) less
/// what the original's lists skip (destroyed or switched off at T0 or in the session) and less the T0 records of the companions, who
/// stand on their live cells instead.
/// </summary>
public sealed class AreaLineOfFire : ILineOfFireMap<AreaObjectRecord>
{
    private const int WallType = 0, PortalType = 1, PcType = 15, NpcType = 16, ObjectFlagsSlot = 18, SpellFlagsSlot = 19;
    private const uint DestroyedOrOff = 0x3, DestroyedFlag = 0x1, PassWalled = 0x100000, BustedArt = 0x200;
    // The original's object mask 0x3801F less walls and portals: containers, scenery, projectiles, PCs, NPCs and traps.
    private static readonly int[] MetTypes = [2, 3, 4, PcType, NpcType, 17];
    private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];

    private readonly AreaStepSnapshot _steps;
    private readonly Func<AreaObjectRecord, bool> _gone, _dead;
    private readonly Func<AreaObjectRecord, bool?> _portalOpen;
    private readonly IReadOnlySet<AreaObjectKey> _moved;
    private readonly Placed _rows;
    private readonly Dictionary<CellCoord, List<AreaObjectRecord>> _live = [];

    // ARC-RENDER-3362: what the rows alone give: the walls' and portals' pieces and the met objects by cell, in the rows' order. A host builds the map again every
    // frame, and walking the map's thousands of rows (and their fields) each time cost more than every question asked of it, so the part is made once per read-only
    // row list (a map snapshot's) and dies with it. What changes between two frames (the moved records, the live actors, the session's tests) is asked at the question.
    private sealed record Placed(Dictionary<(CellCoord, int), List<AreaObjectRecord>> Edges, Dictionary<CellCoord, List<AreaObjectRecord>> Objects);
    private static readonly ConditionalWeakTable<ReadOnlyCollection<AreaObjectRecord>, Placed> PlacedRows = [];

    /// <param name="steps">The map's step evidence (its admitted tiles are the map).</param>
    /// <param name="rows">The map's object rows; their world cells place them.</param>
    /// <param name="moved">T0 records the live overlay moved (the companions); the set must not change while the map is used.</param>
    /// <param name="liveActors">The companions' rows on the cells they stand on now (the hero, the attacker, is not one).</param>
    /// <param name="gone">Whether a row is destroyed or switched off in the session.</param>
    /// <param name="dead">Whether a critter row is dead in the session or its map data.</param>
    /// <param name="portalOpen">Whether a door or window is open or busted now; null when the session does not hold it (then its
    /// T0 ART decides).</param>
    public AreaLineOfFire(AreaStepSnapshot steps, IEnumerable<AreaObjectRecord> rows, IReadOnlySet<AreaObjectKey> moved,
        IReadOnlyDictionary<CellCoord, AreaObjectRecord> liveActors, Func<AreaObjectRecord, bool> gone, Func<AreaObjectRecord, bool> dead,
        Func<AreaObjectRecord, bool?> portalOpen)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        ArgumentNullException.ThrowIfNull(rows);
        _moved = moved ?? throw new ArgumentNullException(nameof(moved));
        ArgumentNullException.ThrowIfNull(liveActors);
        _gone = gone ?? throw new ArgumentNullException(nameof(gone));
        _dead = dead ?? throw new ArgumentNullException(nameof(dead));
        _portalOpen = portalOpen ?? throw new ArgumentNullException(nameof(portalOpen));
        _rows = rows is ReadOnlyCollection<AreaObjectRecord> fixedRows ? PlacedRows.GetValue(fixedRows, Place) : Place(rows);
        foreach (var (cell, row) in liveActors) Add(_live, cell, row);
    }

    private static Placed Place(IEnumerable<AreaObjectRecord> rows)
    {
        var placed = new Placed([], []);
        foreach (var row in rows)
        {
            if (row.WorldX is not { } x || row.WorldY is not { } y || (Flags(row, ObjectFlagsSlot) & (row.Type == NpcType ? DestroyedFlag : DestroyedOrOff)) != 0) continue;
            var cell = new CellCoord(checked((int)x), checked((int)y));
            if (row.Type is WallType or PortalType)
            {
                if (Art(row) is not { } art) continue;
                var key = (cell, AreaWallPieces.Edge(art));
                if (!placed.Edges.TryGetValue(key, out var pieces)) placed.Edges[key] = pieces = [];
                pieces.Add(row);
            }
            else if (MetTypes.Contains(row.Type)) Add(placed.Objects, cell, row);
        }
        return placed;
    }

    private static void Add(Dictionary<CellCoord, List<AreaObjectRecord>> into, CellCoord cell, AreaObjectRecord row)
    {
        if (!into.TryGetValue(cell, out var list)) into[cell] = list = [];
        list.Add(row);
    }

    /// <summary>0x4B8FF0: rotation 0..7; null off the map (here: off the step evidence).</summary>
    public CellCoord? Step(CellCoord from, int rotation)
    {
        if (rotation is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(rotation));
        var next = new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        return Contains(next) ? next : null;
    }

    public bool Contains(CellCoord tile) => _steps.IsAdmitted(new(tile.X, tile.Y));

    /// <summary>The walls and portals on the tile's edge: a wall by its piece (a pass-walled one by the cover test), a door or window
    /// open or busted passing and a closed one by the cover test.</summary>
    public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge)
    {
        if (!_rows.Edges.TryGetValue((tile, edge), out var rows)) yield break;
        foreach (var row in rows)
        {
            if (_moved.Contains(row.Key) || _gone(row)) continue;
            var art = Art(row)!.Value;
            var kind = row.Type == PortalType
                ? (_portalOpen(row) ?? ((art & BustedArt) != 0 || ((art >> 14) & 31) != 0)) ? LineOfFirePieceKind.OpenPortal : LineOfFirePieceKind.ClosedPortal
                : AreaWallPieces.Kind(AreaWallPieces.Piece(art)) switch
                {
                    AreaWallPieceKind.Window => LineOfFirePieceKind.Window,
                    AreaWallPieceKind.Doorway => LineOfFirePieceKind.Doorway,
                    _ => (Flags(row, SpellFlagsSlot) & PassWalled) != 0 ? LineOfFirePieceKind.PassWalled : LineOfFirePieceKind.Solid,
                };
            yield return new(row, kind, Flags(row, ObjectFlagsSlot));
        }
    }

    public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile)
    {
        if (_rows.Objects.TryGetValue(tile, out var rows))
            foreach (var row in rows)
                if (!_moved.Contains(row.Key) && !_gone(row)) yield return Met(row);
        // A live actor stands on its cell now, whether or not its T0 record is a moved one.
        if (_live.TryGetValue(tile, out var live))
            foreach (var row in live)
                if (!_gone(row)) yield return Met(row);
    }

    private LineOfFireObject<AreaObjectRecord> Met(AreaObjectRecord row) => new(row, row.Type is PcType or NpcType && _dead(row), Flags(row, ObjectFlagsSlot));

    private static uint? Art(AreaObjectRecord row) =>
        row.EffectiveFields.FirstOrDefault(f => f.Slot == 0 && f.Presence == AreaPresence.Value)?.Number is { } number ? unchecked((uint)number) : null;

    private static uint Flags(AreaObjectRecord row, int slot) =>
        unchecked((uint)(row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0));
}
