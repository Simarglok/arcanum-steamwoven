using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanum.Data;

namespace Arcanum.TerrainViewer.Core;

public enum ActorDiagnosticProfileId { HumanFixed, MonsterRegression }
public enum ActorDiagnosticClock { ArtMetadata, LegacyOverride }
public sealed record ActorDiagnosticResource(ActorDiagnosticAction Action, uint Selector, int Ordinal, int Palette,
    int PhysicalGroups, int FramesPerGroup, int RawFramesPerSecond, int DiagnosticFramesPerSecond)
{
    /// <summary>ARC-WORLD-020: the ART stride of a walk or run resource (research ARC-RES-047 L7); the frame offsets it
    /// restates are covered by the original metadata commitment.</summary>
    public GaitStride? Stride { get; init; }
    /// <summary>ARC-COMBAT-005: the ART's action frame (the attack's hit), restated from the metadata commitment.</summary>
    public int ActionFrame { get; init; }
}
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
        ActorDiagnosticResource idle, ActorDiagnosticResource walk, ActorDiagnosticResource? run, ActorDiagnosticResource? vault,
        ActorDiagnosticResource? attack, int coverage, long? rgbaBytes)
    {
        Id = id; Clock = clock; Idle = idle; Walk = walk; Run = run; Vault = vault; Attack = attack;
        DistinctKeys = coverage; OriginalRgbaBytes = rgbaBytes;
    }

    private static readonly ActorDiagnosticResource HumanIdle = new(ActorDiagnosticAction.Idle, 0x20000000, 2667, 0, 8, 1, 6, 6);
    // ARC-WORLD-020: walk and run strides from research ARC-RES-047 L7 (per-cycle frame offsets on an axis and a
    // diagonal step); the offsets they restate are part of the metadata commitment below.
    private static readonly ActorDiagnosticResource HumanWalk = new(ActorDiagnosticAction.Walk, 0x20000040, 2668, 0, 8, 10, 17, 17) { Stride = new(10, 17, 42, 22, 60) };
    private static readonly ActorDiagnosticResource HumanRun = new(ActorDiagnosticAction.Run, 0x20000180, 2671, 0, 8, 10, 20, 20) { Stride = new(10, 20, 119, 60, 170) };
    // ARC-WORLD-021: the window vault (animation 13), 9 frames at 8 fps, action frame 2 (research ARC-RES-047 W4/W5).
    private static readonly ActorDiagnosticResource HumanVault = new(ActorDiagnosticAction.Vault, 0x20000340, 2677, 0, 8, 9, 8, 8);
    // ARC-COMBAT-005: the bare-handed attack (animation 20, weapon slot 1), 9 frames at 15 fps, action frame 6.
    private static readonly ActorDiagnosticResource HumanAttack = new(ActorDiagnosticAction.Attack, 0x20000501, 2683, 0, 8, 9, 15, 15) { ActionFrame = 6 };
    public static ActorDiagnosticProfile HumanFixed { get; } = new(ActorDiagnosticProfileId.HumanFixed, ActorDiagnosticClock.ArtMetadata,
        HumanIdle, HumanWalk, null, null, null, 55, 457676);
    /// <summary>ARC-WORLD-020/021: the same human with its run ART (animation 6, resource1 ordinal 2671) and window vault
    /// (animation 13, ordinal 2677), and since ARC-COMBAT-005 its bare-handed attack (animation 20, ordinal 2683), loaded by
    /// the AREA game view only. The WORLD diagnostic and its sealed human validation keep the 55-key <see cref="HumanFixed"/>.</summary>
    public static ActorDiagnosticProfile HumanFixedArea { get; } = new(ActorDiagnosticProfileId.HumanFixed, ActorDiagnosticClock.ArtMetadata,
        HumanIdle, HumanWalk, HumanRun, HumanVault, HumanAttack, 195, 1705144);
    public static ActorDiagnosticProfile MonsterRegression { get; } = new(ActorDiagnosticProfileId.MonsterRegression, ActorDiagnosticClock.LegacyOverride,
        new(ActorDiagnosticAction.Idle, 0xc9001810, 4753, 1, 8, 15, 8, 6),
        new(ActorDiagnosticAction.Walk, 0xc9001850, 4754, 1, 8, 8, 15, 10), null, null, null, 184, null);

    public ActorDiagnosticProfileId Id { get; }
    public ActorDiagnosticClock Clock { get; }
    public ActorDiagnosticResource Idle { get; }
    public ActorDiagnosticResource Walk { get; }
    /// <summary>The run resource, or null when the profile has none (the monster regression).</summary>
    public ActorDiagnosticResource? Run { get; }
    /// <summary>The window vault resource, or null when the profile has none.</summary>
    public ActorDiagnosticResource? Vault { get; }
    /// <summary>ARC-COMBAT-005: the bare-handed attack resource, or null when the profile has none.</summary>
    public ActorDiagnosticResource? Attack { get; }
    /// <summary>The actions this profile decodes, in load order.</summary>
    public IReadOnlyList<ActorDiagnosticAction> Actions => Run is null ? IdleWalk : Vault is null ? IdleWalkRun : Attack is null ? IdleWalkRunVault : IdleWalkRunVaultAttack;
    private static readonly IReadOnlyList<ActorDiagnosticAction> IdleWalk = Array.AsReadOnly([ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk]);
    private static readonly IReadOnlyList<ActorDiagnosticAction> IdleWalkRun = Array.AsReadOnly([ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk, ActorDiagnosticAction.Run]);
    private static readonly IReadOnlyList<ActorDiagnosticAction> IdleWalkRunVault = Array.AsReadOnly([ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk, ActorDiagnosticAction.Run, ActorDiagnosticAction.Vault]);
    private static readonly IReadOnlyList<ActorDiagnosticAction> IdleWalkRunVaultAttack = Array.AsReadOnly([ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk, ActorDiagnosticAction.Run, ActorDiagnosticAction.Vault, ActorDiagnosticAction.Attack]);
    public int DistinctKeys { get; }
    public long? OriginalRgbaBytes { get; }
    public int? ModuleOrdinal => Id == ActorDiagnosticProfileId.MonsterRegression ? 643 : null;
    public string Provenance => Id == ActorDiagnosticProfileId.HumanFixed ? "mapping-only type2/body0 female fixed underwear0 single-layer" : "direct module ordinal643 single-layer monster";

    public static ActorDiagnosticProfile ForMode(TerrainViewerMode mode) => mode == TerrainViewerMode.WorldDiagnostic ? HumanFixed : MonsterRegression;
    public ActorDiagnosticResource Resource(ActorDiagnosticAction action) => action switch
    {
        ActorDiagnosticAction.Idle => Idle, ActorDiagnosticAction.Walk => Walk, ActorDiagnosticAction.Run => Run ?? throw MetadataFailure(),
        ActorDiagnosticAction.Vault => Vault ?? throw MetadataFailure(),
        ActorDiagnosticAction.Attack => Attack ?? throw MetadataFailure(),
        _ => throw MetadataFailure()
    };
    public ActorDiagnosticAction ActionFor(uint selector) => selector == Idle.Selector ? ActorDiagnosticAction.Idle
        : selector == Walk.Selector ? ActorDiagnosticAction.Walk : Run is not null && selector == Run.Selector ? ActorDiagnosticAction.Run
        : Vault is not null && selector == Vault.Selector ? ActorDiagnosticAction.Vault
        : Attack is not null && selector == Attack.Selector ? ActorDiagnosticAction.Attack : throw MetadataFailure();

    public ActorDiagnosticFacing Facing(ActorDiagnosticAction action, int projectOctant)
    {
        _ = Resource(action);
        // Both action tables were independently ratified for HumanFixed. The
        // legacy profile happens to agree numerically; this is not compass parity.
        // ARC-WORLD-020/021, ARC-COMBAT-005: Run, Vault and Attack reuse the same table; their rows are not independently verified.
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
        var expected = action switch
        {
            ActorDiagnosticAction.Idle => "45c4311499073a0a1ed47fa70ca02d21fce062cc4b556e65fc6e0156a91b999d",
            ActorDiagnosticAction.Walk => "be57d502d80fd7e178c646b5b639b212b6f018e2ef284355bd2f98d94a827c78",
            ActorDiagnosticAction.Run => "39f2a93d0a7d667d6811b18ba4dbe4806991d596607f1c822f5785c0f70a5578",
            ActorDiagnosticAction.Vault => "e4f5ddecd90ae61b132229fe0da35494b989f23ebb92d89ec84388ba918817b0",
            ActorDiagnosticAction.Attack => "a311084c0098607d2bf09e0547cff9fa008daae367b28389034a38ba998caec8",
            _ => throw MetadataFailure(),
        };
        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), expected, StringComparison.Ordinal)) throw MetadataFailure();
    }
    private static TerrainViewerException MetadataFailure() => new(new(TerrainViewerErrorCode.ActorMetadata, Stage: TerrainViewerFailureStage.ActorMetadata));
}
