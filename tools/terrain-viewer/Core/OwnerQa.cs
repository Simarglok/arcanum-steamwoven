using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One owner check as the QA page writes it into <c>checks.json</c> (ARC-DEV-2186 step format).</summary>
public sealed record OwnerQaStep(string Id, string Section, string Step, string Expected, string ListStatus);

/// <summary>The latest recorded result of a step in <c>results.jsonl</c> (status, note, report folder name).</summary>
public sealed record OwnerQaResult(string Status, string Note, string? Report);

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
    /// otherwise not an <paramref name="automated"/> run, with the flag or the marker, finds the first <c>local/qa</c> above one of the
    /// start folders.</summary>
    public static string? FindFolder(IEnumerable<string?> startFolders, bool automated, string? folderVariable, string? enabledVariable)
    {
        ArgumentNullException.ThrowIfNull(startFolders);
        if (!string.IsNullOrEmpty(folderVariable)) return Directory.Exists(folderVariable) ? folderVariable : null;
        if (automated) return null;
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
                    return enabledVariable == "1" || File.Exists(Path.Combine(candidate, MarkerFile)) ? candidate : null;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // an unusable start folder is skipped
            }
        }
        return null;
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
                    results[step] = new(status, note, report);
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
        if (BugReportCheck.Normalize(step) is null) return "not a step id";
        if (status is not ("open" or "same" or "differs" or "cannot-test")) return "unknown status";
        if (note.Length > MaxNote) return $"the note is longer than {MaxNote} characters";
        var line = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["kind"] = "result", ["ts"] = utcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), ["step"] = step, ["status"] = status,
            ["note"] = note.Trim(), ["report"] = string.IsNullOrEmpty(report) ? null : report, ["build"] = build, ["src"] = "game",
        }) + "\n";
        try
        {
            Directory.CreateDirectory(folder);
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
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return error.Message;
        }
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
    public IReadOnlyList<OwnerQaStep> Steps => _steps;

    /// <summary>Reads the list and the results again; the current step stays when it is still in the list.</summary>
    public void Reload()
    {
        (_steps, var commit, Problem) = OwnerQaFiles.ReadChecks(Folder);
        ListCommit = commit;
        _results = OwnerQaFiles.ReadResults(Folder);
        if (CurrentId is null || _steps.All(step => step.Id != CurrentId))
        {
            var active = BugReportCheck.ReadActive(Path.Combine(Folder, BugReportCheck.FileName), DateTime.UtcNow);
            CurrentId = active is not null && _steps.Any(step => step.Id == active) ? active : _steps.FirstOrDefault(step => EffectiveStatus(step) == "open")?.Id;
        }
    }

    public OwnerQaStep? Current => _steps.FirstOrDefault(step => step.Id == CurrentId);
    public OwnerQaResult? ResultOf(string id) => _results.GetValueOrDefault(id);
    /// <summary>The recorded status, else the list's own (open when neither is a status).</summary>
    public string EffectiveStatus(OwnerQaStep step) => _results.TryGetValue(step.Id, out var result) ? result.Status : step.ListStatus is "same" or "differs" or "cannot-test" ? step.ListStatus : "open";

    /// <summary>The listed steps: the current one first, then the others of the filter in list order.</summary>
    public IReadOnlyList<OwnerQaStep> Rows()
    {
        var current = Current;
        bool Shown(OwnerQaStep step) => Filter switch
        {
            OwnerQaFilter.Open => EffectiveStatus(step) == "open",
            OwnerQaFilter.Section => current is not null && step.Section == current.Section,
            _ => true,
        };
        var rows = new List<OwnerQaStep>();
        if (current is not null) rows.Add(current);
        rows.AddRange(_steps.Where(step => step.Id != CurrentId && Shown(step)));
        return rows;
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
            if (step.Id != CurrentId && EffectiveStatus(step) == "open") { problem = SetCurrent(step.Id); return true; }
        }
        return false;
    }

    /// <summary>Records a mark for the current step: the note, and the report folder name of the last report sent for it.</summary>
    public string? Mark(string status, string note, DateTime utcNow)
    {
        if (CurrentId is null || status is not ("same" or "differs" or "cannot-test")) return "no current step or a wrong mark";
        var problem = OwnerQaFiles.AppendResult(Folder, CurrentId, status, note, ResultOf(CurrentId)?.Report, Build, utcNow);
        if (problem is null) Reload();
        return problem;
    }

    /// <summary>Records the folder name of a report sent for the current step, keeping its status (open when none is recorded).</summary>
    public string? RecordReport(string folderName, string note, DateTime utcNow)
    {
        if (CurrentId is null) return "no current step";
        var status = _results.TryGetValue(CurrentId, out var result) ? result.Status : "open";
        var problem = OwnerQaFiles.AppendResult(Folder, CurrentId, status, note.Length > 0 ? note : result?.Note ?? "", folderName, Build, utcNow);
        if (problem is null) Reload();
        return problem;
    }
}
