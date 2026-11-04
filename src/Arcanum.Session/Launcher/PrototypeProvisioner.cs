using System.Security.Cryptography;
using Arcanum.Data.Content;
using Arcanum.Executable.Prototypes;

namespace Arcanum.Session.Launcher;

/// <summary>What <see cref="PrototypeProvisioner.Provision"/> found or did. <see cref="Present"/>, <see cref="Cached"/> and <see cref="Built"/> succeed.</summary>
public enum PrototypeProvisionStatus
{
    /// <summary>The installation holds its own prototype files (the original ran there): nothing is mounted.</summary>
    Present,
    /// <summary>A cache built from this executable and this data earlier is intact: it is used.</summary>
    Cached,
    /// <summary>The files were built from the player's <c>Arcanum.exe</c> now and stored.</summary>
    Built,
    /// <summary>The folder is not a ready game folder, or its content cannot be opened.</summary>
    SourceFailed,
    /// <summary>The folder holds no <c>Arcanum.exe</c> that can be read.</summary>
    NoExecutable,
    /// <summary>Its <c>Arcanum.exe</c> is not the retail 1.0.7.4 one, the only executable the project verifies and interprets.</summary>
    UnverifiedExecutable,
    /// <summary>The interpreter stopped, or the executable did something the builder refuses.</summary>
    BuildFailed,
    /// <summary>The files were built but could not be stored in the settings folder.</summary>
    CacheWriteFailed,
    Cancelled,
}

public enum PrototypeProvisionStage { Checking, Building, Storing }

/// <summary>Where the provision is: the stage and, while building, the files done and the total (0 when not known yet).</summary>
public sealed record PrototypeProvisionProgress(PrototypeProvisionStage Stage, int Done = 0, int Total = 0);

/// <param name="MountFolder">The folder to hand to <see cref="PrototypeCacheMount.Use"/>; null when the installation has its own files or nothing was built.</param>
/// <param name="Files">The prototype files in the cache.</param>
public sealed record PrototypeProvisionResult(PrototypeProvisionStatus Status, string? MountFolder = null, string? Detail = null, int Files = 0)
{
    public bool Succeeded => Status is PrototypeProvisionStatus.Present or PrototypeProvisionStatus.Cached or PrototypeProvisionStatus.Built;
}

/// <summary>Switches and test seams of <see cref="PrototypeProvisioner.Provision"/>.</summary>
public sealed record PrototypeProvisionOptions
{
    /// <summary>An installation with at least this many prototype files in its base sources has its own (a finished one holds all 1,423).</summary>
    public int InstalledFileCount { get; init; } = PrototypeProvisioner.InstalledFileCount;

    /// <summary>The executable's SHA-256 that may be interpreted (lower-case hex); the retail 1.0.7.4 one by default.</summary>
    public string ExpectedExeSha256 { get; init; } = PrototypeBuilder.RetailSha256;

    /// <summary>The build (tests pass a stand-in; the default is <see cref="PrototypeBuilder.Build"/> with its default options).</summary>
    public Func<byte[], IOriginalFileSource, Action<int, int>?, PrototypeBuildResult>? Build { get; init; }
}

/// <summary>
/// ARC-DEV-069 (slice E4 of the <see href="../../../../docs/research/prototype-extraction.md">prototype extraction</see>): makes sure a game
/// folder has the prototype files the map readers need. The original writes them to <c>data/proto</c> at its first start; an installation it
/// never ran (a fresh GOG one, or one unpacked on macOS) has none. Like the original's first start, Steamwoven builds them once, from the
/// player's own <c>Arcanum.exe</c> (<see cref="PrototypeBuilder"/>, about half a minute), keeps them in its own settings folder
/// (<see cref="PrototypeCacheStore"/>, keyed by the executable and verified against the data it read) and mounts them under <c>data/</c>
/// (<see cref="PrototypeCacheMount"/>); every later start verifies and reuses them. The game folder is only read. Godot-free; it blocks, so a
/// host calls it off its main thread and shows <paramref name="progress"/>.
/// </summary>
public static class PrototypeProvisioner
{
    /// <summary>The prototype files of a finished installation (and of a fresh build).</summary>
    public const int InstalledFileCount = 1423;

    /// <summary>The largest executable that is read (the retail one is about 2 MB).</summary>
    public const long MaxExeBytes = GameFolderLocator.MaxExeBytes;

    /// <param name="folder">A ready game folder (<see cref="GameFolderCheck.Ready"/>).</param>
    /// <param name="cacheRoot">Steamwoven's folder for built prototypes (created when needed), outside the game folder.</param>
    /// <param name="progress">Called on the calling thread (the builder's), at most once per file built.</param>
    public static PrototypeProvisionResult Provision(GameFolderCheck folder, string cacheRoot, Action<PrototypeProvisionProgress>? progress = null,
        CancellationToken cancel = default, PrototypeProvisionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentException.ThrowIfNullOrEmpty(cacheRoot);
        options ??= new PrototypeProvisionOptions();
        if (folder is not { Ready: true, Plan: { } plan }) return new(PrototypeProvisionStatus.SourceFailed, Detail: "the folder is not a game folder the game can open");
        try
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(new(PrototypeProvisionStage.Checking));
            // The installation's own sources: a cache mounted for an earlier start is not part of what the installation has.
            var own = new ContentMountPlan(plan.GameRoot, plan.ModuleName, plan.SearchOrder.Where(source => source.Role != ContentSourceRole.PrototypeCache));
            var opened = ContentStack.Open(own);
            if (opened.Status != ContentStackStatus.Ready) return new(PrototypeProvisionStatus.SourceFailed, Detail: $"the game content cannot be opened ({opened.Status})");
            using var stack = opened.Stack!;
            var prototypes = ContentPrototypes.Open(stack);
            if (prototypes.Numbers.Count >= options.InstalledFileCount) return new(PrototypeProvisionStatus.Present, Files: prototypes.Numbers.Count);

            var exe = ReadExecutable(plan.GameRoot, out var exeFailure);
            if (exe is null) return exeFailure!;
            var exeSha = Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant();
            if (!string.Equals(exeSha, options.ExpectedExeSha256, StringComparison.OrdinalIgnoreCase))
                return new(PrototypeProvisionStatus.UnverifiedExecutable, Detail: "its Arcanum.exe is not the retail 1.0.7.4 one, the only executable the prototype files are built from");

            var source = new StackOriginalFiles(stack);
            var store = new PrototypeCacheStore(cacheRoot, exeSha);
            store.RemoveLeftovers();
            if (store.Verify(source) is { } cached) return new(PrototypeProvisionStatus.Cached, store.MountFolder, Files: cached);

            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(new(PrototypeProvisionStage.Building));
            var build = (options.Build ?? ((bytes, files, report) => PrototypeBuilder.Build(bytes, files, null, report)))(exe, source, (done, total) =>
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke(new(PrototypeProvisionStage.Building, done, total));
            });
            if (build.Failure is { } failure) return Failed(failure);
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(new(PrototypeProvisionStage.Storing, build.Files.Count, build.Files.Count));
            if (store.Store(build, source) is { } storeFailure) return new(PrototypeProvisionStatus.CacheWriteFailed, Detail: storeFailure);
            // What was stored must read back as a cache (the manifest, every file), or the next start would build again without saying why.
            return store.Verify(source) is { } stored
                ? new(PrototypeProvisionStatus.Built, store.MountFolder, Files: stored)
                : new(PrototypeProvisionStatus.CacheWriteFailed, Detail: "the stored prototype files did not read back");
        }
        catch (OperationCanceledException) { return new(PrototypeProvisionStatus.Cancelled); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return new(PrototypeProvisionStatus.SourceFailed, Detail: $"{error.GetType().Name}: {error.Message}");
        }
    }

    private static PrototypeProvisionResult Failed(PrototypeBuildFailure failure) => failure.Kind switch
    {
        PrototypeBuildFailureKind.UnverifiedExecutable => new(PrototypeProvisionStatus.UnverifiedExecutable, Detail: failure.Detail),
        PrototypeBuildFailureKind.MissingSourceFile => new(PrototypeProvisionStatus.SourceFailed, Detail: failure.Detail),
        _ => new(PrototypeProvisionStatus.BuildFailed, Detail: $"{failure.Kind}: {failure.Detail}"),
    };

    /// <summary><c>Arcanum.exe</c> of the game folder (letters compare ASCII case-insensitively; two names that differ only in case are refused).</summary>
    private static byte[]? ReadExecutable(string gameRoot, out PrototypeProvisionResult? failure)
    {
        failure = null;
        var matches = Directory.EnumerateFiles(gameRoot).Where(path => string.Equals(Path.GetFileName(path), "arcanum.exe", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length != 1) { failure = new(PrototypeProvisionStatus.NoExecutable, Detail: "no single Arcanum.exe in the game folder"); return null; }
        var info = new FileInfo(matches[0]);
        if (info.Length > MaxExeBytes) { failure = new(PrototypeProvisionStatus.UnverifiedExecutable, Detail: "its Arcanum.exe is too large to be the retail one"); return null; }
        return File.ReadAllBytes(info.FullName);
    }
}

/// <summary>The data the original sees when it starts, over the installation's base sources only (it builds its prototypes before it mounts
/// the module): the first base source in search order holds each file.</summary>
internal sealed class StackOriginalFiles(ContentStack stack) : IOriginalFileSource
{
    public IReadOnlyList<string> List(string directory)
    {
        var prefix = directory.Length == 0 ? "" : directory.Replace('\\', '/').ToLowerInvariant() + "/";
        return stack.Keys(prefix)
            .Where(key => ContentPhase.Candidate(stack, key, ContentLoadPhase.GameStart) is not null)
            .Select(key => key[prefix.Length..]).Where(rest => rest.Length > 0 && !rest.Contains('/')).ToList();
    }

    public byte[]? Read(string path)
    {
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } candidate) return null;
        var read = stack.Read(candidate);
        return read.Status == ContentReadStatus.Read ? read.Bytes : null;
    }
}
