using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanum.Data;

namespace Arcanum.TerrainViewer.Core;

public enum ActorDiagnosticProfileId { HumanFixed, MonsterRegression }
public enum ActorDiagnosticClock { ArtMetadata, LegacyOverride }
public sealed record ActorDiagnosticResource(ActorDiagnosticAction Action, uint Selector, int Ordinal, int Palette,
    int PhysicalGroups, int FramesPerGroup, int RawFramesPerSecond, int DiagnosticFramesPerSecond);
public readonly record struct ActorDiagnosticFacing(int ProjectOctant, int SourceFacing, int PhysicalGroup, bool FlipX);

/// <summary>
/// Two closed, immutable, one-layer diagnostics. Numeric identities only; not
/// an appearance resolver, equipment compositor or original scheduler.
/// Human metadata commitments cover all physical rows, not just frame zero.
/// Archive identity is authenticated separately by the production admission.
/// </summary>
public sealed class ActorDiagnosticProfile
{
    private ActorDiagnosticProfile(ActorDiagnosticProfileId id, ActorDiagnosticClock clock,
        ActorDiagnosticResource idle, ActorDiagnosticResource walk, int coverage, long? rgbaBytes)
    {
        Id = id; Clock = clock; Idle = idle; Walk = walk;
        DistinctKeys = coverage; OriginalRgbaBytes = rgbaBytes;
    }

    public static ActorDiagnosticProfile HumanFixed { get; } = new(ActorDiagnosticProfileId.HumanFixed, ActorDiagnosticClock.ArtMetadata,
        new(ActorDiagnosticAction.Idle, 0x20000000, 2667, 0, 8, 1, 6, 6),
        new(ActorDiagnosticAction.Walk, 0x20000040, 2668, 0, 8, 10, 17, 17), 55, 457676);
    public static ActorDiagnosticProfile MonsterRegression { get; } = new(ActorDiagnosticProfileId.MonsterRegression, ActorDiagnosticClock.LegacyOverride,
        new(ActorDiagnosticAction.Idle, 0xc9001810, 4753, 1, 8, 15, 8, 6),
        new(ActorDiagnosticAction.Walk, 0xc9001850, 4754, 1, 8, 8, 15, 10), 184, null);

    public ActorDiagnosticProfileId Id { get; }
    public ActorDiagnosticClock Clock { get; }
    public ActorDiagnosticResource Idle { get; }
    public ActorDiagnosticResource Walk { get; }
    public int DistinctKeys { get; }
    public long? OriginalRgbaBytes { get; }
    public int? ModuleOrdinal => Id == ActorDiagnosticProfileId.MonsterRegression ? 643 : null;
    public string Provenance => Id == ActorDiagnosticProfileId.HumanFixed ? "mapping-only type2/body0 female fixed underwear0 single-layer" : "direct module ordinal643 single-layer monster";

    public static ActorDiagnosticProfile ForMode(TerrainViewerMode mode) => mode == TerrainViewerMode.WorldDiagnostic ? HumanFixed : MonsterRegression;
    public ActorDiagnosticResource Resource(ActorDiagnosticAction action) => action switch
    {
        ActorDiagnosticAction.Idle => Idle, ActorDiagnosticAction.Walk => Walk, _ => throw MetadataFailure()
    };
    public ActorDiagnosticAction ActionFor(uint selector) => selector == Idle.Selector ? ActorDiagnosticAction.Idle
        : selector == Walk.Selector ? ActorDiagnosticAction.Walk : throw MetadataFailure();

    public ActorDiagnosticFacing Facing(ActorDiagnosticAction action, int projectOctant)
    {
        _ = Resource(action);
        // Both action tables were independently ratified for HumanFixed. The
        // legacy profile happens to agree numerically; this is not compass parity.
        return projectOctant switch
        {
            0 => new(0, 0, 0, false), 1 => new(1, 1, 7, true), 2 => new(2, 2, 6, true), 3 => new(3, 3, 5, true),
            4 => new(4, 4, 4, false), 5 => new(5, 5, 5, false), 6 => new(6, 6, 6, false), 7 => new(7, 7, 7, false),
            _ => throw new TerrainViewerException(new(TerrainViewerErrorCode.ActorFacing, Stage: TerrainViewerFailureStage.ActorFacing))
        };
    }
    internal IEnumerable<int> UsedGroups(ActorDiagnosticAction action) => Id == ActorDiagnosticProfileId.MonsterRegression
        ? Enumerable.Range(0, Resource(action).PhysicalGroups)
        : Enumerable.Range(0, 8).Select(slot => Facing(action, slot).PhysicalGroup).Distinct().Order();

    internal void ValidateFrame(ActorDiagnosticFrame frame)
    {
        var descriptor = Resource(ActionFor(frame.TextureKey.Selector));
        var key = frame.TextureKey;
        if (key.ResourceOrdinal != descriptor.Ordinal || key.Palette != descriptor.Palette
            || !UsedGroups(descriptor.Action).Contains(key.PhysicalGroup) || key.Frame < 0 || key.Frame >= descriptor.FramesPerGroup
            || frame.FramesPerAction != descriptor.FramesPerGroup || frame.RawFramesPerSecond != descriptor.RawFramesPerSecond
            || frame.Width <= 0 || frame.Height <= 0) throw MetadataFailure();
    }

    internal void ValidateOriginalMetadata(ActorDiagnosticAction action, ArtMetadata metadata)
    {
        if (Id != ActorDiagnosticProfileId.HumanFixed) return;
        // Independent research numeric serialization: 10 little-endian int32
        // header values followed by width,height,encodedLength,hotX,hotY,offsetX,
        // offsetY for every physical row in group-major order. No asset/pixel hash.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> value = stackalloc byte[4];
        var h = metadata.Header;
        foreach (var number in new[] { checked((int)h.Flags), h.FramesPerSecond, h.BitsPerPixel, h.ActionFrame, h.PhysicalGroupCount, h.FramesPerGroup }
            .Concat(h.PalettePresenceWords.Select(word => checked((int)word))))
        { BinaryPrimitives.WriteInt32LittleEndian(value, number); hash.AppendData(value); }
        foreach (var row in metadata.Frames)
        foreach (var number in new[] { row.Width, row.Height, row.EncodedPayloadLength, row.HotX, row.HotY, row.OffsetX, row.OffsetY })
        { BinaryPrimitives.WriteInt32LittleEndian(value, number); hash.AppendData(value); }
        var expected = action == ActorDiagnosticAction.Idle ? "45c4311499073a0a1ed47fa70ca02d21fce062cc4b556e65fc6e0156a91b999d"
            : "be57d502d80fd7e178c646b5b639b212b6f018e2ef284355bd2f98d94a827c78";
        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), expected, StringComparison.Ordinal)) throw MetadataFailure();
    }
    private static TerrainViewerException MetadataFailure() => new(new(TerrainViewerErrorCode.ActorMetadata, Stage: TerrainViewerFailureStage.ActorMetadata));
}
