using Arcanum.Data.Area;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-375: the decoded frames a map load keeps between loads of the process, under their <see cref="AreaFrameKey"/> (entry, palette, group, frame, policy,
/// payload digest, view length and pixel digest). A window move, a cave return and a cross-map quick load read mostly the frames the previous load
/// decoded; a hit serves the shared immutable <see cref="DecodedAreaFrame"/> (no consumer writes its RGBA) and costs no read, decode or batch.
/// <para>Frames are added only after their batch's <c>Complete</c> succeeded (<see cref="AreaLoadOperation"/>), so each passed the entry, payload, geometry,
/// palette and pixel checks and the batch's authentication; a faulted or cancelled batch adds nothing. Bounded by RGBA bytes and by count, least recently used
/// first; used by the map path only (the pinned exterior keeps no frame between operations). See docs/world/area-loader.md, "Resident frames and visuals across map loads".</para>
/// </summary>
public sealed class AreaResidentFrames
{
    /// <summary>RGBA bytes kept at most (the load result's own cap is 160 MiB).</summary>
    public const long MaxBytes = 256L * 1024 * 1024;
    /// <summary>Frames kept at most.</summary>
    public const int MaxFrames = 32768;

    /// <summary>The process-wide store the map path uses.</summary>
    public static AreaResidentFrames Shared { get; } = new();

    private readonly ResidentLru<AreaFrameKey, DecodedAreaFrame> _frames;

    public AreaResidentFrames(long maxBytes = MaxBytes, int maxFrames = MaxFrames) { _frames = new(maxBytes, maxFrames); }

    public int Count => _frames.Count;
    public long Bytes => _frames.Cost;
    /// <summary>Lookups that were served / not served from the store since construction or the last <see cref="Clear"/>.</summary>
    public (long Hits, long Misses) Lookups => _frames.Lookups;

    /// <summary>The kept frame for <paramref name="key"/> when it matches the descriptor's size (a mismatch is a miss).</summary>
    internal bool TryGet(AreaFrameKey key, int width, int height, out DecodedAreaFrame frame)
    {
        if (_frames.TryGet(key, out var kept) && kept.Width == width && kept.Height == height && kept.Rgba.Length == checked((long)width * height * 4))
        { frame = kept; return true; }
        frame = default!; return false;
    }

    internal void Add(AreaFrameKey key, DecodedAreaFrame frame) => _frames.Add(key, frame, frame.Rgba.Length);

    /// <summary>Forgets every frame (tests, or to give the memory back).</summary>
    public void Clear() => _frames.Clear();
}
