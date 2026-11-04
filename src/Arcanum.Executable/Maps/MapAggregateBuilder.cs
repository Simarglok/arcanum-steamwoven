using System.Security.Cryptography;
using Arcanum.Executable.Prototypes;

namespace Arcanum.Executable.Maps;

/// <summary>One map's mobile aggregate the original's own code wrote: the map's folder name, the file's name under <c>maps\</c> (the map name through <c>0x411830</c>) and its bytes.</summary>
public sealed record BuiltMapAggregate(string MapName, string FileName, byte[] Bytes);

/// <summary>The aggregates built (in the original's order) or the failure, with what the run read and searched.</summary>
public sealed record MapAggregateRunResult(IReadOnlyList<BuiltMapAggregate> Files, PrototypeBuildFailure? Failure, long Steps)
{
    public bool Succeeded => Failure is null;

    /// <summary>Data files the executable read, in the order it first opened them (the editor <c>.mob</c> files, the prototypes, the message files).</summary>
    public IReadOnlyList<string> ReadFiles { get; init; } = [];

    /// <summary>Data files it asked for that no source held and it can do without.</summary>
    public IReadOnlyList<string> MissedFiles { get; init; } = [];

    /// <summary>The directory searches it made (patterns).</summary>
    public IReadOnlyList<string> ListedPatterns { get; init; } = [];

    /// <summary>The start-up functions that returned 0 (the display modules cannot start without a display), in order.</summary>
    public IReadOnlyList<string> InitsReturningZero { get; init; } = [];

    /// <summary>The lines the executable wrote to its log (errors of the aggregate build among them).</summary>
    public IReadOnlyList<string> Log { get; init; } = [];
}

/// <summary>Explicit limits and switches of <see cref="MapAggregateBuilder.Build"/>.</summary>
public sealed record MapAggregateBuildOptions
{
    /// <summary>Interpret only the verified retail executable (default).</summary>
    public bool RequireVerifiedExecutable { get; init; } = true;

    public uint MaxHeapBytes { get; init; } = 256u << 20;

    /// <summary>Interpreter steps of each call into the executable (the start-up and one map's aggregate each take a call).</summary>
    public long MaxStepsPerCall { get; init; } = 20_000_000_000;

    public int MaxWrittenFiles { get; init; } = 512;
    public long MaxWrittenBytes { get; init; } = 256L << 20;

    /// <summary>Research (tests): the byte uninitialized memory holds at the start.</summary>
    internal byte UninitializedFill { get; init; }

    /// <summary>Research (tests): runs on the machine after the services are installed, before the first start-up function.</summary>
    internal Action<X86Machine>? ConfigureMachine { get; init; }
}

/// <summary>
/// ARC-DEV-082 (slice E5 of the <see href="../../../docs/research/prototype-extraction.md">prototype extraction</see>): makes the per-map mobile
/// aggregates the way the player's own <c>Arcanum.exe</c> does at its first module load. The original lists the module's map folders and, for each,
/// reads every editor <c>.mob</c> object and writes them to <c>maps\&lt;ROT13 name&gt;</c> after the module's 16-byte GUID (0x4102F0, name rule
/// 0x411830); a fresh installation has none of them. Like <see cref="PrototypeBuilder"/> it interprets the executable's own functions in
/// <see cref="X86Machine"/>: the object system, the object file reader and writer and the name rule are the executable's; only the system services
/// (memory, the file layer over <see cref="IOriginalFileSource"/>, the clock) are Steamwoven's. The source must hold the module (its map folders and
/// the base and module data) and the prototype files. Nothing of the executable or of the built files is kept here.
/// </summary>
public static class MapAggregateBuilder
{
    /// <summary>The size of the module GUID the original writes at the start of every aggregate.</summary>
    public const int ModuleGuidBytes = 16;

    /// <summary>The bytes the archive format ends with: the 16-byte GUID the original takes, the magic <c>1TAD</c> and two 32-bit sizes (the footer's 28 bytes).</summary>
    public const int ArchiveFooterBytes = 28;

    /// <summary>
    /// The module GUID the original takes from a <c>.dat</c> archive (0x53BC50, read from the file's end): when the dword 12 bytes before the end is
    /// <c>1TAD</c> it is the 16 bytes that start 24 bytes before the end (so its last four bytes are the magic itself, an offset the original
    /// keeps), when it is <c> DAT</c> the GUID is zero; any other archive is not one the original reads (null).
    /// </summary>
    /// <param name="footer">The last <see cref="ArchiveFooterBytes"/> bytes of the archive.</param>
    public static byte[]? ModuleGuidFromArchiveFooter(ReadOnlySpan<byte> footer)
    {
        if (footer.Length != ArchiveFooterBytes) return null;
        var magic = footer.Slice(ArchiveFooterBytes - 12, 4);
        if (magic.SequenceEqual("1TAD"u8)) return footer.Slice(ArchiveFooterBytes - 24, ModuleGuidBytes).ToArray();
        if (magic.SequenceEqual(" DAT"u8)) return new byte[ModuleGuidBytes];
        return null;
    }

    public static MapAggregateRunResult Build(byte[] executable, IOriginalFileSource files, byte[] moduleGuid, MapAggregateBuildOptions? options = null, Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(moduleGuid);
        if (moduleGuid.Length != ModuleGuidBytes) throw new ArgumentException("The module GUID is 16 bytes.", nameof(moduleGuid));
        options ??= new MapAggregateBuildOptions();
        if (options.MaxHeapBytes == 0 || options.MaxHeapBytes > PrototypeBuilder.MaxHeapLimit) throw new ArgumentOutOfRangeException(nameof(options), $"MaxHeapBytes must be between 1 and {PrototypeBuilder.MaxHeapLimit}");
        if (options.MaxStepsPerCall <= 0 || options.MaxWrittenFiles < 0 || options.MaxWrittenBytes < 0) throw new ArgumentOutOfRangeException(nameof(options), "the step, file and byte limits must be positive");
        var read = PeImage.Read(executable);
        if (read.Image is not { } image) return Fail(PrototypeBuildFailureKind.InvalidExecutable, $"{read.Failure!.Kind}: {read.Failure.Detail}");
        if (options.RequireVerifiedExecutable && !string.Equals(Convert.ToHexString(SHA256.HashData(executable)), PrototypeBuilder.RetailSha256, StringComparison.OrdinalIgnoreCase))
            return Fail(PrototypeBuildFailureKind.UnverifiedExecutable, "the executable is not the retail 1.0.7.4 Arcanum.exe");
        var sessionOptions = new PrototypeBuildOptions
        {
            RequireVerifiedExecutable = false, MaxHeapBytes = options.MaxHeapBytes, MaxStepsPerCall = options.MaxStepsPerCall,
            MaxWrittenFiles = options.MaxWrittenFiles, MaxWrittenBytes = options.MaxWrittenBytes, UninitializedFill = options.UninitializedFill,
            ConfigureMachine = options.ConfigureMachine, Aggregate = new AggregateSettings((byte[])moduleGuid.Clone()),
        };
        try
        {
            return new PrototypeBuilder.BuildSession(image, files, sessionOptions).RunAggregates(progress);
        }
        catch (MachineFaultException exception)
        {
            return new([], new PrototypeBuildFailure(PrototypeBuildFailureKind.StartupFault, $"the start-up set-up: {exception.Fault}", exception.Fault), 0);
        }
        catch (ArgumentException exception)
        {
            return Fail(PrototypeBuildFailureKind.InvalidExecutable, $"the image cannot be loaded: {exception.Message}");
        }
    }

    private static MapAggregateRunResult Fail(PrototypeBuildFailureKind kind, string detail) => new([], new PrototypeBuildFailure(kind, detail), 0);
}
