using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class PerfOverlayTests
{
    private static PerfOverlayRoute Route(PerfOverlayChord chord, bool pressed, bool ctrl = true, bool echo = false, bool shift = false,
        bool alt = false, bool meta = false, bool isF = true) => chord.Route(isF, pressed, echo, ctrl, shift, alt, meta);

    [TestMethod]
    public void CtrlFTogglesOnceAndOwnsItsEchoesAndRelease()
    {
        var chord = new PerfOverlayChord();
        Assert.AreEqual(PerfOverlayRoute.Toggle, Route(chord, true));
        Assert.AreEqual(PerfOverlayRoute.Consumed, Route(chord, true, echo: true));
        // Ctrl released first: the F release still belongs to the chord.
        Assert.AreEqual(PerfOverlayRoute.Consumed, Route(chord, false, ctrl: false));
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, false, ctrl: false));
    }

    [TestMethod]
    public void BareFAndOtherModifiersStayTheGames()
    {
        var chord = new PerfOverlayChord();
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, true, ctrl: false));
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, false, ctrl: false));
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, true, shift: true));
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, true, alt: true));
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, true, meta: true));
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, true, isF: false));
        // An echo of a press the chord never took (Ctrl pressed while F was already held) toggles nothing.
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, true, echo: true));
    }

    [TestMethod]
    public void ALostReleaseDoesNotBlockTheNextPressOrKeepItsOwnership()
    {
        var chord = new PerfOverlayChord();
        Assert.AreEqual(PerfOverlayRoute.Toggle, Route(chord, true));
        Assert.AreEqual(PerfOverlayRoute.Toggle, Route(chord, true));
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, true, ctrl: false));
        Assert.AreEqual(PerfOverlayRoute.PassThrough, Route(chord, false, ctrl: false));
    }

    [TestMethod]
    public void RateNeedsTwoGrowingSamples()
    {
        var rate = new PerfRate();
        Assert.IsNull(rate.Sample(1_000_000, 100));
        Assert.AreEqual(200, rate.Sample(1_500_000, 200)!.Value, 1e-9);
        Assert.IsNull(rate.Sample(1_500_000, 300), "no time passed");
        Assert.IsNull(rate.Sample(2_000_000, 10), "the total went back");
        Assert.IsNull(rate.Sample(2_500_000, null), "unreadable");
        Assert.IsNull(rate.Sample(3_000_000, 20), "the first sample after an unreadable one");
    }

    [TestMethod]
    public void MeterReportsFrameAndWorkTimesOfTheLastSecondsAndRefreshesTwiceASecond()
    {
        var meter = new PerfOverlayMeter();
        Assert.IsNull(meter.Read());
        Assert.IsTrue(meter.TakeRefresh(0));
        Assert.IsFalse(meter.TakeRefresh(PerfOverlayMeter.RefreshMicros - 1));
        Assert.IsTrue(meter.TakeRefresh(PerfOverlayMeter.RefreshMicros));
        meter.Record(0, 0, 0, 0, 99);
        // A slow, costly frame that leaves the window, then 4 s of 10 ms frames with one of 30 ms near the end.
        meter.Record(500_000, 0, 0, 0, 90);
        var at = 500_000L;
        for (var i = 1; i <= 400; i++)
        {
            at += i == 399 ? 30_000 : 10_000;
            meter.Record(at, i / 100, 0, 0, i == 399 ? 8 : 2);
        }
        var stats = meter.Read()!.Value;
        Assert.AreEqual(30, stats.MaxMs, 1e-3);
        Assert.AreEqual(10, stats.P95Ms, 1e-3);
        Assert.AreEqual(8, stats.WorkMaxMs, 1e-3);
        Assert.IsTrue(stats.WorkAverageMs is > 2 and < 2.1);
        Assert.IsTrue(stats.SpanSeconds <= PerfOverlayMeter.WindowSeconds + 1e-6);
        Assert.IsTrue(stats.Gen0PerSecond > 0.5);
    }

    [TestMethod]
    public void TextShowsEveryMetric()
    {
        var text = new PerfOverlayReading(new ViewerFrameStats(180, 3, 16.667, 18.2, 33.4, 21.5, 4.25, 0, 5.5, 12),
            9.8, 3.14, 1.5, 412, 1203, 24000, 812L * 1024 * 1024, 310L * 1024 * 1024, 156L * 1024 * 1024, 38.25 * 1024 * 1024, 134.6,
            5120, 14302, 2560, 1440).Text();
        CollectionAssert.AreEqual(new[]
        {
            "FPS 60.0   (Ctrl+F hides)",
            "frame ms   avg 16.7  p95 18.2  max 33.4",
            "update ms  avg 5.5  max 12.0",
            "main step ms 9.8 (worst of 1 s)",
            "render ms  cpu 3.14  gpu 1.50",
            "draw calls 412  objects 1203  prims 24000",
            "memory     resident 812 MiB  video 310 MiB",
            ".NET       heap 156 MiB  alloc 38.3 MiB/s",
            "GC per s   gen0 21.5  gen1 4.3  gen2 0.0",
            "CPU 135%  nodes 5120  Godot objects 14302",
            "window 2560x1440  last 3 s",
        }, text.Split('\n'));
    }

    [TestMethod]
    public void TheFrameCapStandsBesideTheRateWhenTheViewerPacesItself()
    {
        var reading = new PerfOverlayReading(new ViewerFrameStats(180, 3, 16.667, 18.2, 33.4, 0, 0, 0), null, null, null, null, null, null,
            null, null, null, null, null, null, null, 800, 600, 60);
        StringAssert.StartsWith(reading.Text(), "FPS 60.0  cap 60   (Ctrl+F hides)\n");
    }

    [TestMethod]
    public void UnreadableMetricsSayNotAvailableNeverZero()
    {
        var text = new PerfOverlayReading(null, null, null, null, null, null, null, null, long.MaxValue, null, null, null, null, null, 800, 600).Text();
        StringAssert.StartsWith(text, "FPS ...");
        StringAssert.Contains(text, "frame ms   avg n/a  p95 n/a  max n/a");
        StringAssert.Contains(text, "gpu n/a");
        StringAssert.Contains(text, "draw calls n/a");
        StringAssert.Contains(text, "resident n/a  video n/a");
        StringAssert.Contains(text, "alloc n/a");
        StringAssert.Contains(text, "gen0 n/a");
    }
}
