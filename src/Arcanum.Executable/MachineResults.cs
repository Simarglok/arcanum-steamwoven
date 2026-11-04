namespace Arcanum.Executable;

/// <summary>Why <see cref="X86Machine"/> stopped before the call returned.</summary>
public enum MachineFaultKind
{
    /// <summary>An opcode outside the supported integer subset (floating point, system and segment instructions included).</summary>
    UnsupportedInstruction,
    /// <summary>A read of an unmapped or unreadable byte.</summary>
    MemoryRead,
    /// <summary>A write to an unmapped or read-only byte.</summary>
    MemoryWrite,
    /// <summary>An instruction fetch from memory that is not executable.</summary>
    ExecuteOutsideCode,
    /// <summary>A call, jump or return to an address that is neither executable code, a hook nor the return sentinel.</summary>
    ControlOutsideCode,
    /// <summary>A divide by zero or a quotient that does not fit (the CPU's #DE).</summary>
    DivideError,
    /// <summary><c>int3</c>.</summary>
    Breakpoint,
    /// <summary>The call ran more steps than its limit.</summary>
    InstructionLimit,
    /// <summary>A service hook refused the call (<see cref="HookResult.Stop"/>).</summary>
    HookStopped,
    /// <summary>A call through an import slot that no hook answers.</summary>
    UnhookedImport,
}

/// <summary>A typed stop of the interpreter: the instruction it happened at, what and, for memory, the data address.</summary>
public sealed record MachineFault(MachineFaultKind Kind, uint InstructionAddress, string Detail, uint? DataAddress = null)
{
    public override string ToString() => $"{Kind} at 0x{InstructionAddress:X8}: {Detail}";
}

/// <summary>Carries a <see cref="MachineFault"/> out of memory access and decoding; <see cref="X86Machine.Call"/> turns it into a result.</summary>
public sealed class MachineFaultException(MachineFault fault) : Exception(fault.ToString())
{
    public MachineFault Fault { get; } = fault;
}

/// <summary>The outcome of <see cref="X86Machine.Call"/>: the return registers, or the fault.</summary>
public sealed record MachineRunResult(MachineFault? Fault, uint Eax, uint Edx, long Steps, uint ArgumentBytesPopped)
{
    public bool Completed => Fault is null;
}

/// <summary>What a service hook did; the machine applies it.</summary>
public readonly record struct HookResult
{
    internal enum Action { Return, Continue, Stop }

    internal Action Kind { get; private init; }
    internal uint ArgumentBytes { get; private init; }
    internal string? Reason { get; private init; }

    /// <summary>Return to the caller like <c>ret</c> (cdecl) or <c>ret n</c> (stdcall, <paramref name="argumentBytes"/> = n); EAX and EDX are what the hook set.</summary>
    public static HookResult Return(uint argumentBytes = 0) => new() { Kind = Action.Return, ArgumentBytes = argumentBytes };

    /// <summary>Run the original code at the hooked address (an observing hook); only valid where that code is executable.</summary>
    public static HookResult Continue() => new() { Kind = Action.Continue };

    /// <summary>Stop the call with <see cref="MachineFaultKind.HookStopped"/>.</summary>
    public static HookResult Stop(string reason) => new() { Kind = Action.Stop, Reason = reason };
}

/// <summary>A service hook: runs instead of the code at its address when a call or jump reaches it; [ESP] is the return address.</summary>
public delegate HookResult MachineHook(X86Machine machine);

/// <summary>Explicit limits and layout of one <see cref="X86Machine"/>.</summary>
public sealed record MachineOptions
{
    /// <summary>Lowest address of the stack region (page aligned). The default sits below the usual image base 0x400000.</summary>
    public uint StackBase { get; init; } = 0x0010_0000;
    public uint StackSize { get; init; } = 0x0020_0000;
    /// <summary>Base of the FS segment; a zeroed 4 KiB thread block is mapped there (fs:[0] = 0xFFFFFFFF, fs:[0x18] = itself). 0 maps none and leaves FS flat.</summary>
    public uint FsBase { get; init; } = 0x7FFD_F000;
    /// <summary>The return address <see cref="X86Machine.Call"/> pushes; reaching it ends the call. Must stay unmapped.</summary>
    public uint ReturnSentinel { get; init; } = 0xFFFF_FFF0;
    /// <summary>Base of the unmapped range the import slots point into (one 16-byte slot per import).</summary>
    public uint ImportThunkBase { get; init; } = 0xFF00_0000;
    /// <summary>Default step limit of one call (one step per instruction and per hook reached, plus one per repeated string iteration).</summary>
    public long MaxStepsPerCall { get; init; } = 4_000_000_000;
}
