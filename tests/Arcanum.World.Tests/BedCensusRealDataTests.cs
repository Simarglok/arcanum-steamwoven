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
/// overrides the prototype's); numbers only. The main world map (more sector files than a snapshot holds) is not read here: its placements are not counted.
/// </summary>
[TestClass]
public sealed class BedCensusRealDataTests
{
    /// <summary>The pinned result of the installed 1.0.7.4 data (2026-11-12): placed beds, those with a use script, the maps read and the distinct prototypes the placed beds name.</summary>
    private const int PlacedBeds = 191, PlacedBedsWithScript = 19, MapsRead = 80, BedPrototypes = 1;

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
        var perMap = new SortedDictionary<string, (int Beds, int Scripted)>(StringComparer.Ordinal);
        foreach (var map in catalog.Maps)
        {
            if (map.SectorFiles.Count > AreaMapPlans.MaxSectorFiles) { skipped++; Console.WriteLine($"ARC-WORLD-1162 not read: {map.Name} ({map.SectorFiles.Count} sector files)"); continue; }
            var created = AreaMapPlans.Create(stack, catalog, map.Name);
            if (created.Status != AreaMapPlanStatus.Ready) { skipped++; Console.WriteLine($"ARC-WORLD-1162 not read: {map.Name}: {created.Status}"); continue; }
            using var owner = created.Plan!.Open();
            var snapshot = owner.LoadStrict();
            maps++;
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
        Assert.AreEqual((MapsRead, PlacedBeds, PlacedBedsWithScript, BedPrototypes), (maps, beds, withScript, protoBeds.Count), "the installed data's bed census changed");
    }
}
