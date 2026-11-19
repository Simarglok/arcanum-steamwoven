using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One owner check as the QA page writes it into <c>checks.json</c> (ARC-DEV-2186 step format).</summary>
public sealed record OwnerQaStep(string Id, string Section, string Step, string Expected, string ListStatus);

/// <summary>The latest recorded result of a step in <c>results.jsonl</c> (status, note, report folder name, the UTC time of the event).</summary>
public sealed record OwnerQaResult(string Status, string Note, string? Report, string? Ts = null);

/// <summary>Which steps the overlay lists.</summary>
public enum OwnerQaFilter { Open, Section, All }

/// <summary>
/// ARC-DEV-2190 (owner QA epic #2185, slice 5): the files behind the in-game owner-check overlay. The QA folder (<c>local/qa</c> of the
/// main checkout, git-ignored) holds <c>checks.json</c> (the steps, written by <c>./arcanum qa</c> from the list on origin/main),
/// <c>results.jsonl</c> (append-only events of ARC-DEV-2187, the latest event of a step wins) and <c>active-check.txt</c> (the open
/// step, read by Ctrl+B, ARC-DEV-2189). The overlay is on only when the QA folder is found and the owner-QA flag or marker is on:
/// the environment variable <see cref="EnabledVariable"/> = 1 or the file <see cref="MarkerFile"/> in the folder
/// (<c>./arcanum qa overlay on</c>); an automated run needs <see cref="FolderVariable"/> (the isolated folder of a smoke) and never
/// looks beside the game. A normal player build has neither, so it is unchanged. No Godot; every failure is a message, not an exception.
/// </summary>
public static class OwnerQaFiles
{
    public const string EnabledVariable = "ARC_OWNER_QA";
    public const string FolderVariable = "ARC_OWNER_QA_DIR";
    public const string MarkerFile = "owner-qa.on";
    public const string ChecksFile = "checks.json";
    public const string ResultsFile = "results.jsonl";
    public const int MaxNote = 4000;
    private const int MaxLevels = 8;
    private const long MaxFileBytes = 16 * 1024 * 1024;
    /// <summary>The statuses the overlay can record (the page's <c>open</c> is only ever the default).</summary>
    public static readonly string[] Marks = ["same", "differs", "cannot-test"];

    /// <summary>The QA folder when the overlay is on, else null. <paramref name="folderVariable"/> names it directly (any run);
    /// otherwise not an <paramref name="automated"/> run, with the flag or the marker, finds a <c>local/qa</c> above one of the
    /// start folders: the nearest one with the marker, else (flag on) the nearest with a <c>checks.json</c>, else (flag on) the nearest.</summary>
    public static string? FindFolder(IEnumerable<string?> startFolders, bool automated, string? folderVariable, string? enabledVariable)
    {
        ArgumentNullException.ThrowIfNull(startFolders);
        if (!string.IsNullOrEmpty(folderVariable)) return Directory.Exists(folderVariable) ? folderVariable : null;
        if (automated) return null;
        string? firstBare = null;
        foreach (var start in startFolders)
        {
            if (string.IsNullOrEmpty(start)) continue;
            try
            {
                var folder = Path.GetFullPath(start);
                for (var level = 0; level <= MaxLevels && !string.IsNullOrEmpty(folder); level++, folder = Path.GetDirectoryName(folder))
                {
                    var candidate = Path.Combine(folder, "local", "qa");
                    if (!Directory.Exists(candidate)) continue;
                    // ARC-DEV-2236: a nearer `local/qa` that is not the owner's (no marker, no list) is passed over, not taken: the folder is the
                    // first one with the marker, else (flag on) the first one with a list, else (flag on) the first one found.
                    if (File.Exists(Path.Combine(candidate, MarkerFile))) return candidate;
                    if (enabledVariable != "1") continue;
                    if (File.Exists(Path.Combine(candidate, ChecksFile))) return candidate;
                    firstBare ??= candidate;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // an unusable start folder is skipped
            }
        }
        return firstBare;
    }

    /// <summary>The steps of <c>checks.json</c> and the commit of the list; an empty list and a message when it cannot be read.</summary>
    public static (IReadOnlyList<OwnerQaStep> Steps, string Commit, string? Problem) ReadChecks(string folder)
    {
        var path = Path.Combine(folder, ChecksFile);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return ([], "", "no checks.json: run ./arcanum qa once");
            if (info.Length > MaxFileBytes) return ([], "", "checks.json is too large");
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var steps = new List<OwnerQaStep>();
            foreach (var item in root.GetProperty("steps").EnumerateArray())
            {
                string Text(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                var id = Text("id");
                if (BugReportCheck.Normalize(id) is null) continue;
                steps.Add(new(id, Text("section"), Text("step"), Text("expected"), Text("status")));
            }
            return (steps, root.TryGetProperty("commit", out var commit) && commit.ValueKind == JsonValueKind.String ? commit.GetString() ?? "" : "", null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ([], "", "checks.json cannot be read: " + error.Message);
        }
    }

    /// <summary>The latest recorded result of every step (the page's rule: the last valid <c>result</c> event wins; a line that cannot be
    /// read is skipped).</summary>
    public static Dictionary<string, OwnerQaResult> ReadResults(string folder)
    {
        var results = new Dictionary<string, OwnerQaResult>(StringComparer.Ordinal);
        try
        {
            var path = Path.Combine(folder, ResultsFile);
            if (!File.Exists(path) || new FileInfo(path).Length > MaxFileBytes) return results;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out var kind) || kind.GetString() != "result") continue;
                    var step = root.GetProperty("step").GetString();
                    var status = root.GetProperty("status").GetString();
                    if (step is null || status is not ("open" or "same" or "differs" or "cannot-test")) continue;
                    var note = root.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    var report = root.TryGetProperty("report", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } text ? text : null;
                    var ts = root.TryGetProperty("ts", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    results[step] = new(status, note, report, ts);
                }
                catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return results;
    }

    /// <summary>One <c>result</c> event as one line at the end of <c>results.jsonl</c> (a single append, a cut last line closed first):
    /// the format of ARC-DEV-2187 plus <c>build</c> (the build's commit) and <c>src</c> = game. Null when written, else the reason.</summary>
    public static string? AppendResult(string folder, string step, string status, string note, string? report, string build, DateTime utcNow)
    {
        var line = BuildLine(step, status, note, report, build, utcNow, out var problem);
        if (line is null) return problem;
        try
        {
            Directory.CreateDirectory(folder);
            using var guard = TakeLock(folder);
            if (guard is null) return "results.jsonl is busy (the QA page holds its lock), try again";
            WriteLine(folder, line);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
    }

    private static string? BuildLine(string step, string status, string note, string? report, string build, DateTime utcNow, out string? problem)
    {
        problem = null;
        if (BugReportCheck.Normalize(step) is null) { problem = "not a step id"; return null; }
        if (status is not ("open" or "same" or "differs" or "cannot-test")) { problem = "unknown status"; return null; }
        if (note.Length > MaxNote) { problem = $"the note is longer than {MaxNote} characters"; return null; }
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["kind"] = "result", ["ts"] = utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), ["step"] = step, ["status"] = status,
            ["note"] = note.Trim(), ["report"] = string.IsNullOrEmpty(report) ? null : report, ["build"] = build, ["src"] = "game",
        }) + "\n";
    }

    /// <summary>One line at the end of <c>results.jsonl</c>, a cut last line closed first (the caller holds the lock).</summary>
    private static void WriteLine(string folder, string line)
    {
        var path = Path.Combine(folder, ResultsFile);
        var prefix = "";
        if (File.Exists(path))
        {
            using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (existing.Length > 0) { existing.Seek(-1, SeekOrigin.End); if (existing.ReadByte() != '\n') prefix = "\n"; }
        }
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var bytes = Encoding.UTF8.GetBytes(prefix + line);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>ARC-DEV-2768: records the folder name of a report sent for <paramref name="step"/> (the overlay's Send and a Ctrl+B report
    /// tagged with the step), keeping the status the step shows (recorded, else the list's, else open) and its note, so the step counts as
    /// answered. Null when written, else the reason.</summary>
    public static string? RecordReportFor(string folder, string step, string reportFolder, string note, string build, DateTime utcNow)
    {
        if (BugReportCheck.Normalize(step) is null) return "not a step id";
        try
        {
            Directory.CreateDirectory(folder);
            // The read of the status and the append are one step under the page's lock: a mark of the page in between is not overwritten.
            using var guard = TakeLock(folder);
            if (guard is null) return "results.jsonl is busy (the QA page holds its lock), try again";
            var result = ReadResults(folder).GetValueOrDefault(step);
            var status = result?.Status;
            if (status is null)
            {
                var listed = ReadChecks(folder).Steps.FirstOrDefault(item => item.Id == step)?.ListStatus;
                status = listed is "same" or "differs" or "cannot-test" ? listed : "open";
            }
            var kept = note.Trim().Length > 0 ? note.Trim() : result?.Note ?? "";
            if (kept.Length > MaxNote) kept = kept[..MaxNote];          // a long report text must not keep the step from counting as answered
            var line = BuildLine(step, status, kept, reportFolder, build, utcNow, out var problem);
            if (line is null) return problem;
            WriteLine(folder, line);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
    }

    /// <summary>The page's write lock (<c>.lock</c> in the QA folder, an flock of the Python page): a .NET open with FileShare.None takes
    /// the same advisory lock on Unix, so an append of the game and one of the page never overlap (the game writes at its own offset, not
    /// with O_APPEND). Null when it stays held for about two seconds.</summary>
    private static FileStream? TakeLock(string folder)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try { return new FileStream(Path.Combine(folder, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Thread.Sleep(50); }
        }
        return null;
    }

    /// <summary>The step the owner has open (<c>active-check.txt</c>, replaced whole), so Ctrl+B tags its report; null clears it.</summary>
    public static string? WriteActive(string folder, string? step)
    {
        try
        {
            var path = Path.Combine(folder, BugReportCheck.FileName);
            if (step is null) { if (File.Exists(path)) File.Delete(path); return null; }
            if (BugReportCheck.Normalize(step) is null) return "not a step id";
            var temporary = Path.Combine(folder, $".active-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporary, step + "\n");
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
    }
}

/// <summary>
/// ARC-DEV-2190: what the overlay shows and does, without Godot: the list, the results, the current step and the filter. The current
/// step is the open step of the QA folder (<c>active-check.txt</c>; the page and the overlay share it) or, with none, the first open step;
/// it is listed first. Every change of a result or of the current step goes through the files, so the page sees it.
/// </summary>
public sealed class OwnerQaSession
{
    private IReadOnlyList<OwnerQaStep> _steps = [];
    private Dictionary<string, OwnerQaResult> _results = new(StringComparer.Ordinal);
    public OwnerQaSession(string folder, string build) { Folder = folder; Build = build; Reload(); }
    public string Folder { get; }
    public string Build { get; }
    public string ListCommit { get; private set; } = "";
    public string? Problem { get; private set; }
    public string? CurrentId { get; private set; }
    public OwnerQaFilter Filter { get; set; } = OwnerQaFilter.Open;
    /// <summary>ARC-DEV-2768: whether the collapsed "answered" group at the end of the list is expanded.</summary>
    public bool AnsweredShown { get; set; }
    public IReadOnlyList<OwnerQaStep> Steps => _steps;

    /// <summary>Reads the list and the results again; the current step stays when it is still in the list.</summary>
    public void Reload()
    {
        var (steps, commit, problem) = OwnerQaFiles.ReadChecks(Folder);
        Problem = problem;
        if (problem is null || _steps.Count == 0) { _steps = steps; ListCommit = commit; }   // a transient read problem keeps the list in hand
        _results = OwnerQaFiles.ReadResults(Folder);
        if (CurrentId is null || _steps.All(step => step.Id != CurrentId))
        {
            var active = BugReportCheck.ReadActive(Path.Combine(Folder, BugReportCheck.FileName), DateTime.UtcNow);
            // ARC-DEV-2768: an active step that is already answered (its report was sent in an earlier run) is not offered again: the next
            // unanswered step is current, when there is one; the answered one stays reachable in the collapsed group.
            var firstOpen = _steps.FirstOrDefault(step => !IsAnswered(step))?.Id;
            var activeStep = active is not null ? _steps.FirstOrDefault(step => step.Id == active) : null;
            CurrentId = activeStep is not null && (!IsAnswered(activeStep) || firstOpen is null) ? active : firstOpen;
            if (CurrentId is not null && CurrentId != active) OwnerQaFiles.WriteActive(Folder, CurrentId);   // the plate's step is the one Ctrl+B tags
        }
    }

    /// <summary>Writes the current step again (a fresh file time: Ctrl+B ignores a file older than 12 hours, and the page clears it at its start).</summary>
    public void TouchActive() { if (CurrentId is not null) OwnerQaFiles.WriteActive(Folder, CurrentId); }

    public OwnerQaStep? Current => _steps.FirstOrDefault(step => step.Id == CurrentId);
    public OwnerQaResult? ResultOf(string id) => _results.GetValueOrDefault(id);
    /// <summary>The recorded status, else the list's own (open when neither is a status).</summary>
    public string EffectiveStatus(OwnerQaStep step) => _results.TryGetValue(step.Id, out var result) ? result.Status : step.ListStatus is "same" or "differs" or "cannot-test" ? step.ListStatus : "open";

    /// <summary>ARC-DEV-2768: a step is answered when it has a status other than open (marked here, on the page or in the list) or a report
    /// folder recorded for it (a Ctrl+B report tagged with the step counts). Reopening it writes open without a report.</summary>
    public bool IsAnswered(OwnerQaStep step) => EffectiveStatus(step) != "open" || ResultOf(step.Id)?.Report is not null;

    /// <summary>The UTC day (yyyy-MM-dd) of the step's latest recorded event, or "" when it has none (an answer taken from the list).</summary>
    public string AnsweredDate(OwnerQaStep step) => ResultOf(step.Id)?.Ts is { Length: >= 10 } ts ? ts[..10] : "";

    public int AnsweredCount => _steps.Count(IsAnswered);

    /// <summary>The listed steps: the current one first, then the unanswered others of the filter in list order, then (only while
    /// <see cref="AnsweredShown"/>) the answered others of the filter: the "answered" group is collapsed at the end.</summary>
    public IReadOnlyList<OwnerQaStep> Rows()
    {
        var current = Current;
        bool Shown(OwnerQaStep step) => Filter != OwnerQaFilter.Section || (current is not null && step.Section == current.Section);
        var rows = new List<OwnerQaStep>();
        if (current is not null) rows.Add(current);
        var others = _steps.Where(step => step.Id != CurrentId && Shown(step)).ToList();
        rows.AddRange(others.Where(step => !IsAnswered(step)));
        if (AnsweredShown) rows.AddRange(others.Where(IsAnswered));
        return rows;
    }

    /// <summary>Clears the answer of the current step: an <c>open</c> result with no report (the note stays). Null when done, else the reason.</summary>
    public string? Reopen(DateTime utcNow)
    {
        if (CurrentId is null) return "no current step";
        _results = OwnerQaFiles.ReadResults(Folder);
        var problem = OwnerQaFiles.AppendResult(Folder, CurrentId, "open", ResultOf(CurrentId)?.Note ?? "", null, Build, utcNow);
        if (problem is null) Reload();
        return problem;
    }

    /// <summary>Makes a step the current one (the page and Ctrl+B follow). Null when done, else the reason.</summary>
    public string? SetCurrent(string id)
    {
        if (_steps.All(step => step.Id != id)) return "unknown step";
        CurrentId = id;
        return OwnerQaFiles.WriteActive(Folder, id);
    }

    /// <summary>Follows the page: when <c>active-check.txt</c> names another step of the list, it becomes the current one (nothing is
    /// written). True when the current step changed.</summary>
    public bool FollowActive()
    {
        var active = BugReportCheck.ReadActive(Path.Combine(Folder, BugReportCheck.FileName), DateTime.UtcNow);
        if (active is null || active == CurrentId || _steps.All(step => step.Id != active)) return false;
        CurrentId = active;
        return true;
    }

    /// <summary>Makes the next open step after the current one (in list order, wrapping) the current one; false when none is open.</summary>
    public bool AdvanceToNextOpen(out string? problem)
    {
        problem = null;
        var start = _steps.ToList().FindIndex(step => step.Id == CurrentId);
        for (var offset = 1; offset <= _steps.Count; offset++)
        {
            var step = _steps[(start + offset + _steps.Count) % _steps.Count];
            if (step.Id != CurrentId && !IsAnswered(step)) { problem = SetCurrent(step.Id); return true; }
        }
        return false;
    }

    /// <summary>Records a mark for the current step: the note, and the report folder name of the last report sent for it.</summary>
    public string? Mark(string status, string note, DateTime utcNow)
    {
        if (CurrentId is null || status is not ("same" or "differs" or "cannot-test")) return "no current step or a wrong mark";
        _results = OwnerQaFiles.ReadResults(Folder);   // the page may have changed the step since the window opened
        var problem = OwnerQaFiles.AppendResult(Folder, CurrentId, status, note, ResultOf(CurrentId)?.Report, Build, utcNow);
        if (problem is null) Reload();
        return problem;
    }

    /// <summary>Records the folder name of a report sent for the current step, keeping the status the step shows (recorded, else the list's, else open).</summary>
    public string? RecordReport(string folderName, string note, DateTime utcNow)
    {
        if (Current is not { } step) return "no current step";
        _results = OwnerQaFiles.ReadResults(Folder);
        var problem = OwnerQaFiles.RecordReportFor(Folder, step.Id, folderName, note, Build, utcNow);
        if (problem is null) Reload();
        return problem;
    }
}

/// <summary>One input event as the overlay's router needs it (the viewer maps a Godot event to it; a non-key event is null).</summary>
public sealed record OwnerQaKey(int Code, bool IsQ, bool IsEscape, bool Pressed, bool Echo, bool Ctrl, bool Shift, bool Alt, bool Meta);

/// <summary>What the router decided for one event.</summary>
public enum OwnerQaRoute
{
    /// <summary>Not the overlay's: the event goes on to the game (and to Ctrl+B and the windows).</summary>
    PassThrough,
    /// <summary>The overlay's: the viewer marks the event handled and does nothing else.</summary>
    Consumed,
    /// <summary>Ctrl+Q with the overlay available: the viewer marks the event handled and opens the overlay.</summary>
    Open,
    /// <summary>Ctrl+Shift+Q with the overlay available: the viewer marks the event handled and hides or shows the plate.</summary>
    TogglePlate,
    /// <summary>Escape or Ctrl+Q over the open overlay: the viewer marks the event handled and closes it; the key's release is swallowed.</summary>
    Close,
}

/// <summary>
/// ARC-DEV-2236: the routing of the overlay's keys, without Godot (it was in the viewer and only the <c>report</c> smoke stage saw it).
/// An open overlay owns every event (a Ctrl+B or a game key does nothing); the release of the key that closed it is swallowed (Escape
/// acts on release in the windows below); a press of any other key forgets that key; Ctrl+Q and Ctrl+Shift+Q (no Alt, no Meta, no echo)
/// act only when the overlay is available, asked only when the chord was pressed.
/// </summary>
public sealed class OwnerQaInputRouter
{
    private int? _closingCode;

    /// <summary>The key whose release is still to be swallowed, if any.</summary>
    public int? ClosingCode => _closingCode;

    /// <summary>The chord for <paramref name="shift"/> false = Ctrl+Q, true = Ctrl+Shift+Q.</summary>
    public static bool IsChord(OwnerQaKey key, bool shift)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key is { Pressed: true, Echo: false, Ctrl: true, Alt: false, Meta: false, IsQ: true } && key.Shift == shift;
    }

    /// <summary>Decides one event. <paramref name="available"/> is asked only for a Ctrl+Q or Ctrl+Shift+Q press with the overlay closed.</summary>
    public OwnerQaRoute Route(OwnerQaKey? key, bool overlayOpen, Func<bool> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (overlayOpen)
        {
            if (key is { Pressed: true, Echo: false } && (key.IsEscape || IsChord(key, shift: false)))
            {
                _closingCode = key.Code;
                return OwnerQaRoute.Close;
            }
            return OwnerQaRoute.Consumed;
        }
        if (_closingCode is { } closing && key is { Pressed: false } release && release.Code == closing)
        {
            _closingCode = null;
            return OwnerQaRoute.Consumed;
        }
        if (key is null) return OwnerQaRoute.PassThrough;
        if (key.Pressed) _closingCode = null;
        var open = IsChord(key, shift: false);
        if ((!open && !IsChord(key, shift: true)) || !available()) return OwnerQaRoute.PassThrough;
        return open ? OwnerQaRoute.Open : OwnerQaRoute.TogglePlate;
    }

    /// <summary>Forgets a pending release (the viewer's overlay closed some other way).</summary>
    public void Reset() => _closingCode = null;
}
