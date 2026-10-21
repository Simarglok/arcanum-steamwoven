using System.Collections.ObjectModel;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Lighting;

/// <summary>Accumulated light at the lattice nodes of a screen region: node (c, r) sits at chart screen
/// position (40 * (FirstColumn + c), 20 * (FirstRow + r)). <see cref="Lighter"/> and <see cref="Darker"/>
/// are RGBA8 rows (alpha 255), saturating sums of the additive and the DARK lights.</summary>
public sealed class StationaryLightLattice
{
    internal StationaryLightLattice(int firstColumn, int firstRow, int columns, int rows, byte[] lighter, byte[] darker)
    {
        FirstColumn = firstColumn; FirstRow = firstRow; Columns = columns; Rows = rows;
        Lighter = Array.AsReadOnly(lighter); Darker = Array.AsReadOnly(darker);
    }
    public int FirstColumn { get; }
    public int FirstRow { get; }
    public int Columns { get; }
    public int Rows { get; }
    public IReadOnlyList<byte> Lighter { get; }
    public IReadOnlyList<byte> Darker { get; }

    /// <summary>The lighter/darker sums at an absolute node, zero outside the lattice.</summary>
    public (AmbientColor Lighter, AmbientColor Darker) At(int column, int row)
    {
        var c = column - FirstColumn; var r = row - FirstRow;
        if (c < 0 || r < 0 || c >= Columns || r >= Rows) return (default, default);
        var at = (r * Columns + c) * 4;
        return (new(Lighter[at], Lighter[at + 1], Lighter[at + 2]), new(Darker[at], Darker[at + 1], Darker[at + 2]));
    }
}

/// <summary>ARC-RENDER-031: where an animated light stands at a moment: its ART frame and its offsets (ledger LT7).</summary>
public readonly record struct LightPose(int Frame, int OffsetX, int OffsetY);

/// <summary>
/// FR-LIGHT-008 composition of stationary lights in one map chart (ARC-RENDER-012), following the source's
/// light rectangle and render functions and the TIG colour helpers (docs/rendering/area-lighting.md, F0). Independently written; parity of TIG rounding, blit LERP and palettes is
/// NOT_VERIFIED. A light covers the rectangle of its ART frame anchored like a body (tile screen
/// position + offset + (40, 20) − hotspot). Its contribution at a screen point is the frame pixel there,
/// multiplied by its tint (c * t / 255, truncated) unless the tint is white; keyed-out pixels and OFF
/// lights contribute nothing. Colours combine per channel with saturating add and floor-at-zero subtract.
/// </summary>
public sealed class StationaryLightField
{
    public const int NodeWidth = 40, NodeHeight = 20;
    /// <summary>ARC-RENDER-030 raised it from 2^20 for the object lights of a whole map (k'na tha spans the most).</summary>
    public const int MaxLatticeNodes = 1 << 22;
    // Point queries look only at the lights whose rectangle meets the query's bucket (source order kept).
    private const int BucketSide = 256;
    private readonly (StationaryLight Light, StationaryLightFrame Frame, TerrainRect Rect)[] _placements;
    private readonly TerrainPoint[] _tiles;
    private readonly Dictionary<(long, long), int[]> _buckets = new();

    public StationaryLightField(AreaStationaryLightSource source, long chartOriginX, long chartOriginY)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        _placements = source.Lights.Select(light =>
        {
            // ARC-RENDER-030: an object light shows its saved frame of the ART (frame 0 for a stationary light).
            var art = source.Frames[light.ArtNumber];
            if (light.Frame >= art.Frames.Count) throw new InvalidOperationException("LIGHT_FRAME_OUT_OF_RANGE");
            var frame = art.Frames[light.Frame];
            var tile = TerrainGeometry.Project(checked((int)(light.X - chartOriginX)), checked((int)(light.Y - chartOriginY)));
            return (light, frame, new TerrainRect(checked(tile.X + light.OffsetX + 40 - frame.HotX), checked(tile.Y + light.OffsetY + 20 - frame.HotY), frame.Width, frame.Height));
        }).ToArray();
        Placements = new ReadOnlyCollection<(StationaryLight, TerrainRect)>(_placements.Select(p => (p.Light, p.Rect)).ToArray());
        _tiles = source.Lights.Select(light => TerrainGeometry.Project(checked((int)(light.X - chartOriginX)), checked((int)(light.Y - chartOriginY)))).ToArray();
        // ARC-RENDER-031 (ledger LT6): a light animates when its ART has more than one frame and it does not carry 0x20.
        Animated = Array.AsReadOnly(Enumerable.Range(0, _placements.Length)
            .Where(i => source.Frames[_placements[i].Light.ArtNumber].Animated && (_placements[i].Light.Flags & StationaryLightFlags.NoAnimation) == 0).ToArray());
        var buckets = new Dictionary<(long, long), List<int>>();
        for (var index = 0; index < _placements.Length; index++)
        {
            var rect = _placements[index].Rect;
            for (var by = Floor(rect.Y); by <= Floor((long)rect.Y + rect.Height - 1); by++)
                for (var bx = Floor(rect.X); bx <= Floor((long)rect.X + rect.Width - 1); bx++)
                {
                    if (!buckets.TryGetValue((bx, by), out var list)) buckets[(bx, by)] = list = new();
                    list.Add(index);
                }
        }
        foreach (var (key, list) in buckets) _buckets[key] = list.ToArray();
    }

    private static long Floor(long value) => (long)Math.Floor(value / (double)BucketSide);

    /// <summary>The indexes of the animated lights, ascending.</summary>
    public IReadOnlyList<int> Animated { get; }

    /// <summary>
    /// ARC-RENDER-031: every animated light's pose after <paramref name="animationMs"/> of animation time from the load (ledger
    /// LT6, LT7): one step every ⌊1000 / frames per second⌋ ms; each step goes to the next frame, wrapping after the last, and
    /// moves the light by that frame's offset step. The light starts at its saved frame and offsets.
    /// </summary>
    public IReadOnlyDictionary<int, LightPose> PosesAt(long animationMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(animationMs);
        var poses = new Dictionary<int, LightPose>();
        foreach (var index in Animated)
        {
            var light = _placements[index].Light;
            var art = Source.Frames[light.ArtNumber];
            var frames = art.Frames;
            // A rate above 1,000 frames per second steps every 1 ms (the original's delay would be 0; none is installed).
            if (art.FramesPerSecond <= 0) throw new InvalidOperationException("LIGHT_ANIMATION_RATE");
            var steps = animationMs / Math.Max(1, 1000 / art.FramesPerSecond);
            long cycleX = frames.Sum(f => (long)f.StepX), cycleY = frames.Sum(f => (long)f.StepY);
            var cycles = steps / frames.Count; var rest = (int)(steps % frames.Count);
            long x = checked(light.OffsetX + cycles * cycleX), y = checked(light.OffsetY + cycles * cycleY);
            for (var j = 1; j <= rest; j++) { var f = frames[(light.Frame + j) % frames.Count]; x += f.StepX; y += f.StepY; }
            poses[index] = new((int)((light.Frame + steps) % frames.Count), checked((int)x), checked((int)y));
        }
        return poses;
    }

    private (StationaryLight Light, StationaryLightFrame Frame, TerrainRect Rect) Posed(int index, IReadOnlyDictionary<int, LightPose>? poses)
    {
        if (poses is null || !poses.TryGetValue(index, out var pose)) return _placements[index];
        var light = _placements[index].Light;
        var frame = Source.Frames[light.ArtNumber].Frames[pose.Frame];
        var tile = _tiles[index];
        return (light, frame, new TerrainRect(checked(tile.X + pose.OffsetX + 40 - frame.HotX), checked(tile.Y + pose.OffsetY + 20 - frame.HotY), frame.Width, frame.Height));
    }

    public AreaStationaryLightSource Source { get; }
    /// <summary>Every light with its chart screen rectangle, in source order.</summary>
    public IReadOnlyList<(StationaryLight Light, TerrainRect Rect)> Placements { get; }
    public bool HasActiveLights => _placements.Any(p => (p.Light.Flags & StationaryLightFlags.Off) == 0);

    /// <summary>The emitter tint: INDOOR takes the hour's indoor colour, else OUTDOOR the outdoor colour,
    /// else the light's own colour.</summary>
    public static AmbientColor Tint(StationaryLight light, AmbientPair pair) =>
        (light.Flags & StationaryLightFlags.Indoor) != 0 ? pair.Indoor : (light.Flags & StationaryLightFlags.Outdoor) != 0 ? pair.Outdoor : light.Color;

    public static AmbientColor Add(AmbientColor a, AmbientColor b) =>
        new((byte)Math.Min(a.R + b.R, 255), (byte)Math.Min(a.G + b.G, 255), (byte)Math.Min(a.B + b.B, 255));
    /// <summary><paramref name="from"/> minus <paramref name="amount"/>, floored at zero per channel.</summary>
    public static AmbientColor Subtract(AmbientColor from, AmbientColor amount) =>
        new((byte)Math.Max(from.R - amount.R, 0), (byte)Math.Max(from.G - amount.G, 0), (byte)Math.Max(from.B - amount.B, 0));
    public static AmbientColor Multiply(AmbientColor a, AmbientColor b) =>
        new((byte)(a.R * b.R / 255), (byte)(a.G * b.G / 255), (byte)(a.B * b.B / 255));
    /// <summary>A lattice node's colour over an ambient: (ambient + lighter) − darker.</summary>
    public static AmbientColor Compose(AmbientColor ambient, AmbientColor lighter, AmbientColor darker) => Subtract(Add(ambient, lighter), darker);

    private static bool TryContribution((StationaryLight Light, StationaryLightFrame Frame, TerrainRect Rect) placement, long x, long y,
        AmbientPair pair, out AmbientColor color)
    {
        color = default;
        var (light, frame, rect) = placement;
        if ((light.Flags & StationaryLightFlags.Off) != 0 || x < rect.X || y < rect.Y || x >= (long)rect.X + rect.Width || y >= (long)rect.Y + rect.Height) return false;
        if (!frame.TryPixel((int)(x - rect.X), (int)(y - rect.Y), out color)) return false;
        var tint = Tint(light, pair);
        if (tint != AmbientColor.Neutral) color = Multiply(color, tint);
        return true;
    }

    /// <summary>Point colour (the source's object sampling): starting from <paramref name="baseColor"/>, each
    /// admitted light in source order adds, or for DARK subtracts, its contribution at (x, y).</summary>
    public AmbientColor Point(long x, long y, AmbientColor baseColor, AmbientPair pair, Func<StationaryLight, bool>? admit = null,
        IReadOnlyDictionary<int, LightPose>? poses = null)
    {
        var color = baseColor;
        _buckets.TryGetValue((Floor(x), Floor(y)), out var near);
        // ARC-RENDER-031: a posed light is checked wherever it stands now; the rest by their bucket, all in source order.
        IEnumerable<int> order = near ?? [];
        if (poses is { Count: > 0 }) order = order.Where(i => !poses.ContainsKey(i)).Concat(poses.Keys).Order();
        foreach (var index in order)
        {
            var placement = Posed(index, poses);
            if (admit is not null && !admit(placement.Light)) continue;
            if (!TryContribution(placement, x, y, pair, out var contribution)) continue;
            color = (placement.Light.Flags & StationaryLightFlags.Dark) != 0 ? Subtract(color, contribution) : Add(color, contribution);
        }
        return color;
    }

    /// <summary>The terrain lattice over the union of the active lights' rectangles, or null when no
    /// light is active. Nodes outside it carry no light.</summary>
    public StationaryLightLattice? Lattice(AmbientPair pair, IReadOnlyDictionary<int, LightPose>? poses = null)
    {
        var active = Enumerable.Range(0, _placements.Length).Select(i => Posed(i, poses)).Where(p => (p.Light.Flags & StationaryLightFlags.Off) == 0).ToArray();
        if (active.Length == 0) return null;
        var left = active.Min(p => (long)p.Rect.X); var top = active.Min(p => (long)p.Rect.Y);
        var right = active.Max(p => (long)p.Rect.X + p.Rect.Width); var bottom = active.Max(p => (long)p.Rect.Y + p.Rect.Height);
        var firstColumn = (int)Math.Ceiling(left / (double)NodeWidth); var lastColumn = (int)Math.Floor((right - 1) / (double)NodeWidth);
        var firstRow = (int)Math.Ceiling(top / (double)NodeHeight); var lastRow = (int)Math.Floor((bottom - 1) / (double)NodeHeight);
        var columns = Math.Max(0, lastColumn - firstColumn + 1); var rows = Math.Max(0, lastRow - firstRow + 1);
        if (columns == 0 || rows == 0) return null;
        if ((long)columns * rows > MaxLatticeNodes) throw new InvalidOperationException("LIGHT_LATTICE_LIMIT");
        var lighter = new byte[columns * rows * 4]; var darker = new byte[columns * rows * 4];
        for (var at = 3; at < lighter.Length; at += 4) { lighter[at] = 255; darker[at] = 255; }
        // ARC-RENDER-030: each light adds into the nodes its rectangle covers; saturating sums of non-negative contributions
        // do not depend on the order, so this equals the node-by-node sum.
        foreach (var placement in active)
        {
            var rect = placement.Rect;
            var c0 = (int)Math.Max(firstColumn, Math.Ceiling(rect.X / (double)NodeWidth)) - firstColumn;
            var c1 = (int)Math.Min(lastColumn, Math.Floor(((long)rect.X + rect.Width - 1) / (double)NodeWidth)) - firstColumn;
            var r0 = (int)Math.Max(firstRow, Math.Ceiling(rect.Y / (double)NodeHeight)) - firstRow;
            var r1 = (int)Math.Min(lastRow, Math.Floor(((long)rect.Y + rect.Height - 1) / (double)NodeHeight)) - firstRow;
            var target = (placement.Light.Flags & StationaryLightFlags.Dark) != 0 ? darker : lighter;
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                {
                    long x = (long)(firstColumn + c) * NodeWidth, y = (long)(firstRow + r) * NodeHeight;
                    if (!TryContribution(placement, x, y, pair, out var contribution)) continue;
                    var at = (r * columns + c) * 4;
                    target[at] = (byte)Math.Min(target[at] + contribution.R, 255);
                    target[at + 1] = (byte)Math.Min(target[at + 1] + contribution.G, 255);
                    target[at + 2] = (byte)Math.Min(target[at + 2] + contribution.B, 255);
                }
        }
        return new(firstColumn, firstRow, columns, rows, lighter, darker);
    }

    /// <summary>The terrain colour at a screen pixel: the four surrounding nodes composed over the ambient
    /// and interpolated bilinearly (the source LERPs each 40×20 tile quadrant between its corner nodes).
    /// This is the reference the viewer shader implements; rounding parity with TIG is NOT_VERIFIED.</summary>
    public static (double R, double G, double B) TerrainPixel(StationaryLightLattice? lattice, AmbientColor ambient, double x, double y)
    {
        var column = (int)Math.Floor(x / NodeWidth); var row = (int)Math.Floor(y / NodeHeight);
        var tx = x / NodeWidth - column; var ty = y / NodeHeight - row;
        AmbientColor Node(int c, int r)
        {
            if (lattice is null) return ambient;
            var (plus, minus) = lattice.At(c, r);
            return Compose(ambient, plus, minus);
        }
        var a = Node(column, row); var b = Node(column + 1, row); var c0 = Node(column, row + 1); var d = Node(column + 1, row + 1);
        double Lerp(byte p, byte q, byte s, byte t) => (p * (1 - tx) + q * tx) * (1 - ty) + (s * (1 - tx) + t * tx) * ty;
        return (Lerp(a.R, b.R, c0.R, d.R), Lerp(a.G, b.G, c0.G, d.G), Lerp(a.B, b.B, c0.B, d.B));
    }
}
