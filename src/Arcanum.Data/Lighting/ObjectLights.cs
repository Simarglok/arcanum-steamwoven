using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Content;

namespace Arcanum.Data.Lighting;

/// <summary>
/// One object light of an area snapshot (ARC-DATA-039, ledger LT1): the primary light (<see cref="Slot"/> −1) or one of the
/// four overlay lights of an object, as the original builds it from the object's effective fields when its sector loads: the
/// object's world tile and screen offsets, the light flags without the original's runtime bits, the light ART id (its frame
/// is the saved animation frame) and the colour. <see cref="ObjectOff"/>, <see cref="Nocturnal"/> and
/// <see cref="OutdoorTile"/> are the facts the original's load and hour rules read (LT4, LT5): the object switched off,
/// nocturnal scenery, and whether the tile under it is an outdoor tile.
/// </summary>
public sealed record ObjectLight(AreaObjectKey Object, int Type, int Slot, long X, long Y, int OffsetX, int OffsetY, StationaryLightFlags Flags,
    uint ArtId, AmbientColor Color, bool ObjectOff, bool Nocturnal, bool OutdoorTile)
{
    /// <summary>Light ART number (bits 19..27), the key of <c>art/light/light.mes</c>.</summary>
    public int ArtNumber => (int)((ArtId >> 19) & 511);
    /// <summary>The ART frame the light shows (bits 12..18).</summary>
    public int Frame => (int)((ArtId >> 12) & 127);
    public bool Primary => Slot < 0;
}

/// <summary>The object lights of the core sectors of one strict area snapshot, with their light ART frames.</summary>
public sealed class AreaObjectLightSource
{
    internal AreaObjectLightSource(int catalogueMap, string? namesSha256, IList<ObjectLight> lights, IDictionary<int, StationaryLightFrame> frames)
    {
        CatalogueMap = catalogueMap; NamesSha256 = namesSha256;
        Lights = new ReadOnlyCollection<ObjectLight>(lights.ToArray());
        Frames = new ReadOnlyDictionary<int, StationaryLightFrame>(new SortedDictionary<int, StationaryLightFrame>(frames));
    }

    public int CatalogueMap { get; }
    /// <summary>SHA-256 of <c>art/light/light.mes</c>; null when the snapshot has no object light.</summary>
    public string? NamesSha256 { get; }
    public IReadOnlyList<ObjectLight> Lights { get; }
    public IReadOnlyDictionary<int, StationaryLightFrame> Frames { get; }
}

public sealed record ObjectLightReadResult(LightingFailure Failure, AreaObjectLightSource? Source);

/// <summary>
/// ARC-DATA-039 (whole-world plan V7a): the object lights of a strict area snapshot, read as the original builds them at
/// sector load (ledger LT1, LT4): for every core object with a location that is not in an inventory, the primary light of
/// fields 13–15 (the project's slots 12–14) when its ART is not −1, and each overlay slot of the arrays 16–18 (slots 15–17)
/// whose ART is not −1. Effective fields (the instance's, else the prototype's) are the snapshot's. The light ART frames
/// come from the same table and phases as the stationary lights. The saved flag words may carry the original's runtime
/// bits (changed 0x80, animating 0x4, the primary and overlay-slot markers), which are dropped; any other unknown bit, a
/// light ART id that is not a light (type 9, rotation and the low bits 0), a frame past the ART's last and every cap are
/// typed failures. Which lights are lit at which hour is the caller's (the nocturnal rule, LT4/LT5).
/// </summary>
public static class ObjectLightReader
{
    /// <summary>Project policy: the largest installed interior has about 5,200 lights; 1.5 times headroom.</summary>
    public const int MaxLights = 8192;
    // Project slots: offsets, light flags, ART, colour, the overlay arrays, object flags, the first type-specific slot.
    internal const int OffsetXSlot = 2, OffsetYSlot = 3, FlagsSlot = 12, ArtSlot = 13, ColorSlot = 14, OverlayFlagsSlot = 15,
        OverlayArtSlot = 16, OverlayColorSlot = 17, ObjectFlagsSlot = 18, TypeFlagsSlot = 36;
    public const int OverlaySlots = 4;
    private const uint ObjectOffFlag = 0x2, InventoryFlag = 0x1000, NocturnalFlag = 0x4;
    private const int SceneryType = 3;
    /// <summary>The original's runtime bits a saved flag word may carry (LT6, LT8, LT11): animating, changed, the primary and
    /// overlay-slot markers.</summary>
    public const uint RuntimeBits = 0x4 | 0x80 | 0x08000000 | 0xF0000000;

    public static ObjectLightReadResult Read(ContentStack stack, AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(snapshot);
        return Read(new AreaLightingReader.StackFiles(stack), snapshot, token);
    }

    public static ObjectLightReadResult Read(DatArchive artResource, AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(artResource); ArgumentNullException.ThrowIfNull(snapshot);
        return Read(new AreaLightingReader.ArchiveFiles(artResource, "art/light/"), snapshot, token);
    }

    private static ObjectLightReadResult Read(AreaLightingReader.ILightingFiles files, AreaSnapshot snapshot, CancellationToken token)
    {
        if (!snapshot.StrictlyPublished) return new(LightingFailure.SnapshotNotStrict, null);
        var failure = Describe(snapshot, out var lights, token);
        if (failure != LightingFailure.None) return new(failure, null);
        failure = StationaryLightReader.LoadFrames(files, lights.Select(light => light.ArtNumber), token, out var namesSha256, out var frames);
        if (failure != LightingFailure.None) return new(failure, null);
        if (lights.Any(light => light.Frame >= frames[light.ArtNumber].Frames.Count)) return new(LightingFailure.LightArtUnsupported, null);
        return new(LightingFailure.None, new(snapshot.CatalogueMap, namesSha256, lights, frames));
    }

    /// <summary>The object lights of the snapshot's core objects, without their ART (the frames are the caller's).</summary>
    [OriginalFunction(0x4D9590, OriginalFunctionScope.Partial)]
    public static LightingFailure Describe(AreaSnapshot snapshot, out List<ObjectLight> lights, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var found = new List<ObjectLight>();
        lights = found;
        var tiles = new Dictionary<(long, long), uint>();
        foreach (var tile in snapshot.Terrain) tiles[(tile.WorldX, tile.WorldY)] = tile.RawArt;
        foreach (var o in snapshot.Objects)
        {
            token.ThrowIfCancellationRequested();
            if (o.Scope != AreaScope.Core || o.WorldX is not { } x || o.WorldY is not { } y) continue;
            var fields = new Dictionary<int, AreaFieldValue>();
            foreach (var field in o.EffectiveFields) fields[field.Slot] = field;
            long? Number(int slot) => fields.TryGetValue(slot, out var v) && v.Presence == AreaPresence.Value ? v.Number : null;
            long? Element(int slot, int index) => fields.TryGetValue(slot, out var v) && v.Presence == AreaPresence.Array
                ? v.Elements.FirstOrDefault(e => e.Key == index)?.Words.FirstOrDefault() : null;
            var objectFlags = (uint)(Number(ObjectFlagsSlot) ?? 0);
            if ((objectFlags & InventoryFlag) != 0) continue;
            var nocturnal = o.Type == SceneryType && ((uint)(Number(TypeFlagsSlot) ?? 0) & NocturnalFlag) != 0;
            // LT4: the tile ART's bit 8 under the object (an outdoor tile).
            var outdoor = tiles.TryGetValue((x, y), out var tileArt) && (tileArt >> 8 & 1) != 0;
            var offsetX = (int)(Number(OffsetXSlot) ?? 0); var offsetY = (int)(Number(OffsetYSlot) ?? 0);
            if (offsetX is < -StationaryLightReader.MaxOffset or > StationaryLightReader.MaxOffset || offsetY is < -StationaryLightReader.MaxOffset or > StationaryLightReader.MaxOffset)
                return LightingFailure.LightRecordMalformed;
            LightingFailure Add(int slot, long? art, long? flags, long? color)
            {
                if (art is not { } value || (uint)value == uint.MaxValue) return LightingFailure.None;
                var id = (uint)value;
                if (id >> 28 != 9 || (id & 0xFFF) != 0) return LightingFailure.LightArtUnsupported;
                var bits = (uint)(flags ?? 0) & ~RuntimeBits;
                if ((bits & ~(uint)(StationaryLightReader.KnownFlags | StationaryLightFlags.NoAnimation)) != 0) return LightingFailure.LightFlagsUnsupported;
                if (found.Count == MaxLights) return LightingFailure.LimitExceeded;
                var packed = (uint)(color ?? 0);
                found.Add(new(o.Key, o.Type, slot, x, y, offsetX, offsetY, (StationaryLightFlags)bits,
                    id, new((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed), (objectFlags & ObjectOffFlag) != 0, nocturnal, outdoor));
                return LightingFailure.None;
            }
            var failure = Add(-1, Number(ArtSlot), Number(FlagsSlot), Number(ColorSlot));
            if (failure != LightingFailure.None) return failure;
            for (var slot = 0; slot < OverlaySlots; slot++)
            {
                failure = Add(slot, Element(OverlayArtSlot, slot), Element(OverlayFlagsSlot, slot), Element(OverlayColorSlot, slot));
                if (failure != LightingFailure.None) return failure;
            }
        }
        return LightingFailure.None;
    }

    /// <summary>Whether the original builds the light at sector load at <paramref name="hour"/> (LT4): nocturnal scenery on an
    /// outdoor tile only at night (hours 18..5); every other light always.</summary>
    [OriginalFunction(0x4F7940, OriginalFunctionScope.Partial)]
    public static bool BuiltAtLoad(ObjectLight light, int hour)
    {
        ArgumentNullException.ThrowIfNull(light);
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        return !(light.Nocturnal && light.OutdoorTile) || hour is < 6 or > 17;
    }
}
