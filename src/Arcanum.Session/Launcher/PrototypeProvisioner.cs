using System.Security.Cryptography;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Executable.Maps;
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

public enum PrototypeProvisionStage { Checking, Building, Storing, BuildingMaps }

/// <summary>Where the provision is: the stage and, while building, the files done and the total (0 when not known yet).</summary>
public sealed record PrototypeProvisionProgress(PrototypeProvisionStage Stage, int Done = 0, int Total = 0);

/// <param name="MountFolder">The folder to hand to <see cref="PrototypeCacheMount.Use"/>; null when the installation has its own files or nothing was built.</param>
/// <param name="Files">The prototype files in the cache.</param>
/// <param name="MapFiles">The map aggregate files in the cache (ARC-DEV-082).</param>
public sealed record PrototypeProvisionResult(PrototypeProvisionStatus Status, string? MountFolder = null, string? Detail = null, int Files = 0, int MapFiles = 0)
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

    /// <summary>The map aggregate build (ARC-DEV-082; tests pass a stand-in; the default is <see cref="MapAggregateBuilder.Build"/> with its default options): the executable, the data, the module's GUID.</summary>
    public Func<byte[], IOriginalFileSource, byte[], Action<int, int>?, MapAggregateRunResult>? BuildMaps { get; init; }
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
    /// <param name="cacheRoot">Steamwoven's folder for built first-start files (created when needed), outside the game folder.</param>
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
            var hasPrototypes = prototypes.Numbers.Count >= options.InstalledFileCount;
            var missingMaps = MissingAggregates(stack);
            if (hasPrototypes && missingMaps == 0) return new(PrototypeProvisionStatus.Present, Files: prototypes.Numbers.Count);

            var exe = ReadExecutable(plan.GameRoot, out var exeFailure);
            if (exe is null) return exeFailure!;
            var exeSha = Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant();
            if (!string.Equals(exeSha, options.ExpectedExeSha256, StringComparison.OrdinalIgnoreCase))
                return new(PrototypeProvisionStatus.UnverifiedExecutable, Detail: "its Arcanum.exe is not the retail 1.0.7.4 one, the only executable the first-start files are built from");

            var store = new PrototypeCacheStore(cacheRoot, exeSha);
            store.RemoveLeftovers();
            var status = PrototypeProvisionStatus.Present;
            var files = prototypes.Numbers.Count;
            if (!hasPrototypes)
            {
                var provided = ProvidePrototypes(stack, store, exe, options, progress, cancel);
                if (!provided.Succeeded) return provided;
                status = provided.Status;
                files = provided.Files;
            }

            else store.DropPrototypes(); // the cache is mounted for its map files only; an older prototype copy there would shadow the installation's own
            var maps = 0;
            if (missingMaps > 0)
            {
                var provided = ProvideMaps(own, hasPrototypes ? null : store.MountFolder, store, exe, exeSha, options, progress, cancel);
                if (!provided.Succeeded)
                {
                    // An installation that has its own prototypes started before the map aggregates were built: a failure of the map stage there
                    // leaves it as it was (the cache is not mounted, the result says why); a fresh installation needs them and is refused.
                    if (hasPrototypes && provided.Status != PrototypeProvisionStatus.Cancelled)
                        return new(PrototypeProvisionStatus.Present, Files: files, Detail: "the map files could not be made: " + provided.Detail);
                    return provided;
                }
                maps = provided.MapFiles;
                if (provided.Status == PrototypeProvisionStatus.Built || status == PrototypeProvisionStatus.Present) status = provided.Status;
            }
            return new(status, store.MountFolder, Files: files, MapFiles: maps);
        }
        catch (OperationCanceledException) { return new(PrototypeProvisionStatus.Cancelled); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return new(PrototypeProvisionStatus.SourceFailed, Detail: $"{error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>The prototype files: from the cache when it verifies, else built now and stored (the E4 path).</summary>
    private static PrototypeProvisionResult ProvidePrototypes(ContentStack stack, PrototypeCacheStore store, byte[] exe, PrototypeProvisionOptions options,
        Action<PrototypeProvisionProgress>? progress, CancellationToken cancel)
    {
        var source = new StackOriginalFiles(stack);
        if (store.Verify(source) is { } cached)
        {
            cancel.ThrowIfCancellationRequested();
            return new(PrototypeProvisionStatus.Cached, store.MountFolder, Files: cached);
        }

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
        cancel.ThrowIfCancellationRequested();
        return store.Verify(source) is { } stored
            ? new(PrototypeProvisionStatus.Built, store.MountFolder, Files: stored)
            : new(PrototypeProvisionStatus.CacheWriteFailed, Detail: "the stored prototype files did not read back");
    }

    /// <summary>
    /// How many map folders of the module have no aggregate file in the installation (ARC-DEV-082). The original writes one per map folder to
    /// <c>modules/&lt;module&gt;/maps</c> at its first start of the module; an installation it never ran has none of them.
    /// </summary>
    internal static int MissingAggregates(ContentStack stack)
        => ContentPhase.Folders(stack, "maps", ContentLoadPhase.ModuleLoaded)
            .Count(map => ContentPhase.Candidate(stack, "maps/" + WorldCatalog.AggregateName(map), ContentLoadPhase.ModuleLoaded) is null);

    /// <summary>The module's GUID the way the original reads it (0x53BC50): from the end of the module's <c>.dat</c>; null when the module is not an archive of the kind.</summary>
    internal static byte[]? ModuleGuid(ContentMountPlan plan)
    {
        if (plan.SearchOrder.FirstOrDefault(source => source.Role == ContentSourceRole.ModuleArchive) is not { } module) return null;
        var footer = new byte[MapAggregateBuilder.ArchiveFooterBytes];
        using var file = File.OpenRead(module.Path);
        if (file.Length < footer.Length) return null;
        file.Seek(-footer.Length, SeekOrigin.End);
        file.ReadExactly(footer);
        return MapAggregateBuilder.ModuleGuidFromArchiveFooter(footer);
    }

    /// <summary>
    /// The map aggregates (ARC-DEV-082): from the cache when it verifies, else built now from the executable's own code over the module's editor
    /// files (the prototype files come from the installation, or from the cache <paramref name="prototypeCache"/>) and stored beside the prototypes.
    /// </summary>
    private static PrototypeProvisionResult ProvideMaps(ContentMountPlan own, string? prototypeCache, PrototypeCacheStore store, byte[] exe, string exeSha,
        PrototypeProvisionOptions options, Action<PrototypeProvisionProgress>? progress, CancellationToken cancel)
    {
        var guid = ModuleGuid(own);
        if (guid is null) return new(PrototypeProvisionStatus.SourceFailed, Detail: "the module's archive does not end the way the original reads its GUID");
        var identity = PrototypeCacheStore.ModuleIdentity(own);
        if (store.VerifyAggregates(identity) is { } cached)
        {
            cancel.ThrowIfCancellationRequested();
            return new(PrototypeProvisionStatus.Cached, store.MountFolder, MapFiles: cached);
        }

        cancel.ThrowIfCancellationRequested();
        progress?.Invoke(new(PrototypeProvisionStage.BuildingMaps));
        // The build reads the module's data with the prototypes beside it: the installation's own, or the cache this start just made.
        var plan = prototypeCache is null ? own : WithPrototypeCache(own, prototypeCache);
        var opened = ContentStack.Open(plan);
        if (opened.Status != ContentStackStatus.Ready) return new(PrototypeProvisionStatus.SourceFailed, Detail: $"the game content cannot be opened ({opened.Status})");
        using var stack = opened.Stack!;
        var source = new StackOriginalFiles(stack, ContentLoadPhase.ModuleLoaded);
        var build = (options.BuildMaps ?? ((bytes, files, moduleGuid, report) => MapAggregateBuilder.Build(bytes, files, moduleGuid, null, report)))(exe, source, guid, (done, total) =>
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(new(PrototypeProvisionStage.BuildingMaps, done, total));
        });
        if (build.Failure is { } failure)
            return failure.Kind is PrototypeBuildFailureKind.MissingSourceFile or PrototypeBuildFailureKind.UnverifiedExecutable
                ? new(PrototypeProvisionStatus.SourceFailed, Detail: "map files: " + failure.Detail)
                : new(PrototypeProvisionStatus.BuildFailed, Detail: $"map files: {failure.Kind}: {failure.Detail}");
        // The game looks a map's aggregate up by the name rule of the catalogue: a file under any other name would never be found.
        if (build.Files.FirstOrDefault(map => map.FileName != WorldCatalog.AggregateName(map.MapName)) is { } misnamed)
            return new(PrototypeProvisionStatus.BuildFailed, Detail: $"map files: the aggregate of map {misnamed.MapName} was written under another name than the game looks up");
        cancel.ThrowIfCancellationRequested();
        progress?.Invoke(new(PrototypeProvisionStage.Storing, build.Files.Count, build.Files.Count));
        if (store.StoreAggregates(build, identity) is { } storeFailure) return new(PrototypeProvisionStatus.CacheWriteFailed, Detail: storeFailure);
        cancel.ThrowIfCancellationRequested();
        return store.VerifyAggregates(identity) is { } stored
            ? new(PrototypeProvisionStatus.Built, store.MountFolder, MapFiles: stored)
            : new(PrototypeProvisionStatus.CacheWriteFailed, Detail: "the stored map files did not read back");
    }

    /// <summary>The plan with the prototype cache mounted where <see cref="ContentMountPlan.Discover"/> puts it: above the base archives, below <c>data/</c> (the search order lists the highest priority first).</summary>
    private static ContentMountPlan WithPrototypeCache(ContentMountPlan plan, string cacheFolder)
    {
        var order = plan.SearchOrder.Where(source => source.Role != ContentSourceRole.PrototypeCache).ToList();
        var at = order.FindIndex(source => source.Role is ContentSourceRole.BaseArchive or ContentSourceRole.GameRoot or ContentSourceRole.Tig);
        order.Insert(at < 0 ? order.Count : at, new ContentSource(ContentSourceRole.PrototypeCache, 0, Path.GetFullPath(cacheFolder)));
        return new ContentMountPlan(plan.GameRoot, plan.ModuleName, order);
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

/// <summary>The data the original sees: at game start over the installation's base sources only (it builds its prototypes before it mounts
/// the module), the first base source in search order holding each file; once the module is loaded (the map aggregates, ARC-DEV-082) the
/// stack's winner.</summary>
internal sealed class StackOriginalFiles(ContentStack stack, ContentLoadPhase phase = ContentLoadPhase.GameStart) : IOriginalFileSource
{
    public IReadOnlyList<string> List(string directory)
    {
        var prefix = directory.Length == 0 ? "" : directory.Replace('\\', '/').ToLowerInvariant() + "/";
        return stack.Keys(prefix)
            .Where(key => ContentPhase.Candidate(stack, key, phase) is not null)
            .Select(key => key[prefix.Length..]).Where(rest => rest.Length > 0 && !rest.Contains('/')).ToList();
    }

    public IReadOnlyList<string> ListDirectories(string directory) => ContentPhase.Folders(stack, directory, phase);

    public byte[]? Read(string path)
    {
        if (ContentPhase.Candidate(stack, path, phase) is not { } candidate) return null;
        var read = stack.Read(candidate);
        return read.Status == ContentReadStatus.Read ? read.Bytes : null;
    }
}
