namespace Arcanum.Executable.Prototypes;

/// <summary>
/// The allocator behind the executable's own <c>malloc</c>, <c>calloc</c>, <c>realloc</c> and <c>free</c> (hooked: the
/// executable's Win32 heap manager is not interpreted). A bump allocator over mapped, zeroed guest pages with a free
/// list per 16-byte size class; every block is zeroed when it is handed out, so a build never reads a previous
/// block's bytes and its output does not depend on the allocation history. Limits are explicit.
/// </summary>
internal sealed class GuestHeap
{
    public const uint DefaultBase = 0x2000_0000;
    private const uint Granule = 16;
    private const uint ChunkBytes = 1u << 20;

    private readonly MachineMemory memory;
    private readonly uint baseAddress;
    private readonly uint maxBytes;
    private readonly Dictionary<uint, uint> blockSizes = [];
    private readonly Dictionary<uint, Stack<uint>> freeLists = [];
    private readonly byte fill;
    private uint mapped;
    private uint bump;

    /// <param name="fill">What a block that was not requested zeroed holds before the guest writes it (0 by default; research runs
    /// use another byte to find output that depends on uninitialized memory).</param>
    public GuestHeap(MachineMemory memory, uint maxBytes, byte fill = 0, uint baseAddress = DefaultBase)
    {
        if ((baseAddress & (MachineMemory.PageSize - 1)) != 0) throw new ArgumentException("the heap base is not page aligned", nameof(baseAddress));
        this.memory = memory;
        this.baseAddress = baseAddress;
        this.maxBytes = maxBytes;
        this.fill = fill;
        bump = baseAddress;
    }

    /// <summary>Blocks handed out and not freed.</summary>
    public int LiveBlocks => blockSizes.Count;

    /// <summary>Bytes of address space taken from the heap range so far.</summary>
    public uint HighWater => bump - baseAddress;

    /// <summary>A block of at least <paramref name="size"/> bytes (at least one granule), or 0 when the heap limit is reached; zeroed when <paramref name="zero"/>, else holding the fill byte.</summary>
    public uint Allocate(uint size, bool zero = true)
    {
        if (size > maxBytes) return 0;
        var rounded = Math.Max(Granule, (size + Granule - 1) & ~(Granule - 1));
        if (freeLists.TryGetValue(rounded, out var list) && list.Count > 0)
        {
            var reused = list.Pop();
            Fill(reused, rounded, zero);
            blockSizes[reused] = rounded;
            return reused;
        }
        if ((ulong)bump + rounded - baseAddress > maxBytes) return 0;
        var address = bump;
        bump += rounded;
        while (baseAddress + mapped < bump)
        {
            memory.Map(baseAddress + mapped, ChunkBytes, MemoryAccess.ReadWrite, "heap");
            mapped += ChunkBytes;
        }
        blockSizes[address] = rounded;
        if (!zero && fill != 0) Fill(address, rounded, zero: false);
        return address;
    }

    /// <summary>True when <paramref name="address"/> is the start of a block handed out and not freed.</summary>
    public bool IsLive(uint address) => blockSizes.ContainsKey(address);

    /// <summary>Releases a block; false when <paramref name="address"/> is not a live block (0 is accepted and ignored).</summary>
    public bool Free(uint address)
    {
        if (address == 0) return true;
        if (!blockSizes.Remove(address, out var size)) return false;
        if (!freeLists.TryGetValue(size, out var list)) freeLists[size] = list = new Stack<uint>();
        list.Push(address);
        return true;
    }

    /// <summary>The C <c>realloc</c>: 0 for the pointer allocates, size 0 frees; null result when the limit is reached or the block is unknown.</summary>
    public uint Reallocate(uint address, uint size)
    {
        if (address == 0) return Allocate(size, zero: false);
        if (size == 0)
        {
            Free(address);
            return 0;
        }
        if (!blockSizes.TryGetValue(address, out var old)) return 0;
        var replacement = Allocate(size, zero: false);
        if (replacement == 0) return 0;
        var copy = Math.Min(old, size);
        var bytes = new byte[copy];
        memory.Peek(address, bytes);
        memory.Load(replacement, bytes);
        Free(address);
        return replacement;
    }

    private void Fill(uint address, uint size, bool zero)
    {
        var bytes = new byte[size];
        if (!zero && fill != 0) Array.Fill(bytes, fill);
        memory.Load(address, bytes);
    }
}
