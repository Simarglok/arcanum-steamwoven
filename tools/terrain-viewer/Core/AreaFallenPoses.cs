using System.Collections;
using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Simulation.Combat;

namespace Arcanum.TerrainViewer.Core;

public enum AreaFallenStatus
{
    Ready,
    /// <summary>The body is not a critter, monster or unique NPC art (class 2, 12, 13), or it is drawn from bad art.</summary>
    NotACritterArt,
    /// <summary>The body's art comes from no archive the viewer holds (a loose file or an unknown source).</summary>
    SourceUnavailable,
    /// <summary>The archive's entry for the body is not the one the published snapshot read.</summary>
    EntryMismatch,
    /// <summary>Neither the pose's ART nor the fall-down ART exists next to the body's ART.</summary>
    PoseMissing,
    /// <summary>The pose's ART could not be read or decoded, or has no such group.</summary>
    PoseUnreadable,
}

/// <summary>One fallen pose: its frame key, the decoded last frame and its geometry, and the animation it shows.</summary>
public sealed record AreaFallenFrame(AreaFrameKey Key, DecodedAreaFrame Frame, int HotX, int HotY, int Animation);

public sealed record AreaFallenLoad(AreaFallenStatus Status, AreaFallenFrame? Pose, string Detail);

/// <summary>
/// ARC-COMBAT-005: the pose a critter ends in when it dies or falls unconscious, read on demand from the archive its standing art
/// came from. The original sets the dying animation (7 the fall for a death of at most 20 damage or a kill, 17 decapitation for a
/// head hit, 19 severed leg for a leg hit, 18 otherwise; ledger FA10) or the fall-down animation (7) for a knockdown, plays it and
/// keeps its last frame. A critter's ART name ends in the shield, weapon and animation letters, and an animation from 6 to 19
/// drops shield and weapon, so the pose's file is the body's name with <c>x</c>, <c>a</c> and the animation letter; a bloody death
/// without its file falls back to the fall (the original's missing-art repair, ledger MA1). The archive entry the body was drawn
/// from must be the one the published snapshot names (ordinal, flags, sizes, offset, name digest), so a changed archive is
/// refused. Godot-free; a typed status, never an exception for data.
/// </summary>
public static class AreaFallenPoses
{
    public const int FallDown = 7, Decapitation = 17, BlownOutChunk = 18, SeveredLeg = 19;
    public const string Policy = "steamwoven-fallen-pose-v1";
    private static readonly DatReaderOptions Options = new() { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 };

    /// <summary>The animation the original keeps a critter in: the dying animation of a death, the fall of a knockout.</summary>
    public static int Animation(DeathAnimation death) => death switch
    {
        DeathAnimation.Head => Decapitation,
        DeathAnimation.Leg => SeveredLeg,
        DeathAnimation.Body => BlownOutChunk,
        _ => FallDown,
    };

    /// <summary>Reads the last frame of <paramref name="animation"/> for <paramref name="visual"/>'s facing from the archive at
    /// <paramref name="archivePath"/> (the source of the visual's entry).</summary>
    public static AreaFallenLoad Load(AreaVisualRecord visual, string? archivePath, int animation)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (visual.RawArt >> 28 is not (2 or 12 or 13) || visual.Policy.Contains("badart", StringComparison.Ordinal))
            return Refused(AreaFallenStatus.NotACritterArt, $"art class {visual.RawArt >> 28}");
        if (animation is not (FallDown or Decapitation or BlownOutChunk or SeveredLeg)) throw new ArgumentOutOfRangeException(nameof(animation));
        if (archivePath is null || !File.Exists(archivePath)) return Refused(AreaFallenStatus.SourceUnavailable, visual.Entry.Source.ToString());
        try
        {
            using var archive = DatArchive.Open(archivePath, Options);
            var standing = archive.Entries.Where(e => e.Ordinal == visual.Entry.Ordinal).ToArray();
            if (standing.Length != 1 || standing[0].Flags != visual.Entry.Flags || standing[0].StoredSize != visual.Entry.StoredBytes ||
                standing[0].DeclaredOutputSize != visual.Entry.DecodedBytes || standing[0].Offset != visual.Entry.Offset ||
                Digest(standing[0].RawNameBytes.ToArray()) != visual.Entry.NameSha256)
                return Refused(AreaFallenStatus.EntryMismatch, $"ordinal {visual.Entry.Ordinal}");
            var name = Name(standing[0]);
            var dot = name.LastIndexOf('.');
            if (dot < 4) return Refused(AreaFallenStatus.PoseMissing, "name");
            DatEntry? Find(int anim)
            {
                var wanted = string.Concat(name.AsSpan(0, dot - 3), "xa", ((char)('a' + anim)).ToString(), name.AsSpan(dot));
                var found = archive.Entries.Where(e => string.Equals(Name(e), wanted, StringComparison.OrdinalIgnoreCase)).ToArray();
                return found.Length == 1 ? found[0] : null;
            }
            var used = animation;
            var entry = Find(animation);
            if (entry is null && animation != FallDown) { used = FallDown; entry = Find(FallDown); }
            if (entry is null) return Refused(AreaFallenStatus.PoseMissing, $"animation {animation}");
            var bytes = archive.ReadEntry(entry);
            var options = new ArtDecoderOptions { MaxInputBytes = 1024 * 1024, MaxFrameCount = 8 * 64 };
            var metadata = ArtDecoder.Inspect(bytes, options);
            if (visual.PhysicalGroup >= metadata.Header.PhysicalGroupCount || metadata.Header.FramesPerGroup < 1)
                return Refused(AreaFallenStatus.PoseUnreadable, $"group {visual.PhysicalGroup}");
            var last = metadata.Header.FramesPerGroup - 1;
            var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(visual.PhysicalGroup, last, visual.RequestedPalette), options);
            var rgba = decoded.Rgba.ToArray();
            var identity = new AreaEntryIdentity(visual.Entry.Source, entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset,
                Digest(entry.RawNameBytes.ToArray()));
            var key = new AreaFrameKey(identity, decoded.EffectivePaletteSlot, visual.PhysicalGroup, last, Policy, Digest(bytes), bytes.Length, Digest(rgba));
            return new(AreaFallenStatus.Ready, new(key, new(decoded.Width, decoded.Height, rgba), decoded.Frame.HotX, decoded.Frame.HotY, used),
                $"{Name(entry)} frame {last}");
        }
        catch (Exception error) when (error is DatException or ArtException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Refused(AreaFallenStatus.PoseUnreadable, error.GetType().Name + " " + error.Message);
        }
    }

    private static string Name(DatEntry entry) => System.Text.Encoding.ASCII.GetString(entry.RawNameBytes.ToArray()).TrimEnd('\0', ' ').Replace('\\', '/');

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static AreaFallenLoad Refused(AreaFallenStatus status, string detail) => new(status, null, detail);
}

/// <summary>
/// ARC-COMBAT-005: a map's frame repository with the fallen poses added at run time. The canvas and the pick keep one instance
/// per published map, so adding a pose never replaces the repository the texture cache is bound to.
/// </summary>
public sealed class AreaFrameOverlay : IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame>
{
    private readonly Dictionary<AreaFrameKey, DecodedAreaFrame> _added = [];

    public AreaFrameOverlay(IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> map) => Map = map ?? throw new ArgumentNullException(nameof(map));

    public IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Map { get; }

    /// <summary>Adds a frame; a key already present must hold the same pixels.</summary>
    public void Add(AreaFrameKey key, DecodedAreaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(frame);
        if (TryGetValue(key, out var known))
        {
            if (known.Width != frame.Width || known.Height != frame.Height || !known.Rgba.AsSpan().SequenceEqual(frame.Rgba))
                throw new InvalidOperationException("AREA_FALLEN_FRAME_CONFLICT");
            return;
        }
        _added.Add(key, frame);
    }

    public DecodedAreaFrame this[AreaFrameKey key] => TryGetValue(key, out var frame) ? frame : throw new KeyNotFoundException();
    public IEnumerable<AreaFrameKey> Keys => Map.Keys.Concat(_added.Keys);
    public IEnumerable<DecodedAreaFrame> Values => Map.Values.Concat(_added.Values);
    public int Count => Map.Count + _added.Count;
    public bool ContainsKey(AreaFrameKey key) => Map.ContainsKey(key) || _added.ContainsKey(key);
    public bool TryGetValue(AreaFrameKey key, out DecodedAreaFrame value) => Map.TryGetValue(key, out value!) || _added.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<AreaFrameKey, DecodedAreaFrame>> GetEnumerator() => Map.Concat(_added).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
