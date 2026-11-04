namespace Arcanum.Data.Content;

/// <summary>The ordered list of sources the original runtime mounts for one module, highest
/// priority first. Discovery only reads the installation; the original creates the
/// <c>data</c> and module folders when they are missing, this never writes.</summary>
public sealed class ContentMountPlan
{
    public const string DefaultModule = "Arcanum";
    private const int MaxPatches = 10;

    public ContentMountPlan(string gameRoot, string moduleName, IEnumerable<ContentSource> searchOrder)
    {
        ArgumentNullException.ThrowIfNull(gameRoot);
        ArgumentNullException.ThrowIfNull(moduleName);
        ArgumentNullException.ThrowIfNull(searchOrder);
        GameRoot = gameRoot;
        ModuleName = moduleName;
        SearchOrder = Array.AsReadOnly(searchOrder.ToArray());
        if (SearchOrder.Any(source => source is null)) throw new ArgumentException("A source is null.", nameof(searchOrder));
        if (SearchOrder.Distinct(ReferenceEqualityComparer.Instance).Count() != SearchOrder.Count ||
            SearchOrder.Select(source => source.Path).Distinct(StringComparer.Ordinal).Count() != SearchOrder.Count)
            throw new ArgumentException("A source is mounted twice.", nameof(searchOrder));
    }

    public string GameRoot { get; }
    public string ModuleName { get; }
    public IReadOnlyList<ContentSource> SearchOrder { get; }

    /// <summary>Mirrors the pinned start-up sequence: <c>tig.dat</c>, the game folder,
    /// every <c>arcanum*.dat</c> in case-insensitive name order, the host's prototype cache when it has one
    /// (<see cref="PrototypeCacheMount"/>), <c>data</c>, then the module's
    /// <c>.dat</c>, its <c>.patch0</c>..<c>.patch9</c> (only when the <c>.dat</c> exists) and its
    /// folder. Each later mount sits above the earlier ones. Names resolve ASCII
    /// case-insensitively; two names differing only in case are refused as ambiguous.</summary>
    public static ContentMountDiscovery Discover(string gameRoot, string moduleName = DefaultModule)
    {
        ArgumentNullException.ThrowIfNull(gameRoot);
        ArgumentNullException.ThrowIfNull(moduleName);
        if (moduleName.Length == 0 || moduleName[0] == '.' || moduleName.Any(c => c is '/' or '\\' or ':' or '\0' or > '\x7e' or < ' '))
            return new(ContentMountStatus.InvalidModuleName, null, moduleName);
        try
        {
            if (!Directory.Exists(gameRoot)) return new(ContentMountStatus.MissingGameRoot, null, gameRoot);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
            var mounted = new List<ContentSource>();

            var tig = Child(root, "tig.dat");
            if (tig.Status == ChildStatus.Directory) return new(ContentMountStatus.UnsupportedLayout, null, tig.Path);
            if (tig.Status != ChildStatus.File) return Fail(tig, ContentMountStatus.MissingTig, "tig.dat");
            mounted.Add(new(ContentSourceRole.Tig, 0, tig.Path!));
            mounted.Add(new(ContentSourceRole.GameRoot, 0, root));

            var bases = new List<string>();
            foreach (var group in Directory.EnumerateFileSystemEntries(root)
                         .Where(path => IsBaseArchiveName(Path.GetFileName(path)))
                         .GroupBy(path => AsciiLower(Path.GetFileName(path)), StringComparer.Ordinal))
            {
                if (group.Count() != 1) return new(ContentMountStatus.AmbiguousName, null, group.Key);
                if (!File.Exists(group.Single())) return new(ContentMountStatus.UnsupportedLayout, null, group.Single());
                bases.Add(group.Single());
            }
            bases.Sort((a, b) => string.CompareOrdinal(AsciiLower(Path.GetFileName(a)), AsciiLower(Path.GetFileName(b))));
            mounted.AddRange(bases.Select((path, index) => new ContentSource(ContentSourceRole.BaseArchive, index, path)));

            // ARC-DEV-069: the prototype files built from the player's executable (when the host has any), under data/ so a real one wins.
            if (PrototypeCacheMount.Folder is { } cache && Directory.Exists(cache)) mounted.Add(new(ContentSourceRole.PrototypeCache, 0, Path.GetFullPath(cache)));

            var data = Child(root, "data");
            if (data.Status == ChildStatus.Ambiguous) return Fail(data, ContentMountStatus.AmbiguousName, "data");
            if (data.Status == ChildStatus.Directory) mounted.Add(new(ContentSourceRole.DataDirectory, 0, data.Path!));

            var modules = Child(root, "Modules");
            if (modules.Status == ChildStatus.Ambiguous) return Fail(modules, ContentMountStatus.AmbiguousName, "Modules");
            var moduleSources = 0;
            if (modules.Status == ChildStatus.Directory)
            {
                var dat = Child(modules.Path!, moduleName + ".dat");
                if (dat.Status == ChildStatus.Ambiguous) return Fail(dat, ContentMountStatus.AmbiguousName, moduleName + ".dat");
                if (dat.Status == ChildStatus.Directory) return new(ContentMountStatus.UnsupportedLayout, null, dat.Path);
                if (dat.Status == ChildStatus.File)
                {
                    mounted.Add(new(ContentSourceRole.ModuleArchive, 0, dat.Path!));
                    moduleSources++;
                    for (var number = 0; number < MaxPatches; number++)
                    {
                        var patch = Child(modules.Path!, $"{moduleName}.patch{number}");
                        if (patch.Status == ChildStatus.Ambiguous) return Fail(patch, ContentMountStatus.AmbiguousName, $"{moduleName}.patch{number}");
                        if (patch.Status == ChildStatus.Directory) return new(ContentMountStatus.UnsupportedLayout, null, patch.Path);
                        if (patch.Status == ChildStatus.File) mounted.Add(new(ContentSourceRole.ModulePatch, number, patch.Path!));
                    }
                }
                var folder = Child(modules.Path!, moduleName);
                if (folder.Status == ChildStatus.Ambiguous) return Fail(folder, ContentMountStatus.AmbiguousName, moduleName);
                if (folder.Status == ChildStatus.Directory)
                {
                    mounted.Add(new(ContentSourceRole.ModuleDirectory, 0, folder.Path!));
                    moduleSources++;
                }
            }
            if (moduleSources == 0) return new(ContentMountStatus.MissingModule, null, moduleName);

            mounted.Reverse();
            return new(ContentMountStatus.Ready, new ContentMountPlan(root, moduleName, mounted));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(ContentMountStatus.Unreadable, null, gameRoot);
        }
    }

    /// <summary>The pattern <c>arcanum*.dat</c>, matched ASCII case-insensitively.</summary>
    internal static bool IsBaseArchiveName(string name)
    {
        var lower = AsciiLower(name);
        return lower.Length >= "arcanum.dat".Length && lower.StartsWith("arcanum", StringComparison.Ordinal) && lower.EndsWith(".dat", StringComparison.Ordinal);
    }

    internal static string AsciiLower(string value)
        => string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++) span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        });

    private enum ChildStatus { Missing, File, Directory, Ambiguous }
    private readonly record struct ChildLookup(ChildStatus Status, string? Path);

    private static ContentMountDiscovery Fail(ChildLookup lookup, ContentMountStatus status, string name)
        => lookup.Status == ChildStatus.Ambiguous ? new(ContentMountStatus.AmbiguousName, null, name) : new(status, null, name);

    private static ChildLookup Child(string directory, string name)
    {
        var wanted = AsciiLower(name);
        var matches = Directory.EnumerateFileSystemEntries(directory)
            .Where(path => AsciiLower(Path.GetFileName(path)) == wanted).Take(2).ToArray();
        if (matches.Length == 0) return new(ChildStatus.Missing, null);
        if (matches.Length > 1) return new(ChildStatus.Ambiguous, null);
        return Directory.Exists(matches[0]) ? new(ChildStatus.Directory, matches[0]) : new(ChildStatus.File, matches[0]);
    }
}
