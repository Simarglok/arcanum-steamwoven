namespace Arcanum.Executable;

/// <summary>What the interpreted code may do with a mapped page.</summary>
[Flags]
public enum MemoryAccess
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    ReadWrite = Read | Write,
    ReadExecute = Read | Execute,
    All = Read | Write | Execute,
}

/// <summary>One mapped range of <see cref="MachineMemory"/>, page aligned.</summary>
public sealed record MemoryRegion(uint Address, uint Size, MemoryAccess Access, string Name)
{
    public ulong End => (ulong)Address + Size;
}

/// <summary>
/// ARC-DEV-067: the flat 32-bit memory of <see cref="X86Machine"/>. Only mapped pages exist; a read, write or instruction
/// fetch that touches an unmapped page, or a page without that access, raises a <see cref="MachineFaultException"/> with the
/// address, so an unknown path stops instead of reading invented bytes. The host maps the image, the stack and any heap; the
/// host-side <see cref="Load"/> and <see cref="Peek"/> ignore the access rights (the PE loader writes read-only sections).
/// </summary>
public sealed class MachineMemory
{
    public const int PageBits = 12;
    public const uint PageSize = 1u << PageBits;
    /// <summary>The most bytes one machine may map (the census needs about 4 MiB of image plus the host's stack and heap).</summary>
    public const ulong MaxMappedBytes = 1UL << 30;

    private sealed class Page(MemoryAccess access)
    {
        public readonly byte[] Data = new byte[PageSize];
        public readonly MemoryAccess Access = access;
    }

    // Two levels of 1,024 entries: 10 bits of directory, 10 bits of page, 12 bits of offset.
    private readonly Page?[]?[] directory = new Page?[]?[1024];
    private readonly List<MemoryRegion> regions = [];
    private ulong mappedBytes;

    public IReadOnlyList<MemoryRegion> Regions => regions;

    /// <summary>Maps <paramref name="size"/> zeroed bytes at a page-aligned <paramref name="address"/> (size rounded up to pages).</summary>
    /// <exception cref="ArgumentException">The range is not page aligned, wraps past 4 GiB, overlaps a mapped page or exceeds <see cref="MaxMappedBytes"/>.</exception>
    public MemoryRegion Map(uint address, uint size, MemoryAccess access, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if ((address & (PageSize - 1)) != 0) throw new ArgumentException($"address 0x{address:X8} is not page aligned", nameof(address));
        if (size == 0) throw new ArgumentException("size is 0", nameof(size));
        var rounded = ((ulong)size + PageSize - 1) & ~(ulong)(PageSize - 1);
        if ((ulong)address + rounded > 1UL << 32) throw new ArgumentException($"0x{address:X8}+0x{rounded:X} wraps past 4 GiB", nameof(size));
        if (mappedBytes + rounded > MaxMappedBytes) throw new ArgumentException($"mapping 0x{rounded:X} more bytes exceeds the limit 0x{MaxMappedBytes:X}", nameof(size));
        for (ulong page = address; page < address + rounded; page += PageSize)
            if (Find((uint)page) is not null) throw new ArgumentException($"page 0x{page:X8} is already mapped", nameof(address));
        for (ulong page = address; page < address + rounded; page += PageSize)
        {
            var a = (uint)page;
            var table = directory[a >> 22] ??= new Page?[1024];
            table[(a >> PageBits) & 1023] = new Page(access);
        }
        mappedBytes += rounded;
        var region = new MemoryRegion(address, (uint)rounded, access, name);
        regions.Add(region);
        return region;
    }

    public bool IsMapped(uint address) => Find(address) is not null;

    public bool CanExecute(uint address) => Find(address) is { } page && (page.Access & MemoryAccess.Execute) != 0;

    /// <summary>Host write that ignores the access rights (loading sections, writing an import slot); unmapped bytes still fault.</summary>
    public void Load(uint address, ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++) Require((uint)(address + i), MemoryAccess.None, MachineFaultKind.MemoryWrite).Data[(address + (uint)i) & (PageSize - 1)] = bytes[i];
    }

    /// <summary>Host read that ignores the access rights; unmapped bytes still fault.</summary>
    public void Peek(uint address, Span<byte> destination)
    {
        for (var i = 0; i < destination.Length; i++) destination[i] = Require((uint)(address + i), MemoryAccess.None, MachineFaultKind.MemoryRead).Data[(address + (uint)i) & (PageSize - 1)];
    }

    public byte ReadU8(uint address) => Require(address, MemoryAccess.Read, MachineFaultKind.MemoryRead).Data[address & (PageSize - 1)];

    public ushort ReadU16(uint address)
    {
        var offset = address & (PageSize - 1);
        if (offset <= PageSize - 2)
        {
            var data = Require(address, MemoryAccess.Read, MachineFaultKind.MemoryRead).Data;
            return (ushort)(data[offset] | data[offset + 1] << 8);
        }
        return (ushort)(ReadU8(address) | ReadU8(address + 1) << 8);
    }

    public uint ReadU32(uint address)
    {
        var offset = address & (PageSize - 1);
        if (offset <= PageSize - 4)
        {
            var data = Require(address, MemoryAccess.Read, MachineFaultKind.MemoryRead).Data;
            return (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);
        }
        return (uint)(ReadU8(address) | ReadU8(address + 1) << 8 | ReadU8(address + 2) << 16 | ReadU8(address + 3) << 24);
    }

    public void WriteU8(uint address, byte value) => Require(address, MemoryAccess.Write, MachineFaultKind.MemoryWrite).Data[address & (PageSize - 1)] = value;

    public void WriteU16(uint address, ushort value)
    {
        var offset = address & (PageSize - 1);
        if (offset <= PageSize - 2)
        {
            var data = Require(address, MemoryAccess.Write, MachineFaultKind.MemoryWrite).Data;
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            return;
        }
        // Check both pages before writing either, so a fault leaves memory unchanged.
        Require(address + 1, MemoryAccess.Write, MachineFaultKind.MemoryWrite);
        WriteU8(address, (byte)value);
        WriteU8(address + 1, (byte)(value >> 8));
    }

    public void WriteU32(uint address, uint value)
    {
        var offset = address & (PageSize - 1);
        if (offset <= PageSize - 4)
        {
            var data = Require(address, MemoryAccess.Write, MachineFaultKind.MemoryWrite).Data;
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
            return;
        }
        Require(address, MemoryAccess.Write, MachineFaultKind.MemoryWrite);
        Require(address + 3, MemoryAccess.Write, MachineFaultKind.MemoryWrite);
        for (var i = 0; i < 4; i++) WriteU8(address + (uint)i, (byte)(value >> (8 * i)));
    }

    public void ReadBytes(uint address, Span<byte> destination)
    {
        for (var i = 0; i < destination.Length; i++) destination[i] = ReadU8((uint)(address + i));
    }

    public void WriteBytes(uint address, ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++) WriteU8((uint)(address + i), bytes[i]);
    }

    /// <summary>Reads a zero-terminated byte string of at most <paramref name="maxLength"/> bytes (the terminator not counted).</summary>
    /// <exception cref="MachineFaultException">No terminator within the limit, or an unreadable byte.</exception>
    public byte[] ReadCString(uint address, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        var bytes = new List<byte>();
        for (var i = 0; i <= maxLength; i++)
        {
            var b = ReadU8((uint)(address + i));
            if (b == 0) return [.. bytes];
            bytes.Add(b);
        }
        throw new MachineFaultException(new MachineFault(MachineFaultKind.MemoryRead, 0, $"no terminator within {maxLength} bytes of 0x{address:X8}", address));
    }

    /// <summary>The page holding the code byte at <paramref name="address"/>; the page must be executable.</summary>
    internal byte[] FetchPage(uint address) => Require(address, MemoryAccess.Execute, MachineFaultKind.ExecuteOutsideCode).Data;

    private Page? Find(uint address) => directory[address >> 22]?[(address >> PageBits) & 1023];

    private Page Require(uint address, MemoryAccess access, MachineFaultKind kind)
    {
        var page = Find(address);
        if (page is null)
            throw new MachineFaultException(new MachineFault(kind, 0, $"0x{address:X8} is not mapped", address));
        if ((page.Access & access) != access)
            throw new MachineFaultException(new MachineFault(kind, 0, $"0x{address:X8} is mapped without {access} access", address));
        return page;
    }
}
