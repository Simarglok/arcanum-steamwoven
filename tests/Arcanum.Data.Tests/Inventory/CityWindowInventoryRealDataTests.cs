using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Inventory;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Inventory;

/// <summary>
/// ARC-WORLD-2036 opt-in checks on the installed original (DAT_ROOT = the game folder): the windows of the main map over Ashbury and Tarant seed their inventory view.
/// Two data facts refused them before. A held item keeps the location it had when it was made or picked up, so a holder inside the window can list an item that lies
/// outside it (Tarant: <c>ListedObjectUnresolved</c>); the window now admits those items as context rows. An Ashbury NPC (prototype 17146) has a gold handle that
/// names a gold stack another NPC of the same prototype lists (<c>GoldHandleMismatch</c>); the view keeps it as a stale handle. Prints counts only.
/// </summary>
[TestClass]
public sealed class CityWindowInventoryRealDataTests
{
    private const string MainMap = "Arcanum1-024-fixed";

    [TestMethod, TestCategory("RealData")]
    [DataRow(505, 976, "Ashbury: the stale gold handle")]
    [DataRow(504, 976, "Ashbury")]
    [DataRow(505, 977, "Ashbury")]
    [DataRow(506, 976, "Ashbury")]
    [DataRow(504, 975, "Ashbury")]
    [DataRow(505, 979, "Ashbury: seeded before")]
    [DataRow(969, 1026, "Tarant: a listed item outside the window")]
    [DataRow(970, 1026, "Tarant")]
    public void City_windows_of_the_main_map_seed_their_inventory(int x, int y, string what)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DAT_ROOT"))) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(Environment.GetEnvironmentVariable("DAT_ROOT")!);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var planned = AreaMapPlans.CreateWindow(stack, catalog, MainMap, new(x, y));
        Assert.AreEqual(AreaMapPlanStatus.Ready, planned.Status, planned.Detail);
        using var owner = planned.Plan!.Open();
        var window = owner.LoadStrict();
        var read = AreaInventoryReader.Read(window);
        Assert.AreEqual(AreaInventoryFailure.None, read.Failure, $"{what}: refused at {read.SourceIndex}");
        var inventory = read.Inventory!;
        var stale = inventory.Holders.Where(h => h.StaleGoldHandle is not null).Select(h => h.SourceIndex).ToArray();
        var outside = window.Objects.Count(o => o.Key.Kind == AreaSourceKind.Mobile && o.Scope == AreaScope.Context && o.Type is >= 5 and <= 14 && o.WorldX is { } wx && o.WorldY is { } wy &&
            !window.Sectors.Any(s => s.Present && s.Coordinate.X == wx / 64 && s.Coordinate.Y == wy / 64));
        Console.WriteLine($"ARC-WORLD-2036 {x},{y} {what}: holders={inventory.Holders.Count} items={inventory.Items.Count} detached={inventory.Detached.Count} " +
            $"stale-gold-holders=[{string.Join(",", stale)}] held-items-outside-the-window={outside}");
        // The five NPCs of Ashbury whose gold handle names a stack outside their own list (1162 names the one of NPC 5664, the same prototype); the one of Tarant (1461).
        if (x is >= 504 and <= 506 && y is >= 975 and <= 977) CollectionAssert.AreEqual(new[] { 1162, 3030, 3377, 4068, 6955 }, stale, "the stale gold handles of Ashbury");
        if (x == 505 && y == 979) CollectionAssert.AreEqual(Array.Empty<int>(), stale);
        if (x == 969 && y == 1026) { CollectionAssert.AreEqual(new[] { 1461 }, stale); Assert.AreEqual(1, outside, "the listed item that lies outside the window"); }
    }
}
