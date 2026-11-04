using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-067: moves, addressing modes, extensions, exchanges, the stack, flag moves, bit instructions, FS.</summary>
[TestClass]
public sealed class DataMoveTests
{
    [TestMethod]
    public void MovesBetweenRegistersMemoryAndImmediates()
    {
        // mov [eax], ebx ; mov ecx, [eax] ; mov byte [eax+4], bl ; mov dl, [eax+4] ; mov dword [eax+8], imm32 ;
        // mov byte [eax+12], imm8 ; mov word [eax+14], imm16 ; mov ah, 0x7F (B4) ; mov si, 0x1234 (66 BE)
        var machine = Cpu.Run("89 18  8B 08  88 58 04  8A 50 04  C7 40 08 EF BE AD DE  C6 40 0C 5A  66 C7 40 0E 34 12  B4 7F  66 BE 34 12", m =>
        {
            m.Eax = Cpu.Data;
            m.Ebx = 0xCAFE_BABE;
            m.Esi = 0xFFFF_0000;
        });
        Assert.AreEqual(0xCAFE_BABEu, machine.Ecx);
        Assert.AreEqual(0xBEu, machine.Edx & 0xFF);
        Assert.AreEqual(0xDEAD_BEEFu, machine.Memory.ReadU32(Cpu.Data + 8));
        Assert.AreEqual(0x1234_005Au, machine.Memory.ReadU32(Cpu.Data + 12));
        Assert.AreEqual(Cpu.Hex(Cpu.Data & 0xFFFF_00FF | 0x7F00), Cpu.Hex(machine.Eax));
        Assert.AreEqual(0xFFFF_1234u, machine.Esi);
    }

    [TestMethod]
    public void AddressingModes()
    {
        var table = Cpu.Data + 0x100;
        // mov eax, [ebx+ecx*4+8] ; mov edx, [ecx*4+disp32] (SIB without base) ; mov esi, [disp32] ;
        // mov edi, [ebp-4] ; mov ebp, [esp+4] reads the argument above the return address
        var machine = Cpu.Machine("8B 44 8B 08  8B 14 8D " + Cpu.Le(table) + "  8B 35 " + Cpu.Le(table + 4) + "  8B 7D FC  8B 6C 24 04");
        machine.Ebx = table - 8;
        machine.Ecx = 2;
        machine.Ebp = table + 4;
        for (uint i = 0; i < 4; i++) machine.Memory.WriteU32(table + 4 * i, 0x1111_1111 * (i + 1));
        var result = machine.Call(Cpu.Code, [0xA5A5_A5A5], 1000);
        Assert.IsTrue(result.Completed, result.Fault?.ToString());
        Assert.AreEqual(0x3333_3333u, machine.Eax);
        Assert.AreEqual(0x3333_3333u, machine.Edx);
        Assert.AreEqual(0x2222_2222u, machine.Esi);
        Assert.AreEqual(0x1111_1111u, machine.Edi);
        Assert.AreEqual(0xA5A5_A5A5u, machine.Ebp);
    }

    [TestMethod]
    public void LeaComputesTheOffsetOnly()
    {
        // lea eax, [ecx+ecx*2] ; lea edx, [ecx+0x10] ; lea esi, fs:[0x10] (no segment base) ; lea di, [ecx+1]
        var machine = Cpu.Run("8D 04 49  8D 51 10  64 8D 35 10 00 00 00  66 8D 79 01", m => { m.Ecx = 5; m.Edi = 0xAAAA_AAAA; });
        Assert.AreEqual(15u, machine.Eax);
        Assert.AreEqual(0x15u, machine.Edx);
        Assert.AreEqual(0x10u, machine.Esi);
        Assert.AreEqual(0xAAAA_0006u, machine.Edi);
    }

    [TestMethod]
    public void AccumulatorOffsetMoves()
    {
        // mov eax, [disp] (A1) ; mov [disp+4], eax (A3) ; mov al, [disp+8] (A0) ; mov [disp+9], al (A2)
        var d = Cpu.Data;
        var machine = Cpu.Run("A1 " + Cpu.Le(d) + "  A3 " + Cpu.Le(d + 4) + "  A0 " + Cpu.Le(d + 8) + "  A2 " + Cpu.Le(d + 9), m =>
        {
            m.Memory.WriteU32(d, 0x1234_5678);
            m.Memory.WriteU8(d + 8, 0x9A);
        });
        Assert.AreEqual(0x1234_5678u, machine.Memory.ReadU32(d + 4));
        Assert.AreEqual(0x1234_569Au, machine.Eax);
        Assert.AreEqual(0x9Au, machine.Memory.ReadU8(d + 9));
    }

    [TestMethod]
    public void ExtensionsAndConversions()
    {
        // movzx eax, bl ; movsx ecx, bl ; movzx edx, bx ; movsx esi, bx ; movsx di, bl (66 0F BE)
        var machine = Cpu.Run("0F B6 C3  0F BE CB  0F B7 D3  0F BF F3  66 0F BE FB", m => { m.Ebx = 0x1234_8080; m.Edi = 0x5555_5555; });
        Assert.AreEqual(0x80u, machine.Eax);
        Assert.AreEqual(0xFFFF_FF80u, machine.Ecx);
        Assert.AreEqual(0x8080u, machine.Edx);
        Assert.AreEqual(0xFFFF_8080u, machine.Esi);
        Assert.AreEqual(0x5555_FF80u, machine.Edi);

        Assert.AreEqual(0xFFFF_8000u, Cpu.Run("98", m => m.Eax = 0x1234_8000).Eax, "cwde");
        Assert.AreEqual(0x1234_FF80u, Cpu.Run("66 98", m => m.Eax = 0x1234_5680).Eax, "cbw");
        Assert.AreEqual(0xFFFF_FFFFu, Cpu.Run("99", m => m.Eax = 0x8000_0000).Edx, "cdq negative");
        Assert.AreEqual(0u, Cpu.Run("99", m => { m.Eax = 0x7FFF_FFFF; m.Edx = 9; }).Edx, "cdq positive");
        Assert.AreEqual(0xAAAA_FFFFu, Cpu.Run("66 99", m => { m.Eax = 0x8000; m.Edx = 0xAAAA_0000; }).Edx, "cwd");
    }

    [TestMethod]
    public void Exchanges()
    {
        // xchg eax, ecx (91) ; xchg [esi], edx (87 16) ; xchg bl, bh (86 FB)
        var machine = Cpu.Run("91  87 16  86 FB", m =>
        {
            m.Eax = 1;
            m.Ecx = 2;
            m.Esi = Cpu.Data;
            m.Edx = 3;
            m.Memory.WriteU32(Cpu.Data, 4);
            m.Ebx = 0x0000_1234;
        });
        Assert.AreEqual((2u, 1u, 4u), (machine.Eax, machine.Ecx, machine.Edx));
        Assert.AreEqual(3u, machine.Memory.ReadU32(Cpu.Data));
        Assert.AreEqual(0x3412u, machine.Ebx);
        Assert.AreEqual(0x7856_3412u, Cpu.Run("0F C8", m => m.Eax = 0x1234_5678).Eax, "bswap eax");
    }

    [TestMethod]
    public void StackPushesAndPops()
    {
        // push eax ; pop ebx ; push imm32 ; pop ecx ; push -1 (6A FF) ; pop edx ; push [esi] ; pop [esi+4] ;
        // push ax (66 50) ; pop di (66 5F) ; push esp ; pop ebp
        var machine = Cpu.Machine("50 5B  68 78 56 34 12 59  6A FF 5A  FF 36 8F 46 04  66 50 66 5F  54 5D");
        machine.Eax = 0xAABB_CCDD;
        machine.Esi = Cpu.Data;
        machine.Edi = 0x1111_1111;
        machine.Memory.WriteU32(Cpu.Data, 0x0BAD_F00D);
        var esp = machine.Esp;
        var result = machine.Call(Cpu.Code, [], 1000);
        Assert.IsTrue(result.Completed, result.Fault?.ToString());
        Assert.AreEqual(0xAABB_CCDDu, machine.Ebx);
        Assert.AreEqual(0x1234_5678u, machine.Ecx);
        Assert.AreEqual(0xFFFF_FFFFu, machine.Edx);
        Assert.AreEqual(0x0BAD_F00Du, machine.Memory.ReadU32(Cpu.Data + 4));
        Assert.AreEqual(0x1111_CCDDu, machine.Edi);
        // push esp stores ESP before the push: inside the call ESP was esp - 4 (the return sentinel)
        Assert.AreEqual(esp - 4, machine.Ebp);
        Assert.AreEqual(esp, machine.Esp, "the call restores ESP");
    }

    [TestMethod]
    public void PopIntoEspRelativeMemoryUsesTheIncrementedEsp()
    {
        // push 7 ; push 9 ; pop [esp] : the 9 is stored over the 7 ; pop eax
        var machine = Cpu.Run("6A 07 6A 09 8F 04 24 58");
        Assert.AreEqual(9u, machine.Eax);
    }

    [TestMethod]
    public void PushAllAndPopAll()
    {
        // pushad ; xor every register but esp ; popad
        var machine = Cpu.Machine("60  31 C0 31 C9 31 D2 31 DB 31 ED 31 F6 31 FF  61");
        for (var i = 0; i < 8; i++)
            if (i != 4) machine[(X86Register)i] = 0x1000u * (uint)(i + 1);
        Assert.IsTrue(machine.Call(Cpu.Code, [], 1000).Completed);
        for (var i = 0; i < 8; i++)
            if (i != 4) Assert.AreEqual(0x1000u * (uint)(i + 1), machine[(X86Register)i], ((X86Register)i).ToString());
    }

    [TestMethod]
    public void FlagRegisterMoves()
    {
        // pushfd ; pop eax
        var push = Cpu.Run("9C 58", m => Cpu.SetFlags(m, "C1 Z1 O1 D1"));
        Assert.AreEqual(Cpu.Hex(0x2u | 0x1 | 0x40 | 0x200 | 0x400 | 0x800), Cpu.Hex(push.Eax));
        // push eax ; popfd
        var pop = Cpu.Run("50 9D", m => m.Eax = 0x0000_0895);
        Cpu.Flags(pop, "C1 P1 A1 S1 O1 Z0 D0");
        // lahf ; sahf from AH = 0x41 (ZF, CF)
        var lahf = Cpu.Run("9F", m => { Cpu.SetFlags(m, "S1 Z0 A1 P0 C1"); m.Eax = 0; });
        Assert.AreEqual(0x93u, (lahf.Eax >> 8) & 0xFF);
        var sahf = Cpu.Run("9E", m => { m.Eax = 0x4100; Cpu.SetFlags(m, "O1"); });
        Cpu.Flags(sahf, "Z1 C1 S0 A0 P0 O1");
        Cpu.Flags(Cpu.Run("F5", m => m.Carry = true), "C0");
        Cpu.Flags(Cpu.Run("F9"), "C1");
        Cpu.Flags(Cpu.Run("F8", m => m.Carry = true), "C0");
        Cpu.Flags(Cpu.Run("FD"), "D1");
        Cpu.Flags(Cpu.Run("FC", m => m.Direction = true), "D0");
    }

    [TestMethod]
    public void BitTestFamily()
    {
        // bt eax, ecx ; bts eax, 4 ; btr eax, ecx(= bit 1) ; btc eax, 31
        var reg = Cpu.Run("0F A3 C8  0F BA E8 04  0F B3 C8  0F BA F8 1F", m => { m.Eax = 0x0000_0002; m.Ecx = 0x21; });
        Assert.AreEqual(Cpu.Hex(0x8000_0010), Cpu.Hex(reg.Eax));
        Cpu.Flags(reg, "C0");
        var carry = Cpu.Run("0F A3 C8", m => { m.Eax = 0x0000_0002; m.Ecx = 1; });
        Cpu.Flags(carry, "C1");
        // A register offset reaches past the dword: bit 35 is bit 3 of [eax+4]; -1 is bit 31 of [eax-4].
        var memory = Cpu.Run("0F AB 08  0F BB 10", m =>
        {
            m.Eax = Cpu.Data + 0x10;
            m.Ecx = 35;
            m.Edx = unchecked((uint)-1);
            m.Memory.WriteU32(Cpu.Data + 0x0C, 0x8000_0000);
        });
        Assert.AreEqual(8u, memory.Memory.ReadU32(Cpu.Data + 0x14));
        Assert.AreEqual(0u, memory.Memory.ReadU32(Cpu.Data + 0x0C));
        Cpu.Flags(memory, "C1");
        // An immediate offset is masked to the operand: bt dword [eax], 33 tests bit 1.
        var immediate = Cpu.Run("0F BA 20 21", m => { m.Eax = Cpu.Data; m.Memory.WriteU32(Cpu.Data, 2); });
        Cpu.Flags(immediate, "C1");
    }

    [TestMethod]
    public void BitScans()
    {
        // bsf eax, ebx ; bsr ecx, ebx
        var found = Cpu.Run("0F BC C3  0F BD CB", m => m.Ebx = 0x0001_0100);
        Assert.AreEqual((8u, 16u), (found.Eax, found.Ecx));
        Cpu.Flags(found, "Z0");
        var zero = Cpu.Run("0F BC C3", m => { m.Ebx = 0; m.Eax = 77; });
        Cpu.Flags(zero, "Z1");
        Assert.AreEqual(77u, zero.Eax, "a zero source leaves the destination as it was");
    }

    [TestMethod]
    public void TranslateAndFsSegment()
    {
        // xlat: AL = [EBX + AL]
        var xlat = Cpu.Run("D7", m => { m.Ebx = Cpu.Data; m.Eax = 0x1234_5603; m.Memory.WriteU8(Cpu.Data + 3, 0xEE); });
        Assert.AreEqual(0x1234_56EEu, xlat.Eax);
        // mov eax, fs:[0] ; mov ecx, fs:[0x18] ; mov fs:[0], esp (the SEH link) ; mov edx, fs:[0]
        var fs = Cpu.Run("64 A1 00 00 00 00  64 8B 0D 18 00 00 00  64 89 25 00 00 00 00  64 8B 15 00 00 00 00");
        var options = new MachineOptions();
        Assert.AreEqual(0xFFFF_FFFFu, fs.Eax);
        Assert.AreEqual(options.FsBase, fs.Ecx);
        Assert.AreEqual(fs.Esp - 4, fs.Edx);
        // Segment prefixes other than FS are flat: mov eax, ds:[data] ; mov ecx, cs:[data]
        var flat = Cpu.Run("3E A1 " + Cpu.Le(Cpu.Data) + "  2E 8B 0D " + Cpu.Le(Cpu.Data), m => m.Memory.WriteU32(Cpu.Data, 42));
        Assert.AreEqual((42u, 42u), (flat.Eax, flat.Ecx));
    }

    [TestMethod]
    public void NopsAndLock()
    {
        // nop ; nop dword [eax] (0F 1F 00, no access) ; pause (F3 90) ; lock add [ecx], eax
        var machine = Cpu.Run("90  0F 1F 00  F3 90  F0 01 01", m => { m.Eax = 3; m.Ecx = Cpu.Data; });
        Assert.AreEqual(3u, machine.Memory.ReadU32(Cpu.Data));
    }
}
