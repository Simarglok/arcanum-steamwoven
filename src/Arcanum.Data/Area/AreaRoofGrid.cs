namespace Arcanum.Data.Area;

/// <summary>
/// ARC-RENDER-021: the roofs of one map and their fill and fade state, with the original's rules for them
/// (docs/rendering/roofs.md, ledger RF1–RF10). A roof block covers 4 × 4 tiles; its art holds the piece, the fill bit
/// (the roof is hidden: the player is under it) and the fade bit (a faded wall in front of it). The grid starts from
/// the state the map stores at T0. Caller-serialized; no I/O. <see cref="Revision"/> moves on every change.
/// </summary>
public sealed class AreaRoofGrid
{
    private const uint FillBit = 0x2000, FadeBit = 0x1000;
    private readonly Dictionary<(long, long), uint> _art = new();
    private readonly Dictionary<(long, long), AreaRoofRecord> _records = new();
    // Walls of the wall check per normalized roof tile (RF9).
    private readonly Dictionary<(long, long), int> _wallFades = new();

    // The tiles of a 4 × 4 block a piece covers, [piece][row = y % 4][column = x % 4] (verified in the executable, RF4).
    private static readonly bool[][][] Bands = BuildBands();

    public AreaRoofGrid(IEnumerable<AreaRoofRecord> roofs)
    {
        ArgumentNullException.ThrowIfNull(roofs);
        foreach (var roof in roofs)
        {
            var block = (roof.BlockX >> 2, roof.BlockY >> 2);
            if ((roof.BlockX & 3) != 0 || (roof.BlockY & 3) != 0 || roof.Piece is < 0 or > 12 || !_records.TryAdd(block, roof))
                throw new ArgumentException("AREA_ROOF_BLOCK_INVALID", nameof(roofs));
            _art[block] = roof.RawArt;
        }
    }

    public long Revision { get; private set; }
    public int Count => _records.Count;
    public IEnumerable<AreaRoofRecord> Records => _records.Values;

    /// <summary>The current fill and fade bits of one of the grid's roofs (a record of another grid is refused).</summary>
    public (bool Fill, bool Fade) State(AreaRoofRecord roof)
    {
        ArgumentNullException.ThrowIfNull(roof);
        var art = _art[(roof.BlockX >> 2, roof.BlockY >> 2)];
        return ((art & FillBit) != 0, (art & FadeBit) != 0);
    }

    /// <summary>The roof slot of a sector-local tile index (<c>x + 64·y</c>): <c>x/4 + 16·(y/4)</c>.</summary>
    [OriginalFunction(0x4395C0, OriginalFunctionScope.Full)]
    public static int Slot(int tile) => ((tile >> 2) & 0xF) + ((tile >> 8) << 4);

    /// <summary>The tile a roof block is drawn from: its first tile plus 2 on each axis.</summary>
    [OriginalFunction(0x4395E0, OriginalFunctionScope.Full)]
    public static (long X, long Y) Normalize(long x, long y) => (x - x % 4 + 2, y - y % 4 + 2);

    /// <summary>Where a roof block is drawn: the screen position of its normalized tile (<paramref name="locationXy"/>, the
    /// tile's screen origin) moved 120 left and 200 up; the frame's hot spot is subtracted from it.</summary>
    [OriginalFunction(0x439640, OriginalFunctionScope.Full)]
    public static (long X, long Y) ScreenPosition(long x, long y, Func<long, long, (long X, long Y)> locationXy)
    {
        ArgumentNullException.ThrowIfNull(locationXy);
        var (nx, ny) = Normalize(x, y);
        var (sx, sy) = locationXy(nx, ny);
        return (sx - 120, sy - 200);
    }

    /// <summary>The roof art of the block holding the tile, or null.</summary>
    [OriginalFunction(0x4396A0, OriginalFunctionScope.Full)]
    public uint? Art(long x, long y) => _art.TryGetValue((x >> 2, y >> 2), out var art) ? art : null;

    /// <summary>The player entered tile (<paramref name="x"/>, <paramref name="y"/>): the block under it is filled when the
    /// piece's band holds the tile, unfilled otherwise, and a change floods to the neighbouring blocks.</summary>
    [OriginalFunction(0x439D30, OriginalFunctionScope.Full)]
    public void Recalc(long x, long y)
    {
        if (Art(x, y) is not { } art) return;
        var fill = Bands[Piece(art)][(int)(y & 3)][(int)(x & 3)];
        // The original reads the band of the tile itself here (no +3 as for coverage); the bands equal its conditions.
        if (((art & FillBit) != 0) != fill)
        {
            var (nx, ny) = Normalize(x, y);
            Fill(nx, ny, fill, -1);
        }
    }

    [OriginalFunction(0x439EA0, OriginalFunctionScope.Full)]
    public void FillOff(long x, long y) { var (nx, ny) = Normalize(x, y); Fill(nx, ny, false, -1); }

    [OriginalFunction(0x439EC0, OriginalFunctionScope.Full)]
    public void FillOn(long x, long y) { var (nx, ny) = Normalize(x, y); Fill(nx, ny, true, -1); }

    [OriginalFunction(0x439EE0, OriginalFunctionScope.Partial)]
    public void FadeOn(long x, long y) => SetBit(x, y, FadeBit, true);

    [OriginalFunction(0x439F20, OriginalFunctionScope.Full)]
    public void FadeOff(long x, long y) => SetBit(x, y, FadeBit, false);

    /// <summary>The roof a click can hit at a tile (the tile under the clicked point moved 120 pixels down): the roof of
    /// that tile's block when it is not filled, faded or not; null otherwise. Whether the click falls on an opaque pixel of
    /// its frame is the renderer's test.</summary>
    [OriginalFunction(0x439890, OriginalFunctionScope.Partial)]
    public AreaRoofRecord? HitCandidate(long x, long y) =>
        Art(x, y) is { } art && (art & FillBit) == 0 && _records.TryGetValue((x >> 2, y >> 2), out var roof) ? roof : null;

    /// <summary>A wall joined the wall check: the roof of its tile (normalized) counts it, and fades with its first wall.</summary>
    [OriginalFunction(0x438720, OriginalFunctionScope.Full)]
    public void AddWallFade(long x, long y)
    {
        var tile = Normalize(x, y);
        var count = _wallFades.GetValueOrDefault(tile) + 1;
        _wallFades[tile] = count;
        if (count == 1) FadeOn(tile.X, tile.Y);
    }

    /// <summary>A wall left the wall check: the roof of its tile unfades with its last wall. A release without a count is
    /// ignored (the original logs a warning).</summary>
    [OriginalFunction(0x438A50, OriginalFunctionScope.Full)]
    public void ReleaseWallFade(long x, long y)
    {
        var tile = Normalize(x, y);
        if (!_wallFades.TryGetValue(tile, out var count)) return;
        if (count == 1) { _wallFades.Remove(tile); FadeOff(tile.X, tile.Y); }
        else _wallFades[tile] = count - 1;
    }

    /// <summary>A roof is faded when it exists, is not filled and carries the fade bit.</summary>
    [OriginalFunction(0x439FA0, OriginalFunctionScope.Partial)]
    public bool IsFaded(long x, long y) => Art(x, y) is { } art && (art & FillBit) == 0 && (art & FadeBit) != 0;

    /// <summary>
    /// Whether a roof covers the tile: only an indoor tile (<paramref name="indoor"/>) can be covered, by the block holding
    /// the tile three further on both axes when it is not filled and its band holds that tile. Objects ask with
    /// <paramref name="countFaded"/> (a faded roof still covers them), tiles without it (they show under a faded roof).
    /// </summary>
    [OriginalFunction(0x43A030, OriginalFunctionScope.Partial)]
    public bool IsCovered(long x, long y, bool countFaded, Func<long, long, bool> indoor)
    {
        ArgumentNullException.ThrowIfNull(indoor);
        if (!indoor(x, y) || Art(x + 3, y + 3) is not { } art || (art & FillBit) != 0) return false;
        if (!Bands[Piece(art)][(int)((y + 3) % 4)][(int)((x + 3) % 4)]) return false;
        return countFaded || (art & FadeBit) == 0;
    }

    /// <summary>The four corner alphas of a faded roof piece on the original's hardware path (its default roof blit mode,
    /// constant alpha), drawn with alpha interpolated between them; an unfaded roof is opaque. The order of the four values
    /// is the blit's corner order (read as top left, top right, bottom right, bottom left; the blitter itself was not read).
    /// The software path stipples a faded roof instead.</summary>
    [OriginalFunction(0x439140, OriginalFunctionScope.Partial)]
    public static (byte TopLeft, byte TopRight, byte BottomRight, byte BottomLeft) FadeCorners(int piece, AreaRoofShades shades)
    {
        ArgumentNullException.ThrowIfNull(shades);
        byte o = shades.Opaque, p = shades.Partial, t = shades.Transparent;
        return piece switch
        {
            0 => (t, p, p, t), 1 => (p, o, p, t), 2 => (t, p, o, p), 3 => (p, o, o, p), 4 => (p, p, t, t), 5 => (o, o, p, p), 6 => (p, p, o, o),
            7 => (t, t, p, p), 8 => (o, o, o, o), 9 => (p, t, t, p), 10 => (o, p, t, p), 11 => (p, t, p, o), 12 => (o, p, p, o),
            _ => throw new ArgumentOutOfRangeException(nameof(piece)),
        };
    }

    /// <summary>The hot spot and x offset a roof frame is drawn with: a mirrored roof frame uses hot spot x 0 and offset x
    /// 0, unlike other mirrored art (RF7).</summary>
    [OriginalFunction(0x5031C0, OriginalFunctionScope.Partial)]
    public static (int HotX, int HotY, int OffsetX) HotSpot(AreaVisualRecord visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return visual.Mirrored ? (0, visual.HotY, 0) : (visual.HotX, visual.HotY, visual.ArtOffsetX);
    }

    /// <summary>The piece of a roof art: its frame, plus 9 when mirrored.</summary>
    [OriginalFunction(0x504840, OriginalFunctionScope.Partial)]
    public static int Piece(uint art) => (int)((art >> 14) & 31) + ((art & 1) != 0 ? 9 : 0);

    /// <summary>Sets the fill bit of the block at the tile and floods a change to the four neighbouring blocks. A flood
    /// arriving from a side (1: from the east, 3: from the south, 5: from the west, 7: from the north) stops at a piece
    /// whose outer edge faces it, so it stays inside one roof.</summary>
    [OriginalFunction(0x43A1A0, OriginalFunctionScope.Partial)]
    private void Fill(long x, long y, bool fill, int from)
    {
        var work = new Stack<(long X, long Y, int From)>();
        work.Push((x, y, from));
        while (work.TryPop(out var at))
        {
            if (Art(at.X, at.Y) is not { } art || ((art & FillBit) != 0) == fill) continue;
            var piece = Piece(art);
            var stop = at.From switch
            {
                1 => piece is 0 or 1 or 4, 3 => piece is 0 or 2 or 7, 5 => piece is 9 or 11 or 7, 7 => piece is 4 or 10 or 9, _ => false,
            };
            if (stop) continue;
            SetBit(at.X, at.Y, FillBit, fill);
            // The original recurses east, west, south, north in that order; the end state does not depend on the order.
            work.Push((at.X, at.Y - 4, 7)); work.Push((at.X, at.Y + 4, 3)); work.Push((at.X - 4, at.Y, 1)); work.Push((at.X + 4, at.Y, 5));
        }
    }

    private void SetBit(long x, long y, uint bit, bool value)
    {
        var block = (x >> 2, y >> 2);
        if (!_art.TryGetValue(block, out var art)) return;
        var next = value ? art | bit : art & ~bit;
        if (next == art) return;
        _art[block] = next; Revision++;
    }

    private static bool[][][] BuildBands()
    {
        // Edge bands inside a 4 × 4 block (row = y % 4, column = x % 4): west keeps columns 0..2, east 2..3, north rows
        // 2..3, south rows 0..2; outside corners intersect their two bands, inside corners unite them, the centre is all.
        static bool West(int r, int c) => c <= 2; static bool East(int r, int c) => c >= 2;
        static bool North(int r, int c) => r >= 2; static bool South(int r, int c) => r <= 2;
        Func<int, int, bool>[] pieces =
        [
            (r, c) => North(r, c) && West(r, c), West, North, (r, c) => North(r, c) || West(r, c),
            (r, c) => South(r, c) && West(r, c), (r, c) => South(r, c) || West(r, c), (r, c) => North(r, c) || East(r, c),
            (r, c) => North(r, c) && East(r, c), (_, _) => true, (r, c) => South(r, c) && East(r, c), South, East,
            (r, c) => South(r, c) || East(r, c),
        ];
        return pieces.Select(band => Enumerable.Range(0, 4).Select(r => Enumerable.Range(0, 4).Select(c => band(r, c)).ToArray()).ToArray()).ToArray();
    }
}
