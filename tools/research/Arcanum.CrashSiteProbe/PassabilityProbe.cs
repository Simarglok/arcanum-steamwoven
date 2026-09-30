using System.Buffers.Binary;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.CrashSiteProbe;

// This is deliberately a fixed-sector research report, not a collision API.
internal static class PassabilityProbe
{
    private const int Width = 64;
    private const int CellCount = Width * Width;
    private const int BlockMaskBytes = 128 * sizeof(uint);
    private const int OriginX = 92_928;
    private const int OriginY = 82_560;

    private enum Coverage { Supported, Unsupported, Unknown }
    private enum Disposition { Open, Blocked }
    private enum TileClass { Tile, Facade, Unsupported }
    private enum NameRule { NotApplicable, Clear, Blocked, Unknown, Unsupported }
    private readonly record struct Assessment(Coverage Coverage, Disposition? Disposition, string Reason);
    private readonly record struct Local(int X, int Y);

    private const uint OfDestroyed = 0x00000001;
    private const uint OfOff = 0x00000002;
    private const uint OfNoBlock = 0x00000400;
    private const uint OfInventory = 0x00001000;
    private const uint OfDynamic = 0x00002000;

    private enum StaticSupportedness { Supported, Unknown, Unsupported }
    private enum StaticApplicability { Applicable, NotApplicable, Unknown }
    private enum StaticDisposition { Blocker, NoBlock }
    private enum TraversalContext
    {
        NormalCardinalZeroFlags,
        Projectile,
        Sound,
        SkipObjects,
        IgnoreCritters,
        Wall,
        Portal,
        Critter,
        Diagonal,
        SameCell,
        NonAdjacent,
        CrossSector,
    }

    private readonly record struct StaticCandidate(int SourceIndex, int ObjectType, uint? EffectiveFlags,
        bool FlagsInherited, long? Location, uint? AdjacentSpellFlags = null, bool LocationMalformed = false);
    private readonly record struct StaticAssessment(StaticSupportedness Supportedness, StaticApplicability Applicability,
        StaticDisposition? Disposition, string Reason);
    private readonly record struct StaticContribution(StaticCandidate Candidate, Local Destination, StaticAssessment Assessment);
    private readonly record struct StaticGroup(Local Destination, StaticContribution[] Contributions);
    private readonly record struct StaticRow(int SourceIndex, int LocalX, int LocalY, string DirectVsPrototype,
        StaticDisposition? Disposition, int ContributorCount);

    internal static void EmitStaticObstruction(TerrainObstructionSectorReport terrain, StaticObjectInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(inspection);
        if (inspection.TerminalRecordCount != 317 || inspection.Records.Count != 317)
            throw new InvalidDataException("The bounded static-obstruction report requires exactly 317 terminal records.");

        // Keep the existing terrain-only component aggregate distinct from the new
        // object subset; neither is a combined passability answer.
        Emit(terrain, inspection.TerminalRecordCount);

        var candidates = inspection.Records.Select(record => new StaticCandidate(record.SourceIndex, record.ObjectType,
            record.EffectiveFlags, record.FlagsInherited, record.Location)).ToArray();
        var assessments = candidates.Select(candidate => AssessStatic(candidate, TraversalContext.NormalCardinalZeroFlags)).ToArray();
        var contributions = candidates.Zip(assessments).Where(pair => TryDestination(pair.First, out _))
            .Select(pair => new StaticContribution(pair.First, Destination(pair.First), pair.Second)).ToArray();
        var admitted = contributions.Where(contribution => contribution.Assessment.Supportedness != StaticSupportedness.Unsupported &&
            contribution.Assessment.Applicability != StaticApplicability.NotApplicable).ToArray();
        var groups = admitted.GroupBy(contribution => contribution.Destination).OrderBy(group => group.Key.Y).ThenBy(group => group.Key.X)
            .Select(group => new StaticGroup(group.Key, group.OrderBy(item => item.Candidate.SourceIndex).ToArray())).ToArray();
        if (groups.Any(group => group.Contributions.Length > inspection.TerminalRecordCount))
            throw new InvalidDataException("Static-obstruction contributor multiplicity exceeded the terminal record cap.");

        var flagsResolved = candidates.Count(candidate => candidate.ObjectType == 3 && candidate.EffectiveFlags.HasValue);
        var directFlags = candidates.Count(candidate => candidate.ObjectType == 3 && candidate.EffectiveFlags.HasValue && !candidate.FlagsInherited);
        var inheritedFlags = candidates.Count(candidate => candidate.ObjectType == 3 && candidate.EffectiveFlags.HasValue && candidate.FlagsInherited);
        var locationPresent = candidates.Count(candidate => candidate.ObjectType == 3 && candidate.Location.HasValue);
        var locationInRange = candidates.Count(candidate => candidate.ObjectType == 3 && TryDestination(candidate, out _));
        var typeThree = candidates.Count(candidate => candidate.ObjectType == 3);
        var blockerCount = assessments.Count(assessment => assessment.Disposition == StaticDisposition.Blocker);
        var noBlockCount = assessments.Count(assessment => assessment.Disposition == StaticDisposition.NoBlock);
        var unknownCount = assessments.Count(assessment => assessment.Supportedness == StaticSupportedness.Unknown);
        var dynamicExcluded = candidates.Count(candidate => candidate.EffectiveFlags is uint flags && (flags & OfDynamic) != 0);
        var inventoryExcluded = candidates.Count(candidate => candidate.EffectiveFlags is uint flags && (flags & OfInventory) != 0);
        var offExcluded = candidates.Count(candidate => candidate.EffectiveFlags is uint flags && (flags & OfOff) != 0);
        var destroyedExcluded = candidates.Count(candidate => candidate.EffectiveFlags is uint flags && (flags & OfDestroyed) != 0);
        var sameCellGroups = groups.Count(group => group.Contributions.Length > 1);
        var maxMultiplicity = groups.Length == 0 ? 0 : groups.Max(group => group.Contributions.Length);

        Console.WriteLine($"static-obstruction aggregate terminal_records={inspection.TerminalRecordCount} resolved_flags={flagsResolved} type3={typeThree} direct_flags={directFlags} inherited_flags={inheritedFlags} location_present={locationPresent} location_in_range={locationInRange} dynamic_excluded={dynamicExcluded} inventory_excluded={inventoryExcluded} off_not_applicable={offExcluded} destroyed_not_applicable={destroyedExcluded} blocker={blockerCount} no_block={noBlockCount} unknown={unknownCount} distinct_occupied_cells={groups.Length} same_cell_groups={sameCellGroups} maximum_multiplicity={maxMultiplicity} full_passability=unknown");

        var rows = new[]
        {
            FirstRecordRow(candidates, assessments, StaticDisposition.Blocker, direct: true),
            FirstRecordRow(candidates, assessments, StaticDisposition.Blocker, direct: false),
            FirstRecordRow(candidates, assessments, StaticDisposition.NoBlock, direct: true),
            FirstRecordRow(candidates, assessments, StaticDisposition.NoBlock, direct: false),
            FirstGroupRow(groups, requireBlocker: true),
            FirstGroupRow(groups, requireBlocker: false),
        };
        foreach (var row in rows)
            Console.WriteLine($"static-obstruction row source_index={row.SourceIndex} local_x={row.LocalX} local_y={row.LocalY} direct_vs_prototype={row.DirectVsPrototype} disposition={RenderStatic(row.Disposition)} contributor_count={row.ContributorCount}");
    }

    internal static void Emit(TerrainObstructionSectorReport report, int staticCensusCount)
    {
        var maskBlocked = 0;
        var maskNoBlock = 0;
        var facadeNoBlock = 0;
        var facadeBlocked = 0;
        var tileBlocked = 0;
        var tileNoBlock = 0;
        var tileUnknown = 0;
        for (var ordinal = 0; ordinal < CellCount; ordinal++)
        {
            var cell = report.GetCell(ordinal & 63, ordinal >> 6);
            if (cell.SerializedBlockMask.Disposition == TerrainObstructionDisposition.Blocked) maskBlocked++; else if (cell.SerializedBlockMask.Disposition == TerrainObstructionDisposition.NoBlock) maskNoBlock++;
            if (cell.FacadeWalkabilityBit.Disposition == TerrainObstructionDisposition.NoBlock) facadeNoBlock++;
            if (cell.FacadeWalkabilityBit.Disposition == TerrainObstructionDisposition.Blocked) facadeBlocked++;
            if (cell.TileNameRule.Disposition == TerrainObstructionDisposition.Blocked) tileBlocked++;
            else if (cell.TileNameRule.Disposition == TerrainObstructionDisposition.NoBlock) tileNoBlock++;
            else if (cell.TileNameRule.Supportedness == TerrainObstructionSupportedness.Unknown) tileUnknown++;
        }

        Console.WriteLine($"terrain-obstruction profile=base-sector-only sector=1452,1290 origin={OriginX},{OriginY} extent=64x64");
        Console.WriteLine($"terrain-obstruction aggregate serialized_mask_blocked={maskBlocked} serialized_mask_no_block={maskNoBlock} facade_no_block={facadeNoBlock} facade_blocked={facadeBlocked} tile_name_blocked={tileBlocked} tile_name_no_block={tileNoBlock} tile_name_unknown={tileUnknown}");
        Console.WriteLine($"terrain-obstruction aggregate static_census={staticCensusCount} full_passability=not-ready");
        if (maskBlocked + maskNoBlock != CellCount || facadeNoBlock + facadeBlocked != 1_063 || tileBlocked + tileNoBlock + tileUnknown != 3_033)
        {
            throw new InvalidDataException("Terrain obstruction component aggregates did not match the fixed profile's component domains.");
        }
    }

    private static void EmitLegacyCellDiagnostics(byte[] sectorBytes, int tileStart, int blockMaskStart, int blockMaskBytes, int staticCensusCount)
    {
        var mask = ReadMask(sectorBytes, blockMaskStart, blockMaskBytes).ToArray();
        var blocked = 0;
        var facadeWalkable = 0;
        var facadeNonWalkable = 0;
        var tileCount = 0;
        for (var ordinal = 0; ordinal < CellCount; ordinal++)
        {
            if (IsBlocked(mask, ordinal)) blocked++;
            var artId = TileWord(sectorBytes, tileStart, ordinal);
            if ((artId >> 28) == 11)
            {
                if ((artId & 1) == 0) facadeNonWalkable++; else facadeWalkable++;
            }
            else if ((artId >> 28) == 0)
            {
                tileCount++;
            }
        }

        Console.WriteLine($"passability profile=base-sector-only sector=1452,1290 origin={OriginX},{OriginY} extent=64x64 block_mask_words=128 block_mask_bytes={BlockMaskBytes}");
        Console.WriteLine($"passability aggregate serialized_block_set={blocked} serialized_block_clear={CellCount - blocked} facade_walkability_set={facadeWalkable} facade_walkability_clear={facadeNonWalkable} tile_name_rule_unknown={tileCount}");
        Console.WriteLine($"passability aggregate static_census={staticCensusCount} static_spatial_and_runtime_contribution=unknown combined_occupancy_unknown={CellCount}");

        var selected = new SortedSet<int> { 0, 4095, 2078, 1947, 2011, 2075 };
        AddFirst(selected, ordinal => IsBlocked(mask, ordinal));
        AddFirst(selected, ordinal => !IsBlocked(mask, ordinal));
        AddFirst(selected, ordinal => IsFacade(sectorBytes, tileStart, ordinal) && IsBlocked(mask, ordinal) && IsFacadeWalkable(sectorBytes, tileStart, ordinal));
        AddFirst(selected, ordinal => IsFacade(sectorBytes, tileStart, ordinal) && !IsBlocked(mask, ordinal) && !IsFacadeWalkable(sectorBytes, tileStart, ordinal));
        foreach (var ordinal in selected)
        {
            EmitCell(sectorBytes, tileStart, mask, ordinal);
        }

        var unknown = new Assessment(Coverage.Unknown, null, "tile-name-and-static-and-actor-context-unadmitted");
        EmitStep("cardinal", new Local(30, 32), new Local(31, 32), unknown, unknown, unknown, unknown, unknown);
        EmitStep("diagonal", new Local(30, 32), new Local(31, 31), unknown, unknown, unknown, unknown, unknown);
        EmitStep("sector-edge", new Local(63, 63), new Local(64, 63), unknown, unknown, unknown, unknown, unknown);
    }

    internal static void SelfTest()
    {
        var mask = new byte[BlockMaskBytes];
        Set(mask, 0, true);
        Set(mask, 31, true);
        Set(mask, 32, true);
        Set(mask, 63, true);
        Set(mask, 64, true);
        Set(mask, 4095, true);
        foreach (var (x, y, expected) in new[]
        {
            (0, 0, true), (31, 0, true), (32, 0, true), (63, 0, true), (0, 1, true), (63, 63, true), (30, 32, false)
        })
        {
            if (IsBlocked(mask, Ordinal(x, y)) != expected) throw new InvalidOperationException("Synthetic block-mask bit ordering regression.");
        }
        Set(mask, 32, false);
        if (IsBlocked(mask, 32)) throw new InvalidOperationException("Synthetic block-mask clear regression.");
        AssertRejected(() => Ordinal(-1, 0), "negative local coordinate");
        AssertRejected(() => Ordinal(64, 0), "out-of-range local coordinate");
        AssertRejected(() => Ordinal(0, 64), "out-of-range local Y coordinate");
        AssertRejected(() => IsBlocked(mask, -1), "negative ordinal");
        AssertRejected(() => IsBlocked(mask, CellCount), "out-of-range ordinal");
        _ = WorldCoordinate(0, 0, OriginX, OriginY);
        AssertRejected(() => WorldCoordinate(63, 63, int.MaxValue, int.MaxValue), "checked world-coordinate overflow");
        if (ReadMask(new byte[BlockMaskBytes], 0, BlockMaskBytes).Length != BlockMaskBytes) throw new InvalidOperationException("Exact synthetic block-mask framing regression.");
        AssertRejected(() => ReadMask(new byte[BlockMaskBytes - 1], 0, BlockMaskBytes), "truncated block mask");
        AssertRejected(() => ReadMask(new byte[BlockMaskBytes], 1, BlockMaskBytes), "absent block mask");
        AssertRejected(() => ReadMask(new byte[BlockMaskBytes], 0, BlockMaskBytes - 1), "wrong block-mask length");
        AssertRejected(() => ReadMask(new byte[BlockMaskBytes], int.MaxValue, BlockMaskBytes), "checked block-mask offset overflow");

        var tiles = new byte[CellCount * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(tiles, 0xB6000801); // facade, bit 0 set
        BinaryPrimitives.WriteUInt32LittleEndian(tiles.AsSpan(sizeof(uint)), 0xB6000802); // facade, bit 0 clear
        if (!IsFacadeWalkable(tiles, 0, 0) || IsFacadeWalkable(tiles, 0, 1) || IsFacade(tiles, 0, 2)) throw new InvalidOperationException("Synthetic facade walkability classification regression.");

        AssertAssessment(AssessTerrain(true, TileClass.Tile, false, NameRule.Clear), Coverage.Supported, Disposition.Blocked, "serialized block");
        AssertAssessment(AssessTerrain(false, TileClass.Facade, true, NameRule.NotApplicable), Coverage.Supported, Disposition.Open, "facade bit zero set");
        AssertAssessment(AssessTerrain(false, TileClass.Facade, false, NameRule.NotApplicable), Coverage.Supported, Disposition.Blocked, "facade bit zero clear");
        AssertAssessment(AssessTerrain(false, TileClass.Tile, false, NameRule.Blocked), Coverage.Supported, Disposition.Blocked, "tile name blocker");
        AssertAssessment(AssessTerrain(false, TileClass.Tile, false, NameRule.Unknown), Coverage.Unknown, null, "tile name unknown");
        AssertAssessment(AssessTerrain(false, TileClass.Tile, false, NameRule.Unsupported), Coverage.Unsupported, null, "tile name unsupported");
        AssertAssessment(AssessTerrain(false, TileClass.Unsupported, false, NameRule.Unknown), Coverage.Unsupported, null, "unsupported tile class");

        var open = new Assessment(Coverage.Supported, Disposition.Open, "synthetic-open");
        var blocker = new Assessment(Coverage.Supported, Disposition.Blocked, "synthetic-blocker");
        var unresolved = new Assessment(Coverage.Unknown, null, "synthetic-unresolved");
        var unsupported = new Assessment(Coverage.Unsupported, null, "synthetic-unsupported");
        AssertAssessment(Compose(open), Coverage.Supported, Disposition.Open, "composition no blocker");
        AssertAssessment(Compose(open, blocker), Coverage.Supported, Disposition.Blocked, "composition blocker");
        AssertAssessment(Compose(open, unresolved), Coverage.Unknown, null, "composition unresolved");
        AssertAssessment(Compose(blocker, unsupported), Coverage.Unsupported, null, "composition unsupported");
        AssertAssessment(Compose(blocker, blocker), Coverage.Supported, Disposition.Blocked, "composition multiple same-cell blockers");

        foreach (var to in new[] { new Local(31, 32), new Local(29, 32), new Local(30, 33), new Local(30, 31) })
            AssertAssessment(AssessStep(new Local(30, 32), to, open, open, open, open, open), Coverage.Supported, Disposition.Open, "cardinal step");
        foreach (var to in new[] { new Local(31, 33), new Local(31, 31), new Local(29, 33), new Local(29, 31) })
            AssertAssessment(AssessStep(new Local(30, 32), to, open, open, open, open, open), Coverage.Unknown, null, "diagonal step remains unknown");
        AssertAssessment(AssessStep(new Local(30, 32), new Local(30, 32), open, open, open, open, open), Coverage.Unsupported, null, "same-cell step");
        AssertAssessment(AssessStep(new Local(30, 32), new Local(32, 32), open, open, open, open, open), Coverage.Unsupported, null, "non-adjacent step");
        AssertAssessment(AssessStep(new Local(63, 63), new Local(64, 63), open, open, open, open, open), Coverage.Unsupported, null, "sector-edge step");
        StaticObstructionSelfTest();
    }

    private static void StaticObstructionSelfTest()
    {
        var blocker = new StaticCandidate(7, 3, 0, false, PackedWorld(OriginX + 2, OriginY + 3));
        var noBlock = new StaticCandidate(8, 3, OfNoBlock, false, PackedWorld(OriginX + 2, OriginY + 3));
        var inheritedBlocker = new StaticCandidate(9, 3, 0, true, PackedWorld(OriginX + 4, OriginY + 5));
        var inheritedNoBlock = new StaticCandidate(10, 3, OfNoBlock, true, PackedWorld(OriginX + 4, OriginY + 5));
        AssertStatic(AssessStatic(blocker, TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.Blocker, "direct blocker");
        AssertStatic(AssessStatic(noBlock, TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.NoBlock, "direct no-block");
        AssertStatic(AssessStatic(inheritedBlocker, TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.Blocker, "prototype blocker");
        AssertStatic(AssessStatic(inheritedNoBlock, TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.NoBlock, "prototype no-block");
        AssertStatic(AssessStatic(new StaticCandidate(11, 3, OfNoBlock, false, blocker.Location, AdjacentSpellFlags: OfDynamic), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.NoBlock, "slot-19 sentinel has no effect");
        AssertStatic(AssessStatic(new StaticCandidate(13, 3, null, true, blocker.Location), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Unknown, StaticApplicability.Unknown, null, "missing duplicate or mismatched prototype flags remain unknown");
        AssertStatic(AssessStatic(new StaticCandidate(14, 3, 0, false, null), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Unknown, StaticApplicability.Unknown, null, "missing location");
        AssertStatic(AssessStatic(new StaticCandidate(15, 3, 0, false, blocker.Location, LocationMalformed: true), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Unknown, StaticApplicability.Unknown, null, "malformed location");
        AssertStatic(AssessStatic(new StaticCandidate(16, 3, 0, false, PackedWorld(OriginX + Width, OriginY)), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Unknown, StaticApplicability.Unknown, null, "out-of-range location");
        if (TryDestination(new StaticCandidate(17, 3, 0, false, PackedWorld(int.MinValue, int.MaxValue)), out _)) throw new InvalidOperationException("Checked local-coordinate overflow was accepted.");
        AssertStatic(AssessStatic(new StaticCandidate(18, 2, 0, false, blocker.Location), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Unsupported, StaticApplicability.Unknown, null, "type mismatch");
        AssertStatic(AssessStatic(new StaticCandidate(19, 3, OfDynamic, false, blocker.Location), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Unsupported, StaticApplicability.Unknown, null, "dynamic exclusion");
        AssertStatic(AssessStatic(new StaticCandidate(20, 3, OfInventory, false, blocker.Location), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported, StaticApplicability.NotApplicable, null, "inventory exclusion");
        AssertStatic(AssessStatic(new StaticCandidate(21, 3, OfOff, false, blocker.Location), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported, StaticApplicability.NotApplicable, null, "off exclusion");
        AssertStatic(AssessStatic(new StaticCandidate(22, 3, OfDestroyed, false, blocker.Location), TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported, StaticApplicability.NotApplicable, null, "destroyed exclusion");
        foreach (var context in Enum.GetValues<TraversalContext>().Where(context => context != TraversalContext.NormalCardinalZeroFlags))
            AssertStatic(AssessStatic(blocker, context), StaticSupportedness.Unsupported, StaticApplicability.Unknown, null, $"unsupported {context}");
        if (!TryDestination(blocker, out var destination) || destination != new Local(2, 3)) throw new InvalidOperationException("Static source-to-destination coordinate mapping regressed.");

        var knownBlocker = new StaticContribution(blocker, destination, AssessStatic(blocker, TraversalContext.NormalCardinalZeroFlags));
        var knownNoBlock = new StaticContribution(noBlock, destination, AssessStatic(noBlock, TraversalContext.NormalCardinalZeroFlags));
        var unresolved = new StaticContribution(new StaticCandidate(23, 3, null, false, blocker.Location), destination,
            AssessStatic(new StaticCandidate(23, 3, null, false, blocker.Location), TraversalContext.NormalCardinalZeroFlags));
        AssertStatic(AggregateStaticCell([knownBlocker, knownNoBlock]), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.Blocker, "same-cell blocker wins");
        AssertStatic(AggregateStaticCell([knownNoBlock, new StaticContribution(inheritedNoBlock, destination, AssessStatic(inheritedNoBlock, TraversalContext.NormalCardinalZeroFlags))]), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.NoBlock, "same-cell all no-block");
        AssertStatic(AggregateStaticCell([unresolved]), StaticSupportedness.Unknown, StaticApplicability.Unknown, null, "same-cell unresolved");
        AssertStatic(AggregateStaticCell([]), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.NoBlock, "empty admitted static subset");
        var multiplicity = Enumerable.Repeat(knownNoBlock, 317).ToArray();
        if (multiplicity.Length != 317) throw new InvalidOperationException("Static contributor cap fixture changed.");
        AssertStatic(AggregateStaticCell(multiplicity), StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.NoBlock, "same-cell terminal-cap multiplicity");

        AssertStaticRow(FirstRecordRow([blocker, inheritedBlocker], [knownBlocker.Assessment, AssessStatic(inheritedBlocker, TraversalContext.NormalCardinalZeroFlags)], StaticDisposition.Blocker, direct: true),
            new StaticRow(7, 2, 3, "direct", StaticDisposition.Blocker, 1), "first direct blocker row");
        AssertStaticRow(FirstRecordRow([blocker, inheritedBlocker], [knownBlocker.Assessment, AssessStatic(inheritedBlocker, TraversalContext.NormalCardinalZeroFlags)], StaticDisposition.Blocker, direct: false),
            new StaticRow(9, 4, 5, "prototype", StaticDisposition.Blocker, 1), "first prototype blocker row");

        var singletonBlocker = new StaticGroup(new Local(1, 1), [knownBlocker]);
        var multiBlocker = new StaticGroup(new Local(2, 1), [
            new StaticContribution(new StaticCandidate(24, 3, 0, false, PackedWorld(OriginX + 2, OriginY + 1)), new Local(2, 1), AssessStatic(new StaticCandidate(24, 3, 0, false, PackedWorld(OriginX + 2, OriginY + 1)), TraversalContext.NormalCardinalZeroFlags)),
            new StaticContribution(new StaticCandidate(25, 3, OfNoBlock, true, PackedWorld(OriginX + 2, OriginY + 1)), new Local(2, 1), AssessStatic(new StaticCandidate(25, 3, OfNoBlock, true, PackedWorld(OriginX + 2, OriginY + 1)), TraversalContext.NormalCardinalZeroFlags)),
        ]);
        var multiNoBlock = new StaticGroup(new Local(3, 1), [
            new StaticContribution(new StaticCandidate(26, 3, OfNoBlock, false, PackedWorld(OriginX + 3, OriginY + 1)), new Local(3, 1), AssessStatic(new StaticCandidate(26, 3, OfNoBlock, false, PackedWorld(OriginX + 3, OriginY + 1)), TraversalContext.NormalCardinalZeroFlags)),
            new StaticContribution(new StaticCandidate(27, 3, OfNoBlock, true, PackedWorld(OriginX + 3, OriginY + 1)), new Local(3, 1), AssessStatic(new StaticCandidate(27, 3, OfNoBlock, true, PackedWorld(OriginX + 3, OriginY + 1)), TraversalContext.NormalCardinalZeroFlags)),
        ]);
        AssertStaticRow(FirstGroupRow([singletonBlocker, multiBlocker, multiNoBlock], requireBlocker: true),
            new StaticRow(24, 2, 1, "mixed", StaticDisposition.Blocker, 2), "same-cell blocker row skips singleton");
        AssertStaticRow(FirstGroupRow([singletonBlocker, multiBlocker, multiNoBlock], requireBlocker: false),
            new StaticRow(26, 3, 1, "mixed", StaticDisposition.NoBlock, 2), "same-cell all-no-block row");
        AssertStaticRow(FirstGroupRow([singletonBlocker], requireBlocker: true),
            new StaticRow(0, 0, 0, "none", null, 0), "absent same-cell blocker row");
        AssertStaticRow(FirstGroupRow([singletonBlocker, multiBlocker], requireBlocker: false),
            new StaticRow(0, 0, 0, "none", null, 0), "absent same-cell all-no-block row");
    }

    internal static void AssertResolvedDirectOverride(uint resolvedDirectFlags, uint oppositePrototypeFlags)
    {
        if (resolvedDirectFlags == oppositePrototypeFlags)
            throw new InvalidOperationException("Synthetic direct and prototype flag fixtures must be opposite.");
        var candidate = new StaticCandidate(12, 3, resolvedDirectFlags, false, PackedWorld(OriginX + 2, OriginY + 3));
        AssertStatic(AssessStatic(candidate, TraversalContext.NormalCardinalZeroFlags), StaticSupportedness.Supported,
            StaticApplicability.Applicable, StaticDisposition.NoBlock, "resolver direct slot-18 override over opposite prototype default");
    }

    private static StaticAssessment AssessStatic(StaticCandidate candidate, TraversalContext context)
    {
        if (context != TraversalContext.NormalCardinalZeroFlags)
            return new StaticAssessment(StaticSupportedness.Unsupported, StaticApplicability.Unknown, null, "traversal-context-unsupported");
        if (candidate.ObjectType != 3)
            return new StaticAssessment(StaticSupportedness.Unsupported, StaticApplicability.Unknown, null, "not-type-3-scenery");
        if (!candidate.EffectiveFlags.HasValue)
            return new StaticAssessment(StaticSupportedness.Unknown, StaticApplicability.Unknown, null, "object-flags-unresolved");
        var flags = candidate.EffectiveFlags.Value;
        if ((flags & OfDynamic) != 0)
            return new StaticAssessment(StaticSupportedness.Unsupported, StaticApplicability.Unknown, null, "dynamic-scenery-excluded");
        if (!TryDestination(candidate, out _))
            return new StaticAssessment(StaticSupportedness.Unknown, StaticApplicability.Unknown, null, "direct-location-unresolved-or-out-of-range");
        if ((flags & OfInventory) != 0)
            return new StaticAssessment(StaticSupportedness.Supported, StaticApplicability.NotApplicable, null, "inventory-object-not-in-sector-cell-list");
        if ((flags & OfDestroyed) != 0)
            return new StaticAssessment(StaticSupportedness.Supported, StaticApplicability.NotApplicable, null, "destroyed-object-not-applicable");
        if ((flags & OfOff) != 0)
            return new StaticAssessment(StaticSupportedness.Supported, StaticApplicability.NotApplicable, null, "off-object-not-applicable");
        return (flags & OfNoBlock) != 0
            ? new StaticAssessment(StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.NoBlock, "object-no-block")
            : new StaticAssessment(StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.Blocker, "object-blocker");
    }

    private static StaticAssessment AggregateStaticCell(IReadOnlyList<StaticContribution> contributions)
    {
        if (contributions.Any(contribution => contribution.Assessment.Disposition == StaticDisposition.Blocker))
            return new StaticAssessment(StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.Blocker, "same-cell-blocker");
        if (contributions.Any(contribution => contribution.Assessment.Supportedness == StaticSupportedness.Unknown || contribution.Assessment.Applicability == StaticApplicability.Unknown))
            return new StaticAssessment(StaticSupportedness.Unknown, StaticApplicability.Unknown, null, "same-cell-unresolved");
        return new StaticAssessment(StaticSupportedness.Supported, StaticApplicability.Applicable, StaticDisposition.NoBlock, "all-admitted-static-contributors-no-block");
    }

    private static StaticRow FirstRecordRow(IReadOnlyList<StaticCandidate> candidates, IReadOnlyList<StaticAssessment> assessments,
        StaticDisposition disposition, bool direct)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            if (assessments[index].Disposition != disposition || candidates[index].FlagsInherited == direct || !TryDestination(candidates[index], out var destination)) continue;
            return new StaticRow(candidates[index].SourceIndex, destination.X, destination.Y, direct ? "direct" : "prototype", disposition, 1);
        }
        return new StaticRow(0, 0, 0, "none", null, 0);
    }

    private static StaticRow FirstGroupRow(IReadOnlyList<StaticGroup> groups, bool requireBlocker)
    {
        foreach (var group in groups)
        {
            if (group.Contributions.Length < 2) continue;
            var assessment = AggregateStaticCell(group.Contributions);
            if ((requireBlocker && assessment.Disposition != StaticDisposition.Blocker) ||
                (!requireBlocker && assessment.Disposition != StaticDisposition.NoBlock)) continue;
            var first = group.Contributions[0].Candidate;
            var kind = group.Contributions.All(item => !item.Candidate.FlagsInherited) ? "direct" :
                group.Contributions.All(item => item.Candidate.FlagsInherited) ? "prototype" : "mixed";
            return new StaticRow(first.SourceIndex, group.Destination.X, group.Destination.Y, kind, assessment.Disposition, group.Contributions.Length);
        }
        return new StaticRow(0, 0, 0, "none", null, 0);
    }

    private static bool TryDestination(StaticCandidate candidate, out Local destination)
    {
        destination = default;
        if (!candidate.Location.HasValue || candidate.LocationMalformed) return false;
        try
        {
            var worldX = unchecked((int)(uint)candidate.Location.Value);
            var worldY = unchecked((int)(candidate.Location.Value >> 32));
            var localX = checked(worldX - OriginX);
            var localY = checked(worldY - OriginY);
            if ((uint)localX >= Width || (uint)localY >= Width) return false;
            destination = new Local(localX, localY);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    private static Local Destination(StaticCandidate candidate)
    {
        if (!TryDestination(candidate, out var destination)) throw new InvalidDataException("Static destination was not admitted.");
        return destination;
    }

    private static long PackedWorld(int worldX, int worldY)
        => unchecked((long)((ulong)(uint)worldX | ((ulong)(uint)worldY << 32)));

    private static string RenderStatic(StaticDisposition? disposition) => disposition?.ToString() ?? "null";

    private static void AssertStatic(StaticAssessment actual, StaticSupportedness supportedness, StaticApplicability applicability,
        StaticDisposition? disposition, string label)
    {
        if (actual.Supportedness != supportedness || actual.Applicability != applicability || actual.Disposition != disposition)
            throw new InvalidOperationException($"Synthetic static-obstruction assessment '{label}' changed.");
    }

    private static void AssertStaticRow(StaticRow actual, StaticRow expected, string label)
    {
        if (actual != expected) throw new InvalidOperationException($"Synthetic static-obstruction row '{label}' changed.");
    }

    private static void EmitCell(ReadOnlySpan<byte> sectorBytes, int tileStart, ReadOnlySpan<byte> mask, int ordinal)
    {
        var x = ordinal & 63;
        var y = ordinal >> 6;
        var block = IsBlocked(mask, ordinal);
        var facade = IsFacade(sectorBytes, tileStart, ordinal);
        var terrain = AssessTerrain(block, facade ? TileClass.Facade : TileClass.Tile, facade && IsFacadeWalkable(sectorBytes, tileStart, ordinal), facade ? NameRule.NotApplicable : NameRule.Unknown);
        var occupancy = Compose(terrain, new Assessment(Coverage.Unknown, null, "static-spatial-and-runtime-unadmitted"), new Assessment(Coverage.Unknown, null, "actor-context-unadmitted"));
        var world = WorldCoordinate(x, y, OriginX, OriginY);
        Console.WriteLine($"passability cell ordinal={ordinal} local={x},{y} world={world.X},{world.Y} serialized_block={(block ? "set" : "clear")} class={(facade ? "facade" : "tile")} facade_walkability={(facade ? (IsFacadeWalkable(sectorBytes, tileStart, ordinal) ? "set" : "clear") : "na")} terrain_component_coverage={terrain.Coverage} terrain_component_disposition={Render(terrain.Disposition)} occupancy_coverage={occupancy.Coverage} occupancy_disposition={Render(occupancy.Disposition)} reason={occupancy.Reason}");
    }

    private static Assessment AssessTerrain(bool serializedBlock, TileClass tileClass, bool facadeWalkable, NameRule nameRule)
    {
        if (serializedBlock) return new Assessment(Coverage.Supported, Disposition.Blocked, "serialized-block-mask-set");
        if (tileClass == TileClass.Unsupported || nameRule == NameRule.Unsupported) return new Assessment(Coverage.Unsupported, null, "tile-or-name-class-unsupported");
        if (tileClass == TileClass.Facade) return facadeWalkable
            ? new Assessment(Coverage.Supported, Disposition.Open, "facade-walkability-set")
            : new Assessment(Coverage.Supported, Disposition.Blocked, "facade-walkability-clear");
        return nameRule switch
        {
            NameRule.Clear => new Assessment(Coverage.Supported, Disposition.Open, "tile-name-rule-clear"),
            NameRule.Blocked => new Assessment(Coverage.Supported, Disposition.Blocked, "tile-name-rule-blocked"),
            _ => new Assessment(Coverage.Unknown, null, "tile-name-rule-unavailable"),
        };
    }

    private static Assessment Compose(params Assessment[] components)
    {
        if (components.Any(component => component.Coverage == Coverage.Unsupported)) return new Assessment(Coverage.Unsupported, null, components.First(component => component.Coverage == Coverage.Unsupported).Reason);
        if (components.Any(component => component.Coverage == Coverage.Unknown)) return new Assessment(Coverage.Unknown, null, components.First(component => component.Coverage == Coverage.Unknown).Reason);
        if (components.Any(component => component.Disposition == Disposition.Blocked)) return new Assessment(Coverage.Supported, Disposition.Blocked, components.First(component => component.Disposition == Disposition.Blocked).Reason);
        return new Assessment(Coverage.Supported, Disposition.Open, "all-admitted-components-open");
    }

    private static Assessment AssessStep(Local from, Local to, Assessment fromCell, Assessment toCell, Assessment edgeAndCorner, Assessment objectTraversal, Assessment actorContext)
    {
        if (!InBounds(from) || !InBounds(to)) return new Assessment(Coverage.Unsupported, null, "cross-sector-data-absent");
        var deltaX = Math.Abs(checked(to.X - from.X));
        var deltaY = Math.Abs(checked(to.Y - from.Y));
        if (deltaX == 0 && deltaY == 0) return new Assessment(Coverage.Unsupported, null, "same-cell-not-a-step");
        if (deltaX > 1 || deltaY > 1) return new Assessment(Coverage.Unsupported, null, "non-adjacent-routing-unsupported");
        var combined = Compose(fromCell, toCell, edgeAndCorner, objectTraversal, actorContext);
        if (combined.Coverage == Coverage.Unsupported) return combined;
        if (deltaX == 1 && deltaY == 1) return new Assessment(Coverage.Unknown, null, "corner-and-cross-cell-rule-unproved");
        return combined;
    }

    private static void EmitStep(string kind, Local from, Local to, Assessment fromCell, Assessment toCell, Assessment edgeAndCorner, Assessment objectTraversal, Assessment actorContext)
    {
        var assessment = AssessStep(from, to, fromCell, toCell, edgeAndCorner, objectTraversal, actorContext);
        Console.WriteLine($"passability step kind={kind} from={from.X},{from.Y} to={to.X},{to.Y} coverage={assessment.Coverage} disposition={Render(assessment.Disposition)} reason={assessment.Reason}");
    }

    private static (int X, int Y) WorldCoordinate(int localX, int localY, int originX, int originY) => (checked(originX + localX), checked(originY + localY));
    private static bool InBounds(Local local) => (uint)local.X < Width && (uint)local.Y < Width;
    private static string Render(Disposition? disposition) => disposition?.ToString() ?? "null";
    private static void AssertAssessment(Assessment actual, Coverage coverage, Disposition? disposition, string label)
    {
        if (actual.Coverage != coverage || actual.Disposition != disposition) throw new InvalidOperationException($"Synthetic passability assessment '{label}' changed.");
    }

    private static void AddFirst(SortedSet<int> selected, Func<int, bool> predicate)
    {
        if (selected.Any(predicate)) return;
        for (var ordinal = 0; ordinal < CellCount; ordinal++)
        {
            if (predicate(ordinal))
            {
                selected.Add(ordinal);
                return;
            }
        }
    }

    private static ReadOnlySpan<byte> ReadMask(ReadOnlySpan<byte> bytes, int start, int length)
    {
        if (length != BlockMaskBytes) throw new InvalidDataException("The bounded block-mask section is not exactly 512 bytes.");
        var end = checked(start + length);
        if (start < 0 || end > bytes.Length) throw new InvalidDataException("The bounded block-mask section is absent or truncated.");
        return bytes.Slice(start, length);
    }

    private static int Ordinal(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Width) throw new InvalidDataException("Local block-mask coordinates are outside the fixed sector.");
        return checked(x | (y << 6));
    }

    private static bool IsBlocked(ReadOnlySpan<byte> mask, int ordinal)
    {
        if ((uint)ordinal >= CellCount) throw new InvalidDataException("Block-mask ordinal is outside the fixed sector.");
        var word = BinaryPrimitives.ReadUInt32LittleEndian(mask.Slice(checked((ordinal / 32) * sizeof(uint)), sizeof(uint)));
        return (word & (1u << (ordinal % 32))) != 0;
    }

    private static void Set(Span<byte> mask, int ordinal, bool blocked)
    {
        var offset = checked((ordinal / 32) * sizeof(uint));
        var word = BinaryPrimitives.ReadUInt32LittleEndian(mask.Slice(offset, sizeof(uint)));
        word = blocked ? word | (1u << (ordinal % 32)) : word & ~(1u << (ordinal % 32));
        BinaryPrimitives.WriteUInt32LittleEndian(mask.Slice(offset, sizeof(uint)), word);
    }

    private static uint TileWord(ReadOnlySpan<byte> bytes, int tileStart, int ordinal) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(checked(tileStart + (ordinal * sizeof(uint))), sizeof(uint)));
    private static bool IsFacade(ReadOnlySpan<byte> bytes, int tileStart, int ordinal) => (TileWord(bytes, tileStart, ordinal) >> 28) == 11;
    private static bool IsFacadeWalkable(ReadOnlySpan<byte> bytes, int tileStart, int ordinal) => (TileWord(bytes, tileStart, ordinal) & 1) != 0;

    private static void AssertRejected(Action action, string label)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (OverflowException) { return; }
        throw new InvalidOperationException($"Synthetic passability case '{label}' was accepted.");
    }
}
