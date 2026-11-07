using Arcanum.Data.Terrain;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Pre-composed terrain for the area compositor: square world-aligned chunks
/// whose texels are exactly what the per-command terrain draws select under the
/// nearest-sampling rule. Built only when every terrain command is drawn 1:1, opaque
/// (alpha 255, not additive, no Y reflection), in phase 0/1 and samples only fully
/// transparent or fully opaque source texels; otherwise <see cref="TryCreate"/>
/// returns null and the caller keeps per-command drawing.</summary>
public sealed class AreaTerrainChunks
{
    public const int ChunkSize = 512;
    public const int MaxLevel = 3;
    private readonly IReadOnlyList<AreaDrawCommand> _terrain;
    private readonly IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> _frames;
    private readonly Dictionary<(int X, int Y), int[]> _chunks;

    private AreaTerrainChunks(IReadOnlyList<AreaDrawCommand> terrain, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames,
        Dictionary<(int X, int Y), int[]> chunks)
    { _terrain = terrain; _frames = frames; _chunks = chunks; }

    /// <summary>Chunks that contain terrain.</summary>
    public IReadOnlyCollection<(int X, int Y)> Chunks => _chunks.Keys;
    public int TerrainCommandCount => _terrain.Count;

    public static AreaTerrainChunks? TryCreate(AreaScenePlan plan, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return TryCreate(plan.Commands, frames);
    }

    // Command-list seam for synthetic equivalence tests; production passes the plan.
    internal static AreaTerrainChunks? TryCreate(IReadOnlyList<AreaDrawCommand> commands, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(commands); ArgumentNullException.ThrowIfNull(frames);
        var terrain = commands.Where(command => command.Identity is null).ToArray();
        // Terrain must precede every body: chunks are drawn first.
        var lastTerrain = -1; var firstBody = int.MaxValue;
        for (var i = 0; i < commands.Count; i++)
            if (commands[i].Identity is null) lastTerrain = i; else firstBody = Math.Min(firstBody, i);
        if (lastTerrain > firstBody) return null;
        var checkedCrops = new HashSet<(AreaFrameKey, TerrainRect)>();
        foreach (var command in terrain)
        {
            if (command.Phase is not (0 or 1) || command.Blended || command.FlipY
                || command.Source.Width != command.Destination.Width || command.Source.Height != command.Destination.Height
                || command.Source.Width <= 0 || command.Source.Height <= 0
                || !frames.TryGetValue(command.Frame, out var frame)
                || frame.Width <= 0 || frame.Height <= 0 || frame.Rgba.Length != (long)frame.Width * frame.Height * 4
                || command.Source.X < 0 || command.Source.Y < 0
                || (long)command.Destination.X + command.Destination.Width > int.MaxValue || (long)command.Destination.Y + command.Destination.Height > int.MaxValue
                || (long)command.Source.X + command.Source.Width > frame.Width || (long)command.Source.Y + command.Source.Height > frame.Height)
                return null;
            if (checkedCrops.Add((command.Frame, command.Source)) && !BinaryAlpha(frame, command.Source)) return null;
        }
        var chunks = new Dictionary<(int X, int Y), List<int>>();
        for (var index = 0; index < terrain.Length; index++)
        {
            var d = terrain[index].Destination;
            for (var cy = FloorDiv(d.Y, ChunkSize); cy <= FloorDiv(checked(d.Y + d.Height - 1), ChunkSize); cy++)
            for (var cx = FloorDiv(d.X, ChunkSize); cx <= FloorDiv(checked(d.X + d.Width - 1), ChunkSize); cx++)
            {
                if (!chunks.TryGetValue((cx, cy), out var list)) chunks.Add((cx, cy), list = []);
                list.Add(index);
            }
        }
        return new(terrain, frames, chunks.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()));
    }

    /// <summary>The terrain commands composed into one chunk, in plan order (ARC-RENDER-010
    /// uses it to give a chunk its single ambient channel).</summary>
    public IEnumerable<AreaDrawCommand> CommandsIn((int X, int Y) chunk) =>
        _chunks.TryGetValue(chunk, out var indices) ? indices.Select(index => _terrain[index]) : throw new ArgumentOutOfRangeException(nameof(chunk));

    /// <summary>Terrain chunks whose world square intersects the half-open world rectangle.</summary>
    public IEnumerable<(int X, int Y)> Intersecting(double x, double y, double width, double height)
    {
        if (!(width > 0) || !(height > 0)) yield break;
        var x0 = (int)Math.Floor(x / ChunkSize); var x1 = (int)Math.Floor((x + width) / ChunkSize);
        var y0 = (int)Math.Floor(y / ChunkSize); var y1 = (int)Math.Floor((y + height) / ChunkSize);
        for (var cy = y0; cy <= y1; cy++)
        for (var cx = x0; cx <= x1; cx++)
            if (_chunks.ContainsKey((cx, cy))) yield return (cx, cy);
    }

    /// <summary>Texture side for a decimation level: level k keeps world texels
    /// ≡ 2^(k-1) (mod 2^k), which are the only ones sampled at zoom 2^-k with an integer
    /// raster origin.</summary>
    public static int Side(int level)
    {
        if (level is < 0 or > MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        return ChunkSize >> level;
    }

    /// <summary>The draw command for a chunk texture: its world square as destination and
    /// the whole (possibly decimated) texture as source, drawn 1:1 per level.</summary>
    public static AreaDrawCommand Command((int X, int Y) chunk, int level)
    {
        var side = Side(level);
        return new(default, default!, null, new TerrainRect(0, 0, side, side),
            new TerrainRect(checked(chunk.X * ChunkSize), checked(chunk.Y * ChunkSize), ChunkSize, ChunkSize), false, false, 0, 0, 0, 0, 0, 255, false);
    }

    /// <summary>RGBA of one chunk: for each kept world texel, the last terrain command
    /// in plan order whose source texel there is opaque; transparent elsewhere.</summary>
    public byte[] Compose((int X, int Y) chunk, int level)
    {
        if (!_chunks.TryGetValue(chunk, out var indices)) throw new ArgumentOutOfRangeException(nameof(chunk));
        var side = Side(level); var step = 1 << level; var phase = level == 0 ? 0 : step >> 1;
        var originX = checked(chunk.X * ChunkSize); var originY = checked(chunk.Y * ChunkSize);
        var rgba = new byte[checked(side * side * 4)];
        foreach (var index in indices)
        {
            var command = _terrain[index]; var frame = _frames[command.Frame];
            var d = command.Destination; var s = command.Source;
            // Kept texel j covers world originX + j*step + phase; intersect with [d.X, d.X + d.Width).
            var j0 = Math.Max(0, CeilDiv(d.X - originX - phase, step)); var j1 = Math.Min(side, CeilDiv(d.X + d.Width - originX - phase, step));
            var i0 = Math.Max(0, CeilDiv(d.Y - originY - phase, step)); var i1 = Math.Min(side, CeilDiv(d.Y + d.Height - originY - phase, step));
            for (var i = i0; i < i1; i++)
            {
                var sy = s.Y + (originY + i * step + phase - d.Y);
                var row = sy * frame.Width;
                for (var j = j0; j < j1; j++)
                {
                    var offset = originX + j * step + phase - d.X;
                    var sx = command.FlipX ? s.X + s.Width - 1 - offset : s.X + offset;
                    var at = (row + sx) * 4;
                    if (frame.Rgba[at + 3] != 255) continue;
                    var to = (i * side + j) * 4;
                    rgba[to] = frame.Rgba[at]; rgba[to + 1] = frame.Rgba[at + 1]; rgba[to + 2] = frame.Rgba[at + 2]; rgba[to + 3] = 255;
                }
            }
        }
        return rgba;
    }

    private static bool BinaryAlpha(DecodedAreaFrame frame, TerrainRect crop)
    {
        for (var y = crop.Y; y < crop.Y + crop.Height; y++)
        for (var x = crop.X; x < crop.X + crop.Width; x++)
        {
            var alpha = frame.Rgba[(y * frame.Width + x) * 4 + 3];
            if (alpha is not (0 or 255)) return false;
        }
        return true;
    }
    private static int FloorDiv(int value, int divisor) => (int)Math.Floor((double)value / divisor);
    private static int CeilDiv(int value, int divisor) => (int)Math.Ceiling((double)value / divisor);
}
