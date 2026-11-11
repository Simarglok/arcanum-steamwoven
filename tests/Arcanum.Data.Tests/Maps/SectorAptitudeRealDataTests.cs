using Arcanum.Data.Audio;
using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>
/// ARC-MAGIC-028 on the installed original (opt-in, <c>DAT_ROOT</c>): the aptitude adjustment every sector file of the Arcanum
/// module holds, the nature flag of the tile names and the mage backgrounds' block numbers. Counts only, no game text printed.
/// </summary>
[TestClass]
public sealed class SectorAptitudeRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_sector_aptitude_adjustments_and_tile_name_flags()
    {
        using var stack = Open("Arcanum");
        var catalog = WorldCatalog.Open(stack);
        int sectors = 0, withMetadata = 0, nonZero = 0, minimum = int.MaxValue, maximum = int.MinValue, outOfRange = 0;
        var values = new SortedDictionary<int, int>();
        foreach (var map in catalog.Maps)
            foreach (var coordinate in map.SectorFiles)
            {
                var sector = catalog.ReadSector(map, coordinate).Sector!;
                sectors++;
                if (sector.MetadataAndSound is not null) withMetadata++;
                var value = sector.AptitudeAdjustment();
                if (value != 0)
                {
                    nonZero++; values[value] = values.GetValueOrDefault(value) + 1;
                    Console.WriteLine($"non-zero: {map.Name} ({coordinate.X}, {coordinate.Y}) = {value}");
                }
                minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value);
                if (Math.Abs(value) > 100) outOfRange++;
            }
        Console.WriteLine($"sectors {sectors}, with metadata {withMetadata}, non-zero {nonZero}, range {minimum}..{maximum}, beyond 100: {outOfRange}, values {string.Join(" ", values.Select(p => $"{p.Key}x{p.Value}"))}");

        var tiles = TileSoundTable.Open(stack);
        Assert.AreEqual(TileSoundTableStatus.Ready, tiles.Status);
        var table = tiles.Table!;
        var nature = Enumerable.Range(0, TileSoundTable.Categories)
            .Select(c => Enumerable.Range(0, table.Category(c).Count).Count(p => ((table.NameFlags(Art(c, p)) ?? 0) & TileSoundTable.NatureFlag) != 0)).ToArray();
        Console.WriteLine($"tile names with the nature flag per category: {string.Join(" ", nature)}");

        // The mage backgrounds are blocks 62..65 of rules\backgrnd.mes, offered to a PC.
        var loaded = BackgroundTable.Load(stack);
        Assert.AreEqual(BackgroundTableStatus.Ready, loaded.Status);
        foreach (var background in new[] { 62, 63, 64, 65 })
            Assert.IsTrue(loaded.Table!.HasBlock(background) && loaded.Table.Allows(background, 0, 1), $"background {background}");
    }

    // A tile art id naming position p of category c: 0 outdoor flippable, 1 outdoor, 2 indoor flippable, 3 indoor.
    private static uint Art(int category, int position) =>
        (uint)(position << 22) | (category < 2 ? 0x100u : 0) | (category is 0 or 2 ? 0x80u : 0);

    private static ContentStack Open(string module)
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in sector aptitude check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root, module);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }
}
