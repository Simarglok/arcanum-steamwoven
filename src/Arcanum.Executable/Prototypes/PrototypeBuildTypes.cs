namespace Arcanum.Executable.Prototypes;

/// <summary>
/// The player's data as the original's file layer sees it when it starts: the base sources only (the original runs its
/// start-up functions and the prototype check before it mounts the module). The host decides what that is; nothing here writes.
/// </summary>
public interface IOriginalFileSource
{
    /// <summary>
    /// The bytes of the file the original asks for (a relative path with backslashes such as <c>rules\npc.txt</c>; letters
    /// compare ASCII case-insensitively), or null when no mounted source holds it.
    /// </summary>
    byte[]? Read(string path);

    /// <summary>
    /// The names of the files directly inside <paramref name="directory"/> (a relative path with backslashes such as <c>scr</c>,
    /// no trailing separator) over every mounted source, each name once. An empty list when the directory holds none.
    /// </summary>
    IReadOnlyList<string> List(string directory);
}

/// <summary>One prototype file the executable wrote: the code's basic number (the file's ID number is that plus 20), its name under <c>proto\</c> and its bytes.</summary>
public sealed record BuiltPrototype(int Number, string Name, byte[] Bytes);

public enum PrototypeBuildFailureKind
{
    /// <summary>The bytes are not a PE32 i386 image.</summary>
    InvalidExecutable,
    /// <summary>The executable is not the verified retail 1.0.7.4 one (its SHA-256 differs).</summary>
    UnverifiedExecutable,
    /// <summary>The executable asked for a data file it cannot do without and no source holds it.</summary>
    MissingSourceFile,
    /// <summary>The interpreter stopped inside a start-up function.</summary>
    StartupFault,
    /// <summary>The interpreter stopped inside the prototype check or a build.</summary>
    BuildFault,
    /// <summary>The executable did something the builder does not allow (an unexpected file operation) or a build did not write exactly one prototype file.</summary>
    BadOutput,
    /// <summary>The heap, step or file limit was reached.</summary>
    LimitExceeded,
}

public sealed record PrototypeBuildFailure(PrototypeBuildFailureKind Kind, string Detail, MachineFault? Fault = null, int? Number = null);

/// <summary>Explicit limits and switches of <see cref="PrototypeBuilder.Build"/>.</summary>
public sealed record PrototypeBuildOptions
{
    /// <summary>Interpret only the verified retail executable (default). Research on another build turns this off.</summary>
    public bool RequireVerifiedExecutable { get; init; } = true;

    /// <summary>Guest heap limit in bytes.</summary>
    public uint MaxHeapBytes { get; init; } = 256u << 20;

    /// <summary>Interpreter steps of each call into the executable; the call into the original's prototype check is the longest (a fresh installation takes about 2.4 × 10^9).</summary>
    public long MaxStepsPerCall { get; init; } = 20_000_000_000;

    public int MaxWrittenFiles { get; init; } = 4096;
    public long MaxWrittenBytes { get; init; } = 64L << 20;

    /// <summary>
    /// Prototype files the installation already has (names under <c>proto\</c>, such as <c>001020 - Wall.pro</c>). The original's check
    /// builds only the numbers whose file it does not find, so with the full set of a finished installation it rebuilds the files whose
    /// name the check does not expect. Null or empty is a fresh installation: all 1,423 are built.
    /// </summary>
    public IReadOnlySet<string>? ExistingFiles { get; init; }

    /// <summary>Records every instruction kind, address and function of the run (slow); null leaves the census off.</summary>
    public MachineCensus? Census { get; init; }

    /// <summary>Research (tests): runs on the machine after the services are installed, before the first start-up function.</summary>
    internal Action<X86Machine>? ConfigureMachine { get; init; }

    /// <summary>
    /// Research (tests): the byte that uninitialized memory holds at the start (the stack, the registers and heap blocks that
    /// were not requested zeroed); it also moves the stack and the heap. 0 by default. Two runs with different bytes differ in
    /// exactly the output values that depend on uninitialized memory or on an address.
    /// </summary>
    internal byte UninitializedFill { get; init; }
}

/// <summary>The files built (in the original's order) or the failure, with what the run read, searched and called.</summary>
public sealed record PrototypeBuildResult(IReadOnlyList<BuiltPrototype> Files, PrototypeBuildFailure? Failure, long Steps)
{
    public bool Succeeded => Failure is null;

    /// <summary>Data files the executable read, in the order it first opened them.</summary>
    public IReadOnlyList<string> ReadFiles { get; init; } = [];

    /// <summary>Data files it asked for that no source held and it can do without (they read as absent, as in the original).</summary>
    public IReadOnlyList<string> MissedFiles { get; init; } = [];

    /// <summary>The directory searches it made (patterns).</summary>
    public IReadOnlyList<string> ListedPatterns { get; init; } = [];

    /// <summary>How often it called the window callbacks of the start information (view change, redraw).</summary>
    public (int ViewChange, int Redraw) CallbackCalls { get; init; }
}
