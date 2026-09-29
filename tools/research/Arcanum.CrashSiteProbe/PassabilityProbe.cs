using System.Buffers.Binary;

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

    internal static void Emit(byte[] sectorBytes, int tileStart, int blockMaskStart, int blockMaskBytes, int staticCensusCount)
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
