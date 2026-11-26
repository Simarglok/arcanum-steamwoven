using System.Globalization;
using System.Text;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-DEV-3389: what a key event means for the performance overlay.</summary>
public enum PerfOverlayRoute { PassThrough, Toggle, Consumed }

/// <summary>ARC-DEV-3389: what Ctrl+F shows: nothing, the game's own panel, or Apple's Metal Performance HUD.</summary>
public enum PerfOverlayMode { Off, Overlay, MetalHud }

/// <summary>ARC-DEV-3389: the Ctrl+F cycle, and the system tool that switches the Metal HUD of a running process.</summary>
public static class PerfOverlayCycle
{
    /// <summary>Apple's tool for performance features of a running process (macOS 27).</summary>
    public const string HudTool = "/usr/bin/metalperftrace";
    /// <summary>Apple's variable that shows the HUD from a process's start.</summary>
    public const string LoadVariable = "MTL_HUD_ENABLED";

    /// <summary>The next state: off, the panel, the HUD (only where the system can switch it), off.</summary>
    public static PerfOverlayMode Next(PerfOverlayMode mode, bool metalHud) => mode switch
    {
        PerfOverlayMode.Off => PerfOverlayMode.Overlay,
        PerfOverlayMode.Overlay when metalHud => PerfOverlayMode.MetalHud,
        _ => PerfOverlayMode.Off,
    };

    /// <summary>What a start shows: the HUD when the start's environment asked the system for it and it can be switched, else nothing.</summary>
    public static PerfOverlayMode Start(Func<string, string?> variable, bool metalHud)
        => metalHud && variable(LoadVariable) == "1" ? PerfOverlayMode.MetalHud : PerfOverlayMode.Off;

    /// <summary>The tool's arguments that show or hide the HUD of process <paramref name="processId"/>.</summary>
    public static string[] HudArguments(bool shown, int processId)
        => ["setup", shown ? "--enable" : "--disable", "hud", "--pid", processId.ToString(CultureInfo.InvariantCulture)];
}

/// <summary>ARC-DEV-3389: Ctrl+F shows or hides the performance overlay (no key of the original: F under Ctrl is a control code the
/// game ignores, ARC-INPUT-004). Ctrl alone, no Shift, Alt or Command. The consumed press owns its echoes and its release (also when
/// Ctrl goes up first), so nothing else sees a late F.</summary>
public sealed class PerfOverlayChord
{
    private bool _owned;

    public PerfOverlayRoute Route(bool isF, bool pressed, bool echo, bool ctrl, bool shift, bool alt, bool meta)
    {
        if (!isF) return PerfOverlayRoute.PassThrough;
        if (!pressed)
        {
            var owned = _owned; _owned = false;
            return owned ? PerfOverlayRoute.Consumed : PerfOverlayRoute.PassThrough;
        }
        if (echo) return _owned ? PerfOverlayRoute.Consumed : PerfOverlayRoute.PassThrough;
        // A fresh press: a release lost to a focus change never blocks the next one.
        _owned = false;
        if (!ctrl || shift || alt || meta) return PerfOverlayRoute.PassThrough;
        _owned = true;
        return PerfOverlayRoute.Toggle;
    }
}

/// <summary>ARC-DEV-3389: a total that only grows (allocated bytes, processor seconds) as a rate between two samples.</summary>
public struct PerfRate
{
    private long _atMicros;
    private double _total;
    private bool _started;

    /// <summary>The growth per second since the previous sample; null for the first sample, an unreadable total, or a clock or total that
    /// did not go forward.</summary>
    public double? Sample(long nowMicros, double? total)
    {
        if (total is not { } value) { _started = false; return null; }
        var rate = _started && nowMicros > _atMicros && value >= _total ? (value - _total) / ((nowMicros - _atMicros) / 1_000_000.0) : (double?)null;
        _atMicros = nowMicros; _total = value; _started = true;
        return rate;
    }
}

/// <summary>ARC-DEV-3389: the overlay's frame statistics while it is shown: the frame times and the viewer's own work per frame over the
/// last <see cref="WindowSeconds"/> seconds, and when the text is due again. Recording a frame allocates nothing; the viewer makes a new
/// meter at each show, so no frame of the hidden time is counted. Main thread only.</summary>
public sealed class PerfOverlayMeter
{
    public const int WindowSeconds = 3;
    /// <summary>Above this many frames in the window (about 1365 fps) only the newest are kept.</summary>
    public const int Capacity = 4096;
    public const long RefreshMicros = 500_000;

    private readonly ViewerFrameWindow _frames = new(WindowSeconds, Capacity);
    private long _dueMicros = long.MinValue;

    public void Record(long frameStartMicros, int gen0, int gen1, int gen2, float workMs) => _frames.Record(frameStartMicros, gen0, gen1, gen2, workMs);

    /// <summary>True once per <see cref="RefreshMicros"/> (and at the first call): the text is to be written again.</summary>
    public bool TakeRefresh(long nowMicros)
    {
        if (nowMicros < _dueMicros) return false;
        _dueMicros = nowMicros + RefreshMicros;
        return true;
    }

    public ViewerFrameStats? Read() => _frames.Read();
}

/// <summary>ARC-DEV-3389: one refresh of the overlay. A null value is a metric this run cannot read: it shows as <c>n/a</c>, never as
/// zero.</summary>
public sealed record PerfOverlayReading(
    ViewerFrameStats? Frames,
    double? ProcessStepMs, double? RenderCpuMs, double? GpuMs,
    long? DrawCalls, long? DrawnObjects, long? Primitives,
    long? ResidentBytes, long? VideoBytes, long? ManagedBytes, double? AllocatedBytesPerSecond, double? CpuPercent,
    long? Nodes, long? Objects,
    int Width, int Height, int? FpsCap = null, bool MetalHudNext = false)
{
    /// <summary>A byte count above this is a broken counter (the Metal texture monitor underflows to 2^63, issue #3333), not memory.</summary>
    public const long ByteCeiling = 1L << 42;

    /// <summary>The overlay's text, in fixed rows so the panel does not jump.</summary>
    public string Text()
    {
        var text = new StringBuilder(640);
        var frames = Frames;
        text.Append("FPS ").Append(frames is { AverageMs: > 0 } f ? Number(1000 / f.AverageMs, "0.0") : "...");
        // The viewer paces itself (the screen's rate while there is activity, lower when idle): a low rate under the cap is not slowness.
        if (FpsCap is { } cap) text.Append("  cap ").Append(cap.ToString(CultureInfo.InvariantCulture));
        text.Append(MetalHudNext ? "   (Ctrl+F: Metal HUD)\n" : "   (Ctrl+F hides)\n");
        text.Append("frame ms   avg ").Append(Number(frames?.AverageMs, "0.0")).Append("  p95 ").Append(Number(frames?.P95Ms, "0.0"))
            .Append("  max ").Append(Number(frames?.MaxMs, "0.0")).Append('\n');
        text.Append("update ms  avg ").Append(Number(frames?.WorkAverageMs, "0.0")).Append("  max ").Append(Number(frames?.WorkMaxMs, "0.0")).Append('\n');
        text.Append("main step ms ").Append(Number(ProcessStepMs, "0.0")).Append(" (worst of 1 s)\n");
        text.Append("render ms  cpu ").Append(Number(RenderCpuMs, "0.00")).Append("  gpu ").Append(Number(GpuMs, "0.00")).Append('\n');
        text.Append("draw calls ").Append(Count(DrawCalls)).Append("  objects ").Append(Count(DrawnObjects)).Append("  prims ").Append(Count(Primitives)).Append('\n');
        text.Append("memory     resident ").Append(Mib(ResidentBytes)).Append("  video ").Append(Mib(VideoBytes)).Append('\n');
        text.Append(".NET       heap ").Append(Mib(ManagedBytes)).Append("  alloc ")
            .Append(AllocatedBytesPerSecond is { } rate ? Number(rate / (1024 * 1024), "0.0") + " MiB/s" : "n/a").Append('\n');
        text.Append("GC per s   gen0 ").Append(Number(frames?.Gen0PerSecond, "0.0")).Append("  gen1 ").Append(Number(frames?.Gen1PerSecond, "0.0"))
            .Append("  gen2 ").Append(Number(frames?.Gen2PerSecond, "0.0")).Append('\n');
        text.Append("CPU ").Append(CpuPercent is { } cpu ? Number(cpu, "0") + "%" : "n/a").Append("  nodes ").Append(Count(Nodes))
            .Append("  Godot objects ").Append(Count(Objects)).Append('\n');
        text.Append("window ").Append(Width.ToString(CultureInfo.InvariantCulture)).Append('x').Append(Height.ToString(CultureInfo.InvariantCulture))
            .Append("  last ").Append(PerfOverlayMeter.WindowSeconds.ToString(CultureInfo.InvariantCulture)).Append(" s");
        return text.ToString();
    }

    private static string Number(double? value, string format) => value is { } v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "n/a";

    private static string Count(long? value) => value is { } v and >= 0 ? v.ToString(CultureInfo.InvariantCulture) : "n/a";

    private static string Mib(long? bytes) => bytes is { } v and >= 0 and < ByteCeiling ? (v / (1024.0 * 1024)).ToString("0", CultureInfo.InvariantCulture) + " MiB" : "n/a";
}
