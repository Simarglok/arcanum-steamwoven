using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Traversal;

public readonly record struct CardinalTraversalCell(int X, int Y);
public enum CardinalTraversalAdmissionPolicy { CardinalOnly = 0, BoundedAdjacent = 1 }
public enum CardinalTraversalLegRole { XShoulder = 0, XFinal = 1, YShoulder = 2, YFinal = 3 }
public enum CardinalTraversalDirection
{
    XMinusYMinus = 0, XMinus = 1, XMinusYPlus = 2, YPlus = 3,
    XPlusYPlus = 4, XPlus = 5, XPlusYMinus = 6, YMinus = 7
}
public enum CardinalTraversalCoverage { Supported = 0, Unknown = 1, Unsupported = 2 }
public enum CardinalTraversalDisposition { Open = 0, Blocked = 1 }
public enum CardinalTraversalApplicability { Applicable = 0, NotApplicable = 1, Unknown = 2 }
public enum CardinalTraversalInputKind { Structural = 0, AuthenticatedOriginal = 1 }
public enum CardinalTraversalActorState { OrdinaryGround = 0, Floating = 1, Passwall = 2, SpecialOverride = 3 }
public enum CardinalTraversalLocationStatus { InRange = 0, Absent = 1, OutOfRange = 2, Overflow = 3 }
public enum CardinalTraversalComponentKind
{
    Actor = 0, SourceMask = 1, SourceFacade = 2, SourceName = 3,
    DestinationMask = 4, DestinationFacade = 5, DestinationName = 6,
    Registration = 7, Extent = 8, Census = 9, SourceEdges = 10,
    DestinationOtherTypes = 11, SupplementalRegistry = 12, ActorSelf = 13,
    RouteTrapFireWindow = 14, ScriptExecution = 15
}
public enum CardinalTraversalProvenanceKind
{
    SerializedSector = 0, PackedTerrainId = 1, SelectedTileName = 2, SerializedInstance = 3,
    PrototypeDefault = 4, PinnedSpecification = 5, ProjectPolicy = 6, OriginalInputIdentity = 7
}
public enum CardinalTraversalInvocationCode
{
    InputUnavailable = 0, FingerprintMismatch = 1, InputChanged = 2,
    InvalidSelection = 3, MalformedSource = 4, LimitExceeded = 5
}
public enum CardinalTraversalInvocationStage { Selection = 0, Terrain = 1, Records = 2, Prototypes = 3, Fingerprint = 4 }

/// <summary>A construction failure, never successful Unknown evidence. No private inner exception is retained.</summary>
public sealed class CardinalTraversalInvocationException : Exception
{
    internal CardinalTraversalInvocationException(CardinalTraversalInvocationCode code, CardinalTraversalInvocationStage stage,
        int? sourceIndex = null, int? offset = null) : base("CARDINAL_INVOCATION_FAILED")
    { Code = code; Stage = stage; SourceIndex = sourceIndex; Offset = offset; }
    public CardinalTraversalInvocationCode Code { get; }
    public CardinalTraversalInvocationStage Stage { get; }
    public int? SourceIndex { get; }
    public int? Offset { get; }
}

/// <summary>Explicit project diagnostic declaration, not a recovered world actor or caller-authored derived flags.</summary>
public sealed class CardinalTraversalContext
{
    public CardinalTraversalContext(string contextId, int actorKind, int currentArtClass, uint spellFlags, uint pathFlags,
        bool polymorph, bool editor, CardinalTraversalActorState actorState, int supplementalRegistryCount)
    {
        CardinalTraversalRules.ValidateId(contextId, nameof(contextId));
        if (supplementalRegistryCount < 0) throw new ArgumentOutOfRangeException(nameof(supplementalRegistryCount));
        ContextId = contextId; ActorKind = actorKind; CurrentArtClass = currentArtClass; SpellFlags = spellFlags;
        PathFlags = pathFlags; Polymorph = polymorph; Editor = editor; ActorState = actorState;
        SupplementalRegistryCount = supplementalRegistryCount;
    }
    public string ContextId { get; }
    public int ActorKind { get; }
    public int CurrentArtClass { get; }
    public uint SpellFlags { get; }
    public uint PathFlags { get; }
    public bool Polymorph { get; }
    public bool Editor { get; }
    public CardinalTraversalActorState ActorState { get; }
    public int SupplementalRegistryCount { get; }
}

public sealed class CardinalTraversalSectorIdentity
{
    internal CardinalTraversalSectorIdentity(TerrainSectorSelector selector, DatEntry entry)
    {
        SectorX = selector.SectorX; SectorY = selector.SectorY; PackedId = selector.PackedSectorId;
        OriginX = selector.OriginX; OriginY = selector.OriginY; Width = selector.Width; Height = selector.Height;
        EntryOrdinal = entry.Ordinal; Flags = entry.Flags; DecodedSize = entry.DeclaredOutputSize;
    }
    public int SectorX { get; }
    public int SectorY { get; }
    public long PackedId { get; }
    public int OriginX { get; }
    public int OriginY { get; }
    public int Width { get; }
    public int Height { get; }
    public int EntryOrdinal { get; }
    public uint Flags { get; }
    public uint DecodedSize { get; }
    internal bool Same(CardinalTraversalSectorIdentity other) => SectorX == other.SectorX && SectorY == other.SectorY &&
        PackedId == other.PackedId && OriginX == other.OriginX && OriginY == other.OriginY && Width == other.Width &&
        Height == other.Height && EntryOrdinal == other.EntryOrdinal && Flags == other.Flags && DecodedSize == other.DecodedSize;
}

/// <summary>Owned value identity of selected evidence. A digest is not authentication or movement permission.</summary>
public sealed class CardinalTraversalInputIdentity
{
    internal CardinalTraversalInputIdentity(CardinalTraversalInputKind kind, string moduleId, string resourceId, string prototypeId,
        CardinalTraversalSectorIdentity sector, TerrainEntryExpectation? nameEntry, StaticObstructionInputIdentity? original, string digest)
    {
        Kind = kind; ModuleSourceId = moduleId; ResourceSourceId = resourceId; PrototypeSourceId = prototypeId;
        Sector = sector; OriginalIdentity = original; SelectedEvidenceSha256 = digest;
        SelectedNameEntryOrdinal = nameEntry?.Ordinal; SelectedNameEntryFlags = nameEntry?.Flags;
        SelectedNameEntryDecodedSize = nameEntry?.DeclaredDecodedSize;
    }
    public CardinalTraversalInputKind Kind { get; }
    public string ModuleSourceId { get; }
    public string ResourceSourceId { get; }
    public string PrototypeSourceId { get; }
    public CardinalTraversalSectorIdentity Sector { get; }
    public TerrainPathProfile PathProfile => TerrainPathProfile.NonWindowsMacOs;
    public int? SelectedNameEntryOrdinal { get; }
    public uint? SelectedNameEntryFlags { get; }
    public uint? SelectedNameEntryDecodedSize { get; }
    public string SelectedEvidenceSha256 { get; }
    public StaticObstructionInputIdentity? OriginalIdentity { get; }
    internal bool Same(CardinalTraversalInputIdentity other)
    {
        if (Kind != other.Kind || ModuleSourceId != other.ModuleSourceId || ResourceSourceId != other.ResourceSourceId ||
            PrototypeSourceId != other.PrototypeSourceId || PathProfile != other.PathProfile || !Sector.Same(other.Sector) ||
            SelectedNameEntryOrdinal != other.SelectedNameEntryOrdinal || SelectedNameEntryFlags != other.SelectedNameEntryFlags ||
            SelectedNameEntryDecodedSize != other.SelectedNameEntryDecodedSize || SelectedEvidenceSha256 != other.SelectedEvidenceSha256) return false;
        if (OriginalIdentity is null || other.OriginalIdentity is null) return OriginalIdentity is null && other.OriginalIdentity is null;
        return OriginalIdentity.Module == other.OriginalIdentity.Module && OriginalIdentity.Resource == other.OriginalIdentity.Resource &&
            OriginalIdentity.PrototypeAggregateSha256 == other.OriginalIdentity.PrototypeAggregateSha256 &&
            OriginalIdentity.Prototypes.SequenceEqual(other.OriginalIdentity.Prototypes);
    }
}

public sealed class CardinalTraversalRequest
{
    public CardinalTraversalRequest(string profileId, CardinalTraversalInputIdentity inputIdentity, long evidenceEpoch,
        int sectorX, int sectorY, CardinalTraversalCell from, CardinalTraversalCell to,
        CardinalTraversalDirection direction, CardinalTraversalContext context)
    {
        CardinalTraversalRules.ValidateId(profileId, nameof(profileId));
        ArgumentNullException.ThrowIfNull(inputIdentity); ArgumentNullException.ThrowIfNull(context);
        if (evidenceEpoch < 0) throw new ArgumentOutOfRangeException(nameof(evidenceEpoch));
        ProfileId = profileId; InputIdentity = inputIdentity; EvidenceEpoch = evidenceEpoch; SectorX = sectorX; SectorY = sectorY;
        From = from; To = to; Direction = direction; Context = context;
    }
    public string ProfileId { get; }
    public CardinalTraversalInputIdentity InputIdentity { get; }
    public long EvidenceEpoch { get; }
    public int SectorX { get; }
    public int SectorY { get; }
    public CardinalTraversalCell From { get; }
    public CardinalTraversalCell To { get; }
    public CardinalTraversalDirection Direction { get; }
    public CardinalTraversalContext Context { get; }
}

public sealed class CardinalTraversalCoordinateEvidence
{
    internal CardinalTraversalCoordinateEvidence(CardinalTraversalCell local, bool admitted)
    { Local = local; WorldX = admitted ? 92928 + local.X : null; WorldY = admitted ? 82560 + local.Y : null; Ordinal = admitted ? local.X + 64 * local.Y : null; }
    public CardinalTraversalCell Local { get; }
    public int? WorldX { get; }
    public int? WorldY { get; }
    public int? Ordinal { get; }
}

public sealed class CardinalTraversalNumericInput
{
    internal CardinalTraversalNumericInput(string code, long value) { Code = code; Value = value; }
    public string Code { get; }
    public long Value { get; }
}
public sealed class CardinalTraversalProvenance
{
    internal CardinalTraversalProvenance(CardinalTraversalProvenanceKind kind, string sourceId, int? entryOrdinal = null,
        int? sourceIndex = null, int? prototypeAid = null, string? specificationId = null)
    { Kind = kind; SourceId = sourceId; EntryOrdinal = entryOrdinal; SourceIndex = sourceIndex; PrototypeAid = prototypeAid; SpecificationId = specificationId; }
    public CardinalTraversalProvenanceKind Kind { get; }
    public string SourceId { get; }
    public int? EntryOrdinal { get; }
    public int? SourceIndex { get; }
    public int? PrototypeAid { get; }
    public string? SpecificationId { get; }
}
public sealed class CardinalTraversalComponentAssessment
{
    internal CardinalTraversalComponentAssessment(CardinalTraversalComponentKind kind, CardinalTraversalApplicability applicability,
        CardinalTraversalCoverage coverage, CardinalTraversalDisposition? disposition, string reason,
        IEnumerable<CardinalTraversalNumericInput> inputs, IEnumerable<CardinalTraversalProvenance> provenance)
    {
        if ((coverage == CardinalTraversalCoverage.Supported && applicability == CardinalTraversalApplicability.Applicable) != disposition.HasValue)
            throw new ArgumentException("Only supported applicable components carry disposition.");
        ComponentKind = kind; Applicability = applicability; Coverage = coverage; Disposition = disposition; ReasonCode = reason;
        Inputs = Array.AsReadOnly(inputs.ToArray()); Provenance = Array.AsReadOnly(provenance.ToArray());
    }
    public CardinalTraversalComponentKind ComponentKind { get; }
    public CardinalTraversalApplicability Applicability { get; }
    public CardinalTraversalCoverage Coverage { get; }
    public CardinalTraversalDisposition? Disposition { get; }
    public string ReasonCode { get; }
    public IReadOnlyList<CardinalTraversalNumericInput> Inputs { get; }
    public IReadOnlyList<CardinalTraversalProvenance> Provenance { get; }
}
public sealed class CardinalTraversalGap
{
    internal CardinalTraversalGap(CardinalTraversalComponentAssessment result, int? sourceIndex = null)
    { ComponentKind = result.ComponentKind; SourceIndex = sourceIndex; ReasonCode = result.ReasonCode; Provenance = result.Provenance; }
    internal CardinalTraversalGap(CardinalTraversalGap gap, CardinalTraversalLegRole role)
    { ComponentKind = gap.ComponentKind; SourceIndex = gap.SourceIndex; ReasonCode = gap.ReasonCode; Provenance = gap.Provenance; LegRole = role; }
    public CardinalTraversalLegRole? LegRole { get; }
    public CardinalTraversalComponentKind ComponentKind { get; }
    public int? SourceIndex { get; }
    public string ReasonCode { get; }
    public IReadOnlyList<CardinalTraversalProvenance> Provenance { get; }
}

public sealed class CardinalTraversalCensusRecord
{
    internal CardinalTraversalCensusRecord(StaticObjectTraversalRecord raw)
    {
        SourceIndex = raw.SourceIndex; ObjectType = raw.ObjectType; PrototypeAid = raw.PrototypeAid;
        PackedLocation = raw.PackedLocation; DirectFlags = raw.DirectFlags; EffectiveFlags = raw.EffectiveFlags;
        FlagsSource = raw.DirectFlags.HasValue && raw.ObjectType == 3 ? StaticObstructionFlagsSource.Direct :
            raw.FlagsInherited && raw.EffectiveFlags.HasValue ? StaticObstructionFlagsSource.Prototype : StaticObstructionFlagsSource.Unresolved;
        LocationStatus = CardinalTraversalLocationStatus.Absent;
        if (raw.PackedLocation is not long packed) return;
        var wx = unchecked((int)(uint)packed); var wy = unchecked((int)(packed >> 32));
        var x = (long)wx - 92928; var y = (long)wy - 82560;
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
        { LocationStatus = CardinalTraversalLocationStatus.Overflow; return; }
        if (x < 0 || x >= 64 || y < 0 || y >= 64)
        { LocationStatus = CardinalTraversalLocationStatus.OutOfRange; return; }
        WorldX = wx; WorldY = wy; LocalCell = new((int)x, (int)y); LocationStatus = CardinalTraversalLocationStatus.InRange;
    }
    public int SourceIndex { get; }
    public int ObjectType { get; }
    public int? PrototypeAid { get; }
    public long? PackedLocation { get; }
    public int? WorldX { get; }
    public int? WorldY { get; }
    public CardinalTraversalCell? LocalCell { get; }
    public uint? DirectFlags { get; }
    public uint? EffectiveFlags { get; }
    public StaticObstructionFlagsSource FlagsSource { get; }
    public CardinalTraversalLocationStatus LocationStatus { get; }
}

public sealed class CardinalTraversalRecordAssessment
{
    private readonly CardinalTraversalCensusRecord _record;
    internal CardinalTraversalRecordAssessment(CardinalTraversalCensusRecord record, CardinalTraversalApplicability registration,
        CardinalTraversalComponentAssessment result) { _record = record; RegistrationApplicability = registration; Result = result; }
    public int SourceIndex => _record.SourceIndex;
    public int ObjectType => _record.ObjectType;
    public int? PrototypeAid => _record.PrototypeAid;
    public long? PackedLocation => _record.PackedLocation;
    public int? WorldX => _record.WorldX;
    public int? WorldY => _record.WorldY;
    public CardinalTraversalCell? LocalCell => _record.LocalCell;
    public uint? DirectFlags => _record.DirectFlags;
    public uint? EffectiveFlags => _record.EffectiveFlags;
    public StaticObstructionFlagsSource FlagsSource => _record.FlagsSource;
    public CardinalTraversalLocationStatus LocationStatus => _record.LocationStatus;
    public CardinalTraversalApplicability RegistrationApplicability { get; }
    public CardinalTraversalComponentAssessment Result { get; }
}

/// <summary>Object-only shoulder constraint, never a terrain query or runtime waypoint.</summary>
public sealed class CardinalTraversalLegEvidence
{
    internal CardinalTraversalLegEvidence(CardinalTraversalLegRole role, CardinalTraversalCell from, CardinalTraversalCell to,
        CardinalTraversalDirection direction, CardinalTraversalDirection originalDirection,
        IEnumerable<CardinalTraversalComponentAssessment> components, IEnumerable<CardinalTraversalRecordAssessment> records,
        IEnumerable<CardinalTraversalGap> gaps)
    {
        Role = role; From = from; To = to; Direction = direction; OriginalDirection = originalDirection;
        Components = Array.AsReadOnly(components.ToArray()); RecordAssessments = Array.AsReadOnly(records.ToArray());
        Gaps = Array.AsReadOnly(gaps.Select(g => new CardinalTraversalGap(g, role)).ToArray());
    }
    public CardinalTraversalLegRole Role { get; }
    public CardinalTraversalCell From { get; }
    public CardinalTraversalCell To { get; }
    public CardinalTraversalDirection Direction { get; }
    public CardinalTraversalDirection OriginalDirection { get; }
    public IReadOnlyList<CardinalTraversalComponentAssessment> Components { get; }
    public IReadOnlyList<CardinalTraversalRecordAssessment> RecordAssessments { get; }
    public IReadOnlyList<CardinalTraversalGap> Gaps { get; }
}

public sealed class CardinalTraversalAssessment
{
    internal CardinalTraversalAssessment(CardinalTraversalRequest request, CardinalTraversalInputIdentity identity, long epoch,
        bool coordinatesAdmitted, CardinalTraversalCoverage coverage, CardinalTraversalDisposition? disposition, string reason,
        IEnumerable<CardinalTraversalComponentAssessment> components, IEnumerable<CardinalTraversalRecordAssessment> records,
        IEnumerable<CardinalTraversalGap> gaps, IEnumerable<CardinalTraversalLegEvidence>? legs = null)
    {
        if ((coverage == CardinalTraversalCoverage.Supported) != disposition.HasValue) throw new ArgumentException("Whole Supported requires disposition.");
        Request = request; InputIdentity = identity; EvidenceEpoch = epoch; Context = request.Context;
        From = new(request.From, coordinatesAdmitted); To = new(request.To, coordinatesAdmitted); Direction = request.Direction;
        Coverage = coverage; Disposition = disposition; ReasonCode = reason;
        Components = Array.AsReadOnly(components.ToArray()); RecordAssessments = Array.AsReadOnly(records.ToArray()); Gaps = Array.AsReadOnly(gaps.ToArray());
        Legs = Array.AsReadOnly((legs ?? []).ToArray());
        StepEvidence = new(this);
    }
    public CardinalTraversalRequest Request { get; }
    public CardinalTraversalInputIdentity InputIdentity { get; }
    public long EvidenceEpoch { get; }
    public CardinalTraversalContext Context { get; }
    public CardinalTraversalCoordinateEvidence From { get; }
    public CardinalTraversalCoordinateEvidence To { get; }
    public CardinalTraversalDirection Direction { get; }
    public CardinalTraversalCoverage Coverage { get; }
    public CardinalTraversalDisposition? Disposition { get; }
    public string ReasonCode { get; }
    public IReadOnlyList<CardinalTraversalComponentAssessment> Components { get; }
    public IReadOnlyList<CardinalTraversalRecordAssessment> RecordAssessments { get; }
    public IReadOnlyList<CardinalTraversalGap> Gaps { get; }
    public CardinalTraversalStepEvidence StepEvidence { get; }
    public IReadOnlyList<CardinalTraversalLegEvidence> Legs { get; }
}

/// <summary>Immutable full-row step binding, not a permit. Complete Blocked still prohibits a move.</summary>
public sealed class CardinalTraversalStepEvidence
{
    internal CardinalTraversalStepEvidence(CardinalTraversalAssessment assessment)
    {
        ProfileId = assessment.Request.ProfileId; InputIdentity = assessment.InputIdentity; EvidenceEpoch = assessment.EvidenceEpoch;
        Context = assessment.Context; ContextFingerprint = CardinalTraversalEncoding.ContextDigest(Context);
        From = assessment.From; To = assessment.To; Direction = assessment.Direction; Coverage = assessment.Coverage;
        Disposition = assessment.Disposition; ReasonCode = assessment.ReasonCode;
        Components = assessment.Components; RecordAssessments = assessment.RecordAssessments; Gaps = assessment.Gaps;
        Legs = assessment.Legs;
        Provenance = Array.AsReadOnly(Components.SelectMany(c => c.Provenance).Concat(RecordAssessments.SelectMany(r => r.Result.Provenance))
            .Concat(Legs.SelectMany(l => l.Components.SelectMany(c => c.Provenance).Concat(l.RecordAssessments.SelectMany(r => r.Result.Provenance))))
            .Select(p => p.SourceId).Distinct(StringComparer.Ordinal).ToArray());
    }
    public string ProfileId { get; }
    public CardinalTraversalInputIdentity InputIdentity { get; }
    public string SelectedEvidenceSha256 => InputIdentity.SelectedEvidenceSha256;
    public long EvidenceEpoch { get; }
    public CardinalTraversalContext Context { get; }
    public string ContextFingerprint { get; }
    public CardinalTraversalSectorIdentity Sector => InputIdentity.Sector;
    public CardinalTraversalCoordinateEvidence From { get; }
    public CardinalTraversalCoordinateEvidence To { get; }
    public CardinalTraversalDirection Direction { get; }
    public CardinalTraversalCoverage Coverage { get; }
    public CardinalTraversalDisposition? Disposition { get; }
    public string ReasonCode { get; }
    public bool CompleteCoverage => Coverage == CardinalTraversalCoverage.Supported && Gaps.Count == 0;
    public IReadOnlyList<CardinalTraversalLegEvidence> Legs { get; }
    public IReadOnlyList<string> Provenance { get; }
    public IReadOnlyList<CardinalTraversalComponentAssessment> Components { get; }
    public IReadOnlyList<CardinalTraversalRecordAssessment> RecordAssessments { get; }
    public IReadOnlyList<CardinalTraversalGap> Gaps { get; }
}

internal static class CardinalTraversalRules
{
    internal const string ProfileId = "BASE_SECTOR_1452_1290";
    internal static string Profile(CardinalTraversalAdmissionPolicy policy) => policy switch
    {
        CardinalTraversalAdmissionPolicy.CardinalOnly => ProfileId,
        CardinalTraversalAdmissionPolicy.BoundedAdjacent => "BASE_SECTOR_1452_1290_ADJACENT_V1",
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };
    internal const string ContextId = "ISOLATED_PC_CRITTER_ART_NONPOLYMORPH";
    internal const string SpecificationId = "docs/world/cardinal-traversal-contract.md";
    internal static CardinalTraversalContext DeclaredContext() => new(ContextId, 15, 2, 0, 0, false, false, CardinalTraversalActorState.OrdinaryGround, 0);
    internal static void ValidateId(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, parameter);
        if (value.Length > 128 || value.Any(c => c is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_' and not '-' and not '.'))
            throw new ArgumentException("A bounded non-path ASCII identifier is required.", parameter);
    }
}
