using System.Globalization;
using System.Text;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-DEV-2279: the window a soak run opens and holds idle (<c>ARC_SOAK_WINDOW</c>).</summary>
public enum ViewerSoakWindow
{
    /// <summary>No window is opened: the run only logs counters wherever the viewer stands.</summary>
    None,
    /// <summary>The New Character window (menu window 12, the race, gender, background and portrait choice).</summary>
    NewCharacter,
    /// <summary>The character editor in creation mode (menu window 14, the art-heaviest creation window).</summary>
    Editor,
}

/// <summary>
/// ARC-DEV-2279: the soak run's settings, read from environment variables (a development opt-in, never a command-line argument, so
/// the viewer's argument contract and the exported builds' launchers do not change). <c>ARC_SOAK_LOG</c> (a file path) turns it on;
/// <c>ARC_SOAK_WINDOW</c> (<c>new-character</c> or <c>editor</c>) opens that window over the main menu and leaves it idle;
/// <c>ARC_SOAK_INTERVAL</c> (seconds, default 30) is the counters' period; <c>ARC_SOAK_MINUTES</c> (optional) ends the viewer by
/// itself after that long, with a summary line.
/// </summary>
public sealed record ViewerSoakSettings(string LogPath, int IntervalSeconds, int? RunMinutes, ViewerSoakWindow Window)
{
    public const string LogVariable = "ARC_SOAK_LOG";
    public const string WindowVariable = "ARC_SOAK_WINDOW";
    public const string IntervalVariable = "ARC_SOAK_INTERVAL";
    public const string MinutesVariable = "ARC_SOAK_MINUTES";
    public const int DefaultIntervalSeconds = 30;
    public const int MaxIntervalSeconds = 3600;
    public const int MaxRunMinutes = 24 * 60;

    /// <summary>Reads the settings; null when <c>ARC_SOAK_LOG</c> is unset (no soak), else the settings or, with <paramref name="problem"/>
    /// set, null for a value that cannot be used (the viewer then logs the problem and runs without a soak).</summary>
    public static ViewerSoakSettings? Read(Func<string, string?> get, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(get);
        problem = null;
        var path = get(LogVariable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.Any(char.IsControl)) { problem = $"{LogVariable} has a control character"; return null; }
        var window = ViewerSoakWindow.None;
        if (get(WindowVariable) is { Length: > 0 } name)
        {
            switch (name)
            {
                case "new-character": window = ViewerSoakWindow.NewCharacter; break;
                case "editor": window = ViewerSoakWindow.Editor; break;
                default: problem = $"{WindowVariable} must be new-character or editor, not '{name}'"; return null;
            }
        }
        var interval = DefaultIntervalSeconds;
        if (get(IntervalVariable) is { Length: > 0 } intervalText
            && (!int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out interval) || interval is < 1 or > MaxIntervalSeconds))
        { problem = $"{IntervalVariable} must be 1 to {MaxIntervalSeconds} seconds, not '{intervalText}'"; return null; }
        int? minutes = null;
        if (get(MinutesVariable) is { Length: > 0 } minutesText)
        {
            if (!int.TryParse(minutesText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > MaxRunMinutes)
            { problem = $"{MinutesVariable} must be 1 to {MaxRunMinutes} minutes, not '{minutesText}'"; return null; }
            minutes = parsed;
        }
        return new(path, interval, minutes, window);
    }
}

/// <summary>ARC-DEV-2279: one reading of the process's resource counters. Byte counts are whole bytes; <see cref="Window"/> is what the
/// menu showed at the reading (the window name, or "none" while no menu stands). <see cref="GcHeapBytes"/> counts garbage not yet collected
/// (it saw-tooths with the collections); <see cref="GcLiveBytes"/> is what survived the last full collection (the leak signal).</summary>
public sealed record ViewerSoakSample(
    int ElapsedSeconds, string Window, double Fps,
    long TextureBytes, long VideoBytes, long BufferBytes,
    long Objects, long Resources, long Nodes, long OrphanNodes,
    long GodotStaticBytes, long GcHeapBytes, long GcLiveBytes, int Gen0, int Gen1, int Gen2, long WorkingSetBytes, int Threads)
{
    /// <summary>The CSV header of <see cref="Line"/>.</summary>
    public const string Header = "elapsed_s,window,fps,texture_bytes,video_bytes,buffer_bytes,objects,resources,nodes,orphan_nodes,godot_static_bytes,gc_heap_bytes,gc_live_bytes,gc0,gc1,gc2,working_set_bytes,threads";

    /// <summary>The reading as one CSV row (invariant culture, no quoting needed: the window is an enum name).</summary>
    public string Line()
    {
        var c = CultureInfo.InvariantCulture;
        return string.Join(',', ElapsedSeconds.ToString(c), Window, Fps.ToString("0.0", c),
            TextureBytes.ToString(c), VideoBytes.ToString(c), BufferBytes.ToString(c),
            Objects.ToString(c), Resources.ToString(c), Nodes.ToString(c), OrphanNodes.ToString(c),
            GodotStaticBytes.ToString(c), GcHeapBytes.ToString(c), GcLiveBytes.ToString(c), Gen0.ToString(c), Gen1.ToString(c), Gen2.ToString(c),
            WorkingSetBytes.ToString(c), Threads.ToString(c));
    }
}

/// <summary>ARC-DEV-2279: the summary of a soak run: for each counter its first and last reading after the warm-up and the change, so
/// a leak (a counter that only climbs) reads at a glance, and the log's rows give the shape.</summary>
public static class ViewerSoakReport
{
    /// <summary>Seconds at the start that count as warm-up (caches fill; the 2274 idle run was flat after two minutes).</summary>
    public const int WarmupSeconds = 120;

    /// <summary>The one-line summary; the readings before <see cref="WarmupSeconds"/> are left out (all of them when the run was shorter,
    /// with the note).</summary>
    public static string Summary(IReadOnlyList<ViewerSoakSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0) return "ARC-DEV-2279 soak summary: no samples";
        var steady = samples.Where(sample => sample.ElapsedSeconds >= WarmupSeconds).ToList();
        var note = "";
        if (steady.Count < 2) { steady = [.. samples]; note = $" (run shorter than {WarmupSeconds}s of warm-up: all readings used)"; }
        var first = steady[0]; var last = steady[^1];
        var text = new StringBuilder("ARC-DEV-2279 soak summary: ");
        text.Append(CultureInfo.InvariantCulture, $"samples={samples.Count} span={last.ElapsedSeconds}s window={last.Window}{note}");
        void Counter(string name, long from, long to, double scale = 1, string unit = "")
        {
            var c = CultureInfo.InvariantCulture;
            text.Append(c, $" | {name} {(from / scale).ToString("0.0", c)}{unit} -> {(to / scale).ToString("0.0", c)}{unit} ({((to - from) / scale).ToString("+0.0;-0.0;0.0", c)}{unit})");
        }
        const double Mib = 1024 * 1024;
        Counter("texture", first.TextureBytes, last.TextureBytes, Mib, "MiB");
        Counter("video", first.VideoBytes, last.VideoBytes, Mib, "MiB");
        Counter("godotStatic", first.GodotStaticBytes, last.GodotStaticBytes, Mib, "MiB");
        Counter("gcHeap", first.GcHeapBytes, last.GcHeapBytes, Mib, "MiB");
        Counter("gcLive", first.GcLiveBytes, last.GcLiveBytes, Mib, "MiB");
        Counter("workingSet", first.WorkingSetBytes, last.WorkingSetBytes, Mib, "MiB");
        Counter("objects", first.Objects, last.Objects);
        Counter("resources", first.Resources, last.Resources);
        Counter("nodes", first.Nodes, last.Nodes);
        Counter("orphanNodes", first.OrphanNodes, last.OrphanNodes);
        Counter("threads", first.Threads, last.Threads);
        return text.ToString();
    }
}
