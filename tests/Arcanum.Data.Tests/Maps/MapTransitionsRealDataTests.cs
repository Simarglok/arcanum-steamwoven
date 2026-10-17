using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>Opt-in check of the installed original (DAT_ROOT = the game folder) against the transition
/// census of docs/research/world-transitions.md (ARC-RES-035): jump points, use and tile paths, the
/// unconditional cross-map set and the maps the start map reaches through them.</summary>
[TestClass]
public sealed class MapTransitionsRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_listed_map_gives_the_transitions_of_the_research_census()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in transition check; this is not a synthetic pass.");
        var plan = ContentMountPlan.Discover(root, "Arcanum").Plan!;
        using var stack = ContentStack.Open(plan).Stack!;
        using var scripts = ScriptContent.Over(stack);
        var catalog = WorldCatalog.Open(stack);
        var list = MapList.Read(stack);
        Assert.AreEqual(MapListStatus.Read, list.Status);
        Assert.AreEqual(81, list.Entries.Count);

        var sets = catalog.Maps.Where(m => list.Find(m.Name) is not null).Select(m => MapTransitions.Read(catalog, list, m, scripts)).ToArray();
        Assert.AreEqual(79, sets.Length);
        // Only the editor's two side files (as the map catalogue reports them); no sector, object, prototype or script refusal.
        var issues = sets.SelectMany(s => s.Issues).ToArray();
        Assert.AreEqual(2, issues.Length, string.Join("; ", issues.Select(i => i.ToString())));
        Assert.IsTrue(issues.All(i => i.Code == WorldIssueCode.UnrecognisedFile), string.Join("; ", issues.Select(i => i.ToString())));
        var sites = sets.SelectMany(s => s.Sites.Select(site => (From: s.Entry!, Site: site))).ToArray();

        var jumps = sites.Where(p => p.Site.Trigger == MapTransitionTrigger.JumpPoint).ToArray();
        Assert.AreEqual(45, jumps.Length);
        Assert.AreEqual(7, jumps.Count(p => p.Site.DestinationRuntime != p.From.Runtime));
        Assert.IsTrue(jumps.All(p => p.Site.Unconditional));

        var use = sites.Where(p => p.Site.Trigger == MapTransitionTrigger.Use).ToArray();
        var containers = use.Where(p => p.Site.Object!.Type == 2).ToArray();
        Assert.AreEqual(281, use.Length - containers.Length);
        Assert.AreEqual(241, use.Count(p => p.Site.Unconditional));
        Assert.AreEqual(5, containers.Length);
        Assert.IsTrue(containers.All(p => p.Site.Guards.HasFlag(MapTransitionGuard.ContainerOpenCheck)));
        var tiles = sites.Where(p => p.Site.Trigger == MapTransitionTrigger.TileEntry).ToArray();
        Assert.AreEqual(421, tiles.Length);
        Assert.AreEqual(9, tiles.Count(p => p.Site.Unconditional));
        Assert.AreEqual(0, sites.Count(p => p.Site.Trigger == MapTransitionTrigger.SectorEntry));

        var crossing = sites.Where(p => p.Site.Trigger != MapTransitionTrigger.JumpPoint && p.Site.Unconditional && p.Site.DestinationRuntime != p.From.Runtime).ToArray();
        Assert.AreEqual(151, crossing.Length);
        Assert.AreEqual(144, crossing.Count(p => p.Site.Trigger == MapTransitionTrigger.Use && p.Site.Object!.Type == 3));
        Assert.AreEqual(2, crossing.Count(p => p.Site.Trigger == MapTransitionTrigger.Use && p.Site.Object!.Type == 1));
        Assert.AreEqual(5, crossing.Count(p => p.Site.Trigger == MapTransitionTrigger.TileEntry));
        Assert.AreEqual(2, sites.Count(p => p.Site.Unconditional && p.Site.OneShot && p.Site.Trigger != MapTransitionTrigger.JumpPoint));

        // From the start map, jump points and unconditional paths reach 44 maps (5000 included).
        var edges = sites.Where(p => p.Site.Unconditional).ToLookup(p => p.From.Runtime, p => p.Site.DestinationRuntime);
        var reached = new HashSet<int> { 1 }; var queue = new Queue<int>([1]);
        while (queue.TryDequeue(out var map)) foreach (var next in edges[map]) if (reached.Add(next)) queue.Enqueue(next);
        Assert.AreEqual(44, reached.Count);

        // The Crash Site cave pair, from the universal reader as the pinned profiles state it.
        var cave = sites.Single(p => p.From.Catalogue == 5000 && p.Site.Unconditional && p.Site.DestinationCatalogue == 5078);
        Assert.AreEqual((2793, new WorldLocation(2087, 2018)), (cave.Site.Script, cave.Site.Arrival));
        var back = sites.Single(p => p.From.Catalogue == 5078 && p.Site.Unconditional && p.Site.DestinationCatalogue == 5000);
        Assert.AreEqual((1532, new WorldLocation(92913, 82598), new WorldLocation(2093, 2019)), (back.Site.Script, back.Site.Arrival, back.Site.Location));
    }
}
