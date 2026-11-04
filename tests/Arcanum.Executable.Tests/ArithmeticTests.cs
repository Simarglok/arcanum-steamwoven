using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-067: add, adc, sub, sbb, cmp, and, or, xor, test, inc, dec, neg, not and their flag rules on 8, 16 and 32 bits.</summary>
[TestClass]
public sealed class ArithmeticTests
{
    // Each row: code, EAX, EBX, flags before, EAX after, flags after (only the listed flags are checked).
    [TestMethod]
    [DataRow("01 D8", 0x7FFF_FFFFu, 1u, "", 0x8000_0000u, "O1 S1 Z0 C0 A1 P1", DisplayName = "add: signed overflow")]
    [DataRow("01 D8", 0xFFFF_FFFFu, 1u, "", 0u, "C1 Z1 O0 S0 A1 P1", DisplayName = "add: carry to zero")]
    [DataRow("01 D8", 0x0000_0001u, 2u, "", 3u, "C0 Z0 O0 S0 A0 P1", DisplayName = "add: two bits, parity even")]
    [DataRow("01 D8", 0x0000_0001u, 0u, "", 1u, "P0", DisplayName = "add: one bit, parity odd")]
    [DataRow("00 D8", 0x1234_5680u, 0x80u, "", 0x1234_5600u, "C1 O1 Z1 S0 A0", DisplayName = "add al, bl: carry and overflow, upper bytes kept")]
    [DataRow("00 D8", 0x0000_000Fu, 0x01u, "", 0x10u, "A1 C0 O0", DisplayName = "add al, bl: adjust carry")]
    [DataRow("66 01 D8", 0xAAAA_FFFFu, 1u, "", 0xAAAA_0000u, "C1 Z1 O0", DisplayName = "add ax, bx: 16-bit carry, upper half kept")]
    [DataRow("66 01 D8", 0x0000_7FFFu, 1u, "", 0x8000u, "O1 S1 C0", DisplayName = "add ax, bx: 16-bit overflow")]
    [DataRow("11 D8", 0xFFFF_FFFFu, 0u, "C1", 0u, "C1 Z1 A1 O0", DisplayName = "adc: carry in wraps")]
    [DataRow("11 D8", 0x7FFF_FFFFu, 0u, "C1", 0x8000_0000u, "O1 C0 S1", DisplayName = "adc: carry in overflows")]
    [DataRow("11 D8", 5u, 6u, "C0", 11u, "C0", DisplayName = "adc: no carry in")]
    [DataRow("29 D8", 0u, 1u, "", 0xFFFF_FFFFu, "C1 S1 O0 A1 P1 Z0", DisplayName = "sub: borrow")]
    [DataRow("29 D8", 0x8000_0000u, 1u, "", 0x7FFF_FFFFu, "O1 S0 C0 A1 P1", DisplayName = "sub: signed overflow")]
    [DataRow("29 D8", 7u, 7u, "", 0u, "Z1 C0 O0 S0 P1", DisplayName = "sub: equal")]
    [DataRow("28 D8", 0x10u, 0x01u, "", 0x0Fu, "A1 C0", DisplayName = "sub al, bl: adjust borrow")]
    [DataRow("19 D8", 5u, 5u, "C1", 0xFFFF_FFFFu, "C1 S1 Z0", DisplayName = "sbb: borrow in")]
    [DataRow("19 D8", 0u, 0xFFFF_FFFFu, "C1", 0u, "C1 Z1", DisplayName = "sbb: subtrahend plus borrow wraps")]
    [DataRow("19 D8", 9u, 4u, "C0", 5u, "C0", DisplayName = "sbb: no borrow in")]
    [DataRow("39 D8", 3u, 5u, "", 3u, "C1 S1 Z0 O0", DisplayName = "cmp: below, not stored")]
    [DataRow("39 D8", 0x8000_0000u, 1u, "", 0x8000_0000u, "O1 S0 C0", DisplayName = "cmp: signed overflow (jl not taken)")]
    [DataRow("38 D8", 0x80u, 0x80u, "", 0x80u, "Z1 C0", DisplayName = "cmp al, bl: equal")]
    [DataRow("31 C0", 0x1234_5678u, 0u, "C1 O1", 0u, "Z1 P1 C0 O0 S0", DisplayName = "xor eax, eax: clears CF and OF")]
    [DataRow("21 D8", 0xF0F0_F0F0u, 0x8000_00FFu, "C1 O1", 0x8000_00F0u, "S1 Z0 C0 O0 P1", DisplayName = "and")]
    [DataRow("09 D8", 0x0000_0001u, 0x0000_0002u, "C1", 3u, "P1 C0 S0", DisplayName = "or")]
    [DataRow("85 D8", 0x0000_0100u, 0x0000_0001u, "C1", 0x100u, "Z1 C0", DisplayName = "test: not stored")]
    [DataRow("84 E3", 0x0000_8000u, 0x0000_0080u, "", 0x8000u, "S1 Z0", DisplayName = "test bl, ah: high byte register")]
    [DataRow("00 E3", 0x0000_0100u, 0x0000_00FFu, "", 0x100u, "C1 Z1", DisplayName = "add bl, ah: high byte source")]
    [DataRow("83 C0 FF", 1u, 0u, "", 0u, "C1 Z1", DisplayName = "add eax, -1: sign-extended byte immediate")]
    [DataRow("83 E8 01", 0u, 0u, "", 0xFFFF_FFFFu, "C1 S1", DisplayName = "sub eax, 1: byte immediate")]
    [DataRow("81 F0 FF 00 FF 00", 0x0F0F_0F0Fu, 0u, "", 0x0FF0_0FF0u, "S0 C0", DisplayName = "xor eax, imm32")]
    [DataRow("66 81 C0 01 00", 0x5555_FFFFu, 0u, "", 0x5555_0000u, "C1 Z1", DisplayName = "add ax, imm16")]
    [DataRow("80 F8 7F", 0x80u, 0u, "", 0x80u, "O1 S0 C0", DisplayName = "cmp al, 0x7F (80 /7)")]
    [DataRow("82 C0 01", 0xFFu, 0u, "", 0u, "C1 Z1", DisplayName = "add al, 1 (the 82 alias)")]
    [DataRow("04 01", 0x7Fu, 0u, "", 0x80u, "O1 S1", DisplayName = "add al, imm8")]
    [DataRow("05 01 00 00 00", 0xFFFF_FFFFu, 0u, "", 0u, "C1 Z1", DisplayName = "add eax, imm32")]
    [DataRow("2D 02 00 00 00", 1u, 0u, "", 0xFFFF_FFFFu, "C1", DisplayName = "sub eax, imm32")]
    [DataRow("3C 05", 5u, 0u, "", 5u, "Z1", DisplayName = "cmp al, imm8")]
    [DataRow("A8 80", 0x80u, 0u, "", 0x80u, "S1 Z0", DisplayName = "test al, imm8")]
    [DataRow("A9 00 00 00 80", 0x8000_0000u, 0u, "", 0x8000_0000u, "S1 Z0 C0 O0", DisplayName = "test eax, imm32")]
    [DataRow("F7 C0 01 00 00 00", 2u, 0u, "", 2u, "Z1", DisplayName = "test eax, imm32 (F7 /0)")]
    [DataRow("40", 0x7FFF_FFFFu, 0u, "C1", 0x8000_0000u, "O1 S1 C1 A1", DisplayName = "inc: keeps CF")]
    [DataRow("40", 0xFFFF_FFFFu, 0u, "C0", 0u, "Z1 C0 O0", DisplayName = "inc: wraps without carry")]
    [DataRow("48", 0x8000_0000u, 0u, "C0", 0x7FFF_FFFFu, "O1 S0 C0 A1", DisplayName = "dec: signed overflow")]
    [DataRow("48", 0u, 0u, "C0", 0xFFFF_FFFFu, "S1 C0 A1", DisplayName = "dec: zero keeps CF")]
    [DataRow("66 40", 0x1234_FFFFu, 0u, "", 0x1234_0000u, "Z1", DisplayName = "inc ax")]
    [DataRow("FE C0", 0x7Fu, 0u, "", 0x80u, "O1 S1", DisplayName = "inc al (FE /0)")]
    [DataRow("FE C8", 0x00u, 0u, "", 0xFFu, "S1", DisplayName = "dec al (FE /1)")]
    [DataRow("FF C0", 1u, 0u, "", 2u, "Z0", DisplayName = "inc eax (FF /0)")]
    [DataRow("FF C8", 1u, 0u, "", 0u, "Z1", DisplayName = "dec eax (FF /1)")]
    [DataRow("F7 D8", 0u, 0u, "C1", 0u, "C0 Z1", DisplayName = "neg 0: no carry")]
    [DataRow("F7 D8", 5u, 0u, "", 0xFFFF_FFFBu, "C1 S1 O0", DisplayName = "neg 5")]
    [DataRow("F7 D8", 0x8000_0000u, 0u, "", 0x8000_0000u, "C1 O1", DisplayName = "neg the minimum")]
    [DataRow("F6 D8", 0x01u, 0u, "", 0xFFu, "C1 S1", DisplayName = "neg al")]
    [DataRow("F7 D0", 0x0F0F_0000u, 0u, "C1 Z1 O1", 0xF0F0_FFFFu, "C1 Z1 O1", DisplayName = "not: no flags")]
    [DataRow("F6 D0", 0xAAAA_AA0Fu, 0u, "", 0xAAAA_AAF0u, "", DisplayName = "not al")]
    public void AluMatchesTheManual(string code, uint eax, uint ebx, string before, uint expected, string after)
    {
        var machine = Cpu.Run(code, m =>
        {
            m.Eax = eax;
            m.Ebx = ebx;
            Cpu.SetFlags(m, before);
        });
        Assert.AreEqual(Cpu.Hex(expected), Cpu.Hex(machine.Eax));
        Cpu.Flags(machine, after);
    }

    [TestMethod]
    public void AluWritesMemoryOperandsAndCmpDoesNot()
    {
        // add [eax], ebx ; sub byte [eax+4], 1 ; cmp dword [eax+8], ebx ; or word [eax+12], 0x8001
        var machine = Cpu.Run("01 18  80 68 04 01  39 58 08  66 81 48 0C 01 80", m =>
        {
            m.Eax = Cpu.Data;
            m.Ebx = 0x10;
            m.Memory.WriteU32(Cpu.Data, 0xFFFF_FFF0);
            m.Memory.WriteU32(Cpu.Data + 4, 0x0000_0000);
            m.Memory.WriteU32(Cpu.Data + 8, 0x0000_0020);
            m.Memory.WriteU32(Cpu.Data + 12, 0xAAAA_0000);
        });
        Assert.AreEqual(0u, machine.Memory.ReadU32(Cpu.Data));
        Assert.AreEqual(0x0000_00FFu, machine.Memory.ReadU32(Cpu.Data + 4));
        Assert.AreEqual(0x20u, machine.Memory.ReadU32(Cpu.Data + 8));
        Assert.AreEqual(0xAAAA_8001u, machine.Memory.ReadU32(Cpu.Data + 12));
        Cpu.Flags(machine, "S1 C0 O0");
    }

    [TestMethod]
    public void RegisterDestinationFormsRead()
    {
        // mov ecx, 3 ; add ecx, [eax] (03 /r) ; sub cl, [eax+4] (2A /r) ; xor dl, dl ; or dl, [eax+5] (0A /r)
        var machine = Cpu.Run("B9 03 00 00 00  03 08  2A 48 04  30 D2  0A 50 05", m =>
        {
            m.Eax = Cpu.Data;
            m.Memory.WriteU32(Cpu.Data, 0x100);
            m.Memory.WriteU32(Cpu.Data + 4, 0x0000_7F01);
        });
        Assert.AreEqual(0x102u, machine.Ecx);
        Assert.AreEqual(0x7Fu, machine.Edx & 0xFF);
    }
}
