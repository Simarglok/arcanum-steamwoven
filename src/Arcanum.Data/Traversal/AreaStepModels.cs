using Arcanum.Data.Area;

namespace Arcanum.Data.Traversal;

/// <summary>World cell of the admitted area profile (map coordinates, not sector-local).</summary>
public readonly record struct AreaStepCell(long X, long Y);
public enum AreaStepInputKind { Structural = 0, AuthenticatedOriginal = 1 }
public enum AreaStepTerrainSource { SerializedMask = 0, FacadeBit = 1, TileName = 2 }
public enum AreaStepObjectRole { Destination = 0, CounterClockwiseShoulder = 1, ClockwiseShoulder = 2 }
public enum AreaStepInvocationCode { InputUnavailable = 0, FingerprintMismatch = 1, InvalidSelection = 2, MalformedSource = 3, LimitExceeded = 4 }
public enum AreaStepInvocationStage { Selection = 0, Terrain = 1, Rules = 2, Objects = 3, Fingerprint = 4 }

/// <summary>A construction failure, never successful Unknown evidence. No private inner exception is retained.</summary>
public sealed class AreaStepInvocationException : Exception
{
    internal AreaStepInvocationException(AreaStepInvocationCode code, AreaStepInvocationStage stage) : base("AREA_STEP_INVOCATION_FAILED")
    { Code = code; Stage = stage; }
    public AreaStepInvocationCode Code { get; }
    public AreaStepInvocationStage Stage { get; }
}

/// <summary>Content identity of one consumed source: a whole input file or one selected resource payload.</summary>
public sealed class AreaStepSourceIdentity
{
    internal AreaStepSourceIdentity(string role, long length, string sha256) { Role = role; Length = length; Sha256 = sha256; }
    public string Role { get; }
    public long Length { get; }
    public string Sha256 { get; }
}

/// <summary>Owned value identity of the selected evidence. A digest is not authentication or movement permission.</summary>
public sealed class AreaStepInputIdentity
{
    internal AreaStepInputIdentity(AreaStepInputKind kind, string areaProfile, IEnumerable<AreaStepSourceIdentity> sources, string evidenceSha256)
    { Kind = kind; AreaProfile = areaProfile; Sources = Array.AsReadOnly(sources.ToArray()); EvidenceSha256 = evidenceSha256; }
    public AreaStepInputKind Kind { get; }
    public string AreaProfile { get; }
    public IReadOnlyList<AreaStepSourceIdentity> Sources { get; }
    public string EvidenceSha256 { get; }
}

public sealed class AreaStepRequest
{
    public AreaStepRequest(string profileId, string evidenceSha256, long evidenceEpoch, AreaStepCell from, AreaStepCell to,
        CardinalTraversalDirection direction, CardinalTraversalContext context)
    {
        CardinalTraversalRules.ValidateId(profileId, nameof(profileId));
        ArgumentException.ThrowIfNullOrEmpty(evidenceSha256); ArgumentNullException.ThrowIfNull(context);
        if (evidenceEpoch < 0) throw new ArgumentOutOfRangeException(nameof(evidenceEpoch));
        ProfileId = profileId; EvidenceSha256 = evidenceSha256; EvidenceEpoch = evidenceEpoch;
        From = from; To = to; Direction = direction; Context = context;
    }
    public string ProfileId { get; }
    public string EvidenceSha256 { get; }
    public long EvidenceEpoch { get; }
    public AreaStepCell From { get; }
    public AreaStepCell To { get; }
    public CardinalTraversalDirection Direction { get; }
    public CardinalTraversalContext Context { get; }
}

/// <summary>Destination tile component at T0. Coverage is Supported only with a disposition.</summary>
public sealed class AreaStepTerrainEvidence
{
    internal AreaStepTerrainEvidence(AreaStepCell cell, uint rawArt, CardinalTraversalCoverage coverage, CardinalTraversalDisposition? disposition,
        AreaStepTerrainSource? source, string reason)
    {
        if ((coverage == CardinalTraversalCoverage.Supported) != (disposition.HasValue && source.HasValue))
            throw new ArgumentException("Only supported terrain carries a disposition and source.");
        Cell = cell; RawArt = rawArt; Coverage = coverage; Disposition = disposition; Source = source; ReasonCode = reason;
    }
    public AreaStepCell Cell { get; }
    public uint RawArt { get; }
    public CardinalTraversalCoverage Coverage { get; }
    public CardinalTraversalDisposition? Disposition { get; }
    public AreaStepTerrainSource? Source { get; }
    public string ReasonCode { get; }
}

/// <summary>One registered occurrence at a role cell. Exclusions are retained as NotApplicable, never dropped.</summary>
public sealed class AreaStepObjectEvidence
{
    internal AreaStepObjectEvidence(AreaStepObjectEvaluation evaluation, AreaStepObjectRole role)
    {
        Role = role; Cell = evaluation.Cell; Key = evaluation.Key; ObjectType = evaluation.ObjectType;
        Coverage = evaluation.Coverage; Applicability = evaluation.Applicability; Disposition = evaluation.Disposition;
        ReasonCode = evaluation.Reason; CurrentHpMinimum = evaluation.HpMinimum; CurrentHpMaximum = evaluation.HpMaximum;
    }
    public AreaStepObjectRole Role { get; }
    public AreaStepCell Cell { get; }
    public AreaObjectKey Key { get; }
    public int ObjectType { get; }
    public CardinalTraversalCoverage Coverage { get; }
    public CardinalTraversalApplicability Applicability { get; }
    public CardinalTraversalDisposition? Disposition { get; }
    public string ReasonCode { get; }
    public long? CurrentHpMinimum { get; }
    public long? CurrentHpMaximum { get; }
}

public sealed class AreaStepGap
{
    internal AreaStepGap(string code, AreaStepCell? cell = null, AreaObjectKey? key = null, AreaStepObjectRole? role = null)
    { ReasonCode = code; Cell = cell; Object = key; Role = role; }
    public string ReasonCode { get; }
    public AreaStepCell? Cell { get; }
    public AreaObjectKey? Object { get; }
    public AreaStepObjectRole? Role { get; }
}

/// <summary>Immutable directed step result, not a permit. Supported carries a disposition; Blocked keeps unrelated gaps.</summary>
public sealed class AreaStepAssessment
{
    internal AreaStepAssessment(AreaStepRequest request, AreaStepInputIdentity identity, long epoch, CardinalTraversalCoverage coverage,
        CardinalTraversalDisposition? disposition, string reason, AreaStepTerrainEvidence? terrain,
        IEnumerable<AreaStepObjectEvidence> objects, IEnumerable<AreaStepGap> gaps, AreaObjectKey? wallBlocker = null, AreaObjectKey? windowJump = null)
    {
        WallBlocker = wallBlocker; WindowJump = windowJump;
        if ((coverage == CardinalTraversalCoverage.Supported) != disposition.HasValue) throw new ArgumentException("Whole Supported requires disposition.");
        Request = request; InputIdentity = identity; EvidenceEpoch = epoch; Coverage = coverage; Disposition = disposition; ReasonCode = reason;
        DestinationTerrain = terrain; Objects = Array.AsReadOnly(objects.ToArray()); Gaps = Array.AsReadOnly(gaps.ToArray());
        ContextFingerprint = CardinalTraversalEncoding.ContextDigest(request.Context);
    }
    public AreaStepRequest Request { get; }
    public AreaStepInputIdentity InputIdentity { get; }
    public string EvidenceSha256 => InputIdentity.EvidenceSha256;
    public long EvidenceEpoch { get; }
    public string ContextFingerprint { get; }
    public AreaStepCell From => Request.From;
    public AreaStepCell To => Request.To;
    public CardinalTraversalDirection Direction => Request.Direction;
    public CardinalTraversalCoverage Coverage { get; }
    public CardinalTraversalDisposition? Disposition { get; }
    public string ReasonCode { get; }
    public bool CompleteCoverage => Coverage == CardinalTraversalCoverage.Supported && Gaps.Count == 0;
    public AreaStepTerrainEvidence? DestinationTerrain { get; }
    public IReadOnlyList<AreaStepObjectEvidence> Objects { get; }
    public IReadOnlyList<AreaStepGap> Gaps { get; }
    /// <summary>The first solid wall whose edge the step crosses, when that decided the result.</summary>
    public AreaObjectKey? WallBlocker { get; }
    /// <summary>ARC-WORLD-021: the window wall piece this cardinal step crosses (a walker vaults it, research
    /// ARC-RES-047 W1–W4), whatever the verdict except a wall that blocks the edge; the step is a jump only when it is
    /// Open. Null when the step crosses no window.</summary>
    public AreaObjectKey? WindowJump { get; }
}

/// <summary>How an edge decides a step. <see cref="Legacy"/> is the pinned-profile rule: an opening
/// piece is Unknown, every other wall blocks. Whole-map evidence (ARC-WORLD-018) states its rule;
/// <see cref="WindowJump"/> blocks a diagonal leg; on a cardinal step it lets the walker jump the window
/// (ARC-WORLD-021): the step is Open when nothing else decides it, and names the window.
/// <see cref="Leaving"/> (ARC-RES-049) is a closed locked portal that opens only for a step leaving a
/// building as the original's two open attempts see it; any other step is blocked (a plain lock) or
/// Unknown (a lock that depends on the hour).</summary>
internal enum AreaStepEdgeRule { Legacy, Block, Pass, Unknown, WindowJump, Leaving }

/// <summary>One wall or portal on one tile edge (odd direction 1, 3, 5, 7). A <see cref="AreaStepEdgeRule.Leaving"/>
/// portal keeps its raw ART rotation 0..7, which its leaving test steps by.</summary>
internal sealed record AreaStepWallEdge(AreaObjectKey Key, AreaStepCell Cell, int Edge, bool Opening,
    AreaStepEdgeRule Rule = AreaStepEdgeRule.Legacy, string? Reason = null, int RawRotation = -1);

internal sealed record AreaStepObjectEvaluation(AreaObjectKey Key, AreaStepCell Cell, int ObjectType, CardinalTraversalCoverage Coverage,
    CardinalTraversalApplicability Applicability, CardinalTraversalDisposition? Disposition, string Reason, long? HpMinimum = null, long? HpMaximum = null);
