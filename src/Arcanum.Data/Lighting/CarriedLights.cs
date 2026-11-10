using Arcanum.Data.Area;
using Arcanum.Data.Content;

namespace Arcanum.Data.Lighting;

/// <summary>The size of a critter's carried light (ledger EQ12, LT12): the largest of its worn items' light sizes.</summary>
public enum CarriedLightSize { None = 0, Small = 1, Medium = 2, Large = 3, ExtraLarge = 4 }

/// <summary>
/// ARC-DEV-335 (whole-world plan V7d): one critter's carried light as the original keeps it, a primary light of the critter
/// (ledger LT12) that moves with it (LT10): the critter's world tile, its screen offsets and the size of its worn light.
/// </summary>
public sealed record CarriedLight(long X, long Y, int OffsetX, int OffsetY, CarriedLightSize Size);

public sealed record CarriedLightReadResult(LightingFailure Failure, StationaryLightFrame? Frame);

/// <summary>
/// ARC-DEV-335 (ledger LT12, 0x467E80, 0x45FA70, 0x43EBD0): the carried light of a critter is light ART 1 (frame 0, rotation 0) with one
/// colour per size, set whenever the worn items' light is recomputed; its flags are 0 (not off, not dark, tinted by its own
/// colour). The ART frame comes from the same table as every light ART (<c>art/light/light.mes</c>).
/// </summary>
public static class CarriedLightRules
{
    /// <summary>The light ART number every carried light uses (LT12).</summary>
    public const int ArtNumber = 1;

    /// <summary>The colour of a carried light of a size (LT12): extra large (244, 255, 255), large (255, 214, 172), medium
    /// (236, 138, 85), small (167, 88, 58); a critter without a worn light has none.</summary>
    public static AmbientColor Color(CarriedLightSize size) => size switch
    {
        CarriedLightSize.ExtraLarge => new(244, 255, 255),
        CarriedLightSize.Large => new(255, 214, 172),
        CarriedLightSize.Medium => new(236, 138, 85),
        CarriedLightSize.Small => new(167, 88, 58),
        _ => throw new ArgumentOutOfRangeException(nameof(size), "A critter without a light has no carried light colour."),
    };

    /// <summary>The carried light's frame, read from the content stack as the original reads any light ART.</summary>
    public static CarriedLightReadResult Read(ContentStack stack, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return Load(new AreaLightingReader.StackFiles(stack), token);
    }

    /// <summary>The same from the art resource archive, as the exterior and cave modes read their other light ART.</summary>
    public static CarriedLightReadResult Read(DatArchive artResource, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(artResource);
        return Load(new AreaLightingReader.ArchiveFiles(artResource, "art/light/"), token);
    }

    private static CarriedLightReadResult Load(AreaLightingReader.ILightingFiles files, CancellationToken token)
    {
        var failure = StationaryLightReader.LoadFrames(files, [ArtNumber], token, out _, out var frames);
        return failure != LightingFailure.None ? new(failure, null) : new(LightingFailure.None, frames[ArtNumber]);
    }

    /// <summary>The sector light a carried light stands for: ART 1 frame 0, flags 0, the size's colour.</summary>
    public static StationaryLight ToLight(CarriedLight carried)
    {
        ArgumentNullException.ThrowIfNull(carried);
        if (carried.Size == CarriedLightSize.None || !Enum.IsDefined(carried.Size)) throw new ArgumentException("A carried light needs a size.", nameof(carried));
        var sector = new AreaSectorCoordinate(carried.X >> 6, carried.Y >> 6).Packed;
        return new StationaryLight(sector, -1, carried.X, carried.Y, carried.OffsetX, carried.OffsetY, StationaryLightFlags.None,
            (9u << 28) | ((uint)ArtNumber << 19), Color(carried.Size), "carried");
    }
}
