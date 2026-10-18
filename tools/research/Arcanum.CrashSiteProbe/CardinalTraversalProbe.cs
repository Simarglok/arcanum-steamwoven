using System.Buffers.Binary;
using System.Collections.ObjectModel;
using Arcanum.Data;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.CrashSiteProbe;

// Independently authored evidence composition, not a port of the source traversal routine.
// Nothing here is a production admission API or an installed-game snapshot.
internal static class CardinalTraversalProbe
{
    private const string Profile = "BASE_SECTOR_1452_1290";
    private const string ContextId = "ISOLATED_PC_CRITTER_ART_NONPOLYMORPH";
    private const string Source = "docs/world/cardinal-traversal-contract.md";
    private enum Coverage { Supported, Unknown, Unsupported }
    private enum Applicability { Applicable, NotApplicable, Unknown }
    private enum Disposition { Open, Blocked }
    private readonly record struct Cell(int X, int Y);
    private sealed record Context(int Kind = 15, int ArtClass = 2, uint SpellFlags = 0,
        bool OrdinaryGround = true, bool NonEditor = true, bool IsolatedRegistry = true,
        uint PathFlags = 0, uint BeginFlags = 0x400);
    private sealed record Request(string ProfileId, string InputId, string ContextId,
        int SectorX, int SectorY, Cell From, Cell To, int Direction, Context Actor);
    private sealed record Row(string Id, Applicability Applicability, Coverage Coverage,
        Disposition? Result, string Numbers, string Reason, string Provenance, bool Unconditional = false);
    private sealed record Candidate(int Index, int Type, uint? DirectFlags, uint? EffectiveFlags,
        bool Inherited, long? Location, int? Prototype, uint? SpellFlags = null);
    private sealed record TerrainFact(uint Raw, uint MaskWord, Row Mask, Row Facade, Row Name);
    private sealed record Assessment(Coverage Coverage, Disposition? Result, string Reason,
        IReadOnlyList<Row> Rows, IReadOnlyList<string> Gaps);

    private sealed class Snapshot
    {
        internal Snapshot(string inputId, Context actor, Cell zoneMin, Cell zoneMax,
            IEnumerable<TerrainFact> terrain, IEnumerable<Candidate> records, int terminalCount,
            bool registration = true, bool extent = true)
        {
            InputId = inputId; Actor = actor; ZoneMin = zoneMin; ZoneMax = zoneMax;
            Terrain = Array.AsReadOnly(terrain.ToArray()); Records = Array.AsReadOnly(records.ToArray());
            TerminalCount = terminalCount; Registration = registration; Extent = extent;
            if (Terrain.Count != 4096 || terminalCount < 0 || terminalCount > 4096 || Records.Count > 4096)
                throw new InvalidDataException("CARDINAL_SNAPSHOT_SHAPE_INVALID");
        }
        internal string InputId { get; }
        internal Context Actor { get; }
        internal Cell ZoneMin { get; }
        internal Cell ZoneMax { get; }
        internal ReadOnlyCollection<TerrainFact> Terrain { get; }
        internal ReadOnlyCollection<Candidate> Records { get; }
        internal int TerminalCount { get; }
        internal bool Registration { get; }
        internal bool Extent { get; }
    }

    private static Row Open(string id, string numbers, string reason, string provenance = Source) =>
        new(id, Applicability.Applicable, Coverage.Supported, Disposition.Open, numbers, reason, provenance);
    private static Row Absent(string id, string numbers, string reason, string provenance = Source) =>
        new(id, Applicability.NotApplicable, Coverage.Supported, null, numbers, reason, provenance);
    private static Row Gap(string id, string numbers, string reason) =>
        new(id, Applicability.Unknown, Coverage.Unknown, null, numbers, reason, Source);
    private static Row Block(string id, string numbers, string reason) =>
        new(id, Applicability.Applicable, Coverage.Supported, Disposition.Blocked, numbers, reason, Source, true);

    private static uint DerivedFlags(Context actor)
    {
        // Source worksheet: initial flags zero; PC portal=true, current critter ART window=true.
        var portal = actor.Kind == 15;
        var window = actor.ArtClass == 2;
        return ((actor.SpellFlags & 0x20000) != 0 ? 3u : 0u) | (!portal ? 1u : 0u) | (!window ? 2u : 0u);
    }

    private static bool InSector(Cell cell) => (uint)cell.X < 64 && (uint)cell.Y < 64;
    private static bool InZone(Snapshot snapshot, Cell cell) => cell.X >= snapshot.ZoneMin.X && cell.X <= snapshot.ZoneMax.X &&
        cell.Y >= snapshot.ZoneMin.Y && cell.Y <= snapshot.ZoneMax.Y;
    private static bool TryLocal(long? location, out Cell cell)
    {
        cell = default;
        if (location is not long value) return false;
        var x = (long)unchecked((int)(uint)value) - 92928;
        var y = (long)unchecked((int)(value >> 32)) - 82560;
        if (x is < 0 or >= 64 || y is < 0 or >= 64) return false;
        cell = new((int)x, (int)y); return true;
    }

    private static Assessment Assess(Snapshot snapshot, Request request)
    {
        Assessment Reject(Coverage coverage, string reason) => new(coverage, null, reason, Array.Empty<Row>(),
            coverage == Coverage.Unknown ? Array.AsReadOnly(new[] { reason }) : Array.Empty<string>());
        if (request.ProfileId != Profile || request.InputId != snapshot.InputId || request.ContextId != ContextId ||
            request.SectorX != 1452 || request.SectorY != 1290)
            return Reject(Coverage.Unsupported, "REQUEST_IDENTITY_UNSUPPORTED");
        if (!InSector(request.From) || !InSector(request.To)) return Reject(Coverage.Unsupported, "CROSS_SECTOR_UNSUPPORTED");
        if (!InZone(snapshot, request.From) || !InZone(snapshot, request.To)) return Reject(Coverage.Unsupported, "ZONE_UNSUPPORTED");
        if (request.Actor != snapshot.Actor || !request.Actor.OrdinaryGround || !request.Actor.NonEditor || !request.Actor.IsolatedRegistry ||
            request.Actor.Kind != 15 || request.Actor.ArtClass != 2 || request.Actor.SpellFlags != 0 || request.Actor.BeginFlags != 0x400)
            return Reject(Coverage.Unsupported, "ACTOR_STATE_UNSUPPORTED");
        var dx = (long)request.To.X - request.From.X; var dy = (long)request.To.Y - request.From.Y;
        if (dx == 0 && dy == 0) return Reject(Coverage.Unsupported, "SAME_CELL_UNSUPPORTED");
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return Reject(Coverage.Unsupported, "NONADJACENT_UNSUPPORTED");
        if ((request.Actor.PathFlags & 0x1000) != 0) return Reject(Coverage.Unknown, "PATH_FLAG_0X1000_UNAVAILABLE");
        if (request.Actor.PathFlags != 0) return Reject(Coverage.Unsupported, "PATH_STATE_UNSUPPORTED");
        if (dx != 0 && dy != 0) return Reject(Coverage.Unknown, "DIAGONAL_CORNER_UNPROVED");
        var direction = dx < 0 ? 1 : dy > 0 ? 3 : dx > 0 ? 5 : 7;
        if (request.Direction != direction) return Reject(Coverage.Unsupported, "DIRECTION_MISMATCH");

        var rows = new List<Row>();
        rows.Add(Open("ACTOR", $"kind=15,art_class=2,spell=0,path=0,begin=1024,traversal={DerivedFlags(request.Actor)}",
            "DECLARED_TUPLE_DERIVED_FLAGS", "PROJECT_CONTEXT_AND_PINNED_HELPERS"));
        var fromOrdinal = request.From.X + 64 * request.From.Y;
        var toOrdinal = request.To.X + 64 * request.To.Y;
        var from = snapshot.Terrain[fromOrdinal]; var to = snapshot.Terrain[toOrdinal];
        foreach (var component in new[] { from.Mask, from.Facade, from.Name })
            rows.Add(Absent("FROM_" + component.Id, component.Numbers,
                "SOURCE_TILE_NOT_INVOKED_" + component.Reason));
        rows.AddRange(new[] { to.Mask, to.Facade, to.Name });
        rows.Add(snapshot.Registration ? Open("REGISTRATION", "exact_cell=1", "LOCATION_LIST_PROVEN") : Gap("REGISTRATION", "exact_cell=null", "REGISTRATION_UNPROVED"));
        rows.Add(snapshot.Extent ? Open("EXTENT", "neighbor_footprint=0", "EXACT_CELL_NO_VISUAL_EXTENT") : Gap("EXTENT", "neighbor_footprint=null", "EXTENT_UNPROVED"));
        var censusComplete = snapshot.Records.Count == snapshot.TerminalCount && snapshot.Records.Select(r => r.Index).Distinct().Count() == snapshot.Records.Count;
        rows.Add(censusComplete ? Open("CENSUS", $"records={snapshot.Records.Count},terminal={snapshot.TerminalCount}", "FULL_TERMINAL_CENSUS") :
            Gap("CENSUS", $"records={snapshot.Records.Count},terminal={snapshot.TerminalCount}", "CENSUS_INCOMPLETE"));
        // A destination scenery fact is not a whole-context blocker if an unproved
        // source portal/AI callback could precede it. Absence needs ALL types, not flags.
        var allScenery = censusComplete && snapshot.Records.All(r => r.Type == 3);

        // Survey ALL records before querying cells: filtered subset emptiness is not closure.
        foreach (var record in snapshot.Records.OrderBy(r => r.Index))
        {
            var flags = record.DirectFlags ?? record.EffectiveFlags;
            var numeric = $"index={record.Index},type={record.Type},flags={(flags.HasValue ? flags.Value.ToString() : "null")},flags_source={(record.DirectFlags.HasValue ? "Direct" : record.Inherited ? "Prototype" : "Unresolved")},prototype={record.Prototype?.ToString() ?? "null"}";
            if (!TryLocal(record.Location, out var local))
            { rows.Add(Gap($"RECORD_{record.Index}", numeric, "UNMASKED_LOCATION_UNRESOLVED")); continue; }
            numeric += $",local={local.X}:{local.Y}";
            if (!flags.HasValue || (!record.DirectFlags.HasValue && !record.Inherited))
            { rows.Add(Gap($"RECORD_{record.Index}", numeric, "FLAGS_UNRESOLVED")); continue; }
            if (record.Type != 3 || (flags.Value & 0x2000) != 0)
            { rows.Add(Gap($"RECORD_{record.Index}", numeric, "OTHER_TYPE_OR_DYNAMIC_UNADMITTED")); continue; }
            if ((flags.Value & 0x1003) != 0)
            { rows.Add(Absent($"RECORD_{record.Index}", numeric, "INVENTORY_OFF_DESTROYED_NOT_IN_QUERY")); continue; }
            if (local != request.To)
            { rows.Add(Absent($"RECORD_{record.Index}", numeric, "SCENERY_NOT_DESTINATION_OR_SOURCE_MASK")); continue; }
            rows.Add((flags.Value & 0x400) == 0 && snapshot.Registration && snapshot.Extent
                ? Block($"RECORD_{record.Index}", numeric, "DESTINATION_SCENERY_BLOCKER") with { Unconditional = allScenery }
                : !snapshot.Registration || !snapshot.Extent
                    ? Gap($"RECORD_{record.Index}", numeric, "SCENERY_MEMBERSHIP_UNPROVED")
                    : Open($"RECORD_{record.Index}", numeric, "DESTINATION_SCENERY_NO_BLOCK"));
        }
        rows.Add(allScenery ? Absent("SOURCE_EDGES", "mask=3,count=0", "SERIALIZED_WALL_PORTAL_ABSENT") :
            Gap("SOURCE_EDGES", "mask=3,count=null", "SOURCE_EDGE_CENSUS_UNADMITTED"));
        rows.Add(allScenery ? Absent("DESTINATION_OTHER_TYPES", "mask=229407,count=0", "SERIALIZED_NONSCENERY_ABSENT") :
            Gap("DESTINATION_OTHER_TYPES", "mask=229407,count=null", "DESTINATION_TYPE_CENSUS_UNADMITTED"));
        rows.Add(Absent("SUPPLEMENTAL_REGISTRY", "count=0", "DECLARED_EMPTY_NOT_INSTALLED_GAME_ABSENCE", "PROJECT_DIAGNOSTIC_INPUT_POLICY"));
        rows.Add(Absent("ACTOR_SELF", "destination_count=0", "ISOLATED_QUERY_ACTOR_AT_SOURCE_PC_NOT_SOURCE_MASK", "PROJECT_POLICY_AND_EXACT_CELL_SOURCE"));
        rows.Add(Absent("ROUTE_TRAP_FIRE_WINDOW", "invoked=0", "NOT_CALLED_BY_ADJACENT_ANIMATION_PREDICATE"));
        rows.Add(Absent("SCRIPT_EXECUTION", "invoked=0", "NO_SCRIPT_CALL_IN_SELECTED_PREDICATE", "SOURCE_AND_PROJECT_NO_EXECUTION_POLICY"));
        var gaps = rows.Where(r => r.Coverage != Coverage.Supported || r.Applicability == Applicability.Unknown).Select(r => r.Id + ":" + r.Reason).ToArray();
        var blocker = rows.Any(r => r.Coverage == Coverage.Supported && r.Applicability == Applicability.Applicable && r.Result == Disposition.Blocked && r.Unconditional);
        return new(blocker ? Coverage.Supported : gaps.Length != 0 ? Coverage.Unknown : Coverage.Supported,
            blocker ? Disposition.Blocked : gaps.Length != 0 ? null : Disposition.Open,
            blocker ? "WHOLE_CONTEXT_UNCONDITIONAL_BLOCKER" : gaps.Length != 0 ? "COVERAGE_GAPS" : "COMPLETE_NONBLOCKING_CLOSURE",
            Array.AsReadOnly(rows.ToArray()), Array.AsReadOnly(gaps));
    }

    private static Row FromTerrain(string id, TerrainObstructionComponentResult result, string numbers) =>
        new(id, (Applicability)(int)result.Applicability,
            result.Supportedness == TerrainObstructionSupportedness.Supported ? Coverage.Supported : Coverage.Unknown,
            result.Disposition == TerrainObstructionDisposition.Blocked ? Disposition.Blocked : result.Disposition == TerrainObstructionDisposition.NoBlock ? Disposition.Open : null,
            numbers, result.ReasonCode, $"{result.Provenance.Kind}:{result.Provenance.SourceId}:{result.Provenance.EntryOrdinal?.ToString() ?? "null"}",
            result.Disposition == TerrainObstructionDisposition.Blocked);

    private static TerrainFact[] ProjectTerrain(TerrainObstructionSectorReport report, byte[] bytes, int tileStart, int maskStart)
    {
        var facts = new TerrainFact[4096];
        for (var i = 0; i < facts.Length; i++)
        {
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(tileStart + i * 4, 4));
            var word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(maskStart + (i / 32) * 4, 4));
            var cell = report.GetCell(i % 64, i / 64);
            facts[i] = new(raw, word,
                FromTerrain("TO_MASK", cell.SerializedBlockMask, $"ordinal={i},word_index={i / 32},bit={i % 32},word={word}"),
                FromTerrain("TO_FACADE", cell.FacadeWalkabilityBit, $"raw={raw},class={raw >> 28},bit0={raw & 1}"),
                FromTerrain("TO_NAME", cell.TileNameRule, $"raw={raw},category_bits={(raw >> 7) & 3},material={(raw >> 22) & 63}"));
        }
        return facts;
    }

    internal static void Emit(TerrainObstructionSectorReport terrain, StaticObstructionSectorReport staticReport,
        StaticObjectInspection inspection, byte[] bytes, int tileStart, int maskStart, int tileScripts, int sectorScriptBytes,
        string moduleSha, string resourceSha, string prototypeAggregate)
    {
        if (inspection.CensusStatus != StaticObjectCensusStatus.Complete || staticReport.Records.Count != inspection.Records.Count ||
            staticReport.Sector != terrain.Sector)
            throw new InvalidDataException("CARDINAL_PRODUCTION_CENSUS_MISMATCH");
        var candidates = inspection.Records.Select(r => new Candidate(r.SourceIndex, r.ObjectType, r.DirectFlags,
            r.EffectiveFlags, r.FlagsInherited, r.Location, r.PrototypeAid)).ToArray();
        foreach (var candidate in candidates)
        {
            var component = staticReport.Records.Single(r => r.SourceIndex == candidate.Index);
            if (component.EffectiveFlags != (candidate.DirectFlags ?? candidate.EffectiveFlags) ||
                (TryLocal(candidate.Location, out var local) && candidate.Type == 3 && component.Destination != new StaticObstructionLocalCell(local.X, local.Y)))
                throw new InvalidDataException("CARDINAL_COMPONENT_PROJECTION_MISMATCH");
            if (candidate.Type == 3 && TryLocal(candidate.Location, out _) &&
                (candidate.DirectFlags ?? candidate.EffectiveFlags) is uint flags && (flags & 0x3003) == 0 &&
                (component.Result.Supportedness != StaticObstructionSupportedness.Supported ||
                 component.Result.Applicability != StaticObstructionApplicability.Applicable ||
                 component.Result.Disposition != ((flags & 0x400) == 0 ? StaticObstructionDisposition.Blocker : StaticObstructionDisposition.NoBlock)))
                throw new InvalidDataException("CARDINAL_STATIC_COMPONENT_RESULT_MISMATCH");
        }
        var actor = new Context();
        var facts = ProjectTerrain(terrain, bytes, tileStart, maskStart);
        var inputId = moduleSha + ":" + resourceSha + ":" + prototypeAggregate;
        var snapshot = new Snapshot(inputId, actor, new(4, 0), new(8, 3), facts, candidates, inspection.TerminalRecordCount);
        Console.WriteLine($"cardinal identity profile={Profile} context={ContextId} module_sha256={moduleSha} resource_sha256={resourceSha} probe_prototypes_id_length_sha256={prototypeAggregate}");
        Console.WriteLine($"cardinal census terminal={snapshot.TerminalCount} full_records={candidates.Length} types={string.Join(',', candidates.GroupBy(r => r.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))} strict_locations={candidates.Count(r => TryLocal(r.Location, out _))} direct_flags={candidates.Count(r => r.DirectFlags.HasValue)} prototype_flags={candidates.Count(r => !r.DirectFlags.HasValue && r.Inherited)} tile_scripts={tileScripts} sector_script_bytes={sectorScriptBytes}");
        Console.WriteLine($"cardinal context kind=15 current_art_class=2 polymorph=false portal=true window=true derived_flags={DerivedFlags(actor)} path_flags=0 begin_flags=1024 supplemental=0 actor_self=source_only executable=NOT_VERIFIED");
        var chosen = new[] { (new Cell(6, 1), new Cell(5, 1)), (new Cell(5, 1), new Cell(6, 1)),
            (new Cell(6, 1), new Cell(7, 1)), (new Cell(7, 1), new Cell(6, 1)) };
        var assessed = chosen.Select(pair => (pair.Item1, pair.Item2, Result: Assess(snapshot, MakeRequest(snapshot, pair.Item1, pair.Item2)))).ToArray();
        if (!PairComplete(assessed.Select(r => r.Result)))
        {
            Console.WriteLine("cardinal selection first_zone=exhaustive_deterministic reason=PROPOSED_PAIR_NOT_FULLY_ADMITTED");
            assessed = Enumerate(snapshot).Select(pair => (pair.From, pair.To, Result: Assess(snapshot, MakeRequest(snapshot, pair.From, pair.To))))
                .ToArray();
        }
        foreach (var row in assessed) EmitAssessment(snapshot, row.Item1, row.Item2, row.Result);
        if (!PairComplete(assessed.Select(r => r.Result)))
        {
            Console.WriteLine("cardinal selection alternative_zone=42..46,0..3 reason=FIRST_ZONE_NO_COMPLETE_PAIR");
            snapshot = new Snapshot(inputId, actor, new(42, 0), new(46, 3), facts, candidates, inspection.TerminalRecordCount);
            var alternative = Enumerate(snapshot).Select(pair => (pair.From, pair.To, Result: Assess(snapshot, MakeRequest(snapshot, pair.From, pair.To)))).ToArray();
            foreach (var row in alternative) EmitAssessment(snapshot, row.From, row.To, row.Result);
            if (!PairComplete(alternative.Select(r => r.Result))) throw new InvalidDataException("CARDINAL_BOUNDED_PAIR_NOT_ADMITTED");
        }
        Console.WriteLine("cardinal admitted_open_and_blocked=true gaps=0 scope=diagnostic_source_data_only");
    }

    private static bool PairComplete(IEnumerable<Assessment> rows) => rows.Any(r => r.Coverage == Coverage.Supported && r.Result == Disposition.Open && r.Gaps.Count == 0) &&
        rows.Any(r => r.Coverage == Coverage.Supported && r.Result == Disposition.Blocked && r.Gaps.Count == 0);
    private static IEnumerable<(Cell From, Cell To)> Enumerate(Snapshot snapshot)
    {
        for (var y = snapshot.ZoneMin.Y; y <= snapshot.ZoneMax.Y; y++)
            for (var x = snapshot.ZoneMin.X; x <= snapshot.ZoneMax.X; x++)
                foreach (var delta in new[] { new Cell(-1, 0), new Cell(0, 1), new Cell(1, 0), new Cell(0, -1) })
                { var to = new Cell(x + delta.X, y + delta.Y); if (InZone(snapshot, to)) yield return (new(x, y), to); }
    }
    private static Request MakeRequest(Snapshot snapshot, Cell from, Cell to) => new(Profile, snapshot.InputId, ContextId, 1452, 1290, from, to,
        to.X < from.X ? 1 : to.Y > from.Y ? 3 : to.X > from.X ? 5 : 7, snapshot.Actor);
    private static void EmitAssessment(Snapshot snapshot, Cell from, Cell to, Assessment result)
    {
        Console.WriteLine($"cardinal row from={from.X},{from.Y} to={to.X},{to.Y} world_from={92928 + from.X},{82560 + from.Y} world_to={92928 + to.X},{82560 + to.Y} ordinal_from={from.X + 64 * from.Y} ordinal_to={to.X + 64 * to.Y} direction={MakeRequest(snapshot, from, to).Direction} coverage={result.Coverage} disposition={result.Result?.ToString() ?? "null"} gaps={result.Gaps.Count} reason={result.Reason}");
        foreach (var row in result.Rows.Where(r => !r.Id.StartsWith("RECORD_", StringComparison.Ordinal) || r.Applicability != Applicability.NotApplicable || r.Reason != "SCENERY_NOT_DESTINATION_OR_SOURCE_MASK"))
            Console.WriteLine($"cardinal component id={row.Id} applicability={row.Applicability} coverage={row.Coverage} result={row.Result?.ToString() ?? "null"} numeric={row.Numbers} reason={row.Reason} provenance={row.Provenance}");
        Console.WriteLine($"cardinal full_record_rows_retained={result.Rows.Count(r => r.Id.StartsWith("RECORD_", StringComparison.Ordinal))} gaps={string.Join(',', result.Gaps)}");
    }

    internal static void SelfTest()
    {
        var actor = new Context();
        var open = new TerrainFact(0, 0, Open("TO_MASK", "word=0", "SERIALIZED_BLOCK_MASK"),
            Absent("TO_FACADE", "class=0", "FACADE_NOT_APPLICABLE"), Open("TO_NAME", "material=0", "TILE_NAME_RULE_NO_BLOCK"));
        var facts = Enumerable.Repeat(open, 4096).ToArray();
        var clear = new Candidate(0, 3, 1024, 0, false, Packed(6, 1), 7001, 0x2000);
        Snapshot Fixture(Candidate[] records, bool registration = true, bool extent = true, Context? context = null, TerrainFact[]? cells = null, int? terminal = null) =>
            new("SYNTHETIC", context ?? actor, new(4, 0), new(8, 3), cells ?? facts, records, terminal ?? records.Length, registration, extent);
        var count = 0;
        void Check(Snapshot s, Request r, Coverage coverage, Disposition? disposition, string label, bool gaps = false)
        {
            var result = Assess(s, r);
            if (result.Coverage != coverage || result.Result != disposition || (result.Gaps.Count != 0) != gaps)
                throw new InvalidOperationException("CARDINAL_FIXTURE_" + label);
            count++;
        }
        void Standard(Snapshot s, Coverage coverage, Disposition? disposition, string label, bool gaps = false) =>
            Check(s, MakeRequest(s, new(6, 1), new(5, 1)), coverage, disposition, label, gaps);
        var s = Fixture([clear]);
        Standard(s, Coverage.Supported, Disposition.Open, "COMPLETE_OPEN");
        var blocker = clear with { Index = 1, DirectFlags = 0, EffectiveFlags = 1024, Location = Packed(5, 1) };
        Standard(Fixture([clear, blocker]), Coverage.Supported, Disposition.Blocked, "DIRECT_ZERO_OVERRIDE");
        Standard(Fixture([blocker with { DirectFlags = null, EffectiveFlags = 0, Inherited = true }]), Coverage.Supported, Disposition.Blocked, "PROTOTYPE_BLOCKER");
        Standard(Fixture([blocker with { DirectFlags = null, EffectiveFlags = 1024, Inherited = true }]), Coverage.Supported, Disposition.Open, "PROTOTYPE_NOBLOCK");
        foreach (var flags in new uint[] { 1, 2, 0x1000, 0x400 })
            Standard(Fixture([blocker with { DirectFlags = flags }]), Coverage.Supported, Disposition.Open, "DEFEATED_OR_INAPPLICABLE");
        foreach (var missing in new[] { clear with { EffectiveFlags = null, DirectFlags = null }, clear with { Location = null },
            clear with { Location = Packed(70, 1) }, clear with { Location = long.MinValue }, clear with { Type = 0 }, clear with { Type = 17, Location = null },
            clear with { DirectFlags = 0x2000 }, clear with { DirectFlags = null, EffectiveFlags = 0, Inherited = false } })
            Standard(Fixture([missing]), Coverage.Unknown, null, "HIDDEN_MISSING_OR_UNSUPPORTED", true);
        Standard(Fixture([clear], terminal: 2), Coverage.Unknown, null, "SUBSET_CENSUS", true);
        Standard(Fixture([clear], registration: false), Coverage.Unknown, null, "REGISTRATION", true);
        Standard(Fixture([clear], extent: false), Coverage.Unknown, null, "EXTENT", true);
        Standard(Fixture([clear, clear with { Index = 1 }]), Coverage.Supported, Disposition.Open, "MULTIPLE_NOBLOCK");
        Standard(Fixture([blocker, clear with { Index = 2, Location = Packed(5, 1) }]), Coverage.Supported, Disposition.Blocked, "MULTIPLE_BLOCKER");
        Standard(Fixture([blocker, clear with { Index = 2, DirectFlags = null, EffectiveFlags = null, Location = Packed(5, 1) }]), Coverage.Supported, Disposition.Blocked, "BLOCKER_RETAINS_GAP", true);
        foreach (var reason in new[] { "TILE_NAME_ENTRY_MISSING", "TILE_NAME_ENTRY_AMBIGUOUS", "TILE_NAME_MAPPING_UNRESOLVED", "TILE_NAME_VALUE_UNSUPPORTED" })
        {
            var unknown = facts.ToArray(); unknown[69] = open with { Name = Gap("TO_NAME", "material=0", reason) };
            Standard(Fixture([clear], cells: unknown), Coverage.Unknown, null, reason, true);
        }
        foreach (var to in new[] { new Cell(5, 1), new Cell(6, 2), new Cell(7, 1), new Cell(6, 0) })
        {
            Check(s, MakeRequest(s, new(6, 1), to), Coverage.Supported, Disposition.Open, "CARDINAL");
            Check(s, MakeRequest(s, to, new(6, 1)), Coverage.Supported, Disposition.Open, "REVERSE");
        }
        var request = MakeRequest(s, new(6, 1), new(5, 1));
        foreach (var invalid in new[] { request with { ProfileId = "OTHER" }, request with { InputId = "OTHER" }, request with { ContextId = "OTHER" },
            request with { SectorX = 1453 }, request with { Direction = 5 }, request with { To = new(3, 1) }, request with { To = new(64, 1) },
            request with { To = new(int.MaxValue, int.MinValue) }, request with { To = new(6, 1) }, request with { To = new(8, 1) },
            request with { Actor = actor with { OrdinaryGround = false } }, request with { Actor = actor with { NonEditor = false } },
            request with { Actor = actor with { Kind = 16 } }, request with { Actor = actor with { ArtClass = 1 } }, request with { Actor = actor with { SpellFlags = 0x20000 } },
            request with { Actor = actor with { IsolatedRegistry = false } }, request with { Actor = actor with { BeginFlags = 0 } } })
            Check(s, invalid, Coverage.Unsupported, null, "BOUNDARY_OR_CONTEXT");
        foreach (var to in new[] { new Cell(5, 0), new Cell(5, 2), new Cell(7, 0), new Cell(7, 2) })
            Check(s, request with { To = to }, Coverage.Unknown, null, "DIAGONAL", true);
        var flagged = Fixture([clear], context: actor with { PathFlags = 0x1000 });
        Standard(flagged, Coverage.Unknown, null, "PATH_1000", true);
        Standard(Fixture([clear], context: actor with { PathFlags = 1 }), Coverage.Unsupported, null, "OTHER_PATH_STATE");
        Standard(Fixture([clear, clear]), Coverage.Unknown, null, "DUPLICATE_RECORD_INDEX", true);
        var maskFacts = facts.ToArray(); maskFacts[69] = open with { Mask = Block("TO_MASK", "word=32,bit=5", "SERIALIZED_BLOCK_MASK") };
        Standard(Fixture([clear], cells: maskFacts), Coverage.Supported, Disposition.Blocked, "UNCONDITIONAL_MASK");
        maskFacts[69] = maskFacts[69] with { Name = Gap("TO_NAME", "", "UNRESOLVED_NAME") };
        Standard(Fixture([clear], cells: maskFacts), Coverage.Supported, Disposition.Blocked, "MASK_RETAINS_NAME_GAP", true);
        Standard(Fixture([blocker], registration: false), Coverage.Unknown, null, "UNREGISTERED_BLOCKER", true);
        Standard(Fixture([blocker], terminal: 2), Coverage.Unknown, null, "BLOCKER_WITH_HIDDEN_SOURCE_TYPE", true);
        Standard(Fixture([blocker, clear with { Index = 2, Type = 1 }]), Coverage.Unknown, null, "BLOCKER_WITH_UNADMITTED_PORTAL", true);
        var mutable = new[] { blocker }; var owned = Fixture(mutable); mutable[0] = clear; facts[69] = open with { Name = Gap("TO_NAME", "", "MUTATED") };
        Standard(owned, Coverage.Supported, Disposition.Blocked, "OWNED_BUFFER_BLOCKED");
        if (owned.Actor != actor || DerivedFlags(actor) != 0) throw new InvalidOperationException("CARDINAL_CONTEXT_MUTATED");
        _ = Assess(owned, MakeRequest(owned, new(5, 1), new(6, 1))); // return/finish queries do not advance or alter a context.
        Standard(owned, Coverage.Supported, Disposition.Blocked, "RETURN_FINISH_IMMUTABLE");
        ProductionTerrainFixtureChecks();
        Console.WriteLine($"cardinal self-test PASS literal_cases={count} production_terrain_cases=10 same_assessment_seam=true");
    }
    private static long Packed(int x, int y) => unchecked((long)((ulong)(uint)(82560 + y) << 32 | (uint)(92928 + x)));

    internal static void AssertParsedFlags(uint parsedFlags, bool expectedOpen, bool dynamic)
    {
        var clear = new TerrainFact(0, 0, Open("TO_MASK", "word=0", "SERIALIZED_BLOCK_MASK"),
            Absent("TO_FACADE", "class=0", "FACADE_NOT_APPLICABLE"), Open("TO_NAME", "material=0", "TILE_NAME_RULE_NO_BLOCK"));
        var snapshot = new Snapshot("SYNTHETIC", new(), new(4, 0), new(8, 3), Enumerable.Repeat(clear, 4096),
            [new(0, 3, parsedFlags, null, false, Packed(5, 1), 7001)], 1);
        var result = Assess(snapshot, MakeRequest(snapshot, new(6, 1), new(5, 1)));
        if (result.Coverage != (dynamic ? Coverage.Unknown : Coverage.Supported) ||
            result.Result != (dynamic ? null : expectedOpen ? Disposition.Open : Disposition.Blocked))
            throw new InvalidOperationException("CARDINAL_PARSED_SLOT18_OR_DEFAULT_REGRESSION");
    }

    private static void ProductionTerrainFixtureChecks()
    {
        // Synthetic archive/sector envelopes, no original content or parser implementation.
        var sector = new byte[16952];
        BinaryPrimitives.WriteInt32LittleEndian(sector.AsSpan(16388), 1);
        BinaryPrimitives.WriteInt32LittleEndian(sector.AsSpan(16392), 0xAA0004);
        BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(4 + 69 * 4), 0x180); // category0/material0
        using var module = DatArchive.Open(new MemoryStream(FixtureDat("maps\\fixture\\test.sec", sector)), leaveOpen: false);
        foreach (var (value, expected) in new[] { ("abc", Disposition.Open), ("abc/b", Disposition.Blocked), ("abc/q", (Disposition?)null), ("abc/f", Disposition.Blocked) })
        {
            using var resource = DatArchive.Open(new MemoryStream(FixtureDat("art\\tile\\tilename.mes", System.Text.Encoding.ASCII.GetBytes("{0}{" + value + "}"))), leaveOpen: false);
            var selector = new TerrainSectorSelector("maps\\fixture\\test.sec", "fixture", 1452, 1290, 86570436012, 92928, 82560, 64, 64);
            var report = TerrainObstructionInspector.InspectSector(module, "SYNTHETIC_MODULE", selector,
                new TerrainTileNameSource(resource, "SYNTHETIC_RESOURCE", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
            var snapshot = new Snapshot("SYNTHETIC", new(), new(4, 0), new(8, 3), ProjectTerrain(report, sector, 4, 16436), [], 0);
            var result = Assess(snapshot, MakeRequest(snapshot, new(6, 1), new(5, 1)));
            if (result.Result != expected || result.Coverage != (expected.HasValue ? Coverage.Supported : Coverage.Unknown))
                throw new InvalidOperationException("CARDINAL_PRODUCTION_TERRAIN_FIXTURE");
        }
        // Real production parsing of missing/ambiguous names, not a forged result row.
        foreach (var names in new[] { new[] { "other.mes" }, new[] { "art\\tile\\tilename.mes", "ART/TILE/TILENAME.MES" } })
        {
            using var resource = DatArchive.Open(new MemoryStream(FixtureDat(names, "{0}{abc}"u8.ToArray())), leaveOpen: false);
            var report = TerrainObstructionInspector.InspectSector(module, "SYNTHETIC_MODULE",
                new("maps\\fixture\\test.sec", "fixture", 1452, 1290, 86570436012, 92928, 82560, 64, 64),
                new(resource, "SYNTHETIC_RESOURCE", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
            var snapshot = new Snapshot("SYNTHETIC", new(), new(4, 0), new(8, 3), ProjectTerrain(report, sector, 4, 16436), [], 0);
            var result = Assess(snapshot, MakeRequest(snapshot, new(6, 1), new(5, 1)));
            if (result.Coverage != Coverage.Unknown || result.Result is not null) throw new InvalidOperationException("CARDINAL_NAME_SELECTION_FIXTURE");
        }
        foreach (var facade in new uint[] { 0xB0000000, 0xB0000001 })
        {
            var facadeSector = sector.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(facadeSector.AsSpan(4 + 69 * 4), facade);
            using var facadeModule = DatArchive.Open(new MemoryStream(FixtureDat("maps\\fixture\\test.sec", facadeSector)), leaveOpen: false);
            var report = TerrainObstructionInspector.InspectSector(facadeModule, "SYNTHETIC_MODULE",
                new("maps\\fixture\\test.sec", "fixture", 1452, 1290, 86570436012, 92928, 82560, 64, 64), null);
            var snapshot = new Snapshot("SYNTHETIC", new(), new(4, 0), new(8, 3), ProjectTerrain(report, facadeSector, 4, 16436), [], 0);
            facadeSector.AsSpan().Clear(); facadeModule.Dispose();
            var result = Assess(snapshot, MakeRequest(snapshot, new(6, 1), new(5, 1)));
            if (result.Coverage != Coverage.Supported || result.Result != (facade == 0xB0000001 ? Disposition.Open : Disposition.Blocked))
                throw new InvalidOperationException("CARDINAL_FACADE_AND_OWNERSHIP_FIXTURE");
        }
        foreach (var ordinal in new[] { 69, 70 })
        {
            var masked = sector.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(masked.AsSpan(16436 + (ordinal / 32) * 4), 1u << (ordinal % 32));
            using var maskedModule = DatArchive.Open(new MemoryStream(FixtureDat("maps\\fixture\\test.sec", masked)), leaveOpen: false);
            using var resource = DatArchive.Open(new MemoryStream(FixtureDat("art\\tile\\tilename.mes", "{0}{abc}"u8.ToArray())), leaveOpen: false);
            var report = TerrainObstructionInspector.InspectSector(maskedModule, "SYNTHETIC_MODULE",
                new("maps\\fixture\\test.sec", "fixture", 1452, 1290, 86570436012, 92928, 82560, 64, 64),
                new(resource, "SYNTHETIC_RESOURCE", "art\\tile\\tilename.mes", TerrainPathProfile.NonWindowsMacOs));
            var snapshot = new Snapshot("SYNTHETIC", new(), new(4, 0), new(8, 3), ProjectTerrain(report, masked, 4, 16436), [], 0);
            var result = Assess(snapshot, MakeRequest(snapshot, new(6, 1), new(5, 1)));
            if (result.Coverage != Coverage.Supported || result.Result != (ordinal == 69 ? Disposition.Blocked : Disposition.Open))
                throw new InvalidOperationException("CARDINAL_MASK_DESTINATION_ONLY_FIXTURE");
        }
    }
    private static byte[] FixtureDat(string name, byte[] bytes) => FixtureDat([name], bytes);
    private static byte[] FixtureDat(string[] names, byte[] bytes)
    {
        using var output = new MemoryStream(); output.Write(bytes);
        using var index = new MemoryStream();
        void U32(Stream stream, uint value) { Span<byte> encoded = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(encoded, value); stream.Write(encoded); }
        U32(index, (uint)names.Length);
        foreach (var name in names)
        {
            var raw = System.Text.Encoding.ASCII.GetBytes(name + "\0"); U32(index, (uint)raw.Length); index.Write(raw); U32(index, 0);
            U32(index, 1); U32(index, (uint)bytes.Length); U32(index, (uint)bytes.Length); U32(index, 0);
        }
        index.Position = 0; index.CopyTo(output); output.Write(new byte[16]); output.Write("1TAD"u8);
        U32(output, (uint)names.Sum(n => n.Length + 1)); U32(output, (uint)(index.Length + 28)); return output.ToArray();
    }
}
