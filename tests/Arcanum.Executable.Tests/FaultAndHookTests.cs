using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Executable.Tests;

/// <summary>ARC-DEV-067: every stop is a typed fault with its address; service hooks answer calls and jumps.</summary>
[TestClass]
public sealed class FaultAndHookTests
{
    [TestMethod]
    [DataRow("D9 E8", "floating point", DisplayName = "fld1")]
    [DataRow("9B", "floating point", DisplayName = "fwait")]
    [DataRow("0F A2", "0F A2", DisplayName = "cpuid")]
    [DataRow("0F 31", "0F 31", DisplayName = "rdtsc")]
    [DataRow("0F B1 08", "0F B1", DisplayName = "cmpxchg")]
    [DataRow("67 8B 00", "opcode 67", DisplayName = "address-size prefix")]
    [DataRow("65 8B 00", "opcode 65", DisplayName = "GS override")]
    [DataRow("8E D8", "opcode 8E", DisplayName = "mov to a segment register")]
    [DataRow("37", "opcode 37", DisplayName = "aaa")]
    [DataRow("CD 21", "opcode CD", DisplayName = "int")]
    [DataRow("F4", "opcode F4", DisplayName = "hlt")]
    [DataRow("C8 08 00 01", "nesting level", DisplayName = "enter with nesting")]
    [DataRow("8D C0", "lea of a register", DisplayName = "lea register form")]
    [DataRow("FF D8", "FF /3", DisplayName = "far call")]
    [DataRow("66 E8 00 00", "16-bit call", DisplayName = "16-bit call")]
    public void UnsupportedInstructionsStopWithTheirBytes(string code, string detail)
    {
        // a nop first, so the fault names the second instruction's address
        var fault = Cpu.Fault("90 " + code);
        Assert.AreEqual(MachineFaultKind.UnsupportedInstruction, fault.Kind);
        Assert.AreEqual(Cpu.Code + 1, fault.InstructionAddress);
        StringAssert.Contains(fault.Detail, detail);
        StringAssert.Contains(fault.Detail, "bytes " + Cpu.Bytes(code)[0].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void MemoryFaultsNameTheDataAddress()
    {
        var read = Cpu.Fault("8B 05 00 00 00 10");
        Assert.AreEqual((MachineFaultKind.MemoryRead, Cpu.Code, (uint?)0x1000_0000u), (read.Kind, read.InstructionAddress, read.DataAddress));
        // a write to the read-only code
        var write = Cpu.Fault("89 05 " + Cpu.Le(Cpu.Code));
        Assert.AreEqual((MachineFaultKind.MemoryWrite, (uint?)Cpu.Code), (write.Kind, write.DataAddress));
        // a dword that straddles the end of the data region
        var straddle = Cpu.Fault("C7 05 " + Cpu.Le(Cpu.Data + Cpu.DataSize - 2) + " 01 00 00 00");
        Assert.AreEqual(MachineFaultKind.MemoryWrite, straddle.Kind);
        // the stack runs out
        var stack = Cpu.Fault("50 EB FD", steps: 10_000_000);
        Assert.AreEqual(MachineFaultKind.MemoryWrite, stack.Kind);
    }

    [TestMethod]
    public void ControlTransfersMustReachCode()
    {
        var data = Cpu.Fault("FF D0", m => m.Eax = Cpu.Data);
        Assert.AreEqual((MachineFaultKind.ControlOutsideCode, Cpu.Code), (data.Kind, data.InstructionAddress));
        StringAssert.Contains(data.Detail, Cpu.Hex(Cpu.Data));
        Assert.AreEqual(MachineFaultKind.ControlOutsideCode, Cpu.Fault("E9 00 00 00 10").Kind);
        // a return to a garbage address
        Assert.AreEqual(MachineFaultKind.ControlOutsideCode, Cpu.Fault("68 00 00 00 30 C3").Kind);
        // falling off the end of the code region
        var machine = new X86Machine();
        machine.Memory.Map(Cpu.Code, 0x1000, MemoryAccess.ReadExecute, "code");
        machine.Memory.Load(Cpu.Code + 0xFFF, [0x90]);
        var fallOff = machine.Call(Cpu.Code + 0xFFF, [], 100);
        Assert.AreEqual((MachineFaultKind.ExecuteOutsideCode, Cpu.Code + 0x1000), (fallOff.Fault!.Kind, fallOff.Fault.InstructionAddress));
    }

    [TestMethod]
    public void BreakpointAndStepLimit()
    {
        Assert.AreEqual(MachineFaultKind.Breakpoint, Cpu.Fault("CC").Kind);
        var loop = Cpu.Fault("EB FE", steps: 1000);
        Assert.AreEqual((MachineFaultKind.InstructionLimit, Cpu.Code), (loop.Kind, loop.InstructionAddress));
        // a repeated string counts each iteration
        var rep = Cpu.Fault("F3 AA", m => { m.Ecx = 5000; m.Edi = Cpu.Data; }, steps: 100);
        Assert.AreEqual(MachineFaultKind.InstructionLimit, rep.Kind);
    }

    [TestMethod]
    public void AFaultRestoresTheStackPointerAndTheMachineRunsAgain()
    {
        var machine = Cpu.Machine("50 50 50 CC");
        var esp = machine.Esp;
        Assert.AreEqual(MachineFaultKind.Breakpoint, machine.Call(Cpu.Code, [1, 2], 100).Fault!.Kind);
        Assert.AreEqual(esp, machine.Esp);
        machine.Memory.Load(Cpu.Code, Cpu.Bytes("B8 05 00 00 00 C3"));
        Assert.AreEqual(5u, machine.Call(Cpu.Code, [], 100).Eax);
    }

    [TestMethod]
    public void HooksAnswerCallsWithEitherConvention()
    {
        var service = 0x0060_0000u; // not mapped: only the hook answers there
        var stdcall = 0x0060_0010u;
        // push 21 ; call service ; add esp, 4 ; mov ebx, eax ; push 2 ; push 3 ; call stdcall ; add eax, ebx
        var machine = Cpu.Machine($"6A 15  E8 {Cpu.Le(service - (Cpu.Code + 7))}  83 C4 04  89 C3  6A 02  6A 03  E8 {Cpu.Le(stdcall - (Cpu.Code + 21))}  01 D8");
        machine.Hook(service, "double", m =>
        {
            m.Eax = m.Argument(0) * 2;
            return HookResult.Return();
        });
        machine.Hook(stdcall, "multiply", m =>
        {
            m.Eax = m.Argument(0) * m.Argument(1);
            return HookResult.Return(8);
        });
        machine.Census = new MachineCensus();
        var result = machine.Call(Cpu.Code, [], 1000);
        Assert.IsTrue(result.Completed, result.Fault?.ToString());
        Assert.AreEqual(48u, result.Eax);
        Assert.AreEqual(1L, machine.Census.Hooks["double"]);
        Assert.AreEqual(1L, machine.Census.Hooks["multiply"]);
        Assert.IsTrue(machine.Census.Functions.ContainsKey(service));
    }

    [TestMethod]
    public void HooksAnswerTailJumpsDirectCallsAndCanObserveOrStop()
    {
        var service = 0x0060_0000u;
        // jmp service (a tail call: the hook returns to our caller)
        var tail = Cpu.Machine($"E9 {Cpu.Le(service - (Cpu.Code + 5))}");
        tail.Hook(service, "seven", m => { m.Eax = 7; return HookResult.Return(); });
        Assert.AreEqual(7u, tail.Call(Cpu.Code, [], 100).Eax);
        // Call() straight at a hook
        Assert.AreEqual(7u, tail.Call(service, [], 100).Eax);

        // An observing hook on real code continues into it.
        var observed = 0;
        var observe = Cpu.Machine("E8 01 00 00 00 C3");
        observe.Memory.Load(Cpu.Code + 6, Cpu.Bytes("B8 2A 00 00 00 C3"));
        observe.Hook(Cpu.Code + 6, "observer", _ => { observed++; return HookResult.Continue(); });
        var result = observe.Call(Cpu.Code, [], 100);
        Assert.AreEqual((42u, 1), (result.Eax, observed));

        // A continuing hook where there is no code stops.
        var nowhere = Cpu.Machine($"E8 {Cpu.Le(service - (Cpu.Code + 5))}");
        nowhere.Hook(service, "observer", _ => HookResult.Continue());
        Assert.AreEqual(MachineFaultKind.ControlOutsideCode, nowhere.Call(Cpu.Code, [], 100).Fault!.Kind);

        // A refusing hook stops the call with its reason and address.
        var stop = Cpu.Machine($"E8 {Cpu.Le(service - (Cpu.Code + 5))}");
        stop.Hook(service, "file open", _ => HookResult.Stop("no such file"));
        var fault = stop.Call(Cpu.Code, [], 100).Fault!;
        Assert.AreEqual((MachineFaultKind.HookStopped, service), (fault.Kind, fault.InstructionAddress));
        StringAssert.Contains(fault.Detail, "file open: no such file");

        // A hook's own memory fault stops the call at the hook.
        var bad = Cpu.Machine($"E8 {Cpu.Le(service - (Cpu.Code + 5))}");
        bad.Hook(service, "reader", m => { m.Eax = m.Memory.ReadU32(0x7000_0000); return HookResult.Return(); });
        var memory = bad.Call(Cpu.Code, [], 100).Fault!;
        Assert.AreEqual((MachineFaultKind.MemoryRead, service, (uint?)0x7000_0000u), (memory.Kind, memory.InstructionAddress, memory.DataAddress));
    }

    [TestMethod]
    public void MachineRejectsMisuse()
    {
        var machine = Cpu.Machine("");
        Assert.ThrowsExactly<ArgumentException>(() => machine.Hook(machine.Options.ReturnSentinel, "x", _ => HookResult.Return()));
        Assert.ThrowsExactly<ArgumentException>(() => machine.Memory.Map(Cpu.Code, 0x1000, MemoryAccess.Read, "again"));
        Assert.ThrowsExactly<ArgumentException>(() => machine.Memory.Map(0x0070_0001, 0x1000, MemoryAccess.Read, "unaligned"));
        Assert.ThrowsExactly<ArgumentException>(() => machine.Memory.Map(0xFFFF_F000, 0x2000, MemoryAccess.Read, "wraps"));
        // a hook may not call the machine again
        var service = 0x0060_0000u;
        var nested = Cpu.Machine($"E8 {Cpu.Le(service - (Cpu.Code + 5))}");
        nested.Hook(service, "nested", m => { m.Call(Cpu.Code, [], 10); return HookResult.Return(); });
        Assert.ThrowsExactly<InvalidOperationException>(() => nested.Call(Cpu.Code, [], 100));
    }

    [TestMethod]
    public void MemoryHelpers()
    {
        var memory = new MachineMemory();
        memory.Map(0x1000, 0x2000, MemoryAccess.ReadWrite, "data");
        memory.Map(0x3000, 0x1000, MemoryAccess.Read, "rodata");
        memory.WriteU32(0x1FFE, 0x1122_3344); // straddles two pages
        Assert.AreEqual(0x1122_3344u, memory.ReadU32(0x1FFE));
        Assert.AreEqual(0x3344, memory.ReadU16(0x1FFE));
        memory.WriteU16(0x1FFF, 0xBEEF);
        Assert.AreEqual(0xBEEF, memory.ReadU16(0x1FFF));
        memory.WriteBytes(0x1100, "name\0rest"u8);
        CollectionAssert.AreEqual("name"u8.ToArray(), memory.ReadCString(0x1100, 16));
        Assert.ThrowsExactly<MachineFaultException>(() => memory.ReadCString(0x1105, 2));
        // the host load ignores the access; the guest write does not, and a failed straddling write changes nothing
        memory.Load(0x3000, [1, 2, 3, 4]);
        Assert.AreEqual(0x0403_0201u, memory.ReadU32(0x3000));
        Assert.ThrowsExactly<MachineFaultException>(() => memory.WriteU32(0x2FFE, 0xFFFF_FFFF));
        Assert.AreEqual(0u, memory.ReadU16(0x2FFE));
        Assert.IsFalse(memory.CanExecute(0x1000));
        Assert.AreEqual(2, memory.Regions.Count);
    }
}
