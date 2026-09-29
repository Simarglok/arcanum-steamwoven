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

        Console.WriteLine("passability step kind=cardinal from=30,32 to=31,32 coverage=Unknown disposition=null reason=tile-name-and-static-and-actor-context-unadmitted");
        Console.WriteLine("passability step kind=diagonal from=30,32 to=31,31 coverage=Unknown disposition=null reason=corner-and-cross-cell-rule-unproved");
        Console.WriteLine("passability step kind=sector-edge from=63,63 to=64,63 coverage=Unsupported disposition=null reason=cross-sector-data-absent");
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
        AssertRejected(() => Ordinal(-1, 0), "negative local coordinate");
        AssertRejected(() => Ordinal(64, 0), "out-of-range local coordinate");
        AssertRejected(() => IsBlocked(mask, -1), "negative ordinal");
        AssertRejected(() => IsBlocked(mask, CellCount), "out-of-range ordinal");
        AssertRejected(() => ReadMask(new byte[BlockMaskBytes - 1], 0, BlockMaskBytes), "truncated block mask");
        AssertRejected(() => ReadMask(new byte[BlockMaskBytes], 0, BlockMaskBytes - 1), "wrong block-mask length");
        AssertRejected(() => ReadMask(new byte[BlockMaskBytes], int.MaxValue, BlockMaskBytes), "checked block-mask offset overflow");

        var tiles = new byte[CellCount * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(tiles, 0xB6000801); // facade, bit 0 set
        BinaryPrimitives.WriteUInt32LittleEndian(tiles.AsSpan(sizeof(uint)), 0xB6000802); // facade, bit 0 clear
        if (!IsFacadeWalkable(tiles, 0, 0) || IsFacadeWalkable(tiles, 0, 1) || IsFacade(tiles, 0, 2)) throw new InvalidOperationException("Synthetic facade walkability classification regression.");
    }

    private static void EmitCell(ReadOnlySpan<byte> sectorBytes, int tileStart, ReadOnlySpan<byte> mask, int ordinal)
    {
        var x = ordinal & 63;
        var y = ordinal >> 6;
        var block = IsBlocked(mask, ordinal);
        var facade = IsFacade(sectorBytes, tileStart, ordinal);
        // `tile_is_blocking` returns immediately for either a set base-mask bit
        // or a non-walkable facade. This remains a terrain component fact only:
        // combined occupancy still preserves unresolved object and actor context.
        var facadeBlocks = facade && !IsFacadeWalkable(sectorBytes, tileStart, ordinal);
        var terrainBlocks = block || facadeBlocks;
        var terrain = terrainBlocks ? "Blocked" : "null";
        var terrainCoverage = terrainBlocks ? "Supported" : "Unknown";
        var reason = block ? "serialized-block-mask-set" : facadeBlocks ? "facade-walkability-blocked" : facade ? "facade-walkability-clear-but-static-unresolved" : "tile-name-rule-and-static-unresolved";
        Console.WriteLine($"passability cell ordinal={ordinal} local={x},{y} world={checked(OriginX + x)},{checked(OriginY + y)} serialized_block={(block ? "set" : "clear")} class={(facade ? "facade" : "tile")} facade_walkability={(facade ? (IsFacadeWalkable(sectorBytes, tileStart, ordinal) ? "set" : "clear") : "na")} terrain_component_coverage={terrainCoverage} terrain_component_disposition={terrain} occupancy_coverage=Unknown occupancy_disposition=null reason={reason}");
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
