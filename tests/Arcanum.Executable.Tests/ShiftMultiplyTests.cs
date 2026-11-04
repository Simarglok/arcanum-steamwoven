using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-067: shifts, rotates, double shifts, multiply and divide with their flag rules and #DE.</summary>
[TestClass]
public sealed class ShiftMultiplyTests
{
    // Each row: code, EAX, ECX, flags before, EAX after, flags after.
    [TestMethod]
    [DataRow("D1 E0", 0x8000_0000u, 0u, "", 0u, "C1 Z1 O1", DisplayName = "shl eax, 1: carry out")]
    [DataRow("C1 E0 04", 0x0F00_0001u, 0u, "", 0xF000_0010u, "C0 S1 Z0", DisplayName = "shl eax, 4")]
    [DataRow("D3 E0", 0x0000_0003u, 0x21u, "", 6u, "C0", DisplayName = "shl eax, cl: count masked to 5 bits")]
    [DataRow("D3 E0", 0x1234_5678u, 0x20u, "C1 Z1 S1", 0x1234_5678u, "C1 Z1 S1", DisplayName = "shl eax, cl: count 0 changes nothing")]
    [DataRow("D2 E0", 0x0000_0001u, 8u, "", 0u, "Z1 S0", DisplayName = "shl al, 8: all bits out (CF undefined)")]
    [DataRow("D2 E0", 0x0000_00FFu, 9u, "C1", 0u, "Z1", DisplayName = "shl al, 9: past the width (CF undefined)")]
    [DataRow("D1 E8", 0x8000_0001u, 0u, "", 0x4000_0000u, "C1 O1 S0", DisplayName = "shr eax, 1: OF is the old sign")]
    [DataRow("C1 E8 1F", 0x8000_0000u, 0u, "", 1u, "C0 Z0", DisplayName = "shr eax, 31")]
    [DataRow("66 D3 E8", 0xABCD_8000u, 16u, "", 0xABCD_0000u, "Z1", DisplayName = "shr ax, 16 (CF undefined)")]
    [DataRow("C1 F8 1F", 0x8000_0000u, 0u, "", 0xFFFF_FFFFu, "C0 S1", DisplayName = "sar eax, 31")]
    [DataRow("D0 F8", 0x0000_0081u, 0u, "", 0xC0u, "C1 O0 S1", DisplayName = "sar al, 1")]
    [DataRow("D2 F8", 0x0000_0080u, 12u, "", 0xFFu, "S1", DisplayName = "sar al, 12: sign fill past the width")]
    [DataRow("D1 F0", 0x4000_0000u, 0u, "", 0x8000_0000u, "C0 O1", DisplayName = "sal eax, 1 (D1 /6)")]
    [DataRow("C1 C0 08", 0x1234_5678u, 0u, "", 0x3456_7812u, "C0", DisplayName = "rol eax, 8")]
    [DataRow("D1 C0", 0x8000_0000u, 0u, "", 1u, "C1 O1", DisplayName = "rol eax, 1")]
    [DataRow("D2 C0", 0x0000_0081u, 8u, "Z1", 0x81u, "C1 Z1", DisplayName = "rol al, 8: flags only, ZF kept")]
    [DataRow("C1 C8 04", 0x1234_5678u, 0u, "", 0x8123_4567u, "C1", DisplayName = "ror eax, 4")]
    [DataRow("D1 C8", 0x0000_0001u, 0u, "", 0x8000_0000u, "C1 O1", DisplayName = "ror eax, 1")]
    [DataRow("D1 D0", 0x8000_0000u, 0u, "C1", 1u, "C1 O1", DisplayName = "rcl eax, 1 through carry")]
    [DataRow("C1 D0 02", 0x4000_0000u, 0u, "C1", 2u, "C1", DisplayName = "rcl eax, 2")]
    [DataRow("D2 D0", 0x0000_0055u, 9u, "C1", 0x55u, "C1", DisplayName = "rcl al, 9: a full turn")]
    [DataRow("D0 D8", 0x0000_0001u, 0u, "C1", 0x80u, "C1 O1", DisplayName = "rcr al, 1 through carry")]
    [DataRow("D1 D8", 0x0000_0002u, 0u, "C0", 1u, "C0 O0", DisplayName = "rcr eax, 1")]
    public void ShiftMatchesTheManual(string code, uint eax, uint ecx, string before, uint expected, string after)
    {
        var machine = Cpu.Run(code, m =>
        {
            m.Eax = eax;
            m.Ecx = ecx;
            Cpu.SetFlags(m, before);
        });
        Assert.AreEqual(Cpu.Hex(expected), Cpu.Hex(machine.Eax));
        Cpu.Flags(machine, after);
    }

    [TestMethod]
    public void DoubleShiftsTakeBitsFromTheSource()
    {
        // shld eax, ebx, 4
        var left = Cpu.Run("0F A4 D8 04", m => { m.Eax = 0x1234_5678; m.Ebx = 0x9ABC_DEF0; });
        Assert.AreEqual(Cpu.Hex(0x2345_6789), Cpu.Hex(left.Eax));
        Cpu.Flags(left, "C1 S0 Z0");
        // shrd eax, ebx, cl
        var right = Cpu.Run("0F AD D8", m => { m.Eax = 0x1234_5678; m.Ebx = 0x9ABC_DEF0; m.Ecx = 8; });
        Assert.AreEqual(Cpu.Hex(0xF012_3456), Cpu.Hex(right.Eax));
        Cpu.Flags(right, "C0 S1");
        // shrd eax, ebx, 1: OF is the sign change
        var one = Cpu.Run("0F AC D8 01", m => { m.Eax = 0x0000_0001; m.Ebx = 1; });
        Assert.AreEqual(Cpu.Hex(0x8000_0000), Cpu.Hex(one.Eax));
        Cpu.Flags(one, "C1 O1");
        // shld with a count of 0 (cl = 32) changes nothing
        var none = Cpu.Run("0F A5 D8", m => { m.Eax = 5; m.Ebx = 7; m.Ecx = 32; m.Carry = true; });
        Assert.AreEqual(5u, none.Eax);
        Cpu.Flags(none, "C1");
    }

    [TestMethod]
    public void MultiplyIntoTheAccumulator()
    {
        // mul ebx
        var mul = Cpu.Run("F7 E3", m => { m.Eax = 0xFFFF_FFFF; m.Ebx = 2; });
        Assert.AreEqual((0xFFFF_FFFEu, 1u), (mul.Eax, mul.Edx));
        Cpu.Flags(mul, "C1 O1");
        var small = Cpu.Run("F7 E3", m => { m.Eax = 3; m.Ebx = 4; m.Edx = 0x55; });
        Assert.AreEqual((12u, 0u), (small.Eax, small.Edx));
        Cpu.Flags(small, "C0 O0");
        // mul bl: AX = AL × BL, upper EAX kept
        var mulByte = Cpu.Run("F6 E3", m => { m.Eax = 0xAAAA_0010; m.Ebx = 0x10; });
        Assert.AreEqual(Cpu.Hex(0xAAAA_0100), Cpu.Hex(mulByte.Eax));
        Cpu.Flags(mulByte, "C1 O1");
        // mul bx: DX:AX
        var mulWord = Cpu.Run("66 F7 E3", m => { m.Eax = 0x0000_FFFF; m.Ebx = 0xFFFF; m.Edx = 0x1111_0000; });
        Assert.AreEqual((0x0000_0001u, 0x1111_FFFEu), (mulWord.Eax, mulWord.Edx));
        // imul ebx: EDX:EAX signed
        var imul = Cpu.Run("F7 EB", m => { m.Eax = unchecked((uint)-2); m.Ebx = 3; });
        Assert.AreEqual((0xFFFF_FFFAu, 0xFFFF_FFFFu), (imul.Eax, imul.Edx));
        Cpu.Flags(imul, "C0 O0");
        var wide = Cpu.Run("F7 EB", m => { m.Eax = 0x4000_0000; m.Ebx = 4; });
        Assert.AreEqual((0u, 1u), (wide.Eax, wide.Edx));
        Cpu.Flags(wide, "C1 O1");
        // imul bl: AX signed
        var imulByte = Cpu.Run("F6 EB", m => { m.Eax = 0x80; m.Ebx = 0xFF; });
        Assert.AreEqual(0x0080u, imulByte.Eax & 0xFFFF);
        Cpu.Flags(imulByte, "C1 O1");
    }

    [TestMethod]
    public void TwoAndThreeOperandMultiplyTruncates()
    {
        var product = Cpu.Run("0F AF CB", m => { m.Ecx = 0x10000; m.Ebx = 0x10000; });
        Assert.AreEqual(0u, product.Ecx);
        Cpu.Flags(product, "C1 O1");
        var fits = Cpu.Run("6B C1 FD", m => m.Ecx = 5);
        Assert.AreEqual(Cpu.Hex(unchecked((uint)-15)), Cpu.Hex(fits.Eax));
        Cpu.Flags(fits, "C0 O0");
        var large = Cpu.Run("69 C1 00 00 01 00", m => m.Ecx = 0x10000);
        Assert.AreEqual(0u, large.Eax);
        Cpu.Flags(large, "C1 O1");
        var word = Cpu.Run("66 0F AF C3", m => { m.Eax = 0x7777_0100; m.Ebx = 0x100; });
        Assert.AreEqual(Cpu.Hex(0x7777_0000), Cpu.Hex(word.Eax));
        Cpu.Flags(word, "C1");
        var memory = Cpu.Run("0F AF 05 " + Cpu.Le(Cpu.Data), m => { m.Eax = 7; m.Memory.WriteU32(Cpu.Data, unchecked((uint)-6)); });
        Assert.AreEqual(Cpu.Hex(unchecked((uint)-42)), Cpu.Hex(memory.Eax));
    }

    [TestMethod]
    public void DivideAndRemainder()
    {
        var div = Cpu.Run("F7 F3", m => { m.Edx = 1; m.Eax = 0; m.Ebx = 2; });
        Assert.AreEqual((0x8000_0000u, 0u), (div.Eax, div.Edx));
        var divByte = Cpu.Run("F6 F3", m => { m.Eax = 0xEEEE_0105; m.Ebx = 0x10; });
        Assert.AreEqual(Cpu.Hex(0xEEEE_0510), Cpu.Hex(divByte.Eax));
        var divWord = Cpu.Run("66 F7 F3", m => { m.Edx = 0x0001; m.Eax = 0x0000; m.Ebx = 2; });
        Assert.AreEqual((0x8000u, 0u), (divWord.Eax, divWord.Edx));
        // idiv truncates toward zero and the remainder takes the dividend's sign
        var idiv = Cpu.Run("F7 FB", m => { m.Edx = 0xFFFF_FFFF; m.Eax = unchecked((uint)-7); m.Ebx = 2; });
        Assert.AreEqual((unchecked((uint)-3), unchecked((uint)-1)), (idiv.Eax, idiv.Edx));
        var idivByte = Cpu.Run("F6 FB", m => { m.Eax = 0xFF9C; m.Ebx = 7; });
        Assert.AreEqual(0xFEF2u, idivByte.Eax & 0xFFFF);
        var cdqIdiv = Cpu.Run("99 F7 F9", m => { m.Eax = 100; m.Ecx = unchecked((uint)-7); });
        Assert.AreEqual((unchecked((uint)-14), 2u), (cdqIdiv.Eax, cdqIdiv.Edx));
    }

    [TestMethod]
    [DataRow("F7 F3", 0u, 5u, 0u, DisplayName = "div by zero")]
    [DataRow("F7 F3", 2u, 0u, 2u, DisplayName = "div quotient too large")]
    [DataRow("F7 FB", 0xFFFF_FFFFu, 0x8000_0000u, 0xFFFF_FFFFu, DisplayName = "idiv minimum by -1")]
    [DataRow("F7 FB", 0u, 0x8000_0000u, 1u, DisplayName = "idiv quotient 2^31")]
    [DataRow("F6 FB", 0u, 0x8000u, 0xFFu, DisplayName = "idiv al: -32768 by -1")]
    public void DivideErrorStops(string code, uint edx, uint eax, uint ebx)
    {
        var fault = Cpu.Fault(code, m => { m.Edx = edx; m.Eax = eax; m.Ebx = ebx; });
        Assert.AreEqual(MachineFaultKind.DivideError, fault.Kind);
        Assert.AreEqual(Cpu.Code, fault.InstructionAddress);
    }
}
