using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-069 research only: a census of every light the installed module can show (docs/research/ledger/world.md, rows LT1-LT15):
// the stored sector lights of every map's own sector files and of the terrain templates the main map's generated sectors
// use (weighted by how many sectors use each), the object lights (primary and four overlay slots) of every sector object and
// map mobile with their prototype's defaults, the prototypes' light fields, the carried-light size flags of items and
// critters, and the light ART of art/light/light.mes (frame counts and rates). Aggregates only: counts, flag and ART
// numbers; never names, text or payload bytes.
internal static partial class Program
{
    // Project field slots (the executable's field index - 1): light flags, ART id and colour, then the overlay arrays;
    // object flags; the first type-specific slot (scenery, item and critter flags).
    private const int LcLightFlags = 12, LcLightArtSlot = 13, LcOverlayFlags = 15, LcOverlayArt = 16, LcObjectFlags = 18, LcTypeFlags = 36;
    private const int LcOverlaySlots = 4;
    private const uint LcObjectOff = 0x2, LcInventory = 0x1000, LcDontLight = 0x100000;
    private const int LcScenery = 3, LcPc = 15, LcNpc = 16;
    private const uint LcNocturnal = 0x4;
    // Item light sizes (item flags bits 9..12, as the carried-light rule reads them) and critter flags OCF_LIGHT_SMALL..XLARGE.
    private static readonly (uint Bit, string Name)[] LcItemSizes = [(0x200, "small"), (0x400, "medium"), (0x800, "large"), (0x1000, "xlarge")];
    private static readonly (uint Bit, string Name)[] LcCritterSizes = [(0x80000, "small"), (0x100000, "medium"), (0x200000, "large"), (0x400000, "xlarge")];

    private static int ProbeLightCensus(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || (args.Length == 4 && args[2] != "--module"))
            throw new ArgumentException("LIGHT_CENSUS_USAGE --light-census --game-root PATH [--module NAME]");
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException($"LIGHT_CENSUS_MOUNT_{discovery.Status}");
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException($"LIGHT_CENSUS_STACK_{opened.Status}");
        using var stack = opened.Stack!;
        Console.WriteLine(JsonSerializer.Serialize(LightCensus(stack), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>A stored light record's facts: its flags, ART number (bits 19..27 of a type-9 ART id), frame and whether an
    /// owner word is set (record words 0 and 1).</summary>
    internal readonly record struct LcRecord(uint Flags, int ArtType, int ArtNumber, int Frame, bool Owned);

    internal static LcRecord LcDecode(IReadOnlyList<int> words)
    {
        if (words.Count != 12) throw new InvalidDataException("LIGHT_CENSUS_RECORD");
        var art = (uint)words[7];
        return new((uint)words[6], (int)(art >> 28), (int)(art >> 19 & 0x1FF), (int)(art >> 12 & 0x7F), words[0] != 0 || words[1] != 0);
    }

    /// <summary>The light ART number of an object field value (a type-9 ART id), or null for none (-1) or another type.</summary>
    internal static int? LcArtNumber(long? value) => value is not { } v || (uint)v == uint.MaxValue || (uint)v >> 28 != 9 ? null : (int)((uint)v >> 19 & 0x1FF);

    internal static IEnumerable<string> LcBits(uint flags)
    {
        for (var bit = 0; bit < 32; bit++)
            if ((flags & 1u << bit) != 0) yield return "0x" + (1u << bit).ToString("x", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static object LightCensus(ContentStack stack)
    {
        var catalog = WorldCatalog.Open(stack);
        var animated = LcLightArtTable(stack, out var artTable);
        var prototypes = new Dictionary<int, WorldObject>();
        var protoLights = new CensusTally();
        foreach (var number in catalog.PrototypeNumbers.Order())
            if (catalog.ReadPrototype(number).Prototype is { } prototype)
            {
                prototypes[number] = prototype;
                LcObject(prototype, null, protoLights, animated, "prototype");
            }

        var stored = new CensusTally(); var objects = new CensusTally(); var perMap = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var maps = 0; var sectors = 0; var refusedSectors = 0;
        foreach (var map in catalog.Maps)
        {
            maps++;
            long mapLights = 0;
            foreach (var coordinate in map.SectorFiles)
            {
                var read = catalog.ReadSector(map, coordinate);
                if (read.Sector is not { } sector) { refusedSectors++; continue; }
                sectors++;
                foreach (var record in sector.Lights) { LcStored(LcDecode(record), stored, animated, "own", 1); mapLights++; }
                foreach (var o in sector.Objects) mapLights += LcObject(o, Prototype(o), objects, animated, "sector");
            }
            var mobiles = catalog.ReadMobiles(map);
            foreach (var o in mobiles.Objects) mapLights += LcObject(o, Prototype(o), objects, animated, "mobile");
            if (mapLights > 0) perMap[map.Name] = mapLights;
        }

        // The main map's generated sectors: each template file once, weighted by the sectors that use it.
        var templates = new CensusTally(); var templateFiles = 0; long generated = 0;
        if (MapList.Read(stack).ByRuntime(1) is { } start && catalog.Find(start.Name) is { } main && catalog.ReadTerrain(main).Grid is { } grid)
        {
            var uses = new Dictionary<string, (WorldSectorSource Source, long Count)>(StringComparer.Ordinal);
            for (var y = 0; y < grid.Header.HeightSectors; y++)
                for (var x = 0; x < grid.Header.WidthSectors; x++)
                {
                    var source = catalog.ResolveSector(main, new(x, y), grid);
                    if (source.Kind != WorldSectorSourceKind.Template || source.Path is not { } path) continue;
                    generated++;
                    uses[path] = uses.TryGetValue(path, out var seen) ? (seen.Source, seen.Count + 1) : (source, 1);
                }
            foreach (var (path, use) in uses.OrderBy(u => u.Key, StringComparer.Ordinal))
            {
                if (catalog.ReadResolvedSector(main, use.Source).Sector is not { } sector) { templates.Add("refused"); continue; }
                templateFiles++;
                if (sector.Lights.Count > 0) templates.Add("files_with_lights");
                foreach (var record in sector.Lights) LcStored(LcDecode(record), templates, animated, "template", use.Count);
                foreach (var o in sector.Objects) LcObject(o, Prototype(o), templates, animated, "template_object", use.Count);
            }
        }

        return new
        {
            light_art = new { entries = artTable.Count, animated = animated.Count, frames = animated.Values.GroupBy(a => a.Frames).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Count()), rates = animated.Values.GroupBy(a => a.Fps).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Count()) },
            maps, sector_files = sectors, refused_sector_files = refusedSectors,
            stored_lights = stored.Snapshot(), object_lights = objects.Snapshot(), prototypes = protoLights.Snapshot(),
            maps_with_lights = perMap.Count, lights_per_map_top = perMap.OrderByDescending(m => m.Value).ThenBy(m => m.Key, StringComparer.Ordinal).Take(10).Select(m => m.Value).ToArray(),
            main_map = new { generated_sectors = generated, template_files = templateFiles, weighted = templates.Snapshot() },
        };

        WorldObject? Prototype(WorldObject o) => o.PrototypeNumber is { } p ? prototypes.GetValueOrDefault(p) : null;
    }

    private static void LcStored(LcRecord record, CensusTally tally, IReadOnlyDictionary<int, (int Frames, int Fps)> animated, string scope, long weight)
    {
        tally.Add($"{scope}.records", weight);
        if (record.Owned) tally.Add($"{scope}.owned", weight);
        if (record.ArtType != 9) tally.Add($"{scope}.art_not_light", weight);
        if (record.Frame != 0) tally.Add($"{scope}.frame_not_0", weight);
        if (animated.ContainsKey(record.ArtNumber)) tally.Add($"{scope}.animated_art", weight);
        foreach (var bit in LcBits(record.Flags)) tally.Add($"{scope}.flag.{bit}", weight);
    }

    /// <summary>Counts an object's effective lights (its own field, else its prototype's) and returns how many it has.</summary>
    private static long LcObject(WorldObject o, WorldObject? prototype, CensusTally tally, IReadOnlyDictionary<int, (int Frames, int Fps)> animated, string scope, long weight = 1)
    {
        WorldFieldValue? Field(int slot) => o.Fields.TryGetValue(slot, out var own) ? own : prototype?.Fields.GetValueOrDefault(slot);
        var objectFlags = (uint)(Field(LcObjectFlags)?.Number ?? 0);
        var typeFlags = (uint)(Field(LcTypeFlags)?.Number ?? 0);
        tally.Add($"{scope}.objects", weight);
        long lights = 0;
        void Light(string kind, int number, uint flags)
        {
            lights++;
            tally.Add($"{scope}.{kind}", weight);
            tally.Add($"{scope}.{kind}.type.{o.Type}", weight);
            if (animated.ContainsKey(number)) tally.Add($"{scope}.{kind}.animated_art", weight);
            foreach (var bit in LcBits(flags)) tally.Add($"{scope}.{kind}.flag.{bit}", weight);
            if ((objectFlags & LcObjectOff) != 0) tally.Add($"{scope}.{kind}.object_off", weight);
            if ((objectFlags & LcInventory) != 0) tally.Add($"{scope}.{kind}.in_inventory", weight);
            if (o.Type == LcScenery && (typeFlags & LcNocturnal) != 0) tally.Add($"{scope}.{kind}.nocturnal_scenery", weight);
        }
        if (LcArtNumber(Field(LcLightArtSlot)?.Number) is { } primary) Light("primary", primary, (uint)(Field(LcLightFlags)?.Number ?? 0));
        var overlayArt = Field(LcOverlayArt); var overlayFlags = Field(LcOverlayFlags);
        for (var slot = 0; slot < LcOverlaySlots; slot++)
        {
            var art = overlayArt?.Elements.FirstOrDefault(e => e.Key == slot)?.Words.FirstOrDefault();
            if (LcArtNumber(art) is not { } number) continue;
            Light("overlay", number, (uint)(overlayFlags?.Elements.FirstOrDefault(e => e.Key == slot)?.Words.FirstOrDefault() ?? 0));
        }
        if ((objectFlags & LcDontLight) != 0) tally.Add($"{scope}.dont_light_objects", weight);
        // One count per object: its largest size, items and critters apart.
        var (sizes, family) = o.Type is LcPc or LcNpc ? (LcCritterSizes, "critter") : o.Type is >= 5 and <= 14 ? (LcItemSizes, "item") : ([], "");
        if (sizes.LastOrDefault(s => (typeFlags & s.Bit) != 0) is { Name: { } largest }) tally.Add($"{scope}.light_size.{family}.{largest}", weight);
        return lights;
    }

    /// <summary>The light ART names (art/light/light.mes, base sources) and, of those whose ART reads, the animated ones
    /// (more than one frame) with their frame count and rate.</summary>
    private static IReadOnlyDictionary<int, (int Frames, int Fps)> LcLightArtTable(ContentStack stack, out IReadOnlyDictionary<int, string> table)
    {
        var names = new Dictionary<int, string>();
        var animated = new Dictionary<int, (int Frames, int Fps)>();
        table = names;
        if (ContentPhase.Candidate(stack, "art/light/light.mes", ContentLoadPhase.GameStart) is not { } candidate) return animated;
        var read = stack.Read(candidate);
        if (read.Status != ContentReadStatus.Read || MesFile.Parse(read.Bytes).File is not { } mes) return animated;
        foreach (var message in mes.Messages) names[message.Number] = Encoding.Latin1.GetString(message.Bytes.Span).Trim();
        foreach (var (number, name) in names)
        {
            if (name.Length == 0 || name.Any(c => c is '/' or '\\' or ':') || name.Contains("..", StringComparison.Ordinal)) continue;
            // A light ART is read when it is first drawn, from the whole stack.
        if (ContentPhase.Candidate(stack, "art/light/" + name.ToLowerInvariant() + ".art", ContentLoadPhase.ModuleLoaded) is not { } art) continue;
            var bytes = stack.Read(art);
            if (bytes.Status != ContentReadStatus.Read) continue;
            try
            {
                var header = ArtDecoder.Inspect(bytes.Bytes).Header;
                if (header.FramesPerGroup > 1) animated[number] = (header.FramesPerGroup, header.FramesPerSecond);
            }
            catch (ArtException) { }
        }
        return animated;
    }

    private static void LightCensusSelfTest()
    {
        static void Require(bool value, string what) { if (!value) throw new InvalidOperationException("light-census self-test failed: " + what); }
        // A type-9 ART id: light 3, frame 2.
        var art = unchecked((int)(9u << 28 | 3u << 19 | 2u << 12));
        var record = LcDecode([0, 0, 64, 0, 0, 0, 0x5, art, 0, 0, 0, 0]);
        Require(record is { Flags: 0x5, ArtType: 9, ArtNumber: 3, Frame: 2, Owned: false }, "record");
        Require(LcDecode([7, 0, 0, 0, 0, 0, 0, art, 0, 0, 0, 0]).Owned, "owner word");
        Require(LcArtNumber(-1) is null && LcArtNumber(uint.MaxValue) is null && LcArtNumber(art) == 3 && LcArtNumber(1 << 28) is null, "object ART");
        Require(string.Join(",", LcBits(0x8000002)) == "0x2,0x8000000", "bits");
        Require(LcItemSizes.Select(s => s.Bit).SequenceEqual(new uint[] { 1u << 9, 1u << 10, 1u << 11, 1u << 12 }), "item sizes");
        Console.WriteLine("light-census self-test: PASS (record words, ART number, object ART, flag bits, item sizes)");
    }
}
