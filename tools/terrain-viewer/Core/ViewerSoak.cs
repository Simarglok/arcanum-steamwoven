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
    /// <summary>ARC-DEV-1398: the length of the <c>soak</c> smoke stage in minutes (<c>ARC_SOAK_STAGE_MINUTES</c>).</summary>
    public const string StageMinutesVariable = "ARC_SOAK_STAGE_MINUTES";
    public const int DefaultStageMinutes = 6;
    /// <summary>The shortest stage: the 120 s warm-up plus two minutes of readings, so the verdict has at least two readings after the warm-up.</summary>
    public const int MinStageMinutes = 4;
    public const int MaxStageMinutes = 120;

    /// <summary>ARC-DEV-1398: the soak stage's length: the variable (<see cref="MinStageMinutes"/> to <see cref="MaxStageMinutes"/>) or <see cref="DefaultStageMinutes"/>;
    /// null with <paramref name="problem"/> set for a value that cannot be used. The default leaves four minutes after the 120 s warm-up.</summary>
    public static int? StageMinutes(Func<string, string?> get, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(get);
        problem = null;
        if (get(StageMinutesVariable) is not { Length: > 0 } text) return DefaultStageMinutes;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes is >= MinStageMinutes and <= MaxStageMinutes) return minutes;
        problem = $"{StageMinutesVariable} must be {MinStageMinutes} to {MaxStageMinutes} minutes, not '{text}'";
        return null;
    }

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
    long GodotStaticBytes, long GcHeapBytes, long GcLiveBytes, int Gen0, int Gen1, int Gen2, long WorkingSetBytes, int Threads, int OpenFiles = -1)
{
    /// <summary>The CSV header of <see cref="Line"/>.</summary>
    public const string Header = "elapsed_s,window,fps,texture_bytes,video_bytes,buffer_bytes,objects,resources,nodes,orphan_nodes,godot_static_bytes,gc_heap_bytes,gc_live_bytes,gc0,gc1,gc2,working_set_bytes,threads,open_files";

    /// <summary>The reading as one CSV row (invariant culture, no quoting needed: the window is an enum name).</summary>
    public string Line()
    {
        var c = CultureInfo.InvariantCulture;
        return string.Join(',', ElapsedSeconds.ToString(c), Window, Fps.ToString("0.0", c),
            TextureBytes.ToString(c), VideoBytes.ToString(c), BufferBytes.ToString(c),
            Objects.ToString(c), Resources.ToString(c), Nodes.ToString(c), OrphanNodes.ToString(c),
            GodotStaticBytes.ToString(c), GcHeapBytes.ToString(c), GcLiveBytes.ToString(c), Gen0.ToString(c), Gen1.ToString(c), Gen2.ToString(c),
            WorkingSetBytes.ToString(c), Threads.ToString(c), OpenFiles.ToString(c));
    }

    /// <summary>ARC-DEV-1398: the reading as the one resource line the viewer's log prints every half minute and the bug report carries
    /// (the CSV row holds the same numbers; this is the form a person reads).</summary>
    public string Text()
    {
        var c = CultureInfo.InvariantCulture;
        const double Mib = 1024 * 1024;
        string M(long bytes) => (bytes / Mib).ToString("0.0", c) + "MiB";
        return $"{ViewerResourceLine.Prefix} t={ElapsedSeconds}s fps={Fps.ToString("0.0", c)} texture={M(TextureBytes)} video={M(VideoBytes)} buffer={M(BufferBytes)}"
            + $" objects={Objects} resources={Resources} nodes={Nodes} orphans={OrphanNodes} godotStatic={M(GodotStaticBytes)}"
            + $" gcLive={M(GcLiveBytes)} gcHeap={M(GcHeapBytes)} gc={Gen0}/{Gen1}/{Gen2} workingSet={M(WorkingSetBytes)} threads={Threads}"
            + $" openFiles={(OpenFiles < 0 ? "?" : OpenFiles.ToString(c))}";
    }
}

/// <summary>ARC-DEV-1398: the resource line and the reading of the one counter Godot has no monitor for.</summary>
public static class ViewerResourceLine
{
    /// <summary>The start of every resource line in the viewer log (grep this).</summary>
    public const string Prefix = "ARC-DEV-1398 resources";
    /// <summary>The period of the resource line when no soak interval is set.</summary>
    public const int DefaultIntervalSeconds = 30;

    /// <summary>How many file descriptors the process has open (the listing of <c>/dev/fd</c>, which holds the descriptor of the listing
    /// itself: a constant one more), or -1 where the system has no such folder (Windows) or it cannot be read.</summary>
    public static int OpenFileDescriptors()
    {
        try
        {
            foreach (var folder in new[] { "/dev/fd", "/proc/self/fd" })
                if (Directory.Exists(folder)) return Directory.EnumerateFileSystemEntries(folder).Count();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
        return -1;
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
        void Counter(string name, Func<ViewerSoakSample, long> read, double scale = 1, string unit = "")
        {
            var c = CultureInfo.InvariantCulture;
            long from = read(first), to = read(last), max = steady.Max(read);
            text.Append(c, $" | {name} {(from / scale).ToString("0.0", c)}{unit} -> {(to / scale).ToString("0.0", c)}{unit} ({((to - from) / scale).ToString("+0.0;-0.0;0.0", c)}{unit}) max {(max / scale).ToString("0.0", c)}{unit}");
        }
        const double Mib = 1024 * 1024;
        Counter("texture", x => x.TextureBytes, Mib, "MiB");
        Counter("video", x => x.VideoBytes, Mib, "MiB");
        Counter("godotStatic", x => x.GodotStaticBytes, Mib, "MiB");
        Counter("gcHeap", x => x.GcHeapBytes, Mib, "MiB");
        Counter("gcLive", x => x.GcLiveBytes, Mib, "MiB");
        Counter("workingSet", x => x.WorkingSetBytes, Mib, "MiB");
        Counter("objects", x => x.Objects);
        Counter("resources", x => x.Resources);
        Counter("nodes", x => x.Nodes);
        Counter("orphanNodes", x => x.OrphanNodes);
        Counter("threads", x => x.Threads);
        if (first.OpenFiles >= 0 && last.OpenFiles >= 0) Counter("openFiles", x => x.OpenFiles);
        return text.ToString();
    }
}

/// <summary>
/// ARC-DEV-1398: the verdict of a scripted soak (the <c>soak</c> smoke stage). A counter that grows by more than its allowance between
/// the first and the last reading after the warm-up fails the run; the allowances are loose (a cache that fills while the player's
/// route is walked for the first time is no leak) and are the things to tune when a real run shows a counter that is flat by nature.
/// </summary>
public static class ViewerSoakLimits
{
    private const long Mib = 1024 * 1024;

    /// <summary>The counters and their allowed growth (in the unit of the counter: bytes, or a count).</summary>
    public static IReadOnlyList<(string Name, long Allowed, Func<ViewerSoakSample, long> Read)> Allowances { get; } =
    [
        ("gcLive", 48 * Mib, x => x.GcLiveBytes), ("godotStatic", 48 * Mib, x => x.GodotStaticBytes),
        ("texture", 64 * Mib, x => x.TextureBytes), ("video", 64 * Mib, x => x.VideoBytes), ("workingSet", 256 * Mib, x => x.WorkingSetBytes),
        ("objects", 2000, x => x.Objects), ("resources", 400, x => x.Resources), ("nodes", 400, x => x.Nodes), ("orphanNodes", 20, x => x.OrphanNodes),
        ("threads", 6, x => x.Threads), ("openFiles", 16, x => x.OpenFiles),
    ];

    /// <summary>The counters that grew past their allowance between the first and last reading at or after the warm-up; the single entry
    /// "too few readings after the warm-up" when fewer than two readings are left (a run that short proves nothing). Empty: the soak holds.
    /// A counter the system cannot read (open files -1) is left out.</summary>
    public static IReadOnlyList<string> Exceeded(IReadOnlyList<ViewerSoakSample> samples, int warmupSeconds = ViewerSoakReport.WarmupSeconds)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var steady = samples.Where(sample => sample.ElapsedSeconds >= warmupSeconds).ToList();
        if (steady.Count < 2) return ["too few readings after the " + warmupSeconds.ToString(CultureInfo.InvariantCulture) + "s warm-up (" + steady.Count.ToString(CultureInfo.InvariantCulture) + ")"];
        var problems = new List<string>();
        foreach (var (name, allowed, read) in Allowances)
        {
            long from = read(steady[0]), to = read(steady[^1]);
            if (from < 0 || to < 0) continue;
            if (to - from > allowed) problems.Add($"{name} grew by {(to - from).ToString(CultureInfo.InvariantCulture)} (allowed {allowed.ToString(CultureInfo.InvariantCulture)}): {from.ToString(CultureInfo.InvariantCulture)} -> {to.ToString(CultureInfo.InvariantCulture)}");
        }
        return problems;
    }
}
