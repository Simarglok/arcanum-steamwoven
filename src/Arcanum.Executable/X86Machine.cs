namespace Arcanum.Executable;

/// <summary>The eight general registers in their encoding order.</summary>
public enum X86Register
{
    Eax,
    Ecx,
    Edx,
    Ebx,
    Esp,
    Ebp,
    Esi,
    Edi,
}

/// <summary>
/// ARC-DEV-067: an interpreter of the 32-bit integer subset of x86 that the original's prototype build reaches
/// (<c>docs/research/prototype-extraction.md</c>; the subset, the flag choices and the limits are in <c>docs/project/components/Arcanum.Executable.md</c>).
/// Flat memory (<see cref="MachineMemory"/>) with one optional FS base, no floating point, no system instructions, no
/// exceptions or interrupts. A call runs until it returns to the sentinel or stops with a typed <see cref="MachineFault"/>:
/// an instruction outside the subset, a control transfer outside executable memory that no hook answers, an access outside
/// mapped memory, a divide error or the step limit. Service hooks answer calls and jumps to their address instead of the
/// code there (a hook fires on a call, jump or return to its address, not when execution falls through into it). One
/// machine runs on one thread; it is not thread safe.
/// </summary>
public sealed partial class X86Machine
{
    private const int EaxIndex = 0, EcxIndex = 1, EdxIndex = 2, EbxIndex = 3, EspIndex = 4, EbpIndex = 5, EsiIndex = 6, EdiIndex = 7;

    private sealed record HookEntry(string Name, MachineHook? Handler);

    private readonly uint[] r = new uint[8];
    private readonly Dictionary<uint, HookEntry> hooks = [];
    private readonly Dictionary<string, List<uint>> importThunks = new(StringComparer.OrdinalIgnoreCase);
    private uint eip;
    private bool cf, pf, af, zf, sf, of, df;
    // AC (bit 18) and ID (bit 21): kept as written, so the CPUID-detection idiom sees an ID bit that toggles and then stops at cpuid.
    private uint storedFlags;
    private bool transferred;
    private uint instructionStart;
    private long steps, stepLimit;
    private bool running;

    public X86Machine(MachineOptions? options = null)
    {
        Options = options ?? new MachineOptions();
        Memory = new MachineMemory();
        Memory.Map(Options.StackBase, Options.StackSize, MemoryAccess.ReadWrite, "stack");
        r[EspIndex] = Options.StackBase + Options.StackSize - 16;
        if (Options.FsBase != 0)
        {
            Memory.Map(Options.FsBase, MachineMemory.PageSize, MemoryAccess.ReadWrite, "thread block");
            Memory.WriteU32(Options.FsBase, 0xFFFF_FFFF);
            Memory.WriteU32(Options.FsBase + 0x18, Options.FsBase);
        }
        if (Memory.IsMapped(Options.ReturnSentinel)) throw new ArgumentException("the return sentinel is mapped", nameof(options));
    }

    public MachineOptions Options { get; }
    public MachineMemory Memory { get; }
    /// <summary>Set to record what the following calls reach; null (the default) records nothing.</summary>
    public MachineCensus? Census { get; set; }

    public uint this[X86Register register]
    {
        get => r[(int)register];
        set => r[(int)register] = value;
    }

    public uint Eax { get => r[EaxIndex]; set => r[EaxIndex] = value; }
    public uint Ecx { get => r[EcxIndex]; set => r[EcxIndex] = value; }
    public uint Edx { get => r[EdxIndex]; set => r[EdxIndex] = value; }
    public uint Ebx { get => r[EbxIndex]; set => r[EbxIndex] = value; }
    public uint Esp { get => r[EspIndex]; set => r[EspIndex] = value; }
    public uint Ebp { get => r[EbpIndex]; set => r[EbpIndex] = value; }
    public uint Esi { get => r[EsiIndex]; set => r[EsiIndex] = value; }
    public uint Edi { get => r[EdiIndex]; set => r[EdiIndex] = value; }
    public uint Eip => eip;

    public bool Carry { get => cf; set => cf = value; }
    public bool Parity { get => pf; set => pf = value; }
    public bool Adjust { get => af; set => af = value; }
    public bool Zero { get => zf; set => zf = value; }
    public bool Sign { get => sf; set => sf = value; }
    public bool Overflow { get => of; set => of = value; }
    public bool Direction { get => df; set => df = value; }

    /// <summary>
    /// The flags register as <c>pushfd</c> stores it: CF, PF, AF, ZF, SF, DF, OF, AC and ID as written, IF always set and
    /// the fixed bit 1. <c>popfd</c> cannot change IF; IOPL, TF, NT and the other system bits are not modelled and read 0.
    /// </summary>
    public uint Eflags
    {
        get => 0x2u | (cf ? 0x1u : 0) | (pf ? 0x4u : 0) | (af ? 0x10u : 0) | (zf ? 0x40u : 0) | (sf ? 0x80u : 0)
            | 0x200u | (df ? 0x400u : 0) | (of ? 0x800u : 0) | storedFlags;
        set
        {
            cf = (value & 0x1) != 0;
            pf = (value & 0x4) != 0;
            af = (value & 0x10) != 0;
            zf = (value & 0x40) != 0;
            sf = (value & 0x80) != 0;
            storedFlags = value & (0x4_0000u | 0x20_0000u);
            df = (value & 0x400) != 0;
            of = (value & 0x800) != 0;
        }
    }

    /// <summary>Answers calls and jumps to <paramref name="address"/> with <paramref name="hook"/> (replacing an earlier hook there).</summary>
    public void Hook(uint address, string name, MachineHook hook)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(hook);
        if (address == Options.ReturnSentinel) throw new ArgumentException("the return sentinel cannot be hooked", nameof(address));
        hooks[address] = new HookEntry(name, hook);
    }

    /// <summary>
    /// Points every import slot of the loaded <paramref name="image"/> at its own address in the unmapped thunk range. A call
    /// through a slot then reaches the hook <see cref="HookImport"/> set for it, or stops with
    /// <see cref="MachineFaultKind.UnhookedImport"/> and the import's name. Once per machine; every thunk is checked before
    /// any slot changes, so a refusal leaves the machine as it was.
    /// </summary>
    /// <exception cref="InvalidOperationException">Imports were bound already.</exception>
    /// <exception cref="ArgumentException">The thunk range wraps, or a thunk is mapped, the sentinel or hooked.</exception>
    public void BindImports(PeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (importThunks.Count > 0) throw new InvalidOperationException("imports are bound already");
        if ((ulong)Options.ImportThunkBase + 16UL * (ulong)image.Imports.Count > 1UL << 32)
            throw new ArgumentException("the import thunk range wraps past 4 GiB", nameof(image));
        for (var i = 0; i < image.Imports.Count; i++)
        {
            var thunk = Options.ImportThunkBase + 16u * (uint)i;
            if (Memory.IsMapped(thunk) || thunk == Options.ReturnSentinel || hooks.ContainsKey(thunk))
                throw new ArgumentException($"import thunk 0x{thunk:X8} is mapped, the return sentinel or already hooked", nameof(image));
        }
        Span<byte> slot = stackalloc byte[4];
        for (var i = 0; i < image.Imports.Count; i++)
        {
            var import = image.Imports[i];
            var thunk = Options.ImportThunkBase + 16u * (uint)i;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(slot, thunk);
            Memory.Load(import.SlotAddress, slot);
            hooks[thunk] = new HookEntry(import.FullName, null);
            if (!importThunks.TryGetValue(import.FullName, out var thunks)) importThunks[import.FullName] = thunks = [];
            thunks.Add(thunk);
        }
    }

    /// <summary>Answers every bound slot of the import <c>library!name</c> (library case ignored); false when none was bound.</summary>
    public bool HookImport(string fullName, MachineHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        if (!importThunks.TryGetValue(fullName, out var thunks)) return false;
        foreach (var thunk in thunks) hooks[thunk] = new HookEntry(hooks[thunk].Name, hook);
        return true;
    }

    /// <summary>The <paramref name="index"/>-th stack argument of a hooked call ([ESP + 4 + 4 × index]).</summary>
    public uint Argument(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return Memory.ReadU32(r[EspIndex] + 4 + 4 * (uint)index);
    }

    /// <summary>
    /// Calls <paramref name="function"/> with the stack <paramref name="arguments"/> (pushed right to left, then the
    /// return sentinel), clears DF (the Win32 calling convention), runs until it returns there and restores ESP. Either
    /// calling convention works: the result says how many argument bytes the callee popped. Registers other than ESP and
    /// the flags keep what the call left. An exception a hook throws (a host bug) is not a fault and propagates.
    /// </summary>
    /// <exception cref="InvalidOperationException">A hook called the machine again.</exception>
    /// <exception cref="ArgumentException"><paramref name="function"/> is the return sentinel.</exception>
    public MachineRunResult Call(uint function, ReadOnlySpan<uint> arguments, long? maxSteps = null)
    {
        if (running) throw new InvalidOperationException("the machine is already running (a hook may not call it again)");
        if (function == Options.ReturnSentinel) throw new ArgumentException("the return sentinel is not a function", nameof(function));
        var saved = r[EspIndex];
        running = true;
        steps = 0;
        stepLimit = maxSteps ?? Options.MaxStepsPerCall;
        instructionStart = function;
        df = false;
        try
        {
            for (var i = arguments.Length - 1; i >= 0; i--) Push(arguments[i], 4);
            Push(Options.ReturnSentinel, 4);
            var entry = r[EspIndex];
            Census?.Function(function);
            Transfer(function);
            Run();
            if (r[EspIndex] < entry + 4)
                throw Fault(MachineFaultKind.ControlOutsideCode, $"reached the return sentinel with ESP 0x{r[EspIndex]:X8} below the return address");
            return new MachineRunResult(null, r[EaxIndex], r[EdxIndex], steps, r[EspIndex] - entry - 4);
        }
        catch (MachineFaultException exception)
        {
            var fault = exception.Fault;
            if (fault.InstructionAddress == 0) fault = fault with { InstructionAddress = instructionStart };
            return new MachineRunResult(fault, r[EaxIndex], r[EdxIndex], steps, 0);
        }
        finally
        {
            r[EspIndex] = saved;
            running = false;
        }
    }

    private void Run()
    {
        while (true)
        {
            if (transferred)
            {
                transferred = false;
                if (eip == Options.ReturnSentinel) return;
                if (hooks.TryGetValue(eip, out var hook) && RunHook(hook)) continue;
            }
            if (steps >= stepLimit) throw Fault(MachineFaultKind.InstructionLimit, $"more than {stepLimit} steps");
            steps++;
            Step();
        }
    }

    /// <returns>True when the hook returned (the loop goes on at its return address); false to run the code at the address.</returns>
    private bool RunHook(HookEntry hook)
    {
        instructionStart = eip;
        Census?.Hook(hook.Name);
        if (hook.Handler is null) throw Fault(MachineFaultKind.UnhookedImport, hook.Name);
        if (steps >= stepLimit) throw Fault(MachineFaultKind.InstructionLimit, $"more than {stepLimit} steps");
        steps++;
        var result = hook.Handler(this);
        switch (result.Kind)
        {
            case HookResult.Action.Return:
                var target = Pop(4);
                r[EspIndex] += result.ArgumentBytes;
                Transfer(target);
                return true;
            case HookResult.Action.Continue:
                if (!Memory.CanExecute(eip)) throw Fault(MachineFaultKind.ControlOutsideCode, $"hook {hook.Name} continued at 0x{eip:X8}, which is not code");
                return false;
            default:
                throw Fault(MachineFaultKind.HookStopped, $"{hook.Name}: {result.Reason}");
        }
    }

    /// <summary>A call, jump or return: the target must be the sentinel, a hook or executable memory.</summary>
    private void Transfer(uint target)
    {
        if (target != Options.ReturnSentinel && !hooks.ContainsKey(target) && !Memory.CanExecute(target))
            throw Fault(MachineFaultKind.ControlOutsideCode, $"transfer to 0x{target:X8}");
        eip = target;
        transferred = true;
    }

    private MachineFaultException Fault(MachineFaultKind kind, string detail) => new(new MachineFault(kind, instructionStart, detail));
}
