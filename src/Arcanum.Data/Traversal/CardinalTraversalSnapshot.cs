using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Traversal;

internal sealed record CardinalTerrainCell(uint Raw, uint? MaskWord, int? CategoryBits, int? NameCategoryIndex,
    int? MaterialOrdinal, int? MesKey, TerrainCellObstructionComponents Components);

/// <summary>Owned numeric evidence, with no source retention or query-time I/O. Admission is deliberately bounded.</summary>
public sealed class CardinalTraversalSnapshot
{
    private readonly CardinalTerrainCell[] _terrain;
    private readonly StaticObstructionSectorReport _static;
    private readonly bool _censusShapeComplete;
    private readonly bool _membershipComplete;
    private readonly IReadOnlyDictionary<int, StaticObstructionRecordAssessment[]> _recordsByIndex;

    internal CardinalTraversalSnapshot(CardinalTraversalInputIdentity identity, long epoch, CardinalTerrainCell[] terrain,
        StaticObstructionSectorReport staticReport, int terminalCount, int tileScripts, int sectorScriptBytes,
        IEnumerable<CardinalTraversalCensusRecord> census, bool censusShapeComplete, bool membershipComplete,
        CardinalTraversalAdmissionPolicy policy = CardinalTraversalAdmissionPolicy.CardinalOnly)
    {
        InputIdentity = identity; EvidenceEpoch = epoch; DeclaredContext = CardinalTraversalRules.DeclaredContext();
        ProfileId = CardinalTraversalRules.Profile(policy); AdmissionPolicy = policy;
        _terrain = terrain.ToArray(); _static = staticReport;
        TerminalRecordCount = terminalCount; TileScriptCount = tileScripts; SectorScriptBytes = sectorScriptBytes;
        Census = Array.AsReadOnly(census.ToArray()); _censusShapeComplete = censusShapeComplete; _membershipComplete = membershipComplete;
        _recordsByIndex = staticReport.Records.GroupBy(r => r.SourceIndex).ToDictionary(g => g.Key, g => g.ToArray());
    }
    public string ProfileId { get; }
    public CardinalTraversalAdmissionPolicy AdmissionPolicy { get; }
    public CardinalTraversalInputIdentity InputIdentity { get; }
    public long EvidenceEpoch { get; }
    public CardinalTraversalContext DeclaredContext { get; }
    public int TerminalRecordCount { get; }
    public int TileScriptCount { get; }
    public int SectorScriptBytes { get; }
    public IReadOnlyList<CardinalTraversalCensusRecord> Census { get; }

    public CardinalTraversalAssessment Assess(CardinalTraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var denial = Validate(request);
        if (denial is not null)
            return new(request, InputIdentity, EvidenceEpoch, false, CardinalTraversalCoverage.Unsupported, null, denial, [], [], []);

        // Branch gaps occur only after complete semantic admission, and never run normal composition.
        var branchGaps = new List<CardinalTraversalGap>();
        if (((int)request.Direction & 1) == 0 && AdmissionPolicy == CardinalTraversalAdmissionPolicy.CardinalOnly)
            branchGaps.Add(new(Unknown(CardinalTraversalComponentKind.SourceEdges, "DIAGONAL_RULE_UNPROVED", Spec())));
        if (request.Context.PathFlags == 0x1000)
            branchGaps.Add(new(Unknown(CardinalTraversalComponentKind.Actor, "PATH_BRANCH_UNAVAILABLE", Spec())));
        if (branchGaps.Count != 0)
            return new(request, InputIdentity, EvidenceEpoch, true, CardinalTraversalCoverage.Unknown, null, branchGaps[0].ReasonCode, [], [], branchGaps);

        var components = new List<CardinalTraversalComponentAssessment>();
        var records = new List<CardinalTraversalRecordAssessment>();
        var gaps = new List<CardinalTraversalGap>();
        void Add(CardinalTraversalComponentAssessment c)
        {
            components.Add(c);
            if (c.Coverage != CardinalTraversalCoverage.Supported) gaps.Add(new(c));
        }
        Add(Open(CardinalTraversalComponentKind.Actor, "DECLARED_ACTOR_SOURCE_PROVED", Spec(),
            [new("ACTOR_KIND", 15), new("CURRENT_ART_CLASS", 2), new("DERIVED_TRAVERSAL_FLAGS", 0)]));
        var from = _terrain[request.From.X + 64 * request.From.Y];
        var to = _terrain[request.To.X + 64 * request.To.Y];
        Add(Terrain(CardinalTraversalComponentKind.SourceMask, from, from.Components.SerializedBlockMask, source: true));
        Add(Terrain(CardinalTraversalComponentKind.SourceFacade, from, from.Components.FacadeWalkabilityBit, source: true));
        Add(Terrain(CardinalTraversalComponentKind.SourceName, from, from.Components.TileNameRule, source: true));
        Add(Terrain(CardinalTraversalComponentKind.DestinationMask, to, to.Components.SerializedBlockMask));
        Add(Terrain(CardinalTraversalComponentKind.DestinationFacade, to, to.Components.FacadeWalkabilityBit));
        Add(Terrain(CardinalTraversalComponentKind.DestinationName, to, to.Components.TileNameRule));
        Add(_membershipComplete ? Open(CardinalTraversalComponentKind.Registration, "EXACT_CELL_REGISTRATION_PROVED", Spec())
            : Unknown(CardinalTraversalComponentKind.Registration, "REGISTRATION_UNCERTAIN", Spec()));
        Add(_membershipComplete ? Open(CardinalTraversalComponentKind.Extent, "EXACT_CELL_EXTENT_PROVED", Spec())
            : Unknown(CardinalTraversalComponentKind.Extent, "EXTENT_UNCERTAIN", Spec()));
        Add(_censusShapeComplete ? Open(CardinalTraversalComponentKind.Census, "TERMINAL_CENSUS_COMPLETE", Serialized(),
            [new("TERMINAL_RECORD_COUNT", TerminalRecordCount), new("RETAINED_RECORD_COUNT", Census.Count)])
            : Unknown(CardinalTraversalComponentKind.Census, "TERMINAL_CENSUS_UNCERTAIN", Serialized()));

        // Full record evaluation and ALL missing fact gaps, not only a winning primary record reason.
        AssessObjectRecords(request.To, records, gaps);
        Add(_membershipComplete ? Open(CardinalTraversalComponentKind.SourceEdges, "SERIALIZED_SOURCE_EDGE_TYPES_ABSENT", Spec())
            : Unknown(CardinalTraversalComponentKind.SourceEdges, "SOURCE_EDGE_CENSUS_UNCERTAIN", Spec()));
        Add(_membershipComplete ? Open(CardinalTraversalComponentKind.DestinationOtherTypes, "SERIALIZED_OTHER_TYPES_ABSENT", Spec())
            : Unknown(CardinalTraversalComponentKind.DestinationOtherTypes, "OTHER_TYPE_CENSUS_UNCERTAIN", Spec()));
        Add(Open(CardinalTraversalComponentKind.SupplementalRegistry, "DECLARED_EMPTY_SUPPLEMENTAL_REGISTRY", Policy(), [new("REGISTRY_COUNT", 0)]));
        Add(NotApplicable(CardinalTraversalComponentKind.ActorSelf, "ACTOR_AT_SOURCE_ONLY", Policy()));
        Add(NotApplicable(CardinalTraversalComponentKind.RouteTrapFireWindow, "ROUTE_ONLY_RULES_NOT_APPLICABLE", Spec()));
        Add(NotApplicable(CardinalTraversalComponentKind.ScriptExecution, "SELECTED_HELPER_NO_SCRIPT_EXECUTION", Spec(),
            [new("TILE_SCRIPT_COUNT", TileScriptCount), new("SECTOR_SCRIPT_BYTES", SectorScriptBytes)]));

        var tileBlocker = components.Where(c => c.ComponentKind is CardinalTraversalComponentKind.DestinationMask or
            CardinalTraversalComponentKind.DestinationFacade or CardinalTraversalComponentKind.DestinationName)
            .Any(c => c.Disposition == CardinalTraversalDisposition.Blocked);
        // Reuse the existing destination-only production component, but never mistake its subset for closure.
        var diagonal = ((int)request.Direction & 1) == 0;
        var legs = diagonal ? AssessLegs(request) : Array.Empty<CardinalTraversalLegEvidence>();
        gaps.AddRange(legs.SelectMany(l => l.Gaps));
        var sceneryBlocker = _membershipComplete && (diagonal
            ? legs.Any(l => l.RecordAssessments.Any(r => r.Result.Disposition == CardinalTraversalDisposition.Blocked))
            : _static.GetDestinationForStep(request.From.X, request.From.Y, request.To.X, request.To.Y).Result.Disposition == StaticObstructionDisposition.Blocker);
        if (tileBlocker || sceneryBlocker)
            return new(request, InputIdentity, EvidenceEpoch, true, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked,
                tileBlocker ? "UNCONDITIONAL_TILE_BLOCKER" : diagonal ? "ADMITTED_ADJACENT_SCENERY_BLOCKER" : "ADMITTED_DESTINATION_SCENERY_BLOCKER", components, records, gaps, legs);
        if (gaps.Count != 0)
            return new(request, InputIdentity, EvidenceEpoch, true, CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE", components, records, gaps, legs);
        return new(request, InputIdentity, EvidenceEpoch, true, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open,
            "COMPLETE_NONBLOCKING_EVIDENCE", components, records, gaps, legs);
    }

    private void AssessObjectRecords(CardinalTraversalCell destination, List<CardinalTraversalRecordAssessment> records,
        List<CardinalTraversalGap> gaps, CardinalTraversalDirection? originalDirection = null)
    {
        foreach (var row in Census)
        {
            var provenance = RecordProvenance(row);
            var inputs = RecordInputs(row).ToList();
            if (originalDirection is { } direction) inputs.Add(new("ORIGINAL_DIRECTION", (int)direction));
            var missing = new List<string>();
            if (row.ObjectType != 3) missing.Add("TYPE_UNADMITTED");
            if (row.ObjectType == 3 && row.EffectiveFlags is null) missing.Add("FLAGS_UNRESOLVED");
            if (row.ObjectType == 3 && row.EffectiveFlags is uint state && (state & 0x2000) != 0) missing.Add("DYNAMIC_UNADMITTED");
            if (row.LocationStatus != CardinalTraversalLocationStatus.InRange)
                missing.Add(row.LocationStatus switch
                {
                    CardinalTraversalLocationStatus.Absent => "DIRECT_LOCATION_ABSENT",
                    CardinalTraversalLocationStatus.Overflow => "DIRECT_LOCATION_OVERFLOW",
                    _ => "DIRECT_LOCATION_OUT_OF_RANGE"
                });
            var matching = _recordsByIndex.TryGetValue(row.SourceIndex, out var matches) ? matches : [];
            if (matching.Length != 1 || (row.ObjectType == 3 &&
                (matching[0].EffectiveFlags != row.EffectiveFlags || matching[0].FlagsSource != row.FlagsSource ||
                 matching[0].Result.Supportedness != StaticObstructionSupportedness.Supported)))
                missing.Add("STATIC_CENSUS_AGREEMENT_UNCERTAIN");
            CardinalTraversalComponentAssessment result;
            CardinalTraversalApplicability registration;
            if (missing.Count != 0)
            {
                registration = CardinalTraversalApplicability.Unknown;
                result = Unknown(CardinalTraversalComponentKind.Census, missing[0], provenance, inputs);
                foreach (var reason in missing)
                    gaps.Add(new(Unknown(CardinalTraversalComponentKind.Census, reason, provenance, inputs), row.SourceIndex));
            }
            else
            {
                var ordinary = matching[0];
                var exclusion = ordinary.Result.Applicability == StaticObstructionApplicability.NotApplicable;
                registration = exclusion ? CardinalTraversalApplicability.NotApplicable : CardinalTraversalApplicability.Applicable;
                if (exclusion)
                    result = NotApplicable(CardinalTraversalComponentKind.Census, ordinary.Result.ReasonCode, provenance, inputs);
                else if (row.LocalCell != destination)
                    result = NotApplicable(CardinalTraversalComponentKind.Census, "RECORD_NOT_AT_DESTINATION", provenance, inputs);
                else
                    result = new(CardinalTraversalComponentKind.Census, CardinalTraversalApplicability.Applicable, CardinalTraversalCoverage.Supported,
                        ordinary.Result.Disposition == StaticObstructionDisposition.Blocker ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open,
                        ordinary.Result.ReasonCode, inputs, provenance);
            }
            records.Add(new(row, registration, result));
        }
    }

    private CardinalTraversalLegEvidence[] AssessLegs(CardinalTraversalRequest request)
    {
        var hx = new CardinalTraversalCell(request.To.X, request.From.Y);
        var hy = new CardinalTraversalCell(request.From.X, request.To.Y);
        var x = request.To.X < request.From.X ? CardinalTraversalDirection.XMinus : CardinalTraversalDirection.XPlus;
        var y = request.To.Y < request.From.Y ? CardinalTraversalDirection.YMinus : CardinalTraversalDirection.YPlus;
        CardinalTraversalLegEvidence Leg(CardinalTraversalLegRole role, CardinalTraversalCell from, CardinalTraversalCell to,
            CardinalTraversalDirection direction)
        {
            var components = new List<CardinalTraversalComponentAssessment>();
            var records = new List<CardinalTraversalRecordAssessment>();
            var gaps = new List<CardinalTraversalGap>();
            void Closure(CardinalTraversalComponentKind kind, string proved, string missing, bool complete)
            {
                var inputs = new[] { new CardinalTraversalNumericInput("ORIGINAL_DIRECTION", (int)request.Direction) };
                var c = complete ? Open(kind, proved, Spec(), inputs) : Unknown(kind, missing, Spec(), inputs);
                components.Add(c); if (!complete) gaps.Add(new(c));
            }
            Closure(CardinalTraversalComponentKind.Registration, "EXACT_CELL_REGISTRATION_PROVED", "REGISTRATION_UNCERTAIN", _membershipComplete);
            Closure(CardinalTraversalComponentKind.Extent, "EXACT_CELL_EXTENT_PROVED", "EXTENT_UNCERTAIN", _membershipComplete);
            Closure(CardinalTraversalComponentKind.Census, "TERMINAL_CENSUS_COMPLETE", "TERMINAL_CENSUS_UNCERTAIN", _censusShapeComplete);
            Closure(CardinalTraversalComponentKind.SourceEdges, "SERIALIZED_SOURCE_EDGE_TYPES_ABSENT", "SOURCE_EDGE_CENSUS_UNCERTAIN", _membershipComplete);
            Closure(CardinalTraversalComponentKind.DestinationOtherTypes, "SERIALIZED_OTHER_TYPES_ABSENT", "OTHER_TYPE_CENSUS_UNCERTAIN", _membershipComplete);
            AssessObjectRecords(to, records, gaps, request.Direction);
            return new(role, from, to, direction, request.Direction, components, records, gaps);
        }
        // Both paths must satisfy the object predicate; no shoulder terrain is queried.
        return [Leg(CardinalTraversalLegRole.XShoulder, request.From, hx, x), Leg(CardinalTraversalLegRole.XFinal, hx, request.To, y),
            Leg(CardinalTraversalLegRole.YShoulder, request.From, hy, y), Leg(CardinalTraversalLegRole.YFinal, hy, request.To, x)];
    }

    private string? Validate(CardinalTraversalRequest r)
    {
        if (r.ProfileId != ProfileId) return "PROFILE_UNSUPPORTED";
        if (!InputIdentity.Same(r.InputIdentity)) return "INPUT_IDENTITY_MISMATCH";
        if (r.EvidenceEpoch != EvidenceEpoch) return "EPOCH_MISMATCH";
        if (r.SectorX != 1452 || r.SectorY != 1290) return "SECTOR_UNSUPPORTED";
        if (!Bounds(r.From) || !Bounds(r.To))
        {
            // A bounded adjacent escape differs from arbitrary invalid coordinates. Never wrap arithmetic.
            if (Bounds(r.From) && Math.Abs((long)r.To.X - r.From.X) <= 1 && Math.Abs((long)r.To.Y - r.From.Y) <= 1)
                return "CROSS_SECTOR_DATA_ABSENT";
            return "COORDINATE_OUT_OF_RANGE";
        }
        if (!Zone(r.From) || !Zone(r.To)) return "ZONE_UNSUPPORTED";
        var dx = (long)r.To.X - r.From.X; var dy = (long)r.To.Y - r.From.Y;
        if (dx == 0 && dy == 0) return "SAME_CELL_UNSUPPORTED";
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return "NONADJACENT_UNSUPPORTED";
        var deltas = new (int X, int Y)[] { (-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1) };
        if ((uint)r.Direction >= 8) return "DIRECTION_UNSUPPORTED";
        if (dx != deltas[(int)r.Direction].X || dy != deltas[(int)r.Direction].Y) return "DIRECTION_MISMATCH";
        var c = r.Context;
        if (c.ContextId != CardinalTraversalRules.ContextId || c.ActorKind != 15 || c.CurrentArtClass != 2 ||
            c.SpellFlags != 0 || (c.PathFlags != 0 && c.PathFlags != 0x1000) || c.Polymorph || c.Editor ||
            c.ActorState != CardinalTraversalActorState.OrdinaryGround || c.SupplementalRegistryCount != 0) return "CONTEXT_UNSUPPORTED";
        return null;
    }
    private static bool Bounds(CardinalTraversalCell c) => (uint)c.X < 64 && (uint)c.Y < 64;
    private static bool Zone(CardinalTraversalCell c) => c.X is >= 4 and <= 8 && c.Y is >= 0 and <= 3;

    private CardinalTraversalComponentAssessment Terrain(CardinalTraversalComponentKind kind, CardinalTerrainCell cell,
        TerrainObstructionComponentResult result, bool source = false)
    {
        var p = result.Provenance;
        var provenance = new[] { new CardinalTraversalProvenance(p.Kind switch
        {
            TerrainObstructionProvenanceKind.SerializedSector => CardinalTraversalProvenanceKind.SerializedSector,
            TerrainObstructionProvenanceKind.PackedTerrainId => CardinalTraversalProvenanceKind.PackedTerrainId,
            TerrainObstructionProvenanceKind.TileNameMes => CardinalTraversalProvenanceKind.SelectedTileName,
            _ => CardinalTraversalProvenanceKind.ProjectPolicy
        }, p.SourceId, p.EntryOrdinal) };
        var inputs = new List<CardinalTraversalNumericInput>
        {
            new("ORDINAL", cell.Components.Ordinal), new("RAW_TERRAIN", cell.Raw), new("PACKED_CLASS", cell.Raw >> 28)
        };
        if (kind is CardinalTraversalComponentKind.SourceMask or CardinalTraversalComponentKind.DestinationMask)
        {
            inputs.Add(new("MASK_WORD_INDEX", cell.Components.Ordinal / 32)); inputs.Add(new("MASK_BIT", cell.Components.Ordinal % 32));
            if (cell.MaskWord is uint word) { inputs.Add(new("MASK_WORD", word)); inputs.Add(new("MASK_VALUE", (word >> (cell.Components.Ordinal % 32)) & 1)); }
        }
        if (kind is CardinalTraversalComponentKind.SourceName or CardinalTraversalComponentKind.DestinationName)
        {
            if (cell.CategoryBits is int bits) inputs.Add(new("CATEGORY_BITS", bits));
            if (cell.NameCategoryIndex is int category) inputs.Add(new("NAME_CATEGORY_INDEX", category));
            if (cell.MaterialOrdinal is int material) inputs.Add(new("MATERIAL_ORDINAL", material));
            if (cell.MesKey is int key) inputs.Add(new("MES_KEY", key));
        }
        if (source) return NotApplicable(kind, "SOURCE_TILE_NOT_QUERIED", provenance, inputs);
        // Unhandled in-profile component Unsupported becomes a required Unknown, not request rejection.
        return new(kind, (CardinalTraversalApplicability)result.Applicability,
            result.Supportedness == TerrainObstructionSupportedness.Supported ? CardinalTraversalCoverage.Supported : CardinalTraversalCoverage.Unknown,
            result.Disposition.HasValue ? result.Disposition == TerrainObstructionDisposition.Blocked ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open : null,
            result.ReasonCode, inputs, provenance);
    }
    private IEnumerable<CardinalTraversalProvenance> Serialized() =>
        [new(CardinalTraversalProvenanceKind.SerializedSector, InputIdentity.ModuleSourceId, InputIdentity.Sector.EntryOrdinal)];
    private static IEnumerable<CardinalTraversalProvenance> Spec() =>
        [new(CardinalTraversalProvenanceKind.PinnedSpecification, CardinalTraversalRules.SpecificationId, specificationId: CardinalTraversalRules.SpecificationId)];
    private static IEnumerable<CardinalTraversalProvenance> Policy() =>
        [new(CardinalTraversalProvenanceKind.ProjectPolicy, CardinalTraversalRules.ContextId)];
    private IEnumerable<CardinalTraversalProvenance> RecordProvenance(CardinalTraversalCensusRecord row)
    {
        var result = new List<CardinalTraversalProvenance>
        {
            new(CardinalTraversalProvenanceKind.SerializedInstance, InputIdentity.ModuleSourceId, InputIdentity.Sector.EntryOrdinal, row.SourceIndex),
            new(CardinalTraversalProvenanceKind.PinnedSpecification, CardinalTraversalRules.SpecificationId, specificationId: CardinalTraversalRules.SpecificationId)
        };
        if (row.FlagsSource == StaticObstructionFlagsSource.Prototype)
            result.Add(new(CardinalTraversalProvenanceKind.PrototypeDefault, InputIdentity.PrototypeSourceId, prototypeAid: row.PrototypeAid));
        if (InputIdentity.OriginalIdentity is not null)
            result.Add(new(CardinalTraversalProvenanceKind.OriginalInputIdentity, InputIdentity.OriginalIdentity.Module.Sha256, InputIdentity.Sector.EntryOrdinal));
        return result;
    }
    private static IEnumerable<CardinalTraversalNumericInput> RecordInputs(CardinalTraversalCensusRecord r)
    {
        var values = new List<CardinalTraversalNumericInput> { new("SOURCE_INDEX", r.SourceIndex), new("OBJECT_TYPE", r.ObjectType) };
        if (r.DirectFlags is uint direct) values.Add(new("DIRECT_FLAGS", direct));
        if (r.EffectiveFlags is uint flags) values.Add(new("EFFECTIVE_FLAGS", flags));
        return values;
    }
    internal static CardinalTraversalComponentAssessment Open(CardinalTraversalComponentKind kind, string reason,
        IEnumerable<CardinalTraversalProvenance> p, IEnumerable<CardinalTraversalNumericInput>? inputs = null) =>
        new(kind, CardinalTraversalApplicability.Applicable, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, reason, inputs ?? [], p);
    internal static CardinalTraversalComponentAssessment NotApplicable(CardinalTraversalComponentKind kind, string reason,
        IEnumerable<CardinalTraversalProvenance> p, IEnumerable<CardinalTraversalNumericInput>? inputs = null) =>
        new(kind, CardinalTraversalApplicability.NotApplicable, CardinalTraversalCoverage.Supported, null, reason, inputs ?? [], p);
    internal static CardinalTraversalComponentAssessment Unknown(CardinalTraversalComponentKind kind, string reason,
        IEnumerable<CardinalTraversalProvenance> p, IEnumerable<CardinalTraversalNumericInput>? inputs = null) =>
        new(kind, CardinalTraversalApplicability.Unknown, CardinalTraversalCoverage.Unknown, null, reason, inputs ?? [], p);
}
