using System.Globalization;
using System.Text;
using Arcanum.Session;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-DEV-051: what a line of the bug report's log came from: an engine log message, an engine error or warning, or a
/// session event the viewer records (a map, cell, combat or window change, a message-log entry, fired queue events).</summary>
public enum BugReportLineKind { Log, Error, Event }

/// <summary>One kept line: its wall-clock time (UTC), its monotonic time and its text (one line, bounded).</summary>
public sealed record BugReportLine(DateTime AtUtc, long AtMilliseconds, BugReportLineKind Kind, string Text);

/// <summary>Bounds of the in-memory log: the window kept (120 s), and the lines, characters per line and characters in all a burst
/// may hold before the oldest go early.</summary>
public sealed record BugReportLogLimits
{
    public int WindowMilliseconds { get; init; } = 120_000;
    public int MaxLines { get; init; } = 20_000;
    public int MaxLineChars { get; init; } = 2_000;
    public long MaxChars { get; init; } = 4_000_000;

    public static BugReportLogLimits Default { get; } = new();

    public BugReportLogLimits Validate() => WindowMilliseconds is >= 1_000 and <= 600_000 && MaxLines is >= 16 and <= 1_000_000
        && MaxLineChars is >= 80 and <= 100_000 && MaxChars >= MaxLineChars && MaxChars <= 64_000_000 ? this
        : throw new ArgumentOutOfRangeException(nameof(BugReportLogLimits), "Bug report log limits are out of range.");
}

/// <summary>
/// ARC-DEV-051: the bug report's ring buffer. It keeps, at all times, the lines of the last <see cref="BugReportLogLimits.WindowMilliseconds"/>
/// (120 s), in memory only: older lines go when a line is added or the window is read, and a burst beyond the line or character
/// bound drops its oldest lines early (<see cref="Dropped"/> counts them). A line is one line of text (line breaks become " | ",
/// control characters are dropped) cut to <see cref="BugReportLogLimits.MaxLineChars"/>. Thread-safe: the engine logs from any
/// thread. <see cref="Process"/> is the process's buffer the viewer's engine logger fills.
/// </summary>
public sealed class BugReportLog
{
    private readonly object _gate = new();
    private readonly Queue<BugReportLine> _lines = new();
    private readonly Func<long> _monotonicMilliseconds;
    private readonly Func<DateTime> _utcNow;
    private long _chars;
    private long _dropped;

    public BugReportLog(BugReportLogLimits? limits = null, Func<long>? monotonicMilliseconds = null, Func<DateTime>? utcNow = null)
    {
        Limits = (limits ?? BugReportLogLimits.Default).Validate();
        _monotonicMilliseconds = monotonicMilliseconds ?? (() => Environment.TickCount64);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>The process's buffer: the viewer's engine logger and its session-event lines write here from the first frame.</summary>
    public static BugReportLog Process { get; } = new();

    public BugReportLogLimits Limits { get; }

    /// <summary>Lines dropped before their 120 s were up, because a burst passed the line or character bound.</summary>
    public long Dropped { get { lock (_gate) return _dropped; } }

    public void Add(BugReportLineKind kind, string? text)
    {
        var line = Clean(text ?? "", Limits.MaxLineChars);
        if (line.Length == 0) return;
        var now = _monotonicMilliseconds();
        var entry = new BugReportLine(_utcNow(), now, kind, line);
        lock (_gate)
        {
            _lines.Enqueue(entry); _chars += line.Length;
            Trim(now);
        }
    }

    /// <summary>The kept lines of the last window, oldest first.</summary>
    public IReadOnlyList<BugReportLine> Recent()
    {
        var now = _monotonicMilliseconds();
        lock (_gate)
        {
            Trim(now);
            return _lines.ToArray();
        }
    }

    private void Trim(long now)
    {
        while (_lines.Count > 0 && now - _lines.Peek().AtMilliseconds > Limits.WindowMilliseconds) _chars -= _lines.Dequeue().Text.Length;
        while (_lines.Count > Limits.MaxLines || _chars > Limits.MaxChars) { _chars -= _lines.Dequeue().Text.Length; _dropped++; }
    }

    /// <summary>One line: line breaks joined with " | ", other control characters dropped, trailing blanks trimmed, cut with "…".</summary>
    public static string Clean(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(Math.Min(text.Length, maxChars + 1));
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var part in lines)
        {
            var trimmed = part.TrimEnd();
            if (trimmed.Length == 0) continue;
            if (builder.Length > 0) builder.Append(" | ");
            foreach (var c in trimmed) if (!char.IsControl(c) || c == '\t') builder.Append(c == '\t' ? ' ' : c);
            if (builder.Length > maxChars) break;
        }
        return builder.Length <= maxChars ? builder.ToString() : builder.ToString(0, maxChars - 1) + "…";
    }
}

/// <summary>ARC-DEV-051: the game state a report names (each a one-line text the host composes), and further rows (label, value).</summary>
public sealed record BugReportFacts(string Build, string Rules, string Map, string Hero, string Combat, IReadOnlyList<(string Label, string Value)> Other);

/// <summary>The save a report carries: the session as it was when the report key was pressed, its save-list details and preview.</summary>
public sealed record BugReportSave(GameSessionSnapshot Snapshot, GameSaveSummary Summary, byte[]? Thumbnail);

/// <summary>
/// Everything one report writes. <see cref="Screenshot"/> (PNG) and <see cref="Save"/> are null when they could not be taken, with
/// the reason in <see cref="ScreenshotMissing"/> and <see cref="SaveMissing"/>; <see cref="Log"/> is the buffer's last window when
/// the key was pressed. <see cref="TakenUtc"/> and <see cref="TakenLocal"/> are that moment. <see cref="Check"/> (ARC-DEV-2189) is the
/// owner check the report belongs to, <c>ARC-TYPE-NNN#k</c> (<see cref="BugReportCheck"/>); null for an ordinary report.
/// </summary>
public sealed record BugReportContent(string Text, BugReportFacts Facts, IReadOnlyList<BugReportLine> Log, long LogDropped, DateTime TakenUtc,
    DateTime TakenLocal, byte[]? Screenshot, string? ScreenshotMissing, BugReportSave? Save, string? SaveMissing, string? Check = null);

/// <summary>Where a report may go: <see cref="Primary"/> (the <c>reports</c> folder beside the launched game) unless
/// <see cref="PrimaryRefusal"/> says why it cannot be used, then <see cref="Fallback"/> (the user folder's <c>reports</c>). A run that
/// is not a packaged game has no primary and uses the fallback as its own place.</summary>
public sealed record BugReportPlaces(string? Primary, string? PrimaryRefusal, string Fallback);

/// <summary>One report's outcome: the folder written (null when no folder could be made), whether it is the fallback and why, the
/// files written and the parts that failed (each with its reason).</summary>
public sealed record BugReportResult(string? Folder, bool Fallback, string? FallbackReason, IReadOnlyList<string> Written, IReadOnlyList<string> Failed)
{
    public bool Saved => Folder is not null && Written.Contains(BugReportWriter.ReportFile);

    /// <summary>The line the window and the log show.</summary>
    public string Status => Folder is null ? "the report could not be written: " + string.Join("; ", Failed)
        : (Saved ? "report saved to " : "report INCOMPLETE in ") + Folder
          + (Fallback ? $" (the folder beside the game could not be used: {FallbackReason})" : "")
          + (Failed.Count == 0 ? "" : "; not written: " + string.Join("; ", Failed));
}

/// <summary>ARC-DEV-051: where a packaged game was launched from and whether that folder may take reports.</summary>
public static class BugReportLocation
{
    public const string FolderName = "reports";

    /// <summary>The folder the player launched the game from: on macOS the folder holding the <c>.app</c> bundle (the executable
    /// is <c>X.app/Contents/MacOS/X</c>), elsewhere the executable's folder; null for a path without a folder.</summary>
    public static string? LaunchFolder(string executablePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);
        var folder = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (folder is null) return null;
        var contents = Path.GetDirectoryName(folder);
        var bundle = contents is null ? null : Path.GetDirectoryName(contents);
        if (bundle is not null && Path.GetFileName(folder) == "MacOS" && Path.GetFileName(contents) == "Contents"
            && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(bundle);
        return folder;
    }

    /// <summary>The places of a report. <paramref name="packaged"/>: a packaged game (its launch folder is the primary place);
    /// otherwise the user folder's reports are the only place. macOS runs a quarantined app from a random read-only copy (App
    /// Translocation), never beside the app the player sees, so a translocated path is refused before anything is tried.</summary>
    public static BugReportPlaces Places(bool packaged, string executablePath, string userReports)
    {
        ArgumentException.ThrowIfNullOrEmpty(userReports);
        if (!packaged) return new(null, null, userReports);
        if (string.IsNullOrEmpty(executablePath)) return new(null, "the game's folder is not known", userReports);
        if (executablePath.Contains("/AppTranslocation/", StringComparison.Ordinal))
            return new(null, "macOS runs this app from a read-only translocated copy (move the app to another folder once, or remove its quarantine mark)", userReports);
        var launch = LaunchFolder(executablePath);
        return launch is null ? new(null, "the game's folder is not known", userReports) : new(Path.Combine(launch, FolderName), null, userReports);
    }
}

/// <summary>
/// ARC-DEV-051: writes one bug report folder, <c>reports/YYYY-MM-DD_HH-MM-SS</c> (local time; <c>_2</c>, <c>_3</c>… when the second
/// is taken), with <c>screenshot.png</c>, <c>log.txt</c> (the last 120 s), the save (<c>save/slot0000.json</c> with its details and
/// preview: a one-save save list the ordinary loader reads) and <c>report.md</c> last, which names the player's text, the build, the
/// game state and what is in the folder or missing. The primary place is tried first; a primary that cannot take the folder (not
/// writable, a protected or read-only place) gives the fallback with the reason. A part that fails after the folder exists is listed
/// and the rest is still written. No upload. No Godot.
/// </summary>
public static class BugReportWriter
{
    public const string ReportFile = "report.md";
    public const string LogFile = "log.txt";
    public const string ScreenshotFile = "screenshot.png";
    public const string SaveFolder = "save";
    public const string StampFormat = "yyyy-MM-dd_HH-mm-ss";
    private const int MaxSameSecond = 99;
    private static int WindowSeconds => BugReportLogLimits.Default.WindowMilliseconds / 1000;

    public static BugReportResult Write(BugReportContent content, BugReportPlaces places)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(places);
        var stamp = content.TakenLocal.ToString(StampFormat, CultureInfo.InvariantCulture);
        var failed = new List<string>();
        string? fallbackReason = places.PrimaryRefusal;
        var folder = places.Primary is { } primary && fallbackReason is null ? CreateFolder(primary, stamp, out fallbackReason) : null;
        // The fallback stands in for a primary place; a run without one (not a packaged game) uses it as its own place.
        var fallback = folder is null && (places.Primary is not null || places.PrimaryRefusal is not null);
        if (folder is null)
        {
            folder = CreateFolder(places.Fallback, stamp, out var refusal);
            if (folder is null)
            {
                if (fallback) failed.Add($"beside the game: {fallbackReason}");
                failed.Add($"{places.Fallback}: {refusal}");
                return new(null, fallback, fallbackReason, [], failed);
            }
        }
        if (!fallback) fallbackReason = null;
        string dir = folder;
        var written = new List<string>();
        void File(string name, Func<string?> write)
        {
            var problem = write();
            if (problem is null) written.Add(name); else failed.Add($"{name}: {problem}");
        }
        if (content.Screenshot is { } png) File(ScreenshotFile, () => Bytes(Path.Combine(dir, ScreenshotFile), png));
        File(LogFile, () => Text(Path.Combine(dir, LogFile), LogText(content)));
        string? saveName = null;
        if (content.Save is { } save)
        {
            var result = new GameSaveLibrary(Path.Combine(dir, SaveFolder)).Create(save.Snapshot, save.Summary, save.Thumbnail);
            if (result.Saved) { saveName = result.Name; written.Add($"{SaveFolder}/{result.Name}.json"); if (result.Warning is not null) failed.Add($"{SaveFolder} details: {result.Warning}"); }
            else failed.Add($"{SaveFolder}: {AreaQuickSave.Describe(result)}");
        }
        File(ReportFile, () => Text(Path.Combine(dir, ReportFile), Markdown(content, saveName, written, failed, fallbackReason)));
        return new(dir, fallback, fallbackReason, written, failed);
    }

    /// <summary>Makes <c>root/stamp</c> (or <c>stamp_N</c>), null with the reason when the root cannot take it.</summary>
    private static string? CreateFolder(string root, string stamp, out string? refusal)
    {
        refusal = null;
        try
        {
            Directory.CreateDirectory(root);
            for (var n = 1; n <= MaxSameSecond; n++)
            {
                var path = Path.Combine(root, n == 1 ? stamp : $"{stamp}_{n}");
                if (Directory.Exists(path) || System.IO.File.Exists(path)) continue;
                Directory.CreateDirectory(path);
                // Some read-only or protected places let a folder be listed but not a file be written: prove it now.
                var probe = Path.Combine(path, ".write-test");
                try
                {
                    System.IO.File.WriteAllBytes(probe, []);
                    System.IO.File.Delete(probe);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Leave nothing behind in a place that cannot take the report.
                    try { Directory.Delete(path, recursive: true); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                    throw;
                }
                return path;
            }
            refusal = $"{MaxSameSecond} reports in the same second";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            refusal = $"{error.GetType().Name}: {error.Message}";
        }
        return null;
    }

    private static string? Bytes(string path, byte[] bytes)
    {
        try { System.IO.File.WriteAllBytes(path, bytes); return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return error.GetType().Name + ": " + error.Message; }
    }

    private static string? Text(string path, string text) => Bytes(path, new UTF8Encoding(false).GetBytes(text));

    /// <summary>log.txt: a header, then one line per kept line: its UTC time, seconds before the report key, kind and text.</summary>
    public static string LogText(BugReportContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"# Arcanum Steamwoven bug report log: the last {WindowSeconds} s before the report key, {content.Log.Count} lines")
            .Append(content.LogDropped > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({content.LogDropped} older lines of a burst dropped early)") : "").Append('\n')
            .Append("# columns: UTC time | seconds before the report key | log, error or event | text\n");
        foreach (var line in content.Log)
        {
            var before = (content.TakenUtc - line.AtUtc).TotalSeconds;
            builder.Append(line.AtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append(' ')
                .Append(Math.Max(0, before).ToString("-0.000", CultureInfo.InvariantCulture).PadLeft(8)).Append(" s ")
                .Append(line.Kind switch { BugReportLineKind.Error => "error", BugReportLineKind.Event => "event", _ => "log  " }).Append(' ')
                .Append(line.Text).Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>report.md: the player's text, the game state, the folder's files and how to read them.</summary>
    public static string Markdown(BugReportContent content, string? saveName, IReadOnlyList<string> written, IReadOnlyList<string> failed, string? fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(content);
        var builder = new StringBuilder();
        var local = content.TakenLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        builder.Append(CultureInfo.InvariantCulture, $"# Bug report {local}\n\n");
        // ARC-DEV-2189: one machine-readable line before the first heading; only a well-formed step id is written.
        if (BugReportCheck.Normalize(content.Check) is { } check) builder.Append(BugReportCheck.LinePrefix).Append(check).Append("\n\n");
        builder.Append("## What the player wrote\n\n");
        var text = content.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length == 0) builder.Append("(no text)\n");
        else foreach (var line in text.Split('\n')) builder.Append("> ").Append(line).Append('\n');
        builder.Append("\n## Game state when the report key was pressed\n\n| | |\n| --- | --- |\n");
        void Row(string label, string value) => builder.Append("| ").Append(Cell(label)).Append(" | ").Append(Cell(value)).Append(" |\n");
        Row("Build", content.Facts.Build);
        Row("Taken", $"{local} local time, {content.TakenUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}");
        Row("Rules", content.Facts.Rules);
        Row("Map", content.Facts.Map);
        Row("Hero", content.Facts.Hero);
        Row("Combat", content.Facts.Combat);
        foreach (var (label, value) in content.Facts.Other) Row(label, value);
        builder.Append("\n## Files\n\n");
        builder.Append($"- `{LogFile}`: ").Append(written.Contains(LogFile)
            ? string.Create(CultureInfo.InvariantCulture, $"the last {WindowSeconds} s of the game's log and session events, {content.Log.Count} lines.") : "not written.").Append('\n');
        builder.Append($"- `{ScreenshotFile}`: ").Append(written.Contains(ScreenshotFile) ? "the screen as it was when the key was pressed (before the report window opened)."
            : "missing: " + (content.ScreenshotMissing ?? "not written") + ".").Append('\n');
        builder.Append(saveName is not null
            ? $"- `{SaveFolder}/{saveName}.json` (with `{saveName}.meta.json` and `{saveName}.png`): the game saved when the key was pressed, a save of the ordinary save list. A window open then (see Windows above) is not in the save: a save holds the game, not an open dialog, barter or map window. To load it, copy the files into the game's save folder (`saves/map/` in the packaged game) under a free slot number and choose it in Load Game.\n"
            : $"- `{SaveFolder}/`: no save: {content.SaveMissing ?? "not written"}.\n");
        if (fallbackReason is not null) builder.Append(CultureInfo.InvariantCulture, $"\nThis folder is in the user folder because the folder beside the game could not be used: {fallbackReason}.\n");
        if (failed.Count > 0)
        {
            builder.Append("\n## Not written\n\n");
            foreach (var failure in failed) builder.Append("- ").Append(failure).Append('\n');
        }
        builder.Append("\n## Reading this report\n\nStart with the player's text and the screenshot, then the game state above, then `log.txt` from its end backwards ")
            .Append("(the last lines are the moments before the key). The save reproduces the moment. The folder holds the player's original game art and texts: ")
            .Append("never commit or publish it.\n");
        return builder.ToString();
    }

    private static string Cell(string text) => BugReportLog.Clean(text, 1_000).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);
}

/// <summary>ARC-DEV-051: the save a report takes when its key is pressed.</summary>
public static class BugReportCapture
{
    /// <summary>The actor's placement recorded as F7 records it (<see cref="AreaQuickSave"/>), then the whole session captured with
    /// its save-list details; a refused placement gives no save and the reason.</summary>
    public static (BugReportSave? Save, string? Refusal) Save(GameSessionOwner session, ActorPlacement actor, Func<GameSessionOwner, GameSaveSummary> summary,
        byte[]? thumbnail)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(summary);
        var placed = session.PlaceActor(session.Version, actor);
        if (placed.Disposition == GameSessionDisposition.Rejected) return (null, $"actor placement refused ({placed.Failure})");
        return (new(session.Capture(), summary(session), thumbnail), null);
    }
}
