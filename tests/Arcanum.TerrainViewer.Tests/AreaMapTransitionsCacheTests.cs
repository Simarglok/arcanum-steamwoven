using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-368 (O5b): a map's transitions are read once per process for the same content stack. The identity the cache keys by
/// follows the stack's mounted sources, and the opt-in check shows a second read of the main map comes from the cache and equals the first.</summary>
[TestClass]
public sealed class AreaMapTransitionsCacheTests
{
    [TestMethod]
    public void The_stack_identity_follows_the_mounted_sources_and_their_archives()
    {
        var first = Directory.CreateTempSubdirectory("arc-dev-368-a-"); var second = Directory.CreateTempSubdirectory("arc-dev-368-b-");
        try
        {
            string Identity(DirectoryInfo root)
            {
                var discovery = ContentMountPlan.Discover(root.FullName, "Syn");
                Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
                var opened = ContentStack.Open(discovery.Plan!);
                Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
                using var stack = opened.Stack!;
                return AreaMapTransitionsCache.Identity(stack);
            }
            void Make(DirectoryInfo root, params (string Name, byte[] Bytes)[] tig)
            {
                File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat(tig));
                Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
                File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
            }
            Make(first); Make(second);
            var identity = Identity(first);
            Assert.AreEqual(identity, Identity(first), "the same stack, the same identity");
            Assert.AreNotEqual(identity, Identity(second), "another game folder is another stack");
            // An archive that changed in size is another content, though its path is the same.
            File.WriteAllBytes(Path.Combine(first.FullName, "tig.dat"), AreaOwnerFixture.Dat(("art/x.art", new byte[] { 1, 2, 3 })));
            Assert.AreNotEqual(identity, Identity(first), "a changed archive is another stack");
        }
        finally { first.Delete(recursive: true); second.Delete(recursive: true); }
    }

    [TestMethod, TestCategory("RealData")]
    public void A_map_read_again_in_the_process_takes_its_transitions_from_the_cache_and_they_equal_the_first_read()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in cache check; this is not a synthetic pass."); return; }
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        AreaMapTransitionsCache.Clear();
        var map = catalog.Find("Arcanum1-024-fixed")!;
        var before = AreaMapTransitionsCache.Reads;
        var first = AreaMapLoadOperation.Transitions(stack, catalog, map);
        Assert.IsNull(first.Failure);
        Assert.AreEqual(before + 1, AreaMapTransitionsCache.Reads, "the first read comes from the content");
        var again = AreaMapLoadOperation.Transitions(stack, catalog, map);
        Assert.AreEqual(before + 1, AreaMapTransitionsCache.Reads, "the second read comes from the cache");
        Assert.AreSame(first.Transitions, again.Transitions);
        Assert.AreEqual(first.Transitions!.Sites.Count, again.Transitions!.Sites.Count);
        CollectionAssert.AreEquivalent(first.Destinations!.ToArray(), again.Destinations!.ToArray());
        // A fresh read, not the cache, gives the same records as the cached ones.
        AreaMapTransitionsCache.Clear();
        var fresh = AreaMapLoadOperation.Transitions(stack, catalog, map);
        Assert.AreNotSame(first.Transitions, fresh.Transitions);
        CollectionAssert.AreEqual(first.Transitions!.Sites.Select(site => site.ToString()).ToArray(), fresh.Transitions!.Sites.Select(site => site.ToString()).ToArray());
        CollectionAssert.AreEquivalent(first.Destinations!.ToArray(), fresh.Destinations!.ToArray());
    }

    /// <summary>ARC-DEV-392: a second process takes the main map's transitions from the device store, and they equal a fresh read of the content.</summary>
    [TestMethod, TestCategory("RealData"), DoNotParallelize]
    public void A_map_kept_for_the_next_process_comes_back_equal_to_a_fresh_read_of_the_content()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in store check; this is not a synthetic pass."); return; }
        var folder = Directory.CreateTempSubdirectory("arc-dev-392-real-");
        var saved = AreaMapTransitionsStore.Root;
        try
        {
            AreaMapTransitionsStore.Root = folder.FullName;
            using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
            var catalog = WorldCatalog.Open(stack);
            var map = catalog.Find("Arcanum1-024-fixed")!;
            AreaMapTransitionsCache.Clear();
            var first = AreaMapLoadOperation.Transitions(stack, catalog, map, out var firstSource);
            Assert.AreEqual("content+stored", firstSource);
            // A new process has an empty memory cache.
            AreaMapTransitionsCache.Clear();
            var reads = AreaMapTransitionsCache.Reads;
            var kept = AreaMapLoadOperation.Transitions(stack, catalog, map, out var keptSource);
            Assert.AreEqual("device", keptSource);
            Assert.AreEqual(reads, AreaMapTransitionsCache.Reads, "the content was not read");
            Assert.AreNotSame(first.Transitions, kept.Transitions);
            Assert.AreEqual(first.Transitions!.Sites.Count, kept.Transitions!.Sites.Count);
            Assert.AreEqual(first.Transitions.IsStartMap, kept.Transitions.IsStartMap);
            Assert.AreEqual(first.Transitions.Entry, kept.Transitions.Entry);
            for (var i = 0; i < first.Transitions.Sites.Count; i++)
            {
                var (a, b) = (first.Transitions.Sites[i], kept.Transitions.Sites[i]);
                Assert.AreEqual(a with { Chain = [] }, b with { Chain = [] }, $"site {i}");
                CollectionAssert.AreEqual(a.Chain.ToArray(), b.Chain.ToArray(), $"chain {i}");
            }
            CollectionAssert.AreEqual(first.Transitions.Issues.ToArray(), kept.Transitions.Issues.ToArray());
            CollectionAssert.AreEqual(first.Transitions.Placed.ToArray(), kept.Transitions.Placed.ToArray());
            CollectionAssert.AreEquivalent(first.Destinations!.ToArray(), kept.Destinations!.ToArray());
            // The memory cache now answers, and a store off reads the content again.
            AreaMapLoadOperation.Transitions(stack, catalog, map, out var third);
            Assert.AreEqual("memory", third);
            AreaMapTransitionsCache.Clear(); AreaMapTransitionsStore.Root = null;
            AreaMapLoadOperation.Transitions(stack, catalog, map, out var off);
            Assert.AreEqual("content", off);
        }
        finally { AreaMapTransitionsStore.Root = saved; AreaMapTransitionsCache.Clear(); folder.Delete(recursive: true); }
    }
}
