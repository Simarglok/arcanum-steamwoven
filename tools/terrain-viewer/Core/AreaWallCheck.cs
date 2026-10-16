using Arcanum.Data.Area;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Which side of a wall sprite is see-through. <see cref="Left"/> and <see cref="Right"/> blend linearly across
/// the drawn width from <see cref="AreaWallCheck.EdgeAlpha"/> on that side to opaque on the other; <see cref="Both"/>
/// draws the whole sprite at <see cref="AreaWallCheck.FadedAlpha"/>.</summary>
public enum AreaWallFade { None, Left, Right, Both }

/// <summary>One wall object at its tile: the current ART rotation (bits 11..13 of the current art id) and whether its
/// wall flags forbid fading (<c>OWAF_TRANS_DISALLOW</c>).</summary>
public sealed record AreaWallPiece(AreaObjectKey Key, long X, long Y, int Rotation, bool FadeDisallowed);

/// <summary>The fade set after a recalculation: the 4×4 block the player was resolved to, the block regions it stood
/// in, and the fade of every wall that is not opaque. <see cref="Recalculated"/> is false when the source's same-block
/// rule kept the previous set. <see cref="SharedEdgeTiles"/> counts faded edge tiles holding more than one wall, where
/// only the first in the area's same-tile order fades (the source fades the head of its tile list).</summary>
public sealed record AreaWallCheckState(long BlockX, long BlockY, AreaWallRegions Regions,
    IReadOnlyDictionary<AreaObjectKey, AreaWallFade> Fades, bool Recalculated, int SharedEdgeTiles = 0)
{
    public static AreaWallCheckState Empty { get; } = new(0, 0, AreaWallRegions.None,
        new Dictionary<AreaObjectKey, AreaWallFade>().AsReadOnly(), false);
    public AreaWallFade FadeOf(AreaObjectKey key) => Fades.TryGetValue(key, out var fade) ? fade : AreaWallFade.None;
}

/// <summary>The four triangles a 4×4 block is cut into by its diagonals; a tile on a diagonal is in two.</summary>
[Flags]
public enum AreaWallRegions { None = 0, LowY = 1, LowX = 2, HighY = 4, HighX = 8 }

/// <summary>
/// ARC-RENDER-011: the original wall check, which makes walls in front of the player see-through. Restated from the
/// study of CE <c>src/game/wallcheck.c</c> and the wall branch of <c>object.c</c> at the pinned research revision
/// (see docs/rendering/terrain-viewer.md); no CE code or structure is copied.
///
/// Walls stand on a lattice of corners every four tiles. The player's tile is resolved to the 4×4 block whose corner
/// is <c>(x - (x-2) mod 4, y - (y-2) mod 4)</c>; a player on the block's low-X (or low-Y) line who shares the tile with
/// a wall facing the other way belongs to the previous block. Around that block the check walks a fixed, ordered list of
/// lattice edges (three tiles between two corners). A complete edge (a wall on all three tiles) hides edges behind it;
/// an edge that is not hidden and is a fade edge makes the walls on its three tiles see-through and marks its two
/// corners, whose walls fade on the side that faces the edge. While the player stays in the same block and shares a
/// region with the previous position, the previous set is kept.
/// </summary>
public sealed class AreaWallCheck
{
    /// <summary>Alpha of a wholly faded wall (out of 255).</summary>
    public const int FadedAlpha = 64;
    /// <summary>Alpha of the faded side of a corner wall; the other side stays 255.</summary>
    public const int EdgeAlpha = 128;

    private readonly Dictionary<(long X, long Y), AreaWallPiece[]> _walls;
    public AreaWallCheckState State { get; private set; } = AreaWallCheckState.Empty;
    public int WallCount { get; }

    public AreaWallCheck(IEnumerable<AreaWallPiece> walls)
    {
        ArgumentNullException.ThrowIfNull(walls);
        var all = walls.ToArray();
        if (all.Any(wall => wall is null || wall.Rotation is < 0 or > 7)) throw new ArgumentException("AREA_WALL_PIECE_INVALID");
        if (all.Select(wall => wall.Key).Distinct().Count() != all.Length) throw new ArgumentException("AREA_WALL_PIECE_DUPLICATE");
        _walls = all.GroupBy(wall => (wall.X, wall.Y)).ToDictionary(group => group.Key, group => group.ToArray());
        WallCount = all.Length;
    }

    /// <summary>Every wall of a loaded area in the area's own same-tile order (the drawn upright order, ties by source
    /// index). Wall flags other than <c>OWAF_TRANS_DISALLOW</c> are refused: authored translucency is not supported.</summary>
    public static AreaWallCheck FromArea(AreaSnapshot snapshot, AreaScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(plan);
        var objects = snapshot.Objects.Where(body => body.Type == 0).ToDictionary(body => body.Key);
        var pieces = new List<AreaWallPiece>();
        foreach (var command in plan.Commands.Where(command => command.Identity is { } key && objects.ContainsKey(key)))
        {
            var body = objects[command.Identity!.Value];
            var visual = snapshot.Visuals.Single(candidate => candidate.Key == command.Visual);
            var flags = body.EffectiveFields.SingleOrDefault(field => field.Slot == 36 && field.Presence == AreaPresence.Value)?.Number
                ?? throw new InvalidOperationException("AREA_WALL_FLAGS_REQUIRED");
            if ((flags & ~1L) != 0) throw new InvalidOperationException("AREA_UNSUPPORTED_WALL_FLAGS");
            pieces.Add(new(body.Key, body.WorldX!.Value, body.WorldY!.Value, (int)((visual.RawArt >> 11) & 7), flags == 1));
        }
        return new(pieces);
    }

    /// <summary>Forgets the previous position and every fade, as a map change does.</summary>
    public void Reset() => State = AreaWallCheckState.Empty;

    /// <summary>Recalculates for the player's tile and returns (and keeps) the new state.</summary>
    public AreaWallCheckState Recalculate(long x, long y)
    {
        if (x < 2 || y < 2) throw new ArgumentOutOfRangeException(nameof(x), "AREA_WALL_CHECK_LOCATION");
        var localX = (int)((x - 2) % 4); var localY = (int)((y - 2) % 4);
        // A wall shared with the player's tile on a block line can move the player to the previous block.
        if (localX == 0)
            foreach (var wall in WallsAt(x, y))
            {
                if (wall.Rotation is 4 or 5) { localX = 3; x--; }
                else if (wall.Rotation is 2 or 3) { localY = 3; y--; }
            }
        else if (localY == 0)
            foreach (var wall in WallsAt(x, y))
                if (wall.Rotation is 2 or 3) { localY = 3; y--; }
        var blockX = x - localX; var blockY = y - localY;
        var regions = RegionsOf(localX, localY);
        if (blockX == State.BlockX && blockY == State.BlockY && (State.Regions & regions) != 0)
            return State = State with { Recalculated = false };

        var hidden = new bool[Edges.Length];
        for (var index = 0; index < Edges.Length; index++)
        {
            var edge = Edges[index];
            if (!edge.Occludes || hidden[index] || !Complete(blockX, blockY, edge)) continue;
            foreach (var target in edge.Hides) hidden[target] = true;
            foreach (var (region, targets) in edge.HidesIn)
                if ((regions & region) != 0) foreach (var target in targets) hidden[target] = true;
        }

        var fades = new Dictionary<AreaObjectKey, AreaWallFade>(); var shared = 0;
        var corners = new Dictionary<(int X, int Y), (bool YStart, bool YEnd, bool XStart, bool XEnd)>();
        for (var index = 0; index < Edges.Length; index++)
        {
            var edge = Edges[index];
            if (!edge.Fades || hidden[index]) continue;
            var any = false;
            foreach (var (tileX, tileY) in Tiles(blockX, blockY, edge))
            {
                var here = WallsAt(tileX, tileY);
                if (here.Count == 0) continue;
                any = true; Set(here[0], AreaWallFade.Both);
                if (here.Count > 1) shared++;
            }
            if (!any) continue;
            // The start corner fades its far side, the end corner its near side.
            Mark(edge.Start, edge.AlongY, start: true);
            Mark(edge.End, edge.AlongY, start: false);
        }
        foreach (var (corner, marks) in corners)
            foreach (var wall in WallsAt(blockX + 4 * corner.X, blockY + 4 * corner.Y))
            {
                // Rotations 0,1,4,5 take the marks of edges along Y, rotations 2,3,6,7 those of edges along X.
                var (start, end) = wall.Rotation is 0 or 1 or 4 or 5 ? (marks.YStart, marks.YEnd) : (marks.XStart, marks.XEnd);
                if (start || end) Set(wall, start && end ? AreaWallFade.Both : start ? AreaWallFade.Right : AreaWallFade.Left);
            }
        return State = new(blockX, blockY, regions, fades.AsReadOnly(), true, shared);

        void Set(AreaWallPiece wall, AreaWallFade fade)
        {
            if (!wall.FadeDisallowed) fades[wall.Key] = fade;
        }
        void Mark((int X, int Y) corner, bool alongY, bool start)
        {
            var marks = corners.TryGetValue(corner, out var known) ? known : default;
            if (alongY) marks = start ? marks with { YStart = true } : marks with { YEnd = true };
            else marks = start ? marks with { XStart = true } : marks with { XEnd = true };
            corners[corner] = marks;
        }
    }

    /// <summary>The block regions of a tile at local (x,y), 0..3 each.</summary>
    public static AreaWallRegions RegionsOf(int x, int y)
    {
        if (x is < 0 or > 3 || y is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(x));
        var regions = AreaWallRegions.None;
        if (y <= x && x + y <= 3) regions |= AreaWallRegions.LowY;
        if (x <= y && x + y <= 3) regions |= AreaWallRegions.LowX;
        if (y >= x && x + y >= 3) regions |= AreaWallRegions.HighY;
        if (x >= y && x + y >= 3) regions |= AreaWallRegions.HighX;
        return regions;
    }

    private IReadOnlyList<AreaWallPiece> WallsAt(long x, long y) => _walls.TryGetValue((x, y), out var found) ? found : Array.Empty<AreaWallPiece>();
    private bool Complete(long blockX, long blockY, Edge edge) => Tiles(blockX, blockY, edge).All(tile => WallsAt(tile.X, tile.Y).Count != 0);
    private static IEnumerable<(long X, long Y)> Tiles(long blockX, long blockY, Edge edge)
    {
        var (dx, dy) = edge.AlongY ? (0, 1) : (-1, 0);
        for (var step = 1; step <= 3; step++)
            yield return (blockX + 4 * edge.Start.X + step * dx, blockY + 4 * edge.Start.Y + step * dy);
    }

    /// <summary>One lattice edge: its start corner (in four-tile units from the block corner), its direction (+Y, or −X),
    /// whether a complete edge hides others (<see cref="Occludes"/>), whether it fades (<see cref="Fades"/>), the edges it
    /// hides when complete, and the ones it hides only while the player is in a given region.</summary>
    private sealed record Edge((int X, int Y) Start, bool AlongY, bool Occludes, bool Fades, int[] Hides,
        (AreaWallRegions Region, int[] Targets)[] HidesIn)
    {
        public (int X, int Y) End => AlongY ? (Start.X, Start.Y + 1) : (Start.X - 1, Start.Y);
    }

    private static Edge E(string name, string kind, string hides = "", string lowY = "", string lowX = "", string highY = "", string highX = "")
        => new(Parse(name).Start, Parse(name).AlongY, kind != "late", kind != "test", Names(hides),
            [(AreaWallRegions.LowY, Names(lowY)), (AreaWallRegions.LowX, Names(lowX)), (AreaWallRegions.HighY, Names(highY)), (AreaWallRegions.HighX, Names(highX))]);
    private static ((int X, int Y) Start, bool AlongY) Parse(string name)
    {
        var parts = name[2..^1].Split(',');
        return ((int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)), name[0] == 'Y');
    }
    private static int[] Names(string names) => names.Length == 0 ? Array.Empty<int>()
        : names.Split(' ').Select(name => Array.IndexOf(Order, name) is var index and >= 0 ? index : throw new InvalidOperationException("AREA_WALL_EDGE_NAME")).ToArray();

    // Edge names: "Y(x,y)" runs from lattice corner (x,y) to (x,y+1); "X(x,y)" from (x,y) to (x−1,y). The player's block
    // is the square (0,0)–(1,1); +X and +Y are towards the viewer. Evaluation order matters: an edge hidden before its
    // turn hides nothing. Kinds: "fade" occludes and fades, "test" only occludes, "late" only fades.
    private static readonly string[] Order =
    [
        "Y(1,0)", "X(1,1)", "X(1,0)", "Y(0,0)", "X(2,1)", "Y(1,1)", "Y(2,0)", "X(1,2)", "Y(1,-1)", "X(0,1)", "Y(2,1)",
        "X(2,2)", "X(2,0)", "Y(0,1)", "Y(3,0)", "X(1,3)", "Y(-1,0)", "X(1,-1)", "X(2,-1)", "Y(-1,1)", "X(3,1)", "Y(1,2)",
        "Y(2,-2)", "X(-1,2)", "Y(1,-2)", "X(-1,1)", "Y(2,-1)", "X(0,2)", "X(4,1)", "Y(1,3)", "Y(3,1)", "X(2,3)", "X(3,2)", "Y(2,2)",
    ];
    private static readonly Edge[] Edges =
    [
        E("Y(1,0)", "fade", "X(2,1) Y(2,0) Y(2,1) X(2,0) Y(3,0) X(3,1) Y(2,-1) X(4,1) Y(3,1) X(3,2)",
            lowY: "X(2,2) X(2,3) Y(2,2)", highY: "X(2,-1) Y(2,-2)", highX: "X(2,2) X(2,-1) Y(2,-2) X(2,3) Y(2,2)"),
        E("X(1,1)", "fade", "Y(1,1) X(1,2) X(2,2) Y(0,1) X(1,3) Y(1,2) X(0,2) Y(1,3) X(2,3) Y(2,2)",
            lowX: "Y(2,1) Y(3,1) X(3,2)", highY: "Y(2,1) Y(-1,1) X(-1,2) Y(3,1) X(3,2)", highX: "Y(-1,1) X(-1,2)"),
        E("X(1,0)", "test", "Y(1,-1) X(1,-1) X(2,-1) Y(2,-2) Y(1,-2)", lowY: "Y(2,-1)", lowX: "Y(2,-1)"),
        E("Y(0,0)", "test", "X(0,1) Y(-1,0) Y(-1,1) X(-1,2) X(-1,1)", lowY: "X(0,2)", lowX: "X(0,2)"),
        E("X(2,1)", "fade", "Y(2,1) Y(3,1) X(3,2)", lowY: "X(2,2) X(2,3) Y(2,2)", highX: "X(2,2) X(2,3) Y(2,2)"),
        E("Y(1,1)", "fade", "X(2,3) Y(2,2)", lowX: "Y(2,1) X(2,2) Y(3,1) X(3,2)", highY: "Y(2,1) X(2,2) Y(3,1) X(3,2)"),
        E("Y(2,0)", "fade", "Y(3,0) X(3,1) X(4,1)"),
        E("X(1,2)", "fade", "X(1,3) Y(1,2) Y(1,3)"),
        E("Y(1,-1)", "fade", "X(2,-1) Y(2,-2)", lowY: "Y(2,-1)", lowX: "Y(2,-1)"),
        E("X(0,1)", "fade", "Y(-1,1) X(-1,2)", lowY: "X(0,2)", lowX: "X(0,2)"),
        E("Y(2,1)", "fade", "Y(3,1) X(3,2)"),
        E("X(2,2)", "fade", "X(2,3) Y(2,2)"),
        E("X(2,0)", "test", "Y(2,-1)", highY: "Y(2,-2)", highX: "Y(2,-2)"),
        E("Y(0,1)", "test", "X(0,2)", highY: "X(-1,2)", highX: "X(-1,2)"),
        E("Y(3,0)", "fade", "X(4,1)"),
        E("X(1,3)", "fade", "Y(1,3)"),
        E("Y(-1,0)", "test", "X(-1,1)"),
        E("X(1,-1)", "test", "Y(1,-2)"),
        E("X(2,-1)", "test", "Y(2,-2)"),
        E("Y(-1,1)", "test", "X(-1,2)"),
        E("X(3,1)", "fade", lowY: "Y(3,1)", highX: "Y(3,1)"),
        E("Y(1,2)", "fade", lowX: "X(2,3)", highY: "X(2,3)"),
        E("Y(2,-2)", "late"), E("X(-1,2)", "late"), E("Y(1,-2)", "late"), E("X(-1,1)", "late"), E("Y(2,-1)", "late"), E("X(0,2)", "late"),
        E("X(4,1)", "late"), E("Y(1,3)", "late"), E("Y(3,1)", "late"), E("X(2,3)", "late"), E("X(3,2)", "late"), E("Y(2,2)", "late"),
    ];
}
