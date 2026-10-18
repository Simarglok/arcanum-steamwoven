using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Terrain;

namespace Arcanum.CrashSiteProbe;

// Research-only, independently authored. No production admission API or second wire parser.
internal static partial class Program
{
    private const string AreaTraversalSchema = "AREA_TRAVERSAL_RESEARCH_V1";
    private const string AreaTraversalRule = "S1_NORMAL_GROUND_FILE_FRONTIER_V1";
    private const string AreaTraversalPin = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52";
    private readonly record struct ResearchCell(int Map, long X, long Y);
    private sealed record ResearchActor(string Caller, string Chart, string Id, int Kind, uint Art, uint SpellFlags,
        bool Ground, string StateMode, string Classification, string Profile, string RegistryPolicy, string StateToken, bool RegistryClosed = false);
    private sealed record ResearchScope(long Packed, AreaScope Scope);
    private sealed record ResearchNameBinding(AreaEntryIdentity Entry, string PayloadSha256);
    private sealed record ResearchSelection(int CatalogueMap, int RuntimeMap, string Profile, IReadOnlyList<AreaInputIdentity> Inputs,
        IReadOnlyList<AreaSectorRecord> Sectors, IReadOnlyList<AreaObjectRecord> Objects, IReadOnlyList<AreaTerrainRecord> Terrain,
        IReadOnlyList<AreaVisualRecord> Visuals, AreaReconciliation Reconciliation, IReadOnlyList<ResearchScope> AuthorizedScope);
    private static ResearchSelection Selection(AreaSnapshot s) => new(s.CatalogueMap, s.RuntimeMap, s.Profile, s.Inputs, s.Sectors,
        s.Objects, s.Terrain, s.Visuals, s.Reconciliation, Frozen(s.Sectors.Select(x => new ResearchScope(x.Key.Packed, x.Scope))));
    private static ResearchBinding Bind(ResearchSelection s, ResearchActor a, IReadOnlyList<ResearchComponent> components,
        ResearchNameBinding? name = null, string inputKind = "Structural", long generation = 1) =>
        new(AreaTraversalSchema, AreaTraversalRule, AreaTraversalPin, inputKind, s.RuntimeMap, s.Profile,
            CanonicalDigest(SelectedArray(s, components, name, inputKind)), CanonicalDigest(ActorArray(a)), generation);
    private sealed record ResearchBinding(string Schema, string Rule, string SourcePin, string InputKind, int Map,
        string Profile, string SelectedDigest, string ActorDigest, long Generation);
    private sealed record ResearchComponent(ResearchCell Cell, string Kind, string Coverage, string Applicability,
        string? Disposition, string Reason, string ProvenanceKind, string SourceId, int? Ordinal, string Classification = "InputObserved", uint? RawArt = null);
    private sealed record ResearchGap(string Code, ResearchCell? Cell, AreaObjectKey? Object, int? Slot, string Dependency, string NextPrerequisite, string Classification = "Unknown");
    private sealed record ResearchRecordRow(string Role, AreaObjectKey Key, string Coverage, string Applicability,
        string? Disposition, string Reason, string Classification);
    private sealed record ResearchLeg(string Role, ResearchCell From, ResearchCell To, string Direction,
        string OriginalDirection, IReadOnlyList<ResearchRecordRow> Records, IReadOnlyList<ResearchGap> Gaps);
    private sealed record ResearchQuery(ResearchBinding Binding, ResearchCell From, ResearchCell To, string Direction, string Scope);
    private sealed record ResearchResult(ResearchQuery Query, string Coverage, string? Disposition, string Reason,
        bool CompleteCoverage, string Readiness, IReadOnlyList<string> Preconditions, IReadOnlyList<ResearchComponent> Components,
        IReadOnlyList<ResearchRecordRow> Records, IReadOnlyList<ResearchLeg> Legs, IReadOnlyList<ResearchGap> Gaps);

    private static IReadOnlyList<T> Frozen<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
    private static bool IsResearchSha256(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static object?[] CellArray(ResearchCell c) => [c.Map, c.X, c.Y];
    private static object?[] ObjectKeyArray(AreaObjectKey k) => [k.Kind.ToString(), k.RuntimeMap, k.Sector, k.SourceIndex, k.Offset, k.SourceSha256, k.ObjectIdSha256, k.Length];
    private static object?[] VisualKeyArray(AreaVisualKey k) => [k.Kind.ToString(), k.Sector.RuntimeMap, k.Sector.Packed, k.Sector.SourceSha256,
        k.SourceIndex, k.ObjectIdSha256, k.Layer.ToString(), k.SparseIndex, k.Object is { } o ? ObjectKeyArray(o) : null];
    private static object?[] EntryArray(AreaEntryIdentity e) => [e.Source.Role.ToString(), e.Source.Number, e.Ordinal, e.Flags, e.StoredBytes, e.DecodedBytes, e.Offset, e.NameSha256];
    private static object?[] FieldArray(AreaFieldValue f) => [f.Slot, f.Presence.ToString(), f.Origin.ToString(), f.Number,
        f.Elements.OrderBy(e => e.Key).Select(e => new object?[] { e.Key, e.Words.Select(w => (object?)checked((uint)w)).ToArray() }).ToArray(), f.Fingerprint];
    private static object?[] OccurrenceArray(AreaObjectRecord o) => [ObjectKeyArray(o.Key), o.Version, o.Type, o.PrototypeNumber,
        o.PrototypeReferenceSha256, o.PrototypeOwnSha256, o.RawSha256, o.SemanticSha256, o.Scope.ToString(), o.SerializedLocation,
        o.WorldX, o.WorldY, o.Disposition.ToString(), o.DirectFields.OrderBy(f => f.Slot).Select(FieldArray).ToArray(),
        o.EffectiveFields.OrderBy(f => f.Slot).Select(FieldArray).ToArray(), o.SchemaReady, o.PlacementReady, o.RequiredLayersReady];
    private static object?[] ComponentArray(ResearchComponent c) => [CellArray(c.Cell), c.Kind, c.Coverage, c.Applicability, c.Disposition,
        c.Reason, c.ProvenanceKind, c.SourceId, c.Ordinal, c.Classification, c.RawArt];
    private static object?[] ActorArray(ResearchActor a) => [AreaTraversalSchema, "ACTOR_CONTEXT", 1, a.Caller, a.Chart, a.Id,
        a.Kind, a.Art, a.SpellFlags, a.Ground, a.StateMode, a.Classification, a.Profile, a.RegistryPolicy, a.StateToken, a.RegistryClosed];
    private static object?[] BindingArray(ResearchBinding b) => [b.Schema, b.Rule, b.SourcePin, b.InputKind, b.Map, b.Profile, b.SelectedDigest, b.ActorDigest, b.Generation];
    private static object?[] GapArray(ResearchGap g) => [g.Code, g.Cell is { } c ? CellArray(c) : null, g.Object is { } o ? ObjectKeyArray(o) : null, g.Slot, g.Dependency, g.NextPrerequisite, g.Classification];
    private static object?[] RecordRowArray(ResearchRecordRow r) => [r.Role, ObjectKeyArray(r.Key), r.Coverage, r.Applicability, r.Disposition, r.Reason, r.Classification];
    private static object?[] ResultArray(ResearchResult r) => [new object?[] { BindingArray(r.Query.Binding), CellArray(r.Query.From), CellArray(r.Query.To), r.Query.Direction, r.Query.Scope },
        r.Coverage, r.Disposition, r.Reason, r.CompleteCoverage, r.Readiness, r.Preconditions.ToArray(), r.Components.Select(ComponentArray).ToArray(), r.Records.Select(RecordRowArray).ToArray(),
        r.Legs.Select(l => new object?[] { l.Role, CellArray(l.From), CellArray(l.To), l.Direction, l.OriginalDirection, l.Records.Select(RecordRowArray).ToArray(), l.Gaps.Select(GapArray).ToArray() }).ToArray(), r.Gaps.Select(GapArray).ToArray()];

    // Not JsonSerializer: a deliberately restricted grammar and explicit integer widths.
    private static string Canonical(object? value)
    {
        var b = new StringBuilder(); Write(value); return b.ToString();
        void Write(object? v)
        {
            switch (v)
            {
                case null: b.Append("null"); break;
                case bool flag: b.Append(flag ? "true" : "false"); break;
                case string text:
                    if (text.Any(c => c is < ' ' or > '~')) throw new InvalidDataException("CANONICAL_ASCII");
                    b.Append('"'); foreach (var c in text) { if (c is '"' or '\\') b.Append('\\'); b.Append(c); } b.Append('"'); break;
                case int or uint or long: b.Append(((IFormattable)v).ToString(null, CultureInfo.InvariantCulture)); break;
                case Array array:
                    b.Append('['); for (var i = 0; i < array.Length; i++) { if (i != 0) b.Append(','); Write(array.GetValue(i)); } b.Append(']'); break;
                default: throw new InvalidDataException("CANONICAL_TYPE");
            }
        }
    }
    private static string CanonicalDigest(object? value) => AreaHash(Encoding.UTF8.GetBytes(Canonical(value)));
    private static IEnumerable<AreaObjectRecord> OrderedObjects(IEnumerable<AreaObjectRecord> objects) => objects
        .OrderBy(o => o.Key.Kind.ToString(), StringComparer.Ordinal).ThenBy(o => o.Key.RuntimeMap).ThenBy(o => o.Key.Sector)
        .ThenBy(o => o.Key.SourceIndex).ThenBy(o => o.Key.Offset).ThenBy(o => o.Key.SourceSha256, StringComparer.Ordinal)
        .ThenBy(o => o.Key.ObjectIdSha256, StringComparer.Ordinal).ThenBy(o => o.Key.Length);
    private static AreaObjectRecord CopyOccurrence(AreaObjectRecord o) => o with
    {
        DirectFields = CopyFields(o.DirectFields), EffectiveFields = CopyFields(o.EffectiveFields)
    };
    private static IReadOnlyList<AreaFieldValue> CopyFields(IEnumerable<AreaFieldValue> fields) => Frozen(fields.Select(f => f with
    { Elements = Frozen(f.Elements.Select(e => e with { Words = Frozen(e.Words) })) }));

    private sealed class ResearchEvidence
    {
        internal ResearchBinding Binding { get; }
        internal ResearchActor Actor { get; }
        internal IReadOnlyList<AreaObjectRecord> Objects { get; }
        private readonly Dictionary<long, AreaSectorRecord> _sectors;
        private readonly Dictionary<long, AreaScope> _scope;
        private readonly Dictionary<ResearchCell, ResearchComponent[]> _components;
        // Synthetic declarations are explicit. Original FILE snapshots never close loaded registry.
        private readonly bool _registryClosed;
        internal ResearchEvidence(ResearchBinding binding, ResearchActor actor, ResearchSelection selection,
            IEnumerable<ResearchComponent> components, ResearchNameBinding? name = null)
        {
            if (binding.Generation < 0 || binding.Schema != AreaTraversalSchema || binding.Rule != AreaTraversalRule || binding.SourcePin != AreaTraversalPin ||
                binding.ActorDigest != CanonicalDigest(ActorArray(actor)) || binding.Profile != actor.Profile || binding.Map < 0 ||
                binding.InputKind is not ("Structural" or "AuthenticatedOriginal") || !IsResearchSha256(binding.SelectedDigest) || (actor.RegistryClosed && binding.InputKind != "Structural") ||
                selection.RuntimeMap != binding.Map || selection.Profile != binding.Profile)
                throw new InvalidDataException("EVIDENCE_BINDING");
            if (actor.Caller != "sub_425760" || actor.Chart != "WORLD_CELL_CENTERS_V1" || actor.Kind != 15 || actor.Art >> 28 != 2 || actor.SpellFlags != 0 || !actor.Ground ||
                actor.StateMode != "DeclaredDiagnosticOverlay" || actor.Classification != "ProjectDeclared" || actor.RegistryPolicy != "NONEDITOR_EXACT_TILE" || string.IsNullOrEmpty(actor.StateToken))
                throw new InvalidDataException("ACTOR_APPLICABILITY");
            Binding = binding; Actor = actor; _registryClosed = actor.RegistryClosed;
            var owned = selection with { Inputs = Frozen(selection.Inputs),
                Sectors = Frozen(selection.Sectors.Select(s => s with { Sections = Frozen(s.Sections), Lights = Frozen(s.Lights.Select(Frozen)) })),
                Objects = Frozen(OrderedObjects(selection.Objects).Select(CopyOccurrence)), Terrain = Frozen(selection.Terrain),
                Visuals = Frozen(selection.Visuals), AuthorizedScope = Frozen(selection.AuthorizedScope) };
            _scope = owned.AuthorizedScope.ToDictionary(s => s.Packed, s => s.Scope);
            foreach (var s in owned.AuthorizedScope)
                if (s.Scope is not (AreaScope.Core or AreaScope.Context) || AreaSectorCoordinate.Unpack(s.Packed).Packed != s.Packed)
                    throw new InvalidDataException("SCOPE_BINDING");
            _sectors = owned.Sectors.ToDictionary(s => s.Key.Packed);
            foreach (var s in _sectors.Values)
                if (s.Key.RuntimeMap != binding.Map || AreaSectorCoordinate.Unpack(s.Key.Packed) != s.Coordinate || !_scope.TryGetValue(s.Key.Packed, out var scope) || scope != s.Scope)
                    throw new InvalidDataException("SECTOR_BINDING");
            Objects = owned.Objects;
            if (Objects.Select(o => o.Key).Distinct().Count() != Objects.Count || Objects.Any(o => o.Key.RuntimeMap != binding.Map || o.Type is < 0 or > 17 || !Enum.IsDefined(o.Key.Kind) ||
                o.Key.SourceIndex < 0 || o.Key.Offset < 0 || o.Key.Length <= 0 || !IsResearchSha256(o.Key.SourceSha256) || !IsResearchSha256(o.Key.ObjectIdSha256)))
                throw new InvalidDataException("OCCURRENCE_BINDING");
            foreach (var o in Objects)
            {
                if (o.Key.Kind is not (AreaSourceKind.Static or AreaSourceKind.Mobile) || !Enum.IsDefined(o.Scope) || !Enum.IsDefined(o.Disposition) ||
                    !o.PlacementReady || o.SerializedLocation is not { } location || o.WorldX is not { } x || o.WorldY is not { } y ||
                    x is < 0 or > 4294967295L || y is < 0 or > 4294967295L)
                    throw new InvalidDataException("OCCURRENCE_PLACEMENT");
                if (o.Key.Kind == AreaSourceKind.Static)
                {
                    if (o.Key.Sector is not { } packed || !_sectors.TryGetValue(packed, out var sector) || !sector.Present ||
                        sector.Scope != o.Scope || sector.Key.SourceSha256 != o.Key.SourceSha256 ||
                        x != checked(sector.Coordinate.OriginX + (location & 63)) || y != checked(sector.Coordinate.OriginY + ((location >> 32) & 63)))
                        throw new InvalidDataException("STATIC_PLACEMENT_BINDING");
                }
                else if (o.Key.Sector is not null || x != unchecked((int)location) || y != unchecked((int)(location >> 32)) ||
                    o.Scope != Scope(new(binding.Map, x, y))) throw new InvalidDataException("MOBILE_PLACEMENT_BINDING");
                foreach (var fields in new[] { o.DirectFields, o.EffectiveFields })
                {
                    if (fields.Select(f => f.Slot).Distinct().Count() != fields.Count || fields.Any(f => f.Slot < 0 || !Enum.IsDefined(f.Presence) || !Enum.IsDefined(f.Origin)))
                        throw new InvalidDataException("FIELD_SHAPE");
                    foreach (var f in fields)
                        if (f.Elements.Any(e => e.Key < 0 || e.Words.Any(w => w is < 0 or > uint.MaxValue)) || f.Elements.Select(e => e.Key).Distinct().Count() != f.Elements.Count ||
                            f.Presence is AreaPresence.Null or AreaPresence.Empty or AreaPresence.Absent && f.Number.HasValue ||
                            f.Presence != AreaPresence.Array && f.Elements.Count != 0 ||
                            f.Slot is 0 or 18 or 19 && f.Number is { } scalar && scalar is < int.MinValue or > int.MaxValue)
                            throw new InvalidDataException("FIELD_SHAPE");
                }
            }
            var copiedComponents = components.ToArray();
            _components = copiedComponents.GroupBy(c => c.Cell).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var (cell, rows) in _components)
                if (cell.Map != binding.Map || Sector(cell) is not { Present: true } || rows.Length != 3 || rows.Select(r => r.Kind).Distinct().Count() != 3 ||
                    rows.Any(r => r.Kind is not ("Mask" or "Facade" or "Name") || r.Coverage is not ("Supported" or "Unknown" or "Unsupported") ||
                        r.Applicability is not ("Applicable" or "NotApplicable" or "Unknown") || r.Disposition is not (null or "NoBlock" or "Blocked") ||
                        (r.Coverage == "Supported" && r.Applicability == "Applicable") != (r.Disposition is not null))) throw new InvalidDataException("COMPONENT_BINDING");
            if (binding.SelectedDigest != CanonicalDigest(SelectedArray(owned, copiedComponents, name, binding.InputKind)))
                throw new InvalidDataException("SELECTED_CONTENT_BINDING");
        }
        private AreaSectorRecord? Sector(ResearchCell c)
        {
            if (c.X is < 0 or > 4294967295L || c.Y is < 0 or > 4294967295L) return null;
            var packed = new AreaSectorCoordinate(c.X / 64, c.Y / 64).Packed;
            return _sectors.GetValueOrDefault(packed);
        }
        private AreaScope Scope(ResearchCell c) => c.X is < 0 or > 4294967295L || c.Y is < 0 or > 4294967295L ? AreaScope.Outside :
            _scope.GetValueOrDefault(new AreaSectorCoordinate(c.X / 64, c.Y / 64).Packed, AreaScope.Outside);
        internal ResearchResult Assess(ResearchQuery q)
        {
            if (q.Binding != Binding || q.From.Map != Binding.Map || q.To.Map != Binding.Map || q.Scope is not ("CoreWithContextEvidence" or "Path0x1000Unavailable")) throw new InvalidDataException("QUERY_BINDING");
            var direction = Directions.FirstOrDefault(d => d.Name == q.Direction);
            if (direction.Name is null) throw new InvalidDataException("QUERY_DIRECTION");
            var dx = checked(q.To.X - q.From.X); var dy = checked(q.To.Y - q.From.Y);
            var components = new List<ResearchComponent>(); var records = new List<ResearchRecordRow>(); var legs = new List<ResearchLeg>(); var gaps = new List<ResearchGap>();
            if (dx != direction.X || dy != direction.Y) return Finish("Unsupported", null, "RELATION_UNSUPPORTED");
            if (q.From.X is < 0 or > 4294967295L || q.To.X is < 0 or > 4294967295L || q.From.Y is < 0 or > 4294967295L || q.To.Y is < 0 or > 4294967295L)
                return Finish("Unsupported", null, "WORLD_COORDINATE_UNSUPPORTED");
            var from = Sector(q.From); var to = Sector(q.To);
            if (Scope(q.From) != AreaScope.Core || Scope(q.To) is not (AreaScope.Core or AreaScope.Context))
                return Finish("Unsupported", null, "SCOPE_UNSUPPORTED");
            if (q.Scope == "Path0x1000Unavailable")
            {
                Gap("PATH_FLAG_0x1000_CALLER_UNAVAILABLE", q.From, null, null, "S11", "Supply separately selected path caller/state evidence; no S1 fallback");
                return Finish("Unknown", null, "PATH_BRANCH_UNAVAILABLE");
            }
            if (from is not { Present: true }) Gap("SOURCE_SECTOR_MISSING", q.From, null, null, "S10", "Supply identity-bound source sector");
            if (to is not { Present: true }) Gap("NEIGHBOR_SECTOR_MISSING", q.To, null, null, "S10", "Supply identity-bound neighbor sector");
            foreach (var o in Objects.Where(o => !o.SchemaReady))
                Gap("OCCURRENCE_SCHEMA_UNREADY", null, o.Key, null, "S6/S7/S8", "Close retained occurrence schema before completeness");
            if (_components.TryGetValue(q.To, out var terrain))
            {
                components.AddRange(terrain.OrderBy(c => c.Kind == "Mask" ? 0 : c.Kind == "Facade" ? 1 : 2));
                foreach (var c in terrain.Where(c => c.Coverage != "Supported" || c.Applicability == "Unknown")) Gap(c.Reason, q.To, null, null, "S3", "Resolve selected mask/name/facade component");
            }
            else Gap("TERRAIN_COMPONENTS_MISSING", q.To, null, null, "S3", "Inspect identity-bound destination sector components");
            if (dx != 0 && dy != 0)
            {
                var index = Array.FindIndex(Directions, d => d.Name == q.Direction);
                var ccw = Directions[(index + 7) % 8]; var cw = Directions[(index + 1) % 8];
                var a = new ResearchCell(Binding.Map, checked(q.From.X + ccw.X), checked(q.From.Y + ccw.Y));
                var b = new ResearchCell(Binding.Map, checked(q.From.X + cw.X), checked(q.From.Y + cw.Y));
                Leg("SourceToCcw", q.From, a, ccw.Name); Leg("CcwToFinal", a, q.To, cw.Name);
                Leg("SourceToCw", q.From, b, cw.Name); Leg("CwToFinal", b, q.To, ccw.Name);
            }
            else Leg("Adjacent", q.From, q.To, q.Direction);
            var terrainBlock = components.Any(c => c.Coverage == "Supported" && c.Applicability == "Applicable" && c.Disposition == "Blocked");
            if (terrainBlock) return Finish("Supported", "Blocked", "FINAL_TERRAIN_BLOCKED");
            if (gaps.Count != 0) return Finish("Unknown", null, "DEPENDENCY_FRONTIER");
            if (records.Any(r => r.Disposition == "Blocked")) return Finish("Supported", "Blocked", "REGISTERED_OBJECT_BLOCKED");
            return Finish("Supported", "Open", "ALL_APPLICABLE_DEPENDENCIES_CLOSED");

            void Gap(string code, ResearchCell? cell, AreaObjectKey? key, int? slot, string dependency, string next) => gaps.Add(new(code, cell, key, slot, dependency, next));
            void Leg(string role, ResearchCell start, ResearchCell end, string dir)
            {
                var startGap = gaps.Count; var startRecord = records.Count;
                foreach (var cell in new[] { start, end })
                {
                    var sector = Sector(cell);
                    if (sector is null || !sector.Present) Gap("LEG_SECTOR_MISSING", cell, null, null, "S5/S10", "Supply identity-bound shoulder/leg sector");
                    if (!_registryClosed) Gap("POST_LOAD_REGISTRY_UNOBSERVED", cell, null, null, "S6/S7/S8", "Observe complete exact-tile list after selected-source load/fold hooks");
                }
                foreach (var (cell, source) in new[] { (start, true), (end, false) })
                    foreach (var o in Objects.Where(o => o.WorldX == cell.X && o.WorldY == cell.Y))
                    {
                        var r = Classify(o, role + (source ? ":Source" : ":Destination"), source, dir, q.Direction, _registryClosed);
                        records.Add(r);
                        if (r.Coverage != "Supported") Gap(r.Reason, cell, o.Key, r.Reason == "FLAGS_UNRESOLVED" ? 18 : r.Reason == "CURRENT_ART_UNRESOLVED" ? 0 : null,
                            o.Type is 15 or 16 ? "S9 object_hp_current/stat/effect" : "S6/S7/S8", o.Type is 15 or 16 ? "Observe current HP with derived stat/effect/load closure" : "Close named field/registration/state dependency");
                    }
                legs.Add(new(role, start, end, dir, q.Direction, Frozen(records.Skip(startRecord)), Frozen(gaps.Skip(startGap))));
            }
            ResearchResult Finish(string coverage, string? disposition, string reason) => new(q, coverage, disposition, reason,
                coverage == "Supported" && gaps.Count == 0, gaps.Count == 0 && coverage == "Supported" ? "RESEARCH_CLOSED" : "NOT_READY",
                Frozen(new[] { "S1_NORMAL_GROUND_ONLY", "NO_LIVE_ADMISSION", Actor.Classification }), Frozen(components), Frozen(records), Frozen(legs), Frozen(gaps));
        }
    }
    private static readonly (string Name, int X, int Y)[] Directions =
    [
        ("XMinusYMinus", -1, -1), ("XMinus", -1, 0), ("XMinusYPlus", -1, 1), ("YPlus", 0, 1),
        ("XPlusYPlus", 1, 1), ("XPlus", 1, 0), ("XPlusYMinus", 1, -1), ("YMinus", 0, -1)
    ];
    private static long? Scalar(AreaObjectRecord o, int slot) => o.EffectiveFields.SingleOrDefault(f => f.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } ? n : null;
    private static ResearchRecordRow Classify(AreaObjectRecord o, string role, bool source, string direction, string originalDirection, bool registryClosed)
    {
        string coverage = "Supported", applicability = "Applicable", reason; string? disposition = null;
        if (source && o.Type is not (0 or 1) || !source && o.Type is >= 5 and <= 14) { applicability = "NotApplicable"; reason = "TYPE_MASK_EXCLUDED"; }
        else if (Scalar(o, 18) is not { } flags) { coverage = "Unknown"; reason = "FLAGS_UNRESOLVED"; }
        else if ((flags & 0x1000) != 0) { applicability = "NotApplicable"; reason = "INVENTORY_INSERTION_REFUSED"; }
        else if ((flags & 3) != 0) { applicability = "NotApplicable"; reason = "NONEDITOR_DESTROYED_OFF"; }
        else if (o.Type is 15 or 16) { coverage = "Unknown"; reason = "CURRENT_HP_STAT_EFFECT_UNOBSERVED"; }
        else if (o.Type is 0 or 1)
        {
            if (Scalar(o, 0) is not { } art) { coverage = "Unknown"; reason = "CURRENT_ART_UNRESOLVED"; }
            else
            {
                var raw = unchecked((uint)checked((int)art));
                var expectedClass = o.Type == 0 ? 1u : 3u;
                var rot = (int)((raw >> 11) & 7) | 1;
                var dir = Array.FindIndex(Directions, d => d.Name == direction);
                var original = Array.FindIndex(Directions, d => d.Name == originalDirection);
                var onEdge = source ? rot == dir : ((rot + 4) % 8) == dir;
                if (raw >> 28 != expectedClass) { coverage = "Unknown"; reason = "EDGE_ART_CLASS_UNSUPPORTED"; }
                else if (!onEdge) { applicability = "NotApplicable"; reason = "EDGE_ORIENTATION_EXCLUDED"; }
                else if (!registryClosed) { coverage = "Unknown"; reason = "POST_LOAD_REGISTRY_UNOBSERVED"; }
                else if (o.Type == 1) { coverage = "Unknown"; reason = "PORTAL_OPEN_SCRIPT_LOCK_UNOBSERVED"; }
                else
                {
                    var piece = (raw >> 14) & 63;
                    var window = piece is 10 or 13 or 14 or 17 or 18 or 19;
                    var opening = piece is 22 or 25 or 26 or 29 or 30 or 31 or 32;
                    if (opening || window && (original & 1) != 0) { disposition = "Open"; reason = "WALL_OPENING_OR_CARDINAL_JUMP"; }
                    else if (window) { disposition = "Blocked"; reason = "DIAGONAL_WINDOW"; }
                    else if (Scalar(o, 19) is not { } spell) { coverage = "Unknown"; reason = "WALL_PASSWALL_UNOBSERVED"; }
                    else { disposition = (spell & 0x100000) == 0 ? "Blocked" : "Open"; reason = "WALL_SPELL_STATE"; }
                }
            }
        }
        else if (!registryClosed) { coverage = "Unknown"; reason = "POST_LOAD_REGISTRY_UNOBSERVED"; }
        else { disposition = (flags & 0x400) == 0 ? "Blocked" : "Open"; reason = "DESTINATION_FLAGS"; }
        return new(role, o.Key, coverage, applicability, disposition, reason, "InputObserved");
    }

    private static ResearchComponent CopyComponent(ResearchCell cell, string kind, TerrainObstructionComponentResult r, uint rawArt) =>
        new(cell, kind, r.Supportedness.ToString(), r.Applicability.ToString(), r.Disposition?.ToString(),
            r.ReasonCode, r.Provenance.Kind.ToString(), r.Provenance.SourceId, r.Provenance.EntryOrdinal, "InputObserved", rawArt);
    private static ResearchActor DeclaredAreaActor(string profile) => new("sub_425760", "WORLD_CELL_CENTERS_V1", "HUMAN_FIXED_DIAGNOSTIC", 15, 0x20000000, 0,
        true, "DeclaredDiagnosticOverlay", "ProjectDeclared", profile, "NONEDITOR_EXACT_TILE", "FILE_EFFECTIVE_INITIAL_NO_LOADED_OBSERVATION_V1");
    private static object?[] SelectedArray(ResearchSelection s, IReadOnlyList<ResearchComponent> components, ResearchNameBinding? nameBinding, string inputKind)
    {
        var r = s.Reconciliation;
        return [AreaTraversalSchema, "SELECTED_EVIDENCE", 1, AreaTraversalRule, AreaTraversalPin, inputKind, s.CatalogueMap, s.RuntimeMap, s.Profile,
            s.Inputs.OrderBy(i => i.Key.Role.ToString(), StringComparer.Ordinal).ThenBy(i => i.Key.Number).Select(i => new object?[] { i.Key.Role.ToString(), i.Key.Number, i.Length, i.Sha256 }).ToArray(),
            s.Sectors.OrderBy(x => x.Key.RuntimeMap).ThenBy(x => x.Key.Packed).ThenBy(x => x.Key.SourceSha256, StringComparer.Ordinal).Select(x => new object?[] { x.Key.RuntimeMap, x.Key.Packed, x.Key.SourceSha256,
                x.Scope.ToString(), x.Present, x.DatOrdinal, x.Townmap, x.LightCount, x.RoofDiscriminator, x.Placeholder, x.TerminalCount,
                x.Sections.Select(p => new object?[] { p.Start, p.Length }).ToArray(), x.Lights.Select(l => l.ToArray()).ToArray(), x.Entry is { } e ? EntryArray(e) : null, x.PayloadSha256 }).ToArray(),
            OrderedObjects(s.Objects).Select(OccurrenceArray).ToArray(),
            s.Terrain.OrderBy(t => t.Key.Sector.Packed).ThenBy(t => t.Key.SourceIndex).Select(t => new object?[] { t.Key.Sector.RuntimeMap, t.Key.Sector.Packed, t.Key.Sector.SourceSha256, t.Key.SourceIndex,
                t.LocalX, t.LocalY, t.WorldX, t.WorldY, t.RawArt, t.LookupArt, t.Scope.ToString(), VisualKeyArray(t.SelectedVisual) }).ToArray(),
            s.Visuals.OrderBy(v => Canonical(VisualKeyArray(v.Key)), StringComparer.Ordinal).Select(v => new object?[] { VisualKeyArray(v.Key), v.RawArt, v.LookupArt, EntryArray(v.Entry), v.PayloadSha256,
                v.ViewLength, v.NominalFacing, v.SourceGroup, v.PhysicalGroup, v.Frame, v.RequestedPalette, v.EffectivePalette, v.Mirrored, v.Width, v.Height, v.HotX, v.HotY,
                v.ArtOffsetX, v.ArtOffsetY, v.ObjectOffsetX, v.ObjectOffsetY, v.Scale, v.EncodedBytes, v.RgbaBytes, v.RgbaSha256, v.Policy }).ToArray(),
            new object?[] { r.Survey, r.CoreSectors, r.ContextPresent, r.ContextMissing, r.CoreTerrain, r.ContextTerrain, r.Statics, r.Aggregate, r.CoreMobiles, r.ContextMobiles, r.OutsideMobiles, r.UnknownMobiles, r.EditorCorrespondence, r.Prototypes, r.Visuals },
            components.OrderBy(c => c.Cell.X).ThenBy(c => c.Cell.Y).ThenBy(c => c.Kind == "Mask" ? 0 : c.Kind == "Facade" ? 1 : 2).Select(ComponentArray).ToArray(),
            nameBinding is { } n ? new object?[] { EntryArray(n.Entry), n.PayloadSha256 } : null,
            s.AuthorizedScope.OrderBy(x => x.Packed).Select(x => new object?[] { x.Packed, x.Scope.ToString() }).ToArray()];
    }
    private static int ProbeAreaTraversal(string[] args)
    {
        string[] switches = ["--module", "--resource1", "--resource2", "--mobile", "--prototype-root", "--oracle"];
        if (args.Length != 12 || switches.Where((s, i) => args[i * 2] != s).Any()) throw new ArgumentException("AREA_TRAVERSAL_ARGUMENTS");
        var oracleBefore = InputFingerprint.Create(args[11]);
        if (oracleBefore.Sha256 != "bfe9770d75a1c1922ca8817fd0c239e589c3de48020022f7de6bd450ef2d2b60") throw new InvalidDataException("ORACLE_IDENTITY");
        using var oracle = JsonDocument.Parse(File.ReadAllBytes(args[11]));
        var paths = new List<(AreaInputKey Key, string Path)>
        {
            (new(AreaSourceRole.Module), args[1]), (new(AreaSourceRole.Resource1), args[3]), (new(AreaSourceRole.Resource2), args[5]), (new(AreaSourceRole.NormalGameMobile), args[7])
        };
        foreach (var p in oracle.RootElement.GetProperty("prototypes").EnumerateArray())
        {
            var n = p.GetProperty("own_number").GetInt32();
            var matches = Directory.EnumerateFiles(args[9], $"{n:D6} - *.pro").Take(2).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("PROTOTYPE_SELECTION");
            paths.Add((new(AreaSourceRole.Prototype, n), matches[0]));
        }
        var before = paths.Select(p => InputFingerprint.Create(p.Path)).ToArray();
        var inputs = paths.Select(p => AreaInput.FromFile(p.Key, p.Path)).ToArray();
        using var owner = AreaOriginalValidator.Open(new(inputs));
        var snapshot = owner.LoadStrict();
        CheckTraversalOracle(snapshot, oracle.RootElement);
        for (var i = 0; i < paths.Count; i++)
        {
            var selected = snapshot.Inputs.Single(p => p.Key == paths[i].Key);
            if (selected.Length != before[i].Bytes || selected.Sha256 != before[i].Sha256) throw new InvalidDataException("SNAPSHOT_SOURCE_IDENTITY");
        }
        var components = new List<ResearchComponent>(); ResearchNameBinding? nameBinding = null;
        using (var module = DatArchive.Open(args[1], BoundedOptions()))
        using (var resource = DatArchive.Open(args[5], BoundedOptions()))
        {
            const string namePath = "art/tile/tilename.mes";
            var names = resource.Entries.Where(e => AreaName(e).Equals(namePath, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (names.Length != 1) throw new InvalidDataException("NAME_SELECTION");
            var name = names[0]; var nameIdentity = new AreaEntryIdentity(new(AreaSourceRole.Resource2), name.Ordinal, name.Flags, name.StoredSize, name.DeclaredOutputSize, name.Offset, AreaHash(name.RawNameBytes.ToArray()));
            nameBinding = new(nameIdentity, AreaHash(resource.ReadEntry(name)));
            var nameSource = new TerrainTileNameSource(resource, snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Resource2).Sha256, namePath, TerrainPathProfile.NonWindowsMacOs);
            foreach (var s in snapshot.Sectors.Where(s => s.Present))
            {
                var e = module.Entries.Single(e => e.Ordinal == s.DatOrdinal);
                var identity = new AreaEntryIdentity(new(AreaSourceRole.Module), e.Ordinal, e.Flags, e.StoredSize, e.DeclaredOutputSize, e.Offset, AreaHash(e.RawNameBytes.ToArray()));
                if (identity != s.Entry || AreaHash(module.ReadEntry(e)) != s.PayloadSha256) throw new InvalidDataException("SECTOR_SELECTED_IDENTITY");
                var selectedRawPath = Encoding.ASCII.GetString(e.RawNameBytes.ToArray()[..^1]);
                var selector = new TerrainSectorSelector(selectedRawPath, "Arcanum1-024-fixed", checked((int)s.Coordinate.X), checked((int)s.Coordinate.Y), s.Key.Packed,
                    checked((int)s.Coordinate.OriginX), checked((int)s.Coordinate.OriginY), 64, 64, new(e.Ordinal, e.Flags, e.DeclaredOutputSize));
                var report = TerrainObstructionInspector.InspectSector(module, s.Key.SourceSha256, selector, nameSource);
                if (report.Sector.EntryOrdinal != e.Ordinal || report.Sector.OriginX != s.Coordinate.OriginX || report.Sector.OriginY != s.Coordinate.OriginY) throw new InvalidDataException("COMPONENT_SECTOR_IDENTITY");
                foreach (var t in snapshot.Terrain.Where(t => t.Key.Sector == s.Key))
                {
                    var c = report.GetCell(t.LocalX, t.LocalY);
                    if (c.WorldX != t.WorldX || c.WorldY != t.WorldY || c.Ordinal != t.Key.SourceIndex || c.TileNameRule.Provenance.EntryOrdinal is { } ordinal && ordinal != name.Ordinal)
                        throw new InvalidDataException("COMPONENT_CELL_IDENTITY");
                    var cell = new ResearchCell(snapshot.RuntimeMap, t.WorldX, t.WorldY);
                    components.Add(CopyComponent(cell, "Mask", c.SerializedBlockMask,t.RawArt)); components.Add(CopyComponent(cell, "Facade", c.FacadeWalkabilityBit,t.RawArt)); components.Add(CopyComponent(cell, "Name", c.TileNameRule,t.RawArt));
                }
            }
        }
        var actor = DeclaredAreaActor(snapshot.Profile);
        var selection = Selection(snapshot);
        var binding = Bind(selection, actor, components, nameBinding, "AuthenticatedOriginal");
        var evidence = new ResearchEvidence(binding, actor, selection, components, nameBinding);
        var queries = new List<(string Label, ResearchCell From, ResearchCell To, string Direction)>
        {
            ("FrozenForwardAreaProfile", new(snapshot.RuntimeMap,92934,82561), new(snapshot.RuntimeMap,92933,82561), "XMinus"),
            ("FrozenReverseAreaProfile", new(snapshot.RuntimeMap,92933,82561), new(snapshot.RuntimeMap,92934,82561), "XPlus"),
            ("FrozenOpenAreaProfile", new(snapshot.RuntimeMap,92934,82561), new(snapshot.RuntimeMap,92935,82561), "XPlus"),
            ("FrozenOpenReverseAreaProfile", new(snapshot.RuntimeMap,92935,82561), new(snapshot.RuntimeMap,92934,82561), "XMinus"),
            ("XBorder", new(snapshot.RuntimeMap,92991,82561), new(snapshot.RuntimeMap,92992,82561), "XPlus"),
            ("XBorderReverse", new(snapshot.RuntimeMap,92992,82561), new(snapshot.RuntimeMap,92991,82561), "XMinus"),
            ("YBorder", new(snapshot.RuntimeMap,92934,82623), new(snapshot.RuntimeMap,92934,82624), "YPlus"),
            ("YBorderReverse", new(snapshot.RuntimeMap,92934,82624), new(snapshot.RuntimeMap,92934,82623), "YMinus"),
            ("SharedCorner", new(snapshot.RuntimeMap,92991,82623), new(snapshot.RuntimeMap,92992,82624), "XPlusYPlus"),
            ("SharedCornerReverse", new(snapshot.RuntimeMap,92992,82624), new(snapshot.RuntimeMap,92991,82623), "XMinusYMinus"),
            ("SameCell", new(snapshot.RuntimeMap,92934,82561), new(snapshot.RuntimeMap,92934,82561), "XPlus"),
            ("Nonneighbor", new(snapshot.RuntimeMap,92934,82561), new(snapshot.RuntimeMap,92936,82561), "XPlus"),
            ("DirectionMismatch", new(snapshot.RuntimeMap,92934,82561), new(snapshot.RuntimeMap,92935,82561), "YPlus"),
            ("Outside", new(snapshot.RuntimeMap,92800,82432), new(snapshot.RuntimeMap,92801,82432), "XPlus")
        };
        foreach (var type in new[] { 2, 16, 5, 7, 8, 9, 13, 14 })
        {
            var o = evidence.Objects.FirstOrDefault(o => o.Type == type && o.Scope == AreaScope.Core && o.WorldX.HasValue && o.WorldY.HasValue);
            if (o is not null) queries.Add(($"Type{type}", new(snapshot.RuntimeMap,o.WorldX!.Value-1,o.WorldY!.Value), new(snapshot.RuntimeMap,o.WorldX.Value,o.WorldY.Value), "XPlus"));
        }
        foreach (var missing in snapshot.Sectors.Where(s => !s.Present))
        {
            var core = snapshot.Sectors.FirstOrDefault(s => s.Scope == AreaScope.Core && Math.Abs(s.Coordinate.X-missing.Coordinate.X)+Math.Abs(s.Coordinate.Y-missing.Coordinate.Y)==1);
            if (core is null) continue;
            var dx = missing.Coordinate.X-core.Coordinate.X; var dy = missing.Coordinate.Y-core.Coordinate.Y;
            var x = core.Coordinate.OriginX+(dx==1?63:0); var y = core.Coordinate.OriginY+(dy==1?63:0);
            queries.Add(("MissingContext", new(snapshot.RuntimeMap,x,y), new(snapshot.RuntimeMap,x+dx,y+dy), Directions.Single(d=>d.X==dx && d.Y==dy).Name)); break;
        }
        var results = queries.Select(q => new { q.Label, Result = evidence.Assess(new(binding, q.From, q.To, q.Direction, "CoreWithContextEvidence")) }).ToArray();
        var contributorRows = evidence.Objects.Select(o => new { o.Key, o.Type, o.Scope, o.WorldX, o.WorldY, DirectFields = o.DirectFields.Count, EffectiveFields = o.EffectiveFields.Count,
            FieldsDigest = CanonicalDigest(new object?[] { o.DirectFields.OrderBy(f=>f.Slot).Select(FieldArray).ToArray(), o.EffectiveFields.OrderBy(f=>f.Slot).Select(FieldArray).ToArray() }),
            Row = Classify(o, "InventoryDestination", false, "XPlus", "XPlus", false) }).ToArray();
        // The sole authenticated publication boundary is AFTER every extra read and query.
        foreach (var p in paths.Where(p => p.Key.Role == AreaSourceRole.Prototype))
        {
            var candidates = Directory.EnumerateFiles(args[9], $"{p.Key.Number:D6} - *.pro").Take(2).ToArray();
            if (candidates.Length != 1 || candidates[0] != p.Path) throw new InvalidDataException("PROTOTYPE_SELECTION_CHANGED");
        }
        VerifyFinalInputs(paths.Select(p => p.Path).Append(args[11]).ToArray(), before.Append(oracleBefore).ToArray());
        owner.Dispose();
        Console.WriteLine(JsonSerializer.Serialize(new { Schema = AreaTraversalSchema, Binding = binding, Actor = actor, snapshot.Reconciliation,
            Readiness = "NOT_READY", StateMode = "FileEffectiveInitial", ActorStateMode = actor.StateMode, RuleClassification = "SourceConfirmed", SourceDisposalSafe = true,
            SectorRows = snapshot.Sectors.Select(s => new { s.Key, s.Scope, s.Present, s.DatOrdinal, s.TerminalCount, s.PayloadSha256 }),
            TypeRows = evidence.Objects.GroupBy(o => (o.Key.Kind,o.Scope,o.Type)).Select(g => new { Kind = g.Key.Kind.ToString(), Scope = g.Key.Scope.ToString(), g.Key.Type, Count = g.Count() }),
            ComponentRows = components.GroupBy(c => (c.Kind,c.Coverage,c.Applicability,c.Disposition,c.Reason)).Select(g => new { g.Key.Kind, g.Key.Coverage, g.Key.Applicability, g.Key.Disposition, g.Key.Reason, Count = g.Count() }),
            Contributors = contributorRows, Results = results, ResultDigests = results.Select(r=>new { r.Label, Digest=CanonicalDigest(ResultArray(r.Result)) }),
            Path0x1000 = evidence.Assess(new(binding, new(snapshot.RuntimeMap,92934,82561),new(snapshot.RuntimeMap,92935,82561),"XPlus","Path0x1000Unavailable")),
            OriginalInputsUnchanged = true, OriginalExecutableParity = "NOT_VERIFIED", ProductionAdmissionExpanded = false }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
        return 0;
    }
    private static void VerifyFinalInputs(string[] paths, InputFingerprint[] before)
    {
        if (paths.Length != before.Length) throw new InvalidDataException("FINAL_GUARD_SHAPE");
        for (var i = 0; i < paths.Length; i++) if (InputFingerprint.Create(paths[i]) != before[i]) throw new InvalidDataException("AREA_TRAVERSAL_INPUT_CHANGED");
    }
    private static void AreaTraversalSelfTest()
    {
        var checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks++; }
        void Reject(Action call, string label)
        {
            try { call(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException or TerrainException or DatException or AreaException or OperationCanceledException) { checks++; return; }
            throw new InvalidOperationException("ACCEPTED_INVALID_" + label);
        }
        const string sha = "0000000000000000000000000000000000000000000000000000000000000000";
        var actor = DeclaredAreaActor("fixture") with { StateToken = "state-1" };
        var binding = new ResearchBinding(AreaTraversalSchema, AreaTraversalRule, AreaTraversalPin, "Structural", 1, "fixture", sha, CanonicalDigest(ActorArray(actor)), 1);
        static AreaSectorRecord Sector(long x, long y, bool present = true) => new(new("0000000000000000000000000000000000000000000000000000000000000000",1,new AreaSectorCoordinate(x,y).Packed),new(x,y),AreaScope.Core,present,0,2,0,1,0xAA0004,0,[],[],null,null);
        AreaSectorRecord[] sectors = [Sector(1,1),Sector(2,1),Sector(1,2),Sector(2,2)];
        static ResearchCell C(long x,long y) => new(1,x,y);
        static ResearchComponent[] Clear(ResearchCell c) => [new(c,"Mask","Supported","Applicable","NoBlock","FIXTURE_MASK","SerializedSector","fixture",0),
            new(c,"Facade","Supported","NotApplicable",null,"FIXTURE_FACADE","PackedTerrainId","fixture",0),new(c,"Name","Supported","Applicable","NoBlock","FIXTURE_NAME","TileNameMes","fixture",0)];
        AreaObjectRecord O(int index,int type,long x,long y,long? flags=0) => new(new(sha,1,AreaSourceKind.Static,new AreaSectorCoordinate(x/64,y/64).Packed,index,index*100,sha,79),119,type,7001,sha,sha,sha,sha,
            AreaScope.Core,(x & 63) | ((y & 63) << 32),x,y,AreaDisposition.BodyEligibleConditionally,flags.HasValue?[new(18,AreaPresence.Value,AreaValueOrigin.Instance,flags,[],null)]:[],
            flags.HasValue?[new(18,AreaPresence.Value,AreaValueOrigin.Instance,flags,[],null)]:[],true,true,true);
        ResearchSelection S(IEnumerable<AreaObjectRecord>? objects = null, IEnumerable<AreaSectorRecord>? selected = null) =>
            new(1,1,"fixture",[],Frozen(selected??sectors),Frozen(objects??[]),[],[],new(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0),Frozen(sectors.Select(s=>new ResearchScope(s.Key.Packed,s.Scope))));
        ResearchEvidence E(IEnumerable<AreaObjectRecord>? objects=null, IEnumerable<ResearchComponent>? rows=null, bool registry=true, IEnumerable<AreaSectorRecord>? selected=null)
        {
            var s=S(objects,selected); var a=actor with{RegistryClosed=registry}; var c=Frozen(rows??Clear(C(128,128)));
            return new(Bind(s,a,c),a,s,c);
        }
        ResearchResult Q(ResearchEvidence e,long x,long y,long tx,long ty,string d) => e.Assess(new(e.Binding,C(x,y),C(tx,ty),d,"CoreWithContextEvidence"));

        // T1: literal borders and four-sector corner; actual public coordinate seam.
        foreach(var pair in new[]{(127L,80L,128L,80L,"XPlus"),(128L,80L,127L,80L,"XMinus"),(80L,127L,80L,128L,"YPlus"),(80L,128L,80L,127L,"YMinus"),(127L,127L,128L,128L,"XPlusYPlus")})
        {
            var e=E(rows:Clear(C(pair.Item3,pair.Item4))); var r=Q(e,pair.Item1,pair.Item2,pair.Item3,pair.Item4,pair.Item5);
            Check(r.Disposition=="Open" && r.Gaps.Count==0,"T1_BORDER");
        }
        Check(new AreaSectorCoordinate(67108863,67108863).Packed==4503599627370495,"T1_PACKED_MAX");
        Reject(()=>new AreaSectorCoordinate(-1,0),"T1_SIGNED"); Reject(()=>new AreaSectorCoordinate(67108864,0),"T1_26BIT");
        Reject(()=>AreaSectorCoordinate.Unpack(4503599627370496),"T1_PACKED_OVERFLOW");
        Reject(()=>Q(E(),long.MinValue,80,long.MaxValue,80,"XPlus"),"T1_DELTA_OVERFLOW");

        // T2: expectations are literal source-independent delta rows, not Directions.
        foreach(var row in new[]{(-1,-1,"XMinusYMinus"),(-1,0,"XMinus"),(-1,1,"XMinusYPlus"),(0,1,"YPlus"),(1,1,"XPlusYPlus"),(1,0,"XPlus"),(1,-1,"XPlusYMinus"),(0,-1,"YMinus")})
            Check(Q(E(rows:Clear(C(80+row.Item1,80+row.Item2))),80,80,80+row.Item1,80+row.Item2,row.Item3).Disposition=="Open","T2_DELTA");
        foreach(var row in new[]{(80L,80L,"XPlus"),(82L,80L,"XPlus"),(81L,80L,"YPlus"),(0L,0L,"XPlus")})
            Check(Q(E(),80,80,row.Item1,row.Item2,row.Item3).Coverage=="Unsupported","T2_RELATION");
        var block=O(1,3,81,80); var forward=Q(E([block],Clear(C(81,80))),80,80,81,80,"XPlus");
        var reverse=Q(E([block],Clear(C(80,80))),81,80,80,80,"XMinus");
        Check(forward.Disposition=="Blocked" && reverse.Disposition=="Open","T2_ASYMMETRY");
        var wall=O(0,0,80,80) with { EffectiveFields=[new(18,AreaPresence.Value,AreaValueOrigin.Instance,0,[],null),new(0,AreaPresence.Value,AreaValueOrigin.Instance,0x10028800,[],null)] };
        Check(Q(E([wall],Clear(C(79,80))),80,80,79,80,"XMinus").Disposition=="Open","T2_CARDINAL_WINDOW");
        Check(Q(E([wall],Clear(C(79,79))),80,80,79,79,"XMinusYMinus").Disposition=="Blocked","T2_DIAGONAL_WINDOW");

        // T3: original diagonal direction and all four ordered object legs retained.
        var diagonal=Q(E(rows:Clear(C(128,128))),127,127,128,128,"XPlusYPlus");
        Check(diagonal.Legs.Select(l=>l.Role).SequenceEqual(new[]{"SourceToCcw","CcwToFinal","SourceToCw","CwToFinal"}),"T3_ORDER");
        Check(diagonal.Legs.Select(l=>l.Direction).SequenceEqual(new[]{"YPlus","XPlus","XPlus","YPlus"}),"T3_DIRECTIONS");
        Check(diagonal.Legs.All(l=>l.OriginalDirection=="XPlusYPlus") && diagonal.Components.Count==3,"T3_FINAL_ONCE");
        var shoulderTerrain=Clear(C(127,128)); shoulderTerrain[0]=shoulderTerrain[0] with{Disposition="Blocked"};
        Check(Q(E(rows:Clear(C(128,128)).Concat(shoulderTerrain)),127,127,128,128,"XPlusYPlus").Disposition=="Open","T3_NO_SHOULDER_TERRAIN");
        foreach(var xy in new[]{(127L,128L),(128L,127L),(128L,128L)})
            Check(Q(E([O(1,3,xy.Item1,xy.Item2)],Clear(C(128,128))),127,127,128,128,"XPlusYPlus").Disposition=="Blocked","T3_OBJECT_TRUTH_TABLE");
        Check(Q(E(selected:sectors.Where(s=>s.Coordinate!=new AreaSectorCoordinate(1,2))),127,127,128,128,"XPlusYPlus").Gaps.Any(g=>g.Code=="LEG_SECTOR_MISSING"),"T3_MISSING_SHOULDER");

        // T4/T5: coincident/null-ID records, direct zero/prototype nonzero, absent/null/empty/sparse copied.
        var noBlock=O(2,3,81,80,1024); var two=Q(E([noBlock,block],Clear(C(81,80))),80,80,81,80,"XPlus");
        Check(two.Disposition=="Blocked" && two.Records.Count==2 && two.Records[0].Key.SourceIndex==1,"T4_MULTIPLICITY_ORDER");
        var words=new long[]{0,4294967295};
        var fields=new List<AreaFieldValue>{new(18,AreaPresence.Value,AreaValueOrigin.Instance,0,[],null),new(30,AreaPresence.Array,AreaValueOrigin.Prototype,null,[new(9,[1,2]),new(2,words)],null),
            new(31,AreaPresence.Null,AreaValueOrigin.Instance,null,[],null),new(34,AreaPresence.Empty,AreaValueOrigin.Instance,null,[],null)};
        var evidence=E([block with{EffectiveFields=fields}],Clear(C(81,80))); words[0]=7; fields.Clear();
        Check(evidence.Objects[0].EffectiveFields.Count==4 && evidence.Objects[0].EffectiveFields[1].Elements[1].Words[0]==0,"T5_COPY_WORDS");
        Check(Q(evidence,80,80,81,80,"XPlus").Disposition=="Blocked","T5_EXPLICIT_ZERO");
        Check(Q(E([O(1,3,81,80,null)],Clear(C(81,80))),80,80,81,80,"XPlus").Gaps.Any(g=>g.Code=="FLAGS_UNRESOLVED"),"T5_ABSENT_FLAGS");
        Check(Q(E([block with{EffectiveFields=[new(18,AreaPresence.Value,AreaValueOrigin.Prototype,1024,[],null)]}],Clear(C(81,80))),80,80,81,80,"XPlus").Disposition=="Open","T5_INHERITED");

        // T6: literal original flag masks; DONTDRAW never means absent, NPC HP not flags.
        foreach(var flag in new[]{1,2,4096})
            Check(Q(E([O(1,3,81,80,flag)],Clear(C(81,80))),80,80,81,80,"XPlus").Records.Single().Applicability=="NotApplicable","T6_EXCLUSIONS");
        foreach(var flag in new[]{0,256,8192})
            Check(Q(E([O(1,3,81,80,flag)],Clear(C(81,80))),80,80,81,80,"XPlus").Disposition=="Blocked","T6_NOT_VISUAL_FILTER");
        Check(Q(E([O(1,16,81,80,1024)],Clear(C(81,80))),80,80,81,80,"XPlus").Gaps.Any(g=>g.Code=="CURRENT_HP_STAT_EFFECT_UNOBSERVED"),"T6_NPC_NO_BLOCK_NOT_DEAD");
        Check(Q(E([O(1,5,81,80)],Clear(C(81,80))),80,80,81,80,"XPlus").Records.Single().Applicability=="NotApplicable","T6_ITEM_MASK");

        // T7/T8: unknown is not false, final blocker retains gaps, input errors before shortcuts.
        var final=Clear(C(81,80)); final[0]=final[0] with{Disposition="Blocked"}; final[2]=final[2] with{Coverage="Unknown",Applicability="Unknown",Disposition=null,Reason="TILE_NAME_ENTRY_MISSING"};
        var blocked=Q(E([block],final,false),80,80,81,80,"XPlus");
        Check(blocked.Disposition=="Blocked" && !blocked.CompleteCoverage && blocked.Readiness=="NOT_READY" && blocked.Gaps.Count>0,"T7_TERRAIN_PRECEDENCE");
        Check(Q(E([block],Clear(C(81,80)),false),80,80,81,80,"XPlus").Disposition is null,"T7_OBJECT_REGISTRATION");
        var pathEvidence=E(rows:Clear(C(81,80)));
        var pathResult=pathEvidence.Assess(new(pathEvidence.Binding,C(80,80),C(81,80),"XPlus","Path0x1000Unavailable"));
        Check(pathResult.Coverage=="Unknown" && pathResult.Disposition is null && pathResult.Legs.Count==0 && pathResult.Gaps.Single().Code=="PATH_FLAG_0x1000_CALLER_UNAVAILABLE","T7_PATH_NO_FALLBACK");
        Reject(()=>E([block,block]),"T8_DUPLICATE_KEY");
        Reject(()=>E([block with{EffectiveFields=[new(18,AreaPresence.Null,AreaValueOrigin.Instance,0,[],null)]}]),"T8_FIELD_SHAPE");
        Reject(()=>E([block with{EffectiveFields=[new(30,AreaPresence.Array,AreaValueOrigin.Instance,null,[new(0,[-1])],null)]}]),"T8_UNSIGNED_WORD");
        Reject(()=>new ResearchEvidence(binding with{Map=2},actor,S(),[]),"T8_MAP");
        Reject(()=>new ResearchEvidence(binding,actor with{Kind=16},S(),[]),"T8_ACTOR");
        Reject(()=>new ResearchEvidence(binding with{SelectedDigest="invalid"},actor,S(),[]),"T8_DIGEST_SHAPE");
        Reject(()=>E([block with{EffectiveFields=[new(18,AreaPresence.Value,AreaValueOrigin.Instance,2147483648L,[],null)]}]),"T8_FLAG_WIDTH");
        var unknownApplicable=Clear(C(81,80)); unknownApplicable[0]=unknownApplicable[0] with {Applicability="Unknown",Disposition=null,Reason="FIXTURE_APPLICABILITY_UNKNOWN"};
        Check(Q(E(rows:unknownApplicable),80,80,81,80,"XPlus").Coverage=="Unknown","T7_APPLICABILITY_UNKNOWN");
        Reject(()=>ProbeAreaTraversal([]),"T8_ARGV");

        // T9: same extended guard used by originals, after extra synthetic reads. No originals mutated.
        var path=Path.Combine(Path.GetTempPath(),"arc-res017-self-test-"+Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path,[1,2,3]); var before=InputFingerprint.Create(path);
        using(var extra=File.OpenRead(path)) { Check(extra.ReadByte()==1,"T9_EXTRA_READ"); }
        VerifyFinalInputs([path],[before]); File.WriteAllBytes(path,[1,2,4]);
        Reject(()=>VerifyFinalInputs([path],[before]),"T9_LATE_MUTATION");
        // Retain the owned synthetic artifact as evidence; no cleanup of shared data.
        Reject(()=>evidence.Assess(new(evidence.Binding with{Generation=2},C(80,80),C(81,80),"XPlus","CoreWithContextEvidence")),"T9_GENERATION");
        Reject(()=>evidence.Assess(new(evidence.Binding with{ActorDigest=sha},C(80,80),C(81,80),"XPlus","CoreWithContextEvidence")),"T9_ACTOR_DIGEST");
        Check(((System.Collections.IList)evidence.Objects).IsReadOnly,"T9_READONLY");

        // Review R1: valid content binding first, then changed evidence with the OLD binding.
        var selected=S([block]); var clearRows=Clear(C(81,80)); var closedActor=actor with{RegistryClosed=true};
        var validBinding=Bind(selected,closedActor,clearRows); var historical=new ResearchEvidence(validBinding,closedActor,selected,clearRows);
        Check(Q(historical,80,80,81,80,"XPlus").Disposition=="Blocked","R1_VALID_CANONICAL_BINDING");
        var changedMask=clearRows.ToArray(); changedMask[0]=changedMask[0] with{Disposition="Blocked"};
        Reject(()=>new ResearchEvidence(validBinding,closedActor,selected,changedMask),"R1_STALE_MASK");
        Reject(()=>new ResearchEvidence(validBinding,closedActor,selected with{Sectors=Frozen(selected.Sectors.Select(s=>s with{Placeholder=s.Placeholder+1}))},clearRows),"R1_STALE_NEIGHBOR");
        Reject(()=>new ResearchEvidence(validBinding,closedActor,selected with{Objects=[block with{EffectiveFields=[new(18,AreaPresence.Value,AreaValueOrigin.Instance,1024,[],null)]}]},clearRows),"R1_STALE_OCCURRENCE");
        Reject(()=>new ResearchEvidence(validBinding,closedActor,selected with{Objects=[]},clearRows),"R1_STALE_OCCURRENCE_REMOVAL");
        Reject(()=>new ResearchEvidence(validBinding,closedActor with{RegistryClosed=false},selected,clearRows),"R1_STALE_REGISTRY");
        Reject(()=>new ResearchEvidence(validBinding,closedActor with{StateToken="state-2"},selected,clearRows),"R1_STALE_STATE");
        Reject(()=>new ResearchEvidence(validBinding,closedActor,selected with{AuthorizedScope=selected.AuthorizedScope.Skip(1).ToArray()},clearRows),"R1_STALE_SCOPE");
        var renewed=E([noBlock],Clear(C(81,80)));
        Reject(()=>renewed.Assess(new(validBinding,C(80,80),C(81,80),"XPlus","CoreWithContextEvidence")),"R1_OLD_QUERY_NEW_EVIDENCE");
        Check(Q(historical,80,80,81,80,"XPlus").Disposition=="Blocked" && Q(renewed,80,80,81,80,"XPlus").Disposition=="Open","R1_DETACHED_HISTORY");

        // Review R2: no unlocated record can silently disappear from the tile predicate.
        foreach(var malformed in new[]{block with{SerializedLocation=null,WorldX=null,WorldY=null,PlacementReady=false,SchemaReady=false,RequiredLayersReady=false,Disposition=AreaDisposition.MissingMandatoryFact},
            block with{WorldX=null},block with{WorldY=null},block with{SerializedLocation=null},block with{WorldX=-1},block with{WorldY=4294967296L},
            block with{PlacementReady=false},block with{WorldX=82},block with{Scope=AreaScope.Context},block with{Key=block.Key with{RuntimeMap=2}},
            block with{Key=block.Key with{Sector=67108866}},block with{Key=block.Key with{SourceSha256=new string('1',64)}},block with{Key=block.Key with{Sector=null}}})
            Reject(()=>E([malformed],Clear(C(81,80))),"R2_PLACEMENT_SHAPE");
        var schemaGap=Q(E([O(9,3,90,90) with{SchemaReady=false}],Clear(C(81,80))),80,80,81,80,"XPlus");
        Check(schemaGap.Coverage=="Unknown" && schemaGap.Disposition is null && schemaGap.Gaps.Single().Code=="OCCURRENCE_SCHEMA_UNREADY","R2_UNREADY_COMPLETENESS");
        Check(Q(E([block with{RequiredLayersReady=false}],Clear(C(81,80))),80,80,81,80,"XPlus").Disposition=="Blocked","R2_LAYERS_NOT_EXCLUSION");
        var mobile=block with{Key=block.Key with{Kind=AreaSourceKind.Mobile,Sector=null},SerializedLocation=343597383761};
        Check(Q(E([mobile],Clear(C(81,80))),80,80,81,80,"XPlus").Disposition=="Blocked","R2_MOBILE_WORLD_PLACEMENT");
        Reject(()=>E([mobile with{SerializedLocation=343597383762}],Clear(C(81,80))),"R2_MOBILE_LOCATION_MISMATCH");
        Reject(()=>E([mobile with{Key=mobile.Key with{Sector=67108865}}],Clear(C(81,80))),"R2_MOBILE_SECTOR_OWNERSHIP");
        Reject(()=>E([mobile with{Scope=AreaScope.Context}],Clear(C(81,80))),"R2_MOBILE_SCOPE_MISMATCH");
        var mutableSectors=sectors.ToArray(); var mutableRows=Clear(C(81,80)); var mutableObjects=new[]{block};
        var mutableSelection=S(mutableObjects) with{Sectors=mutableSectors,Objects=mutableObjects};
        var immutable=new ResearchEvidence(Bind(mutableSelection,closedActor,mutableRows),closedActor,mutableSelection,mutableRows);
        mutableSectors[0]=Sector(1,1,false); mutableRows[0]=mutableRows[0] with{Disposition="Blocked"}; mutableObjects[0]=noBlock;
        Check(Q(immutable,80,80,81,80,"XPlus").Disposition=="Blocked" && immutable.Objects.Single().WorldX==81,"R2_OWNED_PLACEMENT_COMPONENT_SECTOR_COPY");

        // Review R3: geometric scope is declared separately, not inferred from availability.
        foreach(var pair in new[]{(127L,80L,128L,80L,"XPlus",67108866L),(128L,80L,127L,80L,"XMinus",67108865L),
            (80L,127L,80L,128L,"YPlus",134217729L),(80L,128L,80L,127L,"YMinus",67108865L),
            (127L,127L,128L,128L,"XPlusYPlus",134217730L),(128L,128L,127L,127L,"XMinusYMinus",67108865L)})
        {
            var omitted=Q(E(rows:[],selected:sectors.Where(s=>s.Key.Packed!=pair.Item6)),pair.Item1,pair.Item2,pair.Item3,pair.Item4,pair.Item5);
            var absent=Q(E(rows:[],selected:sectors.Select(s=>s.Key.Packed==pair.Item6?s with{Present=false}:s)),pair.Item1,pair.Item2,pair.Item3,pair.Item4,pair.Item5);
            Check(omitted.Coverage=="Unknown" && omitted.Disposition is null && omitted.Readiness=="NOT_READY" && omitted.Gaps.Any(g=>g.Code=="NEIGHBOR_SECTOR_MISSING" && g.Cell==C(pair.Item3,pair.Item4)),"R3_OMITTED_ADMITTED_NEIGHBOR");
            Check(absent.Coverage=="Unknown" && absent.Disposition is null && absent.Gaps.Select(g=>g.Code).SequenceEqual(omitted.Gaps.Select(g=>g.Code)),"R3_PRESENT_FALSE_EQUIVALENCE");
            var reverseMissing=Q(E(rows:[],selected:sectors.Where(s=>s.Key.Packed!=pair.Item6)),pair.Item3,pair.Item4,pair.Item1,pair.Item2,
                pair.Item5=="XPlus"?"XMinus":pair.Item5=="XMinus"?"XPlus":pair.Item5=="YPlus"?"YMinus":pair.Item5=="YMinus"?"YPlus":pair.Item5=="XPlusYPlus"?"XMinusYMinus":"XPlusYPlus");
            Check(reverseMissing.Coverage=="Unknown" && reverseMissing.Gaps.Any(g=>g.Code=="SOURCE_SECTOR_MISSING"),"R3_OMITTED_SOURCE");
        }
        foreach(var outside in new[]{(64L,80L,63L,80L,"XMinus"),(191L,80L,192L,80L,"XPlus"),(80L,64L,80L,63L,"YMinus"),(80L,191L,80L,192L,"YPlus")})
        {
            var r=Q(E(rows:[]),outside.Item1,outside.Item2,outside.Item3,outside.Item4,outside.Item5);
            Check(r.Coverage=="Unsupported" && r.Disposition is null && r.Reason=="SCOPE_UNSUPPORTED","R3_TRUE_OUTSIDE");
        }

        // T10: Python hashlib/json literals, independently computed before these checks.
        const string actorPayload="[\"AREA_TRAVERSAL_RESEARCH_V1\",\"ACTOR_CONTEXT\",1,\"sub_425760\",\"WORLD_CELL_CENTERS_V1\",\"HUMAN_FIXED_DIAGNOSTIC\",15,536870912,0,true,\"DeclaredDiagnosticOverlay\",\"ProjectDeclared\",\"fixture\",\"NONEDITOR_EXACT_TILE\",\"state-1\",false]";
        Check(Canonical(ActorArray(actor))==actorPayload && CanonicalDigest(ActorArray(actor))=="9dfa1a228beb1f04a35bf03da67f70e135a2ed0365f57517a8b8eeaeab68cce7","T10_ACTOR_LITERAL");
        Check(Canonical(FieldArray(new(18,AreaPresence.Value,AreaValueOrigin.Instance,0,[],null)))=="[18,\"Value\",\"Instance\",0,[],null]" &&
            CanonicalDigest(FieldArray(new(18,AreaPresence.Value,AreaValueOrigin.Instance,0,[],null)))=="df80171b2be70e88bf4c1446cb75eaa8ee9bb47dff745bd12fae482196e91f77","T10_ZERO_LITERAL");
        var sparse=new AreaFieldValue(30,AreaPresence.Array,AreaValueOrigin.Prototype,null,[new(9,[1,2]),new(2,[0,4294967295])],null);
        Check(Canonical(FieldArray(sparse))=="[30,\"Array\",\"Prototype\",null,[[2,[0,4294967295]],[9,[1,2]]],null]" && CanonicalDigest(FieldArray(sparse))=="24c4b5764e9b24c48a0d663d2abbd521767f732b81a00d477792bd8d13ca2934","T10_SPARSE_LITERAL");
        Check(CanonicalDigest(ActorArray(actor with{StateToken="state-2"}))!=binding.ActorDigest,"T10_STATE_INVALIDATION");
        Reject(()=>Canonical(new object?[]{1.0}),"T10_FLOAT"); Reject(()=>Canonical("\n"),"T10_NONASCII");
        object?[] selectedFixture=["AREA_TRAVERSAL_RESEARCH_V1","SELECTED_EVIDENCE",1,"S1_NORMAL_GROUND_FILE_FRONTIER_V1","9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52","Structural",1,1,"fixture",
            Array.Empty<object?>(),Array.Empty<object?>(),Array.Empty<object?>(),Array.Empty<object?>(),Array.Empty<object?>(),new int[15],Array.Empty<object?>(),null,Array.Empty<object?>()];
        const string selectedPayload="[\"AREA_TRAVERSAL_RESEARCH_V1\",\"SELECTED_EVIDENCE\",1,\"S1_NORMAL_GROUND_FILE_FRONTIER_V1\",\"9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52\",\"Structural\",1,1,\"fixture\",[],[],[],[],[],[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],[],null,[]]";
        Check(Canonical(selectedFixture)==selectedPayload && CanonicalDigest(selectedFixture)=="5d7826e4e9138e37d678276f8b1b59e0a056539d4c2f3e6e24a3a0eb17f92aaf","T10_SELECTED_LITERAL");
        AreaTraversalOwnerOracles(Check, Reject);
        Console.WriteLine($"area-traversal-self-test: PASS ({checks} independent research assertions; T1-T10, structural only; production owner/framing limits separately exercised by root Area tests)");
    }

    // Test-local literal v119 envelopes. These are encoders, not a parallel parser.
    private static void AreaTraversalOwnerOracles(Action<bool,string> check, Action<Action,string> reject)
    {
        static byte[] Id(short kind,int n=0) { var b=new byte[24]; BinaryPrimitives.WriteInt16LittleEndian(b,kind); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8),n); return b; }
        static byte[] Record(long loc)
        {
            var b=new byte[79]; BinaryPrimitives.WriteInt32LittleEndian(b,119); Id(1,7001).CopyTo(b,4); Id(0).CopyTo(b,28);
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(52),3); BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(56),1);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(58),2); b[70]=1; BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(71),loc); return b;
        }
        static byte[] Prototype()
        {
            using var s=new MemoryStream(); WriteU32(s,119); s.Write(Id(-1)); s.Write(Id(1,7001)); WriteU32(s,3); s.Write(new byte[12]);
            int[] optional=[1,5,6,7,15,16,17,30,31,34,35,37,40,41];
            for(var f=0;f<42;f++) if(optional.Contains(f)) s.WriteByte(0); else WriteU32(s,f==0?0x40000000u:f==11?100u:0u);
            return s.ToArray();
        }
        static byte[] Sector(params byte[][] objects)
        {
            using var s=new MemoryStream(); WriteU32(s,0); s.Write(new byte[16384]); WriteU32(s,1); WriteU32(s,0xAA0003);
            WriteU32(s,0); s.Write(new byte[12]); WriteU32(s,2); s.Write(new byte[20]);
            foreach(var o in objects) s.Write(o); WriteU32(s,(uint)objects.Length); return s.ToArray();
        }
        // Deliberately independent literal world/sector expectations, not the source mapper.
        var first=Record(0); var high=Record(130);
        var moduleBytes=CreateSyntheticDat(new(NameBytes("maps/test/67108865.sec"),Sector(first,first)),new(NameBytes("maps/test/67108866.sec"),Sector(high))).Bytes;
        byte[] tile=Encoding.ASCII.GetBytes("{0}{stone}{100}{stone}{200}{stone}{300}{stone}{400}{end}");
        (string Name,byte[] Bytes)[] resourceRows=[("tile.mes",tile),("facade.mes",Encoding.ASCII.GetBytes("{0}{fixture}")),("scenery.mes",Encoding.ASCII.GetBytes("{0}{fixture.art}")),
            ("art/tile/stonebse0a.art",CreateSyntheticArt()),("art/scenery/fixture.art",CreateSyntheticArt()),("art/tile/tilename.mes",Encoding.ASCII.GetBytes("{300}{aaa}"))];
        var resourceBytes=CreateSyntheticDat(resourceRows.Select(r=>new SyntheticDatEntry(NameBytes(r.Name),r.Bytes)).ToArray()).Bytes;
        var prototype=Prototype();
        (AreaInputKey Key,byte[] Bytes)[] files=[(new(AreaSourceRole.Module),moduleBytes),(new(AreaSourceRole.Resource1),resourceBytes),(new(AreaSourceRole.Prototype,7001),prototype)];
        using var resource=DatArchive.Open(new MemoryStream(resourceBytes));
        var mappings=new[]{AreaMappingRole.Tile,AreaMappingRole.Facade,AreaMappingRole.Scenery}.Select((role,i)=>
        {
            var e=resource.Entries[i]; return new AreaMappingBinding(role,new(new(AreaSourceRole.Resource1),i,e.Flags,e.StoredSize,e.DeclaredOutputSize,e.Offset,AreaHash(e.RawNameBytes.ToArray())),AreaHash(resourceRows[i].Bytes));
        });
        var plan=new AreaSourcePlan(1,1,new(1,1),2,"maps/test",files.Select(f=>new AreaInputIdentity(f.Key,f.Bytes.Length,AreaHash(f.Bytes))),mappings);
        AreaDataOwner Owner(AreaLoaderOptions? limits=null, byte[]? changedModule=null) => AreaDataOwner.Open(new(files.Select(f=>new AreaInput(f.Key,new MemoryStream(f.Key.Role==AreaSourceRole.Module?changedModule??f.Bytes:f.Bytes)))),plan,limits ?? new());
        using var owner=Owner(new(){MaxObjects=3}); var snapshot=owner.LoadStrict();
        check(snapshot.Objects.Count==3 && snapshot.Objects.Select(o=>o.Key).Distinct().Count()==3,"T1_T4_OWNER_MULTIPLICITY");
        check(snapshot.Objects.Select(o=>o.WorldX).SequenceEqual(new long?[]{64,64,130}) && snapshot.Objects.All(o=>o.WorldY==64),"T1_OWNER_LOWBIT_REBASE");
        check(snapshot.Objects.All(o=>o.EffectiveFields.Single(f=>f.Slot==18).Origin==AreaValueOrigin.Prototype),"T5_OWNER_INHERITANCE");
        using var module=DatArchive.Open(new MemoryStream(moduleBytes));
        var rows=new List<ResearchComponent>();
        foreach(var s in snapshot.Sectors.Where(s=>s.Present))
        {
            var report=TerrainObstructionInspector.InspectSector(module,"fixture",new($"maps/test/{s.Key.Packed}.sec","test",(int)s.Coordinate.X,(int)s.Coordinate.Y,s.Key.Packed,(int)s.Coordinate.OriginX,(int)s.Coordinate.OriginY,64,64),
                new(resource,"fixture","art/tile/tilename.mes",TerrainPathProfile.NonWindowsMacOs));
            foreach(var t in snapshot.Terrain.Where(t=>t.Key.Sector==s.Key))
            {
                var c=report.GetCell(t.LocalX,t.LocalY); var cell=new ResearchCell(1,c.WorldX,c.WorldY);
                rows.Add(CopyComponent(cell,"Mask",c.SerializedBlockMask,t.RawArt)); rows.Add(CopyComponent(cell,"Facade",c.FacadeWalkabilityBit,t.RawArt)); rows.Add(CopyComponent(cell,"Name",c.TileNameRule,t.RawArt));
            }
        }
        var actor=DeclaredAreaActor(snapshot.Profile) with{RegistryClosed=true}; var selection=Selection(snapshot); var binding=Bind(selection,actor,rows);
        check(binding.SelectedDigest==CanonicalDigest(SelectedArray(selection,rows.AsEnumerable().Reverse().ToArray(),null,"Structural")),"T10_SELECTED_COMPONENT_ORDER");
        var changedRows=rows.ToArray(); changedRows[0]=changedRows[0] with{Reason="NEW_NEIGHBOR_STATE"};
        reject(()=>new ResearchEvidence(binding,actor,selection,changedRows),"T9_NEIGHBOR_STATE_INVALIDATION");
        var evidence=new ResearchEvidence(binding,actor,selection,rows);
        var query=new ResearchQuery(binding,new(1,127,64),new(1,128,64),"XPlus","CoreWithContextEvidence");
        var result=evidence.Assess(query); owner.Dispose(); resource.Dispose(); module.Dispose();
        check(result.Coverage=="Unknown" && result.Disposition is null && result.Gaps.Any(g=>g.Code=="BLOCK_MASK_ABSENT"),"T7_ACTUAL_COMPONENT_GAP");
        check(Canonical(ResultArray(result))==Canonical(ResultArray(evidence.Assess(query))),"T9_OWNER_DISPOSAL_IO_FREE");
        using var capped=Owner(new(){MaxObjects=2}); var cappedReport=capped.Inspect();
        check(!cappedReport.IsComplete && capped.PublishedSnapshot is null && cappedReport.Issues.Any(i=>i.Code==AreaErrorCode.BudgetExceeded),"T8_OWNER_CAP_PLUS_ONE_NO_PUBLICATION");
        using var canceled=Owner(); var token=new CancellationToken(true); reject(()=>canceled.LoadStrict(token),"T8_OWNER_CANCEL");
        reject(()=>Owner(changedModule:moduleBytes[..^1]),"T8_OWNER_SOURCE_IDENTITY");
    }

    private static void CheckTraversalOracle(AreaSnapshot s, JsonElement oracle)
    {
        if (s.Reconciliation != new AreaReconciliation(621,9,11,5,36864,45056,426,7399,88,10,7301,0,7399,56,2444)) throw new InvalidDataException("ORACLE_CENSUS");
        var sectors = oracle.GetProperty("sectors").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetInt64());
        foreach (var row in s.Sectors.Where(x=>x.Present))
        {
            var x=sectors[row.Key.Packed]; var e=x.GetProperty("entry");
            if (row.PayloadSha256!=x.GetProperty("payload_sha256").GetString() || row.DatOrdinal!=e.GetProperty("ordinal").GetInt32() || row.Entry!.NameSha256!=e.GetProperty("name_sha256").GetString() || row.TerminalCount!=x.GetProperty("framing").GetProperty("terminal").GetInt32()) throw new InvalidDataException("ORACLE_SECTOR");
        }
        var statics=oracle.GetProperty("statics").EnumerateArray().ToDictionary(x=>(x.GetProperty("sector_id").GetInt64(),x.GetProperty("ordinal").GetInt32()));
        var mobiles=oracle.GetProperty("mobiles").EnumerateArray().ToDictionary(x=>x.GetProperty("index").GetInt32());
        foreach(var o in s.Objects)
        {
            var x=o.Key.Kind==AreaSourceKind.Static?statics[(o.Key.Sector!.Value,o.Key.SourceIndex)]:mobiles[o.Key.SourceIndex];
            if(o.RawSha256!=x.GetProperty("sha256").GetString() || o.SemanticSha256!=x.GetProperty("semantic_sha256").GetString() || o.Key.ObjectIdSha256!=x.GetProperty("own_sha256").GetString() || o.Key.Offset!=x.GetProperty("offset").GetInt64() || o.Key.Length!=x.GetProperty("length").GetInt32() || o.Type!=x.GetProperty("type").GetInt32() || o.WorldX!=x.GetProperty("world_x").GetInt64() || o.WorldY!=x.GetProperty("world_y").GetInt64()) throw new InvalidDataException("ORACLE_OCCURRENCE");
        }
        var terrain=oracle.GetProperty("terrain").EnumerateArray().ToDictionary(x=>(x[0].GetInt64(),x[1].GetInt32()));
        foreach(var t in s.Terrain)
        {
            var x=terrain[(t.Key.Sector.Packed,t.Key.SourceIndex)];
            if(t.RawArt!=x[2].GetUInt32() || t.WorldX!=x[4].GetInt64() || t.WorldY!=x[5].GetInt64()) throw new InvalidDataException("ORACLE_TERRAIN");
        }
    }
}
