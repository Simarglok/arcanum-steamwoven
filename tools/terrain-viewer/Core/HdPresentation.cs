using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Hd;
using Arcanum.Data.Terrain;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Which texture draws a frame key (ARC-RENDER-013 visual profile). Nothing else
/// depends on it: plans, picking, routes, light, wall check and Session read Legacy data.</summary>
public enum VisualProfile { Legacy, Hd }

/// <summary>Why one frame drew Legacy under profile <see cref="VisualProfile.Hd"/>
/// (ARC-RENDER-013, Frame binding). Every value is a per-frame Legacy draw; none fails the
/// view, the map load or the session.</summary>
public enum HdFallbackReason { NoPack, PackRejected, FrameMissing, SourceMismatch, FrameRejected, Invalid, HdAxisLimit, HdBudget, Pending }

/// <summary>The portable HD question for one Legacy frame: the install-independent frame key,
/// the ART name hash and the Legacy size and RGBA hash this installation decodes.</summary>
public static class HdFrameQueries
{
    /// <summary>An AREA frame: every part comes from the frame key the scene plan already
    /// carries (<see cref="AreaFrameKey"/>); no pixel is hashed again.</summary>
    public static HdSpriteQuery FromArea(AreaFrameKey key, DecodedAreaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(frame);
        return new(new(key.PayloadSha256, key.ViewLength, key.Group, key.Frame, key.Palette), key.Entry.NameSha256,
            new(frame.Width, frame.Height, key.RgbaSha256));
    }

    /// <summary>ARC-RENDER-016: the question for one composed level-0 terrain chunk.
    /// <paramref name="composed"/> is <see cref="AreaTerrainChunks.Compose"/> at level 0 (the
    /// Legacy chunk this installation draws, hashed here); the tile list comes from the plan.</summary>
    public static HdSpriteQuery FromTerrainChunk(AreaTerrainChunks terrain, (int X, int Y) chunk, ReadOnlySpan<byte> composed)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var side = AreaTerrainChunks.Side(0);
        if (composed.Length != (long)side * side * 4) throw new ArgumentException("A composed level-0 chunk is size × size × 4 bytes.", nameof(composed));
        return HdSpriteQuery.ForTerrainChunk(new(chunk.X, chunk.Y, HdChunkKey.TilesDigest(TerrainChunkTiles(terrain, chunk))),
            new(side, side, Sha256(composed)));
    }

    /// <summary>The Legacy RGBA SHA-256 of every tile and facade frame composed into a chunk, in
    /// plan order (the pack's <c>tiles</c> list; the frame keys already carry the hashes).</summary>
    public static IReadOnlyList<string> TerrainChunkTiles(AreaTerrainChunks terrain, (int X, int Y) chunk)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return terrain.CommandsIn(chunk).Select(command => command.Frame.RgbaSha256).ToArray();
    }

    /// <summary>A frame decoded outside the AREA owner (the controlled actor, WORLD statics):
    /// the SHA-256 of the whole ART entry, the bytes it was decoded from, the SHA-256 of the
    /// raw entry name, the physical group, frame and effective palette slot, and the decoded
    /// Legacy RGBA, hashed here.</summary>
    public static HdSpriteQuery FromArt(string payloadSha256, int viewLength, string nameSha256, int group, int frame, int palette,
        int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentException.ThrowIfNullOrEmpty(payloadSha256);
        ArgumentException.ThrowIfNullOrEmpty(nameSha256);
        if (width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4) throw new ArgumentException("Legacy RGBA must be width × height × 4 bytes.", nameof(rgba));
        return new(new(payloadSha256, viewLength, group, frame, palette), nameSha256, new(width, height, Sha256(rgba)));
    }

    /// <summary>The ART identity of one archive entry, as <see cref="FromArt"/> takes it: the
    /// SHA-256 of its decoded bytes, their length and the SHA-256 of its raw name bytes.</summary>
    public static (string PayloadSha256, int ViewLength, string NameSha256) ArtIdentity(ReadOnlySpan<byte> payload, IReadOnlyList<byte> rawName)
    {
        ArgumentNullException.ThrowIfNull(rawName);
        return (Sha256(payload), payload.Length, Sha256(rawName.ToArray()));
    }

    /// <summary>Lowercase hex SHA-256, the form every pack digest uses.</summary>
    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>The HD source crop of ARC-RENDER-013: the Legacy crop <c>(c, m)</c> becomes
/// <c>(N·c, N·m)</c> on each axis, with the destination, reflection and every other input of
/// the exact rule unchanged.</summary>
public static class HdCrop
{
    /// <summary>Largest <c>c + m</c> the 16-bit index rows hold (<see cref="AreaNearestAxis"/>).</summary>
    public const int MaxAxisEnd = 65534;

    public static TerrainRect Scale(TerrainRect legacy, int scale)
    {
        if (scale < 1) throw new ArgumentOutOfRangeException(nameof(scale));
        return new(checked(legacy.X * scale), checked(legacy.Y * scale), checked(legacy.Width * scale), checked(legacy.Height * scale));
    }

    /// <summary>False when <c>N·(c + m) &gt; 65534</c> on either axis: that frame draws Legacy
    /// (<see cref="HdFallbackReason.HdAxisLimit"/>).</summary>
    public static bool Fits(TerrainRect legacy, int scale)
        => (long)scale * ((long)legacy.X + legacy.Width) <= MaxAxisEnd && (long)scale * ((long)legacy.Y + legacy.Height) <= MaxAxisEnd;
}

/// <summary>Counts of one submission: items drawn HD, and Legacy draws by reason. Items that
/// are never HD candidates (terrain, chunks, walls in this stage) are not counted.</summary>
public sealed class HdSubmissionStats
{
    private readonly int[] _fallback = new int[Enum.GetValues<HdFallbackReason>().Length];
    public int Hd { get; private set; }
    public int Fallback => _fallback.Sum();
    public int this[HdFallbackReason reason] => _fallback[(int)reason];
    public void Clear() { Hd = 0; Array.Clear(_fallback); }
    public void Add(HdFallbackReason? reason)
    {
        if (reason is { } r) _fallback[(int)r]++;
        else Hd++;
    }
    /// <summary>"HD 12, Legacy 3 (Pending 2, FrameMissing 1)"; reasons in enum order, zero
    /// counts omitted.</summary>
    public string Format()
    {
        var text = new StringBuilder($"HD {Hd}, Legacy {Fallback}");
        var reasons = Enum.GetValues<HdFallbackReason>().Where(r => this[r] > 0).Select(r => $"{r} {this[r]}").ToArray();
        if (reasons.Length > 0) text.Append(" (").Append(string.Join(", ", reasons)).Append(')');
        return text.ToString();
    }
}

/// <summary>One domain's HD limits (ARC-RENDER-013 Budgets, lowered per device).</summary>
/// <param name="VisibleBytes">HD bytes the frames of one submission may use.</param>
/// <param name="RetainFrames">HD frames kept after they left the view.</param>
/// <param name="RetainBytes">HD bytes kept after they left the view.</param>
/// <param name="TotalBytes">Optional cap on visible + retained (WORLD shares one HD byte limit).</param>
public sealed record HdDomainBudget(long VisibleBytes, int RetainFrames, long RetainBytes, long? TotalBytes = null)
{
    /// <summary>AREA: visible and retained limits of the device budget.</summary>
    public static HdDomainBudget Area(HdDeviceBudget device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return new(device.AreaVisibleBytes, device.AreaRetainedFrames, device.AreaRetainedBytes);
    }
    /// <summary>ARC-RENDER-016 AREA terrain chunks: one HD byte limit for visible and kept chunks
    /// (the reserved terrain ceiling, N² × the Legacy 192 MiB chunk cache, i.e. the same 192 chunks)
    /// and the Legacy chunk count cap (1,024) as the frame count.</summary>
    public static HdDomainBudget Terrain(HdDeviceBudget device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return new(device.TerrainBytes, TerrainChunkCap, device.TerrainBytes, device.TerrainBytes);
    }
    /// <summary>The Legacy <c>AreaChunkTextureCache</c> chunk count cap.</summary>
    public const int TerrainChunkCap = 1024;
    /// <summary>WORLD: one HD byte limit for the actor and static textures, visible or kept, and
    /// the Legacy key caps (195 actor + 5 static) as the frame count.</summary>
    public static HdDomainBudget World(HdDeviceBudget device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return new(device.WorldBytes, TerrainCachePolicy.MaxActorDistinctKeys + TerrainCachePolicy.MaxStaticDistinctKeys, device.WorldBytes, device.WorldBytes);
    }
}
