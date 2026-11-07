using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.World;

namespace Arcanum.World;

/// <summary>
/// ARC-COMBAT-010: the shown map as the original's drop reads it (<see cref="IDropMap"/>, ledger "Drop cell" DC1–DC7), in session
/// (world) cells over the map's T0 step evidence. The path is the original's request with the drop's flags (0x41F3C0 with 0x10):
/// the destination's gate, the direct attempt (0x41F840) and the 64×64 search (0x41F9F0), with the step tests of the step evidence
/// (the tile 0x4D7110 and the object traversal 0x43FD70 the evidence is built on) in the drop's traversal mode, where critters do not
/// block; doors and windows by their runtime passage. A tile outside the step evidence counts as off the map (the original's map is
/// the whole map; the AREA exterior admits nine sectors of it).
/// </summary>
public sealed class AreaDropMap : IDropMap
{
    /// <summary>The drop's path request: at most 200 rotations, the direct attempt up to 32 tiles, the search window 64 × 64.</summary>
    public const int MaxRotations = OriginalPathRequest.BufferRotations, DirectLimit = OriginalPathRequest.DirectLimit, Window = OriginalPathRequest.Window,
        ResultCap = OriginalPathRequest.ResultCap;
    private const int TrapCost = 90;
    private const uint NoBlockObject = 0x400, TrapNpcIgnores = 0x1000000, TrapSpotted = 0x3000000;
    private const int ContainerType = 2, SceneryType = 3, PcType = 15, NpcType = 16, TrapType = 17, ObjectFlagsSlot = 18, TrapFlagsSlot = 36;

    private readonly AreaStepSnapshot _steps;
    private readonly Func<IReadOnlyDictionary<AreaObjectKey, AreaPortalPassage>?>? _portals;
    private readonly Func<AreaObjectRecord, bool> _gone, _dead;
    private readonly IReadOnlySet<AreaObjectKey> _moved;
    private readonly IReadOnlySet<CellCoord> _liveActors;
    private readonly Dictionary<CellCoord, List<AreaObjectRecord>> _rows = [];
    private readonly Dictionary<AreaObjectKey, AreaObjectRecord> _byKey = [];

    /// <param name="rows">The map's object rows; their world cells place them.</param>
    /// <param name="moved">T0 records the live overlay moved (companions; since ARC-WORLD-065 removed objects too, which
    /// <paramref name="gone"/> already answers for).</param>
    /// <param name="liveActors">The cells the hero and the companions stand on now.</param>
    /// <param name="gone">Whether a row is destroyed or switched off in the session (the original's object lists skip it).</param>
    /// <param name="dead">Whether a critter row is dead in the session or its map data.</param>
    public AreaDropMap(AreaStepSnapshot steps, IEnumerable<AreaObjectRecord> rows, IReadOnlySet<AreaObjectKey> moved, IReadOnlySet<CellCoord> liveActors,
        Func<AreaObjectRecord, bool> gone, Func<AreaObjectRecord, bool> dead,
        Func<IReadOnlyDictionary<AreaObjectKey, AreaPortalPassage>?>? portals = null)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        ArgumentNullException.ThrowIfNull(rows);
        _moved = moved ?? throw new ArgumentNullException(nameof(moved));
        _liveActors = liveActors ?? throw new ArgumentNullException(nameof(liveActors));
        _gone = gone ?? throw new ArgumentNullException(nameof(gone));
        _dead = dead ?? throw new ArgumentNullException(nameof(dead));
        _portals = portals;
        foreach (var row in rows)
        {
            _byKey.TryAdd(row.Key, row);
            if (row.Type is ContainerType or SceneryType or PcType or NpcType or TrapType && row.WorldX is { } x && row.WorldY is { } y)
            {
                var cell = new CellCoord(checked((int)x), checked((int)y));
                if (!_rows.TryGetValue(cell, out var list)) _rows[cell] = list = [];
                list.Add(row);
            }
        }
    }

    private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];

    /// <summary>0x4B8FF0: rotation 0..7 by the table, 8 (any value above 7) in place; null off the map (here: off the step evidence).</summary>
    public CellCoord? Step(CellCoord from, int rotation)
    {
        var next = rotation is >= 0 and <= 7 ? new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) : from;
        return _steps.IsAdmitted(new(next.X, next.Y)) ? next : null;
    }

    /// <summary>0x4407C0 with the critter mask: a PC or NPC record standing there (dead too), or the hero or a companion now.</summary>
    public bool HasCritter(CellCoord cell) =>
        _liveActors.Contains(cell) || Rows(cell).Any(row => row.Type is PcType or NpcType && !_moved.Contains(row.Key) && !_gone(row));

    /// <summary>0x41F3C0 with flags 0x10: equal tiles none; the destination's gate; the direct attempt; else the search; capped at 8.
    /// ARC-WORLD-073: the request is the shared <see cref="OriginalPathRequest"/> (the click route's too) over this map's tests.</summary>
    public DropPath Path(CellCoord from, CellCoord to, DropOwner owner)
    {
        if (!Enum.IsDefined(owner)) throw new ArgumentOutOfRangeException(nameof(owner));
        var path = OriginalPathRequest.Request(new Tests(this, owner), from, to, MaxRotations);
        return path.End is OriginalPathEnd.Same or OriginalPathEnd.Gate ? DropPath.None : new(path.Result, path.Partial, path.Rotations);
    }

    /// <summary>The request's tests on this map for one mover (traps by its kind, DC8).</summary>
    private sealed class Tests(AreaDropMap map, DropOwner owner) : IOriginalPathMap
    {
        public CellCoord? Step(CellCoord from, int rotation) => map.Step(from, rotation);
        public bool TileBlocked(CellCoord cell) => map.TileBlocked(cell);
        public OriginalPathTraversal Traverse(CellCoord from, int rotation) => map.Traverse(from, rotation);
        public int EnterCost(CellCoord to) => map.TrapAvoided(to, owner) ? TrapCost : OriginalPathRequest.BaseCost;
        public bool DestinationOpen(CellCoord to) => map.DestinationOpen(to);
    }

    // The gate (0x41F3C0): the tile not blocked, and no container, scenery or living critter on it without the no-block flag.
    private bool DestinationOpen(CellCoord to)
    {
        if (TileBlocked(to)) return false;
        if (_liveActors.Contains(to)) return false;
        return !Rows(to).Any(row => !_gone(row) && (Flags(row, ObjectFlagsSlot) & NoBlockObject) == 0 && (row.Type is ContainerType or SceneryType
            || row.Type is PcType or NpcType && !_moved.Contains(row.Key) && !_dead(row)));
    }

    /// <summary>0x4B8D50: the rotation from one tile toward another; diagonal when twice the minor delta reaches the major one.</summary>
    public static int Rotation(CellCoord a, CellCoord b) => OriginalPathRequest.Rotation(a, b);

    // 0x4D7110 from the evidence: the tile's own verdict; a tile the evidence cannot speak for counts as blocked.
    private bool TileBlocked(CellCoord cell) =>
        _steps.Terrain(new(cell.X, cell.Y)) is not { Coverage: CardinalTraversalCoverage.Supported, Disposition: CardinalTraversalDisposition.Open };

    // 0x43FD70 in the drop's mode (traversal flags 4): walls and doors or windows the owner cannot pass block (the evidence names
    // both as the wall blocker), and containers, scenery and traps without the no-block flag that still stand; critters do not. A
    // window piece crossed on a cardinal step is the "window" output.
    private OriginalPathTraversal Traverse(CellCoord from, int rotation)
    {
        var to = new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        var assessment = _steps.Assess(_steps.Request(new(from.X, from.Y), new(to.X, to.Y), GridReplay.MapDirection((NeighborDirection)rotation)),
            _portals?.Invoke());
        if (assessment.Coverage == CardinalTraversalCoverage.Unsupported) return new(true, false, null);
        if (assessment.WallBlocker is not null) return new(true, false, 0);
        var blocker = assessment.Objects.FirstOrDefault(o => o.Disposition == CardinalTraversalDisposition.Blocked && o.ObjectType is not (PcType or NpcType) &&
            !(_byKey.TryGetValue(o.Key, out var row) && _gone(row)));
        if (blocker is not null) return new(true, false, blocker.ObjectType);
        // Evidence the step rules could not settle, about anything but a critter or a removed object, is not passed.
        if (assessment.Gaps.Any(gap => gap.Object is not { } key || !_byKey.TryGetValue(key, out var row) || row.Type is not (PcType or NpcType) && !_gone(row)))
            return new(true, false, null);
        return new(false, assessment.WindowJump is not null, null);
    }

    // 0x4BCAB0 and 0x4BBFE0: the first trap on the tile, and whether the owner avoids it: an object never; an NPC without a PC
    // leader every trap unless its flag 0x1000000 is set; a PC or an NPC led by one only a spotted trap (flags 0x3000000).
    private bool TrapAvoided(CellCoord cell, DropOwner owner)
    {
        if (owner == DropOwner.Object || Rows(cell).FirstOrDefault(row => row.Type == TrapType && !_gone(row)) is not { } trap) return false;
        var flags = Flags(trap, TrapFlagsSlot);
        return owner == DropOwner.PcSide ? (flags & TrapSpotted) != 0 : (flags & TrapNpcIgnores) == 0;
    }

    private IEnumerable<AreaObjectRecord> Rows(CellCoord cell) => _rows.TryGetValue(cell, out var list) ? list : [];

    private static uint Flags(AreaObjectRecord row, int slot) =>
        unchecked((uint)(row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0));
}
