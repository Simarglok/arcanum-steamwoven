using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-079 (MIG4, TM3): a companion's body comes from its home map's data by identity, wherever it stands and whether or not this run
/// ever loaded the home map; the directory reads a map once (a window once per window), keeps a bounded number of reads, remembers a failed
/// read and never guesses. Synthetic sources: the real read is the opt-in real-data tests' and the native smoke's.
/// </summary>
[TestClass]
public sealed class AreaHomeDirectoryTests
{
    private static readonly CellCoord Stored = new(92913, 82598);

    private static AreaInventoryMap Rows(int map) => new(map, new([], []), new Dictionary<string, AreaObjectRecord>());

    private static AreaHomeRead Read(int map, AreaCompanionTemplate[]? templates = null, params (long X, long Y)[] core) =>
        new(map, Rows(map), templates ?? [], new Dictionary<AreaFrameKey, DecodedAreaFrame>(), core.Length == 0 ? null : core.ToHashSet());

    private sealed class Source(Func<int, CellCoord?, AreaHomeLoad> read) : IAreaHomeSource
    {
        public readonly List<(int Map, CellCoord? Near)> Asked = [];
        public AreaHomeLoad Read(int runtimeMap, CellCoord? near, IReadOnlySet<string> led, CancellationToken token) { Asked.Add((runtimeMap, near)); AskedLed.Add(led); return read(runtimeMap, near); }
        public readonly List<IReadOnlySet<string>> AskedLed = [];
    }

    [TestMethod]
    public void A_body_is_read_from_a_home_map_this_run_never_loaded_and_kept()
    {
        var template = AreaVisitorTalkTests.Template();   // home map 1
        var source = new Source((map, _) => new(Read(map, [template]), string.Empty));
        var homes = new AreaHomeDirectory(source);
        var (found, frames, detail) = homes.Body(template.Entity, 1, null);
        Assert.AreSame(template, found, detail);
        Assert.AreEqual(0, frames.Count);
        Assert.AreSame(template, homes.Body(template.Entity, 1, null).Template);
        Assert.AreEqual(1, source.Asked.Count, "read once, then held");
    }

    // ARC-AI-1646: a source that builds the body of the led NPCs it is told about (as AreaMapHomes does), and none otherwise.
    private sealed class LedBodySource(AreaCompanionTemplate template) : IAreaHomeSource
    {
        public readonly List<(int Map, CellCoord? Near, string Led)> Asked = [];
        public AreaHomeLoad Read(int runtimeMap, CellCoord? near, IReadOnlySet<string> led, CancellationToken token)
        {
            Asked.Add((runtimeMap, near, string.Join(",", led.Order(StringComparer.Ordinal))));
            var templates = led.Contains(template.Entity) ? new[] { template } : [];
            return new(new(runtimeMap, Rows(runtimeMap), templates, new Dictionary<AreaFrameKey, DecodedAreaFrame>(),
                near is null ? null : new HashSet<(long X, long Y)> { (near.Value.X >> 6, near.Value.Y >> 6) }), string.Empty);
        }
    }

    [TestMethod]
    public void A_led_npc_without_a_dialog_has_its_body_read_from_the_home_map_and_the_read_is_kept()
    {
        var template = AreaVisitorTalkTests.Template();
        var source = new LedBodySource(template);
        var homes = new AreaHomeDirectory(source);
        var (found, _, detail) = homes.Body(template.Entity, 1, null);
        Assert.AreSame(template, found, detail);
        Assert.AreEqual(1, source.Asked.Count);
        Assert.AreEqual(template.Entity, source.Asked[0].Led, "only the led NPC is asked for");
        Assert.AreSame(template, homes.Body(template.Entity, 1, null).Template);
        Assert.AreEqual(1, source.Asked.Count, "held after the read");
        CollectionAssert.AreEquivalent(new[] { template.Entity }, homes.Reads.Single().Led.ToArray());
    }

    [TestMethod]
    public void A_read_held_before_the_npc_joined_is_read_again_once_with_the_npc_asked_for()
    {
        var template = AreaVisitorTalkTests.Template();
        var source = new LedBodySource(template);
        var homes = new AreaHomeDirectory(source);
        // The read made for the speakers only (a map change before the join, or the viewer's own read of the map it loaded).
        Assert.IsNotNull(homes.Home(1, Stored).Read);
        Assert.AreEqual(string.Empty, source.Asked.Single().Led);
        Assert.IsNull(homes.Held(1, Stored)!.Template(template.Entity));
        var found = homes.Body(template.Entity, 1, Stored);
        Assert.AreSame(template, found.Template, found.Detail);
        Assert.AreEqual(2, source.Asked.Count);
        Assert.AreEqual(Stored, source.Asked[1].Near, "the window around the NPC's stored cell");
        Assert.AreEqual(1, homes.Reads.Count, "the second read replaces the first of the same window");
        Assert.AreSame(template, homes.Body(template.Entity, 1, Stored).Template);
        Assert.AreEqual(2, source.Asked.Count);
    }

    [TestMethod]
    public void A_remembered_read_without_the_led_npc_is_read_again_and_an_npc_the_map_does_not_have_is_asked_once()
    {
        var template = AreaVisitorTalkTests.Template();
        var source = new LedBodySource(template);
        var homes = new AreaHomeDirectory(source);
        homes.Remember(Read(1));
        Assert.AreSame(template, homes.Body(template.Entity, 1, null).Template);
        Assert.AreEqual(1, source.Asked.Count);
        var missing = homes.Body("not-on-this-map", 1, null);
        Assert.IsNull(missing.Template);
        StringAssert.Contains(missing.Detail, "lists no body");
        Assert.AreEqual(2, source.Asked.Count);
        Assert.AreEqual(string.Join(",", new[] { "not-on-this-map", template.Entity }.Order(StringComparer.Ordinal)), source.Asked[1].Led, "the NPCs asked for so far are kept");
        Assert.IsNull(homes.Body("not-on-this-map", 1, null).Template);
        Assert.AreEqual(2, source.Asked.Count, "asked once");
    }

    [TestMethod]
    public void A_remembered_map_answers_without_a_read()
    {
        var template = AreaVisitorTalkTests.Template();
        var source = new Source((_, _) => throw new AssertFailedException("the source must not be asked"));
        var homes = new AreaHomeDirectory(source);
        homes.Remember(Read(1, [template]));
        Assert.AreSame(template, homes.Body(template.Entity, 1, Stored).Template);
        Assert.AreEqual(0, source.Asked.Count);
    }

    [TestMethod]
    public void A_window_is_read_around_the_stored_cell_and_a_window_without_one_is_not_guessed()
    {
        var template = AreaVisitorTalkTests.Template();
        var source = new Source((map, near) => near is null ? new(null, "opens only as a window") : new(Read(map, [template], (Stored.X >> 6, Stored.Y >> 6)), string.Empty));
        var homes = new AreaHomeDirectory(source);
        var without = homes.Body(template.Entity, 1, null);
        Assert.IsNull(without.Template);
        StringAssert.Contains(without.Detail, "window");
        Assert.AreSame(template, homes.Body(template.Entity, 1, Stored).Template, "the cell a record names reads the window around it");
        Assert.AreEqual(Stored, source.Asked.Last().Near);
        Assert.AreEqual(2, source.Asked.Count);
    }

    [TestMethod]
    public void Another_window_of_the_same_map_is_read_and_held_next_to_the_first()
    {
        var template = AreaVisitorTalkTests.Template();
        // A source whose window holds the sector of the cell it was asked about.
        var source = new Source((map, near) => new(Read(map, [template], (near!.Value.X >> 6, near.Value.Y >> 6)), string.Empty));
        var homes = new AreaHomeDirectory(source);
        var other = new CellCoord(Stored.X + 640, Stored.Y);
        Assert.IsNotNull(homes.Home(1, Stored).Read);
        Assert.IsNotNull(homes.Home(1, other).Read);
        Assert.AreEqual(2, homes.Reads.Count);
        Assert.IsNotNull(homes.Home(1, Stored).Read);
        Assert.IsNotNull(homes.Home(1, other).Read);
        Assert.AreEqual(2, source.Asked.Count, "each window is read once");
    }

    [TestMethod]
    public void A_body_hit_keeps_its_read_from_being_the_least_recently_used()
    {
        var template = AreaVisitorTalkTests.Template();
        var homes = new AreaHomeDirectory(null, maxReads: 2);
        homes.Remember(Read(1, [template])); homes.Remember(Read(2));
        Assert.AreSame(template, homes.Body(template.Entity, 1, null).Template);   // map 1 is now the most recently used
        homes.Remember(Read(3));
        CollectionAssert.AreEquivalent(new[] { 1, 3 }, homes.Reads.Select(read => read.Map).ToArray());
    }

    [TestMethod]
    public void A_failed_read_is_remembered_and_a_source_that_throws_is_the_same_failure()
    {
        var calls = 0;
        var homes = new AreaHomeDirectory(new Source((_, _) => { calls++; throw new InvalidOperationException("boom"); }));
        var first = homes.Home(1, Stored);
        Assert.IsNull(first.Read);
        StringAssert.Contains(first.Detail, "boom");
        Assert.IsNull(homes.Home(1, Stored).Read);
        Assert.AreEqual(1, calls, "not asked again");
        Assert.IsNull(new AreaHomeDirectory(null).Home(1, Stored).Read, "no source: a typed failure");
        Assert.IsFalse(new AreaHomeDirectory(null).CanRead);
    }

    [TestMethod]
    public void A_read_of_another_map_or_one_that_misses_the_cell_is_refused()
    {
        var homes = new AreaHomeDirectory(new Source((_, _) => new(Read(9), string.Empty)));
        StringAssert.Contains(homes.Home(1, null).Detail, "returned map 9");
        var window = new AreaHomeDirectory(new Source((map, _) => new(Read(map, null, (0, 0)), string.Empty)));
        StringAssert.Contains(window.Home(1, Stored).Detail, "does not hold the cell");
    }

    [TestMethod]
    public void Reads_are_bounded_and_the_least_recently_used_leaves_first()
    {
        var homes = new AreaHomeDirectory(null, maxReads: 2);
        homes.Remember(Read(1)); homes.Remember(Read(2));
        Assert.IsNotNull(homes.Held(1));   // map 2 is now the least recently used
        homes.Remember(Read(3));
        CollectionAssert.AreEquivalent(new[] { 1, 3 }, homes.Reads.Select(read => read.Map).ToArray());
        homes.Remember(Read(3));
        Assert.AreEqual(2, homes.Reads.Count, "a read of the same map and window replaces the held one");
        Assert.Throws<ArgumentOutOfRangeException>(() => new AreaHomeDirectory(null, 0));
    }

    [TestMethod]
    public void A_map_record_source_reads_the_window_around_the_cell_the_session_recorded()
    {
        var asked = new List<(int Map, CellCoord? Near)>();
        var homes = new AreaHomeDirectory(new Source((map, near) => { asked.Add((map, near)); return new(Read(map), string.Empty); }));
        var rows = new AreaMapRecords(null!, homes, map => map == 1 ? Stored : null);
        Assert.IsNotNull(rows.Load(1, default).Map);
        CollectionAssert.AreEqual(new (int, CellCoord?)[] { (1, Stored) }, asked);
    }
}
