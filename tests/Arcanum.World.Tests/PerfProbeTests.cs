using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-DEV-367: the standing timing probe. It prints nothing and records nothing while off, and one line per mark, span and lap step
/// while on. The probe is process-wide, so every test sets its own state and puts it back.</summary>
[TestClass, DoNotParallelize]
public sealed class PerfProbeTests
{
    private static List<string> Capture(bool on, Action work)
    {
        var lines = new List<string>(); var (was, sink) = (PerfProbe.Enabled, PerfProbe.Sink);
        try { PerfProbe.Enable(on); PerfProbe.Sink = lines.Add; work(); return lines; }
        finally { PerfProbe.Enable(was); PerfProbe.Sink = sink; }
    }

    [TestMethod]
    public void OffPrintsAndRecordsNothing()
    {
        var lines = Capture(false, () =>
        {
            PerfProbe.Mark("test.off.mark");
            using (PerfProbe.Span("test.off.span")) { }
            using (var lap = PerfProbe.Lap("test.off.lap")) lap.Step("a");
            Assert.AreEqual(5, PerfProbe.Measure("test.off.measure", () => 5));
        });
        Assert.AreEqual(0, lines.Count);
        Assert.IsNull(PerfProbe.TimeOf("test.off.mark"));
        Assert.IsNull(PerfProbe.MillisecondsOf("test.off.span"));
    }

    [TestMethod]
    public void MarkSpanAndLapPrintOneLineEachAndKeepTheirValues()
    {
        var lines = Capture(true, () =>
        {
            PerfProbe.Mark("test.on.mark");
            using (PerfProbe.Span("test.on.span")) { _ = new byte[100_000]; Thread.Sleep(5); }
            using (var lap = PerfProbe.Lap("test.on.lap")) { lap.Step("a"); lap.Step("b"); }
        });
        Assert.AreEqual(1 + 1 + 3, lines.Count, string.Join("\n", lines));
        Assert.IsTrue(lines.All(line => line.StartsWith(PerfProbe.Prefix + " ", StringComparison.Ordinal)));
        StringAssert.Contains(lines[0], "mark name=test.on.mark t=");
        StringAssert.Contains(lines[1], "span name=test.on.span ms=");
        StringAssert.Contains(lines[1], "alloc-kib=");
        StringAssert.Contains(lines[1], "gc=");
        CollectionAssert.AreEqual(new[] { "test.on.lap.a", "test.on.lap.b", "test.on.lap" },
            lines.Skip(2).Select(line => line.Split("name=")[1].Split(' ')[0]).ToArray());
        Assert.IsTrue(PerfProbe.TimeOf("test.on.mark") > 0);
        Assert.IsTrue(PerfProbe.MillisecondsOf("test.on.span") >= 4);
        Assert.IsTrue(PerfProbe.MillisecondsOf("test.on.lap") >= PerfProbe.MillisecondsOf("test.on.lap.a"));
    }

    [TestMethod]
    public void ASpanCountsItsOwnThreadsAllocation()
    {
        var lines = Capture(true, () => Task.Run(() => { using (PerfProbe.Span("test.thread.span")) _ = new byte[4_000_000]; }).Wait());
        var kib = int.Parse(lines.Single().Split("alloc-kib=")[1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsTrue(kib >= 3900, lines.Single());
    }

    [TestMethod]
    public void EnvironmentSwitchesAreRead()
    {
        string? Take(string name) => Environment.GetEnvironmentVariable(name);
        var (probe, stages) = (Take("ARC_PERF_PROBE"), Take("ARC_AREA_MAP_SMOKE_ONLY"));
        try
        {
            void Set(string? probeValue, string? stageValue) { Environment.SetEnvironmentVariable("ARC_PERF_PROBE", probeValue); Environment.SetEnvironmentVariable("ARC_AREA_MAP_SMOKE_ONLY", stageValue); }
            Set(null, null); Assert.IsFalse(PerfProbe.FromEnvironment());
            Set("1", null); Assert.IsTrue(PerfProbe.FromEnvironment());
            Set("0", null); Assert.IsFalse(PerfProbe.FromEnvironment());
            Set(null, "map,perf"); Assert.IsTrue(PerfProbe.FromEnvironment());
            Set(null, "perf-menu"); Assert.IsTrue(PerfProbe.FromEnvironment());
            Set(null, "map,menu"); Assert.IsFalse(PerfProbe.FromEnvironment());
        }
        finally { Environment.SetEnvironmentVariable("ARC_PERF_PROBE", probe); Environment.SetEnvironmentVariable("ARC_AREA_MAP_SMOKE_ONLY", stages); }
    }
}
