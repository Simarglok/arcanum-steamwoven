using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-067: the 16 conditions (jcc rel8 and rel32, setcc, cmovcc), jumps, calls, returns, loops, frames, strings.</summary>
[TestClass]
public sealed class ControlTests
{
    private static readonly string[] Names = ["o", "no", "b", "ae", "e", "ne", "be", "a", "s", "ns", "p", "np", "l", "ge", "le", "g"];

    /// <summary>The condition table of the manual (Jcc), written independently of the interpreter's encoding-order switch.</summary>
    private static bool Expected(string name, bool c, bool z, bool s, bool o, bool p) => name switch
    {
        "o" => o,
        "no" => !o,
        "b" => c,
        "ae" => !c,
        "e" => z,
        "ne" => !z,
        "be" => c || z,
        "a" => !c && !z,
        "s" => s,
        "ns" => !s,
        "p" => p,
        "np" => !p,
        "l" => s != o,
        "ge" => s == o,
        "le" => z || s != o,
        _ => !z && s == o,
    };

    [TestMethod]
    public void EveryConditionOverEveryFlagCombination()
    {
        for (var bits = 0; bits < 32; bits++)
        {
            bool c = (bits & 1) != 0, z = (bits & 2) != 0, s = (bits & 4) != 0, o = (bits & 8) != 0, p = (bits & 16) != 0;
            void Flags(X86Machine m)
            {
                m.Carry = c;
                m.Zero = z;
                m.Sign = s;
                m.Overflow = o;
                m.Parity = p;
            }
            for (var condition = 0; condition < 16; condition++)
            {
                var expected = Expected(Names[condition], c, z, s, o, p);
                var label = $"{Names[condition]} with C{(c ? 1 : 0)} Z{(z ? 1 : 0)} S{(s ? 1 : 0)} O{(o ? 1 : 0)} P{(p ? 1 : 0)}";
                // jcc rel8 over mov eax, 1 ; jcc rel32 over mov ecx, 1 ; setcc dl ; cmovcc esi, ebx
                var machine = Cpu.Run($"{0x70 + condition:X2} 05 B8 01 00 00 00  0F {0x80 + condition:X2} 05 00 00 00 B9 01 00 00 00  0F {0x90 + condition:X2} C2  0F {0x40 + condition:X2} F3", m =>
                {
                    m.Eax = 0;
                    m.Ecx = 0;
                    m.Edx = 0xFFFF_FF77;
                    m.Ebx = 9;
                    m.Esi = 0;
                    Flags(m);
                });
                Assert.AreEqual(expected ? 0u : 1u, machine.Eax, "j" + label);
                Assert.AreEqual(expected ? 0u : 1u, machine.Ecx, "j rel32 " + label);
                Assert.AreEqual(expected ? 0xFFFF_FF01u : 0xFFFF_FF00u, machine.Edx, "set" + label);
                Assert.AreEqual(expected ? 9u : 0u, machine.Esi, "cmov" + label);
            }
        }
    }

    [TestMethod]
    public void JumpsAndLoops()
    {
        // mov ecx, 5 ; xor eax, eax ; L: inc eax ; dec ecx ; jnz L (rel8 back)
        Assert.AreEqual(5u, Cpu.Run("B9 05 00 00 00  31 C0  40  49  75 FC").Eax);
        // jmp rel32 over mov eax, 1 ; jmp rel8 over mov eax, 2
        Assert.AreEqual(0u, Cpu.Run("31 C0  E9 05 00 00 00  B8 01 00 00 00  EB 05  B8 02 00 00 00").Eax);
        // mov ecx, 3 ; xor eax, eax ; L: inc eax ; loop L
        Assert.AreEqual(3u, Cpu.Run("B9 03 00 00 00  31 C0  40  E2 FD").Eax);
        // loope stops when ZF clears: ecx = 5 ; L: inc eax ; cmp eax, 2 ; loope... (ZF set only at eax == 2)
        var loope = Cpu.Run("B9 05 00 00 00  31 C0  40  83 F8 02  E1 FA", m => m.Eax = 0);
        Assert.AreEqual((1u, 4u), (loope.Eax, loope.Ecx));
        // loopne stops when ZF sets
        var loopne = Cpu.Run("B9 05 00 00 00  31 C0  40  83 F8 02  E0 FA");
        Assert.AreEqual((2u, 3u), (loopne.Eax, loopne.Ecx));
        // jecxz taken when ECX is 0
        Assert.AreEqual(0u, Cpu.Run("31 C0  E3 05  B8 01 00 00 00", m => m.Ecx = 0).Eax);
        Assert.AreEqual(1u, Cpu.Run("31 C0  E3 05  B8 01 00 00 00", m => m.Ecx = 1).Eax);
    }

    [TestMethod]
    public void CallsAndReturns()
    {
        // call +4 (to F) ; add eax, 1 ; ret ; F: mov eax, 10 ; ret
        var machine = Cpu.Machine("E8 04 00 00 00  83 C0 01  C3  B8 0A 00 00 00  C3");
        machine.Census = new MachineCensus();
        var result = machine.Call(Cpu.Code, [], 1000);
        Assert.IsTrue(result.Completed, result.Fault?.ToString());
        Assert.AreEqual(11u, result.Eax);
        Assert.AreEqual(0u, result.ArgumentBytesPopped);
        CollectionAssert.AreEquivalent(new[] { Cpu.Code, Cpu.Code + 9 }, machine.Census.Functions.Keys.ToArray());
        Assert.AreEqual(5L, result.Steps);
    }

    [TestMethod]
    public void StdcallReturnPopsItsArguments()
    {
        // mov eax, [esp+4] ; add eax, [esp+8] ; ret 8
        var machine = Cpu.Machine("8B 44 24 04  03 44 24 08  C2 08 00");
        var esp = machine.Esp;
        var result = machine.Call(Cpu.Code, [40, 2], 1000);
        Assert.AreEqual(42u, result.Eax);
        Assert.AreEqual(8u, result.ArgumentBytesPopped);
        Assert.AreEqual(esp, machine.Esp);
    }

    [TestMethod]
    public void IndirectCallsAndJumpTables()
    {
        var target = Cpu.Code + 0x100;
        var table = Cpu.Data + 0x40;
        // call eax ; call [table] ; mov ecx, 1 ; jmp [ecx*4+table] ; (0x100) F: inc edx ; ret ; (0x200) G: mov esi, 7 ; ret
        var machine = Cpu.Machine("FF D0  FF 15 " + Cpu.Le(table) + "  B9 01 00 00 00  FF 24 8D " + Cpu.Le(table));
        machine.Memory.Load(target, Cpu.Bytes("42 C3"));
        machine.Memory.Load(Cpu.Code + 0x200, Cpu.Bytes("BE 07 00 00 00 C3"));
        machine.Memory.WriteU32(table, target);
        machine.Memory.WriteU32(table + 4, Cpu.Code + 0x200);
        machine.Eax = target;
        machine.Edx = 0;
        var result = machine.Call(Cpu.Code, [], 1000);
        Assert.IsTrue(result.Completed, result.Fault?.ToString());
        Assert.AreEqual(2u, machine.Edx);
        Assert.AreEqual(7u, machine.Esi);
    }

    [TestMethod]
    public void FramesWithEnterAndLeave()
    {
        // push ebp ; mov ebp, esp ; sub esp, 16 ; mov [ebp-4], 5 ; mov eax, [ebp-4] ; leave ; enter 8, 0 ; add eax, [ebp+8] ; leave
        var machine = Cpu.Machine("55  8B EC  83 EC 10  C7 45 FC 05 00 00 00  8B 45 FC  C9  C8 08 00 00  03 45 08  C9");
        machine.Ebp = 0x1234;
        var result = machine.Call(Cpu.Code, [37], 1000);
        Assert.IsTrue(result.Completed, result.Fault?.ToString());
        Assert.AreEqual(42u, result.Eax);
        Assert.AreEqual(0x1234u, machine.Ebp);
    }

    [TestMethod]
    public void StringMovesAndStores()
    {
        var source = Cpu.Data;
        var destination = Cpu.Data + 0x100;
        // rep movsb ; then rep stosd of 2 dwords of EAX
        var machine = Cpu.Run("F3 A4  B9 02 00 00 00  F3 AB", m =>
        {
            m.Memory.WriteBytes(source, "hello"u8);
            m.Esi = source;
            m.Edi = destination;
            m.Ecx = 5;
            m.Eax = 0x0101_0101;
        });
        var copy = new byte[13];
        machine.Memory.ReadBytes(destination, copy);
        CollectionAssert.AreEqual("hello"u8.ToArray().Concat(Enumerable.Repeat((byte)1, 8)).ToArray(), copy);
        Assert.AreEqual((source + 5, destination + 13, 0u), (machine.Esi, machine.Edi, machine.Ecx));

        // std ; rep movsd backwards (two dwords ending at the given addresses) ; cld
        var backwards = Cpu.Run("FD  F3 A5  FC", m =>
        {
            m.Memory.WriteU32(source + 4, 0xAAAA_AAAA);
            m.Memory.WriteU32(source + 8, 0xBBBB_BBBB);
            m.Esi = source + 8;
            m.Edi = destination + 8;
            m.Ecx = 2;
        });
        Assert.AreEqual(0xAAAA_AAAAu, backwards.Memory.ReadU32(destination + 4));
        Assert.AreEqual(0xBBBB_BBBBu, backwards.Memory.ReadU32(destination + 8));
        Assert.AreEqual((source, destination), (backwards.Esi, backwards.Edi));

        // lodsb ; lodsd ; movsw (66 A5) ; stosb ; rep stosw (66 F3 AB) with ECX = 2
        var single = Cpu.Run("AC  AD  66 A5  AA  B9 02 00 00 00  66 F3 AB", m =>
        {
            m.Memory.WriteBytes(source, Cpu.Bytes("11 22 33 44 55 66 77"));
            m.Esi = source;
            m.Edi = destination;
        });
        Assert.AreEqual(0x5544_3322u, single.Eax);
        var written = new byte[7];
        single.Memory.ReadBytes(destination, written);
        CollectionAssert.AreEqual(Cpu.Bytes("66 77 22 22 33 22 33"), written);
    }

    [TestMethod]
    public void StringScansAndCompares()
    {
        var text = Cpu.Data;
        // strlen idiom: xor eax, eax ; or ecx, -1 ; repne scasb ; not ecx ; dec ecx
        var length = Cpu.Run("31 C0  83 C9 FF  F2 AE  F7 D1  49", m =>
        {
            m.Memory.WriteBytes(text, "arcanum\0"u8);
            m.Edi = text;
        });
        Assert.AreEqual(7u, length.Ecx);
        Cpu.Flags(length, "Z0");
        // repe cmpsb stops at the first difference (index 2) with ZF clear
        var compare = Cpu.Run("F3 A6", m =>
        {
            m.Memory.WriteBytes(text, "abcd"u8);
            m.Memory.WriteBytes(text + 0x10, "abXd"u8);
            m.Esi = text;
            m.Edi = text + 0x10;
            m.Ecx = 4;
        });
        Assert.AreEqual((1u, text + 3), (compare.Ecx, compare.Esi));
        Cpu.Flags(compare, "Z0 C0");
        // cmpsd once, scasd once
        var once = Cpu.Run("A7  AF", m =>
        {
            m.Memory.WriteU32(text, 5);
            m.Memory.WriteU32(text + 0x10, 5);
            m.Memory.WriteU32(text + 0x14, 6);
            m.Esi = text;
            m.Edi = text + 0x10;
            m.Eax = 7;
        });
        Cpu.Flags(once, "Z0 C0");
        Assert.AreEqual(text + 0x18, once.Edi);
        // A repeat with ECX = 0 does nothing, not even the flags.
        var none = Cpu.Run("F3 A6", m => { m.Ecx = 0; m.Esi = 0; m.Edi = 0; m.Zero = true; m.Carry = true; });
        Cpu.Flags(none, "Z1 C1");
    }

    [TestMethod]
    public void CensusListsFunctionsKindsAndAddresses()
    {
        // push 3 ; call F ; add esp, 4 ; rep stosb (ecx = 0) ; ret ; F: mov eax, [esp+4] ; shl eax, 1 ; ret
        var machine = Cpu.Machine("6A 03  E8 06 00 00 00  83 C4 04  F3 AA  C3  8B 44 24 04  D1 E0  C3");
        machine.Census = new MachineCensus();
        machine.Ecx = 0;
        var result = machine.Call(Cpu.Code, [], 1000);
        Assert.AreEqual(6u, result.Eax);
        var census = machine.Census;
        Assert.AreEqual(8L, census.Instructions);
        Assert.AreEqual(8, census.DistinctInstructionAddresses);
        Assert.IsTrue(census.Executed(Cpu.Code + 0x0D));
        Assert.AreEqual(1L, census.Functions[Cpu.Code + 0x0D]);
        CollectionAssert.AreEquivalent(new[] { "push", "call", "add", "rep stos", "ret", "mov", "shl" }, census.InstructionKinds.Keys.ToArray());
        Assert.AreEqual(2L, census.InstructionKinds["ret"]);
        StringAssert.Contains(census.Report(), $"function 0x{Cpu.Code + 0x0D:X8} 1");
        StringAssert.Contains(census.Report(), "kind rep stos 1");
    }
}
