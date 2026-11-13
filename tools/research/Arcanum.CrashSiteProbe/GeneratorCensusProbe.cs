using System.Text.Json;
using Arcanum.Data.Content;

namespace Arcanum.CrashSiteProbe;

/// <summary>
/// ARC-WORLD-1526 (ledger GS5, VBG15): the data question behind the monster generator copy. The original copies the whole generator object (0x4EF920), so a generator whose instance overrides fields of its
/// prototype (scripts, name, stats, faction, inventory ...) makes critters that carry them; Steamwoven makes a critter of the generator's prototype only. This census walks every installed map's mobile
/// objects (the .mob files, the sectors' object windows and the map aggregates), takes the NPCs whose effective NPC flags carry the generator bit 0x80000 and reports, by field slot, how many of them override the
/// slot and how many of those carry a value that differs from the prototype's. Numbers only: no text, no names, no paths of the player's files leave the probe.
/// </summary>
internal static partial class Program
{
    private const int GcNpcType = 16, GcNpcFlagsSlot = 69, GcGeneratorSlot = 90, GcGeneratorBit = 0x80000, GcHeldCountSlot = 55;
    // The slots that make an instance a generator or place it (location 1, object flags 18, NPC flags 69, generator dword 90) are not content a copy carries beyond the marks the original clears (0x4BA720).
    private static readonly HashSet<int> GcPlacementSlots = [1, 18, 69, 90];

    internal sealed record GcRow(string Kind, int Prototype, IReadOnlyList<int> Overrides, IReadOnlyList<int> Differs, string Own = "", int Held = 0);

    private static int ProbeGeneratorCensus(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] != "--game-root" || (args.Length == 4 && args[2] != "--module"))
            throw new ArgumentException("GENERATOR_CENSUS_USAGE --generator-census --game-root PATH [--module NAME]");
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException($"GENERATOR_CENSUS_MOUNT_{discovery.Status}");
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException($"GENERATOR_CENSUS_STACK_{opened.Status}");
        using var stack = opened.Stack!;
        var keys = CensusKeys(stack);
        var refusals = new CensusTally();
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|GENERATOR_CENSUS_READ_{result.Status}");
            return null;
        }

        var prototypes = new Dictionary<int, (PrototypeInfo Info, byte[] Bytes)>();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("proto/", StringComparison.Ordinal) && k.Key.EndsWith(".pro", StringComparison.Ordinal)))
        {
            if (Read(winner, "prototype") is not { } bytes) continue;
            try { var info = ObjectWire.ParsePrototype(bytes); prototypes[info.Id.AidValue()] = (info, bytes); }
            catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { refusals.Add("prototype|parse"); }
        }

        var rows = new List<GcRow>();
        var npcs = 0;
        void Objects(string kind, ReadOnlySpan<byte> window, IEnumerable<InstanceInfo> instances)
        {
            foreach (var instance in instances)
            {
                if (instance.Type != GcNpcType) continue;
                npcs++;
                if (instance.PrototypeId.Type != 1 || !prototypes.TryGetValue(instance.PrototypeId.AidValue(), out var proto)) { refusals.Add("npc|unresolved_prototype"); continue; }
                var row = GcClassify(kind, instance, window, proto.Info, proto.Bytes);
                if (row is not null) rows.Add(row);
            }
        }
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("maps/", StringComparison.Ordinal) && k.Key.Count(c => c == '/') == 2))
        {
            var file = key.Split('/')[2];
            if (file.EndsWith(".mob", StringComparison.Ordinal) && Read(winner, "mobile") is { } mob)
            {
                try { var instance = ObjectWire.ParseNext(mob, out _); Objects("mobile", mob, [instance]); }
                catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { refusals.Add("mobile|parse"); }
            }
            else if (file.EndsWith(".sec", StringComparison.Ordinal) && Read(winner, "sector") is { } sec && sec.Length <= CensusMaxSectorBytes)
            {
                try
                {
                    var slice = SectorSlice.Parse(sec, false); var framing = SectorFraming.Parse(sec, slice.LightCount);
                    var window = sec.AsSpan(framing.ObjectStart, framing.ObjectBytes);
                    Objects("sector", window, ObjectWire.ParseWindow(window, slice.TrailingInt32));
                }
                catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { refusals.Add("sector|parse"); }
            }
        }
        var mapNames = keys.Keys.Where(k => k.StartsWith("maps/", StringComparison.Ordinal) && k.Count(c => c == '/') == 2).Select(k => k.Split('/')[1]).Distinct().ToArray();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("maps/", StringComparison.Ordinal) && k.Key.Count(c => c == '/') == 1))
        {
            if (Read(winner, "aggregate") is not { } bytes) continue;
            try { foreach (var record in FrameAggregate(bytes, CensusMaxAggregateBytes)) Objects("aggregate", bytes.AsSpan(record.Offset, record.Length), [record.Instance]); }
            catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { refusals.Add("aggregate|parse"); }
        }
        Console.WriteLine(JsonSerializer.Serialize(GcReport(rows, npcs, mapNames.Length, refusals.Snapshot()), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>The row of an NPC instance whose effective NPC flags carry the generator bit, else null.</summary>
    private static GcRow? GcClassify(string kind, InstanceInfo instance, ReadOnlySpan<byte> window, PrototypeInfo proto, byte[] protoBytes)
    {
        var flags = instance.NumericFields.TryGetValue(GcNpcFlagsSlot, out var own) ? own : proto.NumericFields.GetValueOrDefault(GcNpcFlagsSlot);
        if ((flags & GcGeneratorBit) == 0) return null;
        var differs = new List<int>();
        foreach (var slot in instance.OverrideSlots)
        {
            if (instance.NumericFields.TryGetValue(slot, out var value)) { if (!proto.NumericFields.TryGetValue(slot, out var original) || original != value) differs.Add(slot); continue; }
            var mine = instance.Arrays.FirstOrDefault(a => a.Field == slot); var theirs = proto.Arrays.FirstOrDefault(a => a.Field == slot);
            if (mine is null && theirs is null) continue;
            if (mine is null || theirs is null || !window[mine.Start..mine.End].SequenceEqual(protoBytes.AsSpan(theirs.Start, theirs.End - theirs.Start))) differs.Add(slot);
        }
        return new(kind, proto.Id.AidValue(), instance.OverrideSlots.ToArray(), differs.ToArray(), $"{proto.Id.AidValue()}:{instance.Location}", instance.NumericFields.GetValueOrDefault(GcHeldCountSlot));
    }

    internal static object GcReport(IReadOnlyList<GcRow> sources, int npcs, int maps, IReadOnlyDictionary<string, long> refusals)
    {
        // A map object is read from its editor file and from the map's aggregate (the strict loader checks that they correspond): the counts below are of the distinct objects, the sources are counted apart.
        var rows = sources.DistinctBy(r => r.Own).ToArray();
        var content = rows.Select(r => (Row: r, Extra: r.Differs.Where(s => !GcPlacementSlots.Contains(s)).ToArray())).ToArray();
        return new
        {
            maps, npcs, generator_sources = sources.Count, generators = rows.Length,
            by_kind = sources.GroupBy(r => r.Kind).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            with_held_items = rows.Count(r => r.Held > 0), held_items_total = rows.Sum(r => r.Held), most_held_items = rows.Select(r => r.Held).DefaultIfEmpty(0).Max(),
            distinct_prototypes = rows.Select(r => r.Prototype).Distinct().Count(),
            override_slots = rows.SelectMany(r => r.Overrides).GroupBy(s => s).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            differing_slots = rows.SelectMany(r => r.Differs).GroupBy(s => s).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            generators_with_content_differences = content.Count(c => c.Extra.Length > 0),
            content_differing_slots = content.SelectMany(c => c.Extra).GroupBy(s => s).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            refusals = refusals,
        };
    }

    private static void GeneratorCensusSelfTest()
    {
        static void Check(bool condition, string what) { if (!condition) throw new InvalidOperationException($"generator-census self-test failed: {what}"); }
        var rows = new GcRow[] { new("mobile", 7, [1, 18, 69, 90], [1, 18, 90], "a"), new("aggregate", 7, [1, 69, 90, 21], [1, 21], "b"), new("sector", 9, [], [], "c"), new("aggregate", 7, [1, 69, 90, 21], [1, 21], "b") };
        var json = JsonSerializer.Serialize(GcReport(rows, 10, 2, new Dictionary<string, long>()));
        Check(json.Contains("\"generator_sources\":4,\"generators\":3,\"by_kind\":{\"aggregate\":2,\"mobile\":1,\"sector\":1}", StringComparison.Ordinal) && json.Contains("\"distinct_prototypes\":2", StringComparison.Ordinal), "the counts");
        Check(json.Contains("\"generators_with_content_differences\":1,\"content_differing_slots\":{\"21\":1}", StringComparison.Ordinal), "content differences leave the placement slots out");
        Console.WriteLine("generator-census self-test: PASS");
    }
}
