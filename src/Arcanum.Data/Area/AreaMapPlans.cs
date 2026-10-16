using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Area;

public enum AreaMapPlanStatus { Ready, UnknownMap, NoSectors, TooLarge, MissingSource, UnsupportedSource, Unreadable }

/// <summary>A structural plan for one whole map, its inputs and loader limits sized for it.
/// <see cref="Open"/> transfers the inputs to a new <see cref="AreaDataOwner"/>.</summary>
public sealed class AreaMapPlan
{
    internal AreaMapPlan(WorldMap map, AreaSourcePlan plan, IReadOnlyList<(AreaInputKey Key, string Path)> files, AreaLoaderOptions limits, WorldLocation? startTile)
    { Map = map; Plan = plan; Files = files; Limits = limits; StartTile = startTile; }
    public WorldMap Map { get; }
    /// <summary>The map's start tile from <c>rules/maplist.mes</c> (where a new arrival lands), or
    /// null for an unlisted map. <c>startloc.txt</c> holds the same two numbers for every installed
    /// map and is not a tile of it, so it is not used.</summary>
    public WorldLocation? StartTile { get; }
    public AreaSourcePlan Plan { get; }
    public AreaLoaderOptions Limits { get; }
    /// <summary>The local file of every input the plan authenticates.</summary>
    public IReadOnlyList<(AreaInputKey Key, string Path)> Files { get; }

    /// <summary>Opens the inputs read-only and the owner over them; every stream closes if
    /// opening fails. The owner re-authenticates every input against <see cref="Plan"/>.</summary>
    public AreaDataOwner Open()
    {
        var inputs = new List<AreaInput>();
        try
        {
            foreach (var (key, path) in Files) inputs.Add(AreaInput.FromFile(key, path));
            return AreaDataOwner.Open(new(inputs), Plan, Limits);
        }
        catch (AreaException) { throw; }
        catch { foreach (var input in inputs) input.Stream.Dispose(); throw; }
    }
}

public sealed record AreaMapPlanResult(AreaMapPlanStatus Status, AreaMapPlan? Plan, string? Detail = null);

/// <summary>ARC-DATA-019: builds a structural <see cref="AreaSourcePlan"/> for any map of the
/// universal catalogue, so the existing area owner, step model and viewer can load it. Every
/// sector file of the map is core; input identities are hashed from the installation at plan
/// time (no pinned values); ART files with bytes after their last frame are read as the original
/// reads them. Limitation: resources resolve in the module archive and the first two base
/// archives (<c>arcanum1.dat</c>, <c>arcanum2.dat</c>) with their own uniqueness rule, not in full
/// stack order; overrides in later archives or loose folders are not seen (ARC-DATA-018).</summary>
public static class AreaMapPlans
{
    /// <summary>Largest map a whole-map plan loads at once (the largest interior has 79 sector
    /// files). The main map is generated and streamed, not loaded whole.</summary>
    public const int MaxSectorFiles = 256;
    /// <summary>The scale factor of the limits never exceeds this, so a big or malformed map
    /// fails with a budget error instead of exhausting memory.</summary>
    internal const double MaxLimitScale = 16;

    private static readonly (AreaMappingRole Role, int Resource, string Path)[] MappingPaths =
    [
        (AreaMappingRole.Tile, 2, "art/tile/tilename.mes"),
        (AreaMappingRole.Facade, 2, "art/facade/facadename.mes"),
        (AreaMappingRole.Scenery, 2, "art/scenery/scenery.mes"),
        (AreaMappingRole.Container, 2, "art/container/container.mes"),
        (AreaMappingRole.ItemGround, 2, "art/item/item_ground.mes"),
        (AreaMappingRole.MonsterNames, 1, "art/monster/monster.mes"),
        (AreaMappingRole.UniqueNpcNames, 1, "art/unique_npc/unique_npc.mes"),
        (AreaMappingRole.MissingPolicy, 1, "art/missing.dat"),
        (AreaMappingRole.WallStructure, 2, "art/wall/structure.mes"),
        (AreaMappingRole.WallName, 2, "art/wall/wallname.mes"),
        (AreaMappingRole.Portal, 2, "art/portal/portal.mes"),
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
        var resource1 = BaseArchive(stack, "arcanum1.dat"); var resource2 = BaseArchive(stack, "arcanum2.dat");
        if (module is null || resource1 is null || resource2 is null) return new(AreaMapPlanStatus.MissingSource, null, "module archive, arcanum1.dat or arcanum2.dat");
        // The owner reads sectors from the module archive; a sector overridden elsewhere in the
        // stack would make the plan's prototype set describe different objects.
        foreach (var coordinate in map.SectorFiles)
            if (stack.Resolve($"{map.Folder}/{coordinate.Id}.sec").Winner?.Source != module)
                return new(AreaMapPlanStatus.UnsupportedSource, null, "a sector file is overridden outside the module archive");

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

        var files = new List<(AreaInputKey Key, string Path)>
        {
            (new(AreaSourceRole.Module), module.Path), (new(AreaSourceRole.Resource1), resource1.Path), (new(AreaSourceRole.Resource2), resource2.Path),
        };
        long aggregateBytes = 16;
        if (mobiles.Source == WorldMobileSource.Aggregate && map.AggregatePath is { } aggregatePath)
        {
            var winner = stack.Resolve(aggregatePath).Winner;
            if (winner?.FilePath is not { } path) return new(AreaMapPlanStatus.UnsupportedSource, null, "the mobile aggregate is not a loose file");
            files.Add((new(AreaSourceRole.NormalGameMobile), path)); aggregateBytes = winner.Length;
        }
        foreach (var number in prototypes)
        {
            var path = catalog.PrototypePath(number);
            if (path is null) return new(AreaMapPlanStatus.MissingSource, null, $"prototype {number}");
            var winner = stack.Resolve(path).Winner;
            if (winner?.FilePath is not { } file) return new(AreaMapPlanStatus.UnsupportedSource, null, $"prototype {number} is not a loose file");
            files.Add((new(AreaSourceRole.Prototype, number), file));
        }

        var identities = files.Select(f => Identity(f.Key, f.Path)).ToArray();
        var mappings = new List<AreaMappingBinding>();
        using (var r1 = DatArchive.Open(resource1.Path))
        using (var r2 = DatArchive.Open(resource2.Path))
        {
            foreach (var (role, resource, path) in MappingPaths)
            {
                var archive = resource == 1 ? r1 : r2;
                var matches = archive.Entries.Where(e => Normalize(e) == path).ToArray();
                if (matches.Length != 1) return new(AreaMapPlanStatus.MissingSource, null, path);
                var entry = matches[0];
                var key = new AreaInputKey(resource == 1 ? AreaSourceRole.Resource1 : AreaSourceRole.Resource2);
                mappings.Add(new(role, new(key, entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset, Hash(entry.RawNameBytes.ToArray())),
                    Hash(archive.ReadEntry(entry))));
            }
        }

        var (catalogueMap, runtimeMap, startTile) = CatalogueEntry(stack, map.Name);
        var start = startTile is { } tile && map.HasSectorFile(WorldSectorCoordinate.Containing(tile))
            ? WorldSectorCoordinate.Containing(tile) : map.SectorFiles[0];
        var plan = new AreaSourcePlan(catalogueMap, runtimeMap, new(start.X, start.Y), 0, map.Folder, identities, mappings)
        {
            Selection = AreaSectorSelection.AllSectors, AllowArtTrailingBytes = true,
        };
        return new(AreaMapPlanStatus.Ready, new(map, plan, files.AsReadOnly(), Limits(map.SectorFiles.Count, objects, mobiles.Objects.Count, aggregateBytes, prototypes.Count), startTile));
    }

    /// <summary>Limits from the pinned exterior profile (20 present sectors, 524 objects): memory
    /// budgets scaled by the map's sectors or objects, whichever grows more, capped at
    /// <see cref="MaxLimitScale"/>; frame budgets at that cap outright. Count limits follow the
    /// map exactly.</summary>
    internal static AreaLoaderOptions Limits(int sectors, long objects, int mobiles, long aggregateBytes, int prototypes)
    {
        var baseline = new AreaLoaderOptions();
        var factor = Math.Clamp(Math.Max(sectors / 20.0, objects / 524.0), 1.0, MaxLimitScale);
        long Scale(long value) => (long)Math.Ceiling(value * factor);
        var visuals = Math.Min(int.MaxValue, checked(sectors * 4096L + objects * 4 + 64));
        // Frame budgets count every visual's ART read while the snapshot is built; interiors read
        // larger ART per visual than the exterior, so whole maps get the capped budget outright.
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
            MaxPrototypes = Math.Max(1, prototypes), MaxObjects = (int)Math.Clamp(objects + 1, 1, int.MaxValue), MaxVisuals = (int)visuals,
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
    internal static (int Catalogue, int Runtime, WorldLocation? Start) CatalogueEntry(ContentStack stack, string mapName)
    {
        var winner = stack.Resolve("rules/maplist.mes").Winner;
        if (winner is null) return (0, 0, null);
        var read = stack.Read(winner);
        if (read.Status != ContentReadStatus.Read) return (0, 0, null);
        var parsed = MesFile.Parse(read.Bytes!);
        if (parsed.Status != MesParseStatus.Parsed) return (0, 0, null);
        for (var number = 5000; number < 5200; number++)
        {
            if (parsed.File!.Find(number) is not { } message) break;
            var values = Encoding.Latin1.GetString(message.Bytes.Span).Split(',');
            if (!string.Equals(values[0].Trim(), mapName, StringComparison.OrdinalIgnoreCase)) continue;
            // Read like C strtoll (leading digits, else 0), as the original's value parser does; a
            // negative coordinate is not a tile, so it is treated as absent.
            WorldLocation? start = null;
            if (values.Length >= 3 && WorldCatalog.StrToLong(values[1]) is var x and >= 0 && WorldCatalog.StrToLong(values[2]) is var y and >= 0) start = new(x, y);
            return (number, number - 4999, start);
        }
        return (0, 0, null);
    }

    private static ContentSource? BaseArchive(ContentStack stack, string fileName) =>
        stack.Plan.SearchOrder.SingleOrDefault(s => s.Role == ContentSourceRole.BaseArchive && string.Equals(Path.GetFileName(s.Path), fileName, StringComparison.OrdinalIgnoreCase));

    private static AreaInputIdentity Identity(AreaInputKey key, string path)
    {
        using var stream = File.OpenRead(path);
        return new(key, stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static string Normalize(DatEntry entry) => Encoding.Latin1.GetString(entry.RawNameBytes.ToArray()).TrimEnd('\0').Replace('\\', '/').ToLowerInvariant();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
