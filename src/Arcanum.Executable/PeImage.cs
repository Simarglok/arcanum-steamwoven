using System.Buffers.Binary;
using System.Text;

namespace Arcanum.Executable;

/// <summary>One section of a <see cref="PeImage"/>: its virtual range, its bytes in the file and its access.</summary>
public sealed record PeSection(string Name, uint VirtualAddress, uint VirtualSize, uint RawOffset, uint RawSize, uint Characteristics)
{
    public const uint Executable = 0x2000_0000;
    public const uint Readable = 0x4000_0000;
    public const uint Writable = 0x8000_0000;

    public MemoryAccess Access =>
        ((Characteristics & Readable) != 0 ? MemoryAccess.Read : MemoryAccess.None)
        | ((Characteristics & Writable) != 0 ? MemoryAccess.ReadWrite : MemoryAccess.None)
        | ((Characteristics & Executable) != 0 ? MemoryAccess.Execute | MemoryAccess.Read : MemoryAccess.None);

    /// <summary>The mapped length: the virtual size, or the raw size when the linker left the virtual size 0.</summary>
    public uint MappedSize => VirtualSize != 0 ? VirtualSize : RawSize;
}

/// <summary>One imported function: its DLL, its name (or <c>#ordinal</c>) and the absolute address of its import slot.</summary>
public sealed record PeImport(string Library, string Name, uint SlotAddress)
{
    public string FullName => $"{Library}!{Name}";
}

public enum PeReadFailureKind
{
    TooLarge,
    Truncated,
    NotMz,
    NotPe,
    NotI386,
    NotPe32,
    TooManySections,
    BadSection,
    BadImportTable,
}

public sealed record PeReadFailure(PeReadFailureKind Kind, string Detail);

/// <summary>The image or the reason the file is not a supported PE32 i386 image.</summary>
public sealed record PeReadResult(PeImage? Image, PeReadFailure? Failure)
{
    public bool Succeeded => Image is not null;
}

/// <summary>Bounds of <see cref="PeImage.Read"/>.</summary>
public sealed record PeReadLimits
{
    public long MaxFileBytes { get; init; } = 64L << 20;
    public uint MaxImageBytes { get; init; } = 256u << 20;
    public int MaxSections { get; init; } = 96;
    public int MaxImports { get; init; } = 8192;
    public int MaxNameBytes { get; init; } = 256;
}

/// <summary>
/// ARC-DEV-067: a read-only PE32 (i386) image for <see cref="X86Machine"/>: its fixed image base, its sections and its
/// import slots. Relocations are not applied (the image is mapped at its own base) and nothing is resolved: an import slot
/// points into an unmapped thunk range once <see cref="X86Machine.BindImports"/> ran, so an unhooked import stops the run.
/// The file's bytes stay in this object only; nothing of them is written anywhere by this type.
/// </summary>
public sealed class PeImage
{
    private readonly byte[] file;

    private PeImage(byte[] file, uint imageBase, uint sizeOfImage, uint sizeOfHeaders, uint entryPoint, uint sectionAlignment,
        IReadOnlyList<PeSection> sections, IReadOnlyList<PeImport> imports)
    {
        this.file = file;
        ImageBase = imageBase;
        SizeOfImage = sizeOfImage;
        SizeOfHeaders = sizeOfHeaders;
        EntryPoint = entryPoint;
        SectionAlignment = sectionAlignment;
        Sections = sections;
        Imports = imports;
    }

    public uint ImageBase { get; }
    public uint SizeOfImage { get; }
    public uint SizeOfHeaders { get; }
    /// <summary>Absolute address of the entry point.</summary>
    public uint EntryPoint { get; }
    public uint SectionAlignment { get; }
    public IReadOnlyList<PeSection> Sections { get; }
    public IReadOnlyList<PeImport> Imports { get; }

    /// <summary>The section holding the absolute address, if any.</summary>
    public PeSection? SectionAt(uint address)
    {
        var rva = address - ImageBase;
        foreach (var section in Sections)
            if (rva >= section.VirtualAddress && rva - section.VirtualAddress < section.MappedSize) return section;
        return null;
    }

    /// <summary>Maps the headers (read only) and every section with its own access at the image base, then copies their bytes.</summary>
    /// <exception cref="ArgumentException">A range of the image is already mapped.</exception>
    public void LoadInto(MachineMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        memory.Map(ImageBase, SizeOfHeaders, MemoryAccess.Read, "headers");
        memory.Load(ImageBase, file.AsSpan(0, (int)Math.Min(SizeOfHeaders, (uint)file.Length)));
        foreach (var section in Sections)
        {
            memory.Map(ImageBase + section.VirtualAddress, section.MappedSize, section.Access, section.Name);
            var copy = Math.Min(section.RawSize, section.MappedSize);
            if (copy > 0) memory.Load(ImageBase + section.VirtualAddress, file.AsSpan((int)section.RawOffset, (int)copy));
        }
    }

    public static PeReadResult Read(byte[] file, PeReadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        limits ??= new PeReadLimits();
        static PeReadResult Fail(PeReadFailureKind kind, string detail) => new(null, new PeReadFailure(kind, detail));

        if (file.LongLength > limits.MaxFileBytes) return Fail(PeReadFailureKind.TooLarge, $"{file.LongLength} bytes exceed {limits.MaxFileBytes}");
        if (file.Length < 0x40) return Fail(PeReadFailureKind.Truncated, "no DOS header");
        if (file[0] != 'M' || file[1] != 'Z') return Fail(PeReadFailureKind.NotMz, "no MZ signature");
        var pe = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x3C));
        if ((ulong)pe + 24 > (ulong)file.Length) return Fail(PeReadFailureKind.Truncated, $"PE header offset 0x{pe:X} is outside the file");
        if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)pe)) != 0x0000_4550) return Fail(PeReadFailureKind.NotPe, "no PE signature");
        var coff = (int)pe + 4;
        var machine = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(coff));
        if (machine != 0x014C) return Fail(PeReadFailureKind.NotI386, $"machine 0x{machine:X4} is not i386");
        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(coff + 2));
        int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(coff + 16));
        if (sectionCount > limits.MaxSections) return Fail(PeReadFailureKind.TooManySections, $"{sectionCount} sections exceed {limits.MaxSections}");
        var optional = coff + 20;
        if (optionalSize < 96 || optional + optionalSize > file.Length) return Fail(PeReadFailureKind.Truncated, $"optional header of {optionalSize} bytes");
        var magic = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(optional));
        if (magic != 0x010B) return Fail(PeReadFailureKind.NotPe32, $"optional header magic 0x{magic:X4} is not PE32");
        var entry = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(optional + 16));
        var imageBase = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(optional + 28));
        var sectionAlignment = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(optional + 32));
        var sizeOfImage = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(optional + 56));
        var sizeOfHeaders = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(optional + 60));
        var directoryCount = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(optional + 92));
        if (sizeOfImage == 0 || sizeOfImage > limits.MaxImageBytes) return Fail(PeReadFailureKind.TooLarge, $"image size 0x{sizeOfImage:X}");
        if ((imageBase & (MachineMemory.PageSize - 1)) != 0 || (ulong)imageBase + sizeOfImage > 1UL << 32)
            return Fail(PeReadFailureKind.BadSection, $"image base 0x{imageBase:X8} with size 0x{sizeOfImage:X}");
        if (sectionAlignment < MachineMemory.PageSize || (sectionAlignment & (MachineMemory.PageSize - 1)) != 0)
            return Fail(PeReadFailureKind.BadSection, $"section alignment 0x{sectionAlignment:X} is not a multiple of the page");
        if (sizeOfHeaders == 0 || sizeOfHeaders > sizeOfImage) return Fail(PeReadFailureKind.BadSection, $"header size 0x{sizeOfHeaders:X}");

        var table = optional + optionalSize;
        if ((long)table + 40L * sectionCount > file.Length) return Fail(PeReadFailureKind.Truncated, "section table");
        var sections = new List<PeSection>(sectionCount);
        ulong previousEnd = AlignUp(sizeOfHeaders, MachineMemory.PageSize);
        for (var i = 0; i < sectionCount; i++)
        {
            var at = file.AsSpan(table + 40 * i, 40);
            var name = Encoding.ASCII.GetString(at[..8]).TrimEnd('\0');
            var section = new PeSection(name,
                BinaryPrimitives.ReadUInt32LittleEndian(at[12..]), BinaryPrimitives.ReadUInt32LittleEndian(at[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(at[20..]), BinaryPrimitives.ReadUInt32LittleEndian(at[16..]),
                BinaryPrimitives.ReadUInt32LittleEndian(at[36..]));
            if (section.MappedSize == 0) return Fail(PeReadFailureKind.BadSection, $"section {name} is empty");
            if ((section.VirtualAddress & (MachineMemory.PageSize - 1)) != 0 || section.VirtualAddress < previousEnd)
                return Fail(PeReadFailureKind.BadSection, $"section {name} at 0x{section.VirtualAddress:X} overlaps or is not page aligned");
            var end = AlignUp((ulong)section.VirtualAddress + section.MappedSize, MachineMemory.PageSize);
            if (end > sizeOfImage) return Fail(PeReadFailureKind.BadSection, $"section {name} ends past the image size");
            if (section.RawSize > 0 && (ulong)section.RawOffset + Math.Min(section.RawSize, section.MappedSize) > (ulong)file.Length)
                return Fail(PeReadFailureKind.Truncated, $"section {name} raw data is outside the file");
            previousEnd = end;
            sections.Add(section);
        }

        var imports = new List<PeImport>();
        if (directoryCount > 1 && optionalSize >= 96 + 16)
        {
            var importRva = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(optional + 96 + 8));
            var importSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(optional + 96 + 12));
            if (importRva != 0 && importSize != 0)
            {
                var failure = ReadImports(file, sections, imageBase, importRva, limits, imports);
                if (failure is not null) return Fail(PeReadFailureKind.BadImportTable, failure);
            }
        }
        return new(new PeImage(file, imageBase, sizeOfImage, sizeOfHeaders, imageBase + entry, sectionAlignment, sections, imports), null);
    }

    private static ulong AlignUp(ulong value, uint alignment) => (value + alignment - 1) & ~(ulong)(alignment - 1);

    private static string? ReadImports(byte[] file, List<PeSection> sections, uint imageBase, uint directoryRva, PeReadLimits limits, List<PeImport> imports)
    {
        for (uint descriptor = directoryRva; ; descriptor += 20)
        {
            if (FileOffset(sections, descriptor, 20, file.Length) is not { } at) return $"import descriptor at RVA 0x{descriptor:X} is outside the file";
            var lookup = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
            var nameRva = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 12));
            var slots = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 16));
            if (lookup == 0 && nameRva == 0 && slots == 0) return null;
            if (ReadName(file, sections, nameRva, limits.MaxNameBytes) is not { } library) return $"import library name at RVA 0x{nameRva:X}";
            var names = lookup != 0 ? lookup : slots;
            for (uint i = 0; ; i++)
            {
                if (FileOffset(sections, names + 4 * i, 4, file.Length) is not { } thunk) return $"import lookup of {library} is outside the file";
                var entry = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(thunk));
                if (entry == 0) break;
                string name;
                if ((entry & 0x8000_0000) != 0) name = $"#{entry & 0xFFFF}";
                else if (ReadName(file, sections, entry + 2, limits.MaxNameBytes) is { } text) name = text;
                else return $"import name of {library} at RVA 0x{entry:X}";
                if (imports.Count >= limits.MaxImports) return $"more than {limits.MaxImports} imports";
                var slot = (ulong)slots + 4 * i;
                if (!sections.Any(s => slot >= s.VirtualAddress && slot + 4 <= (ulong)s.VirtualAddress + s.MappedSize))
                    return $"import slot of {library}!{name} at RVA 0x{slot:X} is outside every section";
                imports.Add(new PeImport(library, name, imageBase + slots + 4 * i));
            }
        }
    }

    private static string? ReadName(byte[] file, List<PeSection> sections, uint rva, int maxBytes)
    {
        if (FileOffset(sections, rva, 1, file.Length) is not { } at) return null;
        var end = Array.IndexOf(file, (byte)0, at, Math.Min(maxBytes + 1, file.Length - at));
        return end < 0 ? null : Encoding.ASCII.GetString(file, at, end - at);
    }

    private static int? FileOffset(List<PeSection> sections, uint rva, uint length, int fileLength)
    {
        foreach (var section in sections)
        {
            if (rva < section.VirtualAddress) continue;
            var offset = (ulong)rva - section.VirtualAddress;
            if (offset + length > Math.Min(section.RawSize, section.MappedSize)) continue;
            var at = section.RawOffset + offset;
            return at + length <= (ulong)fileLength ? (int)at : null;
        }
        return null;
    }
}
