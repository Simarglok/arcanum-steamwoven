using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Content;

namespace Arcanum.TerrainViewer.Core;

public enum AreaExtraArtStatus { Ready, Unresolved, MissingFile, Unreadable, LimitExceeded }

/// <summary>One decoded frame of an art id no body drew at T0: its visual record (the art id, frame size and hotspot; a key of its
/// own, <see cref="AreaExtraArt.VisualKey"/>), its frame key in the map's repository and its pixels.</summary>
public sealed record AreaExtraFrame(AreaVisualRecord Visual, AreaFrameKey Key, DecodedAreaFrame Frame)
{
    /// <summary>ARC-COMBAT-016: the frames of the ART's group (a projectile's frame advances once per flight step).</summary>
    public int FramesPerGroup { get; init; } = 1;
    /// <summary>ARC-MAGIC-017: the ART header's frame rate and action frame (an eye candy's pace and release frame).</summary>
    public int FramesPerSecond { get; init; }
    public int ActionFrame { get; init; }
}

public sealed record AreaExtraArtLoad(AreaExtraArtStatus Status, AreaExtraFrame? Frame, string Detail);

/// <summary>Explicit limits of the extra art one viewer run reads from one module (every map it shows): distinct art ids, their
/// pixels and one file's size. An art id past them is refused and kept refused.</summary>
public sealed record AreaExtraArtLimits(int MaxArts = 4096, long MaxRgbaBytes = 64L * 1048576, int MaxFileBytes = 16 * 1048576);

/// <summary>
/// ARC-RENDER-033: the frames of art ids that no body of the shown map drew at T0: a scenery's or trap's destroyed ART (field 24)
/// and an item's ground ART once it lies on the ground (spilled from a container, dropped by an NPC, placed by the hero); since
/// ARC-MAGIC-017 also the eye-candy overlays of spells (class 14). The art
/// id names its file by the area owner's rules (<see cref="AreaObjectArt"/>, classes 4, 6 and 7), read from the game's content
/// stack in its search order with the owner's frame rules: the frame the id selects, group 0 for a single-rotation ART, the ART's
/// own extent when bytes follow its last frame. Each art id is read once; a failure is kept and reported, never retried.
/// </summary>
public sealed class AreaExtraArt : IDisposable
{
    public const string Policy = "steamwoven-extra-art-v1";

    private readonly ContentStack _stack;
    private readonly AreaExtraArtLimits _limits;
    private readonly Dictionary<AreaMappingRole, IReadOnlyDictionary<int, string>?> _tables = [];
    private readonly Dictionary<uint, AreaExtraArtLoad> _loads = [];
    private long _rgbaBytes;

    /// <param name="stack">The game's content stack; this object owns it and disposes it.</param>
    public AreaExtraArt(ContentStack stack, AreaExtraArtLimits? limits = null)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _limits = limits ?? new();
        if (_limits.MaxArts <= 0 || _limits.MaxRgbaBytes <= 0 || _limits.MaxFileBytes <= 0) throw new ArgumentOutOfRangeException(nameof(limits));
    }

    /// <summary>The art ids read so far, with their outcome.</summary>
    public IReadOnlyDictionary<uint, AreaExtraArtLoad> Loads => _loads;

    /// <summary>The visual key of an extra frame: layer <see cref="AreaLayerKind.Body"/> on the source kind
    /// <see cref="AreaSourceKind.Static"/> of runtime map 0 (no map's sector), the art id as its index.</summary>
    public static AreaVisualKey VisualKey(uint aid) => new(AreaSourceKind.Static, new("extra-art", 0, 0), unchecked((int)aid), Policy);

    public AreaExtraArtLoad Load(uint aid)
    {
        if (_loads.TryGetValue(aid, out var known)) return known;
        var load = _loads.Count >= _limits.MaxArts ? new(AreaExtraArtStatus.LimitExceeded, null, $"more than {_limits.MaxArts} art ids") : Read(aid);
        if (load.Frame is { } frame) _rgbaBytes += frame.Frame.Rgba.Length;
        _loads[aid] = load;
        return load;
    }

    private AreaExtraArtLoad Read(uint aid)
    {
        var resolved = AreaObjectArt.Resolve(aid, (role, key) => Table(role) is { } table && table.TryGetValue(key, out var name) ? name : null);
        if (resolved.Path is not { } selection) return new(AreaExtraArtStatus.Unresolved, null, $"{resolved.Status} {resolved.Detail}");
        var found = _stack.Resolve(selection.Path);
        if (found.Winner is not { } winner) return new(AreaExtraArtStatus.MissingFile, null, selection.Path);
        if (winner.Length > _limits.MaxFileBytes) return new(AreaExtraArtStatus.LimitExceeded, null, $"{selection.Path} {winner.Length} bytes");
        var read = _stack.Read(winner);
        if (read.Status != ContentReadStatus.Read || read.Bytes is not { } bytes) return new(AreaExtraArtStatus.Unreadable, null, $"{selection.Path} {read.Status}");
        try
        {
            var options = new ArtDecoderOptions { MaxInputBytes = _limits.MaxFileBytes, MaxEncodedFrameBytes = _limits.MaxFileBytes, AllowTrailingBytes = true };
            var metadata = ArtDecoder.Inspect(bytes, options);
            // A single-rotation ART has only group 0 whatever rotation the id asks for (the owner's rule).
            var group = bytes.Length >= 4 && (BinaryPrimitives.ReadUInt32LittleEndian(bytes) & 1) == 1 ? 0 : selection.Group;
            if (group >= metadata.Header.PhysicalGroupCount || selection.Frame >= metadata.Header.FramesPerGroup)
                return new(AreaExtraArtStatus.Unreadable, null, $"{selection.Path} group {group} frame {selection.Frame}");
            var view = bytes.Length - metadata.TrailingBytes;
            var decoded = ArtDecoder.Decode(bytes.AsSpan(0, view), new ArtFrameSelection(group, selection.Frame, selection.Palette), options);
            var rgba = decoded.Rgba.ToArray();
            if (_rgbaBytes + rgba.Length > _limits.MaxRgbaBytes) return new(AreaExtraArtStatus.LimitExceeded, null, $"{selection.Path} pixels");
            var payload = Digest(bytes.AsSpan(0, view)); var pixels = Digest(rgba);
            var entry = new AreaEntryIdentity(new(AreaSourceRole.Loose, -1), 0, 0, 0, 0, 0, Digest(System.Text.Encoding.ASCII.GetBytes(found.Key)));
            var visual = new AreaVisualRecord(VisualKey(aid), aid, selection.Lookup, entry, payload, view, selection.Nominal, selection.Group, group, selection.Frame,
                selection.Palette, decoded.EffectivePaletteSlot, false, decoded.Width, decoded.Height, decoded.Frame.HotX, decoded.Frame.HotY,
                decoded.Frame.OffsetX, decoded.Frame.OffsetY, 0, 0, 100, decoded.Frame.EncodedPayloadLength, rgba.Length, pixels, Policy);
            return new(AreaExtraArtStatus.Ready, new(visual, AreaFrameKey.From(visual), new(decoded.Width, decoded.Height, rgba))
                { FramesPerGroup = metadata.Header.FramesPerGroup, FramesPerSecond = metadata.Header.FramesPerSecond, ActionFrame = metadata.Header.ActionFrame },
                $"{selection.Path} frame {selection.Frame}");
        }
        catch (Exception error) when (error is ArtException or ArgumentException)
        {
            return new(AreaExtraArtStatus.Unreadable, null, $"{selection.Path} {error.GetType().Name}");
        }
    }

    private IReadOnlyDictionary<int, string>? Table(AreaMappingRole role)
    {
        if (_tables.TryGetValue(role, out var table)) return table;
        var found = _stack.Resolve(AreaObjectArt.MappingPath(role));
        table = found.Winner is { } winner && _stack.Read(winner) is { Status: ContentReadStatus.Read, Bytes: { } bytes } ? AreaObjectArt.ReadNameTable(bytes) : null;
        _tables[role] = table;
        return table;
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose() => _stack.Dispose();
}
