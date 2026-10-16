namespace Arcanum.Data.Content;

/// <summary>Where a mounted source comes from in the original start-up sequence
/// (see docs/research/content-precedence.md). Directory roles hold loose files.</summary>
public enum ContentSourceRole { Tig, GameRoot, BaseArchive, DataDirectory, ModuleArchive, ModulePatch, ModuleDirectory }
public enum ContentSourceKind { Archive, Directory }

/// <param name="Number">The mount position among <c>arcanum*.dat</c> base archives (0 = first
/// mounted, lowest priority) or the PATCH digit; 0 for every other role.</param>
/// <param name="Path">The full local path of the archive file or directory.</param>
public sealed record ContentSource(ContentSourceRole Role, int Number, string Path)
{
    public ContentSourceKind Kind => Role is ContentSourceRole.GameRoot or ContentSourceRole.DataDirectory or ContentSourceRole.ModuleDirectory
        ? ContentSourceKind.Directory : ContentSourceKind.Archive;
}

public enum ContentMountStatus { Ready, MissingGameRoot, MissingTig, MissingModule, InvalidModuleName, AmbiguousName, UnsupportedLayout, Unreadable }
public sealed record ContentMountDiscovery(ContentMountStatus Status, ContentMountPlan? Plan, string? Detail = null);

public enum ContentStackStatus { Ready, ArchiveUnreadable, UnsortedArchiveIndex, UnsupportedEntryName, IgnoreListUnsupported, DirectoryRecordCollision, LooseLimitExceeded, AmbiguousLooseName, LooseUnreadable }

/// <summary>Opened stack or the typed reason it could not be opened. <see cref="Detail"/>
/// names the failing source path; it never carries payload bytes.</summary>
public sealed record ContentStackOpenResult(ContentStackStatus Status, ContentStack? Stack, string? Detail = null);

public sealed class ContentStackOptions
{
    public DatReaderOptions Dat { get; init; } = new();
    /// <summary>Upper bound on loose files indexed across all directory sources.</summary>
    public int MaxLooseFiles { get; init; } = 200_000;
    /// <summary>Upper bound on a single loose file returned by <see cref="ContentStack.Read"/>.</summary>
    public long MaxLooseFileBytes { get; init; } = 256L * 1024 * 1024;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Dat);
        if (MaxLooseFiles <= 0 || MaxLooseFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(ContentStackOptions), "Loose-file limits must be positive.");
    }
}

/// <summary>One physical copy of a virtual path: an archive member or a loose file.</summary>
public sealed record ContentCandidate(ContentSource Source, DatEntry? Entry, string? FilePath, long Length);

public enum ContentResolutionStatus { Resolved, NotFound, InvalidPath }

/// <summary>Every physical copy of one virtual path in search order. The first candidate is
/// what the original runtime opens; the rest are shadowed by it.</summary>
public sealed record ContentResolution(ContentResolutionStatus Status, string Key, IReadOnlyList<ContentCandidate> Candidates)
{
    public ContentCandidate? Winner => Candidates.Count == 0 ? null : Candidates[0];
    public IEnumerable<ContentCandidate> Shadowed => Candidates.Skip(1);
}

public enum ContentReadStatus { Read, TooLarge, Unreadable }
public sealed record ContentReadResult(ContentReadStatus Status, byte[]? Bytes, string? Detail = null);
