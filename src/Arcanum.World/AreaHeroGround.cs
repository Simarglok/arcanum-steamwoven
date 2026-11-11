using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Session;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-031 (FR-CHAR-020, the wiring of ARC-MAGIC-028): the ground under the hero as the stat getter 0x4B0490 reads it, from the shown area's snapshot: the aptitude adjustment of the sector
/// the hero's cell lies in (<see cref="AreaSectorRecord.AptitudeAdjustment"/>), and whether the tile's art is outdoor (0x5038C0) and its name carries the nature flag (0x4EBBA0,
/// <see cref="TileSoundTable.MageGround"/>). A cell the snapshot holds no sector or tile for (outside the window, outside the map) answers the neutral ground, as the original does where its
/// sector cannot be locked; the nature flag needs the tile-name table and is false without one. The snapshot is the viewer's current window, so the answer follows a window move.
/// </summary>
public sealed class AreaHeroGround
{
    private readonly Func<AreaSnapshot?> _snapshot;
    private readonly Func<(AreaChart Chart, CellCoord Cell)?> _hero;
    private readonly Func<TileSoundTable?> _tiles;
    private AreaSnapshot? _indexed;
    private Dictionary<(long X, long Y), uint> _art = [];
    private Dictionary<(long X, long Y), int> _aptitude = [];

    /// <param name="snapshot">The shown area's snapshot now (null: none shown).</param>
    /// <param name="hero">The hero's committed chart cell with the chart that places it in the world now (null: none): a chart cell is relative to the map's chart origin, the snapshot's tiles and
    /// sectors are in world cells (<see cref="AreaChart.ToWorld"/>).</param>
    /// <param name="tiles">The tile-name table of the module, or null while it is not read.</param>
    public AreaHeroGround(Func<AreaSnapshot?> snapshot, Func<(AreaChart Chart, CellCoord Cell)?> hero, Func<TileSoundTable?> tiles)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _hero = hero ?? throw new ArgumentNullException(nameof(hero));
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    /// <summary>The ground at the hero's cell now (the session's <see cref="GameSessionOwner.HeroGround"/>).</summary>
    public SessionHeroGround Read()
    {
        if (_snapshot() is not { } snapshot || _hero() is not { } hero || WorldCell(hero.Chart, hero.Cell) is not { } cell) return default;
        if (!ReferenceEquals(_indexed, snapshot))
        {
            _art = new Dictionary<(long, long), uint>(snapshot.Terrain.Count);
            foreach (var tile in snapshot.Terrain) _art.TryAdd((tile.WorldX, tile.WorldY), tile.RawArt);
            _aptitude = new Dictionary<(long, long), int>();
            foreach (var sector in snapshot.Sectors) if (sector.Present) _aptitude.TryAdd((sector.Coordinate.X, sector.Coordinate.Y), sector.AptitudeAdjustment);
            _indexed = snapshot;
        }
        return Resolve(cell, _aptitude, _art, _tiles());
    }

    /// <summary>The world cell of a chart cell (<see cref="AreaChart.ToWorld"/>), or null where it does not fit the cell type (the sector and tile indexes are in 32-bit cells).</summary>
    public static CellCoord? WorldCell(AreaChart chart, CellCoord chartCell)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var world = chart.ToWorld(chartCell);
        return world.X is >= 0 and <= int.MaxValue && world.Y is >= 0 and <= int.MaxValue ? new((int)world.X, (int)world.Y) : null;
    }

    /// <summary>
    /// The ground at <paramref name="cell"/> over the window's two indexes: the aptitude adjustment per sector (the cell's sector is its coordinates shifted right by 6; a sector not in the index adds 0)
    /// and the raw tile art per world cell (a cell not in the index has neither tile fact). <paramref name="tiles"/> null leaves the nature flag false and the outdoor test to the static rule.
    /// </summary>
    public static SessionHeroGround Resolve(CellCoord cell, IReadOnlyDictionary<(long X, long Y), int> aptitude, IReadOnlyDictionary<(long X, long Y), uint> art, TileSoundTable? tiles)
    {
        ArgumentNullException.ThrowIfNull(aptitude);
        ArgumentNullException.ThrowIfNull(art);
        var adjustment = aptitude.GetValueOrDefault(((long)cell.X >> 6, (long)cell.Y >> 6));
        if (!art.TryGetValue((cell.X, cell.Y), out var tile)) return new(adjustment);
        var (outdoor, nature) = tiles is not null ? tiles.MageGround(tile) : (TileSoundTable.IsOutdoor(tile), false);
        return new(adjustment, outdoor, nature);
    }
}
