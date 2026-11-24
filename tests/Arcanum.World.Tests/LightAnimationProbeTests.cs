using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-RENDER-3126: the diagnostic switch and the per-second line of the animated lights' view pushes. The timing probe is process-wide, so each test sets
/// its own state and puts it back.</summary>
[TestClass, DoNotParallelize]
public sealed class LightAnimationProbeTests
{
    private static List<string> Capture(bool on, Action work)
    {
        var lines = new List<string>(); var (was, sink) = (PerfProbe.Enabled, PerfProbe.Sink);
        try { PerfProbe.Enable(on); PerfProbe.Sink = lines.Add; work(); return lines; }
        finally { PerfProbe.Enable(was); PerfProbe.Sink = sink; }
    }

    [TestMethod]
    public void OnlyZeroTurnsTheAnimationPushOff()
    {
        Assert.IsTrue(LightAnimationProbe.IsDisabled("0"));
        Assert.IsTrue(LightAnimationProbe.IsDisabled(" 0 "));
        foreach (var value in new[] { null, "", "1", "off", "false", "00" }) Assert.IsFalse(LightAnimationProbe.IsDisabled(value), value);
    }

    [TestMethod]
    public void OffPrintsNothing()
    {
        var probe = new LightAnimationProbe(1);
        var lines = Capture(false, () => { for (var i = 0; i < 5; i++) probe.Frame(Stopwatch.Frequency * (i + 1), 60, 3); });
        Assert.AreEqual(0, lines.Count);
    }

    [TestMethod]
    public void OnePrintedLineACompletedWindowWithFramesPushesAndFps()
    {
        var probe = new LightAnimationProbe(1);
        var second = Stopwatch.Frequency;
        var lines = Capture(true, () =>
        {
            probe.Frame(second, 50, 4); // opens the window
            for (var i = 1; i <= 9; i++) probe.Frame(second + second * i / 10, 50, 4); // frames inside it: nothing yet
            probe.Push(3.0, 2048); probe.Push(5.0, 4096);
            probe.Frame(second * 2, 47.5, 4); // the tenth frame ends it
        });
        Assert.AreEqual(1, lines.Count, string.Join("\n", lines));
        var line = lines[0];
        StringAssert.StartsWith(line, "ARC-DEV-367 perf light-anim ");
        foreach (var part in new[] { "lights=4", "frames=10", "fps-avg=10.0", "fps-engine=47.5", "pushes=2", "push-ms-total=8.0", "push-ms-max=5.0", "push-alloc-kib=6" })
            StringAssert.Contains(line, part);
    }

    [TestMethod]
    public void TheNextWindowStartsFromZero()
    {
        var probe = new LightAnimationProbe(1);
        var second = Stopwatch.Frequency;
        var lines = Capture(true, () =>
        {
            probe.Frame(second, 60, 1); probe.Push(1, 0); probe.Frame(second * 2, 60, 1);
            probe.Frame(second * 3, 60, 1);
        });
        Assert.AreEqual(2, lines.Count);
        StringAssert.Contains(lines[1], "pushes=0");
        StringAssert.Contains(lines[1], "frames=1");
    }
}
