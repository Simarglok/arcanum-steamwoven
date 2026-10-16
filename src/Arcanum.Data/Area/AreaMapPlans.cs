using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Area;

public enum AreaMapPlanStatus { Ready, UnknownMap, NoSectors, MissingSource, UnsupportedSource }

/// <summary>A structural plan for one whole map, its inputs and loader limits sized for it.
/// <see cref="Open"/> transfers the inputs to a new <see cref="AreaDataOwner"/>.</summary>
public sealed class AreaMapPlan
{
    internal AreaMapPlan(WorldMap map, AreaSourcePlan plan, IReadOnlyList<(AreaInputKey Key, string Path)> files, AreaLoaderOptions limits)
    { Map = map; Plan = plan; Files = files; Limits = limits; }
    public WorldMap Map { get; }
    public AreaSourcePlan Plan { get; }
    public AreaLoaderOptions Limits { get; }
    internal IReadOnlyList<(AreaInputKey Key, string Path)> Files { get; }

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
    ];

    public static AreaMapPlanResult Create(ContentStack stack, WorldCatalog catalog, string mapName)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(mapName);
        var map = catalog.Find(mapName);
        if (map is null) return new(AreaMapPlanStatus.UnknownMap, null);
        if (map.SectorFiles.Count == 0) return new(AreaMapPlanStatus.NoSectors, null);

        var module = stack.Plan.SearchOrder.SingleOrDefault(s => s.Role == ContentSourceRole.ModuleArchive);
        var resource1 = BaseArchive(stack, "arcanum1.dat"); var resource2 = BaseArchive(stack, "arcanum2.dat");
        if (module is null || resource1 is null || resource2 is null) return new(AreaMapPlanStatus.MissingSource, null, "module archive, arcanum1.dat or arcanum2.dat");

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

        var (catalogueMap, runtimeMap) = CatalogueNumbers(stack, map.Name);
        var start = map.StartLocation is { } location && map.HasSectorFile(WorldSectorCoordinate.Containing(location))
            ? WorldSectorCoordinate.Containing(location) : map.SectorFiles[0];
        var plan = new AreaSourcePlan(catalogueMap, runtimeMap, new(start.X, start.Y), 0, map.Folder, identities, mappings)
        {
            Selection = AreaSectorSelection.AllSectors, AllowArtTrailingBytes = true,
        };
        return new(AreaMapPlanStatus.Ready, new(map, plan, files.AsReadOnly(), Limits(map.SectorFiles.Count, objects, mobiles.Objects.Count, aggregateBytes, prototypes.Count)));
    }

    /// <summary>Limits scaled from the pinned exterior profile by the map's size, so a small
    /// interior keeps the exterior's envelope and a large one grows with it.</summary>
    internal static AreaLoaderOptions Limits(int sectors, long objects, int mobiles, long aggregateBytes, int prototypes)
    {
        var baseline = new AreaLoaderOptions();
        var visuals = checked(sectors * 4096L + objects * 4 + 64);
        var factor = Math.Max(1.0, (double)visuals / baseline.MaxVisuals);
        long Scale(long value) => (long)Math.Min(long.MaxValue / 4, Math.Ceiling(value * factor));
        return new()
        {
            MaxManagedBytes = Scale(baseline.MaxManagedBytes), MaxRetainedBytes = Scale(baseline.MaxRetainedBytes), MaxBuildPhaseBytes = Scale(baseline.MaxBuildPhaseBytes),
            MaxFrameRequests = Scale(baseline.MaxFrameRequests), MaxFrameStoredBytes = Scale(baseline.MaxFrameStoredBytes), MaxFrameDecodedBytes = Scale(baseline.MaxFrameDecodedBytes),
            MaxFrameRgbaBytes = Scale(baseline.MaxFrameRgbaBytes), MaxFrameEncodedBytes = Scale(baseline.MaxFrameEncodedBytes),
            MaxSurvey = sectors, MaxPresentSectors = sectors, MaxAggregateRecords = Math.Max(1, mobiles), MaxAggregateBytes = (int)Math.Max(16, aggregateBytes),
            MaxPrototypes = Math.Max(1, prototypes), MaxObjects = (int)Math.Min(int.MaxValue, objects + 1), MaxVisuals = (int)Math.Min(int.MaxValue, visuals),
        };
    }

    /// <summary><c>rules/maplist.mes</c> numbers maps from 5000; the runtime map number is the
    /// catalogue number minus 4999 (pinned CE <c>map_list_info_load</c>). 0/0 when the map is not listed.</summary>
    internal static (int Catalogue, int Runtime) CatalogueNumbers(ContentStack stack, string mapName)
    {
        var winner = stack.Resolve("rules/maplist.mes").Winner;
        if (winner is null) return (0, 0);
        var read = stack.Read(winner);
        if (read.Status != ContentReadStatus.Read) return (0, 0);
        var parsed = MesFile.Parse(read.Bytes!);
        if (parsed.Status != MesParseStatus.Parsed) return (0, 0);
        foreach (var message in parsed.File!.Messages.Where(m => m.Number >= 5000))
        {
            var name = Encoding.Latin1.GetString(message.Bytes.Span).Split(',')[0].Trim();
            if (string.Equals(name, mapName, StringComparison.OrdinalIgnoreCase)) return (message.Number, message.Number - 4999);
        }
        return (0, 0);
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
