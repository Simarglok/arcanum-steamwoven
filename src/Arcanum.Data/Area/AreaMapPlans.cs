using System.Security.Cryptography;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;

namespace Arcanum.Data.Area;

public enum AreaMapPlanStatus { Ready, UnknownMap, NoSectors, TooLarge, MissingSource, UnsupportedSource, Unreadable }

/// <summary>One input of a whole-map plan: a local file, or one member of a local archive
/// (<see cref="Member"/> is its entry ordinal), read into memory when the plan opens (ARC-DATA-018).</summary>
public sealed record AreaMapPlanInput(AreaInputKey Key, string Path, int? Member = null);

/// <summary>A structural plan for one whole map, its inputs and loader limits sized for it.
/// <see cref="Open"/> transfers the inputs to a new <see cref="AreaDataOwner"/>.</summary>
public sealed class AreaMapPlan
{
    internal AreaMapPlan(WorldMap map, AreaSourcePlan plan, IReadOnlyList<AreaMapPlanInput> files, AreaLoaderOptions limits, WorldLocation? startTile)
    { Map = map; Plan = plan; Files = files; Limits = limits; StartTile = startTile; }
    public WorldMap Map { get; }
    /// <summary>The map's start tile from <c>rules/maplist.mes</c> (where a new arrival lands), or
    /// null for an unlisted map. <c>startloc.txt</c> holds the same two numbers for every installed
    /// map and is not a tile of it, so it is not used.</summary>
    public WorldLocation? StartTile { get; }
    public AreaSourcePlan Plan { get; }
    public AreaLoaderOptions Limits { get; }
    /// <summary>Where every input the plan authenticates is read from.</summary>
    public IReadOnlyList<AreaMapPlanInput> Files { get; }

    /// <summary>Opens the inputs read-only and the owner over them; every stream closes if
    /// opening fails. The owner re-authenticates every input against <see cref="Plan"/>, so an
    /// archive member that changed since planning is refused.</summary>
    public AreaDataOwner Open()
    {
        var inputs = new List<AreaInput>();
        var archives = new Dictionary<string, DatArchive>(StringComparer.Ordinal);
        try
        {
            foreach (var file in Files)
                inputs.Add(file.Member is { } ordinal ? new AreaInput(file.Key, new MemoryStream(AreaMapPlans.ReadMember(archives, file.Path, ordinal), writable: false)) : AreaInput.FromFile(file.Key, file.Path));
            foreach (var archive in archives.Values) archive.Dispose();
            archives.Clear();
            return AreaDataOwner.Open(new(inputs), Plan, Limits);
        }
        catch (AreaException) { foreach (var archive in archives.Values) archive.Dispose(); foreach (var input in inputs) input.Stream.Dispose(); throw; }
        catch (Exception error) when (error is DatException or IOException or UnauthorizedAccessException)
        {
            // An archive holding a member input that cannot be read again since planning.
            foreach (var archive in archives.Values) archive.Dispose(); foreach (var input in inputs) input.Stream.Dispose();
            throw new AreaException(new(error is DatException ? AreaErrorCode.MalformedRecord : AreaErrorCode.MissingSource, AreaStage.Input));
        }
        catch { foreach (var archive in archives.Values) archive.Dispose(); foreach (var input in inputs) input.Stream.Dispose(); throw; }
    }
}

public sealed record AreaMapPlanResult(AreaMapPlanStatus Status, AreaMapPlan? Plan, string? Detail = null);

/// <summary>ARC-DATA-019: builds a structural <see cref="AreaSourcePlan"/> for any map of the
/// universal catalogue, so the existing area owner, step model and viewer can load it. Every
/// sector file of the map is core; input identities are hashed from the installation at plan
/// time (no pinned values); ART files with bytes after their last frame are read as the original
/// reads them. ARC-DATA-018: resources resolve in content-stack order: every stack source that
/// wins a name under <c>art/</c> is an input of the plan's <see cref="AreaSourcePlan.ResourceOrder"/>
/// (an archive, or each loose file such a folder wins), and
/// the mobile aggregate is read from whichever source wins it. ARC-DATA-026: prototypes are the
/// base-source files the original reads at game start (<see cref="ContentPrototypes"/>), and so are
/// the name tables and the mask file (ARC-DATA-027, <see cref="ContentLoadPhase.GameStart"/>). Sector and
/// editor mobile files must be the module archive's own.</summary>
public static class AreaMapPlans
{
    /// <summary>Largest map a whole-map plan loads at once (the largest interior has 79 sector
    /// files). The main map is generated and streamed, not loaded whole.</summary>
    public const int MaxSectorFiles = 256;
    /// <summary>Most loose files under <c>art/</c> a plan reads as resources (each is one input).</summary>
    public const int MaxLooseResources = 1024;
    /// <summary>The scale factor of the limits never exceeds this, so a big or malformed map
    /// fails with a budget error instead of exhausting memory.</summary>
    internal const double MaxLimitScale = 16;
    private const string ResourcePrefix = "art/";
    // Largest mobile aggregate read from an archive member (installed ones are under 2 MiB).
    private const long MaxMemberBytes = 64L * 1048576;

    private static readonly (AreaMappingRole Role, string Path)[] MappingPaths =
    [
        (AreaMappingRole.Tile, "art/tile/tilename.mes"),
        (AreaMappingRole.Facade, "art/facade/facadename.mes"),
        (AreaMappingRole.Scenery, "art/scenery/scenery.mes"),
        (AreaMappingRole.Container, "art/container/container.mes"),
        (AreaMappingRole.ItemGround, "art/item/item_ground.mes"),
        (AreaMappingRole.MonsterNames, "art/monster/monster.mes"),
        (AreaMappingRole.UniqueNpcNames, "art/unique_npc/unique_npc.mes"),
        (AreaMappingRole.MissingPolicy, "art/missing.dat"),
        (AreaMappingRole.WallStructure, "art/wall/structure.mes"),
        (AreaMappingRole.WallName, "art/wall/wallname.mes"),
        (AreaMappingRole.Portal, "art/portal/portal.mes"),
        (AreaMappingRole.Roof, "art/roof/roofname.mes"),
        (AreaMappingRole.RoofShade, "art/roof/roofshade.mes"),
    ];

    public static AreaMapPlanResult Create(ContentStack stack, WorldCatalog catalog, string mapName)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(mapName);
        var map = catalog.Find(mapName);
        if (map is null) return new(AreaMapPlanStatus.UnknownMap, null);
        if (map.SectorFiles.Count == 0) return new(AreaMapPlanStatus.NoSectors, null);
        if (map.SectorFiles.Count > MaxSectorFiles) return new(AreaMapPlanStatus.TooLarge, null, $"{map.SectorFiles.Count} sector files");
        try { return CreateChecked(stack, catalog, map); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DatException or InvalidOperationException)
        { return new(AreaMapPlanStatus.Unreadable, null, error.GetType().Name); }
    }

    private static AreaMapPlanResult CreateChecked(ContentStack stack, WorldCatalog catalog, WorldMap map)
    {
        var module = stack.Plan.SearchOrder.SingleOrDefault(s => s.Role == ContentSourceRole.ModuleArchive);
        if (module is null) return new(AreaMapPlanStatus.MissingSource, null, "module archive");
        // The owner reads sector and editor mobile files from the module archive; one overridden
        // elsewhere in the stack would make the plan describe different objects.
        foreach (var key in stack.Keys(map.Folder + "/").Where(k => k.EndsWith(".sec", StringComparison.Ordinal) || k.EndsWith(".mob", StringComparison.Ordinal)))
            if (stack.Resolve(key).Winner?.Source != module)
                return new(AreaMapPlanStatus.UnsupportedSource, null, "a sector or editor mobile file is overridden outside the module archive");

        // Prototypes and object counts come from the catalogue's own reads of the map.
        var prototypes = new SortedSet<int>(); long objects = 0;
        foreach (var coordinate in map.SectorFiles)
        {
            var sector = catalog.ReadSector(map, coordinate).Sector;
            if (sector is null) continue;
            objects += sector.Objects.Count;
            foreach (var item in sector.Objects) if (item.PrototypeNumber is { } number) prototypes.Add(number);
        }
        var mobiles = catalog.ReadMobiles(map);
        objects += mobiles.Objects.Count;
        foreach (var item in mobiles.Objects) if (item.PrototypeNumber is { } number) prototypes.Add(number);

        // Resource sources in search order: each archive that wins a name under art/, and each
        // loose file a folder wins there. Every resource name's winner is then a listed source.
        // A loose name that is no valid virtual path (a backslash in a file name, say) cannot be
        // resolved, so nothing reads it; it is left out.
        var winners = stack.Keys(ResourcePrefix).Where(AreaSourcePlan.LoosePath).Select(key => (Key: key, Winner: stack.Resolve(key).Winner))
            .Where(w => w.Winner is not null).Select(w => (w.Key, Winner: w.Winner!)).ToArray();
        var keys = new Dictionary<ContentSource, AreaInputKey> { [module] = new(AreaSourceRole.Module) };
        var order = new List<AreaResourceSource>();
        var files = new List<AreaMapPlanInput> { new(new(AreaSourceRole.Module), module.Path) };
        var identities = new List<AreaInputIdentity>();
        var loose = 0;
        for (var position = 0; position < stack.Plan.SearchOrder.Count; position++)
        {
            var source = stack.Plan.SearchOrder[position];
            var held = winners.Where(w => w.Winner.Source == source).ToArray();
            if (held.Length == 0) continue;
            if (source.Kind == ContentSourceKind.Archive)
            {
                if (!keys.TryGetValue(source, out var key))
                {
                    key = ResourceKey(stack, source, position);
                    keys.Add(source, key); files.Add(new(key, source.Path));
                }
                order.Add(new(key));
                continue;
            }
            foreach (var (name, winner) in held)
            {
                if (++loose > MaxLooseResources) return new(AreaMapPlanStatus.TooLarge, null, $"more than {MaxLooseResources} loose resources");
                if (winner.Length > 16L * 1048576) return new(AreaMapPlanStatus.TooLarge, null, $"loose resource {name}");
                var key = new AreaInputKey(AreaSourceRole.Loose, loose);
                order.Add(new(key, name)); files.Add(new(key, winner.FilePath!));
            }
        }

        identities.AddRange(files.Select(f => Identity(f.Key, f.Path)));
        long aggregateBytes = 16;
        if (mobiles.Source == WorldMobileSource.Aggregate && map.AggregatePath is { } aggregatePath)
        {
            var winner = stack.Resolve(aggregatePath).Winner;
            if (winner is null) return new(AreaMapPlanStatus.MissingSource, null, "the mobile aggregate");
            if (Member(stack, new(AreaSourceRole.NormalGameMobile), winner, files, identities, MaxMemberBytes, "the mobile aggregate") is { } failure) return failure;
            aggregateBytes = winner.Length;
        }
        // Prototypes are the files the original reads at game start, from the base sources only (ARC-DATA-026).
        foreach (var number in prototypes)
        {
            var winner = catalog.Prototypes.Candidate(number);
            if (winner is null)
                return new(AreaMapPlanStatus.MissingSource, null, catalog.Prototypes.IsAmbiguous(number) ? $"prototype {number} is held by two files" : $"prototype {number}");
            if (Member(stack, new(AreaSourceRole.Prototype, number), winner, files, identities, 1048576, $"prototype {number}") is { } failure) return failure;
        }

        var mappings = new List<AreaMappingBinding>();
        // ARC-DATA-027: the name tables and the mask file are read by the original's Name init, before the module is
        // mounted (exe-checked), so each comes from the first base source. Its source must be one of the plan's inputs:
        // an archive of the resource order, or the loose file the order lists for that name.
        foreach (var (role, path) in MappingPaths)
        {
            var winner = ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart);
            if (winner is null)
            {
                // Without a mask file the original rebuilds the masks (N3), as the owner does. Without the two roof
                // tables no roof is read (ARC-RENDER-021), as before them; their read phase is assumed, not exe-checked.
                if (role is AreaMappingRole.MissingPolicy or AreaMappingRole.Roof or AreaMappingRole.RoofShade) continue;
                return new(AreaMapPlanStatus.MissingSource, null, path);
            }
            AreaInputKey key;
            if (winner.Entry is not null) { if (!keys.TryGetValue(winner.Source, out key)) return new(AreaMapPlanStatus.UnsupportedSource, null, $"{path} is read from a source outside the resource order"); }
            else if (order.SingleOrDefault(s => s.Path == path) is { } listed && files.Single(f => f.Key == listed.Key).Path == winner.FilePath) key = listed.Key;
            else return new(AreaMapPlanStatus.UnsupportedSource, null, $"{path} is read from a source outside the resource order");
            var read = stack.Read(winner);
            if (read.Status != ContentReadStatus.Read) return new(AreaMapPlanStatus.Unreadable, null, path);
            var entry = winner.Entry is { } member
                ? new AreaEntryIdentity(key, member.Ordinal, member.Flags, member.StoredSize, member.DeclaredOutputSize, member.Offset, Hash(member.RawNameBytes.ToArray()))
                : AreaDataOwner.LooseIdentity(key, path, winner.Length);
            mappings.Add(new(role, entry, Hash(read.Bytes!)));
        }

        var (catalogueMap, runtimeMap, startTile) = CatalogueEntry(stack, map.Name);
        var start = startTile is { } tile && map.HasSectorFile(WorldSectorCoordinate.Containing(tile))
            ? WorldSectorCoordinate.Containing(tile) : map.SectorFiles[0];
        var plan = new AreaSourcePlan(catalogueMap, runtimeMap, new(start.X, start.Y), 0, map.Folder, identities, mappings)
        {
            Selection = AreaSectorSelection.AllSectors, AllowArtTrailingBytes = true, ResourceOrder = order,
        };
        var resources = identities.Count(i => i.Key.Role is AreaSourceRole.Archive or AreaSourceRole.Loose);
        return new(AreaMapPlanStatus.Ready, new(map, plan, files.AsReadOnly(), Limits(map.SectorFiles.Count, objects, mobiles.Objects.Count, aggregateBytes, prototypes.Count, resources), startTile));
    }

    /// <summary>The input key of a resource archive: the two base archives keep their historical
    /// roles (<c>arcanum1.dat</c> Resource1, <c>arcanum2.dat</c> Resource2, which the viewer also reads
    /// outside the owner); any other archive is <see cref="AreaSourceRole.Archive"/> numbered by its
    /// search-order position. The key names the input; resolution follows the order alone.</summary>
    private static AreaInputKey ResourceKey(ContentStack stack, ContentSource source, int position)
    {
        if (source.Role == ContentSourceRole.BaseArchive)
            foreach (var (name, role) in new[] { ("arcanum1.dat", AreaSourceRole.Resource1), ("arcanum2.dat", AreaSourceRole.Resource2) })
                if (string.Equals(Path.GetFileName(source.Path), name, StringComparison.OrdinalIgnoreCase) &&
                    stack.Plan.SearchOrder.Count(s => s.Role == ContentSourceRole.BaseArchive && string.Equals(Path.GetFileName(s.Path), name, StringComparison.OrdinalIgnoreCase)) == 1)
                    return new(role);
        return new(AreaSourceRole.Archive, position);
    }

    /// <summary>The input of a prototype or aggregate: a loose winner is opened as its file, an
    /// archive member (at most <paramref name="maxBytes"/>, as the owner admits) is read through the
    /// stack, hashed and opened from memory.</summary>
    private static AreaMapPlanResult? Member(ContentStack stack, AreaInputKey key, ContentCandidate winner, List<AreaMapPlanInput> files,
        List<AreaInputIdentity> identities, long maxBytes, string what)
    {
        if (winner.Entry is not { } entry) { files.Add(new(key, winner.FilePath!)); identities.Add(Identity(key, winner.FilePath!)); return null; }
        if (winner.Length > maxBytes) return new(AreaMapPlanStatus.TooLarge, null, what);
        var read = stack.Read(winner);
        if (read.Status != ContentReadStatus.Read) return new(AreaMapPlanStatus.Unreadable, null, what);
        files.Add(new(key, winner.Source.Path, entry.Ordinal));
        identities.Add(new(key, read.Bytes!.Length, Hash(read.Bytes)));
        return null;
    }

    /// <summary>One member of an archive, read for <see cref="AreaMapPlan.Open"/>; each archive is
    /// indexed once per open.</summary>
    internal static byte[] ReadMember(Dictionary<string, DatArchive> archives, string archivePath, int ordinal)
    {
        if (!archives.TryGetValue(archivePath, out var archive)) archives.Add(archivePath, archive = DatArchive.Open(archivePath));
        if (ordinal < 0 || ordinal >= archive.Entries.Count) throw new AreaException(new(AreaErrorCode.MissingSource, AreaStage.Input));
        return archive.ReadEntry(archive.Entries[ordinal]);
    }

    /// <summary>Limits from the pinned exterior profile (20 present sectors, 524 objects): memory
    /// budgets scaled by the map's sectors or objects, whichever grows more, capped at
    /// <see cref="MaxLimitScale"/>; frame budgets at that cap outright. Count limits follow the
    /// map exactly.</summary>
    internal static AreaLoaderOptions Limits(int sectors, long objects, int mobiles, long aggregateBytes, int prototypes, int resources = 0)
    {
        var baseline = new AreaLoaderOptions();
        var factor = Math.Clamp(Math.Max(sectors / 20.0, objects / 524.0), 1.0, MaxLimitScale);
        long Scale(long value) => (long)Math.Ceiling(value * factor);
        var visuals = Math.Min(int.MaxValue, checked(sectors * (4096L + 256) + objects * 4 + 64));
        // Frame budgets count every decoded frame while the snapshot is built (one per distinct
        // selection on a whole map, ARC-DATA-024); interiors read larger ART per frame than the
        // exterior, so whole maps get the capped budget outright.
        long Frames(long value) => (long)Math.Ceiling(value * MaxLimitScale);
        return new()
        {
            MaxManagedBytes = Scale(baseline.MaxManagedBytes), MaxRetainedBytes = Scale(baseline.MaxRetainedBytes), MaxBuildPhaseBytes = Scale(baseline.MaxBuildPhaseBytes),
            // One frame operation may read an entry up to the whole-map entry cap (16 MiB).
            MaxFrameOperationBytes = checked(baseline.MaxFrameOperationBytes * (long)MaxLimitScale),
            MaxFrameRequests = Frames(baseline.MaxFrameRequests), MaxFrameStoredBytes = Frames(baseline.MaxFrameStoredBytes), MaxFrameDecodedBytes = Frames(baseline.MaxFrameDecodedBytes),
            MaxFrameRgbaBytes = Frames(baseline.MaxFrameRgbaBytes), MaxFrameEncodedBytes = Frames(baseline.MaxFrameEncodedBytes),
            MaxSurvey = sectors, MaxPresentSectors = sectors, MaxAggregateRecords = Math.Max(1, mobiles),
            MaxAggregateBytes = (int)Math.Clamp(aggregateBytes, 16, int.MaxValue),
            MaxPrototypes = Math.Max(1, prototypes), MaxResourceInputs = resources, MaxObjects = (int)Math.Clamp(objects + 1, 1, int.MaxValue), MaxVisuals = (int)visuals,
        };
    }

    /// <summary><c>rules/maplist.mes</c> numbers maps from 5000 without gaps, at most 200 (pinned
    /// CE <c>map_list_info_load</c> stops at the first missing number); the runtime map number is
    /// the catalogue number minus 4999. 0/0 when the map is not listed or the list does not parse.</summary>
    internal static (int Catalogue, int Runtime) CatalogueNumbers(ContentStack stack, string mapName)
    {
        var (catalogue, runtime, _) = CatalogueEntry(stack, mapName);
        return (catalogue, runtime);
    }

    /// <summary>The map list entry: catalogue and runtime numbers and the start tile (the entry's
    /// second and third values read like C strtoll; null when the entry has fewer values).</summary>
    internal static (int Catalogue, int Runtime, WorldLocation? Start) CatalogueEntry(ContentStack stack, string mapName) =>
        MapList.Read(stack).Find(mapName) is { } entry ? (entry.Catalogue, entry.Runtime, entry.Start) : (0, 0, null);

    private static AreaInputIdentity Identity(AreaInputKey key, string path)
    {
        using var stream = File.OpenRead(path);
        return new(key, stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
