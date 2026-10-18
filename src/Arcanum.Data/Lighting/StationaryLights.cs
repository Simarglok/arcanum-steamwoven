using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Content;

namespace Arcanum.Data.Lighting;

/// <summary>Serialized light flag bits whose source disposition is established (RES-018): OFF excludes
/// the light, DARK subtracts instead of adding, INDOOR/OUTDOOR take the emitter tint from the current
/// ambient pair (INDOOR first). Any other bit is refused, never guessed.</summary>
[Flags]
public enum StationaryLightFlags : uint { None = 0, Off = 1, Dark = 2, Indoor = 8, Outdoor = 16 }

/// <summary>One decoded stationary sector light (FR-LIGHT-008): the 48-byte wire of the pinned source
/// (owner word 0, absolute world location inside its own sector, signed screen offset, flags, light ART
/// id, colour stored r,b,g). The serialized tint, palette and padding words are not interpreted; they
/// stay bound through <see cref="RecordSha256"/>.</summary>
public sealed record StationaryLight(long Sector, int Index, long X, long Y, int OffsetX, int OffsetY,
    StationaryLightFlags Flags, uint ArtId, AmbientColor Color, string RecordSha256)
{
    /// <summary>Light ART number (bits 19..27), the key of <c>art/light/light.mes</c>.</summary>
    public int ArtNumber => (int)((ArtId >> 19) & 511);
}

/// <summary>Detached frame 0 of one static light ART: size, hotspot and an RGB contribution mask.
/// <see cref="Mask"/> is width*height RGBA; alpha 0 marks a pixel equal to the colour key
/// (palette index 0's colour), which contributes nothing.</summary>
public sealed class StationaryLightFrame
{
    internal StationaryLightFrame(int number, string name, string payloadSha256, int width, int height, int hotX, int hotY, byte[] mask)
    {
        Number = number; Name = name; PayloadSha256 = payloadSha256; Width = width; Height = height; HotX = hotX; HotY = hotY;
        Mask = Array.AsReadOnly(mask);
    }
    public int Number { get; }
    public string Name { get; }
    public string PayloadSha256 { get; }
    public int Width { get; }
    public int Height { get; }
    public int HotX { get; }
    public int HotY { get; }
    public IReadOnlyList<byte> Mask { get; }

    /// <summary>The source colour at a frame pixel, or false where the pixel is outside or keyed out.</summary>
    public bool TryPixel(int x, int y, out AmbientColor color)
    {
        color = default;
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        var at = (y * Width + x) * 4;
        if (Mask[at + 3] == 0) return false;
        color = new(Mask[at], Mask[at + 1], Mask[at + 2]);
        return true;
    }
}

/// <summary>All stationary lights of the core sectors of one strict area snapshot, with their frames.</summary>
public sealed class AreaStationaryLightSource
{
    internal AreaStationaryLightSource(int catalogueMap, string? namesSha256, IList<StationaryLight> lights, IDictionary<int, StationaryLightFrame> frames)
    {
        CatalogueMap = catalogueMap; NamesSha256 = namesSha256;
        Lights = new ReadOnlyCollection<StationaryLight>(lights.ToArray());
        Frames = new ReadOnlyDictionary<int, StationaryLightFrame>(new SortedDictionary<int, StationaryLightFrame>(frames));
    }

    /// <summary>Synthetic construction for fixtures: every light's ART number must have a frame.</summary>
    public static AreaStationaryLightSource Create(int catalogueMap, IEnumerable<StationaryLight> lights, IEnumerable<(int Number, int Width, int Height, int HotX, int HotY, byte[] Mask)> frames)
    {
        ArgumentNullException.ThrowIfNull(lights); ArgumentNullException.ThrowIfNull(frames);
        var table = new Dictionary<int, StationaryLightFrame>();
        foreach (var frame in frames)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Mask is null || frame.Mask.Length != checked(frame.Width * frame.Height * 4) ||
                !table.TryAdd(frame.Number, new(frame.Number, "synthetic", LightingMes.Hash(frame.Mask), frame.Width, frame.Height, frame.HotX, frame.HotY, (byte[])frame.Mask.Clone())))
                throw new ArgumentException("Synthetic light frames must be distinct and complete.", nameof(frames));
        }
        var list = lights.ToArray();
        if (list.Any(light => light is null || !table.ContainsKey(light.ArtNumber) || (light.Flags & ~StationaryLightReader.KnownFlags) != 0))
            throw new ArgumentException("Every synthetic light needs a known flag set and a frame.", nameof(lights));
        return new(catalogueMap, null, list, table);
    }

    public int CatalogueMap { get; }
    /// <summary>SHA-256 of <c>art/light/light.mes</c>; null for synthetic sources.</summary>
    public string? NamesSha256 { get; }
    public IReadOnlyList<StationaryLight> Lights { get; }
    public IReadOnlyDictionary<int, StationaryLightFrame> Frames { get; }
}

public sealed record StationaryLightReadResult(LightingFailure Failure, AreaStationaryLightSource? Source);

/// <summary>
/// FR-LIGHT-008 reader (ARC-RENDER-012). Decodes the raw light words the strict area snapshot retained
/// for each core sector and resolves each light ART number through <c>art/light/light.mes</c>.
/// <see cref="Read(ContentStack, AreaSnapshot, CancellationToken)"/> (ARC-DATA-027) reads as the original:
/// <c>art/light/light.mes</c> at game start, so from the first base source (<see cref="ContentLoadPhase.GameStart"/>,
/// exe-checked), each light ART when it is drawn, so the content stack's winner. The pinned profiles'
/// <see cref="Read(DatArchive, AreaSnapshot, CancellationToken)"/> reads both from one ART resource archive (the
/// snapshot's Resource2, which the caller re-verifies afterwards).
/// Fail-closed policy: an owner word other than 0 (an object light), a location outside the record's own
/// sector, an unknown flag bit, an ART id that is not a plain light (type 9, frame/rotation/flags 0), an
/// animated or multi-group light ART, a missing or ambiguous resource, and every cap are typed failures.
/// The raw location is taken as the absolute world location inside the record's own sector: an adopted
/// policy (NOT_VERIFIED), since the decompiled sector-relative rebuild is marked TODO in the source and is
/// not taken literally; a location outside the sector is refused.
/// </summary>
public static class StationaryLightReader
{
    /// <summary>ARC-DATA-029: the largest installed interior has 207 stationary lights and 9 light frames.</summary>
    public const int MaxLights = 512;
    public const int MaxFrames = 32;
    public const int MaxFrameSide = 1024;
    public const int MaxOffset = 16384;
    public const StationaryLightFlags KnownFlags = StationaryLightFlags.Off | StationaryLightFlags.Dark | StationaryLightFlags.Indoor | StationaryLightFlags.Outdoor;

    /// <summary>The pinned profiles' read: the name table and every light ART from <paramref name="artResource"/> alone.</summary>
    public static StationaryLightReadResult Read(DatArchive artResource, AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(artResource); ArgumentNullException.ThrowIfNull(snapshot);
        return Read(new AreaLightingReader.ArchiveFiles(artResource, "art/light/"), snapshot, token);
    }

    /// <summary>ARC-DATA-027: the name table and the light ART through <paramref name="stack"/> in the phase the original reads them.</summary>
    public static StationaryLightReadResult Read(ContentStack stack, AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(snapshot);
        return Read(new AreaLightingReader.StackFiles(stack), snapshot, token);
    }

    private static StationaryLightReadResult Read(AreaLightingReader.ILightingFiles files, AreaSnapshot snapshot, CancellationToken token)
    {
        var failure = ReadCore(files, snapshot, token, out var source);
        return new(failure, failure == LightingFailure.None ? source : null);
    }

    /// <summary>Decodes one 12-word serialized record of the given sector.</summary>
    public static LightingFailure TryDecode(AreaSectorCoordinate sector, int index, IReadOnlyList<int> words, out StationaryLight? light)
    {
        light = null;
        if (words is null || words.Count != 12) return LightingFailure.LightRecordMalformed;
        if (words[0] != 0 || words[1] != 0) return LightingFailure.LightNotStationary;
        long x = (uint)words[2], y = (uint)words[3];
        if (x < sector.OriginX || x >= sector.OriginX + 64 || y < sector.OriginY || y >= sector.OriginY + 64) return LightingFailure.LightOutsideSector;
        if (words[4] is < -MaxOffset or > MaxOffset || words[5] is < -MaxOffset or > MaxOffset) return LightingFailure.LightRecordMalformed;
        var flags = (StationaryLightFlags)(uint)words[6];
        if ((flags & ~KnownFlags) != 0) return LightingFailure.LightFlagsUnsupported;
        var art = (uint)words[7];
        if (art >> 28 != 9 || (art & 0x7FFFF) != 0) return LightingFailure.LightArtUnsupported;
        var color = (uint)words[8];
        var bytes = new byte[48];
        for (var w = 0; w < 12; w++) System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 * w), words[w]);
        light = new(sector.Packed, index, x, y, words[4], words[5], flags, art,
            new((byte)color, (byte)(color >> 16), (byte)(color >> 8)), LightingMes.Hash(bytes));
        return LightingFailure.None;
    }

    private static LightingFailure ReadCore(AreaLightingReader.ILightingFiles files, AreaSnapshot snapshot, CancellationToken token, out AreaStationaryLightSource? source)
    {
        source = null;
        if (!snapshot.StrictlyPublished) return LightingFailure.SnapshotNotStrict;
        var lights = new List<StationaryLight>();
        foreach (var sector in snapshot.Sectors.Where(sector => sector.Scope == AreaScope.Core).OrderBy(sector => sector.Key.Packed))
        {
            token.ThrowIfCancellationRequested();
            if (sector.Lights.Count != sector.LightCount) return LightingFailure.LightRecordMalformed;
            for (var index = 0; index < sector.Lights.Count; index++)
            {
                if (lights.Count == MaxLights) return LightingFailure.LimitExceeded;
                var failure = TryDecode(sector.Coordinate, index, sector.Lights[index], out var light);
                if (failure != LightingFailure.None) return failure;
                lights.Add(light!);
            }
        }
        var frames = new Dictionary<int, StationaryLightFrame>();
        string? namesSha256 = null;
        if (lights.Count > 0)
        {
            // The name table is read at game start (a_name_light_init); a light ART when it is first drawn.
            var failure = files.Read("art/light/light.mes", ContentLoadPhase.GameStart, out var namesBytes);
            if (failure != LightingFailure.None) return failure;
            failure = LightingMes.TryParse(namesBytes, out var names);
            if (failure != LightingFailure.None) return failure;
            namesSha256 = LightingMes.Hash(namesBytes);
            foreach (var number in lights.Select(light => light.ArtNumber).Distinct().Order())
            {
                token.ThrowIfCancellationRequested();
                if (frames.Count == MaxFrames) return LightingFailure.LimitExceeded;
                if (!names.TryGetValue(number, out var raw)) return LightingFailure.LightArtNotCatalogued;
                var file = AreaLightingReader.SafeName(raw);
                if (file is null) return LightingFailure.UnsafeResourceName;
                var name = "art/light/" + file.ToLowerInvariant() + ".art";
                failure = files.Read(name, ContentLoadPhase.ModuleLoaded, out var artBytes);
                if (failure != LightingFailure.None) return failure;
                failure = DecodeFrame(number, name, artBytes, out var frame);
                if (failure != LightingFailure.None) return failure;
                frames.Add(number, frame!);
            }
        }
        token.ThrowIfCancellationRequested();
        source = new AreaStationaryLightSource(snapshot.CatalogueMap, namesSha256, lights, frames);
        return LightingFailure.None;
    }

    // Static lights only: one group of one frame (an animated light ART is refused, not frozen).
    private static LightingFailure DecodeFrame(int number, string name, byte[] bytes, out StationaryLightFrame? frame)
    {
        frame = null;
        ArtFrame decoded;
        try
        {
            var metadata = ArtDecoder.Inspect(bytes);
            if (metadata.Header.PhysicalGroupCount != 1 || metadata.Header.FramesPerGroup != 1) return LightingFailure.LightArtUnsupported;
            var first = metadata.Frames[0];
            if (first.Width is <= 0 or > MaxFrameSide || first.Height is <= 0 or > MaxFrameSide) return LightingFailure.LimitExceeded;
            decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(0, 0, 0));
        }
        catch (ArtException) { return LightingFailure.LightArtUnsupported; }
        catch (ArgumentOutOfRangeException) { return LightingFailure.LightArtUnsupported; }
        // The source keys out every pixel whose colour equals palette index 0's colour (the ART colour
        // key), not only index 0, which the decoder already leaves transparent.
        var palette = decoded.PalettesBySlot[decoded.EffectivePaletteSlot]!.RawTable;
        byte keyR = palette[2], keyG = palette[1], keyB = palette[0];
        var mask = decoded.Rgba.ToArray();
        for (var at = 0; at < mask.Length; at += 4)
            if (mask[at + 3] != 0 && mask[at] == keyR && mask[at + 1] == keyG && mask[at + 2] == keyB) mask[at + 3] = 0;
        frame = new(number, name, LightingMes.Hash(bytes), decoded.Width, decoded.Height, decoded.Frame.HotX, decoded.Frame.HotY, mask);
        return LightingFailure.None;
    }
}
