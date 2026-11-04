using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-067: the PE32 reader on a synthetic image (built here, no original bytes) and its typed refusals.</summary>
[TestClass]
public sealed class PeImageTests
{
    private const int PeOffset = 0x80;
    private const int Optional = PeOffset + 24;
    private const int SectionTable = Optional + 0xE0;

    /// <summary>
    /// A three-section image at 0x400000: .text (RX, two calls through import slots), .idata (RW, one KERNEL32.dll
    /// descriptor with a named and an ordinal import) and .bss (RW, no raw data).
    /// </summary>
    internal static byte[] SyntheticImage()
    {
        var file = new byte[0x600];
        var span = file.AsSpan();
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x3C..], PeOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[PeOffset..], 0x0000_4550);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 4)..], 0x014C);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 6)..], 3);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 20)..], 0xE0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[Optional..], 0x010B);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 16)..], 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 28)..], 0x0040_0000);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 32)..], 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 36)..], 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 56)..], 0x4000);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 60)..], 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 92)..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 104)..], 0x2000);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 108)..], 40);
        Section(span, 0, ".text", 0x10, 0x1000, 0x200, 0x200, 0x6000_0020);
        Section(span, 1, ".idata", 0x100, 0x2000, 0x200, 0x400, 0xC000_0040);
        Section(span, 2, ".bss", 0x800, 0x3000, 0, 0, 0xC000_0080);
        // .text: call [0x402028] ; ret ; call [0x40202C] ; ret
        Cpu.Bytes("FF 15 28 20 40 00 C3  FF 15 2C 20 40 00 C3").CopyTo(span[0x200..]);
        // .idata at file 0x400 = RVA 0x2000
        var idata = span[0x400..];
        BinaryPrimitives.WriteUInt32LittleEndian(idata[0x00..], 0x2034); // lookup table
        BinaryPrimitives.WriteUInt32LittleEndian(idata[0x0C..], 0x2060); // library name
        BinaryPrimitives.WriteUInt32LittleEndian(idata[0x10..], 0x2028); // import slots
        foreach (var at in new[] { 0x28, 0x34 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(idata[at..], 0x2040);
            BinaryPrimitives.WriteUInt32LittleEndian(idata[(at + 4)..], 0x8000_0007);
        }
        Encoding.ASCII.GetBytes("GetTickCount\0").CopyTo(idata[0x42..]);
        Encoding.ASCII.GetBytes("KERNEL32.dll\0").CopyTo(idata[0x60..]);
        return file;
    }

    private static void Section(Span<byte> file, int index, string name, uint virtualSize, uint virtualAddress, uint rawSize, uint rawOffset, uint characteristics)
    {
        var at = file[(SectionTable + 40 * index)..];
        Encoding.ASCII.GetBytes(name).CopyTo(at);
        BinaryPrimitives.WriteUInt32LittleEndian(at[8..], virtualSize);
        BinaryPrimitives.WriteUInt32LittleEndian(at[12..], virtualAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(at[16..], rawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(at[20..], rawOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(at[36..], characteristics);
    }

    [TestMethod]
    public void ReadsHeadersSectionsAndImports()
    {
        var result = PeImage.Read(SyntheticImage());
        Assert.IsTrue(result.Succeeded, result.Failure?.ToString());
        var image = result.Image!;
        Assert.AreEqual((0x0040_0000u, 0x4000u, 0x200u, 0x0040_1000u), (image.ImageBase, image.SizeOfImage, image.SizeOfHeaders, image.EntryPoint));
        CollectionAssert.AreEqual(new[] { ".text", ".idata", ".bss" }, image.Sections.Select(s => s.Name).ToArray());
        Assert.AreEqual(MemoryAccess.ReadExecute, image.Sections[0].Access);
        Assert.AreEqual(MemoryAccess.ReadWrite, image.Sections[1].Access);
        Assert.AreEqual(".bss", image.SectionAt(0x0040_37FF)!.Name);
        Assert.IsNull(image.SectionAt(0x0040_3800));
        CollectionAssert.AreEqual(new[] { "KERNEL32.dll!GetTickCount", "KERNEL32.dll!#7" }, image.Imports.Select(i => i.FullName).ToArray());
        CollectionAssert.AreEqual(new[] { 0x0040_2028u, 0x0040_202Cu }, image.Imports.Select(i => i.SlotAddress).ToArray());
    }

    [TestMethod]
    public void LoadsTheImageWithItsAccessAndBindsImports()
    {
        var image = PeImage.Read(SyntheticImage()).Image!;
        var machine = new X86Machine();
        image.LoadInto(machine.Memory);
        machine.BindImports(image);
        Assert.AreEqual((ushort)0x5A4D, machine.Memory.ReadU16(0x0040_0000));
        Assert.ThrowsExactly<MachineFaultException>(() => machine.Memory.WriteU8(0x0040_0000, 0));
        Assert.ThrowsExactly<MachineFaultException>(() => machine.Memory.WriteU8(0x0040_1000, 0));
        Assert.IsTrue(machine.Memory.CanExecute(0x0040_1000));
        Assert.IsFalse(machine.Memory.CanExecute(0x0040_2000));
        Assert.AreEqual(0u, machine.Memory.ReadU32(0x0040_37FC), ".bss is zeroed");
        machine.Memory.WriteU32(0x0040_3000, 1);

        var unhooked = machine.Call(image.EntryPoint, [], 100).Fault!;
        Assert.AreEqual(MachineFaultKind.UnhookedImport, unhooked.Kind);
        Assert.AreEqual("KERNEL32.dll!GetTickCount", unhooked.Detail);

        Assert.IsTrue(machine.HookImport("kernel32.DLL!GetTickCount", m => { m.Eax = 1234; return HookResult.Return(); }));
        Assert.IsTrue(machine.HookImport("KERNEL32.dll!#7", m => { m.Eax = 7; return HookResult.Return(); }));
        Assert.IsFalse(machine.HookImport("USER32.dll!MessageBoxA", _ => HookResult.Return()));
        Assert.AreEqual(1234u, machine.Call(image.EntryPoint, [], 100).Eax);
        Assert.AreEqual(7u, machine.Call(image.EntryPoint + 7, [], 100).Eax);
    }

    [TestMethod]
    [DataRow("not MZ", PeReadFailureKind.NotMz)]
    [DataRow("short", PeReadFailureKind.Truncated)]
    [DataRow("PE offset", PeReadFailureKind.Truncated)]
    [DataRow("not PE", PeReadFailureKind.NotPe)]
    [DataRow("x64", PeReadFailureKind.NotI386)]
    [DataRow("PE32+", PeReadFailureKind.NotPe32)]
    [DataRow("unaligned section", PeReadFailureKind.BadSection)]
    [DataRow("overlapping sections", PeReadFailureKind.BadSection)]
    [DataRow("section past the image", PeReadFailureKind.BadSection)]
    [DataRow("raw data past the file", PeReadFailureKind.Truncated)]
    [DataRow("import table in .bss", PeReadFailureKind.BadImportTable)]
    [DataRow("unterminated import name", PeReadFailureKind.BadImportTable)]
    [DataRow("image too large", PeReadFailureKind.TooLarge)]
    public void RefusesMalformedImages(string damage, PeReadFailureKind kind)
    {
        var file = SyntheticImage();
        var span = file.AsSpan();
        switch (damage)
        {
            case "not MZ": file[0] = (byte)'X'; break;
            case "short": file = file[..0x30]; break;
            case "PE offset": BinaryPrimitives.WriteUInt32LittleEndian(span[0x3C..], 0x5F0); break;
            case "not PE": file[PeOffset + 1] = (byte)'X'; break;
            case "x64": BinaryPrimitives.WriteUInt16LittleEndian(span[(PeOffset + 4)..], 0x8664); break;
            case "PE32+": BinaryPrimitives.WriteUInt16LittleEndian(span[Optional..], 0x020B); break;
            case "unaligned section": BinaryPrimitives.WriteUInt32LittleEndian(span[(SectionTable + 40 + 12)..], 0x2010); break;
            case "overlapping sections": BinaryPrimitives.WriteUInt32LittleEndian(span[(SectionTable + 40 + 12)..], 0x1000); break;
            case "section past the image": BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 56)..], 0x3000); break;
            case "raw data past the file": BinaryPrimitives.WriteUInt32LittleEndian(span[(SectionTable + 40 + 20)..], 0x10000); break;
            case "import table in .bss": BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 104)..], 0x3000); break;
            case "unterminated import name":
                for (var i = 0x400 + 0x60; i < file.Length; i++) file[i] = (byte)'A';
                break;
            case "image too large": BinaryPrimitives.WriteUInt32LittleEndian(span[(Optional + 56)..], 0x2000_0000); break;
        }
        var result = PeImage.Read(file);
        Assert.IsFalse(result.Succeeded, damage);
        Assert.AreEqual(kind, result.Failure!.Kind, result.Failure.Detail);
    }

    [TestMethod]
    public void HonoursItsLimits()
    {
        Assert.AreEqual(PeReadFailureKind.TooLarge, PeImage.Read(SyntheticImage(), new PeReadLimits { MaxFileBytes = 0x100 }).Failure!.Kind);
        Assert.AreEqual(PeReadFailureKind.TooManySections, PeImage.Read(SyntheticImage(), new PeReadLimits { MaxSections = 2 }).Failure!.Kind);
        Assert.AreEqual(PeReadFailureKind.BadImportTable, PeImage.Read(SyntheticImage(), new PeReadLimits { MaxImports = 1 }).Failure!.Kind);
    }
}
