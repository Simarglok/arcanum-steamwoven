using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-067: the forms the random cross-check did not reach: FS memory and string forms, ESP-relative operands,
/// 16-bit multiply and divide, rotate-through-carry counts, memory forms of shifts, bits, setcc and cmovcc, and the
/// call boundary (DF, the flags register in user mode, hooks reached by a return, steps).</summary>
[TestClass]
public sealed class EdgeCaseTests
{
    [TestMethod]
    public void ExceptionChainLinkAndUnlinkThroughFs()
    {
        // push fs:[0] ; mov fs:[0], esp ; mov ecx, fs:[0] ; pop fs:[0] ; mov edx, fs:[0]
        var machine = Cpu.Run("64 FF 35 00 00 00 00  64 89 25 00 00 00 00  64 8B 0D 00 00 00 00  64 8F 05 00 00 00 00  64 8B 15 00 00 00 00");
        Assert.AreEqual(machine.Esp - 8, machine.Ecx, "the link pointed at the pushed record");
        Assert.AreEqual(0xFFFF_FFFFu, machine.Edx, "the unlink restored the end of the chain");
    }

    [TestMethod]
    public void StringSourceTakesTheOverrideAndTheDestinationStaysFlat()
    {
        // movsb fs:[esi] -> es:[edi]
        var machine = Cpu.Run("64 A4", m => { m.Esi = 1; m.Edi = Cpu.Data; });
        Assert.AreEqual(0xFFu, machine.Memory.ReadU8(Cpu.Data));
        Assert.AreEqual((2u, Cpu.Data + 1), (machine.Esi, machine.Edi));
        // repne movsb repeats like rep
        var repne = Cpu.Run("F2 A4", m => { m.Memory.WriteBytes(Cpu.Data, "abc"u8); m.Esi = Cpu.Data; m.Edi = Cpu.Data + 0x10; m.Ecx = 3; });
        Assert.AreEqual(0x63_6261u, repne.Memory.ReadU32(Cpu.Data + 0x10));
    }

    [TestMethod]
    public void EspRelativeOperandsUseEspBeforeThePush()
    {
        // push [esp+4] ; pop eax
        var push = Cpu.Machine("FF 74 24 04  58");
        Assert.AreEqual(0x77u, push.Call(Cpu.Code, [0x77], 100).Eax);
        // call [esp+4]: the argument is the function
        var call = Cpu.Machine("FF 54 24 04");
        call.Memory.Load(Cpu.Code + 0x100, Cpu.Bytes("B8 05 00 00 00 C3"));
        Assert.AreEqual(5u, call.Call(Cpu.Code, [Cpu.Code + 0x100], 100).Eax);
        // lea ebx, [esp-4] ; push ebx ; pop esp (ESP becomes the popped value) ; mov eax, esp ; add esp, 4
        var popEsp = Cpu.Machine("8D 5C 24 FC  53  5C  89 E0  83 C4 04");
        var esp = popEsp.Esp;
        Assert.IsTrue(popEsp.Call(Cpu.Code, [], 100).Completed);
        Assert.AreEqual(esp - 8, popEsp.Eax);
    }

    [TestMethod]
    public void SixteenBitMultiplyAndDivide()
    {
        // imul bx: DX:AX = AX × BX signed
        var imul = Cpu.Run("66 F7 EB", m => { m.Eax = 0x1111_FFFE; m.Ebx = 3; m.Edx = 0x2222_0000; });
        Assert.AreEqual((0x1111_FFFAu, 0x2222_FFFFu), (imul.Eax, imul.Edx));
        Cpu.Flags(imul, "C0 O0");
        var wide = Cpu.Run("66 F7 EB", m => { m.Eax = 0x4000; m.Ebx = 4; });
        Assert.AreEqual((0u, 1u), (wide.Eax & 0xFFFF, wide.Edx & 0xFFFF));
        Cpu.Flags(wide, "C1 O1");
        // idiv bx: -7 / 2
        var idiv = Cpu.Run("66 F7 FB", m => { m.Edx = 0xFFFF; m.Eax = 0xFFF9; m.Ebx = 2; });
        Assert.AreEqual((0xFFFDu, 0xFFFFu), (idiv.Eax, idiv.Edx));
        Assert.AreEqual(MachineFaultKind.DivideError, Cpu.Fault("66 F7 FB", m => { m.Edx = 0xFFFF; m.Eax = 0x8000; m.Ebx = 0xFFFF; }).Kind);
        // imul ax, cx, -1 (66 6B): sign-extended byte immediate, upper EAX kept
        var three = Cpu.Run("66 6B C1 FF", m => { m.Eax = 0xABCD_0000; m.Ecx = 5; });
        Assert.AreEqual(0xABCD_FFFBu, three.Eax);
        Cpu.Flags(three, "C0 O0");
    }

    [TestMethod]
    public void RotateThroughCarryCountsWrapOverTheWidthPlusOne()
    {
        // rcl ax, cl: 17 is a full turn of the 17-bit value; 16 equals one rotate right
        var full = Cpu.Run("66 D3 D0", m => { m.Eax = 0x1234; m.Ecx = 17; m.Carry = true; });
        Assert.AreEqual(0x1234u, full.Eax);
        Cpu.Flags(full, "C1");
        var sixteen = Cpu.Run("66 D3 D0", m => { m.Eax = 0x0001; m.Ecx = 16; m.Carry = false; });
        Assert.AreEqual(0u, sixteen.Eax);
        Cpu.Flags(sixteen, "C1");
        // rcr al, cl: 9 is a full turn, 10 one step
        var nine = Cpu.Run("D2 D8", m => { m.Eax = 0x55; m.Ecx = 9; m.Carry = false; });
        Assert.AreEqual(0x55u, nine.Eax);
        Cpu.Flags(nine, "C0");
        var ten = Cpu.Run("D2 D8", m => { m.Eax = 0x03; m.Ecx = 10; m.Carry = false; });
        Assert.AreEqual(0x01u, ten.Eax);
        Cpu.Flags(ten, "C1");
    }

    [TestMethod]
    public void MemoryFormsOfShiftsBitsSetccAndCmovcc()
    {
        // shl dword [eax], 1 ; shld [eax+4], ebx, 4 ; sete byte [eax+8] (ZF from the shl: clear) ; bt word [eax+12], cx (17: bit 1 of [eax+14])
        var machine = Cpu.Run("D1 20  0F A4 58 04 04  0F 94 40 08  66 0F A3 48 0C", m =>
        {
            m.Eax = Cpu.Data;
            m.Ebx = 0x9ABC_DEF0;
            m.Ecx = 17;
            m.Memory.WriteU32(Cpu.Data, 0x8000_0001);
            m.Memory.WriteU32(Cpu.Data + 4, 0x1234_5678);
            m.Memory.WriteU8(Cpu.Data + 8, 0x55);
            m.Memory.WriteU16(Cpu.Data + 14, 0x0002);
        });
        Assert.AreEqual(2u, machine.Memory.ReadU32(Cpu.Data));
        Assert.AreEqual(0x2345_6789u, machine.Memory.ReadU32(Cpu.Data + 4));
        Assert.AreEqual(0u, machine.Memory.ReadU8(Cpu.Data + 8));
        Cpu.Flags(machine, "C1");
        // A 16-bit register offset of -1 is bit 15 of the word below.
        var below = Cpu.Run("66 0F A3 08", m => { m.Eax = Cpu.Data + 0x10; m.Ecx = 0xFFFF; m.Memory.WriteU16(Cpu.Data + 0x0E, 0x8000); });
        Cpu.Flags(below, "C1");
        // cmove ebx, [eax] with ZF set; a false cmovcc still reads its source; a shift by 0 still reads memory
        Assert.AreEqual(9u, Cpu.Run("0F 44 18", m => { m.Eax = Cpu.Data; m.Memory.WriteU32(Cpu.Data, 9); m.Zero = true; }).Ebx);
        Assert.AreEqual(MachineFaultKind.MemoryRead, Cpu.Fault("0F 45 1D 00 00 00 10", m => m.Zero = true).Kind);
        Assert.AreEqual(MachineFaultKind.MemoryRead, Cpu.Fault("D3 25 00 00 00 10", m => m.Ecx = 0).Kind);
    }

    [TestMethod]
    public void TheCallClearsDirectionAndTheFlagsRegisterBehavesAsInUserMode()
    {
        // pushfd ; pop eax, with DF left set by the host: the call starts with DF clear
        var machine = Cpu.Machine("9C 58");
        machine.Direction = true;
        Assert.AreEqual(0u, machine.Call(Cpu.Code, [], 100).Eax & 0x400);
        // the CPUID-detection idiom: the ID bit toggles, so the code would go on to cpuid
        var id = Cpu.Run("9C 58 89 C1 35 00 00 20 00 50 9D 9C 58 31 C8");
        Assert.AreEqual(0x0020_0000u, id.Eax);
        Assert.AreEqual(MachineFaultKind.UnsupportedInstruction, Cpu.Fault("0F A2").Kind);
        // popfd cannot clear IF; the system bits read 0
        Assert.AreEqual(0x202u, Cpu.Run("6A 00 9D 9C 58").Eax);
        Assert.AreEqual(0x0004_0200u, Cpu.Run("68 FF 30 04 00 9D 9C 58").Eax & 0x0004_3200, "AC kept, IOPL dropped, IF set");
    }

    [TestMethod]
    public void HooksReachedByAReturnAndTheirSteps()
    {
        var service = 0x0060_0000u;
        // push service ; ret (into the hook, which returns to the sentinel)
        var machine = Cpu.Machine("68 " + Cpu.Le(service) + " C3");
        machine.Hook(service, "nine", m => { m.Eax = 9; return HookResult.Return(); });
        var result = machine.Call(Cpu.Code, [], 100);
        Assert.AreEqual((9u, 3L), (result.Eax, result.Steps), "push, ret and the hook are one step each");
        Assert.AreEqual(1L, machine.Call(service, [], 100).Steps);
        // jumping to the sentinel without returning to it is refused, and so is a call of the sentinel
        var jump = Cpu.Machine("E9 " + Cpu.Le(machine.Options.ReturnSentinel - (Cpu.Code + 5)));
        Assert.AreEqual(MachineFaultKind.ControlOutsideCode, jump.Call(Cpu.Code, [], 100).Fault!.Kind);
        Assert.ThrowsExactly<ArgumentException>(() => machine.Call(machine.Options.ReturnSentinel, [], 100));
    }
}
