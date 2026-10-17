namespace Arcanum.Data.Content;

/// <summary>
/// ARC-DATA-027: when the original reads a file, which decides the sources it can come from. The original mounts
/// <c>tig.dat</c>, the game folder, <c>arcanum*.dat</c> and <c>data/</c>, runs every subsystem's init function, then
/// mounts the module (<c>.dat</c>, patches, folder) and runs the module-load functions (checked in the 1.0.7.4
/// executable, docs/research/original-verification-ledger.md "Prototype phase" and "Load phases"). A file read by an
/// init function and kept for the process sees the base sources only; a file read at module load or later, while the
/// game runs, sees every source.
/// </summary>
public enum ContentLoadPhase
{
    /// <summary>Read by an init function before the module is mounted: the first base source in search order.</summary>
    GameStart,
    /// <summary>Read at module load or later: the content stack's winner.</summary>
    ModuleLoaded,
}

/// <summary>ARC-DATA-027: the candidate the original reads for a path in a load phase.</summary>
public static class ContentPhase
{
    /// <summary>The candidate read in <paramref name="phase"/>, or null when no source of that phase holds the path
    /// (also for a path that is not a valid virtual path).</summary>
    public static ContentCandidate? Candidate(ContentStack stack, string path, ContentLoadPhase phase)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(path);
        return Candidate(stack.Resolve(path), phase);
    }

    /// <summary>The candidate of <paramref name="resolution"/> read in <paramref name="phase"/>.</summary>
    public static ContentCandidate? Candidate(ContentResolution resolution, ContentLoadPhase phase)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return phase switch
        {
            ContentLoadPhase.GameStart => resolution.Candidates.FirstOrDefault(candidate => !IsModule(candidate.Source.Role)),
            ContentLoadPhase.ModuleLoaded => resolution.Winner,
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };
    }

    /// <summary>The module archive, a module patch or the module folder: sources mounted after game start.</summary>
    public static bool IsModule(ContentSourceRole role) =>
        role is ContentSourceRole.ModuleArchive or ContentSourceRole.ModulePatch or ContentSourceRole.ModuleDirectory;
}
