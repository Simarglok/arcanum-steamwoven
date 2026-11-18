using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Content;

namespace Arcanum.Data.Lighting;

/// <summary>Serialized light flag bits whose disposition is established (RES-018; exe-checked LT8, LT14 of ARC-RES-069): OFF
/// excludes the light, DARK subtracts instead of adding, INDOOR/OUTDOOR take the emitter tint from the current ambient pair
/// (INDOOR first), NO_SHADOW (0x40) only keeps the light out of the original's object shadows, which are not drawn. Any
/// other bit is refused, never guessed.</summary>
[Flags]
public enum StationaryLightFlags : uint
{
    None = 0, Off = 1, Dark = 2, Indoor = 8, Outdoor = 16,
    /// <summary>Keeps an object light from animating (LT6); object lights only.</summary>
    NoAnimation = 32,
    NoShadow = 64,
}

/// <summary>One decoded stationary sector light (FR-LIGHT-008): the 48-byte wire of the pinned source
/// (owner word 0, absolute world location inside its own sector, signed screen offset, flags, light ART
/// id, colour stored r,b,g). The serialized tint, palette and padding words are not interpreted; they
/// stay bound through <see cref="RecordSha256"/>.</summary>
public sealed record StationaryLight(long Sector, int Index, long X, long Y, int OffsetX, int OffsetY,
    StationaryLightFlags Flags, uint ArtId, AmbientColor Color, string RecordSha256)
{
    /// <summary>Light ART number (bits 19..27), the key of <c>art/light/light.mes</c>.</summary>
    public int ArtNumber => (int)((ArtId >> 19) & 511);
    /// <summary>The ART frame the light shows (bits 12..18); 0 for a stationary light.</summary>
    public int Frame => (int)((ArtId >> 12) & 127);
    /// <summary>ARC-RENDER-030: the object an object light belongs to (null for a stationary light) and its slot (−1 primary,
    /// 0..3 overlay).</summary>
    public AreaObjectKey? Owner { get; init; }
    public int Slot { get; init; } = -1;
}

/// <summary>Detached frame of one light ART: size, hotspot and an RGB contribution mask.
/// <see cref="Mask"/> is width*height RGBA; alpha 0 marks a pixel equal to the colour key
/// (palette index 0's colour), which contributes nothing. ARC-DATA-039: the frame a light ART number maps to is its
/// frame 0, whose <see cref="Frames"/> lists every frame of an animated ART in order (itself first) with the ART's
/// <see cref="FramesPerSecond"/>; each frame carries the ART's own offset step (<see cref="StepX"/>, <see cref="StepY"/>),
/// by which the original moves a light when it steps to that frame (ledger LT7).</summary>
public sealed class StationaryLightFrame
{
    /// <param name="later">Frames 1.. of an animated ART (frame 0 is this one); null for a static light.</param>
    internal StationaryLightFrame(int number, string name, string payloadSha256, int width, int height, int hotX, int hotY, byte[] mask,
        int stepX = 0, int stepY = 0, IReadOnlyList<StationaryLightFrame>? later = null, int framesPerSecond = 0)
    {
        Number = number; Name = name; PayloadSha256 = payloadSha256; Width = width; Height = height; HotX = hotX; HotY = hotY;
        Mask = Array.AsReadOnly(mask); StepX = stepX; StepY = stepY;
        Frames = Array.AsReadOnly(new[] { this }.Concat(later ?? []).ToArray());
        FramesPerSecond = Frames.Count > 1 ? framesPerSecond : 0;
    }
    public int Number { get; }
    /// <summary>Every frame of the ART, frame 0 (this) first; one for a static light. Described on frame 0 only: a later
    /// frame lists itself alone.</summary>
    public IReadOnlyList<StationaryLightFrame> Frames { get; }
    /// <summary>The ART's frame rate (on frame 0); 0 for a static light.</summary>
    public int FramesPerSecond { get; }
    public int StepX { get; }
    public int StepY { get; }
    public bool Animated => Frames.Count > 1;
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

    /// <summary>ARC-DEV-335: the source of critters' carried lights (ledger LT12) over their one light ART frame.</summary>
    public static AreaStationaryLightSource ForCarried(int catalogueMap, StationaryLightFrame frame, IEnumerable<CarriedLight> lights)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(lights);
        if (frame.Number != CarriedLightRules.ArtNumber) throw new ArgumentException("The frame is not the carried light's ART.", nameof(frame));
        return new(catalogueMap, null, lights.Select(CarriedLightRules.ToLight).ToArray(),
            new Dictionary<int, StationaryLightFrame> { [frame.Number] = frame });
    }

    /// <summary>ARC-DEV-353: the source of critters' lights over the frames of their light ARTs (a critter's own saved light and the carried
    /// light of the hero); every light's ART number must have a frame.</summary>
    public static AreaStationaryLightSource ForCarried(int catalogueMap, IReadOnlyDictionary<int, StationaryLightFrame> frames, IEnumerable<StationaryLight> lights)
    {
        ArgumentNullException.ThrowIfNull(frames); ArgumentNullException.ThrowIfNull(lights);
        var list = lights.ToArray();
        if (list.Any(light => light is null || !frames.ContainsKey(light.ArtNumber)))
            throw new ArgumentException("Every carried light needs a frame.", nameof(lights));
        return new(catalogueMap, null, list, new Dictionary<int, StationaryLightFrame>(frames));
    }

    /// <summary>
    /// ARC-SCRIPT-2585 (ledger OT2, OT10): this source with the lights of the objects in <paramref name="states"/> switched off (true: the light's OFF bit set) or on (false: cleared), as the original's toggle
    /// setters do for every light field of the object (the functions are named in ledger row OT10). A light of any other owner and every
    /// stationary light stay as they are; this source itself when nothing changes.
    /// </summary>
    public AreaStationaryLightSource WithObjectsOff(IReadOnlyDictionary<AreaObjectKey, bool> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (states.Count == 0) return this;
        var changed = false;
        var lights = new List<StationaryLight>(Lights.Count);
        foreach (var light in Lights)
        {
            if (light.Owner is { } owner && states.TryGetValue(owner, out var off))
            {
                var flags = off ? light.Flags | StationaryLightFlags.Off : light.Flags & ~StationaryLightFlags.Off;
                if (flags != light.Flags) { lights.Add(light with { Flags = flags }); changed = true; continue; }
            }
            lights.Add(light);
        }
        return changed ? new(CatalogueMap, NamesSha256, lights, new Dictionary<int, StationaryLightFrame>(Frames)) : this;
    }

    public int CatalogueMap { get; }
    /// <summary>SHA-256 of <c>art/light/light.mes</c>; null for synthetic sources.</summary>
    public string? NamesSha256 { get; }
    public IReadOnlyList<StationaryLight> Lights { get; }
    public IReadOnlyDictionary<int, StationaryLightFrame> Frames { get; }

    /// <summary>
    /// ARC-RENDER-030: the stationary lights with the object lights the original has built at <paramref name="hour"/> (ledger
    /// LT4, LT5): every object light except nocturnal scenery on an outdoor tile by day, with the flags of
    /// <see cref="ObjectLightReader.FlagsAtLoad"/>. The object lights follow the stationary ones in object order; drawing
    /// order between the two kinds is not read (NOT_VERIFIED).
    /// </summary>
    public AreaStationaryLightSource WithObjectLights(AreaObjectLightSource objects, int hour)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (objects.CatalogueMap != CatalogueMap) throw new ArgumentException("The object lights belong to another map.", nameof(objects));
        var lights = Lights.ToList();
        foreach (var light in objects.Lights)
        {
            // ARC-DEV-353: a critter's light moves with the critter, so it is not a fixed light of the sector.
            if (light.Critter || !ObjectLightReader.BuiltAtLoad(light, hour)) continue;
            var sector = new AreaSectorCoordinate(light.X >> 6, light.Y >> 6).Packed;
            lights.Add(new StationaryLight(sector, -1, light.X, light.Y, light.OffsetX, light.OffsetY, ObjectLightReader.FlagsAtLoad(light, hour),
                light.ArtId, light.Color, light.Object.ObjectIdSha256) { Owner = light.Object, Slot = light.Slot });
        }
        var frames = new Dictionary<int, StationaryLightFrame>(Frames);
        foreach (var (number, frame) in objects.Frames) frames.TryAdd(number, frame);
        return new(CatalogueMap, NamesSha256 ?? objects.NamesSha256, lights, frames);
    }
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
/// sector, an unknown flag bit, an ART id that is not a plain light (type 9, frame/rotation/flags 0), a
/// multi-group light ART or one of more than <see cref="MaxArtFrames"/> frames, a missing or ambiguous resource,
/// and every cap are typed failures. Since ARC-DATA-039 an animated light ART is read with all its frames.
/// The raw location is taken as the absolute world location inside the record's own sector: an adopted
/// policy (NOT_VERIFIED), since the decompiled sector-relative rebuild is marked TODO in the source and is
/// not taken literally; a location outside the sector is refused.
/// </summary>
public static class StationaryLightReader
{
    /// <summary>ARC-DATA-029: the largest installed interior has 207 stationary lights and 9 light frames.</summary>
    public const int MaxLights = 512;
    /// <summary>Distinct light ART per snapshot; ARC-DATA-039 raised it from 16 for object lights: the installed name table
    /// has 28 entries.</summary>
    public const int MaxFrames = 32;
    /// <summary>ARC-DATA-039: frames of one animated light ART; the installed module's longest has 15.</summary>
    public const int MaxArtFrames = 32;
    /// <summary>ARC-DATA-039: the RGBA bytes of every light ART frame one read keeps (project policy, the old bound of 16
    /// one-frame ARTs at the largest side).</summary>
    public const long MaxLightRgbaBytes = 64L * 1024 * 1024;
    public const int MaxFrameSide = 1024;
    public const int MaxOffset = 16384;
    public const StationaryLightFlags KnownFlags = StationaryLightFlags.Off | StationaryLightFlags.Dark | StationaryLightFlags.Indoor | StationaryLightFlags.Outdoor
        | StationaryLightFlags.NoShadow;

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
        var loaded = LoadFrames(files, lights.Select(light => light.ArtNumber), token, out var namesSha256, out var frames);
        if (loaded != LightingFailure.None) return loaded;
        token.ThrowIfCancellationRequested();
        source = new AreaStationaryLightSource(snapshot.CatalogueMap, namesSha256, lights, frames);
        return LightingFailure.None;
    }

    /// <summary>The light ART frames of <paramref name="numbers"/> (ARC-DATA-027 phases): the name table at game start, each
    /// ART when it is drawn. Shared by the stationary and the object lights (ARC-DATA-039).</summary>
    internal static LightingFailure LoadFrames(AreaLightingReader.ILightingFiles files, IEnumerable<int> numbers, CancellationToken token,
        out string? namesSha256, out Dictionary<int, StationaryLightFrame> frames)
    {
        frames = new(); namesSha256 = null;
        var wanted = numbers.Distinct().Order().ToArray();
        if (wanted.Length == 0) return LightingFailure.None;
        // The name table is read at game start (a_name_light_init); a light ART when it is first drawn.
        var failure = files.Read("art/light/light.mes", ContentLoadPhase.GameStart, out var namesBytes);
        if (failure != LightingFailure.None) return failure;
        failure = LightingMes.TryParse(namesBytes, out var names);
        if (failure != LightingFailure.None) return failure;
        namesSha256 = LightingMes.Hash(namesBytes);
        long rgba = 0;
        foreach (var number in wanted)
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
            rgba += frame!.Frames.Sum(f => (long)f.Mask.Count);
            if (rgba > MaxLightRgbaBytes) return LightingFailure.LimitExceeded;
            frames.Add(number, frame);
        }
        return LightingFailure.None;
    }

    // One group of up to MaxArtFrames frames; ARC-DATA-039 admits an animated light ART (ledger LT6, LT7) with all its frames.
    private static LightingFailure DecodeFrame(int number, string name, byte[] bytes, out StationaryLightFrame? frame)
    {
        frame = null;
        var hash = LightingMes.Hash(bytes);
        var decodedFrames = new List<(int W, int H, int X, int Y, byte[] Mask, int Sx, int Sy)>();
        int fps;
        try
        {
            var metadata = ArtDecoder.Inspect(bytes);
            var count = metadata.Header.FramesPerGroup;
            if (metadata.Header.PhysicalGroupCount != 1 || count < 1) return LightingFailure.LightArtUnsupported;
            if (count > MaxArtFrames) return LightingFailure.LimitExceeded;
            fps = count > 1 ? metadata.Header.FramesPerSecond : 0;
            if (count > 1 && fps <= 0) return LightingFailure.LightArtUnsupported;
            for (var index = 0; index < count; index++)
            {
                var meta = metadata.Frames[index];
                if (meta.Width is <= 0 or > MaxFrameSide || meta.Height is <= 0 or > MaxFrameSide) return LightingFailure.LimitExceeded;
                var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(0, index, 0));
                // The source keys out every pixel whose colour equals palette index 0's colour (the ART colour
                // key), not only index 0, which the decoder already leaves transparent.
                var palette = decoded.PalettesBySlot[decoded.EffectivePaletteSlot]!.RawTable;
                byte keyR = palette[2], keyG = palette[1], keyB = palette[0];
                var mask = decoded.Rgba.ToArray();
                for (var at = 0; at < mask.Length; at += 4)
                    if (mask[at + 3] != 0 && mask[at] == keyR && mask[at + 1] == keyG && mask[at + 2] == keyB) mask[at + 3] = 0;
                decodedFrames.Add((decoded.Width, decoded.Height, decoded.Frame.HotX, decoded.Frame.HotY, mask, meta.OffsetX, meta.OffsetY));
            }
        }
        catch (ArtException) { return LightingFailure.LightArtUnsupported; }
        catch (ArgumentOutOfRangeException) { return LightingFailure.LightArtUnsupported; }
        StationaryLightFrame Make((int W, int H, int X, int Y, byte[] Mask, int Sx, int Sy) f, IReadOnlyList<StationaryLightFrame>? later = null) =>
            new(number, name, hash, f.W, f.H, f.X, f.Y, f.Mask, f.Sx, f.Sy, later, fps);
        frame = Make(decodedFrames[0], Array.AsReadOnly(decodedFrames.Skip(1).Select(f => Make(f)).ToArray()));
        return LightingFailure.None;
    }
}
