using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-3128: the rolling frame-cost window of the resource line.</summary>
[TestClass]
public sealed class ViewerFrameWindowTests
{
    private const long Ms = 1000;

    [TestMethod]
    public void NothingToReadBeforeTwoFrames()
    {
        var window = new ViewerFrameWindow();
        Assert.IsNull(window.Read());
        window.Record(1_000_000, 0, 0, 0); // the first call only sets the start
        Assert.IsNull(window.Read());
        window.Record(1_016_000, 0, 0, 0);
        Assert.IsNull(window.Read(), "one frame has no span to put a rate on");
        window.Record(1_032_000, 0, 0, 0);
        Assert.IsNotNull(window.Read());
    }

    [TestMethod]
    public void AverageP95AndMaxOfKnownFrames()
    {
        var window = new ViewerFrameWindow();
        long t = 5_000_000;
        window.Record(t, 0, 0, 0);
        // 99 frames of 10 ms and one of 200 ms: avg 10 + 190/100 = 11.9, nearest-rank p95 (rank 95 of 100) = 10, max 200.
        for (var i = 0; i < 99; i++) { t += 10 * Ms; window.Record(t, 0, 0, 0); }
        t += 200 * Ms; window.Record(t, 0, 0, 0);
        var stats = window.Read()!.Value;
        Assert.AreEqual(100, stats.Frames);
        Assert.AreEqual(11.9, stats.AverageMs, 1e-3);
        Assert.AreEqual(10.0, stats.P95Ms, 1e-3);
        Assert.AreEqual(200.0, stats.MaxMs, 1e-3);
    }

    [TestMethod]
    public void P95SeesAFifthOfSlowFrames()
    {
        var window = new ViewerFrameWindow();
        long t = 0;
        window.Record(t, 0, 0, 0);
        for (var i = 0; i < 100; i++) { t += (i % 5 == 0 ? 50 : 10) * Ms; window.Record(t, 0, 0, 0); }
        Assert.AreEqual(50.0, window.Read()!.Value.P95Ms, 1e-3);
    }

    [TestMethod]
    public void CollectionRatesAreCountsPerSecondOfTheSpan()
    {
        var window = new ViewerFrameWindow();
        long t = 0; int g0 = 0, g1 = 0, g2 = 0;
        window.Record(t, g0, g1, g2);
        for (var second = 1; second <= 10; second++)
        {
            t += 1_000_000; g0 += 6; g1 += 2; if (second % 5 == 0) g2++;
            window.Record(t, g0, g1, g2);
        }
        var stats = window.Read()!.Value;
        // The first kept frame is the one after the start (counts 6/2/0 at 1 s); the span is 9 s: 54 gen0, 18 gen1, 2 gen2.
        Assert.AreEqual(10, stats.Frames);
        Assert.AreEqual(9.0, stats.SpanSeconds, 1e-9);
        Assert.AreEqual(6.0, stats.Gen0PerSecond, 1e-9);
        Assert.AreEqual(2.0, stats.Gen1PerSecond, 1e-9);
        Assert.AreEqual(2.0 / 9, stats.Gen2PerSecond, 1e-9);
    }

    [TestMethod]
    public void FramesOlderThanTheWindowAreDropped()
    {
        var window = new ViewerFrameWindow(windowSeconds: 10);
        long t = 0;
        window.Record(t, 0, 0, 0);
        for (var i = 0; i < 50; i++) { t += 100 * Ms; window.Record(t, 0, 0, 0); } // 5 s of 100 ms frames
        for (var i = 0; i < 600; i++) { t += 20 * Ms; window.Record(t, 0, 0, 0); } // 12 s of 20 ms frames
        var stats = window.Read()!.Value;
        Assert.AreEqual(20.0, stats.AverageMs, 1e-3, "the 100 ms frames have left the window");
        Assert.AreEqual(20.0, stats.MaxMs, 1e-3);
        Assert.IsTrue(stats.SpanSeconds is > 9.9 and <= 10.0, stats.SpanSeconds.ToString());
        Assert.AreEqual(501, stats.Frames);
    }

    [TestMethod]
    public void AFullRingKeepsTheNewestFrames()
    {
        var window = new ViewerFrameWindow(windowSeconds: 30, capacity: 16);
        long t = 0;
        window.Record(t, 0, 0, 0);
        for (var i = 0; i < 100; i++) { t += (i < 84 ? 100 : 5) * Ms; window.Record(t, 0, 0, 0); }
        var stats = window.Read()!.Value;
        Assert.AreEqual(16, stats.Frames);
        Assert.AreEqual(5.0, stats.MaxMs, 1e-3);
        Assert.AreEqual(window.Count, stats.Frames);
    }

    [TestMethod]
    public void AClockThatGoesBackStartsTheWindowAfresh()
    {
        var window = new ViewerFrameWindow();
        window.Record(1_000_000, 0, 0, 0);
        window.Record(1_010_000, 0, 0, 0);
        window.Record(900_000, 0, 0, 0);
        Assert.AreEqual(0, window.Count);
        window.Record(916_000, 0, 0, 0);
        window.Record(932_000, 0, 0, 0);
        var stats = window.Read()!.Value;
        Assert.AreEqual(2, stats.Frames);
        Assert.AreEqual(16.0, stats.AverageMs, 1e-3);
    }

    [TestMethod]
    public void AStallLongerThanTheWindowIsStillRead()
    {
        var window = new ViewerFrameWindow(windowSeconds: 30);
        long t = 0;
        window.Record(t, 0, 0, 0);
        for (var i = 0; i < 100; i++) { t += 16 * Ms; window.Record(t, 0, 0, 0); }
        t += 45_000_000; window.Record(t, 1, 0, 0); // a 45 s stall (a cold map load)
        var stats = window.Read()!.Value;
        Assert.AreEqual(45_000.0, stats.MaxMs, 1e-3);
        Assert.AreEqual(2, stats.Frames, "only the frame before the stall and the stall frame are left");
    }

    [TestMethod]
    public void RecordingAllocatesNothing()
    {
        var window = new ViewerFrameWindow();
        long t = 0;
        for (var i = 0; i < 20_000; i++) { t += 16_000; window.Record(t, 0, 0, 0); } // warm up and wrap the ring
        // ARC-DEV-3426: a background collection that ends inside the measured loop can raise this counter by the unused rest of the thread's
        // allocation context (up to about 8 KB) although nothing was allocated (dotnet/runtime#134724, open). A blocking collection leaves the
        // thread without a context and the loop takes none, so the exact zero below holds. Nothing that allocates may go between these lines.
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100_000; i++) { t += 16_000; window.Record(t, i / 1000, 0, 0); }
        Assert.AreEqual(before, GC.GetAllocatedBytesForCurrentThread(), "Record allocates nothing per frame");
    }

    [TestMethod]
    public void TheResourceLineCarriesTheFrameStats()
    {
        var stats = new ViewerFrameStats(900, 30.0, 21.34, 33.4, 120.0, 4.5, 1.0, 0.0333);
        Assert.AreEqual("frameMs=21.3/33.4/120.0 gcRate=4.50/1.00/0.03/s over=30s", stats.Text());
        var sample = new ViewerSoakSample(90, "none", 23, 0, 0, 0, 1, 2, 3, 0, 0, 0, 0, 8, 3, 2, 0, 34, 61, stats);
        StringAssert.EndsWith(sample.Text(), " openFiles=61 frameMs=21.3/33.4/120.0 gcRate=4.50/1.00/0.03/s over=30s");
        StringAssert.EndsWith(sample.Line(), ",61", "the CSV row keeps its columns");
        StringAssert.EndsWith((sample with { Frames = null }).Text(), " openFiles=61 frameMs=?");
    }
}
