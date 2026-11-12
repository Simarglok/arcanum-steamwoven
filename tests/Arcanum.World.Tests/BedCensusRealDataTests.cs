using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-WORLD-1162 opt-in (<c>DAT_ROOT</c>, the game folder): the bed census of ledger SWB5 and SWB14. A bed is a scenery object (type 3) whose art id has art type 4 and sub-type 0x16 (bits 6 to 10). The test
/// reads every placed bed of every whole map the snapshot path opens, with the prototype it names and the script on attachment point 1 its effective fields hold (an instance's script array
/// overrides the prototype's); numbers only. Slice 2 adds the NPC rows whose standpoint is a bed's tile. The main world map (more sector files than a snapshot holds) is counted by the second test,
/// ARC-WORLD-1262: a sector-by-sector reader (one <c>ReadSector</c> per file, no window, no snapshot) that keeps only the bed rows and the NPC standpoints; the first test's numbers are checked against the same reader.
/// </summary>
[TestClass]
public sealed class BedCensusRealDataTests
{
    /// <summary>The pinned result of the installed 1.0.7.4 data (2026-11-12): placed beds, those with a use script, the maps read and the distinct prototypes the placed beds name.</summary>
    private const int PlacedBeds = 191, PlacedBedsWithScript = 19, MapsRead = 80, BedPrototypes = 1;
    /// <summary>ARC-WORLD-1162 (SWB21, same data): the NPC rows with a readable route (2236) whose night standpoint, whose day standpoint and whose either standpoint is the tile of a placed bed; the standpoint step seats such an NPC.</summary>
    private const int NpcRoutes = 2236, NpcNightOnBed = 41, NpcDayOnBed = 21, NpcEitherOnBed = 51;
    /// <summary>ARC-WORLD-1262 (same data): the main world map's census: the sector files read, placed beds (static and mobile), those with a use script, the distinct bed prototypes, the NPC rows with a readable route and those whose night, day, either standpoint is a bed's tile.</summary>
    private const int MainSectorFiles = 621, MainPlacedBeds = 275, MainScripted = 32, MainBedPrototypes = 2, MainNpcRoutes = 1556, MainNightOnBed = 145, MainDayOnBed = 23, MainEitherOnBed = 150, MainSectorsWithBeds = 72, MainTemplateSectors = 3999379, MainTemplateFiles = 208;

    [TestMethod, TestCategory("RealData")]
    public void Every_placed_bed_of_the_installed_maps_is_counted_with_its_use_script()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;

        // the placed beds
        var catalog = WorldCatalog.Open(stack);
        int maps = 0, skipped = 0, beds = 0, withScript = 0;
        var protoBeds = new SortedSet<int>();
        var scripts2 = new SortedDictionary<int, int>();
        int npcRoutes = 0, nightOnBed = 0, dayOnBed = 0, eitherOnBed = 0;
        var perMap = new SortedDictionary<string, (int Beds, int Scripted)>(StringComparer.Ordinal);
        foreach (var map in catalog.Maps)
        {
            if (map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles) { skipped++; Console.WriteLine($"ARC-WORLD-1162 not read: {map.Name} ({map.SectorFiles.Count} sector files)"); continue; }
            var created = AreaMapPlans.Create(stack, catalog, map.Name);
            if (created.Status != AreaMapPlanStatus.Ready) { skipped++; Console.WriteLine($"ARC-WORLD-1162 not read: {map.Name}: {created.Status}"); continue; }
            using var owner = created.Plan!.Open();
            var snapshot = owner.LoadStrict();
            maps++;
            var bedTiles = AreaBeds.Beds(snapshot.Objects).Where(b => b.WorldX is not null && b.WorldY is not null).Select(b => (b.WorldX!.Value, b.WorldY!.Value)).ToHashSet();
            foreach (var npc in snapshot.Objects.Where(o => o.Type == 16))
            {
                if (AreaNpcRouteReader.Read(npc) is not { Status: AreaNpcRouteStatus.Ready, Route: { } route }) continue;
                npcRoutes++;
                var night = bedTiles.Contains((route.StandpointNight.X, route.StandpointNight.Y)); var day = bedTiles.Contains((route.StandpointDay.X, route.StandpointDay.Y));
                if (night) nightOnBed++; if (day) dayOnBed++; if (night || day) eitherOnBed++;
            }
            foreach (var row in AreaBeds.Beds(snapshot.Objects))
            {
                beds++; protoBeds.Add(row.PrototypeNumber);
                var element = row.EffectiveFields.SingleOrDefault(f => f.Slot == 31)?.Elements.SingleOrDefault(e => e.Key == 1);
                var script = element is { Words.Count: 3 } ? (int)element.Words[2] : 0;
                if (script != 0) { withScript++; scripts2[script] = scripts2.GetValueOrDefault(script) + 1; }
                var counts = perMap.GetValueOrDefault(map.Name);
                perMap[map.Name] = (counts.Beds + 1, counts.Scripted + (script != 0 ? 1 : 0));
            }
        }
        Console.WriteLine($"ARC-WORLD-1162 npc standpoints on beds: npc-rows-with-route={npcRoutes} night-on-bed={nightOnBed} day-on-bed={dayOnBed} either={eitherOnBed}");
        Console.WriteLine($"ARC-WORLD-1162 bed prototypes of the placed beds: {string.Join(",", protoBeds)}");
        Console.WriteLine($"ARC-WORLD-1162 bed census maps={maps} not-read={skipped} placed-beds={beds} with-use-script={withScript} without={beds - withScript}");
        foreach (var pair in perMap.Where(p => p.Value.Scripted > 0)) Console.WriteLine($"ARC-WORLD-1162 scripted map={pair.Key} beds={pair.Value.Beds} scripted={pair.Value.Scripted}");
        foreach (var pair in scripts2) Console.WriteLine($"ARC-WORLD-1162 script {pair.Key} beds={pair.Value}");
        var content = ScriptContent.Over(stack);
        foreach (var number in scripts2.Keys)
        {
            var program = content.Script(number).Program;
            var ends = program?.Lines.SelectMany(line => new[] { line.OnTrue.Type, line.OnFalse.Type }).Where(type => type is 1 or 2).GroupBy(type => type).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine($"ARC-WORLD-1162 script {number} lines={program?.Count} skip-default-ends={ends?.GetValueOrDefault(1)} run-default-ends={ends?.GetValueOrDefault(2)}");
        }
        Assert.AreEqual((NpcRoutes, NpcNightOnBed, NpcDayOnBed, NpcEitherOnBed), (npcRoutes, nightOnBed, dayOnBed, eitherOnBed), "the installed data's NPC standpoints on beds changed");
        Assert.AreEqual((MapsRead, PlacedBeds, PlacedBedsWithScript, BedPrototypes), (maps, beds, withScript, protoBeds.Count), "the installed data's bed census changed");
    }

    [TestMethod, TestCategory("RealData")]
    public void The_sector_by_sector_reader_reproduces_the_snapshot_census_and_counts_the_main_map()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);

        // the reader is validated on the 80 maps the snapshot path opens: it must give that census's numbers
        var whole = new MapCensus();
        var mains = new List<WorldMap>();
        foreach (var map in catalog.Maps)
        {
            if (map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles) { mains.Add(map); continue; }
            if (AreaMapPlans.Create(stack, catalog, map.Name).Status != AreaMapPlanStatus.Ready) continue;
            whole.Add(SectorBedReader.Read(catalog, map));
        }
        Console.WriteLine($"ARC-WORLD-1262 reader on the 80 maps: maps={whole.Maps} placed-beds={whole.Beds} with-use-script={whole.Scripted} npc-rows-with-route={whole.NpcRoutes} night-on-bed={whole.NightOnBed} day-on-bed={whole.DayOnBed} either={whole.EitherOnBed}");
        Assert.AreEqual((MapsRead, PlacedBeds, PlacedBedsWithScript, BedPrototypes), (whole.Maps, whole.Beds, whole.Scripted, whole.Prototypes.Count), "the sector reader disagrees with the snapshot census");
        Assert.AreEqual((NpcRoutes, NpcNightOnBed, NpcDayOnBed, NpcEitherOnBed), (whole.NpcRoutes, whole.NightOnBed, whole.DayOnBed, whole.EitherOnBed), "the sector reader disagrees with the snapshot census (NPC rows)");

        // the main world map(s): more sector files than a snapshot holds
        var main = mains.Single();
        var census = SectorBedReader.Read(catalog, main, templates: true);
        Console.WriteLine($"ARC-WORLD-1262 main map {main.Name}: sector-files={census.Sectors} placed-beds={census.Beds} (static {census.StaticBeds}, mobile {census.Beds - census.StaticBeds}) with-use-script={census.Scripted} prototypes={string.Join(",", census.Prototypes)}");
        Console.WriteLine($"ARC-WORLD-1262 main map npc-rows-with-route={census.NpcRoutes} night-on-bed={census.NightOnBed} day-on-bed={census.DayOnBed} either={census.EitherOnBed} refused-sectors={census.Refused}");
        foreach (var pair in census.PerSector.Where(p => p.Value.Scripted > 0)) Console.WriteLine($"ARC-WORLD-1262 scripted sector={pair.Key} beds={pair.Value.Beds} scripted={pair.Value.Scripted}");
        foreach (var pair in census.Scripts) Console.WriteLine($"ARC-WORLD-1262 script {pair.Key} beds={pair.Value}");
        var content = ScriptContent.Over(stack);
        foreach (var number in census.Scripts.Keys)
        {
            var program = content.Script(number).Program;
            var ends = program?.Lines.SelectMany(line => new[] { line.OnTrue.Type, line.OnFalse.Type }).Where(type => type is 1 or 2).GroupBy(type => type).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine($"ARC-WORLD-1262 script {number} lines={program?.Count} skip-default-ends={ends?.GetValueOrDefault(1)} run-default-ends={ends?.GetValueOrDefault(2)}");
        }
        Console.WriteLine($"ARC-WORLD-1262 main map sectors-with-beds={census.PerSector.Count} template-sectors={census.TemplateSectors} beds-in-template-sectors={census.TemplateBeds}");
        Assert.AreEqual(0, census.Refused, "a sector file was refused: the census would be short");
        // the template pass must have run, covered every sector without a file and found no bed (the docs say the generated sectors add none)
        Assert.IsTrue(census.GridRead, "the main map's terrain grid was not read");
        Assert.AreEqual((MainTemplateSectors, MainTemplateFiles, 0, 0), (census.TemplateSectors, census.TemplateFiles, census.TemplateBeds, census.TemplateUnreadable), "the terrain templates behind the generated sectors changed or hold a bed");
        Assert.AreEqual(main.Properties.WidthTiles / 64 * (main.Properties.HeightTiles / 64), census.Sectors + census.TemplateSectors, "some sector is neither a file nor a template (a fill sector, say)");
        // the docs' claims: all beds static (mobile aggregate holds none), the NPC rows live in the mobile aggregate only, which beds and scripts, in how many sectors
        Assert.AreEqual((census.Beds, 0), (census.StaticBeds, census.StaticNpcs), "a mobile bed or a static NPC row: the census scope changed");
        CollectionAssert.AreEqual(new[] { 4038, 4045 }, census.Prototypes.ToArray(), "the bed prototypes changed");
        CollectionAssert.AreEqual(new Dictionary<int, int> { [1893] = 8, [1990] = 21, [2429] = 2, [2452] = 1 }.ToArray(), census.Scripts.ToArray(), "the use scripts of the beds changed");
        Assert.AreEqual(MainSectorsWithBeds, census.PerSector.Count, "the number of sectors with beds changed");
        Assert.AreEqual((MainSectorFiles, MainPlacedBeds, MainScripted, MainBedPrototypes), (census.Sectors, census.Beds, census.Scripted, census.Prototypes.Count), "the main map's bed census changed");
        Assert.AreEqual((MainNpcRoutes, MainNightOnBed, MainDayOnBed, MainEitherOnBed), (census.NpcRoutes, census.NightOnBed, census.DayOnBed, census.EitherOnBed), "the main map's NPC standpoints on beds changed");
    }

    /// <summary>What a census of one or more maps holds.</summary>
    private sealed class MapCensus
    {
        public int Maps, Sectors, Beds, StaticBeds, Scripted, NpcRoutes, NightOnBed, DayOnBed, EitherOnBed, Refused, TemplateSectors, TemplateBeds, TemplateFiles, TemplateUnreadable, StaticNpcs;
        public bool GridRead;
        public SortedSet<int> Prototypes { get; } = [];
        public SortedDictionary<int, int> Scripts { get; } = [];
        public SortedDictionary<string, (int Beds, int Scripted)> PerSector { get; } = new(StringComparer.Ordinal);

        public void Add(MapCensus one)
        {
            Maps++; Sectors += one.Sectors; Beds += one.Beds; StaticBeds += one.StaticBeds; Scripted += one.Scripted; NpcRoutes += one.NpcRoutes; NightOnBed += one.NightOnBed; DayOnBed += one.DayOnBed;
            EitherOnBed += one.EitherOnBed; Refused += one.Refused; TemplateSectors += one.TemplateSectors; TemplateBeds += one.TemplateBeds; TemplateFiles += one.TemplateFiles; TemplateUnreadable += one.TemplateUnreadable; StaticNpcs += one.StaticNpcs;
            Prototypes.UnionWith(one.Prototypes);
            foreach (var pair in one.Scripts) Scripts[pair.Key] = Scripts.GetValueOrDefault(pair.Key) + pair.Value;
        }
    }

    /// <summary>
    /// ARC-WORLD-1262: the sector-by-sector bed reader. It reads each sector file of a map once (<see cref="WorldCatalog.ReadSector"/>, no window, no plan, no authentication) and the map's mobile aggregate once, keeps only
    /// the beds (scenery with a bed art id: the instance's art field, else its prototype's) and the NPC rows' standpoints, and forgets the rest, so the 621 files of the main map cost one read each. Effective fields are
    /// the instance's value over the prototype's, as the area builder merges them; a static object's tile is its sector's origin plus its stored tile, a mobile object's is its stored location.
    /// </summary>
    private static class SectorBedReader
    {
        private const int ArtSlot = 0, LocationSlot = 1, ScriptsSlot = 31;

        public static MapCensus Read(WorldCatalog catalog, WorldMap map, bool templates = false)
        {
            var census = new MapCensus();
            var prototypes = new Dictionary<int, WorldObject?>();
            WorldFieldValue? Effective(WorldObject item, int slot)
            {
                if (item.Fields.TryGetValue(slot, out var own)) return own;
                if (item.PrototypeNumber is not { } number) return null;
                if (!prototypes.TryGetValue(number, out var prototype)) prototypes[number] = prototype = catalog.ReadPrototype(number).Prototype;
                return prototype is not null && prototype.Fields.TryGetValue(slot, out var held) ? held : null;
            }
            bool Bed(WorldObject item) => item.Type == 3 && Effective(item, ArtSlot) is { Presence: WorldFieldPresence.Value, Number: { } art } && AreaBeds.IsBedArt(unchecked((uint)art));
            var bedTiles = new HashSet<(long X, long Y)>();
            void Count(WorldObject item, long x, long y, string where, bool isStatic)
            {
                census.Beds++; if (isStatic) census.StaticBeds++;
                census.Prototypes.Add(item.PrototypeNumber ?? -1); bedTiles.Add((x, y));
                var element = Effective(item, ScriptsSlot) is { Presence: WorldFieldPresence.Array } scripts ? scripts.Elements.SingleOrDefault(e => e.Key == 1) : null;
                var script = element is { Words.Count: 3 } ? (int)element.Words[2] : 0;
                if (script != 0) { census.Scripted++; census.Scripts[script] = census.Scripts.GetValueOrDefault(script) + 1; }
                var counts = census.PerSector.GetValueOrDefault(where);
                census.PerSector[where] = (counts.Beds + 1, counts.Scripted + (script != 0 ? 1 : 0));
            }

            foreach (var coordinate in map.SectorFiles)
            {
                var read = catalog.ReadSector(map, coordinate);
                if (read.Sector is not { } sector || sector.ObjectIssue is not null) { census.Refused++; continue; }
                census.Sectors++;
                census.StaticNpcs += sector.Objects.Count(o => o.Type == AreaNpcRouteReader.NpcType);
                foreach (var item in sector.Objects)
                {
                    if (!Bed(item) || Effective(item, LocationSlot) is not { Presence: WorldFieldPresence.Value, Number: { } packed }) continue;
                    var location = WorldLocation.FromPacked(packed);
                    Count(item, ((long)sector.Coordinate.X << 6) + (location.X & 63), ((long)sector.Coordinate.Y << 6) + (location.Y & 63), $"{sector.Coordinate.X},{sector.Coordinate.Y}", isStatic: true);
                }
            }
            // the sectors without a file are generated from a terrain template (ledger SS5): its beds are counted apart, once per template file, and per sector that uses it
            if (templates && catalog.ReadTerrain(map).Grid is { } grid)
            {
                census.GridRead = true;
                var perTemplate = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var y = 0; y < map.Properties.HeightTiles / 64; y++)
                    for (var x = 0; x < map.Properties.WidthTiles / 64; x++)
                    {
                        var source = catalog.ResolveSector(map, new(x, y), grid);
                        if (source.Kind != WorldSectorSourceKind.Template) continue;
                        census.TemplateSectors++;
                        if (!perTemplate.TryGetValue(source.Path!, out var count))
                            perTemplate[source.Path!] = count = catalog.ReadResolvedSector(map, source).Sector?.Objects.Count(Bed) ?? -1;
                        if (count < 0) census.TemplateUnreadable++; else census.TemplateBeds += count;
                    }
                census.TemplateFiles = perTemplate.Count;
                Console.WriteLine($"ARC-WORLD-1262 templates used={perTemplate.Count} with-beds={perTemplate.Values.Count(c => c != 0)}");
            }
            var mobiles = catalog.ReadMobiles(map);
            foreach (var item in mobiles.Objects)
                if (Bed(item) && Effective(item, LocationSlot) is { Presence: WorldFieldPresence.Value, Number: { } packed })
                { var location = WorldLocation.FromPacked(packed); Count(item, location.X, location.Y, "mobile", isStatic: false); }
            foreach (var item in mobiles.Objects.Where(o => o.Type == AreaNpcRouteReader.NpcType))
            {
                if (AreaNpcRouteReader.Read(Row(item, Effective)) is not { Status: AreaNpcRouteStatus.Ready, Route: { } route }) continue;
                census.NpcRoutes++;
                var night = bedTiles.Contains((route.StandpointNight.X, route.StandpointNight.Y)); var day = bedTiles.Contains((route.StandpointDay.X, route.StandpointDay.Y));
                if (night) census.NightOnBed++; if (day) census.DayOnBed++; if (night || day) census.EitherOnBed++;
            }
            return census;
        }

        // The NPC route reader takes an object row; only the slots it reads are filled (effective: the instance's value, else the prototype's).
        private static AreaObjectRecord Row(WorldObject item, Func<WorldObject, int, WorldFieldValue?> effective)
        {
            var fields = new List<AreaFieldValue>();
            foreach (var slot in new[] { 61, 69, 76, 77, 78, 79 })
                if (effective(item, slot) is { } value)
                    fields.Add(new(slot, (AreaPresence)((int)value.Presence + 1), item.Fields.ContainsKey(slot) ? AreaValueOrigin.Instance : AreaValueOrigin.Prototype, value.Number,
                        value.Elements.Select(e => new AreaSparseElement(e.Key, e.Words)).ToArray(), value.Fingerprint));
            return new(default, 119, item.Type, item.PrototypeNumber ?? 0, "", "", "", "", Arcanum.Data.Area.AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
        }
    }
}
